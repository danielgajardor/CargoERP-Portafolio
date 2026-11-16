using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    // =====================================================================================
    // Avisos del portal de clientes (15-nov-2026).
    // Se crean solos al guardar (NotificacionesClienteInterceptor): no hay que llamar nada desde
    // las pantallas. Aquí está el catálogo de tipos, las consultas del portal (campana, página
    // de avisos, marcar leídos), las preferencias de correo y el armado del resumen por correo.
    // =====================================================================================
    public class NotificacionesClienteService
    {
        // Tipos de aviso (columna Tipo). El orden es el de los filtros y las preferencias.
        public const string TFechas = "FECHAS", TDocumento = "DOCUMENTO", TAccion = "ACCION", TSeguimiento = "SEGUIMIENTO",
                            TSoporte = "SOPORTE", TSolicitud = "SOLICITUD", TFactura = "FACTURA";

        public sealed record TipoAviso(string Codigo, string Nombre, string Descripcion, string Icono, string Color, bool PorCorreo);

        public static readonly TipoAviso[] Tipos =
        {
            new(TFechas, "Fechas de la nave", "Stacking, corte documental, ETD, ETA o cambio de nave de tus bookings.", "bi-calendar-event", "#6d2d9e", true),
            new(TDocumento, "Documentos nuevos", "BL, matriz, certificados, packing list y otros documentos disponibles.", "bi-file-earmark-arrow-down", "#2563eb", true),
            new(TAccion, "Requiere tu revisión", "Borradores de BL u otros documentos que esperan tu aprobación.", "bi-exclamation-circle", "#d97706", true),
            new(TSeguimiento, "Seguimiento de la carga", "Avance del booking y de cada contenedor: planta, puerto, embarque, zarpe, arribo, retiro y devolución.", "bi-geo-alt", "#059669", true),
            new(TSoporte, "Respuestas de soporte", "Respuestas del equipo a tus consultas (el correo lo envía Soporte).", "bi-headset", "#7c3aed", false),
            new(TSolicitud, "Cotizaciones y servicios", "Cotizaciones respondidas, bookings nuevos y solicitudes de roleo o cancelación atendidas.", "bi-chat-square-text", "#0891b2", true),
            new(TFactura, "Facturas", "Facturas emitidas para tus operaciones.", "bi-receipt", "#ea580c", true),
        };

        public static TipoAviso Tipo(string? codigo) => Tipos.FirstOrDefault(t => t.Codigo == codigo) ?? Tipos[0];

        // Origen público para los enlaces de los correos: SitioWeb:UrlPublica (https) o el último
        // origen visto en una petición (lo anota el interceptor).
        public static string? OrigenConocido { get; set; }

        private readonly AtilsonContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<NotificacionesClienteService> _log;

        public NotificacionesClienteService(AtilsonContext db, IConfiguration config, ILogger<NotificacionesClienteService> log)
        {
            _db = db; _config = config; _log = log;
        }

        // Sin el script SQL las pantallas no fallan: no hay avisos.
        private async Task<T> Seguro<T>(Func<Task<T>> f, T porDefecto)
        {
            try { return await f(); }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException || ex.InnerException is Microsoft.Data.SqlClient.SqlException)
            {
                _log.LogWarning("Avisos del portal no disponibles (¿falta Sql/2026-11_notificaciones_portal.sql?): {m}", ex.Message);
                return porDefecto;
            }
        }

        public Task<int> ContarNoLeidasAsync(int idCliente) =>
            Seguro(() => _db.NotificacionesCliente.AsNoTracking().CountAsync(n => n.IdCliente == idCliente && n.FechaLeida == null), 0);

        public Task<List<NotificacionCliente>> ListarAsync(int idCliente, int cantidad = 15, string? tipo = null, bool soloNoLeidas = false, int saltar = 0) =>
            Seguro(async () =>
            {
                var q = _db.NotificacionesCliente.AsNoTracking().Where(n => n.IdCliente == idCliente);
                if (!string.IsNullOrWhiteSpace(tipo)) q = q.Where(n => n.Tipo == tipo);
                if (soloNoLeidas) q = q.Where(n => n.FechaLeida == null);
                return await q.OrderByDescending(n => n.Fecha).ThenByDescending(n => n.Id).Skip(saltar).Take(cantidad).ToListAsync();
            }, new List<NotificacionCliente>());

        public Task<Dictionary<string, int>> ContarPorTipoAsync(int idCliente) =>
            Seguro(async () => await _db.NotificacionesCliente.AsNoTracking()
                .Where(n => n.IdCliente == idCliente && n.FechaLeida == null)
                .GroupBy(n => n.Tipo).Select(g => new { g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.N), new Dictionary<string, int>());

        public Task<NotificacionCliente?> MarcarLeidaAsync(int idCliente, int id) =>
            Seguro(async () =>
            {
                var n = await _db.NotificacionesCliente.FirstOrDefaultAsync(x => x.Id == id && x.IdCliente == idCliente);
                if (n != null && n.FechaLeida == null) { n.FechaLeida = DateTime.Now; await _db.SaveChangesAsync(); }
                return n;
            }, (NotificacionCliente?)null);

        public Task<int> MarcarTodasAsync(int idCliente, string? tipo = null) =>
            Seguro(async () =>
            {
                var q = _db.NotificacionesCliente.Where(n => n.IdCliente == idCliente && n.FechaLeida == null);
                if (!string.IsNullOrWhiteSpace(tipo)) q = q.Where(n => n.Tipo == tipo);
                var lista = await q.ToListAsync();
                foreach (var n in lista) n.FechaLeida = DateTime.Now;
                await _db.SaveChangesAsync();
                return lista.Count;
            }, 0);

        // ---------------------------------------------------------------------------------
        // Preferencias de correo (por usuario del portal)
        // ---------------------------------------------------------------------------------
        public static HashSet<string> TiposPorDefecto() => Tipos.Where(t => t.PorCorreo).Select(t => t.Codigo).ToHashSet();

        public static HashSet<string> TiposDe(PreferenciaAvisoCliente? p) =>
            p == null ? TiposPorDefecto() : p.TiposCorreo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

        public Task<HashSet<string>> TiposCorreoAsync(string correo) =>
            Seguro(async () => TiposDe(await _db.PreferenciasAvisoCliente.AsNoTracking().FirstOrDefaultAsync(p => p.Correo == correo)), TiposPorDefecto());

        public Task<bool> GuardarTiposCorreoAsync(string correo, IEnumerable<string> tipos) =>
            Seguro(async () =>
            {
                var validos = tipos.Where(t => Tipos.Any(x => x.Codigo == t && x.PorCorreo)).Distinct().ToList();
                var p = await _db.PreferenciasAvisoCliente.FirstOrDefaultAsync(x => x.Correo == correo);
                if (p == null) { p = new PreferenciaAvisoCliente { Correo = correo }; _db.PreferenciasAvisoCliente.Add(p); }
                p.TiposCorreo = string.Join(",", validos);
                p.FechaModificacion = DateTime.Now;
                await _db.SaveChangesAsync();
                return true;
            }, false);

        // ---------------------------------------------------------------------------------
        // Presentación
        // ---------------------------------------------------------------------------------
        public static string Hace(DateTime fecha, DateTime ahora)
        {
            var d = ahora - fecha;
            if (d.TotalMinutes < 1) return "recién";
            if (d.TotalMinutes < 60) return $"hace {(int)d.TotalMinutes} min";
            if (d.TotalHours < 24) return $"hace {(int)d.TotalHours} h";
            if (d.TotalDays < 2) return "ayer " + fecha.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (d.TotalDays < 7) return $"hace {(int)d.TotalDays} días";
            return fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        public string EnlaceAbsoluto(string? url)
        {
            url = string.IsNullOrWhiteSpace(url) ? "/PortalCliente/Notificaciones" : url;
            string? publica = _config["SitioWeb:UrlPublica"];
            if (Uri.TryCreate(publica, UriKind.Absolute, out var b) && b.Scheme == Uri.UriSchemeHttps && !b.IsLoopback)
                return new Uri(b, url).AbsoluteUri;
            if (Uri.TryCreate(OrigenConocido, UriKind.Absolute, out var o)) return new Uri(o, url).AbsoluteUri;
            return url;
        }

        private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

        /// <summary>Correo con el resumen de avisos nuevos para un usuario del portal.</summary>
        public (string Asunto, string Html) CorreoResumen(string nombre, IReadOnlyList<NotificacionCliente> avisos)
        {
            string asunto = avisos.Count == 1
                ? $"[ATILSON CARGO] {avisos[0].Titulo}"
                : $"[ATILSON CARGO] {avisos.Count} novedades de tus operaciones";
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:0 auto;color:#1f1633;\">");
            sb.Append("<div style=\"background:#2c1b4d;color:#fff;padding:18px 22px;border-radius:10px 10px 0 0;\"><div style=\"font-size:12px;letter-spacing:1px;text-transform:uppercase;opacity:.8;\">Portal de clientes</div>");
            sb.Append($"<div style=\"font-size:19px;font-weight:700;margin-top:4px;\">Hola {H(nombre)}, hay novedades en tus operaciones</div></div>");
            sb.Append("<div style=\"border:1px solid #e7e2f1;border-top:0;border-radius:0 0 10px 10px;padding:6px 22px 18px;\">");
            foreach (var n in avisos)
            {
                var t = Tipo(n.Tipo);
                sb.Append("<div style=\"padding:14px 0;border-bottom:1px solid #f0edf6;\">");
                sb.Append($"<div style=\"font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.6px;color:{t.Color};\">{H(t.Nombre)}</div>");
                sb.Append($"<div style=\"font-size:15px;font-weight:700;margin:3px 0 4px;\">{H(n.Titulo)}</div>");
                if (!string.IsNullOrWhiteSpace(n.Mensaje))
                    sb.Append($"<div style=\"font-size:13.5px;line-height:1.55;color:#4b4462;white-space:pre-line;\">{H(n.Mensaje)}</div>");
                sb.Append($"<div style=\"margin-top:8px;\"><a href=\"{H(EnlaceAbsoluto(n.Url))}\" style=\"color:#6d2d9e;font-weight:700;font-size:13px;text-decoration:none;\">Ver en el portal →</a></div>");
                sb.Append("</div>");
            }
            sb.Append($"<p style=\"font-size:12px;color:#8a83a0;margin-top:16px;\">Puedes elegir qué avisos recibir por correo en <a href=\"{H(EnlaceAbsoluto("/PortalCliente/Notificaciones#preferencias"))}\" style=\"color:#6d2d9e;\">Avisos › Preferencias</a>.</p>");
            sb.Append("</div></div>");
            return (asunto, sb.ToString());
        }
    }
}
