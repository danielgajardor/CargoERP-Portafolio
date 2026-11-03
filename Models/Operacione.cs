using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema; // <-- Agregado para usar [ForeignKey]

namespace AtilsonCargoSpa.Models;

public partial class Operacione
{
    public int Id { get; set; }

    public string NumeroBooking { get; set; } = null!;

    public int IdCliente { get; set; }

    public int IdNaviera { get; set; }

    public int? IdOrigen { get; set; }

    public int? IdDeposito { get; set; }

    public int? IdPuertoOrigen { get; set; }

    public int? IdPuertoDestino { get; set; }

    public int? IdViaje { get; set; }

    public int IdTipoCarga { get; set; }

    public string? Commodity { get; set; }

    public DateTime? FechaStacking { get; set; }

    public DateTime? CutOffMatriz { get; set; }

    public int? IdEstadoOperacion { get; set; }

    public int? IdTipoMovimiento { get; set; }

    public int? IdTipoServicio { get; set; }

    public byte? Activo { get; set; }

    public DateTime? FechaCreacion { get; set; }

    public string? UsuarioCreador { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public string? UsuarioModificador { get; set; }

    public DateTime? LateArrival { get; set; }

    public DateTime? ExtraLateArrival { get; set; }

    public string? Nave { get; set; }

    public string? Transbordo { get; set; }

    public string? Comentarios { get; set; }

    public double? Temperatura { get; set; }

    public string? Atmosfera { get; set; }

    public double? Co2 { get; set; }

    public double? O2 { get; set; }

    public double? Ventilacion { get; set; }

    public double? Humedad { get; set; }

    public bool IsDeleted { get; set; }

    public int? CondicionPago { get; set; }

    public string? NumeroContenedor { get; set; }

    public string? SelloNaviera { get; set; }

    public int? CantidadBultos { get; set; }

    public string? TipoContenedor { get; set; }
    public string? RoleoCancelacionTipo { get; set; }        // "ROLEO" o "CANCELACION_NOSHOW"
    public string? RoleoCancelacionPenalidad { get; set; }    // "SI" / "NO" / "POR_DETERMINAR"
    public string? RoleoCancelacionComentario { get; set; }
    public DateTime? RoleoCancelacionFecha { get; set; }
    public string? RoleoCancelacionUsuario { get; set; }

    public bool RequiereGenset { get; set; }

    public int? DiasLibresDemurrage { get; set; }

    public int? DiasLibresDetention { get; set; }

    public DateTime? EtdPol { get; set; }

    public DateTime? EtaPod { get; set; }

    public DateTime? EtdReal { get; set; }
    public DateTime? EtaReal { get; set; }

    public string? TipoCorreccionBl { get; set; }
    public string? CorreccionBlNuevoValor { get; set; }
    public string? CorreccionBlComentario { get; set; }
    public string? EvidenciaCorreccionBl { get; set; }
    public string? LogCorreccionBl { get; set; }

    public int? IdNave { get; set; }

    public string? CondicionReefer { get; set; }

    public string? MarcaAc { get; set; }

    public string? TipoViaje { get; set; }

    public string? CodigoAtilson { get; set; }

    public string? EstadoWorkflow { get; set; }

    public string? EvidenciaContenedor { get; set; }

    public string? EstadoLar { get; set; }

    public string? EvidenciaLar { get; set; }

    public bool ContenedorIngresado { get; set; }

    // Ciclo de cierre en destino (Arribado → Retirado → Vacío Devuelto): cada
    // hito guarda su fecha REAL (la que informó el cliente/depósito) y quién/
    // cuándo la ingresó en el sistema, separado de FechaModificacion/
    // UsuarioModificador que solo reflejan el último guardado general.
    public DateTime? FechaArriboDestino { get; set; }
    public string? UsuarioArriboDestino { get; set; }
    public DateTime? FechaRetiroDestino { get; set; }
    public string? UsuarioRetiroDestino { get; set; }
    public DateTime? FechaDevolucionVacio { get; set; }
    public string? UsuarioDevolucionVacio { get; set; }
    public string? TerminalPortuario { get; set; }

    public decimal? Kilos { get; set; }

    public virtual GestionTransbordo? GestionTransbordo { get; set; }

    public decimal? Vgm { get; set; }
    public string? TipoTarifaMaritima { get; set; } // "SPOT" o "CONTRATO"

    // Titularidad de la tarifa (oct-2026, minuta Portal Web de Clientes).
    // Obligatorio en operaciones sin tramo marítimo, junto con Booking/Naviera/Nave.
    // Columna agregada vía Sql/2026-10_itinerarios_y_reserva.sql.
    public string? PropietarioContrato { get; set; }

    // Texto libre cuando PropietarioContrato = "OTROS".
    // Columna agregada vía Sql/2026-10_propietario_contrato_emision_bl.sql.
    public string? PropietarioContratoOtro { get; set; }

    // Quién es dueño de la tarifa/acuerdo usado en el embarque (no siempre es el
    // contrato propio). Presente en todos los servicios. Oct-2026 (25-10): solo
    // Atilson, Agunsa u Otro (Cliente); "Otro" admite un texto opcional (ej. el
    // cliente o recibidor). Los valores antiguos Depósito / Naviera / Cliente se leen
    // como "Otro" (Sql/2026-10_cobro_agencia_propietario.sql los migra y deja el
    // nombre antiguo en el texto).
    public static readonly (string Valor, string Texto)[] OpcionesPropietarioContrato =
    {
        ("ATILSON", "Atilson"),
        ("AGUNSA", "Agunsa"),
        ("OTROS", "Otro (Cliente)")
    };

    public static string? NormalizarPropietarioContrato(string? valor)
    {
        var v = (valor ?? "").Trim().ToUpperInvariant();
        if (v == "OTRO" || v == "DEPOSITO" || v == "DEPÓSITO" || v == "NAVIERA" || v == "CLIENTE") v = "OTROS";
        return OpcionesPropietarioContrato.Any(o => o.Valor == v) ? v : null;
    }

    // Texto para mostrar: "Naviera", "Otros: XYZ"...
    public static string TextoPropietarioContrato(string? valor, string? otro)
    {
        var v = NormalizarPropietarioContrato(valor);
        if (v == null) return string.IsNullOrWhiteSpace(valor) ? "" : valor!;
        if (v == "OTROS") return string.IsNullOrWhiteSpace(otro) ? "Otro (Cliente)" : $"Otro: {otro}";
        return OpcionesPropietarioContrato.First(o => o.Valor == v).Texto;
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string PropietarioContratoTexto => TextoPropietarioContrato(PropietarioContrato, PropietarioContratoOtro);
    public virtual ICollection<RoleoCancelacion> RoleosCancelaciones { get; set; } = new List<RoleoCancelacion>();

    public DateTime? ElarDate { get; set; }
    public string? NumeroSello { get; set; }
    public string? FotoContenedor { get; set; }
    public string? FotoSello { get; set; }
    public string? UbicacionOriginalesDestino { get; set; }
    public string? ModalidadCertOrigen { get; set; }
    public string? ModalidadCertFito { get; set; }
    public string? ModalidadCertSanitario { get; set; }
    public string? ModalidadCertCaptura { get; set; }
    public string? TipoEmisionBl { get; set; }
    public string? TipoEmisionBlOtro { get; set; }
    public string? PaisOrigen { get; set; }

    public string? GestionaGate { get; set; }
    public string? PagoGate { get; set; }
    public string? TipoGate { get; set; }
    public bool GateBloqueado { get; set; }

    public string? ModalidadCertificado { get; set; } = "GRUPAL";

    // Cobro al cliente de Honorarios de Agencia y Legalización/Emisión DUS (oct-2026).
    // La AGA los cobra siempre por contenedor; a cliente se cobran igual ("INDIVIDUAL",
    // valor por defecto cuando es NULL) o en un solo cobro por booking ("GRUPAL").
    // Columna agregada vía Sql/2026-10_cobro_agencia_propietario.sql.
    public string? ModalidadCobroAgencia { get; set; }

    public virtual ICollection<ExtracostosOperacion> ExtracostosOperacions { get; set; } = new List<ExtracostosOperacion>();

    public virtual ICollection<Finanzasoperacion> Finanzasoperacions { get; set; } = new List<Finanzasoperacion>();

    public virtual ICollection<SolicitudDesbloqueo> SolicitudesDesbloqueos { get; set; } = new List<SolicitudDesbloqueo>();

    // 👇 SOLUCIÓN: Data Annotations para las llaves foráneas 👇

    [ForeignKey("IdCliente")]
    public virtual Cliente IdClienteNavigation { get; set; } = null!;

    [ForeignKey("IdDeposito")]
    public virtual Deposito? IdDepositoNavigation { get; set; }

    [ForeignKey("IdNave")]
    public virtual Nafe? IdNaveNavigation { get; set; }

    [ForeignKey("IdNaviera")]
    public virtual Naviera IdNavieraNavigation { get; set; } = null!;

    [ForeignKey("IdOrigen")]
    public virtual Origenescarga? IdOrigenNavigation { get; set; }

    [ForeignKey("IdPuertoDestino")]
    public virtual Puerto? IdPuertoDestinoNavigation { get; set; }

    [ForeignKey("IdPuertoOrigen")]
    public virtual Puerto? IdPuertoOrigenNavigation { get; set; }

    [ForeignKey("IdViaje")]
    public virtual Viaje? IdViajeNavigation { get; set; }

    public string? NumeroInstructivo { get; set; }

    public virtual ICollection<OperacionesDocumentale> OperacionesDocumentales { get; set; } = new List<OperacionesDocumentale>();

    public virtual ICollection<OperacionesTerrestre> OperacionesTerrestres { get; set; } = new List<OperacionesTerrestre>();

    public virtual ICollection<Unidadestecnica> Unidadestecnicas { get; set; } = new List<Unidadestecnica>();

    public virtual ICollection<TransaccionesFinanciera> TransaccionesFinancieras { get; set; } = new List<TransaccionesFinanciera>();
    public virtual ICollection<OperacionesAlmacenamiento> OperacionesAlmacenamientos { get; set; } = new List<OperacionesAlmacenamiento>();

    public bool LockFinanzas { get; set; }
    public string? EstadoFinanzas { get; set; }
    public DateTime? FechaEnvioFinanzas { get; set; }

    public string? HistorialCorreos { get; set; }
    public bool? CorreoClienteEnviado { get; set; } = false;
    public int? VersionCorreo { get; set; } = 1;

    // Opcional: Para saber exactamente cuándo se mandó la última versión
    public DateTime? FechaUltimoEnvioCorreo { get; set; }

    // Traspaso Operaciones -> Finanzas (oct-2026): Operaciones "finaliza la tarea"
    // cuando los contenedores están embarcados y la nave zarpó; desde ahí Finanzas
    // toma el booking (alerta NUEVO) y los cambios de costos/cobros quedan bloqueados
    // hasta que Operaciones lo reabra con un motivo. Ver Services/TraspasoFinanzasService.
    // Requiere Sql/2026-10_traspaso_ops_finanzas.sql.
    public string? EstadoTarea { get; set; }
    public DateTime? FechaFinalizacionTarea { get; set; }
    public string? UsuarioFinalizacionTarea { get; set; }
    public int VecesFinalizadaTarea { get; set; }
    public DateTime? FechaReaperturaTarea { get; set; }
    public string? UsuarioReaperturaTarea { get; set; }
    public string? MotivoReaperturaTarea { get; set; }
    public DateTime? FechaVistoFinanzas { get; set; }
    public string? UsuarioVistoFinanzas { get; set; }
}