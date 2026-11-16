using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Avisos del portal de clientes (15-nov-2026): documento nuevo, cambio de fechas de la nave
    // (stacking, corte, ETD, ETA), seguimiento de la carga y contenedores, respuestas de soporte,
    // cotizaciones, facturas. Los crea Services/NotificacionesClienteInterceptor.cs al guardar;
    // el portal los muestra en la campana y en /PortalCliente/Notificaciones, y
    // Services/NotificacionesCorreoWorker.cs envía un resumen por correo (según preferencias).
    // Tabla creada por Sql/2026-11_notificaciones_portal.sql.
    [Table("NotificacionesCliente")]
    public class NotificacionCliente
    {
        public const string CorreoPendiente = "PENDIENTE", CorreoEnviado = "ENVIADO", CorreoOmitido = "OMITIDO";

        [Key] public int Id { get; set; }
        public int IdCliente { get; set; }
        public int? IdOperacion { get; set; }
        [Required, MaxLength(30)] public string Tipo { get; set; } = "";
        [Required, MaxLength(200)] public string Titulo { get; set; } = "";
        [MaxLength(2000)] public string? Mensaje { get; set; }
        [MaxLength(300)] public string? Url { get; set; }
        // Agrupa avisos iguales que llegan seguidos (p. ej. el mismo cambio de stacking en varios bookings).
        [MaxLength(120)] public string? Clave { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;
        public DateTime? FechaLeida { get; set; }
        // null = no se avisa por correo (ya lo hace otro flujo, p. ej. Soporte o zarpe/arribo).
        [MaxLength(20)] public string? CorreoEstado { get; set; }
        public DateTime? FechaCorreo { get; set; }
    }

    // Qué avisos recibe por correo cada usuario del portal. Sin fila = todos los que tienen correo.
    [Table("PreferenciasAvisoCliente")]
    public class PreferenciaAvisoCliente
    {
        [Key] public int Id { get; set; }
        [Required, MaxLength(256)] public string Correo { get; set; } = "";
        // Tipos (NotificacionesCliente.Tipo) que SÍ quiere por correo, separados por coma. "" = ninguno.
        [MaxLength(300)] public string TiposCorreo { get; set; } = "";
        public DateTime FechaModificacion { get; set; } = DateTime.Now;
    }
}
