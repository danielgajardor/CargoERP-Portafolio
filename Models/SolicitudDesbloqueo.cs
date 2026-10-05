using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    public partial class SolicitudDesbloqueo
    {
        [Key]
        public int Id { get; set; }

        public int IdOperacion { get; set; }
        public int? IdUnidadTecnica { get; set; }

        [StringLength(20)]
        public string CertKey { get; set; } = null!;

        [StringLength(500)]
        public string Comentario { get; set; } = null!;

        [StringLength(100)]
        public string UsuarioSolicitante { get; set; } = null!;

        public DateTime FechaSolicitud { get; set; }

        [StringLength(20)]
        public string Estado { get; set; } = "PENDIENTE";

        [StringLength(100)]
        public string? UsuarioResolucion { get; set; }
        public DateTime? FechaResolucion { get; set; }

        [ForeignKey("IdOperacion")]
        public virtual Operacione IdOperacionNavigation { get; set; } = null!;
    }
}