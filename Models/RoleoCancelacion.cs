using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    [Table("RoleosCancelaciones")]
    public partial class RoleoCancelacion
    {
        [Key] public int Id { get; set; }
        public int IdOperacion { get; set; }
        public int? IdUnidadTecnica { get; set; }
        public string Tipo { get; set; } = string.Empty; // ROLEO / CANCELACION_NOSHOW

        public DateTime FechaSolicitud { get; set; }
        public DateTime? EtdOriginal { get; set; }
        public int? DiasAnticipacion { get; set; }
        public string? NavieraTipoTarifa { get; set; }

        public bool PenalidadSistemaSugiere { get; set; }
        public string PenalidadConfirmada { get; set; } = "POR_DETERMINAR";

        public int CantidadContenedoresAfectados { get; set; } = 1;
        public decimal? MontoPenalidadUnitarioUsd { get; set; }
        public string? Comentario { get; set; }


        public bool SolicitadoPorCliente { get; set; }
        public bool Atendido { get; set; } = true;
        public string? NuevaNave { get; set; }
        public DateTime? NuevoEtd { get; set; }
        public DateTime? NuevoEta { get; set; }
        public DateTime? NuevoStacking { get; set; }
        public DateTime? NuevoCierreDocumental { get; set; }
        public bool DatosNuevosPendientes { get; set; }

        public string? Usuario { get; set; }
        public DateTime FechaCreacion { get; set; }

        [ForeignKey("IdOperacion")]
        public virtual Operacione? IdOperacionNavigation { get; set; }
    }
}