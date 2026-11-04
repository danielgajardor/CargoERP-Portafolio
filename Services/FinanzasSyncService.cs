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
            { "TLC MÉXICO", (0m, false) },
            { "TLC COREA DEL SUR", (0m, false) },
        };

        // Estos tres acuerdos son una condición comercial exclusiva del cliente
        // especial (Promar) — nunca deben resultar en $0 para un cliente estándar,
        // aunque el dato AcuerdoOrigen haya quedado guardado así por error, por una
        // operación antigua, o porque el cliente dejó de tener EsClienteEspecial
        // después de guardarse. Este gate vive en el cálculo, no solo en el <select>
        // del panel documental, porque el <select> solo evita el error a futuro —
        // no corrige un dato que ya esté guardado.
        private static readonly HashSet<string> AcuerdosOrigenSoloClienteEspecial = new(StringComparer.OrdinalIgnoreCase)
        {
            "TLC USA", "TLC MÉXICO", "TLC COREA DEL SUR"
        };

        // Detección automática de exención TLC por destino del Booking — Informe Caso
        // Promar, Regla #1. Exclusiva de clientes especiales (EsClienteEspecial), igual
        // que la selección manual que reemplaza. Se chequea primero por país y, si no
        // hay match o el país no está cargado, por nombre de puerto.
        private static readonly Dictionary<string, string> PaisesTlcAutoExencion = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ESTADOS UNIDOS", "TLC USA" }, { "USA", "TLC USA" },
            { "MÉXICO", "TLC MÉXICO" }, { "MEXICO", "TLC MÉXICO" },
            { "COREA DEL SUR", "TLC COREA DEL SUR" }, { "COREA", "TLC COREA DEL SUR" },
        };
        private static readonly Dictionary<string, string> PuertosTlcAutoExencion = new(StringComparer.OrdinalIgnoreCase)
        {
            { "MANZANILLO", "TLC MÉXICO" },
            { "BUSAN", "TLC COREA DEL SUR" },
        };

        private static string? DetectarAcuerdoTlcPorDestino(string? paisDestino, string? nombrePuertoDestino)
        {
            if (!string.IsNullOrWhiteSpace(paisDestino) && PaisesTlcAutoExencion.TryGetValue(paisDestino.Trim(), out var acuerdoPais))
                return acuerdoPais;
            if (!string.IsNullOrWhiteSpace(nombrePuertoDestino))
            {
                string pUpper = nombrePuertoDestino.ToUpper();
                foreach (var kv in PuertosTlcAutoExencion)
                    if (pUpper.Contains(kv.Key)) return kv.Value;
            }
            return null;
        }

        private static decimal? CalcularValorAcuerdoOrigen(string? acuerdoOrigen, bool esClienteEspecial)
        {
            if (string.IsNullOrWhiteSpace(acuerdoOrigen)) return null;
            if (AcuerdosOrigenSoloClienteEspecial.Contains(acuerdoOrigen) && !esClienteEspecial) return null;
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
        // Penalidades de naviera que Finanzas mantiene en USD (sin pasar a CLP):
        // Roleo, Cancelación y No Show — igual que el Flete Marítimo.
        public static bool EsPenalidadNavieraEnUsd(string? tipoCosto)
        {
            string t = (tipoCosto ?? "").ToUpperInvariant();
            return t.Contains("ROLEO") || t.Contains("NO SHOW") || t.Contains("NOSHOW")
                || t.Contains("CANCELACIÓN") || t.Contains("CANCELACION");
        }

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

        // NOTA (auditoría oct-2026): este método tiene ~1000 líneas con 4 funciones
        // locales anidadas (GenerarEspejo, GenerarEspejoDocumental, ObtenerTarifaTransportistaAsync,
        // ObtenerTarifaGatePlanaAsync) — casi imposible de testear aisladamente. Se agregaron los
        // "#region" de abajo para que al menos sea navegable (cada bloque de negocio — Marítimo,
        // Terrestre, Documental, ISPS, Gate, Extracostos — se puede colapsar/ubicar de un vistazo).
        //
        // NO se hizo la extracción completa a clases/estrategias separadas que sugiere el informe:
        // casi todo el método anidado lee y escribe el mismo estado compartido (op, txExistentes,
        // conceptosGratuitosCliente, las listas precargadas de tarifas, y las propias funciones
        // GenerarEspejo/GenerarEspejoDocumental) por clausura (closure). Separarlo en clases reales
        // significa reescribir esa cadena de dependencias a mano, sin poder compilar ni ejecutar
        // este motor para verificar que cada captura de variable se movió correctamente — en el
        // motor que calcula y concilia movimientos financieros reales de la empresa, un error de
        // ese tipo no rompe la compilación (se notaría de inmediato): queda compilando pero
        // calculando mal algún monto/moneda/responsable, de forma silenciosa. Se prefirió no
        // arriesgar eso a ciegas; queda como recomendación para una sesión dedicada con el
        // compilador disponible, idealmente con pruebas de regresión antes/después por operación.
        public async Task SincronizarAsync(int idOperacion, string usuario)
        {
            #region Preparación (carga de la operación, tarifas gratuitas del cliente, preload de tarifas documentales)
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

            // Tarifas comerciales especiales del cliente (ej. Promar): conceptos marcados
            // "Servicio Gratuito" en TarifasClientes (Comercial) que no deben cobrarse al
            // cliente aunque el motor calcule un valor — el Egreso real (lo que Atilson le
            // paga a la agencia/naviera/transportista) sigue intacto, solo se anula el
            // Ingreso. Una sola consulta por sincronización, reutilizada en ambos
            // generadores de abajo.
            var conceptosGratuitosCliente = await _context.TarifasClientes.AsNoTracking()
                .Where(t => t.IdCliente == op.IdCliente && t.EsActiva && t.EsServicioGratuito)
                .Select(t => t.Concepto!)
                .ToListAsync();

            // Tarifas Documentales/Maestras: se precargan UNA sola vez por sincronización
            // (en vez de una consulta por cada certificado × documento × unidad técnica, que
            // es lo que hacía GenerarEspejoDocumental antes de este cambio — con varios
            // contenedores esto llegaba a cientos de round-trips). Ambas listas ya vienen
            // ordenadas/filtradas igual que las consultas que reemplazan, así que filtrar en
            // memoria más abajo da el mismo resultado.
            var todasTarifasDocumentalesActivas = await _context.TarifasDocumentales.AsNoTracking()
                .Where(t => t.EsActiva == true)
                .OrderBy(t => t.Id)
                .ToListAsync();
            var todasTarifasMaestrasDocumental = await _context.TarifasMaestras.AsNoTracking()
                .Where(t => t.EsActiva && t.Categoria == "Documental")
                .ToListAsync();

            // Tarifas de VENTA del cliente (Comercial > Clientes), vigentes hoy. Con ellas
            // el Ingreso de Flete Marítimo, Flete Terrestre, Gate y Courier toma el valor
            // pactado con el cliente; sin tarifa de venta, el Ingreso sigue espejando el
            // costo (comportamiento anterior). Ver VentaClienteHelper.
            var ventasCliente = await _context.TarifasClientes.AsNoTracking()
                .Where(t => t.IdCliente == op.IdCliente && t.EsActiva && !t.EsServicioGratuito
                         && t.PrecioPactado > 0 && t.FechaInicioVigencia <= ahora && t.FechaFinVigencia >= ahora.Date)
                .OrderByDescending(t => t.Id)
                .ToListAsync();
            #endregion

            #region GenerarEspejo / GenerarEspejoDocumental (funciones locales compartidas por todos los bloques de abajo)
            bool EsConceptoGratuitoCliente(string conceptoCompleto)
            {
                if (!conceptosGratuitosCliente.Any() || string.IsNullOrWhiteSpace(conceptoCompleto)) return false;
                string baseConcepto = System.Text.RegularExpressions.Regex.Replace(conceptoCompleto, @"\s*\(U\d+\)", "").Trim().ToUpper();
                return conceptosGratuitosCliente.Any(c => !string.IsNullOrWhiteSpace(c) &&
                    (baseConcepto.Contains(c.ToUpper()) || c.ToUpper().Contains(baseConcepto)));
            }

            // costoSinCargo / ventaSinCargo (oct-2026): el acuerdo Collect de la naviera / del
            // cliente deja ese concepto sin costo (ej. Late Arrival en cerezas). La línea queda
            // en $0, en provisión y marcada en JustificacionManual (no es "Por asignar").
            async Task GenerarEspejo(string grupo, string concepto, bool activo, string busquedaTarifa, decimal? valorOverride = null, string? monedaOverride = null, int? idProveedorOverride = null, bool convertirAClp = false, bool? ivaExento = null, string? responsablePagoOverride = null, string? responsableOverride = null, decimal? valorVentaOverride = null, string? monedaVentaOverride = null, bool costoSinCargo = false, bool ventaSinCargo = false, string? notaSinCargo = null, bool soloTarifaEspecifica = false)
            {
                string? MarcaSinCargo(bool sinCargo, string? actual) =>
                    sinCargo ? (notaSinCargo ?? AcuerdoCollectHelper.MarcaSinCosto)
                    : (AcuerdoCollectHelper.EsMarcaSinCosto(actual) ? null : actual);
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
                    // Flete terrestre y flete marítimo (oct-2026): el costo SOLO sale de la tarifa
                    // del proveedor para ese tramo/ruta. Si no hay, queda en $0 "Pendiente valorizar"
                    // (Por asignar). Antes caía a un valor genérico del maestro de conceptos, que
                    // aparecía como si fuera la tarifa real.
                    else if (soloTarifaEspecifica) { montoCosto = 0m; moneda = monedaOverride ?? moneda; }
                    else
                    {
                        var tarifa = await _context.TarifasMaestras.AsNoTracking().FirstOrDefaultAsync(t => t.EsActiva && t.Concepto.ToUpper().Contains(busquedaTarifa.ToUpper()));
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
                        // Antes se multiplicaba por un valor fijo de 37.500; ahora la UF del día.
                        var (montoUfClp, monedaUfClp, _) = await NormalizarMonedaConTasaAsync(montoCosto, moneda);
                        montoCosto = montoUfClp; moneda = monedaUfClp;
                    }
                    // Venta al cliente (oct-2026): si Comercial tiene una tarifa de venta
                    // vigente para este cliente y concepto, el Ingreso usa ESE valor (con su
                    // propia moneda). Si no hay, se mantiene el espejo del costo como antes.
                    decimal montoVenta = montoCosto;
                    string monedaVenta = moneda;
                    decimal? tasaVenta = tasaUsada;
                    decimal? montoOriginalUsdVenta = montoOriginalUsd;
                    if (valorVentaOverride.HasValue && valorVentaOverride.Value > 0)
                    {
                        montoVenta = valorVentaOverride.Value;
                        monedaVenta = string.IsNullOrWhiteSpace(monedaVentaOverride) ? "CLP" : monedaVentaOverride.Trim().ToUpperInvariant();
                        tasaVenta = null;
                        montoOriginalUsdVenta = null;
                        if (convertirAClp)
                        {
                            if (monedaVenta == "USD") montoOriginalUsdVenta = montoVenta;
                            var (montoVentaClp, monedaVentaClp, tasaV) = await NormalizarMonedaConTasaAsync(montoVenta, monedaVenta);
                            montoVenta = montoVentaClp; monedaVenta = monedaVentaClp; tasaVenta = tasaV;
                        }
                        else if (monedaVenta == "UF")
                        {
                            var (montoVentaClp, monedaVentaClp, _) = await NormalizarMonedaConTasaAsync(montoVenta, monedaVenta);
                            montoVenta = montoVentaClp; monedaVenta = monedaVentaClp;
                        }
                    }

                    // Sin costo por acuerdo: el costo va en $0, pero si el cliente sí paga (su
                    // acuerdo no lo libera), su cobro sigue siendo el valor normal del concepto.
                    if (costoSinCargo) { montoCosto = 0m; montoOriginalUsd = null; tasaUsada = null; }

                    // Si Responsable != Cliente (Atilson lo absorbe, o es culpa de un
                    // Proveedor/Agencia), el Ingreso al cliente no debe existir: no hay
                    // nada que cobrarle por un concepto que no le corresponde pagar.
                    string responsableParaNuevo = responsableOverride ?? ResponsabilidadValores.Cliente;
                    bool esResponsableCliente = txIngreso == null
                        ? (responsableParaNuevo == ResponsabilidadValores.Cliente)
                        : (string.IsNullOrEmpty(txIngreso.Responsable) || txIngreso.Responsable == ResponsabilidadValores.Cliente);
                    bool esGratuitoCliente = EsConceptoGratuitoCliente(concepto);
                    decimal montoVentaFinal = (esResponsableCliente && !esGratuitoCliente) ? montoVenta : 0m;
                    string estadoVentaFinal = esGratuitoCliente
                        ? "PROVISIÓN"
                        : (esResponsableCliente
                            ? (montoVentaFinal > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR")
                            : "NO FACTURABLE");
                    if (ventaSinCargo)
                    {
                        montoVentaFinal = 0m; montoOriginalUsdVenta = null; tasaVenta = null;
                        if (estadoVentaFinal != "NO FACTURABLE") estadoVentaFinal = "PROVISIÓN";
                    }
                    string estadoCostoFinal = costoSinCargo ? "PROVISIÓN" : (montoCosto > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR");

                    if (txIngreso == null)
                    {
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = grupo, TipoMovimiento = "INGRESO", Concepto = concepto, MontoNeto = montoVentaFinal, Moneda = monedaVenta, TipoCambio = esResponsableCliente ? tasaVenta : null, MontoOriginalUsd = esResponsableCliente ? montoOriginalUsdVenta : null, EstadoFila = estadoVentaFinal, FechaCreacion = ahora, UsuarioCreador = usuario, IdCliente = op.IdCliente, ResponsablePago = responsablePagoOverride ?? ResponsabilidadValores.Atilson, Responsable = responsableParaNuevo, IvaExento = ivaExento, JustificacionManual = MarcaSinCargo(ventaSinCargo, null) });
                    }
                    else
                    {
                        txIngreso.Concepto = concepto;
                        if (!txIngreso.TarifaManual)
                        {
                            txIngreso.MontoNeto = montoVentaFinal;
                            txIngreso.Moneda = monedaVenta;
                            txIngreso.TipoCambio = esResponsableCliente ? tasaVenta : null;
                            txIngreso.MontoOriginalUsd = esResponsableCliente ? montoOriginalUsdVenta : null;
                            txIngreso.EstadoFila = estadoVentaFinal;
                            txIngreso.IvaExento = ivaExento;
                            txIngreso.JustificacionManual = MarcaSinCargo(ventaSinCargo, txIngreso.JustificacionManual);
                        }
                    }

                    if (txEgreso == null)
                    {
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = grupo, TipoMovimiento = "EGRESO", Concepto = concepto, MontoNeto = montoCosto, Moneda = moneda, TipoCambio = tasaUsada, MontoOriginalUsd = montoOriginalUsd, EstadoFila = estadoCostoFinal, FechaCreacion = ahora, UsuarioCreador = usuario, IdProveedor = idProveedorOverride, ResponsablePago = responsablePagoOverride ?? ResponsabilidadValores.Atilson, Responsable = responsableParaNuevo, IvaExento = ivaExento, JustificacionManual = MarcaSinCargo(costoSinCargo, null) });
                    }
                    else
                    {
                        txEgreso.Concepto = concepto;
                        if (idProveedorOverride.HasValue) txEgreso.IdProveedor = idProveedorOverride;
                        if (!txEgreso.TarifaManual) { txEgreso.MontoNeto = montoCosto; txEgreso.Moneda = moneda; txEgreso.TipoCambio = tasaUsada; txEgreso.MontoOriginalUsd = montoOriginalUsd; txEgreso.EstadoFila = estadoCostoFinal; txEgreso.IvaExento = ivaExento; txEgreso.JustificacionManual = MarcaSinCargo(costoSinCargo, txEgreso.JustificacionManual); }
                    }
                }
                else
                {
                    if (txIngreso != null && (txIngreso.EstadoFila == "PROVISIÓN" || txIngreso.EstadoFila == "PENDIENTE VALORIZAR" || txIngreso.EstadoFila == "NO FACTURABLE")) _context.TransaccionesFinancieras.Remove(txIngreso);
                    if (txEgreso != null && (txEgreso.EstadoFila == "PROVISIÓN" || txEgreso.EstadoFila == "PENDIENTE VALORIZAR")) _context.TransaccionesFinancieras.Remove(txEgreso);
                }
            }

            async Task GenerarEspejoDocumental(string conceptoEspejo, bool activo, string busquedaTarifa, int? idAgenciaAduana, decimal? valorOverride = null, string? numeroDocumento = null, string? monedaOverride = null, bool convertirAClp = false, decimal? valorVentaOverride = null, string? monedaVentaOverride = null, bool generarIngreso = true, decimal factorIngreso = 1m, bool forzarIngreso = false)
            {
                var txsIngreso = txExistentes.Where(t => t.Concepto == conceptoEspejo && t.TipoMovimiento == "INGRESO").ToList();
                var txsEgreso = txExistentes.Where(t => t.Concepto == conceptoEspejo && t.TipoMovimiento == "EGRESO").ToList();

                var txIngreso = txsIngreso.FirstOrDefault();
                var txEgreso = txsEgreso.FirstOrDefault();

                if (txsIngreso.Count > 1) _context.TransaccionesFinancieras.RemoveRange(txsIngreso.Skip(1));
                if (txsEgreso.Count > 1) _context.TransaccionesFinancieras.RemoveRange(txsEgreso.Skip(1));

                // generarIngreso = false: el costo se registra igual (ej. la AGA cobra este
                // contenedor), pero al cliente no se le cobra en esta línea porque va en un
                // solo cobro grupal (oct-2026, Honorarios y DUS). Se retira el Ingreso si aún
                // está en provisión; uno ya facturado o ajustado a mano no se toca.
                if (!generarIngreso && txIngreso != null && !txIngreso.TarifaManual
                    && (txIngreso.EstadoFila == "PROVISIÓN" || txIngreso.EstadoFila == "PENDIENTE VALORIZAR" || txIngreso.EstadoFila == "NO FACTURABLE"))
                {
                    _context.TransaccionesFinancieras.Remove(txIngreso);
                    txExistentes.Remove(txIngreso);
                    txIngreso = null;
                }

                if (activo)
                {
                    // Para conceptos con convertirAClp=true (hoy solo Courier) también se
                    // reingresa al bloque si falta TipoCambio guardado — esto "sana" filas
                    // creadas antes de que este campo empezara a persistirse correctamente,
                    // sin depender de que cambien de estado o de N° de documento.
                    bool faltaTipoCambio = convertirAClp && ((txIngreso != null && (txIngreso.TipoCambio == null || txIngreso.MontoOriginalUsd == null)) || (txEgreso != null && (txEgreso.TipoCambio == null || txEgreso.MontoOriginalUsd == null)));

                    // Si el concepto debería quedar en $0 por tarifa plana (checkbox "Servicio
                    // Gratuito" de Comercial) o por exención TLC, pero la fila ya tiene un monto
                    // distinto de $0 guardado de antes, hay que forzar el recálculo aunque nada
                    // más haya cambiado. Nunca toca una fila ya calibrada a mano (TarifaManual).
                    bool deberiaSerCeroAhora = EsConceptoGratuitoCliente(conceptoEspejo) || (valorOverride.HasValue && valorOverride.Value == 0m);
                    bool necesitaAjusteGratuito = txIngreso != null && !txIngreso.TarifaManual && deberiaSerCeroAhora && txIngreso.MontoNeto != 0m;

                    // Con tarifa de venta del cliente (Comercial), el Ingreso se recalcula
                    // mientras siga en provisión y no se haya ajustado a mano.
                    // forzarIngreso: el Ingreso depende de algo más que el costo (ej. cobro
                    // grupal de Honorarios/DUS), así que se recalcula en cada sincronización
                    // mientras siga en provisión.
                    bool necesitaAjusteVenta = ((valorVentaOverride.HasValue && valorVentaOverride.Value > 0) || forzarIngreso)
                        && txIngreso != null && !txIngreso.TarifaManual
                        && (txIngreso.EstadoFila == "PROVISIÓN" || txIngreso.EstadoFila == "PENDIENTE VALORIZAR");

                    if (txIngreso == null || txEgreso == null || txIngreso.EstadoFila == "PENDIENTE VALORIZAR" || txEgreso.EstadoFila == "PENDIENTE VALORIZAR" || txIngreso.NumeroDocumento != numeroDocumento || faltaTipoCambio || necesitaAjusteGratuito || necesitaAjusteVenta)
                    {
                        decimal montoCosto = 0m;
                        string moneda = "CLP";

                        if (idAgenciaAduana.HasValue)
                        {
                            // Preload en memoria (ver todasTarifasDocumentalesActivas arriba) — mismo
                            // filtro y mismo orden que la consulta original, sin ir a la BD por cada llamada.
                            var tarifaDoc = todasTarifasDocumentalesActivas
                                .FirstOrDefault(t => t.IdAgenciaAduana == idAgenciaAduana.Value && t.Concepto.ToUpper().Contains(busquedaTarifa.ToUpper()));

                            if (tarifaDoc != null)
                            {
                                montoCosto = tarifaDoc.ValorNeto;
                                moneda = tarifaDoc.Moneda ?? "CLP";
                            }
                        }

                        if (montoCosto == 0m)
                        {
                            var tarifaMaestra = todasTarifasMaestrasDocumental
                                .FirstOrDefault(t => t.Concepto.ToUpper().Contains(busquedaTarifa.ToUpper()));
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
                        // factorIngreso: cobro grupal = el costo de una unidad x N contenedores en
                        // una sola línea (cuando no se conoce la suma exacta por contenedor).
                        decimal montoVenta = Math.Round(montoCosto * factorIngreso, moneda == "CLP" ? 0 : 2);
                        string monedaVentaDoc = moneda;
                        decimal? tasaVentaDoc = tasaUsadaDoc;
                        decimal? montoOriginalUsdVentaDoc = montoOriginalUsdDoc.HasValue ? montoOriginalUsdDoc * factorIngreso : null;
                        if (valorVentaOverride.HasValue && valorVentaOverride.Value > 0)
                        {
                            montoVenta = valorVentaOverride.Value;
                            monedaVentaDoc = string.IsNullOrWhiteSpace(monedaVentaOverride) ? "CLP" : monedaVentaOverride.Trim().ToUpperInvariant();
                            tasaVentaDoc = null;
                            montoOriginalUsdVentaDoc = null;
                            if (convertirAClp)
                            {
                                if (monedaVentaDoc == "USD") montoOriginalUsdVentaDoc = montoVenta;
                                var (mV, monV, tV) = await NormalizarMonedaConTasaAsync(montoVenta, monedaVentaDoc);
                                montoVenta = mV; monedaVentaDoc = monV; tasaVentaDoc = tV;
                            }
                            else
                            {
                                (montoVenta, monedaVentaDoc) = await NormalizarMonedaDocumentalAsync(montoVenta, monedaVentaDoc);
                            }
                        }

                        string estadoVenta = (montoVenta > 0 || (valorOverride.HasValue && valorOverride.Value == 0)) ? "PROVISIÓN" : "PENDIENTE VALORIZAR";
                        string estadoCosto = (montoCosto > 0 || (valorOverride.HasValue && valorOverride.Value == 0)) ? "PROVISIÓN" : "PENDIENTE VALORIZAR";

                        bool esResponsableClienteDoc = txIngreso == null || string.IsNullOrEmpty(txIngreso.Responsable) || txIngreso.Responsable == ResponsabilidadValores.Cliente;
                        bool esGratuitoClienteDoc = EsConceptoGratuitoCliente(conceptoEspejo);
                        decimal montoVentaFinalDoc = (esResponsableClienteDoc && !esGratuitoClienteDoc) ? montoVenta : 0m;
                        string estadoVentaFinalDoc = esGratuitoClienteDoc ? "PROVISIÓN" : (esResponsableClienteDoc ? estadoVenta : "NO FACTURABLE");

                        if (txIngreso == null) { if (generarIngreso) _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = "Documental", TipoMovimiento = "INGRESO", Concepto = conceptoEspejo, MontoNeto = montoVentaFinalDoc, Moneda = monedaVentaDoc, TipoCambio = esResponsableClienteDoc ? tasaVentaDoc : null, MontoOriginalUsd = esResponsableClienteDoc ? montoOriginalUsdVentaDoc : null, EstadoFila = estadoVentaFinalDoc, FechaCreacion = ahora, UsuarioCreador = usuario, IdCliente = op.IdCliente, NumeroDocumento = numeroDocumento, ResponsablePago = ResponsabilidadValores.Atilson, Responsable = ResponsabilidadValores.Cliente }); }
                        else
                        {
                            txIngreso.NumeroDocumento = numeroDocumento;
                            if (txIngreso.EstadoFila == "PENDIENTE VALORIZAR" || necesitaAjusteGratuito || necesitaAjusteVenta) { txIngreso.MontoNeto = montoVentaFinalDoc; txIngreso.Moneda = monedaVentaDoc; txIngreso.EstadoFila = estadoVentaFinalDoc; }
                            else if (!esResponsableClienteDoc && txIngreso.EstadoFila != "NO FACTURABLE") { txIngreso.MontoNeto = 0m; txIngreso.EstadoFila = "NO FACTURABLE"; }
                            if (convertirAClp) { txIngreso.TipoCambio = esResponsableClienteDoc ? tasaVentaDoc : null; txIngreso.MontoOriginalUsd = esResponsableClienteDoc ? montoOriginalUsdVentaDoc : null; }
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

            #endregion

            #region 1. MARÍTIMO — cruce real contra TarifasMaritimas (Naviera + POL + POD + Equipo)
            bool isMaritimo = new[] { 1, 2, 3, 5, 8, 9, 10, 12 }.Contains(op.IdTipoServicio ?? 0);
            string naviera = op.IdNavieraNavigation?.NombreNaviera ?? "Por Confirmar";

            // Modalidad Collect / No Prepaid (instrucción de Cristian): el consignatario le
            // paga el flete directo a la naviera, la naviera nos paga un REBATE a nosotros
            // (INGRESO) y nosotros le devolvemos una parte al cliente (EGRESO). La diferencia
            // es nuestra comisión/profit. Mismo GrupoCobro "Marítimo" para heredar el mismo
            // tratamiento IVA-exento que ya tienen ControlFinanciero/AcuerdosOperativos, pero
            // con Concepto "Rebate Marítimo (...)" — NO usa GenerarEspejo porque esa función
            // siempre espeja Ingreso=Egreso del mismo monto, y aquí son dos montos
            // independientes (Rebate Naviera ≠ Rebate Cliente).
            bool esCollectMar = op.CondicionPago == 3 || op.CondicionPago == 4; // Collect / Full Collect

            async Task GenerarRebateMaritimo(string concepto, string tipoMovimiento, bool activo, decimal? valorOverride, string moneda, string responsable, string responsablePago)
            {
                var txsRebate = txExistentes.Where(t => t.GrupoCobro == "Marítimo" && t.Concepto == concepto && t.TipoMovimiento == tipoMovimiento).ToList();
                var txRebate = txsRebate.FirstOrDefault();
                if (txsRebate.Count > 1) _context.TransaccionesFinancieras.RemoveRange(txsRebate.Skip(1));

                if (activo)
                {
                    decimal monto = valorOverride ?? 0m;
                    string estado = monto > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR";
                    if (txRebate == null)
                    {
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera
                        {
                            IdOperacion = op.Id, GrupoCobro = "Marítimo", TipoMovimiento = tipoMovimiento, Concepto = concepto,
                            MontoNeto = monto, Moneda = moneda, EstadoFila = estado, FechaCreacion = ahora, UsuarioCreador = usuario,
                            IdCliente = op.IdCliente, IdProveedor = tipoMovimiento == "INGRESO" ? op.IdNaviera : null,
                            ResponsablePago = responsablePago, Responsable = responsable, IvaExento = true
                        });
                    }
                    else if (!txRebate.TarifaManual)
                    {
                        txRebate.MontoNeto = monto; txRebate.Moneda = moneda; txRebate.EstadoFila = estado; txRebate.IvaExento = true;
                    }
                }
                else if (txRebate != null && (txRebate.EstadoFila == "PROVISIÓN" || txRebate.EstadoFila == "PENDIENTE VALORIZAR"))
                {
                    _context.TransaccionesFinancieras.Remove(txRebate);
                }
            }

            decimal? valorFleteMarDb = null;
            decimal? valorRebateNavieraDb = null;
            // Tarifa de flete por equipo (oct-2026): el flete se cobra por contenedor y cada
            // contenedor puede tener su propio equipo (20'/40', Dry/Reefer).
            Func<string?, TarifasMaritima?> tarifaMarPorEquipo = _ => null;
            if (isMaritimo && op.IdNaviera > 0)
            {
                var polNombre = (op.IdPuertoOrigenNavigation?.NombrePuerto ?? "").ToUpper();
                var podNombre = (op.IdPuertoDestinoNavigation?.NombrePuerto ?? "").ToUpper();
                var (tamOp, tipoOp) = NormalizarEquipo(op.TipoContenedor);

                // La tarifa más reciente primero: si Comercial carga una nueva para la misma
                // ruta y equipo, esa es la que manda.
                var candidatasMar = (await _context.TarifasMaritimas.AsNoTracking()
                    .Where(t => t.EsActiva && t.IdNaviera == op.IdNaviera)
                    .ToListAsync())
                    .OrderByDescending(t => t.FechaInicioVigencia <= DateOnly.FromDateTime(ahora) ? 1 : 0)
                    .ThenByDescending(t => t.FechaInicioVigencia)
                    .ThenByDescending(t => t.Id)
                    .ToList();

                bool PolPodOk(TarifasMaritima t)
                {
                    var pol = (t.Pol ?? "").ToUpper();
                    var pod = (t.Pod ?? "").ToUpper();
                    // POL y POD tienen que estar en la operación y calzar con la tarifa.
                    bool polOk = polNombre != "" && pol != "" && (pol == polNombre || pol.Contains(polNombre) || polNombre.Contains(pol));
                    bool podOk = podNombre != "" && pod != "" && (pod == podNombre || pod.Contains(podNombre) || podNombre.Contains(pod));
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

                // (oct-2026) Ya no hay "3° intento" con cualquier equipo de la ruta: sin tarifa
                // para ese tamaño y familia (Dry/Reefer), el flete queda Por asignar.

                if (matchMar != null)
                {
                    valorFleteMarDb = matchMar.TarifaUsd;
                    valorRebateNavieraDb = matchMar.RebateUsd;
                }

                tarifaMarPorEquipo = equipo =>
                {
                    var (tamU, tipoU) = NormalizarEquipo(equipo);
                    if (tamU == "") return null;
                    return candidatasMar.Where(PolPodOk).FirstOrDefault(t => { var (tamT, tipoT) = NormalizarEquipo(t.Equipamiento); return tamT == tamU && tipoT == tipoU; })
                        ?? candidatasMar.Where(PolPodOk).FirstOrDefault(t => { var (tamT, tipoT) = NormalizarEquipo(t.Equipamiento); return tamT == tamU && (tipoU == "RF") == (tipoT == "RF"); });
                };
            }

            // Acuerdos COLLECT (oct-2026): tarifa de la naviera y del cliente para esta
            // naviera, destino y commodity (TarifasCollect). Dan el rebate de cada lado y
            // las condiciones sin costo (Late Arrival, Extra Late, Corrección de BL).
            TarifaCollect? acuerdoCollectNaviera = null;
            TarifaCollect? acuerdoCollectCliente = null;
            if (isMaritimo && esCollectMar && op.IdNaviera > 0)
            {
                List<TarifaCollect> tarifasCollectNav;
                try
                {
                    tarifasCollectNav = await _context.TarifasCollect.AsNoTracking()
                        .Where(t => t.EsActiva && t.IdNaviera == op.IdNaviera)
                        .ToListAsync();
                }
                catch (Exception exCollect)
                {
                    // Sin la tabla (script 2026-10_tarifas_collect.sql pendiente) sigue el modelo anterior.
                    System.Diagnostics.Trace.TraceWarning($"No se pudieron leer TarifasCollect (¿falta correr el script?): {exCollect.Message}");
                    tarifasCollectNav = new List<TarifaCollect>();
                }
                string? polCollect = op.IdPuertoOrigenNavigation?.NombrePuerto;
                string? podCollect = op.IdPuertoDestinoNavigation?.NombrePuerto;
                DateTime fechaCollect = op.EtdPol ?? ahora;
                acuerdoCollectNaviera = AcuerdoCollectHelper.Elegir(tarifasCollectNav, TarifaCollect.TipoNaviera, null, op.IdNaviera, polCollect, podCollect, op.Commodity, fechaCollect);
                acuerdoCollectCliente = AcuerdoCollectHelper.Elegir(tarifasCollectNav, TarifaCollect.TipoCliente, op.IdCliente, op.IdNaviera, polCollect, podCollect, op.Commodity, fechaCollect);
            }

            // Flete Marítimo (modelo Prepaid): se desactiva/retira si la operación es Collect.
            // Oct-2026: el flete se cobra POR CONTENEDOR, igual que Gate, ISPS, honorarios y el
            // rebate. Antes había una sola línea por booking y un booking de 3 contenedores
            // quedaba con el flete de uno solo. El principal mantiene su concepto de siempre
            // ("Flete Marítimo (Naviera)"); cada contenedor adicional lleva "(Uxx)".
            var contenedoresFlete = new List<(int? IdUnidad, string? Equipo, int? TipoCarga, string? Condicion)> { (null, op.TipoContenedor, op.IdTipoCarga, op.CondicionReefer) };
            contenedoresFlete.AddRange((op.Unidadestecnicas ?? new List<Unidadestecnica>()).OrderBy(u => u.Id)
                .Select(u => ((int?)u.Id, string.IsNullOrWhiteSpace(u.TipoContenedor) ? op.TipoContenedor : u.TipoContenedor, (int?)(u.IdTipoCarga ?? op.IdTipoCarga), string.IsNullOrWhiteSpace(u.CondicionReefer) ? op.CondicionReefer : u.CondicionReefer)));
            foreach (var cf in contenedoresFlete)
            {
                string sufijoFlete = cf.IdUnidad.HasValue ? $" (U{cf.IdUnidad})" : "";
                decimal? fleteCosto = cf.IdUnidad.HasValue ? tarifaMarPorEquipo(cf.Equipo)?.TarifaUsd : valorFleteMarDb;
                var ventaFlete = isMaritimo
                    ? VentaClienteHelper.ElegirMaritima(ventasCliente, op.IdNaviera, op.IdPuertoOrigenNavigation?.NombrePuerto, op.IdPuertoDestinoNavigation?.NombrePuerto, VentaClienteHelper.EquipoOperacion(cf.Equipo, cf.TipoCarga), cf.Condicion)
                    : null;
                string conceptoFlete = $"Flete Marítimo{sufijoFlete} ({naviera})";
                await GenerarEspejo("Marítimo", conceptoFlete, isMaritimo && !esCollectMar, $"Flete Marítimo{sufijoFlete}", fleteCosto, "USD", soloTarifaEspecifica: true,
                    valorVentaOverride: ventaFlete?.PrecioPactado, monedaVentaOverride: ventaFlete?.Moneda);

                // Handling de la venta (Comercial > Clientes > Marítimo): va en la línea de
                // ingreso del flete mientras siga en provisión y no se haya ajustado a mano.
                if (isMaritimo && !esCollectMar)
                {
                    foreach (var txH in _context.TransaccionesFinancieras.Local.Where(t => t.IdOperacion == op.Id && t.TipoMovimiento == "INGRESO"
                        && t.GrupoCobro == "Marítimo" && t.Concepto == conceptoFlete && !t.TarifaManual
                        && (t.EstadoFila == "PROVISIÓN" || t.EstadoFila == "PENDIENTE VALORIZAR")))
                        txH.ProfitHandling = ventaFlete?.ProfitHandling ?? 0m;
                }
            }
            // Fletes de contenedores que ya no existen (solo si siguen en provisión).
            {
                var idsUnidadesFlete = contenedoresFlete.Where(x => x.IdUnidad.HasValue).Select(x => x.IdUnidad!.Value).ToHashSet();
                foreach (var txF in txExistentes.Where(t => t.GrupoCobro == "Marítimo" && (t.Concepto ?? "").StartsWith("Flete Marítimo (U")).ToList())
                {
                    var mF = System.Text.RegularExpressions.Regex.Match(txF.Concepto ?? "", @"^Flete Marítimo \(U(\d+)\)");
                    if (mF.Success && !idsUnidadesFlete.Contains(int.Parse(mF.Groups[1].Value))
                        && (txF.EstadoFila == "PROVISIÓN" || txF.EstadoFila == "PENDIENTE VALORIZAR" || txF.EstadoFila == "NO FACTURABLE")
                        && _context.Entry(txF).State != EntityState.Deleted)
                        _context.TransaccionesFinancieras.Remove(txF);
                }
            }

            // Rebate Marítimo (modelo Collect / No Prepaid), ver nota más arriba.
            if (isMaritimo && op.IdCliente != null)
            {
                decimal? valorRebateClienteDb = null;
                if (esCollectMar)
                {
                    // AUDITORÍA oct-2026: antes esto tomaba "la tarifa Marítimo más reciente
                    // del cliente" sin cruzarla con la naviera/ruta real de esta operación —
                    // un cliente que embarca con más de una naviera (o ruta) puede tener varias
                    // filas TarifasClientes activas con GrupoCobro="Marítimo", cada una con su
                    // propio RebateUsd, y la más reciente no es necesariamente la que aplica a
                    // ESTE booking. Se corrige con el mismo criterio de cruce Naviera+POL+POD
                    // que ya usa candidatasMar/PolPodOk arriba (coincidencia exacta primero,
                    // mismo cruce "contains" en ambos sentidos para tolerar nombres parciales,
                    // y una cascada de respaldo si no hay match exacto).
                    string polNombreRebate = (op.IdPuertoOrigenNavigation?.NombrePuerto ?? "").ToUpper();
                    string podNombreRebate = (op.IdPuertoDestinoNavigation?.NombrePuerto ?? "").ToUpper();

                    bool PolPodOkCliente(TarifasCliente t)
                    {
                        var pol = (t.Pol ?? "").ToUpper();
                        var pod = (t.Pod ?? "").ToUpper();
                        bool polOk = string.IsNullOrEmpty(polNombreRebate) || pol.Contains(polNombreRebate) || polNombreRebate.Contains(pol);
                        bool podOk = string.IsNullOrEmpty(podNombreRebate) || pod.Contains(podNombreRebate) || podNombreRebate.Contains(pod);
                        return polOk && podOk;
                    }

                    var candidatasRebateCliente = await _context.TarifasClientes.AsNoTracking()
                        .Where(t => t.IdCliente == op.IdCliente && t.EsActiva && t.GrupoCobro == "Marítimo")
                        .OrderByDescending(t => t.Id)
                        .ToListAsync();

                    // 1° intento: misma naviera y misma ruta (POL/POD) que esta operación.
                    // 2° intento: misma naviera, sin exigir ruta exacta.
                    // 3° intento: cualquier tarifa Marítimo activa del cliente (mejor
                    // referencia disponible que ninguna) — mismo criterio de respaldo que
                    // el 3er intento de candidatasMar más arriba.
                    var tarifaClienteCollect =
                        candidatasRebateCliente.FirstOrDefault(t => t.IdNaviera == op.IdNaviera && PolPodOkCliente(t))
                        ?? candidatasRebateCliente.FirstOrDefault(t => t.IdNaviera == op.IdNaviera)
                        ?? candidatasRebateCliente.FirstOrDefault();

                    valorRebateClienteDb = tarifaClienteCollect?.RebateUsd;
                }

                // oct-2026: con tarifa Collect, el rebate de cada lado es Pública − Neta; sin
                // ella se usa el RebateUsd antiguo. El rebate es por contenedor: una línea por
                // cada uno ("(Uxx)" para los adicionales). Si el acuerdo del cliente no le
                // entrega rebate (0), no se genera su línea.
                decimal? rebateNaviera = acuerdoCollectNaviera != null ? acuerdoCollectNaviera.RebateUsd : valorRebateNavieraDb;
                decimal? rebateCliente = acuerdoCollectCliente != null ? acuerdoCollectCliente.RebateUsd : valorRebateClienteDb;
                bool generaRebateCliente = esCollectMar && !(acuerdoCollectCliente != null && acuerdoCollectCliente.RebateUsd <= 0m);
                var unidadesRebate = new List<int?> { null };
                unidadesRebate.AddRange((op.Unidadestecnicas ?? new List<Unidadestecnica>()).Select(u => (int?)u.Id));
                foreach (var idURebate in unidadesRebate)
                {
                    string sufijoRebate = idURebate.HasValue ? $" (U{idURebate})" : "";
                    await GenerarRebateMaritimo($"Rebate Marítimo (Naviera){sufijoRebate}", "INGRESO", esCollectMar, rebateNaviera, "USD", "NAVIERA", "NAVIERA");
                    await GenerarRebateMaritimo($"Rebate Marítimo (Cliente){sufijoRebate}", "EGRESO", generaRebateCliente, rebateCliente, "USD", "ATILSON", "ATILSON");
                }
                // Líneas de contenedores que ya no existen (solo en provisión).
                var idsUnidadesRebate = unidadesRebate.Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
                foreach (var txHuerfano in txExistentes.Where(t => t.GrupoCobro == "Marítimo" && (t.Concepto ?? "").StartsWith("Rebate Marítimo (")).ToList())
                {
                    var mU = System.Text.RegularExpressions.Regex.Match(txHuerfano.Concepto ?? "", @"\(U(\d+)\)");
                    if (mU.Success && !idsUnidadesRebate.Contains(int.Parse(mU.Groups[1].Value))
                        && (txHuerfano.EstadoFila == "PROVISIÓN" || txHuerfano.EstadoFila == "PENDIENTE VALORIZAR")
                        && _context.Entry(txHuerfano).State != EntityState.Deleted)
                        _context.TransaccionesFinancieras.Remove(txHuerfano);
                }
            }

            // 1b. TIPO DE EMISIÓN B/L — un tipo por booking (EmisionBlHelper):
            // Originales, Destino, Seawaybill, Telex Release, Express Release u Otro.
            // oct-2026: antes Detalle, Create/Comercial y Finanzas usaban códigos
            // distintos y la tarifa no calzaba. La tarifa se busca por naviera y tipo
            // (para "Otro", por el texto escrito); trae su moneda y si aplica IVA. En
            // USD se convierte a CLP guardando el USD original como referencia. Los
            // conceptos mantienen el texto histórico "Emisión BL - X".
            if (isMaritimo && op.IdNaviera > 0)
            {
                var tarifasBlNaviera = await _context.TarifasBl.AsNoTracking()
                    .Where(t => t.IdNaviera == op.IdNaviera && t.EsActiva)
                    .ToListAsync();

                var busquedasActivasBl = new List<string>();
                string? tipoBl = EmisionBlHelper.TipoActual(op.TipoEmisionBl);
                string nombreBl = tipoBl == null ? "" : EmisionBlHelper.NombreFinanzas(tipoBl, op.TipoEmisionBlOtro);
                if (tipoBl != null && nombreBl != "")
                {
                    var tarifaBl = EmisionBlHelper.BuscarTarifa(tarifasBlNaviera, tipoBl, op.TipoEmisionBlOtro);
                    decimal? valorBl = tarifaBl?.ValorNeto;
                    string monedaBl = string.IsNullOrWhiteSpace(tarifaBl?.Moneda) ? "USD" : tarifaBl!.Moneda;
                    bool? ivaExentoBl = false; // Emisión BL siempre + IVA (USD o CLP)

                    string busquedaBl = EmisionBlHelper.PrefijoConcepto + nombreBl;
                    // Originales muestra el Lugar de emisión en el concepto (informativo:
                    // la tarifa es la misma para las 4 sedes).
                    string sufijoLugar = tipoBl == EmisionBlHelper.Originales && !string.IsNullOrWhiteSpace(op.UbicacionOriginalesDestino)
                        ? $" ({op.UbicacionOriginalesDestino})"
                        : "";

                    await GenerarEspejo("Marítimo", busquedaBl + sufijoLugar, true, busquedaBl, valorBl, monedaBl, convertirAClp: monedaBl == "USD", ivaExento: ivaExentoBl);
                    busquedasActivasBl.Add(busquedaBl);
                }

                // El tipo anterior (si se cambió) se retira, con la misma
                // regla que GenerarEspejo usa al desactivar: solo filas aún no facturadas.
                foreach (var txBl in txExistentes.Where(t => t.GrupoCobro == "Marítimo" && t.Concepto.StartsWith(EmisionBlHelper.PrefijoConcepto)).ToList())
                {
                    if (busquedasActivasBl.Any(b => txBl.Concepto.Contains(b))) continue;
                    bool retirable = txBl.EstadoFila == "PROVISIÓN" || txBl.EstadoFila == "PENDIENTE VALORIZAR"
                        || (txBl.TipoMovimiento == "INGRESO" && txBl.EstadoFila == "NO FACTURABLE");
                    if (retirable && _context.Entry(txBl).State != EntityState.Deleted) _context.TransaccionesFinancieras.Remove(txBl);
                }
            }

            #endregion

            #region 2. TERRESTRE CON CRUCE LOGÍSTICO — un concepto (costo+venta) por cada
            // contenedor gestionado por separado. El Contenedor Principal mantiene
            // el mismo formato histórico (compatibilidad con bookings de un solo
            // contenedor); los adicionales solo generan su propio concepto "Flete
            // Terrestre (Uxx) (...)" cuando ya existe una fila OperacionesTerrestre
            // propia — si nadie gestionó ese contenedor por separado, no genera nada.
            bool isTerrestre = new[] { 1, 2, 4, 6, 8, 9, 11, 13 }.Contains(op.IdTipoServicio ?? 0);
            var terr = op.OperacionesTerrestres.FirstOrDefault(x => x.IdUnidadTecnica == null);

            // Devuelve la tarifa del tramo y el transportista de ESE contenedor (oct-2026: el
            // Id se guarda en la fila de egreso para que Control Financiero muestre el
            // transportista de cada contenedor y no el del principal en todos).
            async Task<(decimal? Valor, int? IdProveedor)> ObtenerTarifaTransportistaAsync(OperacionesTerrestre? terrUnidad, string retiroU, string plantaU, string entregaU)
            {
                // Cruce en Helpers/TarifaTransporteHelper (mismo que usa el diagnóstico de
                // Control Financiero "¿Por qué no hay tarifa?").
                var res = await TarifaTransporteHelper.BuscarAsync(_context, terrUnidad, op.IdTipoCarga, retiroU, plantaU, entregaU, ahora);
                return (res.Valor, res.IdProveedor);
            }

            // Contenedor Principal (formato idéntico al histórico)
            string retiro = terr?.DepositoRetiro ?? "Retiro";
            string planta = terr?.PlantaCarga ?? "Planta";
            string entrega = terr?.PuertoEntrega ?? "Destino";
            string ruta = isTerrestre ? $"{retiro} - {planta} - {entrega}" : "Terrestre";

            // Se guarda la tarifa completa del tramo: sus recargos (falso flete, porteo,
            // interplanta) los usan los extracostos de transporte más abajo.
            var resTranspPrincipal = isTerrestre ? await TarifaTransporteHelper.BuscarAsync(_context, terr, op.IdTipoCarga, retiro, planta, entrega, ahora) : null;
            decimal? tarifaTranspDb = resTranspPrincipal?.Valor;
            int? idTransportista = resTranspPrincipal?.IdProveedor;

            string equipoOpVenta = VentaClienteHelper.EquipoOperacion(op.TipoContenedor, op.IdTipoCarga);
            var ventaTerr = isTerrestre ? VentaClienteHelper.ElegirTerrestre(ventasCliente, retiro, planta, entrega, equipoOpVenta) : null;
            // Oct-2026: la venta terrestre se carga por ciudades (Valparaíso → Rancagua → San
            // Antonio) y la operación trae nombres de depósito y planta; sin cruce directo se
            // busca con la ciudad de cada punto (mismo criterio que el costo del transportista).
            if (isTerrestre && ventaTerr == null && ventasCliente.Any())
            {
                var ciu = await TarifaTransporteHelper.CiudadesTramoAsync(_context, terr, retiro, planta, entrega);
                ventaTerr = VentaClienteHelper.ElegirTerrestre(ventasCliente, ciu.Retiro, ciu.Planta, ciu.Puerto, equipoOpVenta);
            }
            await GenerarEspejo("Transporte", $"Flete Terrestre ({ruta})", isTerrestre, "Flete Terrestre", tarifaTranspDb, "CLP", idProveedorOverride: idTransportista, soloTarifaEspecifica: true,
                valorVentaOverride: ventaTerr?.PrecioPactado, monedaVentaOverride: ventaTerr?.Moneda);

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

                var (tarifaU, idTransportistaU) = await ObtenerTarifaTransportistaAsync(terrU, retiroU, plantaU, entregaU);

                var ventaTerrU = VentaClienteHelper.ElegirTerrestre(ventasCliente, retiroU, plantaU, entregaU, equipoOpVenta);
                if (ventaTerrU == null && ventasCliente.Any())
                {
                    var ciuU = await TarifaTransporteHelper.CiudadesTramoAsync(_context, terrU, retiroU, plantaU, entregaU);
                    ventaTerrU = VentaClienteHelper.ElegirTerrestre(ventasCliente, ciuU.Retiro, ciuU.Planta, ciuU.Puerto, equipoOpVenta);
                }
                await GenerarEspejo("Transporte", $"Flete Terrestre{sufijoU} ({rutaU})", true, $"Flete Terrestre{sufijoU}", tarifaU, "CLP", idProveedorOverride: idTransportistaU, soloTarifaEspecifica: true,
                    valorVentaOverride: ventaTerrU?.PrecioPactado, monedaVentaOverride: ventaTerrU?.Moneda);
            }

            // 2b. TARIFA PLANA PAQUETIZADA (acuerdos comerciales especiales, ej. Promar)
            var tarifaPlanaCliente = await _context.TarifasClientes.AsNoTracking()
                .Where(t => t.IdCliente == op.IdCliente && t.EsActiva && t.Concepto != null && t.Concepto.ToUpper().Contains("TARIFA PLANA PAQUETIZADA"))
                .OrderByDescending(t => t.Id)
                .FirstOrDefaultAsync();

            if (tarifaPlanaCliente != null)
            {
                var txPlana = txExistentes.FirstOrDefault(t => t.TipoMovimiento == "INGRESO" && t.Concepto == tarifaPlanaCliente.Concepto);
                if (txPlana == null)
                {
                    _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera
                    {
                        IdOperacion = op.Id,
                        GrupoCobro = tarifaPlanaCliente.GrupoCobro ?? "Documental",
                        TipoMovimiento = "INGRESO",
                        Concepto = tarifaPlanaCliente.Concepto,
                        MontoNeto = tarifaPlanaCliente.PrecioPactado,
                        Moneda = tarifaPlanaCliente.Moneda ?? "USD",
                        EstadoFila = "PROVISIÓN",
                        ResponsablePago = ResponsabilidadValores.Atilson,
                        Responsable = ResponsabilidadValores.Cliente,
                        FechaCreacion = ahora,
                        UsuarioCreador = usuario,
                        IdCliente = op.IdCliente
                    });
                }
                else if (!txPlana.TarifaManual)
                {
                    txPlana.MontoNeto = tarifaPlanaCliente.PrecioPactado;
                    txPlana.Moneda = tarifaPlanaCliente.Moneda ?? "USD";
                    txPlana.EstadoFila = "PROVISIÓN";
                }
            }

            #endregion

            #region 3. DOCUMENTAL
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

                bool esClienteEspecialOrigen = op.IdClienteNavigation?.EsClienteEspecial == true;
                string? acuerdoOrigenEfectivo = doc.AcuerdoOrigen;
                if (esClienteEspecialOrigen)
                {
                    string? acuerdoTlcAuto = DetectarAcuerdoTlcPorDestino(op.IdPuertoDestinoNavigation?.Pais, op.IdPuertoDestinoNavigation?.NombrePuerto);
                    if (acuerdoTlcAuto != null) acuerdoOrigenEfectivo = acuerdoTlcAuto;
                }
                decimal? valorAcuerdoOrigen = CalcularValorAcuerdoOrigen(acuerdoOrigenEfectivo, esClienteEspecialOrigen);
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
                                // Mismo preload en memoria que en GenerarEspejoDocumental (ver arriba).
                                var tarifaDoc = todasTarifasDocumentalesActivas
                                    .FirstOrDefault(t => t.IdAgenciaAduana == idAg.Value && t.Concepto.ToUpper().Contains(busquedaFito.ToUpper()));
                                if (tarifaDoc != null) { montoTarifa = tarifaDoc.ValorNeto; monedaTarifa = tarifaDoc.Moneda ?? "USD"; }
                            }

                            if (montoTarifa == 0m)
                            {
                                var tarifaMaestra = todasTarifasMaestrasDocumental
                                    .FirstOrDefault(t => t.Concepto.ToUpper().Contains(busquedaFito.ToUpper()));
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

                var conceptosLegacyAEliminar = new List<string> {
                    $"Emisión DUS{sufijoUnidad}", $"Emisión DIN{sufijoUnidad}", $"Legalización Documental{sufijoUnidad}",
                    $"CERTIFICADO ORIGEN{sufijoUnidad}", $"CERTIFICADO SANITARIO SERNAPESCA{sufijoUnidad}",
                    $"CERTIFICADO CAPTURA SERNAPESCA{sufijoUnidad}", $"FITO{sufijoUnidad}",
                    $"Certificado Sanitario Sernapesca{sufijoUnidad}", $"Certificado Fitosanitario SAG{sufijoUnidad}",
                    $"Certificado de Origen FNG{sufijoUnidad}", $"Trámite COA Aduana{sufijoUnidad}"
                };
                // oct-2026: la Legalización/Emisión DUS vuelve a ser por contenedor (la AGA la
                // cobra 1 a 1), así que sus líneas "(Uxx)" ya no se limpian aquí — ver bloque "3a".

                var txsLegacy = txExistentes.Where(t => conceptosLegacyAEliminar.Contains(t.Concepto) && (t.EstadoFila == "PROVISIÓN" || t.EstadoFila == "PENDIENTE VALORIZAR")).ToList();
                foreach (var txLegacy in txsLegacy) _context.TransaccionesFinancieras.Remove(txLegacy);

                // Honorarios de Agencia y Legalización/Emisión DUS se movieron fuera de este
                // loop — ver bloques "3a" y "3b" más abajo — porque ambos se cobran una sola
                // vez por booking, sin importar si los certificados de este booking están en
                // modalidad Grupal o Individual por contenedor.

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

                    // Tarifa de la empresa de courier asignada en Operaciones (DHL/FedEx/
                    // Chilexpress); si Comercial no cargó una para esa empresa, la general.
                    var tarifasCourierConcepto = await _context.TarifasMaestras.AsNoTracking()
                        .Where(t => t.EsActiva && t.Categoria == "Courier" && t.Concepto.ToUpper() == conceptoCourier.ToUpper())
                        .ToListAsync();
                    var tarifaCourierVigente = CourierHelper.ElegirTarifaCosto(tarifasCourierConcepto, conceptoCourier, doc.EmpresaCourier);

                    // Antes la moneda iba fija en "USD" aunque la tarifa estuviera en CLP.
                    string monedaCourier = string.IsNullOrWhiteSpace(tarifaCourierVigente?.Moneda) ? "USD" : tarifaCourierVigente!.Moneda;
                    var ventaCourier = VentaClienteHelper.ElegirCourier(ventasCliente, conceptoCourier, doc.EmpresaCourier);
                    await GenerarEspejoDocumental($"Servicio Courier{sufijoUnidad}", true, conceptoCourier, null, tarifaCourierVigente?.ValorNeto, null, monedaCourier, convertirAClp: true,
                        valorVentaOverride: ventaCourier?.PrecioPactado, monedaVentaOverride: ventaCourier?.Moneda);
                }
                await GenerarEspejoDocumental($"Aclaración Documental{sufijoUnidad}", doc.ExtensionDocumental == true, "Aclaración", idAg, doc.ValorAclaracion);
                await GenerarEspejoDocumental($"Cancelación Documental{sufijoUnidad}", doc.GuiaVisado == true, "Cancelación", idAg, doc.ValorCancelacion);
                await GenerarEspejoDocumental($"Roleo de Carga{sufijoUnidad}", doc.Roleo == true, "Roleo de Carga", idAg, doc.ValorRoleo);
                await GenerarEspejoDocumental($"Generación IVV{sufijoUnidad}", doc.GeneracionIvv == true, "Generación IVV", idAg);
            }

            // Cobro al cliente de Honorarios y DUS (oct-2026): por contenedor (por defecto,
            // igual que los cobra la AGA) o un solo cobro grupal por booking.
            bool cobroAgenciaGrupal = string.Equals(op.ModalidadCobroAgencia, "GRUPAL", StringComparison.OrdinalIgnoreCase);

            // 3a. LEGALIZACIÓN / EMISIÓN DUS — por contenedor desde oct-2026 (antes una sola
            // vez por booking).
            // Se activa a todo evento si el servicio documental está activo, con o sin número
            // de DUS/DIN ya cargado. Toma los datos del registro documental principal
            // (IdUnidadTecnica == null); si no existe, usa el primero disponible.
            if (reqDocumentalServicio)
            {
                var docParaDus = op.OperacionesDocumentales.FirstOrDefault(d => d.IdUnidadTecnica == null)
                               ?? op.OperacionesDocumentales.FirstOrDefault();

                if (docParaDus != null)
                {
                    bool tieneDus = !string.IsNullOrEmpty(docParaDus.DusDin);
                    bool tieneDin = !string.IsNullOrEmpty(docParaDus.Din);
                    decimal? valorLegalizacionDus = tieneDus ? docParaDus.ValorDus : (tieneDin ? docParaDus.ValorDin : null);
                    string busquedaLegalizacionDus = (tieneDin && !tieneDus) ? "DIN" : "DUS EMBARQUES NORMALES";

                    // oct-2026: la AGA cobra la Legalización/Emisión DUS por cada contenedor
                    // (1 a 1). Al cliente se le cobra igual, o en un solo cobro grupal si el
                    // booking tiene ModalidadCobroAgencia = "GRUPAL" (solo la línea principal
                    // lleva Ingreso).
                    var contenedoresDus = new List<int?> { null };
                    contenedoresDus.AddRange((op.Unidadestecnicas ?? new List<Unidadestecnica>()).Select(u => (int?)u.Id));
                    var lineasDus = contenedoresDus.Select(idUnidadDus =>
                    {
                        var docUnidadDus = op.OperacionesDocumentales.FirstOrDefault(d => d.IdUnidadTecnica == idUnidadDus) ?? docParaDus;
                        decimal? valorDusUnidad = (tieneDin && !tieneDus ? docUnidadDus.ValorDin : docUnidadDus.ValorDus) ?? valorLegalizacionDus;
                        // Venta al cliente (Comercial > Documental, SAG o SERNAPESCA): DUS sin IVA.
                        var ventaDusUnidad = VentaClienteHelper.ElegirDocumental(ventasCliente, esDus: true, VentaClienteHelper.ClasificacionDocumental(docUnidadDus));
                        return (IdUnidad: idUnidadDus, IdAg: docUnidadDus.IdAgenciaAduana ?? docParaDus.IdAgenciaAduana, Valor: valorDusUnidad, Venta: ventaDusUnidad);
                    }).ToList();
                    // Cobro grupal: la línea del 1° contenedor cobra la SUMA de todos.
                    bool sumaDusConocida = lineasDus.All(l => l.Valor.HasValue);
                    bool ventasDusCompletas = lineasDus.All(l => l.Venta != null) && lineasDus.Select(l => (l.Venta!.Moneda ?? "CLP").ToUpperInvariant()).Distinct().Count() == 1;
                    foreach (var l in lineasDus)
                    {
                        string sufijoDus = l.IdUnidad.HasValue ? $" (U{l.IdUnidad})" : "";
                        bool esLineaGrupal = cobroAgenciaGrupal && !l.IdUnidad.HasValue && lineasDus.Count > 1;
                        decimal? ventaDus = esLineaGrupal
                            ? (ventasDusCompletas ? lineasDus.Sum(x => x.Venta!.PrecioPactado) : (sumaDusConocida ? lineasDus.Sum(x => x.Valor!.Value) : null))
                            : l.Venta?.PrecioPactado;
                        string? monedaVentaDus = (esLineaGrupal && !ventasDusCompletas) ? null : l.Venta?.Moneda;
                        await GenerarEspejoDocumental($"Legalización / Emisión DUS{sufijoDus}", true, busquedaLegalizacionDus, l.IdAg, l.Valor,
                            valorVentaOverride: ventaDus,
                            monedaVentaOverride: monedaVentaDus,
                            generarIngreso: !cobroAgenciaGrupal || !l.IdUnidad.HasValue,
                            factorIngreso: esLineaGrupal && !sumaDusConocida && !ventasDusCompletas ? lineasDus.Count : 1m,
                            forzarIngreso: true);
                    }

                    // El DUS se cobra al cliente sin IVA (oct-2026).
                    foreach (var txDusIng in _context.TransaccionesFinancieras.Local
                        .Where(t => t.IdOperacion == op.Id && t.TipoMovimiento == "INGRESO" && (t.Concepto ?? "").StartsWith("Legalización / Emisión DUS")))
                        txDusIng.IvaExento = true;
                }
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

                var lineasHon = new List<(int? IdUnidad, int? IdAg, decimal? Valor, TarifasCliente? Venta)>();
                foreach (var idUnidadHon in contenedoresHon)
                {
                    var docUnidadHon = op.OperacionesDocumentales.FirstOrDefault(d => d.IdUnidadTecnica == idUnidadHon) ?? docPrincipalHon;
                    if (docUnidadHon == null) continue;

                    decimal? honorariosAgencia = null;
                    if (docUnidadHon.AplicaSernapesca == true) honorariosAgencia = 111500m;
                    else if (docUnidadHon.AplicaSag == true) honorariosAgencia = 78000m;
                    // Venta al cliente (Comercial > Documental, SAG o SERNAPESCA): Honorarios + IVA.
                    var ventaHonUnidad = VentaClienteHelper.ElegirDocumental(ventasCliente, esDus: false, VentaClienteHelper.ClasificacionDocumental(docUnidadHon));
                    lineasHon.Add((idUnidadHon, docUnidadHon.IdAgenciaAduana, honorariosAgencia, ventaHonUnidad));
                }

                // Cobro grupal (oct-2026): la línea del 1° contenedor cobra la SUMA de todos
                // los contenedores; las demás quedan solo con su costo.
                bool sumaHonConocida = lineasHon.All(l => l.Valor.HasValue);
                bool ventasHonCompletas = lineasHon.Count > 0 && lineasHon.All(l => l.Venta != null) && lineasHon.Select(l => (l.Venta!.Moneda ?? "CLP").ToUpperInvariant()).Distinct().Count() == 1;
                foreach (var l in lineasHon)
                {
                    string sufijoHon = l.IdUnidad.HasValue ? $" (U{l.IdUnidad})" : "";
                    bool esLineaGrupal = cobroAgenciaGrupal && !l.IdUnidad.HasValue && lineasHon.Count > 1;
                    decimal? ventaHon = esLineaGrupal
                        ? (ventasHonCompletas ? lineasHon.Sum(x => x.Venta!.PrecioPactado) : (sumaHonConocida ? lineasHon.Sum(x => x.Valor!.Value) : null))
                        : l.Venta?.PrecioPactado;
                    string? monedaVentaHon = (esLineaGrupal && !ventasHonCompletas) ? null : l.Venta?.Moneda;
                    await GenerarEspejoDocumental($"Honorarios Agencia de Aduanas{sufijoHon}", l.IdAg.HasValue, "HONORARIOS AGENCIA", l.IdAg, l.Valor,
                        valorVentaOverride: ventaHon,
                        monedaVentaOverride: monedaVentaHon,
                        generarIngreso: !cobroAgenciaGrupal || !l.IdUnidad.HasValue,
                        factorIngreso: esLineaGrupal && !sumaHonConocida && !ventasHonCompletas ? lineasHon.Count : 1m,
                        forzarIngreso: true);
                }
            }

            // 3c. DESCUENTO MULTICONTENEDOR — ACUERDOS COMERCIALES ESPECIALES (ej. Promar)
            // En modalidad Individual, cada contenedor genera su propio Certificado de
            // Origen a precio completo. El acuerdo especial del cliente dice que solo el
            // primero se cobra entero — el resto (N-1) se descuenta en una sola línea,
            // porque el trabajo real de la agencia no escala linealmente por contenedor.
            // Se usa .Local (no txExistentes) para ver también las líneas recién creadas
            // en esta misma pasada de sincronización, no solo las que ya estaban en BD.
            if (op.IdClienteNavigation?.EsClienteEspecial == true)
            {
                var lineasOrigenPorContenedor = _context.TransaccionesFinancieras.Local
                    .Where(t => t.IdOperacion == op.Id
                             && t.TipoMovimiento == "INGRESO"
                             && t.GrupoCobro == "Documental"
                             && t.Concepto != null
                             && t.Concepto.StartsWith("Certificado de Origen")
                             && !t.Concepto.Contains(" R1") && !t.Concepto.Contains(" R2") && !t.Concepto.Contains(" R3")
                             && t.MontoNeto > 0)
                    .OrderByDescending(t => t.MontoNeto)
                    .ToList();

                const string conceptoDescuentoOrigen = "Descuento Multicontenedor — Certificado de Origen";
                var txDescuentoOrigen = txExistentes.FirstOrDefault(t => t.Concepto == conceptoDescuentoOrigen);

                if (lineasOrigenPorContenedor.Count > 1)
                {
                    decimal totalADescontar = -lineasOrigenPorContenedor.Skip(1).Sum(t => t.MontoNeto);
                    string monedaDescuento = lineasOrigenPorContenedor.First().Moneda ?? "CLP";

                    if (txDescuentoOrigen == null)
                    {
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera
                        {
                            IdOperacion = op.Id,
                            GrupoCobro = "Documental",
                            TipoMovimiento = "INGRESO",
                            Concepto = conceptoDescuentoOrigen,
                            MontoNeto = totalADescontar,
                            Moneda = monedaDescuento,
                            EstadoFila = "PROVISIÓN",
                            ResponsablePago = ResponsabilidadValores.Atilson,
                            Responsable = ResponsabilidadValores.Cliente,
                            FechaCreacion = ahora,
                            UsuarioCreador = usuario,
                            IdCliente = op.IdCliente
                        });
                    }
                    else if (!txDescuentoOrigen.TarifaManual)
                    {
                        txDescuentoOrigen.MontoNeto = totalADescontar;
                        txDescuentoOrigen.Moneda = monedaDescuento;
                        txDescuentoOrigen.EstadoFila = "PROVISIÓN";
                    }
                }
                else if (txDescuentoOrigen != null && (txDescuentoOrigen.EstadoFila == "PROVISIÓN" || txDescuentoOrigen.EstadoFila == "PENDIENTE VALORIZAR"))
                {
                    _context.TransaccionesFinancieras.Remove(txDescuentoOrigen);
                }
            }

            #endregion

            #region 4. ISPS - SEGURIDAD DE LA CARGA
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
                var tarifaIsps = await _context.TarifasMaestras.AsNoTracking().FirstOrDefaultAsync(t => t.EsActiva && t.Concepto.ToUpper() == conceptoIspsMaestro);
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

            #endregion

            #region GATE (IN/OUT) -> CATEGORÍA "DEPÓSITO"
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
                bool gateOutIncluido = op.GestionaGate == "Gate out incluido para cliente" || op.GestionaGate == "Atilson entrega concesión incluido para cliente";

                string depRetiro = terr?.DepositoRetiro ?? "";
                int? idDepositoFisico = null;
                if (!string.IsNullOrEmpty(depRetiro))
                {
                    var deposito = await _context.Depositos.AsNoTracking().FirstOrDefaultAsync(d => d.NombreDeposito == depRetiro);
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
                // Oct-2026: si hay más de una tarifa para el mismo depósito y equipo, manda la
                // vigente y más reciente (antes ganaba la más antigua y una tarifa nueva cargada
                // en Comercial no se aplicaba).
                List<TarifaGate> PorVigencia(IEnumerable<TarifaGate> lista) => lista
                    .OrderByDescending(t => (t.FechaInicioVigencia == null || t.FechaInicioVigencia <= ahora) && (t.FechaFinVigencia == null || t.FechaFinVigencia >= ahora.Date) ? 1 : 0)
                    .ThenByDescending(t => t.FechaInicioVigencia ?? DateTime.MinValue)
                    .ThenByDescending(t => t.Id)
                    .ToList();

                (decimal Valor, string Moneda)? MatchTarifa(List<TarifaGate> tarifasEntrada)
                {
                    var tarifas = PorVigencia(tarifasEntrada);
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
                    // Componentes del mismo tipo (DRY/RF). Si hay filas por tamaño exacto
                    // (20/40) se usan esas; si no, las genéricas del tipo.
                    var filasNaviera = PorVigencia(await _context.Set<TarifaGate>().AsNoTracking()
                        .Where(t => t.IdNaviera == idNaviera && t.TipoMovimiento == tipoMovimiento && t.EsActiva && t.IdDeposito == null)
                        .ToListAsync());
                    var compsTipo = filasNaviera.Where(t => t.Componente != null && VentaClienteHelper.FamiliaEquipo(t.TipoContenedor) == tipoBusq).ToList();
                    var compsExactos = sizeBusq != "" ? compsTipo.Where(t => VentaClienteHelper.TamanoEquipo(t.TipoContenedor) == sizeBusq).ToList() : new List<TarifaGate>();
                    var compsGenericos = compsTipo.Where(t => VentaClienteHelper.TamanoEquipo(t.TipoContenedor) == "").ToList();
                    var componentes = compsExactos.Any() ? compsExactos
                        : compsGenericos.Any() ? compsGenericos
                        : compsTipo.GroupBy(t => VentaClienteHelper.TamanoEquipo(t.TipoContenedor)).Select(g => g.ToList()).FirstOrDefault() ?? new List<TarifaGate>();

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

                    // Modo una fila total por naviera (ej. Hapag: Dry 142.500 / Reefer 151.300).
                    // Antes tomaba la primera fila sin mirar Dry/Reefer; ahora prioriza el
                    // tipo y tamaño del contenedor, y deja la fila sin tipo como respaldo.
                    var totales = filasNaviera.Where(t => t.Componente == null).ToList();
                    var legado = totales.FirstOrDefault(t => VentaClienteHelper.FamiliaEquipo(t.TipoContenedor) == tipoBusq && sizeBusq != "" && VentaClienteHelper.TamanoEquipo(t.TipoContenedor) == sizeBusq)
                              ?? totales.FirstOrDefault(t => VentaClienteHelper.FamiliaEquipo(t.TipoContenedor) == tipoBusq && VentaClienteHelper.TamanoEquipo(t.TipoContenedor) == "")
                              ?? totales.FirstOrDefault(t => VentaClienteHelper.FamiliaEquipo(t.TipoContenedor) == tipoBusq)
                              ?? totales.FirstOrDefault(t => VentaClienteHelper.FamiliaEquipo(t.TipoContenedor) == "");
                    if (legado == null || !legado.ValorNeto.HasValue) return null;
                    return (legado.ValorNeto.Value, string.IsNullOrWhiteSpace(legado.Moneda) ? "CLP" : legado.Moneda);
                }

                // Tarifa por depósito (no-plana): es la misma tarifa física para cualquier
                // contenedor del booking (mismo depósito, misma naviera, mismo tipo de
                // equipo), así que se calcula una sola vez aquí y se reutiliza en el loop.
                if (aplicaGateOut && !aplicaTarifaPlana && idDepositoFisico != null)
                {
                    var tOutTodos = await _context.Set<TarifaGate>().AsNoTracking().Where(t => t.IdDeposito == idDepositoFisico && t.TipoMovimiento == "OUT" && t.EsActiva).ToListAsync();
                    var tOutNaviera = tOutTodos.Where(t => t.IdNaviera == op.IdNaviera).ToList();
                    var candidatasOut = tOutNaviera.Any() ? tOutNaviera : tOutTodos.Where(t => t.IdNaviera == null).ToList();
                    var resultOut = MatchTarifa(candidatasOut);
                    if (resultOut.HasValue) { valorGateOutBase = resultOut.Value.Valor; monedaGateOutBase = resultOut.Value.Moneda; }
                }

                if (aplicaGateIn && !aplicaTarifaPlana && idDepositoFisico != null)
                {
                    var tInTodos = await _context.Set<TarifaGate>().AsNoTracking().Where(t => t.IdDeposito == idDepositoFisico && t.TipoMovimiento == "IN" && t.EsActiva).ToListAsync();
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
                    var provDepo = await _context.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.NombreProveedor.Contains(depRetiro));
                    idProveedorGate = provDepo?.Id;
                }

                // Venta Gate al cliente (Comercial > Clientes > Gate Out/In).
                string equipoGateVenta = VentaClienteHelper.EquipoOperacion(op.TipoContenedor, op.IdTipoCarga);
                var ventaGateOut = aplicaGateOut ? VentaClienteHelper.ElegirGate(ventasCliente, "OUT", depRetiro, op.IdNaviera, equipoGateVenta) : null;
                var ventaGateIn = aplicaGateIn ? VentaClienteHelper.ElegirGate(ventasCliente, "IN", depRetiro, op.IdNaviera, equipoGateVenta) : null;

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

                    // "Gate out incluido para cliente" (oct-2026): el Gate OUT no se le cobra al
                    // cliente (lo asume Atilson). Antes se comparaba con un texto que el
                    // formulario ya no usa y el cliente quedaba cobrado igual.
                    foreach (var txG in txExistentes.Where(t => t.GrupoCobro == "Depósito" && !t.TarifaManual
                        && (t.Concepto == conceptoGateOutU || t.Concepto == conceptoGateInU)))
                    {
                        bool esOut = txG.Concepto == conceptoGateOutU;
                        string? resp = string.IsNullOrWhiteSpace(op.GestionaGate) ? null
                            : (esOut && gateOutIncluido ? ResponsabilidadValores.Atilson : ResponsabilidadValores.Cliente);
                        if (resp != null) txG.Responsable = resp;
                    }

                    // Tarifa en $0 = el depósito/naviera no cobra: "sin costo", no "Por asignar".
                    // Si tampoco hay venta al cliente, el ingreso queda en $0 sin cobro.
                    bool outSinCosto = aplicaGateOut && valorGateOut == 0m;
                    bool inSinCosto = aplicaGateIn && valorGateIn == 0m;
                    await GenerarEspejo("Depósito", conceptoGateOutU, aplicaGateOut, conceptoGateOutU, valorGateOut, monedaGateOut, idProveedorGate, convertirAClp: true,
                        valorVentaOverride: ventaGateOut?.PrecioPactado, monedaVentaOverride: ventaGateOut?.Moneda,
                        responsableOverride: gateOutIncluido ? ResponsabilidadValores.Atilson : null,
                        costoSinCargo: outSinCosto, ventaSinCargo: outSinCosto && ventaGateOut == null, notaSinCargo: outSinCosto ? AcuerdoCollectHelper.MarcaSinCostoTarifa : null);
                    await GenerarEspejo("Depósito", conceptoGateInU, aplicaGateIn, conceptoGateInU, valorGateIn, monedaGateIn, idProveedorGate, convertirAClp: true,
                        valorVentaOverride: ventaGateIn?.PrecioPactado, monedaVentaOverride: ventaGateIn?.Moneda,
                        costoSinCargo: inSinCosto, ventaSinCargo: inSinCosto && ventaGateIn == null, notaSinCargo: inSinCosto ? AcuerdoCollectHelper.MarcaSinCostoTarifa : null);
                }
            }

            #endregion

            #region 5. EXTRACOSTOS: CÁLCULOS MATEMÁTICOS DE FALSOS FLETES EN RUTA + extracostos
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
                    // DECISIÓN DE NEGOCIO/LÓGICA PENDIENTE (auditoría oct-2026): por precedencia de
                    // operadores en C#, "&&" liga más fuerte que "||", así que esta línea se
                    // evalúa como "GATE OUT" || ("GATE IN" && "MSC") — es decir: un extracosto
                    // "GATE OUT" siempre clasifica como Marítimo (cualquier naviera), pero un
                    // extracosto "GATE IN" solo clasifica como Marítimo cuando ADEMÁS es MSC; para
                    // cualquier otra naviera, "GATE IN" no entra aquí y sigue probando las
                    // condiciones de Transporte/Documental de abajo, cayendo en "Otros" si ninguna
                    // aplica. No se puede saber desde el código si este trato distinto para
                    // GATE IN-no-MSC es intencional (una regla real de negocio) o un paréntesis
                    // que faltó al escribir la condición. No se modificó para no arriesgar
                    // reclasificar extracostos reales sin confirmación — si la intención real era
                    // "(GATE OUT || GATE IN) && MSC" o "GATE OUT || GATE IN" sin más, hay que
                    // corregirlo explícitamente con quien conoce la regla de negocio.
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

                // Recargos de transporte (oct-2026): si el tramo del transportista los tiene
                // cargados en Comercial (Falso flete planta / ruta / depósito, Porteo,
                // Interplanta), manda ese valor. Antes el falso flete siempre se calculaba con
                // el 90/80/70 % del flete aunque Comercial hubiera cargado otro monto.
                var tt = resTranspPrincipal?.Tarifa;
                decimal? Recargo(decimal? valorTarifa) => valorTarifa is > 0 ? valorTarifa : null;
                if (tarifaTranspDb.HasValue)
                {
                    if (ext.TipoCosto == "Falso Flete en Ruta")
                    {
                        if ((ext.Motivo ?? "").Contains("Mas de la mitad")) montoOverride = Recargo(tt?.FalsoFleteRutaMayor50) ?? tarifaTranspDb * 0.80m;
                        else if ((ext.Motivo ?? "").Contains("Menos de la mitad")) montoOverride = Recargo(tt?.FalsoFleteRutaMenor50) ?? tarifaTranspDb * 0.70m;
                        monedaOverride = "CLP";
                    }
                    else if (ext.TipoCosto == "Falso Flete Planta")
                    {
                        montoOverride = Recargo(tt?.FalsoFletePlanta) ?? tarifaTranspDb * 0.90m;
                        monedaOverride = "CLP";
                    }
                }
                if (tt != null)
                {
                    decimal? recargoFijo = ext.TipoCosto switch
                    {
                        "Falso Flete Depósito" => Recargo(tt.FalsoFleteDeposito),
                        "Porteo" => Recargo(tt.ServicioPorteoPuerto),
                        "Interplanta" => Recargo(tt.Interplanta),
                        _ => null
                    };
                    if (recargoFijo.HasValue) { montoOverride = recargoFijo; monedaOverride = "CLP"; }
                }

                // Roleo / Cancelación / No Show: monto y moneda tal como se registraron
                // (la penalidad de la naviera, en USD). Ver "mantenerUsd" más abajo.
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

                // Acuerdo Collect (oct-2026): Late Arrival, Extra Late y Corrección de BL dentro
                // del plazo pueden ir sin costo según la tarifa Collect de la naviera (costo) y
                // la del cliente (cobro). Si el cliente no tiene tarifa Collect y la naviera no
                // cobra, tampoco se le cobra al cliente.
                bool costoSinCargoExt = false, ventaSinCargoExt = false;
                string? notaSinCargoExt = null;
                if (esCollectMar && (acuerdoCollectNaviera != null || acuerdoCollectCliente != null))
                {
                    DateTime? etdAcuerdo = op.EtdReal ?? op.EtdPol;
                    bool AplicaSinCargo(TarifaCollect? a) => a != null && (
                        (AcuerdoCollectHelper.EsExtraLate(ext.TipoCosto) && a.ExtraLateSinCosto)
                        || (AcuerdoCollectHelper.EsLateArrival(ext.TipoCosto) && a.LateArrivalSinCosto)
                        || (CorreccionBlHelper.EsCorreccion(ext) && AcuerdoCollectHelper.CorreccionSinCosto(a, ext.FechaCreacion, etdAcuerdo)));
                    costoSinCargoExt = AplicaSinCargo(acuerdoCollectNaviera);
                    ventaSinCargoExt = acuerdoCollectCliente != null ? AplicaSinCargo(acuerdoCollectCliente) : costoSinCargoExt;
                    if (costoSinCargoExt || ventaSinCargoExt)
                        notaSinCargoExt = $"{AcuerdoCollectHelper.MarcaSinCosto} ({op.Commodity ?? "sin commodity"})";
                }

                string conceptoUnico = $"{ext.TipoCosto} (EXT-{ext.Id})";
                // oct-2026: antes "Marítimo" quedaba excluido a propósito de la conversión a
                // CLP (por eso Late Arrival/Roleo/Cancelación/Corrección BL etc. en USD se
                // mostraban tal cual en AcuerdosOperativos/ControlFinanciero, sin el "(USD x)"
                // de referencia que sí tiene ISPS/Gate). Confirmado con Dnsaile: ya no
                // corresponde — todo extracosto marítimo reportado libremente en USD debe
                // convertirse a CLP igual que el resto, mostrando el monto original en USD
                // como referencia (GenerarEspejo ya hace esa conversión vía convertirAClp;
                // esto solo empareja el flag con lo que ya hacía "Emisión BL" más arriba).
                // No afecta al Flete Marítimo base ni a "conceptoOtro" (líneas ~551/649):
                // esos siguen con su propio convertirAClp explícito, sin cambios.
                //
                // oct-2026 (minuta BL/Roleos): EXCEPCIÓN — las penalidades de Roleo,
                // Cancelación y No Show se quedan en su USD original, igual que el Flete
                // Marítimo (Finanzas las liquida en dólares). El resto (Late Arrival,
                // Corrección BL, etc.) se sigue convirtiendo a CLP como hasta ahora.
                bool mantenerUsd = EsPenalidadNavieraEnUsd(ext.TipoCosto);
                bool convertirAClpExt = monedaOverride == "USD" && !mantenerUsd;
                await GenerarEspejo(grupoExtracosto, conceptoUnico, true, conceptoUnico, montoOverride, monedaOverride, idProveedorOverride: ext.IdProveedor, convertirAClp: convertirAClpExt, responsablePagoOverride: NormalizarResponsabilidad(ext.ResponsableGestion), responsableOverride: NormalizarResponsabilidad(ext.Responsable),
                    costoSinCargo: costoSinCargoExt, ventaSinCargo: ventaSinCargoExt, notaSinCargo: notaSinCargoExt);
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
            #endregion

            await _context.SaveChangesAsync();
        }
    }
}