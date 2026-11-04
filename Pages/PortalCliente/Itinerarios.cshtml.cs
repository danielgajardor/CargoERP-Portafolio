using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    // Itinerarios de naves para el cliente, estilo buscador de vuelos (oct-2026,
    // minuta Portal Web de Clientes). Muestra las salidas de las próximas 5
    // semanas que Operaciones carga en Operaciones/Itinerarios; cada tarjeta
    // lleva a Solicitar Reserva con esa salida ya elegida.
    public class ItinerariosModel : PageModel
    {
        private readonly AtilsonContext _context;

        public ItinerariosModel(AtilsonContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)] public int? Pol { get; set; }
        [BindProperty(SupportsGet = true)] public int? Pod { get; set; }
        [BindProperty(SupportsGet = true)] public int? Naviera { get; set; }

        public List<ItinerarioNave> Salidas { get; set; } = new();
        public List<Puerto> OpcionesPol { get; set; } = new();
        public List<Puerto> OpcionesPod { get; set; } = new();
        public List<Naviera> OpcionesNaviera { get; set; } = new();
        public int TotalProximas { get; set; }
        public string? NombrePol { get; set; }
        public string? NombrePod { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!int.TryParse(User.FindFirst("IdCliente")?.Value, out _))
                return RedirectToPage("/Login");

            // Una sola consulta de las próximas salidas; las opciones de los
            // filtros salen de ahí, así el cliente solo elige rutas que tienen
            // salidas (no busca a ciegas una ruta vacía).
            var todas = await ItinerariosHelper.ProximasSalidasAsync(_context, max: 500);
            TotalProximas = todas.Count;

            OpcionesPol = todas.Where(s => s.IdPuertoOrigenNavigation != null)
                .Select(s => s.IdPuertoOrigenNavigation!)
                .GroupBy(PuertosHelper.Clave).Select(g => g.OrderBy(p => p.Id).First())
                .OrderBy(p => PuertosHelper.EsChileno(p) ? 0 : 1).ThenBy(p => p.NombrePuerto).ToList();
            OpcionesPod = todas.Where(s => s.IdPuertoDestinoNavigation != null)
                .Select(s => s.IdPuertoDestinoNavigation!)
                .GroupBy(PuertosHelper.Clave).Select(g => g.OrderBy(p => p.Id).First())
                .OrderBy(p => p.Pais).ThenBy(p => p.NombrePuerto).ToList();
            OpcionesNaviera = todas.Where(s => s.IdNavieraNavigation != null)
                .Select(s => s.IdNavieraNavigation!)
                .GroupBy(n => n.Id).Select(g => g.First())
                .OrderBy(n => n.NombreNaviera).ToList();

            // Filtro por puerto equivalente (mismo nombre+país), no por Id exacto:
            // el puerto puede venir de una cotización con el otro Id duplicado.
            var puertoPol = Pol == null ? null : await _context.Puertos.FindAsync(Pol.Value);
            var puertoPod = Pod == null ? null : await _context.Puertos.FindAsync(Pod.Value);
            NombrePol = puertoPol?.NombrePuerto;
            NombrePod = puertoPod?.NombrePuerto;
            string? clavePol = puertoPol == null ? null : PuertosHelper.Clave(puertoPol);
            string? clavePod = puertoPod == null ? null : PuertosHelper.Clave(puertoPod);
            // Para que el filtro quede marcado en el <select> aunque llegue el Id duplicado.
            if (clavePol != null) Pol = OpcionesPol.FirstOrDefault(p => PuertosHelper.Clave(p) == clavePol)?.Id ?? Pol;
            if (clavePod != null) Pod = OpcionesPod.FirstOrDefault(p => PuertosHelper.Clave(p) == clavePod)?.Id ?? Pod;

            Salidas = todas
                .Where(s => Pol == null || (clavePol != null && s.IdPuertoOrigenNavigation != null && PuertosHelper.Clave(s.IdPuertoOrigenNavigation) == clavePol))
                .Where(s => Pod == null || (clavePod != null && s.IdPuertoDestinoNavigation != null && PuertosHelper.Clave(s.IdPuertoDestinoNavigation) == clavePod))
                .Where(s => Naviera == null || s.IdNaviera == Naviera)
                .ToList();

            return Page();
        }
    }
}
