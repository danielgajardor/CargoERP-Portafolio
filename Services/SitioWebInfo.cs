using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace AtilsonCargoSpa.Services
{
    /// <summary>
    /// Datos de contacto del sitio público (sección "SitioWeb" de appsettings), para el
    /// layout público (_LayoutPublico): barra superior, pie, botón de WhatsApp.
    /// Lo que está vacío no se muestra.
    /// </summary>
    public class SitioWebInfo
    {
        private readonly IConfiguration _config;
        public SitioWebInfo(IConfiguration config) { _config = config; }

        private string? Conf(string k) => string.IsNullOrWhiteSpace(_config[k]) ? null : _config[k]!.Trim();

        public string CorreoContacto => Conf("SitioWeb:Correo") ?? "contacto@cargoflow-demo.cl";
        public string? Telefono => Conf("SitioWeb:Telefono");
        public string? TelefonoEnlace => Telefono == null ? null : "tel:" + Regex.Replace(Telefono, @"[^\d+]", "");
        public string? WhatsApp => Conf("SitioWeb:WhatsApp") is string w ? Regex.Replace(w, @"\D", "") : null;
        public string? LinkedIn => Conf("SitioWeb:LinkedIn");
        public string? Instagram => Conf("SitioWeb:Instagram");
        public string? Facebook => Conf("SitioWeb:Facebook");
        // En producción una red solo se muestra cuando existe una URL/cuenta real configurada.
        public string? InstagramUrl => Instagram;
        public string? FacebookUrl => Facebook;
        public string? WhatsAppUrl => WhatsApp != null ? "https://wa.me/" + WhatsApp : null;
        public bool AsistenteConIa => Conf("AsistenteIA:ApiKey") != null;

        // Video de la empresa en la landing (nov-2026), "SitioWeb:Video": un enlace de YouTube
        // (watch, youtu.be o shorts) o un archivo propio del sitio (/video/atilson.mp4 en wwwroot).
        public string? Video => Conf("SitioWeb:Video");
        public string? VideoYoutubeId => Video is string v && Regex.Match(v, @"(?:youtu\.be/|v=|/embed/|/shorts/)([A-Za-z0-9_-]{11})") is { Success: true } m ? m.Groups[1].Value
            : Video is string v2 && Regex.IsMatch(v2, @"^[A-Za-z0-9_-]{11}$") ? v2 : null;
        public string? VideoArchivo => Video is string v && Regex.IsMatch(v, @"\.(mp4|webm)(\?.*)?$", RegexOptions.IgnoreCase) ? v : null;
        public string VideoTitulo => Conf("SitioWeb:VideoTitulo") ?? "Conoce Atilson Cargo";
        // Imagen de portada opcional para el archivo propio (si no hay, se ve el primer cuadro).
        public string? VideoPortada => Conf("SitioWeb:VideoPortada");

        // Página "Nosotros" (nov-2026): false oculta el enlace del menú mientras se completa el contenido.
        public bool MostrarNosotros => !string.Equals(Conf("SitioWeb:MostrarNosotros"), "false", StringComparison.OrdinalIgnoreCase);
    }
}
