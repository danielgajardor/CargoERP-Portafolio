using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    // Minuta Roleos/Cancelaciones/No Show (oct-2026): un booking cancelado NUNCA se
    // elimina; debe seguir visible en el registro operativo (Main Board, Detalle,
    // historial y Finanzas). Se considera cancelado si:
    //  - el estado del booking o de alguno de sus contenedores es "Cancelado", o
    //  - tiene registrada una Cancelación / No Show.
    public static class BookingCanceladoHelper
    {
        public const string MensajeNoEliminar =
            "Este booking está cancelado y no se puede eliminar: debe quedar visible en el registro operativo.";

        public static bool EstaCancelado(Operacione op) =>
            EsEstadoCancelado(op.EstadoWorkflow)
            || (op.Unidadestecnicas?.Any(u => EsEstadoCancelado(u.Estado)) ?? false)
            || (op.RoleosCancelaciones?.Any(r => r.Tipo == "CANCELACION_NOSHOW") ?? false);

        public static async Task<bool> EstaCanceladoAsync(AtilsonContext context, int idOperacion)
        {
            var op = await context.Operaciones
                .AsNoTracking()
                .Include(o => o.Unidadestecnicas)
                .Include(o => o.RoleosCancelaciones)
                .FirstOrDefaultAsync(o => o.Id == idOperacion);
            return op != null && EstaCancelado(op);
        }

        private static bool EsEstadoCancelado(string? estado) =>
            (estado ?? "").ToUpperInvariant().Contains("CANCELADO");
    }
}
