using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Pages.PortalCliente;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Operaciones
{
    // Bandeja de cotizaciones del Portal Cliente (oct-2026, minuta Portal Web de
    // Clientes): el ejecutivo ve lo que pidió el cliente y responde el valor por
    // tramo — Marítimo: monto; Terrestre: puerto de salida/entrega + monto;
    // Documental: monto. La respuesta queda visible en PortalCliente/MisCotizaciones
    // y, si se marca, se avisa al cliente por correo.
    // Solo muestra TipoSolicitud = COTIZACION; las solicitudes de reserva siguen
    // su flujo de siempre (Centro de Notificaciones → Create precargado).
    public class CotizacionesModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly EmailService _emailService;
        private readonly ILogger<CotizacionesModel> _logger;

        public CotizacionesModel(AtilsonContext context, EmailService emailService, ILogger<CotizacionesModel> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        public const string FiltroPendientes = "pendientes";
        public const string FiltroRespondidas = "respondidas";
        public const string FiltroArchivadas = "archivadas";
        public static readonly string[] Monedas = { "USD", "CLP" };

        [BindProperty(SupportsGet = true)]
        public string Filtro { get; set; } = FiltroPendientes;

        [BindProperty(SupportsGet = true)]
        public int? Id { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Buscar { get; set; }

        public List<Cotizacion> Lista { get; set; } = new();
        public Cotizacion? Seleccionada { get; set; }
        public int TotalPendientes { get; set; }
        public int TotalRespondidas { get; set; }
        public int TotalArchivadas { get; set; }
        public List<Puerto> PuertosChile { get; set; } = new();

        // Reservas que el cliente pidió desde la cotización seleccionada
        // ("Reservar esta salida" / "Solicitar reserva" en Mis Cotizaciones).
        public List<Cotizacion> ReservasDeSeleccionada { get; set; } = new();

        // Aviso tras un POST, como código en la URL del redirect (no TempData: en
        // el servidor la cookie de TempData no sobrevive el redirect — ver el
        // comentario equivalente en PortalCliente/SolicitarReserva).
        [BindProperty(SupportsGet = true)]
        public string? Aviso { get; set; }

        public static readonly Dictionary<string, (string Tipo, string Texto)> Avisos = new()
        {
            ["resp_ok"] = ("success", "Respuesta guardada. El cliente ya la ve en su portal."),
            ["resp_mail"] = ("success", "Respuesta guardada y se avisó al cliente por correo."),
            ["resp_sincorreo"] = ("warning", "Respuesta guardada, pero el cliente no tiene correo registrado: no se le pudo avisar."),
            ["resp_mailfallo"] = ("warning", "Respuesta guardada, pero el correo al cliente no se pudo enviar. Revisa la configuración SMTP."),
            ["err_monto"] = ("danger", "Ingresa al menos un monto para responder la cotización."),
            ["err_negativo"] = ("danger", "Los montos no pueden ser negativos."),
            ["err_puerto"] = ("danger", "Selecciona el puerto de salida/entrega del tramo terrestre."),
            ["err_puertoinv"] = ("danger", "El puerto de salida/entrega no es válido."),
            ["err_archivada"] = ("warning", "La cotización está archivada; restáurala antes de responder."),
            ["err_noexiste"] = ("danger", "No se encontró la cotización."),
            ["archivada"] = ("success", "Cotización archivada."),
            ["restaurada"] = ("success", "Cotización restaurada.")
        };

        // Los montos llegan como texto y se parsean con InvariantCulture (mismo
        // criterio que NuevoCosto.Monto en Operaciones/Index): <input type=number>
        // siempre envía "1250.50", y el binder de decimal usa la cultura del
        // servidor — en es-CL el punto es separador de miles y daría 125050.
        public class RespuestaInput
        {
            public int IdCotizacion { get; set; }
            public string? MontoMaritimo { get; set; }
            public string? MonedaMaritimo { get; set; }
            public int? IdPuertoTerrestre { get; set; }
            public string? MontoTerrestre { get; set; }
            public string? MonedaTerrestre { get; set; }
            public string? MontoDocumental { get; set; }
            public string? MonedaDocumental { get; set; }
            public string? ValidaHasta { get; set; } // "yyyy-MM-dd" del <input type=date>
            public string? ComentarioRespuesta { get; set; }
            public bool NotificarCliente { get; set; }
        }

        [BindProperty]
        public RespuestaInput Respuesta { get; set; } = new();

        private IQueryable<Cotizacion> BaseCotizaciones() => _context.Cotizaciones
            .Where(c => c.TipoSolicitud == Cotizacion.TipoCotizacion);

        public static string NombreCliente(Cotizacion c) =>
            !string.IsNullOrWhiteSpace(c.IdClienteNavigation?.NombreCliente)
                ? c.IdClienteNavigation!.NombreCliente!
                : c.IdClienteNavigation?.RazonSocial ?? "Cliente sin nombre";

        public static string NombrePuerto(Puerto? p) => PuertosHelper.Nombre(p);

        public static string ResumenRuta(Cotizacion c)
        {
            var partes = new List<string>();
            if (c.IdPuertoOrigenNavigation != null || c.IdPuertoDestinoNavigation != null)
                partes.Add($"{c.IdPuertoOrigenNavigation?.NombrePuerto ?? "?"} → {c.IdPuertoDestinoNavigation?.NombrePuerto ?? "?"}");
            // Tramo terrestre: planta (zona) ⇄ puerto. Desde oct-2026 el portal ya no pide
            // "zona de retiro / entrega": se usa el puerto del tramo (o la zona antigua si existe).
            string? puertoTer = c.IdPuertoTerrestreNavigation?.NombrePuerto ?? c.ZonaEntrega;
            string? origenTer = !string.IsNullOrWhiteSpace(c.PlantaCarga)
                ? (string.IsNullOrWhiteSpace(c.ZonaCarga) ? c.PlantaCarga : $"{c.PlantaCarga} ({c.ZonaCarga})")
                : c.ZonaCarga;
            if (!string.IsNullOrWhiteSpace(origenTer) || !string.IsNullOrWhiteSpace(puertoTer))
                partes.Add($"{origenTer ?? "?"} ⇄ {puertoTer ?? "?"}");
            if (!string.IsNullOrWhiteSpace(c.TipoCargaDocumental))
                partes.Add(c.TipoCargaDocumental!);
            return partes.Count == 0 ? "Sin detalle" : string.Join(" · ", partes);
        }

        private async Task CargarAsync()
        {
            Filtro = Filtro is FiltroRespondidas or FiltroArchivadas ? Filtro : FiltroPendientes;

            var activas = BaseCotizaciones().Where(c => c.Activo == true);
            TotalPendientes = await activas.CountAsync(c => c.Estado != Cotizacion.EstadoRespondida);
            TotalRespondidas = await activas.CountAsync(c => c.Estado == Cotizacion.EstadoRespondida);
            TotalArchivadas = await BaseCotizaciones().CountAsync(c => c.Activo != true);

            var q = BaseCotizaciones()
                .AsNoTracking()
                .Include(c => c.IdClienteNavigation)
                .Include(c => c.IdPuertoOrigenNavigation)
                .Include(c => c.IdPuertoDestinoNavigation)
                .Include(c => c.IdPuertoTerrestreNavigation)
                .AsQueryable();

            q = Filtro switch
            {
                FiltroRespondidas => q.Where(c => c.Activo == true && c.Estado == Cotizacion.EstadoRespondida),
                FiltroArchivadas => q.Where(c => c.Activo != true),
                _ => q.Where(c => c.Activo == true && c.Estado != Cotizacion.EstadoRespondida)
            };

            if (!string.IsNullOrWhiteSpace(Buscar))
            {
                var b = Buscar.Trim();
                q = q.Where(c => (c.IdClienteNavigation != null && ((c.IdClienteNavigation.NombreCliente ?? "").Contains(b) || c.IdClienteNavigation.RazonSocial.Contains(b)))
                              || c.Id.ToString() == b);
            }

            // Pendientes: la más antigua primero (la que lleva más tiempo esperando).
            Lista = Filtro == FiltroPendientes
                ? await q.OrderBy(c => c.FechaSolicitud).Take(200).ToListAsync()
                : await q.OrderByDescending(c => c.FechaRespuesta ?? c.FechaSolicitud).Take(200).ToListAsync();

            if (Id != null)
            {
                Seleccionada = Lista.FirstOrDefault(c => c.Id == Id)
                    ?? await BaseCotizaciones().AsNoTracking()
                        .Include(c => c.IdClienteNavigation)
                        .Include(c => c.IdPuertoOrigenNavigation)
                        .Include(c => c.IdPuertoDestinoNavigation)
                        .Include(c => c.IdPuertoTerrestreNavigation)
                        .FirstOrDefaultAsync(c => c.Id == Id);
            }
            Seleccionada ??= Lista.FirstOrDefault();

            if (Seleccionada != null)
            {
                ReservasDeSeleccionada = await _context.Cotizaciones.AsNoTracking()
                    .Include(c => c.IdItinerarioNavigation)
                    .Where(c => c.IdCotizacionOrigen == Seleccionada.Id)
                    .OrderByDescending(c => c.FechaSolicitud)
                    .ToListAsync();
            }

            PuertosChile = await PuertosHelper.ListarUnicosAsync(_context, soloChile: true);
        }

        public async Task OnGetAsync()
        {
            await CargarAsync();
        }

        public async Task<IActionResult> OnPostResponderAsync()
        {
            var r = Respuesta;
            var cot = await BaseCotizaciones()
                .Include(c => c.IdClienteNavigation)
                .Include(c => c.IdPuertoOrigenNavigation)
                .Include(c => c.IdPuertoDestinoNavigation)
                .FirstOrDefaultAsync(c => c.Id == r.IdCotizacion);

            if (cot == null)
                return RedirectToPage(new { filtro = Filtro, aviso = "err_noexiste" });
            if (cot.Activo != true)
                return RedirectToPage(new { filtro = FiltroArchivadas, id = cot.Id, aviso = "err_archivada" });

            bool mar = SolicitarCotizacionModel.IncluyeModulo(cot.TipoServicio, "MAR");
            bool ter = SolicitarCotizacionModel.IncluyeModulo(cot.TipoServicio, "TER");
            bool doc = SolicitarCotizacionModel.IncluyeModulo(cot.TipoServicio, "DOC");

            string? Moneda(string? m, string porDefecto) => m != null && Monedas.Contains(m) ? m : porDefecto;
            decimal? Leer(string? texto) =>
                decimal.TryParse(texto?.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;
            decimal? Monto(string? texto) => Leer(texto) is decimal v && v > 0 ? Math.Round(v, 2) : null;

            string? error = null;
            if ((mar && Leer(r.MontoMaritimo) < 0) || (ter && Leer(r.MontoTerrestre) < 0) || (doc && Leer(r.MontoDocumental) < 0))
                error = "err_negativo";

            cot.MontoMaritimo = mar ? Monto(r.MontoMaritimo) : null;
            cot.MonedaMaritimo = cot.MontoMaritimo != null ? Moneda(r.MonedaMaritimo, "USD") : null;

            cot.MontoTerrestre = ter ? Monto(r.MontoTerrestre) : null;
            cot.MonedaTerrestre = cot.MontoTerrestre != null ? Moneda(r.MonedaTerrestre, "CLP") : null;
            cot.IdPuertoTerrestre = ter ? r.IdPuertoTerrestre : null;
            if (cot.IdPuertoTerrestre != null && !await _context.Puertos.AnyAsync(p => p.Id == cot.IdPuertoTerrestre))
                error ??= "err_puertoinv";
            if (cot.MontoTerrestre != null && cot.IdPuertoTerrestre == null)
                error ??= "err_puerto";

            cot.MontoDocumental = doc ? Monto(r.MontoDocumental) : null;
            cot.MonedaDocumental = cot.MontoDocumental != null ? Moneda(r.MonedaDocumental, "USD") : null;

            if (cot.MontoMaritimo == null && cot.MontoTerrestre == null && cot.MontoDocumental == null)
                error ??= "err_monto";

            // Nada se guarda si hay error (los cambios en memoria se descartan al
            // terminar el request sin SaveChanges).
            if (error != null)
                return RedirectToPage(new { filtro = Filtro, id = cot.Id, aviso = error });

            bool esActualizacion = cot.Estado == Cotizacion.EstadoRespondida;
            cot.ValidaHasta = DateOnly.TryParseExact(r.ValidaHasta, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var vh) ? vh : null;
            cot.ComentarioRespuesta = string.IsNullOrWhiteSpace(r.ComentarioRespuesta) ? null : r.ComentarioRespuesta.Trim();
            cot.FechaRespuesta = DateTime.Now;
            cot.UsuarioRespuesta = User.Identity?.Name;
            cot.Estado = Cotizacion.EstadoRespondida;

            await _context.SaveChangesAsync();

            string aviso = "resp_ok";

            if (r.NotificarCliente)
            {
                var correo = cot.IdClienteNavigation?.Correo;
                if (string.IsNullOrWhiteSpace(correo))
                {
                    aviso = "resp_sincorreo";
                }
                else
                {
                    try
                    {
                        if (cot.IdPuertoTerrestre != null)
                            cot.IdPuertoTerrestreNavigation = await _context.Puertos.FindAsync(cot.IdPuertoTerrestre);
                        await _emailService.EnviarCorreoAsync(correo,
                            $"[ATILSON CARGO] {(esActualizacion ? "Cotización actualizada" : "Tu cotización está lista")} #{cot.Id}",
                            ArmarCorreoRespuesta(cot, esActualizacion));
                        aviso = "resp_mail";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "No se pudo enviar el correo de respuesta de la cotización {IdCotizacion}", cot.Id);
                        aviso = "resp_mailfallo";
                    }
                }
            }

            return RedirectToPage(new { filtro = FiltroRespondidas, id = cot.Id, aviso });
        }

        public async Task<IActionResult> OnPostArchivarAsync(int idCotizacion, bool restaurar)
        {
            var cot = await BaseCotizaciones().FirstOrDefaultAsync(c => c.Id == idCotizacion);
            if (cot == null)
                return RedirectToPage(new { filtro = Filtro, aviso = "err_noexiste" });

            if (restaurar)
            {
                cot.Activo = true;
                cot.Estado = cot.FechaRespuesta != null ? Cotizacion.EstadoRespondida : "NUEVA SOLICITUD";
                await _context.SaveChangesAsync();
                return RedirectToPage(new { filtro = cot.Estado == Cotizacion.EstadoRespondida ? FiltroRespondidas : FiltroPendientes, id = cot.Id, aviso = "restaurada" });
            }

            cot.Activo = false;
            cot.Estado = "ARCHIVADO";
            await _context.SaveChangesAsync();
            return RedirectToPage(new { filtro = Filtro, aviso = "archivada" });
        }

        public static string FormatearMonto(decimal? monto, string? moneda)
        {
            if (monto == null) return "No cotizado";
            var cl = CultureInfo.GetCultureInfo("es-CL");
            return moneda == "CLP" ? $"CLP {monto.Value.ToString("N0", cl)}" : $"{moneda ?? "USD"} {monto.Value.ToString("N2", cl)}";
        }

        // Mismo estilo de correo que la Confirmación de Reserva (Operaciones/Index):
        // cabecera con degradado de marca + tabla simple. Todo texto del cliente o
        // del ejecutivo pasa por HtmlEncode.
        private string ArmarCorreoRespuesta(Cotizacion c, bool esActualizacion)
        {
            string E(string? s) => WebUtility.HtmlEncode(s ?? "");
            const string td1 = "padding:10px 14px; font-size:12px; color:#64748b; width:42%; border-bottom:0.5px solid #e2e8f0;";
            const string td2 = "padding:10px 14px; font-size:13px; color:#0f172a; font-weight:700; border-bottom:0.5px solid #e2e8f0;";

            var filas = new List<string>();
            if (c.MontoMaritimo != null)
                filas.Add($"<tr><td style='{td1}'>Flete marítimo<br><span style='font-size:11px;'>{E(c.IdPuertoOrigenNavigation?.NombrePuerto)} → {E(c.IdPuertoDestinoNavigation?.NombrePuerto)} · {E(c.TipoContenedor)}</span></td><td style='{td2}'>{E(FormatearMonto(c.MontoMaritimo, c.MonedaMaritimo))} <span style='font-weight:400; color:#64748b;'>por contenedor</span></td></tr>");
            if (c.MontoTerrestre != null)
                filas.Add($"<tr><td style='{td1}'>Transporte terrestre<br><span style='font-size:11px;'>{E(string.IsNullOrWhiteSpace(c.PlantaCarga) ? c.ZonaCarga : $"{c.PlantaCarga} ({c.ZonaCarga})")} ⇄ {E(c.IdPuertoTerrestreNavigation?.NombrePuerto ?? c.ZonaEntrega)}</span></td><td style='{td2}'>{E(FormatearMonto(c.MontoTerrestre, c.MonedaTerrestre))}</td></tr>");
            if (c.MontoDocumental != null)
                filas.Add($"<tr><td style='{td1}'>Servicio documental<br><span style='font-size:11px;'>{E(c.TipoCargaDocumental)}</span></td><td style='{td2}'>{E(FormatearMonto(c.MontoDocumental, c.MonedaDocumental))}</td></tr>");

            string validez = c.ValidaHasta != null
                ? $"<p style='font-size:12px; color:#475569; margin:14px 0 0;'>Tarifa válida hasta el <strong>{c.ValidaHasta.Value.ToString("dd'/'MM'/'yyyy")}</strong>.</p>"
                : "";
            string comentario = string.IsNullOrWhiteSpace(c.ComentarioRespuesta)
                ? ""
                : $"<div style='margin-top:18px; padding:14px 16px; background:#f8fafc; border-left:3px solid #6d2d9e; border-radius:6px; font-size:13px; color:#334155; white-space:pre-wrap;'>{E(c.ComentarioRespuesta)}</div>";
            string urlPortal = Url.Page("/PortalCliente/MisCotizaciones", null, new { id = c.Id }, Request.Scheme) ?? "";

            return $@"<div style='font-family:Arial,sans-serif; max-width:640px; margin:0 auto;'>
<div style='background:linear-gradient(135deg,#0f172a 0%,#6d2d9e 100%); padding:28px 32px; border-radius:8px 8px 0 0;'>
<p style='color:#cbd5e1; font-size:10px; text-transform:uppercase; letter-spacing:1px; margin:0 0 4px;'>Atilson Cargo SpA — Cotización #{c.Id}</p>
<h1 style='color:#fff; font-size:22px; margin:0;'>{(esActualizacion ? "Actualizamos tu cotización" : "Tu cotización está lista")}</h1>
<p style='color:#a78bfa; font-size:13px; margin:6px 0 0;'>{E(c.TipoServicio)}</p></div>
<div style='background:#fff; padding:28px 32px; border:0.5px solid #e2e8f0; border-top:none; border-radius:0 0 8px 8px;'>
<p style='font-size:15px; color:#0f172a;'>Estimado/a <strong>{E(NombreCliente(c))}</strong>,</p>
<p style='font-size:14px; color:#475569; line-height:1.6;'>Estas son las tarifas que te ofrecemos para lo que nos consultaste. Valores netos, no incluyen IVA cuando corresponda.</p>
<table style='width:100%; border-collapse:collapse; border:0.5px solid #e2e8f0;'>{string.Join("", filas)}</table>
{validez}{comentario}
<div style='text-align:center; margin:30px 0 8px;'><a href='{E(urlPortal)}' style='background:#0f172a; color:#fff; padding:14px 32px; text-decoration:none; border-radius:6px; font-weight:800; font-size:14px; display:inline-block;'>Ver en el Portal Cliente</a></div>
<hr style='border:0; border-top:0.5px solid #e2e8f0; margin:28px 0;'>
<p style='font-size:11px; color:#94a3b8; text-align:center; margin:0;'><strong>Atilson Cargo SpA</strong> · Ante cualquier consulta responda este correo.</p>
</div></div>";
        }
    }
}
