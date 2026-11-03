using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Portal (oct-2026): empresa que se registra sola desde el portal. Queda PENDIENTE
    // hasta que Atilson la revisa en Maestros › Clientes › Solicitudes de registro; al
    // aprobarla se crean el cliente, su usuario (rol Cliente) y su planta.
    // Ver Sql/2026-10_portal_registro_plantas.sql.
    [Table("SolicitudesRegistroCliente")]
    public class SolicitudRegistroCliente
    {
        public const string EstadoPendiente = "PENDIENTE";
        public const string EstadoAprobada = "APROBADA";
        public const string EstadoRechazada = "RECHAZADA";

        public int Id { get; set; }
        public string RazonSocial { get; set; } = null!;
        public string Rut { get; set; } = null!;
        public string NombreContacto { get; set; } = null!;
        public string Telefono { get; set; } = null!;
        public string Correo { get; set; } = null!;
        public string DireccionOficina { get; set; } = null!;
        public string? NombrePlanta { get; set; }
        public int? IdCiudadPlanta { get; set; }
        public string? DireccionPlanta { get; set; }
        public string? UrlGoogleMapsPlanta { get; set; }
        public string PasswordHash { get; set; } = null!;
        public string Estado { get; set; } = EstadoPendiente;
        public DateTime FechaSolicitud { get; set; }
        public string? IpOrigen { get; set; }
        public DateTime? FechaRevision { get; set; }
        public string? UsuarioRevision { get; set; }
        public string? MotivoRechazo { get; set; }
        public int? IdClienteCreado { get; set; }
        public int? IdUsuarioCreado { get; set; }
    }
}
