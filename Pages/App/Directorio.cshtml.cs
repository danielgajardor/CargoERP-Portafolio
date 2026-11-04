using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App
{
    // Directorio del equipo (oct-2026): quién es quién y cómo contactarlo.
    // Foto, cargo y teléfono los completa cada persona en Mi perfil.
    public class DirectorioModel : PageModel
    {
        private readonly AtilsonContext _context;
        public DirectorioModel(AtilsonContext context) => _context = context;

        public record Persona(Usuario Usuario, PerfilUsuario? Perfil);
        public List<Persona> Personas { get; private set; } = new();
        public bool FaltaScript { get; private set; }

        public async Task OnGetAsync()
        {
            var usuarios = await _context.Usuarios.AsNoTracking()
                .Where(u => u.Rol != "Cliente" && u.Activo == 1)
                .OrderBy(u => u.NombreCompleto).ToListAsync();
            var perfiles = new Dictionary<int, PerfilUsuario>();
            try
            {
                var ids = usuarios.Select(u => u.Id).ToList();
                perfiles = (await _context.PerfilesUsuario.AsNoTracking().Where(p => ids.Contains(p.IdUsuario)).ToListAsync())
                    .GroupBy(p => p.IdUsuario).ToDictionary(g => g.Key, g => g.First());
            }
            catch
            {
                FaltaScript = true; // Sql/2026-10_perfiles_soporte.sql aún no se corre
            }
            Personas = usuarios.Select(u =>
            {
                perfiles.TryGetValue(u.Id, out var p);
                return new Persona(u, p);
            }).ToList();
        }
    }
}
