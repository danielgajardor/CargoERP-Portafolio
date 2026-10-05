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
        public string Moneda { get; set; } = null!;
        public decimal? TipoCambio { get; set; }

        public string EstadoFila { get; set; } = "PROVISIÓN";

        // Aquí se guarda la Factura del Proveedor o la Factura/NC al Cliente
        public string? NumeroDocumento { get; set; }

        // --- NUEVO: CONTROL DUAL PARA EGRESOS ---
        public string? NumeroOrdenCompra { get; set; }

        public bool? IvaExento { get; set; }
        public DateTime? FechaEmision { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreador { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string? UsuarioModificador { get; set; }
        public string? Responsable { get; set; }
        public string? RutaFactura { get; set; }

        public bool TarifaManual { get; set; } = false;

        public bool AceptaDiferenciaPago { get; set; }
        public string? JustificacionManual { get; set; }

        // --- ESCUDO DE SEGURIDAD ---
        public string? RutaComprobante { get; set; }

        // --- CONTROL DE VERIFICACIÓN GATE-OUT/IN (Valor AGA vs Valor Original) ---
        // ConfirmadoAga: Finanzas activó la verificación al recibir la factura de la Agencia.
        // ValorAga: monto exacto digitado desde esa factura física, para contrastar contra
        // MontoNeto (el valor que Atilson ya tiene pactado en TarifaGate).
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