using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Pages.Finanzas;

namespace AtilsonCargoSpa.Services
{
    // Recorre solo los clientes con SuscripcionCobranzaActiva = true (activada
    // manualmente por Finanzas desde /Finanzas/Cobranza) y les envía el
    // recordatorio con adjuntos PDF/Excel una vez por semana, hasta que su
    // saldo llegue a 0 o se desactive la suscripción.
    public class CobranzaRecordatorioService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CobranzaRecordatorioService> _logger;

        public CobranzaRecordatorioService(IServiceScopeFactory scopeFactory, ILogger<CobranzaRecordatorioService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await ProcesarRecordatoriosAsync(); }
                catch (Exception ex) { _logger.LogError(ex, "Error no controlado procesando recordatorios de cobranza; se reintenta en el próximo ciclo."); }

                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task ProcesarRecordatoriosAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtilsonContext>();

            var clientesSuscritos = await db.Clientes
                .Where(c => c.SuscripcionCobranzaActiva && c.Correo != null)
                .ToListAsync();

            if (!clientesSuscritos.Any()) return;

            // CobranzaModel expone la lógica compartida de armado del correo +
            // adjuntos para no duplicarla entre el envío manual y el automático.
            var cobranzaModel = ActivatorUtilities.CreateInstance<CobranzaModel>(scope.ServiceProvider);

            // Una sola consulta agrupada para el último envío de TODOS los clientes
            // suscritos, en vez de una consulta por cliente dentro del loop (N+1).
            var idsClientesSuscritos = clientesSuscritos.Select(c => c.Id).ToList();
            var ultimosEnvios = await db.RecordatoriosCobranza
                .Where(r => idsClientesSuscritos.Contains(r.IdCliente))
                .GroupBy(r => r.IdCliente)
                .Select(g => new { IdCliente = g.Key, UltimoEnvio = g.Max(r => r.FechaEnvio) })
                .ToListAsync();
            var ultimoEnvioPorCliente = ultimosEnvios.ToDictionary(x => x.IdCliente, x => x.UltimoEnvio);

            foreach (var cliente in clientesSuscritos)
            {
                DateTime? ultimoEnvio = ultimoEnvioPorCliente.TryGetValue(cliente.Id, out var fechaUltimoEnvio) ? fechaUltimoEnvio : null;

                if (ultimoEnvio.HasValue && (DateTime.Now - ultimoEnvio.Value).TotalDays < 7) continue;

                // Un fallo puntual (SMTP caído, cliente sin saldo esta semana, etc.)
                // no debe cancelar el resto del lote — se reintenta en el próximo ciclo.
                try
                {
                    await cobranzaModel.EnviarRecordatorioClienteAsync(cliente, "AUTOMATICO", null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo enviar el recordatorio de cobranza automático al cliente {IdCliente}.", cliente.Id);
                    continue;
                }
            }
        }
    }
}