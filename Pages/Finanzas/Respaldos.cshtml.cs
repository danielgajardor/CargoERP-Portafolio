using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Finanzas
{
    public class RespaldosModel : PageModel
    {
        private readonly AtilsonContext _context;
        public RespaldosModel(AtilsonContext context) => _context = context;

        [BindProperty(SupportsGet = true)] public string? Buscar { get; set; }
        [BindProperty(SupportsGet = true)] public string? Contraparte { get; set; }
        [BindProperty(SupportsGet = true)] public string? Nave { get; set; }
        public List<string> NavesLista { get; set; } = new();
        [BindProperty(SupportsGet = true)] public string? TipoDocumento { get; set; }
        [BindProperty(SupportsGet = true)] public string? Servicio { get; set; }
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
        public const int TamanoPagina = 20;
        public int TotalBookings { get; set; }
        public int TotalPaginas { get; set; }

        public List<TransaccionesFinanciera> Documentos { get; set; } = new();
        public List<string> Servicios { get; set; } = new();

        public static string GrupoVisible(string? grupo) => PagosModel.GrupoVisible(grupo);
        public static int OrdenServicio(string? grupo) => PagosModel.OrdenServicio(grupo);

        public static string Clasificar(TransaccionesFinanciera tx)
        {
            string c = (tx.Concepto ?? "").ToUpperInvariant();
            string numero = (tx.NumeroDocumento ?? "").Trim().ToUpperInvariant();
            if (numero.StartsWith("NC-") || (c.Contains("NOTA") && c.Contains("COBRO")))
                return tx.Moneda == "USD" ? "Nota de cobro USD" : "Nota de cobro CLP";
            if (tx.IvaExento == true || ControlFinancieroModel.EsMaritimoExento(tx.Concepto, tx.IvaExento)) return "Factura exenta";
            return "Factura afecta";
        }

        private static int? UnidadDeConcepto(string? concepto)
        {
            var m = System.Text.RegularExpressions.Regex.Match(concepto ?? "", @"\(U(\d+)\)");
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }

        public string ResolverContraparte(TransaccionesFinanciera tx)
        {
            var op = tx.IdOperacionNavigation;
            if (tx.TipoMovimiento == "INGRESO")
                return op.IdClienteNavigation?.RazonSocial ?? "Cliente";

            string grupo = (tx.GrupoCobro ?? "").ToUpperInvariant();
            int? unidad = UnidadDeConcepto(tx.Concepto);
            string? nombre = tx.IdProveedorNavigation?.NombreProveedor ?? tx.ContraparteHistorica;

            if (grupo.Contains("DOCUMENTAL") || grupo.Contains("ADUANA"))
            {
                var docs = op.OperacionesDocumentales ?? new List<OperacionesDocumentale>();
                var doc = docs.FirstOrDefault(x => x.IdUnidadTecnica == unidad)
                       ?? docs.FirstOrDefault(x => x.IdUnidadTecnica == null)
                       ?? docs.FirstOrDefault();
                nombre = doc?.IdAgenciaAduanaNavigation?.NombreAgencia ?? doc?.AgenciaAduana ?? nombre;
            }
            else if (grupo.Contains("GATE") || grupo.Contains("DEPOS"))
            {
                nombre = op.IdNavieraNavigation?.AplicaTarifaGatePlana == true
                    ? op.IdNavieraNavigation.NombreNaviera
                    : op.OperacionesTerrestres?.FirstOrDefault(x => x.IdUnidadTecnica == unidad)?.DepositoRetiro
                      ?? op.OperacionesTerrestres?.FirstOrDefault(x => x.IdUnidadTecnica == null)?.DepositoRetiro
                      ?? nombre;
            }
            else if (grupo.Contains("TERREST") || grupo.Contains("TRANSP"))
                nombre = op.OperacionesTerrestres?.FirstOrDefault(x => x.IdUnidadTecnica == unidad)?.EmpresaTransporte
                      ?? op.OperacionesTerrestres?.FirstOrDefault(x => x.IdUnidadTecnica == null)?.EmpresaTransporte
                      ?? nombre;
            else if (grupo.Contains("MARIT"))
                nombre = op.IdNavieraNavigation?.NombreNaviera ?? nombre;
            else if (grupo.Contains("ALMAC") || grupo.Contains("BODEGA") || grupo.Contains("PATIO"))
                nombre = op.OperacionesAlmacenamientos?.FirstOrDefault()?.IdProveedorNavigation?.NombreProveedor ?? nombre;

            return string.IsNullOrWhiteSpace(nombre) ? "Proveedor por asignar" : nombre;
        }

        public async Task OnGetAsync()
        {
            NavesLista = await AtilsonCargoSpa.Helpers.FiltroNaveHelper.OpcionesAsync(_context);
            Servicios = (await _context.TransaccionesFinancieras.AsNoTracking().Where(x => !x.IdOperacionNavigation.IsDeleted && x.GrupoCobro != null)
                .Select(x => x.GrupoCobro).Distinct().ToListAsync())
                .Select(GrupoVisible)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(OrdenServicio).ThenBy(x => x).ToList();

            var q = _context.TransaccionesFinancieras.AsNoTracking().AsSplitQuery()
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.IdClienteNavigation)
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.IdNavieraNavigation)
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.OperacionesTerrestres)
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.OperacionesDocumentales).ThenInclude(d => d.IdAgenciaAduanaNavigation)
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.OperacionesAlmacenamientos).ThenInclude(a => a.IdProveedorNavigation)
                .Include(x => x.IdProveedorNavigation)
                .Where(x => !x.IdOperacionNavigation.IsDeleted &&
                            (x.RutaFactura != null || x.RutaComprobante != null || x.NumeroDocumento != null));

            if (!string.IsNullOrWhiteSpace(Buscar))
            {
                string s = Buscar.Trim();
                q = q.Where(x =>
                    (x.IdOperacionNavigation.NumeroBooking != null && x.IdOperacionNavigation.NumeroBooking.Contains(s)) ||
                    (x.NumeroDocumento != null && x.NumeroDocumento.Contains(s)) ||
                    (x.Concepto != null && x.Concepto.Contains(s)) ||
                    (x.IdOperacionNavigation.IdClienteNavigation != null && x.IdOperacionNavigation.IdClienteNavigation.RazonSocial != null && x.IdOperacionNavigation.IdClienteNavigation.RazonSocial.Contains(s)) ||
                    (x.IdProveedorNavigation != null && x.IdProveedorNavigation.NombreProveedor != null && x.IdProveedorNavigation.NombreProveedor.Contains(s)) ||
                    (x.ContraparteHistorica != null && x.ContraparteHistorica.Contains(s)) ||
                    (x.IdOperacionNavigation.IdNavieraNavigation != null && x.IdOperacionNavigation.IdNavieraNavigation.NombreNaviera.Contains(s)) ||
                    x.IdOperacionNavigation.OperacionesTerrestres.Any(t => t.EmpresaTransporte != null && t.EmpresaTransporte.Contains(s)) ||
                    x.IdOperacionNavigation.OperacionesDocumentales.Any(d =>
                        (d.IdAgenciaAduanaNavigation != null && d.IdAgenciaAduanaNavigation.NombreAgencia.Contains(s)) ||
                        (d.AgenciaAduana != null && d.AgenciaAduana.Contains(s))) ||
                    x.IdOperacionNavigation.OperacionesAlmacenamientos.Any(a =>
                        a.IdProveedorNavigation != null && a.IdProveedorNavigation.NombreProveedor.Contains(s)));
            }
            string? nave = AtilsonCargoSpa.Helpers.FiltroNaveHelper.Normalizar(Nave);
            if (nave != null) q = q.Where(x => x.IdOperacionNavigation.Nave != null && x.IdOperacionNavigation.Nave.Contains(nave));
            if (Contraparte == "CLIENTE") q = q.Where(x => x.TipoMovimiento == "INGRESO");
            else if (Contraparte == "PROVEEDOR") q = q.Where(x => x.TipoMovimiento == "EGRESO");
            if (!string.IsNullOrWhiteSpace(Servicio))
            {
                Servicio = GrupoVisible(Servicio);
                if (Servicio == "Marítimo")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Marít") || x.GrupoCobro.Contains("Marit")));
                else if (Servicio == "Transporte")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Terrest") || x.GrupoCobro.Contains("Transp")));
                else if (Servicio == "Documental")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Document") || x.GrupoCobro.Contains("Aduana")));
                else if (Servicio == "Gate-Out/In")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Gate") || x.GrupoCobro.Contains("Depósito") || x.GrupoCobro.Contains("Deposito")));
                else if (Servicio == "Almacenamiento")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Almacen") || x.GrupoCobro.Contains("Bodega") || x.GrupoCobro.Contains("Patio")));
                else
                    q = q.Where(x => x.GrupoCobro == Servicio);
            }

            if (string.IsNullOrWhiteSpace(TipoDocumento))
            {
                var idsQuery = q.Select(x => x.IdOperacion).Distinct();
                TotalBookings = await idsQuery.CountAsync();
                TotalPaginas = System.Math.Max(1, (int)System.Math.Ceiling(TotalBookings / (double)TamanoPagina));
                Pagina = System.Math.Clamp(Pagina, 1, TotalPaginas);

                var idsPagina = await idsQuery.OrderByDescending(id => id)
                    .Skip((Pagina - 1) * TamanoPagina)
                    .Take(TamanoPagina)
                    .ToListAsync();

                Documentos = await q.Where(x => idsPagina.Contains(x.IdOperacion))
                    .OrderByDescending(x => x.IdOperacion)
                    .ThenByDescending(x => x.FechaModificacion ?? x.FechaCreacion)
                    .ThenByDescending(x => x.Id)
                    .ToListAsync();
            }
            else
            {
                // La clasificación de exento/afecto usa reglas de negocio compartidas con
                // Control Financiero; por eso se aplica después de materializar el conjunto
                // ya filtrado por búsqueda/contraparte/servicio.
                var todos = await q.OrderByDescending(x => x.IdOperacion)
                    .ThenByDescending(x => x.FechaModificacion ?? x.FechaCreacion)
                    .ThenByDescending(x => x.Id)
                    .ToListAsync();
                todos = todos.Where(x => Clasificar(x) == TipoDocumento).ToList();

                var ids = todos.Select(x => x.IdOperacion).Distinct().OrderByDescending(x => x).ToList();
                TotalBookings = ids.Count;
                TotalPaginas = System.Math.Max(1, (int)System.Math.Ceiling(TotalBookings / (double)TamanoPagina));
                Pagina = System.Math.Clamp(Pagina, 1, TotalPaginas);
                var idsPagina = ids.Skip((Pagina - 1) * TamanoPagina).Take(TamanoPagina).ToHashSet();
                Documentos = todos.Where(x => idsPagina.Contains(x.IdOperacion)).ToList();
            }
        }
    }
}
