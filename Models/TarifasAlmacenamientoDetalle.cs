using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    [Table("TarifasAlmacenamientoDetalle")]
    public partial class TarifasAlmacenamientoDetalle
    {
        [Key]
        public int Id { get; set; }
        public int IdProveedor { get; set; }

        [StringLength(200)]
        public string Concepto { get; set; } = null!;

        [Column(TypeName = "decimal(18, 2)")]
        public decimal ValorNeto { get; set; }

        [StringLength(10)]
        public string Moneda { get; set; } = "CLP";

        public bool AplicaIva { get; set; } = true;
        public int Orden { get; set; }
        public bool EsActiva { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreador { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string? UsuarioModificador { get; set; }

        [StringLength(10)]
        public string? TipoContenedor { get; set; }

        [StringLength(20)]
        public string? TipoCarga { get; set; }

        [StringLength(10)]
        public string? EstadoContenedor { get; set; }

        [ForeignKey("IdProveedor")]
        public virtual Proveedore? IdProveedorNavigation { get; set; }
    }
}