using AtilsonCargoSpa.Pages.Operaciones;

namespace AtilsonCargoSpa.Models;

public class TransportePanelVm
{
    public required Operacione Op { get; set; }
    public OperacionesTerrestre? Terr { get; set; }
    public int? IdUnidadTecnica { get; set; }
    public required string IdSuffix { get; set; }
    public string? Label { get; set; }
    public required IOperacionesConsultaPageModel PageModel { get; set; }

    // Con tramo marítimo el Depósito de retiro vacío se define en la pestaña Marítimo
    // y aquí se muestra solo como información (oct-2026).
    public bool DepositoDesdeMaritimo { get; set; }
    public string? DepositoRetiroBooking { get; set; }
}