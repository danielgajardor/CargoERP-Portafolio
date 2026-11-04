using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages
{
    /// <summary>
    /// Registro de empresas en el portal (oct-2026). Crea una solicitud PENDIENTE; el
    /// cliente, su usuario y su planta se crean cuando Atilson la aprueba.
    /// </summary>
    [AllowAnonymous]
    public class RegistroModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly RegistroClientesService _registro;
        private readonly ILogger<RegistroModel> _logger;

        // Freno simple contra envíos masivos: máximo 5 solicitudes por IP por hora.
        private static readonly ConcurrentDictionary<string, List<DateTime>> EnviosPorIp = new();

        public RegistroModel(AtilsonContext context, RegistroClientesService registro, ILogger<RegistroModel> logger)
        {
            _context = context;
            _registro = registro;
            _logger = logger;
        }

        public sealed class Formulario
        {
            [Required(ErrorMessage = "Escribe la razón social.")]
            [StringLength(100, ErrorMessage = "La razón social admite hasta 100 caracteres.")]
            public string? RazonSocial { get; set; }

            [Required(ErrorMessage = "Escribe el RUT de la empresa.")]
            [StringLength(12, ErrorMessage = "Revisa el RUT (ej: 76.123.456-7).")]
            public string? Rut { get; set; }

            [Required(ErrorMessage = "Escribe la dirección de la oficina.")]
            [StringLength(255, ErrorMessage = "La dirección admite hasta 255 caracteres.")]
            public string? DireccionOficina { get; set; }

            [Required(ErrorMessage = "Escribe el nombre de la persona de contacto.")]
            [StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres.")]
            public string? NombreContacto { get; set; }

            [Required(ErrorMessage = "Escribe un teléfono de contacto.")]
            [StringLength(50, ErrorMessage = "El teléfono admite hasta 50 caracteres.")]
            public string? Telefono { get; set; }

            [Required(ErrorMessage = "Escribe tu correo.")]
            [EmailAddress(ErrorMessage = "Revisa el correo.")]
            [StringLength(100, ErrorMessage = "El correo admite hasta 100 caracteres.")]
            public string? Correo { get; set; }


            [Required(ErrorMessage = "Elige una contraseña.")]
            [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
            public string? Password { get; set; }

            [Required(ErrorMessage = "Repite la contraseña.")]
            [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
            public string? ConfirmarPassword { get; set; }

            /// <summary>Campo trampa: las personas no lo ven; si viene con texto, es un robot.</summary>
            public string? SitioWeb { get; set; }

            /// <summary>Momento en que se mostró el formulario (ticks UTC): un envío en menos de 2 segundos es automático.</summary>
            public long Mostrado { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public List<SelectListItem> Zonas { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public bool Enviado { get; set; }

        public string? CorreoEnviado { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            // Un cliente con sesión va a su portal. Un usuario interno sí puede ver y probar el
            // formulario (antes se le redirigía al Dashboard y parecía que el registro no abría).
            // Con una sesión de cliente abierta ya no se redirige al portal (parecía que la página
            // no cargaba); la vista muestra un aviso para cerrar esa sesión (nov-2026).
            Form.Mostrado = DateTime.UtcNow.Ticks;
            CorreoEnviado = TempData["RegistroCorreo"] as string;
            await CargarZonasAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Anti-robots: campo trampa invisible y tiempo mínimo de llenado. Si algo falla se
            // explica (antes se respondía "recibido" sin guardar, y parecía que no funcionaba).
            bool trampa = !string.IsNullOrWhiteSpace(Form.SitioWeb);
            bool muyRapido = Form.Mostrado > 0 && DateTime.UtcNow.Ticks - Form.Mostrado < TimeSpan.TicksPerSecond * 2;
            if (trampa || muyRapido)
            {
                _logger.LogWarning("Registro descartado como automático (trampa={Trampa}, rápido={Rapido}).", trampa, muyRapido);
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

            Form.RazonSocial = Limpio(Form.RazonSocial);
            Form.Rut = Limpio(Form.Rut);
            Form.DireccionOficina = Limpio(Form.DireccionOficina);
            Form.NombreContacto = Limpio(Form.NombreContacto);
            Form.Telefono = Limpio(Form.Telefono);
            Form.Correo = Limpio(Form.Correo);

            if (Form.Rut != null && !RutHelper.EsValido(Form.Rut))
                ModelState.AddModelError("Form.Rut", "El RUT no es válido. Revisa los números y el dígito verificador.");
          
            if (Form.Password != null && Form.Password.Length >= 8 && (!Form.Password.Any(char.IsLetter) || !Form.Password.Any(char.IsDigit)))
                ModelState.AddModelError("Form.Password", "La contraseña debe tener letras y números.");

            if (ModelState.IsValid)
            {
                var bloqueo = await _registro.MotivoBloqueoAsync(Form.Rut!, Form.Correo!);
                if (bloqueo != null) ModelState.AddModelError(string.Empty, bloqueo);
            }

            if (!ModelState.IsValid)
                return await VolverConErrores();

            var solicitud = new SolicitudRegistroCliente
            {
                RazonSocial = Form.RazonSocial!,
                Rut = RutHelper.Formatear(Form.Rut),
                NombreContacto = Form.NombreContacto!,
                Telefono = Form.Telefono!,
                Correo = Form.Correo!.ToLowerInvariant(),
                DireccionOficina = Form.DireccionOficina!,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Form.Password),
                Estado = SolicitudRegistroCliente.EstadoPendiente,
                FechaSolicitud = DateTime.Now,
                IpOrigen = ip.Length > 64 ? ip.Substring(0, 64) : ip
            };
            _context.SolicitudesRegistroCliente.Add(solicitud);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // Típicamente: falta correr el script SQL del registro en la base.
                _logger.LogError(ex, "No se pudo guardar la solicitud de registro de {Rut}", solicitud.Rut);
                ModelState.AddModelError(string.Empty, "No pudimos guardar tu solicitud por un problema de nuestro sistema. Inténtalo más tarde o escríbenos.");
                return await VolverConErrores();
            }
            lock (envios) envios.Add(ahora);

            // Confirmación a quien se registró y aviso interno a Atilson (en segundo plano).
            await _registro.AvisarNuevaSolicitudAsync(solicitud, Url.Page("/App/Clientes/Solicitudes", null, null, Request.Scheme) ?? "");

            TempData["RegistroCorreo"] = solicitud.Correo;
            return RedirectToPage(new { enviado = true });
        }

        private async Task<IActionResult> VolverConErrores()
        {
            Form.Password = null;
            Form.ConfirmarPassword = null;
            ModelState.Remove("Form.Password");
            ModelState.Remove("Form.ConfirmarPassword");
            Form.Mostrado = DateTime.UtcNow.Ticks - TimeSpan.TicksPerSecond * 10;
            ModelState.Remove("Form.Mostrado");
            await CargarZonasAsync();
            return Page();
        }

        private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private async Task CargarZonasAsync()
        {
            var chile = await _context.Paises.AsNoTracking()
                .Where(p => p.Nombre == "Chile" || p.Nombre == "CHILE")
                .Select(p => (int?)p.Id).FirstOrDefaultAsync();
            var ciudades = await _context.Ciudades.AsNoTracking()
                .Where(c => chile == null || c.IdPais == chile)
                .Select(c => new { c.Id, c.Nombre })
                .ToListAsync();
        }
    }
}
