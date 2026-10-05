using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AtilsonCargoSpa.Models;
using System;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    public class SolicitarReservaModel : PageModel
    {
        private readonly AtilsonContext _context;

        public SolicitarReservaModel(AtilsonContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Cotizacion NuevaCotizacion { get; set; } = new Cotizacion();

        [BindProperty(SupportsGet = true)]
        public string OrigenQ { get; set; }

        [BindProperty(SupportsGet = true)]
        public string DestinoQ { get; set; }

        // Devuelve el IdCliente de la sesión, o null si no hay uno válido.
        // Centralizado aquí porque tanto OnGet como OnPost lo necesitan.
        private int? ObtenerIdClienteSesion()
        {
            var claimCliente = User.FindFirst("IdCliente")?.Value;
            if (!string.IsNullOrEmpty(claimCliente) && int.TryParse(claimCliente, out int idCliente))
                return idCliente;
            return null;
        }

        public IActionResult OnGet()
        {
            if (ObtenerIdClienteSesion() == null)
                return RedirectToPage("/Login");

            if (!string.IsNullOrEmpty(OrigenQ))
            {
                NuevaCotizacion.Origen = OrigenQ;
            }

            if (!string.IsNullOrEmpty(DestinoQ))
            {
                NuevaCotizacion.Destino = DestinoQ;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null)
                return RedirectToPage("/Login");

            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                NuevaCotizacion.FechaSolicitud = DateTime.Now;
                NuevaCotizacion.Estado = "NUEVA SOLICITUD";
                NuevaCotizacion.Activo = true;
                // Antes quedaba en null ("Para pruebas") — la cotización nunca
                // quedaba asociada al cliente real que la solicitó.
                NuevaCotizacion.IdCliente = idCliente.Value;

                _context.Cotizaciones.Add(NuevaCotizacion);
                await _context.SaveChangesAsync();

                TempData["SolicitudExitosa"] = "true";
                return RedirectToPage("./SolicitarReserva");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Ocurrió un error al guardar: " + ex.Message);
                return Page();
            }
        }
    }
}