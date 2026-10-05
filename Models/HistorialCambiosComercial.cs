using System;

namespace AtilsonCargoSpa.Models
{
    public class HistorialCambiosComercial
    {
        public int Id { get; set; }
        public string TipoEntidad { get; set; } = string.Empty;
        public int IdRegistro { get; set; }
        public string Campo { get; set; } = string.Empty;
        public string? ValorAnterior { get; set; }
        public string? ValorNuevo { get; set; }
        public string Comentario { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public DateTime FechaCambio { get; set; }
        public int? IdOperacion { get; set; }
    }
}