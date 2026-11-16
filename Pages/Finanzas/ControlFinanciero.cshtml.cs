using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Finanzas
{
    public class ControlFinancieroModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly IWebHostEnvironment _env;

        private readonly FinanzasSyncService _sync;

        // Valor esperado en la factura: neto + IVA (desde oct-2026 también todo lo Marítimo).
        // El rebate Collect va sin IVA (devolución de flete).
        // Flete Marítimo, Roleo, Cancelación y No Show: siempre en USD y sin IVA.
        public static bool EsMaritimoUsdSinIva(string? concepto)
        {
            string c = (concepto ?? "").ToUpperInvariant();
            return c.StartsWith("FLETE MARÍTIMO") || c.StartsWith("FLETE MARITIMO")
                || c.StartsWith("ROLEO MARÍTIMO") || c.StartsWith("ROLEO MARITIMO")
                || c.StartsWith("CANCELACIÓN / NO SHOW") || c.StartsWith("CANCELACION / NO SHOW");
        }

        public static bool EsFleteMaritimo(string? concepto)
        {
            string c = (concepto ?? "").ToUpperInvariant();
            return c.StartsWith("FLETE MARÍTIMO") || c.StartsWith("FLETE MARITIMO");
        }

        /// <summary>
        /// IVA de una fila Marítima, en Ingresos y Egresos (regla de gerencia, nov-2026). Depende
        /// del concepto, no de la tarifa (la tarifa guarda lo que cobra la naviera, tal cual):
        ///  - Flete, Roleo, Cancelación y No-Show (todas las navieras): USD, exentos.
        ///  - Rebate: exento salvo que la fila diga lo contrario.
        ///  - Late Arrival, Extra Late, Emisión BL, Corrección de BL y demás recargos: CLP + IVA.
        /// </summary>
        public static bool EsMaritimoExento(string? concepto, bool? ivaExento)
        {
            if ((concepto ?? "").StartsWith("Rebate Marítimo", StringComparison.OrdinalIgnoreCase)) return ivaExento != false;
            return EsMaritimoUsdSinIva(concepto);
        }

        public static bool EsIngresoMaritimoExento(string? concepto, bool? ivaExento) => EsMaritimoExento(concepto, ivaExento);

        /// <summary>
        /// Orden dentro de la sección Marítimo (Control Financiero y Acuerdos Operativos): flete,
        /// penalidades, rebate, emisión BL, correcciones, otros recargos y al final Late Arrival
        /// y Extra Late Arrival.
        /// </summary>
        public static int OrdenMaritimo(string? concepto)
        {
        string c = (concepto ?? "").ToUpperInvariant();
        if (c.StartsWith("FLETE MARÍTIMO") || c.StartsWith("FLETE MARITIMO")) return 1;
        if (c.Contains("ROLEO")) return 2;
        if (c.Contains("CANCELACI") || c.Contains("NO SHOW")) return 3;
        if (c.Contains("REBATE")) return 4;
        if (c.StartsWith("CORRECCIÓN DE BL N°") || c.StartsWith("CORRECCION DE BL N°")) return 6;
        if (c.Contains("EXTRA LATE") || c.Contains("(ELAR)")) return 91;
        if (c.Contains("LATE ARRIVAL") || c.Contains("(LAR)")) return 90;
        if (c.Contains("BL") && (c.Contains("EMISI") || c.Contains("ORIGINAL") || c.Contains("SEAWAY") || c.Contains("SEA WAYBILL") || c.Contains("TELEX") || c.Contains("EXPRESS") || c.Contains("DESTINO"))) return 5;
        return 50;
        }

        private static decimal ValorCompararAga(TransaccionesFinanciera tx)
            => ((tx.GrupoCobro == "Marítimo" && EsMaritimoExento(tx.Concepto, tx.IvaExento))
                || (tx.GrupoCobro != "Marítimo" && EsMaritimoUsdSinIva(tx.Concepto)))
                ? tx.MontoNeto
                : Math.Round(tx.MontoNeto * 1.19m, tx.Moneda == "CLP" ? 0 : 2);

        private static decimal TolAga(TransaccionesFinanciera tx) => tx.Moneda == "CLP" ? 1m : 0.01m;

        public ControlFinancieroModel(AtilsonContext context, IWebHostEnvironment env, AtilsonCargoSpa.Services.FinanzasSyncService sync)
        {
            _context = context;
            _env = env;
            _sync = sync;
        }

        public List<TarifaGate> ComponentesGate { get; set; } = new();

        // Conceptos marcados "Servicio Gratuito" en TarifasClientes (Comercial),
        // agrupados por cliente — mismo patrón que usa AcuerdosOperativosModel,
        // para saber si un Ingreso en $0 es un ajuste real pendiente o un $0
        // intencional por tarifa plana paquetizada (ej. Marfrio).
        public Dictionary<int, List<string>> TarifasGratuitasPorCliente { get; set; } = new();

        public static bool ConceptoCubiertoPorTarifaPlana(string? conceptoCompleto, List<string> conceptosGratuitos)
        {
            if (conceptosGratuitos == null || !conceptosGratuitos.Any() || string.IsNullOrWhiteSpace(conceptoCompleto)) return false;
            string baseConcepto = System.Text.RegularExpressions.Regex.Replace(conceptoCompleto, @"\s*\(U\d+\)", "").Trim().ToUpper();
            return conceptosGratuitos.Any(c => !string.IsNullOrWhiteSpace(c) &&
                (baseConcepto.Contains(c.ToUpper()) || c.ToUpper().Contains(baseConcepto)));
        }

        public IList<Operacione> Operaciones { get; set; } = default!;
        public int RebatesLegadosConDocumento { get; private set; }

        // Los rebates son exclusivamente de Collect / Full Collect.
        // Filtrar en pantalla provisiones antiguas de Prepaid mientras se corrige
        // su origen; no ocultar movimientos ya documentados ni rebates Collect.
        private void OcultarRebatesProvisionalesNoCollect(IEnumerable<Operacione> operaciones)
        {
            foreach (var op in operaciones.Where(o => o.CondicionPago is not (3 or 4)))
            {
                var rebateLegacy = op.TransaccionesFinancieras
                    .Where(t => (t.Concepto ?? "").StartsWith("Rebate Marítimo", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var tx in rebateLegacy)
                {
                    bool provisional = tx.EstadoFila is "PROVISIÓN" or "PENDIENTE VALORIZAR" or "NO FACTURABLE";
                    bool sinDocumento = string.IsNullOrWhiteSpace(tx.NumeroDocumento)
                        && string.IsNullOrWhiteSpace(tx.NumeroOrdenCompra)
                        && string.IsNullOrWhiteSpace(tx.RutaFactura)
                        && string.IsNullOrWhiteSpace(tx.RutaComprobante);
                    if (provisional && sinDocumento && !tx.TarifaManual)
                        op.TransaccionesFinancieras.Remove(tx);
                    else RebatesLegadosConDocumento++;
                }
            }
        }


        // Equivalencia en CLP (oct-2026): dólar observado y UF del día (Banco Central, con
        // respaldo si no responde). Solo para mostrar referencias y totales consolidados;
        // no cambia ningún monto guardado.
        public decimal DolarHoy { get; set; }
        public decimal UfHoy { get; set; }
        public AtilsonCargoSpa.Services.IndicadorValor? DolarInfo { get; set; }
        public AtilsonCargoSpa.Services.IndicadorValor? UfInfo { get; set; }

        // Nombre de cada agencia de aduanas (por Id), para el proveedor de las filas
        // documentales cuando la navegación no viene cargada.
        public Dictionary<int, string> NombresAgencias { get; set; } = new();

        // Proveedores del maestro para "Agregar concepto" (nov-2026).
        public List<(int Id, string Nombre)> ProveedoresLista { get; set; } = new();

        // Traspaso Ops -> Finanzas (oct-2026): filas que cambiaron entre la finalización
        // anterior y la última (solo bookings finalizados más de una vez).
        public Dictionary<int, List<string>> CambiosTraspaso { get; set; } = new();
        /// <summary>Respaldo adjunto a la última reapertura de cada operación reabierta.</summary>
        public Dictionary<int, (string Ruta, string Nombre)> RespaldosReapertura { get; set; } = new();

        // Filtros de Control Financiero. Se aplican antes de los Include para no
        // cargar operaciones que el usuario no está mirando.
        [BindProperty(SupportsGet = true)] public string? FiltroBooking { get; set; }
        [BindProperty(SupportsGet = true)] public int? FiltroClienteId { get; set; }
        [BindProperty(SupportsGet = true)] public int? FiltroNavieraId { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroNave { get; set; }
        public List<string> FiltroNaves { get; set; } = new();
        [BindProperty(SupportsGet = true)] public string? FiltroServicio { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroProveedor { get; set; }
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
        public const int TamanoPagina = 20;
        public int TotalOperaciones { get; set; }
        public int TotalPaginas { get; set; }

        public List<Cliente> FiltroClientes { get; set; } = new();
        public List<Naviera> FiltroNavieras { get; set; } = new();

        private static string NormalizarServicio(string? s)
        {
            string v = (s ?? "").Trim().ToUpperInvariant()
                .Replace("Í", "I").Replace("Ó", "O").Replace("É", "E").Replace("Á", "A").Replace("Ú", "U");
            if (v.Contains("MARIT")) return "MARITIMO";
            if (v.Contains("TERREST") || v.Contains("TRANSP")) return "TRANSPORTE";
            if (v.Contains("DOC")) return "DOCUMENTAL";
            if (v.Contains("GATE") || v.Contains("DEPOS")) return "GATE";
            if (v.Contains("ALMAC") || v.Contains("BODEGA") || v.Contains("PATIO")) return "ALMACENAMIENTO";
            return string.IsNullOrWhiteSpace(v) ? "" : "OTROS";
        }

        public bool ServicioVisible(string? categoria)
            => string.IsNullOrWhiteSpace(FiltroServicio) ||
               NormalizarServicio(categoria) == NormalizarServicio(FiltroServicio);

        // ?abrir=ID desde el Main Board: despliega ese booking al cargar.
        [BindProperty(SupportsGet = true)]
        public int? Abrir { get; set; }

        public async Task<IActionResult> OnPostGuardarComponentesGateAsync(int transaccionId, List<string> componentes, string todos)
        {
            var tx = await _context.TransaccionesFinancieras.FindAsync(transaccionId);
            if (tx == null) return RedirectToPage("./ControlFinanciero");

            if (tx.EstadoFila == "PAGADO" || tx.EstadoFila == "CONCILIADO")
            {
                TempData["ErrorMsg"] = "No se pueden cambiar los componentes de un Gate ya pagado.";
                return RedirectToPage("./ControlFinanciero");
            }
            if (componentes == null || !componentes.Any())
            {
                TempData["ErrorMsg"] = "Debe quedar al menos un componente de Gate aplicado.";
                return RedirectToPage("./ControlFinanciero");
            }

            var excluidos = (todos ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(c => !componentes.Contains(c)).ToList();
            string? valor = excluidos.Any() ? string.Join(",", excluidos) : null;

            // Costo (EGRESO) e ingreso (INGRESO) del mismo Gate comparten exenciones
            var hermanas = await _context.TransaccionesFinancieras
                .Where(t => t.IdOperacion == tx.IdOperacion && t.Concepto == tx.Concepto &&
                            (t.GrupoCobro == "Depósito" || t.GrupoCobro == "Deposito"))
                .ToListAsync();
            foreach (var h in hermanas) { h.ComponentesGateExcluidos = valor; h.FechaModificacion = DateTime.Now; h.UsuarioModificador = User.Identity?.Name ?? "Finanzas"; }
            await _context.SaveChangesAsync();

            await _sync.SincronizarAsync(tx.IdOperacion, User.Identity?.Name ?? "Finanzas");
            return RedirectToPage("./ControlFinanciero");
        }
        public async Task OnGetAsync()
        {
            if (HttpContext.RequestServices.GetService(typeof(AtilsonCargoSpa.Services.IndicadoresEconomicosService)) is AtilsonCargoSpa.Services.IndicadoresEconomicosService ind)
            {
                // No esperar 8-20 s por Banco Central/APIs al cambiar de pestaña.
                // IndicadoresActualizador actualiza dólar y UF en segundo plano.
                DolarInfo = ind.ObtenerDisponibleSinRed(AtilsonCargoSpa.Services.IndicadoresEconomicosService.Dolar);
                UfInfo = ind.ObtenerDisponibleSinRed(AtilsonCargoSpa.Services.IndicadoresEconomicosService.Uf);
                // No mostrar equivalencias CLP calculadas con un dato guardado antiguo.
                // Conservamos la fuente/fecha para el indicador visual.
                bool Vigente(AtilsonCargoSpa.Services.IndicadorValor dato) =>
                    dato.Valor > 0m && dato.Fecha.HasValue &&
                    dato.Fecha.Value.Date >= DateTime.Today.AddDays(-7);
                DolarHoy = Vigente(DolarInfo) ? DolarInfo.Valor : 0m;
                UfHoy = Vigente(UfInfo) ? UfInfo.Valor : 0m;
            }

            FiltroClientes = await _context.Clientes.AsNoTracking()
                .Where(x => !x.IsDeleted && x.Activo == 1)
                .OrderBy(x => x.RazonSocial)
                .ToListAsync();
            FiltroNavieras = await _context.Navieras.AsNoTracking()
                .Where(x => !x.IsDeleted && x.Activo == 1)
                .OrderBy(x => x.NombreNaviera)
                .ToListAsync();

            FiltroNaves = await AtilsonCargoSpa.Helpers.FiltroNaveHelper.OpcionesAsync(_context);

            var query = _context.Operaciones.AsNoTracking().Where(o => !o.IsDeleted);

            if (!string.IsNullOrWhiteSpace(FiltroBooking))
            {
                string booking = FiltroBooking.Trim();
                query = query.Where(o => o.NumeroBooking != null && o.NumeroBooking.Contains(booking));
            }
            if (FiltroClienteId.HasValue)
                query = query.Where(o => o.IdCliente == FiltroClienteId.Value);
            if (FiltroNavieraId.HasValue)
                query = query.Where(o => o.IdNaviera == FiltroNavieraId.Value);
            string? naveFiltro = AtilsonCargoSpa.Helpers.FiltroNaveHelper.Normalizar(FiltroNave);
            if (naveFiltro != null)
                query = query.Where(o => o.Nave != null && o.Nave.Contains(naveFiltro));

            if (!string.IsNullOrWhiteSpace(FiltroServicio))
            {
                string servicio = NormalizarServicio(FiltroServicio);
                if (servicio == "MARITIMO")
                    query = query.Where(o => o.TransaccionesFinancieras.Any(t => t.GrupoCobro == "Marítimo" || t.GrupoCobro == "Maritimo"));
                else if (servicio == "TRANSPORTE")
                    query = query.Where(o => o.TransaccionesFinancieras.Any(t => t.GrupoCobro == "Transporte" || t.GrupoCobro == "Terrestre"));
                else if (servicio == "DOCUMENTAL")
                    query = query.Where(o => o.TransaccionesFinancieras.Any(t => t.GrupoCobro == "Documental" || t.GrupoCobro == "Aduana"));
                else if (servicio == "GATE")
                    query = query.Where(o => o.TransaccionesFinancieras.Any(t => t.GrupoCobro == "Depósito" || t.GrupoCobro == "Deposito" || t.GrupoCobro == "Gate-Out/In"));
                else if (servicio == "ALMACENAMIENTO")
                    query = query.Where(o => o.TransaccionesFinancieras.Any(t => t.GrupoCobro == "Almacenamiento" || t.GrupoCobro == "Bodega" || t.GrupoCobro == "Patio"));
                else
                    query = query.Where(o => o.TransaccionesFinancieras.Any(t =>
                        t.GrupoCobro != "Marítimo" && t.GrupoCobro != "Maritimo" &&
                        t.GrupoCobro != "Transporte" && t.GrupoCobro != "Terrestre" &&
                        t.GrupoCobro != "Documental" && t.GrupoCobro != "Aduana" &&
                        t.GrupoCobro != "Depósito" && t.GrupoCobro != "Deposito" && t.GrupoCobro != "Gate-Out/In" &&
                        t.GrupoCobro != "Almacenamiento" && t.GrupoCobro != "Bodega" && t.GrupoCobro != "Patio"));
            }

            if (!string.IsNullOrWhiteSpace(FiltroProveedor))
            {
                string proveedor = FiltroProveedor.Trim();
                query = query.Where(o =>
                    o.TransaccionesFinancieras.Any(t =>
                        (t.IdProveedorNavigation != null && t.IdProveedorNavigation.NombreProveedor.Contains(proveedor)) ||
                        (t.ContraparteHistorica != null && t.ContraparteHistorica.Contains(proveedor))) ||
                    // Mismo criterio que Pagos: naviera del booking y agencia vinculada por Id.
                    (o.IdNavieraNavigation != null && o.IdNavieraNavigation.NombreNaviera.Contains(proveedor)) ||
                    o.OperacionesTerrestres.Any(t => t.EmpresaTransporte != null && t.EmpresaTransporte.Contains(proveedor)) ||
                    o.OperacionesDocumentales.Any(d =>
                        (d.IdAgenciaAduanaNavigation != null && d.IdAgenciaAduanaNavigation.NombreAgencia.Contains(proveedor)) ||
                        (d.AgenciaAduana != null && d.AgenciaAduana.Contains(proveedor))) ||
                    o.OperacionesAlmacenamientos.Any(a => a.IdProveedorNavigation != null && a.IdProveedorNavigation.NombreProveedor.Contains(proveedor)));
            }

            TotalOperaciones = await query.CountAsync();
            TotalPaginas = Math.Max(1, (int)Math.Ceiling(TotalOperaciones / (double)TamanoPagina));
            Pagina = Math.Clamp(Pagina, 1, TotalPaginas);

            Operaciones = await query
                .OrderBy(o =>
                    o.EstadoTarea == TraspasoFinanzas.EstadoFinalizada && o.FechaFinalizacionTarea != null &&
                    (o.FechaVistoFinanzas == null || o.FechaVistoFinanzas < o.FechaFinalizacionTarea) ? 0 :
                    o.EstadoTarea == TraspasoFinanzas.EstadoReabierta && o.FechaReaperturaTarea != null &&
                    (o.FechaVistoFinanzas == null || o.FechaVistoFinanzas < o.FechaReaperturaTarea) ? 1 :
                    o.EstadoTarea == TraspasoFinanzas.EstadoReabierta ? 2 : 3)
                .ThenByDescending(o => o.Id)
                .Skip((Pagina - 1) * TamanoPagina)
                .Take(TamanoPagina)
                .AsSplitQuery()
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.OperacionesDocumentales).ThenInclude(d => d.IdAgenciaAduanaNavigation)
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.OperacionesAlmacenamientos).ThenInclude(a => a.IdProveedorNavigation)
                .Include(o => o.Unidadestecnicas)
                .Include(o => o.TransaccionesFinancieras).ThenInclude(t => t.IdProveedorNavigation)
                .ToListAsync();
            OcultarRebatesProvisionalesNoCollect(Operaciones);

            CambiosTraspaso = await new TraspasoFinanzasService(_context).CambiosUltimaFinalizacionAsync(
                Operaciones.Where(o => o.VecesFinalizadaTarea > 1).Select(o => o.Id));
            RespaldosReapertura = await new TraspasoFinanzasService(_context).RespaldosUltimaReaperturaAsync(
                Operaciones.Where(TraspasoFinanzas.EstaReabierta).Select(o => o.Id));

            NombresAgencias = await _context.AgenciasAduanas.AsNoTracking()
                .Where(a => a.NombreAgencia != null)
                .ToDictionaryAsync(a => a.Id, a => a.NombreAgencia!);

            ComponentesGate = await _context.TarifasGate.AsNoTracking()
                .Where(t => t.EsActiva && t.Componente != null && t.IdNaviera != null)
                .ToListAsync();

            ProveedoresLista = (await _context.Proveedores.AsNoTracking()
                    .Where(p => p.Activo == 1 && p.NombreProveedor != null)
                    .OrderBy(p => p.NombreProveedor)
                    .Select(p => new { p.Id, p.NombreProveedor })
                    .ToListAsync())
                .Select(p => (p.Id, p.NombreProveedor)).ToList();

            TarifasGratuitasPorCliente = await _context.TarifasClientes.AsNoTracking()
                .Where(t => t.EsActiva && t.EsServicioGratuito && t.IdCliente != null)
                .GroupBy(t => t.IdCliente!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(t => t.Concepto!).ToList());
        }

        public async Task<IActionResult> OnGetEgresosPartialAsync()
        {
            var operacionesEgresos = await _context.Operaciones
    .AsNoTracking()
    .AsSplitQuery()
    .Where(o => !o.IsDeleted && o.TransaccionesFinancieras.Any(t => t.TipoMovimiento == "EGRESO"))
    .Include(o => o.IdClienteNavigation)
    .Include(o => o.IdNavieraNavigation)
    .Include(o => o.OperacionesDocumentales)
    .ThenInclude(d => d.IdAgenciaAduanaNavigation)
    .Include(o => o.OperacionesTerrestres)
    .Include(o => o.OperacionesAlmacenamientos)
    .ThenInclude(a => a.IdProveedorNavigation)
    .Include(o => o.TransaccionesFinancieras)
    .ThenInclude(t => t.IdProveedorNavigation)
    .OrderByDescending(o => o.Id)
    .ToListAsync();
            OcultarRebatesProvisionalesNoCollect(operacionesEgresos);

            return Partial("_EgresosPartial", operacionesEgresos);
        }

        public async Task<IActionResult> OnPostGuardarVerificacionAgaAsync(int transaccionId, string accion, decimal? valorAga)
        {
            // nov-2026: el enlace del modelo depende de la cultura del servidor ("1234.56" →
            // 123456 en es-CL). Se relee el texto del formulario con la regla de MontoHelper.
            if (Request.HasFormContentType && Request.Form.ContainsKey("valorAga"))
                valorAga = AtilsonCargoSpa.Helpers.MontoHelper.ParseONulo(Request.Form["valorAga"]);
            var tx = await _context.TransaccionesFinancieras.FindAsync(transaccionId);
            if (tx == null) return RedirectToPage("./ControlFinanciero");

            if (tx.EstadoFila == "PAGADO" || tx.EstadoFila == "CONCILIADO")
            {
                TempData["ErrorMsg"] = "No se puede modificar la verificación de un concepto ya pagado.";
                return RedirectToPage("./ControlFinanciero");
            }

            decimal original = ValorCompararAga(tx);

            switch (accion)
            {
                case "confirmar":
                    tx.ConfirmadoAga = true;
                    tx.ValorAga = original;
                    tx.AceptaDiferenciaPago = false;
                    tx.NotaDiferenciaAga = null;
                    break;

                case "desconfirmar":
                    tx.ConfirmadoAga = false;
                    tx.ValorAga = null;
                    tx.AceptaDiferenciaPago = false;
                    tx.NotaDiferenciaAga = null;
                    break;

                case "discrepancia":
                    if (!valorAga.HasValue || valorAga.Value <= 0)
                    {
                        TempData["ErrorMsg"] = "Ingresa el valor actualizado que deseas confirmar.";
                        return RedirectToPage("./ControlFinanciero");
                    }
                    bool cambio = tx.ValorAga != valorAga.Value;
                    tx.ConfirmadoAga = true;
                    tx.ValorAga = valorAga.Value;
                    // Si el valor cambió (o ahora coincide), la justificación anterior deja de valer
                    if (cambio || Math.Abs(valorAga.Value - original) <= TolAga(tx))
                    {
                        tx.AceptaDiferenciaPago = false;
                        tx.NotaDiferenciaAga = null;
                    }
                    break;

                default:
                    return RedirectToPage("./ControlFinanciero");
            }

            tx.FechaModificacion = DateTime.Now;
            tx.UsuarioModificador = User.Identity?.Name ?? "Finanzas";
            await _context.SaveChangesAsync();
            return RedirectToPage("./ControlFinanciero");
        }

        // "¿Por qué no hay tarifa?" en un flete terrestre sin valor: corre el mismo cruce que
        // FinanzasSync y devuelve el detalle (transportista, tramo y tarifas que no calzan).
        public async Task<IActionResult> OnGetDiagnosticoTransporteAsync(int idOperacion, int? idUnidad)
        {
            var op = await _context.Operaciones.AsNoTracking()
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.Unidadestecnicas)
                .FirstOrDefaultAsync(o => o.Id == idOperacion);
            if (op == null) return new JsonResult(new { ok = false, lineas = new[] { "No se encontró la operación." } });
            var terr = op.OperacionesTerrestres.Where(t => t.IdUnidadTecnica == idUnidad).OrderBy(t => t.Id).FirstOrDefault();
            if (terr == null)
                return new JsonResult(new { ok = false, lineas = new[] { "Este contenedor no tiene datos de transporte guardados (pestaña Transporte del Detalle)." } });
            // Mismo criterio que FinanzasSync: cada contenedor con su propio Dry/Reefer.
            int tipoCargaDiag = idUnidad.HasValue
                ? (op.Unidadestecnicas.FirstOrDefault(u => u.Id == idUnidad.Value)?.IdTipoCarga ?? op.IdTipoCarga)
                : op.IdTipoCarga;
            var res = await AtilsonCargoSpa.Helpers.TarifaTransporteHelper.BuscarAsync(_context, terr, tipoCargaDiag,
                terr.DepositoRetiro, terr.PlantaCarga, terr.PuertoEntrega, DateTime.Now);
            var lineas = new List<string>();
            if (res.Tarifa != null)
            {
                lineas.Add($"Sí hay tarifa: {res.NombreProveedor}, {res.Tarifa.IdCiudadOrigenNavigation?.Nombre} → {res.Tarifa.IdCiudadPlantaNavigation?.Nombre} → {res.Tarifa.IdCiudadDestinoNavigation?.Nombre}, $ {res.Tarifa.ValorNeto:N0}.");
                lineas.Add("Usa \"Aplicar tarifa\" para recalcular este booking en Finanzas.");
            }
            lineas.AddRange(res.Diagnostico);
            return new JsonResult(new { ok = res.Tarifa != null, lineas });
        }

        // Recalcula las filas financieras de un booking (botón "Aplicar tarifa" del diagnóstico).
        public async Task<IActionResult> OnPostRecalcularOperacionAsync(int idOperacion)
        {
            await _sync.SincronizarAsync(idOperacion, User.Identity?.Name ?? "Finanzas");
            return new JsonResult(new { success = true });
        }

        // Finanzas abrió (desplegó) el booking: deja de verse como NUEVO / reabierta sin ver.
        public async Task<IActionResult> OnPostMarcarVistoTraspasoAsync(int id)
        {
            bool ok = await new TraspasoFinanzasService(_context).MarcarVistoAsync(id, User.Identity?.Name);
            return new JsonResult(new { success = ok });
        }

        public async Task<IActionResult> OnPostGuardarNumeroDocAsync(int transaccionId, string numeroDoc)
        {
            if (transaccionId > 0 && !string.IsNullOrWhiteSpace(numeroDoc))
            {
                var transaccion = await _context.TransaccionesFinancieras.FindAsync(transaccionId);
                if (transaccion != null)
                {
                    transaccion.NumeroDocumento = numeroDoc.Trim().ToUpper();
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage("./ControlFinanciero");
        }


        public async Task<IActionResult> OnPostEmitirNotaCobroAsync(int operacionId, List<int> idsExentos)
        {
            bool tienePendientes = await _context.TransaccionesFinancieras
                .AnyAsync(t => t.IdOperacion == operacionId && t.TipoMovimiento == "INGRESO" && t.MontoNeto == 0m && !t.TarifaManual && t.EstadoFila != "NO FACTURABLE" && (t.JustificacionManual == null || !t.JustificacionManual.StartsWith("Sin costo")));

            bool tieneIncompletos = await _context.TransaccionesFinancieras
                .AnyAsync(t => t.IdOperacion == operacionId && t.TipoMovimiento == "INGRESO" &&
                               string.IsNullOrWhiteSpace(t.NumeroDocumentoDocumental) &&
                               (t.Concepto.Contains("Origen") || t.Concepto.Contains("Fitosanitario") || t.Concepto.Contains("Sanitario") || t.Concepto.Contains("Captura")));

            if (tienePendientes || tieneIncompletos)
            {
                TempData["ErrorMsg"] = "Bloqueo de seguridad: Existen montos sin asignar o certificados INCOMPLETOS. Operaciones debe ingresar el Número de Documento.";
                return RedirectToPage("./ControlFinanciero");
            }

            if (idsExentos == null || !idsExentos.Any()) return RedirectToPage("./ControlFinanciero");

            var ultimasNc = await _context.TransaccionesFinancieras
                .Where(t => t.NumeroDocumento != null && t.NumeroDocumento.StartsWith("NC-"))
                .Select(t => t.NumeroDocumento)
                .ToListAsync();

            int maxCorrelativo = 3500;
            foreach (var nc in ultimasNc)
            {
                if (int.TryParse(nc.Replace("NC-", ""), out int num))
                {
                    if (num > maxCorrelativo) maxCorrelativo = num;
                }
            }

            string nuevoFolio = $"NC-{maxCorrelativo + 1}";

            var transacciones = await _context.TransaccionesFinancieras
                .Where(t => idsExentos.Contains(t.Id) && t.IdOperacion == operacionId)
                .ToListAsync();

            foreach (var tx in transacciones) tx.NumeroDocumento = nuevoFolio;
            await _context.SaveChangesAsync();

            return RedirectToPage("/Finanzas/NotaCobro", new { operacionId = operacionId, numeroDoc = nuevoFolio });
        }

        // --- BOTÓN 1: SUBIR FACTURA PROVEEDOR (PDF) ---
        public async Task<IActionResult> OnPostSubirFacturaAsync(int transaccionId, IFormFile archivoFactura)
        {
            if (archivoFactura != null && archivoFactura.Length > 0)
            {
                var transaccion = await _context.TransaccionesFinancieras
                    .Include(t => t.IdOperacionNavigation)
                    .FirstOrDefaultAsync(t => t.Id == transaccionId);

                if (transaccion != null)
                {
                    string safeBookingName = string.Join("_", (transaccion.IdOperacionNavigation?.NumeroBooking ?? "SIN-BOOKING").Split(Path.GetInvalidFileNameChars()));
                    string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "facturas", safeBookingName);

                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    string uniqueFileName = $"FACT_{Guid.NewGuid().ToString().Substring(0, 8)}_{archivoFactura.FileName}";
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await archivoFactura.CopyToAsync(fileStream);
                    }

                    // Se guarda el archivo visual pero NO cambia el estado a PAGADO
                    transaccion.RutaFactura = $"/uploads/facturas/{safeBookingName}/{uniqueFileName}";
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage("./ControlFinanciero");
        }

        public async Task<IActionResult> OnPostGuardarAceptaDiferenciaAsync(int transaccionId, bool aceptaDiferencia, string? notaDiferenciaAga)
        {
            if (transaccionId > 0)
            {
                if (aceptaDiferencia && string.IsNullOrWhiteSpace(notaDiferenciaAga))
                {
                    TempData["ErrorMsg"] = "Debes explicar el motivo de la diferencia antes de confirmarla.";
                    return RedirectToPage("./ControlFinanciero");
                }

                var tx = await _context.TransaccionesFinancieras.FindAsync(transaccionId);
                if (tx != null)
                {
                    tx.AceptaDiferenciaPago = aceptaDiferencia;
                    tx.NotaDiferenciaAga = aceptaDiferencia ? notaDiferenciaAga!.Trim() : null;
                    tx.FechaModificacion = DateTime.Now;
                    tx.UsuarioModificador = User.Identity?.Name ?? "Finanzas";
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage("./ControlFinanciero");
        }

        // Compatibilidad con pestañas antiguas: este endpoint ya NO cambia estados ni guarda pagos.
        // La única fuente de verdad para registrar un pago es /Finanzas/Pagos.
        public async Task<IActionResult> OnPostSubirComprobanteAsync(int transaccionId, IFormFile? archivoRespaldo)
        {
            string? booking = await _context.TransaccionesFinancieras.AsNoTracking()
                .Where(t => t.Id == transaccionId)
                .Select(t => t.IdOperacionNavigation.NumeroBooking)
                .FirstOrDefaultAsync();

            TempData["ErrorMsg"] = "Los pagos se registran únicamente desde la pestaña Pagos.";
            return RedirectToPage("./Pagos", new { Booking = booking });
        }

        // ---------------------------------------------------------------------
        // Documento de respaldo del proveedor de origen (nov-2026, pedido de Cristian).
        // En Egresos documentales la agencia intermediaria (ej. Stembex) cobra con su Nota de
        // Cobro (N° de documento de la fila) y adjunta la factura original del terminal (ej. TPS,
        // STI). Esa factura se anota aquí como respaldo. Si la misma factura del terminal ya
        // está registrada en otro egreso se avisa dónde, antes de guardar (puede ser un cobro
        // duplicado del terminal o de la agencia).
        // ---------------------------------------------------------------------
        public async Task<IActionResult> OnPostGuardarRespaldoOrigenAsync(string ids, string? proveedorOrigen, string? numeroOrigen, bool confirmar = false,
            IFormFile? archivoOrigen = null)
        {
            var listaIds = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();
            if (listaIds.Count == 0) return new JsonResult(new { success = false, message = "No se indicó la fila." });

            var txs = await _context.TransaccionesFinancieras.Where(t => listaIds.Contains(t.Id)).ToListAsync();
            if (txs.Count != listaIds.Count || txs.Any(t => t.TipoMovimiento != "EGRESO"))
                return new JsonResult(new { success = false, message = "Solo se puede registrar en filas de Egresos." });
            var idsOps = txs.Select(t => t.IdOperacion).Distinct().ToList();
            if (await _context.Operaciones.AnyAsync(o => idsOps.Contains(o.Id) && o.LockFinanzas))
                return new JsonResult(new { success = false, message = "La operación está liquidada y bloqueada en Finanzas." });

            string? prov = string.IsNullOrWhiteSpace(proveedorOrigen) ? null : proveedorOrigen.Trim().ToUpperInvariant();
            string? num = string.IsNullOrWhiteSpace(numeroOrigen) ? null : numeroOrigen.Trim().ToUpperInvariant();
            if (prov != null && prov.Length > 150) prov = prov[..150];
            if (num != null && num.Length > 100) num = num[..100];
            // 12-nov: la factura indirecta («N° Fact Ind») se registra en la misma celda que el N° Fact,
            // solo con su número; el proveedor de origen pasa a ser opcional (se conserva si ya estaba).
            if (archivoOrigen != null && archivoOrigen.Length > 0 && num == null)
                return new JsonResult(new { success = false, message = "Indica el N° de la factura de origen para adjuntar su archivo." });
            string? errorArchivo = ValidarArchivoFactura(archivoOrigen);
            if (errorArchivo != null) return new JsonResult(new { success = false, message = errorArchivo });

            if (num != null && !confirmar)
            {
                var repetidos = await ConceptoExtraordinarioHelper.RespaldosRepetidosAsync(_context, num, prov, listaIds);
                if (repetidos.Count > 0)
                    return new JsonResult(new { success = false, duplicado = true, detalle = repetidos });
            }

            // El archivo se guarda una vez y vale para todas las filas (×N comparten la factura).
            string? rutaOrigen = null;
            if (archivoOrigen != null && archivoOrigen.Length > 0)
            {
                string booking = await _context.Operaciones.Where(o => o.Id == txs[0].IdOperacion).Select(o => o.NumeroBooking).FirstOrDefaultAsync() ?? "";
                rutaOrigen = await GuardarArchivoFacturaAsync(archivoOrigen, booking, "ORIG");
            }

            foreach (var t in txs)
            {
                t.ProveedorOrigen = num == null ? null : prov;
                t.NumeroDocumentoOrigen = num;
                // Sin N° ya no hay respaldo: se suelta su archivo (queda en el disco, no se borra).
                if (num == null) t.RutaFacturaOrigen = null;
                else if (rutaOrigen != null) t.RutaFacturaOrigen = rutaOrigen;
                t.FechaModificacion = DateTime.Now;
                t.UsuarioModificador = User.Identity?.Name ?? "Finanzas";
            }
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        // PDF de factura para una fila agrupada ×N (un contenedor por línea): el mismo archivo se
        // asigna a las líneas del grupo que todavía no lo tienen, como hace la subida individual.
        public async Task<IActionResult> OnPostSubirFacturaGrupoAsync(string ids, IFormFile? archivoFactura)
        {
            var listaIds = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();
            if (listaIds.Count == 0) return new JsonResult(new { success = false, message = "No se indicó la fila." });
            if (archivoFactura == null || archivoFactura.Length == 0) return new JsonResult(new { success = false, message = "No llegó el archivo." });
            string? errorArchivo = ValidarArchivoFactura(archivoFactura);
            if (errorArchivo != null) return new JsonResult(new { success = false, message = errorArchivo });

            var txs = await _context.TransaccionesFinancieras.Where(t => listaIds.Contains(t.Id)).ToListAsync();
            if (txs.Count != listaIds.Count || txs.Select(t => t.IdOperacion).Distinct().Count() != 1)
                return new JsonResult(new { success = false, message = "Las filas no corresponden a un mismo booking." });
            var pendientes = txs.Where(t => string.IsNullOrEmpty(t.RutaFactura)).ToList();
            if (pendientes.Count == 0) return new JsonResult(new { success = false, message = "Todas las líneas ya tienen su factura." });

            string booking = await _context.Operaciones.Where(o => o.Id == txs[0].IdOperacion).Select(o => o.NumeroBooking).FirstOrDefaultAsync() ?? "";
            string ruta = await GuardarArchivoFacturaAsync(archivoFactura, booking, "FACT");
            foreach (var t in pendientes) t.RutaFactura = ruta;
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true, lineas = pendientes.Count });
        }

        // 13-nov: cambiar el N° de una factura de una fila ×N, o quitarla de sus contenedores (vuelven a
        // quedar pendientes de factura con el PDF desvinculado). No se toca nada ya pagado.
        public async Task<IActionResult> OnPostEditarFacturaGrupoAsync(string ids, string? numeroDoc, bool quitar = false)
        {
            var listaIds = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();
            if (listaIds.Count == 0) return new JsonResult(new { success = false, message = "No se indicó la factura." });
            string? numero = string.IsNullOrWhiteSpace(numeroDoc) ? null : numeroDoc.Trim().ToUpperInvariant();
            if (!quitar && numero == null) return new JsonResult(new { success = false, message = "Ingresa el N° de factura." });
            if (numero?.Length > 50) return new JsonResult(new { success = false, message = "El N° de factura es demasiado largo." });

            var txs = await _context.TransaccionesFinancieras.Where(t => listaIds.Contains(t.Id)).ToListAsync();
            if (txs.Count != listaIds.Count || txs.Select(t => t.IdOperacion).Distinct().Count() != 1 || txs.Any(t => t.TipoMovimiento != "EGRESO"))
                return new JsonResult(new { success = false, message = "Las filas no corresponden a una misma factura de egresos." });
            if (txs.Any(t => t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO"))
                return new JsonResult(new { success = false, message = "La factura tiene contenedores ya pagados: no se puede cambiar." });

            foreach (var t in txs)
            {
                if (quitar) { t.NumeroDocumento = null; t.RutaFactura = null; }
                else t.NumeroDocumento = numero;
                t.FechaModificacion = DateTime.Now;
                t.UsuarioModificador = User.Identity?.Name ?? "Finanzas";
            }
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        static readonly string[] ExtensionesFactura = { ".pdf", ".png", ".jpg", ".jpeg" };

        static string? ValidarArchivoFactura(IFormFile? archivo)
        {
            if (archivo == null || archivo.Length == 0) return null;
            if (!ExtensionesFactura.Contains(Path.GetExtension(archivo.FileName).ToLowerInvariant()))
                return "El archivo debe ser PDF, PNG o JPG.";
            if (archivo.Length > 15 * 1024 * 1024) return "El archivo supera los 15 MB.";
            return null;
        }

        // Misma carpeta y forma de nombre que la subida individual (OnPostSubirFacturaAsync).
        async Task<string> GuardarArchivoFacturaAsync(IFormFile archivo, string? numeroBooking, string prefijo)
        {
            string carpeta = string.Join("_", (string.IsNullOrWhiteSpace(numeroBooking) ? "SIN-BOOKING" : numeroBooking).Split(Path.GetInvalidFileNameChars()));
            string destino = Path.Combine(_env.WebRootPath, "uploads", "facturas", carpeta);
            if (!Directory.Exists(destino)) Directory.CreateDirectory(destino);
            string nombre = string.Join("_", Path.GetFileName(archivo.FileName).Split(Path.GetInvalidFileNameChars()));
            string unico = $"{prefijo}_{Guid.NewGuid().ToString().Substring(0, 8)}_{nombre}";
            using (var fs = new FileStream(Path.Combine(destino, unico), FileMode.Create))
                await archivo.CopyToAsync(fs);
            return $"/uploads/facturas/{carpeta}/{unico}";
        }

        // ---------------------------------------------------------------------
        // Concepto extraordinario (nov-2026): costo o cobro que no sale de las tarifas, agregado
        // a mano en el detalle del booking. Impacta la utilidad del booking (pestaña Resumen) y
        // la sincronización de Finanzas no lo toca (ver ConceptoExtraordinarioHelper).
        // ---------------------------------------------------------------------
        public async Task<IActionResult> OnPostAgregarConceptoExtraAsync(int idOperacion, string? tipo, string? seccion, string? concepto,
            int? idProveedor, string? proveedorTexto, string? monto, string? moneda, string? iva, string? documentos,
            string? proveedorOrigen, string? numeroOrigen, bool confirmar = false,
            IFormFile? archivoDocumento = null, IFormFile? archivoOrigen = null)
        {
            var op = await _context.Operaciones.FirstOrDefaultAsync(o => o.Id == idOperacion && !o.IsDeleted);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });
            if (op.LockFinanzas) return new JsonResult(new { success = false, message = "La operación está liquidada y bloqueada en Finanzas." });
            string? errorArchivo = ValidarArchivoFactura(archivoDocumento) ?? ValidarArchivoFactura(archivoOrigen);
            if (errorArchivo != null) return new JsonResult(new { success = false, message = errorArchivo });
            if (archivoOrigen != null && archivoOrigen.Length > 0 && string.IsNullOrWhiteSpace(numeroOrigen))
                return new JsonResult(new { success = false, message = "Indica el N° de la factura de origen para adjuntar su archivo." });

            var r = await ConceptoExtraordinarioServicio.CrearAsync(_context, op, new ConceptoExtraordinarioServicio.Datos(
                tipo, seccion, concepto, idProveedor, proveedorTexto, monto, moneda, iva, documentos, proveedorOrigen, numeroOrigen),
                "Control Financiero", User.Identity?.Name, confirmar);
            if (!r.Ok) return new JsonResult(new { success = false, message = r.Mensaje, duplicado = r.Repetidos.Count > 0, detalle = r.Repetidos });
            // Los archivos se guardan solo cuando el concepto ya pasó todas las validaciones.
            if (r.Linea != null && archivoDocumento != null && archivoDocumento.Length > 0)
                r.Linea.RutaFactura = await GuardarArchivoFacturaAsync(archivoDocumento, op.NumeroBooking, "FACT");
            if (r.Linea != null && archivoOrigen != null && archivoOrigen.Length > 0 && r.Linea.TipoMovimiento == "EGRESO")
                r.Linea.RutaFacturaOrigen = await GuardarArchivoFacturaAsync(archivoOrigen, op.NumeroBooking, "ORIG");
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostEliminarConceptoExtraAsync(int transaccionId)
        {
            var tx = await _context.TransaccionesFinancieras.Include(t => t.IdOperacionNavigation).FirstOrDefaultAsync(t => t.Id == transaccionId);
            if (tx == null || !ConceptoExtraordinarioHelper.Es(tx))
                return new JsonResult(new { success = false, message = "Solo se pueden quitar conceptos extraordinarios." });
            if (tx.IdOperacionNavigation.LockFinanzas)
                return new JsonResult(new { success = false, message = "La operación está liquidada y bloqueada en Finanzas." });
            if (tx.EstadoFila is "PAGADO" or "CONCILIADO" || !string.IsNullOrWhiteSpace(tx.RutaComprobante))
                return new JsonResult(new { success = false, message = "Ya tiene un pago registrado: no se puede quitar." });
            _context.TransaccionesFinancieras.Remove(tx);
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }
    }
}