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
        // ==================== LISTAS PARA TABLAS ====================
        public IList<TarifasCliente> TarifasClientes { get; set; } = default!;
        public IList<TarifasMaritima> TarifasMaritimas { get; set; } = default!;
        public IList<TarifasMaestra> TarifasMaestrasMaritimo { get; set; } = default!;
        [BindProperty] public TarifasMaestra NuevaTarifaMaritimoExtra { get; set; } = new();

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
        public SelectList ListaCiudades { get; set; } = default!;
        public SelectList ListaNavieras { get; set; } = default!;
        public SelectList ListaAgencias { get; set; } = default!;
        public SelectList ListaConceptosDocumentales { get; set; } = default!;
        public SelectList ListaPols { get; set; } = default!;
        public SelectList ListaPods { get; set; } = default!;
        // Catálogo maestro de Puertos (mismo origen que usa Operaciones para POL/POD),
        // para que Comercial deje de aceptar texto libre y ambos módulos compartan
        // exactamente los mismos nombres de puerto.
        public SelectList ListaPuertosMaestro { get; set; } = default!;
        public async Task OnGetAsync()
        {
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
            var conceptosDoc = await _context.TarifasMaestras
                .Where(t => t.EsActiva && t.Categoria == "Documental")
                .Select(t => t.Concepto.ToUpper())
                .Distinct()
                .ToListAsync();
            ListaConceptosDocumentales = new SelectList(conceptosDoc);
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
            TarifasGate = await _context.TarifasGate
                .Include(t => t.IdDepositoNavigation)
                .Where(t => t.EsActiva)
                .OrderByDescending(t => t.Id)
                .ToListAsync();
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
            if (NuevaTarifaBl.TipoEmision != "OTRO") NuevaTarifaBl.TipoEmisionOtro = null;
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
        public async Task<IActionResult> OnPostEditarTarifaUniversalAsync(int IdTarifaUniversal, string TipoEntidad, decimal ValorTarifaUniversal, DateTime? NuevaFechaVigenciaUniversal, string? ComentarioAuditoria, string? ComentariosTarifaUniversal)
        {
            string usuario = User.Identity?.Name ?? "Comercial";
            DateTime ahora = DateTime.Now;
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
                    t.PrecioPactado = ValorTarifaUniversal;
                    if (NuevaFechaVigenciaUniversal.HasValue) t.FechaFinVigencia = NuevaFechaVigenciaUniversal.Value;
                    t.UsuarioModificador = usuario;
                    t.FechaModificacion = ahora;
                    RegistrarHistorialComercial("Cliente", t.Id, $"Venta: {t.Concepto}", $"{t.Moneda} {valorAnterior:N0}", $"{t.Moneda} {ValorTarifaUniversal:N0}", ComentarioAuditoria);
                    if (NuevaFechaVigenciaUniversal.HasValue && fechaAnterior != NuevaFechaVigenciaUniversal.Value)
                        RegistrarHistorialComercial("Cliente", t.Id, "Vigencia", fechaAnterior.ToString("dd/MM/yyyy"), NuevaFechaVigenciaUniversal.Value.ToString("dd/MM/yyyy"), ComentarioAuditoria);
                }
            }
            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostEditarCostoMaritimoAsync()
        {
            var tarifaExistente = await _context.TarifasMaritimas.FindAsync(EditarCostoMaritimo.Id);
            if (tarifaExistente != null && EditarCostoMaritimo.TarifaUsd > 0)
            {
                tarifaExistente.TarifaUsd = EditarCostoMaritimo.TarifaUsd;
                if (EditarCostoMaritimo.FechaInicioVigencia != default) tarifaExistente.FechaInicioVigencia = EditarCostoMaritimo.FechaInicioVigencia;
                tarifaExistente.FechaModificacion = DateTime.Now;
                tarifaExistente.UsuarioModificador = User.Identity?.Name ?? "Comercial";
                await _context.SaveChangesAsync();
                await ResincronizarPendientesAsync("Marítimo");
            }
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostEditarCostoTerrestreAsync(string? ComentarioAuditoria)
        {
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
            NuevaVentaAlmacenamiento.GrupoCobro = "Almacenamiento";
            NuevaVentaAlmacenamiento.EsActiva = true;
            NuevaVentaAlmacenamiento.UsuarioCreador = User.Identity?.Name ?? "Comercial";
            if (NuevaVentaAlmacenamiento.FechaFinVigencia <= new DateTime(2000, 1, 1)) NuevaVentaAlmacenamiento.FechaFinVigencia = DateTime.Now.AddYears(1);
            var propiedades = typeof(TarifasCliente).GetProperties();
            foreach (var prop in propiedades)
            {
                if (prop.PropertyType == typeof(DateTime))
                {
                    var valorActual = (DateTime)prop.GetValue(NuevaVentaAlmacenamiento);
                    if (valorActual <= new DateTime(1900, 1, 1)) prop.SetValue(NuevaVentaAlmacenamiento, DateTime.Now);
                }
            }
            _context.TarifasClientes.Add(NuevaVentaAlmacenamiento);
            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearVentaMaritimaAsync()
        {
            NuevaVentaMaritima.GrupoCobro = "Marítimo";
            NuevaVentaMaritima.EsActiva = true;
            NuevaVentaMaritima.UsuarioCreador = User.Identity?.Name ?? "Comercial";
            var prop = NuevaVentaMaritima.GetType().GetProperty("PlacPlug");
            if (prop != null && decimal.TryParse(Request.Form["PlacPlug_Venta"], out decimal plugVal)) prop.SetValue(NuevaVentaMaritima, plugVal);
            _context.TarifasClientes.Add(NuevaVentaMaritima);
            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearCostoMaritimoAsync()
        {
            NuevoCostoMaritimo.EsActiva = true;
            var prop = NuevoCostoMaritimo.GetType().GetProperty("PlacPlug");
            if (prop != null && decimal.TryParse(Request.Form["PlacPlug_Costo"], out decimal plugVal)) prop.SetValue(NuevoCostoMaritimo, plugVal);
            _context.TarifasMaritimas.Add(NuevoCostoMaritimo);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Marítimo");
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearVentaTerrestreAsync() { NuevaVentaTerrestre.GrupoCobro = "Terrestre"; NuevaVentaTerrestre.EsActiva = true; NuevaVentaTerrestre.UsuarioCreador = User.Identity?.Name ?? "Comercial"; _context.TarifasClientes.Add(NuevaVentaTerrestre); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostCrearVentaDocumentalAsync() { NuevaVentaMaritima.GrupoCobro = "Documental"; NuevaVentaMaritima.EsActiva = true; NuevaVentaMaritima.UsuarioCreador = User.Identity?.Name ?? "Comercial"; _context.TarifasClientes.Add(NuevaVentaMaritima); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostCrearVentaCourierAsync()
        {
            NuevaVentaCourier.GrupoCobro = "Courier";
            NuevaVentaCourier.EsActiva = true;
            NuevaVentaCourier.UsuarioCreador = User.Identity?.Name ?? "Comercial";
            _context.TarifasClientes.Add(NuevaVentaCourier);
            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearTarifaAlmacenamientoAsync() { NuevaTarifaAlmacenamiento.EsActiva = true; NuevaTarifaAlmacenamiento.FechaCreacion = DateTime.Now; NuevaTarifaAlmacenamiento.UsuarioCreador = User.Identity?.Name ?? "Comercial"; _context.TarifasAlmacenamientos.Add(NuevaTarifaAlmacenamiento); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }

        public async Task<IActionResult> OnPostCrearDetalleAlmacenamientoAsync()
        {
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
        public async Task<IActionResult> OnPostCrearTarifaDocumentalAsync() { NuevaTarifaDocumental.Categoria = "Documental"; NuevaTarifaDocumental.EsActiva = true; _context.TarifasMaestras.Add(NuevaTarifaDocumental); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostCrearTarifaCourierAsync()
        {
            NuevaTarifaCourier.Categoria = "Courier";
            NuevaTarifaCourier.EsActiva = true;
            _context.TarifasMaestras.Add(NuevaTarifaCourier);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Documental");
            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostCrearTarifaMaritimoExtraAsync()
        {
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
            var tarifaAnterior = await _context.TarifasDocumentales.FirstOrDefaultAsync(t => t.IdAgenciaAduana == NuevoCostoDocumental.IdAgenciaAduana && t.Concepto == NuevoCostoDocumental.Concepto && t.EsActiva == true);
            if (tarifaAnterior != null) tarifaAnterior.EsActiva = false;
            NuevoCostoDocumental.EsActiva = true;
            NuevoCostoDocumental.FechaCreacion = DateTime.Now;
            _context.TarifasDocumentales.Add(NuevoCostoDocumental);
            await _context.SaveChangesAsync();
            await ResincronizarPendientesAsync("Documental");
            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnPostCrearProveedorAsync() { NuevoProveedor.Activo = 1; _context.Proveedores.Add(NuevoProveedor); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostCrearAgenciaAduanaAsync() { NuevaAgencia.Activo = 1; NuevaAgencia.FechaCreacion = DateTime.Now; NuevaAgencia.UsuarioCreador = User.Identity?.Name ?? "Comercial"; _context.AgenciasAduanas.Add(NuevaAgencia); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostCrearConductorAsync() { NuevoConductor.Activo = true; _context.Conductores.Add(NuevoConductor); await _context.SaveChangesAsync(); return RedirectToPage("./Index"); }
        // ==================== HANDLERS DE ELIMINACIÓN (ARCHIVADO LÓGICO/FÍSICO) ====================
        public async Task<IActionResult> OnPostEliminarTarifaClienteAsync(int idTarifa) { var tarifa = await _context.TarifasClientes.FindAsync(idTarifa); if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostEliminarCostoMaritimoAsync(int idTarifa) { var tarifa = await _context.TarifasMaritimas.FindAsync(idTarifa); if (tarifa != null) { _context.TarifasMaritimas.Remove(tarifa); await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostEliminarTarifaAlmacenamientoAsync(int idTarifa) { var tarifa = await _context.TarifasAlmacenamientos.FindAsync(idTarifa); if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostEliminarCostoTerrestreAsync(int idTarifa) { var tarifa = await _context.Tarifasterrestres.FindAsync(idTarifa); if (tarifa != null) { _context.Tarifasterrestres.Remove(tarifa); await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostEliminarTarifaDocumentalAsync(int idTarifa) { var tarifa = await _context.TarifasMaestras.FindAsync(idTarifa); if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
        public async Task<IActionResult> OnPostEliminarCostoDocumentalAsync(int idTarifa) { var tarifa = await _context.TarifasDocumentales.FindAsync(idTarifa); if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); } return RedirectToPage(); }
        public async Task<IActionResult> OnPostEliminarTarifaGateAsync(int idTarifa) { var tarifa = await _context.TarifasGate.FindAsync(idTarifa); if (tarifa != null) { tarifa.EsActiva = false; await _context.SaveChangesAsync(); } return RedirectToPage("./Index"); }
    }
}