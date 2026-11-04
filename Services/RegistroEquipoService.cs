using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    // ============================================================================
    // Cuentas del equipo Atilson (oct-2026)
    // ----------------------------------------------------------------------------
    // 1) La persona pide su cuenta en /RegistroEquipo (nombre, RUT, teléfono, correo,
    //    contraseña). Queda PENDIENTE en dbo.SolicitudesRegistroEquipo.
    // 2) Un administrador (PermisosEquipo.EsAdmin) la revisa en Maestros › Equipo
    //    Atilson, elige el rol y la valida: ahí se crea el usuario en dbo.Usuarios.
    // 3) Desde la misma pantalla se cambia el rol o se desactiva a alguien del equipo.
    // Los correos usan el mismo envío en segundo plano del registro de clientes.
    // ============================================================================
    public class RegistroEquipoService
    {
        private const string Encabezado = "ATILSON CARGO · EQUIPO";
        private readonly AtilsonContext _context;
        private readonly RegistroClientesService _correos;
        private readonly ILogger<RegistroEquipoService> _logger;

        public RegistroEquipoService(AtilsonContext context, RegistroClientesService correos, ILogger<RegistroEquipoService> logger)
        {
            _context = context;
            _correos = correos;
            _logger = logger;
        }

        /// <summary>Motivo por el que no se puede registrar/aprobar (correo o RUT ya usados), o null.</summary>
        public async Task<string?> MotivoBloqueoAsync(string rut, string correo, int? excluirSolicitud = null)
        {
            string c = RegistroClientesService.NormalizarCorreo(correo);
            string r = RutHelper.Formatear(rut);
            if (await _context.Usuarios.AsNoTracking().AnyAsync(u => u.Correo.ToLower() == c))
                return "Ese correo ya tiene una cuenta en el ERP. Si olvidaste tu contraseña, pídele ayuda a un administrador.";
            bool pendiente = await _context.SolicitudesRegistroEquipo.AsNoTracking()
                .AnyAsync(s => s.Estado == SolicitudRegistroEquipo.EstadoPendiente && (s.Correo == c || s.Rut == r)
                               && (excluirSolicitud == null || s.Id != excluirSolicitud));
            if (pendiente) return "Ya hay una solicitud pendiente con ese correo o RUT. Un administrador la revisará pronto.";
            return null;
        }

        public async Task<int> ContarPendientesAsync()
        {
            try
            {
                return await _context.SolicitudesRegistroEquipo.AsNoTracking().CountAsync(s => s.Estado == SolicitudRegistroEquipo.EstadoPendiente);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No se pudo contar solicitudes del equipo (¿falta Sql/2026-10_registro_equipo.sql?)");
                return 0;
            }
        }

        /// <summary>Estado de una solicitud aún no aprobada para ese correo (para explicar en el Login).</summary>
        public async Task<SolicitudRegistroEquipo?> SolicitudNoAprobadaAsync(string correo)
        {
            try
            {
                string c = RegistroClientesService.NormalizarCorreo(correo);
                return await _context.SolicitudesRegistroEquipo.AsNoTracking()
                    .Where(s => s.Correo == c && s.Estado != SolicitudRegistroEquipo.EstadoAprobada)
                    .OrderByDescending(s => s.FechaSolicitud)
                    .FirstOrDefaultAsync();
            }
            catch
            {
                return null;
            }
        }

        public async Task AvisarNuevaSolicitudAsync(SolicitudRegistroEquipo s, string urlRevision)
        {
            _correos.EncolarCorreo(s.Correo, "Recibimos tu solicitud de cuenta en el ERP Atilson", RegistroClientesService.Plantilla("Recibimos tu solicitud",
                $"<p>Hola {WebUtility.HtmlEncode(s.NombreCompleto)},</p>" +
                "<p>Recibimos tu solicitud de cuenta para el ERP de Atilson Cargo. Un administrador revisará tus datos, te asignará el rol que corresponde y te avisaremos a este correo cuando puedas entrar.</p>" +
                "<p>Si no hiciste esta solicitud, puedes ignorar este mensaje.</p>", encabezado: Encabezado));

            // A los administradores (los que validan cuentas) y al correo de avisos configurado.
            string html = RegistroClientesService.Plantilla("Nueva cuenta por validar",
                $"<p><b>{WebUtility.HtmlEncode(s.NombreCompleto)}</b> (RUT {WebUtility.HtmlEncode(s.Rut)}) pidió una cuenta del equipo.</p>" +
                $"<p>{WebUtility.HtmlEncode(s.Correo)} · {WebUtility.HtmlEncode(s.Telefono)}" + (string.IsNullOrWhiteSpace(s.AreaSolicitada) ? "" : $"<br>Área indicada: {WebUtility.HtmlEncode(s.AreaSolicitada)}") + "</p>",
                "Revisar y asignar rol", urlRevision, Encabezado);
            foreach (var destino in await _correos.CorreosInternosAsync(incluirVentas: false))
                _correos.EncolarCorreo(destino, $"Nueva solicitud de cuenta del equipo: {s.NombreCompleto}", html);
        }

        /// <summary>Últimas cuentas del equipo por validar (para el aviso en pantalla del ERP).</summary>
        public async Task<List<(int Id, string Nombre, DateTime Fecha)>> UltimasPendientesAsync(int max = 3)
        {
            try
            {
                var l = await _context.SolicitudesRegistroEquipo.AsNoTracking()
                    .Where(x => x.Estado == SolicitudRegistroEquipo.EstadoPendiente)
                    .OrderByDescending(x => x.Id).Take(max)
                    .Select(x => new { x.Id, x.NombreCompleto, x.FechaSolicitud }).ToListAsync();
                return l.Select(x => (x.Id, x.NombreCompleto, x.FechaSolicitud)).ToList();
            }
            catch { return new(); }
        }

        public async Task<(bool Ok, string Mensaje)> AprobarAsync(int idSolicitud, string? rol, string usuario, string urlLogin)
        {
            var s = await _context.SolicitudesRegistroEquipo.FirstOrDefaultAsync(x => x.Id == idSolicitud);
            if (s == null) return (false, "No se encontró la solicitud.");
            if (s.Estado != SolicitudRegistroEquipo.EstadoPendiente) return (false, "Esta solicitud ya fue revisada.");
            string? rolValido = PermisosEquipo.NormalizarRol(rol);
            if (rolValido == null) return (false, "Elige el rol de la persona antes de validar la cuenta.");
            if (await _context.Usuarios.AsNoTracking().AnyAsync(u => u.Correo.ToLower() == s.Correo))
                return (false, $"Ya existe un usuario con el correo {s.Correo}. Rechaza esta solicitud o cambia el rol de ese usuario en la pestaña Usuarios.");

            var nuevo = new Usuario
            {
                NombreCompleto = s.NombreCompleto.Length > 100 ? s.NombreCompleto.Substring(0, 100) : s.NombreCompleto,
                Correo = s.Correo,
                Password = s.PasswordHash,
                Rol = rolValido,
                Activo = 1
            };
            _context.Usuarios.Add(nuevo);
            await _context.SaveChangesAsync();

            s.Estado = SolicitudRegistroEquipo.EstadoAprobada;
            s.RolAsignado = rolValido;
            s.FechaRevision = DateTime.Now;
            s.UsuarioRevision = usuario;
            s.IdUsuarioCreado = nuevo.Id;
            await _context.SaveChangesAsync();

            _correos.EncolarCorreo(s.Correo, "Tu cuenta del ERP Atilson está activa", RegistroClientesService.Plantilla("Tu cuenta está activa",
                $"<p>Hola {WebUtility.HtmlEncode(s.NombreCompleto)},</p>" +
                $"<p>Tu cuenta del ERP fue validada con el rol <b>{WebUtility.HtmlEncode(rolValido)}</b>. Ya puedes entrar con tu correo y la contraseña que elegiste.</p>",
                "Entrar al ERP", urlLogin, Encabezado));
            return (true, $"Cuenta de {s.NombreCompleto} validada con el rol {rolValido}. Ya puede entrar con su correo.");
        }

        public async Task<(bool Ok, string Mensaje)> RechazarAsync(int idSolicitud, string motivo, string usuario)
        {
            motivo = (motivo ?? "").Trim();
            if (motivo.Length == 0) return (false, "Escribe el motivo del rechazo.");
            if (motivo.Length > 500) motivo = motivo.Substring(0, 500);
            var s = await _context.SolicitudesRegistroEquipo.FirstOrDefaultAsync(x => x.Id == idSolicitud);
            if (s == null) return (false, "No se encontró la solicitud.");
            if (s.Estado != SolicitudRegistroEquipo.EstadoPendiente) return (false, "Esta solicitud ya fue revisada.");
            s.Estado = SolicitudRegistroEquipo.EstadoRechazada;
            s.MotivoRechazo = motivo;
            s.FechaRevision = DateTime.Now;
            s.UsuarioRevision = usuario;
            await _context.SaveChangesAsync();
            _correos.EncolarCorreo(s.Correo, "Tu solicitud de cuenta del ERP Atilson", RegistroClientesService.Plantilla("Solicitud no aprobada",
                $"<p>Hola {WebUtility.HtmlEncode(s.NombreCompleto)},</p><p>Tu solicitud de cuenta no fue aprobada. Motivo: {WebUtility.HtmlEncode(motivo)}</p>", encabezado: Encabezado));
            return (true, $"Solicitud de {s.NombreCompleto} rechazada.");
        }

        public async Task<(bool Ok, string Mensaje)> CambiarRolAsync(int idUsuario, string? rol, string? correoActual, string usuario)
        {
            string? rolValido = PermisosEquipo.NormalizarRol(rol);
            if (rolValido == null) return (false, "Rol no válido.");
            var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == idUsuario);
            if (u == null || u.Rol == "Cliente") return (false, "No se encontró el usuario del equipo.");
            if (EsUnoMismo(u, correoActual) && u.Rol == PermisosEquipo.RolAdministrador && rolValido != PermisosEquipo.RolAdministrador)
                return (false, "No puedes quitarte a ti mismo el rol Administrador. Pídeselo a otro administrador.");
            if (u.Rol == rolValido) return (true, $"{u.NombreCompleto} ya tenía el rol {rolValido}.");
            string antes = u.Rol;
            u.Rol = rolValido;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Rol de {Usuario} cambiado de {Antes} a {Despues} por {Quien}", u.Correo, antes, rolValido, usuario);
            return (true, $"{u.NombreCompleto}: rol cambiado de {antes} a {rolValido}. Lo verá la próxima vez que inicie sesión.");
        }

        public async Task<(bool Ok, string Mensaje)> CambiarEstadoAsync(int idUsuario, bool activar, string? correoActual, string usuario)
        {
            var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == idUsuario);
            if (u == null || u.Rol == "Cliente") return (false, "No se encontró el usuario del equipo.");
            if (!activar && EsUnoMismo(u, correoActual)) return (false, "No puedes desactivar tu propia cuenta.");
            u.Activo = (byte)(activar ? 1 : 0);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Usuario {Usuario} {Accion} por {Quien}", u.Correo, activar ? "activado" : "desactivado", usuario);
            return (true, activar ? $"{u.NombreCompleto} puede volver a entrar." : $"{u.NombreCompleto} ya no puede entrar al ERP.");
        }

        private static bool EsUnoMismo(Usuario u, string? correoActual) =>
            !string.IsNullOrWhiteSpace(correoActual) && string.Equals(u.Correo, correoActual.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
