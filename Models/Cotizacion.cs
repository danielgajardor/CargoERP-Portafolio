using System;
using System.ComponentModel.DataAnnotations.Schema;


namespace AtilsonCargoSpa.Models
{
    public partial class Cotizacion
    {
        public int Id { get; set; }
        public int? IdCliente { get; set; }
        public string TipoServicio { get; set; } = null!;
        public string? TipoCarga { get; set; }
        public string? Origen { get; set; }
        public string? Destino { get; set; }
        public string? Mercancia { get; set; }
        public string? Comentarios { get; set; }

        // --- Transporte Nacional ---
        public string? PlantaCarga { get; set; }
        public string? DepositoRetiro { get; set; }
        public string? ZonaCarga { get; set; }
        public DateTime? FechaCarga { get; set; }

        // --- Marítimo ---
        public int? CantidadContenedores { get; set; }
        public DateTime? FechaTentativaEmbarque { get; set; }
        public bool? RequiereSeguro { get; set; }

        // --- Contenedor / Reefer (oct-2026, pedido de Cristián: la cotización
        // inicial debe exigir los datos que determinan cantidad y tipo de
        // contenedores) — mismo vocabulario que Operaciones/Create para que
        // la precarga automática al aprobar la cotización pueda reconocerlos
        // sin traducir texto libre. Columnas agregadas vía
        // Sql/2026-10_cotizacion_contenedor_reefer.sql.
        public string? TipoContenedor { get; set; }
        public string? CondicionReefer { get; set; }
        public decimal? Temperatura { get; set; }
        public int? Humedad { get; set; }
        public string? Ventilacion { get; set; }

        // --- Documental ---
        public string? TipoCertificado { get; set; }
        public string? PaisDestinoFinal { get; set; }
        public bool? RequiereSag { get; set; }
        public bool? RequiereSernapesca { get; set; }

        public string? Estado { get; set; }
        public DateTime? FechaSolicitud { get; set; }
        public bool? Activo { get; set; }

        // --- Cotización real vs. Solicitud de Reserva (oct-2026, minuta Portal
        // Web de Clientes). Misma tabla para ambos flujos: el panel de
        // notificaciones de Operaciones y la precarga de Create ya leen de aquí.
        // NULL se trata como RESERVA (filas anteriores al cambio). Columnas
        // agregadas vía Sql/2026-10_cotizacion_real_respuesta.sql.
        public const string TipoCotizacion = "COTIZACION";
        public const string TipoReserva = "RESERVA";
        public const string EstadoRespondida = "RESPONDIDA";
        public const string EstadoConvertida = "CONVERTIDA"; // reserva que ya se creó como operación en Create

        public string? TipoSolicitud { get; set; }

        [NotMapped]
        public bool EsCotizacion => TipoSolicitud == TipoCotizacion;

        public int? IdPuertoOrigen { get; set; }
        public int? IdPuertoDestino { get; set; }
        public string? ZonaEntrega { get; set; }
        public string? TipoCargaDocumental { get; set; }

        // --- Respuesta del ejecutivo (Operaciones/Cotizaciones) ---
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? MontoMaritimo { get; set; }
        public string? MonedaMaritimo { get; set; }

        public int? IdPuertoTerrestre { get; set; }
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? MontoTerrestre { get; set; }
        public string? MonedaTerrestre { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? MontoDocumental { get; set; }
        public string? MonedaDocumental { get; set; }

        public string? ComentarioRespuesta { get; set; }
        public DateOnly? ValidaHasta { get; set; }
        public DateTime? FechaRespuesta { get; set; }
        public string? UsuarioRespuesta { get; set; }

        // --- Solicitud de Reserva completa (Fase 3, oct-2026) ---
        // Columnas agregadas vía Sql/2026-10_itinerarios_y_reserva.sql.
        public int? IdItinerario { get; set; }        // salida elegida en el portal
        public int? IdNaviera { get; set; }           // del itinerario, o informada por el cliente (Ter/Doc)
        public string? Nave { get; set; }
        public string? NumeroBooking { get; set; }    // booking que el cliente ya tiene (Ter/Doc)
        public string? NumeroInstructivo { get; set; }
        public DateTime? FechaCorteDocumental { get; set; }
        public int? IdCotizacionOrigen { get; set; }  // cotización respondida que originó la reserva

        [ForeignKey("IdItinerario")]
        public virtual ItinerarioNave? IdItinerarioNavigation { get; set; }

        [ForeignKey("IdNaviera")]
        public virtual Naviera? IdNavieraNavigation { get; set; }

        // El portal bindea la entidad completa ([BindProperty] Cotizacion), así
        // que un cliente podría postear a mano campos que solo debe escribir el
        // ejecutivo. Los dos formularios del portal llaman esto antes de guardar.
        public void LimpiarCamposRespuesta()
        {
            MontoMaritimo = null; MonedaMaritimo = null;
            IdPuertoTerrestre = null; MontoTerrestre = null; MonedaTerrestre = null;
            MontoDocumental = null; MonedaDocumental = null;
            ComentarioRespuesta = null; ValidaHasta = null;
            FechaRespuesta = null; UsuarioRespuesta = null;
        }

        [ForeignKey("IdCliente")]
        public virtual Cliente? IdClienteNavigation { get; set; }

        [ForeignKey("IdPuertoOrigen")]
        public virtual Puerto? IdPuertoOrigenNavigation { get; set; }

        [ForeignKey("IdPuertoDestino")]
        public virtual Puerto? IdPuertoDestinoNavigation { get; set; }

        [ForeignKey("IdPuertoTerrestre")]
        public virtual Puerto? IdPuertoTerrestreNavigation { get; set; }
    }
}