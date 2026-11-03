using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtilsonCargoSpa.Pages.App.Clientes
{
    /// <summary>Se mantiene la dirección /Create por compatibilidad: abre la ficha nueva.</summary>
    public class CreateModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("./Edit");
    }
}
