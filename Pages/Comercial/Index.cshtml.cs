using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
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

        // ===== Utilidades de formularios (oct-2026) =====
        // Los <input type=number> envían el monto con punto decimal ("75.50"). Si el
        // servidor corre en es-CL, el binder lo interpreta con coma decimal y guarda
        // 7550. Esto re-lee cada decimal del formulario de forma independiente de la
        // cultura (acepta también "1.234,5" y "1234,5" si se escribió a mano).
        private static bool TryParseMonto(string? texto, out decimal valor)
        {
            valor = 0m;
            var t = (texto ?? "").Trim().Replace(" ", "").Replace("$", "");
            if (t == "") return false;
            if (t.Contains(',') && t.Contains('.')) t = t.Replace(".", "").Replace(",", ".");
            else if (t.Contains(',')) t = t.Replace(",", ".");
            return decimal.TryParse(t, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out valor);
        }

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
            // Fechas por defecto en los formularios de venta (antes salían en 01-01-0001).
            foreach (var venta in new[] { NuevaVentaMaritima, NuevaVentaTerrestre, NuevaVentaAlmacenamiento, NuevaVentaCourier, NuevaVentaGate })
            {
                venta.FechaInicioVigencia = DateTime.Today;
                venta.FechaFinVigencia = DateTime.Today.AddYears(1);
            }
            ListaAgencias = new SelectList(await _context.AgenciasAduanas.Where(a => a.Activo == 1).ToListAsync(), "Id", "NombreAgencia");
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
            var clientes = await _context.Clientes.Where(c => c.Activo == 1).OrderBy(c => c.RazonSocial).ToListAsync();
            ListaClientes = new SelectList(clientes.Select(c => new { Id = c.Id, RazonSocial = c.RazonSocial?.ToUpper() }), "Id", "RazonSocial");
            var proveedores = await _context.Proveedores.Where(p => p.Activo == 1).OrderBy(p => p.NombreProveedor).ToListAsync();
            ListaProveedores = new SelectList(proveedores.Select(p => new { Id = p.Id, NombreProveedor = p.NombreProveedor?.ToUpper() }), "Id", "NombreProveedor");
            ListaTransportistas = new SelectList(proveedores.Where(p => p.IdTipoProveedor != 12).Select(p => new { Id = p.Id, NombreProveedor = p.NombreProveedor?.ToUpper() }), "Id", "NombreProveedor");
            var ciudadesList = await _context.Set<Ciudade>().OrderBy(c => c.Nombre).ToListAsync();
            var ciudadesUpper = ciudadesList.Select(c => new { Id = c.Id, Nombre = c.Nombre?.ToUpper() }).ToList();
            ListaCiudades = new SelectList(ciudadesUpper, "Id", "Nombre");
            ViewData["ListaCiudadesNombre"] = new SelectList(ciudadesUpper, "Nombre", "Nombre");
            var navieras = await _context.Navieras.Where(n => n.Activo == 1).OrderBy(n => n.NombreNaviera).ToListAsync();
            ListaNavieras = new SelectList(navieras.Select(n => new { Id = n.Id, NombreNaviera = n.NombreNaviera?.ToUpper() }), "Id", "NombreNaviera");
            // Puertos maestros (misma tabla que usa Operaciones/Create)
            // por nombre para que el value guardado en TarifasMaritimas.Pol/Pod sea
            // siempre idéntico al NombrePuerto usado en el resto del sistema.
            var puertosMaestroDb = await _context.Puertos.Where(p => p.Activo == 1).OrderBy(p => p.NombrePuerto).ToListAsync();
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
            var conceptosDoc = conceptosDocMaestro.Concat(ventasDocumentales.Select(v => v.Concepto)).Distinct().OrderBy(c => c).ToList();
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
            // ISPS y SMS (acuerdo Promar, minuta oct-2026) siempre disponibles para marcar si
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
            TarifasGate = await _context.TarifasGate
                .Include(t => t.IdDepositoNavigation)
                .Where(t => t.EsActiva)
                .OrderByDescending(t => t.Id)
                .ToListAsync();
            ComponentesGateInactivos = await _context.TarifasGate
                .Where(t => !t.EsActiva && t.Componente != null && (t.Componente == "CONTROL_SELLO" || t.Componente == "BL_FEE"))
                .ToListAsync();
            ListaDepositos = (await _context.Depositos.AsNoTracking().Where(d => d.Activo == null || d.Activo == 1).ToListAsync())
                .OrderBy(d => (d.NombreDeposito ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            TarifasAlmacenamientos = await _context.TarifasAlmacenamientos
    .Include(t => t.IdProveedorNavigation)
    .Where(t => t.EsActiva)
    .OrderByDescending(t => t.Id)
    .ToListAsync();
            DetallesAlmacenamiento = await _context.TarifasAlmacenamientoDetalles
                .Where(t => t.EsActiva)
                .OrderBy(t => t.IdProveedor).ThenBy(t => t.Orden)
                .ToListAsync();
            TarifasBl = await _context.TarifasBl
    .Include(t => t.IdNavieraNavigation)
    .Where(t => t.EsActiva)
    .OrderBy(t => t.IdNavieraNavigation!.NombreNaviera).ThenBy(t => t.TipoEmision)
    .ToListAsync();
            // 2. FILTRO MARÍTIMO (Costos)
            var queryMaritimas = _context.TarifasMaritimas.AsNoTracking().Include(t => t.IdNavieraNavigation).AsQueryable();
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
            TarifasMaestrasDocumentales = await _context.TarifasMaestras.Where(t => t.EsActiva && t.Categoria == "Documental").OrderByDescending(t => t.Id).ToListAsync();
            // Lista SEPARADA para Courier: antes se filtraba "Courier" dentro de
            // TarifasMaestrasDocumentales (que solo trae Categoria=="Documental"),
            // por lo que el panel de Courier siempre salía vacío. Se corrige aquí.
            TarifasMaestrasCourier = await _context.TarifasMaestras.Where(t => t.EsActiva && t.Categoria == "Courier").OrderByDescending(t => t.ValorNeto).ToListAsync();
            TarifasMaestrasMaritimo = await _context.TarifasMaestras
                .Include(t => t.IdNavieraNavigation)
                .Where(t => t.EsActiva && t.Categoria == "Marítimo")
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
                return RedirectToPage("./Index");
            }
            NuevaTarifaBl.TipoEmision = tipoCanon;
            NuevaTarifaBl.TipoEmisionOtro = tipoCanon == AtilsonCargoSpa.Helpers.EmisionBlHelper.Otro ? nombreOtro : null;
            NuevaTarifaBl.Moneda = NuevaTarifaBl.Moneda == "CLP" ? "CLP" : "USD";

            // <input type=number> siempre envía punto decimal; se lee con cultura
            // invariante para que "75.50" no se transforme en 7550 en un servidor es-CL.
            var valorTxt = Request.Form["NuevaTarifaBl.ValorNeto"].ToString().Trim();
            if (decimal.TryParse(valorTxt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var valorInv))
                NuevaTarifaBl.ValorNeto = valorInv;
            _context.TarifasBl.Add(NuevaTarifaBl);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Marítimo");
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
                    t.ValorNeto = ValorTarifaUniversal;
                    t.UsuarioModificador = usuario;
                    t.FechaModificacion = ahora;
                    RegistrarHistorialComercial("MaritimoExtra", t.Id, $"Tarifa: {t.Concepto}", $"{t.Moneda} {valorAnterior:N0}", $"{t.Moneda} {ValorTarifaUniversal:N0}", ComentarioAuditoria);
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
        public async Task<IActionResult> OnPostEditarCostoMaritimoAsync()
        {
            ReparsearDecimales(EditarCostoMaritimo, nameof(EditarCostoMaritimo));
            var tarifaExistente = await _context.TarifasMaritimas.FindAsync(EditarCostoMaritimo.Id);
            if (tarifaExistente != null && EditarCostoMaritimo.TarifaUsd > 0)
            {
                tarifaExistente.TarifaUsd = EditarCostoMaritimo.TarifaUsd;
                tarifaExistente.RebateUsd = EditarCostoMaritimo.RebateUsd;
                if (EditarCostoMaritimo.FechaInicioVigencia != default) tarifaExistente.FechaInicioVigencia = EditarCostoMaritimo.FechaInicioVigencia;
                tarifaExistente.FechaModificacion = DateTime.Now;
                tarifaExistente.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                await _context.SaveChangesAsync();
                await ResincronizarPendientesAsync("Marítimo");
            }
            return RedirectToPage("./Index");
        }
        // Modalidad Collect/No Prepaid: asigna o corrige el rebate que le devolvemos a
        // este cliente para una tarifa Marítimo ya existente (no crea tarifas nuevas).
        public async Task<IActionResult> OnPostEditarRebateClienteAsync(int idTarifaCliente, decimal? rebateUsd)
        {
            var tarifaCliente = await _context.TarifasClientes.FindAsync(idTarifaCliente);
            if (tarifaCliente != null)
            {
                tarifaCliente.RebateUsd = rebateUsd;
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
            await ResincronizarCollectAsync(nueva.IdNaviera, nueva.IdCliente);
            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostArchivarTarifaCollectAsync(int idTarifa)
        {
            var t = await _context.TarifasCollect.FirstOrDefaultAsync(x => x.Id == idTarifa);
            if (t != null)
            {
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
            if (NuevaVentaCourier.Clasificacion == null) return RedirectToPage("./Index");
            _context.TarifasClientes.Add(NuevaVentaCourier);
            await _context.SaveChangesAsync();
            await ResincronizarVentasClienteAsync(NuevaVentaCourier.IdCliente, "Documental");
            return RedirectToPage("./Index");
        }

        // ==================== CARGA MASIVA DE VENTAS ====================
        [TempData] public string? MensajeCargaMasiva { get; set; }

        public async Task<IActionResult> OnGetPlantillaVentasAsync()
        {
            var clientes = await _context.Clientes.AsNoTracking().Where(c => c.Activo == 1).OrderBy(c => c.RazonSocial).Select(c => c.RazonSocial).ToListAsync();
            var navieras = await _context.Navieras.AsNoTracking().Where(n => n.Activo == 1).OrderBy(n => n.NombreNaviera).Select(n => n.NombreNaviera ?? "").ToListAsync();
            var depositos = (await _context.Depositos.AsNoTracking().Where(d => d.Activo == null || d.Activo == 1).Select(d => d.NombreDeposito).ToListAsync())
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
            var puertos = await _context.Puertos.AsNoTracking().Where(p => p.Activo == 1).OrderBy(p => p.NombrePuerto).Select(p => p.NombrePuerto).Distinct().ToListAsync();
            var bytes = AtilsonCargoSpa.Helpers.CargaMasivaVentasHelper.GenerarPlantilla(clientes, navieras, depositos, puertos);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"plantilla_ventas_clientes_{DateTime.Today:yyyyMMdd}.xlsx");
        }

        public async Task<IActionResult> OnPostCargaMasivaVentasAsync(Microsoft.AspNetCore.Http.IFormFile? archivoVentas)
        {
            if (archivoVentas == null || archivoVentas.Length == 0)
            {
                MensajeCargaMasiva = "ERROR|Selecciona un archivo Excel (.xlsx).";
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
                res = AtilsonCargoSpa.Helpers.CargaMasivaVentasHelper.Leer(stream, clientes, navieras, depositos, usuario);
            }
            catch (Exception)
            {
                MensajeCargaMasiva = "ERROR|No se pudo leer el archivo. Usa la plantilla descargada desde esta misma pantalla (formato .xlsx).";
                return RedirectToPage("./Index");
            }

            // Todo o nada: si alguna fila tiene error no se guarda ninguna, para no dejar
            // cargas a medias que después cuesta encontrar.
            if (res.Errores.Any())
            {
                MensajeCargaMasiva = "ERROR|No se cargó ninguna tarifa. Corrige y vuelve a subir:|" + string.Join("|", res.Errores.Take(25))
                    + (res.Errores.Count > 25 ? $"|… y {res.Errores.Count - 25} errores más." : "");
                return RedirectToPage("./Index");
            }
            if (!res.Tarifas.Any())
            {
                MensajeCargaMasiva = "ERROR|El archivo no trae filas con datos.";
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

            MensajeCargaMasiva = $"OK|Se cargaron {res.Tarifas.Count} tarifas de venta ({res.FilasLeidas} filas del archivo).";
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
            if (NuevaVentaGate.IdCliente is null or <= 0 || NuevaVentaGate.PrecioPactado <= 0) return RedirectToPage("./Index");

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
            if (NuevaTarifaGate.IdDeposito == null && NuevaTarifaGate.IdNaviera == null) return RedirectToPage("./Index");
            string mov = (NuevaTarifaGate.TipoMovimiento ?? "").Trim().ToUpperInvariant();
            var movimientos = mov == "AMBOS" ? new[] { "OUT", "IN" } : (mov == "IN" ? new[] { "IN" } : new[] { "OUT" });
            string? tc = NormalizarContenedorGate(NuevaTarifaGate.TipoContenedor);
            if (tc == null) return RedirectToPage("./Index");
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
            if (valorGate <= 0 && !desglosada && !sinCosto) return RedirectToPage("./Index");

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
            _context.Tarifasterrestres.Add(NuevoCostoTerrestre);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Transporte");
            return RedirectToPage("./Index");
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
            if (NuevaTarifaCourier.Clasificacion == null) return RedirectToPage("./Index");
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
            _context.TarifasMaestras.Add(NuevaTarifaMaritimoExtra);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Marítimo");
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostEliminarTarifaMaritimoExtraAsync(int idTarifa)
        {
            var tarifa = await _context.TarifasMaestras.FindAsync(idTarifa);
            if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); }
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
        public async Task<IActionResult> OnPostEliminarCostoMaritimoAsync(int idTarifa) { var tarifa = await _context.TarifasMaritimas.FindAsync(idTarifa); if (tarifa != null) { _context.TarifasMaritimas.Remove(tarifa); await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
        

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
        public async Task<IActionResult> OnPostEliminarCostoTerrestreAsync(int idTarifa) { var tarifa = await _context.Tarifasterrestres.FindAsync(idTarifa); if (tarifa != null) { _context.Tarifasterrestres.Remove(tarifa); await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
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