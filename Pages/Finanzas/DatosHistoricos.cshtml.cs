using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using ClosedXML.Excel;
using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.Finanzas
{
    public class DatosHistoricosModel : PageModel
    {
        private readonly AtilsonContext _context;
        public DatosHistoricosModel(AtilsonContext context) => _context = context;

        [BindProperty] public IFormFile? ArchivoExcel { get; set; }
        [BindProperty] public List<IFormFile> ArchivosAnalisis { get; set; } = new();
        [BindProperty(SupportsGet = true)] public string? Buscar { get; set; }
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

        public const int TamanoPagina = 20;
        public int Total { get; set; }
        public int TotalPaginas { get; set; }
        public List<Operacione> Historicos { get; set; } = new();

        // Listas para "Creación de operación" (nov-2026).
        public List<(int Id, string Nombre)> ClientesLista { get; set; } = new();
        public List<(int Id, string Nombre)> NavierasLista { get; set; } = new();
        public List<(int Id, string Nombre)> ProveedoresLista { get; set; } = new();
        // 13-nov: el proveedor de cada concepto se elige según su sección (minuta: Marítimo → navieras,
        // Documental → agencias de aduana, Terrestre → transportistas, Almacenaje → depósitos).
        // Valor de cada opción: "nav:id", "aga:id" o "prov:id" (Proveedores: transportistas y depósitos).
        public sealed record OpcionProveedor(string Valor, string Nombre);
        public Dictionary<string, List<OpcionProveedor>> ProveedoresPorSeccion { get; set; } = new();

        private static readonly string[] HBooking = { "BOOKING", "BKG", "NUMEROBOOKING", "NBOOKING", "BOOKINGNO", "BOOKINGNUMBER", "NROBOOKING", "NUMBOOKING", "CODIGOBOOKING", "NUMERODERESERVA", "RESERVA", "REFERENCIA", "REFERENCIACLIENTE", "NREFERENCIA", "REFCLIENTE" };
        private static readonly string[] HCliente = { "CLIENTE", "RAZONSOCIAL", "NOMBRECLIENTE", "NOMBREDELCLIENTE", "EMPRESACLIENTE", "NOMBREEMPRESA", "EXPORTADOR", "IMPORTADOR", "MANDANTE" };
        private static readonly string[] HRut = { "RUT", "RUTCLIENTE", "RUTEXPORTADOR", "RUTIMPORTADOR" };
        private static readonly string[] HNaviera = { "NAVIERA", "CARRIER", "LINEANAVIERA", "LINEA" };
        private static readonly string[] HFecha = { "FECHA", "FECHAOPERACION", "FECHAEMBARQUE", "FECHAFACTURA", "ETD" };
        private static readonly string[] HServicio = { "SERVICIO", "TIPOSERVICIO", "GRUPO", "GRUPOCOBRO", "CATEGORIA" };
        private static readonly string[] HConcepto = { "CONCEPTO", "DESCRIPCION", "DETALLE", "ITEM", "GLOSA" };
        private static readonly string[] HMovimiento = { "TIPOMOVIMIENTO", "MOVIMIENTO", "INGRESOEGRESO", "TIPOCOBRO" };
        private static readonly string[] HMoneda = { "MONEDA", "CURRENCY", "DIVISA" };
        private static readonly string[] HMonto = { "MONTO", "VALOR", "NETO", "VALORNETO", "MONTONETO", "MONTOCLP", "MONTOUSD", "VALORCLP", "VALORUSD", "TOTAL", "TOTALNETO", "IMPORTENETO", "IMPORTE" };
        private static readonly string[] HEstado = { "ESTADO", "ESTADOPAGO", "ESTADOCOBRO", "ESTADODOCUMENTO" };
        private static readonly string[] HDocumento = { "NUMERODOCUMENTO", "NDOCUMENTO", "FACTURA", "NFACTURA", "FOLIO", "NOTACOBRO", "NUMEROFACTURA" };
        private static readonly string[] HProveedor = { "PROVEEDOR", "BENEFICIARIO", "TRANSPORTISTA", "AGENCIA", "CONTRAPARTE" };

        private static string N(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return "";
            string d = valor.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char ch in d)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch))
                    sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string Txt(IXLCell cell) => cell.IsEmpty() ? "" : cell.GetFormattedString().Trim();

        private static string? Cortar(string? valor, int max)
        {
            if (string.IsNullOrWhiteSpace(valor)) return null;
            string t = valor.Trim();
            return t.Length <= max ? t : t[..max];
        }

        private static decimal? Monto(IXLCell cell)
        {
            if (cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.Number) return (decimal)cell.GetDouble();
            string s = Txt(cell).Replace("USD", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("CLP", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("$", "").Trim().Replace(" ", "");
            if (string.IsNullOrWhiteSpace(s) || s == "-") return null;

            // 1.234.567,89 / 1,234,567.89 / 1234567
            if (s.Contains(',') && s.Contains('.'))
            {
                if (s.LastIndexOf(',') > s.LastIndexOf('.')) s = s.Replace(".", "").Replace(',', '.');
                else s = s.Replace(",", "");
            }
            else if (s.Contains(','))
            {
                int dec = s.Length - s.LastIndexOf(',') - 1;
                s = dec is 1 or 2 ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
            }
            else if (s.Contains('.'))
            {
                // Chile: los valores de texto "1.234" o "12.345" son miles,
                // NO decimales. Evita importar $12.345 como $12,345.
                // Un único punto seguido de 1-2 dígitos es decimal (ej. USD 1350.50).
                var grupos = s.TrimStart('+', '-').Split('.');
                bool miles = grupos.Length > 1 && grupos[0].Length is >= 1 and <= 3 &&
                    grupos.Skip(1).All(g => g.Length == 3 && g.All(char.IsDigit));
                if (miles) s = s.Replace(".", "");
                else if (s.Count(x => x == '.') > 1) return null; // Formato incoherente
            }

            return decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var m) ? m : null;
        }

        private static DateTime? Fecha(IXLCell cell)
        {
            if (cell.IsEmpty()) return null;
            if (cell.TryGetValue<DateTime>(out var dt)) return dt;
            string s = Txt(cell);
            string[] formatos = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "MM/dd/yyyy" };
            if (DateTime.TryParseExact(s, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return dt;
            if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("es-CL"), DateTimeStyles.None, out dt)) return dt;
            return null;
        }

        private static bool EsAlias(string n, IEnumerable<string> aliases) => aliases.Contains(n, StringComparer.OrdinalIgnoreCase);

        private static int FilaEncabezado(IXLWorksheet ws)
        {
            int ultima = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 1, 15);
            int mejorFila = 1, mejorScore = -1;
            var todos = HBooking.Concat(HCliente).Concat(HRut).Concat(HNaviera).Concat(HFecha).Concat(HServicio)
                .Concat(HConcepto).Concat(HMovimiento).Concat(HMoneda).Concat(HMonto).Concat(HEstado)
                .Concat(HDocumento).Concat(HProveedor).ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int r = 1; r <= ultima; r++)
            {
                int score = ws.Row(r).CellsUsed().Select(c => N(Txt(c))).Count(h => todos.Contains(h));
                if (score > mejorScore) { mejorScore = score; mejorFila = r; }
            }
            return mejorFila;
        }

        private static string? Valor(Dictionary<int,string> headersN, IXLRow row, params string[] aliases)
        {
            var col = headersN.FirstOrDefault(h => EsAlias(h.Value, aliases)).Key;
            return col <= 0 ? null : Txt(row.Cell(col));
        }

        private static IXLCell? Celda(Dictionary<int,string> headersN, IXLRow row, params string[] aliases)
        {
            var col = headersN.FirstOrDefault(h => EsAlias(h.Value, aliases)).Key;
            return col <= 0 ? null : row.Cell(col);
        }

        private static string CanonServicio(string? servicio, string? concepto)
        {
            string n = N((servicio ?? "") + " " + (concepto ?? ""));
            if (n.Contains("MARIT") || n.Contains("FLETE") || n.Contains("ISPS") || n.Contains("THC") || n.Contains("REBATE") || n.Contains("LATEARRIVAL")) return "Marítimo";
            if (n.Contains("TRANSP") || n.Contains("TERREST") || n.Contains("FLETELOCAL") || n.Contains("PORTEO")) return "Transporte";
            if (n.Contains("DOCUMENT") || n.Contains("ADUANA") || n.Contains("HONORARIO") || n.Contains("DUS") || n.Contains("DIN") || n.Contains("EMISIONBL") || n.Contains("CORRECCIONBL")) return "Documental";
            if (n.Contains("GATE") || n.Contains("DEPOSITO")) return "Gate-Out/In";
            if (n.Contains("ALMAC") || n.Contains("BODEGA") || n.Contains("PATIO")) return "Almacenamiento";
            return "Otros";
        }

        private static string? CanonMovimiento(string? movimiento, string? hoja)
        {
            string n = N(movimiento);
            if (n.Contains("EGRES") || n.Contains("COSTO") || n.Contains("GASTO") || n.Contains("PROVEEDOR") || n == "PAGO") return "EGRESO";
            if (n.Contains("INGRES") || n.Contains("VENTA") || n.Contains("COBRO") || n.Contains("CLIENTE") || n.Contains("FACTURACION")) return "INGRESO";

            string h = N(hoja);
            if (h.Contains("COSTO") || h.Contains("EGRES") || h.Contains("PROVEEDOR") || h.Contains("CUENTASPORPAGAR")) return "EGRESO";
            if (h.Contains("FACTUR") || h.Contains("INGRES") || h.Contains("VENTA") || h.Contains("COBR") || h.Contains("CLIENTE")) return "INGRESO";
            return null;
        }

        private static string CanonMoneda(string? moneda, string? encabezado)
        {
            string n = N((moneda ?? "") + " " + (encabezado ?? ""));
            if (n.Contains("USD") || n.Contains("DOLAR")) return "USD";
            return "CLP";
        }

        private static string CanonEstado(string? estado, bool tieneDocumento)
        {
            string n = N(estado);
            if (n.Contains("CONCILI")) return "CONCILIADO";
            if (n.Contains("PAGAD") || n.Contains("COBRAD") || n.Contains("CANCELADO")) return "PAGADO";
            if (n.Contains("NOFACTUR")) return "NO FACTURABLE";
            if (n.Contains("FACTUR") || n.Contains("EMITID") || n.Contains("PENDIENTEPAGO") || n.Contains("PORPAGAR") || n.Contains("PORCOBRAR")) return "FACTURADO";
            return tieneDocumento ? "FACTURADO" : "PROVISIÓN";
        }

        private static bool PareceConceptoMonetario(string header)
        {
            string n = N(header);
            string[] claves = { "FLETE", "COSTO", "VENTA", "INGRESO", "EGRESO", "MONTO", "VALOR", "FACTURA",
                "COBRO", "PAGO", "HONORARIO", "ISPS", "THC", "BL", "DUS", "DIN", "GATE", "TRANSP", "ALMAC",
                "LATE", "REBATE", "DOCUMENT", "AGENCI", "SEGURO", "DEMURRAGE", "DETENTION", "PORTEO" };
            string[] excluir = { "CANTIDAD", "DIAS", "CONTENEDOR", "PESO", "KILOS", "BULTOS", "TEMPERATURA", "ETD", "ETA" };
            return claves.Any(n.Contains) && !excluir.Any(n.Contains);
        }

        private sealed record MovimientoImportado(string Tipo, string Grupo, string Concepto, decimal Monto, string Moneda, string Estado, string? Documento, string? Proveedor);

        private static IEnumerable<MovimientoImportado> MovimientosDeFila(IXLWorksheet ws, IXLRow row,
            Dictionary<int,string> headers, Dictionary<int,string> headersN)
        {
            string? concepto = Valor(headersN, row, HConcepto);
            var montoCell = Celda(headersN, row, HMonto);
            decimal? monto = montoCell == null ? null : Monto(montoCell);
            string? movRaw = Valor(headersN, row, HMovimiento);
            string? tipo = CanonMovimiento(movRaw, ws.Name);
            string? servicio = Valor(headersN, row, HServicio);
            string? monedaRaw = Valor(headersN, row, HMoneda);
            string? estadoRaw = Valor(headersN, row, HEstado);
            string? doc = Valor(headersN, row, HDocumento);
            string? proveedor = Valor(headersN, row, HProveedor);

            if (monto.HasValue && monto.Value != 0 && !string.IsNullOrWhiteSpace(concepto) && tipo != null)
            {
                string mon = CanonMoneda(monedaRaw, headers.FirstOrDefault(h => EsAlias(headersN[h.Key], HMonto)).Value);
                yield return new MovimientoImportado(tipo, CanonServicio(servicio, concepto), concepto.Trim(), Math.Abs(monto.Value), mon,
                    CanonEstado(estadoRaw, !string.IsNullOrWhiteSpace(doc)), string.IsNullOrWhiteSpace(doc) ? null : doc.Trim(),
                    string.IsNullOrWhiteSpace(proveedor) ? null : proveedor.Trim());
                yield break;
            }

            var baseHeaders = HBooking.Concat(HCliente).Concat(HRut).Concat(HNaviera).Concat(HFecha).Concat(HServicio)
                .Concat(HConcepto).Concat(HMovimiento).Concat(HMoneda).Concat(HMonto).Concat(HEstado).Concat(HDocumento).Concat(HProveedor)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var h in headers)
            {
                if (baseHeaders.Contains(headersN[h.Key]) || !PareceConceptoMonetario(h.Value)) continue;
                decimal? val = Monto(row.Cell(h.Key));
                if (!val.HasValue || val.Value == 0) continue;

                string? tipoWide = CanonMovimiento(h.Value, ws.Name);
                if (tipoWide == null) continue;
                string mon = CanonMoneda(monedaRaw, h.Value);
                yield return new MovimientoImportado(tipoWide, CanonServicio(servicio, h.Value), h.Value.Trim(), Math.Abs(val.Value), mon,
                    CanonEstado(estadoRaw, !string.IsNullOrWhiteSpace(doc)), string.IsNullOrWhiteSpace(doc) ? null : doc.Trim(),
                    string.IsNullOrWhiteSpace(proveedor) ? null : proveedor.Trim());
            }
        }

        public async Task OnGetAsync()
        {
            ClientesLista = (await _context.Clientes.AsNoTracking().Where(c => !c.IsDeleted && c.RazonSocial != null)
                    .OrderBy(c => c.RazonSocial).Select(c => new { c.Id, c.RazonSocial }).ToListAsync())
                .Select(c => (c.Id, c.RazonSocial!)).ToList();
            NavierasLista = (await _context.Navieras.AsNoTracking().Where(n => !n.IsDeleted && n.Activo == 1)
                    .OrderBy(n => n.NombreNaviera).Select(n => new { n.Id, n.NombreNaviera }).ToListAsync())
                .Select(n => (n.Id, n.NombreNaviera)).ToList();
            ProveedoresLista = (await _context.Proveedores.AsNoTracking().Where(p => p.Activo == 1 && p.NombreProveedor != null)
                    .OrderBy(p => p.NombreProveedor).Select(p => new { p.Id, p.NombreProveedor }).ToListAsync())
                .Select(p => (p.Id, p.NombreProveedor)).ToList();

            var provs = await _context.Proveedores.AsNoTracking().Where(p => p.Activo == 1 && p.NombreProveedor != null)
                .OrderBy(p => p.NombreProveedor).Select(p => new { p.Id, p.NombreProveedor, p.IdTipoProveedor }).ToListAsync();
            var agencias = await _context.AgenciasAduanas.AsNoTracking().Where(a => a.Activo == 1 && a.NombreAgencia != null)
                .OrderBy(a => a.NombreAgencia).Select(a => new { a.Id, a.NombreAgencia }).ToListAsync();
            var navieras = NavierasLista.Select(n => new OpcionProveedor("nav:" + n.Id, n.Nombre)).ToList();
            var aga = agencias.Select(a => new OpcionProveedor("aga:" + a.Id, a.NombreAgencia)).ToList();
            var transportistas = provs.Where(p => p.IdTipoProveedor != AtilsonCargoSpa.Helpers.TransportistaHelper.TipoDeposito)
                .Select(p => new OpcionProveedor("prov:" + p.Id, p.NombreProveedor)).ToList();
            var depositos = provs.Where(p => p.IdTipoProveedor == AtilsonCargoSpa.Helpers.TransportistaHelper.TipoDeposito)
                .Select(p => new OpcionProveedor("prov:" + p.Id, p.NombreProveedor)).ToList();
            ProveedoresPorSeccion = new Dictionary<string, List<OpcionProveedor>>
            {
                ["Marítimo"] = navieras,
                ["Documental"] = aga,
                ["Terrestre"] = transportistas,
                ["Almacenaje"] = depositos,
                ["Otros"] = navieras.Concat(aga).Concat(transportistas).Concat(depositos).OrderBy(o => o.Nombre).ToList(),
            };

            var q = _context.Operaciones.AsNoTracking()
                .Where(o => !o.IsDeleted && o.EsHistorica);

            if (!string.IsNullOrWhiteSpace(Buscar))
            {
                string s = Buscar.Trim();
                q = q.Where(o => o.NumeroBooking.Contains(s) ||
                    o.IdClienteNavigation.RazonSocial.Contains(s) ||
                    o.TransaccionesFinancieras.Any(t => t.NumeroDocumento != null && t.NumeroDocumento.Contains(s)));
            }

            Total = await q.CountAsync();
            TotalPaginas = Math.Max(1, (int)Math.Ceiling(Total / (double)TamanoPagina));
            Pagina = Math.Clamp(Pagina, 1, TotalPaginas);
            Historicos = await q.OrderByDescending(o => o.FechaImportacionHistorica ?? o.FechaCreacion)
                .ThenByDescending(o => o.Id)
                .Skip((Pagina - 1) * TamanoPagina).Take(TamanoPagina)
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.TransaccionesFinancieras)
                .ToListAsync();
        }


        // Plantilla descargable desde la misma pantalla de Finanzas.
        // Sin registros ficticios: cada fila que completa Finanzas es un movimiento real.
        public IActionResult OnGetPlantilla()
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Movimientos");
            string[] columnas = {
                "Booking", "Cliente", "RUT", "Naviera", "Fecha", "Servicio",
                "Concepto", "Movimiento", "Moneda", "Monto", "Estado", "NumeroDocumento", "Proveedor"
            };
            for (int i = 0; i < columnas.Length; i++)
            {
                ws.Cell(1, i + 1).Value = columnas[i];
                ws.Column(i + 1).Width = i is 0 or 2 or 4 or 8 or 11 ? 18 : 25;
            }
            var encabezado = ws.Range(1, 1, 1, columnas.Length);
            encabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#30205B");
            encabezado.Style.Font.FontColor = XLColor.White;
            encabezado.Style.Font.Bold = true;
            encabezado.Style.Alignment.WrapText = true;
            ws.Row(1).Height = 32;
            ws.SheetView.FreezeRows(1);
            ws.Range(1, 1, 1, columnas.Length).SetAutoFilter();
            ws.Column(5).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(10).Style.NumberFormat.Format = "#,##0.00";

            var guia = wb.Worksheets.Add("Instrucciones");
            guia.Cell("A1").Value = "DATOS HISTÓRICOS · ATILSON";
            guia.Cell("A1").Style.Font.Bold = true;
            guia.Cell("A1").Style.Font.FontSize = 16;
            string[] notas = {
                "Completa una fila por cada ingreso o egreso histórico. No incluyas datos de prueba.",
                "Obligatorios: Booking, Cliente, Concepto, Movimiento (INGRESO/EGRESO), Moneda (CLP/USD) y Monto.",
                "Para un mismo booking puedes agregar varias filas: factura cliente, flete naviera, documentos, transporte y otros.",
                "Opcionales: RUT, Naviera, Fecha, Servicio, Estado, NumeroDocumento, Proveedor.",
                "Movimiento INGRESO: cuenta por cobrar al cliente. EGRESO: cuenta por pagar al proveedor.",
                "Estado: PROVISIÓN, FACTURADO, PAGADO o CONCILIADO. Un documento informado se toma como facturado si el estado está vacío.",
                "Fecha: dd/MM/yyyy. Monto: número sin símbolo de moneda (por ejemplo 120000 o 1350,50).",
                "Usa los bookings reales históricos. Si pertenecen a una operación activa del ERP, no se importarán para evitar duplicados.",
                "Puedes importar el mismo archivo otra vez: el sistema evita duplicar movimientos iguales.",
                "Para adaptar un Excel de la empresa, conserva Booking y Cliente y revisa la advertencia de columnas de cada hoja.",
                "No borres el encabezado de la pestaña Movimientos ni insertes subtotales en las filas de datos."
            };
            for (int i = 0; i < notas.Length; i++) guia.Cell(i + 3, 1).Value = notas[i];
            guia.Column(1).Width = 115;
            guia.Range(3, 1, notas.Length + 2, 1).Style.Alignment.WrapText = true;
            foreach (var i in Enumerable.Range(3, notas.Length)) guia.Row(i).Height = 30;
            using var mem = new MemoryStream();
            wb.SaveAs(mem);
            return File(mem.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "ATILSON_Plantilla_Datos_Historicos.xlsx");
        }

        // ClosedXML lee xlsx/xlsm. Los Excel antiguos (BIFF .xls) se transforman
        // a un workbook temporal para reutilizar exactamente el mismo importador.
        private static XLWorkbook AbrirExcel(Stream stream, string extension)
        {
            if (extension != ".xls")
            {
                try { return new XLWorkbook(stream); }
                catch (Exception)
                {
                    // Algunos Excel antiguos contienen tablas dinámicas o metadatos que
                    // ClosedXML no puede leer. Reintentar sus celdas como datos planos.
                    if (!stream.CanSeek) throw;
                    stream.Position = 0;
                }
            }
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var libro = new XLWorkbook();
            using var reader = ExcelReaderFactory.CreateReader(stream);
            int numero = 0;
            do
            {
                numero++;
                string nombre = string.IsNullOrWhiteSpace(reader.Name) ? "Hoja" + numero : reader.Name;
                if (nombre.Length > 31) nombre = nombre[..31];
                if (libro.Worksheets.Any(x => x.Name == nombre)) nombre = "Hoja" + numero;
                var ws = libro.Worksheets.Add(nombre);
                int fila = 0;
                while (reader.Read())
                {
                    fila++;
                    for (int col = 0; col < reader.FieldCount; col++)
                    {
                        object? dato = reader.GetValue(col);
                        if (dato == null || dato == DBNull.Value) continue;
                        var celda = ws.Cell(fila, col + 1);
                        if (dato is DateTime fecha) celda.Value = fecha;
                        else if (dato is double numeroValor) celda.Value = numeroValor;
                        else if (dato is bool logico) celda.Value = logico;
                        else celda.Value = dato.ToString() ?? "";
                    }
                }
            } while (reader.NextResult());
            return libro;
        }


        // Inspección SIN base de datos: clasifica hojas de libros heterogéneos.
        // Es el primer paso del importador multi-fuente; no genera transacciones.
        private static string PerfilHojaExcel(string archivo, string hoja, IReadOnlyCollection<string> encabezados)
        {
            string a = N(archivo);
            string h = N(hoja);
            bool Tiene(params string[] partes) => encabezados.Any(x => partes.Any(p => x.Contains(p)));
            if (h.StartsWith("TABLA") || h.Contains("DIRECCIONENVIO") ||
                h.Contains("ENVIOENCOMIENDAS") || h.Contains("RESUMEN"))
                return "AUXILIAR · excluir de movimientos";
            if (h.Contains("COMPRASCONBOLETA")) return "GASTOS GENERALES · revisar fuera del booking";
            if (a.Contains("FACTURASATILSON"))
            {
                if (h.Contains("EXENTAS")) return "INGRESOS · facturas exentas sin booking directo";
                if (h.Contains("AFECTAS")) return "INGRESOS · facturas afectas sin booking directo";
                if (h.Contains("NOTADECOBRO")) return "INGRESOS · notas de cobro CLP/USD";
            }
            if (a.Contains("PROVEEDORESTRANSPORT") || h.Contains("FACTURATTE"))
                return "EGRESOS · facturas transportistas (saldos y pagos aparte)";
            if (a.Contains("REGISTROSDEFACTURASAGENCIA") ||
                h.Contains("STEMBEX") || h.Contains("STEPHENS"))
                return "EGRESOS · facturas/notas agencia (vincular booking)";
            if (a.Contains("DETALLEEMBARQUES"))
                return "EMBARQUES · datos logísticos, ingresos y costos mixtos";
            if (a.Contains("RESPALDOFACTURACION"))
                return "INGRESOS · facturación cliente por booking";
            if (Tiene("FACTURA", "NFACT", "NDOCTO") && Tiene("MONTO", "VALOR") &&
                Tiene("PROVEEDOR")) return "EGRESOS · probable factura proveedor";
            if (Tiene("BOOKING", "RESERVA") && Tiene("CLIENTE", "RAZONSOCIAL"))
                return "EMBARQUES/FACTURACIÓN · revisar columnas";
            if (Tiene("FACTURA", "NOTACOBRO") && Tiene("MONTO", "VALOR"))
                return "DOCUMENTOS FINANCIEROS · falta vinculación";
            return "SIN CLASIFICAR · revisar manualmente";
        }

        public async Task<IActionResult> OnPostAnalizarFormatosAsync()
        {
            if (ArchivosAnalisis == null || ArchivosAnalisis.Count == 0)
            {
                TempData["ErrorMsg"] = "Selecciona al menos un Excel para analizar las hojas.";
                return RedirectToPage();
            }
            if (ArchivosAnalisis.Count > 8)
            {
                TempData["ErrorMsg"] = "Analiza hasta 8 archivos por vez para evitar sobrecargar el servidor.";
                return RedirectToPage();
            }

            var lineas = new List<string>();
            int hojas = 0, auxiliares = 0, enRevision = 0;
            foreach (var archivo in ArchivosAnalisis)
            {
                if (archivo == null || archivo.Length == 0) continue;
                var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
                if (ext is not (".xlsx" or ".xlsm" or ".xls") || archivo.Length > 20 * 1024 * 1024)
                {
                    lineas.Add(Path.GetFileName(archivo.FileName) + ": tipo o tamaño no admitido (20 MB máx.).");
                    enRevision++;
                    continue;
                }

                try
                {
                    using var mem = new MemoryStream();
                    await archivo.CopyToAsync(mem);
                    mem.Position = 0;
                    using var wb = AbrirExcel(mem, ext);
                    var resultados = new List<string>();
                    foreach (var ws in wb.Worksheets)
                    {
                        hojas++;
                        int ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 0;
                        int filaEncabezado = Math.Min(FilaEncabezado(ws), Math.Max(1, ultimaFila));
                        var cabeceras = ws.Row(filaEncabezado).CellsUsed()
                            .Take(40).Select(c => N(Txt(c))).Where(x => x.Length > 0).ToHashSet();
                        string perfil = PerfilHojaExcel(archivo.FileName, ws.Name, cabeceras);
                        if (perfil.StartsWith("AUXILIAR")) auxiliares++;
                        if (perfil.Contains("REVISAR", StringComparison.OrdinalIgnoreCase) ||
                            perfil.Contains("SIN CLASIFICAR")) enRevision++;
                        // Conteo orientativo, no filas financieras confirmadas.
                        resultados.Add($" • {ws.Name} ({Math.Max(0, ultimaFila - filaEncabezado)} filas aprox.): {perfil}");
                    }
                    lineas.Add($"{Path.GetFileName(archivo.FileName)} · {resultados.Count} hoja(s)");
                    foreach (string detalle in resultados.Take(25))
                        lineas.Add(detalle);
                    if (resultados.Count > 25)
                        lineas.Add($" • Y {resultados.Count - 25} hojas adicionales.");
                }
                catch (Exception)
                {
                    // No divulgar datos internos del archivo o errores de librería en UI.
                    lineas.Add(Path.GetFileName(archivo.FileName) + ": no se pudo analizar; guarda una copia como .xlsx válido desde Excel y reintenta.");
                    enRevision++;
                }
            }
            TempData["AnalisisFuentes"] =
                $"ANÁLISIS SIN IMPORTAR · {ArchivosAnalisis.Count} archivo(s), {hojas} hoja(s), {auxiliares} auxiliares y {enRevision} con revisión especial. No se creó ninguna operación, factura ni pago.\n" +
                string.Join("\n", lineas.Take(44)) +
                (lineas.Count > 44 ? $"\n... {lineas.Count - 44} líneas adicionales. Analiza menos archivos para ver todas." : "");
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostImportarAsync(bool soloValidar = false)
        {
            if (ArchivoExcel == null || ArchivoExcel.Length == 0)
            {
                TempData["ErrorMsg"] = "Selecciona un archivo Excel (.xlsx, .xlsm o .xls). Si no puedes seleccionarlo, verifica la extensión y descarga la plantilla.";
                return RedirectToPage();
            }

            string ext = Path.GetExtension(ArchivoExcel.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xlsm" && ext != ".xls")
            {
                TempData["ErrorMsg"] = "Formato no admitido. Sube un Excel .xlsx, .xlsm o .xls, o utiliza la plantilla descargable.";
                return RedirectToPage();
            }

            await using var importTx = await _context.Database.BeginTransactionAsync();
            try
            {
                string usuario = User.Identity?.Name ?? "Finanzas";
                string origen = Cortar(Path.GetFileName(ArchivoExcel.FileName), 260) ?? "historico.xlsx";
                DateTime ahora = DateTime.Now;
                int operacionesNuevas = 0, movimientosNuevos = 0, ingresosNuevos = 0, egresosNuevos = 0, filasOmitidas = 0, filasYaImportadas = 0, operacionesActualizadas = 0, hojasReconocidas = 0;
                var avisos = new List<string>();
    
                var clientes = await _context.Clientes.Where(c => !c.IsDeleted).ToListAsync();
                var navieras = await _context.Navieras.Where(n => !n.IsDeleted).ToListAsync();
                var proveedores = await _context.Proveedores.AsNoTracking().ToListAsync();
                var historicosExistentes = await _context.Operaciones
                    .Where(o => o.EsHistorica && !o.IsDeleted)
                    .Include(o => o.TransaccionesFinancieras)
                    .Include(o => o.Finanzasoperacions)
                    .ToListAsync();
                var porBooking = historicosExistentes
                    .GroupBy(o => N(o.NumeroBooking))
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    
                var bookingsVivos = (await _context.Operaciones.AsNoTracking()
                    .Where(o => !o.EsHistorica && !o.IsDeleted)
                    .Select(o => o.NumeroBooking).ToListAsync())
                    .Select(N).ToHashSet(StringComparer.OrdinalIgnoreCase);
    
                Cliente? ClientePor(string nombre, string? rut)
                {
                    string nr = N(rut), nn = N(nombre);
                    return clientes.FirstOrDefault(c => nr.Length > 0 && N(c.Rut) == nr)
                        ?? clientes.FirstOrDefault(c => N(c.RazonSocial) == nn || N(c.NombreCliente) == nn);
                }
    
                Proveedore? ProveedorPor(string? nombre)
                {
                    string nn = N(nombre);
                    if (nn.Length == 0) return null;
                    return proveedores.FirstOrDefault(p => N(p.NombreProveedor) == nn || N(p.RazonSocial) == nn || N(p.Rut) == nn);
                }
    
                async Task<Naviera> NavieraPor(string? nombre)
                {
                    string texto = Cortar(string.IsNullOrWhiteSpace(nombre) ? "HISTÓRICO / NO INFORMADO" : nombre, 45) ?? "HISTÓRICO";
                    string nn = N(texto);
                    var n = navieras.FirstOrDefault(x => N(x.NombreNaviera) == nn);
                    if (n != null) return n;
                    n = new Naviera
                    {
                        NombreNaviera = texto,
                        Activo = 0,
                        IsDeleted = false,
                        FechaCreacion = ahora,
                        FechaModificacion = ahora,
                        UsuarioCreador = usuario,
                        UsuarioModificador = usuario
                    };
                    _context.Navieras.Add(n);
                    await _context.SaveChangesAsync();
                    navieras.Add(n);
                    return n;
                }
    
                await using var stream = ArchivoExcel.OpenReadStream();
                using var wb = AbrirExcel(stream, ext);
    
                foreach (var ws in wb.Worksheets)
                {
                    if (ws.LastRowUsed() == null || ws.LastColumnUsed() == null) continue;
                    int hr = FilaEncabezado(ws);
                    int lastCol = ws.LastColumnUsed()!.ColumnNumber();
                    int lastRow = ws.LastRowUsed()!.RowNumber();
                    var headers = new Dictionary<int,string>();
                    var headersN = new Dictionary<int,string>();
                    for (int col = 1; col <= lastCol; col++)
                    {
                        string h = Txt(ws.Cell(hr, col));
                        if (h.Length == 0) continue;
                        headers[col] = h;
                        headersN[col] = N(h);
                    }
    
                    bool tieneBooking = headersN.Values.Any(h => EsAlias(h, HBooking));
                    bool tieneCliente = headersN.Values.Any(h => EsAlias(h, HCliente));
                    if (!tieneBooking || !tieneCliente)
                    {
                        avisos.Add($"{ws.Name}: no se encontraron Booking y Cliente. Encabezados detectados: {string.Join(", ", headers.Values.Take(15))}. Utiliza la plantilla o conserva estos nombres en tu Excel.");
                        continue;
                    }
    
                    hojasReconocidas++;
                    for (int r = hr + 1; r <= lastRow; r++)
                    {
                        var row = ws.Row(r);
                        if (!row.CellsUsed().Any()) continue;
    
                        string booking = (Cortar(Valor(headersN, row, HBooking), 45) ?? "").Trim();
                        string clienteNombre = (Cortar(Valor(headersN, row, HCliente), 100) ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(booking) || string.IsNullOrWhiteSpace(clienteNombre))
                        {
                            filasOmitidas++;
                            continue;
                        }
    
                        string bkey = N(booking);
                        if (bookingsVivos.Contains(bkey))
                        {
                            filasOmitidas++;
                            if (avisos.Count < 30) avisos.Add($"{ws.Name} fila {r}: {booking} ya existe o existió como operación normal; no se mezcló con el histórico.");
                            continue;
                        }

                        // Primero detectamos información financiera. No se crean maestros ni
                        // operaciones vacías por hojas auxiliares que solo traigan Booking/Cliente.
                        var movimientos = MovimientosDeFila(ws, row, headers, headersN)
                            .GroupBy(m => new { m.Tipo, Grupo = N(m.Grupo), Concepto = N(m.Concepto), m.Monto, m.Moneda, Documento = N(m.Documento) })
                            .Select(g => g.First())
                            .ToList();
                        if (movimientos.Count == 0)
                        {
                            filasOmitidas++;
                            if (avisos.Count < 30) avisos.Add($"{ws.Name} fila {r} · {booking}: no se detectó un monto/concepto financiero seguro.");
                            continue;
                        }

                        bool yaExistiaHistorico = porBooking.TryGetValue(bkey, out var opExistente);
                        var movimientosPendientes = yaExistiaHistorico
                            ? movimientos.Where(m => !opExistente!.TransaccionesFinancieras.Any(t =>
                                t.TipoMovimiento == m.Tipo &&
                                N(t.GrupoCobro) == N(m.Grupo) &&
                                N(t.Concepto) == N(m.Concepto) &&
                                t.Moneda == m.Moneda &&
                                t.MontoNeto == m.Monto &&
                                N(t.NumeroDocumento) == N(m.Documento))).ToList()
                            : movimientos;

                        // Reimportar exactamente la misma planilla es una operación nula:
                        // no duplica movimientos ni modifica la fecha/fuente del histórico.
                        if (yaExistiaHistorico && movimientosPendientes.Count == 0)
                        {
                            filasYaImportadas++;
                            continue;
                        }

                        string? rut = Cortar(Valor(headersN, row, HRut), 12);
                        Cliente? cliente;
                        Operacione op;
                        DateTime? fecha = Celda(headersN, row, HFecha) is IXLCell fcell ? Fecha(fcell) : null;

                        if (yaExistiaHistorico)
                        {
                            op = opExistente!;
                            cliente = clientes.FirstOrDefault(x => x.Id == op.IdCliente);
                            if (cliente == null)
                            {
                                filasOmitidas++;
                                if (avisos.Count < 30) avisos.Add($"{ws.Name} fila {r} · {booking}: el cliente vinculado al histórico ya no existe.");
                                continue;
                            }

                            if (N(cliente.RazonSocial) != N(clienteNombre) && N(cliente.NombreCliente) != N(clienteNombre) && avisos.Count < 30)
                                avisos.Add($"{ws.Name} fila {r} · {booking}: el Excel indica cliente '{clienteNombre}', pero el histórico ya pertenece a '{cliente.RazonSocial}'. Se conservó el cliente original.");

                            if (op.FechaImportacionHistorica != ahora) operacionesActualizadas++;
                            op.FechaImportacionHistorica = ahora;
                            op.OrigenHistorico = origen;
                            op.FechaModificacion = ahora;
                            op.UsuarioModificador = usuario;
                        }
                        else
                        {
                            cliente = ClientePor(clienteNombre, rut);
                            if (cliente == null)
                            {
                                cliente = new Cliente
                                {
                                    RazonSocial = Cortar(clienteNombre, 100)!,
                                    NombreCliente = Cortar(clienteNombre, 100),
                                    Rut = Cortar(rut, 12),
                                    Activo = 1,
                                    IsDeleted = false,
                                    FechaCreacion = ahora,
                                    FechaModificacion = ahora,
                                    UsuarioCreador = usuario,
                                    UsuarioModificador = usuario
                                };
                                _context.Clientes.Add(cliente);
                                await _context.SaveChangesAsync();
                                clientes.Add(cliente);
                            }

                            string? navieraNombre = Valor(headersN, row, HNaviera);
                            var naviera = await NavieraPor(navieraNombre);

                            op = new Operacione
                            {
                                NumeroBooking = booking,
                                IdCliente = cliente.Id,
                                IdNaviera = naviera.Id,
                                IdTipoCarga = 1,
                                Activo = 0,
                                IsDeleted = false,
                                EsHistorica = true,
                                OrigenHistorico = origen,
                                FechaImportacionHistorica = ahora,
                                FechaCreacion = fecha,
                                FechaModificacion = ahora,
                                UsuarioCreador = "IMPORTACIÓN HISTÓRICA",
                                UsuarioModificador = usuario,
                                EstadoWorkflow = "HISTÓRICO"
                            };
                            _context.Operaciones.Add(op);
                            await _context.SaveChangesAsync();

                            op.Finanzasoperacions.Add(new Finanzasoperacion
                            {
                                IdOperacion = op.Id,
                                FechaCreacion = fecha ?? ahora,
                                FechaModificacion = ahora,
                                UsuarioCreador = "IMPORTACIÓN HISTÓRICA",
                                UsuarioModificador = usuario,
                                ObservacionesFinanzas = $"Importado desde {origen}"
                            });
                            await _context.SaveChangesAsync();
                            porBooking[bkey] = op;
                            operacionesNuevas++;
                        }

                        foreach (var m in movimientosPendientes)
                        {
                            var prov = m.Tipo == "EGRESO" ? ProveedorPor(m.Proveedor) : null;
                            var tx = new TransaccionesFinanciera
                            {
                                IdOperacion = op.Id,
                                GrupoCobro = m.Grupo,
                                TipoMovimiento = m.Tipo,
                                ResponsablePago = m.Tipo == "INGRESO" ? "CLIENTE" : "ATILSON",
                                Responsable = m.Tipo == "INGRESO" ? "Cliente" : "Atilson",
                                Concepto = Cortar(m.Concepto, 250) ?? "Concepto histórico",
                                IdCliente = m.Tipo == "INGRESO" ? cliente.Id : null,
                                IdProveedor = prov?.Id,
                                ContraparteHistorica = Cortar(m.Tipo == "EGRESO" ? m.Proveedor : cliente.RazonSocial, 200),
                                MontoNeto = m.Monto,
                                ProfitHandling = 0m,
                                Moneda = m.Moneda,
                                EstadoFila = m.Estado,
                                NumeroDocumento = Cortar(m.Documento, 100),
                                FechaEmision = fecha,
                                FechaCreacion = fecha ?? ahora,
                                FechaModificacion = ahora,
                                UsuarioCreador = "IMPORTACIÓN HISTÓRICA",
                                UsuarioModificador = usuario,
                                TarifaManual = true,
                                JustificacionManual = $"Dato histórico importado desde {origen} · hoja {ws.Name} · fila {r}"
                            };
                            op.TransaccionesFinancieras.Add(tx);
                            movimientosNuevos++;
                            if (m.Tipo == "INGRESO") ingresosNuevos++; else egresosNuevos++;
                        }

                        await _context.SaveChangesAsync();
                    }
                }
    
                // Recalcula el pequeño caché financiero de cada histórico tocado para que
                // Facturación y reportes antiguos también lo reconozcan.
                foreach (var op in porBooking.Values.Where(o => o.FechaImportacionHistorica == ahora))
                {
                    var txs = op.TransaccionesFinancieras;
                    var fin = op.Finanzasoperacions.FirstOrDefault();
                    if (fin == null) continue;
                    decimal S(string tipo, params string[] grupos) => txs.Where(t => t.TipoMovimiento == tipo && grupos.Any(g => N(t.GrupoCobro) == N(g))).Sum(t => t.MontoNeto);
                    fin.VentaMaritimo = S("INGRESO", "Marítimo");
                    fin.VentaTerrestre = S("INGRESO", "Transporte", "Terrestre");
                    fin.VentaDocumental = S("INGRESO", "Documental", "Aduana");
                    fin.VentaGate = S("INGRESO", "Gate-Out/In", "Depósito");
                    fin.CostoMaritimoNeto = S("EGRESO", "Marítimo");
                    fin.CostoTerrestreNeto = S("EGRESO", "Transporte", "Terrestre");
                    fin.CostoAgenciaNeto = S("EGRESO", "Documental", "Aduana");
                    fin.CostoGateNeto = S("EGRESO", "Gate-Out/In", "Depósito");
                    fin.FechaModificacion = ahora;
                    fin.UsuarioModificador = usuario;
                }
                await _context.SaveChangesAsync();
    
                // No informar éxito si ninguna hoja o movimiento coincide con el formato.
                if (movimientosNuevos == 0)
                {
                    await importTx.RollbackAsync();
                    if (filasYaImportadas > 0 && filasOmitidas == 0)
                    {
                        TempData["SuccessMsg"] = $"No se agregaron movimientos: las {filasYaImportadas} filas reconocidas ya habían sido importadas. No se duplicó información.";
                        return RedirectToPage();
                    }
                    string motivo = hojasReconocidas == 0
                        ? "No se reconoció ninguna hoja con columnas Booking y Cliente."
                        : "Se reconocieron columnas, pero ninguna fila tenía un movimiento seguro (Concepto, Movimiento INGRESO/EGRESO y Monto distinto de cero).";
                    TempData["ErrorMsg"] = motivo + " No se importó ni modificó información. Descarga la plantilla y compara las columnas.";
                    if (avisos.Any()) TempData["ImportWarnings"] = string.Join("\n", avisos.Take(30));
                    return RedirectToPage();
                }

                if (soloValidar)
                {
                    await importTx.RollbackAsync();
                    TempData["PreviewMsg"] = $"PREVISUALIZACIÓN: se reconocieron {operacionesNuevas} bookings nuevos y {movimientosNuevos} movimientos ({ingresosNuevos} ingresos, {egresosNuevos} egresos). {filasOmitidas} filas omitidas; {filasYaImportadas} ya importadas. NO se guardó ningún dato. Revisa los conteos y, si corresponden, utiliza Importar histórico para confirmarlos.";
                    if (avisos.Any()) TempData["ImportWarnings"] = string.Join("\n", avisos.Take(30));
                    return RedirectToPage();
                }

                TempData["SuccessMsg"] = $"Importación histórica lista: {operacionesNuevas} booking(s) nuevos, {movimientosNuevos} movimiento(s) agregados ({ingresosNuevos} ingresos, {egresosNuevos} egresos), {filasOmitidas} fila(s) omitidas.";
                if (operacionesActualizadas > 0)
                    TempData["SuccessMsg"] += $" Se reconocieron {operacionesActualizadas} filas de bookings históricos ya existentes sin duplicarlos.";
                if (avisos.Any()) TempData["ImportWarnings"] = string.Join("\n", avisos.Take(30));
                await importTx.CommitAsync();
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                await importTx.RollbackAsync();
                System.Diagnostics.Trace.TraceError($"Importación histórica fallida: {ex}");
                TempData["ErrorMsg"] = "No se importó ningún dato. El archivo no pudo procesarse o tiene columnas inválidas; revisa la plantilla y vuelve a intentarlo. La importación fue revertida por completo.";
                return RedirectToPage();
            }
        }

        // ---------------------------------------------------------------------
        // Creación de operación (nov-2026, pedido de Cristian): mientras se resuelve la
        // importación de las planillas antiguas, Finanzas puede ingresar a mano una operación
        // histórica con sus columnas mínimas (booking, contenedor, nave y viaje, cliente,
        // proveedor) y sus conceptos (sección, concepto, valor neto, moneda, IVA y documentos).
        // Solo impacta Finanzas (cuentas por cobrar, por pagar y resultado): la operación queda
        // como histórica, bloqueada en Operaciones y sin tareas de Customer Service.
        // Si el booking ya existe (histórico u operación normal), los conceptos se agregan a él
        // en su sección, sin tocar los datos de la operación.
        // ---------------------------------------------------------------------
        public sealed class LineaCreacion
        {
            public string? Tipo { get; set; }
            public string? Seccion { get; set; }
            public string? Concepto { get; set; }
            public string? Proveedor { get; set; }       // Id del maestro, "otro" o vacío
            public string? ProveedorTexto { get; set; }
            public string? Monto { get; set; }
            public string? Moneda { get; set; }
            public string? Iva { get; set; }
            public string? Documentos { get; set; }
        }

        public sealed class CreacionInput
        {
            public string? Booking { get; set; }
            public string? Contenedor { get; set; }
            public string? NaveViaje { get; set; }
            public string? Cliente { get; set; }          // Id del maestro u "otro"
            public string? ClienteTexto { get; set; }
            public int? IdNaviera { get; set; }
            public string? Fecha { get; set; }            // yyyy-MM-dd
            public List<LineaCreacion> Lineas { get; set; } = new();
        }

        [BindProperty] public CreacionInput Creacion { get; set; } = new();

        public async Task<IActionResult> OnPostCrearOperacionAsync()
        {
            var c = Creacion;
            string usuario = User.Identity?.Name ?? "Finanzas";
            string booking = (c.Booking ?? "").Trim().ToUpperInvariant();
            if (booking.Length == 0 || booking.Length > 45)
            {
                TempData["ErrorMsg"] = "Creación de operación: ingresa el N° de booking (máximo 45 caracteres).";
                return RedirectToPage();
            }
            var lineas = (c.Lineas ?? new()).Where(l => !string.IsNullOrWhiteSpace(l.Concepto) || !string.IsNullOrWhiteSpace(l.Monto)).ToList();
            if (lineas.Count == 0)
            {
                TempData["ErrorMsg"] = "Creación de operación: agrega al menos un concepto con su valor.";
                return RedirectToPage();
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var op = await _context.Operaciones.FirstOrDefaultAsync(o => o.NumeroBooking == booking);
                bool nueva = op == null;
                if (op != null && op.IsDeleted)
                {
                    TempData["ErrorMsg"] = $"Creación de operación: el booking {booking} existió y fue eliminado; no se puede reutilizar.";
                    return RedirectToPage();
                }
                if (op != null && op.LockFinanzas)
                {
                    TempData["ErrorMsg"] = $"Creación de operación: el booking {booking} está liquidado y bloqueado en Finanzas.";
                    return RedirectToPage();
                }

                DateTime ahora = DateTime.Now;
                DateTime? fecha = DateTime.TryParseExact(c.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

                if (nueva)
                {
                    Cliente? cliente = null;
                    if (int.TryParse(c.Cliente, out int idCli))
                        cliente = await _context.Clientes.FirstOrDefaultAsync(x => x.Id == idCli && !x.IsDeleted);
                    else if (c.Cliente == "otro" && !string.IsNullOrWhiteSpace(c.ClienteTexto))
                    {
                        string nombre = Cortar(c.ClienteTexto.Trim().ToUpperInvariant(), 100)!;
                        cliente = await _context.Clientes.FirstOrDefaultAsync(x => !x.IsDeleted && (x.RazonSocial == nombre || x.NombreCliente == nombre));
                        if (cliente == null)
                        {
                            cliente = new Cliente { RazonSocial = nombre, NombreCliente = nombre, Activo = 1, IsDeleted = false, FechaCreacion = ahora, FechaModificacion = ahora, UsuarioCreador = usuario, UsuarioModificador = usuario };
                            _context.Clientes.Add(cliente);
                            await _context.SaveChangesAsync();
                        }
                    }
                    if (cliente == null)
                    {
                        TempData["ErrorMsg"] = "Creación de operación: elige el cliente (o escribe su nombre si no está en el maestro).";
                        return RedirectToPage();
                    }

                    int idNaviera;
                    if (c.IdNaviera is > 0 && await _context.Navieras.AnyAsync(n => n.Id == c.IdNaviera)) idNaviera = c.IdNaviera.Value;
                    else
                    {
                        var navHist = await _context.Navieras.FirstOrDefaultAsync(n => n.NombreNaviera == "HISTÓRICO / NO INFORMADO");
                        if (navHist == null)
                        {
                            navHist = new Naviera { NombreNaviera = "HISTÓRICO / NO INFORMADO", Activo = 0, IsDeleted = false, FechaCreacion = ahora, FechaModificacion = ahora, UsuarioCreador = usuario, UsuarioModificador = usuario };
                            _context.Navieras.Add(navHist);
                            await _context.SaveChangesAsync();
                        }
                        idNaviera = navHist.Id;
                    }

                    op = new Operacione
                    {
                        NumeroBooking = booking,
                        IdCliente = cliente.Id,
                        IdNaviera = idNaviera,
                        IdTipoCarga = 1,
                        Nave = Cortar(c.NaveViaje?.Trim().ToUpperInvariant(), 150),
                        NumeroContenedor = Cortar(c.Contenedor?.Trim().ToUpperInvariant(), 50),
                        Activo = 0,
                        IsDeleted = false,
                        EsHistorica = true,
                        OrigenHistorico = "Creación manual en Finanzas",
                        FechaImportacionHistorica = ahora,
                        FechaCreacion = fecha ?? ahora,
                        FechaModificacion = ahora,
                        UsuarioCreador = usuario,
                        UsuarioModificador = usuario,
                        EstadoWorkflow = "HISTÓRICO"
                    };
                    _context.Operaciones.Add(op);
                    await _context.SaveChangesAsync();
                    _context.Finanzasoperacions.Add(new Finanzasoperacion
                    {
                        IdOperacion = op.Id,
                        FechaCreacion = fecha ?? ahora,
                        FechaModificacion = ahora,
                        UsuarioCreador = usuario,
                        UsuarioModificador = usuario,
                        ObservacionesFinanzas = "Creación manual de operación histórica en Finanzas"
                    });
                }

                var errores = new List<string>();
                int n = 0;
                foreach (var l in lineas)
                {
                    n++;
                    var (idProv, provTexto, errorProv) = await ResolverProveedorAsync(l);
                    if (errorProv != null) { errores.Add($"Concepto {n}: {errorProv}"); continue; }
                    var r = await ConceptoExtraordinarioServicio.CrearAsync(_context, op!, new ConceptoExtraordinarioServicio.Datos(
                        l.Tipo, l.Seccion, l.Concepto, idProv, provTexto,
                        l.Monto, l.Moneda, l.Iva, l.Documentos, null, null), "Datos Históricos · Creación de operación", usuario, true);
                    if (!r.Ok) errores.Add($"Concepto {n}: {r.Mensaje}");
                }
                if (errores.Count > 0)
                {
                    await tx.RollbackAsync();
                    TempData["ErrorMsg"] = "Creación de operación: no se guardó nada. " + string.Join(" ", errores.Take(8));
                    return RedirectToPage();
                }
                await _context.SaveChangesAsync();

                if (op!.EsHistorica) await RecalcularCacheHistoricoAsync(op.Id, usuario);
                await tx.CommitAsync();

                TempData["SuccessMsg"] = nueva
                    ? $"Operación histórica {booking} creada con {lineas.Count} concepto(s). Ya aparece en Control Financiero."
                    : $"Se agregaron {lineas.Count} concepto(s) al booking {booking}{(op.EsHistorica ? " (histórico)" : " (operación existente)")}, en su sección.";
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                System.Diagnostics.Trace.TraceError($"Creación de operación histórica fallida: {ex}");
                TempData["ErrorMsg"] = "Creación de operación: no se pudo guardar. No se modificó ningún dato.";
                return RedirectToPage();
            }
        }

        // Proveedor de un concepto: "prov:id" es del maestro de proveedores (transportistas y depósitos);
        // navieras y agencias de aduana tienen su propio maestro, así que se guardan por nombre. Un
        // número solo (formularios anteriores) se toma como proveedor. "otro" = texto escrito.
        private async Task<(int? IdProveedor, string? Texto, string? Error)> ResolverProveedorAsync(LineaCreacion l)
        {
            string v = (l.Proveedor ?? "").Trim();
            if (v.Length == 0) return (null, null, null);
            if (v == "otro") return (null, l.ProveedorTexto, null);
            int Id(string pref) => int.TryParse(v.Substring(pref.Length), out int x) ? x : 0;
            if (v.StartsWith("prov:")) return (Id("prov:"), null, null);
            if (v.StartsWith("nav:"))
            {
                int id = Id("nav:");
                string? nombre = await _context.Navieras.AsNoTracking().Where(x => x.Id == id).Select(x => x.NombreNaviera).FirstOrDefaultAsync();
                return nombre == null ? (null, null, "la naviera elegida no existe.") : (null, nombre, null);
            }
            if (v.StartsWith("aga:"))
            {
                int id = Id("aga:");
                string? nombre = await _context.AgenciasAduanas.AsNoTracking().Where(x => x.Id == id).Select(x => x.NombreAgencia).FirstOrDefaultAsync();
                return nombre == null ? (null, null, "la agencia de aduana elegida no existe.") : (null, nombre, null);
            }
            return int.TryParse(v, out int idProv) ? (idProv, null, null) : (null, null, "proveedor no reconocido.");
        }

        // Pequeño caché financiero del histórico (mismo cálculo que la importación desde Excel).
        private async Task RecalcularCacheHistoricoAsync(int idOperacion, string usuario)
        {
            var txs = await _context.TransaccionesFinancieras.AsNoTracking().Where(t => t.IdOperacion == idOperacion).ToListAsync();
            var fin = await _context.Finanzasoperacions.FirstOrDefaultAsync(x => x.IdOperacion == idOperacion);
            if (fin == null) return;
            decimal S(string tipo, params string[] grupos) => txs.Where(t => t.TipoMovimiento == tipo && grupos.Any(g => N(t.GrupoCobro) == N(g))).Sum(t => t.MontoNeto);
            fin.VentaMaritimo = S("INGRESO", "Marítimo");
            fin.VentaTerrestre = S("INGRESO", "Transporte", "Terrestre");
            fin.VentaDocumental = S("INGRESO", "Documental", "Aduana");
            fin.VentaGate = S("INGRESO", "Gate-Out/In", "Depósito");
            fin.CostoMaritimoNeto = S("EGRESO", "Marítimo");
            fin.CostoTerrestreNeto = S("EGRESO", "Transporte", "Terrestre");
            fin.CostoAgenciaNeto = S("EGRESO", "Documental", "Aduana");
            fin.CostoGateNeto = S("EGRESO", "Gate-Out/In", "Depósito");
            fin.FechaModificacion = DateTime.Now;
            fin.UsuarioModificador = usuario;
            await _context.SaveChangesAsync();
        }
    }
}
