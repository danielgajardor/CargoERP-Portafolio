using ClosedXML.Excel;
using DocumentFormat.OpenXml.Drawing.Diagrams;
using DocumentFormat.OpenXml.Vml.Spreadsheet;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AtilsonCargoSpa.Services
{
    public class LiquidacionLineaDto
    {
        public string Concepto { get; set; } = "";
        public string? Contraparte { get; set; }
        public string? NumeroDocumento { get; set; }
        public bool Pagado { get; set; }
        public string Moneda { get; set; } = "CLP";
        public decimal MontoNeto { get; set; }
    }

    public class LiquidacionOperacionDto
    {
        public string TituloDocumento { get; set; } = "LIQUIDACIÓN";
        public string NumeroBooking { get; set; } = "";
        public string LabelContraparte { get; set; } = "Cliente";
        public string NombreContraparte { get; set; } = "";
        public string Naviera { get; set; } = "";
        public string Contenedor { get; set; } = "";
        public string Sello { get; set; } = "";
        public string Planta { get; set; } = "";
        public string CondicionPago { get; set; } = "";
        public List<LiquidacionLineaDto> Base { get; set; } = new();
        public List<LiquidacionLineaDto> Extras { get; set; } = new();
        public decimal TotalUSD { get; set; }
        public decimal PagadoUSD { get; set; }
        public decimal SaldoUSD { get; set; }
        public decimal TotalCLP { get; set; }
        public decimal PagadoCLP { get; set; }
        public decimal SaldoCLP { get; set; }
    }

    public class ConsolidadoOperacionDto
    {
        public string NumeroBooking { get; set; } = "";
        public DateTime? Fecha { get; set; }
        public decimal IngresoUsd { get; set; }
        public decimal IngresoClp { get; set; }
        public decimal EgresoUsd { get; set; }
        public decimal EgresoClp { get; set; }
    }

    public class ConsolidadoClienteDto
    {
        public string ClienteNombre { get; set; } = "";
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public List<ConsolidadoOperacionDto> Operaciones { get; set; } = new();
        public decimal TotalIngresoUsd => Operaciones.Sum(o => o.IngresoUsd);
        public decimal TotalIngresoClp => Operaciones.Sum(o => o.IngresoClp);
        public decimal TotalEgresoUsd => Operaciones.Sum(o => o.EgresoUsd);
        public decimal TotalEgresoClp => Operaciones.Sum(o => o.EgresoClp);
    }

    public class EstadoCuentaOperacionDto
    {
        public string NumeroBooking { get; set; } = "";
        public DateTime? Fecha { get; set; }
        public decimal TotalUsd { get; set; }
        public decimal PagadoUsd { get; set; }
        public decimal SaldoUsd { get; set; }
        public decimal TotalClp { get; set; }
        public decimal PagadoClp { get; set; }
        public decimal SaldoClp { get; set; }
    }

    public class EstadoCuentaClienteDto
    {
        public string ClienteNombre { get; set; } = "";
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public List<EstadoCuentaOperacionDto> Operaciones { get; set; } = new();
        public decimal TotalUsd => Operaciones.Sum(o => o.TotalUsd);
        public decimal PagadoUsd => Operaciones.Sum(o => o.PagadoUsd);
        public decimal SaldoUsd => Operaciones.Sum(o => o.SaldoUsd);
        public decimal TotalClp => Operaciones.Sum(o => o.TotalClp);
        public decimal PagadoClp => Operaciones.Sum(o => o.PagadoClp);
        public decimal SaldoClp => Operaciones.Sum(o => o.SaldoClp);
    }

    public class LiquidacionExportService
    {
        private static readonly BaseColor ColorDark = new BaseColor(15, 23, 42);
        private static readonly BaseColor ColorAccent = new BaseColor(2, 132, 199);
        private static readonly BaseColor ColorGray = new BaseColor(100, 116, 139);
        private static readonly BaseColor ColorLight = new BaseColor(248, 250, 252);
        private static readonly BaseColor ColorGreen = new BaseColor(5, 150, 105);
        private static readonly BaseColor ColorRed = new BaseColor(220, 38, 38);

        private static Font FontTitle => new Font(Font.FontFamily.HELVETICA, 18, Font.BOLD, ColorDark);
        private static Font FontSubtitle => new Font(Font.FontFamily.HELVETICA, 8, Font.BOLD, ColorGray);
        private static Font FontBadge => new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD, ColorAccent);
        private static Font FontLabel => new Font(Font.FontFamily.HELVETICA, 7, Font.BOLD, ColorGray);
        private static Font FontValue => new Font(Font.FontFamily.HELVETICA, 9, Font.BOLD, ColorDark);
        private static Font FontTableHeader => new Font(Font.FontFamily.HELVETICA, 7, Font.BOLD, ColorGray);
        private static Font FontTableCell => new Font(Font.FontFamily.HELVETICA, 8, Font.NORMAL, ColorDark);
        private static Font FontTotalLabel => new Font(Font.FontFamily.HELVETICA, 9, Font.BOLD, BaseColor.WHITE);
        private static Font FontTotalValue => new Font(Font.FontFamily.HELVETICA, 13, Font.BOLD, BaseColor.WHITE);

        private static string FormatMonto(string moneda, decimal monto) =>
            moneda == "USD" ? $"USD {monto:N2}" : $"$ {monto:N0}";

        public byte[] GenerarPdfOperacion(LiquidacionOperacionDto dto)
        {
            using var ms = new MemoryStream();
            var doc = new Document(PageSize.A4, 40, 40, 40, 40);
            PdfWriter.GetInstance(doc, ms);
            doc.Open();

            bool esEgreso = dto.LabelContraparte != "Cliente";

            var headerTable = new PdfPTable(2) { WidthPercentage = 100 };
            headerTable.SetWidths(new float[] { 1, 1 });

            var pBrand = new PdfPCell(new Phrase("ATILSON", FontTitle)) { Border = Rectangle.NO_BORDER };
            pBrand.AddElement(new Paragraph("GLOBAL LOGISTICS SOLUTIONS", FontSubtitle));
            headerTable.AddCell(pBrand);

            var pDoc = new PdfPCell() { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
            pDoc.AddElement(new Paragraph(dto.TituloDocumento, FontBadge) { Alignment = Element.ALIGN_RIGHT });
            pDoc.AddElement(new Paragraph($"BKG: {dto.NumeroBooking}", FontValue) { Alignment = Element.ALIGN_RIGHT });
            pDoc.AddElement(new Paragraph($"Fecha Emisión: {DateTime.Now:dd-MM-yyyy}", FontSubtitle) { Alignment = Element.ALIGN_RIGHT });
            headerTable.AddCell(pDoc);
            doc.Add(headerTable);

            doc.Add(new Paragraph(" "));
            doc.Add(new Chunk(new iTextSharp.text.pdf.draw.LineSeparator(0.5f, 100, ColorGray, Element.ALIGN_CENTER, -2)));
            doc.Add(new Paragraph(" "));

            var infoTable = new PdfPTable(3) { WidthPercentage = 100, SpacingBefore = 10, SpacingAfter = 15 };
            void AddInfo(string lbl, string val)
            {
                var cell = new PdfPCell() { Padding = 8, BackgroundColor = ColorLight, BorderColor = new BaseColor(226, 232, 240) };
                cell.AddElement(new Paragraph(lbl.ToUpper(), FontLabel));
                cell.AddElement(new Paragraph(val, FontValue));
                infoTable.AddCell(cell);
            }
            AddInfo(dto.LabelContraparte, dto.NombreContraparte);
            AddInfo("Naviera Asignada", dto.Naviera);
            AddInfo("Condición", dto.CondicionPago);
            AddInfo("Contenedor / Equipo", dto.Contenedor);
            AddInfo("Sello de Seguridad", dto.Sello);
            AddInfo("Planta / Origen Carga", dto.Planta);
            doc.Add(infoTable);

            void AddSeccion(string titulo, List<LiquidacionLineaDto> lineas)
            {
                doc.Add(new Paragraph(titulo, new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD, ColorDark)) { SpacingBefore = 10, SpacingAfter = 6 });
                int cols = esEgreso ? 6 : 5;
                var t = new PdfPTable(cols) { WidthPercentage = 100 };
                t.SetWidths(esEgreso ? new float[] { 24, 20, 20, 12, 10, 14 } : new float[] { 30, 22, 15, 13, 20 });

                void Th(string txt) => t.AddCell(new PdfPCell(new Phrase(txt.ToUpper(), FontTableHeader)) { BackgroundColor = ColorLight, Padding = 6 });
                Th("Concepto");
                if (esEgreso) Th("Proveedor / Beneficiario");
                Th("Documento");
                Th("Estado");
                Th("Mon.");
                Th("Monto Neto");

                if (!lineas.Any())
                {
                    t.AddCell(new PdfPCell(new Phrase("Sin registros.", FontTableCell)) { Colspan = cols, Padding = 8, HorizontalAlignment = Element.ALIGN_CENTER });
                }
                foreach (var l in lineas)
                {
                    void Td(string txt, int align = Element.ALIGN_LEFT, BaseColor? color = null)
                    {
                        var f = color != null ? new Font(Font.FontFamily.HELVETICA, 8, Font.NORMAL, color) : FontTableCell;
                        t.AddCell(new PdfPCell(new Phrase(txt, f)) { Padding = 6, HorizontalAlignment = align });
                    }
                    Td(l.Concepto);
                    if (esEgreso) Td(l.Contraparte ?? "-");
                    Td(string.IsNullOrEmpty(l.NumeroDocumento) ? "POR EMITIR" : l.NumeroDocumento);
                    Td(l.Pagado ? "PAGADO" : "PENDIENTE", Element.ALIGN_CENTER, l.Pagado ? ColorGreen : ColorRed);
                    Td(l.Moneda, Element.ALIGN_CENTER);
                    Td(FormatMonto(l.Moneda, l.MontoNeto), Element.ALIGN_RIGHT);
                }
                doc.Add(t);
            }

            AddSeccion(esEgreso ? "1. Detalle de Costos y Servicios Base" : "1. Detalle de Fletes y Servicios Base", dto.Base);
            AddSeccion(esEgreso ? "2. Desviaciones y Extracostos (a pagar)" : "2. Desviaciones y Extracostos (Notas de Cobro)", dto.Extras);

            var totalsTable = new PdfPTable(1) { WidthPercentage = 100, SpacingBefore = 15 };
            var totalsCell = new PdfPCell() { BackgroundColor = ColorDark, Padding = 15 };

            if (dto.TotalUSD > 0)
            {
                totalsCell.AddElement(new Paragraph($"TOTAL USD: USD {dto.TotalUSD:N2}   |   PAGADO: USD {dto.PagadoUSD:N2}", new Font(Font.FontFamily.HELVETICA, 8, Font.NORMAL, new BaseColor(203, 213, 225))));
                totalsCell.AddElement(new Paragraph($"SALDO PENDIENTE USD: USD {dto.SaldoUSD:N2}", FontTotalValue));
            }
            if (dto.TotalCLP > 0)
            {
                totalsCell.AddElement(new Paragraph($"TOTAL CLP: $ {dto.TotalCLP:N0}   |   PAGADO: $ {dto.PagadoCLP:N0}", new Font(Font.FontFamily.HELVETICA, 8, Font.NORMAL, new BaseColor(203, 213, 225))) { SpacingBefore = 6 });
                totalsCell.AddElement(new Paragraph($"SALDO PENDIENTE CLP: $ {dto.SaldoCLP:N0}", FontTotalValue));
            }
            totalsTable.AddCell(totalsCell);
            doc.Add(totalsTable);

            doc.Add(new Paragraph(" "));
            doc.Add(new Paragraph("Documento generado automáticamente por el Módulo Financiero de Atilson Cargo SpA.", new Font(Font.FontFamily.HELVETICA, 7, Font.ITALIC, ColorGray)) { Alignment = Element.ALIGN_CENTER });

            doc.Close();
            return ms.ToArray();
        }

        public byte[] GenerarExcelOperacion(LiquidacionOperacionDto dto)
        {
            bool esEgreso = dto.LabelContraparte != "Cliente";
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Liquidación");

            ws.Cell(1, 1).Value = "ATILSON CARGO SPA"; ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 16;
            ws.Cell(2, 1).Value = dto.TituloDocumento;
            ws.Cell(3, 1).Value = $"Booking: {dto.NumeroBooking}"; ws.Cell(3, 1).Style.Font.Bold = true;
            ws.Cell(4, 1).Value = $"Fecha Emisión: {DateTime.Now:dd-MM-yyyy}";

            ws.Cell(6, 1).Value = dto.LabelContraparte; ws.Cell(6, 2).Value = dto.NombreContraparte;
            ws.Cell(7, 1).Value = "Naviera"; ws.Cell(7, 2).Value = dto.Naviera;
            ws.Cell(8, 1).Value = "Condición"; ws.Cell(8, 2).Value = dto.CondicionPago;
            ws.Cell(9, 1).Value = "Contenedor"; ws.Cell(9, 2).Value = dto.Contenedor;
            ws.Cell(10, 1).Value = "Sello"; ws.Cell(10, 2).Value = dto.Sello;
            ws.Cell(11, 1).Value = "Planta"; ws.Cell(11, 2).Value = dto.Planta;
            for (int r = 6; r <= 11; r++) ws.Cell(r, 1).Style.Font.Bold = true;

            int row = 13;
            row = EscribirSeccionExcel(ws, row, esEgreso ? "DETALLE DE COSTOS Y SERVICIOS BASE" : "DETALLE DE FLETES Y SERVICIOS BASE", dto.Base, esEgreso);
            row += 1;
            row = EscribirSeccionExcel(ws, row, esEgreso ? "DESVIACIONES Y EXTRACOSTOS (A PAGAR)" : "DESVIACIONES Y EXTRACOSTOS (NOTAS DE COBRO)", dto.Extras, esEgreso);

            row += 2;
            if (dto.TotalUSD > 0)
            {
                ws.Cell(row, 1).Value = "TOTAL USD"; ws.Cell(row, 2).Value = dto.TotalUSD; row++;
                ws.Cell(row, 1).Value = "PAGADO USD"; ws.Cell(row, 2).Value = dto.PagadoUSD; row++;
                ws.Cell(row, 1).Value = "SALDO PENDIENTE USD"; ws.Cell(row, 2).Value = dto.SaldoUSD; ws.Row(row).Style.Font.Bold = true; row += 2;
            }
            if (dto.TotalCLP > 0)
            {
                ws.Cell(row, 1).Value = "TOTAL CLP"; ws.Cell(row, 2).Value = dto.TotalCLP; row++;
                ws.Cell(row, 1).Value = "PAGADO CLP"; ws.Cell(row, 2).Value = dto.PagadoCLP; row++;
                ws.Cell(row, 1).Value = "SALDO PENDIENTE CLP"; ws.Cell(row, 2).Value = dto.SaldoCLP; ws.Row(row).Style.Font.Bold = true;
            }

            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private int EscribirSeccionExcel(IXLWorksheet ws, int row, string titulo, List<LiquidacionLineaDto> lineas, bool esEgreso)
        {
            ws.Cell(row, 1).Value = titulo;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0F172A");
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
            row++;

            var headers = esEgreso
                ? new[] { "Concepto", "Proveedor / Beneficiario", "Documento", "Estado", "Moneda", "Monto Neto" }
                : new[] { "Concepto", "Documento", "Estado", "Moneda", "Monto Neto" };

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(row, c + 1).Value = headers[c];
                ws.Cell(row, c + 1).Style.Font.Bold = true;
                ws.Cell(row, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
            }
            row++;

            if (!lineas.Any()) { ws.Cell(row, 1).Value = "Sin registros."; return row + 1; }

            foreach (var l in lineas)
            {
                int c = 1;
                ws.Cell(row, c++).Value = l.Concepto;
                if (esEgreso) ws.Cell(row, c++).Value = l.Contraparte ?? "-";
                ws.Cell(row, c++).Value = string.IsNullOrEmpty(l.NumeroDocumento) ? "POR EMITIR" : l.NumeroDocumento;
                ws.Cell(row, c++).Value = l.Pagado ? "PAGADO" : "PENDIENTE";
                ws.Cell(row, c++).Value = l.Moneda;
                ws.Cell(row, c++).Value = l.MontoNeto;
                row++;
            }
            return row;
        }

        public byte[] GenerarPdfConsolidado(ConsolidadoClienteDto dto)
        {
            using var ms = new MemoryStream();
            var doc = new Document(PageSize.A4.Rotate(), 30, 30, 40, 40);
            PdfWriter.GetInstance(doc, ms);
            doc.Open();

            doc.Add(new Paragraph("ATILSON — CONSOLIDADO DE CUENTA POR CLIENTE", FontTitle));
            doc.Add(new Paragraph($"Cliente: {dto.ClienteNombre}", FontValue));
            doc.Add(new Paragraph($"Período: {dto.Desde:dd-MM-yyyy} al {dto.Hasta:dd-MM-yyyy}", FontSubtitle));
            doc.Add(new Paragraph(" "));

            var t = new PdfPTable(6) { WidthPercentage = 100 };
            t.SetWidths(new float[] { 20, 15, 15, 15, 15, 20 });
            void Th(string txt) => t.AddCell(new PdfPCell(new Phrase(txt.ToUpper(), FontTableHeader)) { BackgroundColor = ColorLight, Padding = 6 });
            Th("Booking"); Th("Ingreso USD"); Th("Ingreso CLP"); Th("Egreso USD"); Th("Egreso CLP"); Th("Rentabilidad CLP Aprox.");

            foreach (var o in dto.Operaciones)
            {
                void Td(string txt, int align = Element.ALIGN_LEFT) => t.AddCell(new PdfPCell(new Phrase(txt, FontTableCell)) { Padding = 6, HorizontalAlignment = align });
                Td(o.NumeroBooking);
                Td(o.IngresoUsd > 0 ? $"USD {o.IngresoUsd:N2}" : "-", Element.ALIGN_RIGHT);
                Td(o.IngresoClp > 0 ? $"$ {o.IngresoClp:N0}" : "-", Element.ALIGN_RIGHT);
                Td(o.EgresoUsd > 0 ? $"USD {o.EgresoUsd:N2}" : "-", Element.ALIGN_RIGHT);
                Td(o.EgresoClp > 0 ? $"$ {o.EgresoClp:N0}" : "-", Element.ALIGN_RIGHT);
                Td($"$ {(o.IngresoClp - o.EgresoClp):N0}", Element.ALIGN_RIGHT);
            }
            doc.Add(t);

            doc.Add(new Paragraph(" "));
            var totalsTable = new PdfPTable(1) { WidthPercentage = 100 };
            var cell = new PdfPCell() { BackgroundColor = ColorDark, Padding = 15 };
            cell.AddElement(new Paragraph($"TOTAL INGRESOS: USD {dto.TotalIngresoUsd:N2}  /  $ {dto.TotalIngresoClp:N0}", FontTotalLabel));
            cell.AddElement(new Paragraph($"TOTAL EGRESOS: USD {dto.TotalEgresoUsd:N2}  /  $ {dto.TotalEgresoClp:N0}", FontTotalLabel));
            cell.AddElement(new Paragraph($"RENTABILIDAD CLP: $ {(dto.TotalIngresoClp - dto.TotalEgresoClp):N0}", FontTotalValue));
            totalsTable.AddCell(cell);
            doc.Add(totalsTable);

            doc.Close();
            return ms.ToArray();
        }

        public byte[] GenerarExcelConsolidado(ConsolidadoClienteDto dto)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Consolidado");
            ws.Cell(1, 1).Value = "ATILSON — CONSOLIDADO DE CUENTA POR CLIENTE"; ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Value = $"Cliente: {dto.ClienteNombre}";
            ws.Cell(3, 1).Value = $"Período: {dto.Desde:dd-MM-yyyy} al {dto.Hasta:dd-MM-yyyy}";

            int row = 5;
            var headers = new[] { "Booking", "Fecha", "Ingreso USD", "Ingreso CLP", "Egreso USD", "Egreso CLP", "Rentabilidad CLP" };
            for (int c = 0; c < headers.Length; c++) { ws.Cell(row, c + 1).Value = headers[c]; ws.Cell(row, c + 1).Style.Font.Bold = true; }
            row++;

            foreach (var o in dto.Operaciones)
            {
                ws.Cell(row, 1).Value = o.NumeroBooking;
                ws.Cell(row, 2).Value = o.Fecha?.ToString("dd-MM-yyyy") ?? "-";
                ws.Cell(row, 3).Value = o.IngresoUsd;
                ws.Cell(row, 4).Value = o.IngresoClp;
                ws.Cell(row, 5).Value = o.EgresoUsd;
                ws.Cell(row, 6).Value = o.EgresoClp;
                ws.Cell(row, 7).Value = o.IngresoClp - o.EgresoClp;
                row++;
            }
            row += 1;
            ws.Cell(row, 1).Value = "TOTALES"; ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 3).Value = dto.TotalIngresoUsd;
            ws.Cell(row, 4).Value = dto.TotalIngresoClp;
            ws.Cell(row, 5).Value = dto.TotalEgresoUsd;
            ws.Cell(row, 6).Value = dto.TotalEgresoClp;
            ws.Cell(row, 7).Value = dto.TotalIngresoClp - dto.TotalEgresoClp;
            ws.Row(row).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // Versión SOLO-INGRESOS para el portal de clientes: nunca muestra Egresos
        // ni Rentabilidad, a diferencia de GenerarPdfConsolidado/GenerarExcelConsolidado
        // (internos), que sí exponen esos datos para uso de Finanzas.
        public byte[] GenerarPdfEstadoCuentaCliente(EstadoCuentaClienteDto dto)
        {
            using var ms = new MemoryStream();
            var doc = new Document(PageSize.A4, 40, 40, 40, 40);
            PdfWriter.GetInstance(doc, ms);
            doc.Open();

            doc.Add(new Paragraph("ATILSON — ESTADO DE CUENTA", FontTitle));
            doc.Add(new Paragraph($"Cliente: {dto.ClienteNombre}", FontValue));
            doc.Add(new Paragraph($"Período: {dto.Desde:dd-MM-yyyy} al {dto.Hasta:dd-MM-yyyy}", FontSubtitle));
            doc.Add(new Paragraph(" "));

            var t = new PdfPTable(5) { WidthPercentage = 100 };
            t.SetWidths(new float[] { 25, 18, 18, 18, 21 });
            void Th(string txt) => t.AddCell(new PdfPCell(new Phrase(txt.ToUpper(), FontTableHeader)) { BackgroundColor = ColorLight, Padding = 6 });
            Th("Booking"); Th("Total USD"); Th("Pagado USD"); Th("Total CLP"); Th("Saldo Pendiente");

            foreach (var o in dto.Operaciones)
            {
                void Td(string txt, int align = Element.ALIGN_LEFT, BaseColor? color = null)
                {
                    var f = color != null ? new Font(Font.FontFamily.HELVETICA, 8, Font.NORMAL, color) : FontTableCell;
                    t.AddCell(new PdfPCell(new Phrase(txt, f)) { Padding = 6, HorizontalAlignment = align });
                }
                Td(o.NumeroBooking);
                Td(o.TotalUsd > 0 ? $"USD {o.TotalUsd:N2}" : "-", Element.ALIGN_RIGHT);
                Td(o.PagadoUsd > 0 ? $"USD {o.PagadoUsd:N2}" : "-", Element.ALIGN_RIGHT);
                Td(o.TotalClp > 0 ? $"$ {o.TotalClp:N0}" : "-", Element.ALIGN_RIGHT);
                string saldoTxt = ((o.SaldoUsd > 0 ? $"USD {o.SaldoUsd:N2} " : "") + (o.SaldoClp > 0 ? $"$ {o.SaldoClp:N0}" : "")).Trim();
                if (string.IsNullOrWhiteSpace(saldoTxt)) saldoTxt = "$ 0";
                Td(saldoTxt, Element.ALIGN_RIGHT, (o.SaldoUsd > 0 || o.SaldoClp > 0) ? ColorRed : ColorGreen);
            }
            doc.Add(t);

            doc.Add(new Paragraph(" "));
            var totalsTable = new PdfPTable(1) { WidthPercentage = 100 };
            var cell = new PdfPCell() { BackgroundColor = ColorDark, Padding = 15 };
            if (dto.TotalUsd > 0) cell.AddElement(new Paragraph($"SALDO PENDIENTE USD: USD {dto.SaldoUsd:N2}", FontTotalValue));
            if (dto.TotalClp > 0) cell.AddElement(new Paragraph($"SALDO PENDIENTE CLP: $ {dto.SaldoClp:N0}", FontTotalValue) { SpacingBefore = 6 });
            totalsTable.AddCell(cell);
            doc.Add(totalsTable);

            doc.Close();
            return ms.ToArray();
        }

        public byte[] GenerarExcelEstadoCuentaCliente(EstadoCuentaClienteDto dto)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Estado de Cuenta");
            ws.Cell(1, 1).Value = "ATILSON — ESTADO DE CUENTA"; ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Value = $"Cliente: {dto.ClienteNombre}";
            ws.Cell(3, 1).Value = $"Período: {dto.Desde:dd-MM-yyyy} al {dto.Hasta:dd-MM-yyyy}";

            int row = 5;
            var headers = new[] { "Booking", "Fecha", "Total USD", "Pagado USD", "Saldo USD", "Total CLP", "Pagado CLP", "Saldo CLP" };
            for (int c = 0; c < headers.Length; c++) { ws.Cell(row, c + 1).Value = headers[c]; ws.Cell(row, c + 1).Style.Font.Bold = true; }
            row++;

            foreach (var o in dto.Operaciones)
            {
                ws.Cell(row, 1).Value = o.NumeroBooking;
                ws.Cell(row, 2).Value = o.Fecha?.ToString("dd-MM-yyyy") ?? "-";
                ws.Cell(row, 3).Value = o.TotalUsd;
                ws.Cell(row, 4).Value = o.PagadoUsd;
                ws.Cell(row, 5).Value = o.SaldoUsd;
                ws.Cell(row, 6).Value = o.TotalClp;
                ws.Cell(row, 7).Value = o.PagadoClp;
                ws.Cell(row, 8).Value = o.SaldoClp;
                row++;
            }
            row += 1;
            ws.Cell(row, 1).Value = "TOTALES"; ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 3).Value = dto.TotalUsd;
            ws.Cell(row, 4).Value = dto.PagadoUsd;
            ws.Cell(row, 5).Value = dto.SaldoUsd;
            ws.Cell(row, 6).Value = dto.TotalClp;
            ws.Cell(row, 7).Value = dto.PagadoClp;
            ws.Cell(row, 8).Value = dto.SaldoClp;
            ws.Row(row).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }
}