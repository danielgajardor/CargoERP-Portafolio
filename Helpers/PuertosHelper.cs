using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    // Catálogo de puertos para el Portal Cliente y los módulos de Cotizaciones /
    // Itinerarios (oct-2026). La tabla puertos tiene nombres repetidos activos
    // (ej. SAN ANTONIO / CHILE con Id 140 y 141; CALLAO / PERU con Id 10 y 186),
    // así que:
    //  - Para mostrar, se deja UNO por nombre+país (el de menor Id), siempre el
    //    mismo en todas las pantallas.
    //  - Para buscar (ej. itinerarios de una ruta), se comparan todos los Id
    //    equivalentes de ese nombre+país, por si algún dato quedó con el otro Id.
    public static class PuertosHelper
    {
        public static string Clave(Puerto p) =>
            $"{p.NombrePuerto.Trim().ToUpper()}|{(p.Pais ?? "").Trim().ToUpper()}";

        public static bool EsChileno(Puerto p) => (p.Pais ?? "").Trim().ToUpper() == "CHILE";

        // Un puerto por nombre+país, Chile primero, luego por país y nombre.
        public static async Task<List<Puerto>> ListarUnicosAsync(AtilsonContext context, bool soloChile = false)
        {
            var puertos = await context.Puertos.AsNoTracking()
                .Where(p => p.Activo == 1)
                .ToListAsync();

            return puertos
                .Where(p => !soloChile || EsChileno(p))
                .GroupBy(Clave)
                .Select(g => g.OrderBy(p => p.Id).First())
                .OrderBy(p => EsChileno(p) ? 0 : 1)
                .ThenBy(p => (p.Pais ?? "").Trim().ToUpper())
                .ThenBy(p => p.NombrePuerto)
                .ToList();
        }

        // Todos los Id (activos o no) con el mismo nombre+país que idPuerto.
        public static async Task<List<int>> IdsEquivalentesAsync(AtilsonContext context, int idPuerto)
        {
            var p = await context.Puertos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == idPuerto);
            if (p == null) return new List<int> { idPuerto };
            var nombre = p.NombrePuerto.Trim().ToUpper();
            var pais = (p.Pais ?? "").Trim().ToUpper();
            var ids = await context.Puertos.AsNoTracking()
                .Where(x => x.NombrePuerto.Trim().ToUpper() == nombre && (x.Pais ?? "").Trim().ToUpper() == pais)
                .Select(x => x.Id)
                .ToListAsync();
            if (!ids.Contains(idPuerto)) ids.Add(idPuerto);
            return ids;
        }

        public static string Nombre(Puerto? p) =>
            p == null ? "—" : $"{p.NombrePuerto}{(string.IsNullOrWhiteSpace(p.Pais) ? "" : ", " + p.Pais)}";
    }
}
