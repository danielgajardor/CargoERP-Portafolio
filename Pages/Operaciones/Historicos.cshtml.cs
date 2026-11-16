using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Operaciones
{
    public class HistoricosModel : PageModel
    {
        private readonly AtilsonContext _context;
        public HistoricosModel(AtilsonContext context) => _context = context;

        [BindProperty(SupportsGet = true)] public string? Buscar { get; set; }
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
        public const int TamanoPagina = 20;
        public int Total { get; set; }
        public int TotalPaginas { get; set; }
        public List<Operacione> Operaciones { get; set; } = new();

        public async Task OnGetAsync()
        {
            var q = _context.Operaciones.AsNoTracking()
                .Where(o => !o.IsDeleted && o.EsHistorica);

            if (!string.IsNullOrWhiteSpace(Buscar))
            {
                string s = Buscar.Trim();
                q = q.Where(o => o.NumeroBooking.Contains(s) ||
                    o.IdClienteNavigation.RazonSocial.Contains(s) ||
                    (o.IdNavieraNavigation != null && o.IdNavieraNavigation.NombreNaviera.Contains(s)));
            }

            Total = await q.CountAsync();
            TotalPaginas = Math.Max(1, (int)Math.Ceiling(Total / (double)TamanoPagina));
            Pagina = Math.Clamp(Pagina, 1, TotalPaginas);

            Operaciones = await q.OrderByDescending(o => o.FechaCreacion ?? o.FechaImportacionHistorica)
                .ThenByDescending(o => o.Id)
                .Skip((Pagina - 1) * TamanoPagina)
                .Take(TamanoPagina)
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.TransaccionesFinancieras)
                .ToListAsync();
        }
    }
}
