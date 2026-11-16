using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AtilsonCargoSpa.Models;
using ClosedXML.Excel;

namespace AtilsonCargoSpa.Helpers
{
    // Carga masiva de tarifas de COSTO (proveedores), nov-2026. Complementa la carga masiva
    // de ventas: una fila del Excel = una tarifa, con la misma forma que guardan los
    // formularios de Comercial > Proveedores, para que Finanzas las cruce igual.
    //   MARITIMO   → TarifasMaritimas   (naviera + POL + POD + equipo, USD)
    //   TERRESTRE  → TarifasTerrestres  (transportista + origen + planta + destino + Dry/Reefer, CLP)
    //   GATE       → TarifasGate        (depósito y/o naviera + movimiento + contenedor)
    //   DOCUMENTAL → TarifasDocumentales (agencia + trámite + clasificación)
    public static class CargaMasivaCostosHelper
    {
        public static readonly string[] Columnas =
        {
            "Servicio", "Proveedor (naviera, transportista o agencia)", "Origen / POL / Depósito", "Planta",
            "Destino / POD", "Equipo / Contenedor", "Movimiento Gate", "Trámite (Documental)",
            "Clasificación (Documental)", "Moneda", "Valor neto", "Aplica IVA",
            "Vigente desde", "Vigente hasta", "Comentarios"
        };

        public static readonly string[] Servicios = { "MARITIMO", "TERRESTRE", "GATE", "DOCUMENTAL" };

        public static readonly string[] EquiposMaritimos = { "20' DRY STD", "40' DRY STD", "40' DRY HC", "20' REEFER", "40' REEFER" };

        public sealed class Resultado
        {
            public List<TarifasMaritima> Maritimas { get; } = new();
            public List<Tarifasterrestre> Terrestres { get; } = new();
            public List<TarifaGate> Gates { get; } = new();
            public List<TarifasDocumentale> Documentales { get; } = new();
            public List<string> Errores { get; } = new();
            public int FilasLeidas { get; set; }
            public int Total => Maritimas.Count + Terrestres.Count + Gates.Count + Documentales.Count;
        }

        public sealed class Maestros
        {
            public IReadOnlyList<Naviera> Navieras { get; init; } = Array.Empty<Naviera>();
            public IReadOnlyList<Proveedore> Proveedores { get; init; } = Array.Empty<Proveedore>();
            public IReadOnlyList<Deposito> Depositos { get; init; } = Array.Empty<Deposito>();
            public IReadOnlyList<AgenciasAduana> Agencias { get; init; } = Array.Empty<AgenciasAduana>();
            public IReadOnlyList<Ciudade> Ciudades { get; init; } = Array.Empty<Ciudade>();
            public IReadOnlyList<string> Puertos { get; init; } = Array.Empty<string>();
            /// <summary>País de cada puerto por nombre (PuertosHelper.MapaPaisesAsync): POL y POD de distinto país.</summary>
            public IReadOnlyDictionary<string, string> PaisPorPuerto { get; init; } = new Dictionary<string, string>();
        }

        public static byte[] GenerarPlantilla(Maestros m)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Costos");
            for (int i = 0; i < Columnas.Length; i++)
            {
                var c = ws.Cell(1, i + 1);
                c.Value = Columnas[i];
                c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#2C1B4D");
                c.Style.Font.FontColor = XLColor.White;
            }
            string nav = m.Navieras.Select(n => n.NombreNaviera).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "MSC";
            string prov = m.Proveedores.Select(p => p.NombreProveedor).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "TRANSPORTES EJEMPLO";
            string dep = m.Depositos.Select(d => d.NombreDeposito).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "";
            string aga = m.Agencias.Select(a => a.NombreAgencia).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "AGENCIA EJEMPLO";
            object[][] ejemplos =
            {
                new object[] { "MARITIMO", nav, "SAN ANTONIO", "", "SHANGHAI", "40' REEFER", "", "", "", "USD", 2850.5, "", DateTime.Today, DateTime.Today.AddMonths(3), "Ejemplo" },
                new object[] { "TERRESTRE", prov, "SAN ANTONIO", "RANCAGUA", "SAN ANTONIO", "REEFER", "", "", "", "CLP", 450000, "", DateTime.Today, "", "" },
                new object[] { "GATE", nav, dep, "", "", "40RF", "OUT", "", "", "CLP", 95000, "", DateTime.Today, "", "Proveedor = naviera (opcional)" },
                new object[] { "DOCUMENTAL", aga, "", "", "", "", "", "HONORARIOS AGENCIA DE ADUANAS", "SAG", "CLP", 55000, "SI", DateTime.Today, "", "" }
            };
            for (int r = 0; r < ejemplos.Length; r++)
                for (int c = 0; c < ejemplos[r].Length; c++)
                {
                    var celda = ws.Cell(r + 2, c + 1);
                    switch (ejemplos[r][c])
                    {
                        case DateTime d: celda.Value = d; celda.Style.DateFormat.Format = "dd-mm-yyyy"; break;
                        case int n: celda.Value = (double)n; break;
                        case double x: celda.Value = x; break;
                        default: celda.Value = ejemplos[r][c]?.ToString() ?? ""; break;
                    }
                    celda.Style.Font.FontColor = XLColor.FromHtml("#64748B");
                }
            ws.Columns().AdjustToContents(8.0, 40.0);
            ws.SheetView.FreezeRows(1);

            var ayuda = wb.Worksheets.Add("Instrucciones");
            string[] lineas =
            {
                "Una fila por tarifa de costo. Borra las filas de ejemplo antes de subir.",
                "Servicio: MARITIMO, TERRESTRE, GATE o DOCUMENTAL.",
                "MARITIMO: Proveedor = naviera. Obligatorios POL y POD (nombres de la hoja Puertos). Equipo: " + string.Join(", ", EquiposMaritimos) + " (también 40RF, 40HC, 20DRY…). Moneda USD.",
                "TERRESTRE: Proveedor = transportista. Origen (retiro), Planta (opcional) y Destino son ciudades de la hoja Ciudades. Equipo: DRY o REEFER. Moneda CLP. Los falsos fletes se calculan como en el formulario (90/80/70%).",
                "GATE: Depósito en 'Origen / POL / Depósito' y/o naviera en 'Proveedor' (al menos uno). Movimiento OUT, IN o AMBOS. Contenedor: 20DRY, 40DRY, DRY, 40RF o RF. Moneda CLP o USD.",
                "DOCUMENTAL: Proveedor = agencia de aduanas. Trámite obligatorio (ej. HONORARIOS AGENCIA DE ADUANAS, LEGALIZACIÓN DUS EMBARQUES NORMALES, CERTIFICADO FITOSANITARIO). Clasificación COMUN, SAG o SERNAPESCA. Moneda CLP, USD o UF. Aplica IVA: SI / NO. Reemplaza la tarifa vigente del mismo trámite y agencia.",
                "Valor neto: número sin símbolo; se acepta 450.000, 1.200,75 o 2850.50.",
                "Vigente desde vacío = hoy; Vigente hasta vacío = sin fecha de término.",
                "Si alguna fila tiene un error no se guarda ninguna: se indica la fila y qué corregir."
            };
            for (int i = 0; i < lineas.Length; i++) ayuda.Cell(i + 1, 1).Value = lineas[i];
            ayuda.Column(1).Width = 150;

            void Lista(string nombre, IEnumerable<string> valores)
            {
                var hoja = wb.Worksheets.Add(nombre);
                hoja.Cell(1, 1).Value = nombre.ToUpper();
                hoja.Cell(1, 1).Style.Font.Bold = true;
                int f = 2;
                foreach (var v in valores.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
                    hoja.Cell(f++, 1).Value = v;
                hoja.Column(1).AdjustToContents(10.0, 60.0);
            }
            Lista("Navieras", m.Navieras.Select(n => n.NombreNaviera ?? ""));
            Lista("Transportistas", m.Proveedores.Select(p => p.NombreProveedor));
            Lista("Agencias", m.Agencias.Select(a => a.NombreAgencia));
            Lista("Depositos", m.Depositos.Select(d => d.NombreDeposito));
            Lista("Puertos", m.Puertos);
            Lista("Ciudades", m.Ciudades.Select(c => c.Nombre));

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public static Resultado Leer(Stream archivo, Maestros m, string usuario)
        {
            var res = new Resultado();
            string N(string? t) => VentaClienteHelper.Norm(t);

            using var wb = new XLWorkbook(archivo);
            var ws = wb.Worksheets.FirstOrDefault(w => N(w.Name) == "COSTOS") ?? wb.Worksheet(1);
            var ultima = ws.LastRowUsed()?.RowNumber() ?? 1;
            string Txt(IXLRow row, int col) => row.Cell(col).GetFormattedString().Trim();
            var clavesDocumental = new Dictionary<string, int>();
            DateTime ahora = DateTime.Now;

            for (int r = 2; r <= ultima; r++)
            {
                var row = ws.Row(r);
                if (Enumerable.Range(1, Columnas.Length).All(c => string.IsNullOrWhiteSpace(Txt(row, c)))) continue;
                res.FilasLeidas++;
                string fila = $"Fila {r}";

                string servicio = N(Txt(row, 1)).Replace(" ", "");
                if (servicio == "MARITIMA") servicio = "MARITIMO";
                if (servicio == "TRANSPORTE") servicio = "TERRESTRE";
                if (servicio == "DEPOSITO") servicio = "GATE";
                if (!Servicios.Contains(servicio)) { res.Errores.Add($"{fila}: servicio \"{Txt(row, 1)}\" no válido (MARITIMO, TERRESTRE, GATE, DOCUMENTAL)."); continue; }

                if (!LeerMonto(row.Cell(11), out decimal monto)) { res.Errores.Add($"{fila}: valor neto vacío o no numérico."); continue; }
                if (monto < 0) { res.Errores.Add($"{fila}: el valor neto no puede ser negativo."); continue; }

                string proveedorTxt = Txt(row, 2);
                string origen = Txt(row, 3), planta = Txt(row, 4), destino = Txt(row, 5);
                string equipo = Txt(row, 6);
                string moneda = N(Txt(row, 10));
                string comentarios = Txt(row, 15);
                DateTime desde = LeerFecha(row.Cell(13)) ?? DateTime.Today;
                DateTime? hasta = LeerFecha(row.Cell(14));
                if (hasta.HasValue && hasta.Value < desde) { res.Errores.Add($"{fila}: 'Vigente hasta' es anterior a 'Vigente desde'."); continue; }

                switch (servicio)
                {
                    case "MARITIMO":
                    {
                        var nav = BuscarPorNombre(m.Navieras, n => n.NombreNaviera, proveedorTxt);
                        if (nav == null) { res.Errores.Add($"{fila}: naviera \"{proveedorTxt}\" no existe (ver hoja Navieras)."); continue; }
                        string? pol = BuscarPuerto(m.Puertos, origen), pod = BuscarPuerto(m.Puertos, destino);
                        if (pol == null) { res.Errores.Add($"{fila}: POL \"{origen}\" no existe en el maestro de puertos."); continue; }
                        if (pod == null) { res.Errores.Add($"{fila}: POD \"{destino}\" no existe en el maestro de puertos."); continue; }
                        if (m.PaisPorPuerto.TryGetValue(PuertosHelper.ClaveTexto(pol), out var paisPol) && paisPol != ""
                            && m.PaisPorPuerto.TryGetValue(PuertosHelper.ClaveTexto(pod), out var paisPod) && paisPod == paisPol)
                        { res.Errores.Add($"{fila}: POL \"{pol}\" y POD \"{pod}\" son de {paisPol} (el destino debe ser de otro país)."); continue; }
                        string? eq = NormalizarEquipoMaritimo(equipo);
                        if (eq == null) { res.Errores.Add($"{fila}: equipo \"{equipo}\" no válido ({string.Join(", ", EquiposMaritimos)})."); continue; }
                        if (moneda != "" && moneda != "USD") { res.Errores.Add($"{fila}: el flete marítimo se carga en USD."); continue; }
                        if (monto <= 0) { res.Errores.Add($"{fila}: el flete debe ser mayor a cero."); continue; }
                        res.Maritimas.Add(new TarifasMaritima
                        {
                            IdNaviera = nav.Id, Pol = pol, Pod = pod, Equipamiento = eq, TarifaUsd = monto,
                            FechaInicioVigencia = DateOnly.FromDateTime(desde),
                            FechaFinVigencia = hasta.HasValue ? DateOnly.FromDateTime(hasta.Value) : null,
                            Comentarios = Comentario(comentarios), EsActiva = true,
                            FechaModificacion = ahora, UsuarioModificador = usuario
                        });
                        break;
                    }
                    case "TERRESTRE":
                    {
                        var prov = BuscarPorNombre(m.Proveedores, p => p.NombreProveedor, proveedorTxt);
                        if (prov == null) { res.Errores.Add($"{fila}: transportista \"{proveedorTxt}\" no existe (ver hoja Transportistas)."); continue; }
                        var cOrigen = BuscarPorNombre(m.Ciudades, c => c.Nombre, origen);
                        var cDestino = BuscarPorNombre(m.Ciudades, c => c.Nombre, destino);
                        Ciudade? cPlanta = planta == "" ? null : BuscarPorNombre(m.Ciudades, c => c.Nombre, planta);
                        if (cOrigen == null) { res.Errores.Add($"{fila}: ciudad de origen \"{origen}\" no existe (ver hoja Ciudades)."); continue; }
                        if (cDestino == null) { res.Errores.Add($"{fila}: ciudad de destino \"{destino}\" no existe (ver hoja Ciudades)."); continue; }
                        if (planta != "" && cPlanta == null) { res.Errores.Add($"{fila}: ciudad de planta \"{planta}\" no existe (ver hoja Ciudades)."); continue; }
                        string fam = VentaClienteHelper.FamiliaEquipo(equipo.ToUpperInvariant());
                        if (fam != "RF" && fam != "DRY") { res.Errores.Add($"{fila}: equipo terrestre \"{equipo}\" no válido (DRY o REEFER)."); continue; }
                        if (moneda != "" && moneda != "CLP") { res.Errores.Add($"{fila}: el flete terrestre se carga en CLP."); continue; }
                        if (monto <= 0) { res.Errores.Add($"{fila}: el flete debe ser mayor a cero."); continue; }
                        res.Terrestres.Add(new Tarifasterrestre
                        {
                            IdProveedor = prov.Id, IdCiudadOrigen = cOrigen.Id, IdCiudadPlanta = cPlanta?.Id, IdCiudadDestino = cDestino.Id,
                            IdTipoCarga = fam == "RF" ? 2 : 1, ValorNeto = monto,
                            FalsoFletePlanta = monto * 0.90m, FalsoFleteRutaMayor50 = monto * 0.80m, FalsoFleteRutaMenor50 = monto * 0.70m,
                            HorasLibresPlanta = 7, HorasLibresPuerto = 3,
                            FechaInicioVigencia = DateOnly.FromDateTime(desde),
                            FechaFinVigencia = hasta.HasValue ? DateOnly.FromDateTime(hasta.Value) : null,
                            Comentarios = Comentario(comentarios), EsActiva = true,
                            FechaCreacion = ahora, UsuarioCreador = usuario, FechaModificacion = ahora, UsuarioModificador = usuario
                        });
                        break;
                    }
                    case "GATE":
                    {
                        Naviera? nav = null;
                        if (proveedorTxt != "")
                        {
                            nav = BuscarPorNombre(m.Navieras, n => n.NombreNaviera, proveedorTxt);
                            if (nav == null) { res.Errores.Add($"{fila}: naviera \"{proveedorTxt}\" no existe (ver hoja Navieras)."); continue; }
                        }
                        Deposito? dep = null;
                        if (origen != "")
                        {
                            dep = BuscarPorNombre(m.Depositos, d => d.NombreDeposito, origen);
                            if (dep == null) { res.Errores.Add($"{fila}: depósito \"{origen}\" no existe (ver hoja Depositos)."); continue; }
                        }
                        if (nav == null && dep == null) { res.Errores.Add($"{fila}: Gate requiere depósito y/o naviera."); continue; }
                        string mov = N(Txt(row, 7));
                        var movs = mov == "AMBOS" || mov == "IN/OUT" ? new[] { "OUT", "IN" } : mov == "IN" ? new[] { "IN" } : mov == "OUT" || mov == "" ? new[] { "OUT" } : Array.Empty<string>();
                        if (movs.Length == 0) { res.Errores.Add($"{fila}: movimiento Gate \"{Txt(row, 7)}\" no válido (OUT, IN o AMBOS)."); continue; }
                        string tc = equipo.ToUpperInvariant().Replace(" ", "").Replace("'", "");
                        string? tcFinal = tc switch
                        {
                            "20DRY" or "40DRY" or "DRY" or "40RF" or "RF" => tc,
                            "40REEFER" => "40RF",
                            "REEFER" => "RF",
                            _ => null
                        };
                        if (tcFinal == null) { res.Errores.Add($"{fila}: contenedor Gate \"{equipo}\" no válido (20DRY, 40DRY, DRY, 40RF o RF)."); continue; }
                        if (moneda == "") moneda = "CLP";
                        if (moneda != "CLP" && moneda != "USD") { res.Errores.Add($"{fila}: moneda \"{moneda}\" no válida para Gate (CLP o USD)."); continue; }
                        foreach (var mv in movs)
                            res.Gates.Add(new TarifaGate
                            {
                                IdNaviera = nav?.Id, IdDeposito = dep?.Id, IdTipoCarga = tcFinal.Contains("RF") ? 2 : 1,
                                TipoContenedor = tcFinal, TipoMovimiento = mv, ValorNeto = monto, Moneda = moneda,
                                FechaInicioVigencia = desde, FechaFinVigencia = hasta, EsActiva = true,
                                FechaCreacion = ahora, UsuarioCreador = usuario
                            });
                        break;
                    }
                    case "DOCUMENTAL":
                    {
                        var aga = BuscarPorNombre(m.Agencias, a => a.NombreAgencia, proveedorTxt);
                        if (aga == null) { res.Errores.Add($"{fila}: agencia de aduanas \"{proveedorTxt}\" no existe (ver hoja Agencias)."); continue; }
                        string concepto = Txt(row, 8).ToUpper();
                        if (concepto == "") { res.Errores.Add($"{fila}: falta el trámite documental."); continue; }
                        string clasif = N(Txt(row, 9));
                        clasif = clasif switch { "" or "COMUN" or "COMUNES" => "COMUN", "SAG" => "SAG", "SERNAPESCA" => "SERNAPESCA", _ => "?" };
                        if (clasif == "?") { res.Errores.Add($"{fila}: clasificación \"{Txt(row, 9)}\" no válida (COMUN, SAG o SERNAPESCA)."); continue; }
                        if (moneda == "") moneda = "CLP";
                        if (moneda != "CLP" && moneda != "USD" && moneda != "UF") { res.Errores.Add($"{fila}: moneda \"{moneda}\" no válida (CLP, USD o UF)."); continue; }
                        string clave = aga.Id + "|" + N(concepto);
                        if (clavesDocumental.TryGetValue(clave, out int filaAnterior)) { res.Errores.Add($"{fila}: repite agencia y trámite de la fila {filaAnterior}."); continue; }
                        clavesDocumental[clave] = r;
                        string iva = N(Txt(row, 12));
                        res.Documentales.Add(new TarifasDocumentale
                        {
                            IdAgenciaAduana = aga.Id, Concepto = concepto, Clasificacion = clasif, ValorNeto = monto, Moneda = moneda,
                            AplicaIva = !(iva == "NO" || iva == "N" || iva == "FALSE" || iva == "EXENTO"),
                            FechaInicioVigencia = desde, FechaFinVigencia = hasta, Comentarios = Comentario(comentarios),
                            EsActiva = true, FechaCreacion = ahora
                        });
                        break;
                    }
                }
            }
            return res;
        }

        // "40RF", "40' REEFER", "40 HC", "20DV"… → equipo del catálogo de Comercial.
        public static string? NormalizarEquipoMaritimo(string? texto)
        {
            var t = VentaClienteHelper.Norm(texto).Replace(" ", "").Replace("'", "").Replace("´", "").Replace("’", "");
            if (t.Length < 2) return null;
            string tam = t.StartsWith("20") ? "20" : t.StartsWith("40") ? "40" : "";
            if (tam == "") return null;
            string resto = t.Substring(2);
            bool reefer = resto.Contains("RF") || resto.Contains("REEFER") || resto.Contains("RH") || resto.Contains("RQ");
            if (reefer) return tam + "' REEFER";
            bool hc = resto.Contains("HC") || resto.Contains("HQ") || resto.Contains("HIGHCUBE");
            if (hc) return tam == "40" ? "40' DRY HC" : null;
            bool dry = resto == "" || resto.Contains("DRY") || resto.Contains("STD") || resto.Contains("DV") || resto.Contains("GP") || resto.Contains("ST");
            return dry ? tam + "' DRY STD" : null;
        }

        private static string? Comentario(string texto) => string.IsNullOrWhiteSpace(texto) ? "CARGA MASIVA" : texto.Trim().ToUpper();

        private static T? BuscarPorNombre<T>(IEnumerable<T> lista, Func<T, string?> nombre, string texto) where T : class
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            string buscado = VentaClienteHelper.Norm(texto);
            return lista.FirstOrDefault(x => VentaClienteHelper.Norm(nombre(x)) == buscado);
        }

        private static string? BuscarPuerto(IEnumerable<string> puertos, string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            string buscado = VentaClienteHelper.Norm(texto);
            var p = puertos.FirstOrDefault(x => VentaClienteHelper.Norm(x) == buscado);
            return p?.Trim().ToUpper();
        }

        private static bool LeerMonto(IXLCell celda, out decimal valor)
        {
            valor = 0m;
            if (celda.DataType == XLDataType.Number) { valor = (decimal)celda.GetDouble(); return true; }
            return MontoHelper.TryParse(celda.GetFormattedString(), out valor);
        }

        private static DateTime? LeerFecha(IXLCell celda)
        {
            if (celda.IsEmpty()) return null;
            if (celda.DataType == XLDataType.DateTime) return celda.GetDateTime().Date;
            if (celda.DataType == XLDataType.Number) return DateTime.FromOADate(celda.GetDouble()).Date;
            var t = celda.GetFormattedString().Trim();
            if (t == "") return null;
            string[] formatos = { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy" };
            return DateTime.TryParseExact(t, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
        }
    }
}
