using System;
namespace AtilsonCargoSpa.Models
{
    public partial class TarifaBl
    {
        public int Id { get; set; }
        public int IdNaviera { get; set; }
        public string TipoEmision { get; set; } = null!;
        public string? TipoEmisionOtro { get; set; }
        public string Moneda { get; set; } = "USD";
        public decimal ValorNeto { get; set; }
        public bool AplicaIva { get; set; } = true;
        public bool EsActiva { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreador { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string? UsuarioModificador { get; set; }
        public virtual Naviera? IdNavieraNavigation { get; set; }
    }
}