using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Salida de nave cargada a mano por Operaciones (oct-2026, minuta Portal Web
    // de Clientes): mientras no haya API de navieras, las ejecutivas ingresan las
    // salidas de las próximas 4-5 semanas y el cliente las ve en el portal como
    // tarjetas tipo buscador de vuelos, para elegir nave al solicitar la reserva.
    // Tabla creada por Sql/2026-10_itinerarios_y_reserva.sql.
    [Table("ItinerariosNaves")]
    public class ItinerarioNave
    {
        public int Id { get; set; }
        public int IdNaviera { get; set; }
        public string Nave { get; set; } = null!;
        public string? NumeroViaje { get; set; }
        public int IdPuertoOrigen { get; set; }
        public int IdPuertoDestino { get; set; }
        public DateOnly Etd { get; set; }
        public DateOnly Eta { get; set; }
        public DateTime? CorteDocumental { get; set; }
        public string? Transbordo { get; set; }
        public string? Observaciones { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreador { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string? UsuarioModificador { get; set; }

        [ForeignKey("IdNaviera")]
        public virtual Naviera? IdNavieraNavigation { get; set; }

        [ForeignKey("IdPuertoOrigen")]
        public virtual Puerto? IdPuertoOrigenNavigation { get; set; }

        [ForeignKey("IdPuertoDestino")]
        public virtual Puerto? IdPuertoDestinoNavigation { get; set; }

        [NotMapped]
        public int DiasTransito => Eta.DayNumber - Etd.DayNumber;

        [NotMapped]
        public string NaveViaje => string.IsNullOrWhiteSpace(NumeroViaje) ? Nave : $"{Nave} / {NumeroViaje}";

        // Horizonte que ve el cliente: "salidas a 4-5 semanas plazo".
        public const int SemanasHorizonte = 5;
    }
}
