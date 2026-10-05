using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        public CobranzaRecordatorioService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await ProcesarRecordatoriosAsync(); }
                catch { /* un fallo puntual no debe detener el servicio */ }

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

            foreach (var cliente in clientesSuscritos)
            {
                var ultimoEnvio = await db.RecordatoriosCobranza
                    .Where(r => r.IdCliente == cliente.Id)
                    .OrderByDescending(r => r.FechaEnvio)
                    .Select(r => (DateTime?)r.FechaEnvio)
                    .FirstOrDefaultAsync();

                if (ultimoEnvio.HasValue && (DateTime.Now - ultimoEnvio.Value).TotalDays < 7) continue;

                // Un fallo puntual (SMTP caído, cliente sin saldo esta semana, etc.)
                // no debe cancelar el resto del lote — se reintenta en el próximo ciclo.
                try
                {
                    await cobranzaModel.EnviarRecordatorioClienteAsync(cliente, "AUTOMATICO", null);
                }
                catch
                {
                    continue;
                }
            }
        }
    }
}