using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages
{
    public class LoginModel : PageModel
    {
        private readonly AtilsonContext _context;

        public LoginModel(AtilsonContext context)
        {
            _context = context;
        }

        [BindProperty]
        public string Correo { get; set; }

        [BindProperty]
        public string Password { get; set; }

        public string ErrorMessage { get; set; }

        // Página a la que volver tras entrar (la pone el sistema al pedir login, p. ej. el
        // rastreo de la landing: /PortalCliente/Index?SearchString=BK123). Solo rutas locales.
        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        /// <summary>true si viene del portal de clientes (para abrir la pestaña Clientes).</summary>
        public bool VieneDelPortal => !string.IsNullOrEmpty(ReturnUrl) && ReturnUrl.StartsWith("/PortalCliente", StringComparison.OrdinalIgnoreCase);

        /// <summary>Booking o BL que se buscó en la landing antes de iniciar sesión.</summary>
        public string? BookingBuscado
        {
            get
            {
                if (!VieneDelPortal) return null;
                int i = ReturnUrl!.IndexOf("SearchString=", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                string v = Uri.UnescapeDataString(ReturnUrl.Substring(i + 13).Split('&')[0].Replace('+', ' ')).Trim();
                return v.Length is > 0 and <= 40 ? v : null;
            }
        }

        private IActionResult DestinoTrasLogin(bool esCliente, string porDefecto)
        {
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) && VieneDelPortal == esCliente)
                return LocalRedirect(ReturnUrl);
            return RedirectToPage(porDefecto);
        }

        public void OnGet()
        {
            // Si el usuario ya inició sesión, redirigirlo a su pantalla correspondiente
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (User.IsInRole("Cliente"))
                {
                    Response.Redirect("/PortalCliente/Index");
                }
                else
                {
                    Response.Redirect("/Operaciones/Index");
                }
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Correo) || string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "Por favor ingrese correo y contraseña.";
                return Page();
            }

            // Búsqueda solo por correo — la validación de contraseña se hace aparte
            // para poder distinguir entre hash BCrypt (usuarios ya migrados) y texto
            // plano (usuarios legacy aún no migrados), sin romper el acceso de nadie.
            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Correo == Correo && u.Activo == 1);

            bool credencialesValidas = false;

            if (usuario != null && !string.IsNullOrEmpty(usuario.Password))
            {
                bool pareceBcrypt = usuario.Password.StartsWith("$2");

                if (pareceBcrypt)
                {
                    // Usuario ya migrado: valida contra el hash.
                    try { credencialesValidas = BCrypt.Net.BCrypt.Verify(Password, usuario.Password); }
                    catch { credencialesValidas = false; }
                }
                else
                {
                    // Usuario legacy: aún tiene la contraseña en texto plano.
                    credencialesValidas = usuario.Password == Password;

                    if (credencialesValidas)
                    {
                        // Migración transparente: se rehashea con BCrypt en este mismo
                        // login, para que la próxima vez ya entre por la rama de arriba.
                        usuario.Password = BCrypt.Net.BCrypt.HashPassword(Password);
                        await _context.SaveChangesAsync();
                    }
                }
            }

            if (credencialesValidas && usuario != null)
            {   
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, usuario.NombreCompleto),
                    new Claim(ClaimTypes.Email, usuario.Correo),
                    new Claim(ClaimTypes.Role, usuario.Rol ?? "Usuario"),
                    new Claim("IdCliente", usuario.IdCliente?.ToString() ?? "0")
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity));

                // Mi perfil (oct-2026): último acceso (si falta el script SQL, no hace nada).
                if (HttpContext.RequestServices.GetService(typeof(AtilsonCargoSpa.Services.PerfilUsuarioService)) is AtilsonCargoSpa.Services.PerfilUsuarioService perfiles)
                    await perfiles.RegistrarAccesoAsync(usuario.Id);

                // --- EL DESVIADOR AUTOMÁTICO ---
                // (Si venía de una página protegida de su zona, vuelve a esa página.)
                if (usuario.Rol == "Cliente")
                {
                    return DestinoTrasLogin(true, "/PortalCliente/Index"); // A su extranet privada
                }
                else
                {
                    return DestinoTrasLogin(false, "/App/Dashboard"); // A la matriz de Atilson
                }
            }

            // Portal (oct-2026): si el correo corresponde a un registro de empresa aún en
            // revisión (o rechazado), se explica en vez del mensaje genérico.
            try
            {
                string correoNorm = Correo.Trim().ToLower();
                var solicitud = await _context.SolicitudesRegistroCliente.AsNoTracking()
                    .Where(s => s.Correo == correoNorm && s.Estado != SolicitudRegistroCliente.EstadoAprobada)
                    .OrderByDescending(s => s.FechaSolicitud)
                    .FirstOrDefaultAsync();
                if (solicitud != null && usuario == null)
                {
                    ErrorMessage = solicitud.Estado == SolicitudRegistroCliente.EstadoPendiente
                        ? "Tu registro está en revisión. Te avisaremos por correo cuando Atilson apruebe tu cuenta."
                        : $"Tu solicitud de registro no fue aprobada: {solicitud.MotivoRechazo}. Contacta a tu ejecutivo(a) de Atilson.";
                    return Page();
                }
            }
            catch
            {
                // Sin la tabla de registros (script SQL pendiente) se usa el mensaje de siempre.
            }
            // Equipo Atilson (oct-2026): cuenta del equipo pedida en /RegistroEquipo y aún sin validar.
            try
            {
                string correoNorm = Correo.Trim().ToLower();
                var solicitudEquipo = await _context.SolicitudesRegistroEquipo.AsNoTracking()
                    .Where(s => s.Correo == correoNorm && s.Estado != SolicitudRegistroEquipo.EstadoAprobada)
                    .OrderByDescending(s => s.FechaSolicitud)
                    .FirstOrDefaultAsync();
                if (solicitudEquipo != null && usuario == null)
                {
                    ErrorMessage = solicitudEquipo.Estado == SolicitudRegistroEquipo.EstadoPendiente
                        ? "Tu cuenta del equipo está pendiente: un administrador debe asignarte el rol y validarla. Te avisaremos por correo."
                        : $"Tu solicitud de cuenta no fue aprobada: {solicitudEquipo.MotivoRechazo}. Habla con tu jefatura.";
                    return Page();
                }
            }
            catch
            {
                // Sin la tabla de registros (script SQL pendiente) se usa el mensaje de siempre.
            }

            ErrorMessage = "Credenciales incorrectas o cuenta inactiva.";
            return Page();
        }
    }
}