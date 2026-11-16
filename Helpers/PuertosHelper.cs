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

        // ---------------------------------------------------------------------
        // Ruta POL → POD (nov-2026): el origen y el destino no pueden ser del mismo país
        // (p. ej. Valparaíso → San Antonio). Un país vacío u "OTROS" no se compara.

        /// <summary>Mayúsculas, sin tildes y con espacios simples (igual que ruta-paises.js).</summary>
        public static string ClaveTexto(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var desc = texto.Trim().ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(desc.Length);
            foreach (var ch in desc)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        /// <summary>País en mayúsculas y sin tildes; "" si no hay o es "OTROS".</summary>
        public static string PaisNormalizado(string? pais)
        {
            var n = ClaveTexto(pais);
            return n == "OTROS" ? "" : n;
        }

        public static bool MismoPais(string? paisPol, string? paisPod)
        {
            var a = PaisNormalizado(paisPol);
            return a != "" && a == PaisNormalizado(paisPod);
        }

        public static string MensajeMismoPais(string? pais) =>
            $"El puerto de origen (POL) y el de destino (POD) son de {(string.IsNullOrWhiteSpace(pais) ? "el mismo país" : pais.Trim().ToUpper())}: el destino debe ser de otro país.";

        /// <summary>País común si POL y POD (por Id) son del mismo país; null si no.</summary>
        public static async Task<string?> MismoPaisAsync(AtilsonContext context, int? idPol, int? idPod)
        {
            if (idPol == null || idPod == null) return null;
            var paises = await context.Puertos.AsNoTracking()
                .Where(p => p.Id == idPol || p.Id == idPod)
                .Select(p => new { p.Id, p.Pais })
                .ToListAsync();
            var a = paises.FirstOrDefault(p => p.Id == idPol)?.Pais;
            var b = paises.FirstOrDefault(p => p.Id == idPod)?.Pais;
            return MismoPais(a, b) ? a!.Trim().ToUpper() : null;
        }

        /// <summary>
        /// Igual que MismoPaisAsync, con los puertos por nombre (Comercial guarda el nombre). Si un
        /// nombre existe en más de un país no se bloquea (no se puede saber cuál es).
        /// </summary>
        public static async Task<string?> MismoPaisPorNombreAsync(AtilsonContext context, string? pol, string? pod)
        {
            var mapa = await MapaPaisesAsync(context);
            return MismoPaisPorNombre(mapa, pol, pod);
        }

        public static string? MismoPaisPorNombre(MapaPaises mapa, string? pol, string? pod)
        {
            if (string.IsNullOrWhiteSpace(pol) || string.IsNullOrWhiteSpace(pod)) return null;
            if (!mapa.PorNombre.TryGetValue(ClaveTexto(pol), out var a) || !mapa.PorNombre.TryGetValue(ClaveTexto(pod), out var b)) return null;
            return a != "" && a == b ? a : null;
        }

        /// <summary>País de cada puerto por Id y por nombre (nombre en más de un país = "").</summary>
        public sealed class MapaPaises
        {
            public Dictionary<string, string> PorId { get; init; } = new();
            public Dictionary<string, string> PorNombre { get; init; } = new();
        }

        private static MapaPaises? _mapa;
        private static DateTime _mapaHasta = DateTime.MinValue;

        // Se guarda 5 minutos: los puertos cambian poco y se usa en cada lista de POL / POD.
        public static async Task<MapaPaises> MapaPaisesAsync(AtilsonContext context)
        {
            var actual = _mapa;
            if (actual != null && DateTime.UtcNow < _mapaHasta) return actual;
            var puertos = await context.Puertos.AsNoTracking().Select(p => new { p.Id, p.NombrePuerto, p.Pais }).ToListAsync();
            var nuevo = new MapaPaises
            {
                PorId = puertos.ToDictionary(p => p.Id.ToString(), p => PaisNormalizado(p.Pais)),
                PorNombre = puertos.Where(p => !string.IsNullOrWhiteSpace(p.NombrePuerto))
                    .GroupBy(p => ClaveTexto(p.NombrePuerto))
                    .ToDictionary(g => g.Key, g =>
                    {
                        var paises = g.Select(p => PaisNormalizado(p.Pais)).Where(x => x != "").Distinct().ToList();
                        return paises.Count == 1 ? paises[0] : "";
                    })
            };
            _mapa = nuevo;
            _mapaHasta = DateTime.UtcNow.AddMinutes(5);
            return nuevo;
        }

        /// <summary>Para que un cambio en el maestro de puertos se vea de inmediato.</summary>
        public static void OlvidarMapaPaises() => _mapa = null;
    }
}
