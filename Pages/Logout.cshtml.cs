using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages
{
    public class LogoutModel : PageModel
    {
        public async Task<IActionResult> OnPostAsync()
        {
            // 1. Destruimos la cookie de sesión del usuario
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // 2. Lo mandamos de vuelta a la página de Login
            return RedirectToPage("/Login");
        }

        // Por seguridad, si alguien entra tipeando /Logout en la URL (GET), 
        // también lo deslogueamos.
        // ?vuelta=/RegistroEquipo: tras cerrar sesión vuelve a esa página (solo rutas locales).
        public async Task<IActionResult> OnGetAsync(string? vuelta)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!string.IsNullOrEmpty(vuelta) && Url.IsLocalUrl(vuelta)) return LocalRedirect(vuelta);
            return RedirectToPage("/Login");
        }
    }
}