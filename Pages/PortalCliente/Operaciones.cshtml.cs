using AtilsonCargoSpa.Models;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    /// <summary>
    /// Mis operaciones (oct-2026): la lista completa de embarques del cliente con filtros y
    /// detalle. Usa la misma consulta y acciones (roleo/cancelación) que Inicio, que ahora
    /// solo muestra el resumen.
    /// </summary>
    public class OperacionesModel : IndexModel
    {
        public OperacionesModel(AtilsonContext context) : base(context)
        {
        }
    }
}
