namespace AtilsonCargoSpa.Models
{
    // Modelo del partial PortalCliente/_SalidaCard (tarjeta tipo buscador de
    // vuelos). Modo enlace: AccionUrl + AccionTexto. Modo selector (Solicitar
    // Reserva): RadioName != null, la tarjeta completa es un <label> con radio.
    public class SalidaCardVm
    {
        public ItinerarioNave Salida { get; set; } = null!;
        public string? AccionUrl { get; set; }
        public string? AccionTexto { get; set; }
        public string? RadioName { get; set; }
        public bool Seleccionada { get; set; }
        public bool Compacta { get; set; }
    }
}
