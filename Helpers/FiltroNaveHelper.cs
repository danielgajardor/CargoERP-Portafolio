using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Helpers
{
    // Filtro "Nave" de los módulos de Finanzas y del Mainboard (13-nov-2026). Las operaciones
    // guardan la nave como texto "NAVE / VIAJE"; el filtro elige la nave del catálogo (maestro
    // de naves) y encuentra los bookings cuyo texto la contiene, con cualquier viaje.
    public static class FiltroNaveHelper
    {
        /// <summary>Naves del catálogo, más las escritas en operaciones de los últimos meses.</summary>
        public static async Task<List<string>> OpcionesAsync(AtilsonContext db)
        {
            var catalogo = await db.Naves.AsNoTracking()
                .Where(n => !n.IsDeleted && n.Nombre != null && n.Nombre != "")
                .Select(n => n.Nombre).ToListAsync();
            var desde = System.DateTime.Today.AddMonths(-12);
            var desdeOps = await db.Operaciones.AsNoTracking()
                .Where(o => !o.IsDeleted && o.Nave != null && o.Nave != "" && o.FechaCreacion >= desde)
                .Select(o => o.Nave!).Distinct().Take(500).ToListAsync();
            return catalogo.Concat(desdeOps.Select(SoloNave))
                .Select(n => (n ?? "").Trim().ToUpperInvariant())
                .Where(n => n.Length > 1)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>"MSC ANNA / 245W" → "MSC ANNA".</summary>
        public static string SoloNave(string? naveViaje)
        {
            string t = (naveViaje ?? "").Trim();
            int i = t.IndexOf('/');
            return i > 0 ? t.Substring(0, i).Trim() : t;
        }

        public static string? Normalizar(string? nave) =>
            string.IsNullOrWhiteSpace(nave) ? null : nave.Trim().ToUpperInvariant();
    }
}
