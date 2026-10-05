using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Helpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Services
{
    /// <summary>
    /// Motor de sincronización "espejo" entre Operaciones y Finanzas.
    /// Centralizado aquí para que tanto Operaciones/Index (ediciones rápidas)
    /// como Operaciones/Create (alta de una operación nueva) generen exactamente
    /// las mismas transacciones financieras, con las mismas reglas de negocio
    /// (ISPS por terminal, Gate por depósito/naviera, tarifas documentales por
    /// agencia, falsos fletes, etc.)
    /// </summary>
    public class FinanzasSyncService
    {
        private readonly AtilsonContext _context;
        private readonly IndicadoresEconomicosService _indicadores;

        public FinanzasSyncService(AtilsonContext context, IndicadoresEconomicosService indicadores)
        {
            _context = context;
            _indicadores = indicadores;
        }

        // Valores estandarizados para Responsable/ResponsablePago — evita comparar
        // texto libre con .Contains() en el resto del sistema. "PROVEEDOR" y
        // "AGENCIA" se dejan separados porque Cristián definió que son conceptos
        // distintos (transportista/depósito vs. agencia de aduanas).
        public static class ResponsabilidadValores
        {
            public const string Cliente = "CLIENTE";
            public const string Atilson = "ATILSON";
            public const string Proveedor = "PROVEEDOR";
            public const string Agencia = "AGENCIA";
        }

        // Valores comerciales fijos por Acuerdo de Destino para el Certificado de
        // Origen — reflejan las opciones ya definidas en el selector "Acuerdo
        // Comercial (Destino)" del panel documental. Si AcuerdoOrigen no coincide
        // con ninguna clave (o está vacío), se sigue usando el fallback existente
        // por Agencia de Aduanas (TarifasDocumentales / TarifasMaestras).
        private static readonly Dictionary<string, (decimal Base, bool AplicaIva)> ValoresAcuerdoOrigen = new()
        {
            { "ALIANZA DEL PACÍFICO", (11183m, true) },
            { "BOLIVIA", (11183m, true) },
            { "CHINA", (10000m, false) },
            { "COLOMBIA", (11183m, true) },
            { "CUBA", (11183m, true) },
            { "ECUADOR", (11183m, true) },
            { "EFTA", (10000m, false) },
            { "EMIRATOS ARABES", (11183m, true) },
            { "INDIA", (11183m, true) },
            { "INDONESIA", (11183m, true) },
            { "JAPON", (11183m, true) },
            { "MALASIA", (11183m, true) },
            { "MERCOSUR", (11183m, true) },
            { "PERU", (11183m, true) },
            { "REINO UNIDO", (10000m, false) },
            { "TURQUIA", (11183m, true) },
            { "TAILANDIA", (11183m, true) },
            { "URUGUAY", (11183m, true) },
            { "VENEZUELA", (11183m, true) },
            { "VIETNAM", (11183m, true) },
            { "MERCANCIAS PRODUCIDAS (FGN)", (15500m, true) },
            { "REEXPORTACION", (15500m, true) },
            { "TLC USA", (0m, false) },
        };

        private static decimal? CalcularValorAcuerdoOrigen(string? acuerdoOrigen)
        {
            if (string.IsNullOrWhiteSpace(acuerdoOrigen)) return null;
            if (!ValoresAcuerdoOrigen.TryGetValue(acuerdoOrigen, out var v)) return null;
            return v.AplicaIva ? Math.Round(v.Base * 1.19m) : v.Base;
        }

        // =====================================================================
        // Compara "40' DRY HC" (Operación) contra "40' HC" (TarifasMaritimas.
        // Equipamiento) ignorando la palabra DRY, tildes, apóstrofes y mayúsculas.
        // Separa Tamaño (20/40/45) de Tipo (HC/STD/RF) para matchear con o sin
        // esa distinción, según lo que exista cargado en la tarifa.
        // =====================================================================
        private static (string Tamano, string Tipo) NormalizarEquipo(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return ("", "");
            string t = texto.ToUpper().Replace("'", "").Replace("Á", "A").Replace("É", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ú", "U");

            string tamano = t.Contains("45") ? "45" : (t.Contains("40") ? "40" : (t.Contains("20") ? "20" : ""));

            string tipo;
            if (t.Contains("REEFER") || t.Contains(" RF") || t.Contains("RQ")) tipo = "RF";
            else if (t.Contains("HC") || t.Contains("HIGH CUBE") || t.Contains("HICUBE")) tipo = "HC";
            else if (t.Contains("STD") || t.Contains("STANDARD") || t.Contains("DV")) tipo = "STD";
            else tipo = "";

            return (tamano, tipo);
        }

        // Convierte UF y USD a CLP en tiempo real, devolviendo también la tasa usada
        // (Tasa). Documental descarta la tasa porque no necesita mostrar el monto
        // original como referencia; Gate-Out/In sí la usa (ver GenerarEspejo) para
        // reconstruir el valor en USD como dato de contexto para Finanzas, según
        // instrucción de Cristián: "CLP como valor principal, USD chico en paréntesis
        // gris como referencia".
        private async Task<(decimal Monto, string Moneda, decimal? Tasa)> NormalizarMonedaConTasaAsync(decimal monto, string moneda)
        {
            if (moneda == "UF")
            {
                decimal valorUf = await _indicadores.ObtenerValorUfAsync();
                return (Math.Round(monto * valorUf), "CLP", valorUf);
            }
            if (moneda == "USD")
            {
                decimal valorDolar = await _indicadores.ObtenerValorDolarAsync();
                return (Math.Round(monto * valorDolar), "CLP", valorDolar);
            }
            return (monto, moneda, null);
        }

        private async Task<(decimal Monto, string Moneda)> NormalizarMonedaDocumentalAsync(decimal monto, string moneda)
        {
            var (montoConvertido, monedaConvertida, _) = await NormalizarMonedaConTasaAsync(monto, moneda);
            return (montoConvertido, monedaConvertida);
        }

        public async Task SincronizarAsync(int idOperacion, string usuario)
        {
            var op = await _context.Operaciones
    .Include(o => o.OperacionesTerrestres)
    .Include(o => o.OperacionesDocumentales)
    .Include(o => o.ExtracostosOperacions)
    .Include(o => o.IdClienteNavigation)
    .Include(o => o.IdNavieraNavigation)
    .Include(o => o.IdPuertoOrigenNavigation)
    .Include(o => o.IdPuertoDestinoNavigation)
    .Include(o => o.Unidadestecnicas)
    .FirstOrDefaultAsync(o => o.Id == idOperacion);

            if (op == null) return;

            var txExistentes = await _context.TransaccionesFinancieras
                .Where(t => t.IdOperacion == idOperacion)
                .ToListAsync();

            DateTime ahora = DateTime.Now;

            async Task GenerarEspejo(string grupo, string concepto, bool activo, string busquedaTarifa, decimal? valorOverride = null, string? monedaOverride = null, int? idProveedorOverride = null, bool convertirAClp = false, bool? ivaExento = null, string? responsablePagoOverride = null, string? responsableOverride = null)
            {
                bool busquedaTieneUnidad = busquedaTarifa.Contains("(U");
                var txsIngreso = txExistentes.Where(t => t.GrupoCobro == grupo && t.Concepto.Contains(busquedaTarifa) && (busquedaTieneUnidad || !t.Concepto.Contains(" (U")) && t.TipoMovimiento == "INGRESO").ToList();
                var txsEgreso = txExistentes.Where(t => t.GrupoCobro == grupo && t.Concepto.Contains(busquedaTarifa) && (busquedaTieneUnidad || !t.Concepto.Contains(" (U")) && t.TipoMovimiento == "EGRESO").ToList();

                var txIngreso = txsIngreso.FirstOrDefault();
                var txEgreso = txsEgreso.FirstOrDefault();

                if (txsIngreso.Count > 1) _context.TransaccionesFinancieras.RemoveRange(txsIngreso.Skip(1));
                if (txsEgreso.Count > 1) _context.TransaccionesFinancieras.RemoveRange(txsEgreso.Skip(1));

                if (activo)
                {
                    decimal montoCosto = 0m;
                    string moneda = grupo == "Transporte" ? "CLP" : "USD";

                    if (valorOverride.HasValue) { montoCosto = valorOverride.Value; moneda = monedaOverride ?? "CLP"; }
                    else
                    {
                        var tarifa = await _context.TarifasMaestras.FirstOrDefaultAsync(t => t.EsActiva && t.Concepto.ToUpper().Contains(busquedaTarifa.ToUpper()));
                        if (tarifa != null) { montoCosto = tarifa.ValorNeto; moneda = tarifa.Moneda ?? "USD"; }
                    }

                    // Solo se activa para conceptos que deben mostrarse en CLP (ej: ISPS, Gate).
                    // Marítimo y Transporte quedan intactos con su moneda original.
                    // tasaUsada guarda la tasa de conversión (UF o Dólar) para poder
                    // reconstruir el monto original en USD como referencia visual —
                    // solo se muestra en Gate-Out/In (ISPS no la necesita, queda 100% CLP).
                    decimal? tasaUsada = null;
                    decimal? montoOriginalUsd = null;
                    if (convertirAClp)
                    {
                        if (moneda == "USD") montoOriginalUsd = montoCosto;
                        var (montoConvertido, monedaConvertida, tasa) = await NormalizarMonedaConTasaAsync(montoCosto, moneda);
                        montoCosto = montoConvertido; moneda = monedaConvertida; tasaUsada = tasa;
                    }
                    else if (moneda == "UF")
                    {
                        montoCosto = Math.Round(montoCosto * 37500m); moneda = "CLP";
                    }
                    decimal montoVenta = montoCosto;

                    // Si Responsable != Cliente (Atilson lo absorbe, o es culpa de un
                    // Proveedor/Agencia), el Ingreso al cliente no debe existir: no hay
                    // nada que cobrarle por un concepto que no le corresponde pagar.
                    string responsableParaNuevo = responsableOverride ?? ResponsabilidadValores.Cliente;
                    bool esResponsableCliente = txIngreso == null
                        ? (responsableParaNuevo == ResponsabilidadValores.Cliente)
                        : (string.IsNullOrEmpty(txIngreso.Responsable) || txIngreso.Responsable == ResponsabilidadValores.Cliente);
                    decimal montoVentaFinal = esResponsableCliente ? montoVenta : 0m;
                    string estadoVentaFinal = esResponsableCliente
                        ? (montoVentaFinal > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR")
                        : "NO FACTURABLE";

                    if (txIngreso == null)
                    {
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = grupo, TipoMovimiento = "INGRESO", Concepto = concepto, MontoNeto = montoVentaFinal, Moneda = moneda, TipoCambio = esResponsableCliente ? tasaUsada : null, MontoOriginalUsd = esResponsableCliente ? montoOriginalUsd : null, EstadoFila = estadoVentaFinal, FechaCreacion = ahora, UsuarioCreador = usuario, IdCliente = op.IdCliente, ResponsablePago = responsablePagoOverride ?? ResponsabilidadValores.Atilson, Responsable = responsableParaNuevo, IvaExento = ivaExento });
                    }
                    else
                    {
                        txIngreso.Concepto = concepto;
                        if (!txIngreso.TarifaManual)
                        {
                            txIngreso.MontoNeto = montoVentaFinal;
                            txIngreso.Moneda = moneda;
                            txIngreso.TipoCambio = esResponsableCliente ? tasaUsada : null;
                            txIngreso.MontoOriginalUsd = esResponsableCliente ? montoOriginalUsd : null;
                            txIngreso.EstadoFila = estadoVentaFinal;
                            txIngreso.IvaExento = ivaExento;
                        }
                    }

                    if (txEgreso == null)
                    {
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = grupo, TipoMovimiento = "EGRESO", Concepto = concepto, MontoNeto = montoCosto, Moneda = moneda, TipoCambio = tasaUsada, MontoOriginalUsd = montoOriginalUsd, EstadoFila = montoCosto > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR", FechaCreacion = ahora, UsuarioCreador = usuario, IdProveedor = idProveedorOverride, ResponsablePago = responsablePagoOverride ?? ResponsabilidadValores.Atilson, Responsable = responsableParaNuevo, IvaExento = ivaExento });
                    }
                    else
                    {
                        txEgreso.Concepto = concepto;
                        if (idProveedorOverride.HasValue) txEgreso.IdProveedor = idProveedorOverride;
                        if (!txEgreso.TarifaManual) { txEgreso.MontoNeto = montoCosto; txEgreso.Moneda = moneda; txEgreso.TipoCambio = tasaUsada; txEgreso.MontoOriginalUsd = montoOriginalUsd; txEgreso.EstadoFila = montoCosto > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR"; txEgreso.IvaExento = ivaExento; }
                    }
                }
                else
                {
                    if (txIngreso != null && (txIngreso.EstadoFila == "PROVISIÓN" || txIngreso.EstadoFila == "PENDIENTE VALORIZAR" || txIngreso.EstadoFila == "NO FACTURABLE")) _context.TransaccionesFinancieras.Remove(txIngreso);
                    if (txEgreso != null && (txEgreso.EstadoFila == "PROVISIÓN" || txEgreso.EstadoFila == "PENDIENTE VALORIZAR")) _context.TransaccionesFinancieras.Remove(txEgreso);
                }
            }

            async Task GenerarEspejoDocumental(string conceptoEspejo, bool activo, string busquedaTarifa, int? idAgenciaAduana, decimal? valorOverride = null, string? numeroDocumento = null, string? monedaOverride = null, bool convertirAClp = false)
            {
                var txsIngreso = txExistentes.Where(t => t.Concepto == conceptoEspejo && t.TipoMovimiento == "INGRESO").ToList();
                var txsEgreso = txExistentes.Where(t => t.Concepto == conceptoEspejo && t.TipoMovimiento == "EGRESO").ToList();

                var txIngreso = txsIngreso.FirstOrDefault();
                var txEgreso = txsEgreso.FirstOrDefault();

                if (txsIngreso.Count > 1) _context.TransaccionesFinancieras.RemoveRange(txsIngreso.Skip(1));
                if (txsEgreso.Count > 1) _context.TransaccionesFinancieras.RemoveRange(txsEgreso.Skip(1));

                if (activo)
                {
                    // Para conceptos con convertirAClp=true (hoy solo Courier) también se
                    // reingresa al bloque si falta TipoCambio guardado — esto "sana" filas
                    // creadas antes de que este campo empezara a persistirse correctamente,
                    // sin depender de que cambien de estado o de N° de documento.
                    bool faltaTipoCambio = convertirAClp && ((txIngreso != null && (txIngreso.TipoCambio == null || txIngreso.MontoOriginalUsd == null)) || (txEgreso != null && (txEgreso.TipoCambio == null || txEgreso.MontoOriginalUsd == null)));

                    if (txIngreso == null || txEgreso == null || txIngreso.EstadoFila == "PENDIENTE VALORIZAR" || txEgreso.EstadoFila == "PENDIENTE VALORIZAR" || txIngreso.NumeroDocumento != numeroDocumento || faltaTipoCambio)
                    {
                        decimal montoCosto = 0m;
                        string moneda = "CLP";

                        if (idAgenciaAduana.HasValue)
                        {
                            var tarifaDoc = await _context.TarifasDocumentales
                                .Where(t => t.EsActiva == true && t.IdAgenciaAduana == idAgenciaAduana.Value && t.Concepto.ToUpper().Contains(busquedaTarifa.ToUpper()))
                                .OrderBy(t => t.Id)
                                .FirstOrDefaultAsync();

                            if (tarifaDoc != null)
                            {
                                montoCosto = tarifaDoc.ValorNeto;
                                moneda = tarifaDoc.Moneda ?? "CLP";
                            }
                        }

                        if (montoCosto == 0m)
                        {
                            var tarifaMaestra = await _context.TarifasMaestras
                                .FirstOrDefaultAsync(t => t.EsActiva && t.Categoria == "Documental" && t.Concepto.ToUpper().Contains(busquedaTarifa.ToUpper()));
                            if (tarifaMaestra != null)
                            {
                                montoCosto = tarifaMaestra.ValorNeto;
                                moneda = tarifaMaestra.Moneda ?? "CLP";
                            }
                        }

                        if (valorOverride.HasValue && valorOverride.Value >= 0)
                        {
                            montoCosto = valorOverride.Value;
                            if (!string.IsNullOrEmpty(monedaOverride)) moneda = monedaOverride;
                        }

                        decimal? tasaUsadaDoc = null;
                        decimal? montoOriginalUsdDoc = null;
                        if (convertirAClp)
                        {
                            if (moneda == "USD") montoOriginalUsdDoc = montoCosto;
                            var (montoConvertidoDoc, monedaConvertidaDoc, tasaDoc) = await NormalizarMonedaConTasaAsync(montoCosto, moneda);
                            montoCosto = montoConvertidoDoc; moneda = monedaConvertidaDoc; tasaUsadaDoc = tasaDoc;
                        }
                        else
                        {
                            (montoCosto, moneda) = await NormalizarMonedaDocumentalAsync(montoCosto, moneda);
                        }
                        decimal montoVenta = montoCosto;

                        string estadoVenta = (montoVenta > 0 || (valorOverride.HasValue && valorOverride.Value == 0)) ? "PROVISIÓN" : "PENDIENTE VALORIZAR";
                        string estadoCosto = (montoCosto > 0 || (valorOverride.HasValue && valorOverride.Value == 0)) ? "PROVISIÓN" : "PENDIENTE VALORIZAR";

                        bool esResponsableClienteDoc = txIngreso == null || string.IsNullOrEmpty(txIngreso.Responsable) || txIngreso.Responsable == ResponsabilidadValores.Cliente;
                        decimal montoVentaFinalDoc = esResponsableClienteDoc ? montoVenta : 0m;
                        string estadoVentaFinalDoc = esResponsableClienteDoc ? estadoVenta : "NO FACTURABLE";

                        if (txIngreso == null) _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = "Documental", TipoMovimiento = "INGRESO", Concepto = conceptoEspejo, MontoNeto = montoVenta, Moneda = moneda, TipoCambio = tasaUsadaDoc, MontoOriginalUsd = montoOriginalUsdDoc, EstadoFila = estadoVenta, FechaCreacion = ahora, UsuarioCreador = usuario, IdCliente = op.IdCliente, NumeroDocumento = numeroDocumento, ResponsablePago = ResponsabilidadValores.Atilson, Responsable = ResponsabilidadValores.Cliente });
                        else
                        {
                            txIngreso.NumeroDocumento = numeroDocumento;
                            if (txIngreso.EstadoFila == "PENDIENTE VALORIZAR" && montoVenta >= 0 && esResponsableClienteDoc) { txIngreso.MontoNeto = montoVenta; txIngreso.Moneda = moneda; txIngreso.EstadoFila = estadoVenta; }
                            else if (!esResponsableClienteDoc && txIngreso.EstadoFila != "NO FACTURABLE") { txIngreso.MontoNeto = 0m; txIngreso.EstadoFila = "NO FACTURABLE"; }
                            if (convertirAClp) { txIngreso.TipoCambio = esResponsableClienteDoc ? tasaUsadaDoc : null; txIngreso.MontoOriginalUsd = esResponsableClienteDoc ? montoOriginalUsdDoc : null; }
                        }

                        if (txEgreso == null) _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = "Documental", TipoMovimiento = "EGRESO", Concepto = conceptoEspejo, MontoNeto = montoCosto, Moneda = moneda, TipoCambio = tasaUsadaDoc, MontoOriginalUsd = montoOriginalUsdDoc, EstadoFila = estadoCosto, FechaCreacion = ahora, UsuarioCreador = usuario, NumeroDocumento = numeroDocumento, ResponsablePago = ResponsabilidadValores.Atilson, Responsable = ResponsabilidadValores.Cliente });
                        else
                        {
                            txEgreso.NumeroDocumento = numeroDocumento;
                            if (txEgreso.EstadoFila == "PENDIENTE VALORIZAR" && montoCosto >= 0) { txEgreso.MontoNeto = montoCosto; txEgreso.Moneda = moneda; txEgreso.EstadoFila = estadoCosto; }
                            if (convertirAClp) { txEgreso.TipoCambio = tasaUsadaDoc; txEgreso.MontoOriginalUsd = montoOriginalUsdDoc; }
                        }
                    }
                }
                else
                {
                    if (txIngreso != null && (txIngreso.EstadoFila == "PROVISIÓN" || txIngreso.EstadoFila == "PENDIENTE VALORIZAR" || txIngreso.EstadoFila == "NO FACTURABLE")) _context.TransaccionesFinancieras.Remove(txIngreso);
                    if (txEgreso != null && (txEgreso.EstadoFila == "PROVISIÓN" || txEgreso.EstadoFila == "PENDIENTE VALORIZAR")) _context.TransaccionesFinancieras.Remove(txEgreso);
                }
            }

            // 1. MARITIMO — cruce real contra TarifasMaritimas (Naviera + POL + POD + Equipo)
            bool isMaritimo = new[] { 1, 2, 3, 5, 8, 9, 10, 12 }.Contains(op.IdTipoServicio ?? 0);
            string naviera = op.IdNavieraNavigation?.NombreNaviera ?? "Por Confirmar";

            decimal? valorFleteMarDb = null;
            if (isMaritimo && op.IdNaviera > 0)
            {
                var polNombre = (op.IdPuertoOrigenNavigation?.NombrePuerto ?? "").ToUpper();
                var podNombre = (op.IdPuertoDestinoNavigation?.NombrePuerto ?? "").ToUpper();
                var (tamOp, tipoOp) = NormalizarEquipo(op.TipoContenedor);

                var candidatasMar = await _context.TarifasMaritimas
                    .Where(t => t.EsActiva && t.IdNaviera == op.IdNaviera)
                    .ToListAsync();

                bool PolPodOk(TarifasMaritima t)
                {
                    var pol = (t.Pol ?? "").ToUpper();
                    var pod = (t.Pod ?? "").ToUpper();
                    bool polOk = string.IsNullOrEmpty(polNombre) || pol.Contains(polNombre) || polNombre.Contains(pol);
                    bool podOk = string.IsNullOrEmpty(podNombre) || pod.Contains(podNombre) || podNombre.Contains(pod);
                    return polOk && podOk;
                }

                // 1° intento: mismo tamaño Y mismo tipo exacto (HC / STD / RF)
                var matchMar = candidatasMar.Where(PolPodOk).FirstOrDefault(t =>
                {
                    var (tamT, tipoT) = NormalizarEquipo(t.Equipamiento);
                    return tamOp != "" && tamT == tamOp && tipoT == tipoOp;
                });

                // 2° intento: mismo tamaño y misma familia (Dry vs Reefer), sin exigir HC/STD exacto
                if (matchMar == null)
                {
                    matchMar = candidatasMar.Where(PolPodOk).FirstOrDefault(t =>
                    {
                        var (tamT, tipoT) = NormalizarEquipo(t.Equipamiento);
                        bool mismaFamiliaReefer = (tipoOp == "RF") == (tipoT == "RF");
                        return tamOp != "" && tamT == tamOp && mismaFamiliaReefer;
                    });
                }

                // 3° intento: cualquier tarifa activa de esa naviera+ruta (mejor referencia que ninguna)
                if (matchMar == null)
                {
                    matchMar = candidatasMar.Where(PolPodOk).FirstOrDefault();
                }

                if (matchMar != null) valorFleteMarDb = matchMar.TarifaUsd;
            }

            await GenerarEspejo("Marítimo", $"Flete Marítimo ({naviera})", isMaritimo, "Flete Marítimo", valorFleteMarDb, "USD");

            // 1b. TIPO DE EMISIÓN B/L — checkboxes seleccionados por la ejecutiva,
            // uno o varios a la vez. Cada tipo se cotiza por separado según la
            // naviera (algunas cobran en USD + IVA, otras exentas — lo define
            // TarifaBl.AplicaIva, que sobreescribe la exención por categoría solo
            // en estas filas).
            if (isMaritimo && op.IdNaviera > 0)
            {
                var etiquetasBl = new Dictionary<string, string>
                {
                    { "ORIGINALES", "Originales" },
                    { "DESTINO", "Destino" },
                    { "SEA_WAYBILL", "Sea Waybill" },
                    { "TELEX_RELEASE", "Telex Release" },
                    { "EXPRESS_RELEASE", "Express Release" }
                };
                var tiposSeleccionadosBl = (op.TipoEmisionBl ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet();

                var tarifasBlNaviera = await _context.TarifasBl
                    .Where(t => t.IdNaviera == op.IdNaviera && t.EsActiva)
                    .ToListAsync();

                foreach (var kvBl in etiquetasBl)
                {
                    bool seleccionadoBl = tiposSeleccionadosBl.Contains(kvBl.Key);
                    var tarifaBl = tarifasBlNaviera.FirstOrDefault(t => t.TipoEmision == kvBl.Key);
                    decimal? valorBl = tarifaBl?.ValorNeto;
                    string monedaBl = string.IsNullOrWhiteSpace(tarifaBl?.Moneda) ? "USD" : tarifaBl!.Moneda;
                    bool? ivaExentoBl = tarifaBl != null ? !tarifaBl.AplicaIva : (bool?)null;

                    // "Originales - DESTINO" muestra la ubicación elegida en el concepto
                    // (solo informativo — la tarifa se sigue buscando por TipoEmision
                    // "ORIGINALES_DESTINO", igual para las 4 ubicaciones, mismo precio).
                    string sufijoUbicacion = kvBl.Key == "ORIGINALES" && !string.IsNullOrWhiteSpace(op.UbicacionOriginalesDestino)
    ? $" ({op.UbicacionOriginalesDestino})"
                        : "";
                    string conceptoBl = $"Emisión BL - {kvBl.Value}{sufijoUbicacion}";

                    await GenerarEspejo("Marítimo", conceptoBl, seleccionadoBl, $"Emisión BL - {kvBl.Value}", valorBl, monedaBl, convertirAClp: monedaBl == "USD", ivaExento: ivaExentoBl);
                }

                // Tipo "Otro" — texto libre indicado por Operaciones cuando ninguna
                // de las 3 opciones fijas aplica. No tiene tarifa en el maestro
                // (Comercial la valoriza manualmente en Acuerdos Operativos).
                bool seleccionadoOtro = tiposSeleccionadosBl.Contains("OTRO") && !string.IsNullOrWhiteSpace(op.TipoEmisionBlOtro);
                string conceptoOtro = seleccionadoOtro ? $"Emisión BL - {op.TipoEmisionBlOtro}" : "Emisión BL - Otro";
                await GenerarEspejo("Marítimo", conceptoOtro, seleccionadoOtro, conceptoOtro, null, "USD", convertirAClp: false, ivaExento: null);
            }

            // 2. TERRESTRE CON CRUCE LOGÍSTICO — un concepto (costo+venta) por cada
            // contenedor gestionado por separado. El Contenedor Principal mantiene
            // el mismo formato histórico (compatibilidad con bookings de un solo
            // contenedor); los adicionales solo generan su propio concepto "Flete
            // Terrestre (Uxx) (...)" cuando ya existe una fila OperacionesTerrestre
            // propia — si nadie gestionó ese contenedor por separado, no genera nada.
            bool isTerrestre = new[] { 1, 2, 4, 6, 8, 9, 11, 13 }.Contains(op.IdTipoServicio ?? 0);
            var terr = op.OperacionesTerrestres.FirstOrDefault(x => x.IdUnidadTecnica == null);

            async Task<decimal?> ObtenerTarifaTransportistaAsync(OperacionesTerrestre? terrUnidad, string retiroU, string plantaU, string entregaU)
            {
                if (terrUnidad == null || string.IsNullOrEmpty(terrUnidad.EmpresaTransporte)) return null;

                var proveedor = await _context.Proveedores.FirstOrDefaultAsync(p => p.NombreProveedor == terrUnidad.EmpresaTransporte);
                if (proveedor == null) return null;

                string NormalizeStr(string? text)
                {
                    if (string.IsNullOrWhiteSpace(text)) return "";
                    return text.ToUpper().Replace("Á", "A").Replace("É", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ú", "U");
                }

                string retNorm = NormalizeStr(retiroU);
                string plaNorm = NormalizeStr(plantaU);
                string entNorm = NormalizeStr(entregaU);
                int tipoCargaBuscado = op.IdTipoCarga;

                var tarifasProveedor = await _context.Tarifasterrestres
                    .Include(t => t.IdCiudadOrigenNavigation)
                    .Include(t => t.IdCiudadPlantaNavigation)
                    .Include(t => t.IdCiudadDestinoNavigation)
                    .Where(t => t.IdProveedor == proveedor.Id && t.IdTipoCarga == tipoCargaBuscado && t.EsActiva == true)
                    .ToListAsync();

                var tarifaAplicable = tarifasProveedor.FirstOrDefault(t =>
                    t.IdCiudadOrigenNavigation != null && retNorm.Contains(NormalizeStr(t.IdCiudadOrigenNavigation.Nombre)) &&
                    t.IdCiudadPlantaNavigation != null && plaNorm.Contains(NormalizeStr(t.IdCiudadPlantaNavigation.Nombre)) &&
                    t.IdCiudadDestinoNavigation != null && entNorm.Contains(NormalizeStr(t.IdCiudadDestinoNavigation.Nombre))
                );

                return tarifaAplicable?.ValorNeto;
            }

            // Contenedor Principal (formato idéntico al histórico)
            string retiro = terr?.DepositoRetiro ?? "Retiro";
            string planta = terr?.PlantaCarga ?? "Planta";
            string entrega = terr?.PuertoEntrega ?? "Destino";
            string ruta = isTerrestre ? $"{retiro} - {planta} - {entrega}" : "Terrestre";

            decimal? tarifaTranspDb = isTerrestre ? await ObtenerTarifaTransportistaAsync(terr, retiro, planta, entrega) : null;

            await GenerarEspejo("Transporte", $"Flete Terrestre ({ruta})", isTerrestre, "Flete Terrestre", tarifaTranspDb, "CLP");

            // Contenedores adicionales: uno por cada Unidadestecnica que ya tenga
            // su propia fila en OperacionesTerrestres (creada al guardar esa pestaña).
            var terrUnidadesAdicionales = op.OperacionesTerrestres
                .Where(x => x.IdUnidadTecnica != null)
                .GroupBy(x => x.IdUnidadTecnica)
                .Select(g => g.OrderBy(x => x.Id).First());

            foreach (var terrU in terrUnidadesAdicionales)
            {
                string retiroU = terrU.DepositoRetiro ?? "Retiro";
                string plantaU = terrU.PlantaCarga ?? "Planta";
                string entregaU = terrU.PuertoEntrega ?? "Destino";
                string rutaU = $"{retiroU} - {plantaU} - {entregaU}";
                string sufijoU = $" (U{terrU.IdUnidadTecnica})";

                decimal? tarifaU = await ObtenerTarifaTransportistaAsync(terrU, retiroU, plantaU, entregaU);

                await GenerarEspejo("Transporte", $"Flete Terrestre{sufijoU} ({rutaU})", true, $"Flete Terrestre{sufijoU}", tarifaU, "CLP");
            }

            // 3. DOCUMENTAL
            // Garantiza que todo servicio con componente documental tenga al menos su
            // registro principal, aunque la operación se haya creado sin datos documentales.
            // Sin este registro el foreach de abajo no corre y no se generan los cobros
            // "a todo evento" (Legalización / Emisión DUS, Honorarios, etc.).
            bool reqDocumentalServicio = new[] { 1, 3, 4, 7, 8, 10, 11, 14 }.Contains(op.IdTipoServicio ?? 0);
            if (reqDocumentalServicio && !op.OperacionesDocumentales.Any())
            {
                op.OperacionesDocumentales.Add(new OperacionesDocumentale
                {
                    FechaCreacion = ahora,
                    UsuarioCreador = usuario,
                    Activo = true
                });
                await _context.SaveChangesAsync();
            }

            var docsAProcesar = op.OperacionesDocumentales
                .GroupBy(d => d.IdUnidadTecnica)
                .Select(g => g.OrderBy(x => x.Id).First());

            foreach (var doc in docsAProcesar)
            {
                int? idAg = doc.IdAgenciaAduana;
                string sufijoUnidad = doc.IdUnidadTecnica.HasValue ? $" (U{doc.IdUnidadTecnica})" : "";

                decimal? valorAcuerdoOrigen = CalcularValorAcuerdoOrigen(doc.AcuerdoOrigen);
                string? monedaAcuerdoOrigen = valorAcuerdoOrigen.HasValue ? "CLP" : null;
                bool ori2Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "ori", 2);
                bool ori3Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "ori", 3);
                bool ori4Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "ori", 4);

                await GenerarEspejoDocumental($"Certificado de Origen{sufijoUnidad}", doc.CertificadoOrigen == true, "CERTIFICADO ORIGEN", idAg, valorAcuerdoOrigen ?? doc.ValOri1, doc.RemisionOrigen1, monedaAcuerdoOrigen);
                await GenerarEspejoDocumental($"Certificado de Origen R1{sufijoUnidad}", doc.CertificadoOrigen == true && ori2Bloqueado, "RE-EMISION CERTIFICADOS ORIGEN", idAg, valorAcuerdoOrigen ?? doc.ValOri2, doc.RemisionOrigen2, monedaAcuerdoOrigen);
                await GenerarEspejoDocumental($"Certificado de Origen R2{sufijoUnidad}", doc.CertificadoOrigen == true && ori3Bloqueado, "RE-EMISION CERTIFICADOS ORIGEN", idAg, valorAcuerdoOrigen ?? doc.ValOri3, doc.NumOri3, monedaAcuerdoOrigen);
                await GenerarEspejoDocumental($"Certificado de Origen R3{sufijoUnidad}", doc.CertificadoOrigen == true && ori4Bloqueado, "RE-EMISION CERTIFICADOS ORIGEN", idAg, valorAcuerdoOrigen ?? doc.ValOri4, doc.NumOri4, monedaAcuerdoOrigen);

                bool fit2Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "fit", 2);
                bool fit3Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "fit", 3);
                bool fit4Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "fit", 4);
                await GenerarEspejoDocumental($"Certificado Fitosanitario{sufijoUnidad}", doc.AplicaSag, "FITO", idAg, doc.ValFit1, doc.CertFitosanitario);
                await GenerarEspejoDocumental($"Certificado Fitosanitario R1{sufijoUnidad}", doc.AplicaSag && fit2Bloqueado, "RE-EMISION CERTIFICADOS SAG", idAg, doc.ValFit2, doc.NumFit2);
                await GenerarEspejoDocumental($"Certificado Fitosanitario R2{sufijoUnidad}", doc.AplicaSag && fit3Bloqueado, "RE-EMISION CERTIFICADOS SAG", idAg, doc.ValFit3, doc.NumFit3);
                await GenerarEspejoDocumental($"Certificado Fitosanitario R3{sufijoUnidad}", doc.AplicaSag && fit4Bloqueado, "RE-EMISION CERTIFICADOS SAG", idAg, doc.ValFit4, doc.NumFit4);

                bool san2Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "san", 2);
                bool san3Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "san", 3);
                bool san4Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "san", 4);
                await GenerarEspejoDocumental($"Certificado Sanitario{sufijoUnidad}", doc.AplicaSernapesca, "CERTIFICADO SANITARIO", idAg, doc.ValSan1, doc.CertSanitario);
                await GenerarEspejoDocumental($"Certificado Sanitario R1{sufijoUnidad}", doc.AplicaSernapesca && san2Bloqueado, "RE-EMISION", idAg, doc.ValSan2, doc.NumSan2);
                await GenerarEspejoDocumental($"Certificado Sanitario R2{sufijoUnidad}", doc.AplicaSernapesca && san3Bloqueado, "RE-EMISION", idAg, doc.ValSan3, doc.NumSan3);
                await GenerarEspejoDocumental($"Certificado Sanitario R3{sufijoUnidad}", doc.AplicaSernapesca && san4Bloqueado, "RE-EMISION", idAg, doc.ValSan4, doc.NumSan4);

                bool cap2Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "cap", 2);
                bool cap3Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "cap", 3);
                bool cap4Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "cap", 4);
                await GenerarEspejoDocumental($"Certificado de Captura{sufijoUnidad}", doc.AplicaSernapesca, "CERTIFICADO CAPTURA SERNAPESCA", idAg, doc.ValCap1, doc.NumCap1);
                await GenerarEspejoDocumental($"Certificado de Captura R1{sufijoUnidad}", doc.AplicaSernapesca && cap2Bloqueado, "RE-EMISION", idAg, doc.ValCap2, doc.NumCap2);
                await GenerarEspejoDocumental($"Certificado de Captura R2{sufijoUnidad}", doc.AplicaSernapesca && cap3Bloqueado, "RE-EMISION", idAg, doc.ValCap3, doc.NumCap3);
                await GenerarEspejoDocumental($"Certificado de Captura R3{sufijoUnidad}", doc.AplicaSernapesca && cap4Bloqueado, "RE-EMISION", idAg, doc.ValCap4, doc.NumCap4);

                if (doc.AplicaSag)
                {
                    const string justificacionSagPlanta = "Costo Cero (Inspección en Planta)";

                    bool EsFitoDeEstaUnidad(string? concepto)
                    {
                        if (string.IsNullOrEmpty(concepto) || !concepto.StartsWith("Certificado Fitosanitario")) return false;
                        if (string.IsNullOrEmpty(sufijoUnidad)) return !concepto.Contains(" (U");
                        return concepto.EndsWith(sufijoUnidad);
                    }

                    var txsFito = _context.TransaccionesFinancieras.Local
                        .Where(t => t.IdOperacion == op.Id && EsFitoDeEstaUnidad(t.Concepto))
                        .ToList();
                    if (!txsFito.Any())
                    {
                        var candidatosFito = await _context.TransaccionesFinancieras
                            .Where(t => t.IdOperacion == op.Id && t.Concepto.StartsWith("Certificado Fitosanitario"))
                            .ToListAsync();
                        txsFito = candidatosFito.Where(t => EsFitoDeEstaUnidad(t.Concepto)).ToList();
                    }

                    if (doc.SagModalidad == "Planta")
                    {
                        foreach (var tx in txsFito)
                        {
                            tx.MontoNeto = 0m;
                            tx.Moneda = "CLP";
                            tx.TarifaManual = true;
                            tx.JustificacionManual = justificacionSagPlanta;
                            tx.EstadoFila = "PROVISIÓN";
                        }
                    }
                    else
                    {
                        foreach (var tx in txsFito.Where(t => t.TarifaManual && t.JustificacionManual == justificacionSagPlanta))
                        {
                            bool esReemision = tx.Concepto.Contains(" R1") || tx.Concepto.Contains(" R2") || tx.Concepto.Contains(" R3");
                            string busquedaFito = esReemision ? "RE-EMISION CERTIFICADOS SAG" : "FITO";

                            decimal montoTarifa = 0m;
                            string monedaTarifa = "USD";

                            if (idAg.HasValue)
                            {
                                var tarifaDoc = await _context.TarifasDocumentales
                                    .Where(t => t.EsActiva && t.IdAgenciaAduana == idAg.Value && t.Concepto.ToUpper().Contains(busquedaFito.ToUpper()))
                                    .OrderBy(t => t.Id)
                                    .FirstOrDefaultAsync();
                                if (tarifaDoc != null) { montoTarifa = tarifaDoc.ValorNeto; monedaTarifa = tarifaDoc.Moneda ?? "USD"; }
                            }

                            if (montoTarifa == 0m)
                            {
                                var tarifaMaestra = await _context.TarifasMaestras
                                    .FirstOrDefaultAsync(t => t.EsActiva && t.Categoria == "Documental" && t.Concepto.ToUpper().Contains(busquedaFito.ToUpper()));
                                if (tarifaMaestra != null) { montoTarifa = tarifaMaestra.ValorNeto; monedaTarifa = tarifaMaestra.Moneda ?? "USD"; }
                            }

                            (montoTarifa, monedaTarifa) = await NormalizarMonedaDocumentalAsync(montoTarifa, monedaTarifa);

                            tx.MontoNeto = montoTarifa;
                            tx.Moneda = monedaTarifa;
                            tx.TarifaManual = false;
                            tx.JustificacionManual = null;
                            tx.EstadoFila = montoTarifa > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR";
                        }
                    }
                }

                if (doc.AplicaSernapesca)
                {
                    var txsSani = _context.TransaccionesFinancieras.Local.Where(t => t.IdOperacion == op.Id && (t.Concepto ?? "").Contains("Certificado Sanitario")).ToList();
                    if (!txsSani.Any()) txsSani = await _context.TransaccionesFinancieras.Where(t => t.IdOperacion == op.Id && (t.Concepto ?? "").Contains("Certificado Sanitario")).ToListAsync();
                    foreach (var tx in txsSani) { if (tx.MontoNeto == 0m) tx.TarifaManual = false; }

                    var txsCap = _context.TransaccionesFinancieras.Local.Where(t => t.IdOperacion == op.Id && (t.Concepto ?? "").Contains("Certificado de Captura")).ToList();
                    if (!txsCap.Any()) txsCap = await _context.TransaccionesFinancieras.Where(t => t.IdOperacion == op.Id && (t.Concepto ?? "").Contains("Certificado de Captura")).ToListAsync();
                    foreach (var tx in txsCap) { if (tx.MontoNeto == 0m) tx.TarifaManual = false; }
                }

                await GenerarEspejoDocumental($"Trámite CODAUT{sufijoUnidad}", !string.IsNullOrEmpty(doc.NumCod1), "CODAUT", idAg, doc.ValCod1);
                await GenerarEspejoDocumental($"Trámite CLAVE{sufijoUnidad}", !string.IsNullOrEmpty(doc.NumCla1), "CLAVE", idAg, doc.ValCla1);
                await GenerarEspejoDocumental($"Trámite NEPPEX{sufijoUnidad}", !string.IsNullOrEmpty(doc.NumNep1), "NEPPEX", idAg, doc.ValNep1);

                var conceptosLegacyAEliminar = new[] {
                    $"Emisión DUS{sufijoUnidad}", $"Emisión DIN{sufijoUnidad}", $"Legalización Documental{sufijoUnidad}",
                    $"CERTIFICADO ORIGEN{sufijoUnidad}", $"CERTIFICADO SANITARIO SERNAPESCA{sufijoUnidad}",
                    $"CERTIFICADO CAPTURA SERNAPESCA{sufijoUnidad}", $"FITO{sufijoUnidad}",
                    $"Certificado Sanitario Sernapesca{sufijoUnidad}", $"Certificado Fitosanitario SAG{sufijoUnidad}",
                    $"Certificado de Origen FNG{sufijoUnidad}", $"Trámite COA Aduana{sufijoUnidad}"
                };
                var txsLegacy = txExistentes.Where(t => conceptosLegacyAEliminar.Contains(t.Concepto) && (t.EstadoFila == "PROVISIÓN" || t.EstadoFila == "PENDIENTE VALORIZAR")).ToList();
                foreach (var txLegacy in txsLegacy) _context.TransaccionesFinancieras.Remove(txLegacy);

                bool tieneDus = !string.IsNullOrEmpty(doc.DusDin);
                bool tieneDin = !string.IsNullOrEmpty(doc.Din);
                // A todo evento: todo servicio documental genera Legalización / Emisión DUS
                bool activaLegalizacionDus = true;
                decimal? valorLegalizacionDus = tieneDus ? doc.ValorDus : (tieneDin ? doc.ValorDin : null);
                string busquedaLegalizacionDus = (tieneDin && !tieneDus) ? "DIN" : "DUS EMBARQUES NORMALES";

                await GenerarEspejoDocumental($"Legalización / Emisión DUS{sufijoUnidad}", activaLegalizacionDus, busquedaLegalizacionDus, idAg, valorLegalizacionDus);
                // Honorarios de Agencia: se movió fuera de este loop — ver bloque "3b"
                // más abajo — porque se cobran SIEMPRE por contenedor, sin importar si
                // los certificados de este booking están en modalidad Grupal o Individual.

                // Inyección Courier a Finanzas
                if (doc.AplicaCourier == true)
                {
                    string conceptoCourier = "COURIER NACIONAL";

                    if (doc.TipoCourier == "Internacional")
                    {
                        // Recalculado en vivo contra los puertos ACTUALES de la operación, en vez de
                        // confiar en doc.ZonaCourier/ValorCourier guardados en la pestaña Courier —
                        // esos quedan obsoletos si el puerto se corrige después de guardarla.
                        bool esImportacionCourier = new[] { 8, 9, 10, 11, 12, 13, 14 }.Contains(op.IdTipoServicio ?? 0);
                        string? paisVigente = esImportacionCourier
                            ? op.IdPuertoOrigenNavigation?.Pais?.ToUpper()
                            : op.IdPuertoDestinoNavigation?.Pais?.ToUpper();

                        // Si el usuario seleccionó un país manualmente (distinto al inferido del
                        // puerto), se respeta esa elección explícita.
                        string? paisFinal = !string.IsNullOrWhiteSpace(doc.PaisCourier) ? doc.PaisCourier.ToUpper() : paisVigente;
                        string? zonaVigente = CourierZonas.ObtenerZona(paisFinal);

                        if (!string.IsNullOrEmpty(zonaVigente))
                            conceptoCourier = $"COURIER INTERNACIONAL {zonaVigente.ToUpper()}";
                    }

                    var tarifaCourierVigente = await _context.TarifasMaestras
                        .FirstOrDefaultAsync(t => t.EsActiva && t.Categoria == "Courier" && t.Concepto.ToUpper() == conceptoCourier.ToUpper());

                    await GenerarEspejoDocumental($"Servicio Courier{sufijoUnidad}", true, conceptoCourier, null, tarifaCourierVigente?.ValorNeto, null, "USD", convertirAClp: true);
                }
                await GenerarEspejoDocumental($"Aclaración Documental{sufijoUnidad}", doc.ExtensionDocumental == true, "Aclaración", idAg, doc.ValorAclaracion);
                await GenerarEspejoDocumental($"Cancelación Documental{sufijoUnidad}", doc.GuiaVisado == true, "Cancelación", idAg, doc.ValorCancelacion);
                await GenerarEspejoDocumental($"Roleo de Carga{sufijoUnidad}", doc.Roleo == true, "Roleo de Carga", idAg, doc.ValorRoleo);
                await GenerarEspejoDocumental($"Generación IVV{sufijoUnidad}", doc.GeneracionIvv == true, "Generación IVV", idAg);
            }

            // 3b. HONORARIOS DE AGENCIA — SIEMPRE por contenedor, a diferencia de los
            // certificados (que pueden ir Grupal o Individual). Un contenedor que todavía
            // no tiene su propio registro documental (modalidad Grupal, o Individual recién
            // activada sin guardar) hereda Agencia/SAG/Sernapesca del registro principal.
            if (reqDocumentalServicio)
            {
                var docPrincipalHon = op.OperacionesDocumentales.FirstOrDefault(d => d.IdUnidadTecnica == null);
                var contenedoresHon = new List<int?> { null };
                contenedoresHon.AddRange((op.Unidadestecnicas ?? new List<Unidadestecnica>()).Select(u => (int?)u.Id));

                foreach (var idUnidadHon in contenedoresHon)
                {
                    var docUnidadHon = op.OperacionesDocumentales.FirstOrDefault(d => d.IdUnidadTecnica == idUnidadHon) ?? docPrincipalHon;
                    if (docUnidadHon == null) continue;

                    string sufijoHon = idUnidadHon.HasValue ? $" (U{idUnidadHon})" : "";
                    int? idAgHon = docUnidadHon.IdAgenciaAduana;

                    decimal? honorariosAgencia = null;
                    if (docUnidadHon.AplicaSernapesca == true) honorariosAgencia = 111500m;
                    else if (docUnidadHon.AplicaSag == true) honorariosAgencia = 78000m;

                    await GenerarEspejoDocumental($"Honorarios Agencia de Aduanas{sufijoHon}", idAgHon.HasValue, "HONORARIOS AGENCIA", idAgHon, honorariosAgencia);
                }
            }

            // 4. ISPS - SEGURIDAD DE LA CARGA
            // Lista de contenedores del booking: Principal (null) + cada Unidadtécnica.
            // Gate e ISPS se cobran SIEMPRE por contenedor (a diferencia de los
            // certificados documentales, que dependen de la modalidad Grupal/Individual).
            var contenedoresGateIsps = new List<int?> { null };
            contenedoresGateIsps.AddRange((op.Unidadestecnicas ?? new List<Unidadestecnica>()).Select(u => (int?)u.Id));

            bool reqDocumentalOp = op.OperacionesDocumentales != null && op.OperacionesDocumentales.Any();
            string terminalActual = (terr?.TerminalTerrestreStr ?? "").ToUpper().Trim();

            // Salvaguarda: si el terminal no quedó guardado (ej. una operación "Solo
            // Marítimo" creada antes de corregir el formulario), se infiere desde el
            // puerto chileno de la operación cuando el caso es determinístico
            // (Valparaíso → TPS). San Antonio queda ambiguo entre STI y DP World y
            // requiere selección manual, así que no se auto-completa. Esto NO corrige
            // el dato en la base — solo evita que el cálculo de ISPS se pierda mientras
            // se resuelve la causa raíz en el formulario de creación.
            if (string.IsNullOrEmpty(terminalActual))
            {
                bool esExpoIsps = new[] { 1, 2, 3, 4, 5, 6, 7 }.Contains(op.IdTipoServicio ?? 0);
                string puertoChileno = (esExpoIsps ? op.IdPuertoOrigenNavigation?.NombrePuerto : op.IdPuertoDestinoNavigation?.NombrePuerto)?.ToUpper() ?? "";
                if (puertoChileno.Contains("VALPARAISO") || puertoChileno.Contains("VALPARAÍSO")) terminalActual = "TPS";
            }

            string? terminalLabel = null;
            string? conceptoIspsMaestro = null;

            if (terminalActual.Contains("TPS")) { terminalLabel = "TPS - Valparaíso"; conceptoIspsMaestro = "SEGURIDAD DE LA CARGA TPS"; }
            else if (terminalActual.Contains("STI")) { terminalLabel = "STI - San Antonio"; conceptoIspsMaestro = "SEGURIDAD DE LA CARGA STI"; }
            else if (terminalActual.Contains("DPW") || terminalActual.Contains("DP WORLD")) { terminalLabel = "DP World - San Antonio"; conceptoIspsMaestro = "SEGURIDAD DE LA CARGA DP WORLD"; }

            bool activaIsps = reqDocumentalOp && conceptoIspsMaestro != null;

            decimal? valorIspsOverride = null;
            string? monedaIspsOverride = null;
            if (activaIsps)
            {
                var tarifaIsps = await _context.TarifasMaestras.FirstOrDefaultAsync(t => t.EsActiva && t.Concepto.ToUpper() == conceptoIspsMaestro);
                if (tarifaIsps != null) { valorIspsOverride = tarifaIsps.ValorNeto; monedaIspsOverride = tarifaIsps.Moneda ?? "USD"; }
            }

            // El sufijo "(Uxx)" va INMEDIATAMENTE después de "ISPS" — igual que en
            // Flete Terrestre ($"Flete Terrestre{sufijoU} ({rutaU})") — para que
            // busquedaTarifa siga siendo un prefijo estable del concepto completo,
            // sin importar si el terminal (TPS/STI/DP World) cambia entre sincronías.
            foreach (var idUnidadIsps in contenedoresGateIsps)
            {
                string sufijoIsps = idUnidadIsps.HasValue ? $" (U{idUnidadIsps})" : "";
                string busquedaIspsU = $"Seguridad de la Carga ISPS{sufijoIsps}";
                string conceptoIspsFinalU = activaIsps ? $"{busquedaIspsU} ({terminalLabel})" : busquedaIspsU;
                await GenerarEspejo("Documental", conceptoIspsFinalU, activaIsps, busquedaIspsU, valorIspsOverride, monedaIspsOverride, convertirAClp: true);
            }

            // MOTOR DE TARIFAS GATE (IN/OUT) -> CATEGORÍA "DEPÓSITO"
            bool aplicaGateOut = op.TipoGate == "OUT" || op.TipoGate == "IN/OUT";
            bool aplicaGateIn = op.TipoGate == "IN" || op.TipoGate == "IN/OUT";

            if (aplicaGateOut || aplicaGateIn)
            {
                decimal? valorGateOutBase = null;
                decimal? valorGateInBase = null;
                string monedaGateOutBase = "CLP";
                string monedaGateInBase = "CLP";
                int? idProveedorGate = null;

                // Se compara el nombre de la naviera de ESTA operación (no "la" Hapag
                // global), porque pueden existir varias entidades Hapag distintas en la
                // tabla Navieras (ej. "HAPAG LLOYD" y "HAPAG LLOYD PERU"). Así la tarifa
                // fija se aplica sin importar cuál de ellas seleccionó la operación,
                // siempre que tenga su propia fila en TarifaGate con IdNaviera = op.IdNaviera.
                bool aplicaTarifaPlana = op.IdNavieraNavigation?.AplicaTarifaGatePlana == true;

                string depRetiro = terr?.DepositoRetiro ?? "";
                int? idDepositoFisico = null;
                if (!string.IsNullOrEmpty(depRetiro))
                {
                    var deposito = await _context.Depositos.FirstOrDefaultAsync(d => d.NombreDeposito == depRetiro);
                    idDepositoFisico = deposito?.Id;
                }

                string tc = op.TipoContenedor ?? "";
                bool isRf = tc.Contains("RF") || tc.Contains("REEFER") || op.IdTipoCarga == 2;
                string tipoBusq = isRf ? "RF" : "DRY";
                string sizeBusq = tc.Contains("20") ? "20" : (tc.Contains("40") ? "40" : "");

                // Devuelve monto Y moneda de la tarifa que matcheó. Antes esta función
                // descartaba el campo Moneda de TarifaGate y el motor forzaba CLP siempre;
                // por eso depósitos que cotizan en USD (ej. Agunsa) llegaban mal convertidos.
                // Ahora se respeta la moneda real cargada en la base de datos, fila por fila.
                (decimal Valor, string Moneda)? MatchTarifa(List<TarifaGate> tarifas)
                {
                    var match = tarifas.FirstOrDefault(t => t.TipoContenedor == sizeBusq + tipoBusq)
                             ?? tarifas.FirstOrDefault(t => t.TipoContenedor != null && t.TipoContenedor.Contains("20/40") && t.TipoContenedor.Contains(tipoBusq))
                             ?? tarifas.FirstOrDefault(t => t.TipoContenedor == tipoBusq)
                             ?? tarifas.FirstOrDefault(t => t.TipoContenedor == sizeBusq)
                             ?? tarifas.FirstOrDefault(t => t.TipoContenedor == "AMBOS");
                    if (match == null || !match.ValorNeto.HasValue) return null;
                    return (match.ValorNeto.Value, string.IsNullOrWhiteSpace(match.Moneda) ? "CLP" : match.Moneda);
                }

                // Tarifa plana por Naviera (Hapag-Lloyd, CMA CGM): aplica sin importar
                // el depósito físico de retiro. Si existen las 3 filas desglosadas
                // (BL Fee + Control Sello + Gate Out) para el tipo de contenedor exacto
                // (DRY/RF), se suman. Si no (naviera aún sin desglose cargado — caso
                // actual de Hapag), se cae al modo legado: una fila total única por
                // naviera+movimiento, sin distinguir DRY/RF.
                // Tarifa plana por Naviera: ahora recibe el sufijo del contenedor para
                // que las exclusiones de componentes (BL Fee / Control Sello / Gate Out)
                // que Finanzas desmarca en ControlFinanciero se respeten POR CONTENEDOR,
                // ya que cada unidad puede tener su propio desglose/exclusiones aplicados.
                async Task<(decimal Valor, string Moneda)?> ObtenerTarifaGatePlanaAsync(int idNaviera, string tipoMovimiento, string sufijoContenedor)
                {
                    var componentes = await _context.Set<TarifaGate>()
                        .Where(t => t.IdNaviera == idNaviera && t.TipoMovimiento == tipoMovimiento && t.EsActiva
                                 && t.Componente != null && t.TipoContenedor == tipoBusq)
                        .ToListAsync();

                    if (componentes.Any())
                    {
                        // Componentes que Finanzas desmarcó (exenciones del contrato) —
                        // ahora se buscan en la fila exacta de ESTE contenedor.
                        var txRef = txExistentes.FirstOrDefault(t => t.GrupoCobro == "Depósito" && t.TipoMovimiento == "EGRESO" && t.Concepto == $"Gate {tipoMovimiento} Contenedor{sufijoContenedor}");
                        var excluidos = (txRef?.ComponentesGateExcluidos ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var aplicables = componentes.Where(c => !excluidos.Contains(c.Componente ?? "")).ToList();
                        decimal suma = aplicables.Sum(c => c.ValorNeto ?? 0m);
                        string monedaSuma = string.IsNullOrWhiteSpace(componentes.First().Moneda) ? "USD" : componentes.First().Moneda;
                        return (suma, monedaSuma);
                    }

                    var legado = await _context.Set<TarifaGate>()
                        .FirstOrDefaultAsync(t => t.IdNaviera == idNaviera && t.TipoMovimiento == tipoMovimiento && t.EsActiva && t.Componente == null);
                    if (legado == null || !legado.ValorNeto.HasValue) return null;
                    return (legado.ValorNeto.Value, string.IsNullOrWhiteSpace(legado.Moneda) ? "CLP" : legado.Moneda);
                }

                // Tarifa por depósito (no-plana): es la misma tarifa física para cualquier
                // contenedor del booking (mismo depósito, misma naviera, mismo tipo de
                // equipo), así que se calcula una sola vez aquí y se reutiliza en el loop.
                if (aplicaGateOut && !aplicaTarifaPlana && idDepositoFisico != null)
                {
                    var tOutTodos = await _context.Set<TarifaGate>().Where(t => t.IdDeposito == idDepositoFisico && t.TipoMovimiento == "OUT" && t.EsActiva).ToListAsync();
                    var tOutNaviera = tOutTodos.Where(t => t.IdNaviera == op.IdNaviera).ToList();
                    var candidatasOut = tOutNaviera.Any() ? tOutNaviera : tOutTodos.Where(t => t.IdNaviera == null).ToList();
                    var resultOut = MatchTarifa(candidatasOut);
                    if (resultOut.HasValue) { valorGateOutBase = resultOut.Value.Valor; monedaGateOutBase = resultOut.Value.Moneda; }
                }

                if (aplicaGateIn && !aplicaTarifaPlana && idDepositoFisico != null)
                {
                    var tInTodos = await _context.Set<TarifaGate>().Where(t => t.IdDeposito == idDepositoFisico && t.TipoMovimiento == "IN" && t.EsActiva).ToListAsync();
                    var tInNaviera = tInTodos.Where(t => t.IdNaviera == op.IdNaviera).ToList();
                    var candidatasIn = tInNaviera.Any() ? tInNaviera : tInTodos.Where(t => t.IdNaviera == null).ToList();
                    var resultIn = MatchTarifa(candidatasIn);
                    if (resultIn.HasValue) { valorGateInBase = resultIn.Value.Valor; monedaGateInBase = resultIn.Value.Moneda; }
                }

                // No se guarda el Id de la Agencia en IdProveedor: esa columna es FK hacia
                // Proveedores, no hacia AgenciasAduana. Cuando el pago lo gestiona la Agencia,
                // ControlFinanciero.cshtml resuelve el nombre a mostrar directamente desde
                // op.PagoGate + OperacionesDocumentales, igual que en la sección Documental.
                if (op.PagoGate == "Atilson paga directamente" && !string.IsNullOrEmpty(depRetiro))
                {
                    var provDepo = await _context.Proveedores.FirstOrDefaultAsync(p => p.NombreProveedor.Contains(depRetiro));
                    idProveedorGate = provDepo?.Id;
                }

                // Una transacción por contenedor del booking (Principal + Unidadestecnicas).
                // El valor plano por naviera (si aplica) se recalcula por contenedor porque
                // sus exclusiones de componentes pueden ser distintas entre unidades.
                foreach (var idUnidadGate in contenedoresGateIsps)
                {
                    string sufijoGate = idUnidadGate.HasValue ? $" (U{idUnidadGate})" : "";

                    decimal? valorGateOut = valorGateOutBase;
                    string monedaGateOut = monedaGateOutBase;
                    decimal? valorGateIn = valorGateInBase;
                    string monedaGateIn = monedaGateInBase;

                    if (aplicaGateOut && aplicaTarifaPlana)
                    {
                        var resultOutPlana = await ObtenerTarifaGatePlanaAsync(op.IdNaviera, "OUT", sufijoGate);
                        if (resultOutPlana.HasValue) { valorGateOut = resultOutPlana.Value.Valor; monedaGateOut = resultOutPlana.Value.Moneda; }
                    }
                    if (aplicaGateIn && aplicaTarifaPlana)
                    {
                        var resultInPlana = await ObtenerTarifaGatePlanaAsync(op.IdNaviera, "IN", sufijoGate);
                        if (resultInPlana.HasValue) { valorGateIn = resultInPlana.Value.Valor; monedaGateIn = resultInPlana.Value.Moneda; }
                    }

                    string conceptoGateOutU = $"Gate OUT Contenedor{sufijoGate}";
                    string conceptoGateInU = $"Gate IN Contenedor{sufijoGate}";
                    await GenerarEspejo("Depósito", conceptoGateOutU, aplicaGateOut, conceptoGateOutU, valorGateOut, monedaGateOut, idProveedorGate, convertirAClp: true);
                    await GenerarEspejo("Depósito", conceptoGateInU, aplicaGateIn, conceptoGateInU, valorGateIn, monedaGateIn, idProveedorGate, convertirAClp: true);
                }
            }

            // 5. EXTRACOSTOS: CÁLCULOS MATEMÁTICOS DE FALSOS FLETES EN RUTA + extracostos
            // reportados libremente desde Operaciones. El GrupoOperativo viene explícito
            // desde el propio dropdown de Operaciones (guardado al momento de reportar);
            // solo se usa un heurístico por palabra clave como respaldo para extracostos
            // antiguos que se crearon antes de que existiera ese campo. El fallback final
            // es "Otros" — nunca una categoría inexistente, porque eso lo deja invisible
            // tanto en Acuerdos Operativos como en Control Financiero.
            var extracostos = op.ExtracostosOperacions.ToList();

            string ClasificarGrupoExtracosto(string tipoCosto)
            {
                string t = (tipoCosto ?? "").ToUpper();
                if (t.Contains("ROLEO") || t.Contains("CANCELACIÓN") || t.Contains("CANCELACION") ||
                    t.Contains("LATE ARRIVAL") || t.Contains("LAR") || t.Contains("ELAR") ||
                    t.Contains("DEMURRAGE") || t.Contains("CORRECTOR") || t.Contains("CORRECCION") || t.Contains("CORRECCIÓN") ||
                    t.Contains("PLUG") || t.Contains("MATRIZ") || t.Contains("BL ") || t.StartsWith("BL") ||
                    t.Contains("EARLY ARRIVAL") || t.Contains("TELEX") || t.Contains("EXPRESS RELEASE") ||
                    t.Contains("GATE OUT") || t.Contains("GATE IN") && t.Contains("MSC"))
                    return "Marítimo";
                if (t.Contains("FALSO FLETE") || t.Contains("PORTEO") || t.Contains("INTERPLANTA") ||
                    t.Contains("SOBREESTADÍA") || t.Contains("SOBREESTADIA") || t.Contains("UNDERLAND"))
                    return "Transporte";
                if (t.Contains("ESCÁNER") || t.Contains("ESCANER") || t.Contains("AFORO") ||
                    t.Contains("PROVIDENCIA") || t.Contains("SELLO") || t.Contains("ALMACENAMIENTO") ||
                    t.Contains("TRASPASO") || t.Contains("HABILITACION") || t.Contains("HABILITACIÓN"))
                    return "Documental";
                return "Otros";
            }
            string? NormalizarResponsabilidad(string? valor)
            {
                if (string.IsNullOrWhiteSpace(valor)) return null;
                return valor.Trim().ToUpper() switch
                {
                    "CLIENTE" => ResponsabilidadValores.Cliente,
                    "ATILSON" => ResponsabilidadValores.Atilson,
                    "PROVEEDOR" => ResponsabilidadValores.Proveedor,
                    "AGENCIA" => ResponsabilidadValores.Agencia,
                    _ => null
                };
            }

            foreach (var ext in extracostos)
            {
                decimal? montoOverride = null;
                string? monedaOverride = null;
                string grupoExtracosto = !string.IsNullOrWhiteSpace(ext.GrupoOperativo)
                    ? ext.GrupoOperativo
                    : ClasificarGrupoExtracosto(ext.TipoCosto);

                if (tarifaTranspDb.HasValue)
                {
                    if (ext.TipoCosto == "Falso Flete en Ruta")
                    {
                        if (ext.Motivo.Contains("Mas de la mitad")) montoOverride = tarifaTranspDb * 0.80m;
                        else if (ext.Motivo.Contains("Menos de la mitad")) montoOverride = tarifaTranspDb * 0.70m;
                        monedaOverride = "CLP";
                    }
                    else if (ext.TipoCosto == "Falso Flete Planta")
                    {
                        montoOverride = tarifaTranspDb * 0.90m;
                        monedaOverride = "CLP";
                    }
                }

                // Roleo/Cancelación: comportamiento ya existente, se mantiene intacto.
                if (ext.TipoCosto == "Roleo Marítimo" || ext.TipoCosto == "Cancelación / No Show Marítimo")
                {
                    grupoExtracosto = "Marítimo";
                    if (ext.Monto > 0)
                    {
                        montoOverride = ext.Monto;
                        monedaOverride = string.IsNullOrWhiteSpace(ext.Moneda) ? "USD" : ext.Moneda;
                    }
                }
                // Extracosto reportado libremente desde el modal: usa el valor y moneda
                // que la ejecutiva indicó al reportarlo — solo si no fue ya calculado
                // por una de las reglas de arriba (Falso Flete, Roleo).
                else if (!montoOverride.HasValue && ext.Monto > 0)
                {
                    montoOverride = ext.Monto;
                    monedaOverride = string.IsNullOrWhiteSpace(ext.Moneda) ? "CLP" : ext.Moneda;
                }

                string conceptoUnico = $"{ext.TipoCosto} (EXT-{ext.Id})";
                bool convertirAClpExt = monedaOverride == "USD" && grupoExtracosto != "Marítimo";
                await GenerarEspejo(grupoExtracosto, conceptoUnico, true, conceptoUnico, montoOverride, monedaOverride, convertirAClp: convertirAClpExt, responsablePagoOverride: NormalizarResponsabilidad(ext.ResponsableGestion), responsableOverride: NormalizarResponsabilidad(ext.Responsable));
            }

            // Se revisan TODAS las filas que referencian un extracosto (patrón "(EXT-N)"
            // en el concepto), sin filtrar por GrupoCobro: un extracosto como "Roleo
            // Marítimo" puede cambiar de grupo con el tiempo (de "Extracosto" a
            // "Marítimo", por ejemplo), y las filas viejas quedaban huérfanas para
            // siempre porque el filtro anterior solo miraba GrupoCobro == "Extracosto".
            var txExtracostos = txExistentes.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.Concepto ?? "", @"\(EXT-\d+\)")).ToList();
            foreach (var txe in txExtracostos)
            {
                var match = System.Text.RegularExpressions.Regex.Match(txe.Concepto ?? "", @"\(EXT-(\d+)\)");
                if (!match.Success) continue;
                int extId = int.Parse(match.Groups[1].Value);

                if (!extracostos.Any(e => e.Id == extId))
                {
                    if (txe.EstadoFila == "PROVISIÓN" || txe.EstadoFila == "PENDIENTE VALORIZAR")
                        _context.TransaccionesFinancieras.Remove(txe);
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}