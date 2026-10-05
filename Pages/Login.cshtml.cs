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

                // --- EL DESVIADOR AUTOMÁTICO ---
                if (usuario.Rol == "Cliente")
                {
                    return RedirectToPage("/PortalCliente/Index"); // A su extranet privada
                }
                else
                {
                    return RedirectToPage("/Dashboard"); // A la matriz de Atilson
                }
            }

            ErrorMessage = "Credenciales incorrectas o cuenta inactiva.";
            return Page();
        }
    }
}