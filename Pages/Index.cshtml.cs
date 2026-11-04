using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Pages
{
    // Landing pública (oct-2026).
    // - ?handler=Noticias  → últimas noticias de Mundo Marítimo (JSON, ver NoticiasMaritimasService).
    // - ?handler=Imagen    → imagen de una noticia, servida por https desde aquí.
    // - POST ?handler=Contacto → formulario "Envíanos un mensaje" (correo al equipo comercial).
    // - POST ?handler=Asistente → chat del asistente (ver Services/AsistenteWebService.cs).
    // - ?handler=Indicadores → UF, dólar y euro del día para la barra superior (JSON).
    // La barra, la navegación, el pie y el asistente están en Pages/Shared/_LayoutPublico.cshtml.
    // Teléfono, WhatsApp y redes salen de appsettings, sección "SitioWeb" (si faltan, no se muestran).
    public class IndexModel : PageModel
    {
        private readonly NoticiasMaritimasService _noticias;
        private readonly EmailService _email;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;
        private readonly ILogger<IndexModel> _logger;
        private readonly AsistenteWebService _asistente;

        public IndexModel(NoticiasMaritimasService noticias, EmailService email, IConfiguration config, IMemoryCache cache, ILogger<IndexModel> logger, AsistenteWebService asistente)
        {
            _asistente = asistente;
            _noticias = noticias;
            _email = email;
            _config = config;
            _cache = cache;
            _logger = logger;
        }

        public string CorreoContacto => Config("SitioWeb:Correo") ?? "contacto@cargoflow-demo.cl";
        public string? Telefono => Config("SitioWeb:Telefono");
        public string? TelefonoEnlace => Telefono == null ? null : "tel:" + Regex.Replace(Telefono, @"[^\d+]", "");
        public string? WhatsApp => Config("SitioWeb:WhatsApp") is string w ? Regex.Replace(w, @"\D", "") : null;
        public string LinkedIn => Config("SitioWeb:LinkedIn") ?? "https://www.linkedin.com/";
        public string? Instagram => Config("SitioWeb:Instagram");
        public string? Facebook => Config("SitioWeb:Facebook");

        private string? Config(string clave) => string.IsNullOrWhiteSpace(_config[clave]) ? null : _config[clave]!.Trim();

        public void OnGet() { }

        public async Task<IActionResult> OnGetNoticiasAsync(CancellationToken ct)
        {
            var (lista, fuente) = await _noticias.ObtenerAsync(ct);
            var es = CultureInfo.GetCultureInfo("es-CL");
            Response.Headers.CacheControl = "public, max-age=600";
            return new JsonResult(new
            {
                fuente,
                noticias = lista.Select(n => new
                {
                    titulo = n.Titulo,
                    enlace = n.Enlace,
                    resumen = n.Resumen,
                    fecha = n.Fecha?.ToString("d 'de' MMMM, yyyy", es),
                    imagen = NoticiasMaritimasService.EsDeMundoMaritimo(n.Imagen)
                        ? Url.Page("/Index", "Imagen", new { u = n.Imagen })
                        : (n.Imagen != null && n.Imagen.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? n.Imagen : null)
                })
            });
        }

        // Barra superior del sitio: UF, dólar y euro del día, los mismos que usa Finanzas.
        public async Task<IActionResult> OnGetIndicadoresAsync([FromServices] IndicadoresEconomicosService indicadores)
        {
            object Dato(IndicadorValor v) => new { valor = v.Valor, fecha = v.Fecha?.ToString("dd-MM-yyyy"), fuente = v.Fuente, respaldo = v.EsRespaldo };
            var uf = await indicadores.ObtenerUfAsync();
            var dolar = await indicadores.ObtenerDolarAsync();
            var euro = await indicadores.ObtenerEuroAsync();
            Response.Headers.CacheControl = "public, max-age=600";
            return new JsonResult(new { uf = Dato(uf), dolar = Dato(dolar), euro = Dato(euro) });
        }

        public async Task<IActionResult> OnGetImagenAsync(string? u, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(u)) return NotFound();
            var img = await _noticias.ObtenerImagenAsync(u, ct);
            if (img == null) return NotFound();
            Response.Headers.CacheControl = "public, max-age=86400";
            return File(img.Value.Bytes, img.Value.Tipo);
        }

        public class FormContacto
        {
            public string? Nombre { get; set; }
            public string? Empresa { get; set; }
            public string? Correo { get; set; }
            public string? Mensaje { get; set; }
            public string? SitioWeb { get; set; } // trampa para robots: debe llegar vacío
        }

        public async Task<IActionResult> OnPostContactoAsync([FromForm] FormContacto f)
        {
            string nombre = (f.Nombre ?? "").Trim(), empresa = (f.Empresa ?? "").Trim(), correo = (f.Correo ?? "").Trim(), mensaje = (f.Mensaje ?? "").Trim();

            if (!string.IsNullOrEmpty(f.SitioWeb))
                return new JsonResult(new { ok = true, mensaje = "Recibimos tu mensaje. Te responderemos a la brevedad." });

            if (nombre.Length < 2 || nombre.Length > 100) return Error("Escribe tu nombre.", "Nombre");
            if (!Regex.IsMatch(correo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$") || correo.Length > 120) return Error("Revisa el correo: debe tener el formato nombre@empresa.cl.", "Correo");
            if (mensaje.Length < 10) return Error("Cuéntanos un poco más en el mensaje (al menos 10 caracteres).", "Mensaje");
            if (mensaje.Length > 3000) return Error("El mensaje es muy largo: resúmelo en menos de 3.000 caracteres.", "Mensaje");
            if (empresa.Length > 120) empresa = empresa.Substring(0, 120);

            // Máximo 5 mensajes por hora desde la misma conexión.
            string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
            string clave = "contacto:" + ip;
            int enviados = _cache.TryGetValue(clave, out int n) ? n : 0;
            if (enviados >= 5) return Error($"Ya recibimos varios mensajes desde tu conexión. Escríbenos directo a {CorreoContacto}.", null);

            string destino = Config("SitioWeb:CorreoFormulario") ?? Config("PortalRegistro:CorreoAviso") ?? Config("Smtp:Sender") ?? CorreoContacto;
            string Enc(string s) => WebUtility.HtmlEncode(s);
            string cuerpo =
                $"<p><b>{Enc(nombre)}</b>{(empresa.Length > 0 ? $" de <b>{Enc(empresa)}</b>" : "")} escribió desde el sitio web.</p>" +
                $"<p>Correo: <a href=\"mailto:{Enc(correo)}\">{Enc(correo)}</a></p>" +
                $"<blockquote style=\"margin:12px 0;padding:10px 14px;border-left:3px solid #443094;background:#f4f2fa;white-space:pre-wrap;\">{Enc(mensaje)}</blockquote>" +
                "<p>Responde directamente a ese correo.</p>";
            try
            {
                await _email.EnviarCorreoAsync(destino, $"Contacto web: {nombre}{(empresa.Length > 0 ? " · " + empresa : "")}",
                    RegistroClientesService.Plantilla("Nuevo mensaje desde el sitio web", cuerpo, encabezado: "ATILSON CARGO · SITIO WEB"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo enviar el formulario de contacto de {Correo}", correo);
                return new JsonResult(new
                {
                    ok = false,
                    sinCorreo = true,
                    mensaje = $"No pudimos enviar tu mensaje desde aquí. Escríbenos a {CorreoContacto} y te respondemos a la brevedad.",
                    mailto = $"mailto:{CorreoContacto}?subject={Uri.EscapeDataString("Consulta desde el sitio web")}&body={Uri.EscapeDataString(mensaje + "\n\n" + nombre + (empresa.Length > 0 ? " · " + empresa : "") + "\n" + correo)}"
                });
            }
            _cache.Set(clave, enviados + 1, TimeSpan.FromHours(1));
            return new JsonResult(new { ok = true, mensaje = $"Gracias, {nombre.Split(' ')[0]}. Recibimos tu mensaje y te responderemos a {correo}." });
        }

        private JsonResult Error(string mensaje, string? campo) => new(new { ok = false, mensaje, campo });

        public bool AsistenteConIa => _asistente.IaDisponible;

        private record MensajeChat(string? rol, string? texto);

        // Chat del asistente: recibe la conversación (últimos 12 mensajes) como JSON.
        public async Task<IActionResult> OnPostAsistenteAsync([FromForm] string? historial, [FromForm] string? estado, [FromForm] string? idioma, CancellationToken ct)
        {
            List<AsistenteWebService.Mensaje> mensajes;
            try
            {
                mensajes = (JsonSerializer.Deserialize<List<MensajeChat>>(historial ?? "[]") ?? new())
                    .Where(m => !string.IsNullOrWhiteSpace(m.texto))
                    .TakeLast(12)
                    .Select(m => new AsistenteWebService.Mensaje(m.rol == "user" ? "user" : "assistant", m.texto!.Trim().Length > 800 ? m.texto.Trim()[..800] : m.texto.Trim()))
                    .ToList();
            }
            catch (JsonException) { return BadRequest(); }
            if (!mensajes.Any(m => m.Rol == "user")) return BadRequest();

            AsistenteWebService.EstadoAsistente? memoria = null;
            if (!string.IsNullOrWhiteSpace(estado) && estado.Length <= 4000)
                try { memoria = JsonSerializer.Deserialize<AsistenteWebService.EstadoAsistente>(estado); } catch (JsonException) { }

            // Máximo 80 mensajes por hora desde la misma conexión.
            string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
            string clave = "asistente:" + ip;
            int usados = _cache.TryGetValue(clave, out int u) ? u : 0;
            if (usados >= 80 && idioma == "en")
                return new JsonResult(new { texto = $"We've reached the message limit for now. Email us at {CorreoContacto} or leave your details in the form and we'll reply.", acciones = new[] { new { texto = "Leave my details", url = "/#contacto", icono = "bi-envelope" } } });
            if (usados >= 80)
                return new JsonResult(new { texto = $"Llegamos al límite de mensajes por ahora. Escríbenos a {CorreoContacto} o déjanos tus datos en el formulario y te respondemos.", acciones = new[] { new { texto = "Dejar mis datos", url = "/#contacto", icono = "bi-envelope" } } });
            _cache.Set(clave, usados + 1, TimeSpan.FromHours(1));

            var r = await _asistente.ResponderAsync(mensajes, memoria, ip, ct, idioma == "en" ? "en" : "es");
            return new JsonResult(new
            {
                texto = r.Texto, ia = r.ConIa, sugerencias = r.Sugerencias,
                acciones = r.Acciones.Select(a => new { texto = a.Texto, url = a.Url, icono = a.Icono }),
                estado = r.Estado
            }, new JsonSerializerOptions());
        }
    }
}
