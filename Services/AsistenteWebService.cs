using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    /// <summary>
    /// Asistente del sitio web (nov-2026): el chat flotante del sitio público.
    ///
    /// Funciona sin IA externa:
    ///  • Entiende preguntas por palabras clave tolerantes a tildes, plurales y errores de tipeo
    ///    (base en AsistenteConocimiento.cs: servicios, empresa, portal y un glosario de
    ///    comercio exterior con Incoterms).
    ///  • Responde con datos reales: próximas salidas de naves a un destino (itinerarios
    ///    publicados), navieras con salidas, dólar/UF/euro del día.
    ///  • Recuerda el tema de la conversación (preguntas de seguimiento) y lo que está
    ///    esperando (un destino, un número de booking).
    ///  • Arma una solicitud de cotización con 6 preguntas y la envía por correo al equipo comercial.
    /// Con "AsistenteIA:ApiKey" configurada, lo que no entiende se lo pregunta a la IA.
    /// No accede a datos de clientes: para el estado de una carga lleva al portal.
    /// </summary>
    public partial class AsistenteWebService
    {
        public const string ClienteHttp = "asistente-ia";

        public record Mensaje(string Rol, string Texto);
        public record Accion(string Texto, string Url, string Icono);
        public record Respuesta(string Texto, List<Accion> Acciones, List<string> Sugerencias, EstadoAsistente Estado, bool ConIa);

        /// <summary>Memoria de la conversación. Va y vuelve con cada mensaje (el servidor no guarda nada).</summary>
        public class EstadoAsistente
        {
            public string? Tema { get; set; }
            public string? Grupo { get; set; }
            public string? Esperando { get; set; }   // puerto_itinerario | puerto_transito | codigo
            public string? Flujo { get; set; }       // cotizacion
            public int Paso { get; set; }
            public Dictionary<string, string> Datos { get; set; } = new();
            public int Fallos { get; set; }

            private static readonly HashSet<string> ClavesDatos = new() { "tipo", "origen", "destino", "contenedor", "carga", "fecha", "correo", "contacto" };
            public EstadoAsistente Sanear()
            {
                string? Corto(string? s, int n) => s == null ? null : (s.Length > n ? s[..n] : s);
                Tema = Corto(Tema, 40); Grupo = Corto(Grupo, 20); Esperando = Corto(Esperando, 30); Flujo = Flujo == "cotizacion" ? Flujo : null;
                Paso = Math.Clamp(Paso, 0, 7); Fallos = Math.Clamp(Fallos, 0, 5);
                Datos = (Datos ?? new()).Where(kv => ClavesDatos.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => Corto(kv.Value.Trim(), 160)!);
                return this;
            }
        }

        private readonly IHttpClientFactory _http;
        private readonly IConfiguration _config;
        private readonly ILogger<AsistenteWebService> _logger;
        private readonly AtilsonContext _context;
        private readonly IndicadoresEconomicosService _indicadores;
        private readonly EmailService _email;
        private readonly IMemoryCache _cache;

        public AsistenteWebService(IHttpClientFactory http, IConfiguration config, ILogger<AsistenteWebService> logger,
            AtilsonContext context, IndicadoresEconomicosService indicadores, EmailService email, IMemoryCache cache)
        {
            _http = http; _config = config; _logger = logger; _context = context;
            _indicadores = indicadores; _email = email; _cache = cache;
        }

        private string? Conf(string k) => string.IsNullOrWhiteSpace(_config[k]) ? null : _config[k]!.Trim();
        public bool IaDisponible => Conf("AsistenteIA:ApiKey") != null;
        private string Correo => Conf("SitioWeb:Correo") ?? "contacto@cargoflow-demo.cl";
        private string? WhatsApp => Conf("SitioWeb:WhatsApp") is string w ? Regex.Replace(w, @"\D", "") : null;
        private static readonly CultureInfo Cl = new("es-CL");
        private static readonly Random Azar = new();

        // =====================================================================
        // Entrada
        // =====================================================================
        public async Task<Respuesta> ResponderAsync(List<Mensaje> historial, EstadoAsistente? estado, string? ip, CancellationToken ct, string? idioma = null)
        {
            var e = (estado ?? new EstadoAsistente()).Sanear();
            string texto = historial.LastOrDefault(m => m.Rol == "user")?.Texto?.Trim() ?? "";
            string n = Norm(texto);

            // Inglés (nov-2026): con el sitio en inglés, o si la persona escribe en inglés.
            if ((idioma == "en" && !EsEspanol(n)) || (e.Flujo == null && EsIngles(texto, n)))
                return await ResponderInglesAsync(historial, e, texto, n, ct);

            try
            {
                // 1) Cotización en curso.
                if (e.Flujo == "cotizacion")
                    return await PasoCotizacionAsync(texto, n, e, ip);

                // 2) Lo que el asistente quedó esperando (un destino, un número).
                if (e.Esperando is "puerto_itinerario" or "puerto_transito")
                {
                    var puertos = await DetectarPuertosAsync(n);
                    if (puertos.Count > 0) { bool transito = e.Esperando == "puerto_transito"; e.Esperando = null; return await ItinerariosAsync(puertos, n, e, transito); }
                    if (n.Split(' ').Length <= 4 && MejorTema(n, e).Puntaje < 0.7 && !EsIndicadores(n))
                    {
                        e.Esperando = null; e.Datos.Clear(); e.Datos["destino"] = Titulo(texto.Length > 60 ? texto[..60] : texto);
                        return R($"Por ahora no tengo salidas publicadas hacia {Titulo(texto)}, pero igual podemos cotizar esa ruta: un ejecutivo te propone naves y fechas.", e,
                            new() { A("itinerarios") }, new() { "Sí, armemos la cotización", "Hablar con una persona" });
                    }
                }
                if (e.Esperando == "codigo" && CodigoSeguimiento(texto) is string cod0) { e.Esperando = null; return Seguimiento(cod0, e); }
                e.Esperando = null;

                // 3) Intenciones con datos reales.
                if (EsArmarCotizacion(n)) return await IniciarCotizacionAsync(texto, n, e);
                // "Quiero exportar un 40 reefer a Rotterdam": ya trae datos de un embarque → se arma la solicitud.
                if (DetectarContenedor(n) != null && (DetectarTipo(n) != null || (await DetectarPuertosAsync(n)).Count > 0) && !Hay(n, "que es", "medidas", "capacidad", "cuanto cabe"))
                    return await IniciarCotizacionAsync(texto, n, e);
                if (CodigoSeguimiento(texto) is string cod && (Hay(n, "booking", "bl", "contenedor", "carga", "embarque", "rastrear", "seguimiento", "tracking", "donde esta", "estado") || n.Split(' ').Length <= 2))
                    return Seguimiento(cod, e);
                if (EsSeguimiento(n)) { e.Esperando = "codigo"; e.Tema = "seguimiento"; e.Grupo = "portal"; e.Fallos = 0;
                    return R("Claro. El detalle de cada embarque está en el portal de clientes. Si me das el número de booking, BL o contenedor, te dejo el acceso directo.", e,
                        new() { A("portal"), A("pag:portal-de-clientes") }, new() { "No tengo cuenta", "Hablar con una persona" }); }
                if (await ConvertirAsync(texto, n, e, false) is Respuesta conv) return conv;
                if (EsIndicadores(n)) return await IndicadoresAsync(n, e);
                if (EsNavieras(n)) return await NavierasAsync(e);
                bool pideItinerario = Hay(n, "itinerario", "salida", "salidas", "proxima nave", "proximas naves", "cuando zarpa", "zarpes", "zarpe", "zarpan", "naves a", "nave a", "nave para", "sale una nave", "salen naves", "sale la nave", "cuando sale", "cuando salen", "hay naves", "hay salidas");
                bool pideTransito = Hay(n, "cuanto demora", "cuanto tarda", "cuantos dias", "tiempo de transito", "dias de transito", "cuanto se demora", "demora en llegar", "transit time");
                if (pideItinerario || pideTransito)
                {
                    var puertos = await DetectarPuertosAsync(n);
                    // "¿Y cuánto demora?" después de hablar de un destino: se usa el que ya se mencionó.
                    if (puertos.Count == 0 && e.Datos.TryGetValue("destino", out var destPrevio))
                        puertos = await DetectarPuertosAsync(Norm("a " + destPrevio + (e.Datos.TryGetValue("origen", out var oPrev) ? " desde " + oPrev : "")));
                    if (puertos.Count > 0) return await ItinerariosAsync(puertos, n, e, pideTransito && !pideItinerario);
                    e.Esperando = pideTransito ? "puerto_transito" : "puerto_itinerario"; e.Tema = "itinerarios"; e.Grupo = "maritimo"; e.Fallos = 0;
                    return R(pideTransito
                            ? "Depende de la ruta, la naviera y si va directo o con transbordo. ¿A qué destino va? Te digo cuántos días toman las próximas salidas."
                            : "¡Claro! ¿A qué destino quieres enviar? Te muestro las próximas naves. También puedes ver todas las salidas en Itinerarios, sin registrarte.",
                        e, new() { A("itinerarios") }, await DestinosFrecuentesAsync());
                }

                // 4) Base de conocimiento.
                var (tema, puntaje, segundo) = MejorTema(n, e);
                bool saludo = PuntajeTema(Buscar("saludo")!, n) >= 1;
                if (tema != null && puntaje >= 0.7)
                {
                    if (tema.Id == "cotizar" && ((await DetectarPuertosAsync(n)).Count > 0 || DetectarContenedor(n) != null || DetectarTipo(n) != null))
                        return await IniciarCotizacionAsync(texto, n, e);
                    var r = DesdeTema(tema, e);
                    if (saludo && tema.Grupo != "charla") r = r with { Texto = "¡" + Saludo() + "! " + r.Texto };
                    // Dos preguntas en un mensaje ("¿qué es FOB? ¿y cuánto pesa un contenedor?"): se responden las dos.
                    if (OtraPregunta(texto, tema, AsistenteConocimiento.Temas) is TemaAsistente otro)
                    {
                        var r2 = DesdeTema(otro, new EstadoAsistente());
                        var acc = r.Acciones.Concat(r2.Acciones).GroupBy(a => a.Url).Select(g => g.First()).Take(4).Cast<Accion?>().ToList();
                        return R(r.Texto + "\n\nY sobre tu otra pregunta: " + r2.Texto, e, acc, r.Sugerencias);
                    }
                    if ((await DetectarPuertosAsync(n)).FirstOrDefault() is PuertoDetectado pd && r.Sugerencias.Count < 4 && !r.Sugerencias.Contains("Salidas a " + pd.Nombre))
                        r.Sugerencias.Insert(0, "Salidas a " + pd.Nombre);
                    if (segundo != null && segundo.Grupo != tema.Grupo && segundo.Grupo != "charla" && r.Sugerencias.Count < 4)
                        r.Sugerencias.Insert(0, Etiqueta(segundo));
                    return r;
                }

                // 5) Puertos sueltos: "y a Rotterdam?" → próximas salidas.
                var sueltos = await DetectarPuertosAsync(n);
                if (sueltos.Count > 0 && n.Split(' ').Length <= 6) return await ItinerariosAsync(sueltos, n, e, false);

                // 6) No entendió: IA si está configurada; si no, ayuda y luego una persona.
                if (IaDisponible)
                {
                    var ia = await PreguntarIaAsync(historial, ct);
                    if (!string.IsNullOrWhiteSpace(ia)) { e.Fallos = 0; return new Respuesta(ia.Trim(), new() { A("formulario") }, new(), e, true); }
                }
                e.Fallos++;
                if (e.Fallos >= 2)
                {
                    e.Fallos = 0;
                    return R("Creo que esto lo resuelve mejor alguien del equipo. Déjanos tus datos o escríbenos y te contactamos a la brevedad.", e,
                        new() { A("wsp"), A("formulario") }, AsistenteConocimiento.SugerenciasInicio.ToList());
                }
                string[] noEntendi = {
                    "Perdona, no te entendí bien. ¿Me lo cuentas con otras palabras? Por ejemplo, a dónde quieres enviar tu carga o qué duda tienes.",
                    "Mmm, eso no lo capté. ¿Me das un poco más de detalle?" };
                return R(noEntendi[Azar.Next(noEntendi.Length)], e, new(), AsistenteConocimiento.SugerenciasInicio.ToList());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error del asistente con el mensaje {Texto}", texto);
                return R("Uy, tuve un problema para responder eso. Escríbenos a " + Correo + " o déjanos tus datos y te ayudamos.", new EstadoAsistente(), new() { A("formulario") }, new());
            }
        }

        // =====================================================================
        // Respuestas
        // =====================================================================
        private Respuesta R(string texto, EstadoAsistente e, List<Accion?>? acciones = null, List<string>? sug = null)
            => new(texto, (acciones ?? new()).Where(a => a != null).Select(a => a!).ToList(), sug ?? new(), e, false);

        private Respuesta DesdeTema(TemaAsistente t, EstadoAsistente e)
        {
            e.Tema = t.Id; e.Grupo = t.Grupo; e.Fallos = 0;
            string texto = t.Respuestas[Azar.Next(t.Respuestas.Length)]
                .Replace("{correo}", Correo).Replace("{saludo}", Saludo());
            if (t.Id == "persona" && WhatsApp != null) texto = texto.Replace("Claro. Puedes", "Claro. Puedes escribirnos por WhatsApp,");
            if (t.Id is "horario" or "persona" && HoraChile().DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                texto += " Hoy es fin de semana, así que el equipo te responde el próximo día hábil.";
            var acciones = (t.Acciones ?? Array.Empty<string>()).Select(A).ToList();
            return R(texto, e, acciones, (t.Sugerencias ?? Array.Empty<string>()).ToList());
        }

        private Accion? A(string id) => id switch
        {
            "cotizar" => new("Cotizar en el portal", "/PortalCliente/SolicitarCotizacion", "bi-calculator"),
            "solicitar" => new("Solicitar servicio", "/PortalCliente/SolicitarReserva", "bi-clipboard-check"),
            "itinerarios" => new("Ver itinerarios", "/Itinerarios", "bi-calendar2-week"),
            "portal" => new("Ingreso portal", "/Login", "bi-shield-lock"),
            "registro" => new("Registrar mi empresa", "/Registro", "bi-building-add"),
            "formulario" => new("Dejar mis datos", "/#contacto", "bi-envelope"),
            "wsp" => WhatsApp != null ? new("Escribir por WhatsApp", "https://wa.me/" + WhatsApp, "bi-whatsapp") : null,
            "armar" => null, // se ofrece como sugerencia ("Sí, armemos la cotización")
            _ when id.StartsWith("pag:") => Pagina(id[4..]),
            _ => null
        };
        private static Accion? Pagina(string slug)
        {
            var s = AtilsonCargoSpa.Pages.Servicios.ServiciosWeb.Buscar(slug);
            return s == null ? null : new Accion(s.EsPortal ? "Conocer el portal" : s.Nombre, s.Url, s.Icono);
        }

        private static string Etiqueta(TemaAsistente t) => t.Id switch
        {
            var x when x.StartsWith("g_") => "¿Qué es " + t.Claves[0].ToUpperInvariant() + "?",
            "maritimo" => "Servicio marítimo", "terrestre" => "Servicio terrestre", "documental" => "Servicio documental",
            "aduana" => "Agencia de aduanas", "portal" => "Portal de clientes", "cotizar" => "Quiero cotizar",
            _ => char.ToUpper(t.Claves[0][0]) + t.Claves[0][1..]
        };

        private static DateTime HoraChile()
        {
            foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
                try { return TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(id)); } catch { }
            return DateTime.Now;
        }
        private static string Saludo()
        {
            var ahora = HoraChile();
            return ahora.Hour < 12 ? "Buenos días" : ahora.Hour < 20 ? "Buenas tardes" : "Buenas noches";
        }

        // =====================================================================
        // Comprensión: normalización y puntaje de temas
        // =====================================================================
        private static readonly HashSet<string> Vacias = new()
        {
            "el", "la", "los", "las", "de", "del", "un", "una", "unos", "unas", "y", "o", "a", "al", "en", "por", "para", "con", "mi", "mis",
            "me", "se", "lo", "le", "su", "sus", "es", "son", "ser", "hay", "tengo", "tienen", "tiene", "quiero", "quisiera", "necesito", "puedo",
            "pueden", "podria", "podrian", "favor", "porfa", "porfavor", "hola", "que", "este", "esta", "esto", "eso", "ese", "muy", "mas", "ustedes",
            "usted", "tu", "te", "yo", "nos", "sobre", "saber", "decir", "dime", "explicame", "significa", "significado", "seria", "algo"
        };
        // En las palabras clave solo se ignoran artículos y preposiciones ("documentos necesito" exige ambas).
        private static readonly HashSet<string> VaciasClave = new() { "el", "la", "los", "las", "de", "del", "un", "una", "y", "o", "a", "al", "en", "por", "para", "con", "mi", "mis", "me", "se", "lo", "que", "es", "son", "ustedes",
            // inglés (temas en inglés): "what is transshipment" exige solo "transshipment"
            "the", "an", "of", "to", "in", "on", "for", "is", "are", "do", "does", "you", "your", "i", "my", "what", "how", "it", "with", "and", "can", "be", "s" };
        // Palabras que no se corrigen por parecido (cuando ≠ cuanto, donde ≠ ...).
        private static readonly HashSet<string> SinParecidos = new() { "cuando", "cuanto", "cuanta", "cuantos", "donde", "desde", "hasta", "hacia", "puedo", "pueden" };
        private static readonly HashSet<string> Genericas = new() { "ayuda", "ayudame", "servicios", "datos", "contenedor", "carga", "transporte", "documentos", "servicio", "sistema", "valor", "costo", "planta", "deposito", "entrar", "clave", "estado" };

        public static string Norm(string s)
        {
            var d = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) || c == '&' ? c : ' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }
        private static bool Hay(string n, params string[] frases)
        {
            string t = " " + n + " ";
            return frases.Any(f => t.Contains(" " + f + " ") || (f.Length >= 5 && t.Contains(" " + f)));
        }

        private static int Dl(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int c = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + c);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            return d[a.Length, b.Length];
        }

        /// <summary>Qué tan bien un token del mensaje coincide con un token de una palabra clave (0 a 1).</summary>
        private static double Coincide(string t, string k)
        {
            if (t == k) return 1;
            if (t.Length <= 3 || k.Length <= 3) return (t == k + "s" || k == t + "s") ? 1 : 0;
            if (t == k + "s" || t == k + "es" || k == t + "s" || k == t + "es") return 1;
            if (t.Length >= 6 && k.Length >= 6 && t[..6] == k[..6]) return 0.9;
            if (t.Length >= 6 && k.Length >= 5 && t[..5] == k[..5]) return 0.85;
            if (t.Length >= 5 && k.Length >= 5 && (t.StartsWith(k) || k.StartsWith(t))) return 0.9;
            int min = Math.Min(t.Length, k.Length);
            if (min >= 5 && Math.Abs(t.Length - k.Length) <= 2 && !SinParecidos.Contains(t) && !SinParecidos.Contains(k))
            {
                int dist = Dl(t, k);
                if (dist <= 1 || (min >= 8 && dist <= 2)) return 0.75;
            }
            return 0;
        }

        private static double PuntajeClave(string clave, string[] tokens)
        {
            var ct = Norm(clave).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var contenido = ct.Where(x => !VaciasClave.Contains(x)).ToArray();
            if (contenido.Length == 0) contenido = ct;
            double factor = 1;
            foreach (var k in contenido)
            {
                double mejor = tokens.Length == 0 ? 0 : tokens.Max(t => Coincide(t, k));
                if (mejor <= 0) return 0;
                factor = Math.Min(factor, mejor);
            }
            double peso = contenido.Length == 1 ? (Genericas.Contains(contenido[0]) ? 0.7 : 1) : 1 + 0.6 * (contenido.Length - 1);
            return peso * factor;
        }

        private static string[] TokensContenido(string n)
        {
            var tok = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var c = tok.Where(t => !Vacias.Contains(t)).ToArray();
            return c.Length > 0 ? c : tok;
        }

        private static double PuntajeTema(TemaAsistente t, string n)
        {
            var tokens = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var puntajes = t.Claves.Select(c => PuntajeClave(c, tokens)).Where(p => p > 0).OrderByDescending(p => p).ToList();
            if (puntajes.Count == 0) return 0;
            return puntajes[0] + 0.25 * puntajes.Skip(1).Sum();
        }

        private static readonly HashSet<string> Prioritarios = new() { "cotizar", "solicitar", "persona", "registro", "clave", "molestia" };

        /// <summary>Si el mensaje trae otra pregunta (separada por "?" o por "y además"), su tema; si no, null.</summary>
        private TemaAsistente? OtraPregunta(string texto, TemaAsistente primero, IReadOnlyList<TemaAsistente> temas)
        {
            var partes = Regex.Split(texto, @"[?¿]|\by (?:también|tambien|además|ademas)\b|\band also\b", RegexOptions.IgnoreCase)
                .Select(x => Norm(x)).Where(x => x.Split(' ').Length >= 2).ToList();
            if (partes.Count < 2) return null;
            foreach (var parte in partes)
            {
                var (t, p, _) = MejorTema(parte, new EstadoAsistente(), temas);
                if (t != null && p >= 0.9 && t.Id != primero.Id && t.Grupo != "charla") return t;
            }
            return null;
        }

        private static TemaAsistente? Buscar(string id) => AsistenteConocimiento.Temas.FirstOrDefault(t => t.Id == id);

        private (TemaAsistente? Tema, double Puntaje, TemaAsistente? Segundo) MejorTema(string n, EstadoAsistente e, IReadOnlyList<TemaAsistente>? temas = null)
        {
            if (n.Length == 0) return (null, 0, null);
            temas ??= AsistenteConocimiento.Temas;
            int largo = TokensContenido(n).Length;
            var lista = temas
                .Where(t => t.Id != "saludo")
                .Select(t =>
                {
                    double p = PuntajeTema(t, n);
                    if (p > 0 && e.Grupo != null && t.Grupo == e.Grupo && largo <= 6) p += 0.3; // sigue el tema de la conversación
                    if (p > 0 && t.Grupo == "glosario") p = Hay(n, "que es", "que son", "que significa", "significado", "definicion", "explica", "what is", "what does", "meaning") ? p + 0.5 : p * 0.8;
                    return (t, p);
                })
                .Where(x => x.p > 0)
                .OrderByDescending(x => x.p).ThenByDescending(x => x.t.Grupo == "glosario" ? 1 : 0)
                .ToList();
            if (lista.Count == 0)
            {
                var s = temas.FirstOrDefault(t => t.Id == "saludo");
                return s != null && PuntajeTema(s, n) >= 1 ? (s, 1, null) : (null, 0, null);
            }
            // Lo que la persona quiere hacer (cotizar, hablar con alguien...) pesa más que un tema informativo.
            var accion = lista.FirstOrDefault(x => Prioritarios.Contains(x.t.Id) && x.p >= 1 && x.p >= 0.7 * lista[0].p);
            if (accion.t != null && accion.t != lista[0].t) { var resto = lista.Where(x => x.t != accion.t).ToList(); return (accion.t, accion.p, resto[0].t); }
            var segundo = lista.Count > 1 && lista[1].p >= 0.8 * lista[0].p && lista[1].p >= 1 ? lista[1].t : null;
            return (lista[0].t, lista[0].p, segundo);
        }

        // =====================================================================
        // Seguimiento de carga
        // =====================================================================
        private static string? CodigoSeguimiento(string texto)
        {
            var m = Regex.Match(texto.ToUpperInvariant(), @"\b([A-Z]{4}\d{7}|[A-Z]{0,4}\d{6,12}|[A-Z]{2,4}-?\d{4,10})\b");
            return m.Success ? m.Groups[1].Value.Replace("-", "") : null;
        }
        private static bool EsSeguimiento(string n) => Hay(n, "donde esta mi", "donde va mi", "rastrear", "rastreo", "seguimiento", "tracking", "estado de mi", "ubicar mi", "mi embarque", "mi contenedor", "mi booking", "mi carga");
        private Respuesta Seguimiento(string codigo, EstadoAsistente e)
        {
            e.Tema = "seguimiento"; e.Grupo = "portal"; e.Fallos = 0;
            return R($"Aquí tienes el acceso directo a {codigo} en el portal. Ahí ves la nave, las fechas, los hitos y los documentos (te pedirá iniciar sesión).", e,
                new() { new Accion("Ver " + codigo + " en el portal", "/PortalCliente/Index?SearchString=" + Uri.EscapeDataString(codigo), "bi-search"), A("registro") },
                new() { "¿Qué es un booking?", "Hablar con una persona" });
        }

        // =====================================================================
        // Dólar, UF y euro
        // =====================================================================
        private static bool EsIndicadores(string n) => Hay(n, "dolar", "dolares", "usd", "uf", "euro", "euros", "tipo de cambio", "indicadores", "valor del dolar", "unidad de fomento");
        private async Task<Respuesta> IndicadoresAsync(string n, EstadoAsistente e)
        {
            bool todos = Hay(n, "tipo de cambio", "indicadores") || !(Hay(n, "dolar", "dolares", "usd") || Hay(n, "uf", "unidad de fomento") || Hay(n, "euro", "euros"));
            var lineas = new List<string>();
            bool bancoCentral = true;
            async Task Linea(string nombre, Func<Task<IndicadorValor>> f)
            {
                var v = await f();
                if (v.EsRespaldo || v.Fuente != IndicadoresEconomicosService.FuenteBancoCentral) bancoCentral = false;
                lineas.Add($"• {nombre}: $ {v.Valor.ToString("N2", Cl)}" + (v.Fecha.HasValue ? $" ({v.Fecha.Value.ToString("d 'de' MMMM", Cl)})" : "") + (v.EsRespaldo ? " — último valor disponible" : ""));
            }
            if (todos || Hay(n, "dolar", "dolares", "usd")) await Linea("Dólar observado", _indicadores.ObtenerDolarAsync);
            if (todos || Hay(n, "uf", "unidad de fomento")) await Linea("UF", _indicadores.ObtenerUfAsync);
            if (todos || Hay(n, "euro", "euros")) await Linea("Euro", _indicadores.ObtenerEuroAsync);
            e.Tema = "indicadores"; e.Grupo = "empresa"; e.Fallos = 0;
            static string Frase(string l)
            {
                l = l.TrimStart('•', ' ').Replace(" (", " (valor del ");
                return l.StartsWith("UF") ? "La " + l.Replace(": $ ", " está en $") : "El " + char.ToLower(l[0]) + l[1..].Replace(": $ ", " está en $");
            }
            string texto = lineas.Count == 1
                ? Frase(lineas[0]) + (bancoCentral ? ", según el Banco Central" : "") + ". Es el que usamos para pasar a pesos los montos en moneda extranjera."
                : "Estos son los valores vigentes" + (bancoCentral ? " según el Banco Central" : "") + ":\n" + string.Join("\n", lineas) + "\nSon los que usamos para pasar a pesos los montos en moneda extranjera.";
            return R(texto, e, new(), new() { "Quiero cotizar", "¿Cuándo sale una nave?" });
        }

        // =====================================================================
        // Navieras e itinerarios (datos reales)
        // =====================================================================
        private static bool EsNavieras(string n) => Hay(n, "que navieras", "cuales navieras", "con que navieras", "navieras trabajan", "lineas navieras", "trabajan con msc", "trabajan con maersk", "navieras");
        private async Task<Respuesta> NavierasAsync(EstadoAsistente e)
        {
            e.Tema = "navieras"; e.Grupo = "maritimo"; e.Fallos = 0;
            var salidas = await ItinerariosHelper.ProximasSalidasAsync(_context, max: 500);
            var nombres = salidas.Where(s => s.IdNavieraNavigation != null).Select(s => s.IdNavieraNavigation!.NombreNaviera.Trim()).Distinct().OrderBy(x => x).ToList();
            if (nombres.Count == 0)
                nombres = await _context.NavierasActivas().ToListAsync();
            string lista = nombres.Count == 0 ? "las principales navieras que operan en Chile" : string.Join(", ", nombres.Take(10));
            return R($"Trabajamos con {lista}, entre otras. La elegimos contigo según la ruta, la fecha y la tarifa; en Itinerarios ves sus próximas salidas.", e,
                new() { A("itinerarios") }, new() { "Salidas a Shanghai", "Quiero cotizar" });
        }

        // Destinos con salidas publicadas, para sugerirlos como respuesta rápida.
        private async Task<List<string>> DestinosFrecuentesAsync()
        {
            try
            {
                var salidas = await ItinerariosHelper.ProximasSalidasAsync(_context, max: 500);
                var l = salidas.Where(s => s.IdPuertoDestinoNavigation != null).GroupBy(s => Titulo(s.IdPuertoDestinoNavigation!.NombrePuerto))
                    .OrderByDescending(g => g.Count()).Select(g => g.Key).Take(3).ToList();
                return l.Count > 0 ? l : new() { "Shanghai", "Rotterdam" };
            }
            catch { return new() { "Shanghai", "Rotterdam" }; }
        }

        private record PuertoDetectado(Puerto Puerto, string Nombre, int Posicion, string Previa);

        // Origen y destino según la preposición ("desde X", "a Y"); si no hay, un puerto
        // chileno es el origen (o el destino en una importación).
        private static (PuertoDetectado? Pol, PuertoDetectado? Pod) Roles(List<PuertoDetectado> ps, bool importacion = false)
        {
            PuertoDetectado? pol = null, pod = null;
            foreach (var p in ps)
            {
                if (p.Previa is "desde" or "de" or "del" or "origen" or "from" && pol == null) pol = p;
                else if (p.Previa is "a" or "hacia" or "para" or "hasta" or "rumbo" or "destino" or "to" or "for" or "towards" or "into" && pod == null) pod = p;
            }
            foreach (var p in ps.Where(p => p != pol && p != pod))
            {
                bool chile = PuertosHelper.EsChileno(p.Puerto);
                if ((chile != importacion) && pol == null) pol = p;
                else if (pod == null) pod = p;
                else if (pol == null) pol = p;
            }
            return (pol, pod);
        }
        private static readonly Dictionary<string, string> Alias = new()
        {
            ["valpo"] = "valparaiso", ["sai"] = "san antonio", ["san antonio chile"] = "san antonio", ["la"] = "los angeles", ["long beach"] = "long beach",
            ["hong kong"] = "hong kong", ["nueva york"] = "new york", ["filadelfia"] = "philadelphia", ["amberes"] = "antwerp", ["hamburgo"] = "hamburg",
            ["el callao"] = "callao", ["shangai"] = "shanghai", ["new york city"] = "new york", ["antwerpen"] = "antwerp"
        };

        private async Task<List<PuertoDetectado>> DetectarPuertosAsync(string n)
        {
            var puertos = await _cache.GetOrCreateAsync("asistente:puertos", async ce =>
            {
                ce.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                try { return await _context.Puertos.AsNoTracking().ToListAsync(); }
                catch { return new List<Puerto>(); }
            }) ?? new List<Puerto>();
            string t = " " + n + " ";
            foreach (var kv in Alias) t = t.Replace(" " + kv.Key + " ", " " + kv.Value + " ");
            var tokens = t.Trim().Split(' ');
            var encontrados = new List<PuertoDetectado>();
            foreach (var p in puertos)
            {
                string nombre = Norm(p.NombrePuerto ?? "");
                if (nombre.Length < 4) continue;
                int pos = t.IndexOf(" " + nombre + " ", StringComparison.Ordinal);
                if (pos < 0 && !nombre.Contains(' ') && nombre.Length >= 6)
                {
                    int i = Array.FindIndex(tokens, x => x.Length >= 5 && Dl(x, nombre) <= 1);
                    if (i >= 0) pos = string.Join(" ", tokens.Take(i)).Length;
                }
                if (pos >= 0)
                {
                    var antes = t[..pos].Trim().Split(' ');
                    encontrados.Add(new PuertoDetectado(p, Titulo(p.NombrePuerto!), pos, antes.Length > 0 ? antes[^1] : ""));
                }
            }
            return encontrados.GroupBy(x => PuertosHelper.Clave(x.Puerto)).Select(g => g.First()).OrderBy(x => x.Posicion).ToList();
        }

        private async Task<Respuesta> ItinerariosAsync(List<PuertoDetectado> puertos, string n, EstadoAsistente e, bool enfoqueTransito)
        {
            e.Tema = "itinerarios"; e.Grupo = "maritimo"; e.Fallos = 0;
            var (pol, pod) = Roles(puertos, DetectarTipo(n) == "Importación");

            var salidas = await ItinerariosHelper.ProximasSalidasAsync(_context, max: 500);
            string? cPol = pol == null ? null : PuertosHelper.Clave(pol.Puerto), cPod = pod == null ? null : PuertosHelper.Clave(pod.Puerto);
            var lista = salidas
                .Where(s => cPol == null || (s.IdPuertoOrigenNavigation != null && PuertosHelper.Clave(s.IdPuertoOrigenNavigation) == cPol))
                .Where(s => cPod == null || (s.IdPuertoDestinoNavigation != null && PuertosHelper.Clave(s.IdPuertoDestinoNavigation) == cPod))
                .OrderBy(s => s.Etd).ToList();
            string ruta = (pol != null ? "desde " + pol.Nombre : "") + (pol != null && pod != null ? " " : "") + (pod != null ? "a " + pod.Nombre : "");
            string url = "/Itinerarios?" + string.Join("&", new[] { pol != null ? "pol=" + pol.Puerto.Id : null, pod != null ? "pod=" + pod.Puerto.Id : null }.Where(x => x != null));

            if (lista.Count == 0)
            {
                e.Datos.Clear();
                if (pol != null) e.Datos["origen"] = pol.Nombre;
                if (pod != null) e.Datos["destino"] = pod.Nombre;
                return R($"Por ahora no tengo salidas publicadas {ruta} en las próximas {ItinerarioNave.SemanasHorizonte} semanas, pero igual podemos cotizarla: un ejecutivo te propone naves y fechas.", e,
                    new() { A("itinerarios"), A("formulario") }, new() { "Sí, armemos la cotización", "Ver todas las salidas" });
            }
            var sb = new StringBuilder();
            if (enfoqueTransito)
            {
                int min = lista.Min(s => s.DiasTransito), max = lista.Max(s => s.DiasTransito);
                sb.Append($"Según las salidas publicadas {ruta}, el viaje toma {(min == max ? min + " días" : $"entre {min} y {max} días")}. Estas son las próximas:\n");
            }
            else sb.Append($"Estas son las próximas salidas {ruta}:\n");
            foreach (var s in lista.Take(4))
            {
                string nav = s.IdNavieraNavigation?.NombreNaviera ?? "Naviera";
                sb.Append($"• {nav} · {s.NaveViaje}: zarpa el {s.Etd.ToString("ddd d MMM", Cl)}");
                if (cPol == null) sb.Append($" de {Titulo(s.IdPuertoOrigenNavigation?.NombrePuerto)}");
                sb.Append($", llega el {s.Eta.ToString("ddd d MMM", Cl)}");
                if (cPod == null) sb.Append($" a {Titulo(s.IdPuertoDestinoNavigation?.NombrePuerto)}");
                sb.Append($" ({s.DiasTransito} días{(string.IsNullOrWhiteSpace(s.Transbordo) ? ", directo" : ", vía " + s.Transbordo)})\n");
            }
            if (lista.Count > 4) sb.Append($"…y {lista.Count - 4} más en Itinerarios.\n");
            sb.Append("Las fechas las informa cada naviera y pueden moverse un poco. ¿Quieres que armemos la cotización?");
            e.Datos.Clear();
            if (pol != null) e.Datos["origen"] = pol.Nombre;
            if (pod != null) e.Datos["destino"] = pod.Nombre;
            return R(sb.ToString(), e,
                new() { new Accion("Ver estas salidas", url, "bi-calendar2-week"), A("solicitar") },
                new() { "Sí, armemos la cotización", "¿Qué es un transbordo?" });
        }

        // =====================================================================
        // Cotización guiada
        // =====================================================================
        private static bool EsArmarCotizacion(string n) => Hay(n, "armar la cotizacion", "armemos la cotizacion", "armar cotizacion", "cotizar aqui", "cotizacion aqui", "si armemos", "cotizar por aca", "cotizar por el chat");
        private static readonly string[] Contenedores = { "20' dry", "40' dry", "40' high cube", "20' reefer", "40' reefer" };

        private static string? DetectarContenedor(string n)
        {
            bool reefer = Hay(n, "reefer", "refrigerado", "refrigerada", "frio"), hc = Hay(n, "high cube", "hc", "40hc"), dry = Hay(n, "dry", "seco", "estandar");
            bool v20 = Regex.IsMatch(n, @"\b20\b|\b20 ?pies\b|\b20dv\b"), v40 = Regex.IsMatch(n, @"\b40\b|\b40 ?pies\b|\b40hc\b|\b40dv\b");
            if (hc) return "40' high cube";
            if (v20 && reefer) return "20' reefer";
            if (v40 && reefer) return "40' reefer";
            if (v20 && (dry || !reefer)) return "20' dry";
            if (v40) return "40' dry";
            if (reefer) return "40' reefer";
            return null;
        }
        private static string? DetectarTipo(string n) =>
            Hay(n, "importar", "importacion", "traer", "importo") ? "Importación" : Hay(n, "exportar", "exportacion", "exporto", "enviar a", "mandar a") ? "Exportación" : null;

        private async Task<Respuesta> IniciarCotizacionAsync(string texto, string n, EstadoAsistente e)
        {
            var previos = new Dictionary<string, string>(e.Datos);
            e.Flujo = "cotizacion"; e.Paso = 0; e.Tema = "cotizar"; e.Grupo = "portal"; e.Fallos = 0; e.Esperando = null;
            e.Datos = previos.Where(kv => kv.Key is "origen" or "destino").ToDictionary(kv => kv.Key, kv => kv.Value);
            await Prellenar(n, e);
            return Preguntar(e, "¡Buenísimo! Te hago unas preguntas cortas (si cambias de idea, escribe «cancelar»).\n\n");
        }

        private async Task Prellenar(string n, EstadoAsistente e)
        {
            if (!e.Datos.ContainsKey("tipo") && DetectarTipo(n) is string tipo) e.Datos["tipo"] = tipo;
            if (!e.Datos.ContainsKey("contenedor") && DetectarContenedor(n) is string cont) e.Datos["contenedor"] = cont;
            var puertos = await DetectarPuertosAsync(n);
            if (puertos.Count > 0)
            {
                var (pol, pod) = Roles(puertos, e.Datos.GetValueOrDefault("tipo") == "Importación");
                if (pol != null) e.Datos.TryAdd("origen", pol.Nombre);
                if (pod != null) e.Datos.TryAdd("destino", pod.Nombre);
                if (!e.Datos.ContainsKey("tipo") && pol != null && PuertosHelper.EsChileno(pol.Puerto)) e.Datos["tipo"] = "Exportación";
                if (!e.Datos.ContainsKey("tipo") && pod != null && PuertosHelper.EsChileno(pod.Puerto) && pol != null) e.Datos["tipo"] = "Importación";
            }
        }

        private static readonly string[] OrdenPasos = { "tipo", "origen", "destino", "contenedor", "carga", "fecha", "correo" };

        private Respuesta Preguntar(EstadoAsistente e, string prefijo = "")
        {
            string? falta = OrdenPasos.FirstOrDefault(p => !e.Datos.ContainsKey(p));
            bool imp = e.Datos.GetValueOrDefault("tipo") == "Importación";
            if (falta == null)
            {
                e.Paso = 7;
                var d = e.Datos;
                string resumen = $"Perfecto, te resumo:\n• {d["tipo"]}: {d["origen"]} → {d["destino"]}\n• Contenedor: {d["contenedor"]}\n• Carga: {d["carga"]}\n• Embarque: {d["fecha"]}\n• Contacto: {d["correo"]}{(d.TryGetValue("contacto", out var c) ? " · " + c : "")}\n¿Lo envío al equipo comercial?";
                return R(prefijo + resumen, e, new(), new() { "Enviar solicitud", "Corregir datos", "Cancelar" });
            }
            e.Paso = Array.IndexOf(OrdenPasos, falta);
            return falta switch
            {
                "tipo" => R(prefijo + "Primero, ¿es una exportación o una importación?", e, new(), new() { "Exportación", "Importación" }),
                "origen" => R(prefijo + (imp ? "¿Desde qué puerto o país viene?" : "¿Desde qué puerto chileno sale?"), e, new(), imp ? new() { "Shanghai", "Ningbo", "Busan" } : new() { "San Antonio", "Valparaíso", "Lirquén", "Coronel" }),
                "destino" => R(prefijo + (imp ? "¿Y a qué puerto chileno llega?" : "¿Y a qué puerto o país va?"), e, new(), imp ? new() { "San Antonio", "Valparaíso" } : new() { "Shanghai", "Rotterdam", "Ningbo" }),
                "contenedor" => R(prefijo + "¿Qué tipo de contenedor necesitas?", e, new(), Contenedores.Append("No estoy seguro").ToList()),
                "carga" => R(prefijo + "¿Qué vas a enviar y cuántos contenedores, más o menos? (por ejemplo: cerezas, 4 contenedores)", e, new(), new()),
                "fecha" => R(prefijo + "¿Para cuándo lo necesitas?", e, new(), new() { "Esta semana", "Este mes", "El próximo mes", "Aún no sé" }),
                _ => R(prefijo + "Por último, ¿a qué correo te enviamos la cotización? Si quieres, agrega tu nombre y empresa (por ejemplo: ana@empresa.cl, Ana Pérez, Frutícola Sur).", e, new(), new())
            };
        }

        private async Task<Respuesta> PasoCotizacionAsync(string texto, string n, EstadoAsistente e, string? ip)
        {
            if (Hay(n, "cancelar", "cancela", "salir", "olvidalo", "no gracias", "detener", "ya no", "dejalo"))
            {
                e.Flujo = null; e.Datos.Clear(); e.Paso = 0;
                return R("Listo, la dejamos ahí. ¿Te ayudo con otra cosa?", e, new(), AsistenteConocimiento.SugerenciasInicio.ToList());
            }
            string? paso = e.Paso >= 0 && e.Paso < OrdenPasos.Length ? OrdenPasos[e.Paso] : null;

            // Una pregunta en medio del flujo: se responde y se retoma.
            if (texto.Contains('?') && paso is not ("correo") && e.Paso != 7)
            {
                var (tema, p, _) = MejorTema(n, new EstadoAsistente());
                if (tema != null && p >= 1.2 && tema.Grupo != "charla")
                {
                    var r = DesdeTema(tema, new EstadoAsistente());
                    var sigue = Preguntar(e);
                    return R(r.Texto + "\n\nVolviendo a tu cotización: " + sigue.Texto, e, r.Acciones.Cast<Accion?>().ToList(), sigue.Sugerencias);
                }
            }

            if (e.Paso == 7)
            {
                if (Hay(n, "corregir", "cambiar", "editar", "modificar"))
                {
                    var correo = e.Datos.GetValueOrDefault("correo"); var contacto = e.Datos.GetValueOrDefault("contacto");
                    e.Datos.Clear();
                    if (correo != null) e.Datos["correo"] = correo;
                    if (contacto != null) e.Datos["contacto"] = contacto;
                    return Preguntar(e, "Sin problema, partamos de nuevo.\n\n");
                }
                if (Hay(n, "enviar", "envia", "si", "dale", "ok", "confirmo", "listo", "correcto", "perfecto")) return await EnviarCotizacionAsync(e, ip);
                return Preguntar(e);
            }

            string valor = texto.Length > 150 ? texto[..150] : texto;
            switch (paso)
            {
                case "tipo":
                    var tipo = DetectarTipo(n) ?? (Hay(n, "expo") ? "Exportación" : Hay(n, "impo") ? "Importación" : null);
                    if (tipo == null) return Preguntar(e, "Perdona, no me quedó claro. ");
                    e.Datos["tipo"] = tipo;
                    await Prellenar(n, e);
                    break;
                case "origen":
                case "destino":
                    var ps = await DetectarPuertosAsync(n);
                    e.Datos[paso] = ps.Count > 0 ? ps[0].Nombre : Capitalizar(valor);
                    if (ps.Count > 1 && paso == "origen") e.Datos.TryAdd("destino", ps[1].Nombre);
                    if (!e.Datos.ContainsKey("contenedor") && DetectarContenedor(n) is string c1) e.Datos["contenedor"] = c1;
                    break;
                case "contenedor":
                    e.Datos["contenedor"] = DetectarContenedor(n) ?? (Hay(n, "no se", "no estoy seguro", "no lo se", "ni idea", "por definir") ? "Por definir con el ejecutivo" : Capitalizar(valor));
                    break;
                case "carga":
                    if (valor.Length < 2) return Preguntar(e, "Cuéntame un poco de la carga. ");
                    e.Datos["carga"] = Capitalizar(valor);
                    break;
                case "fecha":
                    e.Datos["fecha"] = Capitalizar(valor);
                    break;
                case "correo":
                    var m = Regex.Match(texto, @"[^@\s,;<>]+@[^@\s,;<>]+\.[A-Za-z]{2,}");
                    if (!m.Success) return Preguntar(e, "Necesito un correo válido para responderte, por ejemplo nombre@empresa.cl. ");
                    e.Datos["correo"] = m.Value.ToLowerInvariant();
                    string resto = Regex.Replace(texto.Replace(m.Value, ""), @"^[\s,;:\-]+|[\s,;:\-]+$", "").Trim();
                    resto = Regex.Replace(resto, @"\s*[,;]\s*", " · ");
                    if (resto.Length > 1) e.Datos["contacto"] = resto.Length > 120 ? resto[..120] : resto;
                    break;
            }
            return Preguntar(e);
        }

        private static string Titulo(string? s) => string.IsNullOrWhiteSpace(s) ? "" : Cl.TextInfo.ToTitleCase(s.Trim().ToLower(Cl));
        private static string Capitalizar(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

        private async Task<Respuesta> EnviarCotizacionAsync(EstadoAsistente e, string? ip)
        {
            var d = e.Datos;
            string clave = "asistente-cotiz:" + (ip ?? "?");
            int enviadas = _cache.TryGetValue(clave, out int k) ? k : 0;
            if (enviadas >= 3)
            {
                e.Flujo = null;
                return R($"Ya recibimos varias solicitudes desde tu conexión. Para esta, escríbenos a {Correo} y te respondemos a la brevedad.", e, new() { A("formulario") }, new());
            }
            string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
            string fila(string k2, string v) => $"<tr><td style=\"padding:6px 12px 6px 0;color:#5d5872\">{k2}</td><td style=\"padding:6px 0;font-weight:600\">{Enc(v)}</td></tr>";
            string cuerpo = "<p>Llegó una solicitud de cotización desde el asistente del sitio web.</p><table style=\"border-collapse:collapse;font-size:14px\">"
                + fila("Operación", d["tipo"]) + fila("Origen", d["origen"]) + fila("Destino", d["destino"]) + fila("Contenedor", d["contenedor"])
                + fila("Carga", d["carga"]) + fila("Embarque", d["fecha"]) + fila("Correo", d["correo"]) + (d.TryGetValue("contacto", out var c) ? fila("Contacto", c) : "")
                + "</table><p>Responde directamente al correo del cliente.</p>";
            string destino = Conf("SitioWeb:CorreoFormulario") ?? Conf("PortalRegistro:CorreoAviso") ?? Conf("Smtp:Sender") ?? Correo;
            e.Flujo = null; e.Paso = 0;
            try
            {
                await _email.EnviarCorreoAsync(destino, $"Cotización web: {d["origen"]} → {d["destino"]} ({d["contenedor"]})",
                    RegistroClientesService.Plantilla("Nueva solicitud de cotización", cuerpo, encabezado: "ATILSON CARGO · SITIO WEB"));
                _cache.Set(clave, enviadas + 1, TimeSpan.FromHours(1));
                string correo = d["correo"];
                e.Datos.Clear();
                return R($"¡Listo! Ya le llegó tu solicitud al equipo comercial; te escribirán a {correo} a la brevedad. ¿Te ayudo con algo más?", e,
                    new() { A("registro") }, new() { "¿Qué documentos necesito para exportar?", "Ver itinerarios", "No, eso es todo" });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo enviar la cotización del asistente");
                string texto = $"Solicitud de cotización\n{d["tipo"]}: {d["origen"]} -> {d["destino"]}\nContenedor: {d["contenedor"]}\nCarga: {d["carga"]}\nEmbarque: {d["fecha"]}\nContacto: {d["correo"]} {d.GetValueOrDefault("contacto")}";
                return R($"No pude enviarla desde aquí. Usa el botón para mandarla por correo a {Correo} con todos los datos ya escritos.", e,
                    new() { new Accion("Enviar por correo", $"mailto:{Correo}?subject={Uri.EscapeDataString("Solicitud de cotización")}&body={Uri.EscapeDataString(texto)}", "bi-envelope") }, new());
            }
        }

        // =====================================================================
        // IA (solo si hay clave configurada)
        // =====================================================================
        private string Sistema()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Eres el asistente virtual del sitio web de Atilson Cargo SpA, freight forwarder con oficina en A. Prat 814 Of. 503, Valparaíso, Chile.");
            sb.AppendLine("Respondes en español de Chile, cordial y profesional, en 1 a 4 frases cortas. Sin markdown.");
            sb.AppendLine("Servicios: marítimo FCL dry y reefer (exportación e importación, prepaid o collect), terrestre (depósito, planta y puerto), documental (BL, matrices, certificados), agencia de aduanas (DUS, DIN, SAG, junto a agentes de aduana habilitados) y portal de clientes 24/7.");
            sb.AppendLine("No inventes tarifas, tiempos ni fechas; deriva a cotizar en el portal o al formulario. No tienes acceso a datos de clientes. Contacto: " + Correo + ".");
            return sb.ToString();
        }

        private async Task<string?> PreguntarIaAsync(List<Mensaje> historial, CancellationToken ct)
        {
            try
            {
                var msgs = new List<object>();
                string? rolPrevio = null;
                foreach (var m in historial.SkipWhile(m => m.Rol != "user"))
                {
                    string rol = m.Rol == "user" ? "user" : "assistant";
                    if (rol == rolPrevio) continue;
                    msgs.Add(new { role = rol, content = m.Texto });
                    rolPrevio = rol;
                }
                if (msgs.Count == 0 || rolPrevio != "user") return null;
                var client = _http.CreateClient(ClienteHttp);
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                req.Headers.Add("x-api-key", Conf("AsistenteIA:ApiKey"));
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Content = JsonContent.Create(new { model = Conf("AsistenteIA:Modelo") ?? "claude-haiku-5-5", max_tokens = 400, system = Sistema(), messages = msgs });
                using var res = await client.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) return null;
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                var sb = new StringBuilder();
                foreach (var b in doc.RootElement.GetProperty("content").EnumerateArray())
                    if (b.GetProperty("type").GetString() == "text") sb.Append(b.GetProperty("text").GetString());
                return sb.ToString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "El asistente IA no respondió.");
                return null;
            }
        }
    }

    internal static class AsistenteConsultas
    {
        // Navieras activas (si aún no hay itinerarios publicados).
        public static IQueryable<string> NavierasActivas(this AtilsonContext ctx) =>
            ctx.Navieras.AsNoTracking().Where(n => n.Activo == 1).OrderBy(n => n.NombreNaviera).Select(n => n.NombreNaviera).Take(10);
    }
}
