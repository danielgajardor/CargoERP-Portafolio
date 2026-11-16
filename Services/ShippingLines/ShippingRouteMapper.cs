using System.Globalization;
using System.Text.Json;

namespace AtilsonCargoSpa.Services.ShippingLines;

// Modelo neutral para mostrar itinerarios de cualquier naviera.
// Se conserva el contrato JSON de origen para diagnóstico, pero las vistas usan estos DTO.
public sealed record ShippingRouteView(
    string Origin, string Destination, DateTimeOffset? Departure,
    DateTimeOffset? Arrival, string TransitTime, string Transhipments,
    string VesselNames, IReadOnlyList<ShippingLegView> Legs, int? TranshipmentCount, int? SolutionNumber);

public sealed record ShippingLegView(
    int Sequence, string Mode, string Origin, string Destination,
    DateTimeOffset? Departure, DateTimeOffset? Arrival, string VesselName);

public static class ShippingRouteMapper
{
    public static IReadOnlyList<ShippingRouteView> FromDcsa(JsonElement routes)
    {
        if (routes.ValueKind != JsonValueKind.Array)
            return Array.Empty<ShippingRouteView>();

        var result = new List<ShippingRouteView>();
        foreach (var route in routes.EnumerateArray())
        {
            if (route.ValueKind != JsonValueKind.Object) continue;
            var legs = new List<ShippingLegView>();
            if (Property(route, "legs") is JsonElement rawLegs && rawLegs.ValueKind == JsonValueKind.Array)
            {
                foreach (var leg in rawLegs.EnumerateArray())
                {
                    if (leg.ValueKind != JsonValueKind.Object) continue;
                    var transport = Property(leg, "transport");
                    var vessel = transport.HasValue ? Property(transport.Value, "vessel") : null;
                    legs.Add(new(
                        Number(leg, "sequenceNumber") ?? (legs.Count + 1),
                        transport.HasValue ? Text(transport.Value, "modeOfTransport") : "",
                        Place(leg, "departure"), Place(leg, "arrival"),
                        Date(leg, "departure"), Date(leg, "arrival"),
                        vessel.HasValue ? Text(vessel.Value, "name") : ""));
                }
            }

            var names = string.Join(", ", legs.Select(x => x.VesselName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            // Número de cambios de buque entre tramos marítimos consecutivos.
            // No confundir tramos con transbordos: servicios terrestres o una misma nave
            // en varios tramos no constituyen necesariamente un transbordo.
            int? transhipmentCount = null;
            if (legs.Count > 0 && legs.All(x => x.Mode == "VESSEL"
                && !string.IsNullOrWhiteSpace(x.VesselName)))
                transhipmentCount = legs.Zip(legs.Skip(1),
                    (a, b) => string.Equals(a.VesselName, b.VesselName,
                        StringComparison.OrdinalIgnoreCase) ? 0 : 1).Sum();
            var transhipments = transhipmentCount.HasValue
                ? (transhipmentCount.Value == 0 ? "Directo" : $"{transhipmentCount.Value} transbordo(s)")
                : "Sin confirmar";
            result.Add(new(
                Place(route, "placeOfReceipt"), Place(route, "placeOfDelivery"),
                Date(route, "placeOfReceipt"), Date(route, "placeOfDelivery"),
                Transit(route),
                transhipments, names, legs, transhipmentCount, Number(route, "solutionNumber")));
        }
        return result;
    }

    private static string Transit(JsonElement route)
    {
        // DCSA: transitTime es un entero que representa días, no una cadena.
        var reported = Number(route, "transitTime");
        if (reported is >= 0)
            return $"{reported.Value} días";

        // Si falta el dato oficial, estimar a partir de las fechas con zona horaria.
        var departure = Date(route, "placeOfReceipt");
        var arrival = Date(route, "placeOfDelivery");
        if (departure.HasValue && arrival.HasValue && arrival > departure)
        {
            var days = (arrival.Value - departure.Value).TotalDays;
            return $"≈ {days:0.#} días (calculado)";
        }
        return "No informado";
    }

    private static JsonElement? Property(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) ? value : null;

    private static string Text(JsonElement obj, string name)
    {
        var value = Property(obj, name);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.String
            ? value.Value.GetString() ?? "" : "";
    }

    private static int? Number(JsonElement obj, string name)
    {
        var value = Property(obj, name);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Number
            && value.Value.TryGetInt32(out var n) ? n : null;
    }

    private static string Place(JsonElement obj, string name)
    {
        var place = Property(obj, name);
        if (!place.HasValue) return "No informado";
        var location = Property(place.Value, "location");
        if (!location.HasValue) return "No informado";
        var city = Text(location.Value, "locationName");
        var code = Text(location.Value, "UNLocationCode");
        if (string.IsNullOrEmpty(city)) return string.IsNullOrEmpty(code) ? "No informado" : code;
        return string.IsNullOrEmpty(code) ? city : $"{city} ({code})";
    }

    private static DateTimeOffset? Date(JsonElement obj, string name)
    {
        var place = Property(obj, name);
        if (!place.HasValue) return null;
        var value = Text(place.Value, "dateTime");
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt : null;
    }
}
