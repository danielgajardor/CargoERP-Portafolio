using System;

namespace AtilsonCargoSpa.Models
{
    public partial class ReglasTlc
    {
        public int Id { get; set; }
        public string Pais { get; set; } = null!;
        public string? Puerto { get; set; }
        public string AcuerdoOrigenKey { get; set; } = null!;
        public bool EsActiva { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreador { get; set; }
    }
}