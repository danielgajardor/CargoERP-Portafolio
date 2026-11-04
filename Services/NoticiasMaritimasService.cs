using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    /// <summary>Una noticia lista para mostrar en la landing.</summary>
    public record NoticiaMaritima(string Titulo, string Enlace, string? Imagen, string? Resumen, DateTime? Fecha);

    // ============================================================================
    // Noticias de Mundo Marítimo para la landing (oct-2026)
    // ----------------------------------------------------------------------------
    // Antes la landing pedía el feed desde el navegador a rss2json.com (servicio de
    // terceros con límite de uso) y casi nunca traía imagen. Ahora el servidor:
    //   1) lee el RSS de mundomaritimo.cl (prueba varias rutas conocidas);
    //   2) si no hay RSS, lee la portada de noticias y toma los enlaces a artículos;
    //   3) a cada noticia sin imagen le busca la imagen del artículo (og:image);
    //   4) guarda el resultado 30 min en memoria (y el último bueno por 24 h, por si
    //      el sitio se cae) para no consultar Mundo Marítimo en cada visita.
    // Las imágenes se sirven a través de la landing (?handler=Imagen) porque Mundo
    // Marítimo las publica por http:// y el navegador las bloquea en un sitio https.
    // ============================================================================
    public class NoticiasMaritimasService
    {
        public const string ClienteHttp = "mundomaritimo";
        public const string ClienteImagenes = "mundomaritimo-img"; // sin redirecciones automáticas
        public const string SitioBase = "https://www.mundomaritimo.cl";
        private static readonly string[] RutasRss = { "/noticias/feed", "/noticias/rss", "/rss", "/feed", "/rss.xml" };
        private static readonly string[] RutasPortada = { "/noticias/index/", "/" };
        private static readonly HashSet<string> SlugsNoArticulo = new(StringComparer.OrdinalIgnoreCase)
        { "index", "editor", "friend", "get_image", "categoria", "categorias", "buscar", "busqueda", "tag", "tags", "autor", "page", "feed", "rss" };

        private const string CacheNoticias = "mm:noticias";
        private const string CacheUltimaBuena = "mm:noticias:ultima";
        private const int MaxNoticias = 9;
        private const int MaxBytesImagen = 2_500_000;

        private readonly IHttpClientFactory _http;
        private readonly IMemoryCache _cache;
        private readonly ILogger<NoticiasMaritimasService> _logger;
        private readonly SemaphoreSlim _candado = new(1, 1);

        public NoticiasMaritimasService(IHttpClientFactory http, IMemoryCache cache, ILogger<NoticiasMaritimasService> logger)
        {
            _http = http;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>Últimas noticias y de dónde salieron ("rss", "portada", "cache" o "sin-conexion").</summary>
        public async Task<(IReadOnlyList<NoticiaMaritima> Noticias, string Fuente)> ObtenerAsync(CancellationToken ct = default)
        {
            if (_cache.TryGetValue(CacheNoticias, out (IReadOnlyList<NoticiaMaritima>, string) enCache)) return enCache;

            await _candado.WaitAsync(ct);
            try
            {
                if (_cache.TryGetValue(CacheNoticias, out enCache)) return enCache;

                string fuente = "rss";
                var noticias = await LeerRssAsync(ct);
                if (noticias.Count < 3)
                {
                    fuente = "portada";
                    noticias = await LeerPortadaAsync(ct);
                }
                noticias = await CompletarDesdeArticulosAsync(noticias, ct);

                if (noticias.Count > 0)
                {
                    var resultado = ((IReadOnlyList<NoticiaMaritima>)noticias, fuente);
                    _cache.Set(CacheNoticias, resultado, TimeSpan.FromMinutes(30));
                    _cache.Set(CacheUltimaBuena, noticias, TimeSpan.FromHours(24));
                    return resultado;
                }

                // Mundo Marítimo no respondió: usamos lo último que funcionó y reintentamos en 5 min.
                var ultima = _cache.Get<List<NoticiaMaritima>>(CacheUltimaBuena);
                var sinConexion = ((IReadOnlyList<NoticiaMaritima>)(ultima ?? new List<NoticiaMaritima>()), ultima != null ? "cache" : "sin-conexion");
                _cache.Set(CacheNoticias, sinConexion, TimeSpan.FromMinutes(5));
                return sinConexion;
            }
            finally
            {
                _candado.Release();
            }
        }

        // ---------------------------------------------------------------- RSS
        private async Task<List<NoticiaMaritima>> LeerRssAsync(CancellationToken ct)
        {
            foreach (var ruta in RutasRss)
            {
                string? xml = await DescargarTextoAsync(SitioBase + ruta, ct);
                if (xml == null || !xml.TrimStart().StartsWith("<", StringComparison.Ordinal)) continue;
                try
                {
                    var lista = ParsearRss(xml);
                    if (lista.Count > 0) return lista;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "La ruta {Ruta} de Mundo Marítimo no es un RSS válido", ruta);
                }
            }
            return new List<NoticiaMaritima>();
        }

        /// <summary>Lee RSS 2.0 o Atom. Público para poder probarlo con un XML de ejemplo.</summary>
        public static List<NoticiaMaritima> ParsearRss(string xml)
        {
            var doc = XDocument.Parse(xml);
            XNamespace media = "http://search.yahoo.com/mrss/";
            XNamespace content = "http://purl.org/rss/1.0/modules/content/";
            XNamespace atom = "http://www.w3.org/2005/Atom";
            var lista = new List<NoticiaMaritima>();

            foreach (var item in doc.Descendants("item").Take(MaxNoticias * 2))
            {
                string titulo = Limpiar(item.Element("title")?.Value);
                string? enlace = AUrlAbsoluta(item.Element("link")?.Value?.Trim());
                if (titulo.Length == 0 || enlace == null) continue;
                string descripcionHtml = item.Element("description")?.Value ?? "";
                string contenidoHtml = item.Element(content + "encoded")?.Value ?? "";

                string? imagen =
                    item.Elements("enclosure").FirstOrDefault(e => (e.Attribute("type")?.Value ?? "image").StartsWith("image", StringComparison.OrdinalIgnoreCase))?.Attribute("url")?.Value
                    ?? item.Elements(media + "content").FirstOrDefault()?.Attribute("url")?.Value
                    ?? item.Descendants(media + "thumbnail").FirstOrDefault()?.Attribute("url")?.Value
                    ?? PrimeraImagen(descripcionHtml)
                    ?? PrimeraImagen(contenidoHtml);

                lista.Add(new NoticiaMaritima(titulo, enlace, AUrlAbsoluta(imagen), Resumir(descripcionHtml.Length > 0 ? descripcionHtml : contenidoHtml), LeerFecha(item.Element("pubDate")?.Value)));
            }

            foreach (var entry in doc.Descendants(atom + "entry").Take(MaxNoticias * 2))
            {
                string titulo = Limpiar(entry.Element(atom + "title")?.Value);
                string? enlace = AUrlAbsoluta(entry.Elements(atom + "link").FirstOrDefault(l => (l.Attribute("rel")?.Value ?? "alternate") == "alternate")?.Attribute("href")?.Value);
                if (titulo.Length == 0 || enlace == null) continue;
                string resumenHtml = entry.Element(atom + "summary")?.Value ?? entry.Element(atom + "content")?.Value ?? "";
                string? imagen = entry.Elements(media + "content").FirstOrDefault()?.Attribute("url")?.Value
                                 ?? entry.Elements(atom + "link").FirstOrDefault(l => l.Attribute("rel")?.Value == "enclosure")?.Attribute("href")?.Value
                                 ?? PrimeraImagen(resumenHtml);
                lista.Add(new NoticiaMaritima(titulo, enlace, AUrlAbsoluta(imagen), Resumir(resumenHtml),
                    LeerFecha(entry.Element(atom + "published")?.Value ?? entry.Element(atom + "updated")?.Value)));
            }

            // Solo artículos del propio Mundo Marítimo (la tarjeta enlaza ahí).
            return lista.Where(n => EsDeMundoMaritimo(n.Enlace)).GroupBy(n => n.Enlace).Select(g => g.First()).Take(MaxNoticias).ToList();
        }

        // ---------------------------------------------------------------- Portada (sin RSS)
        private async Task<List<NoticiaMaritima>> LeerPortadaAsync(CancellationToken ct)
        {
            foreach (var ruta in RutasPortada)
            {
                string? html = await DescargarTextoAsync(SitioBase + ruta, ct);
                if (html == null) continue;
                var enlaces = EnlacesDeArticulos(html);
                if (enlaces.Count >= 3)
                    return enlaces.Take(MaxNoticias).Select(e => new NoticiaMaritima(e.Titulo, e.Enlace, null, null, null)).ToList();
            }
            return new List<NoticiaMaritima>();
        }

        /// <summary>Enlaces a artículos (/noticias/{slug}) de una página de Mundo Marítimo, en orden de aparición.</summary>
        public static List<(string Enlace, string Titulo)> EnlacesDeArticulos(string html)
        {
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lista = new List<(string, string)>();
            var rx = new Regex(@"<a\b[^>]*href\s*=\s*[""'](?<u>(?:https?://(?:www\.)?mundomaritimo\.cl)?/noticias/(?<slug>[a-z0-9][a-z0-9\-]{8,}))/?[""'][^>]*>(?<t>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in rx.Matches(html))
            {
                string slug = m.Groups["slug"].Value;
                if (SlugsNoArticulo.Contains(slug) || !slug.Contains('-')) continue;
                string? url = AUrlAbsoluta(m.Groups["u"].Value);
                if (url == null) continue;
                string titulo = Limpiar(m.Groups["t"].Value);
                if (vistos.Contains(url))
                {
                    // El mismo artículo suele enlazarse dos veces (imagen y título): nos quedamos con el título con texto.
                    int i = lista.FindIndex(x => x.Item1 == url);
                    if (i >= 0 && lista[i].Item2.Length < 15 && titulo.Length >= 15) lista[i] = (url, titulo);
                    continue;
                }
                vistos.Add(url);
                lista.Add((url, titulo));
            }
            return lista;
        }

        // ---------------------------------------------------------------- Artículo (og:image)
        private async Task<List<NoticiaMaritima>> CompletarDesdeArticulosAsync(List<NoticiaMaritima> noticias, CancellationToken ct)
        {
            using var limite = new SemaphoreSlim(4);
            var tareas = noticias.Select(async n =>
            {
                if (n.Imagen != null && !string.IsNullOrWhiteSpace(n.Resumen) && n.Titulo.Length >= 15 && n.Fecha != null) return n;
                if (!EsDeMundoMaritimo(n.Enlace)) return n; // solo se consultan páginas del propio sitio
                await limite.WaitAsync(ct);
                try
                {
                    string? html = await DescargarTextoAsync(n.Enlace, ct);
                    if (html == null) return n;
                    var meta = MetaDeArticulo(html);
                    return n with
                    {
                        Titulo = n.Titulo.Length >= 15 ? n.Titulo : (meta.Titulo ?? n.Titulo),
                        Imagen = n.Imagen ?? AUrlAbsoluta(meta.Imagen),
                        Resumen = string.IsNullOrWhiteSpace(n.Resumen) ? meta.Resumen : n.Resumen,
                        Fecha = n.Fecha ?? meta.Fecha
                    };
                }
                finally
                {
                    limite.Release();
                }
            });
            var completas = await Task.WhenAll(tareas);
            return completas.Where(n => n.Titulo.Length > 0).ToList();
        }

        /// <summary>og:title, og:image, og:description y fecha de publicación de un artículo.</summary>
        public static (string? Titulo, string? Imagen, string? Resumen, DateTime? Fecha) MetaDeArticulo(string html)
        {
            string? Meta(string nombre)
            {
                var m = Regex.Match(html, $@"<meta\b[^>]*(?:property|name)\s*=\s*[""']{Regex.Escape(nombre)}[""'][^>]*content\s*=\s*[""'](?<v>[^""']*)[""']", RegexOptions.IgnoreCase)
                    is { Success: true } a ? a
                    : Regex.Match(html, $@"<meta\b[^>]*content\s*=\s*[""'](?<v>[^""']*)[""'][^>]*(?:property|name)\s*=\s*[""']{Regex.Escape(nombre)}[""']", RegexOptions.IgnoreCase);
                return m.Success ? WebUtility.HtmlDecode(m.Groups["v"].Value).Trim() : null;
            }
            string? titulo = Meta("og:title");
            string? imagen = Meta("og:image") ?? Meta("twitter:image");
            string? resumen = Meta("og:description") ?? Meta("description");
            DateTime? fecha = LeerFecha(Meta("article:published_time") ?? Meta("og:updated_time") ?? Meta("date"));
            return (string.IsNullOrWhiteSpace(titulo) ? null : Limpiar(titulo), string.IsNullOrWhiteSpace(imagen) ? null : imagen,
                    string.IsNullOrWhiteSpace(resumen) ? null : Resumir(resumen), fecha);
        }

        // ---------------------------------------------------------------- Imágenes (proxy)
        /// <summary>Solo se consultan y sirven direcciones de Mundo Marítimo (evita usar la landing como proxy abierto).</summary>
        public static bool EsDeMundoMaritimo(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            && (u.Host.Equals("mundomaritimo.cl", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".mundomaritimo.cl", StringComparison.OrdinalIgnoreCase));

        public async Task<(byte[] Bytes, string Tipo)?> ObtenerImagenAsync(string url, CancellationToken ct = default)
        {
            if (!EsDeMundoMaritimo(url)) return null;
            string clave = "mm:img:" + url;
            if (_cache.TryGetValue(clave, out (byte[], string) guardada)) return guardada;

            try
            {
                var cliente = _http.CreateClient(ClienteImagenes);
                string actual = url;
                for (int salto = 0; salto < 4; salto++)
                {
                    using var resp = await cliente.GetAsync(actual, HttpCompletionOption.ResponseHeadersRead, ct);
                    if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location != null)
                    {
                        // Redirecciones solo hacia el mismo sitio (o su versión https).
                        var siguiente = new Uri(new Uri(actual), resp.Headers.Location).ToString();
                        if (!EsDeMundoMaritimo(siguiente)) return null;
                        actual = siguiente;
                        continue;
                    }
                    if (!resp.IsSuccessStatusCode) return null;
                    string tipo = resp.Content.Headers.ContentType?.MediaType ?? "";
                    if (!tipo.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
                    if (resp.Content.Headers.ContentLength > MaxBytesImagen) return null;
                    var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                    if (bytes.Length == 0 || bytes.Length > MaxBytesImagen) return null;
                    var resultado = (bytes, tipo);
                    _cache.Set(clave, resultado, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12), Size = null });
                    return resultado;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogDebug(ex, "No se pudo traer la imagen {Url}", url);
            }
            return null;
        }

        // ---------------------------------------------------------------- utilidades
        private async Task<string?> DescargarTextoAsync(string url, CancellationToken ct)
        {
            try
            {
                var cliente = _http.CreateClient(ClienteHttp);
                using var resp = await cliente.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogInformation("Mundo Marítimo no respondió en {Url}: {Error}", url, ex.Message);
                return null;
            }
        }

        private static string? AUrlAbsoluta(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = WebUtility.HtmlDecode(url.Trim());
            if (url.StartsWith("//")) url = "https:" + url;
            if (url.StartsWith("/")) url = SitioBase + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return null;
            return u.ToString();
        }

        private static string? PrimeraImagen(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            var m = Regex.Match(html, @"<img\b[^>]*\bsrc\s*=\s*[""'](?<s>[^""']+)[""']", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["s"].Value : null;
        }

        private static string Limpiar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            string sinEtiquetas = Regex.Replace(texto, "<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(sinEtiquetas), @"\s+", " ").Trim();
        }

        private static string? Resumir(string? html)
        {
            string t = Limpiar(html);
            if (t.Length == 0) return null;
            if (t.Length <= 160) return t;
            int corte = t.LastIndexOf(' ', 157);
            return t.Substring(0, corte > 100 ? corte : 157).TrimEnd(',', '.', ';', ':') + "…";
        }

        private static DateTime? LeerFecha(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return null;
            valor = valor.Trim();
            if (DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto)) return dto.LocalDateTime;
            // RFC 822 con zona en letras (p. ej. "Fri, 30 Oct 2026 10:00:00 GMT" o "-0300").
            var limpio = Regex.Replace(valor, @"\s+(GMT|UT|UTC|[A-Z]{3,4})$", " +0000");
            if (DateTimeOffset.TryParseExact(limpio, new[] { "ddd, d MMM yyyy HH:mm:ss zzz", "ddd, dd MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm:ss zzz" },
                    CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dto)) return dto.LocalDateTime;
            return null;
        }
    }
}
