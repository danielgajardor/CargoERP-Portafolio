using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    // =====================================================================================
    // Resumen por correo de los avisos del portal (15-nov-2026).
    // Cada 2 minutos junta los avisos pendientes de correo de cada cliente (los que tienen al
    // menos 2 minutos, para agrupar cambios seguidos: p. ej. el stacking de 15 bookings) y envía
    // UN correo por usuario del portal con los tipos que tiene activos en sus preferencias.
    // Avisos de más de 2 días sin enviar (SMTP caído mucho tiempo) se omiten: ya están en la
    // campana del portal. Sin SMTP configurado no se marca nada (se envían cuando se configure).
    // =====================================================================================
    public sealed class NotificacionesCorreoWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<NotificacionesCorreoWorker> _log;
        public static readonly TimeSpan Espera = TimeSpan.FromMinutes(2);

        public NotificacionesCorreoWorker(IServiceScopeFactory scopes, ILogger<NotificacionesCorreoWorker> log)
        {
            _scopes = scopes; _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await EnviarPendientesAsync(stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException || ex.InnerException is Microsoft.Data.SqlClient.SqlException)
                {
                    _log.LogWarning("Correo de avisos del portal: {m}", ex.Message);
                }
                catch (Exception ex) { _log.LogWarning(ex, "Correo de avisos del portal: error inesperado."); }
                try { await Task.Delay(Espera, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }

        public async Task<int> EnviarPendientesAsync(CancellationToken ct = default, bool sinEspera = false)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtilsonContext>();
            var email = scope.ServiceProvider.GetRequiredService<EmailService>();
            var avisos = scope.ServiceProvider.GetRequiredService<NotificacionesClienteService>();

            if (!email.DiagnosticarConfiguracion().Valido) return 0;

            var ahora = DateTime.Now;
            var limite = sinEspera ? ahora : ahora - Espera;
            var pendientes = await db.NotificacionesCliente
                .Where(n => n.CorreoEstado == NotificacionCliente.CorreoPendiente && n.Fecha <= limite)
                .OrderBy(n => n.Fecha).Take(500).ToListAsync(ct);
            if (pendientes.Count == 0) return 0;

            // Muy antiguos: solo campana.
            foreach (var n in pendientes.Where(n => n.Fecha < ahora.AddDays(-2)))
            {
                n.CorreoEstado = NotificacionCliente.CorreoOmitido; n.FechaCorreo = ahora;
            }

            int enviados = 0;
            foreach (var grupo in pendientes.Where(n => n.CorreoEstado == NotificacionCliente.CorreoPendiente).GroupBy(n => n.IdCliente))
            {
                var usuarios = await db.Usuarios.AsNoTracking()
                    .Where(u => u.IdCliente == grupo.Key && u.Rol == "Cliente" && (u.Activo == null || u.Activo == 1))
                    .Select(u => new { u.Correo, u.NombreCompleto }).ToListAsync(ct);
                var correos = usuarios.Select(u => u.Correo).ToList();
                var prefs = await db.PreferenciasAvisoCliente.AsNoTracking().Where(p => correos.Contains(p.Correo)).ToListAsync(ct);

                bool algunoFallo = false;
                foreach (var u in usuarios.Where(u => !string.IsNullOrWhiteSpace(u.Correo) && u.Correo.Contains('@')))
                {
                    var tipos = NotificacionesClienteService.TiposDe(prefs.FirstOrDefault(p => string.Equals(p.Correo, u.Correo, StringComparison.OrdinalIgnoreCase)));
                    var suyos = grupo.Where(n => tipos.Contains(n.Tipo)).OrderByDescending(n => n.Fecha).ToList();
                    if (suyos.Count == 0) continue;
                    var (asunto, html) = avisos.CorreoResumen(string.IsNullOrWhiteSpace(u.NombreCompleto) ? "cliente" : u.NombreCompleto, suyos);
                    try
                    {
                        await email.EnviarCorreoAsync(u.Correo.Trim(), asunto, html);
                        enviados++;
                    }
                    catch (Exception ex)
                    {
                        algunoFallo = true;
                        _log.LogWarning("Correo de avisos a un usuario del cliente {c} no enviado: {m}", grupo.Key, ex.Message);
                    }
                }
                // Si el servidor rechazó el envío, se reintenta en la próxima vuelta (máx. 2 días).
                if (algunoFallo) continue;
                foreach (var n in grupo)
                {
                    n.CorreoEstado = usuarios.Count == 0 ? NotificacionCliente.CorreoOmitido : NotificacionCliente.CorreoEnviado;
                    n.FechaCorreo = ahora;
                }
            }
            await db.SaveChangesAsync(ct);
            return enviados;
        }
    }
}
