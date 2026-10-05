using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using AtilsonCargoSpa.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Hosting;


namespace AtilsonCargoSpa.Pages.Operaciones
{
    public class DetalleModel : PageModel, AtilsonCargoSpa.Models.IOperacionesConsultaPageModel
    {
        private readonly AtilsonContext _context;
        private readonly FinanzasSyncService _finanzasSync;

        private readonly IWebHostEnvironment _env;
        private readonly EmailService _emailService;

        public List<SelectListItem> ListaCadenasAlmacen { get; set; } = new();
        public List<ConductorAutoDto> ChoferesHistorialAlmacen { get; set; } = new();

        public class ConductorAutoDto
        {
            public string? Nombre { get; set; }
            public string? Rut { get; set; }
            public string? Telefono { get; set; }
            public string? Patente { get; set; }
        }
        public DetalleModel(AtilsonContext context, FinanzasSyncService finanzasSync, IWebHostEnvironment env, EmailService emailService)
        {
            _context = context;
            _finanzasSync = finanzasSync;
            _env = env;
            _emailService = emailService;
        }

        public Operacione Op { get; set; } = default!;

        public bool ReqMaritimo { get; set; }
        public bool ReqTerrestre { get; set; }
        public bool ReqDocumental { get; set; }
        public bool ReqMatriz { get; set; }
        public string NombreServicio { get; set; } = "";

        // Mismo criterio que IndexModel.OperacionCerradaPorRetiro — con varios
        // contenedores, todos deben tener su FechaRetiroDestino registrada.
        public bool OperacionCerrada { get; set; }
        public DateTime? FechaCierreMasReciente { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var op = await _context.Operaciones
                .AsSplitQuery()
                .Where(o => o.Id == id && !o.IsDeleted)
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.IdPuertoOrigenNavigation)
                .Include(o => o.IdPuertoDestinoNavigation)
                .Include(o => o.RoleosCancelaciones)
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.OperacionesDocumentales)
                .Include(o => o.Unidadestecnicas)
                .Include(o => o.ExtracostosOperacions)
                .Include(o => o.TransaccionesFinancieras)
                .Include(o => o.SolicitudesDesbloqueos)
                .Include(o => o.OperacionesAlmacenamientos)
    .ThenInclude(a => a.IdProveedorNavigation)
.Include(o => o.GestionTransbordo)
.FirstOrDefaultAsync();

            if (op == null) return NotFound();

            Op = op;

            int s = op.IdTipoServicio ?? 0;
            ReqMaritimo = new[] { 1, 2, 3, 5, 8, 9, 10, 12 }.Contains(s);
            ReqTerrestre = new[] { 1, 2, 4, 6, 8, 9, 11, 13 }.Contains(s);
            ReqDocumental = new[] { 1, 3, 4, 7, 8, 10, 11, 14 }.Contains(s);
            ReqMatriz = ReqMaritimo || ReqDocumental;

            NombreServicio = s switch
            {
                1 => "EXP INTEGRAL",
                2 => "EXP MAR + TER",
                3 => "EXP MAR + DOC",
                4 => "EXP TER + DOC",
                5 => "EXP MAR",
                6 => "EXP TER",
                7 => "EXP DOC",
                8 => "IMP INTEGRAL",
                9 => "IMP MAR + TER",
                10 => "IMP MAR + DOC",
                11 => "IMP TER + DOC",
                12 => "IMP MAR",
                13 => "IMP TER",
                14 => "IMP DOC",
                _ => "POR CONFIRMAR"
            };

            ViewData["PaisesCourier"] = new SelectList(
    await _context.Puertos.Where(p => p.Activo == 1 && p.Pais != null)
        .Select(p => p.Pais!.ToUpper()).Distinct().OrderBy(x => x).ToListAsync()
);
            ViewData["TarifasMaritimo"] = await _context.TarifasMaestras.Where(t => t.EsActiva && t.Categoria == "Marítimo").OrderBy(t => t.Concepto).ToListAsync();
            ViewData["TarifasTerrestre"] = await _context.TarifasMaestras.Where(t => t.EsActiva && t.Categoria == "Terrestre").OrderBy(t => t.Concepto).ToListAsync();
            ViewData["TarifasDocumental"] = await _context.TarifasMaestras.Where(t => t.EsActiva && t.Categoria == "Documental").OrderBy(t => t.Concepto).ToListAsync();
            ViewData["IdNaviera"] = new SelectList(_context.Navieras.Where(n => n.Activo == 1), "Id", "NombreNaviera");
            ViewData["PuertosList"] = await _context.Puertos.Where(p => p.Activo == 1).OrderBy(p => p.NombrePuerto).ToListAsync();
            ViewData["IdAgenciaAduana"] = new SelectList(await _context.AgenciasAduanas.ToListAsync(), "Id", "NombreAgencia");
            ViewData["DepositosList"] = await _context.Depositos.Where(d => d.Activo == 1).OrderBy(d => d.NombreDeposito).ToListAsync();
            ViewData["PlantasList"] = await _context.Plantas.Include(p => p.Ciudad).Where(p => p.Activo).ToListAsync();
            var proveedoresTransporteRaw = await _context.Proveedores.Where(p => p.Activo == 1 && (p.IdTipoProveedor == 2 || p.IdTipoProveedor == 10)).ToListAsync();
            ViewData["ProveedoresTransporteList"] = proveedoresTransporteRaw
                .Where(p => !string.IsNullOrWhiteSpace(p.NombreProveedor))
                .GroupBy(p => p.NombreProveedor.Trim().ToUpper())
                .Select(g => g.First())
                .OrderBy(p => p.NombreProveedor)
                .ToList();
            var cadenasAlmacen = await _context.Proveedores
    .Include(p => p.IdCiudadNavigation)
    .Where(p => p.IdTipoProveedor == 12 && p.Activo == 1)
    .ToListAsync();
            var ordenCiudadesAlmacen = new List<string> { "VALPARAÍSO", "SAN ANTONIO", "LIRQUÉN" };
            var gruposPorCiudadAlmacen = new Dictionary<string, SelectListGroup>();
            SelectListGroup ObtenerGrupoAlmacen(string ciudad)
            {
                if (!gruposPorCiudadAlmacen.TryGetValue(ciudad, out var grupo)) { grupo = new SelectListGroup { Name = ciudad }; gruposPorCiudadAlmacen[ciudad] = grupo; }
                return grupo;
            }
            ListaCadenasAlmacen = cadenasAlmacen
                .Select(p => new { p.Id, Nombre = p.NombreProveedor ?? "S/N", Ciudad = (p.IdCiudadNavigation?.Nombre ?? "SIN CIUDAD ASIGNADA").ToUpper() })
                .OrderBy(x => ordenCiudadesAlmacen.Contains(x.Ciudad) ? ordenCiudadesAlmacen.IndexOf(x.Ciudad) : int.MaxValue)
                .ThenBy(x => x.Ciudad).ThenBy(x => x.Nombre)
                .Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Nombre, Group = ObtenerGrupoAlmacen(x.Ciudad) })
                .ToList();
            ChoferesHistorialAlmacen = await _context.Conductores
                .Where(c => c.Activo == true)
                .Select(c => new ConductorAutoDto { Nombre = c.Nombre, Rut = c.Rut, Telefono = c.Telefono, Patente = c.Patente })
                .ToListAsync();

            OperacionCerrada = Op.Unidadestecnicas != null && Op.Unidadestecnicas.Any()
    ? Op.FechaRetiroDestino.HasValue && Op.Unidadestecnicas.All(u => u.FechaRetiroDestino.HasValue)
    : Op.FechaRetiroDestino.HasValue;

            if (OperacionCerrada)
            {
                var fechas = new List<DateTime>();
                if (Op.FechaRetiroDestino.HasValue) fechas.Add(Op.FechaRetiroDestino.Value);
                if (Op.Unidadestecnicas != null) fechas.AddRange(Op.Unidadestecnicas.Where(u => u.FechaRetiroDestino.HasValue).Select(u => u.FechaRetiroDestino!.Value));
                FechaCierreMasReciente = fechas.Any() ? fechas.Max() : null;
            }

            return Page();

            return Page();
        }

        public async Task<IActionResult> OnPostQuickEditCourierAsync(int IdOperacion, int? IdUnidadTecnica, IFormFile? UploadFullSet, IFormFile? UploadDhl)
        {
            var op = await _context.Operaciones
                .Include(o => o.OperacionesDocumentales)
                .Include(o => o.IdPuertoOrigenNavigation)
                .Include(o => o.IdPuertoDestinoNavigation)
                .FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op == null) return NotFound();

            var docDb = op.OperacionesDocumentales.FirstOrDefault(x => x.IdUnidadTecnica == IdUnidadTecnica);
            if (docDb == null)
            {
                docDb = new OperacionesDocumentale { IdUnidadTecnica = IdUnidadTecnica, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true };
                op.OperacionesDocumentales.Add(docDb);
            }

            docDb.AplicaCourier = Request.Form["docUpdates.AplicaCourier"].Contains("true");
            if (docDb.AplicaCourier == true)
            {
                docDb.TipoCourier = Request.Form["docUpdates.TipoCourier"].FirstOrDefault();

                string? paisFormulario = Request.Form["docUpdates.PaisCourier"].FirstOrDefault();
                if (docDb.TipoCourier == "Internacional")
                {
                    bool esImportacion = new[] { 8, 9, 10, 11, 12, 13, 14 }.Contains(op.IdTipoServicio ?? 0);
                    string? paisInferido = esImportacion
                        ? op.IdPuertoOrigenNavigation?.Pais?.ToUpper()
                        : op.IdPuertoDestinoNavigation?.Pais?.ToUpper();
                    docDb.PaisCourier = !string.IsNullOrWhiteSpace(paisFormulario) ? paisFormulario : paisInferido;
                }
                else
                {
                    docDb.PaisCourier = null;
                }

                docDb.ZonaCourier = docDb.TipoCourier == "Internacional" ? CourierZonas.ObtenerZona(docDb.PaisCourier) : null;
                docDb.EmpresaCourier = Request.Form["docUpdates.EmpresaCourier"].FirstOrDefault();
                docDb.TrackingCourier = Request.Form["docUpdates.TrackingCourier"].FirstOrDefault();
                string baseConcept = docDb.TipoCourier == "Internacional" ? $"COURIER INTERNACIONAL {docDb.ZonaCourier}" : "COURIER NACIONAL";
                var tarifaCourier = await _context.TarifasMaestras.FirstOrDefaultAsync(t => t.EsActiva && t.Categoria == "Courier" && t.Concepto.ToUpper() == baseConcept.ToUpper());
                docDb.ValorCourier = tarifaCourier?.ValorNeto;

                if (Request.Form.ContainsKey("docUpdates.NumFullSet")) docDb.NumFullSet = Request.Form["docUpdates.NumFullSet"].FirstOrDefault();
                if (Request.Form.ContainsKey("docUpdates.TrackingDhl")) docDb.TrackingDhl = Request.Form["docUpdates.TrackingDhl"].FirstOrDefault();
                if (Request.Form.ContainsKey("docUpdates.LogFullSet")) docDb.LogFullSet = Request.Form["docUpdates.LogFullSet"].FirstOrDefault();
                if (Request.Form.ContainsKey("docUpdates.LogDhl")) docDb.LogDhl = Request.Form["docUpdates.LogDhl"].FirstOrDefault();

                string uploadsFolderCourier = Path.Combine(_env.WebRootPath, "uploads", "documentos");
                if (!Directory.Exists(uploadsFolderCourier)) Directory.CreateDirectory(uploadsFolderCourier);

                if (UploadFullSet != null && UploadFullSet.Length > 0)
                {
                    string fileNameFs = $"{op.Id}_{DateTime.Now.Ticks}_{Path.GetFileName(UploadFullSet.FileName).Replace(" ", "_")}";
                    using (var streamFs = new FileStream(Path.Combine(uploadsFolderCourier, fileNameFs), FileMode.Create)) { await UploadFullSet.CopyToAsync(streamFs); }
                    docDb.RutaFullSet = $"/uploads/documentos/{fileNameFs}";
                }
                if (UploadDhl != null && UploadDhl.Length > 0)
                {
                    string fileNameDhl = $"{op.Id}_{DateTime.Now.Ticks}_{Path.GetFileName(UploadDhl.FileName).Replace(" ", "_")}";
                    using (var streamDhl = new FileStream(Path.Combine(uploadsFolderCourier, fileNameDhl), FileMode.Create)) { await UploadDhl.CopyToAsync(streamDhl); }
                    docDb.RutaDhl = $"/uploads/documentos/{fileNameDhl}";
                }
            }
            op.FechaModificacion = DateTime.Now;
            op.UsuarioModificador = User.Identity?.Name ?? "Sistema";
            RegistrarHito(op, "DOCUMENTAL", $"Courier actualizado — Tipo: {docDb.TipoCourier ?? "-"} | País: {docDb.PaisCourier ?? "-"} | Zona: {docDb.ZonaCourier ?? "-"} | Valor: {docDb.ValorCourier?.ToString("N0") ?? "-"}");
            await _context.SaveChangesAsync();
            await _finanzasSync.SincronizarAsync(IdOperacion, User.Identity?.Name ?? "Sistema");

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
            TempData["SuccessMsg"] = "Courier registrado correctamente.";
            return RedirectToPage(new { id = IdOperacion });
        }
        // Replica de IndexModel.ObtenerEstadoConcepto — misma señal que usa
        // ControlFinanciero para "Pendiente cierre Acuerdo".
        public (string? PagadoPor, string? Responsable, bool PendienteTarifa, string ConceptoReal) ObtenerEstadoConcepto(Operacione op, string conceptoBusqueda)
        {
            var tx = op.TransaccionesFinancieras?
                .FirstOrDefault(t => t.TipoMovimiento == "EGRESO" && (t.Concepto ?? "").Contains(conceptoBusqueda));
            if (tx == null) return (null, null, false, conceptoBusqueda);
            bool pendiente = tx.MontoNeto == 0m && !tx.TarifaManual;
            return (tx.ResponsablePago, tx.Responsable, pendiente, tx.Concepto);
        }

        public async Task<IActionResult> OnPostQuickEditTransporteAsync(
    int IdOperacion, int? IdUnidadTecnica, string? EmpresaTransporte, string? RutTransporte, string? CorreoTransporte,
    string? NombreConductor, string? RutConductor, string? TelefonoConductor, string? Patente, string? PatenteRampla,
    DateTime? FechaCarga, string? DepositoRetiro, string? PlantaCarga, string? ZonaCarga, string? LinkTracking,
    string? PuertoEntrega, string? TerminalTerrestreStr, string? NumeroContenedor, string? NumeroSello,
    bool AplicaInterplanta, string? PlantaCarga2, string? LinkMaps2, string? PlantaCarga3, string? LinkMaps3,
    string? FolioRetiro, DateTime? FechaRetiroVacio, IFormFile? UploadFolioRetiro,
    IFormFile? FotoContenedor, IFormFile? FotoSello, IFormFile? UploadBookingTransporte)
        {
            var operacion = await _context.Operaciones
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.Unidadestecnicas)
                .FirstOrDefaultAsync(o => o.Id == IdOperacion);

            if (operacion != null)
            {
                var terrDb = operacion.OperacionesTerrestres.FirstOrDefault(x => x.IdUnidadTecnica == IdUnidadTecnica);
                if (terrDb == null)
                {
                    terrDb = new OperacionesTerrestre { IdOperacion = operacion.Id, IdUnidadTecnica = IdUnidadTecnica, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true };
                    _context.OperacionesTerrestres.Add(terrDb);
                }

                if (Request.Form.ContainsKey("EmpresaTransporte")) terrDb.EmpresaTransporte = EmpresaTransporte;
                if (Request.Form.ContainsKey("RutTransporte")) terrDb.RutTransporte = RutTransporte;
                if (Request.Form.ContainsKey("CorreoTransporte")) terrDb.CorreoTransporte = CorreoTransporte;
                if (Request.Form.ContainsKey("NombreConductor")) terrDb.NombreConductor = NombreConductor;
                if (Request.Form.ContainsKey("RutConductor")) terrDb.RutConductor = RutConductor;
                if (Request.Form.ContainsKey("TelefonoConductor")) terrDb.TelefonoConductor = TelefonoConductor;
                if (Request.Form.ContainsKey("Patente")) terrDb.Patente = Patente;
                if (Request.Form.ContainsKey("PatenteRampla")) terrDb.PatenteRampla = PatenteRampla;
                if (Request.Form.ContainsKey("FechaCarga")) terrDb.FechaCarga = FechaCarga;
                if (Request.Form.ContainsKey("DepositoRetiro")) terrDb.DepositoRetiro = DepositoRetiro;
                if (Request.Form.ContainsKey("PlantaCarga")) terrDb.PlantaCarga = PlantaCarga;
                if (Request.Form.ContainsKey("ZonaCarga")) terrDb.ZonaCarga = ZonaCarga;
                if (Request.Form.ContainsKey("LinkTracking")) terrDb.LinkTracking = LinkTracking;
                if (Request.Form.ContainsKey("FolioRetiro")) terrDb.FolioRetiro = FolioRetiro;
                if (Request.Form.ContainsKey("FechaRetiroVacio")) terrDb.FechaRetiroVacio = FechaRetiroVacio;

                terrDb.AplicaInterplanta = AplicaInterplanta;
                if (Request.Form.ContainsKey("PlantaCarga2")) terrDb.PlantaCarga2 = PlantaCarga2;
                if (Request.Form.ContainsKey("LinkMaps2")) terrDb.LinkMaps2 = LinkMaps2;
                if (Request.Form.ContainsKey("PlantaCarga3")) terrDb.PlantaCarga3 = PlantaCarga3;
                if (Request.Form.ContainsKey("LinkMaps3")) terrDb.LinkMaps3 = LinkMaps3;

                if (Request.Form.ContainsKey("PuertoEntrega")) terrDb.PuertoEntrega = PuertoEntrega;
                if (Request.Form.ContainsKey("TerminalTerrestreStr")) terrDb.TerminalTerrestreStr = TerminalTerrestreStr;

                if (IdUnidadTecnica.HasValue)
                {
                    var unidad = operacion.Unidadestecnicas.FirstOrDefault(u => u.Id == IdUnidadTecnica.Value);
                    if (unidad != null)
                    {
                        if (Request.Form.ContainsKey("NumeroContenedor")) unidad.NroContenedor = NumeroContenedor;
                        if (Request.Form.ContainsKey("NumeroSello")) unidad.NumeroSello = NumeroSello;
                    }
                }
                else
                {
                    if (Request.Form.ContainsKey("NumeroContenedor")) operacion.NumeroContenedor = NumeroContenedor;
                    if (Request.Form.ContainsKey("NumeroSello")) operacion.NumeroSello = NumeroSello;
                }

                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "evidencias");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                if (FotoContenedor != null && FotoContenedor.Length > 0)
                {
                    string uNameConte = $"{operacion.Id}_Conte_{DateTime.Now.Ticks}_{Path.GetFileName(FotoContenedor.FileName).Replace(" ", "_")}";
                    using (var stream = new FileStream(Path.Combine(uploadsFolder, uNameConte), FileMode.Create)) { await FotoContenedor.CopyToAsync(stream); }
                    string rutaFoto = $"/uploads/evidencias/{uNameConte}";
                    if (IdUnidadTecnica.HasValue)
                    {
                        var unidad = operacion.Unidadestecnicas.FirstOrDefault(u => u.Id == IdUnidadTecnica.Value);
                        if (unidad != null) unidad.FotoContenedor = rutaFoto;
                    }
                    else operacion.FotoContenedor = rutaFoto;
                }
                if (FotoSello != null && FotoSello.Length > 0)
                {
                    string uNameSello = $"{operacion.Id}_Sello_{DateTime.Now.Ticks}_{Path.GetFileName(FotoSello.FileName).Replace(" ", "_")}";
                    using (var stream = new FileStream(Path.Combine(uploadsFolder, uNameSello), FileMode.Create)) { await FotoSello.CopyToAsync(stream); }
                    string rutaFotoSello = $"/uploads/evidencias/{uNameSello}";
                    if (IdUnidadTecnica.HasValue)
                    {
                        var unidad = operacion.Unidadestecnicas.FirstOrDefault(u => u.Id == IdUnidadTecnica.Value);
                        if (unidad != null) unidad.FotoSello = rutaFotoSello;
                    }
                    else operacion.FotoSello = rutaFotoSello;
                }
                if (UploadBookingTransporte != null && UploadBookingTransporte.Length > 0)
                {
                    string uNameBooking = $"{operacion.Id}_BookingTransporte_{DateTime.Now.Ticks}_{Path.GetFileName(UploadBookingTransporte.FileName).Replace(" ", "_")}";
                    using (var stream = new FileStream(Path.Combine(uploadsFolder, uNameBooking), FileMode.Create)) { await UploadBookingTransporte.CopyToAsync(stream); }
                    terrDb.RutaBookingTransporte = $"/uploads/evidencias/{uNameBooking}";
                    RegistrarHito(operacion, "TRANSPORTE", $"Archivo de Booking adjuntado en Solicitud de Servicio" + (IdUnidadTecnica.HasValue ? $" (U{IdUnidadTecnica})" : "") + $": {UploadBookingTransporte.FileName}");
                }
                if (UploadFolioRetiro != null && UploadFolioRetiro.Length > 0)
                {
                    string uNameFolio = $"{operacion.Id}_Folio_{DateTime.Now.Ticks}_{Path.GetFileName(UploadFolioRetiro.FileName).Replace(" ", "_")}";
                    using (var stream = new FileStream(Path.Combine(uploadsFolder, uNameFolio), FileMode.Create)) { await UploadFolioRetiro.CopyToAsync(stream); }
                    terrDb.RutaFolioRetiro = $"/uploads/evidencias/{uNameFolio}";
                }

                operacion.FechaModificacion = DateTime.Now;
                operacion.UsuarioModificador = User.Identity?.Name ?? "Sistema";
                operacion.CorreoClienteEnviado = false;

                await _context.SaveChangesAsync();
                await _finanzasSync.SincronizarAsync(IdOperacion, User.Identity?.Name ?? "Sistema");

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Datos de transporte actualizados exitosamente.";
            }
            else if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new JsonResult(new { success = false, message = "Operación no encontrada" });
            }

            return RedirectToPage(new { id = IdOperacion });
        }

        public async Task<IActionResult> OnPostGuardarAlmacenamientoAsync(
    int IdOperacion, int? IdProveedor, int? IdProveedorRetiro, string? EstadoContenedor,
    DateTime? FechaIngreso, string? ConductorIngresoNombre, string? ConductorIngresoRut, string? ConductorIngresoTelefono, string? CamionIngresoPatente, string? Comentarios,
    DateTime? FechaSalida, string? ConductorSalidaNombre, string? ConductorSalidaRut, string? ConductorSalidaTelefono, string? CamionSalidaPatente)
        {
            var operacion = await _context.Operaciones.Include(o => o.OperacionesAlmacenamientos).FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (operacion == null) return new JsonResult(new { success = false, message = "Operación no encontrada" });

            var almacen = operacion.OperacionesAlmacenamientos.FirstOrDefault();
            if (almacen == null)
            {
                almacen = new OperacionesAlmacenamiento { IdOperacion = IdOperacion, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" };
                _context.OperacionesAlmacenamientos.Add(almacen);
            }

            int? proveedorFinal = IdProveedorRetiro ?? IdProveedor;
            if (Request.Form.ContainsKey("IdProveedorRetiro") || Request.Form.ContainsKey("IdProveedor")) almacen.IdProveedor = proveedorFinal;
            if (Request.Form.ContainsKey("EstadoContenedor")) almacen.EstadoContenedor = EstadoContenedor;

            if (Request.Form.ContainsKey("FechaIngreso")) almacen.FechaIngreso = FechaIngreso;
            if (Request.Form.ContainsKey("ConductorIngresoNombre")) almacen.ConductorIngresoNombre = ConductorIngresoNombre;
            if (Request.Form.ContainsKey("ConductorIngresoRut")) almacen.ConductorIngresoRut = ConductorIngresoRut;
            if (Request.Form.ContainsKey("ConductorIngresoTelefono")) almacen.ConductorIngresoTelefono = ConductorIngresoTelefono;
            if (Request.Form.ContainsKey("CamionIngresoPatente")) almacen.CamionIngresoPatente = CamionIngresoPatente;
            if (Request.Form.ContainsKey("Comentarios")) almacen.Comentarios = Comentarios;

            if (Request.Form.ContainsKey("FechaSalida")) almacen.FechaSalida = FechaSalida;
            if (Request.Form.ContainsKey("ConductorSalidaNombre")) almacen.ConductorSalidaNombre = ConductorSalidaNombre;
            if (Request.Form.ContainsKey("ConductorSalidaRut")) almacen.ConductorSalidaRut = ConductorSalidaRut;
            if (Request.Form.ContainsKey("ConductorSalidaTelefono")) almacen.ConductorSalidaTelefono = ConductorSalidaTelefono;
            if (Request.Form.ContainsKey("CamionSalidaPatente")) almacen.CamionSalidaPatente = CamionSalidaPatente;

            var op = await _context.Operaciones.FindAsync(IdOperacion);
            if (op != null)
            {
                RegistrarHito(op, "GENERAL", $"Almacenamiento actualizado — Depósito: {proveedorFinal?.ToString() ?? "Sin asignar"} | Ingreso: {FechaIngreso?.ToString("dd/MM HH:mm") ?? "-"} | Salida: {FechaSalida?.ToString("dd/MM HH:mm") ?? "-"}");
            }

            await _context.SaveChangesAsync();
            await SincronizarAlmacenamientoAFinanzasAsync(IdOperacion);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
            TempData["SuccessMsg"] = "Almacenamiento actualizado correctamente.";
            return RedirectToPage(new { id = IdOperacion });
        }

        public async Task<IActionResult> OnPostSubirRespaldoAlmacenAsync(int IdOperacion, IFormFile? ArchivoTransferenciaAlmacen)
        {
            try
            {
                var operacion = await _context.Operaciones.Include(o => o.OperacionesAlmacenamientos).FirstOrDefaultAsync(o => o.Id == IdOperacion);
                if (operacion == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

                var almacen = operacion.OperacionesAlmacenamientos.FirstOrDefault();
                if (almacen == null)
                {
                    almacen = new OperacionesAlmacenamiento { IdOperacion = IdOperacion, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" };
                    _context.OperacionesAlmacenamientos.Add(almacen);
                }

                if (ArchivoTransferenciaAlmacen != null && ArchivoTransferenciaAlmacen.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "evidencias");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                    string uniqueFileName = $"{IdOperacion}_ComprobantePatio_{DateTime.Now.Ticks}_{Path.GetFileName(ArchivoTransferenciaAlmacen.FileName).Replace(" ", "_")}";
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var stream = new FileStream(filePath, FileMode.Create)) { await ArchivoTransferenciaAlmacen.CopyToAsync(stream); }
                    almacen.RutaArchivoTransferencia = $"/uploads/evidencias/{uniqueFileName}";
                    await _context.SaveChangesAsync();
                    return new JsonResult(new { success = true, ruta = almacen.RutaArchivoTransferencia });
                }
                return new JsonResult(new { success = false, message = "No se recibió ningún archivo." });
            }
            catch (Exception ex) { return new JsonResult(new { success = false, message = ex.Message }); }
        }

        public async Task<IActionResult> OnPostEnviarCorreoTransporteAsync(int id, string correoDestino, string copiaCc, string asunto, string cuerpoHtml, string tipoCorreo)
        {
            try
            {
                var op = await _context.Operaciones.FindAsync(id);
                if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada" });

                string htmlFinal = $@"
            <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.05);'>
                <div style='background: linear-gradient(135deg, #0f172a 0%, #6d2d9e 100%); padding: 25px; text-align: center;'>
                    <h2 style='color: #fff; margin: 0; letter-spacing: 1px;'>Notificación Logística</h2>
                </div>
                <div style='padding: 30px; background-color: #ffffff;'>
                    <p style='font-size: 14px; line-height: 1.6; color: #475569;'>{cuerpoHtml.Replace("\n", "<br>")}</p>
                    <hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 30px 0;'>
                    <p style='font-size: 11px; color: #94a3b8; text-align: center; margin: 0;'>Atilson Cargo SpA.</p>
                </div>
            </div>";

                await _emailService.EnviarCorreoAsync(correoDestino, asunto, htmlFinal);
                RegistrarHito(op, "TRANSPORTE", $"Correo de transporte ({tipoCorreo}) enviado a: {correoDestino}");
                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }
            catch (Exception ex) { return new JsonResult(new { success = false, message = ex.Message }); }
        }
        // Mismo motor de sincronización que ya usa IndexModel — trasladado tal cual,
        // con el fix de match por depósito+tamaño+tipo+Full/Vacío que ya aplicamos
        // en una sesión anterior.
        private async Task SincronizarAlmacenamientoAFinanzasAsync(int idOperacion)
        {
            var op = await _context.Operaciones
                .Include(o => o.OperacionesAlmacenamientos)
                .Include(o => o.TransaccionesFinancieras)
                .FirstOrDefaultAsync(o => o.Id == idOperacion);

            if (op == null) return;

            var almacen = op.OperacionesAlmacenamientos.FirstOrDefault();
            int? idProveedor = almacen?.IdProveedor;
            int? idCliente = op.IdCliente;

            string usuario = User.Identity?.Name ?? "Sistema";
            DateTime ahora = DateTime.Now;

            var txsEgresoActuales = op.TransaccionesFinancieras.Where(t => t.TipoMovimiento == "EGRESO" && t.GrupoCobro == "Almacenamiento").ToList();
            var txsIngresoActuales = op.TransaccionesFinancieras.Where(t => t.TipoMovimiento == "INGRESO" && t.GrupoCobro == "Almacenamiento").ToList();

            if (idProveedor.HasValue)
            {
                var tarifasCosto = await _context.TarifasAlmacenamientos.Where(t => t.IdProveedor == idProveedor.Value && t.EsActiva).ToListAsync();
                var tarifasVenta = await _context.TarifasClientes.Where(t => t.IdCliente == idCliente && t.GrupoCobro == "Almacenamiento" && t.EsActiva).ToListAsync();

                decimal montoCostoBase = 0m; decimal montoCostoReefer = 0m; string conceptoCostoBase = "Tarifa Base Almacenaje";

                string tcAlmOp = (op.TipoContenedor ?? "").ToUpper();
                string tamAlm = tcAlmOp.Contains("40") ? "40" : (tcAlmOp.Contains("20") ? "20" : "");
                bool esRfAlm = tcAlmOp.Contains("RF") || tcAlmOp.Contains("REEFER") || op.IdTipoCarga == 2;
                string tipoCargaAlm = esRfAlm ? "RF" : "DRY";
                string? estadoAlm = almacen?.EstadoContenedor;

                TarifasAlmacenamientoDetalle? detalleMatch = null;
                if (!string.IsNullOrWhiteSpace(estadoAlm))
                {
                    var detallesProveedor = await _context.TarifasAlmacenamientoDetalles
                        .Where(d => d.IdProveedor == idProveedor.Value && d.EsActiva && d.EstadoContenedor == estadoAlm)
                        .ToListAsync();

                    detalleMatch = detallesProveedor.FirstOrDefault(d => d.TipoContenedor == tamAlm && d.TipoCarga == tipoCargaAlm)
                                 ?? detallesProveedor.FirstOrDefault(d => d.TipoContenedor == null && d.TipoCarga == tipoCargaAlm)
                                 ?? detallesProveedor.FirstOrDefault(d => d.TipoContenedor == tamAlm && d.TipoCarga == null)
                                 ?? detallesProveedor.FirstOrDefault(d => d.TipoContenedor == null && d.TipoCarga == null);
                }

                if (detalleMatch != null)
                {
                    montoCostoBase = detalleMatch.ValorNeto;
                    conceptoCostoBase = $"{detalleMatch.Concepto} ({(tarifasCosto.FirstOrDefault()?.DiasLibres ?? 0)} Días Libres)";
                    montoCostoReefer = tarifasCosto.FirstOrDefault()?.TarifaConexionReefer ?? 0m;
                }
                else if (tarifasCosto.Any())
                {
                    var costoPrincipal = tarifasCosto.First();
                    montoCostoBase = costoPrincipal.TarifaBase;
                    montoCostoReefer = costoPrincipal.TarifaConexionReefer;
                    conceptoCostoBase = $"Tarifa Almacenaje ({costoPrincipal.DiasLibres} Días Libres)";
                }

                var txEgresoBase = txsEgresoActuales.FirstOrDefault(t => (t.Concepto ?? "").Contains("Tarifa Almacenaje") || t.Concepto == "Tarifa Base Almacenaje");
                if (txEgresoBase == null)
                {
                    _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = "Almacenamiento", TipoMovimiento = "EGRESO", Concepto = conceptoCostoBase, MontoNeto = montoCostoBase, Moneda = "CLP", EstadoFila = montoCostoBase > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR", FechaCreacion = ahora, UsuarioCreador = usuario, IdProveedor = idProveedor.Value });
                }
                else if (!txEgresoBase.TarifaManual)
                {
                    txEgresoBase.Concepto = conceptoCostoBase; txEgresoBase.MontoNeto = montoCostoBase; txEgresoBase.IdProveedor = idProveedor.Value; txEgresoBase.EstadoFila = montoCostoBase > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR";
                }

                decimal montoVentaBase = 0m; string monedaVentaBase = "CLP"; string conceptoVentaBase = "Servicio de Almacenaje";
                var ventaPrincipal = tarifasVenta.FirstOrDefault(v => !(v.Concepto ?? "").ToUpper().Contains("REEFER"));
                if (ventaPrincipal != null) { montoVentaBase = ventaPrincipal.PrecioPactado; monedaVentaBase = ventaPrincipal.Moneda ?? "CLP"; conceptoVentaBase = ventaPrincipal.Concepto; }

                var txIngresoBase = txsIngresoActuales.FirstOrDefault(t => t.Concepto == conceptoVentaBase || (t.Concepto ?? "").Contains("Almacenaje"));
                if (txIngresoBase == null)
                {
                    _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = "Almacenamiento", TipoMovimiento = "INGRESO", Concepto = conceptoVentaBase, MontoNeto = montoVentaBase, Moneda = monedaVentaBase, EstadoFila = montoVentaBase > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR", FechaCreacion = ahora, UsuarioCreador = usuario, IdCliente = idCliente });
                }
                else if (!txIngresoBase.TarifaManual)
                {
                    txIngresoBase.Concepto = conceptoVentaBase; txIngresoBase.MontoNeto = montoVentaBase; txIngresoBase.Moneda = monedaVentaBase; txIngresoBase.EstadoFila = montoVentaBase > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR";
                }

                if (op.IdTipoCarga == 2)
                {
                    string conceptoReeferCosto = "Conexión Reefer Almacén (x Hora)";
                    var txEgresoReefer = txsEgresoActuales.FirstOrDefault(t => (t.Concepto ?? "").Contains("Reefer"));
                    if (txEgresoReefer == null)
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = "Almacenamiento", TipoMovimiento = "EGRESO", Concepto = conceptoReeferCosto, MontoNeto = montoCostoReefer, Moneda = "CLP", EstadoFila = montoCostoReefer > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR", FechaCreacion = ahora, UsuarioCreador = usuario, IdProveedor = idProveedor.Value });
                    else if (!txEgresoReefer.TarifaManual) { txEgresoReefer.MontoNeto = montoCostoReefer; txEgresoReefer.EstadoFila = montoCostoReefer > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR"; }

                    decimal montoVentaReefer = 0m; string monedaVentaReefer = "CLP"; string conceptoVentaReefer = "Conexión Reefer Almacén (Venta x Hora)";
                    var ventaReefer = tarifasVenta.FirstOrDefault(v => (v.Concepto ?? "").ToUpper().Contains("REEFER"));
                    if (ventaReefer != null) { montoVentaReefer = ventaReefer.PrecioPactado; monedaVentaReefer = ventaReefer.Moneda ?? "CLP"; conceptoVentaReefer = ventaReefer.Concepto; }

                    var txIngresoReefer = txsIngresoActuales.FirstOrDefault(t => (t.Concepto ?? "").Contains("Reefer"));
                    if (txIngresoReefer == null)
                        _context.TransaccionesFinancieras.Add(new TransaccionesFinanciera { IdOperacion = op.Id, GrupoCobro = "Almacenamiento", TipoMovimiento = "INGRESO", Concepto = conceptoVentaReefer, MontoNeto = montoVentaReefer, Moneda = monedaVentaReefer, EstadoFila = montoVentaReefer > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR", FechaCreacion = ahora, UsuarioCreador = usuario, IdCliente = idCliente });
                    else if (!txIngresoReefer.TarifaManual) { txIngresoReefer.Concepto = conceptoVentaReefer; txIngresoReefer.MontoNeto = montoVentaReefer; txIngresoReefer.Moneda = monedaVentaReefer; txIngresoReefer.EstadoFila = montoVentaReefer > 0 ? "PROVISIÓN" : "PENDIENTE VALORIZAR"; }
                }
            }
            else
            {
                foreach (var tx in txsEgresoActuales.Where(t => !t.TarifaManual)) _context.TransaccionesFinancieras.Remove(tx);
                foreach (var tx in txsIngresoActuales.Where(t => !t.TarifaManual)) _context.TransaccionesFinancieras.Remove(tx);
            }

            await _context.SaveChangesAsync();
        }

        public async Task<IActionResult> OnPostEnviarBorradorBLAsync(string idOperacion, string correoDestino, IFormFile? ArchivoBL)
        {
            try
            {
                int realId = int.Parse(idOperacion.Replace("sub-", ""));
                var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == realId);
                if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada" });

                var doc = op.OperacionesDocumentales.FirstOrDefault();
                if (doc != null && doc.EstadoDocumental != "B/L APROBADO" && doc.EstadoDocumental != "CAMBIOS B/L") doc.EstadoDocumental = "BORRADOR ENVIADO";

                var req = HttpContext.Request; string baseUrl = $"{req.Scheme}://{req.Host}";
                string urlAprobar = $"{baseUrl}/Operaciones/Index?handler=RespuestaBorradorBL&id={idOperacion}&respuesta=aprobar";
                string urlRechazar = $"{baseUrl}/Operaciones/Index?handler=RespuestaBorradorBL&id={idOperacion}&respuesta=rechazar";
                string html = $@"<div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 8px;'><div style='background: linear-gradient(135deg, #0f172a 0%, #6d2d9e 100%); padding: 25px; text-align: center; border-radius: 8px 8px 0 0;'><h2 style='color: #fff; margin: 0;'>Revisión de Borrador de B/L</h2><p style='color:#cbd5e1; margin-top:5px;'>Booking: <strong>{op.NumeroBooking}</strong></p></div><div style='padding: 30px; background-color: #ffffff;'><p>Estimado cliente, junto con saludar,</p><p>A continuación enviamos borrador de BL adjunto para su revisión y aprobación.</p><p>En caso de aprobar o requerir modificaciones, haga click en los botones correspondientes a continuación (Esto actualizará automáticamente la plataforma logística):</p><div style='text-align: center; margin-top: 40px; margin-bottom: 20px;'><a href='{urlAprobar}' style='background-color: #16a34a; color: white; padding: 14px 28px; text-decoration: none; border-radius: 6px; font-weight: bold; margin-right: 10px; display: inline-block;'>APROBAR DRAFT</a><a href='{urlRechazar}' style='background-color: #dc2626; color: white; padding: 14px 28px; text-decoration: none; border-radius: 6px; font-weight: bold; display: inline-block;'>RECHAZAR / SOLICITAR CAMBIO</a></div><hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 30px 0;'><p style='font-size: 11px; color: #94a3b8; text-align: center; margin: 0;'>Atilson Cargo SpA.</p></div></div>";

                await _emailService.EnviarCorreoAsync(correoDestino, $"[ATILSON CARGO] Borrador BL para Revisión - BKG {op.NumeroBooking}", html);
                RegistrarHito(op, "DOCUMENTAL", $"Borrador de BL enviado al cliente para revisión a: {correoDestino}");
                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }
            catch (Exception ex) { return new JsonResult(new { success = false, message = ex.Message }); }
        }

        public async Task<IActionResult> OnPostGuardarDocBaseAsync(int IdOperacion, string Campo, string? Valor, IFormFile? Archivo)
        {
            var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada" });

            var docDb = op.OperacionesDocumentales.FirstOrDefault(x => x.IdUnidadTecnica == null);
            if (docDb == null)
            {
                docDb = new OperacionesDocumentale { FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true };
                op.OperacionesDocumentales.Add(docDb);
            }

            string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "documentos");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
            async Task<string?> SaveFile(IFormFile? file)
            {
                if (file == null || file.Length == 0) return null;
                string fileName = $"{op.Id}_{DateTime.Now.Ticks}_{Path.GetFileName(file.FileName).Replace(" ", "_")}";
                using var stream = new FileStream(Path.Combine(uploadsFolder, fileName), FileMode.Create);
                await file.CopyToAsync(stream);
                return $"/uploads/documentos/{fileName}";
            }

            string? rutaGuardada = await SaveFile(Archivo);

            switch (Campo)
            {
                case "instructivo":
                    docDb.InstructivoCliente = Valor;
                    if (rutaGuardada != null) docDb.RutaInstructivo = rutaGuardada;
                    break;
                case "capturaAga":
                    docDb.NumCapturaAga = Valor;
                    if (rutaGuardada != null) docDb.RutaCapturaAga = rutaGuardada;
                    break;
                case "booking":
                    docDb.BookingAtilson = Valor;
                    if (rutaGuardada != null) docDb.RutaBookingAtilson = rutaGuardada;
                    break;
                case "guia":
                    docDb.GuiaDespacho = Valor;
                    if (rutaGuardada != null) docDb.RutaGuia = rutaGuardada;
                    break;
                case "factura":
                    docDb.FacturaExportacion = Valor;
                    if (rutaGuardada != null) docDb.RutaFactura = rutaGuardada;
                    break;
                case "packing":
                    docDb.NotificarA = Valor;
                    if (rutaGuardada != null) docDb.RutaPacking = rutaGuardada;
                    break;
                case "extra":
                    docDb.CertExtra = Valor;
                    if (rutaGuardada != null) docDb.RutaCertExtra = rutaGuardada;
                    break;
                case "libreven":
                    docDb.CertLibreVenta = Valor;
                    if (rutaGuardada != null) docDb.RutaLibreVenta = rutaGuardada;
                    break;
                default:
                    return new JsonResult(new { success = false, message = "Campo no reconocido." });
            }

            op.FechaModificacion = DateTime.Now;
            op.UsuarioModificador = User.Identity?.Name ?? "Sistema";
            RegistrarHito(op, "DOCUMENTAL", $"Documento Base [{Campo.ToUpper()}] actualizado" + (rutaGuardada != null ? " con archivo adjunto." : "."));
            await _context.SaveChangesAsync();

            return new JsonResult(new { success = true, ruta = rutaGuardada });
        }

        // ===== Gest. LAR — portado de IndexModel =====
        public async Task<IActionResult> OnPostUpdateLarAsync(int IdOperacion, int? IdUnidadTecnica, string? EstadoLar, DateTime? LateArrival, DateTime? ElarDate, bool ContenedorIngresado, DateTime? LlegadaPuertoReal, IFormFile? EvidenciaLar)
        {
            var op = await _context.Operaciones
                .Include(o => o.ExtracostosOperacions)
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.Unidadestecnicas)
                .FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op == null) return NotFound();

            string? evidenciaGuardada = null;
            if (EvidenciaLar != null && EvidenciaLar.Length > 0)
            {
                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "evidencias");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string uniqueLar = $"{op.Id}_LAR_{DateTime.Now.Ticks}_{Path.GetFileName(EvidenciaLar.FileName).Replace(" ", "_")}";
                using (var stream = new FileStream(Path.Combine(uploadsFolder, uniqueLar), FileMode.Create)) { await EvidenciaLar.CopyToAsync(stream); }
                evidenciaGuardada = $"/uploads/evidencias/{uniqueLar}";
            }

            if (IdUnidadTecnica.HasValue)
            {
                var unidad = op.Unidadestecnicas.FirstOrDefault(u => u.Id == IdUnidadTecnica.Value);
                if (unidad == null) return NotFound();

                unidad.EstadoLar = EstadoLar;
                unidad.LateArrival = LateArrival;
                unidad.ElarDate = ElarDate;
                unidad.ContenedorIngresado = ContenedorIngresado;
                if (evidenciaGuardada != null) unidad.EvidenciaLar = evidenciaGuardada;
                unidad.FechaModificacion = DateTime.Now;
                unidad.UsuarioModificador = User.Identity?.Name ?? "Sistema";

                if (LlegadaPuertoReal.HasValue)
                {
                    var terrUnidad = op.OperacionesTerrestres.FirstOrDefault(x => x.IdUnidadTecnica == IdUnidadTecnica.Value);
                    if (terrUnidad == null) { terrUnidad = new OperacionesTerrestre { IdUnidadTecnica = IdUnidadTecnica.Value, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true }; op.OperacionesTerrestres.Add(terrUnidad); }
                    terrUnidad.LlegadaPuerto = LlegadaPuertoReal.Value;
                }

                RegistrarHito(op, "MARITIMO", $"LAR/ELAR actualizado (Contenedor {unidad.NroContenedor ?? ("U" + unidad.Id)}) — Estado: {EstadoLar ?? "Sin estado"} | LAR: {LateArrival?.ToString("dd/MM HH:mm") ?? "-"} | ELAR: {ElarDate?.ToString("dd/MM HH:mm") ?? "-"} | Ingresado: {(ContenedorIngresado ? "Sí" : "No")}");
            }
            else
            {
                op.EstadoLar = EstadoLar; op.LateArrival = LateArrival; op.ElarDate = ElarDate; op.ContenedorIngresado = ContenedorIngresado;
                if (evidenciaGuardada != null) op.EvidenciaLar = evidenciaGuardada;

                if (LlegadaPuertoReal.HasValue)
                {
                    var terrDb = op.OperacionesTerrestres.FirstOrDefault(x => x.IdUnidadTecnica == null);
                    if (terrDb == null) { terrDb = new OperacionesTerrestre { FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true }; op.OperacionesTerrestres.Add(terrDb); }
                    terrDb.LlegadaPuerto = LlegadaPuertoReal.Value;
                }

                RegistrarHito(op, "MARITIMO", $"LAR/ELAR actualizado — Estado: {EstadoLar ?? "Sin estado"} | LAR: {LateArrival?.ToString("dd/MM HH:mm") ?? "-"} | ELAR: {ElarDate?.ToString("dd/MM HH:mm") ?? "-"} | Ingresado: {(ContenedorIngresado ? "Sí" : "No")}");
            }

            if (EstadoLar == "AUTORIZADO LAR" || EstadoLar == "INGRESADO CON LAR" || ElarDate.HasValue)
            {
                var costoLar = op.ExtracostosOperacions.FirstOrDefault(e => e.TipoCosto == "Late Arrival (LAR)");
                string elarMsg = ElarDate.HasValue ? $" | ELAR: {ElarDate.Value.ToString("dd/MM/yyyy HH:mm")}" : "";
                if (costoLar == null)
                {
                    op.ExtracostosOperacions.Add(new ExtracostosOperacion { TipoCosto = "Late Arrival (LAR)", Motivo = $"LAR {(EstadoLar == "AUTORIZADO LAR" ? "Autorizado" : "Ingresado")}. Nuevo Cut-Off: {LateArrival?.ToString("dd/MM/yyyy HH:mm") ?? "N/A"}{elarMsg}", Monto = 0, Moneda = "USD", Evidencia = evidenciaGuardada ?? op.EvidenciaLar ?? "Generado automáticamente por sistema", FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" });
                }
                else if (ElarDate.HasValue && !costoLar.Motivo.Contains("ELAR"))
                {
                    costoLar.Motivo += elarMsg;
                }
            }

            op.FechaModificacion = DateTime.Now; op.CorreoClienteEnviado = false;

            await _context.SaveChangesAsync();
            await _finanzasSync.SincronizarAsync(IdOperacion, User.Identity?.Name ?? "Sistema");

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
            TempData["SuccessMsg"] = "Gestión de Late Arrival registrada. Si fue autorizado, se notificó a Finanzas.";
            return RedirectToPage(new { id = IdOperacion });
        }

        // ===== Documental completo — portado de IndexModel (faltaba por completo) =====
        public async Task<IActionResult> OnPostQuickEditDocumentalAsync(int IdOperacion, int? IdUnidadTecnica,
            IFormFile? UploadFactura, IFormFile? UploadGuia, IFormFile? UploadInstructivo, IFormFile? UploadPacking, IFormFile? UploadCertExtra, IFormFile? UploadLibreVenta, IFormFile? UploadDhl, IFormFile? UploadBookingAtilson, IFormFile? UploadFullSet,
            IFormFile? UploadOri1, IFormFile? UploadOri2, IFormFile? UploadOri3, IFormFile? UploadOri4,
            IFormFile? UploadCertFito, IFormFile? UploadFit2, IFormFile? UploadFit3, IFormFile? UploadFit4,
            IFormFile? UploadCertSanitario, IFormFile? UploadSan2, IFormFile? UploadSan3, IFormFile? UploadSan4,
            IFormFile? UploadCod1, IFormFile? UploadCod2, IFormFile? UploadCod3, IFormFile? UploadCod4,
            IFormFile? UploadCla1, IFormFile? UploadCla2, IFormFile? UploadCla3, IFormFile? UploadCla4,
            IFormFile? UploadNep1, IFormFile? UploadNep2, IFormFile? UploadNep3, IFormFile? UploadNep4,
            IFormFile? UploadCap1, IFormFile? UploadCap2, IFormFile? UploadCap3, IFormFile? UploadCap4,
            IFormFile? UploadCoa, IFormFile? UploadDt, IFormFile? UploadCapturaAga)
        {
            try
            {
                var op = await _context.Operaciones
                    .Include(o => o.OperacionesDocumentales)
                    .Include(o => o.ExtracostosOperacions)
                    .FirstOrDefaultAsync(o => o.Id == IdOperacion);

                if (op == null) return NotFound();

                var docDb = op.OperacionesDocumentales.FirstOrDefault(x => x.IdUnidadTecnica == IdUnidadTecnica);
                if (docDb == null)
                {
                    docDb = new OperacionesDocumentale { IdUnidadTecnica = IdUnidadTecnica, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true };
                    op.OperacionesDocumentales.Add(docDb);
                }

                string? F(string key) { var val = Request.Form[key].FirstOrDefault(); return string.IsNullOrWhiteSpace(val) ? null : val.Trim(); }
                bool FBoolPreserve(string key, bool valorActual) => Request.Form.ContainsKey(key) ? Request.Form[key].Any(v => v == "true" || v == "on") : valorActual;
                int? FInt(string key) { var val = Request.Form[key].FirstOrDefault(); return int.TryParse(val, out var r) ? r : null; }
                int? ParseDinero(string key) { var val = Request.Form[key].FirstOrDefault(); if (string.IsNullOrWhiteSpace(val)) return null; var clean = new string(val.Where(c => char.IsDigit(c) || c == '-').ToArray()); if (string.IsNullOrWhiteSpace(clean)) return null; return int.TryParse(clean, out var r) ? r : null; }

                docDb.AplicaSag = Request.Form["docUpdates.AplicaSag"].Contains("true");
                docDb.AplicaSernapesca = Request.Form["docUpdates.AplicaSernapesca"].Contains("true");

                string? sagModalidadAnterior = docDb.SagModalidad;
                string? sagModalidadNueva = Request.Form.ContainsKey("docUpdates.SagModalidad") ? F("docUpdates.SagModalidad") : sagModalidadAnterior;
                bool sagModalidadCambio = !string.IsNullOrEmpty(sagModalidadNueva) && sagModalidadNueva != sagModalidadAnterior;
                if (Request.Form.ContainsKey("docUpdates.SagModalidad")) docDb.SagModalidad = sagModalidadNueva;

                bool isReqDocumental = new[] { 1, 3, 4, 7, 8, 10, 11, 14 }.Contains(op.IdTipoServicio ?? 0);
                docDb.CertificadoOrigen = isReqDocumental || docDb.AplicaSag || docDb.AplicaSernapesca;

                if (Request.Form.ContainsKey("docUpdates.IdAgenciaAduana")) docDb.IdAgenciaAduana = FInt("docUpdates.IdAgenciaAduana");
                docDb.DusDin = F("docUpdates.Dus");
                docDb.Din = F("docUpdates.Din");
                docDb.ValorDus = ParseDinero("docUpdates.ValorDus");
                docDb.ValorDin = ParseDinero("docUpdates.ValorDin");
                docDb.ValorAclaracion = ParseDinero("docUpdates.ValorAclaracion");
                docDb.ValorCancelacion = ParseDinero("docUpdates.ValorCancelacion");
                docDb.ValorRoleo = ParseDinero("docUpdates.ValorRoleo");

                docDb.EstadoDocumental = F("docUpdates.EstadoDocumental");
                docDb.ExtensionDocumental = FBoolPreserve("docUpdates.ExtensionDocumental", docDb.ExtensionDocumental == true);
                docDb.GuiaVisado = FBoolPreserve("docUpdates.GuiaVisado", docDb.GuiaVisado == true);
                docDb.Roleo = FBoolPreserve("docUpdates.Roleo", docDb.Roleo == true);
                docDb.GeneracionIvv = FBoolPreserve("docUpdates.GeneracionIvv", docDb.GeneracionIvv == true);

                docDb.AcuerdoOrigen = F("docUpdates.AcuerdoOrigen");
                docDb.RemisionOrigen1 = F("docUpdates.NumOri1"); docDb.RemisionOrigen2 = F("docUpdates.NumOri2"); docDb.NumOri3 = F("docUpdates.NumOri3"); docDb.NumOri4 = F("docUpdates.NumOri4");
                docDb.ValOri1 = ParseDinero("docUpdates.ValOri1"); docDb.ValOri2 = ParseDinero("docUpdates.ValOri2"); docDb.ValOri3 = ParseDinero("docUpdates.ValOri3"); docDb.ValOri4 = ParseDinero("docUpdates.ValOri4");

                docDb.CertFitosanitario = F("docUpdates.NumFit1"); docDb.NumFit2 = F("docUpdates.NumFit2"); docDb.NumFit3 = F("docUpdates.NumFit3"); docDb.NumFit4 = F("docUpdates.NumFit4");
                docDb.ValFit1 = ParseDinero("docUpdates.ValFit1"); docDb.ValFit2 = ParseDinero("docUpdates.ValFit2"); docDb.ValFit3 = ParseDinero("docUpdates.ValFit3"); docDb.ValFit4 = ParseDinero("docUpdates.ValFit4");

                docDb.CertSanitario = F("docUpdates.NumSan1"); docDb.NumSan2 = F("docUpdates.NumSan2"); docDb.NumSan3 = F("docUpdates.NumSan3"); docDb.NumSan4 = F("docUpdates.NumSan4");
                docDb.ValSan1 = ParseDinero("docUpdates.ValSan1"); docDb.ValSan2 = ParseDinero("docUpdates.ValSan2"); docDb.ValSan3 = ParseDinero("docUpdates.ValSan3"); docDb.ValSan4 = ParseDinero("docUpdates.ValSan4");

                docDb.NumCod1 = F("docUpdates.NumCod1"); docDb.NumCla1 = F("docUpdates.NumCla1"); docDb.NumNep1 = F("docUpdates.NumNep1");

                docDb.FacturaExportacion = F("docUpdates.FacturaExportacion");
                docDb.InstructivoCliente = F("docUpdates.InstructivoCliente");
                docDb.NotificarA = F("docUpdates.NotificarA");
                docDb.CertExtra = F("docUpdates.CertExtra");
                docDb.CertLibreVenta = F("docUpdates.CertLibreVenta");
                docDb.BookingAtilson = F("docUpdates.BookingAtilson");
                docDb.NumGuiaDespacho = F("docUpdates.NumGuiaDespacho");
                docDb.NumFullSet = F("docUpdates.NumFullSet");
                docDb.TrackingDhl = F("docUpdates.TrackingDhl");

                docDb.LogGuia = F("docUpdates.LogGuia");
                docDb.LogInstructivo = F("docUpdates.LogInstructivo");
                docDb.LogBookingAtilson = F("docUpdates.LogBookingAtilson");
                docDb.LogFullSet = F("docUpdates.LogFullSet");
                docDb.LogDhl = F("docUpdates.LogDhl");
                docDb.LogFactura = F("docUpdates.LogFactura");
                docDb.LogPacking = F("docUpdates.LogPacking");
                docDb.LogExtra = F("docUpdates.LogExtra");
                docDb.LogLibreVenta = Request.Form["docUpdates.LogLibreVenta"].FirstOrDefault();

                docDb.NumCap1 = F("docUpdates.NumCap1"); docDb.NumCap2 = F("docUpdates.NumCap2"); docDb.NumCap3 = F("docUpdates.NumCap3"); docDb.NumCap4 = F("docUpdates.NumCap4");
                docDb.NumCoa1 = F("docUpdates.NumCoa1"); docDb.NumDt1 = F("docUpdates.NumDt1");

                string uploadsFolder = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "documentos");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                async Task SaveFile(IFormFile? file, Action<string> setPath)
                {
                    if (file == null || file.Length == 0) return;
                    string fileName = $"{op.Id}_{DateTime.Now.Ticks}_{Path.GetFileName(file.FileName).Replace(" ", "_")}";
                    using var stream = new FileStream(Path.Combine(uploadsFolder, fileName), FileMode.Create);
                    await file.CopyToAsync(stream);
                    setPath($"/uploads/documentos/{fileName}");
                }

                await SaveFile(UploadFactura, p => docDb.RutaFactura = p);
                await SaveFile(UploadGuia, p => docDb.RutaGuia = p);
                await SaveFile(UploadInstructivo, p => docDb.RutaInstructivo = p);
                await SaveFile(UploadPacking, p => docDb.RutaPacking = p);
                await SaveFile(UploadCertExtra, p => docDb.RutaCertExtra = p);
                await SaveFile(UploadLibreVenta, p => docDb.RutaLibreVenta = p);
                await SaveFile(UploadDhl, p => docDb.RutaDhl = p);
                await SaveFile(UploadOri1, p => docDb.EvidenciaOrigen1 = p);
                await SaveFile(UploadOri2, p => docDb.EvidenciaOrigen2 = p);
                await SaveFile(UploadOri3, p => docDb.EvidenciaOrigen3 = p);
                await SaveFile(UploadOri4, p => docDb.EvidenciaOrigen4 = p);
                await SaveFile(UploadCertFito, p => docDb.EvidenciaFito1 = p);
                await SaveFile(UploadFit2, p => docDb.EvidenciaFito2 = p);
                await SaveFile(UploadFit3, p => docDb.EvidenciaFito3 = p);
                await SaveFile(UploadFit4, p => docDb.EvidenciaFito4 = p);
                await SaveFile(UploadCertSanitario, p => docDb.EvidenciaSanitario1 = p);
                await SaveFile(UploadSan2, p => docDb.EvidenciaSanitario2 = p);
                await SaveFile(UploadSan3, p => docDb.EvidenciaSanitario3 = p);
                await SaveFile(UploadSan4, p => docDb.EvidenciaSanitario4 = p);
                await SaveFile(UploadCod1, p => docDb.EvidenciaCod1 = p);
                await SaveFile(UploadCod2, p => docDb.EvidenciaCod2 = p);
                await SaveFile(UploadCod3, p => docDb.EvidenciaCod3 = p);
                await SaveFile(UploadCod4, p => docDb.EvidenciaCod4 = p);
                await SaveFile(UploadCla1, p => docDb.EvidenciaCla1 = p);
                await SaveFile(UploadCla2, p => docDb.EvidenciaCla2 = p);
                await SaveFile(UploadCla3, p => docDb.EvidenciaCla3 = p);
                await SaveFile(UploadCla4, p => docDb.EvidenciaCla4 = p);
                await SaveFile(UploadNep1, p => docDb.EvidenciaNep1 = p);
                await SaveFile(UploadNep2, p => docDb.EvidenciaNep2 = p);
                await SaveFile(UploadNep3, p => docDb.EvidenciaNep3 = p);
                await SaveFile(UploadNep4, p => docDb.EvidenciaNep4 = p);
                await SaveFile(UploadCap1, p => docDb.EvidenciaCap1 = p);
                await SaveFile(UploadCap2, p => docDb.EvidenciaCap2 = p);
                await SaveFile(UploadCap3, p => docDb.EvidenciaCap3 = p);
                await SaveFile(UploadCap4, p => docDb.EvidenciaCap4 = p);
                await SaveFile(UploadCoa, p => docDb.EvidenciaCoa1 = p);
                await SaveFile(UploadDt, p => docDb.EvidenciaDt1 = p);
                await SaveFile(UploadBookingAtilson, p => docDb.RutaBookingAtilson = p);
                await SaveFile(UploadFullSet, p => docDb.RutaFullSet = p);
                await SaveFile(UploadCapturaAga, p => docDb.RutaCapturaAga = p);

                docDb.ObsOrigen = F("docUpdates.ObsOrigen");
                docDb.ObsCaptura = F("docUpdates.ObsCaptura");
                docDb.LogOrigen = Request.Form["docUpdates.LogOrigen"].FirstOrDefault();
                docDb.LogFitosanitario = Request.Form["docUpdates.LogFitosanitario"].FirstOrDefault();
                docDb.LogSanitario = Request.Form["docUpdates.LogSanitario"].FirstOrDefault();
                docDb.LogCaptura = Request.Form["docUpdates.LogCaptura"].FirstOrDefault();
                docDb.LogCoa = Request.Form["docUpdates.LogCoa"].FirstOrDefault();
                docDb.LogDt = Request.Form["docUpdates.LogDt"].FirstOrDefault();
                docDb.LogCodaut = Request.Form["docUpdates.LogCodaut"].FirstOrDefault();
                docDb.LogClave = Request.Form["docUpdates.LogClave"].FirstOrDefault();
                docDb.LogNeppex = Request.Form["docUpdates.LogNeppex"].FirstOrDefault();

                if (sagModalidadCambio) RegistrarLogSagModalidad(docDb, sagModalidadNueva!, User.Identity?.Name ?? "Sistema");

                op.FechaModificacion = DateTime.Now;
                op.UsuarioModificador = User.Identity?.Name ?? "Sistema";
                RegistrarHito(op, "DOCUMENTAL", $"Documentación guardada — DUS: {docDb.DusDin ?? "-"} | SAG: {(docDb.AplicaSag ? "Sí" : "No")} | Sernapesca: {(docDb.AplicaSernapesca ? "Sí" : "No")}");
                op.CorreoClienteEnviado = false;

                await _context.SaveChangesAsync();
                await _finanzasSync.SincronizarAsync(IdOperacion, User.Identity?.Name ?? "Sistema");

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Progreso documental guardado correctamente.";
            }
            catch (Exception ex)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = false, message = ex.Message });
                TempData["ErrorMsg"] = "Error al guardar: " + ex.Message;
            }
            return RedirectToPage(new { id = IdOperacion });
        }

        // ===== Envío de Certificado Sanitario/Captura al cliente — portado de IndexModel =====
        public async Task<IActionResult> OnPostEnviarCertificadoClienteAsync(string idOperacion, string tipo, string correoDestino)
        {
            try
            {
                int realId = int.Parse(idOperacion.Replace("sub-", "").Replace("v3-", ""));
                var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == realId);
                if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada" });

                var doc = op.OperacionesDocumentales.FirstOrDefault();
                string nombreCert = tipo == "san" ? "Sanitario" : "de Captura";
                string? linkArchivo = null; int version = 1;

                if (tipo == "san") { if (!string.IsNullOrEmpty(doc?.EvidenciaSanitario4)) { linkArchivo = doc.EvidenciaSanitario4; version = 4; } else if (!string.IsNullOrEmpty(doc?.EvidenciaSanitario3)) { linkArchivo = doc.EvidenciaSanitario3; version = 3; } else if (!string.IsNullOrEmpty(doc?.EvidenciaSanitario2)) { linkArchivo = doc.EvidenciaSanitario2; version = 2; } else { linkArchivo = doc?.EvidenciaSanitario1; version = 1; } }
                else { if (!string.IsNullOrEmpty(doc?.EvidenciaCap4)) { linkArchivo = doc.EvidenciaCap4; version = 4; } else if (!string.IsNullOrEmpty(doc?.EvidenciaCap3)) { linkArchivo = doc.EvidenciaCap3; version = 3; } else if (!string.IsNullOrEmpty(doc?.EvidenciaCap2)) { linkArchivo = doc.EvidenciaCap2; version = 2; } else { linkArchivo = doc?.EvidenciaCap1; version = 1; } }

                if (string.IsNullOrEmpty(linkArchivo)) return new JsonResult(new { success = false, message = "No hay ningún documento guardado para enviar." });

                var req = HttpContext.Request; string baseUrl = $"{req.Scheme}://{req.Host}"; string urlDescarga = $"{baseUrl}{linkArchivo}";
                string urlAprobar = $"{baseUrl}/Operaciones/Index?handler=RespuestaCertificadoCliente&id={idOperacion}&tipo={tipo}&respuesta=aprobar"; string urlRechazar = $"{baseUrl}/Operaciones/Index?handler=RespuestaCertificadoCliente&id={idOperacion}&tipo={tipo}&respuesta=rechazar";
                string botonesHtml = $@"<a href='{urlAprobar}' style='background-color:#16a34a;color:#fff;padding:14px 28px;text-decoration:none;border-radius:6px;font-weight:bold;margin-right:10px;display:inline-block;'>APROBAR CERTIFICADO</a> <a href='{urlRechazar}' style='background-color:#dc2626;color:#fff;padding:14px 28px;text-decoration:none;border-radius:6px;font-weight:bold;display:inline-block;'>SOLICITAR CAMBIO</a>";

                string html = $@"<div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 8px;'><div style='background: linear-gradient(135deg, #0f172a 0%, #6d2d9e 100%); padding: 25px; text-align: center; border-radius: 8px 8px 0 0;'><h2 style='color: #fff; margin: 0;'>Revisión de Certificado {nombreCert}{(version > 1 ? $" (V{version})" : "")}</h2><p style='color:#cbd5e1; margin-top:5px;'>Booking: <strong>{op.NumeroBooking}</strong></p></div><div style='padding: 30px; background-color: #ffffff;'><p>Estimado cliente, junto con saludar,</p><p>A continuación enviamos copia del Certificado {nombreCert} para su revisión.</p><div style='text-align: center; margin: 25px 0;'><a href='{urlDescarga}' style='color: #0ea5e9; font-weight: bold; text-decoration: underline;'>Ver Documento Adjunto (PDF/JPG)</a></div><p>En caso de aprobar o requerir modificaciones, haga click en los botones correspondientes:</p><div style='text-align: center; margin-top: 30px; margin-bottom: 20px;'>{botonesHtml}</div><hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 30px 0;'><p style='font-size: 11px; color: #94a3b8; text-align: center; margin: 0;'>Atilson Cargo SpA.</p></div></div>";

                await _emailService.EnviarCorreoAsync(correoDestino, $"[ATILSON CARGO] Revisión Certificado {nombreCert}{(version > 1 ? $" (V{version})" : "")} - BKG {op.NumeroBooking}", html);

                string logEntry = $"<div style='border-left:2px solid #0ea5e9; padding:6px; margin-bottom:6px; background:#f0f9ff; border-top:1px solid #bae6fd; border-right:1px solid #bae6fd; border-bottom:1px solid #bae6fd;'><div class='d-flex justify-content-between align-items-center mb-1'><span style='font-weight:800; color:#0284c7; text-transform:uppercase; font-size:10px;'>ENVÍO A CLIENTE{(version > 1 ? $" (V{version})" : "")}</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Operaciones Atilson</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>Certificado enviado para revisión a: <strong>{correoDestino}</strong></div></div>\n";
                if (doc != null) { if (tipo == "san") doc.LogSanitario = logEntry + (doc.LogSanitario ?? ""); else if (tipo == "cap") doc.LogCaptura = logEntry + (doc.LogCaptura ?? ""); }

                op.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }
            catch (Exception ex) { return new JsonResult(new { success = false, message = ex.Message }); }
        }

        private void RegistrarLogSagModalidad(OperacionesDocumentale doc, string nuevaModalidad, string usuario)
        {
            string fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            string texto = nuevaModalidad == "Planta"
                ? "SAG definido como EN PLANTA — sin costo para Atilson, Agencia ni Cliente."
                : "SAG definido como FUERA DE PLANTA (Puerto) — se activa el cobro para Atilson, Agencia y Cliente.";
            string color = nuevaModalidad == "Planta" ? "#16a34a" : "#d97706";
            string bg = nuevaModalidad == "Planta" ? "#f0fdf4" : "#fffbeb";
            string borde = nuevaModalidad == "Planta" ? "#bbf7d0" : "#fde68a";
            string entrada = $"<div style='border-left:2px solid {color}; padding:6px; margin-bottom:6px; background:{bg}; border-top:1px solid {borde}; border-right:1px solid {borde}; border-bottom:1px solid {borde};'><div style='display:flex; justify-content:space-between; align-items:center; margin-bottom:4px;'><span style='font-weight:800; color:{color}; text-transform:uppercase; font-size:10px;'>MODALIDAD SAG</span><span style='color:#64748b; font-size:9px;'>{fecha}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>{usuario}</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>{texto}</div></div>\n";
            doc.LogFitosanitario = entrada + (doc.LogFitosanitario ?? "");
        }

        // ===== Gestión de Transbordo =====
        public async Task<IActionResult> OnPostGuardarTransbordoAsync(int IdOperacion, DateTime? FechaArriboTransbordo, int? DiasLibres)
        {
            var op = await _context.Operaciones.Include(o => o.GestionTransbordo).FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

            var trans = op.GestionTransbordo;
            if (trans == null)
            {
                trans = new GestionTransbordo { IdOperacion = IdOperacion, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" };
                _context.GestionesTransbordo.Add(trans);
            }

            trans.FechaArriboTransbordo = FechaArriboTransbordo;
            trans.DiasLibres = DiasLibres;
            trans.FechaModificacion = DateTime.Now;
            trans.UsuarioModificador = User.Identity?.Name ?? "Sistema";

            RegistrarHito(op, "MARITIMO", $"Transbordo — Puerto: {op.Transbordo ?? "-"} | Arribo: {FechaArriboTransbordo?.ToString("dd/MM/yyyy HH:mm") ?? "-"} | Días libres: {DiasLibres?.ToString() ?? "-"}");
            op.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
            TempData["SuccessMsg"] = "Gestión de transbordo actualizada.";
            return RedirectToPage(new { id = IdOperacion });
        }

        public async Task<IActionResult> OnPostConfirmarNuevaNaveTransbordoAsync(int IdOperacion, string? NuevaNaveConfirmada, DateTime? NuevoEtdTransbordo, DateTime? NuevoEtaDestinoFinal, bool SincronizarEtaOperacion)
        {
            var op = await _context.Operaciones
                .Include(o => o.GestionTransbordo)
                .Include(o => o.ExtracostosOperacions)
                .FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

            var trans = op.GestionTransbordo;
            if (trans == null)
            {
                trans = new GestionTransbordo { IdOperacion = IdOperacion, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" };
                _context.GestionesTransbordo.Add(trans);
            }

            bool esReconfirmacion = trans.NuevoEtdTransbordo.HasValue;

            trans.NuevaNaveConfirmada = NuevaNaveConfirmada;
            trans.NuevoEtdTransbordo = NuevoEtdTransbordo;
            trans.NuevoEtaDestinoFinal = NuevoEtaDestinoFinal;
            trans.FechaModificacion = DateTime.Now;
            trans.UsuarioModificador = User.Identity?.Name ?? "Sistema";

            if (SincronizarEtaOperacion && NuevoEtaDestinoFinal.HasValue)
            {
                op.EtaPod = NuevoEtaDestinoFinal;
            }

            RegistrarHito(op, "MARITIMO", $"Nueva nave de transbordo confirmada — Nave: {NuevaNaveConfirmada ?? "-"} | ETD: {NuevoEtdTransbordo?.ToString("dd/MM/yyyy HH:mm") ?? "-"} | ETA Final: {NuevoEtaDestinoFinal?.ToString("dd/MM/yyyy HH:mm") ?? "-"}");
            op.FechaModificacion = DateTime.Now;
            op.CorreoClienteEnviado = false;

            // Primera confirmación: se deja un extracosto en $0 para que Comercial/Finanzas
            // valide si el cambio de nave implica ajuste de tarifa — mismo patrón que LAR.
            if (!esReconfirmacion)
            {
                op.ExtracostosOperacions.Add(new ExtracostosOperacion
                {
                    TipoCosto = "Ajuste Nave Transbordo",
                    Motivo = $"Nave de transbordo confirmada: {NuevaNaveConfirmada ?? "-"}. Validar si corresponde ajuste de tarifa marítima.",
                    Monto = 0,
                    Moneda = "USD",
                    Evidencia = "Generado automáticamente por sistema",
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = User.Identity?.Name ?? "Sistema"
                });
            }

            await _context.SaveChangesAsync();
            await _finanzasSync.SincronizarAsync(IdOperacion, User.Identity?.Name ?? "Sistema");

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
            TempData["SuccessMsg"] = "Nueva nave de transbordo confirmada.";
            return RedirectToPage(new { id = IdOperacion });
        }

        // ===== Marítimo — copiado tal cual del handler real de Index.cshtml.cs =====

        // ===== Marítimo — copiado tal cual del handler real de Index.cshtml.cs =====
        public async Task<IActionResult> OnPostQuickEditMaritimoAsync(
            int IdOperacion, int? IdNaviera, string? Nave, string? TipoTarifaMaritima, string? Transbordo,
            int? IdPuertoOrigen, int? IdPuertoDestino, string? TerminalTerrestreStr,
            DateTime? EtdPol, DateTime? EtaPod, DateTime? FechaStacking, DateTime? CutOffMatriz,
            DateTime? LateArrival, DateTime? ElarDate)
        {
            var operacion = await _context.Operaciones
                .Include(o => o.OperacionesTerrestres)
                .FirstOrDefaultAsync(o => o.Id == IdOperacion);

            if (operacion == null)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = false, message = "Operación no encontrada" });
                return RedirectToPage(new { id = IdOperacion });
            }

            if (Request.Form.ContainsKey("IdNaviera") && IdNaviera.HasValue) operacion.IdNaviera = IdNaviera.Value;
            if (Request.Form.ContainsKey("Nave")) operacion.Nave = Nave;
            if (Request.Form.ContainsKey("TipoTarifaMaritima")) operacion.TipoTarifaMaritima = TipoTarifaMaritima;
            if (Request.Form.ContainsKey("Transbordo")) operacion.Transbordo = Transbordo;
            if (Request.Form.ContainsKey("IdPuertoOrigen") && IdPuertoOrigen.HasValue) operacion.IdPuertoOrigen = IdPuertoOrigen.Value;
            if (Request.Form.ContainsKey("IdPuertoDestino") && IdPuertoDestino.HasValue) operacion.IdPuertoDestino = IdPuertoDestino.Value;
            if (Request.Form.ContainsKey("EtdPol")) operacion.EtdPol = EtdPol;
            if (Request.Form.ContainsKey("EtaPod")) operacion.EtaPod = EtaPod;
            if (Request.Form.ContainsKey("FechaStacking")) operacion.FechaStacking = FechaStacking;
            if (Request.Form.ContainsKey("CutOffMatriz")) operacion.CutOffMatriz = CutOffMatriz;
            if (Request.Form.ContainsKey("LateArrival")) operacion.LateArrival = LateArrival;
            if (Request.Form.ContainsKey("ElarDate")) operacion.ElarDate = ElarDate;

            if (Request.Form.ContainsKey("TerminalTerrestreStr"))
            {
                var terrPrincipal = operacion.OperacionesTerrestres.FirstOrDefault(x => x.IdUnidadTecnica == null);
                if (terrPrincipal == null)
                {
                    terrPrincipal = new OperacionesTerrestre { IdOperacion = operacion.Id, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true };
                    operacion.OperacionesTerrestres.Add(terrPrincipal);
                }
                terrPrincipal.TerminalTerrestreStr = TerminalTerrestreStr;
            }

            operacion.FechaModificacion = DateTime.Now;
            operacion.UsuarioModificador = User.Identity?.Name ?? "Sistema";
            operacion.CorreoClienteEnviado = false;
            RegistrarHito(operacion, "MARITIMO", $"Datos marítimos actualizados — Naviera: {IdNaviera?.ToString() ?? "-"} | Nave: {Nave ?? "-"} | Tarifa: {TipoTarifaMaritima ?? "-"} | ETD: {EtdPol?.ToString("dd/MM HH:mm") ?? "-"}");

            await _context.SaveChangesAsync();
            await _finanzasSync.SincronizarAsync(IdOperacion, User.Identity?.Name ?? "Sistema");

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
            TempData["SuccessMsg"] = "Información marítima actualizada.";
            return RedirectToPage(new { id = IdOperacion });
        }

        // ===== Gate — copiado tal cual del handler real de Index.cshtml.cs =====
        public async Task<IActionResult> OnPostUpdateGateAsync(int IdOperacion, string? GestionaGate, string? PagoGate, string? TipoGate)
        {
            var op = await _context.Operaciones.FindAsync(IdOperacion);
            if (op != null)
            {
                if (op.GateBloqueado)
                {
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                        return new JsonResult(new { success = false, message = "Gate bloqueado: solicita el desbloqueo a Finanzas." });
                    TempData["ErrorMsg"] = "Gate bloqueado: solicita el desbloqueo a Finanzas.";
                    return RedirectToPage(new { id = IdOperacion });
                }

                op.GestionaGate = GestionaGate; op.PagoGate = PagoGate; op.TipoGate = TipoGate; op.CorreoClienteEnviado = false;
                op.GateBloqueado = true;
                RegistrarHito(op, "GENERAL", $"Gate guardado y bloqueado — Tipo: {TipoGate ?? "-"} | Gestiona: {GestionaGate ?? "-"} | Pago: {PagoGate ?? "-"}");
                await _context.SaveChangesAsync();
                await _finanzasSync.SincronizarAsync(IdOperacion, User.Identity?.Name ?? "Sistema");
                await SincronizarResponsabilidadGateAsync(IdOperacion, GestionaGate, PagoGate);

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Configuración de Gate guardada y bloqueada con éxito.";
            }
            else if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = false, message = "Operación no encontrada" });
            return RedirectToPage(new { id = IdOperacion });
        }

        private async Task SincronizarResponsabilidadGateAsync(int idOperacion, string? gestionaGate, string? pagoGate)
        {
            string responsablePago;
            string responsable;

            if (gestionaGate == "Cliente gestiona directo")
            {
                responsablePago = "CLIENTE";
                responsable = "CLIENTE";
            }
            else
            {
                responsablePago = (pagoGate ?? "").Contains("Agencia", StringComparison.OrdinalIgnoreCase) ? "AGENCIA" : "ATILSON";
                responsable = gestionaGate == "Atilson entrega concesión incluido para cliente" ? "ATILSON" : "CLIENTE";
            }

            var txsGate = await _context.TransaccionesFinancieras
                .Where(t => t.IdOperacion == idOperacion && t.TipoMovimiento == "EGRESO" &&
                            ((t.Concepto ?? "").Contains("Gate OUT Contenedor") || (t.Concepto ?? "").Contains("Gate IN Contenedor")))
                .ToListAsync();

            if (!txsGate.Any()) return;

            foreach (var tx in txsGate)
            {
                tx.ResponsablePago = responsablePago;
                tx.Responsable = responsable;
                tx.FechaModificacion = DateTime.Now;
                tx.UsuarioModificador = User.Identity?.Name ?? "Sistema";
            }
            await _context.SaveChangesAsync();
        }

        // ===== Matriz y B/L — copiado tal cual de Index.cshtml.cs =====
        public async Task<IActionResult> OnPostUpdateMatrizAsync(int IdOperacion, bool MatrizPresentada, IFormFile? EvidenciaMatriz, IFormFile? EvidenciaEnvioNaviera, string? NumeroBL, IFormFile? ArchivoBL)
        {
            var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op != null)
            {
                var docDb = op.OperacionesDocumentales.OrderBy(x => x.Id).FirstOrDefault();
                if (docDb == null) { docDb = new OperacionesDocumentale { FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true }; op.OperacionesDocumentales.Add(docDb); }
                docDb.MatrizPresentada = MatrizPresentada;
                if (Request.Form.ContainsKey("docUpdates.LogMatriz")) docDb.LogMatriz = Request.Form["docUpdates.LogMatriz"].FirstOrDefault();

                if (Request.Form.ContainsKey("TipoEmisionBlMarker"))
                {
                    var seleccionadosBl = Request.Form["TipoEmisionBl"].Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
                    op.TipoEmisionBl = seleccionadosBl.Any() ? string.Join(",", seleccionadosBl) : null;
                    op.TipoEmisionBlOtro = seleccionadosBl.Contains("OTRO") ? Request.Form["TipoEmisionBlOtro"].ToString()?.Trim() : null;
                    op.UbicacionOriginalesDestino = seleccionadosBl.Contains("ORIGINALES") ? Request.Form["UbicacionOriginalesDestino"].ToString()?.Trim() : null;
                }

                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "evidencias");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                if (EvidenciaMatriz != null && EvidenciaMatriz.Length > 0) { string uNameMatriz = $"{op.Id}_Matriz_{DateTime.Now.Ticks}_{Path.GetFileName(EvidenciaMatriz.FileName).Replace(" ", "_")}"; using (var stream = new FileStream(Path.Combine(uploadsFolder, uNameMatriz), FileMode.Create)) await EvidenciaMatriz.CopyToAsync(stream); docDb.EvidenciaMatriz = $"/uploads/evidencias/{uNameMatriz}"; }
                if (EvidenciaEnvioNaviera != null && EvidenciaEnvioNaviera.Length > 0) { string uNameEnvio = $"{op.Id}_EnvioNaviera_{DateTime.Now.Ticks}_{Path.GetFileName(EvidenciaEnvioNaviera.FileName).Replace(" ", "_")}"; using (var stream = new FileStream(Path.Combine(uploadsFolder, uNameEnvio), FileMode.Create)) await EvidenciaEnvioNaviera.CopyToAsync(stream); }
                if (ArchivoBL != null && ArchivoBL.Length > 0) { string uNameBL = $"{op.Id}_BL_{DateTime.Now.Ticks}_{Path.GetFileName(ArchivoBL.FileName).Replace(" ", "_")}"; using (var stream = new FileStream(Path.Combine(uploadsFolder, uNameBL), FileMode.Create)) await ArchivoBL.CopyToAsync(stream); docDb.RutaBL = $"/uploads/evidencias/{uNameBL}"; }

                op.FechaModificacion = DateTime.Now; op.UsuarioModificador = User.Identity?.Name ?? "Sistema";
                RegistrarHito(op, "DOCUMENTAL", $"Matriz actualizada — Presentada: {(MatrizPresentada ? "✓ Confirmada" : "Pendiente")} | B/L: {NumeroBL ?? "-"}");
                op.CorreoClienteEnviado = false;

                await _context.SaveChangesAsync();
                await _finanzasSync.SincronizarAsync(IdOperacion, User.Identity?.Name ?? "Sistema");

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Documentación de Matriz y B/L actualizada correctamente.";
            }
            return RedirectToPage(new { id = IdOperacion });
        }

        // ===== Responsabilidad (Pagado Por / Responsable) — copiado tal cual =====
        public async Task<IActionResult> OnPostGuardarResponsabilidadAsync(int idOperacion, string concepto, string pagadoPor, string responsable)
        {
            var transacciones = await _context.TransaccionesFinancieras
                .Where(t => t.IdOperacion == idOperacion && t.Concepto == concepto)
                .ToListAsync();

            if (!transacciones.Any())
                return new JsonResult(new { success = false, message = "No se encontró el concepto." });

            foreach (var tx in transacciones)
            {
                tx.ResponsablePago = pagadoPor;
                tx.Responsable = responsable;
                tx.FechaModificacion = DateTime.Now;
                tx.UsuarioModificador = User.Identity?.Name ?? "Operaciones";
            }
            await _context.SaveChangesAsync();
            await _finanzasSync.SincronizarAsync(idOperacion, User.Identity?.Name ?? "Sistema");

            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostAddCostoAsync(int id)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            string nombreUsuario = User.Identity?.Name ?? "Sistema";
            var tipo = Request.Form["NuevoCosto.TipoCosto"];
            var motivo = Request.Form["NuevoCosto.Motivo"];
            var responsable = Request.Form["NuevoCosto.Responsable"];
            var responsableGestion = Request.Form["NuevoCosto.ResponsableGestion"];
            var grupoOperativo = Request.Form["NuevoCosto.GrupoOperativo"];
            var monedaForm = Request.Form["NuevoCosto.Moneda"];
            decimal.TryParse(Request.Form["NuevoCosto.Monto"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal montoForm);
            var file = Request.Form.Files.GetFile("NuevoCosto.Evidencia");

            if (!string.IsNullOrEmpty(tipo) && file != null && file.Length > 0 && montoForm > 0)
            {
                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "evidencias");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string uName = $"{id}_Costo_{DateTime.Now.Ticks}_{Path.GetFileName(file.FileName).Replace(" ", "_")}";
                using (var s = new FileStream(Path.Combine(uploadsFolder, uName), FileMode.Create)) { await file.CopyToAsync(s); }
                var costo = new ExtracostosOperacion
                {
                    IdOperacion = id,
                    TipoCosto = tipo,
                    Motivo = motivo,
                    Responsable = responsable,
                    ResponsableGestion = responsableGestion,
                    GrupoOperativo = string.IsNullOrWhiteSpace(grupoOperativo) ? null : grupoOperativo.ToString(),
                    Moneda = string.IsNullOrWhiteSpace(monedaForm) ? "CLP" : monedaForm.ToString(),
                    Monto = montoForm,
                    Evidencia = $"/uploads/evidencias/{uName}",
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = nombreUsuario
                };
                _context.ExtracostosOperacions.Add(costo);
                var opCosto = await _context.Operaciones.FindAsync(id);
                if (opCosto != null) { RegistrarHito(opCosto, "GENERAL", $"Extracosto reportado — Tipo: {tipo} | Valor: {costo.Moneda} {montoForm:N0} | Gestiona: {responsableGestion} | Paga: {responsable} | Motivo: {motivo}"); }

                await _context.SaveChangesAsync();
                await _finanzasSync.SincronizarAsync(id, User.Identity?.Name ?? "Sistema");

                if (isAjax) return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Incidencia financiera reportada exitosamente.";
            }
            else if (isAjax) return new JsonResult(new { success = false, message = "Faltan datos obligatorios (categoría, valor o evidencia)." });
            return RedirectToPage(new { id });
        }

        // ===== Roleo / Cancelación — copiado tal cual (incluye el helper de penalidad) =====
        private (bool sugiere, int? dias) CalcularPenalidadSugerida(Naviera? naviera, DateTime? etd, string? tipoTarifa)
        {
            if (naviera == null || !etd.HasValue) return (false, null);
            int dias = (int)Math.Ceiling((etd.Value - DateTime.Now).TotalDays);
            int umbral = naviera.DiasPenalidadRoleo;
            if (dias > umbral) return (false, dias);
            if (naviera.PenalidadRoleoSiempreAplica) return (true, dias);
            bool esSpot = (tipoTarifa ?? "").ToUpper() == "SPOT";
            return (esSpot, dias);
        }

        public async Task<IActionResult> OnPostRegistrarRoleoCancelacionAsync(
            int idOperacion, int? idUnidadTecnica, string tipo, string? penalidadOverride, string? comentario,
            int cantidadContenedoresAfectados,
            string? nuevaNave, DateTime? nuevoEtd, DateTime? nuevoEta, DateTime? nuevoStacking, DateTime? nuevoCierreDocumental,
            bool datosNuevosPendientes)
        {
            if (tipo != "ROLEO" && tipo != "CANCELACION_NOSHOW")
                return new JsonResult(new { success = false, message = "Tipo no reconocido." });

            var op = await _context.Operaciones
                .Include(o => o.ExtracostosOperacions)
                .Include(o => o.IdNavieraNavigation)
                .FirstOrDefaultAsync(o => o.Id == idOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

            string usuario = User.Identity?.Name ?? "Operaciones";
            var (sugerida, dias) = CalcularPenalidadSugerida(op.IdNavieraNavigation, op.EtdPol, op.TipoTarifaMaritima);

            string penalidadFinal = !string.IsNullOrWhiteSpace(penalidadOverride) && penalidadOverride != "AUTOMATICO"
                ? penalidadOverride
                : (sugerida ? "SI" : "NO");

            var registro = new RoleoCancelacion
            {
                IdOperacion = idOperacion,
                IdUnidadTecnica = idUnidadTecnica,
                Tipo = tipo,
                FechaSolicitud = DateTime.Now,
                EtdOriginal = op.EtdPol,
                DiasAnticipacion = dias,
                NavieraTipoTarifa = op.TipoTarifaMaritima,
                PenalidadSistemaSugiere = sugerida,
                PenalidadConfirmada = penalidadFinal,
                CantidadContenedoresAfectados = cantidadContenedoresAfectados < 1 ? 1 : cantidadContenedoresAfectados,
                MontoPenalidadUnitarioUsd = op.IdNavieraNavigation?.MontoPenalidadRoleoUsd,
                Comentario = comentario?.Trim(),
                NuevaNave = nuevaNave,
                NuevoEtd = nuevoEtd,
                NuevoEta = nuevoEta,
                NuevoStacking = nuevoStacking,
                NuevoCierreDocumental = nuevoCierreDocumental,
                DatosNuevosPendientes = datosNuevosPendientes,
                Usuario = usuario,
                FechaCreacion = DateTime.Now
            };
            _context.RoleosCancelaciones.Add(registro);

            if (!datosNuevosPendientes)
            {
                if (!string.IsNullOrWhiteSpace(nuevaNave)) op.Nave = nuevaNave;
                if (nuevoEtd.HasValue) op.EtdPol = nuevoEtd;
                if (nuevoEta.HasValue) op.EtaPod = nuevoEta;
                if (nuevoStacking.HasValue) op.FechaStacking = nuevoStacking;
                if (nuevoCierreDocumental.HasValue) op.CutOffMatriz = nuevoCierreDocumental;
            }

            string nombreTipo = tipo == "ROLEO" ? "Roleo Marítimo" : "Cancelación / No Show Marítimo";
            string textoPenalidad = penalidadFinal switch { "SI" => "CON penalidad.", "NO" => "SIN penalidad.", _ => "Penalidad por determinar." };
            string textoDias = dias.HasValue ? $" (a {dias} días del zarpe" + (op.IdNavieraNavigation?.PenalidadRoleoSiempreAplica == true ? ", naviera con umbral especial)" : $", umbral {op.IdNavieraNavigation?.DiasPenalidadRoleo ?? 7} días)") : "";

            decimal montoTotal = 0;
            if (penalidadFinal == "SI" && registro.MontoPenalidadUnitarioUsd.HasValue)
                montoTotal = registro.MontoPenalidadUnitarioUsd.Value * registro.CantidadContenedoresAfectados;

            op.ExtracostosOperacions.Add(new ExtracostosOperacion
            {
                TipoCosto = nombreTipo,
                Motivo = $"{textoPenalidad}{textoDias} — {registro.CantidadContenedoresAfectados} contenedor(es) afectado(s).{(string.IsNullOrWhiteSpace(comentario) ? "" : $" {comentario.Trim()}")}",
                Monto = montoTotal,
                Moneda = "USD",
                Evidencia = "Generado automáticamente por sistema",
                FechaCreacion = DateTime.Now,
                UsuarioCreador = usuario
            });

            RegistrarHito(op, "MARITIMO", $"{nombreTipo} registrado — {textoPenalidad}{textoDias}" + (datosNuevosPendientes ? " | Nuevo itinerario PENDIENTE de confirmar." : $" | Nueva nave: {nuevaNave ?? "-"}"));

            await _context.SaveChangesAsync();
            await _finanzasSync.SincronizarAsync(idOperacion, User.Identity?.Name ?? "Sistema");

            return new JsonResult(new { success = true, idRegistro = registro.Id, penalidadSugerida = sugerida, penalidadConfirmada = penalidadFinal, dias, fecha = registro.FechaCreacion.ToString("dd/MM/yyyy HH:mm") });
        }

        public async Task<IActionResult> OnPostConfirmarItinerarioRoleoAsync(int idRegistro, string? nuevaNave, DateTime? nuevoEtd, DateTime? nuevoEta, DateTime? nuevoStacking, DateTime? nuevoCierreDocumental)
        {
            var registro = await _context.RoleosCancelaciones.Include(r => r.IdOperacionNavigation).FirstOrDefaultAsync(r => r.Id == idRegistro);
            if (registro == null) return new JsonResult(new { success = false, message = "Registro no encontrado." });

            registro.NuevaNave = nuevaNave;
            registro.NuevoEtd = nuevoEtd;
            registro.NuevoEta = nuevoEta;
            registro.NuevoStacking = nuevoStacking;
            registro.NuevoCierreDocumental = nuevoCierreDocumental;
            registro.DatosNuevosPendientes = false;

            var op = registro.IdOperacionNavigation;
            if (op != null)
            {
                if (!string.IsNullOrWhiteSpace(nuevaNave)) op.Nave = nuevaNave;
                if (nuevoEtd.HasValue) op.EtdPol = nuevoEtd;
                if (nuevoEta.HasValue) op.EtaPod = nuevoEta;
                if (nuevoStacking.HasValue) op.FechaStacking = nuevoStacking;
                if (nuevoCierreDocumental.HasValue) op.CutOffMatriz = nuevoCierreDocumental;
                RegistrarHito(op, "MARITIMO", $"Itinerario de roleo confirmado — Nueva nave: {nuevaNave ?? "-"} | ETD: {nuevoEtd?.ToString("dd/MM HH:mm") ?? "-"}");
            }

            await _context.SaveChangesAsync();
            if (op != null) await _finanzasSync.SincronizarAsync(op.Id, User.Identity?.Name ?? "Sistema");

            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostActualizarPenalidadRoleoAsync(int idRegistro, string penalidad)
        {
            var registro = await _context.RoleosCancelaciones
                .Include(r => r.IdOperacionNavigation).ThenInclude(op => op.ExtracostosOperacions)
                .FirstOrDefaultAsync(r => r.Id == idRegistro);
            if (registro == null) return new JsonResult(new { success = false, message = "Registro no encontrado." });

            registro.PenalidadConfirmada = penalidad;
            var op = registro.IdOperacionNavigation;

            if (op != null)
            {
                string nombreTipo = registro.Tipo == "ROLEO" ? "Roleo Marítimo" : "Cancelación / No Show Marítimo";
                string textoPenalidad = penalidad switch { "SI" => "CON penalidad.", "NO" => "SIN penalidad.", _ => "Penalidad por determinar." };
                decimal montoTotal = 0;
                if (penalidad == "SI" && registro.MontoPenalidadUnitarioUsd.HasValue)
                    montoTotal = registro.MontoPenalidadUnitarioUsd.Value * registro.CantidadContenedoresAfectados;

                var extracosto = op.ExtracostosOperacions
                    .Where(e => e.TipoCosto == nombreTipo)
                    .FirstOrDefault(e => Math.Abs((e.FechaCreacion - registro.FechaCreacion).TotalMinutes) < 5);

                if (extracosto != null)
                {
                    extracosto.Monto = montoTotal;
                    extracosto.Motivo = $"{textoPenalidad} — {registro.CantidadContenedoresAfectados} contenedor(es) afectado(s).{(string.IsNullOrWhiteSpace(registro.Comentario) ? "" : $" {registro.Comentario}")}";
                }

                RegistrarHito(op, "MARITIMO", $"{nombreTipo}: penalidad actualizada — {textoPenalidad}");
                await _finanzasSync.SincronizarAsync(op.Id, User.Identity?.Name ?? "Sistema");
            }

            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true, penalidad = registro.PenalidadConfirmada });
        }
        private void RegistrarHito(Operacione op, string modulo, string descripcion)
        {
            string usuario = User.Identity?.Name ?? "Sistema";
            string fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            string linea = $"{fecha}|{usuario}|{modulo}|{descripcion}\n";
            op.HistorialCorreos = linea + (op.HistorialCorreos ?? "");
        }
        public (string? PagadoPor, string? Responsable, bool PendienteTarifa, string ConceptoReal) ObtenerEstadoConcepto(Operacione op, string conceptoBusqueda, int idUnidadTecnica)
        {
            string conceptoExacto = $"{conceptoBusqueda} (U{idUnidadTecnica})";
            var tx = op.TransaccionesFinancieras?.FirstOrDefault(t => t.TipoMovimiento == "EGRESO" && t.Concepto == conceptoExacto);
            if (tx == null) return (null, null, false, conceptoExacto);
            bool pendiente = tx.MontoNeto == 0m && !tx.TarifaManual;
            return (tx.ResponsablePago, tx.Responsable, pendiente, tx.Concepto);
        }

        public bool HayTarifaPendiente(Operacione op, params string[] conceptosBusqueda)
        {
            if (op.TransaccionesFinancieras == null) return false;
            return conceptosBusqueda.Any(cb => op.TransaccionesFinancieras.Any(t =>
                t.TipoMovimiento == "EGRESO" && (t.Concepto ?? "").Contains(cb) && t.MontoNeto == 0m && !t.TarifaManual));
        }

        public bool HayTarifaPendiente(Operacione op, int idUnidadTecnica, params string[] conceptosBusqueda)
        {
            if (op.TransaccionesFinancieras == null) return false;
            return conceptosBusqueda.Any(cb =>
            {
                string conceptoExacto = $"{cb} (U{idUnidadTecnica})";
                return op.TransaccionesFinancieras.Any(t => t.TipoMovimiento == "EGRESO" && t.Concepto == conceptoExacto && t.MontoNeto == 0m && !t.TarifaManual);
            });
        }

        public (string? PagadoPor, string? Responsable, bool PendienteTarifa, string ConceptoReal) ObtenerEstadoConceptoExacto(Operacione op, string conceptoExacto)
        {
            var tx = op.TransaccionesFinancieras?.FirstOrDefault(t => t.TipoMovimiento == "EGRESO" && t.Concepto == conceptoExacto);
            if (tx == null) return (null, null, false, conceptoExacto);
            bool pendiente = tx.MontoNeto == 0m && !tx.TarifaManual;
            return (tx.ResponsablePago, tx.Responsable, pendiente, tx.Concepto);
        }

        public bool ModalidadCertBloqueada(Operacione op, string certKey)
        {
            if (op.OperacionesDocumentales == null) return false;
            if (op.OperacionesDocumentales.Count > 1)
                return op.OperacionesDocumentales.Any(x => TieneDatosCert(x, certKey));
            var principal = op.OperacionesDocumentales.FirstOrDefault();
            return principal != null && TieneDatosCert(principal, certKey);
        }

        private bool TieneDatosCert(OperacionesDocumentale x, string certKey) => certKey switch
        {
            "ori" => !string.IsNullOrWhiteSpace(x.RemisionOrigen1),
            "fit" => !string.IsNullOrWhiteSpace(x.CertFitosanitario),
            "san" => !string.IsNullOrWhiteSpace(x.CertSanitario),
            "cap" => !string.IsNullOrWhiteSpace(x.NumCap1),
            _ => false
        };

        public async Task<IActionResult> OnPostGuardarCertificadoAsync(
            int IdOperacion, int? IdUnidadTecnica, string CertKey, bool EnviarFinanzas, bool SinNumero,
            IFormFile? UploadCertA1, IFormFile? UploadCertA2, IFormFile? UploadCertA3, IFormFile? UploadCertA4)
        {
            var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada" });

            var docDb = op.OperacionesDocumentales.FirstOrDefault(x => x.IdUnidadTecnica == IdUnidadTecnica);
            if (docDb == null)
            {
                docDb = new OperacionesDocumentale { IdUnidadTecnica = IdUnidadTecnica, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true };
                op.OperacionesDocumentales.Add(docDb);
            }

            string? F(string key) { var v = Request.Form[key].FirstOrDefault(); return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }
            bool FBoolPreserve(string key, bool valorActual) { if (Request.Form.TryGetValue(key, out var vals)) return vals.Any(v => v == "true" || v == "on"); return valorActual; }
            int? FInt(string key) { var val = Request.Form[key].FirstOrDefault(); return int.TryParse(val, out var r) ? r : null; }
            int? ParseDinero(string key) { var val = Request.Form[key].FirstOrDefault(); if (string.IsNullOrWhiteSpace(val)) return null; var clean = new string(val.Where(c => char.IsDigit(c) || c == '-').ToArray()); if (string.IsNullOrWhiteSpace(clean)) return null; return int.TryParse(clean, out var r) ? r : null; }

            bool esCertConReemisiones = CertKey == "ori" || CertKey == "fit" || CertKey == "san" || CertKey == "cap";
            int targetSlot = esCertConReemisiones ? DocumentalHelpers.SiguienteSlotEditable(docDb, CertKey) : 1;
            if (esCertConReemisiones && EnviarFinanzas && targetSlot > 4)
                return new JsonResult(new { success = false, message = "Ya se alcanzó el máximo de 3 reemisiones para este certificado." });

            string CampoNumParaSlot(string certKey, int slot) => certKey switch
            {
                "ori" => slot switch { 1 => "docUpdates.NumOri1", 2 => "docUpdates.NumOri2", 3 => "docUpdates.NumOri3", _ => "docUpdates.NumOri4" },
                "fit" => slot switch { 1 => "docUpdates.NumFit1", 2 => "docUpdates.NumFit2", 3 => "docUpdates.NumFit3", _ => "docUpdates.NumFit4" },
                "san" => slot switch { 1 => "docUpdates.NumSan1", 2 => "docUpdates.NumSan2", 3 => "docUpdates.NumSan3", _ => "docUpdates.NumSan4" },
                "cap" => slot switch { 1 => "docUpdates.NumCap1", 2 => "docUpdates.NumCap2", 3 => "docUpdates.NumCap3", _ => "docUpdates.NumCap4" },
                "coa" => "docUpdates.NumCoa1",
                "dt" => "docUpdates.NumDt1",
                "cod" => "docUpdates.NumCod1",
                "cla" => "docUpdates.NumCla1",
                "nep" => "docUpdates.NumNep1",
                _ => ""
            };
            string campoNumPrincipal = CampoNumParaSlot(CertKey, targetSlot);

            bool esUnoDeEstosCuatro = CertKey == "ori" || CertKey == "fit" || CertKey == "san" || CertKey == "cap";
            if (EnviarFinanzas && !SinNumero && esUnoDeEstosCuatro && string.IsNullOrWhiteSpace(F(campoNumPrincipal)))
                return new JsonResult(new { success = false, message = "Falta N° de documento. Marca 'Enviar sin número' si corresponde." });

            if (Request.Form.ContainsKey("docUpdates.AplicaSag")) docDb.AplicaSag = FBoolPreserve("docUpdates.AplicaSag", docDb.AplicaSag);
            if (Request.Form.ContainsKey("docUpdates.AplicaSernapesca")) docDb.AplicaSernapesca = FBoolPreserve("docUpdates.AplicaSernapesca", docDb.AplicaSernapesca);

            bool isReqDocumental = new[] { 1, 3, 4, 7, 8, 10, 11, 14 }.Contains(op.IdTipoServicio ?? 0);
            docDb.CertificadoOrigen = isReqDocumental || docDb.AplicaSag || docDb.AplicaSernapesca;

            switch (CertKey)
            {
                case "ori":
                    docDb.RemisionOrigen1 = F("docUpdates.NumOri1"); docDb.RemisionOrigen2 = F("docUpdates.NumOri2"); docDb.NumOri3 = F("docUpdates.NumOri3"); docDb.NumOri4 = F("docUpdates.NumOri4");
                    docDb.ValOri1 = ParseDinero("docUpdates.ValOri1"); docDb.ValOri2 = ParseDinero("docUpdates.ValOri2"); docDb.ValOri3 = ParseDinero("docUpdates.ValOri3"); docDb.ValOri4 = ParseDinero("docUpdates.ValOri4");
                    docDb.ObsOrigen = F("docUpdates.ObsOrigen"); docDb.AcuerdoOrigen = F("docUpdates.AcuerdoOrigen"); docDb.CertificadoOrigen = true;
                    break;
                case "fit":
                    docDb.CertFitosanitario = F("docUpdates.NumFit1"); docDb.NumFit2 = F("docUpdates.NumFit2"); docDb.NumFit3 = F("docUpdates.NumFit3"); docDb.NumFit4 = F("docUpdates.NumFit4");
                    docDb.ValFit1 = ParseDinero("docUpdates.ValFit1"); docDb.ValFit2 = ParseDinero("docUpdates.ValFit2"); docDb.ValFit3 = ParseDinero("docUpdates.ValFit3"); docDb.ValFit4 = ParseDinero("docUpdates.ValFit4");
                    docDb.AplicaSag = true;
                    docDb.CertificadoOrigen = true;
                    if (Request.Form.ContainsKey("docUpdates.SagModalidad")) docDb.SagModalidad = F("docUpdates.SagModalidad");
                    break;
                case "san":
                    docDb.CertSanitario = F("docUpdates.NumSan1"); docDb.NumSan2 = F("docUpdates.NumSan2"); docDb.NumSan3 = F("docUpdates.NumSan3"); docDb.NumSan4 = F("docUpdates.NumSan4");
                    docDb.ValSan1 = ParseDinero("docUpdates.ValSan1"); docDb.ValSan2 = ParseDinero("docUpdates.ValSan2"); docDb.ValSan3 = ParseDinero("docUpdates.ValSan3"); docDb.ValSan4 = ParseDinero("docUpdates.ValSan4");
                    docDb.AplicaSernapesca = true;
                    docDb.CertificadoOrigen = true;
                    break;
                case "cap":
                    docDb.NumCap1 = F("docUpdates.NumCap1"); docDb.NumCap2 = F("docUpdates.NumCap2"); docDb.NumCap3 = F("docUpdates.NumCap3"); docDb.NumCap4 = F("docUpdates.NumCap4");
                    docDb.ValCap1 = ParseDinero("docUpdates.ValCap1"); docDb.ValCap2 = ParseDinero("docUpdates.ValCap2"); docDb.ValCap3 = ParseDinero("docUpdates.ValCap3"); docDb.ValCap4 = ParseDinero("docUpdates.ValCap4");
                    docDb.ObsCaptura = F("docUpdates.ObsCaptura"); docDb.AplicaSernapesca = true;
                    docDb.CertificadoOrigen = true;
                    break;
                case "coa": docDb.NumCoa1 = F("docUpdates.NumCoa1"); break;
                case "dt": docDb.NumDt1 = F("docUpdates.NumDt1"); break;
                case "cod": docDb.NumCod1 = F("docUpdates.NumCod1"); break;
                case "cla": docDb.NumCla1 = F("docUpdates.NumCla1"); break;
                case "nep": docDb.NumNep1 = F("docUpdates.NumNep1"); break;
                case "aduana":
                    if (Request.Form.ContainsKey("docUpdates.IdAgenciaAduana")) docDb.IdAgenciaAduana = FInt("docUpdates.IdAgenciaAduana");
                    docDb.DusDin = F("docUpdates.Dus"); docDb.Din = F("docUpdates.Din");
                    docDb.ValorDus = ParseDinero("docUpdates.ValorDus"); docDb.ValorDin = ParseDinero("docUpdates.ValorDin");
                    docDb.EstadoDocumental = F("docUpdates.EstadoDocumental");
                    docDb.ExtensionDocumental = FBoolPreserve("docUpdates.ExtensionDocumental", docDb.ExtensionDocumental == true);
                    docDb.GuiaVisado = FBoolPreserve("docUpdates.GuiaVisado", docDb.GuiaVisado == true);
                    docDb.Roleo = FBoolPreserve("docUpdates.Roleo", docDb.Roleo == true);
                    docDb.GeneracionIvv = FBoolPreserve("docUpdates.GeneracionIvv", docDb.GeneracionIvv == true);
                    break;
                default: return new JsonResult(new { success = false, message = "Certificado no reconocido (en esta fase solo Aduana/Origen)." });
            }

            string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "documentos");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
            async Task SaveFile(IFormFile? file, Action<string> setPath)
            {
                if (file == null || file.Length == 0) return;
                string fileName = $"{op.Id}_{DateTime.Now.Ticks}_{Path.GetFileName(file.FileName).Replace(" ", "_")}";
                using var stream = new FileStream(Path.Combine(uploadsFolder, fileName), FileMode.Create);
                await file.CopyToAsync(stream);
                setPath($"/uploads/documentos/{fileName}");
            }

            string slotEsActiva = targetSlot.ToString();
            Action<string> setEvidencia = (CertKey, slotEsActiva) switch
            {
                ("ori", "1") => p => docDb.EvidenciaOrigen1 = p,
                ("ori", "2") => p => docDb.EvidenciaOrigen2 = p,
                ("ori", "3") => p => docDb.EvidenciaOrigen3 = p,
                ("ori", "4") => p => docDb.EvidenciaOrigen4 = p,
                ("fit", "1") => p => docDb.EvidenciaFito1 = p,
                ("fit", "2") => p => docDb.EvidenciaFito2 = p,
                ("fit", "3") => p => docDb.EvidenciaFito3 = p,
                ("fit", "4") => p => docDb.EvidenciaFito4 = p,
                ("san", "1") => p => docDb.EvidenciaSanitario1 = p,
                ("san", "2") => p => docDb.EvidenciaSanitario2 = p,
                ("san", "3") => p => docDb.EvidenciaSanitario3 = p,
                ("san", "4") => p => docDb.EvidenciaSanitario4 = p,
                ("cap", "1") => p => docDb.EvidenciaCap1 = p,
                ("cap", "2") => p => docDb.EvidenciaCap2 = p,
                ("cap", "3") => p => docDb.EvidenciaCap3 = p,
                ("cap", "4") => p => docDb.EvidenciaCap4 = p,
                ("coa", _) => p => docDb.EvidenciaCoa1 = p,
                ("dt", _) => p => docDb.EvidenciaDt1 = p,
                ("cod", _) => p => docDb.EvidenciaCod1 = p,
                ("cla", _) => p => docDb.EvidenciaCla1 = p,
                ("nep", _) => p => docDb.EvidenciaNep1 = p,
                _ => p => { }
            };
            var archivo = Request.Form.Files.GetFile("UploadCertA1");
            await SaveFile(archivo, setEvidencia);

            string sufijoLog = CertKey switch { "ori" => "Origen", "fit" => "Fitosanitario", "san" => "Sanitario", "cap" => "Captura", "coa" => "Coa", "dt" => "Dt", "cod" => "Codaut", "cla" => "Clave", "nep" => "Neppex", _ => "" };
            string? logEntrante = Request.Form[$"docUpdates.Log{sufijoLog}"].FirstOrDefault();
            if (logEntrante != null)
            {
                switch (CertKey)
                {
                    case "ori": docDb.LogOrigen = logEntrante; break;
                    case "fit": docDb.LogFitosanitario = logEntrante; break;
                    case "san": docDb.LogSanitario = logEntrante; break;
                    case "cap": docDb.LogCaptura = logEntrante; break;
                    case "coa": docDb.LogCoa = logEntrante; break;
                    case "dt": docDb.LogDt = logEntrante; break;
                    case "cod": docDb.LogCodaut = logEntrante; break;
                    case "cla": docDb.LogClave = logEntrante; break;
                    case "nep": docDb.LogNeppex = logEntrante; break;
                }
            }

            if (esCertConReemisiones)
            {
                var set2 = (docDb.CertsSinNumero ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                string clave = $"{CertKey}{targetSlot}";
                if (SinNumero) set2.Add(clave); else set2.Remove(clave);
                docDb.CertsSinNumero = string.Join(",", set2);

                if (EnviarFinanzas)
                {
                    var setBloq = (docDb.CertsBloqueados ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                    setBloq.Add(CertKey);
                    setBloq.Add($"{CertKey}{targetSlot}");
                    docDb.CertsBloqueados = string.Join(",", setBloq);
                }
            }

            op.FechaModificacion = DateTime.Now;
            op.UsuarioModificador = User.Identity?.Name ?? "Sistema";
            RegistrarHito(op, "DOCUMENTAL", $"Certificado [{CertKey.ToUpper()}] guardado" + (EnviarFinanzas ? " y enviado a Finanzas." : " como borrador.") + (SinNumero ? " Enviado SIN número." : ""));

            await _context.SaveChangesAsync();
            await _finanzasSync.SincronizarAsync(IdOperacion, User.Identity?.Name ?? "Sistema");

            return new JsonResult(new { success = true, bloqueado = EnviarFinanzas, sinNumero = SinNumero });
        }

        public async Task<IActionResult> OnPostSolicitarDesbloqueoAsync(int idOperacion, int? idUnidadTecnica, string certKey, string comentario)
        {
            if (string.IsNullOrWhiteSpace(comentario)) return new JsonResult(new { success = false, message = "El comentario es obligatorio." });

            bool yaExiste = await _context.SolicitudesDesbloqueos.AnyAsync(s => s.IdOperacion == idOperacion && s.IdUnidadTecnica == idUnidadTecnica && s.CertKey == certKey && s.Estado == "PENDIENTE");
            if (yaExiste) return new JsonResult(new { success = false, message = "Ya existe una solicitud pendiente para este concepto." });

            var op = await _context.Operaciones.FindAsync(idOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

            _context.SolicitudesDesbloqueos.Add(new SolicitudDesbloqueo
            {
                IdOperacion = idOperacion,
                IdUnidadTecnica = idUnidadTecnica,
                CertKey = certKey,
                Comentario = comentario.Trim(),
                UsuarioSolicitante = User.Identity?.Name ?? "Operaciones",
                FechaSolicitud = DateTime.Now,
                Estado = "PENDIENTE"
            });
            RegistrarHito(op, "DOCUMENTAL", $"Solicitud de desbloqueo enviada a Finanzas para [{certKey.ToUpper()}]: {comentario.Trim()}");
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostActualizarModalidadCertAsync(int idOperacion, string certKey, string modalidad)
        {
            if (modalidad != "GRUPAL" && modalidad != "INDIVIDUAL") return new JsonResult(new { success = false, message = "Modalidad inválida." });
            var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == idOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

            string? actual = certKey switch { "ori" => op.ModalidadCertOrigen, "fit" => op.ModalidadCertFito, "san" => op.ModalidadCertSanitario, "cap" => op.ModalidadCertCaptura, _ => null };
            if (ModalidadCertBloqueada(op, certKey) && actual != modalidad)
                return new JsonResult(new { success = false, message = "No se puede cambiar: ya hay datos guardados para este certificado." });

            switch (certKey)
            {
                case "ori": op.ModalidadCertOrigen = modalidad; break;
                case "fit": op.ModalidadCertFito = modalidad; break;
                case "san": op.ModalidadCertSanitario = modalidad; break;
                case "cap": op.ModalidadCertCaptura = modalidad; break;
                default: return new JsonResult(new { success = false, message = "Certificado no reconocido." });
            }
            op.FechaModificacion = DateTime.Now;
            RegistrarHito(op, "DOCUMENTAL", $"Modalidad de {certKey.ToUpper()} cambiada a: {modalidad}");
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }
    }
}