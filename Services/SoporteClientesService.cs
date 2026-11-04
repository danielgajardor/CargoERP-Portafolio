using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    /// <summary>Un mensaje listo para mostrar (el primero es el detalle de la consulta).</summary>
    public record MensajeVista(int Id, bool EsCliente, string Nombre, string Texto, DateTime Fecha);

    // ============================================================================
    // Consultas de soporte del portal (oct-2026) — conversación cliente ↔ equipo
    // ----------------------------------------------------------------------------
    // El cliente abre una consulta desde Portal › Inicio (Soporte operativo) o
    // Portal › Soporte. Desde ahí ambos siguen escribiendo en la misma conversación:
    //   - escribe el cliente  → ABIERTA (le toca responder al equipo)
    //   - escribe el equipo   → RESPONDIDA (correo al cliente)
    //   - "resuelta"/cerrar   → CERRADA; si el cliente vuelve a escribir, se reabre.
    // Tablas dbo.TicketsSoporte y dbo.MensajesSoporte (Sql/2026-10_perfiles_soporte.sql).
    // ============================================================================
    public class SoporteClientesService
    {
        public const int MaxTexto = 2000;
        private readonly AtilsonContext _context;
        private readonly RegistroClientesService _correos;
        private readonly ILogger<SoporteClientesService> _logger;

        public SoporteClientesService(AtilsonContext context, RegistroClientesService correos, ILogger<SoporteClientesService> logger)
        {
            _context = context;
            _correos = correos;
            _logger = logger;
        }

        // ---------------------------------------------------------------- lecturas
        public async Task<int> ContarAbiertosAsync()
        {
            try { return await _context.TicketsSoporte.AsNoTracking().CountAsync(t => t.Estado == TicketSoporte.EstadoAbierto); }
            catch (Exception ex) { _logger.LogDebug(ex, "Sin tabla TicketsSoporte"); return 0; }
        }

        /// <summary>Conversaciones del cliente con respuestas del equipo que aún no ha visto.</summary>
        public async Task<int> ContarNoLeidasClienteAsync(int idCliente)
        {
            try
            {
                return await _context.TicketsSoporte.AsNoTracking()
                    .CountAsync(t => t.IdCliente == idCliente && t.Estado == TicketSoporte.EstadoRespondido
                                     && (t.FechaVistaCliente == null || t.FechaRespuesta > t.FechaVistaCliente));
            }
            catch { return 0; }
        }

        public async Task<bool> TablaDisponibleAsync()
        {
            try
            {
                // Lee las columnas nuevas: si el script SQL se corrió en su versión anterior,
                // faltan y aquí se detecta (en vez de fallar al guardar la consulta).
                await _context.TicketsSoporte.AsNoTracking().Select(t => new { t.Id, t.UltimoMensaje, t.FechaVistaCliente }).FirstOrDefaultAsync();
                await _context.MensajesSoporte.AsNoTracking().AnyAsync();
                return true;
            }
            catch { return false; }
        }

        public async Task<List<TicketSoporte>> DelClienteAsync(int idCliente)
        {
            try
            {
                return await _context.TicketsSoporte.AsNoTracking().Where(t => t.IdCliente == idCliente)
                    .OrderByDescending(t => t.UltimoMensaje ?? t.FechaCreacion).Take(60).ToListAsync();
            }
            catch { return new List<TicketSoporte>(); }
        }

        public async Task<List<TicketSoporte>> PorEstadoAsync(string estado)
        {
            try
            {
                var q = _context.TicketsSoporte.AsNoTracking().Where(t => t.Estado == estado);
                q = estado == TicketSoporte.EstadoAbierto
                    ? q.OrderBy(t => t.UltimoMensaje ?? t.FechaCreacion)              // lo que más espera, primero
                    : q.OrderByDescending(t => t.UltimoMensaje ?? t.FechaCreacion);
                return await q.Take(200).ToListAsync();
            }
            catch { return new List<TicketSoporte>(); }
        }

        public async Task<Dictionary<string, int>> ConteosAsync()
        {
            try
            {
                return await _context.TicketsSoporte.AsNoTracking().GroupBy(t => t.Estado)
                    .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            }
            catch { return new Dictionary<string, int>(); }
        }

        public async Task<TicketSoporte?> ObtenerAsync(int id, int? soloCliente = null)
        {
            try { return await _context.TicketsSoporte.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && (soloCliente == null || t.IdCliente == soloCliente)); }
            catch { return null; }
        }

        /// <summary>Conversación completa: el detalle inicial + los mensajes (desde un Id, para refrescar).</summary>
        public async Task<List<MensajeVista>> ConversacionAsync(TicketSoporte t, int desdeId = 0)
        {
            var lista = new List<MensajeVista>();
            if (desdeId <= 0) lista.Add(new MensajeVista(0, true, t.NombreUsuario ?? "Cliente", t.Detalle, t.FechaCreacion));
            try
            {
                var msgs = await _context.MensajesSoporte.AsNoTracking().Where(m => m.IdTicket == t.Id && m.Id > desdeId)
                    .OrderBy(m => m.Id).ToListAsync();
                // Consultas respondidas antes de existir la conversación: la respuesta guardada en el ticket.
                if (desdeId <= 0 && msgs.Count == 0 && !string.IsNullOrEmpty(t.Respuesta))
                    lista.Add(new MensajeVista(0, false, t.UsuarioRespuesta ?? "Atilson", t.Respuesta, t.FechaRespuesta ?? t.FechaCreacion));
                lista.AddRange(msgs.Select(m => new MensajeVista(m.Id, m.Autor == MensajeSoporte.AutorCliente, m.NombreAutor ?? (m.Autor == MensajeSoporte.AutorCliente ? "Cliente" : "Atilson"), m.Texto, m.Fecha)));
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Sin tabla MensajesSoporte"); }
            return lista;
        }

        /// <summary>Último mensaje de cada conversación (para la vista previa de la lista).</summary>
        public async Task<Dictionary<int, MensajeVista>> UltimosMensajesAsync(IEnumerable<TicketSoporte> tickets)
        {
            var dic = tickets.ToDictionary(t => t.Id, t => new MensajeVista(0, true, t.NombreUsuario ?? "Cliente", t.Detalle, t.FechaCreacion));
            try
            {
                var ids = dic.Keys.ToList();
                var ultimos = await _context.MensajesSoporte.AsNoTracking().Where(m => ids.Contains(m.IdTicket))
                    .GroupBy(m => m.IdTicket).Select(g => g.OrderByDescending(m => m.Id).First()).ToListAsync();
                foreach (var m in ultimos)
                    dic[m.IdTicket] = new MensajeVista(m.Id, m.Autor == MensajeSoporte.AutorCliente, m.NombreAutor ?? "", m.Texto, m.Fecha);
            }
            catch { }
            return dic;
        }

        public async Task MarcarVistaClienteAsync(int idTicket, int idCliente)
        {
            try
            {
                var t = await _context.TicketsSoporte.FirstOrDefaultAsync(x => x.Id == idTicket && x.IdCliente == idCliente);
                if (t == null) return;
                t.FechaVistaCliente = DateTime.Now;
                await _context.SaveChangesAsync();
            }
            catch { }
        }

        // ---------------------------------------------------------------- escrituras
        public async Task<(bool Ok, string Mensaje, string? Campo, int? Id)> CrearAsync(Usuario cliente, string? referencia, string? tipo, string? detalle, string urlRevision)
        {
            referencia = (referencia ?? "").Trim();
            detalle = (detalle ?? "").Trim();
            if (!TicketSoporte.Tipos.Contains(tipo)) return (false, "Elige el tipo de consulta.", "Tipo", null);
            if (detalle.Length < 10) return (false, "Cuéntanos un poco más en el detalle (al menos 10 caracteres).", "Detalle", null);
            if (detalle.Length > MaxTexto) return (false, "El detalle es muy largo: resúmelo en menos de 2.000 caracteres.", "Detalle", null);
            if (referencia.Length > 60) referencia = referencia.Substring(0, 60);
            if (!await TablaDisponibleAsync()) return (false, "El soporte en línea aún no está activo. Escríbenos a contacto@cargoflow-demo.cl.", null, null);

            // Si la referencia es un booking del cliente, se enlaza a la operación.
            int? idOp = null;
            if (referencia.Length > 0 && cliente.IdCliente is int idCli)
            {
                string r = referencia.ToUpper();
                idOp = await _context.Operaciones.AsNoTracking()
                    .Where(o => o.IdCliente == idCli && o.NumeroBooking != null && o.NumeroBooking.ToUpper() == r)
                    .Select(o => (int?)o.Id).FirstOrDefaultAsync();
            }

            var ahora = DateTime.Now;
            var t = new TicketSoporte
            {
                IdCliente = cliente.IdCliente,
                IdUsuario = cliente.Id,
                NombreUsuario = cliente.NombreCompleto,
                CorreoUsuario = cliente.Correo,
                IdOperacion = idOp,
                Referencia = referencia.Length > 0 ? referencia : null,
                Tipo = tipo!,
                Detalle = detalle,
                Estado = TicketSoporte.EstadoAbierto,
                FechaCreacion = ahora,
                UltimoMensaje = ahora,
                FechaVistaCliente = ahora
            };
            _context.TicketsSoporte.Add(t);
            await _context.SaveChangesAsync();
            await AvisarEquipoAsync(t, cliente, t.Detalle, urlRevision, nueva: true);
            return (true, $"Recibimos tu consulta #{t.Id}. Te responderemos en esta conversación y a {cliente.Correo}.", null, t.Id);
        }

        /// <summary>El cliente sigue escribiendo en su consulta (si estaba cerrada, se reabre).</summary>
        public async Task<(bool Ok, string Mensaje)> EscribirClienteAsync(int idTicket, Usuario cliente, string? texto, string urlRevision)
        {
            texto = (texto ?? "").Trim();
            if (texto.Length < 1) return (false, "Escribe tu mensaje.");
            if (texto.Length > MaxTexto) return (false, "El mensaje es muy largo (máx. 2.000 caracteres).");
            var t = await _context.TicketsSoporte.FirstOrDefaultAsync(x => x.Id == idTicket && x.IdCliente == cliente.IdCliente);
            if (t == null) return (false, "No se encontró la consulta.");
            bool reabre = t.Estado == TicketSoporte.EstadoCerrado;
            var ahora = DateTime.Now;
            _context.MensajesSoporte.Add(new MensajeSoporte { IdTicket = t.Id, Autor = MensajeSoporte.AutorCliente, NombreAutor = cliente.NombreCompleto, Texto = texto, Fecha = ahora });
            t.Estado = TicketSoporte.EstadoAbierto;
            t.FechaCierre = null;
            t.UltimoMensaje = ahora;
            t.FechaVistaCliente = ahora;
            await _context.SaveChangesAsync();
            await AvisarEquipoAsync(t, cliente, texto, urlRevision, nueva: false);
            return (true, reabre ? "Reabrimos tu consulta y avisamos al equipo." : "Mensaje enviado.");
        }

        /// <summary>El equipo responde; con cerrar=true además la da por resuelta.</summary>
        public async Task<(bool Ok, string Mensaje)> ResponderAsync(int id, string? respuesta, string usuario, bool cerrar, string urlPortal)
        {
            respuesta = (respuesta ?? "").Trim();
            if (respuesta.Length < 2) return (false, "Escribe la respuesta.");
            if (respuesta.Length > MaxTexto) return (false, "La respuesta es muy larga (máx. 2.000 caracteres).");
            var t = await _context.TicketsSoporte.FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return (false, "No se encontró la consulta.");

            var ahora = DateTime.Now;
            _context.MensajesSoporte.Add(new MensajeSoporte { IdTicket = t.Id, Autor = MensajeSoporte.AutorEquipo, NombreAutor = usuario, Texto = respuesta, Fecha = ahora });
            t.Respuesta = respuesta;           // última respuesta, para correos y listados
            t.FechaRespuesta = ahora;
            t.UsuarioRespuesta = usuario;
            t.UltimoMensaje = ahora;
            t.Estado = cerrar ? TicketSoporte.EstadoCerrado : TicketSoporte.EstadoRespondido;
            t.FechaCierre = cerrar ? ahora : null;
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(t.CorreoUsuario))
                _correos.EncolarCorreo(t.CorreoUsuario, $"Respuesta a tu consulta #{t.Id} en Atilson Cargo",
                    RegistroClientesService.Plantilla("Te respondimos",
                        $"<p>Hola {Enc(t.NombreUsuario ?? "")},</p><p>Sobre tu consulta «{Enc(t.Tipo)}»{(t.Referencia != null ? " (" + Enc(t.Referencia) + ")" : "")}:</p>" +
                        $"<blockquote style=\"margin:12px 0;padding:10px 14px;border-left:3px solid #443094;background:#f4f2fa;white-space:pre-wrap;\">{Enc(respuesta)}</blockquote>" +
                        $"<p>— {Enc(usuario)}, Atilson Cargo</p><p>Puedes seguir la conversación desde el portal.</p>", "Responder en el portal", urlPortal + $"?t={t.Id}"));
            return (true, cerrar ? $"Respondiste y cerraste la consulta #{t.Id}." : $"Respondiste la consulta #{t.Id}. Le avisamos al cliente por correo.");
        }

        public async Task<(bool Ok, string Mensaje)> CambiarEstadoAsync(int id, string estado, int? soloCliente = null)
        {
            var t = await _context.TicketsSoporte.FirstOrDefaultAsync(x => x.Id == id && (soloCliente == null || x.IdCliente == soloCliente));
            if (t == null) return (false, "No se encontró la consulta.");
            t.Estado = estado;
            t.FechaCierre = estado == TicketSoporte.EstadoCerrado ? DateTime.Now : null;
            await _context.SaveChangesAsync();
            return (true, estado == TicketSoporte.EstadoCerrado ? $"Consulta #{t.Id} cerrada." : $"Consulta #{t.Id} reabierta.");
        }

        private async Task AvisarEquipoAsync(TicketSoporte t, Usuario cliente, string texto, string urlRevision, bool nueva)
        {
            string? destino = _correos.CorreoAvisoInterno;
            if (string.IsNullOrWhiteSpace(destino)) return;
            string empresa = cliente.IdCliente is int ic ? (await _context.Clientes.AsNoTracking().Where(c => c.Id == ic).Select(c => c.RazonSocial).FirstOrDefaultAsync() ?? "") : "";
            _correos.EncolarCorreo(destino, $"{(nueva ? "Consulta" : "Nuevo mensaje en la consulta")} #{t.Id}: {t.Tipo}{(t.Referencia != null ? " · " + t.Referencia : "")}",
                RegistroClientesService.Plantilla(nueva ? "Nueva consulta desde el portal" : "El cliente escribió de nuevo",
                    $"<p><b>{Enc(cliente.NombreCompleto)}</b>{(empresa.Length > 0 ? " (" + Enc(empresa) + ")" : "")} escribió:</p>" +
                    $"<p>Tipo: {Enc(t.Tipo)}{(t.Referencia != null ? "<br>Referencia: " + Enc(t.Referencia) : "")}</p>" +
                    $"<blockquote style=\"margin:12px 0;padding:10px 14px;border-left:3px solid #443094;background:#f4f2fa;white-space:pre-wrap;\">{Enc(texto)}</blockquote>",
                    "Responder", urlRevision + $"?t={t.Id}", "ATILSON CARGO · SOPORTE"));
        }

        private static string Enc(string s) => WebUtility.HtmlEncode(s);
    }
}
