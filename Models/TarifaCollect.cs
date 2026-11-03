using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Tarifas RF Fresco COLLECT / "No Prepaid" (oct-2026, pedido de Cristian).
    // El consignatario paga a la naviera la Tarifa Pública; la naviera nos paga a
    // nosotros el rebate (Pública - Neta de la naviera) y nosotros le devolvemos al
    // cliente su rebate (Pública - Neta pactada con el cliente). La diferencia es la
    // comisión de Atilson. Además guarda las condiciones del acuerdo (Late Arrival,
    // Extra Late, Corrección de BL sin costo), que se cruzan por naviera, destino y
    // commodity (cerezas, ciruelas...).
    //   TipoTarifa = "NAVIERA": lo que la naviera le cotiza a Atilson (IdCliente null).
    //   TipoTarifa = "CLIENTE": lo que Atilson le vende al cliente.
    [Table("TarifasCollect")]
    public partial class TarifaCollect
    {
        public const string TipoNaviera = "NAVIERA";
        public const string TipoCliente = "CLIENTE";

        public const string CorreccionAntesZarpe = "ANTES_ZARPE";
        public const string Correccion24h = "24H";
        public const string Correccion48h = "48H";

        [Key]
        public int Id { get; set; }

        [StringLength(10)]
        public string TipoTarifa { get; set; } = TipoNaviera;

        public int? IdCliente { get; set; }
        public int IdNaviera { get; set; }

        [StringLength(100)]
        public string? Pol { get; set; }

        [StringLength(100)]
        public string Pod { get; set; } = null!;

        // Uno o varios commodities separados por coma ("CEREZAS, CIRUELAS"). Vacío = todos.
        [StringLength(200)]
        public string? Commodity { get; set; }

        [StringLength(30)]
        public string? Equipamiento { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal TarifaPublicaUsd { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal TarifaNetaUsd { get; set; }

        public bool LateArrivalSinCosto { get; set; }
        public bool ExtraLateSinCosto { get; set; }

        // Plazo en que la corrección de BL va sin costo: null = con costo,
        // ANTES_ZARPE, 24H o 48H (horas después del zarpe).
        [StringLength(20)]
        public string? CorreccionBlSinCosto { get; set; }

        [StringLength(50)]
        public string? DiasLibresOrigen { get; set; }

        [StringLength(50)]
        public string? DiasLibresDestino { get; set; }

        [StringLength(50)]
        public string? Plug { get; set; }

        [StringLength(500)]
        public string? Comentarios { get; set; }

        public DateTime FechaInicioVigencia { get; set; }
        public DateTime? FechaFinVigencia { get; set; }
        public bool EsActiva { get; set; } = true;

        public DateTime FechaCreacion { get; set; }

        [StringLength(100)]
        public string? UsuarioCreador { get; set; }

        public DateTime? FechaModificacion { get; set; }

        [StringLength(100)]
        public string? UsuarioModificador { get; set; }

        [ForeignKey("IdNaviera")]
        public virtual Naviera? IdNavieraNavigation { get; set; }

        [ForeignKey("IdCliente")]
        public virtual Cliente? IdClienteNavigation { get; set; }

        // Rebate = Tarifa Pública - Tarifa Neta (nunca negativo).
        [NotMapped]
        public decimal RebateUsd => Math.Max(0m, TarifaPublicaUsd - TarifaNetaUsd);
    }
}
