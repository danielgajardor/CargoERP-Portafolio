using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Plantas
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
            public Planta Planta { get; init; } = null!;
            public string? Ciudad { get; init; }
            public string? Cliente { get; init; }
            public bool Activo => Planta.Activo;
        }

        public List<Fila> Filas { get; set; } = new();
        public int TotalActivos { get; set; }
        public int TotalInactivos { get; set; }
        public int SinCiudad { get; set; }
        public int PorValidar { get; set; }
        public List<(string Valor, string Texto)> OpcionesCiudad { get; set; } = new();

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
            Estado = Estado is "inactivos" or "todos" or "porvalidar" ? Estado : "activos";
            var ciudades = await _context.Ciudades.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre);
            var clientes = await _context.Clientes.AsNoTracking().Select(c => new { c.Id, c.RazonSocial }).ToDictionaryAsync(c => c.Id, c => c.RazonSocial);
            var todas = await _context.Plantas.AsNoTracking().ToListAsync();

            TotalActivos = todas.Count(p => p.Activo);
            TotalInactivos = todas.Count - TotalActivos;
            PorValidar = todas.Count(p => p.Activo && !p.Validada);

            var visibles = Estado switch
            {
                "inactivos" => todas.Where(p => !p.Activo),
                "porvalidar" => todas.Where(p => p.Activo && !p.Validada),
                "todos" => todas,
                _ => todas.Where(p => p.Activo)
            };

            Filas = visibles
                .Select(p => new Fila
                {
                    Planta = p,
                    Ciudad = p.IdCiudad > 0 ? ciudades.GetValueOrDefault(p.IdCiudad) : null,
                    Cliente = p.IdCliente.HasValue ? clientes.GetValueOrDefault(p.IdCliente.Value) : null
                })
                .OrderBy(f => f.Planta.Nombre)
                .ToList();

            SinCiudad = Filas.Count(f => f.Ciudad == null);
            OpcionesCiudad = Filas.Where(f => f.Ciudad != null)
                .Select(f => (f.Planta.IdCiudad.ToString(), f.Ciudad!))
                .Distinct()
                .OrderBy(x => x.Item2)
                .ToList();
        }

        /// <summary>Atilson revisó una planta registrada por un cliente en el portal.</summary>
        public async Task<IActionResult> OnPostValidarAsync(int id)
        {
            var planta = await _context.Plantas.FindAsync(id);
            if (planta == null)
            {
                TempData["ToastError"] = "La planta ya no existe.";
                return RedirectToPage("./Index", new { Estado });
            }
            if (!planta.Validada)
            {
                string usuario = MaestrosAuditoria.Usuario(User);
                var ahora = MaestrosAuditoria.Ahora();
                planta.Validada = true;
                planta.FechaValidacion = ahora;
                planta.UsuarioValidacion = usuario;
                planta.FechaModificacion = ahora;
                planta.UsuarioModificador = usuario;
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Validación", "Por validar", "Validada");
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntPlanta, planta.Id, cambios, usuario, "Validada en Maestros", ahora);
                await _context.SaveChangesAsync();
            }
            TempData["ToastOk"] = $"Planta «{planta.Nombre}» validada.";
            return RedirectToPage("./Index", new { Estado, resaltar = planta.Id });
        }

        public async Task<IActionResult> OnPostCambiarEstadoAsync(int id, bool activar)
        {
            var planta = await _context.Plantas.FindAsync(id);
            if (planta == null)
            {
                TempData["ToastError"] = "La planta ya no existe.";
                return RedirectToPage("./Index", new { Estado });
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            if (planta.Activo != activar)
            {
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Estado", planta.Activo ? "Activa" : "Inactiva", activar ? "Activa" : "Inactiva");
                planta.Activo = activar;
                planta.FechaModificacion = MaestrosAuditoria.Ahora();
                planta.UsuarioModificador = usuario;
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntPlanta, planta.Id, cambios, usuario, "Cambio de estado desde la lista");
                await _context.SaveChangesAsync();
            }

            TempData["ToastOk"] = activar
                ? $"Planta «{planta.Nombre}» reactivada."
                : $"Planta «{planta.Nombre}» desactivada. Puedes reactivarla desde «Inactivas».";
            return RedirectToPage("./Index", new { Estado, resaltar = planta.Id });
        }
    }
}
