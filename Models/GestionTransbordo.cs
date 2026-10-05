namespace AtilsonCargoSpa.Models
{
    public class GestionTransbordo
    {
        public int Id { get; set; }
        public int IdOperacion { get; set; }
        public virtual Operacione? IdOperacionNavigation { get; set; }

        public DateTime? FechaArriboTransbordo { get; set; }
        public int? DiasLibres { get; set; }

        public string? NuevaNaveConfirmada { get; set; }
        public DateTime? NuevoEtdTransbordo { get; set; }
        public DateTime? NuevoEtaDestinoFinal { get; set; }

        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreador { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string? UsuarioModificador { get; set; }
    }
}