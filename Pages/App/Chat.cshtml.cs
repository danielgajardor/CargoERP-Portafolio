using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtilsonCargoSpa.Pages.App
{
    // Chat del equipo (nov-2026): privados de a dos y el grupo "Equipo Atilson".
    // La pantalla se arma una vez y después todo va por fetch a estos handlers:
    // la conversación abierta se actualiza cada pocos segundos (wwwroot/js/chat-interno.js)
    // y el menú de cualquier página cuenta los no leídos (wwwroot/js/chat-aviso.js).
    public class ChatModel : PageModel
    {
        private readonly ChatInternoService _chat;
        public ChatModel(ChatInternoService chat) => _chat = chat;

        public bool Disponible { get; private set; } = true;
        public Usuario? Yo { get; private set; }
        public List<ChatResumen> Conversaciones { get; private set; } = new();
        public List<ChatPersona> Equipo { get; private set; } = new();
        public int? Abrir { get; private set; }

        /// <param name="c">Conversación a abrir.</param>
        /// <param name="con">Id de una persona: abre (o crea) la conversación privada con ella. Lo usa el Directorio.</param>
        public async Task<IActionResult> OnGetAsync(int? c, int? con)
        {
            if (!await _chat.DisponibleAsync()) { Disponible = false; return Page(); }
            Yo = await _chat.UsuarioActualAsync(User);
            if (Yo == null) return Page();

            if (con.HasValue)
            {
                Abrir = await _chat.AbrirPrivadaAsync(Yo.Id, con.Value);
            }
            else if (c.HasValue && await _chat.AccesoAsync(c.Value, Yo.Id) != null)
            {
                Abrir = c.Value;
            }
            Conversaciones = await _chat.ConversacionesAsync(Yo.Id, Abrir);
            Abrir ??= Conversaciones.FirstOrDefault()?.Id;
            Equipo = (await _chat.EquipoAsync()).Where(p => p.Id != Yo.Id).ToList();
            return Page();
        }

        public async Task<IActionResult> OnGetConversacionesAsync(int? incluir)
        {
            var yo = await _chat.UsuarioActualAsync(User);
            if (yo == null) return Unauthorized();
            return new JsonResult(await _chat.ConversacionesAsync(yo.Id, incluir));
        }

        public async Task<IActionResult> OnGetMensajesAsync(int id, int despues = 0, int antes = 0)
        {
            var yo = await _chat.UsuarioActualAsync(User);
            if (yo == null) return Unauthorized();
            var conv = await _chat.AccesoAsync(id, yo.Id);
            if (conv == null) return NotFound();
            var mensajes = await _chat.MensajesAsync(id, yo.Id, despues, antes);
            int leidoPorOtro = conv.Tipo == ChatConversacion.TipoPrivada ? await _chat.LeidoPorOtroAsync(id, yo.Id) : 0;
            return new JsonResult(new { mensajes, leidoPorOtro, hayMas = despues == 0 && mensajes.Count >= 60 });
        }

        public async Task<IActionResult> OnPostEnviarAsync(int id, string? texto)
        {
            var yo = await _chat.UsuarioActualAsync(User);
            if (yo == null) return Unauthorized();
            if (await _chat.AccesoAsync(id, yo.Id) == null) return NotFound();
            var (mensaje, error) = await _chat.EnviarAsync(id, yo, texto);
            if (error != null) return BadRequest(new { error });
            return new JsonResult(mensaje);
        }

        public async Task<IActionResult> OnPostLeidoAsync(int id, int hasta)
        {
            var yo = await _chat.UsuarioActualAsync(User);
            if (yo == null) return Unauthorized();
            if (await _chat.AccesoAsync(id, yo.Id) == null) return NotFound();
            await _chat.MarcarLeidoAsync(id, yo.Id, hasta);
            return new JsonResult(new { ok = true });
        }

        public async Task<IActionResult> OnPostAbrirAsync(int idUsuario)
        {
            var yo = await _chat.UsuarioActualAsync(User);
            if (yo == null) return Unauthorized();
            int? id = await _chat.AbrirPrivadaAsync(yo.Id, idUsuario);
            if (id == null) return BadRequest(new { error = "No se puede abrir una conversación con esa persona." });
            return new JsonResult(new { id });
        }

        /// <summary>Para el menú de cualquier página: cuántos sin leer y el último mensaje nuevo (para el aviso).</summary>
        public async Task<IActionResult> OnGetEstadoAsync(int despues = 0)
        {
            var yo = await _chat.UsuarioActualAsync(User);
            if (yo == null) return new JsonResult(new { total = 0 });
            var (total, nuevo) = await _chat.NoLeidosAsync(yo.Id, despues);
            return new JsonResult(new
            {
                total,
                nuevo = nuevo == null ? null : new
                {
                    nuevo.Id,
                    nuevo.IdConversacion,
                    nuevo.Autor,
                    texto = nuevo.Texto.Length > 140 ? nuevo.Texto[..140] + "…" : nuevo.Texto,
                }
            });
        }
    }
}
