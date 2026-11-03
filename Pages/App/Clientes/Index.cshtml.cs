using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;

namespace AtilsonCargoSpa.Pages.App.Clientes
{
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        public sealed class Fila
        {
            public Cliente Cliente { get; init; } = null!;
            public string? Ciudad { get; init; }
            public bool Activo => Cliente.Activo == 1;
        }

        public List<Fila> Filas { get; set; } = new();
        public int TotalActivos { get; set; }
        public int TotalInactivos { get; set; }
        public List<(string Valor, string Texto)> OpcionesCiudad { get; set; } = new();

        /// <summary>activos | inactivos | todos</summary>
        [BindProperty(SupportsGet = true)]
        public string Estado { get; set; } = "activos";

        [BindProperty(SupportsGet = true, Name = "q")]
        public string? Q { get; set; }

        [BindProperty(SupportsGet = true, Name = "f")]
        public string? F { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? Resaltar { get; set; }

        // Filtros que usan las exportaciones a Excel/PDF.
        [BindProperty(SupportsGet = true)]
        public string? SearchString { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? CiudadId { get; set; }

        public async Task OnGetAsync()
        {
            Estado = Estado is "inactivos" or "todos" ? Estado : "activos";
            var ciudades = await _context.Ciudades.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre);
            var todos = await _context.Clientes.AsNoTracking().Where(c => !c.IsDeleted).ToListAsync();
            TotalActivos = todos.Count(c => c.Activo == 1);
            TotalInactivos = todos.Count - TotalActivos;

            Filas = (Estado switch
            {
                "inactivos" => todos.Where(c => c.Activo != 1),
                "todos" => todos,
                _ => todos.Where(c => c.Activo == 1)
            })
            .Select(c => new Fila { Cliente = c, Ciudad = c.IdCiudad.HasValue ? ciudades.GetValueOrDefault(c.IdCiudad.Value) : null })
            .OrderBy(f => f.Cliente.RazonSocial)
            .ToList();

            OpcionesCiudad = Filas.Where(f => f.Ciudad != null)
                .Select(f => (f.Cliente.IdCiudad!.Value.ToString(), f.Ciudad!))
                .Distinct()
                .OrderBy(x => x.Item2)
                .ToList();
        }

        public async Task<IActionResult> OnPostCambiarEstadoAsync(int id, bool activar)
        {
            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente == null)
            {
                TempData["ToastError"] = "El cliente ya no existe.";
                return RedirectToPage("./Index", new { Estado });
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            byte nuevo = (byte)(activar ? 1 : 0);
            if (cliente.Activo != nuevo)
            {
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Estado", cliente.Activo == 1 ? "Activo" : "Inactivo", activar ? "Activo" : "Inactivo");
                cliente.Activo = nuevo;
                cliente.FechaModificacion = MaestrosAuditoria.Ahora();
                cliente.UsuarioModificador = usuario;
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntCliente, cliente.Id, cambios, usuario, "Cambio de estado desde la lista");
                await _context.SaveChangesAsync();
            }

            TempData["ToastOk"] = activar
                ? $"Cliente «{cliente.RazonSocial}» reactivado."
                : $"Cliente «{cliente.RazonSocial}» desactivado. Puedes reactivarlo desde «Inactivos».";
            return RedirectToPage("./Index", new { Estado, resaltar = cliente.Id });
        }

        // ... Mismos métodos de exportación Excel y PDF sin cambios ...
        public async Task<IActionResult> OnPostExportExcelAsync()
        {
            var query = _context.Clientes.Where(c => c.Activo == 1);

            if (!string.IsNullOrEmpty(SearchString))
            {
                query = query.Where(s => s.RazonSocial.Contains(SearchString)
                                      || s.Rut.Contains(SearchString)
                                      || s.NombreCliente.Contains(SearchString));
            }
            if (CiudadId.HasValue)
            {
                query = query.Where(x => x.IdCiudad == CiudadId);
            }

            var lista = await query.ToListAsync();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Clientes Atilson");
                var currentRow = 1;

                worksheet.Cell(currentRow, 1).Value = "RUT";
                worksheet.Cell(currentRow, 2).Value = "Razón Social";
                worksheet.Cell(currentRow, 3).Value = "Nombre Fantasía";
                worksheet.Cell(currentRow, 4).Value = "Contacto";

                var headerRange = worksheet.Range("A1:D1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2c1b4d");
                headerRange.Style.Font.FontColor = XLColor.White;

                foreach (var c in lista)
                {
                    currentRow++;
                    worksheet.Cell(currentRow, 1).Value = c.Rut;
                    worksheet.Cell(currentRow, 2).Value = c.RazonSocial;
                    worksheet.Cell(currentRow, 3).Value = c.NombreCliente;
                    worksheet.Cell(currentRow, 4).Value = c.Contacto;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Reporte_Clientes_Atilson.xlsx");
                }
            }
        }

        public async Task<IActionResult> OnPostExportPdfAsync()
        {
            var query = _context.Clientes.Where(c => c.Activo == 1);

            if (!string.IsNullOrEmpty(SearchString))
            {
                query = query.Where(s => s.RazonSocial.Contains(SearchString)
                                      || s.Rut.Contains(SearchString)
                                      || s.NombreCliente.Contains(SearchString));
            }
            if (CiudadId.HasValue)
            {
                query = query.Where(x => x.IdCiudad == CiudadId);
            }

            var lista = await query.ToListAsync();

            using (var stream = new MemoryStream())
            {
                Document pdfDoc = new Document(PageSize.LETTER.Rotate(), 25, 25, 30, 30);
                PdfWriter.GetInstance(pdfDoc, stream);
                pdfDoc.Open();

                var titleFont = FontFactory.GetFont("Arial", 16, Font.BOLD, new BaseColor(44, 27, 77));
                pdfDoc.Add(new Paragraph("ATILSON CARGO - REPORTE MAESTRO DE CLIENTES\n\n", titleFont));

                PdfPTable table = new PdfPTable(4);
                table.WidthPercentage = 100;
                table.SetWidths(new float[] { 15f, 40f, 25f, 20f });

                string[] headers = { "RUT", "Razón Social", "Nombre Fantasía", "Contacto" };
                foreach (var h in headers)
                {
                    PdfPCell cell = new PdfPCell(new Phrase(h, FontFactory.GetFont("Arial", 10, Font.BOLD, BaseColor.WHITE)));
                    cell.BackgroundColor = new BaseColor(44, 27, 77);
                    cell.Padding = 8;
                    table.AddCell(cell);
                }

                var normalFont = FontFactory.GetFont("Arial", 9);
                foreach (var c in lista)
                {
                    table.AddCell(new PdfPCell(new Phrase(c.Rut ?? "", normalFont)) { Padding = 5 });
                    table.AddCell(new PdfPCell(new Phrase(c.RazonSocial ?? "", normalFont)) { Padding = 5 });
                    table.AddCell(new PdfPCell(new Phrase(c.NombreCliente ?? "", normalFont)) { Padding = 5 });
                    table.AddCell(new PdfPCell(new Phrase(c.Contacto ?? "", normalFont)) { Padding = 5 });
                }

                pdfDoc.Add(table);
                pdfDoc.Close();
                return File(stream.ToArray(), "application/pdf", "Reporte_Clientes_Atilson.pdf");
            }
        }
    }
}