using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    public partial class TransaccionesFinanciera
    {
        [Key]
        public int Id { get; set; }

        public int IdOperacion { get; set; }

        public string GrupoCobro { get; set; } = null!;
        public string TipoMovimiento { get; set; } = null!;

        [Required, StringLength(20)]
        public string ResponsablePago { get; set; } = "CLIENTE";

        public string Concepto { get; set; } = null!;
        public int? IdProveedor { get; set; }
        public int? IdCliente { get; set; }
        public decimal MontoNeto { get; set; }
        public decimal ProfitHandling { get; set; }
        public string Moneda { get; set; } = null!;
        public decimal? TipoCambio { get; set; }

        public string EstadoFila { get; set; } = "PROVISIÓN";

        // Aquí se guarda la Factura del Proveedor o la Factura/NC al Cliente
        public string? NumeroDocumento { get; set; }

        // N° del documento de las filas Documentales (nov-2026): certificado de origen,
        // fitosanitario, sanitario o de captura, y N° de DUS/DIN. Lo escribe FinanzasSyncService
        // desde Operaciones. Antes iba en NumeroDocumento y pisaba el N° de factura en cada
        // sincronización; ahora NumeroDocumento queda solo para la factura (como en el resto).
        // Ver Sql/2026-11_penalidades_factura_documental.sql.
        public string? NumeroDocumentoDocumental { get; set; }

        // Documento de respaldo (nov-2026, Egresos documentales/terminal): la agencia
        // intermediaria (ej. Stembex) cobra con su Nota de Cobro (NumeroDocumento) y adjunta la
        // factura original del terminal (ej. TPS, STI). Aquí van ese proveedor y ese N°, para
        // detectar cobros duplicados. Ver Sql/2026-11_itinerarios_respaldo_conceptos.sql.
        public string? ProveedorOrigen { get; set; }
        public string? NumeroDocumentoOrigen { get; set; }
        // Archivo de la factura de respaldo (nov-2026), como RutaFactura para el documento directo.
        public string? RutaFacturaOrigen { get; set; }

        // --- NUEVO: CONTROL DUAL PARA EGRESOS ---
        public string? NumeroOrdenCompra { get; set; }

        public bool? IvaExento { get; set; }
        public DateTime? FechaEmision { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreador { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string? UsuarioModificador { get; set; }
        public string? Responsable { get; set; }
        // Nombre textual de proveedor/contraparte proveniente de archivos históricos.
        // Se conserva aunque no exista un maestro equivalente en el sistema.
        public string? ContraparteHistorica { get; set; }
        public string? RutaFactura { get; set; }

        public bool TarifaManual { get; set; } = false;

        public bool AceptaDiferenciaPago { get; set; }
        public string? JustificacionManual { get; set; }

        // --- ESCUDO DE SEGURIDAD ---
        public string? RutaComprobante { get; set; }

        // --- CONFIRMACIÓN DE VALOR FINANCIERO (NOMBRES DB LEGADOS) ---
        // ConfirmadoAga: checkbox de confirmación de valor, sin requerir factura AGA.
        // ValorAga: valor confirmado/informado, comparado contra el valor esperado.
        // Se mantienen los nombres de columnas existentes para no romper SQL Server.
        public bool ConfirmadoAga { get; set; } = false;
        public decimal? ValorAga { get; set; }

        public string? ComponentesGateExcluidos { get; set; }
        public string? NotaDiferenciaAga { get; set; }
        // Monto original en USD ANTES de convertir a CLP, guardado tal cual vino de
        // la tarifa — nunca se reconstruye dividiendo MontoNeto/TipoCambio en la
        // vista, porque la tasa del dólar puede fluctuar entre el momento del guardado
        // y el momento en que se renderiza la página, dando un valor ligeramente
        // distinto al real (ej: 26.72 en vez de 26.50).
        public decimal? MontoOriginalUsd { get; set; }

        [ForeignKey("IdOperacion")]
        public virtual Operacione IdOperacionNavigation { get; set; } = null!;

        [ForeignKey("IdProveedor")]
        public virtual Proveedore? IdProveedorNavigation { get; set; }

        [ForeignKey("IdCliente")]
        public virtual Cliente? IdClienteNavigation { get; set; }
    }
}