using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Consultas de soporte del portal de clientes (oct-2026). El cliente la envía desde
    // el portal y conversa con el equipo (Soporte clientes); cada respuesta le llega por
    // correo y en Portal › Soporte. Ver Sql/2026-10_perfiles_soporte.sql.
    [Table("TicketsSoporte")]
    public class TicketSoporte
    {
        public const string EstadoAbierto = "ABIERTO";
        public const string EstadoRespondido = "RESPONDIDO";
        public const string EstadoCerrado = "CERRADO";
        public static readonly string[] Tipos = { "Estado de mi embarque", "Retraso terrestre", "Corrección documental", "Duda de facturación", "Cotización o tarifa", "Otro" };

        public int Id { get; set; }
        public int? IdCliente { get; set; }
        public int? IdUsuario { get; set; }
        public string? NombreUsuario { get; set; }
        public string? CorreoUsuario { get; set; }
        public int? IdOperacion { get; set; }
        public string? Referencia { get; set; }
        public string Tipo { get; set; } = null!;
        public string Detalle { get; set; } = null!;
        public string Estado { get; set; } = EstadoAbierto;
        public DateTime FechaCreacion { get; set; }
        public string? Respuesta { get; set; }
        public DateTime? FechaRespuesta { get; set; }
        public string? UsuarioRespuesta { get; set; }
        public DateTime? FechaCierre { get; set; }
        public DateTime? UltimoMensaje { get; set; }      // para ordenar las conversaciones
        public DateTime? FechaVistaCliente { get; set; }  // el cliente vio la conversación hasta aquí
    }

    // Un mensaje de la conversación de una consulta (oct-2026). El primero es el Detalle
    // del ticket; los siguientes viven aquí. Ver Sql/2026-10_perfiles_soporte.sql.
    [Table("MensajesSoporte")]
    public class MensajeSoporte
    {
        public const string AutorCliente = "CLIENTE";
        public const string AutorEquipo = "EQUIPO";

        public int Id { get; set; }
        public int IdTicket { get; set; }
        public string Autor { get; set; } = null!;
        public string? NombreAutor { get; set; }
        public string Texto { get; set; } = null!;
        public DateTime Fecha { get; set; }
    }
}
