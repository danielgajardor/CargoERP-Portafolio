using System.Security.Claims;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace AtilsonCargoSpa.Pages.App.Equipo
{
    // =====================================================================================
    // Permisos por usuario (13-nov-2026, solo administradores: carpeta /App/Equipo).
    // Por persona se marcan las vistas del menú, por módulo completo o una a una. Sin
    // selección propia, la persona ve lo que da su rol. Aplica sin volver a iniciar sesión.
    // =====================================================================================
    public class PermisosModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly IMemoryCache _cache;

        private readonly EmailService _email;
        private readonly IConfiguration _config;

        public PermisosModel(AtilsonContext context, IMemoryCache cache, EmailService email, IConfiguration config)
        {
            _context = context;
            _cache = cache;
            _email = email;
            _config = config;
        }

        [BindProperty(SupportsGet = true)] public int? Id { get; set; }
        // persona | resumen | historial | correo
        [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "persona";

        // Resumen: vistas efectivas por persona (propias o del rol) y perfiles por rol, para copiar.
        public Dictionary<int, HashSet<string>> Efectivas { get; set; } = new();
        public Dictionary<string, List<string>> PerfilesRol { get; set; } = new();
        public List<(HistorialCambiosComercial Cambio, string Persona)> Historial { get; set; } = new();
        public (bool Valido, string Mensaje) EstadoCorreo { get; set; }
        public string? CorreoRemitente { get; set; }
        public string? CorreoServidor { get; set; }
        public string? CorreoUsuarioActual { get; set; }

        public List<Usuario> Usuarios { get; set; } = new();
        public HashSet<int> ConSeleccion { get; set; } = new();
        public Usuario? Seleccionado { get; set; }
        public HashSet<string> Marcadas { get; set; } = new();
        public HashSet<string> PorRol { get; set; } = new();
        public bool Personalizado { get; set; }
        public string? Modificado { get; set; }
        public bool FaltaScript { get; set; }
        public bool SeleccionadoEsAdmin { get; set; }

        private static ClaimsPrincipal PrincipalDe(Usuario u)
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, u.NombreCompleto ?? ""), new(ClaimTypes.Role, u.Rol ?? "") };
            if (u.PermisoTarifasTransporte) claims.Add(new Claim(PermisosEquipo.ClaimPermiso, PermisosEquipo.PermisoTarifasTransporte));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Rol"));
        }

        /// <summary>Lo que da el rol de una persona (para mostrarlo y para "Restablecer según rol").</summary>
        public static HashSet<string> VistasPorRol(Usuario u)
        {
            var p = PrincipalDe(u);
            return VistasCatalogo.Todas.Where(c => PermisosEquipo.VistaPorRol(p, c)).ToHashSet();
        }

        public async Task OnGetAsync()
        {
            Usuarios = await _context.Usuarios.AsNoTracking().Where(u => u.Rol != "Cliente")
                .OrderByDescending(u => u.Activo == 1).ThenBy(u => u.NombreCompleto).ToListAsync();
            Dictionary<int, UsuarioVista> filas = new();
            try { filas = await _context.UsuariosVistas.AsNoTracking().ToDictionaryAsync(x => x.IdUsuario); }
            catch { FaltaScript = true; }
            ConSeleccion = filas.Keys.ToHashSet();

            foreach (var u in Usuarios)
                Efectivas[u.Id] = filas.TryGetValue(u.Id, out var f)
                    ? f.Vistas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(VistasCatalogo.Existe).ToHashSet()
                    : VistasPorRol(u);
            foreach (var rol in PermisosEquipo.RolesAsignables)
                PerfilesRol[rol] = VistasPorRol(new Usuario { NombreCompleto = "", Rol = rol }).OrderBy(x => x).ToList();

            if (Tab == "historial")
            {
                string ent = MaestrosAuditoria.Prefijo + MaestrosAuditoria.EntPermisos;
                var cambios = await _context.HistorialCambiosComercial.AsNoTracking().Where(h => h.TipoEntidad == ent)
                    .OrderByDescending(h => h.FechaCambio).ThenByDescending(h => h.Id).Take(300).ToListAsync();
                var nombres = Usuarios.ToDictionary(u => u.Id, u => u.NombreCompleto);
                Historial = cambios.Select(h => (h, nombres.TryGetValue(h.IdRegistro, out var n) ? n : $"Usuario #{h.IdRegistro}")).ToList();
            }
            if (Tab == "correo")
            {
                EstadoCorreo = _email.DiagnosticarConfiguracion();
                CorreoRemitente = _config["Smtp:Sender"];
                CorreoServidor = $"{_config["Smtp:Host"]}:{_config["Smtp:Port"]}";
                CorreoUsuarioActual = User.FindFirst(ClaimTypes.Email)?.Value;
            }

            Seleccionado = Usuarios.FirstOrDefault(u => u.Id == Id) ?? Usuarios.FirstOrDefault(u => u.Activo == 1) ?? Usuarios.FirstOrDefault();
            if (Seleccionado == null) return;
            Id = Seleccionado.Id;
            PorRol = VistasPorRol(Seleccionado);
            SeleccionadoEsAdmin = PermisosEquipo.EsAdmin(PrincipalDe(Seleccionado));
            if (filas.TryGetValue(Seleccionado.Id, out var fila))
            {
                Personalizado = true;
                Marcadas = fila.Vistas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(VistasCatalogo.Existe).ToHashSet();
                Modificado = $"{fila.FechaModificacion:dd/MM/yyyy HH:mm}{(string.IsNullOrWhiteSpace(fila.UsuarioModificador) ? "" : " · " + fila.UsuarioModificador)}";
            }
            else Marcadas = PorRol;
        }

        public async Task<IActionResult> OnPostGuardarAsync(int id, List<string>? vistas)
        {
            var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == id && x.Rol != "Cliente");
            if (u == null) return NotFound();
            var elegidas = (vistas ?? new List<string>()).Where(VistasCatalogo.Existe).Distinct().ToList();
            try
            {
                var fila = await _context.UsuariosVistas.FirstOrDefaultAsync(x => x.IdUsuario == id);
                var antes = fila != null
                    ? fila.Vistas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet()
                    : VistasPorRol(u);
                RegistrarCambio(id, antes, elegidas.ToHashSet(), fila == null ? "Vistas propias (antes según rol)" : "Vistas propias");
                if (fila == null) { fila = new UsuarioVista { IdUsuario = id }; _context.UsuariosVistas.Add(fila); }
                fila.Vistas = string.Join(",", elegidas);
                fila.FechaModificacion = DateTime.Now;
                fila.UsuarioModificador = User.Identity?.Name;
                await _context.SaveChangesAsync();
            }
            catch (Exception)
            {
                TempData["PermisosError"] = "No se pudo guardar: falta correr Sql/2026-11_vistas_por_usuario.sql en la base.";
                return RedirectToPage(new { id });
            }
            _cache.Remove(VistasClaimsTransformation.ClaveCache(u.Correo));
            TempData["PermisosOk"] = $"Permisos de {u.NombreCompleto} guardados: {elegidas.Count} vista(s). Aplica en su próxima página, sin volver a iniciar sesión.";
            return RedirectToPage(new { id });
        }

        private static string NombreVista(string clave) =>
            VistasCatalogo.Modulos.SelectMany(m => m.Vistas.Select(v => (m, v))).Where(x => x.v.Clave == clave)
                .Select(x => x.m.Vistas.Length == 1 ? x.m.Nombre : $"{x.m.Nombre} › {x.v.Nombre.Split(" (")[0]}").FirstOrDefault() ?? clave;

        // Historial de permisos: qué vistas se agregaron y quitaron, quién y cuándo.
        private void RegistrarCambio(int idUsuario, HashSet<string> antes, HashSet<string> despues, string comentario)
        {
            var agregadas = despues.Except(antes).Select(NombreVista).ToList();
            var quitadas = antes.Except(despues).Select(NombreVista).ToList();
            var cambios = new MaestrosAuditoria.Cambios();
            if (agregadas.Count > 0) cambios.Agregar("Vistas agregadas", null, string.Join(" · ", agregadas));
            if (quitadas.Count > 0) cambios.Agregar("Vistas quitadas", string.Join(" · ", quitadas), null);
            if (!cambios.Hay) cambios.Agregar("Sin cambios de vistas", null, comentario);
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntPermisos, idUsuario, cambios, User.Identity?.Name ?? "Administrador", comentario);
        }

        // Correo de prueba (13-nov): confirma que el servidor SMTP acepta el envío desde el remitente.
        public async Task<IActionResult> OnPostCorreoPruebaAsync(string? destino)
        {
            destino = (destino ?? "").Trim();
            if (destino == "") { TempData["PermisosError"] = "Indica un correo de destino para la prueba."; return RedirectToPage(new { tab = "correo" }); }
            try
            {
                string remitente = _config["Smtp:Sender"] ?? "";
                await _email.EnviarCorreoAsync(destino, "[ATILSON CARGO] Correo de prueba",
                    "<div style=\"font-family:Arial,sans-serif;font-size:14px;color:#0f172a\"><p>Este es un correo de prueba del sistema Atilson Cargo.</p>"
                    + $"<p>Enviado por {System.Net.WebUtility.HtmlEncode(User.Identity?.Name ?? "")} el {DateTime.Now:dd/MM/yyyy HH:mm} desde <strong>{System.Net.WebUtility.HtmlEncode(remitente)}</strong>.</p>"
                    + "<p>Si lo recibiste, los correos del sistema (confirmaciones de zarpe y arribo, avisos de llegada, reservas…) salen correctamente.</p></div>");
                TempData["PermisosOk"] = $"Correo de prueba enviado a {destino}. Revisa la bandeja de entrada (y spam).";
            }
            catch (Exception ex)
            {
                TempData["PermisosError"] = "No se pudo enviar el correo de prueba: " + ex.Message;
            }
            return RedirectToPage(new { tab = "correo" });
        }

        public async Task<IActionResult> OnPostRestablecerAsync(int id)
        {
            var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == id && x.Rol != "Cliente");
            if (u == null) return NotFound();
            try
            {
                var fila = await _context.UsuariosVistas.FirstOrDefaultAsync(x => x.IdUsuario == id);
                if (fila != null)
                {
                    RegistrarCambio(id, fila.Vistas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(), VistasPorRol(u), $"Restablecido según rol ({u.Rol})");
                    _context.UsuariosVistas.Remove(fila);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                TempData["PermisosError"] = "No se pudo restablecer: falta correr Sql/2026-11_vistas_por_usuario.sql en la base.";
                return RedirectToPage(new { id });
            }
            _cache.Remove(VistasClaimsTransformation.ClaveCache(u.Correo));
            TempData["PermisosOk"] = $"{u.NombreCompleto} vuelve a ver lo que da su rol ({u.Rol}).";
            return RedirectToPage(new { id });
        }
    }
}
