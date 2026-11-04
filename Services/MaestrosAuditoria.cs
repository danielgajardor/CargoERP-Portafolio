using System.Globalization;
using System.Security.Claims;
using System.Text;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Services
{
    /// <summary>
    /// Auditoría de los maestros (oct-2026): quién creó/modificó cada registro, cuándo, y
    /// el detalle campo por campo de cada cambio.
    ///
    /// El detalle se guarda en la tabla existente HistorialCambiosComercial (mismo formato:
    /// entidad, id, campo, valor anterior, valor nuevo, usuario, fecha), con TipoEntidad
    /// "Maestro:&lt;Entidad&gt;" para no mezclarse con el historial de tarifas. Así no hace
    /// falta crear tablas nuevas.
    /// </summary>
    public static class MaestrosAuditoria
    {
        public const string Prefijo = "Maestro:";

        public const string EntDeposito = "Deposito";
        public const string EntPlanta = "Planta";
        public const string EntNaviera = "Naviera";
        public const string EntPuerto = "Puerto";
        public const string EntCliente = "Cliente";
        public const string EntCiudad = "Ciudad";
        public const string EntProveedor = "Proveedor";
        public const string EntAgencia = "AgenciaAduana";

        public static string Usuario(ClaimsPrincipal? user)
        {
            var nombre = user?.Identity?.Name;
            return string.IsNullOrWhiteSpace(nombre) ? "Sistema" : nombre.Trim();
        }

        /// <summary>Hora local de Chile, igual que el resto del ERP (DateTime.Now del servidor).</summary>
        public static DateTime Ahora() => DateTime.Now;

        public static string FormatoFecha(DateTime? f) => f.HasValue && f.Value.Year > 1900 ? f.Value.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture) : "—";

        /// <summary>Mayúsculas, sin tildes y con espacios simples: para comparar nombres duplicados.</summary>
        public static string Normalizar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var desc = texto.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(desc.Length);
            bool espacio = false;
            foreach (var ch in desc)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsWhiteSpace(ch)) { if (!espacio && sb.Length > 0) sb.Append(' '); espacio = true; continue; }
                espacio = false;
                sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        /// <summary>Color #RRGGBB válido o null (evita estilos inyectados en la vista).</summary>
        public static string? ColorValido(string? color)
        {
            var c = color?.Trim();
            return c != null && System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$") ? c.ToLowerInvariant() : null;
        }

        /// <summary>Texto limpio: recorta y convierte "" en null.</summary>
        public static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        /// <summary>
        /// Deja en ModelState solo los errores de los campos que el formulario realmente envía.
        /// Las entidades tienen campos de auditoría y navegaciones obligatorias que no vienen
        /// en el formulario; antes eso invalidaba el guardado sin mostrar ningún mensaje.
        /// </summary>
        public static void SoloValidarCampos(ModelStateDictionary modelState, string prefijo, params string[] campos)
        {
            var permitidos = new HashSet<string>(campos.Select(c => $"{prefijo}.{c}"), StringComparer.OrdinalIgnoreCase);
            foreach (var key in modelState.Keys.ToList())
            {
                if (key.StartsWith(prefijo + ".", StringComparison.OrdinalIgnoreCase) && !permitidos.Contains(key))
                    modelState.Remove(key);
            }
        }

        public sealed class Cambio
        {
            public string Campo { get; init; } = "";
            public string? Antes { get; init; }
            public string? Despues { get; init; }
        }

        /// <summary>Acumula las diferencias campo a campo de una edición.</summary>
        public sealed class Cambios
        {
            private readonly List<Cambio> _lista = new();
            public IReadOnlyList<Cambio> Lista => _lista;
            public bool Hay => _lista.Count > 0;

            public void Comparar(string campo, string? antes, string? despues)
            {
                var a = string.IsNullOrWhiteSpace(antes) ? null : antes.Trim();
                var d = string.IsNullOrWhiteSpace(despues) ? null : despues.Trim();
                if (!string.Equals(a, d, StringComparison.Ordinal))
                    _lista.Add(new Cambio { Campo = campo, Antes = a, Despues = d });
            }

            public void Comparar(string campo, decimal? antes, decimal? despues)
            {
                if (antes != despues)
                    _lista.Add(new Cambio { Campo = campo, Antes = antes?.ToString("0.##", CultureInfo.InvariantCulture), Despues = despues?.ToString("0.##", CultureInfo.InvariantCulture) });
            }

            public void Comparar(string campo, bool antes, bool despues)
            {
                if (antes != despues)
                    _lista.Add(new Cambio { Campo = campo, Antes = antes ? "Sí" : "No", Despues = despues ? "Sí" : "No" });
            }

            public void Agregar(string campo, string? antes, string? despues) => _lista.Add(new Cambio { Campo = campo, Antes = antes, Despues = despues });
        }

        public static void Registrar(AtilsonContext ctx, string entidad, int idRegistro, Cambios cambios, string usuario, string comentario, DateTime? fecha = null)
        {
            var cuando = fecha ?? Ahora();
            foreach (var c in cambios.Lista)
            {
                ctx.HistorialCambiosComercial.Add(new HistorialCambiosComercial
                {
                    TipoEntidad = Prefijo + entidad,
                    IdRegistro = idRegistro,
                    Campo = Recortar(c.Campo, 100)!,
                    ValorAnterior = Recortar(c.Antes, 200),
                    ValorNuevo = Recortar(c.Despues, 200),
                    Comentario = Recortar(comentario, 200)!,
                    Usuario = Recortar(usuario, 100)!,
                    FechaCambio = cuando
                });
            }
        }

        public static void RegistrarEvento(AtilsonContext ctx, string entidad, int idRegistro, string evento, string usuario, string? detalle = null)
        {
            ctx.HistorialCambiosComercial.Add(new HistorialCambiosComercial
            {
                TipoEntidad = Prefijo + entidad,
                IdRegistro = idRegistro,
                Campo = Recortar(evento, 100)!,
                ValorAnterior = null,
                ValorNuevo = Recortar(detalle, 200),
                Comentario = Recortar(evento, 200)!,
                Usuario = Recortar(usuario, 100)!,
                FechaCambio = Ahora()
            });
        }

        private static string? Recortar(string? s, int max) => s == null ? null : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

        public static async Task<List<HistorialCambiosComercial>> HistorialAsync(AtilsonContext ctx, string entidad, int idRegistro, int max = 40)
        {
            var tipo = Prefijo + entidad;
            return await ctx.HistorialCambiosComercial.AsNoTracking()
                .Where(h => h.TipoEntidad == tipo && h.IdRegistro == idRegistro)
                .OrderByDescending(h => h.FechaCambio).ThenByDescending(h => h.Id)
                .Take(max)
                .ToListAsync();
        }

        /// <summary>Último cambio por registro, para maestros cuya tabla no tiene columnas de auditoría.</summary>
        public static async Task<Dictionary<int, (string Usuario, DateTime Fecha)>> UltimosCambiosAsync(AtilsonContext ctx, string entidad)
        {
            var tipo = Prefijo + entidad;
            var filas = await ctx.HistorialCambiosComercial.AsNoTracking()
                .Where(h => h.TipoEntidad == tipo)
                .Select(h => new { h.IdRegistro, h.Usuario, h.FechaCambio })
                .ToListAsync();
            return filas.GroupBy(f => f.IdRegistro)
                .ToDictionary(g => g.Key, g => { var u = g.OrderByDescending(x => x.FechaCambio).First(); return (u.Usuario, u.FechaCambio); });
        }
    }

    /// <summary>Datos del panel "Registro de cambios" de las fichas de maestros.</summary>
    public sealed class MaestroAuditoriaVm
    {
        public string? CreadoPor { get; set; }
        public DateTime? CreadoEl { get; set; }
        public string? ModificadoPor { get; set; }
        public DateTime? ModificadoEl { get; set; }
        public List<HistorialCambiosComercial> Historial { get; set; } = new();
    }
}
