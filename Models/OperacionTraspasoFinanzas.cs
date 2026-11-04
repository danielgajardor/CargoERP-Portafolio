using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Historial del traspaso Operaciones -> Finanzas (oct-2026): cada vez que
    // Operaciones finaliza o reabre la tarea, y cada vez que Finanzas abre el booking.
    // En FINALIZADA, Resumen guarda la foto de las filas financieras de ese momento
    // (una línea por fila: tipo|concepto|moneda|monto) para mostrar qué cambió
    // entre una finalización y la siguiente.
    [Table("OperacionesTraspasoFinanzas")]
    public class OperacionTraspasoFinanzas
    {
        public const string EventoFinalizada = "FINALIZADA";
        public const string EventoReabierta = "REABIERTA";
        public const string EventoVisto = "VISTO";

        [Key]
        public int Id { get; set; }

        public int IdOperacion { get; set; }

        [StringLength(20)]
        public string Evento { get; set; } = EventoFinalizada;

        [StringLength(100)]
        public string? Motivo { get; set; }

        [StringLength(500)]
        public string? Comentario { get; set; }

        [StringLength(150)]
        public string? Usuario { get; set; }

        public DateTime Fecha { get; set; } = DateTime.Now;

        public string? Resumen { get; set; }

        // Archivo de respaldo adjunto al reabrir (oct-2026). Ruta pública bajo
        // /uploads/respaldos-reapertura/ y nombre original del archivo.
        // Ver Sql/2026-10_reapertura_extracostos.sql.
        [StringLength(500)]
        public string? RutaRespaldo { get; set; }

        [StringLength(255)]
        public string? NombreRespaldo { get; set; }
    }
}
