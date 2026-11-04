using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    // Soporte del portal (oct-2026): el cliente conversa con el equipo en cada consulta.
    public class SoporteModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly SoporteClientesService _soporte;
        private readonly PerfilUsuarioService _perfiles;

        public SoporteModel(AtilsonContext context, SoporteClientesService soporte, PerfilUsuarioService perfiles)
        {
            _context = context;
            _soporte = soporte;
            _perfiles = perfiles;
        }

        public List<TicketSoporte> Tickets { get; private set; } = new();
        public Dictionary<int, MensajeVista> Ultimos { get; private set; } = new();
        public TicketSoporte? Actual { get; private set; }
        public List<MensajeVista> Conversacion { get; private set; } = new();
        public List<string> Bookings { get; private set; } = new();
        public bool Disponible { get; private set; } = true;
        public bool MostrarNueva { get; private set; }

        [TempData] public string? Aviso { get; set; }
        [TempData] public string? AvisoError { get; set; }
        [BindProperty(SupportsGet = true)] public int? T { get; set; }
        [BindProperty(SupportsGet = true)] public bool Nueva { get; set; }
        [BindProperty(SupportsGet = true)] public string? Referencia { get; set; }
        [BindProperty] public string? Tipo { get; set; }
        [BindProperty] public string? Detalle { get; set; }
        public string? CampoError { get; set; }
        public string? Error { get; set; }

        private int? IdCliente => int.TryParse(User.FindFirst("IdCliente")?.Value, out int id) && id > 0 ? id : null;
        private string UrlEquipo => Url.Page("/App/Soporte", null, null, Request.Scheme) ?? "/App/Soporte";

        private async Task CargarAsync()
        {
            Disponible = await _soporte.TablaDisponibleAsync();
            if (IdCliente is not int id) return;
            Tickets = await _soporte.DelClienteAsync(id);
            Ultimos = await _soporte.UltimosMensajesAsync(Tickets);
            Bookings = await _context.Operaciones.AsNoTracking().Where(o => o.IdCliente == id && o.NumeroBooking != null)
                .OrderByDescending(o => o.Id).Select(o => o.NumeroBooking).Take(60).ToListAsync();

            MostrarNueva = Nueva || !Tickets.Any() || Error != null;
            if (!MostrarNueva)
            {
                Actual = Tickets.FirstOrDefault(t => t.Id == T) ?? Tickets.First();
                Conversacion = await _soporte.ConversacionAsync(Actual);
                await _soporte.MarcarVistaClienteAsync(Actual.Id, id);
            }
        }

        public bool NoLeida(TicketSoporte t) =>
            t.Estado == TicketSoporte.EstadoRespondido && (t.FechaVistaCliente == null || t.FechaRespuesta > t.FechaVistaCliente) && t.Id != Actual?.Id;

        public async Task OnGetAsync() => await CargarAsync();

        public async Task<IActionResult> OnGetMensajesAsync(int t, int desde)
        {
            if (IdCliente is not int id) return Unauthorized();
            var ticket = await _soporte.ObtenerAsync(t, id);
            if (ticket == null) return NotFound();
            var nuevos = await _soporte.ConversacionAsync(ticket, desde);
            if (nuevos.Any()) await _soporte.MarcarVistaClienteAsync(t, id);
            return new JsonResult(new { estado = ticket.Estado, mensajes = nuevos.Select(m => new { m.Id, m.EsCliente, m.Nombre, m.Texto, fecha = m.Fecha.ToString("dd-MM HH:mm") }) });
        }

        public async Task<IActionResult> OnPostCrearAsync()
        {
            var u = await _perfiles.UsuarioActualAsync(User);
            if (u == null) return RedirectToPage("/Login");
            var r = await _soporte.CrearAsync(u, Referencia, Tipo, Detalle, UrlEquipo);
            if (!r.Ok)
            {
                Error = r.Mensaje;
                CampoError = r.Campo;
                Nueva = true;
                await CargarAsync();
                return Page();
            }
            Aviso = r.Mensaje;
            return RedirectToPage(new { t = r.Id });
        }

        public async Task<IActionResult> OnPostEscribirAsync(int id, string? texto)
        {
            var u = await _perfiles.UsuarioActualAsync(User);
            if (u == null) return RedirectToPage("/Login");
            var r = await _soporte.EscribirClienteAsync(id, u, texto, UrlEquipo);
            if (!r.Ok) AvisoError = r.Mensaje;
            else if (r.Mensaje != "Mensaje enviado.") Aviso = r.Mensaje;
            return RedirectToPage(new { t = id });
        }

        public async Task<IActionResult> OnPostCerrarAsync(int id)
        {
            if (IdCliente is not int idCli) return RedirectToPage();
            var r = await _soporte.CambiarEstadoAsync(id, TicketSoporte.EstadoCerrado, idCli);
            if (r.Ok) Aviso = "Marcamos tu consulta como resuelta. Si necesitas algo más, escribe aquí mismo y se reabre."; else AvisoError = r.Mensaje;
            return RedirectToPage(new { t = id });
        }
    }
}
