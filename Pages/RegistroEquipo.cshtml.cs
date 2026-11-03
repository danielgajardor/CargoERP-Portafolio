using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages
{
    /// <summary>
    /// Solicitud de cuenta para el equipo Atilson (oct-2026). Queda PENDIENTE hasta que un
    /// administrador le asigna el rol y la valida en Maestros › Equipo Atilson.
    /// </summary>
    [AllowAnonymous]
    public class RegistroEquipoModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly RegistroEquipoService _equipo;
        private readonly ILogger<RegistroEquipoModel> _logger;

        // Freno simple contra envíos masivos: máximo 5 solicitudes por IP por hora.
        private static readonly ConcurrentDictionary<string, List<DateTime>> EnviosPorIp = new();

        public RegistroEquipoModel(AtilsonContext context, RegistroEquipoService equipo, ILogger<RegistroEquipoModel> logger)
        {
            _context = context;
            _equipo = equipo;
            _logger = logger;
        }

        public sealed class Formulario
        {
            [Required(ErrorMessage = "Escribe tu nombre y apellido.")]
            [StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres.")]
            public string? NombreCompleto { get; set; }

            [Required(ErrorMessage = "Escribe tu RUT.")]
            [StringLength(12, ErrorMessage = "Revisa el RUT (ej: 12.345.678-5).")]
            public string? Rut { get; set; }

            [Required(ErrorMessage = "Escribe tu teléfono.")]
            [StringLength(50, ErrorMessage = "El teléfono admite hasta 50 caracteres.")]
            public string? Telefono { get; set; }

            [Required(ErrorMessage = "Escribe tu correo.")]
            [EmailAddress(ErrorMessage = "Revisa el correo.")]
            [StringLength(100, ErrorMessage = "El correo admite hasta 100 caracteres.")]
            public string? Correo { get; set; }

            public string? Area { get; set; }

            [Required(ErrorMessage = "Elige una contraseña.")]
            [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
            public string? Password { get; set; }

            [Required(ErrorMessage = "Repite la contraseña.")]
            [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
            public string? ConfirmarPassword { get; set; }

            /// <summary>Campo trampa: las personas no lo ven; si viene con texto, es un robot.</summary>
            public string? SitioWeb { get; set; }

            /// <summary>Momento en que se mostró el formulario (ticks UTC).</summary>
            public long Mostrado { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public bool Enviado { get; set; }

        public string? CorreoEnviado { get; set; }

        public IActionResult OnGet()
        {
            // Antes, con una sesión de cliente abierta en el navegador, esta página redirigía al
            // portal y parecía que "no cargaba" (nov-2026). Ahora se muestra con un aviso para
            // cerrar esa sesión (ver la vista).
            Form.Mostrado = DateTime.UtcNow.Ticks;
            CorreoEnviado = TempData["RegistroEquipoCorreo"] as string;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            bool trampa = !string.IsNullOrWhiteSpace(Form.SitioWeb);
            bool muyRapido = Form.Mostrado > 0 && DateTime.UtcNow.Ticks - Form.Mostrado < TimeSpan.TicksPerSecond * 2;
            if (trampa || muyRapido)
            {
                _logger.LogWarning("Registro de equipo descartado como automático (trampa={Trampa}, rápido={Rapido}).", trampa, muyRapido);
                ModelState.AddModelError(string.Empty, "No pudimos verificar el envío. Revisa los datos y vuelve a presionar «Enviar solicitud».");
                Form.SitioWeb = null;
                ModelState.Remove("Form.SitioWeb");
            }

            string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
            var ahora = DateTime.UtcNow;
            var envios = EnviosPorIp.GetOrAdd(ip, _ => new List<DateTime>());
            lock (envios)
            {
                envios.RemoveAll(t => t < ahora.AddHours(-1));
                if (envios.Count >= 5)
                    ModelState.AddModelError(string.Empty, "Recibimos demasiadas solicitudes desde tu conexión. Inténtalo nuevamente en una hora.");
            }

            Form.NombreCompleto = Limpio(Form.NombreCompleto);
            Form.Rut = Limpio(Form.Rut);
            Form.Telefono = Limpio(Form.Telefono);
            Form.Correo = Limpio(Form.Correo);
            Form.Area = PermisosEquipo.Areas.FirstOrDefault(a => a == Limpio(Form.Area));

            if (Form.Rut != null && !RutHelper.EsValido(Form.Rut))
                ModelState.AddModelError("Form.Rut", "El RUT no es válido. Revisa los números y el dígito verificador.");
            if (Form.Password != null && Form.Password.Length >= 8 && (!Form.Password.Any(char.IsLetter) || !Form.Password.Any(char.IsDigit)))
                ModelState.AddModelError("Form.Password", "La contraseña debe tener letras y números.");

            if (ModelState.IsValid)
            {
                string? bloqueo = null;
                try { bloqueo = await _equipo.MotivoBloqueoAsync(Form.Rut!, Form.Correo!); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "No se pudo revisar el registro del equipo (¿falta Sql/2026-10_registro_equipo.sql?)");
                    bloqueo = "No pudimos procesar tu solicitud por un problema de nuestro sistema. Avísale a tu jefatura.";
                }
                if (bloqueo != null) ModelState.AddModelError(string.Empty, bloqueo);
            }

            if (!ModelState.IsValid)
                return VolverConErrores();

            var solicitud = new SolicitudRegistroEquipo
            {
                NombreCompleto = Form.NombreCompleto!,
                Rut = RutHelper.Formatear(Form.Rut),
                Telefono = Form.Telefono!,
                Correo = RegistroClientesService.NormalizarCorreo(Form.Correo),
                AreaSolicitada = Form.Area,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Form.Password),
                Estado = SolicitudRegistroEquipo.EstadoPendiente,
                FechaSolicitud = DateTime.Now,
                IpOrigen = ip.Length > 64 ? ip.Substring(0, 64) : ip
            };
            _context.SolicitudesRegistroEquipo.Add(solicitud);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "No se pudo guardar la solicitud de cuenta de {Correo}", solicitud.Correo);
                ModelState.AddModelError(string.Empty, "No pudimos guardar tu solicitud por un problema de nuestro sistema. Avísale a tu jefatura.");
                return VolverConErrores();
            }
            lock (envios) envios.Add(ahora);

            await _equipo.AvisarNuevaSolicitudAsync(solicitud, Url.Page("/App/Equipo/Index", null, null, Request.Scheme) ?? "");
            TempData["RegistroEquipoCorreo"] = solicitud.Correo;
            return RedirectToPage(new { enviado = true });
        }

        private IActionResult VolverConErrores()
        {
            Form.Password = null;
            Form.ConfirmarPassword = null;
            ModelState.Remove("Form.Password");
            ModelState.Remove("Form.ConfirmarPassword");
            Form.Mostrado = DateTime.UtcNow.Ticks - TimeSpan.TicksPerSecond * 10;
            ModelState.Remove("Form.Mostrado");
            return Page();
        }

        private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
