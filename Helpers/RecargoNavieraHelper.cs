using System;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    // Fase 2 (primera porción, oct-2026): precarga de monto para "Late Arrival (LAR)"
    // a partir del catálogo de recargos por naviera (TarifasMaestras, GrupoConcepto).
    // Alcance confirmado con Dnsaile: SOLO Late Arrival. Roleo/Cancelación no se toca
    // acá — ya tiene su propio cálculo vía Naviera.DiasPenalidadRoleo/MontoPenalidadRoleoUsd.
    //
    // Principio: si no hay un match claro, no se inventa nada — se deja Monto = 0 igual
    // que antes de esta Fase 2, para que alguien lo revise a mano.
    public static class RecargoNavieraHelper
    {
        public record SugerenciaRecargo(decimal Monto, string? Moneda, string NotaCatalogo);

        // tipoContenedor: Unidadestecnica.TipoContenedor de la unidad afectada cuando se
        // conoce (valores reales, ej. "40' DRY HC", "20' REEFER STD" — no "Dry"/"Reefer"
        // a secas, por eso se busca por sub-string, mismo criterio que ya usa el resto
        // del sistema para distinguir Reefer, ej. AcuerdosOperativos.cshtml.cs). Null
        // cuando el LAR se registra a nivel de booking completo, sin un contenedor
        // específico — ahí no se puede saber Dry vs Reefer, así que no se arriesga.
        public static async Task<SugerenciaRecargo?> SugerirLateArrivalAsync(
            AtilsonContext context, int idNaviera, string? tipoContenedor)
        {
            string tc = (tipoContenedor ?? "").ToUpperInvariant();
            string? grupoEspecifico = tc.Contains("REEFER") || tc.Contains("RF")
                ? "LATE_ARRIVAL_REEFER"
                : tc.Contains("DRY")
                    ? "LATE_ARRIVAL_DRY"
                    : null;

            var candidatos = await context.TarifasMaestras
                .Where(t => t.Categoria == "Marítimo"
                    && t.IdNaviera == idNaviera
                    && t.EsActiva
                    && (t.GrupoConcepto == grupoEspecifico || t.GrupoConcepto == "LATE_ARRIVAL_GENERICO"))
                .ToListAsync();

            var match = candidatos.FirstOrDefault(t => t.GrupoConcepto == grupoEspecifico)
                ?? candidatos.FirstOrDefault(t => t.GrupoConcepto == "LATE_ARRIVAL_GENERICO");

            if (match == null) return null;

            // No se incluye IdNavieraNavigation acá (no hace falta un Include extra solo
            // para el texto): quien llama ya conoce la naviera a través de idNaviera.
            string nota = $"Monto sugerido desde catálogo de recargos ({match.Concepto}). Confirmar antes de facturar.";
            return new SugerenciaRecargo(match.ValorNeto, match.Moneda, nota);
        }

        // Extra Late Arrival (ELAR, oct-2026): se registra como un extracosto Marítimo
        // propio (antes solo se anotaba en el motivo del LAR y no generaba cobro). El monto
        // se sugiere desde el catálogo de la naviera (GrupoConcepto EXTRA_LATE_ARRIVAL, o un
        // concepto "Extra Late Arrival"); si no hay, queda en 0 para valorizarlo a mano.
        public const string TipoExtraLateArrival = "Extra Late Arrival (ELAR)";

        public static async Task<SugerenciaRecargo?> SugerirExtraLateArrivalAsync(AtilsonContext context, int idNaviera)
        {
            var candidatos = await context.TarifasMaestras
                .Where(t => t.Categoria == "Marítimo" && t.EsActiva && t.IdNaviera == idNaviera
                    && (t.GrupoConcepto == "EXTRA_LATE_ARRIVAL" || t.Concepto.ToUpper().Contains("EXTRA LATE")))
                .ToListAsync();
            var match = candidatos.FirstOrDefault(t => t.GrupoConcepto == "EXTRA_LATE_ARRIVAL")
                ?? candidatos.OrderBy(t => t.Concepto.Length).FirstOrDefault();
            if (match == null) return null;
            return new SugerenciaRecargo(match.ValorNeto, match.Moneda, $"Monto sugerido desde catálogo de recargos ({match.Concepto}). Confirmar antes de facturar.");
        }

        // Crea el extracosto ELAR una sola vez por booking (igual que el LAR).
        public static async Task AsegurarExtraLateArrivalAsync(AtilsonContext context, Operacione op, DateTime? elarDate, string? evidencia, string usuario)
        {
            if (!elarDate.HasValue) return;
            if (op.ExtracostosOperacions.Any(e => e.TipoCosto == TipoExtraLateArrival)) return;
            var sugerencia = await SugerirExtraLateArrivalAsync(context, op.IdNaviera);
            string motivo = $"ELAR registrado. Nuevo Cut-Off: {elarDate.Value:dd/MM/yyyy HH:mm}";
            if (sugerencia != null) motivo += $" | {sugerencia.NotaCatalogo}";
            op.ExtracostosOperacions.Add(new ExtracostosOperacion
            {
                TipoCosto = TipoExtraLateArrival,
                Motivo = motivo,
                Monto = sugerencia?.Monto ?? 0,
                Moneda = sugerencia?.Moneda ?? "USD",
                GrupoOperativo = "Marítimo",
                Evidencia = string.IsNullOrWhiteSpace(evidencia) ? "Generado automáticamente por sistema" : evidencia,
                FechaCreacion = DateTime.Now,
                UsuarioCreador = usuario
            });
        }
    }
}
