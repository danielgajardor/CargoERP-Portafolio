using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Puertos
{
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        public sealed class Fila
        {
            public Puerto Puerto { get; init; } = null!;
            public string? Ciudad { get; init; }
            public bool Activo => Puerto.Activo == 1;
        }

        public List<Fila> Filas { get; set; } = new();
        public int TotalActivos { get; set; }
        public int TotalInactivos { get; set; }
        public List<string> Paises { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string Estado { get; set; } = "activos";

        [BindProperty(SupportsGet = true, Name = "q")]
        public string? Q { get; set; }

        [BindProperty(SupportsGet = true, Name = "f")]
        public string? F { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? Resaltar { get; set; }

        public async Task OnGetAsync()
        {
            Estado = Estado is "inactivos" or "todos" ? Estado : "activos";
            var ciudades = await _context.Ciudades.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre);
            var todos = await _context.Puertos.AsNoTracking().ToListAsync();
            TotalActivos = todos.Count(p => p.Activo == 1);
            TotalInactivos = todos.Count - TotalActivos;
            Filas = (Estado switch
            {
                "inactivos" => todos.Where(p => p.Activo != 1),
                "todos" => todos,
                _ => todos.Where(p => p.Activo == 1)
            })
            .Select(p => new Fila { Puerto = p, Ciudad = p.IdCiudad.HasValue ? ciudades.GetValueOrDefault(p.IdCiudad.Value) : null })
            .OrderBy(f => f.Puerto.NombrePuerto)
            .ToList();
            Paises = Filas.Select(f => (f.Puerto.Pais ?? "").Trim()).Where(p => p != "").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
        }

        public async Task<IActionResult> OnPostCambiarEstadoAsync(int id, bool activar)
        {
            var puerto = await _context.Puertos.FindAsync(id);
            if (puerto == null)
            {
                TempData["ToastError"] = "El puerto ya no existe.";
                return RedirectToPage("./Index", new { Estado });
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            byte nuevo = (byte)(activar ? 1 : 0);
            if (puerto.Activo != nuevo)
            {
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Estado", puerto.Activo == 1 ? "Activo" : "Inactivo", activar ? "Activo" : "Inactivo");
                puerto.Activo = nuevo;
                puerto.FechaModificacion = MaestrosAuditoria.Ahora();
                puerto.UsuarioModificador = usuario;
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntPuerto, puerto.Id, cambios, usuario, "Cambio de estado desde la lista");
                await _context.SaveChangesAsync();
            }

            TempData["ToastOk"] = activar
                ? $"Puerto «{puerto.NombrePuerto}» reactivado."
                : $"Puerto «{puerto.NombrePuerto}» desactivado. Puedes reactivarlo desde «Inactivos».";
            return RedirectToPage("./Index", new { Estado, resaltar = puerto.Id });
        }
    }
}
