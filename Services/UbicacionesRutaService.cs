using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    /// <summary>Una parada ubicada en el mapa y qué tan exacta es su posición.</summary>
    public record ParadaRuta(string Tipo, string Nombre, double Lat, double Lon, string Precision, bool Aproximada);

    // ============================================================================
    // Ubicaciones para "Seguir camión" (oct-2026)
    // ----------------------------------------------------------------------------
    // Antes cada parada se buscaba solo por texto (dirección O ciudad O nombre) y el
    // puerto por su nombre de ciudad, así que el mapa podía marcar el centro de la
    // comuna, otro país o fallar si la dirección no se encontraba. Ahora, en orden:
    //   Planta  : coordenadas del enlace de Google Maps de la planta (exacto) →
    //             dirección + ciudad → dirección → nombre + ciudad → ciudad.
    //   Depósito: dirección + ciudad → dirección → nombre + ciudad → ciudad.
    //   Puerto  : terminal portuario conocido (STI, DPW, TPS, SVTI...) → puerto.
    // Solo se aceptan resultados dentro de Chile. Cada parada informa su precisión
    // para que Operaciones sepa si es exacta o aproximada (y cómo mejorarla).
    // Las búsquedas se guardan 7 días en memoria (OpenStreetMap pide no repetirlas).
    // ============================================================================
    public class UbicacionesRutaService
    {
        private readonly AtilsonContext _context;
        private readonly IHttpClientFactory _http;
        private readonly IMemoryCache _cache;
        private readonly ILogger<UbicacionesRutaService> _logger;

        public UbicacionesRutaService(AtilsonContext context, IHttpClientFactory http, IMemoryCache cache, ILogger<UbicacionesRutaService> logger)
        {
            _context = context;
            _http = http;
            _cache = cache;
            _logger = logger;
        }

        // Accesos de los terminales y puertos de Chile (aprox. a la entrada de camiones).
        private static readonly (string[] Claves, string Nombre, double Lat, double Lon)[] Terminales =
        {
            (new[] { "STI", "SAN ANTONIO TERMINAL" }, "Terminal STI, San Antonio", -33.5935, -71.6145),
            (new[] { "DPW SAN ANTONIO", "DPW", "PUERTO CENTRAL", "PCE" }, "Puerto Central (DP World), San Antonio", -33.5795, -71.6125),
            (new[] { "TPS", "TERMINAL PACIFICO SUR" }, "Terminal TPS, Valparaíso", -33.0345, -71.6265),
            (new[] { "TCVAL", "TERMINAL CERROS DE VALPARAISO" }, "Terminal TCVAL, Valparaíso", -33.0255, -71.6195),
            (new[] { "SVTI", "SAN VICENTE TERMINAL" }, "Terminal SVTI, San Vicente", -36.7325, -73.1365),
            (new[] { "DPW LIRQUEN", "LIRQUEN", "LIRQUÉN" }, "Puerto Lirquén (DP World)", -36.7105, -72.9795),
            (new[] { "CORONEL" }, "Puerto Coronel", -37.0305, -73.1555),
        };

        private static readonly (string Clave, string Nombre, double Lat, double Lon)[] Puertos =
        {
            ("SAN ANTONIO", "Puerto San Antonio", -33.5880, -71.6140),
            ("VALPARAISO", "Puerto Valparaíso", -33.0340, -71.6265),
            ("SAN VICENTE", "Puerto San Vicente", -36.7325, -73.1365),
            ("TALCAHUANO", "Puerto Talcahuano", -36.7135, -73.1105),
            ("LIRQUEN", "Puerto Lirquén", -36.7105, -72.9795),
            ("CORONEL", "Puerto Coronel", -37.0305, -73.1555),
            ("QUINTERO", "Puerto Quintero", -32.7790, -71.5310),
            ("IQUIQUE", "Puerto Iquique", -20.2055, -70.1525),
            ("ANTOFAGASTA", "Puerto Antofagasta", -23.6445, -70.4035),
            ("ANGAMOS", "Puerto Angamos, Mejillones", -23.0745, -70.3905),
            ("MEJILLONES", "Puerto Angamos, Mejillones", -23.0745, -70.3905),
            ("ARICA", "Puerto Arica", -18.4765, -70.3235),
            ("PUERTO MONTT", "Puerto Montt", -41.4855, -72.9525),
            ("PUNTA ARENAS", "Puerto Punta Arenas", -53.1660, -70.9060),
            ("CALDERA", "Puerto Caldera", -27.0645, -70.8245),
            ("CHACABUCO", "Puerto Chacabuco", -45.4650, -72.8200),
        };

        public static string Normalizar(string? s) =>
            new string((s ?? "").ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Trim();

        private static readonly string[] CiudadesConocidas =
        {
            "SAN ANTONIO", "VALPARAISO", "SANTIAGO", "SAN VICENTE", "TALCAHUANO", "CORONEL", "LIRQUEN", "PENCO", "CONCEPCION",
            "ARICA", "IQUIQUE", "ANTOFAGASTA", "MEJILLONES", "PUERTO MONTT", "RANCAGUA", "CURICO", "TALCA", "LOS ANDES",
            "QUILLOTA", "LA CALERA", "PLACILLA", "PUDAHUEL", "QUILICURA", "SAN BERNARDO", "MAIPU", "LAMPA", "CHILLAN", "LINARES"
        };

        public static string? CiudadEnNombre(string? nombre)
        {
            string n = Normalizar(nombre);
            return CiudadesConocidas.Where(c => n.Contains(c)).OrderByDescending(c => c.Length).FirstOrDefault();
        }

        public static bool EnChile(double lat, double lon) => lat is >= -56.5 and <= -17.0 && lon is >= -76.5 and <= -66.0;

        /// <summary>Coordenadas dentro de un enlace de Google Maps (@lat,lon · !3d…!4d… · q=lat,lon).</summary>
        public static (double Lat, double Lon)? CoordenadasDeEnlace(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            string u = Uri.UnescapeDataString(url);
            var patrones = new[]
            {
                @"!3d(-?\d{1,2}\.\d+)!4d(-?\d{1,3}\.\d+)",
                @"@(-?\d{1,2}\.\d+),\s*(-?\d{1,3}\.\d+)",
                @"[?&](?:q|query|ll|center|destination|daddr)=(?:loc:)?(-?\d{1,2}\.\d+),\s*(-?\d{1,3}\.\d+)",
                @"^\s*(-?\d{1,2}\.\d+),\s*(-?\d{1,3}\.\d+)\s*$"
            };
            foreach (var p in patrones)
            {
                var m = Regex.Match(u, p);
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                              && double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                              && EnChile(lat, lon))
                    return (lat, lon);
            }
            return null;
        }

        public async Task<(ParadaRuta? Parada, string? Error)> UbicarDepositoAsync(string nombre)
        {
            var dep = await _context.Depositos.AsNoTracking().Include(d => d.IdCiudadNavigation).FirstOrDefaultAsync(d => d.NombreDeposito == nombre);
            // Sin ciudad asignada, se deduce del nombre ("MEDLOG SAN ANTONIO" → San Antonio).
            string? ciudad = dep?.IdCiudadNavigation?.Nombre ?? CiudadEnNombre(nombre);
            return await PorTextoAsync("deposito", $"Depósito {nombre}", dep?.Direccion, nombre, ciudad,
                "agrega la dirección exacta del depósito en Maestros › Depósitos");
        }

        public async Task<(ParadaRuta? Parada, string? Error)> UbicarPlantaAsync(string nombre)
        {
            var pl = await _context.Plantas.AsNoTracking().Include(p => p.Ciudad).FirstOrDefaultAsync(p => p.Nombre == nombre);
            var enlace = await CoordenadasConEnlaceAsync(pl?.UrlGoogleMaps);
            if (enlace != null)
                return (new ParadaRuta("planta", $"Planta {nombre}", enlace.Value.Lat, enlace.Value.Lon, "Ubicación exacta (enlace de Google Maps)", false), null);
            return await PorTextoAsync("planta", $"Planta {nombre}", pl?.Direccion, nombre, pl?.Ciudad?.Nombre,
                "pega el enlace de Google Maps de la planta en Maestros › Plantas");
        }

        public async Task<(ParadaRuta? Parada, string? Error)> UbicarPuertoAsync(string puerto, string? terminal)
        {
            string t = Normalizar(terminal), p = Normalizar(puerto);
            if (t.Length > 0 && t != "-")
            {
                // La clave más larga que calce gana ("DPW LIRQUEN" antes que "DPW"); un terminal de
                // San Antonio solo vale si el puerto es San Antonio (en Lirquén, "DPW" es DPW Lirquén).
                var term = Terminales
                    .SelectMany(x => x.Claves.Select(k => (x, k: Normalizar(k))))
                    .Where(z => t == z.k || t.Contains(z.k))
                    .OrderByDescending(z => z.k.Length)
                    .Select(z => z.x)
                    .FirstOrDefault(x => !(x.Nombre.Contains("San Antonio") && p.Length > 0 && !p.Contains("SAN ANTONIO")));
                if (term.Nombre != null)
                    return (new ParadaRuta("puerto", term.Nombre, term.Lat, term.Lon, "Acceso del terminal portuario", false), null);
            }
            var pto = Puertos.FirstOrDefault(x => p.Contains(x.Clave));
            if (pto.Nombre != null)
                return (new ParadaRuta("puerto", pto.Nombre + (t.Length > 0 && t != "-" ? $" ({terminal})" : ""), pto.Lat, pto.Lon, "Recinto portuario", false), null);
            return await PorTextoAsync("puerto", $"Puerto {puerto}", null, puerto, null, "revisa el nombre del puerto");
        }

        /// <summary>Busca en OpenStreetMap con varias consultas, de la más precisa a la menos precisa.</summary>
        private async Task<(ParadaRuta? Parada, string? Error)> PorTextoAsync(string tipo, string etiqueta, string? direccion, string nombre, string? ciudad, string comoMejorar)
        {
            var intentos = new List<(string Q, string Precision, bool Aprox)>();
            if (!string.IsNullOrWhiteSpace(direccion) && !string.IsNullOrWhiteSpace(ciudad)) intentos.Add(($"{direccion}, {ciudad}", "Dirección", false));
            if (!string.IsNullOrWhiteSpace(direccion)) intentos.Add((direccion!, "Dirección", false));
            if (!string.IsNullOrWhiteSpace(ciudad)) intentos.Add(($"{nombre}, {ciudad}", "Nombre del lugar", true));
            if (!string.IsNullOrWhiteSpace(ciudad)) intentos.Add((ciudad!, $"Centro de {ciudad} (aproximado: {comoMejorar})", true));
            intentos.Add((nombre, $"Búsqueda por nombre (aproximado: {comoMejorar})", true));

            foreach (var i in intentos)
            {
                var c = await GeocodificarAsync(i.Q);
                if (c != null) return (new ParadaRuta(tipo, etiqueta, c.Value.Lat, c.Value.Lon, i.Precision, i.Aprox), null);
            }
            return (null, $"No se pudo ubicar {etiqueta} en el mapa: {comoMejorar}.");
        }

        private async Task<(double Lat, double Lon)?> GeocodificarAsync(string consulta)
        {
            string q = consulta.Trim();
            if (q.Length < 3) return null;
            if (!q.Contains("Chile", StringComparison.OrdinalIgnoreCase)) q += ", Chile";
            string clave = "geo:" + q.ToLowerInvariant();
            if (_cache.TryGetValue(clave, out (double, double)? guardado)) return guardado;
            try
            {
                var cli = _http.CreateClient("osm");
                string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(q)}&format=json&limit=1&countrycodes=cl";
                using var resp = await cli.GetAsync(url);
                (double, double)? res = null;
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        var r = doc.RootElement[0];
                        if (double.TryParse(r.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                            && double.TryParse(r.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                            && EnChile(lat, lon))
                            res = (lat, lon);
                    }
                }
                _cache.Set(clave, res, TimeSpan.FromDays(res == null ? 1 : 7));
                return res;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogInformation("No se pudo geocodificar «{Q}»: {Error}", q, ex.Message);
                return null;
            }
        }

        /// <summary>Coordenadas del enlace de Google Maps; los enlaces cortos (maps.app.goo.gl) se resuelven primero.</summary>
        private async Task<(double Lat, double Lon)?> CoordenadasConEnlaceAsync(string? url)
        {
            var directo = CoordenadasDeEnlace(url);
            if (directo != null || string.IsNullOrWhiteSpace(url)) return directo;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
            string host = u.Host.ToLowerInvariant();
            if (!(host == "maps.app.goo.gl" || host == "goo.gl" || host.EndsWith(".google.com") || host == "google.com" || host.EndsWith(".google.cl"))) return null;
            string clave = "gmaps:" + u;
            if (_cache.TryGetValue(clave, out (double, double)? guardado)) return guardado;
            (double, double)? res = null;
            try
            {
                var cli = _http.CreateClient("osm-sinredir");
                var actual = u;
                for (int i = 0; i < 4 && res == null; i++)
                {
                    using var resp = await cli.GetAsync(actual);
                    var loc = resp.Headers.Location;
                    if (loc == null) break;
                    actual = loc.IsAbsoluteUri ? loc : new Uri(actual, loc);
                    res = CoordenadasDeEnlace(actual.ToString());
                    string h = actual.Host.ToLowerInvariant();
                    if (!(h.EndsWith("google.com") || h.EndsWith("google.cl") || h.EndsWith("goo.gl"))) break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogInformation("No se pudo abrir el enlace de Google Maps {Url}: {Error}", url, ex.Message);
            }
            _cache.Set(clave, res, TimeSpan.FromDays(7));
            return res;
        }
    }
}
