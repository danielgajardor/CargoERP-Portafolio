using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Navieras
{
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        public List<Naviera> Navieras { get; set; } = new();
        public int TotalActivos { get; set; }
        public int TotalInactivos { get; set; }

        [BindProperty(SupportsGet = true)]
        public string Estado { get; set; } = "activos";

        [BindProperty(SupportsGet = true, Name = "q")]
        public string? Q { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? Resaltar { get; set; }

        public async Task OnGetAsync()
        {
            Estado = Estado is "inactivos" or "todos" ? Estado : "activos";
            var todas = await _context.Navieras.AsNoTracking().Where(n => !n.IsDeleted).ToListAsync();
            TotalActivos = todas.Count(n => n.Activo == 1);
            TotalInactivos = todas.Count - TotalActivos;
            Navieras = (Estado switch
            {
                "inactivos" => todas.Where(n => n.Activo != 1),
                "todos" => todas,
                _ => todas.Where(n => n.Activo == 1)
            }).OrderBy(n => n.NombreNaviera).ToList();
        }

        public async Task<IActionResult> OnPostCambiarEstadoAsync(int id, bool activar)
        {
            var naviera = await _context.Navieras.FindAsync(id);
            if (naviera == null)
            {
                TempData["ToastError"] = "La naviera ya no existe.";
                return RedirectToPage("./Index", new { Estado });
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            byte nuevo = (byte)(activar ? 1 : 0);
            if (naviera.Activo != nuevo)
            {
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Estado", naviera.Activo == 1 ? "Activa" : "Inactiva", activar ? "Activa" : "Inactiva");
                naviera.Activo = nuevo;
                naviera.FechaModificacion = MaestrosAuditoria.Ahora();
                naviera.UsuarioModificador = usuario;
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntNaviera, naviera.Id, cambios, usuario, "Cambio de estado desde la lista");
                await _context.SaveChangesAsync();
            }

            TempData["ToastOk"] = activar
                ? $"Naviera «{naviera.NombreNaviera}» reactivada."
                : $"Naviera «{naviera.NombreNaviera}» desactivada. Puedes reactivarla desde «Inactivas».";
            return RedirectToPage("./Index", new { Estado, resaltar = naviera.Id });
        }
    }
}
