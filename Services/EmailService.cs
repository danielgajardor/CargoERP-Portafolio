using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Services
{
    public class EmailService
    {
        public async Task EnviarCorreoAsync(
            string destinatario,
            string asunto,
            string mensajeHtml,
            List<(string NombreArchivo, byte[] Contenido, string TipoMime)>? adjuntos = null)
        {
            // CONFIGURACIÓN SMTP LOCAL (smtp4dev)
            string smtpHost = "localhost";
            int smtpPort = 25; // smtp4dev (servidor de correo falso local)

            string correoRemitente = "operaciones@cargoflow-demo.cl";

            using (var client = new SmtpClient(smtpHost, smtpPort))
            {
                client.UseDefaultCredentials = false;
                client.EnableSsl = false;

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(correoRemitente, "Operaciones CargoFlow"),
                    Subject = asunto,
                    Body = mensajeHtml,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(destinatario);

                var streamsParaCerrar = new List<MemoryStream>();
                if (adjuntos != null)
                {
                    foreach (var adj in adjuntos)
                    {
                        var stream = new MemoryStream(adj.Contenido);
                        streamsParaCerrar.Add(stream);
                        mailMessage.Attachments.Add(new Attachment(stream, adj.NombreArchivo, adj.TipoMime));
                    }
                }

                try
                {
                    await client.SendMailAsync(mailMessage);
                }
                finally
                {
                    foreach (var s in streamsParaCerrar) s.Dispose();
                }
            }
        }
    }
}