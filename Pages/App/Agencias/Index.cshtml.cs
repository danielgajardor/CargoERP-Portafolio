using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Agencias
{
    /// <summary>Maestro de agencias de aduana (oct-2026). Antes solo se podían crear desde Comercial.</summary>
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        public List<AgenciasAduana> Agencias { get; set; } = new();
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
            var todas = await _context.AgenciasAduanas.AsNoTracking().ToListAsync();
            TotalActivos = todas.Count(a => a.Activo == 1);
            TotalInactivos = todas.Count - TotalActivos;
            Agencias = (Estado switch
            {
                "inactivos" => todas.Where(a => a.Activo != 1),
                "todos" => todas,
                _ => todas.Where(a => a.Activo == 1)
            }).OrderBy(a => a.NombreAgencia).ToList();
        }

        public async Task<IActionResult> OnPostCambiarEstadoAsync(int id, bool activar)
        {
            var agencia = await _context.AgenciasAduanas.FindAsync(id);
            if (agencia == null)
            {
                TempData["ToastError"] = "La agencia ya no existe.";
                return RedirectToPage("./Index", new { Estado });
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            int nuevo = activar ? 1 : 0;
            if (agencia.Activo != nuevo)
            {
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Estado", agencia.Activo == 1 ? "Activa" : "Inactiva", activar ? "Activa" : "Inactiva");
                agencia.Activo = nuevo;
                agencia.FechaModificacion = MaestrosAuditoria.Ahora();
                agencia.UsuarioModificador = usuario;
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntAgencia, agencia.Id, cambios, usuario, "Cambio de estado desde la lista");
                await _context.SaveChangesAsync();
            }

            TempData["ToastOk"] = activar
                ? $"Agencia «{agencia.NombreAgencia}» reactivada."
                : $"Agencia «{agencia.NombreAgencia}» desactivada. Puedes reactivarla desde «Inactivas».";
            return RedirectToPage("./Index", new { Estado, resaltar = agencia.Id });
        }
    }
}
