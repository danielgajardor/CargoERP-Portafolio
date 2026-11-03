using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    public class DocumentoArchivoDto
    {
        public string Nombre { get; set; } = "";
        public string? Ruta { get; set; }
        public string IconClass { get; set; } = "icon-blue";
        public string IconBi { get; set; } = "bi-file-earmark";
    }

    public class DocumentosModel : PageModel
    {
        private readonly AtilsonContext _context;

        public DocumentosModel(AtilsonContext context)
        {
            _context = context;
        }

        public IList<Operacione> OperacionesDocumentales { get; set; } = default!;

        [BindProperty(SupportsGet = true)]
        public string? SearchString { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var claimCliente = User.FindFirst("IdCliente")?.Value;
            if (string.IsNullOrEmpty(claimCliente) || !int.TryParse(claimCliente, out int idClienteLogueado))
            {
                return RedirectToPage("/Login");
            }

            var query = _context.Operaciones
                .AsNoTracking()
                .Include(o => o.IdPuertoOrigenNavigation)
                .Include(o => o.IdPuertoDestinoNavigation)
                .Include(o => o.OperacionesDocumentales)
                .Where(o => o.IdCliente == idClienteLogueado && !o.IsDeleted && o.OperacionesDocumentales.Any());

            if (!string.IsNullOrEmpty(SearchString))
            {
                string s = SearchString.Trim();
                query = query.Where(o =>
                    (o.NumeroBooking != null && o.NumeroBooking.Contains(s)) ||
                    (o.NumeroContenedor != null && o.NumeroContenedor.Contains(s)));
            }

            OperacionesDocumentales = await query.OrderByDescending(o => o.Id).ToListAsync();

            return Page();
        }

        // La versión más reciente disponible de un certificado (Principal o la
        // última reemisión con archivo adjunto), respetando el mismo orden 1→4
        // que usa Operaciones internamente.
        public string? UltimaEvidencia(params string?[] evidencias)
            => evidencias.LastOrDefault(e => !string.IsNullOrWhiteSpace(e));

        public List<DocumentoArchivoDto> ArchivosDisponibles(OperacionesDocumentale d)
        {
            return new List<DocumentoArchivoDto>
            {
                new() { Nombre = "Bill of Lading (MBL)", Ruta = d.RutaBL, IconClass = "icon-red", IconBi = "bi-file-earmark-pdf-fill" },
                new() { Nombre = "Certificado de Origen", Ruta = UltimaEvidencia(d.EvidenciaOrigen1, d.EvidenciaOrigen2, d.EvidenciaOrigen3, d.EvidenciaOrigen4), IconClass = "icon-green", IconBi = "bi-globe-americas" },
                new() { Nombre = "Certificado Fitosanitario (SAG)", Ruta = UltimaEvidencia(d.EvidenciaFito1, d.EvidenciaFito2, d.EvidenciaFito3, d.EvidenciaFito4), IconClass = "icon-green", IconBi = "bi-tree-fill" },
                new() { Nombre = "Certificado Sanitario (Sernapesca)", Ruta = UltimaEvidencia(d.EvidenciaSanitario1, d.EvidenciaSanitario2, d.EvidenciaSanitario3, d.EvidenciaSanitario4), IconClass = "icon-green", IconBi = "bi-droplet-fill" },
                new() { Nombre = "Certificado de Captura", Ruta = UltimaEvidencia(d.EvidenciaCap1, d.EvidenciaCap2, d.EvidenciaCap3, d.EvidenciaCap4), IconClass = "icon-green", IconBi = "bi-bezier2" },
                new() { Nombre = "Factura de Exportación", Ruta = d.RutaFactura, IconClass = "icon-blue", IconBi = "bi-receipt" },
                new() { Nombre = "Guía de Despacho", Ruta = d.RutaGuia, IconClass = "icon-blue", IconBi = "bi-truck" },
                new() { Nombre = "Packing List", Ruta = d.RutaPacking, IconClass = "icon-blue", IconBi = "bi-list-check" },
                new() { Nombre = "Certificado Libre Venta", Ruta = d.RutaLibreVenta, IconClass = "icon-green", IconBi = "bi-patch-check-fill" },
                new() { Nombre = "Certificado Extra (HACCP)", Ruta = d.RutaCertExtra, IconClass = "icon-green", IconBi = "bi-shield-check" },
                new() { Nombre = "Full Set Documental", Ruta = d.RutaFullSet, IconClass = "icon-blue", IconBi = "bi-file-earmark-medical-fill" },
                new() { Nombre = "DHL / Courier", Ruta = d.RutaDhl, IconClass = "icon-blue", IconBi = "bi-box-seam" }
            };
        }
    }
}