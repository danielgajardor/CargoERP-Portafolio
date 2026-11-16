using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    /// <summary>
    /// Conceptos extraordinarios (nov-2026, reunión con Cristian, Dani y J): costos o cobros que
    /// no salen de las tarifas de Comercial (un gasto puntual de una agencia, un recargo no
    /// previsto, una operación histórica cargada a mano). Se agregan desde Finanzas › Control
    /// Financiero ("Agregar concepto") o desde Datos Históricos ("Creación de operación") y
    /// quedan como líneas manuales: la sincronización de Finanzas nunca las toca, las
    /// recalcula ni las borra (FinanzasSyncService no las carga).
    /// Se reconocen por TarifaManual + JustificacionManual con el prefijo de abajo, así no se
    /// necesita una columna nueva.
    /// </summary>
    public static class ConceptoExtraordinarioHelper
    {
        public const string Prefijo = "Concepto extraordinario";

        /// <summary>Secciones que se pueden elegir y su GrupoCobro en TransaccionesFinancieras.</summary>
        public static readonly (string Texto, string Grupo)[] Secciones =
        {
            ("Marítimo", "Marítimo"),
            ("Terrestre", "Transporte"),
            ("Documental", "Documental"),
            ("Almacenaje", "Almacenamiento"),
            ("Otros", "Otros"),
        };

        public static string? GrupoDeSeccion(string? seccion)
        {
            string n = Norm(seccion);
            foreach (var (texto, grupo) in Secciones)
                if (Norm(texto) == n || Norm(grupo) == n) return grupo;
            return null;
        }

        public static bool Es(TransaccionesFinanciera t) =>
            t.TarifaManual && (t.JustificacionManual ?? "").StartsWith(Prefijo, StringComparison.Ordinal);

        public static string Justificacion(string origen, string? usuario) =>
            $"{Prefijo} · {origen} · {usuario ?? "Finanzas"} · {DateTime.Now:dd/MM/yyyy HH:mm}";

        /// <summary>Varios números de documento (separados por coma, punto y coma o salto de línea) en un solo texto.</summary>
        public static string? UnirDocumentos(string? texto, int max = 100)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            var docs = texto.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim().ToUpperInvariant()).Where(d => d.Length > 0).Distinct().ToList();
            if (docs.Count == 0) return null;
            string unido = string.Join(" / ", docs);
            return unido.Length <= max ? unido : unido[..max];
        }

        public static string Norm(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            string d = v.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char ch in d)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        /// <summary>
        /// Factura del proveedor de origen (ej. TPS 12345) ya registrada como respaldo en otro
        /// egreso. Sirve para detectar que el terminal cobró dos veces el mismo contenedor o que
        /// la agencia cobró dos veces la misma factura. Devuelve una línea legible por cada caso.
        /// </summary>
        public static async Task<List<string>> RespaldosRepetidosAsync(AtilsonContext db, string numeroOrigen, string? proveedorOrigen, IEnumerable<int> excluirIds)
        {
            string num = Norm(numeroOrigen);
            if (num.Length == 0) return new List<string>();
            var excluir = excluirIds.ToHashSet();
            string numTrim = numeroOrigen.Trim().ToUpperInvariant();
            var candidatos = await db.TransaccionesFinancieras.AsNoTracking()
                .Where(t => t.TipoMovimiento == "EGRESO" && t.NumeroDocumentoOrigen != null && t.NumeroDocumentoOrigen.Contains(numTrim))
                .Select(t => new
                {
                    t.Id, t.Concepto, t.NumeroDocumento, t.NumeroDocumentoOrigen, t.ProveedorOrigen, t.ContraparteHistorica,
                    Proveedor = t.IdProveedorNavigation != null ? t.IdProveedorNavigation.NombreProveedor : null,
                    Booking = t.IdOperacionNavigation.NumeroBooking
                })
                .Take(200)
                .ToListAsync();
            string prov = Norm(proveedorOrigen);
            return candidatos
                .Where(c => !excluir.Contains(c.Id) && Norm(c.NumeroDocumentoOrigen) == num
                            && (prov.Length == 0 || Norm(c.ProveedorOrigen).Length == 0 || Norm(c.ProveedorOrigen) == prov))
                .Select(c => $"Booking {c.Booking} · {System.Text.RegularExpressions.Regex.Replace(c.Concepto ?? "", @"\s*\(EXT-\d+\)", "")}"
                             + $" · doc. directo {(string.IsNullOrWhiteSpace(c.NumeroDocumento) ? "sin N°" : c.NumeroDocumento)}"
                             + (string.IsNullOrWhiteSpace(c.Proveedor ?? c.ContraparteHistorica) ? "" : $" ({c.Proveedor ?? c.ContraparteHistorica})"))
                .Distinct()
                .ToList();
        }
    }
}
