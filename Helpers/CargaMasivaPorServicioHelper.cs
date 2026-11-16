using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace AtilsonCargoSpa.Helpers
{
    // =====================================================================================
    // Cargas masivas de Comercial por servicio y concepto (13-nov-2026). Sobre las plantillas
    // generales de costos (CargaMasivaCostosHelper) y de ventas (CargaMasivaVentasHelper):
    //   - la plantilla de un servicio deja solo sus columnas, sus ejemplos y listas desplegables
    //     del servicio y de su concepto / subconcepto (equipo, movimiento, trámite…);
    //   - al subir con un servicio (y concepto) elegido, se rechazan las filas de otro servicio
    //     o concepto, para que una carga de un servicio no toque tarifas de otro.
    // Los lectores no cambian: las columnas conservan su posición (las que no aplican se ocultan).
    // =====================================================================================
    public static class CargaMasivaPorServicioHelper
    {
        public sealed record Definicion(string Servicio, string Nombre, int[] Columnas, int ColConcepto, string NombreConcepto, IReadOnlyList<string> Conceptos);

        public sealed record Tipo(string Hoja, int ColServicio, IReadOnlyList<Definicion> Servicios);

        private static readonly string[] EquiposMaritimos = CargaMasivaCostosHelper.EquiposMaritimos;
        private static readonly string[] EquiposTerrestres = { "DRY", "REEFER" };
        private static readonly string[] MovimientosGate = { "OUT", "IN", "AMBOS" };
        private static readonly string[] ContenedoresGate = { "20DRY", "40DRY", "DRY", "40RF", "RF" };

        public static Tipo Costos(IEnumerable<string> tramitesDocumentales) => new("Costos", 1, new[]
        {
            new Definicion("MARITIMO", "Marítimo (fletes de naviera)", new[] { 1, 2, 3, 5, 6, 10, 11, 13, 14, 15 }, 6, "Equipo", EquiposMaritimos),
            new Definicion("TERRESTRE", "Terrestre (fletes de transportista)", new[] { 1, 2, 3, 4, 5, 6, 10, 11, 13, 14, 15 }, 6, "Equipo", EquiposTerrestres),
            new Definicion("GATE", "Gate In / Out (depósitos y navieras)", new[] { 1, 2, 3, 6, 7, 10, 11, 13, 14, 15 }, 7, "Movimiento", MovimientosGate),
            new Definicion("DOCUMENTAL", "Documental (agencias de aduana)", new[] { 1, 2, 8, 9, 10, 11, 12, 13, 14, 15 }, 8, "Trámite", Limpiar(tramitesDocumentales)),
        });

        public static Tipo Ventas(IEnumerable<string> conceptosAlmacenamiento, IEnumerable<string> conceptosCourier) => new("Ventas", 2, new[]
        {
            new Definicion("MARITIMO", "Marítimo", new[] { 1, 2, 3, 5, 6, 7, 12, 13, 14, 15, 16, 17 }, 7, "Contenedor", EquiposMaritimos),
            new Definicion("TERRESTRE", "Terrestre", new[] { 1, 2, 3, 4, 5, 7, 12, 13, 14, 15, 16, 17 }, 7, "Contenedor", EquiposTerrestres),
            new Definicion("GATE", "Gate In / Out", new[] { 1, 2, 3, 6, 7, 8, 12, 13, 14, 15, 16, 17 }, 8, "Movimiento", MovimientosGate),
            new Definicion("ALMACENAMIENTO", "Almacenamiento", new[] { 1, 2, 9, 12, 13, 14, 15, 16, 17 }, 9, "Concepto", Limpiar(conceptosAlmacenamiento)),
            new Definicion("COURIER", "Courier", new[] { 1, 2, 9, 10, 11, 12, 13, 14, 15, 16, 17 }, 9, "Concepto", Limpiar(conceptosCourier)),
        });

        private static List<string> Limpiar(IEnumerable<string> valores) =>
            valores.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

        public static Definicion? Buscar(Tipo tipo, string? servicio)
        {
            string s = NormServicio(servicio);
            return s == "" ? null : tipo.Servicios.FirstOrDefault(d => d.Servicio == s);
        }

        public static string NormServicio(string? s)
        {
            string n = VentaClienteHelper.Norm(s).Replace(" ", "");
            return n switch { "MARITIMA" => "MARITIMO", "TRANSPORTE" => "TERRESTRE", "DEPOSITO" => "GATE", _ => n };
        }

        private static bool MismoConcepto(Definicion def, string? valor, string concepto)
        {
            string a = VentaClienteHelper.Norm(valor), b = VentaClienteHelper.Norm(concepto);
            if (def.NombreConcepto == "Equipo" || def.NombreConcepto == "Contenedor")
            {
                if (def.Servicio == "MARITIMO")
                    return CargaMasivaCostosHelper.NormalizarEquipoMaritimo(valor) == CargaMasivaCostosHelper.NormalizarEquipoMaritimo(concepto);
                bool rfA = a.Contains("RF") || a.Contains("REEFER"), rfB = b.Contains("RF") || b.Contains("REEFER");
                return rfA == rfB;
            }
            return a == b;
        }

        /// <summary>Plantilla de un servicio a partir de la plantilla general (bytes).</summary>
        public static byte[] PlantillaDeServicio(byte[] general, Tipo tipo, Definicion def, string? concepto)
        {
            using var entrada = new MemoryStream(general);
            using var wb = new XLWorkbook(entrada);
            var ws = wb.Worksheet(tipo.Hoja);
            int ultimaCol = ws.LastColumnUsed()?.ColumnNumber() ?? 20;
            int ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 1;

            // Solo los ejemplos del servicio (al menos uno).
            for (int r = ultimaFila; r >= 2; r--)
                if (NormServicio(ws.Cell(r, tipo.ColServicio).GetString()) != def.Servicio) ws.Row(r).Delete();
            if ((ws.LastRowUsed()?.RowNumber() ?? 1) < 2) ws.Cell(2, tipo.ColServicio).Value = def.Servicio;
            if (!string.IsNullOrWhiteSpace(concepto))
                for (int r = 2; r <= (ws.LastRowUsed()?.RowNumber() ?? 1); r++) ws.Cell(r, def.ColConcepto).Value = concepto.Trim().ToUpperInvariant();

            for (int c = 1; c <= ultimaCol; c++)
                if (!def.Columnas.Contains(c)) ws.Column(c).Hide();
            var cab = ws.Cell(1, def.ColConcepto);
            cab.Value = def.NombreConcepto + " (concepto)";
            cab.Style.Fill.BackgroundColor = XLColor.FromHtml("#6D2D9E");

            // Listas desplegables (hoja "Listas"): servicio fijo y conceptos del servicio.
            var opciones = string.IsNullOrWhiteSpace(concepto) ? def.Conceptos : new[] { concepto.Trim().ToUpperInvariant() };
            var listas = wb.Worksheets.Add("Listas");
            listas.Cell(1, 1).Value = "SERVICIO";
            listas.Cell(2, 1).Value = def.Servicio;
            listas.Cell(1, 2).Value = def.NombreConcepto.ToUpperInvariant();
            listas.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < opciones.Count; i++) listas.Cell(i + 2, 2).Value = opciones[i];
            listas.Columns().AdjustToContents(10.0, 60.0);
            ws.Range(2, tipo.ColServicio, 1000, tipo.ColServicio).CreateDataValidation().List(listas.Range(2, 1, 2, 1), true);
            if (opciones.Count > 0)
            {
                var dv = ws.Range(2, def.ColConcepto, 1000, def.ColConcepto).CreateDataValidation();
                dv.List(listas.Range(2, 2, opciones.Count + 1, 2), true);
                // Documental, almacenamiento y courier admiten conceptos nuevos: la lista sugiere, no obliga.
                if (def.Servicio == "DOCUMENTAL" || def.Servicio == "ALMACENAMIENTO" || def.Servicio == "COURIER") dv.ShowErrorMessage = false;
            }

            var ayuda = wb.Worksheets.FirstOrDefault(w => w.Name == "Instrucciones");
            if (ayuda != null)
            {
                ayuda.Row(1).InsertRowsAbove(2);
                ayuda.Cell(1, 1).Value = $"Plantilla de {def.Nombre.ToUpperInvariant()}"
                    + (string.IsNullOrWhiteSpace(concepto) ? "" : $" · {def.NombreConcepto.ToLowerInvariant()} {concepto.Trim().ToUpperInvariant()}")
                    + ": solo se muestran sus columnas. Al subirla con este servicio elegido, se rechazan las filas de otro servicio"
                    + (string.IsNullOrWhiteSpace(concepto) ? "." : " o concepto.");
                ayuda.Cell(1, 1).Style.Font.Bold = true;
                ayuda.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#6D2D9E");
            }
            ws.Columns().AdjustToContents(8.0, 40.0);

            using var salida = new MemoryStream();
            wb.SaveAs(salida);
            return salida.ToArray();
        }

        /// <summary>Filas de otro servicio (o concepto) que la carga elegida no admite.</summary>
        public static List<string> FilasFuera(Stream archivo, Tipo tipo, Definicion def, string? concepto)
        {
            var errores = new List<string>();
            using var wb = new XLWorkbook(archivo);
            var ws = wb.Worksheets.FirstOrDefault(w => VentaClienteHelper.Norm(w.Name) == VentaClienteHelper.Norm(tipo.Hoja)) ?? wb.Worksheet(1);
            int ultima = ws.LastRowUsed()?.RowNumber() ?? 1;
            int cols = Math.Max(17, ws.LastColumnUsed()?.ColumnNumber() ?? 17);
            for (int r = 2; r <= ultima; r++)
            {
                var row = ws.Row(r);
                if (Enumerable.Range(1, cols).All(c => string.IsNullOrWhiteSpace(row.Cell(c).GetFormattedString()))) continue;
                string servicio = NormServicio(row.Cell(tipo.ColServicio).GetFormattedString());
                if (servicio != def.Servicio)
                    errores.Add($"Fila {r}: es de {(servicio == "" ? "un servicio vacío" : servicio)}; esta carga es solo de {def.Servicio}.");
                else if (!string.IsNullOrWhiteSpace(concepto) && !MismoConcepto(def, row.Cell(def.ColConcepto).GetFormattedString(), concepto))
                    errores.Add($"Fila {r}: {def.NombreConcepto.ToLowerInvariant()} \"{row.Cell(def.ColConcepto).GetFormattedString()}\"; esta carga es solo de {concepto.Trim().ToUpperInvariant()}.");
            }
            return errores;
        }
    }
}
