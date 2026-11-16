using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    public record ChatPersona(int Id, string Nombre, string Correo, string? Cargo, string? FotoUrl, string Iniciales);

    public record ChatResumen(int Id, string Tipo, string Titulo, string? Subtitulo, int? IdOtro, string? FotoUrl, string Iniciales,
        string? UltimoTexto, string? UltimoAutor, bool UltimoMio, DateTime? UltimaFecha, int NoLeidos);

    public record ChatMensajeVista(int Id, int IdConversacion, int IdUsuario, string Autor, string? FotoUrl, string Iniciales, string Texto, DateTime Fecha, bool Mio);

    // ============================================================================
    // Chat del equipo (nov-2026)
    // ----------------------------------------------------------------------------
    // Mensajes entre los internos: conversaciones privadas de a dos y un grupo general
    // ("Equipo Atilson") con todos. Tablas en Sql/2026-11_chat_interno.sql.
    //
    // Privacidad: una conversación privada solo se entrega a sus dos participantes
    // (Acceso). Ni el grupo ni los administradores pueden leerla desde el ERP.
    //
    // Sin el script corrido, Disponible devuelve false y la pantalla lo avisa; los
    // contadores del menú devuelven 0 en vez de fallar.
    // ============================================================================
    public class ChatInternoService
    {
        public const string NombreGeneral = "Equipo Atilson";
        private readonly AtilsonContext _context;
        private readonly ILogger<ChatInternoService> _logger;

        public ChatInternoService(AtilsonContext context, ILogger<ChatInternoService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public static string ClavePrivada(int a, int b) => a < b ? $"{a}-{b}" : $"{b}-{a}";

        public async Task<bool> DisponibleAsync()
        {
            try { await _context.ChatConversaciones.AsNoTracking().AnyAsync(); return true; }
            catch (Exception ex) when (EsTablaFaltante(ex)) { return false; }
        }

        /// <summary>El usuario interno activo que tiene la sesión, o null (cliente, inactivo o sin correo).</summary>
        public async Task<Usuario?> UsuarioActualAsync(ClaimsPrincipal user)
        {
            string? correo = PerfilUsuarioService.CorreoDe(user)?.ToLower();
            if (string.IsNullOrEmpty(correo)) return null;
            return await _context.Usuarios.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Correo.ToLower() == correo && u.Rol != "Cliente" && u.Activo == 1);
        }

        // --------------------------------------------------------------- personas

        /// <summary>Internos activos (los mismos del Directorio), ordenados por nombre.</summary>
        public async Task<List<ChatPersona>> EquipoAsync()
        {
            var usuarios = await _context.Usuarios.AsNoTracking()
                .Where(u => u.Rol != "Cliente" && u.Activo == 1)
                .OrderBy(u => u.NombreCompleto)
                .Select(u => new { u.Id, u.NombreCompleto, u.Correo })
                .ToListAsync();
            var perfiles = await PerfilesAsync(usuarios.Select(u => u.Id));
            return usuarios.Select(u =>
            {
                perfiles.TryGetValue(u.Id, out var p);
                return new ChatPersona(u.Id, u.NombreCompleto, u.Correo, p?.Cargo, p?.FotoUrl, PerfilUsuarioService.Iniciales(u.NombreCompleto));
            }).ToList();
        }

        /// <summary>Nombre y foto de cualquier usuario (también de quien ya no está activo, para los mensajes antiguos).</summary>
        private async Task<Dictionary<int, ChatPersona>> PersonasAsync(IEnumerable<int> ids)
        {
            var lista = ids.Distinct().ToList();
            if (lista.Count == 0) return new();
            var usuarios = await _context.Usuarios.AsNoTracking().Where(u => lista.Contains(u.Id))
                .Select(u => new { u.Id, u.NombreCompleto, u.Correo }).ToListAsync();
            var perfiles = await PerfilesAsync(lista);
            return usuarios.ToDictionary(u => u.Id, u =>
            {
                perfiles.TryGetValue(u.Id, out var p);
                return new ChatPersona(u.Id, u.NombreCompleto, u.Correo, p?.Cargo, p?.FotoUrl, PerfilUsuarioService.Iniciales(u.NombreCompleto));
            });
        }

        private async Task<Dictionary<int, PerfilUsuario>> PerfilesAsync(IEnumerable<int> ids)
        {
            var lista = ids.ToList();
            try
            {
                return (await _context.PerfilesUsuario.AsNoTracking().Where(p => lista.Contains(p.IdUsuario)).ToListAsync())
                    .GroupBy(p => p.IdUsuario).ToDictionary(g => g.Key, g => g.First());
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Chat: sin perfiles (¿falta Sql/2026-10_perfiles_soporte.sql?)");
                return new();
            }
        }

        private ChatPersona Desconocido(int id) => new(id, "Usuario eliminado", "", null, null, "?");

        // ---------------------------------------------------------- conversaciones

        /// <summary>El grupo de todo el equipo. Se crea si el script no alcanzó a crearlo.</summary>
        public async Task<ChatConversacion> GeneralAsync()
        {
            var g = await _context.ChatConversaciones.OrderBy(c => c.Id).FirstOrDefaultAsync(c => c.Tipo == ChatConversacion.TipoGeneral);
            if (g != null) return g;
            g = new ChatConversacion { Tipo = ChatConversacion.TipoGeneral, Nombre = NombreGeneral, FechaCreacion = DateTime.Now };
            _context.ChatConversaciones.Add(g);
            await _context.SaveChangesAsync();
            return g;
        }

        /// <summary>
        /// Agrega a la persona al grupo general la primera vez. Entra con lo anterior ya
        /// "leído", para que alguien nuevo no parta con todo el historial como pendiente.
        /// </summary>
        private async Task<ChatParticipante> ParticipacionGeneralAsync(int idConv, int idUsuario)
        {
            var p = await _context.ChatParticipantes.FirstOrDefaultAsync(x => x.IdConversacion == idConv && x.IdUsuario == idUsuario);
            if (p != null) return p;
            int ultimo = await _context.ChatMensajes.Where(m => m.IdConversacion == idConv).MaxAsync(m => (int?)m.Id) ?? 0;
            p = new ChatParticipante { IdConversacion = idConv, IdUsuario = idUsuario, UltimoLeidoId = ultimo, FechaIngreso = DateTime.Now };
            _context.ChatParticipantes.Add(p);
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Otra pestaña lo creó al mismo tiempo (índice único): se usa ese.
                _context.Entry(p).State = EntityState.Detached;
                p = await _context.ChatParticipantes.FirstAsync(x => x.IdConversacion == idConv && x.IdUsuario == idUsuario);
            }
            return p;
        }

        /// <summary>
        /// La conversación si la persona puede verla, o null. El grupo general lo ve todo
        /// interno; una privada, solo sus dos participantes.
        /// </summary>
        public async Task<ChatConversacion?> AccesoAsync(int idConv, int idUsuario)
        {
            var c = await _context.ChatConversaciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == idConv);
            if (c == null) return null;
            if (c.Tipo == ChatConversacion.TipoGeneral)
            {
                await ParticipacionGeneralAsync(c.Id, idUsuario);
                return c;
            }
            bool participa = await _context.ChatParticipantes.AnyAsync(x => x.IdConversacion == idConv && x.IdUsuario == idUsuario);
            return participa ? c : null;
        }

        /// <summary>Busca o crea la conversación privada con otra persona del equipo. Null si no corresponde.</summary>
        public async Task<int?> AbrirPrivadaAsync(int idYo, int idOtro)
        {
            if (idYo == idOtro) return null;
            bool otroValido = await _context.Usuarios.AnyAsync(u => u.Id == idOtro && u.Rol != "Cliente" && u.Activo == 1);
            if (!otroValido) return null;
            string clave = ClavePrivada(idYo, idOtro);
            var c = await _context.ChatConversaciones.FirstOrDefaultAsync(x => x.ClavePrivada == clave);
            if (c == null)
            {
                c = new ChatConversacion { Tipo = ChatConversacion.TipoPrivada, ClavePrivada = clave, IdCreador = idYo, FechaCreacion = DateTime.Now };
                _context.ChatConversaciones.Add(c);
                try { await _context.SaveChangesAsync(); }
                catch (DbUpdateException)
                {
                    _context.Entry(c).State = EntityState.Detached;
                    c = await _context.ChatConversaciones.FirstAsync(x => x.ClavePrivada == clave);
                }
            }
            foreach (int id in new[] { idYo, idOtro })
            {
                if (!await _context.ChatParticipantes.AnyAsync(x => x.IdConversacion == c.Id && x.IdUsuario == id))
                    _context.ChatParticipantes.Add(new ChatParticipante { IdConversacion = c.Id, IdUsuario = id, UltimoLeidoId = 0, FechaIngreso = DateTime.Now });
            }
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException ex) { _logger.LogDebug(ex, "Chat: participante ya existía"); }
            return c.Id;
        }

        /// <summary>
        /// Lista de la izquierda: el grupo general primero y después las privadas con
        /// mensajes, de la más reciente a la más antigua. <paramref name="incluir"/> agrega
        /// una privada recién abierta que aún no tiene mensajes.
        /// </summary>
        public async Task<List<ChatResumen>> ConversacionesAsync(int idYo, int? incluir = null)
        {
            var general = await GeneralAsync();
            await ParticipacionGeneralAsync(general.Id, idYo);

            var mias = await _context.ChatParticipantes.AsNoTracking().Where(p => p.IdUsuario == idYo).ToListAsync();
            var ids = mias.Select(p => p.IdConversacion).Distinct().ToList();
            var convs = await _context.ChatConversaciones.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync();
            convs = convs.Where(c => c.Tipo == ChatConversacion.TipoGeneral || c.FechaUltimoMensaje != null || c.Id == incluir).ToList();
            var visibles = convs.Select(c => c.Id).ToList();

            var idsUltimos = await _context.ChatMensajes.AsNoTracking().Where(m => visibles.Contains(m.IdConversacion))
                .GroupBy(m => m.IdConversacion).Select(g => g.Max(m => m.Id)).ToListAsync();
            var ultimos = await _context.ChatMensajes.AsNoTracking().Where(m => idsUltimos.Contains(m.Id)).ToDictionaryAsync(m => m.IdConversacion);
            var noLeidos = await NoLeidosPorConversacionAsync(idYo);

            var otros = convs.Where(c => c.Tipo == ChatConversacion.TipoPrivada).ToDictionary(c => c.Id, c => OtroDe(c, idYo));
            var personas = await PersonasAsync(otros.Values.Concat(ultimos.Values.Select(m => m.IdUsuario)));
            int miembros = await _context.Usuarios.CountAsync(u => u.Rol != "Cliente" && u.Activo == 1);

            var lista = new List<ChatResumen>();
            foreach (var c in convs)
            {
                ultimos.TryGetValue(c.Id, out var u);
                noLeidos.TryGetValue(c.Id, out int n);
                string? autor = u == null ? null : (u.IdUsuario == idYo ? "Tú" : (personas.TryGetValue(u.IdUsuario, out var pa) ? PrimerNombre(pa.Nombre) : "Alguien"));
                if (c.Tipo == ChatConversacion.TipoGeneral)
                {
                    lista.Add(new ChatResumen(c.Id, c.Tipo, c.Nombre ?? NombreGeneral, $"{miembros} personas", null, null, "",
                        u?.Texto, autor, u?.IdUsuario == idYo, u?.FechaEnvio ?? c.FechaUltimoMensaje, n));
                }
                else
                {
                    int idOtro = otros[c.Id];
                    var p = personas.TryGetValue(idOtro, out var po) ? po : Desconocido(idOtro);
                    lista.Add(new ChatResumen(c.Id, c.Tipo, p.Nombre, p.Cargo, idOtro, p.FotoUrl, p.Iniciales,
                        u?.Texto, autor, u?.IdUsuario == idYo, u?.FechaEnvio ?? c.FechaUltimoMensaje, n));
                }
            }
            return lista
                .OrderByDescending(x => x.Tipo == ChatConversacion.TipoGeneral)
                .ThenByDescending(x => x.UltimaFecha ?? DateTime.MinValue)
                .ToList();
        }

        private static int OtroDe(ChatConversacion c, int idYo)
        {
            var partes = (c.ClavePrivada ?? "").Split('-');
            if (partes.Length == 2 && int.TryParse(partes[0], out int a) && int.TryParse(partes[1], out int b))
                return a == idYo ? b : a;
            return 0;
        }

        private static string PrimerNombre(string nombre) => nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nombre;

        // ---------------------------------------------------------------- mensajes

        /// <summary>
        /// Mensajes de una conversación (verificar antes con AccesoAsync).
        /// despues &gt; 0: los nuevos desde ese Id (para la actualización periódica).
        /// Si no: los últimos <paramref name="limite"/>, o los anteriores a <paramref name="antes"/>.
        /// </summary>
        public async Task<List<ChatMensajeVista>> MensajesAsync(int idConv, int idYo, int despues = 0, int antes = 0, int limite = 60)
        {
            var q = _context.ChatMensajes.AsNoTracking().Where(m => m.IdConversacion == idConv);
            List<ChatMensaje> lista;
            if (despues > 0)
                lista = await q.Where(m => m.Id > despues).OrderBy(m => m.Id).Take(300).ToListAsync();
            else
            {
                if (antes > 0) q = q.Where(m => m.Id < antes);
                lista = (await q.OrderByDescending(m => m.Id).Take(Math.Clamp(limite, 1, 200)).ToListAsync());
                lista.Reverse();
            }
            var personas = await PersonasAsync(lista.Select(m => m.IdUsuario));
            return lista.Select(m =>
            {
                var p = personas.TryGetValue(m.IdUsuario, out var x) ? x : Desconocido(m.IdUsuario);
                return new ChatMensajeVista(m.Id, m.IdConversacion, m.IdUsuario, p.Nombre, p.FotoUrl, p.Iniciales, m.Texto, m.FechaEnvio, m.IdUsuario == idYo);
            }).ToList();
        }

        /// <summary>Guarda un mensaje (verificar antes con AccesoAsync). Devuelve el error para mostrar, o el mensaje.</summary>
        public async Task<(ChatMensajeVista? Mensaje, string? Error)> EnviarAsync(int idConv, Usuario yo, string? texto)
        {
            texto = (texto ?? "").Replace("\r\n", "\n").Trim();
            if (texto.Length == 0) return (null, "Escribe un mensaje.");
            if (texto.Length > ChatMensaje.LargoMaximo) return (null, $"El mensaje es muy largo (máximo {ChatMensaje.LargoMaximo:N0} caracteres).");

            var m = new ChatMensaje { IdConversacion = idConv, IdUsuario = yo.Id, Texto = texto, FechaEnvio = DateTime.Now };
            _context.ChatMensajes.Add(m);
            await _context.SaveChangesAsync();

            var c = await _context.ChatConversaciones.FirstOrDefaultAsync(x => x.Id == idConv);
            if (c != null) c.FechaUltimoMensaje = m.FechaEnvio;
            var mia = await _context.ChatParticipantes.FirstOrDefaultAsync(x => x.IdConversacion == idConv && x.IdUsuario == yo.Id);
            if (mia != null && mia.UltimoLeidoId < m.Id) mia.UltimoLeidoId = m.Id;
            await _context.SaveChangesAsync();

            var perfiles = await PerfilesAsync(new[] { yo.Id });
            perfiles.TryGetValue(yo.Id, out var p);
            return (new ChatMensajeVista(m.Id, idConv, yo.Id, yo.NombreCompleto, p?.FotoUrl, PerfilUsuarioService.Iniciales(yo.NombreCompleto), m.Texto, m.FechaEnvio, true), null);
        }

        /// <summary>Marca como leído hasta ese mensaje (nunca retrocede).</summary>
        public async Task MarcarLeidoAsync(int idConv, int idYo, int hastaId)
        {
            var p = await _context.ChatParticipantes.FirstOrDefaultAsync(x => x.IdConversacion == idConv && x.IdUsuario == idYo);
            if (p == null || hastaId <= p.UltimoLeidoId) return;
            int max = await _context.ChatMensajes.Where(m => m.IdConversacion == idConv).MaxAsync(m => (int?)m.Id) ?? 0;
            p.UltimoLeidoId = Math.Min(hastaId, max);
            await _context.SaveChangesAsync();
        }

        /// <summary>En una privada, hasta qué mensaje leyó la otra persona (para el "Visto").</summary>
        public async Task<int> LeidoPorOtroAsync(int idConv, int idYo)
        {
            return await _context.ChatParticipantes.AsNoTracking()
                .Where(x => x.IdConversacion == idConv && x.IdUsuario != idYo)
                .Select(x => (int?)x.UltimoLeidoId).MaxAsync() ?? 0;
        }

        private async Task<Dictionary<int, int>> NoLeidosPorConversacionAsync(int idYo)
        {
            var q = from m in _context.ChatMensajes
                    join p in _context.ChatParticipantes on m.IdConversacion equals p.IdConversacion
                    where p.IdUsuario == idYo && m.Id > p.UltimoLeidoId && m.IdUsuario != idYo
                    group m by m.IdConversacion into g
                    select new { g.Key, N = g.Count() };
            return await q.ToDictionaryAsync(x => x.Key, x => x.N);
        }

        /// <summary>
        /// Para el menú y el aviso emergente: total de mensajes sin leer y el más reciente
        /// de ellos (si es posterior a <paramref name="despuesDe"/>). Nunca lanza.
        /// </summary>
        public async Task<(int Total, ChatMensajeVista? Nuevo)> NoLeidosAsync(int idYo, int despuesDe = 0)
        {
            try
            {
                var general = await GeneralAsync();
                await ParticipacionGeneralAsync(general.Id, idYo);
                var porConv = await NoLeidosPorConversacionAsync(idYo);
                int total = porConv.Values.Sum();
                if (total == 0 || despuesDe == int.MaxValue) return (total, null); // int.MaxValue: solo el total (menú)

                var q = from m in _context.ChatMensajes
                        join p in _context.ChatParticipantes on m.IdConversacion equals p.IdConversacion
                        where p.IdUsuario == idYo && m.Id > p.UltimoLeidoId && m.IdUsuario != idYo && m.Id > despuesDe
                        orderby m.Id descending
                        select m;
                var ult = await q.AsNoTracking().FirstOrDefaultAsync();
                if (ult == null) return (total, null);
                var personas = await PersonasAsync(new[] { ult.IdUsuario });
                var pe = personas.TryGetValue(ult.IdUsuario, out var x) ? x : Desconocido(ult.IdUsuario);
                return (total, new ChatMensajeVista(ult.Id, ult.IdConversacion, ult.IdUsuario, pe.Nombre, pe.FotoUrl, pe.Iniciales, ult.Texto, ult.FechaEnvio, false));
            }
            catch (Exception ex)
            {
                if (!EsTablaFaltante(ex)) _logger.LogWarning(ex, "Chat: no se pudieron contar los mensajes sin leer");
                return (0, null);
            }
        }

        private static bool EsTablaFaltante(Exception ex)
        {
            string m = (ex.InnerException ?? ex).Message;
            return m.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) || m.Contains("ChatConversaciones") || m.Contains("ChatParticipantes") || m.Contains("ChatMensajes");
        }
    }
}
