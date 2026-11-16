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

        // Solo las provisiones automáticas sin documentos pueden ajustarse o darse de baja.
        // Un movimiento facturado, pagado, conciliado, manual o documentado
        // nunca se modifica ni elimina automáticamente durante una sincronización.
        private static bool ProvisionalSinRespaldo(TransaccionesFinanciera t) =>
            !t.TarifaManual &&
            !t.FechaEmision.HasValue &&
            !t.ConfirmadoAga &&
            !t.ValorAga.HasValue &&
            !t.AceptaDiferenciaPago &&
            string.IsNullOrWhiteSpace(t.NotaDiferenciaAga) &&
            t.EstadoFila is ("PROVISIÓN" or "PENDIENTE VALORIZAR" or "NO FACTURABLE") &&
            string.IsNullOrWhiteSpace(t.NumeroDocumento) &&
            string.IsNullOrWhiteSpace(t.NumeroDocumentoOrigen) &&
            string.IsNullOrWhiteSpace(t.NumeroOrdenCompra) &&
            string.IsNullOrWhiteSpace(t.RutaFactura) &&
            string.IsNullOrWhiteSpace(t.RutaComprobante);

        // La tarifa de certificado se debe buscar por trámite, no por la redacción
        // literal del catálogo. Comercial puede guardar "CERTIFICADO CAPTURA SERNAPESCA"
        // o "CERTIFICADO DE CAPTURA", y ambos representan el mismo trámite.
        // Las reemisiones jamás deben valorizar un certificado principal (ni al revés).
        private static bool CoincideConceptoTarifaDocumental(string? conceptoTarifa, string clave)
        {
            string t = VentaClienteHelper.Norm(conceptoTarifa);
            string c = VentaClienteHelper.Norm(clave);
            if (t.Length == 0 || c.Length == 0) return false;
            bool reemision = t.Contains("RE-EMISION") || t.Contains("REEMISION")
                || t.Contains("RE EMISION");
            if (c == "FITO")
                return !reemision && t.Contains("FITO");
            if (c == "CERTIFICADO SANITARIO")
                return !reemision && t.Contains("CERTIFICADO") && t.Contains("SANITARIO")
                    && !t.Contains("FITOSANITARIO");
            if (c == "CERTIFICADO CAPTURA SERNAPESCA")
                return !reemision && t.Contains("CAPTURA") &&
                    (t.Contains("CERTIFICADO") || t.StartsWith("CAPTURA")) && !t.Contains("AGA");
            if (c == "CERTIFICADO ORIGEN")
                return !reemision && t.Contains("CERTIFICADO") && t.Contains("ORIGEN");
            if (c == "RE-EMISION CERTIFICADOS ORIGEN")
                return reemision && t.Contains("ORIGEN");
            if (c == "RE-EMISION CERTIFICADOS SAG")
                return reemision && !t.Contains("SERNAPESCA") && !t.Contains("ORIGEN")
                    && (t.Contains("SAG") || t.Contains("FITO") || t == "RE-EMISION"
                        || t == "REEMISION" || t == "RE-EMISION CERTIFICADOS");
            if (c == "RE-EMISION")
                return reemision && !t.Contains("SAG") && !t.Contains("FITO")
                    && !t.Contains("ORIGEN");
            return t.Contains(c);
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
        // especial (Marfrio) — nunca deben resultar en $0 para un cliente estándar,
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
        // Marfrio, Regla #1. Exclusiva de clientes especiales (EsClienteEspecial), igual
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
            if (t.Contains("REEFER") || t.Contains(" RF") || t.Contains("RF ") || t.EndsWith("RF") || t.Contains("RQ")) tipo = "RF";
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
        /// <summary>
        /// Completa en los extracostos los montos que quedaron en 0 por falta de tarifa cuando se
        /// registraron (nov-2026): Roleo / Cancelación / No-Show con penalidad confirmada, Late
        /// Arrival y Extra Late Arrival. No toca montos ya cargados.
        /// </summary>
        private async Task CompletarMontosDesdeTarifasAsync(Operacione op, List<ExtracostosOperacion> extracostos)
        {
            // Late Arrival con un monto mal sugerido antes de nov-2026: la búsqueda tomaba un
            // recargo cualquiera de la naviera (ej. Corrección de B/L de Hapag, CLP 81.300) cuando
            // el equipo no decía DRY/REEFER. Se reconocen por la nota del catálogo.
            foreach (var lar in extracostos.Where(e => e.TipoCosto == "Late Arrival (LAR)" && e.Monto > 0))
            {
                var m = System.Text.RegularExpressions.Regex.Match(lar.Motivo ?? "", @"\s*\|?\s*Monto sugerido desde catálogo de recargos \((?<c>.+?)\)\. Confirmar antes de facturar\.");
                if (!m.Success || m.Groups["c"].Value.ToUpperInvariant().Contains("LATE")) continue;
                var sug = await RecargoNavieraHelper.SugerirLateArrivalAsync(_context, op.IdNaviera, RecargoNavieraHelper.TipoContenedorDe(op, lar.IdUnidadTecnica));
                lar.Monto = sug?.Monto ?? 0;
                lar.Moneda = sug?.Moneda ?? "USD";
                lar.Motivo = (lar.Motivo ?? "").Replace(m.Value, "")
                    + (sug != null ? " | " + sug.NotaCatalogo : " | Sin tarifa de Late Arrival de la naviera: monto por valorizar.");
            }

            bool EsExtracostoPenalidad(string? tipo) => tipo is "Roleo Marítimo"
                or "Cancelación Marítima" or "No Show Marítimo" or "Cancelación / No Show Marítimo";
            var pendientes = extracostos.Where(e => e.Monto == 0 && (
                EsExtracostoPenalidad(e.TipoCosto)
                || e.TipoCosto == "Late Arrival (LAR)" || e.TipoCosto == RecargoNavieraHelper.TipoExtraLateArrival)).ToList();
            if (!pendientes.Any()) return;

            List<RoleoCancelacion>? registros = null;
            foreach (var ext in pendientes)
            {
                string motivo = ext.Motivo ?? "";
                if (motivo.Contains("catálogo", StringComparison.OrdinalIgnoreCase) || motivo.Contains("sin costo", StringComparison.OrdinalIgnoreCase)) continue;

                if (EsExtracostoPenalidad(ext.TipoCosto))
                {
                    registros ??= await _context.RoleosCancelaciones.Where(r => r.IdOperacion == op.Id).ToListAsync();
                    string tipoReg = ext.TipoCosto switch
                    {
                        "Roleo Marítimo" => "ROLEO",
                        "Cancelación Marítima" => "CANCELACION",
                        "No Show Marítimo" => "NO_SHOW",
                        _ => "CANCELACION_NOSHOW" // soporte para registros antiguos
                    };
                    var reg = registros.Where(r => r.Tipo == tipoReg)
                        .OrderBy(r => Math.Abs((r.FechaCreacion - ext.FechaCreacion).TotalMinutes))
                        .FirstOrDefault(r => Math.Abs((r.FechaCreacion - ext.FechaCreacion).TotalMinutes) < 5);
                    if (reg == null || reg.PenalidadConfirmada != "SI") continue;
                    var tarifa = await RecargoNavieraHelper.SugerirPenalidadAsync(_context, op.IdNaviera,
                        RecargoNavieraHelper.GrupoPenalidad(reg.Tipo, reg.DiasAnticipacion.HasValue && reg.DiasAnticipacion.Value <= (op.IdNavieraNavigation?.DiasPenalidadRoleo ?? 7)));
                    bool desdeTarifa = !reg.MontoPenalidadUnitarioUsd.HasValue;
                    decimal? unitario = reg.MontoPenalidadUnitarioUsd ?? tarifa?.Monto;
                    if (!unitario.HasValue || unitario.Value <= 0) continue;
                    reg.MontoPenalidadUnitarioUsd = unitario;
                    ext.Monto = unitario.Value * Math.Max(1, reg.CantidadContenedoresAfectados);
                    ext.Moneda = desdeTarifa && tarifa != null ? tarifa.Moneda : (ext.Moneda ?? "USD");
                    ext.GrupoOperativo ??= "Marítimo";
                    if (desdeTarifa && tarifa != null) ext.Motivo = (motivo + " | " + tarifa.NotaCatalogo).Trim(' ', '|');
                }
                else
                {
                    var sug = ext.TipoCosto == "Late Arrival (LAR)"
                        ? await RecargoNavieraHelper.SugerirLateArrivalAsync(_context, op.IdNaviera, RecargoNavieraHelper.TipoContenedorDe(op, ext.IdUnidadTecnica))
                        : await RecargoNavieraHelper.SugerirExtraLateArrivalAsync(_context, op.IdNaviera);
                    if (sug == null || sug.Monto <= 0) continue;
                    ext.Monto = sug.Monto;
                    ext.Moneda = sug.Moneda ?? "USD";
                    ext.GrupoOperativo ??= "Marítimo";
                    ext.Motivo = (motivo + " | " + sug.NotaCatalogo).Trim(' ', '|');
                }
            }
        }

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
            // Los históricos pre-ERP son una fotografía contable del Excel. Nunca se
            // recalculan con tarifas/reglas actuales porque eso alteraría la historia.
            bool esHistorica = await _context.Operaciones.AsNoTracking()
                .Where(o => o.Id == idOperacion)
                .Select(o => o.EsHistorica)
                .FirstOrDefaultAsync();
            if (esHistorica) return;

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

            // Conceptos extraordinarios (nov-2026): líneas manuales agregadas por Finanzas. No se
            // cargan aquí para que ningún espejo, limpieza ni ajuste las toque (ver
            // ConceptoExtraordinarioHelper). El prefijo debe coincidir con ConceptoExtraordinarioHelper.Prefijo.
            var txExistentes = await _context.TransaccionesFinancieras
                .Where(t => t.IdOperacion == idOperacion
                         && !(t.TarifaManual && t.JustificacionManual != null && t.JustificacionManual.StartsWith("Concepto extraordinario")))
                .ToListAsync();

            DateTime ahora = DateTime.Now;

            // Tarifas comerciales especiales del cliente (ej. Marfrio): conceptos marcados
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
                .Where(t => t.EsActiva == true &&
                    (!t.FechaInicioVigencia.HasValue || t.FechaInicioVigencia.Value <= ahora) &&
                    (!t.FechaFinVigencia.HasValue || t.FechaFinVigencia.Value >= ahora.Date))
                .OrderByDescending(t => t.FechaInicioVigencia)
                .ThenByDescending(t => t.Id)
                .ToListAsync();
            // Rendimiento (nov-2026): el maestro de conceptos se lee una vez por sincronización;
            // GenerarEspejo lo consultaba en la BD en cada concepto y cada contenedor.
            var tarifasMaestrasActivas = await _context.TarifasMaestras.AsNoTracking()
                .Where(t => t.EsActiva)
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
            async Task GenerarEspejo(string grupo, string concepto, bool activo, string busquedaTarifa, decimal? valorOverride = null, string? monedaOverride = null, int? idProveedorOverride = null, bool convertirAClp = false, bool? ivaExento = null, string? responsablePagoOverride = null, string? responsableOverride = null, decimal? valorVentaOverride = null, string? monedaVentaOverride = null, bool costoSinCargo = false, bool ventaSinCargo = false, string? notaSinCargo = null, bool soloTarifaEspecifica = false, bool? ivaExentoVenta = null)
            {
                // IVA de la venta (nov-2026): el de la tarifa del cliente cuando es distinto del
                // costo (ej. flete: la naviera no cobra IVA, la venta sigue el acuerdo comercial).
                bool? ivaIngreso = ivaExentoVenta ?? ivaExento;
                // En lo Marítimo el IVA sigue la regla del concepto también en filas ajustadas en
                // Acuerdos Operativos (allí solo se ajusta monto y moneda).
                bool ivaManda = grupo == "Marítimo";
                string? MarcaSinCargo(bool sinCargo, string? actual) =>
                    sinCargo ? (notaSinCargo ?? AcuerdoCollectHelper.MarcaSinCosto)
                    : (AcuerdoCollectHelper.EsMarcaSinCosto(actual) ? null : actual);
                bool busquedaTieneUnidad = busquedaTarifa.Contains("(U");
                var txsIngreso = txExistentes.Where(t => t.GrupoCobro == grupo && t.Concepto.Contains(busquedaTarifa) && (busquedaTieneUnidad || !t.Concepto.Contains(" (U")) && t.TipoMovimiento == "INGRESO").ToList();
                var txsEgreso = txExistentes.Where(t => t.GrupoCobro == grupo && t.Concepto.Contains(busquedaTarifa) && (busquedaTieneUnidad || !t.Concepto.Contains(" (U")) && t.TipoMovimiento == "EGRESO").ToList();

                var txIngreso = txsIngreso.FirstOrDefault();
                var txEgreso = txsEgreso.FirstOrDefault();

                // No borrar duplicados automáticamente: pueden tener comprobantes, facturas,
                // pagos o ajustes manuales. Se conservan para conciliación de Finanzas.

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
                        string busquedaUpper = busquedaTarifa.ToUpper();
                        var tarifa = tarifasMaestrasActivas.FirstOrDefault(t => (t.Concepto ?? "").ToUpper().Contains(busquedaUpper));
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
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = grupo, TipoMovimiento = "INGRESO", Concepto = concepto, MontoNeto = montoVentaFinal, Moneda = monedaVenta, TipoCambio = esResponsableCliente ? tasaVenta : null, MontoOriginalUsd = esResponsableCliente ? montoOriginalUsdVenta : null, EstadoFila = estadoVentaFinal, FechaCreacion = ahora, UsuarioCreador = usuario, IdCliente = op.IdCliente, ResponsablePago = responsablePagoOverride ?? ResponsabilidadValores.Atilson, Responsable = responsableParaNuevo, IvaExento = ivaIngreso, JustificacionManual = MarcaSinCargo(ventaSinCargo, null) });
                    }
                    else
                    {
                        if (ProvisionalSinRespaldo(txIngreso)) txIngreso.Concepto = concepto;
                        if (ProvisionalSinRespaldo(txIngreso))
                        {
                            txIngreso.MontoNeto = montoVentaFinal;
                            txIngreso.Moneda = monedaVenta;
                            txIngreso.TipoCambio = esResponsableCliente ? tasaVenta : null;
                            txIngreso.MontoOriginalUsd = esResponsableCliente ? montoOriginalUsdVenta : null;
                            txIngreso.EstadoFila = estadoVentaFinal;
                            txIngreso.IvaExento = ivaIngreso;
                            txIngreso.JustificacionManual = MarcaSinCargo(ventaSinCargo, txIngreso.JustificacionManual);
                        }
                        else if (ivaManda && ivaIngreso.HasValue && ProvisionalSinRespaldo(txIngreso)) txIngreso.IvaExento = ivaIngreso;
                    }

                    if (txEgreso == null)
                    {
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = grupo, TipoMovimiento = "EGRESO", Concepto = concepto, MontoNeto = montoCosto, Moneda = moneda, TipoCambio = tasaUsada, MontoOriginalUsd = montoOriginalUsd, EstadoFila = estadoCostoFinal, FechaCreacion = ahora, UsuarioCreador = usuario, IdProveedor = idProveedorOverride, ResponsablePago = responsablePagoOverride ?? ResponsabilidadValores.Atilson, Responsable = responsableParaNuevo, IvaExento = ivaExento, JustificacionManual = MarcaSinCargo(costoSinCargo, null) });
                    }
                    else
                    {
                        if (ProvisionalSinRespaldo(txEgreso)) txEgreso.Concepto = concepto;
                        if (ProvisionalSinRespaldo(txEgreso) && idProveedorOverride.HasValue) txEgreso.IdProveedor = idProveedorOverride;
                        if (ProvisionalSinRespaldo(txEgreso)) { txEgreso.MontoNeto = montoCosto; txEgreso.Moneda = moneda; txEgreso.TipoCambio = tasaUsada; txEgreso.MontoOriginalUsd = montoOriginalUsd; txEgreso.EstadoFila = estadoCostoFinal; txEgreso.IvaExento = ivaExento; txEgreso.JustificacionManual = MarcaSinCargo(costoSinCargo, txEgreso.JustificacionManual); }
                        else if (ivaManda && ivaExento.HasValue && ProvisionalSinRespaldo(txEgreso)) txEgreso.IvaExento = ivaExento;
                    }
                }
                else
                {
                    if (txIngreso != null && ProvisionalSinRespaldo(txIngreso)) _context.TransaccionesFinancieras.Remove(txIngreso);
                    if (txEgreso != null && ProvisionalSinRespaldo(txEgreso)) _context.TransaccionesFinancieras.Remove(txEgreso);
                }
            }

            async Task GenerarEspejoDocumental(string conceptoEspejo, bool activo, string busquedaTarifa, int? idAgenciaAduana, decimal? valorOverride = null, string? numeroDocumento = null, string? monedaOverride = null, bool convertirAClp = false, decimal? valorVentaOverride = null, string? monedaVentaOverride = null, bool generarIngreso = true, decimal factorIngreso = 1m, bool forzarIngreso = false, string? clasificacionTarifa = null)
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
                if (!generarIngreso && txIngreso != null && ProvisionalSinRespaldo(txIngreso))
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
                    bool necesitaAjusteGratuito = txIngreso != null && ProvisionalSinRespaldo(txIngreso) && deberiaSerCeroAhora && txIngreso.MontoNeto != 0m;

                    // Con tarifa de venta del cliente (Comercial), el Ingreso se recalcula
                    // mientras siga en provisión y no se haya ajustado a mano.
                    // forzarIngreso: el Ingreso depende de algo más que el costo (ej. cobro
                    // grupal de Honorarios/DUS), así que se recalcula en cada sincronización
                    // mientras siga en provisión.
                    bool necesitaAjusteVenta = ((valorVentaOverride.HasValue && valorVentaOverride.Value > 0) || forzarIngreso)
                        && txIngreso != null && ProvisionalSinRespaldo(txIngreso);

                    bool necesitaAjusteCosto = txEgreso != null && ProvisionalSinRespaldo(txEgreso);
                    if (txIngreso == null || txEgreso == null || txIngreso.EstadoFila == "PENDIENTE VALORIZAR" || txEgreso.EstadoFila == "PENDIENTE VALORIZAR" || txIngreso.NumeroDocumentoDocumental != numeroDocumento || faltaTipoCambio || necesitaAjusteGratuito || necesitaAjusteVenta || necesitaAjusteCosto)
                    {
                        decimal montoCosto = 0m;
                        string moneda = "CLP";
                        bool hayTarifaDocumental = false;

                        if (idAgenciaAduana.HasValue)
                        {
                            // Preload en memoria (ver todasTarifasDocumentalesActivas arriba) — mismo
                            // filtro y mismo orden que la consulta original, sin ir a la BD por cada llamada.
                            var tarifasAgencia = todasTarifasDocumentalesActivas
                                .Where(t => t.IdAgenciaAduana == idAgenciaAduana.Value &&
                                    CoincideConceptoTarifaDocumental(t.Concepto, busquedaTarifa))
                                .ToList();
                            // Distintas tarifas para SAG/Sernapesca: preferir la clasificación
                            // exacta y luego una genérica, nunca otra clasificación incompatible.
                            // No cruzar SAG con SERNAPESCA, ni usar una tarifa SAG si
                            // Operaciones dejó la clasificación vacía. "COMUN"/"COMUNES"
                            // son los únicos respaldos generales válidos.
                            bool EsGenerica(TarifasDocumentale t) =>
                                string.IsNullOrWhiteSpace(t.Clasificacion) ||
                                VentaClienteHelper.Norm(t.Clasificacion).StartsWith("COMUN");
                            // Los certificados llevan la clasificación del organismo del
                            // trámite, aunque la operación procese también el otro organismo.
                            // Antes clasificacionTarifa=null hacía que SOLO encontrara COMUN.
                            string? clasificacionEfectiva = conceptoEspejo.StartsWith("Certificado Fitosanitario", StringComparison.OrdinalIgnoreCase)
                                ? "SAG"
                                : conceptoEspejo.StartsWith("Certificado Sanitario", StringComparison.OrdinalIgnoreCase)
                                    || conceptoEspejo.StartsWith("Certificado de Captura", StringComparison.OrdinalIgnoreCase)
                                    ? "SERNAPESCA" : clasificacionTarifa;
                            var tarifaDoc = !string.IsNullOrWhiteSpace(clasificacionEfectiva)
                                ? tarifasAgencia.FirstOrDefault(t =>
                                    VentaClienteHelper.Norm(t.Clasificacion) ==
                                    VentaClienteHelper.Norm(clasificacionEfectiva))
                                    ?? tarifasAgencia.FirstOrDefault(EsGenerica)
                                : tarifasAgencia.FirstOrDefault(EsGenerica);

                            if (tarifaDoc != null)
                            {
                                montoCosto = tarifaDoc.ValorNeto;
                                moneda = tarifaDoc.Moneda ?? "CLP";
                                // El costo $0 definido expresamente por Comercial es válido.
                                hayTarifaDocumental = true;
                            }
                        }

                        if (!hayTarifaDocumental)
                        {
                            var tarifaMaestra = todasTarifasMaestrasDocumental
                                .FirstOrDefault(t => CoincideConceptoTarifaDocumental(t.Concepto, busquedaTarifa));
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

                        bool costoCeroConfigurado = hayTarifaDocumental && montoCosto == 0m;
                        string estadoVenta = (montoVenta > 0 || costoCeroConfigurado || (valorOverride.HasValue && valorOverride.Value == 0)) ? "PROVISIÓN" : "PENDIENTE VALORIZAR";
                        string estadoCosto = (montoCosto > 0 || costoCeroConfigurado || (valorOverride.HasValue && valorOverride.Value == 0)) ? "PROVISIÓN" : "PENDIENTE VALORIZAR";

                        bool esResponsableClienteDoc = txIngreso == null || string.IsNullOrEmpty(txIngreso.Responsable) || txIngreso.Responsable == ResponsabilidadValores.Cliente;
                        bool esGratuitoClienteDoc = EsConceptoGratuitoCliente(conceptoEspejo);
                        decimal montoVentaFinalDoc = (esResponsableClienteDoc && !esGratuitoClienteDoc) ? montoVenta : 0m;
                        string estadoVentaFinalDoc = esGratuitoClienteDoc ? "PROVISIÓN" : (esResponsableClienteDoc ? estadoVenta : "NO FACTURABLE");

                        if (txIngreso == null) { if (generarIngreso) _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = "Documental", TipoMovimiento = "INGRESO", Concepto = conceptoEspejo, MontoNeto = montoVentaFinalDoc, Moneda = monedaVentaDoc, TipoCambio = esResponsableClienteDoc ? tasaVentaDoc : null, MontoOriginalUsd = esResponsableClienteDoc ? montoOriginalUsdVentaDoc : null, EstadoFila = estadoVentaFinalDoc, FechaCreacion = ahora, UsuarioCreador = usuario, IdCliente = op.IdCliente, NumeroDocumentoDocumental = numeroDocumento, ResponsablePago = ResponsabilidadValores.Atilson, Responsable = ResponsabilidadValores.Cliente }); }
                        else
                        {
                            if (ProvisionalSinRespaldo(txIngreso))
                            {
                                txIngreso.NumeroDocumentoDocumental = numeroDocumento;
                                // Resincronizar valores de tarifa aun si la fila ya estaba en PROVISIÓN.
                                txIngreso.MontoNeto = montoVentaFinalDoc;
                                txIngreso.Moneda = monedaVentaDoc;
                                txIngreso.EstadoFila = estadoVentaFinalDoc;
                                txIngreso.TipoCambio = esResponsableClienteDoc ? tasaVentaDoc : null;
                                txIngreso.MontoOriginalUsd = esResponsableClienteDoc ? montoOriginalUsdVentaDoc : null;
                            }
                        }

                        if (txEgreso == null) _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = "Documental", TipoMovimiento = "EGRESO", Concepto = conceptoEspejo, MontoNeto = montoCosto, Moneda = moneda, TipoCambio = tasaUsadaDoc, MontoOriginalUsd = montoOriginalUsdDoc, EstadoFila = estadoCosto, FechaCreacion = ahora, UsuarioCreador = usuario, NumeroDocumentoDocumental = numeroDocumento, ResponsablePago = ResponsabilidadValores.Atilson, Responsable = ResponsabilidadValores.Cliente });
                        else
                        {
                            if (ProvisionalSinRespaldo(txEgreso))
                            {
                                txEgreso.NumeroDocumentoDocumental = numeroDocumento;
                                // Egresos provisionales deben seguir la tarifa vigente de Comercial.
                                txEgreso.MontoNeto = montoCosto;
                                txEgreso.Moneda = moneda;
                                txEgreso.EstadoFila = estadoCosto;
                                txEgreso.TipoCambio = tasaUsadaDoc;
                                txEgreso.MontoOriginalUsd = montoOriginalUsdDoc;
                            }
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

            // Collect / Full Collect: el flete marítimo lo paga el consignatario
            // a la naviera. Control Financiero no genera ni muestra conceptos
            // Rebate Marítimo. Las tarifas Collect se mantienen disponibles para
            // reglas operacionales y condiciones comerciales sin costo.
            bool esCollectMar = op.CondicionPago == 3 || op.CondicionPago == 4; // Collect / Full Collect

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
            List<TarifaCollect> tarifasCollectNav = new();
            if (isMaritimo && esCollectMar && op.IdNaviera > 0)
            {
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
                // Flete marítimo: USD y exento, costo y venta, todas las navieras (nov-2026).
                await GenerarEspejo("Marítimo", conceptoFlete, isMaritimo && !esCollectMar, $"Flete Marítimo{sufijoFlete}", fleteCosto, "USD", soloTarifaEspecifica: true,
                    ivaExento: true,
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

            // REBATES MARÍTIMOS: exclusivamente para COLLECT / FULL COLLECT.
            // El consignatario paga el flete; la naviera paga su rebate a Atilson
            // (INGRESO) y Atilson entrega al cliente el suyo (EGRESO).
            // Nunca espejar ambas direcciones: son montos y contrapartes distintos.
            // Un movimiento facturado/pagado/con respaldo o manual jamás se altera.
            void SincronizarRebate(string concepto, string movimiento, decimal? valor, string moneda)
            {
                var anteriores = txExistentes.Where(t => t.GrupoCobro == "Marítimo" &&
                    string.Equals(t.Concepto, concepto, StringComparison.OrdinalIgnoreCase) &&
                    t.TipoMovimiento == movimiento).ToList();
                // Mantener primero un asiento protegido para no generar una segunda provisión.
                var actual = anteriores.FirstOrDefault(t => !ProvisionalSinRespaldo(t)) ??
                    anteriores.FirstOrDefault();
                foreach (var duplicado in anteriores.Where(t => t != actual && ProvisionalSinRespaldo(t)))
                    _context.TransaccionesFinancieras.Remove(duplicado);

                if (!isMaritimo || !esCollectMar)
                {
                    if (actual != null && ProvisionalSinRespaldo(actual))
                        _context.TransaccionesFinancieras.Remove(actual);
                    return;
                }

                decimal monto = Math.Max(0m, valor ?? 0m);
                string estado = valor.HasValue && monto > 0m ? "PROVISIÓN" : "PENDIENTE VALORIZAR";
                if (actual == null)
                {
                    _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera
                    {
                        IdOperacion = op.Id,
                        GrupoCobro = "Marítimo",
                        TipoMovimiento = movimiento,
                        Concepto = concepto,
                        MontoNeto = monto,
                        Moneda = moneda,
                        EstadoFila = estado,
                        IvaExento = true,
                        IdCliente = movimiento == "EGRESO" ? op.IdCliente : null,
                        ResponsablePago = movimiento == "INGRESO" ? "NAVIERA" : "ATILSON",
                        Responsable = movimiento == "EGRESO" ? ResponsabilidadValores.Cliente : ResponsabilidadValores.Atilson,
                        FechaCreacion = ahora,
                        UsuarioCreador = usuario
                    });
                }
                else if (ProvisionalSinRespaldo(actual))
                {
                    actual.MontoNeto = monto;
                    actual.Moneda = moneda;
                    actual.EstadoFila = estado;
                    actual.IvaExento = true;
                }
            }

            bool CoincideEquipoCollect(string? equipoTarifa, string? equipoOperacion)
            {
                if (string.IsNullOrWhiteSpace(equipoTarifa)) return true;
                string general = equipoTarifa.Trim().ToUpperInvariant();
                if (general is "ALL" or "TODOS" or "TODAS" or "AMBOS") return true;
                var (tamTar, tipoTar) = NormalizarEquipo(equipoTarifa);
                var (tamOp, tipoOp) = NormalizarEquipo(equipoOperacion);
                if (tamOp == "") return false;
                bool reeferOperacion = tipoOp == "RF";
                if (tamTar == "")
                {
                    if (general.Contains("REEFER") || general is "RF") return reeferOperacion;
                    if (general.Contains("DRY") || general.Contains("SECO")) return !reeferOperacion;
                    return false;
                }
                return tamTar == tamOp &&
                    (tipoTar == tipoOp || (tipoTar == "RF") == reeferOperacion);
            }
            bool CoincideLugarRebate(string? tarifaLugar, string? operacionLugar)
                => string.IsNullOrWhiteSpace(tarifaLugar) ||
                   (!string.IsNullOrWhiteSpace(operacionLugar) &&
                    (string.Equals(tarifaLugar.Trim(), operacionLugar.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     tarifaLugar.Contains(operacionLugar, StringComparison.OrdinalIgnoreCase) ||
                     operacionLugar.Contains(tarifaLugar, StringComparison.OrdinalIgnoreCase)));

            var tarifasRebateCliente = isMaritimo && esCollectMar
                ? await _context.TarifasClientes.AsNoTracking()
                    .Where(t => t.IdCliente == op.IdCliente && t.EsActiva &&
                        t.GrupoCobro == "Marítimo" && t.RebateUsd.HasValue &&
                        t.FechaInicioVigencia <= ahora && t.FechaFinVigencia >= ahora.Date)
                    .OrderByDescending(t => t.FechaInicioVigencia).ThenByDescending(t => t.Id)
                    .ToListAsync()
                : new List<TarifasCliente>();

            foreach (var unidadRebate in contenedoresFlete)
            {
                string sufijo = unidadRebate.IdUnidad.HasValue ? $" (U{unidadRebate.IdUnidad})" : "";
                // En un booking multiequipo (20'/40', Dry/Reefer) el acuerdo Collect
                // debe corresponder a ESTE contenedor, no al equipo de la primera fila.
                var acuerdosEquipo = tarifasCollectNav.Where(t =>
                    CoincideEquipoCollect(t.Equipamiento, unidadRebate.Equipo)).ToList();
                DateTime fechaAcuerdo = op.EtdPol ?? ahora;
                var acuerdoNavUnidad = AcuerdoCollectHelper.Elegir(acuerdosEquipo,
                    TarifaCollect.TipoNaviera, null, op.IdNaviera,
                    op.IdPuertoOrigenNavigation?.NombrePuerto,
                    op.IdPuertoDestinoNavigation?.NombrePuerto, op.Commodity, fechaAcuerdo);
                var acuerdoCliUnidad = AcuerdoCollectHelper.Elegir(acuerdosEquipo,
                    TarifaCollect.TipoCliente, op.IdCliente, op.IdNaviera,
                    op.IdPuertoOrigenNavigation?.NombrePuerto,
                    op.IdPuertoDestinoNavigation?.NombrePuerto, op.Commodity, fechaAcuerdo);
                bool acuerdoCliCoincide = acuerdoCliUnidad != null;

                decimal? rebateNaviera = acuerdoNavUnidad?.RebateUsd
                    ?? tarifaMarPorEquipo(unidadRebate.Equipo)?.RebateUsd;
                var tarifaCliente = tarifasRebateCliente.FirstOrDefault(t =>
                    (!t.IdNaviera.HasValue || t.IdNaviera.Value == op.IdNaviera) &&
                    CoincideLugarRebate(t.Pol, op.IdPuertoOrigenNavigation?.NombrePuerto) &&
                    CoincideLugarRebate(t.Pod, op.IdPuertoDestinoNavigation?.NombrePuerto) &&
                    CoincideEquipoCollect(t.TipoContenedor, unidadRebate.Equipo));
                decimal? rebateCliente = acuerdoCliCoincide ? acuerdoCliUnidad!.RebateUsd : tarifaCliente?.RebateUsd;
                string monedaCliente = acuerdoCliCoincide ? "USD" : (tarifaCliente?.Moneda ?? "USD");

                // Principal conserva el concepto histórico; adicionales llevan sufijo Uxx.
                SincronizarRebate("Rebate Marítimo (Naviera)" + sufijo, "INGRESO", rebateNaviera, "USD");
                SincronizarRebate("Rebate Marítimo (Cliente)" + sufijo, "EGRESO", rebateCliente, monedaCliente);
            }

            // Limpiar líneas de rebate de unidades eliminadas; solo si son provisiones
            // automáticas sin respaldo. Nunca borrar movimientos contabilizados.
            var idsRebate = contenedoresFlete.Where(u => u.IdUnidad.HasValue)
                .Select(u => u.IdUnidad!.Value).ToHashSet();
            foreach (var antiguo in txExistentes.Where(t => t.GrupoCobro == "Marítimo" &&
                (t.Concepto ?? "").StartsWith("Rebate Marítimo", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var sufijo = System.Text.RegularExpressions.Regex.Match(antiguo.Concepto ?? "", @"\(U(\d+)\)");
                if (sufijo.Success && !idsRebate.Contains(int.Parse(sufijo.Groups[1].Value)) &&
                    ProvisionalSinRespaldo(antiguo) && _context.Entry(antiguo).State != EntityState.Deleted)
                    _context.TransaccionesFinancieras.Remove(antiguo);
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
                    // Emisión BL: siempre CLP + IVA en Finanzas (costo y venta), cobre la naviera en
                    // USD o CLP y venga o no exenta en su tarifa (nov-2026).
                    bool? ivaExentoBl = false;

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
            async Task<(decimal? Valor, int? IdProveedor)> ObtenerTarifaTransportistaAsync(OperacionesTerrestre? terrUnidad, string retiroU, string plantaU, string entregaU, int idTipoCargaU)
            {
                // Cruce en Helpers/TarifaTransporteHelper (mismo que usa el diagnóstico de
                // Control Financiero "¿Por qué no hay tarifa?").
                var res = await TarifaTransporteHelper.BuscarAsync(_context, terrUnidad, idTipoCargaU, retiroU, plantaU, entregaU, ahora);
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

                // Cada contenedor adicional con SU equipo (nov-2026): antes se usaba el
                // Dry/Reefer y el tamaño del contenedor principal para costo y venta.
                var unidadTer = (op.Unidadestecnicas ?? new List<Unidadestecnica>()).FirstOrDefault(u => u.Id == terrU.IdUnidadTecnica);
                int tipoCargaU = unidadTer?.IdTipoCarga ?? op.IdTipoCarga;
                string equipoVentaU = VentaClienteHelper.EquipoOperacion(
                    string.IsNullOrWhiteSpace(unidadTer?.TipoContenedor) ? op.TipoContenedor : unidadTer!.TipoContenedor, tipoCargaU);

                var (tarifaU, idTransportistaU) = await ObtenerTarifaTransportistaAsync(terrU, retiroU, plantaU, entregaU, tipoCargaU);

                var ventaTerrU = VentaClienteHelper.ElegirTerrestre(ventasCliente, retiroU, plantaU, entregaU, equipoVentaU);
                if (ventaTerrU == null && ventasCliente.Any())
                {
                    var ciuU = await TarifaTransporteHelper.CiudadesTramoAsync(_context, terrU, retiroU, plantaU, entregaU);
                    ventaTerrU = VentaClienteHelper.ElegirTerrestre(ventasCliente, ciuU.Retiro, ciuU.Planta, ciuU.Puerto, equipoVentaU);
                }
                await GenerarEspejo("Transporte", $"Flete Terrestre{sufijoU} ({rutaU})", true, $"Flete Terrestre{sufijoU}", tarifaU, "CLP", idProveedorOverride: idTransportistaU, soloTarifaEspecifica: true,
                    valorVentaOverride: ventaTerrU?.PrecioPactado, monedaVentaOverride: ventaTerrU?.Moneda);
            }

            // 2b. TARIFA PLANA PAQUETIZADA (acuerdos comerciales especiales, ej. Marfrio)
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

            // Cada certificado genera COSTO por contenedor, aunque en Operaciones se
            // haya elegido administrar el certificado de forma conjunta. Las unidades
            // sin ficha documental propia heredan el trámite y la agencia principales,
            // pero NO heredan su número de certificado (son documentos diferentes).
            var docPrincipalCertificados = op.OperacionesDocumentales
                .Where(d => d.IdUnidadTecnica == null).OrderBy(d => d.Id).FirstOrDefault()
                ?? op.OperacionesDocumentales.OrderBy(d => d.Id).FirstOrDefault();
            var contenedoresCert = new List<int?> { null };
            contenedoresCert.AddRange((op.Unidadestecnicas ?? new List<Unidadestecnica>())
                .OrderBy(u => u.Id).Select(u => (int?)u.Id));

            // GRUPAL / INDIVIDUAL son valores históricos persistidos en SQL:
            // visualmente se muestran como "Por booking" / "Por contenedor".
            bool CertPorBooking(string cert) => !string.Equals(cert switch
            {
                "ori" => op.ModalidadCertOrigen,
                "fit" => op.ModalidadCertFito,
                "san" => op.ModalidadCertSanitario,
                "cap" => op.ModalidadCertCaptura,
                _ => null
            }, "INDIVIDUAL", StringComparison.OrdinalIgnoreCase);

            foreach (var idUnidadCert in contenedoresCert)
            {
                var docReal = op.OperacionesDocumentales
                    .Where(d => d.IdUnidadTecnica == idUnidadCert).OrderBy(d => d.Id).FirstOrDefault();
                var doc = docReal ?? docPrincipalCertificados;
                if (doc == null) continue;
                int? idAg = doc.IdAgenciaAduana ?? docPrincipalCertificados?.IdAgenciaAduana;
                string sufijoUnidad = idUnidadCert.HasValue ? $" (U{idUnidadCert.Value})" : "";
                bool tieneFichaPropia = docReal != null;
                bool numeroCertificadoPropio = !idUnidadCert.HasValue || tieneFichaPropia;
                // Los organismos requeridos en la operación aplican también a
                // contenedores adicionales, salvo que se trate de un trámite
                // exclusivo de otra operación. Los datos propios prevalecen.
                bool aplicaSagUnidad = doc.AplicaSag ||
                    (idUnidadCert.HasValue && docPrincipalCertificados?.AplicaSag == true);
                bool aplicaSernaUnidad = doc.AplicaSernapesca ||
                    (idUnidadCert.HasValue && docPrincipalCertificados?.AplicaSernapesca == true);
                bool aplicaOrigenUnidad = doc.CertificadoOrigen == true ||
                    (idUnidadCert.HasValue && docPrincipalCertificados?.CertificadoOrigen == true);
                string? modalidadSagUnidad = doc.SagModalidad ??
                    docPrincipalCertificados?.SagModalidad;
                bool generarIngresoOrigen = !CertPorBooking("ori") || !idUnidadCert.HasValue;
                bool generarIngresoFito = !CertPorBooking("fit") || !idUnidadCert.HasValue;
                bool generarIngresoSanitario = !CertPorBooking("san") || !idUnidadCert.HasValue;
                bool generarIngresoCaptura = !CertPorBooking("cap") || !idUnidadCert.HasValue;

                bool esClienteEspecialOrigen = op.IdClienteNavigation?.EsClienteEspecial == true;
                string? acuerdoOrigenEfectivo = doc.AcuerdoOrigen;
                if (esClienteEspecialOrigen)
                {
                    string? acuerdoTlcAuto = DetectarAcuerdoTlcPorDestino(op.IdPuertoDestinoNavigation?.Pais, op.IdPuertoDestinoNavigation?.NombrePuerto);
                    if (acuerdoTlcAuto != null) acuerdoOrigenEfectivo = acuerdoTlcAuto;
                }
                decimal? valorAcuerdoOrigen = CalcularValorAcuerdoOrigen(acuerdoOrigenEfectivo, esClienteEspecialOrigen);
                string? monedaAcuerdoOrigen = valorAcuerdoOrigen.HasValue ? "CLP" : null;
                // El certificado de origen también puede estar tarifado dentro del
                // grupo SAG/Sernapesca de esta operación, no solo en COMUNES.
                string? clasificacionOrigen = doc.AplicaSag ? "SAG"
                    : doc.AplicaSernapesca ? "SERNAPESCA" : null;
                bool ori2Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "ori", 2);
                bool ori3Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "ori", 3);
                bool ori4Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "ori", 4);

                await GenerarEspejoDocumental($"Certificado de Origen{sufijoUnidad}", aplicaOrigenUnidad, "CERTIFICADO ORIGEN", idAg, valorAcuerdoOrigen ?? doc.ValOri1, (numeroCertificadoPropio ? doc.RemisionOrigen1 : null), monedaAcuerdoOrigen, clasificacionTarifa: clasificacionOrigen, generarIngreso: generarIngresoOrigen);
                await GenerarEspejoDocumental($"Certificado de Origen R1{sufijoUnidad}", aplicaOrigenUnidad && ori2Bloqueado, "RE-EMISION CERTIFICADOS ORIGEN", idAg, valorAcuerdoOrigen ?? doc.ValOri2, (numeroCertificadoPropio ? doc.RemisionOrigen2 : null), monedaAcuerdoOrigen, clasificacionTarifa: clasificacionOrigen, generarIngreso: generarIngresoOrigen);
                await GenerarEspejoDocumental($"Certificado de Origen R2{sufijoUnidad}", aplicaOrigenUnidad && ori3Bloqueado, "RE-EMISION CERTIFICADOS ORIGEN", idAg, valorAcuerdoOrigen ?? doc.ValOri3, (numeroCertificadoPropio ? doc.NumOri3 : null), monedaAcuerdoOrigen, clasificacionTarifa: clasificacionOrigen, generarIngreso: generarIngresoOrigen);
                await GenerarEspejoDocumental($"Certificado de Origen R3{sufijoUnidad}", aplicaOrigenUnidad && ori4Bloqueado, "RE-EMISION CERTIFICADOS ORIGEN", idAg, valorAcuerdoOrigen ?? doc.ValOri4, (numeroCertificadoPropio ? doc.NumOri4 : null), monedaAcuerdoOrigen, clasificacionTarifa: clasificacionOrigen, generarIngreso: generarIngresoOrigen);

                bool fit2Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "fit", 2);
                bool fit3Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "fit", 3);
                bool fit4Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "fit", 4);
                await GenerarEspejoDocumental($"Certificado Fitosanitario{sufijoUnidad}", aplicaSagUnidad, "FITO", idAg, doc.ValFit1, (numeroCertificadoPropio ? doc.CertFitosanitario : null), generarIngreso: generarIngresoFito);
                await GenerarEspejoDocumental($"Certificado Fitosanitario R1{sufijoUnidad}", aplicaSagUnidad && fit2Bloqueado, "RE-EMISION CERTIFICADOS SAG", idAg, doc.ValFit2, (numeroCertificadoPropio ? doc.NumFit2 : null), generarIngreso: generarIngresoFito);
                await GenerarEspejoDocumental($"Certificado Fitosanitario R2{sufijoUnidad}", aplicaSagUnidad && fit3Bloqueado, "RE-EMISION CERTIFICADOS SAG", idAg, doc.ValFit3, (numeroCertificadoPropio ? doc.NumFit3 : null), generarIngreso: generarIngresoFito);
                await GenerarEspejoDocumental($"Certificado Fitosanitario R3{sufijoUnidad}", aplicaSagUnidad && fit4Bloqueado, "RE-EMISION CERTIFICADOS SAG", idAg, doc.ValFit4, (numeroCertificadoPropio ? doc.NumFit4 : null), generarIngreso: generarIngresoFito);

                bool san2Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "san", 2);
                bool san3Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "san", 3);
                bool san4Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "san", 4);
                await GenerarEspejoDocumental($"Certificado Sanitario{sufijoUnidad}", aplicaSernaUnidad, "CERTIFICADO SANITARIO", idAg, doc.ValSan1, (numeroCertificadoPropio ? doc.CertSanitario : null), generarIngreso: generarIngresoSanitario);
                await GenerarEspejoDocumental($"Certificado Sanitario R1{sufijoUnidad}", aplicaSernaUnidad && san2Bloqueado, "RE-EMISION", idAg, doc.ValSan2, (numeroCertificadoPropio ? doc.NumSan2 : null), generarIngreso: generarIngresoSanitario);
                await GenerarEspejoDocumental($"Certificado Sanitario R2{sufijoUnidad}", aplicaSernaUnidad && san3Bloqueado, "RE-EMISION", idAg, doc.ValSan3, (numeroCertificadoPropio ? doc.NumSan3 : null), generarIngreso: generarIngresoSanitario);
                await GenerarEspejoDocumental($"Certificado Sanitario R3{sufijoUnidad}", aplicaSernaUnidad && san4Bloqueado, "RE-EMISION", idAg, doc.ValSan4, (numeroCertificadoPropio ? doc.NumSan4 : null), generarIngreso: generarIngresoSanitario);

                bool cap2Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "cap", 2);
                bool cap3Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "cap", 3);
                bool cap4Bloqueado = DocumentalHelpers.SlotBloqueado(doc, "cap", 4);
                await GenerarEspejoDocumental($"Certificado de Captura{sufijoUnidad}", aplicaSernaUnidad, "CERTIFICADO CAPTURA SERNAPESCA", idAg, doc.ValCap1, (numeroCertificadoPropio ? doc.NumCap1 : null), generarIngreso: generarIngresoCaptura);
                await GenerarEspejoDocumental($"Certificado de Captura R1{sufijoUnidad}", aplicaSernaUnidad && cap2Bloqueado, "RE-EMISION", idAg, doc.ValCap2, (numeroCertificadoPropio ? doc.NumCap2 : null), generarIngreso: generarIngresoCaptura);
                await GenerarEspejoDocumental($"Certificado de Captura R2{sufijoUnidad}", aplicaSernaUnidad && cap3Bloqueado, "RE-EMISION", idAg, doc.ValCap3, (numeroCertificadoPropio ? doc.NumCap3 : null), generarIngreso: generarIngresoCaptura);
                await GenerarEspejoDocumental($"Certificado de Captura R3{sufijoUnidad}", aplicaSernaUnidad && cap4Bloqueado, "RE-EMISION", idAg, doc.ValCap4, (numeroCertificadoPropio ? doc.NumCap4 : null), generarIngreso: generarIngresoCaptura);

                if (aplicaSagUnidad)
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

                    if (modalidadSagUnidad == "Planta")
                    {
                        foreach (var tx in txsFito.Where(ProvisionalSinRespaldo))
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
                        foreach (var tx in txsFito.Where(t =>
                            t.TarifaManual && t.JustificacionManual == justificacionSagPlanta &&
                            !t.FechaEmision.HasValue && !t.ConfirmadoAga &&
                            t.EstadoFila is ("PROVISIÓN" or "PENDIENTE VALORIZAR") &&
                            string.IsNullOrWhiteSpace(t.NumeroDocumento) &&
                            string.IsNullOrWhiteSpace(t.NumeroOrdenCompra) &&
                            string.IsNullOrWhiteSpace(t.RutaFactura) &&
                            string.IsNullOrWhiteSpace(t.RutaComprobante)))
                        {
                            bool esReemision = tx.Concepto.Contains(" R1") || tx.Concepto.Contains(" R2") || tx.Concepto.Contains(" R3");
                            string busquedaFito = esReemision ? "RE-EMISION CERTIFICADOS SAG" : "FITO";

                            decimal montoTarifa = 0m;
                            string monedaTarifa = "USD";

                            bool tarifaEncontrada = false;
                            if (idAg.HasValue)
                            {
                                // La transición Planta -> Puerto usa exactamente el mismo
                                // cruce SAG que un fitosanitario recién creado.
                                var tarifasFito = todasTarifasDocumentalesActivas
                                    .Where(t => t.IdAgenciaAduana == idAg.Value &&
                                        CoincideConceptoTarifaDocumental(t.Concepto, busquedaFito)).ToList();
                                var tarifaDoc = tarifasFito.FirstOrDefault(t =>
                                        VentaClienteHelper.Norm(t.Clasificacion) == "SAG")
                                    ?? tarifasFito.FirstOrDefault(t =>
                                        string.IsNullOrWhiteSpace(t.Clasificacion) ||
                                        VentaClienteHelper.Norm(t.Clasificacion).StartsWith("COMUN"));
                                if (tarifaDoc != null)
                                {
                                    montoTarifa = tarifaDoc.ValorNeto;
                                    monedaTarifa = tarifaDoc.Moneda ?? "CLP";
                                    tarifaEncontrada = true;
                                }
                            }

                            if (!tarifaEncontrada)
                            {
                                var tarifaMaestra = todasTarifasMaestrasDocumental
                                    .FirstOrDefault(t => CoincideConceptoTarifaDocumental(t.Concepto, busquedaFito));
                                if (tarifaMaestra != null)
                                {
                                    montoTarifa = tarifaMaestra.ValorNeto;
                                    monedaTarifa = tarifaMaestra.Moneda ?? "CLP";
                                    tarifaEncontrada = true;
                                }
                            }

                            (montoTarifa, monedaTarifa) = await NormalizarMonedaDocumentalAsync(montoTarifa, monedaTarifa);

                            tx.MontoNeto = montoTarifa;
                            tx.Moneda = monedaTarifa;
                            tx.TarifaManual = false;
                            tx.JustificacionManual = null;
                            tx.EstadoFila = (montoTarifa > 0 || tarifaEncontrada) ? "PROVISIÓN" : "PENDIENTE VALORIZAR";
                        }
                    }
                }

                // Sernapesca no debe borrar marcas TarifaManual por el mero hecho de
                // encontrar un monto 0: puede ser un ajuste legítimo de Finanzas.

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
                    // nov-2026: el "Valor Trámite ($)" del DUS/DIN se quitó de la pestaña Documental;
                    // el costo sale siempre de la tarifa de la agencia en Comercial.
                    decimal? valorLegalizacionDus = null;
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
                        decimal? valorDusUnidad = valorLegalizacionDus;
                        // Venta al cliente (Comercial > Documental, SAG o SERNAPESCA): DUS sin IVA.
                        var ventaDusUnidad = VentaClienteHelper.ElegirDocumental(ventasCliente, esDus: true, VentaClienteHelper.ClasificacionDocumental(docUnidadDus));
                        // N° del DUS (o DIN) para la columna "N° Documento" de Control Financiero.
                        string? numeroDus = (tieneDin && !tieneDus)
                            ? (string.IsNullOrWhiteSpace(docUnidadDus.Din) ? docParaDus.Din : docUnidadDus.Din)
                            : (string.IsNullOrWhiteSpace(docUnidadDus.DusDin) ? docParaDus.DusDin : docUnidadDus.DusDin);
                        return (IdUnidad: idUnidadDus, IdAg: docUnidadDus.IdAgenciaAduana ?? docParaDus.IdAgenciaAduana, Valor: valorDusUnidad, Venta: ventaDusUnidad, Numero: numeroDus);
                    }).ToList();
                    // Cobro POR BOOKING (nov-2026, regla de Atilson): se cobra UNA sola vez al
                    // cliente, en la línea del 1° contenedor, al valor de un contenedor; los demás
                    // contenedores quedan solo con su costo. Por contenedor: un cobro por cada uno.
                    // (Entre el 25-oct y el 10-nov la línea única cobraba la suma de todos.)
                    foreach (var l in lineasDus)
                    {
                        string sufijoDus = l.IdUnidad.HasValue ? $" (U{l.IdUnidad})" : "";
                        await GenerarEspejoDocumental($"Legalización / Emisión DUS{sufijoDus}", true, busquedaLegalizacionDus, l.IdAg, l.Valor,
                            numeroDocumento: l.Numero,
                            valorVentaOverride: l.Venta?.PrecioPactado,
                            monedaVentaOverride: l.Venta?.Moneda,
                            generarIngreso: !cobroAgenciaGrupal || !l.IdUnidad.HasValue,
                            factorIngreso: 1m,
                            forzarIngreso: true);
                    }

                    // El DUS se cobra al cliente sin IVA (oct-2026).
                    foreach (var txDusIng in _context.TransaccionesFinancieras.Local
                        .Where(t => t.IdOperacion == op.Id && t.TipoMovimiento == "INGRESO" && (t.Concepto ?? "").StartsWith("Legalización / Emisión DUS")))
                        if (ProvisionalSinRespaldo(txDusIng)) txDusIng.IvaExento = true;
                }
            }

            // 3b. HONORARIOS DE AGENCIA — SIEMPRE por contenedor, a diferencia de los
            // certificados (que pueden ir Grupal o Individual). Un contenedor que todavía
            // no tiene su propio registro documental (modalidad Grupal, o Individual recién
            // activada sin guardar) hereda Agencia/SAG/Sernapesca del registro principal.
            if (reqDocumentalServicio)
            {
                var docPrincipalHon = op.OperacionesDocumentales.FirstOrDefault(d => d.IdUnidadTecnica == null)
                    ?? op.OperacionesDocumentales.FirstOrDefault();
                var contenedoresHon = new List<int?> { null };
                contenedoresHon.AddRange((op.Unidadestecnicas ?? new List<Unidadestecnica>()).Select(u => (int?)u.Id));

                var lineasHon = new List<(int? IdUnidad, int? IdAg, decimal? Valor, TarifasCliente? Venta, string? Clasificacion)>();
                foreach (var idUnidadHon in contenedoresHon)
                {
                    var docUnidadHon = op.OperacionesDocumentales.FirstOrDefault(d => d.IdUnidadTecnica == idUnidadHon) ?? docPrincipalHon;
                    if (docUnidadHon == null) continue;

                    // La elección SAG/SERNAPESCA y la agencia pertenecen a Operaciones.
                    // Si la unidad no tiene datos propios, hereda los del registro principal.
                    string? clasificacionHon = VentaClienteHelper.ClasificacionDocumental(docUnidadHon)
                        ?? VentaClienteHelper.ClasificacionDocumental(docPrincipalHon);
                    // Costo: tarifa de la agencia para esa clasificación, nunca un monto fijo.
                    decimal? honorariosAgencia = null;
                    // Venta: tarifa del cliente de la MISMA clasificación, no se confunde con costo.
                    var ventaHonUnidad = VentaClienteHelper.ElegirDocumental(ventasCliente, esDus: false, clasificacionHon);
                    lineasHon.Add((idUnidadHon, docUnidadHon.IdAgenciaAduana ?? docPrincipalHon?.IdAgenciaAduana,
                        honorariosAgencia, ventaHonUnidad, clasificacionHon));
                }

                // Cobro POR BOOKING (nov-2026): un solo cobro al valor de un contenedor, en la
                // línea del 1° contenedor; los demás quedan solo con su costo.
                foreach (var l in lineasHon)
                {
                    string sufijoHon = l.IdUnidad.HasValue ? $" (U{l.IdUnidad})" : "";
                    await GenerarEspejoDocumental($"Honorarios Agencia de Aduanas{sufijoHon}", l.IdAg.HasValue, "HONORARIOS AGENCIA", l.IdAg, l.Valor,
                        valorVentaOverride: l.Venta?.PrecioPactado,
                        monedaVentaOverride: l.Venta?.Moneda,
                        generarIngreso: !cobroAgenciaGrupal || !l.IdUnidad.HasValue,
                        factorIngreso: 1m,
                        forzarIngreso: true,
                        clasificacionTarifa: l.Clasificacion);
                }
                // Honorarios están afectos a IVA: no heredar accidentalmente
                // el indicador exento de DUS u otros conceptos documentales.
                foreach (var txHonorarios in _context.TransaccionesFinancieras.Local
                    .Where(t => t.IdOperacion == op.Id && (t.Concepto ?? "").StartsWith("Honorarios Agencia de Aduanas")))
                    if (ProvisionalSinRespaldo(txHonorarios)) txHonorarios.IvaExento = false;
            }

            // 3c. DESCUENTO MULTICONTENEDOR — ACUERDOS COMERCIALES ESPECIALES (ej. Marfrio)
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
                             && t.MontoNeto > 0
                             // Un concepto extraordinario con ese nombre no es un certificado por contenedor.
                             && !ConceptoExtraordinarioHelper.Es(t))
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
            // nov-2026 (reunión con Cristian y J): el ISPS es un gasto de seguridad que factura la
            // agencia en el ámbito documental/terminal, por contenedor. Aparece siempre que el
            // servicio incluye Documental, haya o no Marítimo; un servicio solo Marítimo (o solo
            // Terrestre) no lleva ISPS. Si el tipo de servicio no está informado, se mantiene el
            // criterio anterior (que existan registros documentales).
            int servicioIsps = op.IdTipoServicio ?? 0;
            bool servicioConDocumental = servicioIsps > 0
                ? new[] { 1, 3, 4, 7, 8, 10, 11, 14 }.Contains(servicioIsps)
                : reqDocumentalOp;
            string terminalActual = (terr?.TerminalTerrestreStr ?? "").ToUpper().Trim();
            // Sin Marítimo ni Terrestre el terminal se elige en Documental y queda también en
            // operaciones.TerminalPortuario (antes, sin terminal, el ISPS no aparecía).
            if (string.IsNullOrEmpty(terminalActual)) terminalActual = (op.TerminalPortuario ?? "").ToUpper().Trim();

            // Salvaguarda: si el terminal no quedó guardado (ej. una operación "Solo
            // Marítimo" creada antes de corregir el formulario), se infiere desde el
            // puerto chileno de la operación cuando el caso es determinístico
            // (Valparaíso → TPS). San Antonio queda ambiguo entre STI y DP World y
            // requiere selección manual, así que no se auto-completa. Esto NO corrige
            // el dato en la base — solo evita que el cálculo de ISPS se pierda mientras
            // se resuelve la causa raíz en el formulario de creación.
            // Un servicio solo documental (7 / 14) usa únicamente el terminal elegido en Documental:
            // desde nov-2026 guarda POL/POD, y "Sin ISPS / no aplica" no debe inferirse como TPS.
            bool soloDocumentalIsps = servicioIsps is 7 or 14;
            if (string.IsNullOrEmpty(terminalActual) && !soloDocumentalIsps)
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

            bool activaIsps = servicioConDocumental && conceptoIspsMaestro != null;

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
                // Nov-2026: manda la tarifa de la naviera en ese depósito, pero si la naviera no
                // tiene tarifa para el equipo de ESTE contenedor se usa la general del depósito
                // (sin naviera = "aplica a todas"). Antes, con solo una tarifa de la naviera
                // cargada (ej. 20' Dry), un 40' Reefer quedaba "por valorizar" aunque el
                // depósito tuviera su tarifa general de Reefer.
                List<TarifaGate>? candidatasOutGate = null, genericasOutGate = null;
                List<TarifaGate>? candidatasInGate = null, genericasInGate = null;
                (decimal Valor, string Moneda)? MatchConRespaldo(List<TarifaGate> deNaviera, List<TarifaGate>? generales)
                    => MatchTarifa(deNaviera) ?? (generales != null ? MatchTarifa(generales) : null);
                if (aplicaGateOut && !aplicaTarifaPlana && idDepositoFisico != null)
                {
                    var tOutTodos = await _context.Set<TarifaGate>().AsNoTracking().Where(t => t.IdDeposito == idDepositoFisico && t.TipoMovimiento == "OUT" && t.EsActiva).ToListAsync();
                    candidatasOutGate = tOutTodos.Where(t => t.IdNaviera == op.IdNaviera).ToList();
                    genericasOutGate = tOutTodos.Where(t => t.IdNaviera == null).ToList();
                    var resultOut = MatchConRespaldo(candidatasOutGate, genericasOutGate);
                    if (resultOut.HasValue) { valorGateOutBase = resultOut.Value.Valor; monedaGateOutBase = resultOut.Value.Moneda; }
                }

                if (aplicaGateIn && !aplicaTarifaPlana && idDepositoFisico != null)
                {
                    var tInTodos = await _context.Set<TarifaGate>().AsNoTracking().Where(t => t.IdDeposito == idDepositoFisico && t.TipoMovimiento == "IN" && t.EsActiva).ToListAsync();
                    candidatasInGate = tInTodos.Where(t => t.IdNaviera == op.IdNaviera).ToList();
                    genericasInGate = tInTodos.Where(t => t.IdNaviera == null).ToList();
                    var resultIn = MatchConRespaldo(candidatasInGate, genericasInGate);
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

                    // Equipo de ESTE contenedor (nov-2026): un booking puede mezclar 20'/40' y
                    // Dry/Reefer. Antes todos los contenedores tomaban la tarifa del principal.
                    // tipoBusq/sizeBusq los leen MatchTarifa y ObtenerTarifaGatePlanaAsync.
                    var unidadGate = idUnidadGate.HasValue ? op.Unidadestecnicas?.FirstOrDefault(u => u.Id == idUnidadGate.Value) : null;
                    string tcGateU = ((unidadGate == null || string.IsNullOrWhiteSpace(unidadGate.TipoContenedor)) ? op.TipoContenedor : unidadGate.TipoContenedor) ?? "";
                    int tipoCargaGateU = unidadGate?.IdTipoCarga ?? op.IdTipoCarga;
                    tipoBusq = (tcGateU.Contains("RF") || tcGateU.Contains("REEFER") || tipoCargaGateU == 2) ? "RF" : "DRY";
                    sizeBusq = tcGateU.Contains("20") ? "20" : (tcGateU.Contains("40") ? "40" : "");
                    if (idUnidadGate.HasValue)
                    {
                        if (candidatasOutGate != null)
                        {
                            var rOutU = MatchConRespaldo(candidatasOutGate, genericasOutGate);
                            valorGateOut = rOutU?.Valor; monedaGateOut = rOutU?.Moneda ?? "CLP";
                        }
                        if (candidatasInGate != null)
                        {
                            var rInU = MatchConRespaldo(candidatasInGate, genericasInGate);
                            valorGateIn = rInU?.Valor; monedaGateIn = rInU?.Moneda ?? "CLP";
                        }
                    }
                    string equipoGateVentaU = VentaClienteHelper.EquipoOperacion(tcGateU, tipoCargaGateU);
                    var ventaGateOutU = aplicaGateOut ? VentaClienteHelper.ElegirGate(ventasCliente, "OUT", depRetiro, op.IdNaviera, equipoGateVentaU) : null;
                    var ventaGateInU = aplicaGateIn ? VentaClienteHelper.ElegirGate(ventasCliente, "IN", depRetiro, op.IdNaviera, equipoGateVentaU) : null;

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
                        valorVentaOverride: ventaGateOutU?.PrecioPactado, monedaVentaOverride: ventaGateOutU?.Moneda,
                        responsableOverride: gateOutIncluido ? ResponsabilidadValores.Atilson : null,
                        costoSinCargo: outSinCosto, ventaSinCargo: outSinCosto && ventaGateOutU == null, notaSinCargo: outSinCosto ? AcuerdoCollectHelper.MarcaSinCostoTarifa : null);
                    await GenerarEspejo("Depósito", conceptoGateInU, aplicaGateIn, conceptoGateInU, valorGateIn, monedaGateIn, idProveedorGate, convertirAClp: true,
                        valorVentaOverride: ventaGateInU?.PrecioPactado, monedaVentaOverride: ventaGateInU?.Moneda,
                        costoSinCargo: inSinCosto, ventaSinCargo: inSinCosto && ventaGateInU == null, notaSinCargo: inSinCosto ? AcuerdoCollectHelper.MarcaSinCostoTarifa : null);
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

            // nov-2026: montos que quedaron en 0 porque cuando se registraron no había tarifa en
            // Comercial. Roleo / Cancelación / No-Show con penalidad confirmada toman la tarifa
            // de penalidad (USD 50 por contenedor o la de la naviera); Late Arrival y Extra Late
            // Arrival, la del catálogo de la naviera. Se guarda en el extracosto (queda a la vista
            // en Operaciones). Un monto distinto de 0 nunca se toca.
            await CompletarMontosDesdeTarifasAsync(op, extracostos);

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
                if (EsPenalidadNavieraEnUsd(ext.TipoCosto))
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

                // Roleo / Cancelación / No-Show SIN penalidad (contrato, o fuera del plazo de la
                // naviera): $0 sin costo, no "Por asignar" (antes quedaba pendiente de valorizar y
                // bloqueaba el set de cobro). nov-2026.
                if (EsPenalidadNavieraEnUsd(ext.TipoCosto) && ext.Monto == 0 && (ext.Motivo ?? "").StartsWith("SIN penalidad"))
                {
                    costoSinCargoExt = true; ventaSinCargoExt = true;
                    notaSinCargoExt = $"{AcuerdoCollectHelper.MarcaSinCostoTarifa} (sin penalidad de la naviera)";
                }

                string conceptoUnico = $"{ext.TipoCosto} (EXT-{ext.Id})";
                // Cobro de un contenedor adicional (Late Arrival / Extra Late Arrival por
                // contenedor, nov-2026): sufijo "(Uxx)" como el resto de las filas por contenedor,
                // para que Control Financiero y Acuerdos Operativos muestren de qué contenedor es.
                if (ext.IdUnidadTecnica.HasValue)
                {
                    string conSufijo = $"{conceptoUnico} (U{ext.IdUnidadTecnica.Value})";
                    foreach (var txSinSufijo in txExistentes.Where(t => t.Concepto == conceptoUnico))
                        txSinSufijo.Concepto = conSufijo;
                    conceptoUnico = conSufijo;
                }
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
                // IVA (nov-2026, Ingresos y Egresos): Roleo / Cancelación / No-Show exentos; Late
                // Arrival, Extra Late, Corrección de BL y demás recargos marítimos + IVA. No depende
                // de la tarifa (que guarda lo que cobra la naviera, tal cual).
                bool? ivaExentoExt = mantenerUsd ? true : (grupoExtracosto == "Marítimo" ? false : null);
                await GenerarEspejo(grupoExtracosto, conceptoUnico, true, conceptoUnico, montoOverride, monedaOverride, idProveedorOverride: ext.IdProveedor, convertirAClp: convertirAClpExt, ivaExento: ivaExentoExt, responsablePagoOverride: NormalizarResponsabilidad(ext.ResponsableGestion), responsableOverride: NormalizarResponsabilidad(ext.Responsable),
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