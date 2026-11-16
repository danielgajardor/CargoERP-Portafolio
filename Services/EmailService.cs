using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace AtilsonCargoSpa.Services
{
    // =====================================================================================
    // Envío de correos del sistema (13-nov-2026: MailKit). Todos los correos (confirmaciones de
    // zarpe y arribo, avisos de llegada, reservas, registros…) salen por aquí, desde Smtp:Sender.
    // System.Net.Mail.SmtpClient no sabe autenticarse con AUTH PLAIN (lo que ofrecen muchos
    // hostings, como el de cargoflow-demo.cl) y enviaba sin autenticar ("SMTP AUTH is required");
    // MailKit negocia PLAIN / LOGIN y admite STARTTLS (587) o SSL directo (465).
    // Configuración: Smtp:Host, Smtp:Port, Smtp:Sender, Smtp:Username (opcional) y Smtp:Password
    // (en user-secrets o variable de entorno Smtp__Password, nunca en appsettings.json).
    // =====================================================================================
    public class EmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public Task EnviarCorreoAsync(
            string destinatario,
            string asunto,
            string mensajeHtml,
            List<(string NombreArchivo, byte[] Contenido, string TipoMime)>? adjuntos = null)
            => EnviarCorreoInternoAsync(destinatario, asunto, mensajeHtml, null, adjuntos);

        public Task EnviarCorreoAsync(
            string destinatario,
            string asunto,
            string mensajeHtml,
            string? copiaCc,
            List<(string NombreArchivo, byte[] Contenido, string TipoMime)>? adjuntos = null)
            => EnviarCorreoInternoAsync(destinatario, asunto, mensajeHtml, copiaCc, adjuntos);

        // Comprobación previa sin abrir una conexión SMTP ni mostrar contraseñas.
        // La aceptación real solo se verifica enviando un mensaje a un buzón de prueba.
        // Modo demostración (13-nov): Smtp:Modo = "Demo" → los correos no salen a internet; cada uno
        // se guarda como .eml en la carpeta "correos-demo" del proyecto (se abren con Outlook).
        private bool ModoDemo => string.Equals(_configuration["Smtp:Modo"], "Demo", StringComparison.OrdinalIgnoreCase);

        public (bool Valido, string Mensaje) DiagnosticarConfiguracion()
        {
            if (ModoDemo)
                return (true, "Modo demostración: los correos no se envían; se guardan como .eml en la carpeta correos-demo del proyecto.");
            if (string.IsNullOrWhiteSpace(_configuration["Smtp:Host"]))
                return (false, "Falta configurar el servidor SMTP.");
            if (!int.TryParse(_configuration["Smtp:Port"] ?? "587", out var puerto) || puerto < 1 || puerto > 65535)
                return (false, "Configura un puerto SMTP válido (normalmente 587, o 465 con SSL).");
            var remitente = _configuration["Smtp:Sender"];
            if (string.IsNullOrWhiteSpace(remitente))
                return (false, "Falta configurar el correo remitente.");
            if (!MailboxAddress.TryParse(remitente.Trim(), out _))
                return (false, "La dirección del remitente SMTP no es válida.");
            if (string.IsNullOrWhiteSpace(UsuarioSmtp(remitente)))
                return (false, "Falta el usuario SMTP.");
            if (string.IsNullOrWhiteSpace(_configuration["Smtp:Password"]))
                return (false, "Falta la contraseña SMTP en un secreto del servidor.");
            return (true, "Configuración SMTP presente. Falta comprobar envío y recepción reales.");
        }

        // Smtp:Username vacío o ausente = se autentica con el remitente.
        private string UsuarioSmtp(string? remitente)
        {
            string? usuario = _configuration["Smtp:Username"];
            return string.IsNullOrWhiteSpace(usuario) ? (remitente ?? "").Trim() : usuario.Trim();
        }

        private static string Respuesta(Exception ex)
        {
            string t = (ex.Message + " " + ex.InnerException?.Message).Trim();
            return " (Respuesta del servidor: " + (t.Length > 300 ? t.Substring(0, 300) + "…" : t) + ")";
        }

        // Traduce los errores SMTP más comunes a un mensaje que Operaciones pueda entender,
        // con la respuesta real del servidor al final para poder diagnosticar.
        private static string MensajeAmigable(Exception ex)
        {
            if (ex is OperationCanceledException)
                return "El servidor de correo no respondió a tiempo (30 s). Intenta nuevamente en unos minutos.";
            if (ex is ParseException || ex is FormatException)
                return "Hay una dirección de correo mal escrita en destinatario o copia.";
            string detalle = (ex.Message + " " + ex.InnerException?.Message).ToLowerInvariant();
            if (ex is AuthenticationException)
            {
                if (detalle.Contains("5.7.139") || detalle.Contains("smtpclientauthentication is disabled"))
                    return "Microsoft 365 tiene desactivado el SMTP autenticado para esta cuenta o para la organización." + Respuesta(ex);
                return "El servidor de correo rechazó el usuario o la contraseña SMTP. Revisa la contraseña guardada en user-secrets (Smtp:Password)." + Respuesta(ex);
            }
            if (ex is SslHandshakeException)
                return "El certificado de seguridad del servidor de correo no coincide con el nombre configurado en Smtp:Host. Prueba con el nombre del servidor del hosting." + Respuesta(ex);
            if (ex is SmtpCommandException cmd)
            {
                switch (cmd.ErrorCode)
                {
                    case SmtpErrorCode.RecipientNotAccepted:
                        return $"El servidor de correo rechazó el destinatario {cmd.Mailbox?.Address}. Revisa la dirección." + Respuesta(ex);
                    case SmtpErrorCode.SenderNotAccepted:
                        return $"El servidor de correo no acepta enviar como {cmd.Mailbox?.Address}: el remitente (Smtp:Sender) debe ser la misma cuenta del usuario SMTP o tener permiso para enviar como ella." + Respuesta(ex);
                    default:
                        return "El servidor de correo rechazó el envío." + Respuesta(ex);
                }
            }
            if (ex is SmtpProtocolException)
                return "Se cortó la comunicación con el servidor de correo. Revisa host y puerto (587 con STARTTLS o 465 con SSL)." + Respuesta(ex);
            if (ex is SocketException || ex is System.IO.IOException)
                return "No se pudo conectar con el servidor de correo. Revisa la conexión a internet y el host/puerto SMTP." + Respuesta(ex);
            if (ex is NotSupportedException && detalle.Contains("auth"))
                return "El servidor de correo no ofrece un método de autenticación compatible en este puerto." + Respuesta(ex);
            return ex.Message;
        }

        private static void AgregarDirecciones(InternetAddressList lista, string? direcciones)
        {
            if (string.IsNullOrWhiteSpace(direcciones)) return;
            foreach (string raw in direcciones.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string correo = raw.Trim();
                if (correo.Length == 0) continue;
                var addr = MailboxAddress.Parse(correo);
                if (!lista.Mailboxes.Any(x => x.Address.Equals(addr.Address, StringComparison.OrdinalIgnoreCase)))
                    lista.Add(addr);
            }
        }

        private async Task EnviarCorreoInternoAsync(
            string destinatario,
            string asunto,
            string mensajeHtml,
            string? copiaCc,
            List<(string NombreArchivo, byte[] Contenido, string TipoMime)>? adjuntos)
        {
            string? smtpHostConfig = _configuration["Smtp:Host"];
            if (string.IsNullOrWhiteSpace(smtpHostConfig))
                throw new InvalidOperationException("Falta configurar Smtp:Host en el servidor.");
            string smtpHost = smtpHostConfig.Trim();
            int smtpPort = int.TryParse(_configuration["Smtp:Port"], out var puertoConfigurado) ? puertoConfigurado : 587;
            if (smtpPort is < 1 or > 65535)
                throw new InvalidOperationException("El puerto SMTP configurado es inválido.");
            string? correoRemitenteConfig = _configuration["Smtp:Sender"];
            if (string.IsNullOrWhiteSpace(correoRemitenteConfig))
                throw new InvalidOperationException("Falta configurar Smtp:Sender en el servidor.");
            string correoRemitente = correoRemitenteConfig.Trim();
            string? password = _configuration["Smtp:Password"];
            if (string.IsNullOrWhiteSpace(password) && !ModoDemo)
                throw new InvalidOperationException("Falta configurar Smtp:Password en un secreto o variable de entorno.");
            string usuarioSmtp = UsuarioSmtp(correoRemitente);
            if (string.IsNullOrWhiteSpace(usuarioSmtp) && !ModoDemo)
                throw new InvalidOperationException("Falta Smtp:Username o Smtp:Sender.");

            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress("Atilson Cargo", correoRemitente));
            mensaje.Subject = asunto;
            try
            {
                AgregarDirecciones(mensaje.To, destinatario);
                AgregarDirecciones(mensaje.Cc, copiaCc);
            }
            catch (Exception ex) when (ex is ParseException || ex is FormatException)
            {
                throw new InvalidOperationException(MensajeAmigable(ex), ex);
            }
            if (mensaje.To.Count == 0)
                throw new InvalidOperationException("El correo no tiene destinatarios válidos.");

            var cuerpo = new BodyBuilder { HtmlBody = mensajeHtml };
            if (adjuntos != null)
                foreach (var adj in adjuntos)
                    cuerpo.Attachments.Add(adj.NombreArchivo, adj.Contenido, ContentType.Parse(string.IsNullOrWhiteSpace(adj.TipoMime) ? "application/octet-stream" : adj.TipoMime));
            mensaje.Body = cuerpo.ToMessageBody();

            if (ModoDemo)
            {
                string carpeta = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "correos-demo");
                System.IO.Directory.CreateDirectory(carpeta);
                string nombre = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}.eml";
                await mensaje.WriteToAsync(System.IO.Path.Combine(carpeta, nombre));
                _logger.LogInformation("Modo demostración: correo a {Destinatario} '{Asunto}' guardado en correos-demo/{Archivo}", destinatario, asunto, nombre);
                return;
            }

            // 465 = SSL desde el inicio; cualquier otro puerto (587, 25) exige STARTTLS.
            var seguridad = smtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            // Sin consulta de revocación del certificado: muchos servidores (y redes) no exponen
            // CRL/OCSP y el envío fallaba por eso; el certificado igual se valida (nombre y cadena).
            using var cliente = new SmtpClient { Timeout = 30000, CheckCertificateRevocation = false };
            password ??= "";
            // Evitar que una conexión SMTP caída bloquee la solicitud indefinidamente.
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await cliente.ConnectAsync(smtpHost, smtpPort, seguridad, limite.Token);
                await cliente.AuthenticateAsync(usuarioSmtp, password, limite.Token);
                await cliente.SendAsync(mensaje, limite.Token);
                await cliente.DisconnectAsync(true, limite.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo a {Destinatario} con asunto '{Asunto}'", destinatario, asunto);
                // Mismo tipo de error para todos los llamadores, con mensaje entendible.
                throw new InvalidOperationException(MensajeAmigable(ex), ex);
            }
        }
    }
}
