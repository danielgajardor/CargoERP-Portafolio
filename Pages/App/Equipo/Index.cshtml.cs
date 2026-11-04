using System.Security.Claims;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Equipo
{
    /// <summary>Equipo Atilson (oct-2026): validar cuentas pedidas en /RegistroEquipo, asignar y cambiar roles.</summary>
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly RegistroEquipoService _equipo;

        public IndexModel(AtilsonContext context, RegistroEquipoService equipo)
        {
            _context = context;
            _equipo = equipo;
        }

        public sealed class FilaUsuario
        {
            public Usuario Usuario { get; init; } = null!;
            public string? Rut { get; init; }
            public string? Telefono { get; init; }
            public bool EsUnoMismo { get; init; }
            public DateTime? UltimoAcceso { get; init; } // Mi perfil (oct-2026)
        }

        /// <summary>solicitudes | usuarios | revisadas</summary>
        [BindProperty(SupportsGet = true)]
        public string Vista { get; set; } = "solicitudes";

        public List<SolicitudRegistroEquipo> Solicitudes { get; set; } = new();
        public List<FilaUsuario> Usuarios { get; set; } = new();
        public int Pendientes { get; set; }
        public bool FaltaScript { get; set; }

        private string CorreoActual => User.FindFirst(ClaimTypes.Email)?.Value ?? "";

        public async Task OnGetAsync()
        {
            Vista = Vista is "usuarios" or "revisadas" ? Vista : "solicitudes";
            List<SolicitudRegistroEquipo> todas;
            try
            {
                todas = await _context.SolicitudesRegistroEquipo.AsNoTracking().ToListAsync();
            }
            catch
            {
                FaltaScript = true;
                todas = new();
            }
            Pendientes = todas.Count(s => s.Estado == SolicitudRegistroEquipo.EstadoPendiente);
            Solicitudes = Vista == "revisadas"
                ? todas.Where(s => s.Estado != SolicitudRegistroEquipo.EstadoPendiente).OrderByDescending(s => s.FechaRevision ?? s.FechaSolicitud).ToList()
                : todas.Where(s => s.Estado == SolicitudRegistroEquipo.EstadoPendiente).OrderBy(s => s.FechaSolicitud).ToList();

            var datos = todas.Where(s => s.Estado == SolicitudRegistroEquipo.EstadoAprobada)
                .GroupBy(s => s.Correo).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.FechaSolicitud).First());
            var usuarios = await _context.Usuarios.AsNoTracking().Where(u => u.Rol != "Cliente").OrderBy(u => u.NombreCompleto).ToListAsync();
            var perfiles = new Dictionary<int, PerfilUsuario>();
            try
            {
                var ids = usuarios.Select(u => u.Id).ToList();
                perfiles = (await _context.PerfilesUsuario.AsNoTracking().Where(p => ids.Contains(p.IdUsuario)).ToListAsync())
                    .GroupBy(p => p.IdUsuario).ToDictionary(g => g.Key, g => g.First());
            }
            catch { /* Sql/2026-10_perfiles_soporte.sql pendiente */ }
            Usuarios = usuarios.Select(u =>
            {
                datos.TryGetValue((u.Correo ?? "").ToLowerInvariant(), out var s);
                perfiles.TryGetValue(u.Id, out var pf);
                return new FilaUsuario
                {
                    Usuario = u,
                    Rut = s?.Rut,
                    Telefono = pf?.Telefono ?? s?.Telefono,
                    UltimoAcceso = pf?.UltimoAcceso,
                    EsUnoMismo = string.Equals(u.Correo, CorreoActual, StringComparison.OrdinalIgnoreCase)
                };
            })
            .OrderBy(f => f.Usuario.Activo == 1 ? 0 : 1).ThenBy(f => f.Usuario.NombreCompleto)
            .ToList();
        }

        public async Task<IActionResult> OnPostAprobarAsync(int id, string? rol)
        {
            var (ok, m) = await _equipo.AprobarAsync(id, rol, MaestrosAuditoria.Usuario(User), Url.Page("/Login", null, null, Request.Scheme) ?? "/Login");
            TempData[ok ? "ToastOk" : "ToastError"] = m;
            return RedirectToPage(new { vista = "solicitudes" });
        }

        public async Task<IActionResult> OnPostRechazarAsync(int id, string? motivo)
        {
            var (ok, m) = await _equipo.RechazarAsync(id, motivo ?? "", MaestrosAuditoria.Usuario(User));
            TempData[ok ? "ToastOk" : "ToastError"] = m;
            return RedirectToPage(new { vista = "solicitudes" });
        }

        public async Task<IActionResult> OnPostRolAsync(int id, string? rol)
        {
            var (ok, m) = await _equipo.CambiarRolAsync(id, rol, CorreoActual, MaestrosAuditoria.Usuario(User));
            TempData[ok ? "ToastOk" : "ToastError"] = m;
            return RedirectToPage(new { vista = "usuarios" });
        }

        public async Task<IActionResult> OnPostEstadoAsync(int id, bool activar)
        {
            var (ok, m) = await _equipo.CambiarEstadoAsync(id, activar, CorreoActual, MaestrosAuditoria.Usuario(User));
            TempData[ok ? "ToastOk" : "ToastError"] = m;
            return RedirectToPage(new { vista = "usuarios" });
        }
    }
}
