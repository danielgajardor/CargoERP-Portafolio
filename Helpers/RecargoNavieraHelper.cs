using System;
using System.Collections.Generic;
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
        // AplicaIva (nov-2026): lo que dice la tarifa de Comercial. Finanzas lo usa tal cual para
        // el IVA de la fila (la tarifa manda cómo aparece en Control Financiero).
        public record SugerenciaRecargo(decimal Monto, string? Moneda, string NotaCatalogo, bool? AplicaIva = null);

        // tipoContenedor: Unidadestecnica.TipoContenedor de la unidad afectada cuando se
        // conoce (valores reales, ej. "40' DRY HC", "20' REEFER STD" — no "Dry"/"Reefer"
        // a secas, por eso se busca por sub-string, mismo criterio que ya usa el resto
        // del sistema para distinguir Reefer, ej. AcuerdosOperativos.cshtml.cs). Null
        // cuando el LAR se registra a nivel de booking completo, sin un contenedor
        // específico — ahí no se puede saber Dry vs Reefer, así que no se arriesga.
        public static async Task<SugerenciaRecargo?> SugerirLateArrivalAsync(
            AtilsonContext context, int idNaviera, string? tipoContenedor)
        {
            // nov-2026: antes, con un equipo que no dice DRY ni REEFER (ej. "40HC") el grupo
            // quedaba null y la búsqueda tomaba CUALQUIER recargo de la naviera sin grupo (por
            // ejemplo la corrección de B/L de Hapag, CLP 81.300). Ahora: reefer → REEFER, otro
            // equipo conocido → DRY, sin equipo → solo la tarifa general de la naviera.
            string tc = (tipoContenedor ?? "").ToUpperInvariant().Trim();
            string? grupoEspecifico = tc.Contains("REEFER") || tc.Contains("RF") || tc.Contains("RH")
                ? "LATE_ARRIVAL_REEFER"
                : tc.Length > 0 ? "LATE_ARRIVAL_DRY" : null;

            var candidatos = await context.TarifasMaestras
                .Where(t => t.Categoria == "Marítimo"
                    && t.IdNaviera == idNaviera
                    && t.EsActiva
                    && t.GrupoConcepto != null
                    && (t.GrupoConcepto == grupoEspecifico || t.GrupoConcepto == "LATE_ARRIVAL_GENERICO"))
                .ToListAsync();

            var match = (grupoEspecifico == null ? null : candidatos.FirstOrDefault(t => t.GrupoConcepto == grupoEspecifico))
                ?? candidatos.FirstOrDefault(t => t.GrupoConcepto == "LATE_ARRIVAL_GENERICO");

            if (match == null) return null;

            // No se incluye IdNavieraNavigation acá (no hace falta un Include extra solo
            // para el texto): quien llama ya conoce la naviera a través de idNaviera.
            string nota = $"Monto sugerido desde catálogo de recargos ({match.Concepto}). Confirmar antes de facturar.";
            return new SugerenciaRecargo(match.ValorNeto, match.Moneda, nota, match.AplicaIva);
        }

        // Extra Late Arrival (ELAR, oct-2026): se registra como un extracosto Marítimo
        // propio (antes solo se anotaba en el motivo del LAR y no generaba cobro). El monto
        // se sugiere desde el catálogo de la naviera (GrupoConcepto EXTRA_LATE_ARRIVAL, o un
        // concepto "Extra Late Arrival"); si no hay, queda en 0 para valorizarlo a mano.
        // ------------------------------------------------------------------
        // Penalidades de naviera (nov-2026): Roleo, Cancelación y No-Show.
        // El monto sale de Comercial (TarifasMaestras, Marítimo, GrupoConcepto ROLEO /
        // CANCELACION / NO_SHOW): primero la tarifa de la naviera de la operación y, si no
        // hay, la general (IdNaviera NULL, USD 50 por contenedor, exenta). Ver
        // Sql/2026-11_penalidades_factura_documental.sql.
        // La condición de tarifa define si procede penalidad; el tipo de evento seleccionado
        // (Cancelación o No Show) se respeta por separado para tarifas y auditoría.
        // ------------------------------------------------------------------
        public const string GrupoRoleo = "ROLEO";
        public const string GrupoCancelacion = "CANCELACION";
        public const string GrupoNoShow = "NO_SHOW";

        // Cancelación y No Show son EVENTOS DISTINTOS, no un selector combinado.
        // La fecha/umbral determina si hay penalidad, no cambia automáticamente
        // el tipo elegido ni sustituye una tarifa por otra.
        // CANCELACION_NOSHOW se soporta sólo para registros antiguos ya guardados.
        public static string GrupoPenalidad(string tipoRegistro, bool dentroDelPlazo) => tipoRegistro switch
        {
            "ROLEO" => GrupoRoleo,
            "CANCELACION" => GrupoCancelacion,
            "NO_SHOW" => GrupoNoShow,
            "CANCELACION_NOSHOW" => dentroDelPlazo ? GrupoNoShow : GrupoCancelacion,
            _ => GrupoCancelacion
        };

        public static bool TipoRegistroValido(string? tipo) =>
            tipo is "ROLEO" or "CANCELACION" or "NO_SHOW" or "CANCELACION_NOSHOW";

        public static string NombreExtracostoPenalidad(string? tipo) => tipo switch
        {
            "ROLEO" => "Roleo Marítimo",
            "CANCELACION" => "Cancelación Marítima",
            "NO_SHOW" => "No Show Marítimo",
            _ => "Cancelación / No Show Marítimo" // compatible con historial antiguo
        };

        public static string NombrePenalidad(string? tipo) => tipo switch
        {
            "ROLEO" => "Roleo",
            "CANCELACION" => "Cancelación",
            "NO_SHOW" => "No Show",
            _ => "Cancelación / No Show" // registro legado
        };

        public static async Task<SugerenciaRecargo?> SugerirPenalidadAsync(AtilsonContext context, int? idNaviera, string grupo)
        {
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            var candidatos = (await context.TarifasMaestras.AsNoTracking()
                    .Where(t => t.Categoria == "Marítimo" && t.EsActiva && t.GrupoConcepto == grupo
                        && (t.IdNaviera == null || (idNaviera != null && t.IdNaviera == idNaviera)))
                    .ToListAsync())
                .Where(t => (t.FechaVigenciaDesde == null || t.FechaVigenciaDesde <= hoy) && (t.FechaVigenciaHasta == null || t.FechaVigenciaHasta >= hoy))
                .ToList();
            var match = candidatos.Where(t => t.IdNaviera != null).OrderByDescending(t => t.FechaVigenciaDesde).ThenByDescending(t => t.Id).FirstOrDefault()
                ?? candidatos.Where(t => t.IdNaviera == null).OrderByDescending(t => t.FechaVigenciaDesde).ThenByDescending(t => t.Id).FirstOrDefault();
            if (match == null) return null;
            string origen = match.IdNaviera == null ? "tarifa general de Comercial" : "tarifa de la naviera en Comercial";
            return new SugerenciaRecargo(match.ValorNeto, string.IsNullOrWhiteSpace(match.Moneda) ? "USD" : match.Moneda, $"{match.Concepto}: {match.Moneda} {match.ValorNeto:0.##} por contenedor ({origen}).", match.AplicaIva);
        }

        /// <summary>Las tres tarifas de penalidad vigentes para una naviera (para mostrarlas en el modal).</summary>
        public static async Task<Dictionary<string, SugerenciaRecargo?>> PenalidadesAsync(AtilsonContext context, int? idNaviera)
        {
            var r = new Dictionary<string, SugerenciaRecargo?>();
            foreach (var g in new[] { GrupoRoleo, GrupoCancelacion, GrupoNoShow })
                r[g] = await SugerirPenalidadAsync(context, idNaviera, g);
            return r;
        }

        // ------------------------------------------------------------------
        // Late Arrival / Extra Late Arrival POR CONTENEDOR (nov-2026). Antes había un solo
        // cobro por booking aunque se atrasaran varios contenedores. Ahora cada contenedor
        // tiene el suyo (ExtracostosOperacion.IdUnidadTecnica; null = contenedor principal)
        // y el motivo empieza con la etiqueta del contenedor ([MSKU1234567] o [2° Contenedor]).
        // ------------------------------------------------------------------
        public const string TipoLateArrival = "Late Arrival (LAR)";

        public static string EtiquetaContenedor(Operacione op, int? idUnidad)
        {
            if (idUnidad == null) return ContenedorEtiquetaHelper.EtiquetaCompleta(op.NumeroContenedor, 1);
            var unidades = (op.Unidadestecnicas ?? new List<Unidadestecnica>()).OrderBy(u => u.Id).ToList();
            int idx = unidades.FindIndex(u => u.Id == idUnidad.Value);
            return idx < 0 ? $"Contenedor U{idUnidad}" : ContenedorEtiquetaHelper.EtiquetaCompleta(unidades[idx].NroContenedor, idx + 2);
        }

        public static string TipoContenedorDe(Operacione op, int? idUnidad)
        {
            string? tipo = idUnidad.HasValue ? op.Unidadestecnicas?.FirstOrDefault(u => u.Id == idUnidad.Value)?.TipoContenedor : null;
            return string.IsNullOrWhiteSpace(tipo) ? (op.TipoContenedor ?? "") : tipo;
        }

        private static string Recortar(string s, int max = 500) => s.Length > max ? s[..max] : s;

        /// <summary>Cobro de Late Arrival del contenedor; lo crea con el monto del catálogo si no existe.</summary>
        public static async Task<ExtracostosOperacion> AsegurarLateArrivalAsync(AtilsonContext context, Operacione op, int? idUnidad, string motivo, string? evidencia, string usuario)
        {
            var existente = op.ExtracostosOperacions.FirstOrDefault(e => e.TipoCosto == TipoLateArrival && e.IdUnidadTecnica == idUnidad);
            if (existente != null) return existente;
            var sug = await SugerirLateArrivalAsync(context, op.IdNaviera, TipoContenedorDe(op, idUnidad));
            string m = $"[{EtiquetaContenedor(op, idUnidad)}] {motivo}";
            if (sug != null) m += $" | {sug.NotaCatalogo}";
            var ext = new ExtracostosOperacion
            {
                TipoCosto = TipoLateArrival,
                Motivo = Recortar(m),
                Monto = sug?.Monto ?? 0,
                Moneda = sug?.Moneda ?? "USD",
                GrupoOperativo = "Marítimo",
                IdUnidadTecnica = idUnidad,
                Evidencia = string.IsNullOrWhiteSpace(evidencia) ? "Generado automáticamente por sistema" : evidencia,
                FechaCreacion = DateTime.Now,
                UsuarioCreador = usuario
            };
            op.ExtracostosOperacions.Add(ext);
            return ext;
        }

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
            return new SugerenciaRecargo(match.ValorNeto, match.Moneda, $"Monto sugerido desde catálogo de recargos ({match.Concepto}). Confirmar antes de facturar.", match.AplicaIva);
        }

        // Crea el extracosto ELAR una sola vez por booking (igual que el LAR).
        public static async Task AsegurarExtraLateArrivalAsync(AtilsonContext context, Operacione op, DateTime? elarDate, string? evidencia, string usuario, int? idUnidad = null)
        {
            if (!elarDate.HasValue) return;
            if (op.ExtracostosOperacions.Any(e => e.TipoCosto == TipoExtraLateArrival && e.IdUnidadTecnica == idUnidad)) return;
            var sugerencia = await SugerirExtraLateArrivalAsync(context, op.IdNaviera);
            string motivo = $"[{EtiquetaContenedor(op, idUnidad)}] ELAR registrado. Nuevo Cut-Off: {elarDate.Value:dd/MM/yyyy HH:mm}";
            if (sugerencia != null) motivo += $" | {sugerencia.NotaCatalogo}";
            op.ExtracostosOperacions.Add(new ExtracostosOperacion
            {
                TipoCosto = TipoExtraLateArrival,
                Motivo = Recortar(motivo),
                Monto = sugerencia?.Monto ?? 0,
                Moneda = sugerencia?.Moneda ?? "USD",
                GrupoOperativo = "Marítimo",
                IdUnidadTecnica = idUnidad,
                Evidencia = string.IsNullOrWhiteSpace(evidencia) ? "Generado automáticamente por sistema" : evidencia,
                FechaCreacion = DateTime.Now,
                UsuarioCreador = usuario
            });
        }
    }
}
