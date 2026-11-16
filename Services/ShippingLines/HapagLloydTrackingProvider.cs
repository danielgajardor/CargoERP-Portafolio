using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace AtilsonCargoSpa.Services.ShippingLines;

/// <summary>
/// Conector DCSA Track & Trace 2.3.3. Consulta sin modificar estados internos.
/// Tryout: 100 solicitudes/día y 1/minuto por aplicación en una sola instancia.
/// </summary>
public sealed class HapagLloydTrackingProvider(
    IHttpClientFactory httpClients,
    IConfiguration configuration,
    IMemoryCache cache) : IShippingTrackingProvider
{
    public const string HttpClientName = "HapagLloydTracking";
    public string CarrierCode => "HLCU";
    private static readonly Regex SafeReference = new("^[A-Z0-9-]{4,35}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Queue<DateTimeOffset> Calls = new();
    private static DateTimeOffset _lastCall = DateTimeOffset.MinValue;

    public async Task<ShipmentTrackingResult> GetTrackingAsync(
        ShipmentTrackingQuery query, CancellationToken cancellationToken = default)
    {
        string reference = (query.Reference ?? "").Trim().ToUpperInvariant();
        ShipmentTrackingResult Error(int status, string message) =>
            new(false, status, message, CarrierCode, reference, Array.Empty<ShipmentMilestone>());

        if (!string.Equals(query.CarrierCode, CarrierCode, StringComparison.OrdinalIgnoreCase))
            return Error(400, "La naviera no coincide con el conector Hapag-Lloyd.");
        if (!SafeReference.IsMatch(reference))
            return Error(400, "Ingresa una referencia alfanumérica válida (4 a 35 caracteres).");
        if (query.ReferenceType == ShipmentReferenceType.Container && reference.Length > 15)
            return Error(400, "La referencia de contenedor excede el máximo admitido.");

        string filter = query.ReferenceType switch
        {
            ShipmentReferenceType.Container => "equipmentReference",
            ShipmentReferenceType.Booking => "carrierBookingReference",
            ShipmentReferenceType.BillOfLading => "transportDocumentReference",
            _ => ""
        };
        if (filter.Length == 0) return Error(400, "Selecciona un tipo de búsqueda válido.");
        if (query.ReferenceType == ShipmentReferenceType.BillOfLading && reference.Length > 20)
            return Error(400, "El BL excede el máximo admitido.");

        string key = $"shipping:hlag:tracking:{filter}:{reference}";
        if (cache.TryGetValue<ShipmentTrackingResult>(key, out var previous) && previous is not null)
            return previous with { Message = "Información recuperada de caché; puede no estar actualizada." };

        string? id = configuration["HapagLloyd:ClientId"] ?? configuration["ShippingLines:HapagLloyd:ClientId"];
        string? secret = configuration["HapagLloyd:ClientSecret"] ?? configuration["ShippingLines:HapagLloyd:ClientSecret"];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            return Error(503, "Las credenciales de Hapag-Lloyd no están configuradas en el servidor.");

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<ShipmentTrackingResult>(key, out previous) && previous is not null)
                return previous with { Message = "Información recuperada de caché; puede no estar actualizada." };

            var now = DateTimeOffset.UtcNow;
            while (Calls.Count > 0 && now - Calls.Peek() >= TimeSpan.FromDays(1))
                Calls.Dequeue();
            if (Calls.Count >= 100)
                return Error(429, "Cuota Tryout de 100 consultas en 24 horas alcanzada.");
            if (now - _lastCall < TimeSpan.FromMinutes(1))
                return Error(429, "El plan Tryout permite una consulta nueva por minuto. Inténtalo después.");

            var url = $"?{filter}={Uri.EscapeDataString(reference)}&limit=100";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-IBM-Client-Id", id);
            request.Headers.Add("X-IBM-Client-Secret", secret);
            request.Headers.Add("API-Version", "2");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            Calls.Enqueue(now);
            _lastCall = now;
            using var response = await httpClients.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent ||
                response.StatusCode == HttpStatusCode.NotFound)
                return new(true, (int)response.StatusCode, "No se encontraron eventos para esta referencia.",
                    CarrierCode, reference, Array.Empty<ShipmentMilestone>(), now);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return Error(429, "Hapag-Lloyd rechazó la consulta por límite de solicitudes.");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Error((int)response.StatusCode, "Hapag-Lloyd rechazó la autenticación o los permisos.");
            if (!response.IsSuccessStatusCode)
                return Error((int)response.StatusCode, "No fue posible obtener los eventos de Hapag-Lloyd.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Error(502, "La respuesta de Track & Trace no tiene el formato esperado.");

            var events = doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(ToMilestone)
                .OrderBy(e => e.EventDateTime ?? DateTimeOffset.MaxValue)
                .ToArray();
            var result = new ShipmentTrackingResult(
                true, 200, "Eventos obtenidos de Hapag-Lloyd (API beta).",
                CarrierCode, reference, events, now);
            cache.Set(key, result, TimeSpan.FromMinutes(15));
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(504, "La consulta de seguimiento excedió el tiempo de espera.");
        }
        catch (HttpRequestException)
        {
            return Error(503, "No fue posible conectar con Track & Trace.");
        }
        catch (JsonException)
        {
            return Error(502, "La respuesta de seguimiento no contiene JSON válido.");
        }
        finally { Gate.Release(); }
    }

    private static ShipmentMilestone ToMilestone(JsonElement e)
    {
        var category = Value(e, "eventType");
        var code = category switch
        {
            "EQUIPMENT" => Value(e, "equipmentEventTypeCode"),
            "TRANSPORT" => Value(e, "transportEventTypeCode"),
            "SHIPMENT" => Value(e, "shipmentEventTypeCode"),
            _ => ""
        };
        var eventLocation = Object(e, "eventLocation");
        var transportCall = Object(e, "transportCall");
        var port = transportCall.HasValue ? Object(transportCall.Value, "location") : null;
        var location = eventLocation ?? port;
        string locationCode = location.HasValue ? Value(location.Value, "UNLocationCode") : "";
        if (locationCode.Length == 0 && transportCall.HasValue)
            locationCode = Value(transportCall.Value, "UNLocationCode");
        string locationName = location.HasValue ? Value(location.Value, "locationName") : "";
        return new ShipmentMilestone(
            code, Label(category, code), locationCode, locationName,
            Date(e, "eventDateTime"), Value(e, "eventClassifierCode"));
    }

    private static string Label(string category, string code) => (category, code) switch
    {
        ("TRANSPORT", "ARRI") => "Llegada del transporte",
        ("TRANSPORT", "DEPA") => "Salida del transporte",
        ("EQUIPMENT", "LOAD") => "Contenedor cargado al transporte",
        ("EQUIPMENT", "DISC") => "Contenedor descargado",
        ("EQUIPMENT", "GTIN") => "Ingreso a terminal",
        ("EQUIPMENT", "GTOT") => "Salida de terminal",
        ("EQUIPMENT", "STUF") => "Contenedor llenado",
        ("EQUIPMENT", "STRP") => "Contenedor vaciado",
        ("SHIPMENT", "ISSU") => "Documento emitido",
        ("SHIPMENT", "CONF") => "Documento confirmado",
        _ => string.IsNullOrWhiteSpace(code) ? category : $"{category}: {code}"
    };

    private static JsonElement? Object(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Object ? v : null;
    private static string Value(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static DateTimeOffset? Date(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Value(e, name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt : null;
}
