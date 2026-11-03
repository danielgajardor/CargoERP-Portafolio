using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    /// <summary>
    /// Mantiene al día el dólar observado, la UF y el euro (nov-2026). Al iniciar el sistema y
    /// cada 2 horas consulta las fuentes con más tiempo de espera que una página, así las
    /// pantallas (Control Financiero, sincronización de Finanzas, sitio web) toman el valor
    /// ya cargado y no dependen de que internet responda justo en ese momento.
    /// Con token del Banco Central, mientras no se obtenga su valor del día se reintenta cada
    /// 10 minutos (nov-2026).
    /// </summary>
    public class IndicadoresActualizador : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<IndicadoresActualizador> _logger;

        public IndicadoresActualizador(IServiceScopeFactory scopes, ILogger<IndicadoresActualizador> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { return; }
            while (!ct.IsCancellationRequested)
            {
                bool todoOk = true;
                try
                {
                    using var scope = _scopes.CreateScope();
                    var ind = scope.ServiceProvider.GetRequiredService<IndicadoresEconomicosService>();
                    foreach (var codigo in new[] { IndicadoresEconomicosService.Dolar, IndicadoresEconomicosService.Uf, IndicadoresEconomicosService.Euro })
                    {
                        var v = await ind.ActualizarAsync(codigo);
                        // Con token del Banco Central, solo su valor recién obtenido cuenta como al día.
                        bool definitivo = ind.EsDefinitivo(codigo);
                        if (!definitivo) todoOk = false;
                        _logger.LogInformation("Indicador {Codigo}: {Valor} del {Fecha:dd-MM-yyyy} ({Fuente}{Provisorio})", codigo, v.Valor, v.Fecha, v.Fuente, definitivo ? "" : ", provisorio");
                    }
                }
                catch (Exception ex) { todoOk = false; _logger.LogWarning(ex, "No se pudieron actualizar los indicadores."); }

                // Si algo falló se reintenta en 10 minutos; si no, en 2 horas.
                try { await Task.Delay(todoOk ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(10), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
