using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using AtilsonCargoSpa.Helpers;
using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtilsonCargoSpa.Services;

namespace AtilsonCargoSpa.Pages.Operaciones
{
    public class IndexModel : PageModel, AtilsonCargoSpa.Models.IOperacionesConsultaPageModel
    {
        private readonly AtilsonContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly EmailService _emailService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly FinanzasSyncService _finanzasSync;

        public IndexModel(AtilsonContext context, IWebHostEnvironment env, EmailService emailService, IServiceScopeFactory scopeFactory, FinanzasSyncService finanzasSync)
        {
            _context = context;
            _env = env;
            _emailService = emailService;
            _scopeFactory = scopeFactory;
            _finanzasSync = finanzasSync;
        }

        public IList<Operacione> Operaciones { get; set; } = default!;
        public IList<Cotizacion> NuevasCotizaciones { get; set; } = new List<Cotizacion>();

        public class NotificacionPortalDto
        {
            public string Tipo { get; set; } = ""; // COTIZACION o ROLEO
            public DateTime Fecha { get; set; }
            public string Titulo { get; set; } = "";
            public string Subtitulo { get; set; } = "";
            public string Detalle { get; set; } = "";
            public int? IdReferencia { get; set; }
            public int? IdOperacion { get; set; }
            public string? CotTipoServicio { get; set; }
            public string? CotTipoCarga { get; set; }
            public string? CotOrigen { get; set; }
            public string? CotDestino { get; set; }
            public string? CotMercancia { get; set; }
            public string? CotComentarios { get; set; }
        }

        public List<NotificacionPortalDto> NotificacionesPendientes { get; set; } = new();

        public string? PatenteRampla { get; private set; }
        public string? RutConductor { get; private set; }

        public List<SelectListItem> ListaCadenasAlmacen { get; set; } = new();
        public List<ConductorAutoDto> ChoferesHistorialAlmacen { get; set; } = new();

        public class ConductorAutoDto
        {
            public string? Nombre { get; set; }
            public string? Rut { get; set; }
            public string? Telefono { get; set; }
            public string? Patente { get; set; }
        }

        // Reutiliza la MISMA señal que ya usa ControlFinanciero para "Pendiente
        // cierre Acuerdo" (MontoNeto==0 && !TarifaManual): si no hay tarifa
        // asignada, esto es true, y sirve tanto para pre-cargar los selectores
        // de Pagado Por/Responsable como para bloquear la pestaña en Operaciones.
        public (string? PagadoPor, string? Responsable, bool PendienteTarifa, string ConceptoReal) ObtenerEstadoConcepto(Operacione op, string conceptoBusqueda)
        {
            var tx = op.TransaccionesFinancieras?
                .FirstOrDefault(t => t.TipoMovimiento == "EGRESO" && (t.Concepto ?? "").Contains(conceptoBusqueda));
            if (tx == null) return (null, null, false, conceptoBusqueda);
            bool pendiente = tx.MontoNeto == 0m && !tx.TarifaManual;
            return (tx.ResponsablePago, tx.Responsable, pendiente, tx.Concepto);
        }

        // Para pestañas que agrupan más de un concepto financiero (ej. Aduana:
        // Honorarios Agencia + Legalización DUS): basta con que UNO de ellos esté
        // pendiente para bloquear la pestaña completa.
        public bool HayTarifaPendiente(Operacione op, params string[] conceptosBusqueda)
        {
            if (op.TransaccionesFinancieras == null) return false;
            return conceptosBusqueda.Any(cb => op.TransaccionesFinancieras.Any(t =>
                t.TipoMovimiento == "EGRESO" && (t.Concepto ?? "").Contains(cb) && t.MontoNeto == 0m && !t.TarifaManual));
        }

        // Overloads para modalidad INDIVIDUAL: exigen coincidencia EXACTA con el sufijo
        // " (Uxx)" que genera FinanzasSyncService, para no cruzar el cobro de un
        // contenedor con el de otro. No reemplazan a los de arriba (que siguen
        // usándose tal cual para Grupal / conceptos que no son por-contenedor).
        public (string? PagadoPor, string? Responsable, bool PendienteTarifa, string ConceptoReal) ObtenerEstadoConcepto(Operacione op, string conceptoBusqueda, int idUnidadTecnica)
        {
            string conceptoExacto = $"{conceptoBusqueda} (U{idUnidadTecnica})";
            var tx = op.TransaccionesFinancieras?.FirstOrDefault(t => t.TipoMovimiento == "EGRESO" && t.Concepto == conceptoExacto);
            if (tx == null) return (null, null, false, conceptoExacto);
            bool pendiente = tx.MontoNeto == 0m && !tx.TarifaManual;
            return (tx.ResponsablePago, tx.Responsable, pendiente, tx.Concepto);
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

        // Exact-match: usada para Responsable/Pagado Por POR SLOT en certificados
        // con reemisiones (Origen, Fitosanitario, Sanitario, Captura). A diferencia
        // de ObtenerEstadoConcepto (que usa .Contains y por eso "Certificado de
        // Origen" hace match también con "Certificado de Origen R1"), aquí el
        // llamador arma el string EXACTO (incluyendo "R1"/"R2"/"R3" y el sufijo
        // "(Uxx)" si aplica) para no cruzar la responsabilidad de un slot con otro.
        public (string? PagadoPor, string? Responsable, bool PendienteTarifa, string ConceptoReal) ObtenerEstadoConceptoExacto(Operacione op, string conceptoExacto)
        {
            var tx = op.TransaccionesFinancieras?.FirstOrDefault(t => t.TipoMovimiento == "EGRESO" && t.Concepto == conceptoExacto);
            if (tx == null) return (null, null, false, conceptoExacto);
            bool pendiente = tx.MontoNeto == 0m && !tx.TarifaManual;
            return (tx.ResponsablePago, tx.Responsable, pendiente, tx.Concepto);
        }

        // El switch Grupal/Individual (a nivel de todo el booking) se bloquea apenas
        // existe UN certificado guardado — sigue en uso por Index.cshtml mientras
        // esa vista no se reestructure para permitir modalidad independiente por
        // certificado (ver ModalidadCertBloqueada más abajo, reservado para esa fase).
        public bool ModalidadCertificadoBloqueada(Operacione op)
        {
            if (op.OperacionesDocumentales == null) return false;
            if (op.OperacionesDocumentales.Count > 1) return true;
            return op.OperacionesDocumentales.Any(x =>
                !string.IsNullOrWhiteSpace(x.RemisionOrigen1) || !string.IsNullOrWhiteSpace(x.CertFitosanitario) ||
                !string.IsNullOrWhiteSpace(x.CertSanitario) || !string.IsNullOrWhiteSpace(x.NumCap1) ||
                !string.IsNullOrEmpty(x.CertsBloqueados));
        }

        // Bloquea el switch de UN certificado específico: se activa apenas existe
        // más de un registro documental (señal de que ya se guardó en modo Individual)
        // o el registro principal ya tiene datos de ESE certificado en particular.
        // Los otros 3 certificados no se ven afectados.
        public bool ModalidadCertBloqueada(Operacione op, string certKey)
        {
            if (op.OperacionesDocumentales == null) return false;
            if (op.OperacionesDocumentales.Count > 1)
            {
                // Con varios registros documentales, el bloqueo depende de si ALGUNO
                // de ellos ya tiene datos de este certificado en particular.
                return op.OperacionesDocumentales.Any(x => TieneDatosCert(x, certKey));
            }
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
        public async Task OnGetAsync()
        {
            Operaciones = await _context.Operaciones
.AsNoTracking()
.AsSplitQuery()
.Where(o => o.IsDeleted == false)
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
    .OrderByDescending(o => o.Id)
    .ToListAsync();
            ViewData["IdNaviera"] = new SelectList(_context.Navieras.Where(n => n.Activo == 1), "Id", "NombreNaviera");
            ViewData["IdPuertoOrigen"] = new SelectList(_context.Puertos.Where(p => p.Activo == 1), "Id", "NombrePuerto");
            ViewData["IdTipoServicio"] = new SelectList(_context.Subparametros.Where(p => p.Parametro.Categoria == "TipoMovimiento"), "Id", "Valor");
            ViewData["PlantasList"] = await _context.Plantas.Include(p => p.Ciudad).Where(p => p.Activo).ToListAsync();
            ViewData["PuertosList"] = await _context.Puertos.Where(p => p.Activo == 1).OrderBy(p => p.NombrePuerto).ToListAsync();
            ViewData["DepositosList"] = await _context.Depositos.Where(d => d.Activo == 1).OrderBy(d => d.NombreDeposito).ToListAsync();
            ViewData["IdAgenciaAduana"] = new SelectList(await _context.AgenciasAduanas.ToListAsync(), "Id", "NombreAgencia");
            ViewData["PaisesCourier"] = new SelectList(
    await _context.Puertos.Where(p => p.Activo == 1 && p.Pais != null)
        .Select(p => p.Pais!.ToUpper()).Distinct().OrderBy(x => x).ToListAsync()
);

            ViewData["TarifasMaritimo"] = await _context.TarifasMaestras.Where(t => t.EsActiva && t.Categoria == "Marítimo").OrderBy(t => t.Concepto).ToListAsync();
            ViewData["TarifasTerrestre"] = await _context.TarifasMaestras.Where(t => t.EsActiva && t.Categoria == "Terrestre").OrderBy(t => t.Concepto).ToListAsync();
            ViewData["TarifasDocumental"] = await _context.TarifasMaestras.Where(t => t.EsActiva && t.Categoria == "Documental").OrderBy(t => t.Concepto).ToListAsync();

            NuevasCotizaciones = await _context.Cotizaciones
    .Where(c => c.Estado == "NUEVA SOLICITUD" && c.Activo == true)
    .OrderByDescending(c => c.FechaSolicitud)
    .ToListAsync();

            var solicitudesRoleoPendientes = await _context.RoleosCancelaciones
    .Include(r => r.IdOperacionNavigation)
    .Where(r => r.SolicitadoPorCliente && !r.Atendido)
    .OrderByDescending(r => r.FechaCreacion)
    .ToListAsync();

            NotificacionesPendientes = new List<NotificacionPortalDto>();
            NotificacionesPendientes.AddRange(NuevasCotizaciones.Select(c => new NotificacionPortalDto
            {
                Tipo = "COTIZACION",
                Fecha = c.FechaSolicitud ?? DateTime.Now,
                Titulo = $"Nueva cotización: {c.TipoServicio}",
                Subtitulo = $"{c.Origen} → {c.Destino}",
                Detalle = string.IsNullOrEmpty(c.Mercancia) ? "Sin mercancía especificada" : c.Mercancia,
                IdReferencia = c.Id,
                CotTipoServicio = c.TipoServicio,
                CotTipoCarga = c.TipoCarga,
                CotOrigen = c.Origen,
                CotDestino = c.Destino,
                CotMercancia = c.Mercancia,
                CotComentarios = c.Comentarios
            }));
            NotificacionesPendientes.AddRange(solicitudesRoleoPendientes.Select(r => new NotificacionPortalDto
            {
                Tipo = "ROLEO",
                Fecha = r.FechaCreacion,
                Titulo = $"{(r.Tipo == "ROLEO" ? "Roleo" : "Cancelación / No Show")} solicitado por el cliente",
                Subtitulo = $"BKG: {r.IdOperacionNavigation?.NumeroBooking ?? "S/N"}",
                Detalle = string.IsNullOrWhiteSpace(r.Comentario) ? "Sin comentario adicional." : r.Comentario,
                IdReferencia = r.Id,
                IdOperacion = r.IdOperacion
            }));
            NotificacionesPendientes = NotificacionesPendientes.OrderByDescending(n => n.Fecha).ToList();

            // Misma lista filtrada/deduplicada que usa Create para Empresa Transporte
            // (IdTipoProveedor 2 y 10 son ambos "Transportista"; se excluyen Depósitos
            // y otros tipos).
            var proveedoresTransporteRaw = await _context.Proveedores
                .Where(p => p.Activo == 1 && (p.IdTipoProveedor == 2 || p.IdTipoProveedor == 10))
                .ToListAsync();
            ViewData["ProveedoresTransporteList"] = proveedoresTransporteRaw
    .Where(p => !string.IsNullOrWhiteSpace(p.NombreProveedor))
    .GroupBy(p => p.NombreProveedor.Trim().ToUpper())
    .Select(g => g.First())
    .OrderBy(p => p.NombreProveedor)
    .ToList();

            // --- ALMACENAMIENTO: depósitos (IdTipoProveedor 12) y choferes recientes ---
            var cadenasAlmacen = await _context.Proveedores
                .Include(p => p.IdCiudadNavigation)
                .Where(p => p.IdTipoProveedor == 12 && p.Activo == 1)
                .ToListAsync();

            var ordenCiudadesAlmacen = new List<string> { "VALPARAÍSO", "SAN ANTONIO", "LIRQUÉN" };
            var gruposPorCiudadAlmacen = new Dictionary<string, SelectListGroup>();
            SelectListGroup ObtenerGrupoAlmacen(string ciudad)
            {
                if (!gruposPorCiudadAlmacen.TryGetValue(ciudad, out var grupo))
                {
                    grupo = new SelectListGroup { Name = ciudad };
                    gruposPorCiudadAlmacen[ciudad] = grupo;
                }
                return grupo;
            }

            ListaCadenasAlmacen = cadenasAlmacen
                .Select(p => new { p.Id, Nombre = p.NombreProveedor ?? "S/N", Ciudad = (p.IdCiudadNavigation?.Nombre ?? "SIN CIUDAD ASIGNADA").ToUpper() })
                .OrderBy(x => ordenCiudadesAlmacen.Contains(x.Ciudad) ? ordenCiudadesAlmacen.IndexOf(x.Ciudad) : int.MaxValue)
                .ThenBy(x => x.Ciudad)
                .ThenBy(x => x.Nombre)
                .Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Nombre, Group = ObtenerGrupoAlmacen(x.Ciudad) })
                .ToList();

            ChoferesHistorialAlmacen = await _context.Conductores
                .Where(c => c.Activo == true)
                .Select(c => new ConductorAutoDto { Nombre = c.Nombre, Rut = c.Rut, Telefono = c.Telefono, Patente = c.Patente })
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var operacion = await _context.Operaciones.FindAsync(id);
            if (operacion != null)
            {
                operacion.IsDeleted = true;
                operacion.Activo = 0;
                operacion.FechaModificacion = DateTime.Now;
                operacion.UsuarioModificador = User.Identity?.Name ?? "Sistema";

                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }
            return new JsonResult(new { success = false, message = "No se encontró la operación." });
        }

        public async Task<IActionResult> OnPostQuickEditGeneralesAsync(int IdOperacion, int? IdTipoServicio, string? PaisOrigen, string? Commodity, string? TipoContenedor, double? Temperatura, double? Ventilacion, double? Humedad, string? NumeroInstructivo)
        {
            var operacion = await _context.Operaciones.FindAsync(IdOperacion);
            if (operacion != null)
            {
                if (IdTipoServicio.HasValue) operacion.IdTipoServicio = IdTipoServicio.Value;
                if (!string.IsNullOrWhiteSpace(Commodity)) operacion.Commodity = Commodity;
                if (!string.IsNullOrWhiteSpace(PaisOrigen)) operacion.PaisOrigen = PaisOrigen;
                if (!string.IsNullOrWhiteSpace(TipoContenedor)) operacion.TipoContenedor = TipoContenedor;

                if (Request.Form.ContainsKey("NumeroInstructivo")) operacion.NumeroInstructivo = NumeroInstructivo;

                operacion.Temperatura = Temperatura;
                operacion.Ventilacion = Ventilacion;
                operacion.Humedad = Humedad;

                operacion.FechaModificacion = DateTime.Now;
                operacion.UsuarioModificador = User.Identity?.Name ?? "Sistema";

                RegistrarHito(operacion, "GENERAL", $"Datos base modificados — Servicio: {IdTipoServicio} | Commodity: {Commodity ?? "-"}");
                operacion.CorreoClienteEnviado = false;

                await _context.SaveChangesAsync();
                await SincronizarTodaOperacionAFinanzasAsync(IdOperacion);

                TempData["SuccessMsg"] = "Información base actualizada.";
            }
            return RedirectToPage();
        }
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
                return RedirectToPage();
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
            await SincronizarTodaOperacionAFinanzasAsync(IdOperacion);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
            TempData["SuccessMsg"] = "Información marítima actualizada.";
            return RedirectToPage();
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
                .Include(o => o.Finanzasoperacions)
                .Include(o => o.ExtracostosOperacions)
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
                await SincronizarTodaOperacionAFinanzasAsync(IdOperacion);

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Datos de transporte actualizados exitosamente.";
            }
            else if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new JsonResult(new { success = false, message = "Operación no encontrada" });
            }

            return RedirectToPage();
        }

        // Regla de negocio: ventana de días antes del zarpe, umbral especial para
        // navieras marcadas como "siempre aplica" (Hapag-Lloyd), y para el resto,
        // solo penaliza si el booking es Spot (los de Contrato quedan exentos
        // dentro del mismo plazo).
        private (bool sugiere, int? dias) CalcularPenalidadSugerida(Naviera? naviera, DateTime? etd, string? tipoTarifa)
        {
            if (naviera == null || !etd.HasValue) return (false, null);

            int dias = (int)Math.Ceiling((etd.Value - DateTime.Now).TotalDays);
            int umbral = naviera.DiasPenalidadRoleo;

            if (dias > umbral) return (false, dias); // fuera de ventana, no aplica

            if (naviera.PenalidadRoleoSiempreAplica) return (true, dias); // Hapag: a todo evento

            // Resto de navieras: solo Spot penaliza dentro de la ventana
            bool esSpot = (tipoTarifa ?? "").ToUpper() == "SPOT";
            return (esSpot, dias);
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
                    // Si el usuario no seleccionó país manualmente, se infiere del puerto
                    // ya marcado en la operación: exportación (1-7) usa puerto destino,
                    // importación (8-14) usa puerto origen — mismo rango que ya usa este
                    // archivo más abajo para decidir PuertoEntrega.
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

                // Full Set y DHL: evidencia documental del envío físico, ahora se
                // gestiona desde esta misma pestaña porque va de la mano con el Courier.
                if (Request.Form.ContainsKey("docUpdates.NumFullSet"))
                    docDb.NumFullSet = Request.Form["docUpdates.NumFullSet"].FirstOrDefault();
                if (Request.Form.ContainsKey("docUpdates.TrackingDhl"))
                    docDb.TrackingDhl = Request.Form["docUpdates.TrackingDhl"].FirstOrDefault();
                if (Request.Form.ContainsKey("docUpdates.LogFullSet"))
                    docDb.LogFullSet = Request.Form["docUpdates.LogFullSet"].FirstOrDefault();
                if (Request.Form.ContainsKey("docUpdates.LogDhl"))
                    docDb.LogDhl = Request.Form["docUpdates.LogDhl"].FirstOrDefault();

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
            await SincronizarTodaOperacionAFinanzasAsync(IdOperacion);
            TempData["SuccessMsg"] = "Courier registrado correctamente.";
            return RedirectToPage();
        }
        public async Task<IActionResult> OnPostQuickEditDocumentalAsync(int IdOperacion, int? IdUnidadTecnica, /* ... mantén los mismos IFormFile que ya tienes ... */
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

                // BÚSQUEDA ESPECÍFICA POR CONTENEDOR (Si viene nulo, asume que es el global)
                var docDb = op.OperacionesDocumentales.FirstOrDefault(x => x.IdUnidadTecnica == IdUnidadTecnica);
                if (docDb == null)
                {
                    docDb = new OperacionesDocumentale
                    {
                        IdUnidadTecnica = IdUnidadTecnica, // Asignamos la unidad técnica
                        FechaCreacion = DateTime.Now,
                        UsuarioCreador = User.Identity?.Name ?? "Sistema",
                        Activo = true
                    };
                    op.OperacionesDocumentales.Add(docDb);
                }

                string? F(string key) { var val = Request.Form[key].FirstOrDefault(); return string.IsNullOrWhiteSpace(val) ? null : val.Trim(); }
                bool FBoolPreserve(string key, bool valorActual) => Request.Form.ContainsKey(key) ? Request.Form[key].Any(v => v == "true" || v == "on") : valorActual;
                int? FInt(string key) { var val = Request.Form[key].FirstOrDefault(); return int.TryParse(val, out var r) ? r : null; }
                int? ParseDinero(string key) { var val = Request.Form[key].FirstOrDefault(); if (string.IsNullOrWhiteSpace(val)) return null; var clean = new string(val.Where(c => char.IsDigit(c) || c == '-').ToArray()); if (string.IsNullOrWhiteSpace(clean)) return null; return int.TryParse(clean, out var r) ? r : null; }

                docDb.AplicaSag = Request.Form["docUpdates.AplicaSag"].Contains("true");
                docDb.AplicaSernapesca = Request.Form["docUpdates.AplicaSernapesca"].Contains("true");

                // Detecta si la modalidad SAG cambió en este guardado. El log de
                // auditoría se agrega más abajo (después de sincronizar el log que
                // llega del cliente), porque ese campo se sobrescribe completo unas
                // líneas después y perdería este registro si se escribiera aquí.
                string? sagModalidadAnterior = docDb.SagModalidad;
                string? sagModalidadNueva = Request.Form.ContainsKey("docUpdates.SagModalidad") ? F("docUpdates.SagModalidad") : sagModalidadAnterior;
                bool sagModalidadCambio = !string.IsNullOrEmpty(sagModalidadNueva) && sagModalidadNueva != sagModalidadAnterior;
                if (Request.Form.ContainsKey("docUpdates.SagModalidad")) docDb.SagModalidad = sagModalidadNueva; // "Planta" o "Puerto"

                // REGLA: ORIGEN 100% AUTOMÁTICO SI ES DOCUMENTAL, SAG O SERNAPESCA
                bool isReqDocumental = new[] { 1, 3, 4, 7, 8, 10, 11, 14 }.Contains(op.IdTipoServicio ?? 0);
                docDb.CertificadoOrigen = isReqDocumental || docDb.AplicaSag || docDb.AplicaSernapesca;

                if (Request.Form.ContainsKey("docUpdates.IdAgenciaAduana"))
                    docDb.IdAgenciaAduana = FInt("docUpdates.IdAgenciaAduana");
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

                // ... (El resto del método sigue igual con el bloque await SaveFile(...) de tu código) ...

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

                if (sagModalidadCambio)
                {
                    RegistrarLogSagModalidad(docDb, sagModalidadNueva!, User.Identity?.Name ?? "Sistema");
                }

                op.FechaModificacion = DateTime.Now;
                op.UsuarioModificador = User.Identity?.Name ?? "Sistema";
                RegistrarHito(op, "DOCUMENTAL", $"Documentación guardada — DUS: {docDb.DusDin ?? "-"} | SAG: {(docDb.AplicaSag ? "Sí" : "No")} | Sernapesca: {(docDb.AplicaSernapesca ? "Sí" : "No")}");
                op.CorreoClienteEnviado = false;

                await _context.SaveChangesAsync();
                await SincronizarTodaOperacionAFinanzasAsync(IdOperacion); // Sincroniza a finanzas

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return new JsonResult(new { success = true });

                TempData["SuccessMsg"] = "Progreso documental guardado correctamente.";
            }
            catch (Exception ex)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return new JsonResult(new { success = false, message = ex.Message });
                TempData["ErrorMsg"] = "Error al guardar: " + ex.Message;
            }
            return RedirectToPage();
        }

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

            // Al cambiar el responsable, re-sincronizamos para que el Ingreso
            // aparezca o desaparezca de Finanzas según corresponda.
            await SincronizarTodaOperacionAFinanzasAsync(idOperacion);

            return new JsonResult(new { success = true });
        }
        public async Task<IActionResult> OnPostEnviarFinanzasAsync(int id)
        {
            var operacion = await _context.Operaciones.FindAsync(id);
            if (operacion != null)
            {
                // Bloqueo de caso incompleto: si algún concepto (Ingreso o Egreso) no
                // tiene definido Responsable, no se permite cerrar el expediente hacia
                // Finanzas. Se excluyen filas NO FACTURABLE (ya resueltas por Atilson).
                var conceptosSinResponsable = await _context.TransaccionesFinancieras
                    .Where(t => t.IdOperacion == id && string.IsNullOrEmpty(t.Responsable))
                    .Select(t => t.Concepto)
                    .Distinct()
                    .ToListAsync();

                if (conceptosSinResponsable.Any())
                {
                    TempData["ErrorMsg"] = "No se puede cerrar el expediente: faltan definir Pagado Por/Responsable en: " + string.Join(", ", conceptosSinResponsable);
                    return RedirectToPage();
                }

                operacion.LockFinanzas = true;
                string notaEnvio = $"[{DateTime.Now:dd/MM/yyyy HH:mm} OPERACIONES] Documentación y extracostos cerrados y enviados a Finanzas.\n";
                operacion.Comentarios = notaEnvio + (operacion.Comentarios ?? "");
                operacion.FechaModificacion = DateTime.Now;
                operacion.UsuarioModificador = User.Identity?.Name ?? "Operaciones";
                RegistrarHito(operacion, "DOCUMENTAL", "Documentación cerrada y enviada a Finanzas. Módulo documental bloqueado.");

                await _context.SaveChangesAsync();
                await SincronizarTodaOperacionAFinanzasAsync(id);

                TempData["SuccessMsg"] = $"La operación {operacion.NumeroBooking} se ha enviado a Finanzas y su pestaña documental ha sido bloqueada.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostGuardarCertificadoAsync(
    int IdOperacion, int? IdUnidadTecnica, string CertKey, bool EnviarFinanzas, bool SinNumero,
    IFormFile? UploadCertA1, IFormFile? UploadCertA2, IFormFile? UploadCertA3, IFormFile? UploadCertA4)
        {
            var op = await _context.Operaciones
                .Include(o => o.OperacionesDocumentales)
                .FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada" });

            // BUSCAMOS POR UNIDAD TÉCNICA
            var docDb = op.OperacionesDocumentales.FirstOrDefault(x => x.IdUnidadTecnica == IdUnidadTecnica);
            if (docDb == null)
            {
                // Se crea el registro si aún no existe — esto pasa cuando el tipo de
                // servicio se cambió DESPUÉS de crear la operación (de uno sin Documental
                // a uno que sí lo requiere). Antes esto fallaba en silencio con "Sin
                // registro documental" y cualquier dato (Agencia incluida) parecía no
                // guardarse nunca, sin importar cuántas veces se reintentara.
                docDb = new OperacionesDocumentale
                {
                    IdUnidadTecnica = IdUnidadTecnica,
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = User.Identity?.Name ?? "Sistema",
                    Activo = true
                };
                op.OperacionesDocumentales.Add(docDb);
            }

            string? F(string key) { var v = Request.Form[key].FirstOrDefault(); return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }
            bool FBoolPreserve(string key, bool valorActual) { if (Request.Form.TryGetValue(key, out var vals)) return vals.Any(v => v == "true" || v == "on"); return valorActual; }
            int? FInt(string key) { var val = Request.Form[key].FirstOrDefault(); return int.TryParse(val, out var r) ? r : null; }
            int? ParseDinero(string key) { var val = Request.Form[key].FirstOrDefault(); if (string.IsNullOrWhiteSpace(val)) return null; var clean = new string(val.Where(c => char.IsDigit(c) || c == '-').ToArray()); if (string.IsNullOrWhiteSpace(clean)) return null; return int.TryParse(clean, out var r) ? r : null; }

            // Certificados con reemisiones: determina en qué slot (1=Principal,
            // 2..4=Reemisión 1..3) debe guardarse esta información, según cuáles ya
            // estén bloqueados (enviados a Finanzas). Ya no depende de un campo
            // oculto del formulario — lo calcula el servidor.
            bool esCertConReemisiones = CertKey == "ori" || CertKey == "fit" || CertKey == "san" || CertKey == "cap";
            int targetSlot = esCertConReemisiones ? DocumentalHelpers.SiguienteSlotEditable(docDb, CertKey) : 1;
            if (esCertConReemisiones && EnviarFinanzas && targetSlot > 4)
                return new JsonResult(new { success = false, message = "Ya se alcanzó el máximo de 3 reemisiones para este certificado. Contacte al equipo técnico si necesita una excepción." });

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

            if (EnviarFinanzas && !SinNumero && string.IsNullOrWhiteSpace(F(campoNumPrincipal)) && !string.IsNullOrWhiteSpace(campoNumPrincipal))
                return new JsonResult(new { success = false, message = "Falta N° de documento. Marca 'Enviar sin número' si corresponde." });

            if (Request.Form.ContainsKey("docUpdates.AplicaSag"))
                docDb.AplicaSag = FBoolPreserve("docUpdates.AplicaSag", docDb.AplicaSag);

            if (Request.Form.ContainsKey("docUpdates.AplicaSernapesca"))
                docDb.AplicaSernapesca = FBoolPreserve("docUpdates.AplicaSernapesca", docDb.AplicaSernapesca);

            bool isReqDocumental = new[] { 1, 3, 4, 7, 8, 10, 11, 14 }.Contains(op.IdTipoServicio ?? 0);
            docDb.CertificadoOrigen = isReqDocumental || docDb.AplicaSag || docDb.AplicaSernapesca;

            // Para el registro de auditoría de modalidad SAG (se completa en el case "fit").
            string? sagModalidadAnteriorGuardado = null;
            string? sagModalidadNuevaGuardado = null;

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
                    docDb.CertificadoOrigen = true; // REGLA: Si es SAG, gatilla Origen

                    sagModalidadAnteriorGuardado = docDb.SagModalidad;
                    sagModalidadNuevaGuardado = Request.Form.ContainsKey("docUpdates.SagModalidad") ? F("docUpdates.SagModalidad") : sagModalidadAnteriorGuardado;
                    if (Request.Form.ContainsKey("docUpdates.SagModalidad")) docDb.SagModalidad = sagModalidadNuevaGuardado; // "Planta" o "Puerto"
                    break;
                case "san":
                    docDb.CertSanitario = F("docUpdates.NumSan1"); docDb.NumSan2 = F("docUpdates.NumSan2"); docDb.NumSan3 = F("docUpdates.NumSan3"); docDb.NumSan4 = F("docUpdates.NumSan4");
                    docDb.ValSan1 = ParseDinero("docUpdates.ValSan1"); docDb.ValSan2 = ParseDinero("docUpdates.ValSan2"); docDb.ValSan3 = ParseDinero("docUpdates.ValSan3"); docDb.ValSan4 = ParseDinero("docUpdates.ValSan4");
                    docDb.AplicaSernapesca = true;
                    docDb.CertificadoOrigen = true; // REGLA: Si es Sernapesca, gatilla Origen
                    break;
                case "cap":
                    docDb.NumCap1 = F("docUpdates.NumCap1"); docDb.NumCap2 = F("docUpdates.NumCap2"); docDb.NumCap3 = F("docUpdates.NumCap3"); docDb.NumCap4 = F("docUpdates.NumCap4");
                    docDb.ValCap1 = ParseDinero("docUpdates.ValCap1"); docDb.ValCap2 = ParseDinero("docUpdates.ValCap2"); docDb.ValCap3 = ParseDinero("docUpdates.ValCap3"); docDb.ValCap4 = ParseDinero("docUpdates.ValCap4");
                    docDb.ObsCaptura = F("docUpdates.ObsCaptura"); docDb.AplicaSernapesca = true;
                    docDb.CertificadoOrigen = true; // REGLA: Si es Sernapesca, gatilla Origen
                    break;
                case "aduana":
                    docDb.AplicaSag = FBoolPreserve("docUpdates.AplicaSag", docDb.AplicaSag);
                    docDb.AplicaSernapesca = FBoolPreserve("docUpdates.AplicaSernapesca", docDb.AplicaSernapesca);
                    if (Request.Form.ContainsKey("docUpdates.IdAgenciaAduana"))
                        docDb.IdAgenciaAduana = FInt("docUpdates.IdAgenciaAduana");
                    docDb.DusDin = F("docUpdates.Dus"); docDb.Din = F("docUpdates.Din");
                    docDb.ValorDus = ParseDinero("docUpdates.ValorDus"); docDb.ValorDin = ParseDinero("docUpdates.ValorDin");
                    docDb.ValorAclaracion = ParseDinero("docUpdates.ValorAclaracion"); docDb.ValorCancelacion = ParseDinero("docUpdates.ValorCancelacion"); docDb.ValorRoleo = ParseDinero("docUpdates.ValorRoleo");
                    docDb.EstadoDocumental = F("docUpdates.EstadoDocumental");
                    docDb.MatrizPresentada = FBoolPreserve("docUpdates.MatrizPresentada", docDb.MatrizPresentada == true);
                    docDb.ExtensionDocumental = FBoolPreserve("docUpdates.ExtensionDocumental", docDb.ExtensionDocumental == true);
                    docDb.GuiaVisado = FBoolPreserve("docUpdates.GuiaVisado", docDb.GuiaVisado == true);
                    docDb.Roleo = FBoolPreserve("docUpdates.Roleo", docDb.Roleo == true);
                    docDb.GeneracionIvv = FBoolPreserve("docUpdates.GeneracionIvv", docDb.GeneracionIvv == true);
                    break;
                case "coa": docDb.NumCoa1 = F("docUpdates.NumCoa1"); break;
                case "dt": docDb.NumDt1 = F("docUpdates.NumDt1"); break;
                case "cod": docDb.NumCod1 = F("docUpdates.NumCod1"); break;
                case "cla": docDb.NumCla1 = F("docUpdates.NumCla1"); break;
                case "nep": docDb.NumNep1 = F("docUpdates.NumNep1"); break;
                default: return new JsonResult(new { success = false, message = "Certificado no reconocido" });
            }

            // ... (Continúa el flujo igual, con ProcesarEsActivasSlots, upload de Evidencias, etc.) ...

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

            // El slot de destino ahora lo calcula el servidor (targetSlot) en vez de
            // confiar en el campo oculto "SlotEsActiva" que mandaba el formulario —
            // así un slot ya bloqueado nunca puede recibir un archivo por error.
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

            string? logEntrante = Request.Form[$"docUpdates.Log{CapitalizarCert(CertKey)}"].FirstOrDefault();
            if (logEntrante != null) AsignarLogCert(docDb, CertKey, logEntrante);

            if (CertKey == "fit" && !string.IsNullOrEmpty(sagModalidadNuevaGuardado) && sagModalidadNuevaGuardado != sagModalidadAnteriorGuardado)
            {
                RegistrarLogSagModalidad(docDb, sagModalidadNuevaGuardado!, User.Identity?.Name ?? "Sistema");
            }

            if (esCertConReemisiones)
            {
                MarcarSinNumeroSlot(docDb, CertKey, targetSlot, SinNumero);
                if (EnviarFinanzas)
                {
                    BloquearCert(docDb, CertKey); // mantiene el badge "ENVIADO A FINANZAS" y el flujo de Solicitar Desbloqueo, que siguen operando a nivel de certificado completo
                    BloquearSlot(docDb, CertKey, targetSlot); // bloquea específicamente este slot
                }
            }
            else
            {
                MarcarSinNumero(docDb, CertKey, SinNumero);
                if (EnviarFinanzas) BloquearCert(docDb, CertKey);
            }

            op.FechaModificacion = DateTime.Now;
            op.UsuarioModificador = User.Identity?.Name ?? "Sistema";
            RegistrarHito(op, "DOCUMENTAL", $"Certificado [{CertKey.ToUpper()}] guardado" + (EnviarFinanzas ? " y enviado a Finanzas." : " como borrador.") + (SinNumero ? " Enviado SIN número." : ""));

            await _context.SaveChangesAsync();
            await SincronizarTodaOperacionAFinanzasAsync(IdOperacion);

            return new JsonResult(new { success = true, bloqueado = EnviarFinanzas, sinNumero = SinNumero });
        }

        public async Task<IActionResult> OnPostSolicitarDesbloqueoAsync(int idOperacion, int? idUnidadTecnica, string certKey, string comentario)
        {
            if (string.IsNullOrWhiteSpace(comentario))
                return new JsonResult(new { success = false, message = "El comentario es obligatorio." });

            bool yaExiste = await _context.SolicitudesDesbloqueos
                .AnyAsync(s => s.IdOperacion == idOperacion && s.IdUnidadTecnica == idUnidadTecnica && s.CertKey == certKey && s.Estado == "PENDIENTE");
            if (yaExiste)
                return new JsonResult(new { success = false, message = "Ya existe una solicitud pendiente para este concepto." });

            var op = await _context.Operaciones.FindAsync(idOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

            string usuario = User.Identity?.Name ?? "Operaciones";

            _context.SolicitudesDesbloqueos.Add(new SolicitudDesbloqueo
            {
                IdOperacion = idOperacion,
                IdUnidadTecnica = idUnidadTecnica,
                CertKey = certKey,
                Comentario = comentario.Trim(),
                UsuarioSolicitante = usuario,
                FechaSolicitud = DateTime.Now,
                Estado = "PENDIENTE"
            });

            RegistrarHito(op, "DOCUMENTAL", $"Solicitud de desbloqueo enviada a Finanzas para [{certKey.ToUpper()}]: {comentario.Trim()}");

            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }
        private static readonly Dictionary<string, string> SufijoCampoCert = new() { { "ori", "Ori" }, { "fit", "Fit" }, { "san", "San" }, { "cap", "Cap" } };
        private void ProcesarEsActivasSlots(OperacionesDocumentale doc, string certKey)
        {
            if (!SufijoCampoCert.TryGetValue(certKey, out var suf)) return;
            var set = (doc.CertsBloqueados ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            for (int slot = 1; slot <= 4; slot++) { bool EsActiva = Request.Form[$"docUpdates.EsActiva{suf}{slot}"].Any(v => v == "true" || v == "on"); if (EsActiva) set.Add($"{certKey}{slot}"); }
            doc.CertsBloqueados = string.Join(",", set);
        }
        private string CapitalizarCert(string k) => k switch { "ori" => "Origen", "fit" => "Fitosanitario", "san" => "Sanitario", "cap" => "Captura", "coa" => "Coa", "dt" => "Dt", "cod" => "Codaut", "cla" => "Clave", "nep" => "Neppex", _ => "" };

        public async Task<IActionResult> OnPostUpdateMatrizAsync(int IdOperacion, bool MatrizPresentada, IFormFile? EvidenciaMatriz, IFormFile? EvidenciaEnvioNaviera, string? NumeroBL, IFormFile? ArchivoBL)
        {
            var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op != null)
            {
                var docDb = op.OperacionesDocumentales.OrderBy(x => x.Id).FirstOrDefault();
                if (docDb == null) { docDb = new OperacionesDocumentale { FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true }; op.OperacionesDocumentales.Add(docDb); }
                docDb.MatrizPresentada = MatrizPresentada;
                if (Request.Form.ContainsKey("docUpdates.LogMatriz")) docDb.LogMatriz = Request.Form["docUpdates.LogMatriz"].FirstOrDefault();

                // Checkboxes: si el marcador llegó, se toma el estado actual de la lista
                // (incluye el caso de "todos desmarcados", que un checkbox solo no reporta).
                if (Request.Form.ContainsKey("TipoEmisionBlMarker"))
                {
                    var seleccionadosBl = Request.Form["TipoEmisionBl"].Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
                    op.TipoEmisionBl = seleccionadosBl.Any() ? string.Join(",", seleccionadosBl) : null;
                    op.TipoEmisionBlOtro = seleccionadosBl.Contains("OTRO") ? Request.Form["TipoEmisionBlOtro"].ToString()?.Trim() : null;
                    op.UbicacionOriginalesDestino = seleccionadosBl.Contains("ORIGINALES_DESTINO") ? Request.Form["UbicacionOriginalesDestino"].ToString()?.Trim() : null;
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
                await SincronizarTodaOperacionAFinanzasAsync(IdOperacion);

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Documentación de Matriz y B/L actualizada correctamente.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateLarAsync(int IdOperacion, int? IdUnidadTecnica, string? EstadoLar, DateTime? LateArrival, DateTime? ElarDate, bool ContenedorIngresado, DateTime? LlegadaPuertoReal, IFormFile? EvidenciaLar)
        {
            var op = await _context.Operaciones.Include(o => o.ExtracostosOperacions).Include(o => o.OperacionesTerrestres).Include(o => o.Unidadestecnicas).FirstOrDefaultAsync(o => o.Id == IdOperacion);
            if (op != null)
            {
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

                // El extracosto de LAR se mantiene agrupado a nivel de booking, sin
                // importar qué contenedor lo gatilló (decisión confirmada).
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
                await SincronizarTodaOperacionAFinanzasAsync(IdOperacion);

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Gestión de Late Arrival registrada. Si fue autorizado, se notificó a Finanzas.";
            }
            return RedirectToPage();
        }

        // Registro único por booking (no por contenedor, decisión confirmada) de
        // Roleo o Cancelación/No Show a nivel Marítimo. Genera automáticamente un
        // extracosto etiquetado como Marítimo, con monto en 0 para que Finanzas lo
        // valorice — mismo patrón que ya se usa para LAR.
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

            // Si la ejecutiva no fuerza un valor, se respeta la sugerencia automática
            // del sistema; si la fuerza (SI/NO), queda como decisión manual registrada.
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

            // Si ya se conoce el nuevo itinerario, se actualiza la operación de una
            // vez (mismos campos que usa el resto del sistema: Nave/EtdPol/EtaPod/
            // FechaStacking/CutOffMatriz) — así el resto de pestañas (alertas,
            // Gest. LAR, etc.) quedan sincronizadas sin trabajo adicional.
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
            await SincronizarTodaOperacionAFinanzasAsync(idOperacion);

            return new JsonResult(new
            {
                success = true,
                idRegistro = registro.Id,
                penalidadSugerida = sugerida,
                penalidadConfirmada = penalidadFinal,
                dias,
                fecha = registro.FechaCreacion.ToString("dd/MM/yyyy HH:mm")
            });
        }

        // Permite completar después el itinerario cuando se guardó "pendiente".
        public async Task<IActionResult> OnPostConfirmarItinerarioRoleoAsync(
            int idRegistro, string? nuevaNave, DateTime? nuevoEtd, DateTime? nuevoEta, DateTime? nuevoStacking, DateTime? nuevoCierreDocumental)
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
            if (op != null) await SincronizarTodaOperacionAFinanzasAsync(op.Id);

            return new JsonResult(new { success = true });
        }

        // Permite confirmar la penalidad más adelante (cuando la ejecutiva la
        // marcó "Por determinar" al momento del registro). Actualiza también el
        // extracosto ya creado, sin duplicarlo.
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

                // Ubica el extracosto que se creó junto con este registro (mismo tipo, misma fecha aprox.)
                var extracosto = op.ExtracostosOperacions
                    .Where(e => e.TipoCosto == nombreTipo)
                    .FirstOrDefault(e => Math.Abs((e.FechaCreacion - registro.FechaCreacion).TotalMinutes) < 5);

                if (extracosto != null)
                {
                    extracosto.Monto = montoTotal;
                    extracosto.Motivo = $"{textoPenalidad} — {registro.CantidadContenedoresAfectados} contenedor(es) afectado(s).{(string.IsNullOrWhiteSpace(registro.Comentario) ? "" : $" {registro.Comentario}")}";
                }

                RegistrarHito(op, "MARITIMO", $"{nombreTipo}: penalidad actualizada — {textoPenalidad}");
                await SincronizarTodaOperacionAFinanzasAsync(op.Id);
            }

            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true, penalidad = registro.PenalidadConfirmada });
        }

        public async Task<IActionResult> OnPostToggleIngresoAsync(int id, bool ingresado, int? idUnidadTecnica)
        {
            if (idUnidadTecnica.HasValue)
            {
                var unidad = await _context.Unidadestecnicas.FindAsync(idUnidadTecnica.Value);
                if (unidad == null || unidad.IdOperacion != id) return new JsonResult(new { success = false });
                unidad.ContenedorIngresado = ingresado;
                unidad.FechaModificacion = DateTime.Now;
                unidad.UsuarioModificador = User.Identity?.Name ?? "Sistema";
                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }

            var operacion = await _context.Operaciones.FindAsync(id);
            if (operacion != null) { operacion.ContenedorIngresado = ingresado; operacion.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync(); return new JsonResult(new { success = true }); }
            return new JsonResult(new { success = false });
        }

        public async Task<IActionResult> OnPostUpdateWorkflowAsync(int id, int? idUnidadTecnica, string estado, DateTime? fechaAsociada)
        {
            var operacion = await _context.Operaciones
                .Include(o => o.ExtracostosOperacions)
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.Unidadestecnicas)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (operacion == null) return new JsonResult(new { success = false });

            string contenedorLabel;
            if (idUnidadTecnica.HasValue)
            {
                var unidad = operacion.Unidadestecnicas.FirstOrDefault(u => u.Id == idUnidadTecnica.Value);
                if (unidad == null) return new JsonResult(new { success = false, message = "Contenedor no encontrado." });
                unidad.Estado = estado;
                unidad.FechaModificacion = DateTime.Now;
                unidad.UsuarioModificador = User.Identity?.Name ?? "Sistema";
                contenedorLabel = unidad.NroContenedor ?? $"U{unidad.Id}";
            }
            else
            {
                operacion.EstadoWorkflow = estado;
                contenedorLabel = operacion.NumeroContenedor ?? "Contenedor Principal";
            }

            // Los estados con fecha/hora (Arribado a Planta, Salida de Planta, En Puerto,
            // Entregado a Stacking) escriben en la MISMA fila de OperacionesTerrestre que
            // usa "Finalizar Servicio" para calcular sobreestadía — así ese cálculo queda
            // pre-cargado con lo que Operaciones ya registró aquí, por contenedor.
            if (fechaAsociada.HasValue)
            {
                var terr = operacion.OperacionesTerrestres.FirstOrDefault(x => x.IdUnidadTecnica == idUnidadTecnica);
                if (terr == null)
                {
                    terr = new OperacionesTerrestre { IdOperacion = operacion.Id, IdUnidadTecnica = idUnidadTecnica, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true };
                    operacion.OperacionesTerrestres.Add(terr);
                }
                if (estado.Contains("Arribado a Planta")) terr.LlegadaPlanta = fechaAsociada;
                else if (estado.Contains("Salida de Planta")) terr.SalidaPlanta = fechaAsociada;
                else if (estado.Contains("En Puerto")) terr.LlegadaPuerto = fechaAsociada.Value;
                else if (estado.Contains("Entregado a Stacking")) terr.SalidaPuerto = fechaAsociada;
            }

            if (estado.ToUpper().Contains("CANCELADO"))
            {
                bool existeCosto = operacion.ExtracostosOperacions.Any(e => e.TipoCosto.Contains("Cancelación") && e.Motivo.Contains(contenedorLabel));
                if (!existeCosto)
                {
                    operacion.ExtracostosOperacions.Add(new ExtracostosOperacion { TipoCosto = "Cancelación / Roleo Reserva", Motivo = $"[{contenedorLabel}] Operación cancelada desde el panel operativo. Revisar posibles multas o cobro de falso flete.", Monto = 0, Moneda = "USD", Evidencia = "Alerta generada por el sistema", FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" });
                }
            }

            // Cierre del ciclo logístico: se registra la fecha exacta de retiro del vacío
            // en destino. No se sobreescribe si ya estaba guardada (evita perder el dato
            // real si alguien vuelve a hacer clic en el mismo estado).
            if (estado == "Contenedor Retirado")
            {
                var ahoraRetiro = DateTime.Now;
                if (idUnidadTecnica.HasValue)
                {
                    var unidadRetiro = operacion.Unidadestecnicas.FirstOrDefault(u => u.Id == idUnidadTecnica.Value);
                    if (unidadRetiro != null && !unidadRetiro.FechaRetiroDestino.HasValue) unidadRetiro.FechaRetiroDestino = ahoraRetiro;
                }
                else
                {
                    if (!operacion.FechaRetiroDestino.HasValue) operacion.FechaRetiroDestino = ahoraRetiro;
                }
            }

            RegistrarHito(operacion, "GENERAL", $"Estado ({contenedorLabel}) cambiado a: {estado}" + (fechaAsociada.HasValue ? $" — Fecha evento: {fechaAsociada.Value:dd/MM/yyyy HH:mm}" : "") + (estado == "Contenedor Retirado" ? " — Ciclo logístico cerrado." : ""));
            operacion.FechaModificacion = DateTime.Now; operacion.CorreoClienteEnviado = false;

            await _context.SaveChangesAsync();
            await SincronizarTodaOperacionAFinanzasAsync(id);

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
                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "evidencias"); if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string uName = $"{id}_Costo_{DateTime.Now.Ticks}_{Path.GetFileName(file.FileName).Replace(" ", "_")}"; using (var s = new FileStream(Path.Combine(uploadsFolder, uName), FileMode.Create)) { await file.CopyToAsync(s); }
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
                var opCosto = await _context.Operaciones.FindAsync(id); if (opCosto != null) { RegistrarHito(opCosto, "GENERAL", $"Extracosto reportado — Tipo: {tipo} | Valor: {costo.Moneda} {montoForm:N0} | Gestiona: {responsableGestion} | Paga: {responsable} | Motivo: {motivo}"); }

                await _context.SaveChangesAsync();
                await SincronizarTodaOperacionAFinanzasAsync(id);

                if (isAjax) return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Incidencia financiera reportada exitosamente.";
            }
            else if (isAjax) return new JsonResult(new { success = false, message = "Faltan datos obligatorios (categoría, valor o evidencia)." });
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateResponsableCostoAsync(int idCosto, string responsable)
        {
            var costo = await _context.ExtracostosOperacions.FindAsync(idCosto);
            if (costo != null) { costo.Responsable = responsable; await _context.SaveChangesAsync(); return new JsonResult(new { success = true }); }
            return new JsonResult(new { success = false });
        }

        public async Task<IActionResult> OnPostUpdateGateAsync(int IdOperacion, string? GestionaGate, string? PagoGate, string? TipoGate)
        {
            var op = await _context.Operaciones.FindAsync(IdOperacion);
            if (op != null)
            {
                // Candado: una vez guardado, la pestaña queda bloqueada hasta que
                // Finanzas resuelva una Solicitud de Desbloqueo — mismo patrón que
                // los certificados documentales. Server-side también rechaza el
                // guardado si ya está bloqueada (el <fieldset disabled> del cliente
                // ya lo impide, esto es la segunda capa).
                if (op.GateBloqueado)
                {
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                        return new JsonResult(new { success = false, message = "Gate bloqueado: solicita el desbloqueo a Finanzas." });
                    TempData["ErrorMsg"] = "Gate bloqueado: solicita el desbloqueo a Finanzas.";
                    return RedirectToPage();
                }

                op.GestionaGate = GestionaGate; op.PagoGate = PagoGate; op.TipoGate = TipoGate; op.CorreoClienteEnviado = false;
                op.GateBloqueado = true;
                RegistrarHito(op, "GENERAL", $"Gate guardado y bloqueado — Tipo: {TipoGate ?? "-"} | Gestiona: {GestionaGate ?? "-"} | Pago: {PagoGate ?? "-"}");
                await _context.SaveChangesAsync();
                await SincronizarTodaOperacionAFinanzasAsync(IdOperacion);
                await SincronizarResponsabilidadGateAsync(IdOperacion, GestionaGate, PagoGate);

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = true });
                TempData["SuccessMsg"] = "Configuración de Gate guardada y bloqueada con éxito.";
            }
            else if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { success = false, message = "Operación no encontrada" });
            return RedirectToPage();
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

            // Un único IdProveedor en la fila: se prioriza el que venga informado
            // desde la sección Retiro (suele definirse más tarde, ya con el depósito
            // real confirmado); si no viene, se usa el de Ingreso.
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
            return RedirectToPage();
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

        public async Task<IActionResult> OnPostFinalizarServicioAsync(int id, int? idUnidadTecnica, DateTime? LlegadaPlanta, DateTime? SalidaPlanta, DateTime LlegadaPuerto, DateTime? SalidaPuerto, string? ResponsableLar, bool SorteoEscaner, IFormFile? EvidenciaEscaner, string correoCliente)
        {
            var op = await _context.Operaciones.Include(o => o.OperacionesTerrestres).Include(o => o.ExtracostosOperacions).Include(o => o.Unidadestecnicas).Include(o => o.IdClienteNavigation).FirstOrDefaultAsync(o => o.Id == id);
            if (op == null) return NotFound();

            // Contenedor sobre el que se finaliza el servicio: null = principal (Operacione),
            // con valor = esa Unidadestecnica. Tiempos planta/puerto siempre por contenedor;
            // extracostos de sobreestadía y LAR se agrupan a nivel booking (decisión confirmada).
            Unidadestecnica? unidad = idUnidadTecnica.HasValue
                ? op.Unidadestecnicas.FirstOrDefault(u => u.Id == idUnidadTecnica.Value)
                : null;
            string contenedorLabel = idUnidadTecnica.HasValue
                ? (unidad?.NroContenedor ?? $"U{idUnidadTecnica}")
                : (op.NumeroContenedor ?? "Contenedor Principal");

            // Antes esto siempre buscaba/creaba la fila del contenedor PRINCIPAL
            // (IdUnidadTecnica == null) sin importar para qué contenedor se estaba
            // finalizando el servicio — por eso los tiempos de un contenedor
            // adicional nunca quedaban donde el cálculo de sobreestadía los espera.
            var terr = op.OperacionesTerrestres.FirstOrDefault(x => x.IdUnidadTecnica == idUnidadTecnica);
            if (terr == null)
            {
                terr = new OperacionesTerrestre { IdOperacion = op.Id, IdUnidadTecnica = idUnidadTecnica, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema", Activo = true };
                op.OperacionesTerrestres.Add(terr);
            }
            if (LlegadaPlanta.HasValue) terr.LlegadaPlanta = LlegadaPlanta;
            if (SalidaPlanta.HasValue) terr.SalidaPlanta = SalidaPlanta;
            terr.LlegadaPuerto = LlegadaPuerto;
            if (SalidaPuerto.HasValue) terr.SalidaPuerto = SalidaPuerto;
            terr.SorteoEscaner = SorteoEscaner;

            double horasLibresPlanta = 7; if (double.TryParse(Request.Form["HorasLibresPlanta"], out double hp)) horasLibresPlanta = hp;
            double horasLibresPuerto = 3; if (double.TryParse(Request.Form["HorasLibresPuerto"], out double hpu)) horasLibresPuerto = hpu;

            if (LlegadaPlanta.HasValue && SalidaPlanta.HasValue) { var diff = SalidaPlanta.Value - LlegadaPlanta.Value; if (diff.TotalHours > horasLibresPlanta) { string m = $"[{contenedorLabel}] Camión en planta desde {LlegadaPlanta.Value:HH:mm} hasta {SalidaPlanta.Value:HH:mm}. Total espera: {diff.TotalHours:0.1} hrs. (Límite {horasLibresPlanta}h)"; op.ExtracostosOperacions.Add(new ExtracostosOperacion { TipoCosto = "Sobreestadía Planta", Motivo = m, Monto = 0, Moneda = "USD", Evidencia = "Generado por sistema interno", FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" }); } }
            if (SalidaPuerto.HasValue) { var diffPuerto = SalidaPuerto.Value - LlegadaPuerto; if (diffPuerto.TotalHours > horasLibresPuerto) { string m = $"[{contenedorLabel}] Camión en puerto desde {LlegadaPuerto:HH:mm} hasta {SalidaPuerto.Value:HH:mm}. Total espera: {diffPuerto.TotalHours:0.1} hrs. (Límite {horasLibresPuerto}h)"; op.ExtracostosOperacions.Add(new ExtracostosOperacion { TipoCosto = "Sobreestadía Puerto", Motivo = m, Monto = 0, Moneda = "USD", Evidencia = "Generado por sistema interno", FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" }); } }

            DateTime? cutOff = idUnidadTecnica.HasValue
                ? (unidad?.ElarDate ?? unidad?.LateArrival ?? op.CutOffMatriz)
                : (op.ElarDate ?? op.LateArrival ?? op.CutOffMatriz);
            bool huboLar = cutOff.HasValue && LlegadaPuerto > cutOff.Value;
            if (huboLar)
            {
                if (unidad != null) unidad.EstadoLar = "INGRESADO CON LAR"; else op.EstadoLar = "INGRESADO CON LAR";
                string m = $"[{contenedorLabel}] Ingreso a puerto {LlegadaPuerto:dd/MM HH:mm}, superando el Cut-Off. Criterio: {ResponsableLar}";
                var extCosto = op.ExtracostosOperacions.FirstOrDefault(e => e.TipoCosto == "Late Arrival (LAR)");
                if (extCosto == null) { op.ExtracostosOperacions.Add(new ExtracostosOperacion { TipoCosto = "Late Arrival (LAR)", Motivo = m, Monto = 0, Moneda = "USD", Evidencia = "Generado automáticamente por hora de acceso", FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" }); }
            }
            if (unidad != null) unidad.ContenedorIngresado = true; else op.ContenedorIngresado = true;

            if (SorteoEscaner) { string pathEvidencia = "Pendiente de subir"; if (EvidenciaEscaner != null && EvidenciaEscaner.Length > 0) { string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "evidencias"); if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder); string uName = $"{id}_Escaner_{DateTime.Now.Ticks}_{Path.GetFileName(EvidenciaEscaner.FileName).Replace(" ", "_")}"; using (var stream = new FileStream(Path.Combine(uploadsFolder, uName), FileMode.Create)) { await EvidenciaEscaner.CopyToAsync(stream); } pathEvidencia = $"/uploads/evidencias/{uName}"; } op.ExtracostosOperacions.Add(new ExtracostosOperacion { TipoCosto = "Escáner de Aduana", Motivo = $"[{contenedorLabel}] Unidad seleccionada aleatoriamente para aforo.", Monto = 0, Moneda = "USD", Evidencia = pathEvidencia, FechaCreacion = DateTime.Now, UsuarioCreador = User.Identity?.Name ?? "Sistema" }); }
            RegistrarHito(op, "GENERAL", $"Servicio finalizado ({contenedorLabel}) — Llegada Puerto: {LlegadaPuerto:dd/MM HH:mm} | LAR: {(huboLar ? "Sí" : "No")} | Escáner: {(SorteoEscaner ? "Sí" : "No")}");
            op.EstadoWorkflow = "Entregado a Stacking"; op.FechaModificacion = DateTime.Now; op.CorreoClienteEnviado = false;

            await _context.SaveChangesAsync();
            await SincronizarTodaOperacionAFinanzasAsync(id);

            TempData["SuccessMsg"] = $"Se ha procesado el cierre físico del Booking <strong>{op.NumeroBooking}</strong> — Contenedor <strong>{contenedorLabel}</strong>.";
            return RedirectToPage("./Index");
        }

        public TimeSpan GetTravelTime(string? planta, string? puerto)
        {
            planta = (planta ?? "").ToUpper(); puerto = (puerto ?? "").ToUpper();
            if ((planta.Contains("BUIN") || planta.Contains("PAINE")) && puerto.Contains("VALPARAISO")) return TimeSpan.FromHours(3);
            if ((planta.Contains("MELIPILLA") || planta.Contains("CALERA")) && puerto.Contains("VALPARAISO")) return new TimeSpan(2, 45, 0);
            if (planta.Contains("SAN FELIPE") && puerto.Contains("VALPARAISO")) return new TimeSpan(2, 30, 0);
            if (planta.Contains("SAN FELIPE") && puerto.Contains("SAN ANTONIO")) return new TimeSpan(3, 45, 0);
            if ((planta.Contains("BUIN") || planta.Contains("PAINE")) && puerto.Contains("SAN ANTONIO")) return new TimeSpan(2, 25, 0);
            if (planta.Contains("ROMERAL") && puerto.Contains("SAN ANTONIO")) return new TimeSpan(4, 15, 0);
            if (planta.Contains("ROMERAL") && puerto.Contains("VALPARAISO")) return TimeSpan.FromHours(4);
            return TimeSpan.FromHours(2);
        }

        // ===== Cierre por retiro de contenedor en destino =====
        // Cerrado = TODOS los contenedores del booking (principal + unidades técnicas)
        // tienen su FechaRetiroDestino registrada. Con un solo contenedor sin retirar,
        // el booking sigue activo.
        public bool OperacionCerradaPorRetiro(Operacione op)
        {
            if (op.Unidadestecnicas != null && op.Unidadestecnicas.Any())
                return op.FechaRetiroDestino.HasValue && op.Unidadestecnicas.All(u => u.FechaRetiroDestino.HasValue);
            return op.FechaRetiroDestino.HasValue;
        }

        public bool PendienteGestionRetiro(Operacione op)
        {
            if (OperacionCerradaPorRetiro(op)) return false;
            bool mainPendiente = op.EstadoWorkflow == "Arribado";
            bool unidadPendiente = op.Unidadestecnicas != null && op.Unidadestecnicas.Any(u => u.Estado == "Arribado");
            return mainPendiente || unidadPendiente;
        }

        public void GenerarAlertasProactivas(Operacione op, OperacionesTerrestre? t, OperacionesDocumentale? d, bool reqMar, bool reqDoc, bool reqMatriz, bool reqTerrestre, out string alertMatriz, out string alertStacking, out string alertTransporte, out string alertTransbordo, out string alertArribo)
        {
            alertMatriz = ""; alertStacking = ""; alertTransporte = ""; alertTransbordo = ""; alertArribo = ""; DateTime now = DateTime.Now;
            if (reqMatriz && (d == null || d.MatrizPresentada != true) && op.CutOffMatriz.HasValue) { TimeSpan diff = op.CutOffMatriz.Value - now; if (diff.TotalHours <= 24 && diff.TotalHours > 12) alertMatriz = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-info text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block text-info'><i class='bi bi-info-circle me-1'></i> MATRIZ DOCUMENTAL</strong> Vence en {Math.Round(diff.TotalHours)} hrs ({op.CutOffMatriz.Value:dd/MM HH:mm}).</div>"; else if (diff.TotalHours <= 12 && diff.TotalHours > 3) alertMatriz = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-warning text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block' style='color:#d97706;'><i class='bi bi-exclamation-triangle me-1'></i> MATRIZ DOCUMENTAL</strong> Vence en {Math.Round(diff.TotalHours)} hrs. Urge confirmación.</div>"; else if (diff.TotalHours <= 3 && diff.TotalHours > 0) alertMatriz = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-danger text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block text-danger'><i class='bi bi-x-octagon me-1'></i> MATRIZ CRÍTICA</strong> Vence en {Math.Round(diff.TotalHours)} hrs. Riesgo de multa.</div>"; else if (diff.TotalHours <= 0) alertMatriz = $"<div class='p-2 mb-1 rounded bg-danger bg-opacity-10 border-start border-3 border-danger text-danger shadow-sm' style='font-size: 11px;'><strong class='d-block'><i class='bi bi-x-octagon-fill me-1'></i> MATRIZ VENCIDA</strong> ¡Corte finalizado! Gestión urgente.</div>"; }
            if (reqMar) { DateTime? deadline = op.LateArrival ?? op.CutOffMatriz; if (deadline.HasValue) { TimeSpan travelTime = GetTravelTime(t?.PlantaCarga, op.IdPuertoOrigenNavigation?.NombrePuerto); DateTime maxDeparture = deadline.Value.Subtract(travelTime); TimeSpan timeToDeparture = maxDeparture - now; if (timeToDeparture.TotalHours <= 3) { if (timeToDeparture.TotalHours <= 0) alertStacking = $"<div class='p-2 mb-1 rounded bg-danger bg-opacity-10 border-start border-3 border-danger text-danger shadow-sm alert-stacking' style='font-size: 11px;'><strong class='d-block'><i class='bi bi-truck me-1'></i> ATRASO A PUERTO</strong> Camión debió salir a las {maxDeparture:HH:mm}. <br/><span class='text-muted text-danger'>Riesgo de pérdida de nave.</span></div>"; else alertStacking = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-warning text-dark shadow-sm alert-stacking' style='font-size: 11px;'><strong class='d-block' style='color:#d97706;'><i class='bi bi-cone-striped me-1'></i> TRÁNSITO A PUERTO</strong> Salida límite: {maxDeparture:HH:mm}.</div>"; } } }
            if (reqTerrestre && op.FechaStacking.HasValue) { TimeSpan diffTrans = op.FechaStacking.Value - now; bool transportAsignado = !string.IsNullOrWhiteSpace(t?.EmpresaTransporte); bool asignacionNotificada = t?.AsignacionEnviada == true; bool solicitudEnviada = t?.SolicitudEnviada == true; if (transportAsignado || asignacionNotificada) { alertTransporte = ""; } else if (solicitudEnviada) { alertTransporte = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-primary text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block text-primary'><i class='bi bi-envelope-check me-1'></i> LOGÍSTICA EN PROCESO</strong> Solicitud enviada, esperando chofer.</div>"; } else { if (diffTrans.TotalDays <= 5 && diffTrans.TotalDays > 3) alertTransporte = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-info text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block text-info'><i class='bi bi-truck me-1'></i> LOGÍSTICA</strong> Faltan {diffTrans.TotalDays:0} días para Stacking. <br/><span class='text-muted'>Sugerencia: Solicitar transporte.</span></div>"; else if (diffTrans.TotalDays <= 3 && diffTrans.TotalDays > 2) alertTransporte = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-warning text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block' style='color:#d97706;'><i class='bi bi-truck me-1'></i> ALERTA LOGÍSTICA</strong> Faltan {diffTrans.TotalDays:0} días para Stacking. <br/><span class='text-muted'>Debe confirmar camión.</span></div>"; else if (diffTrans.TotalDays <= 2 && diffTrans.TotalDays > 1) alertTransporte = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-danger text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block text-danger'><i class='bi bi-truck me-1'></i> LOGÍSTICA CRÍTICA</strong> Menos de 48 hrs y NO hay transporte.</div>"; else if (diffTrans.TotalDays <= 1 && diffTrans.TotalDays >= 0) alertTransporte = $"<div class='p-2 mb-1 rounded bg-danger bg-opacity-10 border-start border-3 border-danger text-danger shadow-sm' style='font-size: 11px;'><strong class='d-block'><i class='bi bi-truck me-1'></i> PELIGRO LOGÍSTICO</strong> Stacking abre mañana. <br/><span class='text-muted text-danger'>¡Asignación URGENTE!</span></div>"; else if (diffTrans.TotalDays < 0) alertTransporte = $"<div class='p-2 mb-1 rounded bg-danger text-white border-start border-3 border-dark shadow-sm' style='font-size: 11px;'><strong class='d-block'><i class='bi bi-exclamation-octagon-fill me-1'></i> LOGÍSTICA VENCIDA</strong> STACKING ABIERTO SIN CAMIÓN.</div>"; } }
            if (op.EtdPol.HasValue && (d == null || d.DhlEnviadoCliente != true)) { TimeSpan diffPostZarpe = now - op.EtdPol.Value; bool aplicaSernapesca = (d != null && d.AplicaSernapesca == true); if (aplicaSernapesca && diffPostZarpe.TotalDays >= 3) { alertMatriz += $"<div class='p-2 mb-1 mt-1 rounded bg-light border-start border-3 border-warning text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block' style='color:#d97706;'><i class='bi bi-exclamation-triangle me-1'></i> ALERTA DHL</strong> Han pasado {diffPostZarpe.Days} días post-zarpe.<span class='text-muted d-block'>Urge gestión con AGA.</span></div>"; } else if (!aplicaSernapesca && diffPostZarpe.TotalDays >= 5) { alertMatriz += $"<div class='p-2 mb-1 mt-1 rounded bg-light border-start border-3 border-warning text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block' style='color:#d97706;'><i class='bi bi-exclamation-triangle me-1'></i> ALERTA DHL</strong> Han pasado {diffPostZarpe.Days} días post-zarpe.<span class='text-muted d-block'>Enviar Full Set a cliente.</span></div>"; } }

            if (reqMar && !string.IsNullOrWhiteSpace(op.Transbordo))
            {
                var trans = op.GestionTransbordo;
                string estadoTr = AtilsonCargoSpa.Helpers.TransbordoHelpers.EstadoTransbordo(trans);
                var limiteTr = AtilsonCargoSpa.Helpers.TransbordoHelpers.FechaLimite(trans);
                if (estadoTr == "CRITICO" && limiteTr.HasValue)
                {
                    int diasRest = Math.Max(0, (int)Math.Ceiling((limiteTr.Value - now).TotalDays));
                    alertTransbordo = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-warning text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block' style='color:#d97706;'><i class='bi bi-arrow-left-right me-1'></i> TRANSBORDO</strong> Quedan {diasRest} día(s) para gestionar la nueva nave en {op.Transbordo}.</div>";
                }
                else if (estadoTr == "VENCIDO")
                {
                    alertTransbordo = $"<div class='p-2 mb-1 rounded bg-danger bg-opacity-10 border-start border-3 border-danger text-danger shadow-sm' style='font-size: 11px;'><strong class='d-block'><i class='bi bi-exclamation-octagon-fill me-1'></i> TRANSBORDO VENCIDO</strong> Plazo en {op.Transbordo} superado sin nueva nave confirmada.</div>";
                }
            }

            if (PendienteGestionRetiro(op))
            {
                alertArribo = $"<div class='p-2 mb-1 rounded bg-light border-start border-3 border-success text-dark shadow-sm' style='font-size: 11px;'><strong class='d-block text-success'><i class='bi bi-box-seam me-1'></i> RETIRO PENDIENTE</strong> El contenedor arribó a destino. Gestionar retiro con el cliente y marcar \"Contenedor Retirado\".</div>";
            }
        }

        public async Task<IActionResult> OnGetBuscarEmpresaAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 2) return new JsonResult(new List<object>());
            term = term.ToLower();
            var resultados = await _context.Proveedores.Where(p => p.Activo == 1 && (p.NombreProveedor.ToLower().Contains(term) || p.Rut.ToLower().Contains(term))).Take(10).Select(p => new { empresa = p.NombreProveedor, rut = p.Rut, correo = p.CorreoOperativo }).ToListAsync();
            return new JsonResult(resultados);
        }

        public async Task<IActionResult> OnGetBuscarConductorAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 2) return new JsonResult(new List<object>());
            term = term.ToLower();
            var resultados = await _context.Conductores.Where(c => c.Activo == true && (c.Nombre.ToLower().Contains(term) || c.Patente.ToLower().Contains(term) || c.Rut.ToLower().Contains(term))).Take(10).Select(c => new { conductor = c.Nombre, rut = c.Rut, telefono = c.Telefono, patente = c.Patente }).ToListAsync();
            return new JsonResult(resultados);
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


        public async Task<IActionResult> OnPostActualizarModalidadCertAsync(int idOperacion, string certKey, string modalidad)
        {
            if (modalidad != "GRUPAL" && modalidad != "INDIVIDUAL")
                return new JsonResult(new { success = false, message = "Modalidad inválida." });

            var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == idOperacion);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

            string? actual = certKey switch
            {
                "ori" => op.ModalidadCertOrigen,
                "fit" => op.ModalidadCertFito,
                "san" => op.ModalidadCertSanitario,
                "cap" => op.ModalidadCertCaptura,
                _ => null
            };

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

        private void RegistrarHito(Operacione op, string modulo, string descripcion)
        {
            string usuario = User.Identity?.Name ?? "Sistema";
            string fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            string linea = $"{fecha}|{usuario}|{modulo}|{descripcion}\n";
            op.HistorialCorreos = linea + (op.HistorialCorreos ?? "");
        }

        // Deja registro de auditoría cuando cambia la modalidad SAG (Planta/Puerto),
        // ya que esta decisión gatilla automáticamente el cobro (o no) a Atilson,
        // la Agencia de Aduanas y el Cliente en Finanzas.
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

        public async Task<IActionResult> OnPostEnviarCorreoReservaAsync(int id, string correoDestino, int nuevaVersion)
        {
            try
            {
                var op = await _context.Operaciones.Include(o => o.IdClienteNavigation).Include(o => o.IdNavieraNavigation).Include(o => o.IdPuertoOrigenNavigation).Include(o => o.IdPuertoDestinoNavigation).Include(o => o.OperacionesTerrestres).FirstOrDefaultAsync(o => o.Id == id);
                if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada" });
                var t = op.OperacionesTerrestres?.FirstOrDefault();

                string cliente = op.IdClienteNavigation?.RazonSocial ?? "Cliente"; string booking = op.NumeroBooking ?? "S/N"; string naviera = op.IdNavieraNavigation?.NombreNaviera ?? "-"; string nave = op.Nave ?? "Por confirmar"; string pol = op.IdPuertoOrigenNavigation?.NombrePuerto ?? "-"; string pod = op.IdPuertoDestinoNavigation?.NombrePuerto ?? "-"; string etd = op.EtdPol?.ToString("dd/MM/yyyy") ?? "Por confirmar"; string eta = op.EtaPod?.ToString("dd/MM/yyyy") ?? "Por confirmar"; string contenedor = op.NumeroContenedor ?? "Por asignar"; string sello = op.NumeroSello ?? "Por asignar"; string tipoConte = op.TipoContenedor ?? "-"; string cutOff = op.CutOffMatriz?.ToString("dd/MM/yyyy HH:mm") ?? "Pendiente"; string stacking = op.FechaStacking?.ToString("dd/MM/yyyy HH:mm") ?? "Pendiente"; string lar = op.LateArrival?.ToString("dd/MM/yyyy HH:mm") ?? "No activado"; string planta = t?.PlantaCarga ?? "-"; string deposito = t?.DepositoRetiro ?? "-"; string conductor = t?.NombreConductor ?? "-"; string patente = t?.Patente ?? "-"; string fechaCarga = t?.FechaCarga?.ToString("dd/MM/yyyy HH:mm") ?? "-";
                int s = op.IdTipoServicio ?? 0; string nombreServicio = s switch { 1 => "Exportación Integral", 2 => "Exportación Marítimo + Terrestre", 3 => "Exportación Marítimo + Documental", 5 => "Exportación Marítimo", 6 => "Exportación Terrestre", 8 => "Importación Integral", 12 => "Importación Marítimo", _ => "Por confirmar" };

                var req = HttpContext.Request; string baseUrl = $"{req.Scheme}://{req.Host}"; string pdfUrl = $"{baseUrl}/Operaciones/ReservaPdf/{id}";
                string versionTxt = nuevaVersion > 1 ? $" (V{nuevaVersion} — ACTUALIZACIÓN)" : ""; string asunto = $"[ATILSON CARGO] Confirmación de Reserva — BKG: {booking}{versionTxt}";
                string rowAlt = "background:#f8fafc;"; string td1 = "padding:10px 14px; border-bottom:0.5px solid #e2e8f0; font-size:13px; color:#64748b; width:200px;"; string td2 = "padding:10px 14px; border-bottom:0.5px solid #e2e8f0; font-size:13px; font-weight:700; color:#0f172a;"; string tdCrit = "padding:10px 14px; border-bottom:0.5px solid #e2e8f0; font-size:13px; font-weight:700; color:#dc2626;"; string secHead = "font-size:11px; font-weight:800; text-transform:uppercase; letter-spacing:0.8px; color:#6d2d9e; margin:24px 0 8px; padding-bottom:6px; border-bottom:2px solid #f3e8ff;";
                string alertaBanner = nuevaVersion > 1 ? $@"<div style='background:#fef3c7; border-left:4px solid #d97706; padding:10px 16px; margin-bottom:20px; border-radius:4px; font-size:13px; color:#92400e;'><strong>Actualización V{nuevaVersion}:</strong> Este documento reemplaza la confirmación anterior del Booking {booking}.</div>" : "";

                string html = $@"<div style='font-family:Arial,sans-serif; max-width:640px; margin:0 auto;'><div style='background:linear-gradient(135deg,#0f172a 0%,#6d2d9e 100%); padding:28px 32px; border-radius:8px 8px 0 0;'><p style='color:#cbd5e1; font-size:10px; text-transform:uppercase; letter-spacing:1px; margin:0 0 4px;'>Atilson Cargo SpA — Confirmación Logística</p><h1 style='color:#fff; font-size:22px; margin:0; letter-spacing:-0.3px;'>Reserva Confirmada</h1><p style='color:#a78bfa; font-size:13px; margin:6px 0 0;'>Booking: <strong style='color:#fff;'>{booking}</strong>  ·  {nombreServicio}</p></div><div style='background:#fff; padding:28px 32px; border:0.5px solid #e2e8f0; border-top:none; border-radius:0 0 8px 8px;'>{alertaBanner}<p style='font-size:15px; color:#0f172a;'>Estimado/a <strong>{cliente}</strong>,</p><p style='font-size:14px; color:#475569; line-height:1.6; margin-bottom:0;'>A continuación encontrará el detalle completo de su reserva. Para descargar el documento oficial en PDF, haga clic en el botón al final de este correo.</p><h2 style='{secHead}'>Itinerario marítimo</h2><table style='width:100%; border-collapse:collapse; border:0.5px solid #e2e8f0; border-radius:6px;'><tr><td style='{td1}'>Naviera</td><td style='{td2}'>{naviera}</td></tr><tr style='{rowAlt}'><td style='{td1}'>Nave / Viaje</td><td style='{td2}'>{nave}</td></tr><tr><td style='{td1}'>Puerto Origen (POL)</td><td style='{td2}'>{pol}</td></tr><tr style='{rowAlt}'><td style='{td1}'>Puerto Destino (POD)</td><td style='{td2}'>{pod}</td></tr><tr><td style='{td1}'>ETD — Zarpe estimado</td><td style='{td2}'>{etd}</td></tr><tr style='{rowAlt}'><td style='{td1}'>ETA — Arribo estimado</td><td style='{td2}'>{eta}</td></tr><tr><td style='{td1}'>Tipo de contenedor</td><td style='{td2}'>{tipoConte}</td></tr><tr style='{rowAlt}'><td style='{td1}'>N° Contenedor</td><td style='{td2}'>{contenedor}</td></tr><tr><td style='{td1}'>N° Sello</td><td style='{td2}'>{sello}</td></tr></table><h2 style='{secHead}'>Fechas clave</h2><table style='width:100%; border-collapse:collapse; border:0.5px solid #e2e8f0; border-radius:6px;'><tr><td style='{td1}'>Apertura Stacking (IN)</td><td style='{td2}'>{stacking}</td></tr><tr style='{rowAlt}'><td style='{td1}'>Cut-Off Físico (OUT)</td><td style='{tdCrit}'>{cutOff}</td></tr><tr><td style='{td1}'>Late Arrival (LAR)</td><td style='{td2}'>{lar}</td></tr></table><h2 style='{secHead}'>Logística terrestre</h2><table style='width:100%; border-collapse:collapse; border:0.5px solid #e2e8f0; border-radius:6px;'><tr><td style='{td1}'>Planta de carga</td><td style='{td2}'>{planta}</td></tr><tr style='{rowAlt}'><td style='{td1}'>Depósito retiro vacío</td><td style='{td2}'>{deposito}</td></tr><tr><td style='{td1}'>Fecha presentación planta</td><td style='{td2}'>{fechaCarga}</td></tr><tr style='{rowAlt}'><td style='{td1}'>Conductor asignado</td><td style='{td2}'>{conductor}</td></tr><tr><td style='{td1}'>Patente tracto</td><td style='{td2}'>{patente}</td></tr></table><div style='text-align:center; margin:32px 0 8px;'><a href='{pdfUrl}' target='_blank' style='background:#0f172a; color:#fff; padding:14px 32px; text-decoration:none; border-radius:6px; font-weight:800; font-size:14px; display:inline-block; letter-spacing:0.3px;'>Descargar documento PDF</a></div><p style='font-size:11px; color:#94a3b8; text-align:center; margin:6px 0 0;'>El link abrirá el documento directamente en su navegador.</p><hr style='border:0; border-top:0.5px solid #e2e8f0; margin:28px 0;'><p style='font-size:11px; color:#94a3b8; text-align:center; margin:0;'><strong>Atilson Cargo SpA</strong> · Operaciones Logísticas<br>Ante cualquier consulta responda este correo electrónico.</p></div></div>";

                await _emailService.EnviarCorreoAsync(correoDestino, asunto, html);
                op.CorreoClienteEnviado = true; op.VersionCorreo = nuevaVersion; string nota = $"[{DateTime.Now:dd/MM/yyyy HH:mm} OPERACIONES] Confirmación V{nuevaVersion} enviada a {correoDestino}.\n"; op.Comentarios = nota + (op.Comentarios ?? ""); RegistrarHito(op, "GENERAL", $"Correo reserva V{nuevaVersion} enviado a: {correoDestino}");
                await _context.SaveChangesAsync(); return new JsonResult(new { success = true });
            }
            catch (Exception ex) { return new JsonResult(new { success = false, message = ex.Message }); }
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
                string urlAprobar = $"{baseUrl}/Operaciones/Index?handler=RespuestaBorradorBL&id={idOperacion}&respuesta=aprobar"; string urlRechazar = $"{baseUrl}/Operaciones/Index?handler=RespuestaBorradorBL&id={idOperacion}&respuesta=rechazar";
                string html = $@"<div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 8px;'><div style='background: linear-gradient(135deg, #0f172a 0%, #6d2d9e 100%); padding: 25px; text-align: center; border-radius: 8px 8px 0 0;'><h2 style='color: #fff; margin: 0;'>Revisión de Borrador de B/L</h2><p style='color:#cbd5e1; margin-top:5px;'>Booking: <strong>{op.NumeroBooking}</strong></p></div><div style='padding: 30px; background-color: #ffffff;'><p>Estimado cliente, junto con saludar,</p><p>A continuación enviamos borrador de BL adjunto para su revisión y aprobación.</p><p>En caso de aprobar o requerir modificaciones, haga click en los botones correspondientes a continuación (Esto actualizará automáticamente la plataforma logística):</p><div style='text-align: center; margin-top: 40px; margin-bottom: 20px;'><a href='{urlAprobar}' style='background-color: #16a34a; color: white; padding: 14px 28px; text-decoration: none; border-radius: 6px; font-weight: bold; margin-right: 10px; display: inline-block;'>APROBAR DRAFT</a><a href='{urlRechazar}' style='background-color: #dc2626; color: white; padding: 14px 28px; text-decoration: none; border-radius: 6px; font-weight: bold; display: inline-block;'>RECHAZAR / SOLICITAR CAMBIO</a></div><hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 30px 0;'><p style='font-size: 11px; color: #94a3b8; text-align: center; margin: 0;'>Atilson Cargo SpA.</p></div></div>";

                await _emailService.EnviarCorreoAsync(correoDestino, $"[ATILSON CARGO] Borrador BL para Revisión - BKG {op.NumeroBooking}", html);
                RegistrarHito(op, "DOCUMENTAL", $"Borrador de BL enviado al cliente para revisión a: {correoDestino}");
                await _context.SaveChangesAsync(); return new JsonResult(new { success = true });
            }
            catch (Exception ex) { return new JsonResult(new { success = false, message = ex.Message }); }
        }

        public async Task<IActionResult> OnGetRespuestaSanitarioAgenciaAsync(int id)
        {
            try
            {
                var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == id);
                if (op == null) return Content("Operación no encontrada.");
                var doc = op.OperacionesDocumentales.FirstOrDefault();
                string logEntry = $"<div style='border-left:2px solid #0ea5e9; padding:6px; margin-bottom:6px; background:#f0f9ff; border-top:1px solid #bae6fd; border-right:1px solid #bae6fd; border-bottom:1px solid #bae6fd;'><div style='display:flex; justify-content:space-between; align-items:center; margin-bottom:4px;'><span style='font-weight:800; color:#0284c7; text-transform:uppercase; font-size:10px;'>📋 AGA-SERN CONFIRMÓ RECEPCIÓN</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Agencia Sernapesca</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>La agencia confirmó la recepción del Certificado Sanitario para su trámite. El cierre del <strong>Certificado Final</strong> lo realiza Operaciones al adjuntar el documento definitivo.</div></div>\n";
                if (doc != null) doc.LogSanitario = logEntry + (doc.LogSanitario ?? "");
                RegistrarHito(op, "DOCUMENTAL", "Agencia Sernapesca confirmó recepción del Certificado Sanitario."); op.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync();
                string htmlResponse = $@"<!DOCTYPE html><html lang='es'><head><meta charset='UTF-8'><title>Recepción Confirmada — Atilson Cargo</title><style>body{{font-family:'Segoe UI',sans-serif;background:#f1f5f9;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;}}.card{{background:#fff;padding:40px;border-radius:12px;box-shadow:0 10px 25px rgba(0,0,0,.1);text-align:center;max-width:450px;border-top:6px solid #0ea5e9;}}h1{{color:#0ea5e9;margin-top:0;}}p{{color:#475569;line-height:1.6;font-size:15px;}}</style></head><body><div class='card'><h1>📋 Recepción Confirmada</h1><p>Hemos registrado la recepción del Certificado Sanitario en el sistema de Atilson Cargo SpA.</p><p style='font-size:13px;color:#94a3b8;margin-top:30px;'>Ya puede cerrar esta pestaña.</p></div></body></html>";
                return Content(htmlResponse, "text/html");
            }
            catch (Exception ex) { return Content($"Error al procesar: {ex.Message}"); }
        }

        public async Task<IActionResult> OnGetRespuestaAgenciaAsync(int id, string estado, string tipoDoc)
        {
            try
            {
                var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == id);
                if (op == null) return Content("Operación no encontrada.");
                var doc = op.OperacionesDocumentales.FirstOrDefault();
                string textoEstado = estado == "visado" ? "Visado" : (tipoDoc == "guia" ? "En Proceso de Visación" : "Recepcionado / En Proceso");
                string color = estado == "visado" ? "#16a34a" : "#0ea5e9"; string colorBg = estado == "visado" ? "#f0fdf4" : "#f0f9ff"; string colorBorder = estado == "visado" ? "#bbf7d0" : "#bae6fd"; string emoji = estado == "visado" ? "✅" : "📋";
                string nombreCompleto = tipoDoc switch { "guia" => "Guía de Despacho", "instructivo" => "Instructivo de Embarque", "booking" => "Booking Atilson", "capturaAga" => "Certificado de Captura", "libreven" => "Certificado de Libre Venta", _ => "Documento AGA" };

                string logEntry = $"<div style='border-left:2px solid {color}; padding:6px; margin-bottom:6px; background:{colorBg}; border-top:1px solid {colorBorder}; border-right:1px solid {colorBorder}; border-bottom:1px solid {colorBorder};'><div style='display:flex; justify-content:space-between; align-items:center; margin-bottom:4px;'><span style='font-weight:800; color:{color}; text-transform:uppercase; font-size:10px;'>{emoji} {textoEstado.ToUpper()}</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Agencia de Aduanas</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>La agencia ha confirmado: <strong style='color:{color};'>{textoEstado}</strong></div></div>\n";
                if (doc != null)
                {
                    if (tipoDoc == "guia") doc.LogGuia = logEntry + (doc.LogGuia ?? "");
                    else if (tipoDoc == "instructivo") doc.LogInstructivo = logEntry + (doc.LogInstructivo ?? "");
                    else if (tipoDoc == "booking") doc.LogBookingAtilson = logEntry + (doc.LogBookingAtilson ?? "");
                    else if (tipoDoc == "capturaAga") doc.LogCapturaAga = logEntry + (doc.LogCapturaAga ?? "");
                    else if (tipoDoc == "libreven") doc.LogLibreVenta = logEntry + (doc.LogLibreVenta ?? "");
                }

                RegistrarHito(op, "DOCUMENTAL", $"Agencia confirmó estado de {nombreCompleto}: {textoEstado}");
                op.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync();

                // TEMPORIZADOR INTELIGENTE
                if (estado == "proceso" && tipoDoc == "guia")
                {
                    var req = HttpContext.Request; string baseUrl = $"{req.Scheme}://{req.Host}"; int idOperacion = op.Id; string bookingStr = op.NumeroBooking ?? "S/N";
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMinutes(30)); // 30 Minutos Reales
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                var db = scope.ServiceProvider.GetRequiredService<AtilsonContext>(); var emailSvc = scope.ServiceProvider.GetRequiredService<EmailService>();
                                var opActual = await db.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == idOperacion);
                                if (opActual != null)
                                {
                                    var docActual = opActual.OperacionesDocumentales.FirstOrDefault(); string logActual = docActual?.LogGuia?.ToUpper() ?? "";
                                    if (!logActual.Contains("GUÍA VISADA") && !logActual.Contains("GUIA VISADA") && !logActual.Contains("✅"))
                                    {
                                        string linkVisado = $"{baseUrl}/Operaciones/Index?handler=RespuestaGuiaAgencia&id={idOperacion}&estado=visado";
                                        string correoDestino = "pruebas@demo.cl"; // correo de prueba (llega a smtp4dev)
                                        string htmlRecordatorio = $@"<div style='font-family:Arial,sans-serif;max-width:580px;margin:0 auto;'><div style='background:linear-gradient(135deg,#b91c1c 0%,#7f1d1d 100%);padding:24px 28px;border-radius:8px 8px 0 0;text-align:center;'><h2 style='color:#fff;margin:0;font-size:18px;'>⏰ Recordatorio: Visación Pendiente</h2><p style='color:#fecaca;margin:6px 0 0;font-size:13px;'>Booking: <strong style='color:#fff;'>{bookingStr}</strong></p></div><div style='background:#fff;padding:28px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;'><p style='font-size:14px;color:#475569;line-height:1.6;'>Estimado equipo de Agencia,</p><p style='font-size:14px;color:#475569;line-height:1.6;'>El sistema detecta que han pasado <strong>más de 30 minutos</strong> desde que se marcó el inicio del proceso de visación para el Booking <strong style='color:#0f172a;'>{bookingStr}</strong>, y aún no se ha confirmado su término.</p><div style='background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:20px;margin:24px 0;text-align:center;'><p style='font-size:13px;font-weight:700;color:#0f172a;text-transform:uppercase;margin:0 0 14px;'>Si la guía ya fue visada, por favor confírmelo aquí:</p><a href='{linkVisado}' style='background:#16a34a;color:#fff;padding:14px 22px;text-decoration:none;border-radius:6px;font-weight:800;font-size:13px;display:inline-block;'>✅ CONFIRMAR GUÍA VISADA</a></div></div></div>";
                                        await emailSvc.EnviarCorreoAsync(correoDestino, $"[ALERTA AUTOMÁTICA] Visación Pendiente — BKG: {bookingStr}", htmlRecordatorio);
                                        string logAuto = $"<div style='border-left:2px solid #f59e0b; padding:6px; margin-bottom:6px; background:#fffbeb; border-top:1px solid #fde68a; border-right:1px solid #fde68a; border-bottom:1px solid #fde68a;'><div style='display:flex; justify-content:space-between; align-items:center; margin-bottom:4px;'><span style='font-weight:800; color:#d97706; text-transform:uppercase; font-size:10px;'>⏰ RECORDATORIO ENVIADO</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Sistema Automático</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>Alerta por demora enviada a {correoDestino}</div></div>\n";
                                        if (docActual != null) { docActual.LogGuia = logAuto + docActual.LogGuia; }
                                        await db.SaveChangesAsync();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                var db = scope.ServiceProvider.GetRequiredService<AtilsonContext>(); var opError = await db.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == idOperacion);
                                if (opError != null) { var docError = opError.OperacionesDocumentales.FirstOrDefault(); if (docError != null) { string logError = $"<div style='border-left:2px solid #dc2626; padding:6px; margin-bottom:6px; background:#fef2f2;'><div style='font-weight:800; color:#dc2626; font-size:10px;'>❌ ERROR EN TEMPORIZADOR</div><div style='font-size:10px; color:#1e293b;'>{ex.Message}</div></div>\n"; docError.LogGuia = logError + docError.LogGuia; await db.SaveChangesAsync(); } }
                            }
                        }
                    });
                }
                string htmlResponse = $@"<!DOCTYPE html><html lang='es'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>{textoEstado} — Atilson Cargo</title><style>body{{font-family:'Segoe UI',sans-serif;background:#f1f5f9;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;}}.card{{background:#fff;padding:40px;border-radius:12px;box-shadow:0 10px 25px rgba(0,0,0,.1);text-align:center;max-width:450px;border-top:6px solid {color};}}h1{{color:{color};margin-top:0;}}p{{color:#475569;line-height:1.6;font-size:15px;}}</style></head><body><div class='card'><h1>{emoji} {textoEstado}</h1><p>Hemos registrado la confirmación en el sistema de Atilson Cargo SpA.<br>Nuestros operadores han sido notificados automáticamente.</p><p style='font-size:13px;color:#94a3b8;margin-top:30px;'>Ya puede cerrar esta pestaña.</p></div></body></html>";
                return Content(htmlResponse, "text/html");
            }
            catch (Exception ex) { return Content($"Error al procesar: {ex.Message}"); }
        }

        public async Task<IActionResult> OnGetGuardarCambiosBLAsync(int id, string comentariosCliente)
        {
            try
            {
                var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == id);
                if (op != null)
                {
                    var doc = op.OperacionesDocumentales.FirstOrDefault();
                    if (doc != null) doc.EstadoDocumental = "CAMBIOS B/L";
                    RegistrarHito(op, "DOCUMENTAL", $"El cliente ha RECHAZADO el Borrador de B/L y solicita los siguientes cambios: \"{comentariosCliente}\"");
                    op.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync();
                    string htmlResponse = $@"<!DOCTYPE html><html lang='es'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>Cambios Registrados - Atilson Cargo</title><style>body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f1f5f9; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; padding: 20px; }} .card {{ background: white; padding: 40px; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.1); text-align: center; max-width: 450px; border-top: 6px solid #eab308; }} h1 {{ color: #eab308; margin-top: 0; }} p {{ color: #475569; line-height: 1.6; font-size: 15px; }}</style></head><body><div class='card'><h1>¡Correcciones Registradas!</h1><p>Hemos guardado sus comentarios exitosamente. Nuestro equipo de operaciones ha sido notificado y gestionará la corrección con la naviera a la brevedad.</p><p style='font-size: 13px; color: #94a3b8; margin-top: 30px;'>Ya puede cerrar esta pestaña.</p></div></body></html>";
                    return Content(htmlResponse, "text/html");
                }
                return Content("Operación no encontrada.");
            }
            catch { return Content("Error al procesar su solicitud."); }
        }

        public async Task<JsonResult> OnGetCalcularDistanciaLogisticaAsync(string origen, string destino)
        {
            if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino))
                return new JsonResult(new { success = false, mensaje = "Orígenes inválidos" });

            try
            {
                using var client = new HttpClient();
                // Nominatim exige un User-Agent, de lo contrario bloquea la petición
                client.DefaultRequestHeaders.Add("User-Agent", "AtilsonCargoERP/1.0");

                // 1. Obtener Coordenadas Ciudad Origen
                var coordOrigen = await ObtenerCoordenadasCiudadAsync(client, origen);
                if (coordOrigen == null) return new JsonResult(new { success = false, mensaje = $"No se ubicó {origen}" });

                // 2. Obtener Coordenadas Ciudad Destino
                var coordDestino = await ObtenerCoordenadasCiudadAsync(client, destino);
                if (coordDestino == null) return new JsonResult(new { success = false, mensaje = $"No se ubicó {destino}" });

                // 3. Calcular Distancia Terrestre Real con OSRM
                string osrmUrl = $"http://router.project-osrm.org/route/v1/driving/{coordOrigen.Value.lon},{coordOrigen.Value.lat};{coordDestino.Value.lon},{coordDestino.Value.lat}?overview=false";
                var responseOsrm = await client.GetAsync(osrmUrl);

                if (responseOsrm.IsSuccessStatusCode)
                {
                    var jsonString = await responseOsrm.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonString);
                    var routes = doc.RootElement.GetProperty("routes");

                    if (routes.GetArrayLength() > 0)
                    {
                        // La API entrega en metros, lo pasamos a Kilómetros (1 decimal)
                        var distanceMeters = routes[0].GetProperty("distance").GetDouble();
                        double distanceKm = Math.Round(distanceMeters / 1000.0, 1);

                        return new JsonResult(new { success = true, distanciaKm = distanceKm });
                    }
                }
                return new JsonResult(new { success = false, mensaje = "No se pudo trazar la ruta." });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, mensaje = ex.Message });
            }
        }

        // Método auxiliar de geolocalización
        private async Task<(string lat, string lon)?> ObtenerCoordenadasCiudadAsync(HttpClient client, string ciudad)
        {
            // Agregamos ", Chile" para asegurar que ubica la comuna correctamente
            string search = Uri.EscapeDataString(ciudad + ", Chile");
            string url = $"https://nominatim.openstreetmap.org/search?q={search}&format=json&limit=1";

            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonString);
                if (doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    return (first.GetProperty("lat").GetString()!, first.GetProperty("lon").GetString()!);
                }
            }
            return null;
        }

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
                string tituloAccion = "En caso de aprobar o requerir modificaciones, haga click en los botones correspondientes:";
                string botonesHtml = $@"<a href='{urlAprobar}' style='background-color:#16a34a;color:#fff;padding:14px 28px;text-decoration:none;border-radius:6px;font-weight:bold;margin-right:10px;display:inline-block;'>APROBAR CERTIFICADO</a> <a href='{urlRechazar}' style='background-color:#dc2626;color:#fff;padding:14px 28px;text-decoration:none;border-radius:6px;font-weight:bold;display:inline-block;'>SOLICITAR CAMBIO</a>";

                string html = $@"<div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 8px;'><div style='background: linear-gradient(135deg, #0f172a 0%, #6d2d9e 100%); padding: 25px; text-align: center; border-radius: 8px 8px 0 0;'><h2 style='color: #fff; margin: 0;'>Revisión de Certificado {nombreCert}{(version > 1 ? $" (V{version})" : "")}</h2><p style='color:#cbd5e1; margin-top:5px;'>Booking: <strong>{op.NumeroBooking}</strong></p></div><div style='padding: 30px; background-color: #ffffff;'><p>Estimado cliente, junto con saludar,</p><p>A continuación enviamos copia del Certificado {nombreCert} para su revisión.</p><div style='text-align: center; margin: 25px 0;'><a href='{urlDescarga}' style='color: #0ea5e9; font-weight: bold; text-decoration: underline;'>Ver Documento Adjunto (PDF/JPG)</a></div><p>{tituloAccion}</p><div style='text-align: center; margin-top: 30px; margin-bottom: 20px;'>{botonesHtml}</div><hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 30px 0;'><p style='font-size: 11px; color: #94a3b8; text-align: center; margin: 0;'>Atilson Cargo SpA.</p></div></div>";

                await _emailService.EnviarCorreoAsync(correoDestino, $"[ATILSON CARGO] Revisión Certificado {nombreCert}{(version > 1 ? $" (V{version})" : "")} - BKG {op.NumeroBooking}", html);

                string logEntry = $"<div style='border-left:2px solid #0ea5e9; padding:6px; margin-bottom:6px; background:#f0f9ff; border-top:1px solid #bae6fd; border-right:1px solid #bae6fd; border-bottom:1px solid #bae6fd;'><div class='d-flex justify-content-between align-items-center mb-1'><span style='font-weight:800; color:#0284c7; text-transform:uppercase; font-size:10px;'>ENVÍO A CLIENTE{(version > 1 ? $" (V{version})" : "")}</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Operaciones Atilson</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>Certificado enviado para revisión a: <strong>{correoDestino}</strong></div></div>\n";
                if (tipo == "san") doc.LogSanitario = logEntry + (doc.LogSanitario ?? ""); else if (tipo == "cap") doc.LogCaptura = logEntry + (doc.LogCaptura ?? "");

                op.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }
            catch (Exception ex) { return new JsonResult(new { success = false, message = ex.Message }); }
        }

        public async Task<IActionResult> OnGetRespuestaCertificadoClienteAsync(string id, string tipo, string respuesta)
        {
            try
            {
                int realId = int.Parse(id.Replace("sub-", "").Replace("v3-", ""));
                var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == realId);
                if (op == null) return Content("Operación no encontrada.");

                string nombreCert = tipo == "san" ? "Sanitario" : "de Captura";
                var doc = op.OperacionesDocumentales.FirstOrDefault();

                if (respuesta == "recibido")
                {
                    if (doc != null)
                    {
                        string logEntry = $"<div style='border-left:2px solid #0ea5e9; padding:6px; margin-bottom:6px; background:#f0f9ff; border-top:1px solid #bae6fd; border-right:1px solid #bae6fd; border-bottom:1px solid #bae6fd;'><div class='d-flex justify-content-between align-items-center mb-1'><span style='font-weight:800; color:#0284c7; text-transform:uppercase; font-size:10px;'>RESPUESTA CLIENTE</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Cliente</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>El cliente confirmó la <strong style='color:#0284c7;'>RECEPCIÓN</strong> del Certificado {nombreCert}.</div></div>\n";
                        if (tipo == "san") doc.LogSanitario = logEntry + (doc.LogSanitario ?? ""); else if (tipo == "cap") doc.LogCaptura = logEntry + (doc.LogCaptura ?? "");
                    }
                    op.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync();
                    return Content($@"<!DOCTYPE html><html lang='es'><head><meta charset='UTF-8'><title>Recepción Confirmada</title><style>body{{font-family:'Segoe UI',sans-serif;background:#f1f5f9;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;}}.card{{background:#fff;padding:40px;border-radius:12px;box-shadow:0 10px 25px rgba(0,0,0,.1);text-align:center;max-width:450px;border-top:6px solid #0ea5e9;}}h1{{color:#0ea5e9;margin-top:0;}}p{{color:#475569;line-height:1.6;font-size:15px;}}</style></head><body><div class='card'><h1>¡Recepción Confirmada!</h1><p>Hemos registrado la recepción del Certificado {nombreCert}.</p><p style='font-size:13px;color:#94a3b8;margin-top:30px;'>Ya puede cerrar esta pestaña.</p></div></body></html>", "text/html");
                }

                bool aprobado = respuesta == "aprobar";
                if (aprobado)
                {
                    if (doc != null)
                    {
                        string tagResultado = tipo == "san" ? "VISTO BUENO" : "APROBADO";
                        string logEntry = $"<div style='border-left:2px solid #10b981; padding:6px; margin-bottom:6px; background:#f0fdf4; border-top:1px solid #bbf7d0; border-right:1px solid #bbf7d0; border-bottom:1px solid #bbf7d0;'><div class='d-flex justify-content-between align-items-center mb-1'><span style='font-weight:800; color:#16a34a; text-transform:uppercase; font-size:10px;'>RESPUESTA CLIENTE</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Cliente</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>El cliente ha dado <strong style='color:#16a34a;'>{tagResultado}</strong> al Certificado {nombreCert}.</div></div>\n";
                        if (tipo == "san") doc.LogSanitario = logEntry + (doc.LogSanitario ?? ""); else if (tipo == "cap") doc.LogCaptura = logEntry + (doc.LogCaptura ?? "");
                    }
                    op.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync();
                    return Content($@"<!DOCTYPE html><html lang='es'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>Certificado Aprobado</title><style>body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f1f5f9; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; }} .card {{ background: white; padding: 40px; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.1); text-align: center; max-width: 450px; border-top: 6px solid #16a34a; }} h1 {{ color: #16a34a; margin-top: 0; }} p {{ color: #475569; line-height: 1.6; font-size: 16px; }}</style></head><body><div class='card'><h1>¡Certificado Aprobado!</h1><p>Hemos registrado su aprobación del Certificado {nombreCert} en nuestro sistema.</p><p style='font-size: 13px; color: #94a3b8; margin-top: 30px;'>Ya puede cerrar esta pestaña.</p></div></body></html>", "text/html");
                }
                else
                {
                    var req = HttpContext.Request; string baseUrl = $"{req.Scheme}://{req.Host}"; string formAction = $"{baseUrl}/Operaciones/Index";
                    return Content($@"<!DOCTYPE html><html lang='es'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>Solicitar Cambios</title><style>body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f1f5f9; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; padding: 20px; box-sizing: border-box; }} .card {{ background: white; padding: 40px; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.1); text-align: center; max-width: 500px; width: 100%; border-top: 6px solid #dc2626; box-sizing: border-box; }} h1 {{ color: #dc2626; margin-top: 0; font-size: 22px; }} p {{ color: #475569; line-height: 1.5; font-size: 14px; margin-bottom: 25px; }} textarea {{ width: 100%; padding: 15px; border: 1px solid #cbd5e1; border-radius: 8px; font-family: inherit; font-size: 14px; margin-bottom: 20px; box-sizing: border-box; resize: vertical; min-height: 140px; outline: none; transition: border-color 0.2s; }} textarea:focus {{ border-color: #dc2626; box-shadow: 0 0 0 3px rgba(220,38,38,0.1); }} button {{ background-color: #dc2626; color: white; border: none; padding: 14px 24px; border-radius: 8px; font-weight: bold; font-size: 15px; cursor: pointer; transition: background 0.2s; width: 100%; }} button:hover {{ filter: brightness(0.9); }}</style></head><body><div class='card'><h1>Correcciones Certificado {nombreCert}</h1><p>Por favor, indique detalladamente las correcciones que necesita realizar en el documento:</p><form method='GET' action='{formAction}'><input type='hidden' name='handler' value='GuardarCambiosCertificadoCliente' /><input type='hidden' name='id' value='{id}' /><input type='hidden' name='tipo' value='{tipo}' /><textarea name='comentariosCliente' placeholder='Ej: Favor corregir el peso bruto y actualizar la dirección...' required></textarea><button type='submit'>Enviar Correcciones a Operaciones</button></form></div></body></html>", "text/html");
                }
            }
            catch { return Content("Error al procesar solicitud."); }
        }


        public async Task<IActionResult> OnPostMarcarRoleoAtendidoAsync(int idRegistro)
        {
            var registro = await _context.RoleosCancelaciones.FindAsync(idRegistro);
            if (registro == null) return new JsonResult(new { success = false, message = "Registro no encontrado." });
            registro.Atendido = true;
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        // 1. NUEVO HANDLER PARA ELIMINAR/ARCHIVAR SOLICITUDES DEL PORTAL
        public async Task<IActionResult> OnPostArchivarCotizacionAsync(int idCotizacion)
        {
            var cotizacion = await _context.Cotizaciones.FindAsync(idCotizacion);
            if (cotizacion != null)
            {
                cotizacion.Activo = false;
                cotizacion.Estado = "ARCHIVADO";
                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }
            return new JsonResult(new { success = false, message = "Cotización no encontrada." });
        }
        public async Task<IActionResult> OnGetGuardarCambiosCertificadoClienteAsync(string id, string tipo, string comentariosCliente)
        {
            try
            {
                int realId = int.Parse(id.Replace("sub-", "").Replace("v3-", ""));
                var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == realId);
                if (op != null)
                {
                    string nombreCert = tipo == "san" ? "Sanitario" : "de Captura"; var doc = op.OperacionesDocumentales.FirstOrDefault();
                    if (doc != null)
                    {
                        string logEntry = $"<div style='border-left:2px solid #dc2626; padding:6px; margin-bottom:6px; background:#fef2f2; border-top:1px solid #fecaca; border-right:1px solid #fecaca; border-bottom:1px solid #fecaca;'><div class='d-flex justify-content-between align-items-center mb-1'><span style='font-weight:800; color:#dc2626; text-transform:uppercase; font-size:10px;'>RESPUESTA CLIENTE (RECHAZO)</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Cliente</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>El cliente ha <strong style='color:#dc2626;'>RECHAZADO</strong> el Certificado {nombreCert} y solicita los siguientes cambios:<br><em>\"{comentariosCliente}\"</em></div></div>\n";
                        if (tipo == "san") doc.LogSanitario = logEntry + (doc.LogSanitario ?? ""); else if (tipo == "cap") doc.LogCaptura = logEntry + (doc.LogCaptura ?? "");
                    }
                    op.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync();
                    return Content($@"<!DOCTYPE html><html lang='es'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>Cambios Registrados</title><style>body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f1f5f9; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; padding: 20px; }} .card {{ background: white; padding: 40px; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.1); text-align: center; max-width: 450px; border-top: 6px solid #eab308; }} h1 {{ color: #eab308; margin-top: 0; }} p {{ color: #475569; line-height: 1.6; font-size: 15px; }}</style></head><body><div class='card'><h1>¡Correcciones Registradas!</h1><p>Hemos guardado sus comentarios exitosamente. Nuestro equipo ha sido notificado y gestionará la corrección a la brevedad.</p><p style='font-size: 13px; color: #94a3b8; margin-top: 30px;'>Ya puede cerrar esta pestaña.</p></div></body></html>", "text/html");
                }
                return Content("Operación no encontrada.");
            }
            catch { return Content("Error al procesar su solicitud."); }
        }

        public async Task<IActionResult> OnGetRespuestaGuiaAgenciaAsync(int id, string estado)
        {
            try
            {
                var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == id);
                if (op == null) return Content("Operación no encontrada.");
                var doc = op.OperacionesDocumentales.FirstOrDefault();
                string textoEstado = estado == "visado" ? "Guía Visada" : "En Proceso de Visación";
                string color = estado == "visado" ? "#16a34a" : "#0ea5e9"; string colorBg = estado == "visado" ? "#f0fdf4" : "#f0f9ff"; string colorBorder = estado == "visado" ? "#bbf7d0" : "#bae6fd"; string emoji = estado == "visado" ? "✅" : "📋";
                string logEntry = $"<div style='border-left:2px solid {color}; padding:6px; margin-bottom:6px; background:{colorBg}; border-top:1px solid {colorBorder}; border-right:1px solid {colorBorder}; border-bottom:1px solid {colorBorder};'><div style='display:flex; justify-content:space-between; align-items:center; margin-bottom:4px;'><span style='font-weight:800; color:{color}; text-transform:uppercase; font-size:10px;'>{emoji} {textoEstado.ToUpper()}</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Agencia de Aduanas</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>La agencia ha confirmado: <strong style='color:{color};'>{textoEstado}</strong></div></div>\n";
                if (doc != null) doc.LogGuia = logEntry + (doc.LogGuia ?? "");
                RegistrarHito(op, "DOCUMENTAL", $"Agencia confirmó estado de Guía de Despacho: {textoEstado}"); op.FechaModificacion = DateTime.Now; await _context.SaveChangesAsync();

                if (estado == "proceso")
                {
                    var req = HttpContext.Request; string baseUrl = $"{req.Scheme}://{req.Host}"; int idOperacion = op.Id; string bookingStr = op.NumeroBooking ?? "S/N";
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMinutes(1)); // Prueba a 1 minuto
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                var db = scope.ServiceProvider.GetRequiredService<AtilsonContext>(); var emailSvc = scope.ServiceProvider.GetRequiredService<EmailService>();
                                var opActual = await db.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == idOperacion);
                                if (opActual != null)
                                {
                                    var docActual = opActual.OperacionesDocumentales.FirstOrDefault(); string logActual = docActual?.LogGuia?.ToUpper() ?? "";
                                    if (!logActual.Contains("GUÍA VISADA") && !logActual.Contains("GUIA VISADA") && !logActual.Contains("✅"))
                                    {
                                        string linkVisado = $"{baseUrl}/Operaciones/Index?handler=RespuestaGuiaAgencia&id={idOperacion}&estado=visado";
                                        string correoDestino = "pruebas@demo.cl"; // correo de prueba (llega a smtp4dev)
                                        string htmlRecordatorio = $@"<div style='font-family:Arial,sans-serif;max-width:580px;margin:0 auto;'><div style='background:linear-gradient(135deg,#b91c1c 0%,#7f1d1d 100%);padding:24px 28px;border-radius:8px 8px 0 0;text-align:center;'><h2 style='color:#fff;margin:0;font-size:18px;'>⏰ Recordatorio: Visación Pendiente</h2><p style='color:#fecaca;margin:6px 0 0;font-size:13px;'>Booking: <strong style='color:#fff;'>{bookingStr}</strong></p></div><div style='background:#fff;padding:28px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;'><p style='font-size:14px;color:#475569;line-height:1.6;'>Estimado equipo de Agencia,</p><p style='font-size:14px;color:#475569;line-height:1.6;'>El sistema detecta que han pasado <strong>más de 30 minutos</strong> desde que se marcó el inicio del proceso de visación para el Booking <strong style='color:#0f172a;'>{bookingStr}</strong>, y aún no se ha confirmado su término.</p><div style='background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:20px;margin:24px 0;text-align:center;'><p style='font-size:13px;font-weight:700;color:#0f172a;text-transform:uppercase;margin:0 0 14px;'>Si la guía ya fue visada, por favor confírmelo aquí:</p><a href='{linkVisado}' style='background:#16a34a;color:#fff;padding:14px 22px;text-decoration:none;border-radius:6px;font-weight:800;font-size:13px;display:inline-block;'>✅ CONFIRMAR GUÍA VISADA</a></div></div></div>";
                                        await emailSvc.EnviarCorreoAsync(correoDestino, $"[ALERTA AUTOMÁTICA] Visación Pendiente — BKG: {bookingStr}", htmlRecordatorio);
                                        string logAuto = $"<div style='border-left:2px solid #f59e0b; padding:6px; margin-bottom:6px; background:#fffbeb; border-top:1px solid #fde68a; border-right:1px solid #fde68a; border-bottom:1px solid #fde68a;'><div style='display:flex; justify-content:space-between; align-items:center; margin-bottom:4px;'><span style='font-weight:800; color:#d97706; text-transform:uppercase; font-size:10px;'>⏰ RECORDATORIO ENVIADO</span><span style='color:#64748b; font-size:9px;'>{DateTime.Now:dd/MM/yyyy HH:mm}</span></div><div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>Sistema Automático</strong></div><div style='color:#1e293b; font-size:11px; line-height:1.2;'>Alerta por demora enviada a {correoDestino}</div></div>\n";
                                        if (docActual != null) { docActual.LogGuia = logAuto + docActual.LogGuia; }
                                        await db.SaveChangesAsync();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                var db = scope.ServiceProvider.GetRequiredService<AtilsonContext>(); var opError = await db.Operaciones.Include(o => o.OperacionesDocumentales).FirstOrDefaultAsync(o => o.Id == idOperacion);
                                if (opError != null) { var docError = opError.OperacionesDocumentales.FirstOrDefault(); if (docError != null) { string logError = $"<div style='border-left:2px solid #dc2626; padding:6px; margin-bottom:6px; background:#fef2f2;'><div style='font-weight:800; color:#dc2626; font-size:10px;'>❌ ERROR EN TEMPORIZADOR</div><div style='font-size:10px; color:#1e293b;'>{ex.Message}</div></div>\n"; docError.LogGuia = logError + docError.LogGuia; await db.SaveChangesAsync(); } }
                            }
                        }
                    });
                }
                string htmlResponse = $@"<!DOCTYPE html><html lang='es'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>{textoEstado} — Atilson Cargo</title><style>body{{font-family:'Segoe UI',sans-serif;background:#f1f5f9;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;}}.card{{background:#fff;padding:40px;border-radius:12px;box-shadow:0 10px 25px rgba(0,0,0,.1);text-align:center;max-width:450px;border-top:6px solid {color};}}h1{{color:{color};margin-top:0;}}p{{color:#475569;line-height:1.6;font-size:15px;}}</style></head><body><div class='card'><h1>{emoji} {textoEstado}</h1><p>Hemos registrado la confirmación en el sistema de Atilson Cargo SpA.<br>Nuestros operadores han sido notificados automáticamente.</p><p style='font-size:13px;color:#94a3b8;margin-top:30px;'>Ya puede cerrar esta pestaña.</p></div></body></html>";
                return Content(htmlResponse, "text/html");
            }
            catch (Exception ex) { return Content($"Error al procesar: {ex.Message}"); }
        }

        // =========================================================================================
        // AUTO-INYECCIÓN DE TRÁMITES A FINANZAS ("EFECTO ESPEJO EXACTO")
        // Delega al servicio compartido para que Create e Index usen exactamente el mismo motor.
        // =========================================================================================

        // El "Pagado por"/"Responsable" de las filas de Gate en Finanzas no se
        // selecciona a mano: se deriva directo de GestionaGate + PagoGate, que ya
        // son la fuente de verdad de quién gestiona y quién paga el Gate.
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
        private async Task SincronizarTodaOperacionAFinanzasAsync(int idOperacion)
        {
            await _finanzasSync.SincronizarAsync(idOperacion, User.Identity?.Name ?? "Sistema");
        }
        private bool CertEstaBloqueado(OperacionesDocumentale doc, string certKey)
            => (doc.CertsBloqueados ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(certKey);

        private void BloquearCert(OperacionesDocumentale doc, string certKey)
        {
            var set = (doc.CertsBloqueados ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            set.Add(certKey);
            doc.CertsBloqueados = string.Join(",", set);
        }

        private void MarcarSinNumero(OperacionesDocumentale doc, string certKey, bool EsActiva)
        {
            var set = (doc.CertsSinNumero ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            if (EsActiva) set.Add(certKey); else set.Remove(certKey);
            doc.CertsSinNumero = string.Join(",", set);
        }

        private void BloquearSlot(OperacionesDocumentale doc, string certKey, int slot)
        {
            var set = (doc.CertsBloqueados ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            set.Add($"{certKey}{slot}");
            doc.CertsBloqueados = string.Join(",", set);
        }

        private void MarcarSinNumeroSlot(OperacionesDocumentale doc, string certKey, int slot, bool EsActiva)
        {
            string clave = $"{certKey}{slot}";
            var set = (doc.CertsSinNumero ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            if (EsActiva) set.Add(clave); else set.Remove(clave);
            doc.CertsSinNumero = string.Join(",", set);
        }

        // =========================================================================================
        // MOTOR DE SINCRONIZACIÓN ALMACENAMIENTO -> FINANZAS
        // Trasladado tal cual desde Pages/Operaciones/AlmacenamientoModel.cs (página eliminada).
        // =========================================================================================
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

                // Match por depósito + tamaño + tipo de carga + Full/Vacío, mismo patrón
                // que ya usa Gate. Si no hay match exacto (falta Estado, o el depósito
                // no tiene desglose cargado como Soto/Vigovan), cae al valor único de
                // TarifaBase/TarifaConexionReefer que ya existía — nunca se queda sin
                // cobrar nada.
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
        private void AsignarLogCert(OperacionesDocumentale doc, string certKey, string? log)
        {
            switch (certKey)
            {
                case "ori": doc.LogOrigen = log; break;
                case "fit": doc.LogFitosanitario = log; break;
                case "san": doc.LogSanitario = log; break;
                case "cap": doc.LogCaptura = log; break;
                case "coa": doc.LogCoa = log; break;
                case "dt": doc.LogDt = log; break;
                case "cod": doc.LogCodaut = log; break;
                case "cla": doc.LogClave = log; break;
                case "nep": doc.LogNeppex = log; break;
            }
        }
    }
}