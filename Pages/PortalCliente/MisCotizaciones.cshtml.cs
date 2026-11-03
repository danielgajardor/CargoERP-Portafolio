using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    // El cliente ve sus cotizaciones y la respuesta del ejecutivo (oct-2026).
    // Siempre acotado al IdCliente del claim de sesión — mismo patrón anti-IDOR
    // que el resto del Portal Cliente: aunque llegue ?id= de otra empresa, no
    // aparece porque la consulta ya filtra por cliente.
    public class MisCotizacionesModel : PageModel
    {
        private readonly AtilsonContext _context;

        public MisCotizacionesModel(AtilsonContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public int? Id { get; set; }

        public List<Cotizacion> Cotizaciones { get; set; } = new();

        // Próximas salidas de la ruta de cada cotización marítima respondida
        // (conexión Cotización → Reserva que pidió Cristián: "Reservar esta salida").
        public Dictionary<int, List<ItinerarioNave>> SalidasPorCotizacion { get; set; } = new();

        // Reservas que el cliente ya pidió a partir de cada cotización.
        public Dictionary<int, int> ReservasPorCotizacion { get; set; } = new();

        public enum EstadoVista { Pendiente, Respondida, Vencida, Cerrada }

        public static EstadoVista Estado(Cotizacion c)
        {
            if (c.Activo != true) return EstadoVista.Cerrada;
            if (c.Estado != Cotizacion.EstadoRespondida) return EstadoVista.Pendiente;
            if (c.ValidaHasta != null && c.ValidaHasta.Value < DateOnly.FromDateTime(DateTime.Today)) return EstadoVista.Vencida;
            return EstadoVista.Respondida;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var claim = User.FindFirst("IdCliente")?.Value;
            if (!int.TryParse(claim, out int idCliente))
                return RedirectToPage("/Login");

            Cotizaciones = await _context.Cotizaciones.AsNoTracking()
                .Include(c => c.IdPuertoOrigenNavigation)
                .Include(c => c.IdPuertoDestinoNavigation)
                .Include(c => c.IdPuertoTerrestreNavigation)
                .Where(c => c.IdCliente == idCliente && c.TipoSolicitud == Cotizacion.TipoCotizacion)
                .OrderByDescending(c => c.FechaSolicitud)
                .Take(100)
                .ToListAsync();

            var ids = Cotizaciones.Select(c => c.Id).ToList();
            ReservasPorCotizacion = await _context.Cotizaciones.AsNoTracking()
                .Where(c => c.IdCliente == idCliente && c.IdCotizacionOrigen != null && ids.Contains(c.IdCotizacionOrigen.Value))
                .GroupBy(c => c.IdCotizacionOrigen!.Value)
                .Select(g => new { g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.N);

            var conRuta = Cotizaciones
                .Where(c => Estado(c) == EstadoVista.Respondida && c.IdPuertoOrigenNavigation != null && c.IdPuertoDestinoNavigation != null)
                .ToList();
            if (conRuta.Any())
            {
                var proximas = await ItinerariosHelper.ProximasSalidasAsync(_context, max: 500);
                foreach (var c in conRuta)
                {
                    string kPol = PuertosHelper.Clave(c.IdPuertoOrigenNavigation!);
                    string kPod = PuertosHelper.Clave(c.IdPuertoDestinoNavigation!);
                    var salidas = proximas
                        .Where(s => s.IdPuertoOrigenNavigation != null && s.IdPuertoDestinoNavigation != null
                                    && PuertosHelper.Clave(s.IdPuertoOrigenNavigation) == kPol
                                    && PuertosHelper.Clave(s.IdPuertoDestinoNavigation) == kPod)
                        .ToList();
                    if (salidas.Any()) SalidasPorCotizacion[c.Id] = salidas;
                }
            }

            return Page();
        }
    }
}
