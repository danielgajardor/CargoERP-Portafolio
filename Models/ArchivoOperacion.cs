using System;
using System.Collections.Generic;

namespace AtilsonCargoSpa.Models
{
    // Gestor Documental (Operaciones › Documental › "Gestor Documental"). nov-2026: la clase
    // no estaba en el modelo de EF y la página respondía error 500. Tabla creada por
    // Sql/2026-11_archivos_operaciones.sql (idempotente).
    [System.ComponentModel.DataAnnotations.Schema.Table("ArchivosOperaciones")]
    public partial class ArchivoOperacion
    {
        public int Id { get; set; }
        public int? IdOperacion { get; set; }
        public string? NombreArchivo { get; set; }
        public string? RutaArchivo { get; set; }
        public DateTime? FechaSubida { get; set; }

        public string? TipoDocumento { get; set; }
        public string? UsuarioSubida { get; set; }


        // REPARACIÓN DEL BINARYREADER: Si guardas el archivo en la BD, debe ser byte[] y no string.
        
        
        public byte[]? ArchivoFisico { get; set; }

        public virtual Operacione? IdOperacionNavigation { get; set; }
    }
}