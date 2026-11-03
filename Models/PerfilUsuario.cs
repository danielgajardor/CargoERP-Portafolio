using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Perfil de usuario (oct-2026): foto, datos de contacto y último acceso.
    // Un registro por usuario (clientes e internos). Ver Sql/2026-10_perfiles_soporte.sql.
    [Table("PerfilesUsuario")]
    public class PerfilUsuario
    {
        public int Id { get; set; }
        public int IdUsuario { get; set; }
        public string? FotoUrl { get; set; }
        public string? Telefono { get; set; }
        public string? Cargo { get; set; }
        public DateTime? UltimoAcceso { get; set; }
        public DateTime? FechaCambioPassword { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }
}
