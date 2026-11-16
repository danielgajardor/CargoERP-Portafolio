using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Naves
{
    /// <summary>
    /// Maestro de naves (nov-2026): el catálogo dbo.naves que usan Itinerarios (Agregar salida,
    /// carga masiva) y la ventana «Agregar salida» de Create. Desactivar = IsDeleted (la nave deja
    /// de ofrecerse en las listas; las salidas y bookings guardados no cambian).
    /// </summary>
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        public sealed class Fila
        {
            public Nafe Nave { get; init; } = null!;
            public string Naviera { get; init; } = "";
            public string Color { get; init; } = "#94a3b8";
            public int Salidas { get; init; }
            public DateOnly? Proxima { get; init; }
        }

        public List<Fila> Filas { get; set; } = new();
        public List<Naviera> Navieras { get; set; } = new();
        public int TotalActivos { get; set; }
        public int TotalInactivos { get; set; }

        [BindProperty(SupportsGet = true)]
        public string Estado { get; set; } = "activos";

        [BindProperty(SupportsGet = true, Name = "q")]
        public string? Q { get; set; }

        // Filtro por naviera (id de la naviera, o "0" = sin naviera).
        [BindProperty(SupportsGet = true, Name = "f")]
        public string? F { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? Resaltar { get; set; }

        public async Task OnGetAsync()
        {
            Estado = Estado is "inactivos" or "todos" ? Estado : "activos";
            var todas = await _context.Naves.AsNoTracking().Include(n => n.IdNavieraNavigation).ToListAsync();
            TotalActivos = todas.Count(n => !n.IsDeleted);
            TotalInactivos = todas.Count - TotalActivos;
            Navieras = todas.Where(n => n.IdNavieraNavigation != null).Select(n => n.IdNavieraNavigation!)
                .GroupBy(n => n.Id).Select(g => g.First()).OrderBy(n => n.NombreNaviera).ToList();

            // Salidas activas por naviera + nombre de nave (el texto de la salida es el del catálogo).
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            var salidas = await _context.ItinerariosNaves.AsNoTracking().Where(i => i.Activo)
                .Select(i => new { i.IdNaviera, i.Nave, i.Etd }).ToListAsync();
            var porNave = salidas
                .GroupBy(i => (i.IdNaviera, Nombre: NavesCatalogoHelper.Normalizar(i.Nave)))
                .ToDictionary(g => g.Key, g => (Total: g.Count(), Proxima: g.Where(x => x.Etd >= hoy).Select(x => (DateOnly?)x.Etd).Min()));

            Filas = (Estado switch
            {
                "inactivos" => todas.Where(n => n.IsDeleted),
                "todos" => todas,
                _ => todas.Where(n => !n.IsDeleted)
            })
            .OrderBy(n => n.Nombre).ThenBy(n => n.IdNavieraNavigation?.NombreNaviera)
            .Select(n =>
            {
                porNave.TryGetValue((n.IdNaviera ?? 0, NavesCatalogoHelper.Normalizar(n.Nombre)), out var uso);
                return new Fila
                {
                    Nave = n,
                    Naviera = n.IdNavieraNavigation?.NombreNaviera ?? "",
                    Color = n.IdNavieraNavigation != null ? ItinerariosHelper.Color(n.IdNavieraNavigation) : "#94a3b8",
                    Salidas = uso.Total,
                    Proxima = uso.Proxima
                };
            })
            .ToList();
        }

        public async Task<IActionResult> OnPostCambiarEstadoAsync(int id, bool activar)
        {
            var nave = await _context.Naves.FirstOrDefaultAsync(n => n.Id == id);
            if (nave == null)
            {
                TempData["ToastError"] = "La nave ya no existe.";
                return RedirectToPage("./Index", new { Estado });
            }

            if (activar && nave.IsDeleted)
            {
                // No se reactiva si ya hay otra activa con el mismo nombre en la misma naviera.
                string nombre = NavesCatalogoHelper.Normalizar(nave.Nombre);
                var activas = await _context.Naves.AsNoTracking().Where(n => !n.IsDeleted && n.Id != id && n.IdNaviera == nave.IdNaviera).ToListAsync();
                if (activas.Any(n => NavesCatalogoHelper.Normalizar(n.Nombre) == nombre))
                {
                    TempData["ToastError"] = $"Ya hay una nave activa «{nave.Nombre}» en la misma naviera: no se reactivó para no duplicarla.";
                    return RedirectToPage("./Index", new { Estado, resaltar = nave.Id });
                }
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            bool eliminado = !activar;
            if (nave.IsDeleted != eliminado)
            {
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Estado", nave.IsDeleted ? "Inactiva" : "Activa", activar ? "Activa" : "Inactiva");
                nave.IsDeleted = eliminado;
                nave.FechaModificacion = MaestrosAuditoria.Ahora();
                nave.UsuarioModificador = Recortar(usuario);
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntNave, nave.Id, cambios, usuario, "Cambio de estado desde la lista");
                await _context.SaveChangesAsync();
            }

            TempData["ToastOk"] = activar
                ? $"Nave «{nave.Nombre}» reactivada."
                : $"Nave «{nave.Nombre}» desactivada: ya no aparece al agregar salidas. Las salidas y bookings guardados no cambian.";
            return RedirectToPage("./Index", new { Estado, resaltar = nave.Id });
        }

        internal static string Recortar(string usuario) => usuario.Length > 50 ? usuario[..50] : usuario;
    }
}
