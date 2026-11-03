using System.Net;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Services
{
    /// <summary>
    /// Registro de empresas desde el portal (oct-2026). La solicitud queda PENDIENTE; al
    /// aprobarla se crean el cliente, su usuario (rol Cliente) y su primera planta.
    /// Si el RUT ya es cliente de Atilson, el registro se bloquea (el acceso lo crea su
    /// ejecutivo).
    /// </summary>
    public class RegistroClientesService
    {
        private readonly AtilsonContext _context;
        private readonly EmailService _email;
        private readonly IConfiguration _config;
        private readonly ILogger<RegistroClientesService> _logger;
        private readonly IServiceScopeFactory _scopes;

        public RegistroClientesService(AtilsonContext context, EmailService email, IConfiguration config, ILogger<RegistroClientesService> logger, IServiceScopeFactory scopes)
        {
            _context = context;
            _email = email;
            _config = config;
            _logger = logger;
            _scopes = scopes;
        }

        public static string NormalizarCorreo(string? correo) => (correo ?? "").Trim().ToLowerInvariant();

        /// <summary>Motivo por el que no se puede registrar (o aprobar) esta empresa/correo; null si está libre.</summary>
        public async Task<string?> MotivoBloqueoAsync(string rut, string correo, int? excluirSolicitud = null)
        {
            string rutLimpio = RutHelper.Limpiar(rut);
            string correoNorm = NormalizarCorreo(correo);

            var ruts = await _context.Clientes.AsNoTracking().Where(c => c.Rut != null && !c.IsDeleted).Select(c => c.Rut!).ToListAsync();
            if (ruts.Any(r => RutHelper.Limpiar(r) == rutLimpio))
                return "Esta empresa ya es cliente de Atilson. Pide a tu ejecutivo(a) que te cree el acceso al portal.";

            var correos = await _context.Usuarios.AsNoTracking().Select(u => u.Correo).ToListAsync();
            if (correos.Any(c => NormalizarCorreo(c) == correoNorm))
                return "Ese correo ya tiene una cuenta. Inicia sesión o pide ayuda a tu ejecutivo(a) de Atilson.";

            var pendientes = await _context.SolicitudesRegistroCliente.AsNoTracking()
                .Where(s => s.Estado == SolicitudRegistroCliente.EstadoPendiente && (excluirSolicitud == null || s.Id != excluirSolicitud))
                .Select(s => new { s.Rut, s.Correo })
                .ToListAsync();
            if (pendientes.Any(s => RutHelper.Limpiar(s.Rut) == rutLimpio))
                return "Ya hay una solicitud de registro en revisión para esta empresa. Te avisaremos por correo cuando esté aprobada.";
            if (pendientes.Any(s => NormalizarCorreo(s.Correo) == correoNorm))
                return "Ya hay una solicitud de registro en revisión con este correo.";

            return null;
        }

        public async Task<int> ContarPendientesAsync()
        {
            try
            {
                return await _context.SolicitudesRegistroCliente.AsNoTracking().CountAsync(s => s.Estado == SolicitudRegistroCliente.EstadoPendiente);
            }
            catch (Exception ex)
            {
                // Sin la tabla (script SQL aún no aplicado) el menú no debe caerse.
                _logger.LogWarning(ex, "No se pudieron contar las solicitudes de registro.");
                return 0;
            }
        }

        /// <summary>Plantas y depósitos registrados por clientes que Atilson aún no valida.</summary>
        public async Task<(int Plantas, int Depositos)> ContarUbicacionesPorValidarAsync()
        {
            try
            {
                int plantas = await _context.Plantas.AsNoTracking().CountAsync(p => p.Activo && !p.Validada);
                int depositos = await _context.Depositos.AsNoTracking().CountAsync(d => d.Activo == 1 && !d.Validado);
                return (plantas, depositos);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron contar las plantas/depósitos por validar.");
                return (0, 0);
            }
        }

        public async Task<(bool Ok, string Mensaje)> AprobarAsync(int idSolicitud, string usuario, string urlLogin)
        {
            var s = await _context.SolicitudesRegistroCliente.FirstOrDefaultAsync(x => x.Id == idSolicitud);
            if (s == null) return (false, "La solicitud ya no existe.");
            if (s.Estado != SolicitudRegistroCliente.EstadoPendiente) return (false, $"La solicitud ya fue {(s.Estado == SolicitudRegistroCliente.EstadoAprobada ? "aprobada" : "rechazada")}.");

            var bloqueo = await MotivoBloqueoAsync(s.Rut, s.Correo, s.Id);
            if (bloqueo != null) return (false, "No se puede aprobar: " + bloqueo);
            bool traePlanta = !string.IsNullOrWhiteSpace(s.NombrePlanta) && s.IdCiudadPlanta.HasValue;
            if (traePlanta && !await _context.Ciudades.AnyAsync(c => c.Id == s.IdCiudadPlanta!.Value)) return (false, "No se puede aprobar: la comuna de la planta ya no existe en Maestros › Ciudades.");

            DateTime ahora = MaestrosAuditoria.Ahora();
            await using var tx = await _context.Database.BeginTransactionAsync();

            var cliente = new Cliente
            {
                RazonSocial = s.RazonSocial,
                Rut = RutHelper.Formatear(s.Rut),
                Contacto = s.NombreContacto,
                Telefono = s.Telefono,
                Correo = s.Correo,
                Direccion = s.DireccionOficina,
                Activo = 1,
                FechaCreacion = ahora,
                UsuarioCreador = usuario,
                FechaModificacion = ahora,
                UsuarioModificador = usuario
            };
            _context.Clientes.Add(cliente);
            await _context.SaveChangesAsync();

            var usuarioPortal = new Usuario
            {
                NombreCompleto = s.NombreContacto,
                Correo = s.Correo,
                Password = s.PasswordHash,
                Rol = "Cliente",
                Activo = 1,
                IdCliente = cliente.Id
            };
            _context.Usuarios.Add(usuarioPortal);

            Planta? planta = null;
            if (traePlanta)
            {
                planta = new Planta
                {
                    Nombre = s.NombrePlanta!,
                    IdCiudad = s.IdCiudadPlanta!.Value,
                    IdCliente = cliente.Id,
                    Direccion = s.DireccionPlanta,
                    UrlGoogleMaps = s.UrlGoogleMapsPlanta,
                    Activo = true,
                    RegistradaPorCliente = true,
                    Validada = true,
                    FechaValidacion = ahora,
                    UsuarioValidacion = usuario,
                    FechaCreacion = ahora,
                    UsuarioCreador = Recortar($"{s.NombreContacto} (portal)", 100),
                    FechaModificacion = ahora,
                    UsuarioModificador = usuario
                };
                _context.Plantas.Add(planta);
            }
            await _context.SaveChangesAsync();

            s.Estado = SolicitudRegistroCliente.EstadoAprobada;
            s.FechaRevision = ahora;
            s.UsuarioRevision = usuario;
            s.IdClienteCreado = cliente.Id;
            s.IdUsuarioCreado = usuarioPortal.Id;
            MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntCliente, cliente.Id, "Registro creado", usuario, $"Registro desde el portal (solicitud #{s.Id}), aprobado");
            if (planta != null)
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntPlanta, planta.Id, "Registro creado", usuario, $"Registrada por {cliente.RazonSocial} en el portal, validada al aprobar el registro");
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            EncolarCorreo(s.Correo, "Tu cuenta en el portal de Atilson Cargo fue aprobada", Plantilla("¡Tu cuenta está activa!",
                $"<p>Hola {WebUtility.HtmlEncode(s.NombreContacto)},</p>" +
                $"<p>Aprobamos el registro de <b>{WebUtility.HtmlEncode(s.RazonSocial)}</b>. Ya puedes ingresar al portal con tu correo <b>{WebUtility.HtmlEncode(s.Correo)}</b> y la contraseña que elegiste.</p>",
                "Ingresar al portal", urlLogin));

            return (true, $"Registro de «{cliente.RazonSocial}» aprobado. Se creó el cliente y su usuario ({s.Correo})" + (planta != null ? $" y la planta «{planta.Nombre}»." : "."));
        }

        public async Task<(bool Ok, string Mensaje)> RechazarAsync(int idSolicitud, string motivo, string usuario)
        {
            var s = await _context.SolicitudesRegistroCliente.FirstOrDefaultAsync(x => x.Id == idSolicitud);
            if (s == null) return (false, "La solicitud ya no existe.");
            if (s.Estado != SolicitudRegistroCliente.EstadoPendiente) return (false, "La solicitud ya fue revisada.");
            if (string.IsNullOrWhiteSpace(motivo)) return (false, "Escribe el motivo del rechazo: se le muestra al cliente.");

            s.Estado = SolicitudRegistroCliente.EstadoRechazada;
            s.MotivoRechazo = Recortar(motivo.Trim(), 500);
            s.FechaRevision = MaestrosAuditoria.Ahora();
            s.UsuarioRevision = usuario;
            await _context.SaveChangesAsync();

            EncolarCorreo(s.Correo, "Tu solicitud de registro en Atilson Cargo", Plantilla("Revisamos tu solicitud",
                $"<p>Hola {WebUtility.HtmlEncode(s.NombreContacto)},</p>" +
                $"<p>Revisamos la solicitud de registro de <b>{WebUtility.HtmlEncode(s.RazonSocial)}</b> y no pudimos aprobarla por el siguiente motivo:</p>" +
                $"<blockquote style=\"margin:12px 0;padding:10px 14px;border-left:3px solid #dc2626;background:#fef2f2;color:#7f1d1d;\">{WebUtility.HtmlEncode(s.MotivoRechazo)}</blockquote>" +
                "<p>Si tienes dudas, responde este correo o contacta a tu ejecutivo(a).</p>"));

            return (true, $"Solicitud de «{s.RazonSocial}» rechazada.");
        }

        /// <summary>Correo interno configurado para avisos: PortalRegistro:CorreoAviso o, si no está, el remitente del SMTP.</summary>
        public string? CorreoAvisoInterno => !string.IsNullOrWhiteSpace(_config["PortalRegistro:CorreoAviso"]) ? _config["PortalRegistro:CorreoAviso"] : _config["Smtp:Sender"];

        /// <summary>true si el SMTP tiene remitente y contraseña (sin eso ningún correo sale).</summary>
        public bool CorreoConfigurado => !string.IsNullOrWhiteSpace(_config["Smtp:Sender"]) && !string.IsNullOrWhiteSpace(_config["Smtp:Password"]);

        /// <summary>
        /// Internos que reciben el aviso de un registro nuevo (nov-2026): los correos de
        /// PortalRegistro:CorreoAviso (se aceptan varios, separados por coma), el remitente del
        /// SMTP y los usuarios activos con rol Administrador o Informático y las jefaturas.
        /// Con <paramref name="incluirVentas"/> se suma el equipo de Ventas (registros de empresas).
        /// </summary>
        public async Task<List<string>> CorreosInternosAsync(bool incluirVentas)
        {
            var lista = new List<string>();
            void Sumar(string? c)
            {
                foreach (var x in (c ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (x.Contains('@') && !lista.Contains(NormalizarCorreo(x))) lista.Add(NormalizarCorreo(x));
            }
            Sumar(_config["PortalRegistro:CorreoAviso"]);
            Sumar(_config["Smtp:Sender"]);
            try
            {
                var usuarios = await _context.Usuarios.AsNoTracking()
                    .Where(u => u.Rol != "Cliente" && (u.Activo == null || u.Activo == 1))
                    .Select(u => new { u.Correo, u.Rol, u.NombreCompleto }).ToListAsync();
                foreach (var u in usuarios)
                {
                    bool admin = u.Rol == PermisosEquipo.RolAdministrador || u.Rol == "Informático" || PermisosEquipo.Jefaturas.Contains(u.NombreCompleto);
                    if (admin || (incluirVentas && u.Rol == "Ventas")) Sumar(u.Correo);
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "No se pudo leer el equipo para avisar un registro nuevo."); }
            return lista;
        }

        /// <summary>
        /// Avisos de una solicitud nueva: confirmación a quien se registró y aviso a los internos.
        /// Se envían en segundo plano para que el formulario responda al instante aunque el SMTP tarde.
        /// Además el ERP muestra el aviso en pantalla a los internos (_Layout), aunque el correo falle.
        /// </summary>
        public async Task AvisarNuevaSolicitudAsync(SolicitudRegistroCliente s, string urlRevision)
        {
            EncolarCorreo(s.Correo, "Recibimos tu solicitud de registro en Atilson Cargo", Plantilla("Recibimos tu solicitud",
                $"<p>Hola {WebUtility.HtmlEncode(s.NombreContacto)},</p>" +
                $"<p>Recibimos la solicitud de registro de <b>{WebUtility.HtmlEncode(s.RazonSocial)}</b> en el portal de clientes. Nuestro equipo revisará los datos y te avisaremos a este correo cuando tu cuenta esté activa.</p>" +
                "<p>Si no hiciste esta solicitud, puedes ignorar este mensaje.</p>"));

            string html = Plantilla("Nueva solicitud de registro",
                $"<p><b>{WebUtility.HtmlEncode(s.RazonSocial)}</b> (RUT {WebUtility.HtmlEncode(s.Rut)}) pidió acceso al portal de clientes.</p>" +
                $"<p>Contacto: {WebUtility.HtmlEncode(s.NombreContacto)} · {WebUtility.HtmlEncode(s.Correo)} · {WebUtility.HtmlEncode(s.Telefono)}<br>Oficina: {WebUtility.HtmlEncode(s.DireccionOficina)}</p>",
                "Revisar solicitud", urlRevision);
            foreach (var destino in await CorreosInternosAsync(incluirVentas: true))
                EncolarCorreo(destino, $"Nueva solicitud de registro en el portal: {s.RazonSocial}", html);
        }

        /// <summary>Últimas solicitudes pendientes de empresas (para el aviso en pantalla del ERP).</summary>
        public async Task<List<(int Id, string Nombre, DateTime Fecha)>> UltimasPendientesAsync(int max = 3)
        {
            try
            {
                var l = await _context.SolicitudesRegistroCliente.AsNoTracking()
                    .Where(x => x.Estado == SolicitudRegistroCliente.EstadoPendiente)
                    .OrderByDescending(x => x.Id).Take(max)
                    .Select(x => new { x.Id, x.RazonSocial, x.FechaSolicitud }).ToListAsync();
                return l.Select(x => (x.Id, x.RazonSocial, x.FechaSolicitud)).ToList();
            }
            catch { return new(); }
        }

        /// <summary>Envía un correo de prueba y devuelve el error tal cual, para diagnosticar la configuración SMTP.</summary>
        public async Task<(bool Ok, string Mensaje)> ProbarCorreoAsync(string destino)
        {
            if (string.IsNullOrWhiteSpace(_config["Smtp:Sender"]))
                return (false, "Falta configurar Smtp:Sender (el correo que envía).");
            if (string.IsNullOrWhiteSpace(_config["Smtp:Password"]))
                return (false, "Falta configurar Smtp:Password (contraseña de aplicación de Gmail). En el servidor: variable de entorno Smtp__Password; en desarrollo: dotnet user-secrets set \"Smtp:Password\" \"...\".");
            try
            {
                await _email.EnviarCorreoAsync(destino, "Prueba de correo del ERP Atilson", Plantilla("Prueba de correo",
                    "<p>Si estás leyendo esto, el envío de correos del ERP funciona correctamente.</p>"));
                return (true, $"Correo de prueba enviado a {destino}. Revisa la bandeja de entrada (y la de spam).");
            }
            catch (Exception ex)
            {
                string detalle = (ex.InnerException ?? ex).Message;
                if (detalle.Contains("5.7.0") || detalle.Contains("5.7.8") || detalle.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
                    detalle += " — Gmail rechazó la contraseña: usa una «contraseña de aplicación» de la cuenta remitente (requiere verificación en dos pasos).";
                _logger.LogWarning(ex, "Prueba de correo fallida a {Destino}", destino);
                return (false, "No se pudo enviar: " + detalle);
            }
        }

        /// <summary>Envía un correo en segundo plano (con su propio alcance de servicios); los errores quedan en el log.</summary>
        public void EncolarCorreo(string destino, string asunto, string html)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var email = scope.ServiceProvider.GetRequiredService<EmailService>();
                    await email.EnviarCorreoAsync(destino, asunto, html);
                }
                catch (Exception ex)
                {
                    // El correo es un aviso: si el SMTP no está configurado o falla, el registro igual queda hecho.
                    _logger.LogWarning(ex, "No se pudo enviar el correo \"{Asunto}\" a {Destino}", asunto, destino);
                }
            });
        }

        /// <summary>Plantilla HTML simple de los correos del portal.</summary>
        public static string Plantilla(string titulo, string cuerpoHtml, string? textoBoton = null, string? urlBoton = null, string encabezado = "ATILSON CARGO · PORTAL DE CLIENTES")
        {
            string boton = string.IsNullOrWhiteSpace(textoBoton) || string.IsNullOrWhiteSpace(urlBoton) ? "" :
                $"<p style=\"margin:24px 0 8px;\"><a href=\"{WebUtility.HtmlEncode(urlBoton)}\" style=\"background:#2c1b4d;color:#ffffff;text-decoration:none;font-weight:700;padding:12px 22px;border-radius:8px;display:inline-block;\">{WebUtility.HtmlEncode(textoBoton)}</a></p>" +
                $"<p style=\"font-size:12px;color:#64748b;\">O copia este enlace: {WebUtility.HtmlEncode(urlBoton)}</p>";
            return "<div style=\"background:#f5f4f9;padding:24px 12px;font-family:Segoe UI,Arial,sans-serif;\">" +
                   "<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #ece8f3;border-radius:12px;overflow:hidden;\">" +
                   $"<div style=\"background:#2c1b4d;color:#ffffff;padding:18px 24px;font-size:13px;font-weight:700;letter-spacing:.5px;\">{WebUtility.HtmlEncode(encabezado)}</div>" +
                   $"<div style=\"padding:24px;color:#0f172a;font-size:14px;line-height:1.6;\"><h1 style=\"font-size:20px;margin:0 0 12px;\">{WebUtility.HtmlEncode(titulo)}</h1>{cuerpoHtml}{boton}" +
                   "<p style=\"margin-top:24px;color:#64748b;font-size:12px;\">Equipo Atilson Cargo</p></div></div></div>";
        }

        private static string Recortar(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
