using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    [Table("RecordatoriosCobranza")]
    public partial class RecordatorioCobranza
    {
        [Key] public int Id { get; set; }
        public int IdCliente { get; set; }
        public DateTime FechaEnvio { get; set; }
        public decimal MontoPendienteUsd { get; set; }
        public string TipoEnvio { get; set; } = "MANUAL";
        public string? Usuario { get; set; }

        [ForeignKey("IdCliente")]
        public virtual Cliente? IdClienteNavigation { get; set; }
    }
}