using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AtilsonCargoSpa.Helpers
{
    /// <summary>
    /// Lectura de montos escritos por usuarios en Chile (nov-2026). Una sola regla para todos
    /// los formularios de tarifas, la edición en fila y la carga masiva:
    ///   "450.000" y "1.200.000"  → miles con punto (450000 / 1200000)
    ///   "1.200,75" y "2850,5"     → coma decimal
    ///   "2850.50" y "75.5"        → punto decimal (lo que envía un input numérico)
    ///   "1,200.50"                → formato inglés (1200.50)
    /// Antes "380.000" en un costo terrestre quedaba guardado como $380.
    /// </summary>
    public static class MontoHelper
    {
        // Uno a tres dígitos (sin cero inicial) y grupos de tres separados por punto.
        private static readonly Regex MilesConPunto = new(@"^-?[1-9]\d{0,2}(\.\d{3})+$", RegexOptions.Compiled);

        public static bool TryParse(string? texto, out decimal valor)
        {
            valor = 0m;
            var t = (texto ?? "").Trim()
                .Replace(" ", "").Replace(" ", "").Replace("$", "");
            if (t.Length == 0) return false;

            if (t.Contains(',') && t.Contains('.'))
            {
                // El separador que aparece al final es el decimal.
                t = t.LastIndexOf(',') > t.LastIndexOf('.')
                    ? t.Replace(".", "").Replace(",", ".")
                    : t.Replace(",", "");
            }
            else if (t.Contains(','))
            {
                t = t.Count(c => c == ',') > 1 ? t.Replace(",", "") : t.Replace(",", ".");
            }
            else if (MilesConPunto.IsMatch(t))
            {
                t = t.Replace(".", "");
            }

            return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out valor);
        }

        public static decimal? ParseONulo(string? texto) => TryParse(texto, out var v) ? v : null;

        /// <summary>
        /// Monto entero en pesos. Reemplaza a la lectura "solo dígitos" de Operaciones, que
        /// convertía "25000,00" o "25000.00" (cómo se muestra un decimal guardado) en 2.500.000.
        /// </summary>
        public static int? ParseEntero(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            if (TryParse(texto, out var v) && v >= int.MinValue && v <= int.MaxValue)
                return (int)Math.Round(v, MidpointRounding.AwayFromZero);
            var limpio = new string(texto.Where(c => char.IsDigit(c) || c == '-').ToArray());
            return int.TryParse(limpio, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var r) ? r : null;
        }
    }
}
