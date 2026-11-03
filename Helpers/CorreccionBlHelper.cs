using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    // Corrección de BL (oct-2026): un B/L puede corregirse varias veces y cada corrección
    // tiene su propio costo de la naviera. Cada una se guarda como un extracosto Marítimo
    // ("Corrección de BL N°1", "N°2"...) con su fecha, hora, usuario, motivos, evidencia y
    // monto. Así queda el registro de cada corrección y FinanzasSyncService la refleja sola
    // en Control Financiero ("Corrección de BL N°1 (EXT-id)", sección Marítimo, + IVA).
    //
    // No usa columnas nuevas: todo vive en ExtracostosOperacion, que ya existe.
    public static class CorreccionBlHelper
    {
        public const string PrefijoTipoCosto = "Corrección de BL N°";
        public const string GrupoOperativo = "Marítimo";
        public const string EvidenciaSinArchivo = "Generado por sistema interno";

        // Largo de ExtracostosOperacion.Motivo en la base (HasMaxLength(500)).
        private const int LargoMaximoMotivo = 500;
        private const string Separador = " | ";

        public static string TipoCosto(int numero) => $"{PrefijoTipoCosto}{numero}";

        public static bool EsCorreccion(ExtracostosOperacion? e) =>
            e?.TipoCosto != null && e.TipoCosto.StartsWith(PrefijoTipoCosto, StringComparison.OrdinalIgnoreCase);

        public static int? Numero(ExtracostosOperacion e)
        {
            if (!EsCorreccion(e)) return null;
            var resto = e.TipoCosto.Substring(PrefijoTipoCosto.Length).Trim();
            var digitos = new string(resto.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digitos, out var n) ? n : null;
        }

        // Correcciones del booking en el orden en que se registraron.
        public static List<ExtracostosOperacion> Listar(Operacione op) =>
            (op.ExtracostosOperacions ?? new List<ExtracostosOperacion>())
                .Where(EsCorreccion)
                .OrderBy(e => Numero(e) ?? int.MaxValue)
                .ThenBy(e => e.FechaCreacion)
                .ThenBy(e => e.Id)
                .ToList();

        public static int SiguienteNumero(Operacione op)
        {
            var lista = Listar(op);
            int maxNumero = lista.Select(e => Numero(e) ?? 0).DefaultIfEmpty(0).Max();
            return Math.Max(maxNumero, lista.Count) + 1;
        }

        private static readonly string[] Ordinales =
        {
            "Primera", "Segunda", "Tercera", "Cuarta", "Quinta",
            "Sexta", "Séptima", "Octava", "Novena", "Décima"
        };

        // "Primera corrección", "Segunda corrección"... y desde la 11 "Corrección N° 11".
        public static string Ordinal(int numero) =>
            numero >= 1 && numero <= Ordinales.Length
                ? $"{Ordinales[numero - 1]} corrección"
                : $"Corrección N° {numero}";

        // ===== Texto guardado en ExtracostosOperacion.Motivo =====
        // "Motivos: A, B | Cambio: X → Y | Obs: ... | Costo: ..." — legible tal cual en la
        // tabla de Extracostos y fácil de separar para mostrarlo ordenado en Marítimo.
        public record Detalle(List<string> Motivos, string? Cambio, string? Observacion, string? Costo);

        public static string ArmarMotivo(IEnumerable<string> motivos, string? cambio, string? observacion, string? costo)
        {
            var partes = new List<string> { $"Motivos: {string.Join(", ", motivos.Select(Limpiar))}" };
            if (!string.IsNullOrWhiteSpace(cambio)) partes.Add($"Cambio: {Limpiar(cambio)}");
            if (!string.IsNullOrWhiteSpace(costo)) partes.Add($"Costo: {Limpiar(costo)}");

            string sinObs = string.Join(Separador, partes);
            if (!string.IsNullOrWhiteSpace(observacion))
            {
                // La observación es lo único que se recorta si no cabe.
                int espacio = LargoMaximoMotivo - sinObs.Length - Separador.Length - "Obs: ".Length;
                string obs = Limpiar(observacion);
                if (espacio > 10)
                {
                    if (obs.Length > espacio) obs = obs.Substring(0, espacio - 1).TrimEnd() + "…";
                    partes.Insert(partes.Count - (string.IsNullOrWhiteSpace(costo) ? 0 : 1), $"Obs: {obs}");
                }
            }
            string texto = string.Join(Separador, partes);
            return texto.Length <= LargoMaximoMotivo ? texto : texto.Substring(0, LargoMaximoMotivo);
        }

        public static Detalle Leer(string? motivo)
        {
            var motivos = new List<string>();
            string? cambio = null, obs = null, costo = null;
            foreach (var parte in (motivo ?? "").Split(Separador, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = parte.Trim();
                if (p.StartsWith("Motivos:")) motivos = p.Substring(8).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x != "").ToList();
                else if (p.StartsWith("Cambio:")) cambio = p.Substring(7).Trim();
                else if (p.StartsWith("Obs:")) obs = p.Substring(4).Trim();
                else if (p.StartsWith("Costo:")) costo = p.Substring(6).Trim();
                else obs = obs == null ? p : $"{obs} {p}";
            }
            return new Detalle(motivos, cambio, obs, costo);
        }

        private static string Limpiar(string texto) =>
            texto.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();

        // ===== Costo sugerido desde el catálogo de recargos de la naviera =====
        // TarifasMaestras (Categoría Marítimo, misma naviera, activa y vigente) con un
        // concepto de corrección de B/L. Se excluyen Manifiesto, Matriz y la tarifa especial
        // de carga argentina. Si la naviera tiene dos tramos (antes/después de 48h del
        // zarpe), se elige según las horas desde el ETD (EtdReal ?? EtdPol) al momento de
        // registrar la corrección. Si no hay un match claro, no se inventa: queda en 0 y
        // Finanzas lo valoriza (igual que Late Arrival).
        public static bool EsConceptoCorreccionBl(string? concepto)
        {
            string c = (concepto ?? "").ToUpperInvariant();
            if (!c.Contains("CORRECCI")) return false;
            bool esBl = c.Contains("B/L") || c.Contains(" BL") || c.StartsWith("BL") || c.Contains("BL ");
            if (!esBl) return false;
            return !c.Contains("MANIFIESTO") && !c.Contains("MATRIZ") && !c.Contains("ARGENTIN");
        }

        private static bool EsTramoTemprano(string concepto)
        {
            string c = concepto.ToUpperInvariant();
            return c.Contains("ANTES") || c.Contains("HASTA");
        }

        private static bool EsTramoTardio(string concepto)
        {
            string c = concepto.ToUpperInvariant();
            return c.Contains("POSTERIOR") || c.Contains("DESPUÉS") || c.Contains("DESPUES") || c.Contains("DESDE") || c.Contains("POST");
        }

        public static TarifasMaestra? ElegirTarifa(IReadOnlyList<TarifasMaestra> candidatos, double? horasDesdeEtd)
        {
            if (candidatos.Count == 0) return null;

            bool TieneVentana(TarifasMaestra t) => t.HorasDesdeEvento.HasValue || t.HorasHastaEvento.HasValue;

            if (horasDesdeEtd.HasValue)
            {
                double h = horasDesdeEtd.Value;
                var conVentana = candidatos.Where(TieneVentana).ToList();

                // 1) Ventana de horas registrada en el catálogo (ej. Hapag 0–48h / 48h+).
                var porVentana = conVentana.FirstOrDefault(t =>
                    (!t.HorasDesdeEvento.HasValue || h >= t.HorasDesdeEvento.Value) &&
                    (!t.HorasHastaEvento.HasValue || h < t.HorasHastaEvento.Value));
                if (porVentana != null) return porVentana;

                // Antes del primer tramo: solo vale si ese tramo es "hasta/antes de".
                if (conVentana.Any())
                {
                    var primero = conVentana.OrderBy(t => t.HorasDesdeEvento ?? int.MinValue).First();
                    if (primero.HorasDesdeEvento.HasValue && h < primero.HorasDesdeEvento.Value && EsTramoTemprano(primero.Concepto))
                        return primero;
                }

                // 2) Sin ventana de horas: por el texto del concepto (ej. MSC "antes de
                //    48h" / "posterior a 48h"), con el mismo umbral de 48h desde el ETD.
                var sinVentana = candidatos.Where(t => !TieneVentana(t)).ToList();
                if (sinVentana.Count == 1) return sinVentana[0];
                if (sinVentana.Count > 1)
                {
                    bool tarde = h >= 48;
                    return sinVentana.FirstOrDefault(t => tarde ? EsTramoTardio(t.Concepto) : EsTramoTemprano(t.Concepto))
                        ?? sinVentana.OrderBy(t => t.ValorNeto).First();
                }
                return null;
            }

            // Sin ETD: si hay un solo concepto se usa; con varios, el tramo temprano.
            if (candidatos.Count == 1) return candidatos[0];
            return candidatos.FirstOrDefault(t => EsTramoTemprano(t.Concepto))
                ?? candidatos.OrderBy(t => t.HorasDesdeEvento ?? int.MinValue).ThenBy(t => t.ValorNeto).First();
        }

        public static async Task<RecargoNavieraHelper.SugerenciaRecargo?> SugerirCostoAsync(AtilsonContext context, Operacione op, DateTime momento)
        {
            var hoy = DateOnly.FromDateTime(momento);
            var catalogo = await context.TarifasMaestras.AsNoTracking()
                .Where(t => t.Categoria == "Marítimo" && t.EsActiva && t.IdNaviera == op.IdNaviera)
                .ToListAsync();
            var candidatos = catalogo
                .Where(t => EsConceptoCorreccionBl(t.Concepto))
                .Where(t => (!t.FechaVigenciaDesde.HasValue || t.FechaVigenciaDesde.Value <= hoy)
                         && (!t.FechaVigenciaHasta.HasValue || t.FechaVigenciaHasta.Value >= hoy))
                .OrderBy(t => t.Id)
                .ToList();

            DateTime? etd = op.EtdReal ?? op.EtdPol;
            double? horas = etd.HasValue ? (momento - etd.Value).TotalHours : null;
            var match = ElegirTarifa(candidatos, horas);
            if (match == null) return null;

            string moneda = string.IsNullOrWhiteSpace(match.Moneda) ? "USD" : match.Moneda.Trim().ToUpperInvariant();
            string monto = moneda == "CLP"
                ? match.ValorNeto.ToString("N0", CultureInfo.GetCultureInfo("es-CL"))
                : match.ValorNeto.ToString("N2", CultureInfo.GetCultureInfo("es-CL"));
            return new RecargoNavieraHelper.SugerenciaRecargo(match.ValorNeto, moneda, $"Catálogo naviera: {match.Concepto} ({moneda} {monto})");
        }

        public static string FormatearMonto(decimal monto, string? moneda)
        {
            string m = string.IsNullOrWhiteSpace(moneda) ? "USD" : moneda.Trim().ToUpperInvariant();
            var cl = CultureInfo.GetCultureInfo("es-CL");
            return m == "CLP" ? $"$ {monto.ToString("N0", cl)} CLP" : $"{m} {monto.ToString("N2", cl)}";
        }
    }
}
