using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace AtilsonCargoSpa.Pages.Portal
{
    [Authorize(Roles = "Cliente")]
    public class TrackingModel : PageModel
    {
        private readonly AtilsonContext _context;

        public TrackingModel(AtilsonContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public string BookingNumber { get; set; }

        public Operacione OperacionTracking { get; set; }
        public string EmpresaCliente { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            string idClienteStr = User.FindFirst("IdCliente")?.Value ?? "0";
            if (!int.TryParse(idClienteStr, out int idClienteLogueado) || idClienteLogueado <= 0)
                return Forbid();

            var clienteInfo = await _context.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == idClienteLogueado && !c.IsDeleted && c.Activo == 1);
            if (clienteInfo == null) return Forbid();
            EmpresaCliente = clienteInfo.RazonSocial ?? "Empresa";

            // BUSCAMOS EL BOOKING (Con o Sin Filtro de Seguridad)
            if (!string.IsNullOrWhiteSpace(BookingNumber))
            {
                var queryTracking = _context.Operaciones
                    .Include(o => o.IdPuertoOrigenNavigation)
                    .Include(o => o.IdPuertoDestinoNavigation)
                    .Include(o => o.IdNavieraNavigation)
                    .Include(o => o.Unidadestecnicas)
                    .Include(o => o.IdClienteNavigation)
                    .Where(o => o.NumeroBooking.Trim().ToUpper() == BookingNumber.Trim().ToUpper() && !o.IsDeleted && !o.EsHistorica);

                queryTracking = queryTracking.Where(o => o.IdCliente == idClienteLogueado);

                OperacionTracking = await queryTracking.FirstOrDefaultAsync();
            }

            return Page();
        }
    }
}