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
}