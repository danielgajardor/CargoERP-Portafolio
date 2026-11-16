using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Oculta un aviso para un usuario sin modificar la solicitud de negocio.
    [Table("NotificacionesDescartadas")]
    public class NotificacionDescartada
    {
        [Key] public int Id { get; set; }
        [Required, MaxLength(256)] public string Usuario { get; set; } = "";
        [Required, MaxLength(32)] public string Tipo { get; set; } = "";
        public int IdReferencia { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;
    }
}
