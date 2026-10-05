using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Services
{
    /// <summary>
    /// Obtiene UF y Dólar Observado en tiempo real (mindicador.cl), con caché
    /// en memoria por día calendario para no golpear la API en cada sync.
    /// </summary>
    public class IndicadoresEconomicosService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        private static decimal? _ufCache;
        private static DateTime _ufCacheFecha;
        private static decimal? _dolarCache;
        private static DateTime _dolarCacheFecha;
        private static readonly object _lock = new();

        // Respaldo si la API externa no responde, para que el ERP nunca se
        // caiga por falta de internet. Actualízalos manualmente si falla seguido.
        private const decimal UF_RESPALDO = 37750m;
        private const decimal DOLAR_RESPALDO = 950m;

        public IndicadoresEconomicosService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<decimal> ObtenerValorUfAsync()
        {
            lock (_lock)
            {
                if (_ufCache.HasValue && _ufCacheFecha.Date == DateTime.Now.Date)
                    return _ufCache.Value;
            }
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var json = await client.GetStringAsync("https://mindicador.cl/api/uf");
                using var doc = JsonDocument.Parse(json);
                var valor = doc.RootElement.GetProperty("serie")[0].GetProperty("valor").GetDecimal();
                lock (_lock) { _ufCache = valor; _ufCacheFecha = DateTime.Now; }
                return valor;
            }
            catch
            {
                return _ufCache ?? UF_RESPALDO;
            }
        }

        public async Task<decimal> ObtenerValorDolarAsync()
        {
            lock (_lock)
            {
                if (_dolarCache.HasValue && _dolarCacheFecha.Date == DateTime.Now.Date)
                    return _dolarCache.Value;
            }
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var json = await client.GetStringAsync("https://mindicador.cl/api/dolar");
                using var doc = JsonDocument.Parse(json);
                var valor = doc.RootElement.GetProperty("serie")[0].GetProperty("valor").GetDecimal();
                lock (_lock) { _dolarCache = valor; _dolarCacheFecha = DateTime.Now; }
                return valor;
            }
            catch
            {
                return _dolarCache ?? DOLAR_RESPALDO;
            }
        }
    }
}