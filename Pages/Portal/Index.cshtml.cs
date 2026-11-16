using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace AtilsonCargoSpa.Pages.Portal
{
    [Authorize(Roles = "Cliente")]
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public string BookingNumber { get; set; }

        public Operacione OperacionTracking { get; set; }
        public List<Operacione> MisOperaciones { get; set; } = new List<Operacione>();
        public SelectList NavierasList { get; set; }

        public int EmbarquesActivos { get; set; }
        public int EnTransito { get; set; }
        public int Entregados { get; set; }
        public string EmpresaCliente { get; set; }

        // Portal antiguo (nov-2026): el formulario "Solicitud de Cotización" de esta página
        // apuntaba a un handler que no existía (404). La cotización vive en el Portal Cliente;
        // se lleva al cliente allí con el origen y destino que escribió.
        public IActionResult OnPostCotizar(string? Origen, string? Destino)
            => RedirectToPage("/PortalCliente/SolicitarCotizacion", new { pol = Origen?.Trim(), pod = Destino?.Trim() });

        public async Task<IActionResult> OnGetAsync()
        {
            string idClienteStr = User.FindFirst("IdCliente")?.Value ?? "0";
            if (!int.TryParse(idClienteStr, out int idClienteLogueado) || idClienteLogueado <= 0)
                return Forbid();

            var clienteInfo = await _context.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == idClienteLogueado && !c.IsDeleted && c.Activo == 1);
            if (clienteInfo == null) return Forbid();
            EmpresaCliente = clienteInfo.RazonSocial ?? "Empresa";

            // CARGAR NAVIERAS PARA EL MÓDULO DE COTIZACIÓN
            var navieras = await _context.Navieras
                .Where(n => !n.IsDeleted)
                .OrderBy(n => n.NombreNaviera)
                .ToListAsync();
            NavierasList = new SelectList(navieras, "Id", "NombreNaviera");

            var queryOperaciones = _context.Operaciones
                .Include(o => o.IdPuertoOrigenNavigation)
                .Include(o => o.IdPuertoDestinoNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.IdClienteNavigation)
                .Where(o => !o.IsDeleted && !o.EsHistorica);

            queryOperaciones = queryOperaciones.Where(o => o.IdCliente == idClienteLogueado);

            MisOperaciones = await queryOperaciones
                .OrderByDescending(o => o.Id)
                .Take(50)
                .ToListAsync();

            EmbarquesActivos = MisOperaciones.Count(o => o.EstadoWorkflow != "FINALIZADO" && o.EstadoWorkflow != "CONTENEDOR RETIRADO" && o.EstadoWorkflow != "CANCELADO");
            EnTransito = MisOperaciones.Count(o => o.EstadoWorkflow == "EMBARCADO" || o.EstadoWorkflow == "ZARPE" || o.EstadoWorkflow == "PROXIMO ARRIBO");
            Entregados = MisOperaciones.Count(o => o.EstadoWorkflow == "ARRIBADO" || o.EstadoWorkflow == "FINALIZADO" || o.EstadoWorkflow == "CONTENEDOR RETIRADO");

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