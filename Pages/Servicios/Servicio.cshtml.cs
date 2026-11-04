using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtilsonCargoSpa.Pages.Servicios
{
    // Página pública de cada servicio (nov-2026): /servicios/{slug}. El contenido está en ServiciosWeb.cs.
    public class ServicioModel : PageModel
    {
        public ServicioWeb Servicio { get; private set; } = null!;

        public IActionResult OnGet(string slug)
        {
            var s = ServiciosWeb.Buscar(slug);
            if (s == null) return NotFound();
            Servicio = s;
            return Page();
        }
    }
}
