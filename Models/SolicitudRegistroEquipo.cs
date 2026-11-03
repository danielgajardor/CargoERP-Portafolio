using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Equipo Atilson (oct-2026): una persona del equipo pide su cuenta en /RegistroEquipo.
    // Queda PENDIENTE hasta que un administrador le asigna el rol y la valida en
    // Maestros › Equipo Atilson; recién ahí se crea su usuario en dbo.Usuarios.
    // Ver Sql/2026-10_registro_equipo.sql.
    [Table("SolicitudesRegistroEquipo")]
    public class SolicitudRegistroEquipo
    {
        public const string EstadoPendiente = "PENDIENTE";
        public const string EstadoAprobada = "APROBADA";
        public const string EstadoRechazada = "RECHAZADA";

        public int Id { get; set; }
        public string NombreCompleto { get; set; } = null!;
        public string Rut { get; set; } = null!;
        public string Telefono { get; set; } = null!;
        public string Correo { get; set; } = null!;
        public string? AreaSolicitada { get; set; }
        public string PasswordHash { get; set; } = null!;
        public string Estado { get; set; } = EstadoPendiente;
        public DateTime FechaSolicitud { get; set; }
        public string? IpOrigen { get; set; }
        public string? RolAsignado { get; set; }
        public DateTime? FechaRevision { get; set; }
        public string? UsuarioRevision { get; set; }
        public string? MotivoRechazo { get; set; }
        public int? IdUsuarioCreado { get; set; }
    }
}
