using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App
{
    // Soporte clientes (oct-2026): conversaciones con los clientes que escriben desde el portal.
    public class SoporteModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly SoporteClientesService _soporte;

        public SoporteModel(AtilsonContext context, SoporteClientesService soporte)
        {
            _context = context;
            _soporte = soporte;
        }

        [BindProperty(SupportsGet = true)] public string Vista { get; set; } = TicketSoporte.EstadoAbierto;
        [BindProperty(SupportsGet = true)] public int? T { get; set; }
        public List<TicketSoporte> Tickets { get; private set; } = new();
        public Dictionary<int, MensajeVista> Ultimos { get; private set; } = new();
        public Dictionary<int, string> Empresas { get; private set; } = new();
        public Dictionary<string, int> Conteos { get; private set; } = new();
        public TicketSoporte? Actual { get; private set; }
        public List<MensajeVista> Conversacion { get; private set; } = new();
        public bool Disponible { get; private set; } = true;
        [TempData] public string? Aviso { get; set; }
        [TempData] public string? AvisoError { get; set; }

        private string UrlPortal => Url.Page("/PortalCliente/Soporte", null, null, Request.Scheme) ?? "/PortalCliente/Soporte";

        public async Task OnGetAsync()
        {
            Disponible = await _soporte.TablaDisponibleAsync();
            // Un enlace directo (?t=) abre la consulta en la pestaña que le corresponde.
            if (T is int idT && await _soporte.ObtenerAsync(idT) is TicketSoporte directo && Request.Query["vista"].Count == 0)
                Vista = directo.Estado;
            if (Vista != TicketSoporte.EstadoRespondido && Vista != TicketSoporte.EstadoCerrado) Vista = TicketSoporte.EstadoAbierto;
            Tickets = await _soporte.PorEstadoAsync(Vista);
            Ultimos = await _soporte.UltimosMensajesAsync(Tickets);
            Conteos = await _soporte.ConteosAsync();
            Actual = Tickets.FirstOrDefault(t => t.Id == T) ?? Tickets.FirstOrDefault();
            if (Actual != null) Conversacion = await _soporte.ConversacionAsync(Actual);
            var ids = Tickets.Where(t => t.IdCliente != null).Select(t => t.IdCliente!.Value).Distinct().ToList();
            Empresas = await _context.Clientes.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.RazonSocial);
        }

        public async Task<IActionResult> OnGetMensajesAsync(int t, int desde)
        {
            var ticket = await _soporte.ObtenerAsync(t);
            if (ticket == null) return NotFound();
            var nuevos = await _soporte.ConversacionAsync(ticket, desde);
            return new JsonResult(new { estado = ticket.Estado, mensajes = nuevos.Select(m => new { m.Id, m.EsCliente, m.Nombre, m.Texto, fecha = m.Fecha.ToString("dd-MM HH:mm") }) });
        }

        public async Task<IActionResult> OnPostResponderAsync(int id, string? respuesta, bool cerrar)
        {
            var r = await _soporte.ResponderAsync(id, respuesta, User.Identity?.Name ?? "Atilson", cerrar, UrlPortal);
            if (r.Ok) Aviso = r.Mensaje; else AvisoError = r.Mensaje;
            // Tras responder, la consulta cambia de pestaña: se abre donde quedó.
            return RedirectToPage(new { t = id });
        }

        public async Task<IActionResult> OnPostEstadoAsync(int id, string estado)
        {
            if (estado != TicketSoporte.EstadoCerrado && estado != TicketSoporte.EstadoAbierto) return BadRequest();
            var r = await _soporte.CambiarEstadoAsync(id, estado);
            if (r.Ok) Aviso = r.Mensaje; else AvisoError = r.Mensaje;
            return RedirectToPage(new { t = id });
        }
    }
}
