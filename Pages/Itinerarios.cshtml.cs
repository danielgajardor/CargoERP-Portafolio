using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages
{
    // Itinerarios públicos (nov-2026): cualquier persona ve las salidas de las próximas
    // semanas sin iniciar sesión. Son las mismas que Operaciones carga en
    // Operaciones/Itinerarios y que ve el cliente en el portal; "Reservar" lleva a
    // PortalCliente/SolicitarReserva, que pide iniciar sesión.
    public class ItinerariosModel : PageModel
    {
        private readonly AtilsonContext _context;
        public ItinerariosModel(AtilsonContext context) { _context = context; }

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
        public bool SinDatos { get; set; }

        public async Task OnGetAsync()
        {
            List<ItinerarioNave> todas;
            try { todas = await ItinerariosHelper.ProximasSalidasAsync(_context, max: 500); }
            catch { SinDatos = true; return; } // sin conexión a la base: la página igual se muestra
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

            var puertoPol = Pol == null ? null : OpcionesPol.FirstOrDefault(p => p.Id == Pol) ?? await _context.Puertos.FindAsync(Pol.Value);
            var puertoPod = Pod == null ? null : OpcionesPod.FirstOrDefault(p => p.Id == Pod) ?? await _context.Puertos.FindAsync(Pod.Value);
            NombrePol = puertoPol?.NombrePuerto;
            NombrePod = puertoPod?.NombrePuerto;
            string? clavePol = puertoPol == null ? null : PuertosHelper.Clave(puertoPol);
            string? clavePod = puertoPod == null ? null : PuertosHelper.Clave(puertoPod);

            Salidas = todas
                .Where(s => Pol == null || (clavePol != null && s.IdPuertoOrigenNavigation != null && PuertosHelper.Clave(s.IdPuertoOrigenNavigation) == clavePol))
                .Where(s => Pod == null || (clavePod != null && s.IdPuertoDestinoNavigation != null && PuertosHelper.Clave(s.IdPuertoDestinoNavigation) == clavePod))
                .Where(s => Naviera == null || s.IdNaviera == Naviera)
                .ToList();
        }
    }
}
