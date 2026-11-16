using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System.Collections.Generic;
using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    public class DocumentoArchivoDto
    {
        public string Nombre { get; set; } = "";
        public string? Ruta { get; set; }
        public string Codigo { get; set; } = "";
        public int IdDocumento { get; set; }
        public string IconClass { get; set; } = "icon-blue";
        public string IconBi { get; set; } = "bi-file-earmark";
    }

    public class DocumentosModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly IWebHostEnvironment _env;

        public DocumentosModel(AtilsonContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
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
                .AsSplitQuery()
                .Include(o => o.IdPuertoOrigenNavigation)
                .Include(o => o.IdPuertoDestinoNavigation)
                .Include(o => o.OperacionesDocumentales)
                .Include(o => o.Unidadestecnicas)
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

        // La lista y la descarga usan el mismo catálogo de campos documentales reales.
        // No se generan archivos ficticios ni se devuelve una ruta que no exista en BD.
        public static string? RutaDocumento(OperacionesDocumentale d, string tipo) => tipo switch
        {
            "matriz" => d.EvidenciaMatriz,
            "bl" => d.RutaBL,
            "origen" => UltimaEvidencia(d.EvidenciaOrigen1, d.EvidenciaOrigen2, d.EvidenciaOrigen3, d.EvidenciaOrigen4) ?? d.RutaCertificadoOrigen,
            "fitosanitario" => UltimaEvidencia(d.EvidenciaFito1, d.EvidenciaFito2, d.EvidenciaFito3, d.EvidenciaFito4),
            "sanitario" => UltimaEvidencia(d.EvidenciaSanitario1, d.EvidenciaSanitario2, d.EvidenciaSanitario3, d.EvidenciaSanitario4),
            "captura" => UltimaEvidencia(d.EvidenciaCap1, d.EvidenciaCap2, d.EvidenciaCap3, d.EvidenciaCap4),
            "factura" => d.RutaFactura,
            "guia" => d.RutaGuia,
            "packing" => d.RutaPacking,
            "instructivo" => d.RutaInstructivo,
            "booking" => d.RutaBookingAtilson,
            "libreventa" => d.RutaLibreVenta,
            "extra" => d.RutaCertExtra,
            "fullset" => d.RutaFullSet,
            "courier" => d.RutaDhl,
            "capturaaga" => d.RutaCapturaAga,
            "coa" => d.EvidenciaCoa1,
            "dt" => d.EvidenciaDt1,
            "codaut" => UltimaEvidencia(d.EvidenciaCod1, d.EvidenciaCod2, d.EvidenciaCod3, d.EvidenciaCod4),
            "clave" => UltimaEvidencia(d.EvidenciaCla1, d.EvidenciaCla2, d.EvidenciaCla3, d.EvidenciaCla4),
            "neppex" => UltimaEvidencia(d.EvidenciaNep1, d.EvidenciaNep2, d.EvidenciaNep3, d.EvidenciaNep4),
            _ => null
        };

        // No ofrecer enlaces rotos si la BD conserva una ruta de un archivo que no
        // se copió al servidor. Solo admitimos tipos seguros para mostrar/descargar.
        private string? ResolverRutaFisica(string? ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta)) return null;
            try
            {
                string local = Uri.UnescapeDataString(ruta.Split('?', '#')[0]).Replace('\\', '/');
                if (!local.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return null;
                string baseUploads = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads")) + Path.DirectorySeparatorChar;
                string fisica = Path.GetFullPath(Path.Combine(_env.WebRootPath, local.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
                if (!fisica.StartsWith(baseUploads, StringComparison.OrdinalIgnoreCase)) return null;
                string ext = Path.GetExtension(fisica).ToLowerInvariant();
                if (ext is not (".pdf" or ".jpg" or ".jpeg" or ".png" or ".webp" or ".tif" or ".tiff"
                            or ".xlsx" or ".xls" or ".docx" or ".doc" or ".zip"))
                    return null;
                return System.IO.File.Exists(fisica) ? fisica : null;
            }
            catch { return null; }
        }

        public bool ArchivoExiste(string? ruta) => ResolverRutaFisica(ruta) != null;

        // Solo descarga documentos asociados a una operación DEL cliente autenticado.
        // El usuario no puede pasar una ruta arbitraria: únicamente un tipo permitido y un ID.
        public async Task<IActionResult> OnGetArchivoAsync(int idOperacion, int idDocumento, string? tipo, bool descargar = false)
        {
            var claim = User.FindFirst("IdCliente")?.Value;
            if (!int.TryParse(claim, out var idCliente) || idCliente <= 0) return Forbid();
            if (idOperacion <= 0 || idDocumento <= 0 || string.IsNullOrWhiteSpace(tipo)) return NotFound();

            var autorizado = await _context.Operaciones.AsNoTracking()
                .AnyAsync(o => o.Id == idOperacion && o.IdCliente == idCliente && !o.IsDeleted && !o.EsHistorica);
            if (!autorizado) return NotFound();

            var d = await _context.OperacionesDocumentales.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == idDocumento && x.IdOperacion == idOperacion);
            if (d == null) return NotFound();

            string? ruta = RutaDocumento(d, tipo.ToLowerInvariant());
            if (string.IsNullOrWhiteSpace(ruta)) return NotFound();

            // Validación idéntica a la del listado: ruta local, extensión permitida y archivo real.
            var archivo = ResolverRutaFisica(ruta);
            if (archivo == null) return NotFound();

            string extension = Path.GetExtension(archivo).ToLowerInvariant();
            string mime = extension switch
            {
                ".pdf" => "application/pdf", ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png", ".webp" => "image/webp", ".tif" or ".tiff" => "image/tiff",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xls" => "application/vnd.ms-excel",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword", ".zip" => "application/zip",
                _ => ""
            };
            if (mime.Length == 0) return NotFound();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "private, no-store";
            return new PhysicalFileResult(archivo, mime)
            {
                FileDownloadName = descargar ? Path.GetFileName(archivo) : null,
                EnableRangeProcessing = true
            };
        }

        // La última reemisión con archivo es la versión que ve el cliente.
        public static string? UltimaEvidencia(params string?[] evidencias)
            => evidencias.LastOrDefault(e => !string.IsNullOrWhiteSpace(e));

        public List<DocumentoArchivoDto> ArchivosDisponibles(OperacionesDocumentale d, string? unidad = null)
        {
            string sufijo = string.IsNullOrWhiteSpace(unidad) ? "" : " · " + unidad;
            DocumentoArchivoDto Item(string nombre, string codigo, string icono, string clase = "icon-blue") => new()
            {
                Nombre = nombre + sufijo, Codigo = codigo,
                Ruta = ArchivoExiste(RutaDocumento(d, codigo)) ? RutaDocumento(d, codigo) : null,
                IdDocumento = d.Id, IconBi = icono, IconClass = clase
            };
            return new List<DocumentoArchivoDto>
            {
                Item("Matriz documental", "matriz", "bi-file-earmark-text"),
                Item("Bill of Lading (MBL)", "bl", "bi-file-earmark-pdf-fill", "icon-red"),
                Item("Certificado de Origen", "origen", "bi-globe-americas", "icon-green"),
                Item("Certificado Fitosanitario (SAG)", "fitosanitario", "bi-tree-fill", "icon-green"),
                Item("Certificado Sanitario (Sernapesca)", "sanitario", "bi-droplet-fill", "icon-green"),
                Item("Certificado de Captura", "captura", "bi-bezier2", "icon-green"),
                Item("Factura de Exportación", "factura", "bi-receipt"),
                Item("Guía de Despacho", "guia", "bi-truck"),
                Item("Packing List", "packing", "bi-list-check"),
                Item("Instructivo", "instructivo", "bi-file-earmark-text"),
                Item("Booking Atilson", "booking", "bi-ticket-detailed"),
                Item("Certificado Libre Venta", "libreventa", "bi-patch-check-fill", "icon-green"),
                Item("Certificado Extra (HACCP)", "extra", "bi-shield-check", "icon-green"),
                Item("Full Set Documental", "fullset", "bi-file-earmark-medical-fill"),
                Item("DHL / Courier", "courier", "bi-box-seam"),
                Item("Captura Agencia", "capturaaga", "bi-file-earmark-check"),
                Item("COA", "coa", "bi-file-earmark-medical", "icon-green"),
                Item("DT", "dt", "bi-file-earmark-text", "icon-green"),
                Item("CODAUT", "codaut", "bi-file-earmark-check", "icon-green"),
                Item("Clave", "clave", "bi-file-earmark-check", "icon-green"),
                Item("NEPPEX", "neppex", "bi-file-earmark-check", "icon-green")
            };
        }
    }
}
