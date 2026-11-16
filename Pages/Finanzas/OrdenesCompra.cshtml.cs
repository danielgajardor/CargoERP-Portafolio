using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Finanzas
{
    public class OrdenesCompraModel : PageModel
    {
        private readonly AtilsonContext _context;

        public OrdenesCompraModel(AtilsonContext context)
        {
            _context = context;
        }

        public IList<Operacione> ListaOrdenes { get; set; } = default!;

        [BindProperty(SupportsGet = true)]
        public string? SearchString { get; set; }
        [BindProperty(SupportsGet = true)]
        public int Pagina { get; set; } = 1;
        public const int TamanoPagina = 20;
        public int TotalOperaciones { get; set; }
        public int TotalPaginas { get; set; }

        private static int? UnidadDeConcepto(string? concepto)
        {
            var m = System.Text.RegularExpressions.Regex.Match(concepto ?? "", @"\(U(\d+)\)");
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }

        public string ResolverProveedor(Operacione op, TransaccionesFinanciera tx)
        {
            string? directo = tx.IdProveedorNavigation?.NombreProveedor;
            string grupo = (tx.GrupoCobro ?? "").ToUpperInvariant();
            if (grupo.Contains("TERREST") || grupo.Contains("TRANSP"))
            {
                int? unidad = UnidadDeConcepto(tx.Concepto);
                string? empresa = op.OperacionesTerrestres?
                    .FirstOrDefault(t => t.IdUnidadTecnica == unidad)?.EmpresaTransporte
                    ?? (unidad == null ? op.OperacionesTerrestres?.FirstOrDefault(t => t.IdUnidadTecnica == null)?.EmpresaTransporte : null)
                    ?? directo;
                return string.IsNullOrWhiteSpace(empresa) ? "PROVEEDOR SIN ASIGNAR" : empresa.Trim();
            }
            return string.IsNullOrWhiteSpace(directo) ? "PROVEEDOR SIN ASIGNAR" : directo.Trim();
        }

        public async Task OnGetAsync()
        {
            var query = _context.Operaciones
                .AsNoTracking()
                .AsSplitQuery()
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.TransaccionesFinancieras)
                    .ThenInclude(t => t.IdProveedorNavigation)
                .Where(o => !o.IsDeleted && !o.EsHistorica && o.TransaccionesFinancieras.Any(t =>
                    t.TipoMovimiento == "EGRESO" &&
                    t.GrupoCobro != null &&
                    (t.GrupoCobro.ToUpper().Contains("TERRESTRE") ||
                     t.GrupoCobro.ToUpper().Contains("TRANSPORTE") ||
                     t.GrupoCobro.ToUpper().Contains("EXTRACOSTO"))))
                .AsQueryable();

            if (!string.IsNullOrEmpty(SearchString))
            {
                string s = SearchString.ToLower();
                query = query.Where(o =>
                    (o.NumeroBooking != null && o.NumeroBooking.ToLower().Contains(s)) ||
                    (o.IdClienteNavigation != null && o.IdClienteNavigation.RazonSocial != null && o.IdClienteNavigation.RazonSocial.ToLower().Contains(s)) ||
                    o.OperacionesTerrestres.Any(t => t.EmpresaTransporte != null && t.EmpresaTransporte.ToLower().Contains(s)) ||
                    o.TransaccionesFinancieras.Any(t =>
                        (t.IdProveedorNavigation != null && t.IdProveedorNavigation.NombreProveedor.ToLower().Contains(s)) ||
                        (t.NumeroOrdenCompra != null && t.NumeroOrdenCompra.ToLower().Contains(s)))
                );
            }

            TotalOperaciones = await query.CountAsync();
            TotalPaginas = System.Math.Max(1, (int)System.Math.Ceiling(TotalOperaciones / (double)TamanoPagina));
            Pagina = System.Math.Clamp(Pagina, 1, TotalPaginas);

            ListaOrdenes = await query.OrderByDescending(o => o.Id)
                .Skip((Pagina - 1) * TamanoPagina)
                .Take(TamanoPagina)
                .ToListAsync();
        }

        // ==================== ASIGNACIÓN AUTOMÁTICA DEL N° DE OC ====================
        public async Task<IActionResult> OnPostAsignarOCAsync(string transaccionesIds)
        {
            if (!string.IsNullOrWhiteSpace(transaccionesIds))
            {
                var ids = transaccionesIds.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x, out var id) ? id : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
                if (!ids.Any())
                {
                    TempData["ErrorMsg"] = "No se recibieron conceptos válidos para generar la OC.";
                    return RedirectToPage("./OrdenesCompra", new { SearchString, Pagina });
                }

                var transacciones = await _context.TransaccionesFinancieras
                    .Where(t => ids.Contains(t.Id) &&
                                t.TipoMovimiento == "EGRESO" &&
                                t.GrupoCobro != null &&
                                (t.GrupoCobro.Contains("Terrestre") || t.GrupoCobro.Contains("Transporte") || t.GrupoCobro.Contains("Extracosto")))
                    .ToListAsync();

                if (transacciones.Count != ids.Count || transacciones.Select(t => t.IdOperacion).Distinct().Count() != 1)
                {
                    TempData["ErrorMsg"] = "La OC contiene conceptos inválidos o de bookings distintos.";
                    return RedirectToPage("./OrdenesCompra", new { SearchString, Pagina });
                }

                if (transacciones.Any(t => !string.IsNullOrWhiteSpace(t.NumeroOrdenCompra)))
                {
                    TempData["ErrorMsg"] = "Uno o más conceptos ya tienen una OC asignada.";
                    return RedirectToPage("./OrdenesCompra", new { SearchString, Pagina });
                }

                // CANDADO DE SEGURIDAD: no se emite OC si el valor de transporte sigue
                // "Por asignar" (Comercial no ha cerrado la tarifa). Réplica server-side
                // del bloqueo visual del botón, para que no se pueda saltar manipulando
                // el HTML.
                if (transacciones.Any(t => t.MontoNeto == 0m && !t.TarifaManual && t.EstadoFila != "NO FACTURABLE" && (t.JustificacionManual == null || !t.JustificacionManual.StartsWith("Sin costo"))))
                {
                    TempData["ErrorMsg"] = "No se puede generar la OC: falta el valor de transporte. Comercial debe cerrar la tarifa antes de emitir la orden de compra.";
                    return RedirectToPage("./OrdenesCompra", new { SearchString, Pagina });
                }

                // 1. Buscar el correlativo más alto de OC actuales
                var ultimasOc = await _context.TransaccionesFinancieras
                    .Where(t => t.NumeroOrdenCompra != null && t.NumeroOrdenCompra.StartsWith("OC-"))
                    .Select(t => t.NumeroOrdenCompra)
                    .ToListAsync();

                int maxCorrelativo = 2000;
                foreach (var oc in ultimasOc)
                {
                    if (int.TryParse(oc.Replace("OC-", ""), out int num))
                    {
                        if (num > maxCorrelativo) maxCorrelativo = num;
                    }
                }

                // 2. Generar el nuevo folio
                string nuevaOc = $"OC-{maxCorrelativo + 1}";

                // 3. Asignarle el mismo número correlativo a toda la agrupación
                foreach (var tx in transacciones)
                {
                    tx.NumeroOrdenCompra = nuevaOc;
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./OrdenesCompra", new { SearchString, Pagina });
        }
    }
}