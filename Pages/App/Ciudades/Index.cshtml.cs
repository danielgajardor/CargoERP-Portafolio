using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Ciudades
{
    /// <summary>Maestro de ciudades (oct-2026): depósitos, plantas y tarifas de transporte dependen de ellas.</summary>
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        public sealed class Fila
        {
            public Ciudade Ciudad { get; init; } = null!;
            public string Pais { get; init; } = "";
            public int Depositos { get; init; }
            public int Plantas { get; init; }
            public int Tarifas { get; init; }
        }

        public List<Fila> Filas { get; set; } = new();
        public List<(string Valor, string Texto)> OpcionesPais { get; set; } = new();

        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true, Name = "q")]
        public string? Q { get; set; }

        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true, Name = "f")]
        public string? F { get; set; }

        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
        public int? Resaltar { get; set; }

        public async Task OnGetAsync()
        {
            var paises = await _context.Paises.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
            var ciudades = await _context.Ciudades.AsNoTracking().ToListAsync();
            var depositos = (await _context.Depositos.AsNoTracking().Where(d => d.IdCiudad != null && d.Activo == 1).Select(d => d.IdCiudad!.Value).ToListAsync())
                .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var plantas = (await _context.Plantas.AsNoTracking().Where(p => p.Activo).Select(p => p.IdCiudad).ToListAsync())
                .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var tarifas = (await _context.Tarifasterrestres.AsNoTracking().Select(t => new { t.IdCiudadOrigen, t.IdCiudadDestino }).ToListAsync())
                .SelectMany(t => new[] { t.IdCiudadOrigen, t.IdCiudadDestino })
                .Where(x => x.HasValue).GroupBy(x => x!.Value).ToDictionary(g => g.Key, g => g.Count());

            Filas = ciudades
                .Select(c => new Fila
                {
                    Ciudad = c,
                    Pais = paises.GetValueOrDefault(c.IdPais) ?? "Sin país",
                    Depositos = depositos.GetValueOrDefault(c.Id),
                    Plantas = plantas.GetValueOrDefault(c.Id),
                    Tarifas = tarifas.GetValueOrDefault(c.Id)
                })
                .OrderBy(f => MaestrosAuditoria.Normalizar(f.Pais) == "CHILE" ? 0 : 1)
                .ThenBy(f => f.Pais)
                .ThenBy(f => f.Ciudad.Nombre)
                .ToList();

            OpcionesPais = Filas.Select(f => (f.Ciudad.IdPais.ToString(), f.Pais)).Distinct().OrderBy(x => x.Item2).ToList();
        }
    }
}
