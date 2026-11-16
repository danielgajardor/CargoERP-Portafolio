namespace AtilsonCargoSpa.Services.ShippingLines;

// Contrato agnóstico a naviera para la futura integración Track & Trace.
// No asume endpoints ni nombres de propiedades de Hapag-Lloyd hasta revisar su OpenAPI.
public enum ShipmentReferenceType
{
    Booking,
    Container,
    BillOfLading
}

public sealed record ShipmentTrackingQuery(
    string CarrierCode,
    ShipmentReferenceType ReferenceType,
    string Reference);

public sealed record ShipmentMilestone(
    string? EventCode,
    string? Description,
    string? LocationCode,
    string? LocationName,
    DateTimeOffset? EventDateTime,
    string? Classifier);

public sealed record ShipmentTrackingResult(
    bool Success,
    int StatusCode,
    string Message,
    string CarrierCode,
    string Reference,
    IReadOnlyList<ShipmentMilestone> Events,
    DateTimeOffset? RetrievedAt = null);

public interface IShippingTrackingProvider
{
    string CarrierCode { get; }
    Task<ShipmentTrackingResult> GetTrackingAsync(
        ShipmentTrackingQuery query,
        CancellationToken cancellationToken = default);
}
