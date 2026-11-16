using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using AtilsonCargoSpa.Helpers;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace AtilsonCargoSpa.Pages.Comercial
{
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly FinanzasSyncService _finanzasSync;

        public IndexModel(AtilsonContext context, IWebHostEnvironment env, FinanzasSyncService finanzasSync)
        {
            _context = context;
            _env = env;
            _finanzasSync = finanzasSync;
        }

        // =====================================================================================
        // Tarifas de transporte (13-nov-2026, minuta de roles): quien tiene el permiso por persona
        // "Tarifas de transporte" sin rol comercial ve solo Proveedores › Terrestre (con el
        // comparativo, alta, edición, historial y carga masiva de costos terrestres). En el
        // servidor solo se aceptan esos handlers; el resto responde 403.
        // =====================================================================================
        public bool SoloTransporte => PermisosEquipo.SoloTarifasTransporte(User);

        private static readonly HashSet<string> HandlersTransporte = new(StringComparer.OrdinalIgnoreCase)
        {
            "", "Historial", "FilaTarifa", "GuardarFilaTarifa", "CrearCostoTerrestre", "EditarCostoTerrestre",
            "EliminarCostoTerrestre", "PlantillaCostos", "CargaMasivaCostos", "CrearProveedor", "CrearConductor"
        };

        public override async Task OnPageHandlerExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context,
            Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutionDelegate next)
        {
            if (SoloTransporte)
            {
                string handler = context.HandlerMethod?.Name ?? "";
                bool permitido = HandlersTransporte.Contains(handler);
                if (permitido && handler is "Historial" or "FilaTarifa" or "GuardarFilaTarifa")
                {
                    var req = context.HttpContext.Request;
                    string? tipo = req.Query["tipoEntidad"].FirstOrDefault()
                        ?? (req.HasFormContentType ? req.Form["tipoEntidad"].FirstOrDefault() : null);
                    permitido = string.Equals(tipo, "Terrestre", StringComparison.OrdinalIgnoreCase);
                }
                if (!permitido)
                {
                    context.Result = Forbid();
                    return;
                }
            }
            await next();
        }

        // ===== Utilidades de formularios (oct-2026) =====
        // Los <input type=number> envían el monto con punto decimal ("75.50"). Si el
        // servidor corre en es-CL, el binder lo interpreta con coma decimal y guarda
        // 7550. Esto re-lee cada decimal del formulario de forma independiente de la
        // cultura (acepta también "1.234,5" y "1234,5" si se escribió a mano).
        // Trámites documentales que Control Financiero busca por nombre en las tarifas de
        // agencia (clave que debe contener el concepto → nombre sugerido).
        private static readonly (string Clave, string Nombre)[] ConceptosDocumentalesFinanzas =
        {
            ("HONORARIOS AGENCIA", "HONORARIOS AGENCIA DE ADUANAS"),
            ("DUS EMBARQUES NORMALES", "LEGALIZACIÓN DUS EMBARQUES NORMALES"),
            ("DIN", "LEGALIZACIÓN DIN"),
            ("CERTIFICADO ORIGEN", "CERTIFICADO ORIGEN"),
            ("RE-EMISION CERTIFICADOS ORIGEN", "RE-EMISION CERTIFICADOS ORIGEN"),
            ("FITO", "CERTIFICADO FITOSANITARIO"),
            ("RE-EMISION CERTIFICADOS SAG", "RE-EMISION CERTIFICADOS SAG"),
            ("CERTIFICADO SANITARIO", "CERTIFICADO SANITARIO"),
            ("CERTIFICADO CAPTURA SERNAPESCA", "CERTIFICADO CAPTURA SERNAPESCA"),
            ("RE-EMISION", "RE-EMISION CERTIFICADOS SERNAPESCA"),
            ("CODAUT", "TRÁMITE CODAUT"),
            ("CLAVE", "TRÁMITE CLAVE"),
            ("NEPPEX", "TRÁMITE NEPPEX"),
            ("ACLARACIÓN", "ACLARACIÓN DOCUMENTAL"),
            ("CANCELACIÓN", "CANCELACIÓN DOCUMENTAL"),
            ("ROLEO DE CARGA", "ROLEO DE CARGA"),
            ("GENERACIÓN IVV", "GENERACIÓN IVV"),
        };

        // Montos a la chilena ("450.000", "1.200,75") o con punto decimal: ver MontoHelper.
        private static bool TryParseMonto(string? texto, out decimal valor) => AtilsonCargoSpa.Helpers.MontoHelper.TryParse(texto, out valor);

        private void ReparsearDecimales(object modelo, string prefijo)
        {
            foreach (var prop in modelo.GetType().GetProperties().Where(pp => pp.CanWrite && (pp.PropertyType == typeof(decimal) || pp.PropertyType == typeof(decimal?))))
            {
                string clave = $"{prefijo}.{prop.Name}";
                if (!Request.HasFormContentType || !Request.Form.ContainsKey(clave)) continue;
                string texto = Request.Form[clave].ToString();
                if (string.IsNullOrWhiteSpace(texto))
                {
                    if (prop.PropertyType == typeof(decimal?)) prop.SetValue(modelo, null);
                    continue;
                }
                if (TryParseMonto(texto, out var valor)) prop.SetValue(modelo, valor);
            }
        }

        // Deja lista una tarifa de VENTA a cliente antes de guardarla:
        //  - montos leídos sin depender de la cultura,
        //  - vigencia desde hoy si el formulario no la trae (antes quedaba en el año 1
        //    y SQL Server rechazaba el guardado: era el "error SQL" al crear ventas),
        //  - concepto automático para Marítimo/Terrestre (se quitó "Condición / Concepto").
        private void PrepararVentaCliente(TarifasCliente t, string grupo, string prefijo, string? conceptoPorDefecto = null)
        {
            t.GrupoCobro = grupo;
            t.EsActiva = true;
            t.UsuarioCreador = User.Identity?.Name ?? "Comercial";
            ReparsearDecimales(t, prefijo);
            if (t.FechaInicioVigencia.Year < 2000) t.FechaInicioVigencia = DateTime.Today;
            if (t.FechaFinVigencia.Year < 2000) t.FechaFinVigencia = DateTime.Today.AddYears(1);
            if (!string.IsNullOrWhiteSpace(conceptoPorDefecto) && string.IsNullOrWhiteSpace(t.Concepto)) t.Concepto = conceptoPorDefecto;
            t.Concepto = string.IsNullOrWhiteSpace(t.Concepto) ? null : t.Concepto.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(t.Moneda)) t.Moneda = "CLP";
        }

        // Cuando Comercial agrega/edita una tarifa, cualquier operación que estaba
        // "PENDIENTE VALORIZAR" para ese mismo grupo debe re-sincronizarse para que
        // la pestaña bloqueada en Operaciones se desbloquee sin esperar al próximo
        // guardado manual de esa operación.
        private async Task ResincronizarPendientesAsync(string grupoCobro)
        {
            var idsOperaciones = await _context.TransaccionesFinancieras
                .Where(t => t.GrupoCobro == grupoCobro && t.EstadoFila == "PENDIENTE VALORIZAR")
                .Select(t => t.IdOperacion)
                .Distinct()
                .ToListAsync();

            string usuario = User.Identity?.Name ?? "Comercial";
            foreach (var idOp in idsOperaciones)
            {
                await _finanzasSync.SincronizarAsync(idOp, usuario);
            }
        }

        // Una venta nueva al cliente se refleja en los Ingresos de sus bookings que
        // todavía están en provisión (los facturados o ajustados a mano no se tocan:
        // GenerarEspejo respeta TarifaManual). grupoFinanzas es el GrupoCobro de
        // TransaccionesFinancieras: Marítimo, Transporte, Depósito, Documental...
        private async Task ResincronizarVentasClienteAsync(int? idCliente, string grupoFinanzas)
        {
            if (idCliente is null or <= 0) { await ResincronizarPendientesAsync(grupoFinanzas); return; }
            var idsOperaciones = await _context.TransaccionesFinancieras
                .Where(t => t.GrupoCobro == grupoFinanzas && t.TipoMovimiento == "INGRESO"
                         && (t.EstadoFila == "PROVISIÓN" || t.EstadoFila == "PENDIENTE VALORIZAR")
                         && _context.Operaciones.Any(o => o.Id == t.IdOperacion && o.IdCliente == idCliente && !o.IsDeleted))
                .Select(t => t.IdOperacion)
                .Distinct()
                .ToListAsync();

            string usuario = User.Identity?.Name ?? "Comercial";
            foreach (var idOp in idsOperaciones)
            {
                await _finanzasSync.SincronizarAsync(idOp, usuario);
            }
            await ResincronizarPendientesAsync(grupoFinanzas);
        }

        // Resincroniza TODOS los bookings activos de un cliente — usado cuando cambia
        // el checkbox "Servicio Gratuito" (tarifa plana), porque ese cambio puede afectar
        // bookings que ya estaban valorizados (no solo los "PENDIENTE VALORIZAR").
        private async Task ResincronizarOperacionesClienteAsync(int idCliente)
        {
            var idsOperaciones = await _context.Operaciones
                .Where(o => o.IdCliente == idCliente && !o.IsDeleted)
                .Select(o => o.Id)
                .ToListAsync();

            string usuario = User.Identity?.Name ?? "Comercial";
            foreach (var idOp in idsOperaciones)
            {
                await _finanzasSync.SincronizarAsync(idOp, usuario);
            }
        }
        // ==================== LISTAS PARA TABLAS ====================
        public IList<TarifasCliente> TarifasClientes { get; set; } = default!;
        public IList<TarifasMaritima> TarifasMaritimas { get; set; } = default!;
        public IList<TarifasMaestra> TarifasMaestrasMaritimo { get; set; } = default!;
        [BindProperty] public TarifasMaestra NuevaTarifaMaritimoExtra { get; set; } = new();

        // Vista comparativa: conceptos equivalentes entre navieras (ver ConstruirComparativoRecargos).
        // No es un matcher genérico — es una lista curada a mano de los ítems que realmente
        // son comparables 1 a 1 entre navieras, emparejados por el Concepto exacto que
        // carga el script Sql/2026-10_catalogo_recargos_navieras.sql.
        public List<ComparativoRecargoRow> ComparativoRecargosMaritimo { get; set; } = new();
        public string[] NavierasComparativo { get; } = new[] { "ONE", "MSC", "Hapag-Lloyd", "Maersk" };

        public class ComparativoRecargoRow
        {
            public string Concepto { get; set; } = "";
            public Dictionary<string, TarifasMaestra?> PorNaviera { get; set; } = new();
        }

        private void ConstruirComparativoRecargos()
        {
            var mapa = new List<(string Label, Dictionary<string, string> PorNaviera)>
            {
                ("Late Arrival — Dry", new Dictionary<string, string> {
                    ["ONE"] = "Late / Early Arrival — Dry",
                    ["MSC"] = "Late Arrival — Dry",
                    ["Hapag-Lloyd"] = "Presentación Tardía en Puerto (Late Arrival)",
                    ["Maersk"] = "Late Arrival",
                }),
                ("Late Arrival — Reefer", new Dictionary<string, string> {
                    ["ONE"] = "Late / Early Arrival — Reefer",
                    ["MSC"] = "Late Arrival — Reefer",
                    ["Hapag-Lloyd"] = "Presentación Tardía en Puerto (Late Arrival)",
                    ["Maersk"] = "Late Arrival",
                }),
                ("Early Arrival", new Dictionary<string, string> {
                    ["MSC"] = "Early Arrival",
                    ["Hapag-Lloyd"] = "Presentación Temprana en Puerto (Early Arrival)",
                }),
                ("Corrección de B/L (tarifa base)", new Dictionary<string, string> {
                    ["ONE"] = "Corrección B/L Exportación (Post-Zarpe)",
                    ["MSC"] = "Corrección B/L (antes de 48h del zarpe)",
                    ["Hapag-Lloyd"] = "Corrección de B/L (hasta 48h del ETD)",
                    ["Maersk"] = "Corrección de B/L",
                }),
                ("Telex / Liberación Electrónica", new Dictionary<string, string> {
                    ["MSC"] = "Telex Release",
                    ["Maersk"] = "Liberación Electrónica",
                }),
                ("Recargo por Cancelación de Reserva", new Dictionary<string, string> {
                    ["ONE"] = "Recargo por Cancelación de Reserva (SPOT)",
                    ["Hapag-Lloyd"] = "Cargo por Cancelación de Reserva (Booking)",
                }),
            };

            ComparativoRecargosMaritimo = mapa.Select(m =>
            {
                var fila = new ComparativoRecargoRow { Concepto = m.Label };
                foreach (var naviera in NavierasComparativo)
                {
                    TarifasMaestra? match = null;
                    if (m.PorNaviera.TryGetValue(naviera, out var conceptoBuscado))
                    {
                        match = TarifasMaestrasMaritimo.FirstOrDefault(t =>
                            t.IdNavieraNavigation != null &&
                            t.IdNavieraNavigation.NombreNaviera == naviera &&
                            t.Concepto == conceptoBuscado);
                    }
                    fila.PorNaviera[naviera] = match;
                }
                return fila;
            }).ToList();
        }

        public IList<Tarifasterrestre> TarifasTerrestres { get; set; } = default!;
        public IList<TarifasMaestra> TarifasMaestrasDocumentales { get; set; } = default!;
        public IList<TarifasMaestra> TarifasMaestrasCourier { get; set; } = default!;
        public IList<TarifasDocumentale> CostosAgencias { get; set; } = default!;
        public IList<AgenciasAduana> AgenciasConCostos { get; set; } = default!;
        public IList<TarifasAlmacenamiento> TarifasAlmacenamientos { get; set; } = default!;
        public IList<TarifasAlmacenamientoDetalle> DetallesAlmacenamiento { get; set; } = default!;
        [BindProperty] public TarifasAlmacenamientoDetalle NuevoDetalleAlmacenamiento { get; set; } = new();
        public IList<TarifaGate> TarifasGate { get; set; } = default!;
        public IList<TarifaBl> TarifasBl { get; set; } = default!;
        [BindProperty] public TarifaBl NuevaTarifaBl { get; set; } = new();
        public Dictionary<int, string> NombresNavieras { get; set; } = new();
        // Pestañas "RF Fresco Collect" (oct-2026).
        public TarifasCollectPanelVm PanelCollectNaviera { get; set; } = new();
        public TarifasCollectPanelVm PanelCollectCliente { get; set; } = new();
        // ==================== MÉTRICAS (KPIs) ====================
        public int CountClientesActivas { get; set; }
        public int CountMaritimasActivas { get; set; }
        public int CountTerrestresActivas { get; set; }
        public int CountPorExpirar { get; set; }
        // ==================== FILTROS ====================
        [BindProperty(SupportsGet = true)] public int? FiltroIdCliente { get; set; }
        [BindProperty(SupportsGet = true)] public int? FiltroIdProveedor { get; set; }
        [BindProperty(SupportsGet = true)] public int? FiltroIdNaviera { get; set; }
        [BindProperty(SupportsGet = true)] public int? FiltroIdCiudadOrigen { get; set; }
        [BindProperty(SupportsGet = true)] public int? FiltroIdCiudadPlanta { get; set; }
        [BindProperty(SupportsGet = true)] public int? FiltroIdCiudadDestino { get; set; }
        [BindProperty(SupportsGet = true)] public int? FiltroIdTipoCarga { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroVigencia { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroRuta { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroPol { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroPlanta { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroPod { get; set; }
        // ==================== BINDINGS PARA FORMULARIOS ====================
        [BindProperty] public TarifasCliente NuevaVentaMaritima { get; set; } = new();
        [BindProperty] public TarifasCliente NuevaVentaTerrestre { get; set; } = new();
        [BindProperty] public TarifasCliente NuevaVentaAlmacenamiento { get; set; } = new();
        [BindProperty] public TarifasCliente NuevaVentaCourier { get; set; } = new();
        [BindProperty] public TarifasCliente NuevaVentaGate { get; set; } = new();
        [BindProperty] public TarifaGate NuevaTarifaGate { get; set; } = new();
        [BindProperty] public TarifasMaritima NuevoCostoMaritimo { get; set; } = new();
        [BindProperty] public Tarifasterrestre NuevoCostoTerrestre { get; set; } = new();
        [BindProperty] public TarifasMaestra NuevaTarifaDocumental { get; set; } = new();
        [BindProperty] public TarifasMaestra NuevaTarifaCourier { get; set; } = new();
        [BindProperty] public TarifasDocumentale NuevoCostoDocumental { get; set; } = new();
        [BindProperty] public TarifasAlmacenamiento NuevaTarifaAlmacenamiento { get; set; } = new();
        [BindProperty] public Proveedore NuevoProveedor { get; set; } = new();
        [BindProperty] public Conductore NuevoConductor { get; set; } = new();
        [BindProperty] public AgenciasAduana NuevaAgencia { get; set; } = new();
        [BindProperty] public Tarifasterrestre EditarCostoTerrestre { get; set; } = new();
        [BindProperty] public TarifasMaritima EditarCostoMaritimo { get; set; } = new();
        // ==================== SELECT LISTS ====================
        public SelectList ListaClientes { get; set; } = default!;
        public SelectList ListaProveedores { get; set; } = default!;
        // Transportistas: proveedores activos que no son depósitos (IdTipoProveedor 12).
        public SelectList ListaTransportistas { get; set; } = default!;
        public SelectList ListaCiudades { get; set; } = default!;
        public SelectList ListaNavieras { get; set; } = default!;
        // Depósitos activos en orden alfabético (formularios y filtros de Gate).
        public List<Deposito> ListaDepositos { get; set; } = new();
        // Componentes Gate archivados (checkbox desmarcado) para mostrarlos en la línea CMA.
        public IList<TarifaGate> ComponentesGateInactivos { get; set; } = new List<TarifaGate>();
        public SelectList ListaAgencias { get; set; } = default!;
        public SelectList ListaConceptosDocumentales { get; set; } = default!;
        public SelectList ListaPols { get; set; } = default!;
        public SelectList ListaPods { get; set; } = default!;
        // Catálogo maestro de Puertos (mismo origen que usa Operaciones para POL/POD),
        // para que Comercial deje de aceptar texto libre y ambos módulos compartan
        // exactamente los mismos nombres de puerto.
        public SelectList ListaPuertosMaestro { get; set; } = default!;
        // ==================== TARIFA PLANA (pestaña dedicada) ====================
        // Catálogo de conceptos Documentales ya usados por clasificación (SAG/SERNAPESCA/COMUN),
        // para la pestaña unificada "TARIFA PLANA" donde se eligen los certificados gratis de
        // las 3 clasificaciones juntas, sin mezclarlas con las tablas de detalle normales.
        public List<string> ConceptosDocSag { get; set; } = new();
        public List<string> ConceptosDocSerna { get; set; } = new();
        public List<string> ConceptosDocComun { get; set; } = new();
        // Clientes a listar en la pestaña "TARIFA PLANA": con un cliente filtrado arriba
        // (Filtro Mandante...) se muestra solo ese, aunque todavía no sea especial (para poder
        // activarlo por primera vez); sin filtro se listan los que YA la tienen activa, para
        // verlos de un vistazo sin tener que buscarlos uno por uno.
        public List<Cliente> ClientesTarifaPlanaVista { get; set; } = new();
        // Cuántos clientes tienen Tarifa Plana activa en total — se muestra como contador en
        // la pestañita, visible sin tener que entrar a mirar la lista.
        public int CountClientesTarifaPlana { get; set; }
        public async Task OnGetAsync()
        {
            TipoCargaCostos = await TipoCostosAsync();
            TipoCargaVentas = TipoVentas();
            // Fechas por defecto: ningún formulario nuevo debe abrir con 01-01-0001
            // ni obligar a Comercial a interpretar si la fecha es inicio o expiración.
            foreach (var venta in new[] { NuevaVentaMaritima, NuevaVentaTerrestre, NuevaVentaAlmacenamiento, NuevaVentaCourier, NuevaVentaGate })
            {
                venta.FechaInicioVigencia = DateTime.Today;
                venta.FechaFinVigencia = DateTime.Today.AddYears(1);
            }
            NuevoCostoMaritimo.FechaInicioVigencia = DateOnly.FromDateTime(DateTime.Today);
            NuevoCostoMaritimo.FechaFinVigencia = DateOnly.FromDateTime(DateTime.Today.AddYears(1));
            if (!NuevoCostoTerrestre.FechaInicioVigencia.HasValue)
                NuevoCostoTerrestre.FechaInicioVigencia = DateOnly.FromDateTime(DateTime.Today);
            if (!NuevoCostoTerrestre.FechaFinVigencia.HasValue)
                NuevoCostoTerrestre.FechaFinVigencia = DateOnly.FromDateTime(DateTime.Today.AddYears(1));
            if (!NuevaTarifaMaritimoExtra.FechaVigenciaDesde.HasValue)
                NuevaTarifaMaritimoExtra.FechaVigenciaDesde = DateOnly.FromDateTime(DateTime.Today);
            ListaAgencias = new SelectList(await _context.AgenciasAduanas.AsNoTracking().Where(a => a.Activo == 1).ToListAsync(), "Id", "NombreAgencia");
            AgenciasConCostos = await _context.AgenciasAduanas
    .AsNoTracking()
    .Include(a => a.TarifasDocumentales)
    .Where(a => a.Activo == 1)
    .ToListAsync();
            CostosAgencias = await _context.TarifasDocumentales
                .AsNoTracking()
                .Include(t => t.IdAgenciaAduanaNavigation)
                .Where(t => t.EsActiva == true && t.IdAgenciaAduana != null)
                .ToListAsync();
            await CargarDatosYMetricas();
        }
        private async Task CargarDatosYMetricas()
        {
            DateTime hoyDt = DateTime.Now.Date;
            DateTime limiteExpiracionDt = hoyDt.AddDays(30);
            DateTime limite90 = hoyDt.AddDays(90);
            var clientes = await _context.Clientes.AsNoTracking().Where(c => c.Activo == 1).OrderBy(c => c.RazonSocial).ToListAsync();
            ListaClientes = new SelectList(clientes.Select(c => new { Id = c.Id, RazonSocial = c.RazonSocial?.ToUpper() }), "Id", "RazonSocial");
            var proveedores = await _context.Proveedores.AsNoTracking().Where(p => p.Activo == 1).OrderBy(p => p.NombreProveedor).ToListAsync();
            ListaProveedores = new SelectList(proveedores.Select(p => new { Id = p.Id, NombreProveedor = p.NombreProveedor?.ToUpper() }), "Id", "NombreProveedor");
            ListaTransportistas = new SelectList(proveedores.Where(p => p.IdTipoProveedor != 12).Select(p => new { Id = p.Id, NombreProveedor = p.NombreProveedor?.ToUpper() }), "Id", "NombreProveedor");
            var ciudadesList = await _context.Set<Ciudade>().AsNoTracking().OrderBy(c => c.Nombre).ToListAsync();
            var ciudadesUpper = ciudadesList.Select(c => new { Id = c.Id, Nombre = c.Nombre?.ToUpper() }).ToList();
            ListaCiudades = new SelectList(ciudadesUpper, "Id", "Nombre");
            ViewData["ListaCiudadesNombre"] = new SelectList(ciudadesUpper, "Nombre", "Nombre");
            var navieras = await _context.Navieras.AsNoTracking().Where(n => n.Activo == 1).OrderBy(n => n.NombreNaviera).ToListAsync();
            ListaNavieras = new SelectList(navieras.Select(n => new { Id = n.Id, NombreNaviera = n.NombreNaviera?.ToUpper() }), "Id", "NombreNaviera");
            // Puertos maestros (misma tabla que usa Operaciones/Create)
            // por nombre para que el value guardado en TarifasMaritimas.Pol/Pod sea
            // siempre idéntico al NombrePuerto usado en el resto del sistema.
            var puertosMaestroDb = await _context.Puertos.AsNoTracking().Where(p => p.Activo == 1).OrderBy(p => p.NombrePuerto).ToListAsync();
            var puertosMaestroDedup = puertosMaestroDb.GroupBy(p => p.NombrePuerto.ToUpper()).Select(g => g.First()).ToList();
            ListaPuertosMaestro = new SelectList(puertosMaestroDedup.Select(p => new { Id = p.NombrePuerto.ToUpper(), Nombre = p.NombrePuerto.ToUpper() }), "Id", "Nombre");

            // POL de Tarifas Collect: solo puertos de Chile y Perú (filtra por país ANTES de deduplicar por nombre).
            var puertosPolCollect = puertosMaestroDb
                .Where(p => { var pais = (p.Pais ?? "").Trim().ToUpper(); return pais == "CHILE" || pais == "PERU" || pais == "PERÚ"; })
                .GroupBy(p => p.NombrePuerto.ToUpper())
                .Select(g => g.First())
                .ToList();
            ViewData["PuertosPolCollect"] = new SelectList(puertosPolCollect.Select(p => new { Id = p.NombrePuerto.ToUpper(), Nombre = p.NombrePuerto.ToUpper() }), "Id", "Nombre");
            // El catálogo maestro (TarifasMaestras, Categoria="Documental") no siempre tiene
            // todos los conceptos que en la práctica ya se usan en las ventas de clientes
            // (históricamente se tipeaban como texto libre). Unimos ambas fuentes para que el
            // dropdown de "+ NUEVA VENTA DOCUMENTAL" y el checklist de Tarifa Plana muestren
            // también los conceptos ya en uso, aunque no estén dados de alta en el catálogo.
            // Solo conceptos ACTIVOS: uno archivado (ej. una prueba que se destildó y quedó
            // archivada) no debe quedar colgado para siempre en el catálogo solo porque alguna
            // vez existió — antes no se filtraba y los conceptos de prueba archivados nunca
            // desaparecían de los dropdowns/chips.
            var conceptosDocMaestro = await _context.TarifasMaestras
                .Where(t => t.EsActiva && t.Categoria == "Documental")
                .Select(t => t.Concepto.ToUpper())
                .ToListAsync();
            // Traemos TODAS las ventas/costos Documentales activos en 2 consultas (antes eran
            // 7 consultas separadas: una por cada clasificación x fuente) y agrupamos en memoria.
            // Menos round-trips a la base = menos exposición a un timeout cuando el servidor SQL
            // está ocupado (ej. justo después de correr un script de limpieza).
            var ventasDocumentales = await _context.TarifasClientes
                .Where(t => t.GrupoCobro == "Documental" && t.EsActiva && t.Concepto != null)
                .Select(t => new { Concepto = t.Concepto!.ToUpper(), t.Clasificacion })
                .ToListAsync();
            var costosDocumentales = await _context.TarifasDocumentales
                .Where(t => t.EsActiva)
                .Select(t => new { Concepto = t.Concepto.ToUpper(), t.Clasificacion })
                .ToListAsync();
            var conceptosDoc = conceptosDocMaestro
                .Concat(ventasDocumentales.Select(v => v.Concepto))
                .Concat(costosDocumentales.Select(c => c.Concepto))
                .Distinct().ToList();
            // nov-2026: con el catálogo vacío el selector "Trámite a Tarifar" no traía nada y no
            // se podía cargar ningún costo de agencia. Se agregan los trámites que Finanzas
            // reconoce por nombre (FinanzasSyncService busca "contiene"), solo si todavía no
            // hay un concepto que ya los cubra.
            foreach (var (clave, nombre) in ConceptosDocumentalesFinanzas)
            {
                if (!conceptosDoc.Any(c => c.Contains(clave))) conceptosDoc.Add(nombre);
            }
            conceptosDoc = conceptosDoc.Distinct().OrderBy(c => c).ToList();
            ListaConceptosDocumentales = new SelectList(conceptosDoc);
            // Catálogos por clasificación para la pestaña "TARIFA PLANA": unen 2 fuentes, ambas
            // filtradas a solo activas (ya traídas arriba) —
            //   (1) lo que ya se vendió a algún cliente bajo esa clasificación (TarifasClientes), y
            //   (2) lo que alguna agencia ya tiene tarifado como COSTO bajo esa misma clasificación
            //       (TarifasDocumentales, la tabla de "+ NUEVO COSTO DOCUMENTAL") — así aparecen
            //       conceptos reales (ej. Sanitario/Captura en SERNAPESCA) aunque todavía no se le
            //       hayan vendido a ningún cliente en particular.
            ConceptosDocSag = ventasDocumentales.Where(v => v.Clasificacion == "SAG").Select(v => v.Concepto)
                .Concat(costosDocumentales.Where(c => c.Clasificacion == "SAG").Select(c => c.Concepto))
                .Distinct().OrderBy(c => c).ToList();
            ConceptosDocSerna = ventasDocumentales.Where(v => v.Clasificacion == "SERNAPESCA").Select(v => v.Concepto)
                .Concat(costosDocumentales.Where(c => c.Clasificacion == "SERNAPESCA").Select(c => c.Concepto))
                .Distinct().OrderBy(c => c).ToList();
            // ISPS y SMS (acuerdo Marfrio, minuta oct-2026) siempre disponibles para marcar si
            // van incluidos en la tarifa plana; DUS y honorarios quedan como opción a evaluar.
            // Finanzas los reconoce por nombre ("Seguridad de la Carga ISPS...", "Honorarios
            // Agencia de Aduanas", "Legalización / Emisión DUS").
            var conceptosFijosTarifaPlana = new[] { "ISPS", "SMS", "EMISIÓN DUS", "HONORARIOS AGENCIA" };
            ConceptosDocComun = ventasDocumentales.Where(v => v.Clasificacion == "COMUN").Select(v => v.Concepto)
                .Concat(costosDocumentales.Where(c => c.Clasificacion == "COMUN").Select(c => c.Concepto))
                .Concat(conceptosFijosTarifaPlana)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!.Trim().ToUpper())
                .Distinct().OrderBy(c => c).ToList();
            CountClientesTarifaPlana = await _context.Clientes.CountAsync(c => c.EsClienteEspecial && c.Activo == 1);
            if (FiltroIdCliente.HasValue)
            {
                var clienteFiltradoTp = await _context.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == FiltroIdCliente.Value);
                ClientesTarifaPlanaVista = clienteFiltradoTp != null ? new List<Cliente> { clienteFiltradoTp } : new List<Cliente>();
            }
            else
            {
                ClientesTarifaPlanaVista = await _context.Clientes.AsNoTracking()
                    .Where(c => c.EsClienteEspecial && c.Activo == 1)
                    .OrderBy(c => c.RazonSocial)
                    .ToListAsync();
            }
            var polsVentas = await _context.TarifasClientes.Where(t => t.GrupoCobro == "Marítimo" && !string.IsNullOrEmpty(t.Pol)).Select(t => t.Pol.ToUpper()).ToListAsync();
            var polsCostos = await _context.TarifasMaritimas.Where(t => !string.IsNullOrEmpty(t.Pol)).Select(t => t.Pol.ToUpper()).ToListAsync();
            ListaPols = new SelectList(polsVentas.Concat(polsCostos).Distinct().OrderBy(p => p).ToList());
            var podsVentas = await _context.TarifasClientes.Where(t => t.GrupoCobro == "Marítimo" && !string.IsNullOrEmpty(t.Pod)).Select(t => t.Pod.ToUpper()).ToListAsync();
            var podsCostos = await _context.TarifasMaritimas.Where(t => !string.IsNullOrEmpty(t.Pod)).Select(t => t.Pod.ToUpper()).ToListAsync();
            ListaPods = new SelectList(podsVentas.Concat(podsCostos).Distinct().OrderBy(p => p).ToList());
            // 1. FILTRO DE VENTAS (Clientes)
            var queryVentas = _context.TarifasClientes.AsNoTracking().Include(t => t.IdClienteNavigation).Include(t => t.IdNavieraNavigation).Where(t => t.EsActiva == true);
            if (FiltroIdCliente.HasValue) queryVentas = queryVentas.Where(t => t.IdCliente == FiltroIdCliente.Value);
            if (!string.IsNullOrEmpty(FiltroPol)) queryVentas = queryVentas.Where(t => t.Pol == FiltroPol);
            if (!string.IsNullOrEmpty(FiltroPlanta)) queryVentas = queryVentas.Where(t => t.ZonaPlanta == FiltroPlanta);
            if (!string.IsNullOrEmpty(FiltroPod)) queryVentas = queryVentas.Where(t => t.Pod == FiltroPod);
            if (!string.IsNullOrEmpty(FiltroRuta))
            {
                var rutaQuery = FiltroRuta.ToLower();
                queryVentas = queryVentas.Where(t =>
                    (t.Pol != null && t.Pol.ToLower().Contains(rutaQuery)) ||
                    (t.Pod != null && t.Pod.ToLower().Contains(rutaQuery)) ||
                    (t.ZonaPlanta != null && t.ZonaPlanta.ToLower().Contains(rutaQuery)));
            }
            if (!string.IsNullOrEmpty(FiltroVigencia))
            {
                if (FiltroVigencia == "VENCIDOS") queryVentas = queryVentas.Where(t => t.FechaFinVigencia < hoyDt);
                else if (FiltroVigencia == "30DIAS") queryVentas = queryVentas.Where(t => t.FechaFinVigencia >= hoyDt && t.FechaFinVigencia <= limiteExpiracionDt);
                else if (FiltroVigencia == "90DIAS") queryVentas = queryVentas.Where(t => t.FechaFinVigencia >= hoyDt && t.FechaFinVigencia <= limite90);
            }
            TarifasClientes = await queryVentas.OrderByDescending(t => t.Id).ToListAsync();
            NombresNavieras = await _context.Navieras.ToDictionaryAsync(n => n.Id, n => n.NombreNaviera ?? "");

            // Tarifas COLLECT (RF Fresco con rebate), naviera y cliente.
            List<TarifaCollect> tarifasCollect;
            try
            {
                tarifasCollect = await _context.TarifasCollect.AsNoTracking()
                    .Include(t => t.IdClienteNavigation)
                    .Where(t => t.EsActiva)
                    .ToListAsync();
            }
            catch (Exception)
            {
                // Falta correr Sql/2026-10_tarifas_collect.sql: la pestaña queda vacía.
                tarifasCollect = new List<TarifaCollect>();
            }
            var collectNaviera = tarifasCollect.Where(t => t.TipoTarifa == TarifaCollect.TipoNaviera && (!FiltroIdNaviera.HasValue || t.IdNaviera == FiltroIdNaviera)).ToList();
            var collectCliente = tarifasCollect.Where(t => t.TipoTarifa == TarifaCollect.TipoCliente && (!FiltroIdCliente.HasValue || t.IdCliente == FiltroIdCliente)).ToList();
            PanelCollectNaviera = new TarifasCollectPanelVm
            {
                Tipo = TarifaCollect.TipoNaviera, Tarifas = collectNaviera, TarifasNaviera = collectNaviera,
                Navieras = ListaNavieras, Puertos = ListaPuertosMaestro, NombresNavieras = NombresNavieras
            };
            PanelCollectCliente = new TarifasCollectPanelVm
            {
                Tipo = TarifaCollect.TipoCliente, Tarifas = collectCliente,
                TarifasNaviera = tarifasCollect.Where(t => t.TipoTarifa == TarifaCollect.TipoNaviera).ToList(),
                Navieras = ListaNavieras, Clientes = ListaClientes, Puertos = ListaPuertosMaestro, NombresNavieras = NombresNavieras
            };
            TarifasGate = await _context.TarifasGate.AsNoTracking()
                .Include(t => t.IdDepositoNavigation)
                .Where(t => t.EsActiva)
                .OrderByDescending(t => t.Id)
                .ToListAsync();
            ComponentesGateInactivos = await _context.TarifasGate.AsNoTracking()
                .Where(t => !t.EsActiva && t.Componente != null && (t.Componente == "CONTROL_SELLO" || t.Componente == "BL_FEE"))
                .ToListAsync();
            ListaDepositos = (await _context.Depositos.AsNoTracking().Where(d => d.Activo == null || d.Activo == 1).ToListAsync())
                .OrderBy(d => (d.NombreDeposito ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            TarifasAlmacenamientos = await _context.TarifasAlmacenamientos.AsNoTracking()
    .Include(t => t.IdProveedorNavigation)
    .Where(t => t.EsActiva)
    .OrderByDescending(t => t.Id)
    .ToListAsync();
            DetallesAlmacenamiento = await _context.TarifasAlmacenamientoDetalles.AsNoTracking()
                .Where(t => t.EsActiva)
                .OrderBy(t => t.IdProveedor).ThenBy(t => t.Orden)
                .ToListAsync();
            TarifasBl = await _context.TarifasBl.AsNoTracking()
    .Include(t => t.IdNavieraNavigation)
    .Where(t => t.EsActiva)
    .OrderBy(t => t.IdNavieraNavigation!.NombreNaviera).ThenBy(t => t.TipoEmision)
    .ToListAsync();
            // 2. FILTRO MARÍTIMO (Costos)
            // Solo vigentes: "Eliminar" archiva (EsActiva = 0) y la tarifa debe desaparecer
            // de la lista, igual que en Terrestre. Queda en BD para trazabilidad.
            var queryMaritimas = _context.TarifasMaritimas.AsNoTracking().Include(t => t.IdNavieraNavigation).Where(t => t.EsActiva).AsQueryable();
            if (FiltroIdNaviera.HasValue) queryMaritimas = queryMaritimas.Where(t => t.IdNaviera == FiltroIdNaviera.Value);
            if (!string.IsNullOrEmpty(FiltroPol)) queryMaritimas = queryMaritimas.Where(t => t.Pol == FiltroPol);
            if (!string.IsNullOrEmpty(FiltroPod)) queryMaritimas = queryMaritimas.Where(t => t.Pod == FiltroPod);
            TarifasMaritimas = await queryMaritimas.OrderByDescending(t => t.Id).ToListAsync();
            // 3. FILTRO TERRESTRE (Costos)
            var queryTerrestres = _context.Tarifasterrestres
    .AsNoTracking()
    .Include(t => t.IdProveedorNavigation)
    .Include(t => t.IdCiudadOrigenNavigation)
    .Include(t => t.IdCiudadPlantaNavigation)
    .Include(t => t.IdCiudadDestinoNavigation)
    // Solo las vigentes: las que una carga masiva reemplazó (EsActiva = 0) no se muestran.
    .Where(t => t.EsActiva)
    .AsQueryable();
            if (FiltroIdProveedor.HasValue) queryTerrestres = queryTerrestres.Where(t => t.IdProveedor == FiltroIdProveedor.Value);
            if (FiltroIdCiudadOrigen.HasValue) queryTerrestres = queryTerrestres.Where(t => t.IdCiudadOrigen == FiltroIdCiudadOrigen.Value);
            if (FiltroIdCiudadPlanta.HasValue) queryTerrestres = queryTerrestres.Where(t => t.IdCiudadPlanta == FiltroIdCiudadPlanta.Value);
            if (FiltroIdCiudadDestino.HasValue) queryTerrestres = queryTerrestres.Where(t => t.IdCiudadDestino == FiltroIdCiudadDestino.Value);
            if (FiltroIdTipoCarga.HasValue) queryTerrestres = queryTerrestres.Where(t => t.IdTipoCarga == FiltroIdTipoCarga.Value);
            TarifasTerrestres = await queryTerrestres.OrderByDescending(t => t.Id).ToListAsync();
            TarifasMaestrasDocumentales = await _context.TarifasMaestras.AsNoTracking().Where(t => t.EsActiva && t.Categoria == "Documental").OrderByDescending(t => t.Id).ToListAsync();
            // Lista SEPARADA para Courier: antes se filtraba "Courier" dentro de
            // TarifasMaestrasDocumentales (que solo trae Categoria=="Documental"),
            // por lo que el panel de Courier siempre salía vacío. Se corrige aquí.
            TarifasMaestrasCourier = await _context.TarifasMaestras.AsNoTracking().Where(t => t.EsActiva && t.Categoria == "Courier").OrderByDescending(t => t.ValorNeto).ToListAsync();
            TarifasMaestrasMaritimo = await _context.TarifasMaestras.AsNoTracking()
                .Include(t => t.IdNavieraNavigation)
                .Where(t => t.EsActiva && (t.Categoria == "Marítimo"
                    || t.Concepto.StartsWith("SEGURIDAD DE LA CARGA")
                    || t.Concepto.StartsWith("Seguridad de la Carga")))
                .OrderBy(t => t.IdNavieraNavigation != null ? t.IdNavieraNavigation.NombreNaviera : "ZZZ")
                .ThenBy(t => t.Concepto)
                .ToListAsync();
            ConstruirComparativoRecargos();
            CountClientesActivas = TarifasClientes.Count(t => t.FechaFinVigencia.Date >= hoyDt);
            CountMaritimasActivas = TarifasMaritimas.Count;
            CountTerrestresActivas = TarifasTerrestres.Count;
            CountPorExpirar = TarifasClientes.Count(t => t.FechaFinVigencia.Date >= hoyDt && t.FechaFinVigencia.Date <= limiteExpiracionDt);
        }

        private void RegistrarHistorialComercial(string tipoEntidad, int idRegistro, string campo, object? valorAnterior, object? valorNuevo, string? comentario)
        {
            _context.HistorialCambiosComercial.Add(new HistorialCambiosComercial
            {
                TipoEntidad = tipoEntidad,
                IdRegistro = idRegistro,
                Campo = campo,
                ValorAnterior = valorAnterior?.ToString(),
                ValorNuevo = valorNuevo?.ToString(),
                Comentario = string.IsNullOrWhiteSpace(comentario) ? "(Sin comentario)" : comentario.Trim(),
                Usuario = User.Identity?.Name ?? "Comercial",
                FechaCambio = DateTime.Now
            });
        }

        public async Task<IActionResult> OnGetHistorialAsync(string tipoEntidad, int idRegistro)
        {
            var historial = await _context.HistorialCambiosComercial
                .Where(h => h.TipoEntidad == tipoEntidad && h.IdRegistro == idRegistro)
                .OrderByDescending(h => h.FechaCambio)
                .Select(h => new { h.Usuario, Fecha = h.FechaCambio.ToString("dd/MM/yyyy HH:mm"), h.Campo, h.ValorAnterior, h.ValorNuevo, h.Comentario })
                .ToListAsync();
            return new JsonResult(historial);
        }


        // Editor único para todas las tablas tarifarias del Comercial.
        // GET devuelve solamente los campos autorizados; POST registra cada diferencia
        // en HistorialCambiosComercial, con usuario, fecha y motivo obligatorio.
        private static readonly Dictionary<string, string[]> CamposEditablesTarifa = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Maritimo"] = new[] { "TarifaUsd", "RebateUsd", "Pol", "Pod", "Equipamiento", "DiasLibresOrigen", "DiasLibresDestino", "FechaInicioVigencia", "FechaFinVigencia", "Comentarios" },
            ["Terrestre"] = new[] { "ValorNeto", "NombreTramo", "Comentarios", "HorasLibresPlanta", "HorasLibresPuerto", "FechaInicioVigencia", "FechaFinVigencia" },
            ["Documental"] = new[] { "Concepto", "ValorNeto", "Moneda", "AplicaIva", "FechaInicioVigencia", "FechaFinVigencia", "Clasificacion", "Comentarios" },
            ["MaritimoExtra"] = new[] { "Concepto", "Codigo", "ValorNeto", "Moneda", "AplicaIva", "TipoContenedor", "Unidad", "CondicionAplicacion", "EventoReferencia", "HorasDesdeEvento", "HorasHastaEvento", "FechaVigenciaDesde", "FechaVigenciaHasta", "GrupoConcepto" },
            ["Courier"] = new[] { "Concepto", "ValorNeto", "Moneda", "AplicaIva", "FechaVigenciaDesde", "FechaVigenciaHasta" },
            ["Cliente"] = new[] { "Concepto", "PrecioPactado", "Moneda", "AplicaIva", "TipoContenedor", "FechaInicioVigencia", "FechaFinVigencia", "EsServicioGratuito", "Comentarios", "RebateUsd" },
            ["Gate"] = new[] { "ValorNeto", "Moneda", "TipoContenedor", "TipoMovimiento", "FechaInicioVigencia", "FechaFinVigencia" },
            ["Almacenamiento"] = new[] { "TarifaBase", "TarifaConexionReefer", "DiasLibres", "Moneda", "AplicaIva", "Comentarios", "FechaInicioVigencia", "FechaFinVigencia" },
            ["DetalleAlmacenamiento"] = new[] { "Concepto", "ValorNeto", "Moneda", "AplicaIva", "TipoContenedor", "TipoCarga", "EstadoContenedor" },
            ["TarifaBl"] = new[] { "TipoEmision", "TipoEmisionOtro", "ValorNeto", "Moneda", "AplicaIva" },
            ["Collect"] = new[] { "Pol", "Pod", "Commodity", "Equipamiento", "TarifaPublicaUsd", "TarifaNetaUsd",
                "LateArrivalSinCosto", "ExtraLateSinCosto", "CorreccionBlSinCosto", "DiasLibresOrigen", "DiasLibresDestino", "Plug",
                "Comentarios", "FechaInicioVigencia", "FechaFinVigencia" }
        };

        private async Task<object?> BuscarTarifaFilaAsync(string tipo, int id) => tipo switch
        {
            "Maritimo" => await _context.TarifasMaritimas.FindAsync(id),
            "Terrestre" => await _context.Tarifasterrestres.FindAsync(id),
            "Documental" => await _context.TarifasDocumentales.FindAsync(id),
            "MaritimoExtra" or "Courier" => await _context.TarifasMaestras.FindAsync(id),
            "Cliente" => await _context.TarifasClientes.FindAsync(id),
            "Gate" => await _context.TarifasGate.FindAsync(id),
            "Almacenamiento" => await _context.TarifasAlmacenamientos.FindAsync(id),
            "DetalleAlmacenamiento" => await _context.TarifasAlmacenamientoDetalles.FindAsync(id),
            "TarifaBl" => await _context.TarifasBl.FindAsync(id),
            "Collect" => await _context.TarifasCollect.FindAsync(id),
            _ => null
        };

        private static string FormatoTarifaFila(object? valor) => valor switch
        {
            null => "",
            DateOnly d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            DateTime d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => valor.ToString() ?? ""
        };

        public async Task<IActionResult> OnGetFilaTarifaAsync(string tipoEntidad, int idRegistro)
        {
            if (idRegistro <= 0 || !CamposEditablesTarifa.TryGetValue(tipoEntidad, out var nombres)) return NotFound();
            var entidad = await BuscarTarifaFilaAsync(tipoEntidad, idRegistro);
            if (entidad == null) return NotFound();
            var campos = nombres.Select(nombre =>
            {
                var prop = entidad.GetType().GetProperty(nombre);
                var tipo = Nullable.GetUnderlyingType(prop?.PropertyType ?? typeof(string)) ?? prop?.PropertyType ?? typeof(string);
                return new { nombre, tipo = tipo == typeof(bool) ? "bool" :
                    tipo == typeof(DateOnly) || tipo == typeof(DateTime) ? "fecha" :
                    tipo == typeof(int) || tipo == typeof(decimal) ? "numero" : "texto",
                    valor = FormatoTarifaFila(prop?.GetValue(entidad)) };
            }).ToList();
            Response.Headers["Cache-Control"] = "no-store";
            return new JsonResult(new { tipoEntidad, idRegistro, campos });
        }

        public async Task<IActionResult> OnPostGuardarFilaTarifaAsync(string tipoEntidad, int idRegistro, string? motivo)
        {
            if (idRegistro <= 0 || !CamposEditablesTarifa.TryGetValue(tipoEntidad, out var nombres))
                return BadRequest("Tipo de tarifa no reconocido.");
            if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 4)
                return BadRequest("Indica el motivo del cambio (mínimo cuatro caracteres).");

            var entidad = await BuscarTarifaFilaAsync(tipoEntidad, idRegistro);
            if (entidad == null) return NotFound();
            var cambios = new List<(System.Reflection.PropertyInfo Propiedad, object? Antes, object? Despues)>();
            foreach (var nombre in nombres)
            {
                var prop = entidad.GetType().GetProperty(nombre);
                if (prop == null || !prop.CanWrite || !Request.Form.ContainsKey("f_" + nombre)) continue;
                string raw = Request.Form["f_" + nombre].ToString().Trim();
                var tipo = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                bool esNullable = Nullable.GetUnderlyingType(prop.PropertyType) != null || !prop.PropertyType.IsValueType;
                object? nuevo;
                if (raw.Length == 0 && esNullable) nuevo = null;
                else if (tipo == typeof(bool)) nuevo = raw.Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (tipo == typeof(decimal))
                {
                    if (!TryParseMonto(raw, out var v))
                        return BadRequest("Monto inválido: " + nombre);
                    if (v < 0) return BadRequest("Los montos de tarifas no pueden ser negativos.");
                    nuevo = v;
                }
                else if (tipo == typeof(int))
                {
                    if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    {
                        // "1.000" o "48,0": se acepta si es un entero.
                        if (!TryParseMonto(raw, out var d) || d != Math.Truncate(d) || d > int.MaxValue || d < int.MinValue)
                            return BadRequest("Número inválido: " + nombre);
                        v = (int)d;
                    }
                    nuevo = v;
                }
                else if (tipo == typeof(DateOnly))
                {
                    if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var v))
                        return BadRequest("Fecha inválida: " + nombre);
                    nuevo = v;
                }
                else if (tipo == typeof(DateTime))
                {
                    if (!DateTime.TryParseExact(raw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var v))
                        return BadRequest("Fecha inválida: " + nombre);
                    nuevo = v;
                }
                else if (tipo == typeof(string))
                {
                    var limite = prop.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.StringLengthAttribute), true)
                        .Cast<System.ComponentModel.DataAnnotations.StringLengthAttribute>().FirstOrDefault()?.MaximumLength ?? 500;
                    if (raw.Length > limite) return BadRequest($"{nombre}: máximo {limite} caracteres.");
                    nuevo = raw;
                }
                else continue;

                if (nombre == "Moneda" && nuevo is string moneda && moneda is not ("USD" or "CLP"))
                    return BadRequest("La moneda debe ser USD o CLP.");

                var anterior = prop.GetValue(entidad);
                // Fechas: el control <input type=date> no transmite hora; no convertir
                // silenciosamente una fecha almacenada con componente horario.
                if ((tipo == typeof(DateOnly) || tipo == typeof(DateTime)) &&
                    FormatoTarifaFila(anterior) == raw) continue;
                if (!Equals(anterior, nuevo)) cambios.Add((prop, anterior, nuevo));
            }
            if (entidad is TarifaCollect collect)
            {
                decimal publico = cambios.FirstOrDefault(x => x.Propiedad.Name == "TarifaPublicaUsd").Despues is decimal p ? p : collect.TarifaPublicaUsd;
                decimal neto = cambios.FirstOrDefault(x => x.Propiedad.Name == "TarifaNetaUsd").Despues is decimal n ? n : collect.TarifaNetaUsd;
                if (neto > publico) return BadRequest("La tarifa neta no puede superar la tarifa pública.");
            }
            if (cambios.Count == 0) return RedirectToPage("./Index");

            // nov-2026: si se cambia el POL o el POD (marítimo o Collect), no pueden ser del mismo país.
            if ((tipoEntidad == "Maritimo" || tipoEntidad == "Collect") && cambios.Any(x => x.Propiedad.Name == "Pol" || x.Propiedad.Name == "Pod"))
            {
                string? ValorFinal(string campo)
                {
                    var c = cambios.FirstOrDefault(x => x.Propiedad.Name == campo);
                    return c.Propiedad != null ? c.Despues as string : entidad.GetType().GetProperty(campo)?.GetValue(entidad) as string;
                }
                if (await AtilsonCargoSpa.Helpers.PuertosHelper.MismoPaisPorNombreAsync(_context, ValorFinal("Pol"), ValorFinal("Pod")) is string paisFila)
                    return BadRequest(AtilsonCargoSpa.Helpers.PuertosHelper.MensajeMismoPais(paisFila));
            }

            foreach (var cambio in cambios)
                cambio.Propiedad.SetValue(entidad, cambio.Despues);
            if (entidad is Tarifasterrestre terrestre && cambios.Any(x => x.Propiedad.Name == "ValorNeto"))
            {
                terrestre.FalsoFletePlanta = terrestre.ValorNeto * 0.90m;
                terrestre.FalsoFleteRutaMayor50 = terrestre.ValorNeto * 0.80m;
                terrestre.FalsoFleteRutaMenor50 = terrestre.ValorNeto * 0.70m;
            }
            if (entidad is TarifasCliente venta && venta.EsServicioGratuito && venta.PrecioPactado != 0)
                return BadRequest("Una tarifa gratuita debe tener precio 0.");

            if (entidad is TarifaBl bl)
            {
                string tipo = EmisionBlHelper.Canonico(bl.TipoEmision);
                if (!EmisionBlHelper.EsCodigo(tipo)) return BadRequest("Tipo de emisión BL no válido.");
                if (tipo == EmisionBlHelper.Otro && string.IsNullOrWhiteSpace(bl.TipoEmisionOtro))
                    return BadRequest("Para emisión BL 'Otro', indica su denominación.");
                bl.TipoEmision = tipo;
            }

            var ahora = DateTime.Now;
            entidad.GetType().GetProperty("FechaModificacion")?.SetValue(entidad, ahora);
            entidad.GetType().GetProperty("UsuarioModificador")?.SetValue(entidad, User.Identity?.Name ?? "Comercial");
            foreach (var (prop, anterior, nuevo) in cambios)
                RegistrarHistorialComercial(tipoEntidad, idRegistro, prop.Name, FormatoTarifaFila(anterior), FormatoTarifaFila(nuevo), motivo);

            await _context.SaveChangesAsync();
            string grupo = tipoEntidad switch
            {
                "Maritimo" or "MaritimoExtra" or "TarifaBl" or "Collect" => "Marítimo",
                "Terrestre" => "Transporte",
                "Documental" or "Courier" => tipoEntidad,
                "Gate" => "Depósito",
                "Almacenamiento" or "DetalleAlmacenamiento" => "Almacenamiento",
                _ => ""
            };
            if (entidad is TarifasCliente tc && tc.IdCliente.HasValue)
                await ResincronizarOperacionesClienteAsync(tc.IdCliente.Value);
            else if (entidad is TarifaCollect collectActualizada)
                await ResincronizarCollectAsync(collectActualizada.IdNaviera, collectActualizada.IdCliente);
            else if (grupo.Length > 0)
                await ResincronizarPendientesAsync(grupo);
            TempData["SuccessMsg"] = "Tarifa actualizada en la fila. Cambios registrados en el historial.";
            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostCrearTarifaBlAsync()
        {
            NuevaTarifaBl.EsActiva = true;
            NuevaTarifaBl.FechaCreacion = DateTime.Now;
            NuevaTarifaBl.UsuarioCreador = User.Identity?.Name ?? "Comercial";

            // oct-2026: mismo catálogo que Operaciones/Finanzas (EmisionBlHelper):
            // Originales, Destino, Seawaybill, Telex Release, Express Release u Otro.
            // "Otro" lleva nombre; Finanzas lo cruza con el texto que escribe
            // Operaciones en el booking.
            string tipoCanon = AtilsonCargoSpa.Helpers.EmisionBlHelper.Canonico(NuevaTarifaBl.TipoEmision);
            string nombreOtro = AtilsonCargoSpa.Helpers.EmisionBlHelper.NormalizarNombre(NuevaTarifaBl.TipoEmisionOtro);
            if (!AtilsonCargoSpa.Helpers.EmisionBlHelper.EsCodigo(tipoCanon)
                || (tipoCanon == AtilsonCargoSpa.Helpers.EmisionBlHelper.Otro && nombreOtro == ""))
            {
                TempData["ToastError"] = tipoCanon == AtilsonCargoSpa.Helpers.EmisionBlHelper.Otro
                    ? "No se guardó: con tipo de emisión «Otro» hay que escribir el nombre."
                    : "No se guardó: elige el tipo de emisión del BL.";
                return RedirectToPage("./Index");
            }
            NuevaTarifaBl.TipoEmision = tipoCanon;
            NuevaTarifaBl.TipoEmisionOtro = tipoCanon == AtilsonCargoSpa.Helpers.EmisionBlHelper.Otro ? nombreOtro : null;
            NuevaTarifaBl.Moneda = NuevaTarifaBl.Moneda == "CLP" ? "CLP" : "USD";

            // <input type=number> siempre envía punto decimal; se lee con cultura
            // invariante para que "75.50" no se transforme en 7550 en un servidor es-CL.
            if (TryParseMonto(Request.Form["NuevaTarifaBl.ValorNeto"], out var valorInv))
                NuevaTarifaBl.ValorNeto = valorInv;
            _context.TarifasBl.Add(NuevaTarifaBl);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Marítimo");
            return RedirectToPage("./Index");
        }

        /// <summary>
        /// Corrige el IVA con que cobra la naviera en una tarifa marítima (recargo u Emisión BL),
        /// nov-2026. Es el dato de la naviera; Control Financiero sigue la regla del concepto.
        /// </summary>
        public async Task<IActionResult> OnPostCambiarIvaTarifaMaritimaAsync(string tipo, int idTarifa, bool aplicaIva)
        {
            int? idNaviera = null; bool cambio = false; bool anterior = false; string nombre = "";
            if (tipo == "BL")
            {
                var t = await _context.TarifasBl.FindAsync(idTarifa);
                if (t != null && t.AplicaIva != aplicaIva) { anterior = t.AplicaIva; t.AplicaIva = aplicaIva; idNaviera = t.IdNaviera; cambio = true; nombre = $"Emisión BL {t.TipoEmision}"; }
            }
            else
            {
                var t = await _context.TarifasMaestras.FindAsync(idTarifa);
                if (t != null && (t.Categoria == "Marítimo" || (t.Concepto ?? "").StartsWith("SEGURIDAD DE LA CARGA", StringComparison.OrdinalIgnoreCase)) && t.AplicaIva != aplicaIva) { anterior = t.AplicaIva; t.AplicaIva = aplicaIva; idNaviera = t.IdNaviera; cambio = true; nombre = t.Concepto; t.FechaModificacion = DateTime.Now; t.UsuarioModificador = User.Identity?.Name ?? "Comercial"; }
            }
            if (cambio)
            {
                RegistrarHistorialComercial(tipo == "BL" ? "TarifaBl" : "MaritimoExtra", idTarifa, $"IVA de {nombre}",
                    anterior ? "+ IVA" : "Exento", aplicaIva ? "+ IVA" : "Exento", "Cambio de IVA desde Comercial › Marítimo");
                await _context.SaveChangesAsync();
            }
            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostEliminarTarifaBlAsync(int idTarifa)
        {
            var tarifa = await _context.TarifasBl.FindAsync(idTarifa);
            if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); }
            return RedirectToPage("./Index");
        }
        // ==================== HANDLERS UNIVERSALES Y EDICIÓN ====================
        public async Task<IActionResult> OnPostEditarTarifaUniversalAsync(int IdTarifaUniversal, string TipoEntidad, decimal ValorTarifaUniversal, DateTime? NuevaFechaVigenciaUniversal, string? ComentarioAuditoria, string? ComentariosTarifaUniversal, bool EsServicioGratuito = false)
        {
            if (TryParseMonto(Request.Form["ValorTarifaUniversal"], out var valorUniversalInv)) ValorTarifaUniversal = valorUniversalInv;
            string usuario = User.Identity?.Name ?? "Comercial";
            DateTime ahora = DateTime.Now;
            bool debeResincronizarCliente = false;
            int? idClienteResync = null;
            if (TipoEntidad == "Documental")
            {
                var t = await _context.TarifasDocumentales.FindAsync(IdTarifaUniversal);
                if (t != null)
                {
                    decimal valorAnterior = t.ValorNeto;
                    DateTime? fechaAnterior = t.FechaFinVigencia;
                    t.ValorNeto = ValorTarifaUniversal;
                    if (NuevaFechaVigenciaUniversal.HasValue) t.FechaFinVigencia = NuevaFechaVigenciaUniversal.Value;
                    t.UsuarioModificador = usuario;
                    t.FechaModificacion = ahora;
                    RegistrarHistorialComercial("Documental", t.Id, $"Tarifa: {t.Concepto}", $"{t.Moneda} {valorAnterior:N0}", $"{t.Moneda} {ValorTarifaUniversal:N0}", ComentarioAuditoria);
                    if (NuevaFechaVigenciaUniversal.HasValue && fechaAnterior != NuevaFechaVigenciaUniversal)
                        RegistrarHistorialComercial("Documental", t.Id, "Vigencia", fechaAnterior?.ToString("dd/MM/yyyy"), NuevaFechaVigenciaUniversal.Value.ToString("dd/MM/yyyy"), ComentarioAuditoria);
                    await ResincronizarPendientesAsync("Documental");
                }
            }
            else if (TipoEntidad == "Gate")
            {
                var t = await _context.TarifasGate.FindAsync(IdTarifaUniversal);
                if (t != null)
                {
                    decimal valorAnterior = t.ValorNeto ?? 0;
                    DateTime? fechaAnterior = t.FechaFinVigencia;
                    t.ValorNeto = ValorTarifaUniversal;
                    if (NuevaFechaVigenciaUniversal.HasValue) t.FechaFinVigencia = NuevaFechaVigenciaUniversal.Value;
                    t.UsuarioModificador = usuario;
                    t.FechaModificacion = ahora;
                    RegistrarHistorialComercial("Gate", t.Id, "Tarifa Costo Gate", $"{t.Moneda} {valorAnterior:N0}", $"{t.Moneda} {ValorTarifaUniversal:N0}", ComentarioAuditoria);
                    if (NuevaFechaVigenciaUniversal.HasValue && fechaAnterior != NuevaFechaVigenciaUniversal)
                        RegistrarHistorialComercial("Gate", t.Id, "Vigencia", fechaAnterior?.ToString("dd/MM/yyyy"), NuevaFechaVigenciaUniversal.Value.ToString("dd/MM/yyyy"), ComentarioAuditoria);
                    await ResincronizarPendientesAsync("Depósito");
                }
            }
            else if (TipoEntidad == "Almacenamiento")
            {
                var t = await _context.TarifasAlmacenamientos.FindAsync(IdTarifaUniversal);
                if (t != null)
                {
                    decimal valorAnterior = t.TarifaBase;
                    DateTime? fechaAnterior = t.FechaFinVigencia;
                    string? comentariosAnterior = t.Comentarios;
                    t.TarifaBase = ValorTarifaUniversal;
                    if (NuevaFechaVigenciaUniversal.HasValue) t.FechaFinVigencia = NuevaFechaVigenciaUniversal.Value;
                    t.Comentarios = string.IsNullOrWhiteSpace(ComentariosTarifaUniversal) ? null : ComentariosTarifaUniversal.Trim();
                    t.UsuarioModificador = usuario;
                    t.FechaModificacion = ahora;
                    RegistrarHistorialComercial("Almacenamiento", t.Id, "Tarifa Base Almacenaje", $"${valorAnterior:N0}", $"${ValorTarifaUniversal:N0}", ComentarioAuditoria);
                    if (NuevaFechaVigenciaUniversal.HasValue && fechaAnterior != NuevaFechaVigenciaUniversal)
                        RegistrarHistorialComercial("Almacenamiento", t.Id, "Vigencia", fechaAnterior?.ToString("dd/MM/yyyy"), NuevaFechaVigenciaUniversal.Value.ToString("dd/MM/yyyy"), ComentarioAuditoria);
                    if (comentariosAnterior != t.Comentarios)
                        RegistrarHistorialComercial("Almacenamiento", t.Id, "Desglose de Tarifa", comentariosAnterior, t.Comentarios, ComentarioAuditoria);
                }
            }

            else if (TipoEntidad == "DetalleAlmacenamiento")
            {
                var t = await _context.TarifasAlmacenamientoDetalles.FindAsync(IdTarifaUniversal);
                if (t != null)
                {
                    decimal valorAnterior = t.ValorNeto;
                    t.ValorNeto = ValorTarifaUniversal;
                    t.UsuarioModificador = usuario;
                    t.FechaModificacion = ahora;
                    RegistrarHistorialComercial("DetalleAlmacenamiento", t.Id, $"Tarifa: {t.Concepto}", $"{t.Moneda} {valorAnterior:N0}", $"{t.Moneda} {ValorTarifaUniversal:N0}", ComentarioAuditoria);
                }
            }
            else if (TipoEntidad == "Courier")
            {
                var t = await _context.TarifasMaestras.FindAsync(IdTarifaUniversal);
                if (t != null)
                {
                    decimal valorAnterior = t.ValorNeto;
                    t.ValorNeto = ValorTarifaUniversal;
                    t.UsuarioModificador = usuario;
                    t.FechaModificacion = ahora;
                    RegistrarHistorialComercial("Courier", t.Id, $"Tarifa: {t.Concepto}", $"{t.Moneda} {valorAnterior:N0}", $"{t.Moneda} {ValorTarifaUniversal:N0}", ComentarioAuditoria);
                }
            }
            else if (TipoEntidad == "MaritimoExtra")
            {
                var t = await _context.TarifasMaestras.FindAsync(IdTarifaUniversal);
                if (t != null)
                {
                    decimal valorAnterior = t.ValorNeto;
                    DateOnly? fechaAnterior = t.FechaVigenciaHasta;
                    t.ValorNeto = ValorTarifaUniversal;
                    if (NuevaFechaVigenciaUniversal.HasValue)
                        t.FechaVigenciaHasta = DateOnly.FromDateTime(NuevaFechaVigenciaUniversal.Value);
                    t.UsuarioModificador = usuario;
                    t.FechaModificacion = ahora;
                    RegistrarHistorialComercial("MaritimoExtra", t.Id, $"Tarifa: {t.Concepto}", $"{t.Moneda} {valorAnterior:N0}", $"{t.Moneda} {ValorTarifaUniversal:N0}", ComentarioAuditoria);
                    if (NuevaFechaVigenciaUniversal.HasValue && fechaAnterior != t.FechaVigenciaHasta)
                        RegistrarHistorialComercial("MaritimoExtra", t.Id, "Vigente hasta", fechaAnterior?.ToString("dd/MM/yyyy") ?? "Indefinida", t.FechaVigenciaHasta?.ToString("dd/MM/yyyy") ?? "Indefinida", ComentarioAuditoria);
                    await ResincronizarPendientesAsync("Marítimo");
                }
            }
            else if (TipoEntidad == "Cliente")
            {
                var t = await _context.TarifasClientes.FindAsync(IdTarifaUniversal);
                if (t != null)
                {
                    decimal valorAnterior = t.PrecioPactado;
                    DateTime fechaAnterior = t.FechaFinVigencia;
                    bool gratuitoAnterior = t.EsServicioGratuito;
                    t.PrecioPactado = ValorTarifaUniversal;
                    t.EsServicioGratuito = EsServicioGratuito;
                    if (NuevaFechaVigenciaUniversal.HasValue) t.FechaFinVigencia = NuevaFechaVigenciaUniversal.Value;
                    t.UsuarioModificador = usuario;
                    t.FechaModificacion = ahora;
                    RegistrarHistorialComercial("Cliente", t.Id, $"Venta: {t.Concepto}", $"{t.Moneda} {valorAnterior:N0}", $"{t.Moneda} {ValorTarifaUniversal:N0}", ComentarioAuditoria);
                    if (NuevaFechaVigenciaUniversal.HasValue && fechaAnterior != NuevaFechaVigenciaUniversal.Value)
                        RegistrarHistorialComercial("Cliente", t.Id, "Vigencia", fechaAnterior.ToString("dd/MM/yyyy"), NuevaFechaVigenciaUniversal.Value.ToString("dd/MM/yyyy"), ComentarioAuditoria);
                    if (gratuitoAnterior != EsServicioGratuito)
                    {
                        RegistrarHistorialComercial("Cliente", t.Id, "Servicio Gratuito (Tarifa Plana)", gratuitoAnterior ? "SÍ" : "NO", EsServicioGratuito ? "SÍ" : "NO", ComentarioAuditoria);
                        debeResincronizarCliente = true;
                        idClienteResync = t.IdCliente;
                    }
                }
            }
            await _context.SaveChangesAsync();
            if (debeResincronizarCliente && idClienteResync.HasValue)
                await ResincronizarOperacionesClienteAsync(idClienteResync.Value);
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostEditarCostoMaritimoAsync(string? ComentarioAuditoria)
        {
            ReparsearDecimales(EditarCostoMaritimo, nameof(EditarCostoMaritimo));
            var tarifaExistente = await _context.TarifasMaritimas.FindAsync(EditarCostoMaritimo.Id);
            if (tarifaExistente != null && EditarCostoMaritimo.TarifaUsd > 0)
            {
                decimal valorAnterior = tarifaExistente.TarifaUsd;
                decimal? rebateAnterior = tarifaExistente.RebateUsd;
                DateOnly inicioAnterior = tarifaExistente.FechaInicioVigencia;
                DateOnly? finAnterior = tarifaExistente.FechaFinVigencia;

                tarifaExistente.TarifaUsd = EditarCostoMaritimo.TarifaUsd;
                tarifaExistente.RebateUsd = EditarCostoMaritimo.RebateUsd;
                if (EditarCostoMaritimo.FechaInicioVigencia != default)
                    tarifaExistente.FechaInicioVigencia = EditarCostoMaritimo.FechaInicioVigencia;
                tarifaExistente.FechaFinVigencia = EditarCostoMaritimo.FechaFinVigencia;
                tarifaExistente.FechaModificacion = DateTime.Now;
                tarifaExistente.UsuarioModificador = User.Identity?.Name ?? "Comercial";

                if (valorAnterior != tarifaExistente.TarifaUsd)
                    RegistrarHistorialComercial("Maritimo", tarifaExistente.Id, "Flete Base", $"USD {valorAnterior:N2}", $"USD {tarifaExistente.TarifaUsd:N2}", ComentarioAuditoria);
                if (rebateAnterior != tarifaExistente.RebateUsd)
                    RegistrarHistorialComercial("Maritimo", tarifaExistente.Id, "Rebate", rebateAnterior.HasValue ? $"USD {rebateAnterior:N2}" : "—", tarifaExistente.RebateUsd.HasValue ? $"USD {tarifaExistente.RebateUsd:N2}" : "—", ComentarioAuditoria);
                if (inicioAnterior != tarifaExistente.FechaInicioVigencia)
                    RegistrarHistorialComercial("Maritimo", tarifaExistente.Id, "Vigente desde", inicioAnterior.ToString("dd/MM/yyyy"), tarifaExistente.FechaInicioVigencia.ToString("dd/MM/yyyy"), ComentarioAuditoria);
                if (finAnterior != tarifaExistente.FechaFinVigencia)
                    RegistrarHistorialComercial("Maritimo", tarifaExistente.Id, "Vigente hasta", finAnterior?.ToString("dd/MM/yyyy") ?? "Indefinida", tarifaExistente.FechaFinVigencia?.ToString("dd/MM/yyyy") ?? "Indefinida", ComentarioAuditoria);

                await _context.SaveChangesAsync();
                await ResincronizarPendientesAsync("Marítimo");
            }
            return RedirectToPage("./Index");
        }
        // Modalidad Collect/No Prepaid: asigna o corrige el rebate que le devolvemos a
        // este cliente para una tarifa Marítimo ya existente (no crea tarifas nuevas).
        public async Task<IActionResult> OnPostEditarRebateClienteAsync(int idTarifaCliente, decimal? rebateUsd)
        {
            // El enlace del modelo depende de la cultura del servidor ("12.50" → 1250 en es-CL).
            rebateUsd = AtilsonCargoSpa.Helpers.MontoHelper.ParseONulo(Request.Form["rebateUsd"]);
            var tarifaCliente = await _context.TarifasClientes.FindAsync(idTarifaCliente);
            if (tarifaCliente != null)
            {
                var rebateAnterior = tarifaCliente.RebateUsd;
                tarifaCliente.RebateUsd = rebateUsd;
                if (rebateAnterior != rebateUsd)
                    RegistrarHistorialComercial("Cliente", tarifaCliente.Id, "Rebate USD",
                        rebateAnterior?.ToString("N2") ?? "—", rebateUsd?.ToString("N2") ?? "—",
                        "Ajuste de rebate de venta Collect"); 
                tarifaCliente.FechaModificacion = DateTime.Now;
                tarifaCliente.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                await _context.SaveChangesAsync();
                if (tarifaCliente.GrupoCobro == "Marítimo") await ResincronizarPendientesAsync("Marítimo");
            }
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostEditarCostoTerrestreAsync(string? ComentarioAuditoria)
        {
            ReparsearDecimales(EditarCostoTerrestre, nameof(EditarCostoTerrestre));
            var tarifaExistente = await _context.Tarifasterrestres.FindAsync(EditarCostoTerrestre.Id);
            if (tarifaExistente != null && EditarCostoTerrestre.ValorNeto.HasValue)
            {
                decimal valorAnterior = tarifaExistente.ValorNeto ?? 0;
                DateOnly? fechaAnterior = tarifaExistente.FechaFinVigencia;
                tarifaExistente.ValorNeto = EditarCostoTerrestre.ValorNeto;
                tarifaExistente.FalsoFletePlanta = tarifaExistente.ValorNeto * 0.90m;
                tarifaExistente.FalsoFleteRutaMayor50 = tarifaExistente.ValorNeto * 0.80m;
                tarifaExistente.FalsoFleteRutaMenor50 = tarifaExistente.ValorNeto * 0.70m;
                if (EditarCostoTerrestre.FechaFinVigencia != default) tarifaExistente.FechaFinVigencia = EditarCostoTerrestre.FechaFinVigencia;
                tarifaExistente.FechaModificacion = DateTime.Now;
                tarifaExistente.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                RegistrarHistorialComercial("Terrestre", tarifaExistente.Id, "Flete Base", $"${valorAnterior:N0}", $"${tarifaExistente.ValorNeto:N0}", ComentarioAuditoria);
                if (fechaAnterior != tarifaExistente.FechaFinVigencia)
                    RegistrarHistorialComercial("Terrestre", tarifaExistente.Id, "Vigencia", fechaAnterior?.ToString("dd/MM/yyyy"), tarifaExistente.FechaFinVigencia?.ToString("dd/MM/yyyy"), ComentarioAuditoria);
                await _context.SaveChangesAsync();
                await ResincronizarPendientesAsync("Transporte");
            }
            return RedirectToPage("./Index");
        }
        // ==================== HANDLERS DE CREACIÓN ====================
        public async Task<IActionResult> OnPostCrearVentaAlmacenamientoAsync()
        {
            PrepararVentaCliente(NuevaVentaAlmacenamiento, "Almacenamiento", nameof(NuevaVentaAlmacenamiento), "SERVICIO DE ALMACENAJE");
            _context.TarifasClientes.Add(NuevaVentaAlmacenamiento);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Almacenamiento");
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearVentaMaritimaAsync()
        {
            // "Condición / Concepto" se eliminó del formulario (minuta oct-2026): el
            // concepto queda fijo como flete marítimo.
            NuevaVentaMaritima.Concepto = null;
            PrepararVentaCliente(NuevaVentaMaritima, "Marítimo", nameof(NuevaVentaMaritima), "FLETE MARÍTIMO");
            // RF Congelado / RF Fresco Prepaid (oct-2026): la pestaña fija la clasificación;
            // solo aplica a ventas reefer (Dry queda sin clasificar).
            {
                var clasRf = AtilsonCargoSpa.Helpers.VentaClienteHelper.ClasificacionReefer(NuevaVentaMaritima.Clasificacion);
                bool esDry = AtilsonCargoSpa.Helpers.VentaClienteHelper.FamiliaEquipo(NuevaVentaMaritima.TipoContenedor) == "DRY";
                NuevaVentaMaritima.Clasificacion = esDry ? null : clasRf;
            }
            var prop = NuevaVentaMaritima.GetType().GetProperty("PlacPlug");
            if (prop != null && TryParseMonto(Request.Form["PlacPlug_Venta"], out decimal plugVal)) prop.SetValue(NuevaVentaMaritima, plugVal);
            NuevaVentaMaritima.AplicaIva = false; // flete marítimo: siempre exento (nov-2026)
            if (await AtilsonCargoSpa.Helpers.PuertosHelper.MismoPaisPorNombreAsync(_context, NuevaVentaMaritima.Pol, NuevaVentaMaritima.Pod) is string paisVenta)
            {
                MensajeCargaMasiva = "ERROR|Venta marítima: " + AtilsonCargoSpa.Helpers.PuertosHelper.MensajeMismoPais(paisVenta);
                return RedirectToPage("./Index");
            }
            _context.TarifasClientes.Add(NuevaVentaMaritima);
            await _context.SaveChangesAsync();
            await ResincronizarVentasClienteAsync(NuevaVentaMaritima.IdCliente, "Marítimo");
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearCostoMaritimoAsync()
        {
            NuevoCostoMaritimo.EsActiva = true;
            ReparsearDecimales(NuevoCostoMaritimo, nameof(NuevoCostoMaritimo));
            var prop = NuevoCostoMaritimo.GetType().GetProperty("PlacPlug");
            if (prop != null && TryParseMonto(Request.Form["PlacPlug_Costo"], out decimal plugVal)) prop.SetValue(NuevoCostoMaritimo, plugVal);
            if (await AtilsonCargoSpa.Helpers.PuertosHelper.MismoPaisPorNombreAsync(_context, NuevoCostoMaritimo.Pol, NuevoCostoMaritimo.Pod) is string paisCosto)
            {
                MensajeCargaMasivaCostos = "ERROR|Costo marítimo: " + AtilsonCargoSpa.Helpers.PuertosHelper.MensajeMismoPais(paisCosto);
                return RedirectToPage("./Index");
            }
            _context.TarifasMaritimas.Add(NuevoCostoMaritimo);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Marítimo");
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearVentaTerrestreAsync()
        {
            // "Condición / Tramo" se eliminó del formulario: el concepto queda fijo.
            NuevaVentaTerrestre.Concepto = null;
            PrepararVentaCliente(NuevaVentaTerrestre, "Terrestre", nameof(NuevaVentaTerrestre), "FLETE TERRESTRE");
            _context.TarifasClientes.Add(NuevaVentaTerrestre);
            await _context.SaveChangesAsync();
            await ResincronizarVentasClienteAsync(NuevaVentaTerrestre.IdCliente, "Transporte");
            return RedirectToPage("./Index");
        }
        // Venta documental al cliente (oct-2026): solo se elige SAG o SERNAPESCA y se
        // cargan Honorarios (+ IVA) y/o DUS (sin IVA). Cada uno queda como una tarifa
        // propia; Finanzas los usa como valor de venta de "Honorarios Agencia de Aduanas"
        // y "Legalización / Emisión DUS" (por contenedor o en el cobro grupal).
        // ==================== TARIFAS COLLECT (RF Fresco con rebate, oct-2026) ====================
        public async Task<IActionResult> OnPostCrearTarifaCollectAsync()
        {
            var f = Request.Form;
            string tipo = f["TipoTarifa"].ToString() == TarifaCollect.TipoCliente ? TarifaCollect.TipoCliente : TarifaCollect.TipoNaviera;
            int.TryParse(f["IdNaviera"], out int idNaviera);
            int? idCliente = int.TryParse(f["IdCliente"], out int idc) ? idc : null;
            string pod = f["Pod"].ToString().Trim().ToUpperInvariant();
            TryParseMonto(f["TarifaPublicaUsd"], out decimal publica);
            TryParseMonto(f["TarifaNetaUsd"], out decimal neta);
            if (idNaviera <= 0 || pod == "" || publica <= 0 || neta < 0 || (tipo == TarifaCollect.TipoCliente && !idCliente.HasValue))
            {
                MensajeCargaMasiva = "ERROR|Tarifa Collect: faltan naviera, destino, cliente o la tarifa pública.";
                return RedirectToPage("./Index");
            }
            string? Txt(string k, int max) { var v = f[k].ToString().Trim(); return v == "" ? null : (v.Length > max ? v.Substring(0, max) : v); }
            if (await AtilsonCargoSpa.Helpers.PuertosHelper.MismoPaisPorNombreAsync(_context, Txt("Pol", 100), pod) is string paisCollect)
            {
                MensajeCargaMasiva = "ERROR|Tarifa Collect: " + AtilsonCargoSpa.Helpers.PuertosHelper.MensajeMismoPais(paisCollect);
                return RedirectToPage("./Index");
            }
            string corr = f["CorreccionBlSinCosto"].ToString();
            var nueva = new TarifaCollect
            {
                TipoTarifa = tipo,
                IdCliente = tipo == TarifaCollect.TipoCliente ? idCliente : null,
                IdNaviera = idNaviera,
                Pol = Txt("Pol", 100)?.ToUpperInvariant(),
                Pod = pod,
                Commodity = AtilsonCargoSpa.Helpers.CommodityHelper.Unir(f["Commodity"].ToArray()!),
                Equipamiento = "40' REEFER",
                TarifaPublicaUsd = publica,
                TarifaNetaUsd = neta,
                LateArrivalSinCosto = f["LateArrivalSinCosto"].Contains("true"),
                ExtraLateSinCosto = f["ExtraLateSinCosto"].Contains("true"),
                CorreccionBlSinCosto = corr == TarifaCollect.CorreccionAntesZarpe || corr == TarifaCollect.Correccion24h || corr == TarifaCollect.Correccion48h ? corr : null,
                DiasLibresOrigen = Txt("DiasLibresOrigen", 50)?.ToUpperInvariant(),
                DiasLibresDestino = Txt("DiasLibresDestino", 50)?.ToUpperInvariant(),
                Plug = Txt("Plug", 50)?.ToUpperInvariant(),
                Comentarios = Txt("Comentarios", 500),
                FechaInicioVigencia = DateTime.TryParse(f["FechaInicioVigencia"], out var fi) ? fi.Date : DateTime.Today,
                FechaFinVigencia = DateTime.TryParse(f["FechaFinVigencia"], out var ff) ? ff.Date : null,
                EsActiva = true,
                FechaCreacion = DateTime.Now,
                UsuarioCreador = User.Identity?.Name ?? "Comercial"
            };
            _context.TarifasCollect.Add(nueva);
            await _context.SaveChangesAsync();
            RegistrarHistorialComercial("Collect", nueva.Id, "Creación", null,
                $"USD {nueva.TarifaPublicaUsd:N2} pública / USD {nueva.TarifaNetaUsd:N2} neta",
                "Alta de acuerdo Collect desde Comercial");
            await _context.SaveChangesAsync();
            await ResincronizarCollectAsync(nueva.IdNaviera, nueva.IdCliente);
            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostArchivarTarifaCollectAsync(int idTarifa)
        {
            var t = await _context.TarifasCollect.FirstOrDefaultAsync(x => x.Id == idTarifa);
            if (t != null)
            {
                if (t.EsActiva)
                    RegistrarHistorialComercial("Collect", t.Id, "Estado", "Activa", "Archivada", "Archivado de tarifa Collect");
                t.EsActiva = false;
                t.FechaModificacion = DateTime.Now;
                t.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                await _context.SaveChangesAsync();
                await ResincronizarCollectAsync(t.IdNaviera, t.IdCliente);
            }
            return RedirectToPage("./Index");
        }

        // Bookings Collect de esa naviera (y cliente) que siguen en provisión: rebate y
        // condiciones sin costo se recalculan con la tarifa nueva.
        private async Task ResincronizarCollectAsync(int idNaviera, int? idCliente)
        {
            var ids = await _context.Operaciones
                .Where(o => !o.IsDeleted && o.IdNaviera == idNaviera && (o.CondicionPago == 3 || o.CondicionPago == 4)
                         && (!idCliente.HasValue || o.IdCliente == idCliente)
                         && _context.TransaccionesFinancieras.Any(t => t.IdOperacion == o.Id && t.GrupoCobro == "Marítimo"
                             && (t.EstadoFila == "PROVISIÓN" || t.EstadoFila == "PENDIENTE VALORIZAR")))
                .Select(o => o.Id)
                .ToListAsync();
            string usuario = User.Identity?.Name ?? "Comercial";
            foreach (var id in ids) await _finanzasSync.SincronizarAsync(id, usuario);
        }

        public async Task<IActionResult> OnPostCrearVentaDocumentalAsync()
        {
            string clasificacion = (NuevaVentaMaritima.Clasificacion ?? "").Trim().ToUpperInvariant();
            if (clasificacion != "SAG" && clasificacion != "SERNAPESCA")
            {
                TempData["ErrorMsg"] = "Venta documental: elige si es SAG o SERNAPESCA.";
                return RedirectToPage("./Index");
            }
            TryParseMonto(Request.Form["VentaDocHonorarios"], out decimal honorarios);
            TryParseMonto(Request.Form["VentaDocDus"], out decimal dus);
            if (honorarios <= 0 && dus <= 0)
            {
                TempData["ErrorMsg"] = "Venta documental: ingresa el valor de Honorarios y/o del DUS.";
                return RedirectToPage("./Index");
            }

            NuevaVentaMaritima.Concepto = null;
            PrepararVentaCliente(NuevaVentaMaritima, "Documental", nameof(NuevaVentaMaritima));
            var b = NuevaVentaMaritima;
            TarifasCliente Nueva(string concepto, decimal precio, bool aplicaIva) => new TarifasCliente
            {
                IdCliente = b.IdCliente,
                GrupoCobro = "Documental",
                Clasificacion = clasificacion,
                Concepto = concepto,
                PrecioPactado = precio,
                Moneda = b.Moneda,
                AplicaIva = aplicaIva,
                EsActiva = true,
                EsServicioGratuito = false,
                FechaInicioVigencia = b.FechaInicioVigencia,
                FechaFinVigencia = b.FechaFinVigencia,
                UsuarioCreador = b.UsuarioCreador
            };
            var nuevas = new List<TarifasCliente>();
            if (honorarios > 0) nuevas.Add(Nueva(AtilsonCargoSpa.Helpers.VentaClienteHelper.ConceptoVentaHonorarios, honorarios, aplicaIva: true));
            if (dus > 0) nuevas.Add(Nueva(AtilsonCargoSpa.Helpers.VentaClienteHelper.ConceptoVentaDus, dus, aplicaIva: false));

            // La venta anterior del mismo cliente, servicio y concepto queda archivada
            // (la nueva la reemplaza), así no conviven dos valores vigentes.
            var conceptosNuevos = nuevas.Select(n => n.Concepto).ToList();
            var anteriores = await _context.TarifasClientes
                .Where(t => t.IdCliente == b.IdCliente && t.EsActiva && t.GrupoCobro == "Documental"
                         && t.Clasificacion == clasificacion && conceptosNuevos.Contains(t.Concepto))
                .ToListAsync();
            foreach (var a in anteriores)
            {
                a.EsActiva = false;
                a.FechaModificacion = DateTime.Now;
                a.UsuarioModificador = User.Identity?.Name ?? "Comercial";
            }

            _context.TarifasClientes.AddRange(nuevas);
            await _context.SaveChangesAsync();
            await ResincronizarVentasClienteAsync(b.IdCliente, "Documental");
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearVentaCourierAsync()
        {
            PrepararVentaCliente(NuevaVentaCourier, "Courier", nameof(NuevaVentaCourier));
            // Empresa de courier (DHL/FedEx/Chilexpress) en Clasificacion y contrato
            // (Stembex / Propio / Otra AGA) en ZonaPlanta — ver CourierHelper.
            NuevaVentaCourier.Clasificacion = AtilsonCargoSpa.Helpers.CourierHelper.Normalizar(NuevaVentaCourier.Clasificacion);
            NuevaVentaCourier.ZonaPlanta = AtilsonCargoSpa.Helpers.CourierHelper.NormalizarContrato(Request.Form["ContratoCourierVenta"]);
            if (NuevaVentaCourier.Clasificacion == null) { TempData["ToastError"] = "No se guardó: elige la empresa de courier."; return RedirectToPage("./Index"); }
            _context.TarifasClientes.Add(NuevaVentaCourier);
            await _context.SaveChangesAsync();
            await ResincronizarVentasClienteAsync(NuevaVentaCourier.IdCliente, "Documental");
            return RedirectToPage("./Index");
        }

        // ==================== CARGAS MASIVAS POR SERVICIO Y CONCEPTO (13-nov-2026) ====================
        // Plantilla y carga de un solo servicio (y, opcional, un concepto / subconcepto): ver
        // Helpers/CargaMasivaPorServicioHelper.cs. Sin servicio elegido, la plantilla general.
        public AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Tipo? TipoCargaCostos { get; set; }
        public AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Tipo? TipoCargaVentas { get; set; }

        private static readonly string[] TramitesDocumentalesBase =
        {
            "HONORARIOS AGENCIA DE ADUANAS", "LEGALIZACIÓN DUS EMBARQUES NORMALES", "DIN", "CERTIFICADO FITOSANITARIO",
            "CERTIFICADO SANITARIO", "CERTIFICADO DE ORIGEN", "CERTIFICADO DE CAPTURA", "GENERACIÓN IVV"
        };

        private async Task<AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Tipo> TipoCostosAsync()
        {
            var existentes = await _context.TarifasDocumentales.AsNoTracking().Where(t => t.Concepto != null && t.Concepto != "")
                .Select(t => t.Concepto).Distinct().ToListAsync();
            return AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Costos(TramitesDocumentalesBase.Concat(existentes));
        }

        private static AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Tipo TipoVentas() =>
            AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Ventas(new[] { "SERVICIO DE ALMACENAJE" },
                new[] { AtilsonCargoSpa.Helpers.CourierHelper.ConceptoNacional }.Concat(AtilsonCargoSpa.Helpers.CourierHelper.ConceptosInternacionales));

        private FileContentResult ArchivoPlantilla(byte[] general, AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Tipo tipo, string? servicio, string? concepto, string prefijo)
        {
            var def = AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Buscar(tipo, servicio);
            byte[] bytes = def == null ? general : AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.PlantillaDeServicio(general, tipo, def, concepto);
            string sufijo = def == null ? "" : "_" + def.Servicio.ToLowerInvariant()
                + (string.IsNullOrWhiteSpace(concepto) ? "" : "_" + System.Text.RegularExpressions.Regex.Replace(concepto.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'));
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{prefijo}{sufijo}_{DateTime.Today:yyyyMMdd}.xlsx");
        }

        /// <summary>Con servicio elegido: filas de otro servicio o concepto (null si no hay servicio).</summary>
        private static List<string>? FilasFueraDeServicio(System.IO.Stream stream, AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Tipo tipo, string? servicio, string? concepto)
        {
            var def = AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.Buscar(tipo, servicio);
            if (def == null) return null;
            var errores = AtilsonCargoSpa.Helpers.CargaMasivaPorServicioHelper.FilasFuera(stream, tipo, def, concepto);
            stream.Position = 0;
            return errores;
        }

        private static string EtiquetaCarga(string? servicio, string? concepto) =>
            string.IsNullOrWhiteSpace(servicio) ? "" : $" de {servicio.Trim().ToUpperInvariant()}{(string.IsNullOrWhiteSpace(concepto) ? "" : " · " + concepto.Trim().ToUpperInvariant())}";

        // ==================== CARGA MASIVA DE VENTAS ====================
        [TempData] public string? MensajeCargaMasiva { get; set; }

        public async Task<IActionResult> OnGetPlantillaVentasAsync(string? servicio, string? concepto)
        {
            var clientes = await _context.Clientes.AsNoTracking().Where(c => c.Activo == 1).OrderBy(c => c.RazonSocial).Select(c => c.RazonSocial).ToListAsync();
            var navieras = await _context.Navieras.AsNoTracking().Where(n => n.Activo == 1).OrderBy(n => n.NombreNaviera).Select(n => n.NombreNaviera ?? "").ToListAsync();
            var depositos = (await _context.Depositos.AsNoTracking().Where(d => d.Activo == null || d.Activo == 1).Select(d => d.NombreDeposito).ToListAsync())
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
            var puertos = await _context.Puertos.AsNoTracking().Where(p => p.Activo == 1).OrderBy(p => p.NombrePuerto).Select(p => p.NombrePuerto).Distinct().ToListAsync();
            var bytes = AtilsonCargoSpa.Helpers.CargaMasivaVentasHelper.GenerarPlantilla(clientes, navieras, depositos, puertos);
            return ArchivoPlantilla(bytes, TipoVentas(), servicio, concepto, "plantilla_ventas_clientes");
        }

        // ==================== CARGA MASIVA DE COSTOS (nov-2026) ====================
        // Misma idea que la de ventas: plantilla Excel con las listas de maestros, todo o nada.
        [TempData] public string? MensajeCargaMasivaCostos { get; set; }

        private async Task<AtilsonCargoSpa.Helpers.CargaMasivaCostosHelper.Maestros> MaestrosCargaCostosAsync() => new()
        {
            Navieras = await _context.Navieras.AsNoTracking().Where(n => n.Activo == 1).OrderBy(n => n.NombreNaviera).ToListAsync(),
            Proveedores = await _context.Proveedores.AsNoTracking().Where(p => p.Activo == null || p.Activo == 1).OrderBy(p => p.NombreProveedor).ToListAsync(),
            Depositos = await _context.Depositos.AsNoTracking().Where(d => d.Activo == null || d.Activo == 1).OrderBy(d => d.NombreDeposito).ToListAsync(),
            Agencias = await _context.AgenciasAduanas.AsNoTracking().Where(a => a.Activo == 1).OrderBy(a => a.NombreAgencia).ToListAsync(),
            Ciudades = await _context.Ciudades.AsNoTracking().OrderBy(c => c.Nombre).ToListAsync(),
            Puertos = await _context.Puertos.AsNoTracking().Where(p => p.Activo == 1).Select(p => p.NombrePuerto).Distinct().ToListAsync(),
            PaisPorPuerto = (await AtilsonCargoSpa.Helpers.PuertosHelper.MapaPaisesAsync(_context)).PorNombre
        };

        public async Task<IActionResult> OnGetPlantillaCostosAsync(string? servicio, string? concepto)
        {
            if (SoloTransporte) servicio = "TERRESTRE";
            var bytes = AtilsonCargoSpa.Helpers.CargaMasivaCostosHelper.GenerarPlantilla(await MaestrosCargaCostosAsync());
            return ArchivoPlantilla(bytes, await TipoCostosAsync(), servicio, concepto, "plantilla_costos_proveedores");
        }

        public async Task<IActionResult> OnPostCargaMasivaCostosAsync(Microsoft.AspNetCore.Http.IFormFile? archivoCostos, string? servicio, string? concepto)
        {
            if (SoloTransporte) servicio = "TERRESTRE";
            if (archivoCostos == null || archivoCostos.Length == 0)
            {
                MensajeCargaMasivaCostos = "ERROR|Selecciona un archivo Excel (.xlsx).";
                TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                return RedirectToPage("./Index");
            }
            string usuario = User.Identity?.Name ?? "Comercial";
            AtilsonCargoSpa.Helpers.CargaMasivaCostosHelper.Resultado res;
            try
            {
                using var stream = new System.IO.MemoryStream();
                await archivoCostos.CopyToAsync(stream);
                stream.Position = 0;
                var fuera = FilasFueraDeServicio(stream, await TipoCostosAsync(), servicio, concepto);
                if (fuera != null && fuera.Count > 0)
                {
                    MensajeCargaMasivaCostos = $"ERROR|No se cargó ninguna tarifa: la carga{EtiquetaCarga(servicio, concepto)} trae filas de otro servicio o concepto. Corrige y vuelve a subir:|" + string.Join("|", fuera.Take(25));
                    TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                    return RedirectToPage("./Index");
                }
                res = AtilsonCargoSpa.Helpers.CargaMasivaCostosHelper.Leer(stream, await MaestrosCargaCostosAsync(), usuario);
            }
            catch (Exception)
            {
                MensajeCargaMasivaCostos = "ERROR|No se pudo leer el archivo. Usa la plantilla descargada desde esta misma pantalla (formato .xlsx).";
                TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                return RedirectToPage("./Index");
            }

            // Con el permiso de tarifas de transporte solo se cargan costos terrestres.
            if (SoloTransporte && (res.Maritimas.Any() || res.Gates.Any() || res.Documentales.Any()))
                res.Errores.Add("Con tu permiso solo puedes cargar costos de TERRESTRE: quita las filas de marítimo, gate y documental.");
            if (res.Errores.Any())
            {
                MensajeCargaMasivaCostos = "ERROR|No se cargó ninguna tarifa. Corrige y vuelve a subir:|" + string.Join("|", res.Errores.Take(25))
                    + (res.Errores.Count > 25 ? $"|… y {res.Errores.Count - 25} errores más." : "");
                TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                return RedirectToPage("./Index");
            }
            // Tramos terrestres repetidos (en el archivo o ya vigentes en Comercial).
            var tramosVistos = new HashSet<string>();
            foreach (var t in res.Terrestres)
            {
                var ids = await MismoTransportistaAsync(t.IdProveedor!.Value);
                string clave = $"{ids.Min()}|{t.IdCiudadOrigen}|{t.IdCiudadPlanta}|{t.IdCiudadDestino}|{t.IdTipoCarga}";
                if (!tramosVistos.Add(clave))
                {
                    res.Errores.Add("Terrestre: hay dos filas con el mismo transportista y el mismo tramo (retiro, planta, destino y tipo de carga).");
                    continue;
                }
                var repetida = await BuscarTramoTerrestreRepetidoAsync(t);
                if (repetida != null) res.Errores.Add("Terrestre: " + MensajeTramoRepetido(repetida).Replace("No se guardó: ", ""));
            }
            if (res.Errores.Any())
            {
                MensajeCargaMasivaCostos = "ERROR|No se cargó ninguna tarifa. Corrige y vuelve a subir:|" + string.Join("|", res.Errores.Take(25));
                TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                return RedirectToPage("./Index");
            }
            if (res.Total == 0)
            {
                MensajeCargaMasivaCostos = "ERROR|El archivo no trae filas con datos.";
                TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                return RedirectToPage("./Index");
            }

            // Documental: igual que el formulario, la nueva tarifa reemplaza a la vigente del
            // mismo trámite en la misma agencia.
            foreach (var d in res.Documentales)
            {
                var anteriores = await _context.TarifasDocumentales
                    .Where(t => t.EsActiva && t.IdAgenciaAduana == d.IdAgenciaAduana && t.Concepto == d.Concepto)
                    .ToListAsync();
                foreach (var a in anteriores) { a.EsActiva = false; a.FechaModificacion = DateTime.Now; a.UsuarioModificador = usuario; }
            }
            _context.TarifasMaritimas.AddRange(res.Maritimas);
            _context.Tarifasterrestres.AddRange(res.Terrestres);
            _context.TarifasGate.AddRange(res.Gates);
            _context.TarifasDocumentales.AddRange(res.Documentales);
            await _context.SaveChangesAsync();

            string origen = $"Carga masiva ({archivoCostos.FileName})";
            foreach (var t in res.Maritimas) RegistrarHistorialComercial("Maritimo", t.Id, "Alta", null, $"USD {t.TarifaUsd:N2} · {t.Pol} → {t.Pod} · {t.Equipamiento}", origen);
            foreach (var t in res.Terrestres) RegistrarHistorialComercial("Terrestre", t.Id, "Alta", null, $"CLP {t.ValorNeto:N0}", origen);
            foreach (var t in res.Gates) RegistrarHistorialComercial("Gate", t.Id, "Alta", null, $"{t.Moneda} {t.ValorNeto:N0} · {t.TipoMovimiento} {t.TipoContenedor}", origen);
            foreach (var t in res.Documentales) RegistrarHistorialComercial("Documental", t.Id, "Alta", null, $"{t.Moneda} {t.ValorNeto:N0} · {t.Concepto}", origen);
            await _context.SaveChangesAsync();

            if (res.Maritimas.Any()) await ResincronizarPendientesAsync("Marítimo");
            if (res.Terrestres.Any()) await ResincronizarPendientesAsync("Transporte");
            if (res.Gates.Any()) await ResincronizarPendientesAsync("Depósito");
            if (res.Documentales.Any()) await ResincronizarPendientesAsync("Documental");

            var detalle = new List<string>();
            if (res.Maritimas.Any()) detalle.Add($"{res.Maritimas.Count} marítimas");
            if (res.Terrestres.Any()) detalle.Add($"{res.Terrestres.Count} terrestres");
            if (res.Gates.Any()) detalle.Add($"{res.Gates.Count} de Gate");
            if (res.Documentales.Any()) detalle.Add($"{res.Documentales.Count} documentales");
            MensajeCargaMasivaCostos = $"OK|Se cargaron {res.Total} tarifas de costo{EtiquetaCarga(servicio, concepto)} ({string.Join(", ", detalle)}) desde {res.FilasLeidas} filas del archivo.";
            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostCargaMasivaVentasAsync(Microsoft.AspNetCore.Http.IFormFile? archivoVentas, string? servicio, string? concepto)
        {
            if (archivoVentas == null || archivoVentas.Length == 0)
            {
                MensajeCargaMasiva = "ERROR|Selecciona un archivo Excel (.xlsx).";
                TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                return RedirectToPage("./Index");
            }
            var clientes = await _context.Clientes.AsNoTracking().Where(c => c.Activo == 1).ToListAsync();
            var navieras = await _context.Navieras.AsNoTracking().ToListAsync();
            var depositos = await _context.Depositos.AsNoTracking().ToListAsync();
            string usuario = User.Identity?.Name ?? "Comercial";

            AtilsonCargoSpa.Helpers.CargaMasivaVentasHelper.Resultado res;
            try
            {
                using var stream = new System.IO.MemoryStream();
                await archivoVentas.CopyToAsync(stream);
                stream.Position = 0;
                var fuera = FilasFueraDeServicio(stream, TipoVentas(), servicio, concepto);
                if (fuera != null && fuera.Count > 0)
                {
                    MensajeCargaMasiva = $"ERROR|No se cargó ninguna tarifa: la carga{EtiquetaCarga(servicio, concepto)} trae filas de otro servicio o concepto. Corrige y vuelve a subir:|" + string.Join("|", fuera.Take(25));
                    TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                    return RedirectToPage("./Index");
                }
                res = AtilsonCargoSpa.Helpers.CargaMasivaVentasHelper.Leer(stream, clientes, navieras, depositos, usuario,
                    (await AtilsonCargoSpa.Helpers.PuertosHelper.MapaPaisesAsync(_context)).PorNombre);
            }
            catch (Exception)
            {
                MensajeCargaMasiva = "ERROR|No se pudo leer el archivo. Usa la plantilla descargada desde esta misma pantalla (formato .xlsx).";
                TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                return RedirectToPage("./Index");
            }

            // Todo o nada: si alguna fila tiene error no se guarda ninguna, para no dejar
            // cargas a medias que después cuesta encontrar.
            if (res.Errores.Any())
            {
                MensajeCargaMasiva = "ERROR|No se cargó ninguna tarifa. Corrige y vuelve a subir:|" + string.Join("|", res.Errores.Take(25))
                    + (res.Errores.Count > 25 ? $"|… y {res.Errores.Count - 25} errores más." : "");
                TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                return RedirectToPage("./Index");
            }
            if (!res.Tarifas.Any())
            {
                MensajeCargaMasiva = "ERROR|El archivo no trae filas con datos.";
                TempData["ToastError"] = "Carga masiva: no se guardó ninguna tarifa. El detalle quedó sobre la tabla.";
                return RedirectToPage("./Index");
            }

            _context.TarifasClientes.AddRange(res.Tarifas);
            await _context.SaveChangesAsync();
            foreach (var t in res.Tarifas)
                RegistrarHistorialComercial("Cliente", t.Id, "Alta", null, $"{t.Moneda} {t.PrecioPactado:N2}", $"Carga masiva ({archivoVentas.FileName})");
            await _context.SaveChangesAsync();

            var grupos = new Dictionary<string, string> { ["Marítimo"] = "Marítimo", ["Terrestre"] = "Transporte", ["Gate"] = "Depósito", ["Courier"] = "Documental", ["Almacenamiento"] = "Almacenamiento" };
            foreach (var par in res.Tarifas.Select(t => new { t.IdCliente, t.GrupoCobro }).Distinct())
            {
                if (par.GrupoCobro != null && grupos.TryGetValue(par.GrupoCobro, out var grupoFin))
                    await ResincronizarVentasClienteAsync(par.IdCliente, grupoFin);
            }

            MensajeCargaMasiva = $"OK|Se cargaron {res.Tarifas.Count} tarifas de venta{EtiquetaCarga(servicio, concepto)} ({res.FilasLeidas} filas del archivo).";
            return RedirectToPage("./Index");
        }

        // ==================== GATE OUT / IN ====================
        // Venta Gate a cliente: misma estructura que el costo de proveedores (depósito,
        // naviera, movimiento, contenedor). El depósito se guarda en Pol y el
        // movimiento en el Concepto ("GATE OUT" / "GATE IN"); vacío = aplica a todos.
        public async Task<IActionResult> OnPostCrearVentaGateAsync()
        {
            string mov = (Request.Form["MovimientoGateVenta"].ToString() ?? "").Trim().ToUpperInvariant();
            var movimientos = mov == "AMBOS" ? new[] { "OUT", "IN" } : (mov == "IN" ? new[] { "IN" } : new[] { "OUT" });
            NuevaVentaGate.Concepto = null;
            PrepararVentaCliente(NuevaVentaGate, "Gate", nameof(NuevaVentaGate));
            NuevaVentaGate.Pol = string.IsNullOrWhiteSpace(NuevaVentaGate.Pol) ? null : NuevaVentaGate.Pol.Trim().ToUpper();
            NuevaVentaGate.TipoContenedor = NormalizarContenedorGate(NuevaVentaGate.TipoContenedor);
            if (NuevaVentaGate.IdCliente is null or <= 0 || NuevaVentaGate.PrecioPactado <= 0) { TempData["ToastError"] = "No se guardó: elige el cliente e ingresa un precio mayor a cero."; return RedirectToPage("./Index"); }

            foreach (var m in movimientos)
            {
                var fila = CopiarVentaGate(NuevaVentaGate);
                fila.Concepto = "GATE " + m;
                _context.TarifasClientes.Add(fila);
            }
            await _context.SaveChangesAsync();
            await ResincronizarVentasClienteAsync(NuevaVentaGate.IdCliente, "Depósito");
            return RedirectToPage("./Index");
        }

        private static TarifasCliente CopiarVentaGate(TarifasCliente o) => new TarifasCliente
        {
            IdCliente = o.IdCliente,
            PrecioPactado = o.PrecioPactado,
            Moneda = o.Moneda,
            FechaInicioVigencia = o.FechaInicioVigencia,
            FechaFinVigencia = o.FechaFinVigencia,
            EsActiva = true,
            UsuarioCreador = o.UsuarioCreador,
            GrupoCobro = "Gate",
            Pol = o.Pol,
            IdNaviera = o.IdNaviera,
            TipoContenedor = o.TipoContenedor,
            AplicaIva = o.AplicaIva,
            Comentarios = o.Comentarios
        };

        // Contenedores de Gate (minuta oct-2026): solo 20' Dry, 40' Dry y 40' Reefer.
        // "DRY" / "RF" (sin tamaño) = aplica a 20 y 40 del mismo tipo.
        private static string? NormalizarContenedorGate(string? tc)
        {
            var t = (tc ?? "").Trim().ToUpperInvariant().Replace(" ", "").Replace("'", "");
            return t switch
            {
                "20DRY" or "40DRY" or "40RF" or "DRY" or "RF" => t,
                "40REEFER" => "40RF",
                "REEFER" => "RF",
                _ => null
            };
        }

        // Costo Gate de proveedor (depósito o naviera). Para tarifas desglosadas
        // (CMA CGM: Gate Out + Control Sello + BL Fee) se crea una fila por componente,
        // que es como las lee Finanzas; la vista las muestra en una sola línea.
        public async Task<IActionResult> OnPostCrearTarifaGateAsync()
        {
            ReparsearDecimales(NuevaTarifaGate, nameof(NuevaTarifaGate));
            if (NuevaTarifaGate.IdDeposito == null && NuevaTarifaGate.IdNaviera == null) { TempData["ToastError"] = "No se guardó: elige el depósito o la naviera del Gate."; return RedirectToPage("./Index"); }
            string mov = (NuevaTarifaGate.TipoMovimiento ?? "").Trim().ToUpperInvariant();
            var movimientos = mov == "AMBOS" ? new[] { "OUT", "IN" } : (mov == "IN" ? new[] { "IN" } : new[] { "OUT" });
            string? tc = NormalizarContenedorGate(NuevaTarifaGate.TipoContenedor);
            if (tc == null) { TempData["ToastError"] = "No se guardó: elige el tipo de contenedor del Gate."; return RedirectToPage("./Index"); }
            int idTipoCarga = tc.Contains("RF") ? 2 : 1;
            string moneda = string.IsNullOrWhiteSpace(NuevaTarifaGate.Moneda) ? "CLP" : NuevaTarifaGate.Moneda.Trim().ToUpperInvariant();
            DateTime inicio = NuevaTarifaGate.FechaInicioVigencia is { Year: >= 2000 } fi ? fi : DateTime.Today;
            DateTime? fin = NuevaTarifaGate.FechaFinVigencia is { Year: >= 2000 } ff ? ff : null;
            string usuario = User.Identity?.Name ?? "Comercial";

            TryParseMonto(Request.Form["GateControlSello"], out decimal controlSello);
            TryParseMonto(Request.Form["GateBlFee"], out decimal blFee);
            bool desglosada = controlSello > 0 || blFee > 0;
            decimal valorGate = NuevaTarifaGate.ValorNeto ?? 0m;
            // Oct-2026: "Sin costo" permite cargar un Gate en $0 (el depósito no cobra); Finanzas
            // lo muestra como sin costo en vez de "por asignar".
            bool sinCosto = Request.Form["GateSinCosto"].ToString() == "true";
            if (sinCosto) { valorGate = 0m; desglosada = false; }
            if (valorGate <= 0 && !desglosada && !sinCosto) { TempData["ToastError"] = "No se guardó: ingresa el valor del Gate (o marca «Sin costo»)."; return RedirectToPage("./Index"); }

            foreach (var m in movimientos)
            {
                void Agregar(decimal valor, string? componente) => _context.TarifasGate.Add(new TarifaGate
                {
                    IdNaviera = NuevaTarifaGate.IdNaviera,
                    IdDeposito = NuevaTarifaGate.IdDeposito,
                    IdTipoCarga = idTipoCarga,
                    TipoContenedor = tc,
                    TipoMovimiento = m,
                    ValorNeto = valor,
                    Moneda = moneda,
                    Componente = componente,
                    FechaInicioVigencia = inicio,
                    FechaFinVigencia = fin,
                    EsActiva = true,
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = usuario
                });

                if (desglosada)
                {
                    Agregar(valorGate, m == "IN" ? "GATE_IN" : "GATE_OUT");
                    if (controlSello > 0) Agregar(controlSello, "CONTROL_SELLO");
                    if (blFee > 0) Agregar(blFee, "BL_FEE");
                }
                else
                {
                    Agregar(valorGate, null);
                }
            }
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Depósito");
            return RedirectToPage("./Index");
        }

        // Checkbox Control Sello / BL Fee de la línea CMA CGM: activa o desactiva ese
        // componente (Finanzas solo suma los activos) y lo deja en el historial.
        public async Task<IActionResult> OnPostToggleComponenteGateAsync(int idTarifa, bool activo)
        {
            var t = await _context.TarifasGate.FindAsync(idTarifa);
            if (t == null || string.IsNullOrEmpty(t.Componente)) return new JsonResult(new { ok = false });
            if (t.EsActiva != activo)
            {
                string usuario = User.Identity?.Name ?? "Comercial";
                RegistrarHistorialComercial("Gate", t.Id, $"Componente {t.Componente}", t.EsActiva ? "Incluido" : "Excluido", activo ? "Incluido" : "Excluido", "Cambio desde checkbox de la tabla Gate");
                t.EsActiva = activo;
                t.FechaModificacion = DateTime.Now;
                t.UsuarioModificador = usuario;
                await _context.SaveChangesAsync();
                await ResincronizarPendientesAsync("Depósito");
            }
            return new JsonResult(new { ok = true, activo = t.EsActiva });
        }
        public async Task<IActionResult> OnPostCrearTarifaAlmacenamientoAsync() { ReparsearDecimales(NuevaTarifaAlmacenamiento, nameof(NuevaTarifaAlmacenamiento)); NuevaTarifaAlmacenamiento.EsActiva = true; NuevaTarifaAlmacenamiento.FechaCreacion = DateTime.Now; NuevaTarifaAlmacenamiento.UsuarioCreador = User.Identity?.Name ?? "Comercial"; _context.TarifasAlmacenamientos.Add(NuevaTarifaAlmacenamiento); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }

        public async Task<IActionResult> OnPostCrearDetalleAlmacenamientoAsync()
        {
            ReparsearDecimales(NuevoDetalleAlmacenamiento, nameof(NuevoDetalleAlmacenamiento));
            NuevoDetalleAlmacenamiento.EsActiva = true;
            NuevoDetalleAlmacenamiento.FechaCreacion = DateTime.Now;
            NuevoDetalleAlmacenamiento.UsuarioCreador = User.Identity?.Name ?? "Comercial";
            int maxOrden = await _context.TarifasAlmacenamientoDetalles
                .Where(t => t.IdProveedor == NuevoDetalleAlmacenamiento.IdProveedor && t.EsActiva)
                .Select(t => (int?)t.Orden).MaxAsync() ?? 0;
            NuevoDetalleAlmacenamiento.Orden = maxOrden + 1;
            _context.TarifasAlmacenamientoDetalles.Add(NuevoDetalleAlmacenamiento);
            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostEliminarDetalleAlmacenamientoAsync(int idDetalle)
        {
            var detalle = await _context.TarifasAlmacenamientoDetalles.FindAsync(idDetalle);
            if (detalle != null) { detalle.EsActiva = false; await _context.SaveChangesAsync(); }
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearCostoTerrestreAsync()
        {
            ReparsearDecimales(NuevoCostoTerrestre, nameof(NuevoCostoTerrestre));
            NuevoCostoTerrestre.FechaCreacion = DateTime.Now;
            NuevoCostoTerrestre.UsuarioCreador = User.Identity?.Name ?? "Comercial";
            if (!NuevoCostoTerrestre.HorasLibresPlanta.HasValue) NuevoCostoTerrestre.HorasLibresPlanta = 7;
            if (!NuevoCostoTerrestre.HorasLibresPuerto.HasValue) NuevoCostoTerrestre.HorasLibresPuerto = 3;
            if (NuevoCostoTerrestre.ValorNeto.HasValue)
            {
                if (!NuevoCostoTerrestre.FalsoFletePlanta.HasValue) NuevoCostoTerrestre.FalsoFletePlanta = NuevoCostoTerrestre.ValorNeto * 0.90m;
                if (!NuevoCostoTerrestre.FalsoFleteRutaMayor50.HasValue) NuevoCostoTerrestre.FalsoFleteRutaMayor50 = NuevoCostoTerrestre.ValorNeto * 0.80m;
                if (!NuevoCostoTerrestre.FalsoFleteRutaMenor50.HasValue) NuevoCostoTerrestre.FalsoFleteRutaMenor50 = NuevoCostoTerrestre.ValorNeto * 0.70m;
            }
            // nov-2026: no se permite un segundo tramo vigente igual (mismo transportista —o un
            // duplicado suyo en el maestro—, mismo retiro, planta, puerto y tipo de carga, con
            // vigencias que se cruzan). Antes quedaban dos y no era claro cuál tomaba Finanzas.
            var repetida = await BuscarTramoTerrestreRepetidoAsync(NuevoCostoTerrestre);
            if (repetida != null)
            {
                TempData["ToastError"] = MensajeTramoRepetido(repetida);
                return RedirectToPage("./Index");
            }
            _context.Tarifasterrestres.Add(NuevoCostoTerrestre);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Transporte");
            return RedirectToPage("./Index");
        }

        // Agrupación de tarifas terrestres por EMPRESA (nov-2026): un transportista duplicado
        // en el maestro (p. ej. dos "TRIGAL", uno desactivado) se muestra en una sola fila,
        // igual que lo trata Finanzas.
        public string ClaveTransportista(Tarifasterrestre t)
        {
            string nombre = t.IdProveedorNavigation?.NombreProveedor ?? "";
            string corto = AtilsonCargoSpa.Helpers.TarifaTransporteHelper.NormEmpresa(nombre);
            return corto.Length >= 3 ? corto : AtilsonCargoSpa.Helpers.TarifaTransporteHelper.Norm(nombre);
        }

        public Proveedore? ProveedorDelGrupo(IEnumerable<Tarifasterrestre> grupo) => grupo
            .Select(t => t.IdProveedorNavigation).Where(p => p != null)
            .OrderByDescending(p => p!.Activo == 1).ThenBy(p => p!.Id).FirstOrDefault();

        // Proveedores que Finanzas considera la MISMA empresa (TarifaTransporteHelper): mismo
        // nombre, mismo nombre sin "Transportes"/LTDA/SPA, o mismo RUT.
        private async Task<List<int>> MismoTransportistaAsync(int idProveedor)
        {
            var todos = await _context.Proveedores.AsNoTracking().Select(p => new { p.Id, p.NombreProveedor, p.Rut }).ToListAsync();
            var prov = todos.FirstOrDefault(p => p.Id == idProveedor);
            if (prov == null) return new List<int> { idProveedor };
            string norm = AtilsonCargoSpa.Helpers.TarifaTransporteHelper.Norm(prov.NombreProveedor);
            string corto = AtilsonCargoSpa.Helpers.TarifaTransporteHelper.NormEmpresa(prov.NombreProveedor);
            string rut = AtilsonCargoSpa.Helpers.TarifaTransporteHelper.NormRut(prov.Rut);
            return todos.Where(p => p.Id == idProveedor
                    || AtilsonCargoSpa.Helpers.TarifaTransporteHelper.Norm(p.NombreProveedor) == norm
                    || (corto.Length >= 3 && AtilsonCargoSpa.Helpers.TarifaTransporteHelper.NormEmpresa(p.NombreProveedor) == corto)
                    || (rut != "" && AtilsonCargoSpa.Helpers.TarifaTransporteHelper.NormRut(p.Rut) == rut))
                .Select(p => p.Id).ToList();
        }

        private async Task<Tarifasterrestre?> BuscarTramoTerrestreRepetidoAsync(Tarifasterrestre nueva, int? excluirId = null)
        {
            if (!nueva.IdProveedor.HasValue) return null;
            var ids = await MismoTransportistaAsync(nueva.IdProveedor.Value);
            var candidatas = await _context.Tarifasterrestres.AsNoTracking()
                .Include(t => t.IdProveedorNavigation)
                .Include(t => t.IdCiudadOrigenNavigation).Include(t => t.IdCiudadPlantaNavigation).Include(t => t.IdCiudadDestinoNavigation)
                .Where(t => t.EsActiva && t.IdProveedor != null && ids.Contains(t.IdProveedor.Value)
                    && t.IdCiudadOrigen == nueva.IdCiudadOrigen && t.IdCiudadPlanta == nueva.IdCiudadPlanta && t.IdCiudadDestino == nueva.IdCiudadDestino
                    && (t.IdTipoCarga == nueva.IdTipoCarga || t.IdTipoCarga == null || nueva.IdTipoCarga == null)
                    && (excluirId == null || t.Id != excluirId))
                .ToListAsync();
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            DateOnly bIni = nueva.FechaInicioVigencia ?? hoy, bFin = nueva.FechaFinVigencia ?? DateOnly.MaxValue;
            return candidatas
                .Where(t => (t.FechaInicioVigencia ?? DateOnly.MinValue) <= bFin && bIni <= (t.FechaFinVigencia ?? DateOnly.MaxValue))
                .OrderByDescending(t => t.Id).FirstOrDefault();
        }

        private static string MensajeTramoRepetido(Tarifasterrestre t)
        {
            string tramo = $"{t.IdCiudadOrigenNavigation?.Nombre ?? "?"} → {t.IdCiudadPlantaNavigation?.Nombre ?? "sin planta"} → {t.IdCiudadDestinoNavigation?.Nombre ?? "?"}";
            string tipo = t.IdTipoCarga == 2 ? "Reefer" : t.IdTipoCarga == 1 ? "Dry" : "Dry/Reefer";
            string vig = (t.FechaInicioVigencia.HasValue ? $"desde {t.FechaInicioVigencia:dd-MM-yyyy}" : "")
                + (t.FechaFinVigencia.HasValue ? $" hasta {t.FechaFinVigencia:dd-MM-yyyy}" : " sin fecha de término");
            return $"No se guardó: {t.IdProveedorNavigation?.NombreProveedor ?? "el transportista"} ya tiene ese tramo ({tramo}, {tipo}) en $ {t.ValorNeto:N0}, vigente {vig.Trim()}. "
                + "Cambia el valor en esa fila (lápiz) o, si la tarifa nueva parte otro día, ponle «Vigente hasta» a la anterior.";
        }
        public async Task<IActionResult> OnPostCrearTarifaDocumentalAsync() { ReparsearDecimales(NuevaTarifaDocumental, nameof(NuevaTarifaDocumental)); NuevaTarifaDocumental.Categoria = "Documental"; NuevaTarifaDocumental.EsActiva = true; _context.TarifasMaestras.Add(NuevaTarifaDocumental); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostCrearTarifaCourierAsync()
        {
            NuevaTarifaCourier.Categoria = "Courier";
            NuevaTarifaCourier.EsActiva = true;
            ReparsearDecimales(NuevaTarifaCourier, nameof(NuevaTarifaCourier));
            // Empresa de courier (DHL/FedEx/Chilexpress) en Clasificacion y contrato
            // (Stembex / Propio / Otra AGA) en Codigo — ver CourierHelper.
            NuevaTarifaCourier.Clasificacion = AtilsonCargoSpa.Helpers.CourierHelper.Normalizar(NuevaTarifaCourier.Clasificacion);
            NuevaTarifaCourier.Codigo = AtilsonCargoSpa.Helpers.CourierHelper.NormalizarContrato(Request.Form["ContratoCourierCosto"]);
            if (NuevaTarifaCourier.Clasificacion == null) { TempData["ToastError"] = "No se guardó: elige la empresa de courier."; return RedirectToPage("./Index"); }
            _context.TarifasMaestras.Add(NuevaTarifaCourier);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Documental");
            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostCrearTarifaMaritimoExtraAsync()
        {
            ReparsearDecimales(NuevaTarifaMaritimoExtra, nameof(NuevaTarifaMaritimoExtra));
            NuevaTarifaMaritimoExtra.Categoria = "Marítimo";
            NuevaTarifaMaritimoExtra.EsActiva = true;
            NuevaTarifaMaritimoExtra.FechaModificacion = DateTime.Now;
            NuevaTarifaMaritimoExtra.UsuarioModificador = User.Identity?.Name ?? "Comercial";
            _context.TarifasMaestras.Add(NuevaTarifaMaritimoExtra);
            await _context.SaveChangesAsync();
            RegistrarHistorialComercial("MaritimoExtra", NuevaTarifaMaritimoExtra.Id, "Creación", null,
                $"{NuevaTarifaMaritimoExtra.Moneda} {NuevaTarifaMaritimoExtra.ValorNeto:N2}", "Tarifa marítima creada desde Comercial");
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Marítimo");
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostEliminarTarifaMaritimoExtraAsync(int idTarifa)
        {
            var tarifa = await _context.TarifasMaestras.FindAsync(idTarifa);
            if (tarifa != null)
            {
                tarifa.EsActiva = false;
                tarifa.FechaModificacion = DateTime.Now;
                tarifa.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                RegistrarHistorialComercial("MaritimoExtra", tarifa.Id, "Estado", "Activa", "Archivada", "Archivada desde Comercial › Marítimo › Otros");
                await _context.SaveChangesAsync();
                await ResincronizarPendientesAsync("Marítimo");
            }
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearCostoDocumentalAsync()
        {
            ReparsearDecimales(NuevoCostoDocumental, nameof(NuevoCostoDocumental));
            var tarifaAnterior = await _context.TarifasDocumentales.FirstOrDefaultAsync(t => t.IdAgenciaAduana == NuevoCostoDocumental.IdAgenciaAduana && t.Concepto == NuevoCostoDocumental.Concepto && t.EsActiva == true);
            if (tarifaAnterior != null) tarifaAnterior.EsActiva = false;
            NuevoCostoDocumental.EsActiva = true;
            NuevoCostoDocumental.FechaCreacion = DateTime.Now;
            _context.TarifasDocumentales.Add(NuevoCostoDocumental);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Documental");
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearProveedorAsync()
        {
            NuevoProveedor.Activo = 1;
            // El modal es "Nuevo Proveedor de Transporte": sin tipo quedaba en 0 y no
            // aparecía en el selector de transportistas de Operaciones.
            if (NuevoProveedor.IdTipoProveedor <= 0) NuevoProveedor.IdTipoProveedor = 2;
            NuevoProveedor.NombreProveedor = (NuevoProveedor.NombreProveedor ?? "").Trim().ToUpper();
            _context.Proveedores.Add(NuevoProveedor);
            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearAgenciaAduanaAsync() { NuevaAgencia.Activo = 1; NuevaAgencia.FechaCreacion = DateTime.Now; NuevaAgencia.UsuarioCreador = User.Identity?.Name ?? "Comercial"; _context.AgenciasAduanas.Add(NuevaAgencia); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostCrearConductorAsync() { NuevoConductor.Activo = true; _context.Conductores.Add(NuevoConductor); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }
        // ==================== HANDLERS DE ELIMINACIÓN (ARCHIVADO LÓGICO/FÍSICO) ====================
        public async Task<IActionResult> OnPostEliminarTarifaClienteAsync(int idTarifa) { var tarifa = await _context.TarifasClientes.FindAsync(idTarifa); if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }

        // Checklist rápido de "Tarifa Plana" (Comercial > Clientes > Documental, agrupado por cliente):
        // tildar un certificado que el cliente no tiene todavía lo agrega de inmediato como GRATIS
        // ($0, vigencia 1 año) sin pasar por el formulario completo; destildar uno que ya tiene lo
        // archiva (misma acción que el botón ARCHIVAR de la tabla de detalle). Vía AJAX: no recarga
        // la página sola, el JS del front decide cuándo recargar.
        public async Task<IActionResult> OnPostToggleCertificadoGratuitoAsync(int idCliente, string clasificacion, string concepto, bool incluir)
        {
            if (string.IsNullOrWhiteSpace(clasificacion) || string.IsNullOrWhiteSpace(concepto))
                return new JsonResult(new { success = false, message = "Datos incompletos." });

            string usuario = User.Identity?.Name ?? "Comercial";
            DateTime ahora = DateTime.Now;
            string conceptoUpper = concepto.Trim().ToUpper();

            var tarifa = await _context.TarifasClientes.FirstOrDefaultAsync(t =>
                t.IdCliente == idCliente &&
                t.GrupoCobro == "Documental" &&
                t.Clasificacion == clasificacion &&
                t.Concepto != null && t.Concepto.ToUpper() == conceptoUpper);

            bool debeResincronizar = false;

            if (incluir)
            {
                if (tarifa == null)
                {
                    // Valores por defecto desde el catálogo maestro (moneda), si existe.
                    var maestro = await _context.TarifasMaestras.FirstOrDefaultAsync(m =>
                        m.EsActiva && m.Categoria == "Documental" && m.Concepto.ToUpper() == conceptoUpper);

                    tarifa = new TarifasCliente
                    {
                        IdCliente = idCliente,
                        GrupoCobro = "Documental",
                        Clasificacion = clasificacion,
                        Concepto = conceptoUpper,
                        Moneda = maestro?.Moneda ?? "CLP",
                        PrecioPactado = 0,
                        AplicaIva = false,
                        EsServicioGratuito = true,
                        EsActiva = true,
                        FechaInicioVigencia = ahora,
                        FechaFinVigencia = ahora.AddYears(1),
                        UsuarioCreador = usuario,
                        UsuarioModificador = usuario,
                        FechaModificacion = ahora
                    };
                    _context.TarifasClientes.Add(tarifa);
                    debeResincronizar = true;
                }
                else
                {
                    bool gratuitoAnterior = tarifa.EsServicioGratuito;
                    bool estabaActiva = tarifa.EsActiva;
                    if (!gratuitoAnterior) RegistrarHistorialComercial("Cliente", tarifa.Id, "Servicio Gratuito (Tarifa Plana)", "NO", "SÍ", "Marcado vía checklist de tarifa plana (Comercial > Documental)");
                    if (!estabaActiva) RegistrarHistorialComercial("Cliente", tarifa.Id, "Estado", "Archivado", "Activo", "Reactivado vía checklist de tarifa plana");
                    tarifa.EsServicioGratuito = true;
                    tarifa.EsActiva = true;
                    if (tarifa.FechaFinVigencia < ahora) tarifa.FechaFinVigencia = ahora.AddYears(1);
                    tarifa.UsuarioModificador = usuario;
                    tarifa.FechaModificacion = ahora;
                    if (!gratuitoAnterior || !estabaActiva) debeResincronizar = true;
                }
            }
            else
            {
                if (tarifa != null && tarifa.EsActiva)
                {
                    RegistrarHistorialComercial("Cliente", tarifa.Id, "Estado", "Activo", "Archivado", "Quitado vía checklist de tarifa plana (Comercial > Documental)");
                    tarifa.EsActiva = false;
                    tarifa.UsuarioModificador = usuario;
                    tarifa.FechaModificacion = ahora;
                    debeResincronizar = true;
                }
            }

            await _context.SaveChangesAsync();
            if (debeResincronizar) await ResincronizarOperacionesClienteAsync(idCliente);

            return new JsonResult(new { success = true });
        }

        // Activa/desactiva "Tarifa Plana" para un cliente (Cliente.EsClienteEspecial). Hasta esta
        // pestaña, este flag solo se podía editar directo en la base de datos. Desactivarlo NO
        // borra ni archiva las filas de TarifasClientes ya marcadas como gratuitas (no destructivo
        // por diseño) — simplemente deja de aplicar la lógica de tarifa plana al re-sincronizar.
        public async Task<IActionResult> OnPostToggleClienteEspecialAsync(int idCliente, bool activo)
        {
            var cliente = await _context.Clientes.FindAsync(idCliente);
            if (cliente == null)
                return new JsonResult(new { success = false, message = "Cliente no encontrado." });

            bool anterior = cliente.EsClienteEspecial;
            if (anterior != activo)
            {
                cliente.EsClienteEspecial = activo;
                cliente.FechaModificacion = DateTime.Now;
                cliente.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                RegistrarHistorialComercial("ClienteEspecial", cliente.Id, "Tarifa Plana (Cliente Especial)",
                    anterior ? "SÍ" : "NO", activo ? "SÍ" : "NO", "Cambiado desde la pestaña Tarifa Plana (Comercial > Documental)");
                await _context.SaveChangesAsync();
                await ResincronizarOperacionesClienteAsync(idCliente);
            }

            return new JsonResult(new { success = true });
        }
        public async Task<IActionResult> OnPostEliminarCostoMaritimoAsync(int idTarifa)
        {
            var tarifa = await _context.TarifasMaritimas.FindAsync(idTarifa);
            if (tarifa != null)
            {
                tarifa.EsActiva = false;
                tarifa.FechaModificacion = DateTime.Now;
                tarifa.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                RegistrarHistorialComercial("Maritimo", tarifa.Id, "Estado", "Activa", "Archivada", "Archivado lógico; la tarifa se conserva para trazabilidad");
                await _context.SaveChangesAsync();
                await ResincronizarPendientesAsync("Marítimo");
            }
            return RedirectToPage("./Index");
        }
        

        public async Task<IActionResult> OnPostGuardarDetalleAlmacenamientoAsync(int idTarifa, string? comentarios)
        {
            var t = await _context.TarifasAlmacenamientos.FindAsync(idTarifa);
            if (t != null)
            {
                string? anterior = t.Comentarios;
                string? nuevo = string.IsNullOrWhiteSpace(comentarios) ? null : comentarios.Trim();
                if (anterior != nuevo)
                {
                    t.Comentarios = nuevo;
                    t.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                    t.FechaModificacion = DateTime.Now;
                    RegistrarHistorialComercial("Almacenamiento", t.Id, "Detalle / Observaciones", anterior, nuevo, "Edición desde la columna Detalle");
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostEliminarTarifaAlmacenamientoAsync(int idTarifa) { var tarifa = await _context.TarifasAlmacenamientos.FindAsync(idTarifa); if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostEliminarCostoTerrestreAsync(int idTarifa)
        {
            var tarifa = await _context.Tarifasterrestres.FindAsync(idTarifa);
            if (tarifa != null)
            {
                tarifa.EsActiva = false;
                tarifa.FechaModificacion = DateTime.Now;
                tarifa.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                RegistrarHistorialComercial("Terrestre", tarifa.Id, "Estado", "Activa", "Archivada", "Archivado lógico; la tarifa se conserva para trazabilidad");
                await _context.SaveChangesAsync();
                await ResincronizarPendientesAsync("Transporte");
            }
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostEliminarTarifaDocumentalAsync(int idTarifa) { var tarifa = await _context.TarifasMaestras.FindAsync(idTarifa); if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostEliminarCostoDocumentalAsync(int idTarifa) { var tarifa = await _context.TarifasDocumentales.FindAsync(idTarifa); if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); } return RedirectToPage(); }
        public async Task<IActionResult> OnPostEliminarTarifaGateAsync(int idTarifa)
        {
            var tarifa = await _context.TarifasGate.FindAsync(idTarifa);
            if (tarifa != null)
            {
                // Una tarifa desglosada (CMA CGM) se ve como una sola línea: se archivan
                // juntos todos sus componentes.
                var grupo = string.IsNullOrEmpty(tarifa.Componente)
                    ? new List<TarifaGate> { tarifa }
                    : await _context.TarifasGate.Where(t => t.IdNaviera == tarifa.IdNaviera && t.IdDeposito == tarifa.IdDeposito
                            && t.TipoMovimiento == tarifa.TipoMovimiento && t.TipoContenedor == tarifa.TipoContenedor && t.Componente != null).ToListAsync();
                string usuario = User.Identity?.Name ?? "Comercial";
                foreach (var g in grupo)
                {
                    g.EsActiva = false;
                    g.FechaModificacion = DateTime.Now;
                    g.UsuarioModificador = usuario;
                    RegistrarHistorialComercial("Gate", g.Id, "Estado", "Activa", "Archivada", null);
                }
                await _context.SaveChangesAsync();
            }
            return RedirectToPage("./Index");
        }
    }
}