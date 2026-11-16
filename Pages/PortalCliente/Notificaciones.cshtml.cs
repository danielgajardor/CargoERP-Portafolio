using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    // Avisos del portal (15-nov-2026): lista con filtros, marcar como leídos, preferencias de
    // correo, y los datos de la campana del menú (handler Resumen, consultado cada minuto).
    public class NotificacionesModel : PageModel
    {
        private readonly NotificacionesClienteService _avisos;
        public NotificacionesModel(NotificacionesClienteService avisos) { _avisos = avisos; }

        public const int PorPagina = 30;

        [BindProperty(SupportsGet = true)] public string? Tipo { get; set; }
        [BindProperty(SupportsGet = true)] public bool NoLeidas { get; set; }
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

        public List<NotificacionCliente> Avisos { get; private set; } = new();
        public Dictionary<string, int> NoLeidasPorTipo { get; private set; } = new();
        public int TotalNoLeidas => NoLeidasPorTipo.Values.Sum();
        public HashSet<string> TiposCorreo { get; private set; } = new();
        public bool HayMas { get; private set; }
        public string? Mensaje { get; private set; }

        private int? IdCliente() => int.TryParse(User.FindFirst("IdCliente")?.Value, out var id) && id > 0 ? id : null;
        private string Correo() => User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name ?? "";

        public async Task<IActionResult> OnGetAsync(string? ok)
        {
            if (IdCliente() is not int id) return RedirectToPage("/Login");
            if (Pagina < 1) Pagina = 1;
            if (!string.IsNullOrWhiteSpace(Tipo) && NotificacionesClienteService.Tipos.All(t => t.Codigo != Tipo)) Tipo = null;
            var lista = await _avisos.ListarAsync(id, PorPagina + 1, Tipo, NoLeidas, (Pagina - 1) * PorPagina);
            HayMas = lista.Count > PorPagina;
            Avisos = lista.Take(PorPagina).ToList();
            NoLeidasPorTipo = await _avisos.ContarPorTipoAsync(id);
            TiposCorreo = await _avisos.TiposCorreoAsync(Correo());
            Mensaje = ok switch { "pref" => "Preferencias de correo guardadas.", "todas" => "Todos los avisos quedaron como leídos.", _ => null };
            return Page();
        }

        // Campana: no leídos + últimos 8.
        public async Task<IActionResult> OnGetResumenAsync()
        {
            if (IdCliente() is not int id) return new JsonResult(new { ok = false });
            var ahora = DateTime.Now;
            var ultimos = await _avisos.ListarAsync(id, 8);
            return new JsonResult(new
            {
                ok = true,
                noLeidas = await _avisos.ContarNoLeidasAsync(id),
                avisos = ultimos.Select(n =>
                {
                    var t = NotificacionesClienteService.Tipo(n.Tipo);
                    return new
                    {
                        n.Id, n.Titulo, mensaje = n.Mensaje, tipo = t.Nombre, icono = t.Icono, color = t.Color,
                        hace = NotificacionesClienteService.Hace(n.Fecha, ahora), leida = n.FechaLeida != null,
                        url = Url.Page("/PortalCliente/Notificaciones", "Abrir", new { id = n.Id })
                    };
                })
            });
        }

        // Abre el aviso: lo marca leído y lleva a su pantalla.
        public async Task<IActionResult> OnGetAbrirAsync(int id)
        {
            if (IdCliente() is not int idCliente) return RedirectToPage("/Login");
            var n = await _avisos.MarcarLeidaAsync(idCliente, id);
            string destino = n?.Url ?? "";
            // Solo rutas internas del portal.
            if (!destino.StartsWith("/PortalCliente/", StringComparison.OrdinalIgnoreCase)) destino = "/PortalCliente/Notificaciones";
            return LocalRedirect(destino);
        }

        public async Task<IActionResult> OnPostLeerAsync(int id)
        {
            if (IdCliente() is not int idCliente) return new JsonResult(new { ok = false });
            await _avisos.MarcarLeidaAsync(idCliente, id);
            return new JsonResult(new { ok = true, noLeidas = await _avisos.ContarNoLeidasAsync(idCliente) });
        }

        public async Task<IActionResult> OnPostLeerTodasAsync(string? tipo)
        {
            if (IdCliente() is not int idCliente) return RedirectToPage("/Login");
            await _avisos.MarcarTodasAsync(idCliente, tipo);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return new JsonResult(new { ok = true, noLeidas = 0 });
            return RedirectToPage(new { ok = "todas", Tipo = tipo });
        }

        public async Task<IActionResult> OnPostPreferenciasAsync(List<string>? tipos)
        {
            if (IdCliente() is null) return RedirectToPage("/Login");
            await _avisos.GuardarTiposCorreoAsync(Correo(), tipos ?? new List<string>());
            return Redirect(Url.Page("/PortalCliente/Notificaciones", new { ok = "pref" }) + "#preferencias");
        }
    }
}
