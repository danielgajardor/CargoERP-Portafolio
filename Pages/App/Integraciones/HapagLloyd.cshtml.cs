using AtilsonCargoSpa.Services.ShippingLines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace AtilsonCargoSpa.Pages.App.Integraciones;

public class HapagLloydModel(IShippingScheduleProvider schedules) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Origen { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Destino { get; set; } = "";
    [BindProperty(SupportsGet = true)] public DateOnly? Desde { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Hasta { get; set; }
    [BindProperty(SupportsGet = true)] public int? MaxTransbordos { get; set; }
    [BindProperty(SupportsGet = true)] public string? ServicioOrigen { get; set; }
    [BindProperty(SupportsGet = true)] public string? ServicioDestino { get; set; }
    [BindProperty(SupportsGet = true)] public string Orden { get; set; } = "salida";
    public ShippingScheduleResult? Resultado { get; private set; }
    public string? JsonResultado { get; private set; }
    public IReadOnlyList<ShippingRouteView> Itinerarios { get; private set; } = Array.Empty<ShippingRouteView>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Evita consumir cuota cuando se abre la página sin parámetros.
        if (string.IsNullOrWhiteSpace(Origen) && string.IsNullOrWhiteSpace(Destino))
            return;

        Resultado = await schedules.FindRoutesAsync(
            new ShippingScheduleQuery(Origen, Destino, Desde, Hasta, MaxTransbordos,
                ServicioOrigen, ServicioDestino), cancellationToken);
        if (Resultado.Success && Resultado.Routes is JsonElement element)
        {
            var rows = ShippingRouteMapper.FromDcsa(element);
            Itinerarios = Orden switch
            {
                "llegada" => rows.OrderBy(x => x.Arrival ?? DateTimeOffset.MaxValue).ToArray(),
                "transito" => rows.OrderBy(x => x.Departure.HasValue && x.Arrival.HasValue
                    ? (x.Arrival.Value - x.Departure.Value).TotalHours : double.MaxValue).ToArray(),
                "transbordos" => rows.OrderBy(x => x.TranshipmentCount ?? int.MaxValue).ToArray(),
                _ => rows.OrderBy(x => x.Departure ?? DateTimeOffset.MaxValue).ToArray()
            };
            JsonResultado = JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
