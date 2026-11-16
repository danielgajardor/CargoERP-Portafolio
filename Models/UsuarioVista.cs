using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    /// <summary>
    /// Vistas elegidas por el administrador para una persona del equipo (13-nov-2026).
    /// Sin fila = ve lo que da su rol. Vistas: claves de Helpers/VistasCatalogo separadas por coma.
    /// Tabla en Sql/2026-11_vistas_por_usuario.sql.
    /// </summary>
    [Table("UsuariosVistas")]
    public class UsuarioVista
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int IdUsuario { get; set; }

        public string Vistas { get; set; } = "";

        public DateTime FechaModificacion { get; set; } = DateTime.Now;

        [StringLength(150)]
        public string? UsuarioModificador { get; set; }
    }
}
