using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    [Table("TarifasMaestras")]
    public partial class TarifasMaestra
    {
        [Key]
        public int Id { get; set; }

        [StringLength(50)]
        public string Categoria { get; set; } = null!;

        [StringLength(100)]
        public string Concepto { get; set; } = null!;

        [Column(TypeName = "decimal(18, 2)")]
        public decimal ValorNeto { get; set; }

        [StringLength(10)]
        public string Moneda { get; set; } = null!;

        public bool AplicaIva { get; set; }

        public bool EsActiva { get; set; }

        // Nuevas columnas de "Inteligencia de Reglas de Negocio"
        public int? HorasLibres { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal? PorcentajeCobro { get; set; }
        public string? Clasificacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string? UsuarioModificador { get; set; }

        // Naviera dueña de este extracosto (solo aplica a Categoria == "Marítimo";
        // Terrestre/Courier/Documental quedan con IdNaviera = null).
        public int? IdNaviera { get; set; }

        [ForeignKey("IdNaviera")]
        public virtual Naviera? IdNavieraNavigation { get; set; }

        // ===================== CATÁLOGO DE RECARGOS POR NAVIERA (oct-2026) =====================
        // Código interno de la naviera para este concepto (ej. "MAF", "LRF", "BCF").
        // Null cuando la naviera no usa código propio para el ítem (ej. MSC, Maersk).
        [StringLength(20)]
        public string? Codigo { get; set; }

        // "Dry", "Reefer", "All", etc. — null cuando el concepto no distingue por tipo
        // de contenedor (se usa el mismo criterio de texto que NormalizarEquipo).
        [StringLength(30)]
        public string? TipoContenedor { get; set; }

        // Unidad sobre la que se aplica el cobro: "Por B/L", "Por Contenedor", "Por TEU", etc.
        [StringLength(50)]
        public string? Unidad { get; set; }

        // Descripción en texto llano de CUÁNDO aplica este monto (ej. "Desde 48 horas
        // después del ETD", "Solo puerto de Arica"). Siempre se llena cuando el concepto
        // tiene una condición, aunque esa condición no se pueda calcular automáticamente
        // (ver HorasDesdeEvento/HorasHastaEvento abajo) — así queda documentada igual.
        [StringLength(400)]
        public string? CondicionAplicacion { get; set; }

        // Evento contra el cual se mide la ventana de horas de abajo. Por ahora el único
        // valor usado es "ETD" (EtdReal ?? EtdPol de la Operacion) — se deja como texto
        // libre para poder sumar otros eventos más adelante (ej. recepción de B/L) el día
        // que el sistema registre esa fecha en algún lado.
        [StringLength(30)]
        public string? EventoReferencia { get; set; }

        // Ventana de horas relativa a EventoReferencia en la que aplica este monto.
        // Convención: negativo = antes del evento, positivo = después del evento,
        // null = sin límite en ese extremo. Ej. "10 días antes del zarpe" = -240..0;
        // "desde 48h después del ETD" = 48..null. Se dejan null cuando la condición
        // no es un umbral de tiempo calculable (ubicación, a criterio del operador, etc.)
        // — en ese caso CondicionAplicacion sigue siendo la fuente de verdad en texto.
        public int? HorasDesdeEvento { get; set; }
        public int? HorasHastaEvento { get; set; }

        public DateOnly? FechaVigenciaDesde { get; set; }
        public DateOnly? FechaVigenciaHasta { get; set; }

        // ===================== FASE 2: PRECARGA AUTOMÁTICA (oct-2026) =====================
        // Clave estable para matchear un concepto equivalente entre navieras sin depender
        // de comparar texto libre (cada naviera nombra "Late Arrival" distinto: Hapag le
        // dice "Presentación Tardía en Puerto", MSC/ONE separan Dry/Reefer, Maersk no).
        // Por ahora solo se usa para Late Arrival (LAR), el único extracosto que hoy se
        // crea siempre en $0 sin ningún cálculo propio:
        //   LATE_ARRIVAL_DRY / LATE_ARRIVAL_REEFER — tarifa específica por tipo de equipo.
        //   LATE_ARRIVAL_GENERICO — la naviera no distingue por tipo de equipo.
        // Null en el resto de los conceptos (no participan de ningún auto-match todavía).
        [StringLength(30)]
        public string? GrupoConcepto { get; set; }
    }
}