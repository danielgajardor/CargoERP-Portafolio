using AtilsonCargoSpa.Services.ShippingLines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtilsonCargoSpa.Pages.App.Integraciones;

public class TrackAndTraceModel(IShippingTrackingProvider tracking) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public ShipmentReferenceType Tipo { get; set; } = ShipmentReferenceType.Container;

    [BindProperty(SupportsGet = true)]
    public string Referencia { get; set; } = "";

    public ShipmentTrackingResult? Resultado { get; private set; }

    public async Task OnGetAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Referencia))
            return;
        Resultado = await tracking.GetTrackingAsync(
            new ShipmentTrackingQuery("HLCU", Tipo, Referencia), token);
    }
}
