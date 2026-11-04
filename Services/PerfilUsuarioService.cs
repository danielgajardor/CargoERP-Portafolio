using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    /// <summary>Lo mínimo del perfil que muestran los menús laterales (foto).</summary>
    public record ResumenPerfil(int IdUsuario, string Nombre, string? FotoUrl, string? Cargo);

    // ============================================================================
    // Perfil de usuario (oct-2026) — clientes e internos
    // ----------------------------------------------------------------------------
    // Datos de contacto, foto, cambio de contraseña y último
    // acceso. Todo vive en dbo.PerfilesUsuario (Sql/2026-10_perfiles_soporte.sql);
    // el nombre y la contraseña siguen en dbo.Usuarios. Si el script aún no se corrió,
    // las lecturas devuelven null y las pantallas lo avisan en vez de fallar.
    // ============================================================================
    public class PerfilUsuarioService
    {
        public const long MaxBytesFoto = 2 * 1024 * 1024;
        private readonly AtilsonContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly RegistroClientesService _correos;
        private readonly ILogger<PerfilUsuarioService> _logger;

        public PerfilUsuarioService(AtilsonContext context, IWebHostEnvironment env, RegistroClientesService correos, ILogger<PerfilUsuarioService> logger)
        {
            _context = context;
            _env = env;
            _correos = correos;
            _logger = logger;
        }

        public static string? CorreoDe(ClaimsPrincipal user) => user.FindFirst(ClaimTypes.Email)?.Value?.Trim();

        public async Task<Usuario?> UsuarioActualAsync(ClaimsPrincipal user)
        {
            string? correo = CorreoDe(user);
            if (string.IsNullOrEmpty(correo)) return null;
            string c = correo.ToLower();
            return await _context.Usuarios.FirstOrDefaultAsync(u => u.Correo.ToLower() == c);
        }

        /// <summary>Perfil del usuario o null si la tabla aún no existe (script pendiente).</summary>
        public async Task<PerfilUsuario?> ObtenerAsync(int idUsuario, bool crearSiFalta = false)
        {
            try
            {
                var p = await _context.PerfilesUsuario.FirstOrDefaultAsync(x => x.IdUsuario == idUsuario);
                if (p == null && crearSiFalta)
                {
                    p = new PerfilUsuario { IdUsuario = idUsuario, FechaModificacion = DateTime.Now };
                    _context.PerfilesUsuario.Add(p);
                    await _context.SaveChangesAsync();
                }
                return p;
            }
            catch (Exception ex) when (EsTablaFaltante(ex))
            {
                return null;
            }
        }

        public async Task<bool> TablaDisponibleAsync()
        {
            try { await _context.PerfilesUsuario.AsNoTracking().AnyAsync(); return true; }
            catch (Exception ex) when (EsTablaFaltante(ex)) { return false; }
        }

        /// <summary>Foto y cargo para el menú lateral. Nunca lanza.</summary>
        public async Task<ResumenPerfil?> ResumenAsync(ClaimsPrincipal user)
        {
            try
            {
                string? correo = CorreoDe(user);
                if (string.IsNullOrEmpty(correo)) return null;
                string c = correo.ToLower();
                var u = await _context.Usuarios.AsNoTracking().Where(x => x.Correo.ToLower() == c).Select(x => new { x.Id, x.NombreCompleto }).FirstOrDefaultAsync();
                if (u == null) return null;
                var p = await _context.PerfilesUsuario.AsNoTracking().FirstOrDefaultAsync(x => x.IdUsuario == u.Id);
                return new ResumenPerfil(u.Id, u.NombreCompleto, p?.FotoUrl, p?.Cargo);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No se pudo leer el perfil para el menú");
                return null;
            }
        }

        public async Task RegistrarAccesoAsync(int idUsuario)
        {
            try
            {
                var p = await ObtenerAsync(idUsuario, crearSiFalta: true);
                if (p == null) return;
                p.UltimoAcceso = DateTime.Now;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No se pudo registrar el último acceso de {Id}", idUsuario);
            }
        }

        public async Task<(bool Ok, string Mensaje)> GuardarDatosAsync(Usuario u, string? nombre, string? telefono, string? cargo)
        {
            nombre = Regex.Replace((nombre ?? "").Trim(), @"\s+", " ");
            if (nombre.Length < 3) return (false, "Escribe tu nombre y apellido.");
            if (nombre.Length > 100) return (false, "El nombre no puede tener más de 100 caracteres.");
            telefono = (telefono ?? "").Trim();
            if (telefono.Length > 0 && !Regex.IsMatch(telefono, @"^\+?[\d\s\-()]{7,20}$")) return (false, "Revisa el teléfono: solo números, espacios y +, por ejemplo +56 9 1234 5678.");
            cargo = (cargo ?? "").Trim();
            if (cargo.Length > 100) cargo = cargo.Substring(0, 100);

            var p = await ObtenerAsync(u.Id, crearSiFalta: true);
            if (p == null) return (false, ScriptPendiente);
            u.NombreCompleto = nombre;
            p.Telefono = telefono.Length > 0 ? telefono : null;
            p.Cargo = cargo.Length > 0 ? cargo : null;
            p.FechaModificacion = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Guardamos tus datos.");
        }

        public async Task<(bool Ok, string Mensaje)> GuardarFotoAsync(Usuario u, IFormFile? archivo)
        {
            if (archivo == null || archivo.Length == 0) return (false, "Elige una imagen.");
            if (archivo.Length > MaxBytesFoto) return (false, "La imagen pesa más de 2 MB. Usa una más liviana.");
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await archivo.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
            string? ext = ExtensionReal(bytes);
            if (ext == null) return (false, "La foto debe ser JPG, PNG o WEBP.");

            var p = await ObtenerAsync(u.Id, crearSiFalta: true);
            if (p == null) return (false, ScriptPendiente);

            string carpeta = Path.Combine(_env.WebRootPath, "uploads", "perfiles");
            Directory.CreateDirectory(carpeta);
            string nombre = $"{u.Id}-{Guid.NewGuid():N}{ext}";
            await File.WriteAllBytesAsync(Path.Combine(carpeta, nombre), bytes);

            BorrarArchivo(p.FotoUrl);
            p.FotoUrl = "/uploads/perfiles/" + nombre;
            p.FechaModificacion = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Actualizamos tu foto.");
        }

        public async Task<(bool Ok, string Mensaje)> QuitarFotoAsync(Usuario u)
        {
            var p = await ObtenerAsync(u.Id);
            if (p == null || p.FotoUrl == null) return (true, "No tenías foto.");
            BorrarArchivo(p.FotoUrl);
            p.FotoUrl = null;
            p.FechaModificacion = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Quitamos tu foto.");
        }

        public async Task<(bool Ok, string Mensaje, string? Campo)> CambiarPasswordAsync(Usuario u, string? actual, string? nueva, string? confirmar)
        {
            if (string.IsNullOrEmpty(actual)) return (false, "Escribe tu contraseña actual.", "PasswordActual");
            if (!PasswordCorrecta(u, actual)) return (false, "La contraseña actual no es correcta.", "PasswordActual");
            nueva ??= "";
            if (nueva.Length < 8 || !nueva.Any(char.IsLetter) || !nueva.Any(char.IsDigit))
                return (false, "La nueva contraseña debe tener al menos 8 caracteres, con letras y números.", "PasswordNueva");
            if (nueva == actual) return (false, "La nueva contraseña debe ser distinta de la actual.", "PasswordNueva");
            if (nueva != confirmar) return (false, "La confirmación no coincide con la nueva contraseña.", "PasswordConfirmar");

            u.Password = BCrypt.Net.BCrypt.HashPassword(nueva);
            var p = await ObtenerAsync(u.Id, crearSiFalta: true);
            if (p != null) p.FechaCambioPassword = DateTime.Now;
            await _context.SaveChangesAsync();

            // Aviso de seguridad: si no fue la persona, sabrá que debe pedir ayuda.
            _correos.EncolarCorreo(u.Correo, "Cambiaste tu contraseña de Atilson Cargo", RegistroClientesService.Plantilla("Contraseña actualizada",
                $"<p>Hola {WebUtility.HtmlEncode(u.NombreCompleto)},</p><p>La contraseña de tu cuenta se cambió el {DateTime.Now:dd-MM-yyyy} a las {DateTime.Now:HH:mm}.</p>" +
                "<p>Si no fuiste tú, avísanos de inmediato respondiendo este correo.</p>",
                encabezado: u.Rol == "Cliente" ? "ATILSON CARGO · PORTAL DE CLIENTES" : "ATILSON CARGO · EQUIPO"));
            return (true, "Cambiamos tu contraseña. La próxima vez entra con la nueva.", null);
        }

        private static bool PasswordCorrecta(Usuario u, string clave)
        {
            if (string.IsNullOrEmpty(u.Password)) return false;
            if (u.Password.StartsWith("$2"))
            {
                try { return BCrypt.Net.BCrypt.Verify(clave, u.Password); } catch { return false; }
            }
            return u.Password == clave; // usuario aún sin migrar a BCrypt (ver Login)
        }

        /// <summary>Extensión según los primeros bytes (no según el nombre del archivo).</summary>
        public static string? ExtensionReal(byte[] b)
        {
            if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
            if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
            if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
            return null;
        }

        private void BorrarArchivo(string? url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("/uploads/perfiles/")) return;
            try
            {
                string ruta = Path.Combine(_env.WebRootPath, "uploads", "perfiles", Path.GetFileName(url));
                if (File.Exists(ruta)) File.Delete(ruta);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No se pudo borrar la foto anterior {Url}", url);
            }
        }

        public const string ScriptPendiente = "Falta correr Sql/2026-10_perfiles_soporte.sql en la base para guardar el perfil.";

        private static bool EsTablaFaltante(Exception ex)
        {
            string m = (ex.InnerException ?? ex).Message;
            return m.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) || m.Contains("PerfilesUsuario") || m.Contains("TicketsSoporte");
        }

        /// <summary>Iniciales para el avatar sin foto (máx. 2 letras).</summary>
        public static string Iniciales(string? nombre)
        {
            var partes = (nombre ?? "?").Split(new[] { ' ', '@', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 0) return "?";
            return (partes.Length == 1 ? partes[0].Substring(0, 1) : partes[0].Substring(0, 1) + partes[^1].Substring(0, 1)).ToUpper();
        }
    }
}
