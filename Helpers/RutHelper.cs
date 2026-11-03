using System.Linq;

namespace AtilsonCargoSpa.Helpers
{
    /// <summary>RUT chileno: limpieza, validación del dígito verificador y formato 76.123.456-7.</summary>
    public static class RutHelper
    {
        /// <summary>Solo números y K, en mayúsculas (para comparar).</summary>
        public static string Limpiar(string? rut) => new string((rut ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        public static bool EsValido(string? rut)
        {
            var limpio = Limpiar(rut);
            if (limpio.Length < 8 || limpio.Length > 9) return false;
            var cuerpo = limpio[..^1];
            var dv = limpio[^1];
            if (!cuerpo.All(char.IsDigit) || !(char.IsDigit(dv) || dv == 'K')) return false;
            int suma = 0, factor = 2;
            for (int i = cuerpo.Length - 1; i >= 0; i--)
            {
                suma += (cuerpo[i] - '0') * factor;
                factor = factor == 7 ? 2 : factor + 1;
            }
            int resto = 11 - (suma % 11);
            char esperado = resto == 11 ? '0' : resto == 10 ? 'K' : (char)('0' + resto);
            return esperado == dv;
        }

        /// <summary>76123456-7 → 76.123.456-7 (si no es un RUT válido, lo devuelve recortado).</summary>
        public static string Formatear(string? rut)
        {
            var limpio = Limpiar(rut);
            if (limpio.Length < 2) return (rut ?? "").Trim();
            var cuerpo = limpio[..^1];
            var dv = limpio[^1];
            if (!cuerpo.All(char.IsDigit)) return (rut ?? "").Trim();
            var conPuntos = long.Parse(cuerpo).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');
            return $"{conPuntos}-{dv}";
        }
    }
}
