using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace AtilsonCargoSpa.Services.ShippingLines;

// Contrato común para navieras: Hapag-Lloyd es el primer proveedor.
public interface IShippingScheduleProvider
{
    string CarrierCode { get; }
    Task<ShippingScheduleResult> FindRoutesAsync(string origin, string destination, DateOnly? earliestDeparture = null, CancellationToken cancellationToken = default);
    Task<ShippingScheduleResult> FindRoutesAsync(ShippingScheduleQuery query, CancellationToken cancellationToken = default);
}

public sealed record ShippingScheduleQuery(
    string Origin, string Destination, DateOnly? DepartureStart = null,
    DateOnly? DepartureEnd = null, int? MaxTranshipment = null,
    string? ReceiptType = null, string? DeliveryType = null);

public sealed record ShippingScheduleResult(
    bool Success, int StatusCode, string Message, JsonElement? Routes,
    bool FromCache = false);

// No incluir Client ID ni Client Secret en appsettings.json ni en el repositorio.
// Configurar ShippingLines__HapagLloyd__ClientId y __ClientSecret en el servidor.
public sealed class HapagLloydScheduleProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IMemoryCache cache) : IShippingScheduleProvider
{
    public const string HttpClientName = "HapagLloydCommercialSchedule";
    private static bool ValidService(string? s) => string.IsNullOrEmpty(s) || s is "CY" or "SD" or "CFS";
    public string CarrierCode => "HLCU";
    private static readonly Regex Unlocode = new("^[A-Z]{2}[A-Z2-9]{3}$", RegexOptions.Compiled);
    // El plan Tryout limita 50/hora y 1/segundo. Un solo proceso: usar limitador distribuido
    // si ATILSON se despliega en múltiples instancias.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Queue<DateTimeOffset> CallTimes = new();
    private static DateTimeOffset _lastCall = DateTimeOffset.MinValue;

    public Task<ShippingScheduleResult> FindRoutesAsync(
        string origin, string destination, DateOnly? earliestDeparture = null,
        CancellationToken cancellationToken = default)
        => FindRoutesAsync(new ShippingScheduleQuery(origin, destination, earliestDeparture), cancellationToken);

    public async Task<ShippingScheduleResult> FindRoutesAsync(
        ShippingScheduleQuery filters, CancellationToken cancellationToken = default)
    {
        string origin = filters.Origin;
        string destination = filters.Destination;
        origin = origin?.Trim().ToUpperInvariant() ?? "";
        destination = destination?.Trim().ToUpperInvariant() ?? "";
        if (!Unlocode.IsMatch(origin) || !Unlocode.IsMatch(destination))
            return new(false, 400, "Origen y destino deben tener un código UN/LOCODE válido de 5 caracteres.", null);

        if (filters.DepartureStart > filters.DepartureEnd)
            return new(false, 400, "La fecha inicial no puede superar la fecha final.", null);
        if (filters.MaxTranshipment is < 0 or > 10)
            return new(false, 400, "El máximo de transbordos debe estar entre 0 y 10.", null);
        if (!ValidService(filters.ReceiptType) || !ValidService(filters.DeliveryType))
            return new(false, 400, "El tipo de servicio seleccionado no es válido.", null);
        string cacheKey = $"shipping:hlag:schedule:{origin}:{destination}:{filters.DepartureStart:yyyy-MM-dd}:{filters.DepartureEnd:yyyy-MM-dd}:{filters.MaxTranshipment}:{filters.ReceiptType}:{filters.DeliveryType}";
        if (cache.TryGetValue<ShippingScheduleResult>(cacheKey, out var cached) && cached is not null)
            return cached with { FromCache = true };

        string? id = configuration["HapagLloyd:ClientId"] ?? configuration["ShippingLines:HapagLloyd:ClientId"];
        string? secret = configuration["HapagLloyd:ClientSecret"] ?? configuration["ShippingLines:HapagLloyd:ClientSecret"];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            return new(false, 503, "Faltan credenciales de Hapag-Lloyd en la configuración segura.", null);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<ShippingScheduleResult>(cacheKey, out cached) && cached is not null)
                return cached with { FromCache = true };

            var now = DateTimeOffset.UtcNow;
            while (CallTimes.Count > 0 && now - CallTimes.Peek() >= TimeSpan.FromHours(1))
                CallTimes.Dequeue();

            if (CallTimes.Count >= 50 || (now - _lastCall) < TimeSpan.FromSeconds(1))
                return new(false, 429, "Límite Tryout: espera antes de realizar otra consulta.", null);

            var query = $"point-to-point-routes?placeOfReceipt={Uri.EscapeDataString(origin)}&placeOfDelivery={Uri.EscapeDataString(destination)}";
            if (filters.DepartureStart is not null)
                query += $"&departureStartDate={filters.DepartureStart.Value:yyyy-MM-dd}";
            if (filters.DepartureEnd is not null)
                query += $"&departureEndDate={filters.DepartureEnd.Value:yyyy-MM-dd}";
            if (filters.MaxTranshipment is not null)
                query += $"&maxTranshipment={filters.MaxTranshipment.Value}";
            if (!string.IsNullOrEmpty(filters.ReceiptType))
                query += $"&receiptTypeAtOrigin={filters.ReceiptType}";
            if (!string.IsNullOrEmpty(filters.DeliveryType))
                query += $"&deliveryTypeAtDestination={filters.DeliveryType}";
            query += "&limit=15";
            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Add("X-IBM-Client-Id", id);
            request.Headers.Add("X-IBM-Client-Secret", secret);
            request.Headers.Add("API-Version", "1");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Contabilizar antes de enviar: también cuentan intentos que terminan en error.
            CallTimes.Enqueue(now);
            _lastCall = now;
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new(false, 429, "Hapag-Lloyd indicó que se superó el límite de consultas. Intenta más tarde.", null);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(false, (int)response.StatusCode, "Hapag-Lloyd rechazó las credenciales o el permiso de acceso.", null);
            if (!response.IsSuccessStatusCode)
                return new(false, (int)response.StatusCode, "No fue posible consultar itinerarios en Hapag-Lloyd.", null);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return new(false, 502, "La respuesta de Hapag-Lloyd no tiene el formato esperado.", null);
            var result = new ShippingScheduleResult(true, 200, "Consulta realizada correctamente.",
                document.RootElement.Clone());
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, 504, "La consulta a Hapag-Lloyd excedió el tiempo de espera.", null);
        }
        catch (HttpRequestException)
        {
            return new(false, 503, "No hay conexión disponible con Hapag-Lloyd.", null);
        }
        catch (JsonException)
        {
            return new(false, 502, "Hapag-Lloyd devolvió una respuesta que no se pudo interpretar.", null);
        }
        finally { Gate.Release(); }
    }
}
