using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AtilsonCargoSpa.Models;
using ClosedXML.Excel;

namespace AtilsonCargoSpa.Helpers
{
    // Carga masiva de tarifas de VENTA a clientes (minuta oct-2026). Una fila del Excel =
    // una tarifa en TarifasClientes, con la misma forma que guardan los formularios de
    // Comercial > Clientes, para que Finanzas las encuentre igual (VentaClienteHelper).
    public static class CargaMasivaVentasHelper
    {
        public static readonly string[] Columnas =
        {
            "Cliente (razón social o RUT)", "Servicio", "Origen / POL / Depósito", "Planta",
            "Destino / POD", "Naviera", "Contenedor", "Movimiento Gate", "Concepto",
            "Empresa Courier", "Contrato Courier", "Moneda", "Tarifa", "Aplica IVA",
            "Vigente desde", "Vigente hasta", "Comentarios"
        };

        public static readonly string[] Servicios = { "MARITIMO", "TERRESTRE", "GATE", "ALMACENAMIENTO", "COURIER" };

        public record Resultado(List<TarifasCliente> Tarifas, List<string> Errores, int FilasLeidas);

        public static byte[] GenerarPlantilla(IEnumerable<string> clientes, IEnumerable<string> navieras, IEnumerable<string> depositos, IEnumerable<string> puertos)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Ventas");
            for (int i = 0; i < Columnas.Length; i++)
            {
                var c = ws.Cell(1, i + 1);
                c.Value = Columnas[i];
                c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F172A");
                c.Style.Font.FontColor = XLColor.White;
            }
            // Ejemplos (se pueden borrar)
            object[][] ejemplos =
            {
                new object[] { "CLIENTE EJEMPLO SPA", "MARITIMO", "SAN ANTONIO", "", "SHANGHAI", "MSC", "40' DRY HC", "", "", "", "", "USD", 1850, "NO", DateTime.Today, DateTime.Today.AddMonths(3), "Ejemplo" },
                new object[] { "CLIENTE EJEMPLO SPA", "TERRESTRE", "SAN ANTONIO", "RANCAGUA", "SAN ANTONIO", "", "DRY", "", "", "", "", "CLP", 650000, "SI", DateTime.Today, DateTime.Today.AddYears(1), "" },
                new object[] { "CLIENTE EJEMPLO SPA", "GATE", "", "", "", "HAPAG LLOYD", "40RF", "OUT", "", "", "", "CLP", 165000, "SI", DateTime.Today, DateTime.Today.AddYears(1), "Depósito vacío = cualquier depósito" },
                new object[] { "CLIENTE EJEMPLO SPA", "ALMACENAMIENTO", "", "", "", "", "", "", "SERVICIO DE ALMACENAJE", "", "", "CLP", 45000, "SI", DateTime.Today, DateTime.Today.AddYears(1), "" },
                new object[] { "CLIENTE EJEMPLO SPA", "COURIER", "", "", "", "", "", "", CourierHelper.ConceptoNacional, "DHL", "STEMBEX", "CLP", 25000, "SI", DateTime.Today, DateTime.Today.AddYears(1), "" }
            };
            for (int r = 0; r < ejemplos.Length; r++)
                for (int c = 0; c < ejemplos[r].Length; c++)
                {
                    var celda = ws.Cell(r + 2, c + 1);
                    switch (ejemplos[r][c])
                    {
                        case DateTime d: celda.Value = d; celda.Style.DateFormat.Format = "dd-mm-yyyy"; break;
                        case int n: celda.Value = (double)n; break;
                        default: celda.Value = ejemplos[r][c]?.ToString() ?? ""; break;
                    }
                    celda.Style.Font.FontColor = XLColor.FromHtml("#64748B");
                }
            ws.Columns().AdjustToContents(8.0, 40.0);
            ws.SheetView.FreezeRows(1);

            var ayuda = wb.Worksheets.Add("Instrucciones");
            string[] lineas =
            {
                "Una fila por tarifa. Borra las filas de ejemplo antes de subir.",
                "Servicio: MARITIMO, TERRESTRE, GATE, ALMACENAMIENTO o COURIER.",
                "MARITIMO: obligatorio Origen/POL y Destino/POD. Contenedor: ALL, 20' DRY STD, 40' DRY STD, 40' DRY HC, 20' REEFER, 40' REEFER. Naviera opcional.",
                "TERRESTRE: obligatorio Origen (retiro) y Destino; Planta opcional. Contenedor: ALL, DRY o REEFER.",
                "GATE: Movimiento OUT, IN o AMBOS. Contenedor: 20DRY, 40DRY, DRY (20 y 40) o 40RF; vacío = todos. Depósito en 'Origen / POL / Depósito' (vacío = cualquiera). Naviera opcional.",
                "ALMACENAMIENTO: Concepto opcional (por defecto SERVICIO DE ALMACENAJE).",
                "COURIER: Concepto = COURIER NACIONAL o COURIER INTERNACIONAL <ZONA>. Empresa: DHL, FEDEX o CHILEXPRESS. Contrato: STEMBEX, PROPIO u OTRA_AGA.",
                "Moneda: CLP, USD o UF. Tarifa: número sin símbolo (se acepta 1.234,50 o 1234.50). Aplica IVA: SI / NO.",
                "Vigente desde vacío = hoy; Vigente hasta vacío = un año.",
                "Los nombres de cliente, naviera, depósito y puerto deben coincidir con las hojas de listas."
            };
            for (int i = 0; i < lineas.Length; i++) ayuda.Cell(i + 1, 1).Value = lineas[i];
            ayuda.Column(1).Width = 140;

            void Lista(string nombre, IEnumerable<string> valores)
            {
                var hoja = wb.Worksheets.Add(nombre);
                hoja.Cell(1, 1).Value = nombre.ToUpper();
                hoja.Cell(1, 1).Style.Font.Bold = true;
                int f = 2;
                foreach (var v in valores) hoja.Cell(f++, 1).Value = v;
                hoja.Column(1).AdjustToContents(10.0, 60.0);
            }
            Lista("Clientes", clientes);
            Lista("Navieras", navieras);
            Lista("Depositos", depositos);
            Lista("Puertos", puertos);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public static Resultado Leer(Stream archivo, IReadOnlyList<Cliente> clientes, IReadOnlyList<Naviera> navieras, IReadOnlyList<Deposito> depositos, string usuario)
        {
            var tarifas = new List<TarifasCliente>();
            var errores = new List<string>();
            int leidas = 0;

            using var wb = new XLWorkbook(archivo);
            var ws = wb.Worksheets.FirstOrDefault(w => VentaClienteHelper.Norm(w.Name) == "VENTAS") ?? wb.Worksheet(1);
            var ultima = ws.LastRowUsed()?.RowNumber() ?? 1;

            string Txt(IXLRow row, int col) => row.Cell(col).GetFormattedString().Trim();

            for (int r = 2; r <= ultima; r++)
            {
                var row = ws.Row(r);
                if (Enumerable.Range(1, Columnas.Length).All(c => string.IsNullOrWhiteSpace(Txt(row, c)))) continue;
                leidas++;
                string fila = $"Fila {r}";

                string clienteTxt = Txt(row, 1);
                var cliente = BuscarCliente(clientes, clienteTxt);
                if (cliente == null) { errores.Add($"{fila}: cliente \"{clienteTxt}\" no existe o no está activo."); continue; }

                string servicio = VentaClienteHelper.Norm(Txt(row, 2)).Replace(" ", "");
                if (servicio == "MARITIMA") servicio = "MARITIMO";
                if (!Servicios.Contains(servicio)) { errores.Add($"{fila}: servicio \"{Txt(row, 2)}\" no válido (MARITIMO, TERRESTRE, GATE, ALMACENAMIENTO, COURIER)."); continue; }

                if (!LeerMonto(row.Cell(13), out decimal monto) || monto <= 0) { errores.Add($"{fila}: tarifa vacía o no numérica."); continue; }

                string moneda = VentaClienteHelper.Norm(Txt(row, 12));
                if (moneda == "") moneda = "CLP";
                if (moneda != "CLP" && moneda != "USD" && moneda != "UF") { errores.Add($"{fila}: moneda \"{moneda}\" no válida (CLP, USD o UF)."); continue; }

                string ivaTxt = VentaClienteHelper.Norm(Txt(row, 14));
                bool aplicaIva = servicio == "MARITIMO" ? (ivaTxt == "SI" || ivaTxt == "S" || ivaTxt == "TRUE") : !(ivaTxt == "NO" || ivaTxt == "N" || ivaTxt == "FALSE" || ivaTxt == "EXENTO");

                DateTime desde = LeerFecha(row.Cell(15)) ?? DateTime.Today;
                DateTime hasta = LeerFecha(row.Cell(16)) ?? desde.AddYears(1);
                if (hasta < desde) { errores.Add($"{fila}: 'Vigente hasta' es anterior a 'Vigente desde'."); continue; }

                int? idNaviera = null;
                string navTxt = Txt(row, 6);
                if (navTxt != "")
                {
                    var nav = navieras.FirstOrDefault(n => VentaClienteHelper.Norm(n.NombreNaviera) == VentaClienteHelper.Norm(navTxt));
                    if (nav == null) { errores.Add($"{fila}: naviera \"{navTxt}\" no existe."); continue; }
                    idNaviera = nav.Id;
                }

                string origen = Txt(row, 3).ToUpper(), planta = Txt(row, 4).ToUpper(), destino = Txt(row, 5).ToUpper();
                string contenedor = Txt(row, 7).ToUpper();
                string concepto = Txt(row, 9).ToUpper();
                string comentarios = Txt(row, 17);

                TarifasCliente Base(string grupo, string? conceptoFinal) => new TarifasCliente
                {
                    IdCliente = cliente.Id,
                    GrupoCobro = grupo,
                    Concepto = conceptoFinal,
                    PrecioPactado = monto,
                    Moneda = moneda,
                    AplicaIva = aplicaIva,
                    FechaInicioVigencia = desde,
                    FechaFinVigencia = hasta,
                    EsActiva = true,
                    UsuarioCreador = usuario,
                    IdNaviera = idNaviera,
                    Comentarios = string.IsNullOrWhiteSpace(comentarios) ? "CARGA MASIVA" : comentarios.ToUpper()
                };

                switch (servicio)
                {
                    case "MARITIMO":
                        if (origen == "" || destino == "") { errores.Add($"{fila}: Marítimo requiere Origen/POL y Destino/POD."); continue; }
                        var m = Base("Marítimo", "FLETE MARÍTIMO");
                        m.Pol = origen; m.Pod = destino; m.TipoContenedor = contenedor == "" ? "ALL" : contenedor;
                        tarifas.Add(m);
                        break;
                    case "TERRESTRE":
                        if (origen == "" || destino == "") { errores.Add($"{fila}: Terrestre requiere Origen (retiro) y Destino."); continue; }
                        var t = Base("Terrestre", "FLETE TERRESTRE");
                        t.Pol = origen; t.Pod = destino; t.ZonaPlanta = planta == "" ? null : planta;
                        t.TipoContenedor = VentaClienteHelper.FamiliaEquipo(contenedor) switch { "RF" => "REEFER", "DRY" => "DRY", _ => "ALL" };
                        tarifas.Add(t);
                        break;
                    case "GATE":
                        string mov = VentaClienteHelper.Norm(Txt(row, 8));
                        var movs = mov == "AMBOS" ? new[] { "OUT", "IN" } : mov == "IN" ? new[] { "IN" } : mov == "OUT" || mov == "" ? new[] { "OUT" } : Array.Empty<string>();
                        if (movs.Length == 0) { errores.Add($"{fila}: movimiento Gate \"{mov}\" no válido (OUT, IN o AMBOS)."); continue; }
                        string? depGate = null;
                        if (origen != "")
                        {
                            var dep = depositos.FirstOrDefault(d => VentaClienteHelper.Norm(d.NombreDeposito) == VentaClienteHelper.Norm(origen));
                            if (dep == null) { errores.Add($"{fila}: depósito \"{origen}\" no existe."); continue; }
                            depGate = dep.NombreDeposito.Trim().ToUpper();
                        }
                        string tcGate = contenedor.Replace(" ", "").Replace("'", "");
                        string? tcFinal = tcGate switch
                        {
                            "" or "ALL" or "TODOS" => null,
                            "20DRY" or "40DRY" or "40RF" or "DRY" => tcGate,
                            "40REEFER" => "40RF",
                            _ => "X"
                        };
                        if (tcFinal == "X") { errores.Add($"{fila}: contenedor Gate \"{contenedor}\" no válido (20DRY, 40DRY, DRY, 40RF)."); continue; }
                        foreach (var mv in movs)
                        {
                            var g = Base("Gate", "GATE " + mv);
                            g.Pol = depGate; g.TipoContenedor = tcFinal;
                            tarifas.Add(g);
                        }
                        break;
                    case "ALMACENAMIENTO":
                        tarifas.Add(Base("Almacenamiento", concepto == "" ? "SERVICIO DE ALMACENAJE" : concepto));
                        break;
                    case "COURIER":
                        bool conceptoOk = concepto == CourierHelper.ConceptoNacional || CourierHelper.ConceptosInternacionales.Contains(concepto);
                        if (!conceptoOk) { errores.Add($"{fila}: concepto courier \"{concepto}\" no válido."); continue; }
                        string? empresa = CourierHelper.Normalizar(Txt(row, 10));
                        if (empresa == null) { errores.Add($"{fila}: empresa courier \"{Txt(row, 10)}\" no válida (DHL, FEDEX, CHILEXPRESS)."); continue; }
                        var cou = Base("Courier", concepto);
                        cou.Clasificacion = empresa;
                        cou.ZonaPlanta = CourierHelper.NormalizarContrato(Txt(row, 11));
                        tarifas.Add(cou);
                        break;
                }
            }
            return new Resultado(tarifas, errores, leidas);
        }

        private static Cliente? BuscarCliente(IReadOnlyList<Cliente> clientes, string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            string n = VentaClienteHelper.Norm(texto);
            string rut = n.Replace(".", "").Replace("-", "").Replace(" ", "");
            return clientes.FirstOrDefault(c => VentaClienteHelper.Norm(c.RazonSocial) == n)
                ?? clientes.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Rut) && VentaClienteHelper.Norm(c.Rut).Replace(".", "").Replace("-", "").Replace(" ", "") == rut);
        }

        private static bool LeerMonto(IXLCell celda, out decimal valor)
        {
            valor = 0m;
            if (celda.DataType == XLDataType.Number) { valor = (decimal)celda.GetDouble(); return true; }
            var t = celda.GetFormattedString().Trim().Replace("$", "").Replace(" ", "");
            if (t == "") return false;
            if (t.Contains(',') && t.Contains('.'))
                t = t.LastIndexOf(',') > t.LastIndexOf('.') ? t.Replace(".", "").Replace(",", ".") : t.Replace(",", "");
            else if (t.Contains(','))
                t = t.Replace(",", ".");
            else if (t.Count(ch => ch == '.') > 1)
                t = t.Replace(".", "");
            return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
        }

        private static DateTime? LeerFecha(IXLCell celda)
        {
            if (celda.IsEmpty()) return null;
            if (celda.DataType == XLDataType.DateTime) return celda.GetDateTime().Date;
            if (celda.DataType == XLDataType.Number) return DateTime.FromOADate(celda.GetDouble()).Date;
            var t = celda.GetFormattedString().Trim();
            string[] formatos = { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy" };
            return DateTime.TryParseExact(t, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
        }
    }
}
