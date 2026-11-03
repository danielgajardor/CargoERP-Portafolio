/* =====================================================================
   05_actualizacion_noviembre.sql
   Agrega a CargoPortafolio las tablas y columnas nuevas del codigo
   sincronizado el 2026-11-03. Se puede ejecutar mas de una vez:
   solo agrega lo que falta.
   ===================================================================== */
USE CargoPortafolio;
GO
SET NOCOUNT ON;
GO

/* ---------- 1. Columnas nuevas en tablas existentes ---------- */

-- clientes
IF COL_LENGTH('dbo.clientes', 'EsClienteEspecial') IS NULL ALTER TABLE dbo.[clientes] ADD [EsClienteEspecial] BIT NOT NULL DEFAULT (0);

-- depositos
IF COL_LENGTH('dbo.depositos', 'IdCliente') IS NULL ALTER TABLE dbo.[depositos] ADD [IdCliente] INT NULL;
IF COL_LENGTH('dbo.depositos', 'RegistradoPorCliente') IS NULL ALTER TABLE dbo.[depositos] ADD [RegistradoPorCliente] BIT NOT NULL DEFAULT (0);
IF COL_LENGTH('dbo.depositos', 'Validado') IS NULL ALTER TABLE dbo.[depositos] ADD [Validado] BIT NOT NULL DEFAULT (1);
IF COL_LENGTH('dbo.depositos', 'FechaValidacion') IS NULL ALTER TABLE dbo.[depositos] ADD [FechaValidacion] DATETIME NULL;
IF COL_LENGTH('dbo.depositos', 'UsuarioValidacion') IS NULL ALTER TABLE dbo.[depositos] ADD [UsuarioValidacion] NVARCHAR(100) NULL;

-- ExtracostosOperacion
IF COL_LENGTH('dbo.ExtracostosOperacion', 'IdProveedor') IS NULL ALTER TABLE dbo.[ExtracostosOperacion] ADD [IdProveedor] INT NULL;

-- operaciones
IF COL_LENGTH('dbo.operaciones', 'EtdReal') IS NULL ALTER TABLE dbo.[operaciones] ADD [EtdReal] DATETIME NULL;
IF COL_LENGTH('dbo.operaciones', 'EtaReal') IS NULL ALTER TABLE dbo.[operaciones] ADD [EtaReal] DATETIME NULL;
IF COL_LENGTH('dbo.operaciones', 'TipoCorreccionBl') IS NULL ALTER TABLE dbo.[operaciones] ADD [TipoCorreccionBl] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'CorreccionBlNuevoValor') IS NULL ALTER TABLE dbo.[operaciones] ADD [CorreccionBlNuevoValor] NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.operaciones', 'CorreccionBlComentario') IS NULL ALTER TABLE dbo.[operaciones] ADD [CorreccionBlComentario] NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.operaciones', 'EvidenciaCorreccionBl') IS NULL ALTER TABLE dbo.[operaciones] ADD [EvidenciaCorreccionBl] NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.operaciones', 'LogCorreccionBl') IS NULL ALTER TABLE dbo.[operaciones] ADD [LogCorreccionBl] NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.operaciones', 'FechaArriboDestino') IS NULL ALTER TABLE dbo.[operaciones] ADD [FechaArriboDestino] DATETIME NULL;
IF COL_LENGTH('dbo.operaciones', 'UsuarioArriboDestino') IS NULL ALTER TABLE dbo.[operaciones] ADD [UsuarioArriboDestino] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'UsuarioRetiroDestino') IS NULL ALTER TABLE dbo.[operaciones] ADD [UsuarioRetiroDestino] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'FechaDevolucionVacio') IS NULL ALTER TABLE dbo.[operaciones] ADD [FechaDevolucionVacio] DATETIME NULL;
IF COL_LENGTH('dbo.operaciones', 'UsuarioDevolucionVacio') IS NULL ALTER TABLE dbo.[operaciones] ADD [UsuarioDevolucionVacio] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'PropietarioContrato') IS NULL ALTER TABLE dbo.[operaciones] ADD [PropietarioContrato] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'PropietarioContratoOtro') IS NULL ALTER TABLE dbo.[operaciones] ADD [PropietarioContratoOtro] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'ModalidadCobroAgencia') IS NULL ALTER TABLE dbo.[operaciones] ADD [ModalidadCobroAgencia] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'EstadoTarea') IS NULL ALTER TABLE dbo.[operaciones] ADD [EstadoTarea] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'FechaFinalizacionTarea') IS NULL ALTER TABLE dbo.[operaciones] ADD [FechaFinalizacionTarea] DATETIME NULL;
IF COL_LENGTH('dbo.operaciones', 'UsuarioFinalizacionTarea') IS NULL ALTER TABLE dbo.[operaciones] ADD [UsuarioFinalizacionTarea] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'VecesFinalizadaTarea') IS NULL ALTER TABLE dbo.[operaciones] ADD [VecesFinalizadaTarea] INT NOT NULL DEFAULT (0);
IF COL_LENGTH('dbo.operaciones', 'FechaReaperturaTarea') IS NULL ALTER TABLE dbo.[operaciones] ADD [FechaReaperturaTarea] DATETIME NULL;
IF COL_LENGTH('dbo.operaciones', 'UsuarioReaperturaTarea') IS NULL ALTER TABLE dbo.[operaciones] ADD [UsuarioReaperturaTarea] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.operaciones', 'MotivoReaperturaTarea') IS NULL ALTER TABLE dbo.[operaciones] ADD [MotivoReaperturaTarea] NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.operaciones', 'FechaVistoFinanzas') IS NULL ALTER TABLE dbo.[operaciones] ADD [FechaVistoFinanzas] DATETIME NULL;
IF COL_LENGTH('dbo.operaciones', 'UsuarioVistoFinanzas') IS NULL ALTER TABLE dbo.[operaciones] ADD [UsuarioVistoFinanzas] NVARCHAR(200) NULL;

-- OperacionesDocumentales
IF COL_LENGTH('dbo.OperacionesDocumentales', 'NumeroBL') IS NULL ALTER TABLE dbo.[OperacionesDocumentales] ADD [NumeroBL] NVARCHAR(50) NULL;

-- proveedores
IF COL_LENGTH('dbo.proveedores', 'RazonSocial') IS NULL ALTER TABLE dbo.[proveedores] ADD [RazonSocial] NVARCHAR(200) NULL;

-- TarifasMaritimas
IF COL_LENGTH('dbo.TarifasMaritimas', 'RebateUsd') IS NULL ALTER TABLE dbo.[TarifasMaritimas] ADD [RebateUsd] DECIMAL(18, 2) NULL;

-- unidadestecnicas
IF COL_LENGTH('dbo.unidadestecnicas', 'FechaArriboDestino') IS NULL ALTER TABLE dbo.[unidadestecnicas] ADD [FechaArriboDestino] DATETIME NULL;
IF COL_LENGTH('dbo.unidadestecnicas', 'UsuarioArriboDestino') IS NULL ALTER TABLE dbo.[unidadestecnicas] ADD [UsuarioArriboDestino] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.unidadestecnicas', 'UsuarioRetiroDestino') IS NULL ALTER TABLE dbo.[unidadestecnicas] ADD [UsuarioRetiroDestino] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.unidadestecnicas', 'FechaDevolucionVacio') IS NULL ALTER TABLE dbo.[unidadestecnicas] ADD [FechaDevolucionVacio] DATETIME NULL;
IF COL_LENGTH('dbo.unidadestecnicas', 'UsuarioDevolucionVacio') IS NULL ALTER TABLE dbo.[unidadestecnicas] ADD [UsuarioDevolucionVacio] NVARCHAR(200) NULL;

-- Cotizaciones
IF COL_LENGTH('dbo.Cotizaciones', 'PlantaCarga') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [PlantaCarga] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'DepositoRetiro') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [DepositoRetiro] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'ZonaCarga') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [ZonaCarga] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'FechaCarga') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [FechaCarga] DATETIME NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'CantidadContenedores') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [CantidadContenedores] INT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'FechaTentativaEmbarque') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [FechaTentativaEmbarque] DATETIME NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'RequiereSeguro') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [RequiereSeguro] BIT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'TipoContenedor') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [TipoContenedor] NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'CondicionReefer') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [CondicionReefer] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'Temperatura') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [Temperatura] DECIMAL(18, 2) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'Humedad') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [Humedad] INT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'Ventilacion') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [Ventilacion] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'TipoCertificado') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [TipoCertificado] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'PaisDestinoFinal') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [PaisDestinoFinal] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'RequiereSag') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [RequiereSag] BIT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'RequiereSernapesca') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [RequiereSernapesca] BIT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'TipoSolicitud') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [TipoSolicitud] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'IdPuertoOrigen') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [IdPuertoOrigen] INT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'IdPuertoDestino') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [IdPuertoDestino] INT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'ZonaEntrega') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [ZonaEntrega] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'TipoCargaDocumental') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [TipoCargaDocumental] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'MontoMaritimo') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [MontoMaritimo] DECIMAL(18, 2) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'MonedaMaritimo') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [MonedaMaritimo] NVARCHAR(10) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'IdPuertoTerrestre') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [IdPuertoTerrestre] INT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'MontoTerrestre') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [MontoTerrestre] DECIMAL(18, 2) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'MonedaTerrestre') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [MonedaTerrestre] NVARCHAR(10) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'MontoDocumental') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [MontoDocumental] DECIMAL(18, 2) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'MonedaDocumental') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [MonedaDocumental] NVARCHAR(10) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'ComentarioRespuesta') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [ComentarioRespuesta] NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'ValidaHasta') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [ValidaHasta] DATE NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'FechaRespuesta') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [FechaRespuesta] DATETIME NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'UsuarioRespuesta') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [UsuarioRespuesta] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'IdItinerario') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [IdItinerario] INT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'IdNaviera') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [IdNaviera] INT NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'Nave') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [Nave] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'NumeroBooking') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [NumeroBooking] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'NumeroInstructivo') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [NumeroInstructivo] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'FechaCorteDocumental') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [FechaCorteDocumental] DATETIME NULL;
IF COL_LENGTH('dbo.Cotizaciones', 'IdCotizacionOrigen') IS NULL ALTER TABLE dbo.[Cotizaciones] ADD [IdCotizacionOrigen] INT NULL;

-- TarifasMaestras
IF COL_LENGTH('dbo.TarifasMaestras', 'Codigo') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [Codigo] NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.TarifasMaestras', 'TipoContenedor') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [TipoContenedor] NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.TarifasMaestras', 'Unidad') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [Unidad] NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.TarifasMaestras', 'CondicionAplicacion') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [CondicionAplicacion] NVARCHAR(400) NULL;
IF COL_LENGTH('dbo.TarifasMaestras', 'EventoReferencia') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [EventoReferencia] NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.TarifasMaestras', 'HorasDesdeEvento') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [HorasDesdeEvento] INT NULL;
IF COL_LENGTH('dbo.TarifasMaestras', 'HorasHastaEvento') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [HorasHastaEvento] INT NULL;
IF COL_LENGTH('dbo.TarifasMaestras', 'FechaVigenciaDesde') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [FechaVigenciaDesde] DATE NULL;
IF COL_LENGTH('dbo.TarifasMaestras', 'FechaVigenciaHasta') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [FechaVigenciaHasta] DATE NULL;
IF COL_LENGTH('dbo.TarifasMaestras', 'GrupoConcepto') IS NULL ALTER TABLE dbo.[TarifasMaestras] ADD [GrupoConcepto] NVARCHAR(30) NULL;

-- TarifasClientes
IF COL_LENGTH('dbo.TarifasClientes', 'RebateUsd') IS NULL ALTER TABLE dbo.[TarifasClientes] ADD [RebateUsd] DECIMAL(18, 2) NULL;

-- Plantas
IF COL_LENGTH('dbo.Plantas', 'RegistradaPorCliente') IS NULL ALTER TABLE dbo.[Plantas] ADD [RegistradaPorCliente] BIT NOT NULL DEFAULT (0);
IF COL_LENGTH('dbo.Plantas', 'Validada') IS NULL ALTER TABLE dbo.[Plantas] ADD [Validada] BIT NOT NULL DEFAULT (1);
IF COL_LENGTH('dbo.Plantas', 'FechaValidacion') IS NULL ALTER TABLE dbo.[Plantas] ADD [FechaValidacion] DATETIME NULL;
IF COL_LENGTH('dbo.Plantas', 'UsuarioValidacion') IS NULL ALTER TABLE dbo.[Plantas] ADD [UsuarioValidacion] NVARCHAR(100) NULL;

-- TransaccionesFinancieras
IF COL_LENGTH('dbo.TransaccionesFinancieras', 'ProfitHandling') IS NULL ALTER TABLE dbo.[TransaccionesFinancieras] ADD [ProfitHandling] DECIMAL(18, 2) NOT NULL DEFAULT (0);
GO

/* ---------- 2. Tablas nuevas ---------- */

IF OBJECT_ID('dbo.ItinerariosNaves', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[ItinerariosNaves] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdNaviera] INT NOT NULL DEFAULT (0),
    [Nave] NVARCHAR(200) NOT NULL,
    [NumeroViaje] NVARCHAR(200) NULL,
    [IdPuertoOrigen] INT NOT NULL DEFAULT (0),
    [IdPuertoDestino] INT NOT NULL DEFAULT (0),
    [Etd] DATE NOT NULL,
    [Eta] DATE NOT NULL,
    [CorteDocumental] DATETIME NULL,
    [Transbordo] NVARCHAR(200) NULL,
    [Observaciones] NVARCHAR(MAX) NULL,
    [Activo] BIT NOT NULL DEFAULT (1),
    [FechaCreacion] DATETIME NOT NULL DEFAULT (GETDATE()),
    [UsuarioCreador] NVARCHAR(100) NULL,
    [FechaModificacion] DATETIME NULL,
    [UsuarioModificador] NVARCHAR(100) NULL
    );
END

IF OBJECT_ID('dbo.ReglasTlc', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[ReglasTlc] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Pais] NVARCHAR(200) NOT NULL,
    [Puerto] NVARCHAR(200) NULL,
    [AcuerdoOrigenKey] NVARCHAR(200) NOT NULL,
    [EsActiva] BIT NOT NULL DEFAULT (1),
    [FechaCreacion] DATETIME NOT NULL DEFAULT (GETDATE()),
    [UsuarioCreador] NVARCHAR(100) NULL
    );
END

IF OBJECT_ID('dbo.TarifasCollect', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[TarifasCollect] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [TipoTarifa] NVARCHAR(10) NOT NULL,
    [IdCliente] INT NULL,
    [IdNaviera] INT NOT NULL DEFAULT (0),
    [Pol] NVARCHAR(100) NULL,
    [Pod] NVARCHAR(100) NOT NULL,
    [Commodity] NVARCHAR(200) NULL,
    [Equipamiento] NVARCHAR(30) NULL,
    [TarifaPublicaUsd] DECIMAL(18, 2) NOT NULL DEFAULT (0),
    [TarifaNetaUsd] DECIMAL(18, 2) NOT NULL DEFAULT (0),
    [LateArrivalSinCosto] BIT NOT NULL DEFAULT (0),
    [ExtraLateSinCosto] BIT NOT NULL DEFAULT (0),
    [CorreccionBlSinCosto] NVARCHAR(20) NULL,
    [DiasLibresOrigen] NVARCHAR(50) NULL,
    [DiasLibresDestino] NVARCHAR(50) NULL,
    [Plug] NVARCHAR(50) NULL,
    [Comentarios] NVARCHAR(500) NULL,
    [FechaInicioVigencia] DATETIME NOT NULL DEFAULT (GETDATE()),
    [FechaFinVigencia] DATETIME NULL,
    [EsActiva] BIT NOT NULL DEFAULT (1),
    [FechaCreacion] DATETIME NOT NULL DEFAULT (GETDATE()),
    [UsuarioCreador] NVARCHAR(100) NULL,
    [FechaModificacion] DATETIME NULL,
    [UsuarioModificador] NVARCHAR(100) NULL
    );
END

IF OBJECT_ID('dbo.OperacionesTraspasoFinanzas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[OperacionesTraspasoFinanzas] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdOperacion] INT NOT NULL DEFAULT (0),
    [Evento] NVARCHAR(20) NOT NULL,
    [Motivo] NVARCHAR(100) NULL,
    [Comentario] NVARCHAR(500) NULL,
    [Usuario] NVARCHAR(150) NULL,
    [Fecha] DATETIME NOT NULL DEFAULT (GETDATE()),
    [Resumen] NVARCHAR(MAX) NULL,
    [RutaRespaldo] NVARCHAR(500) NULL,
    [NombreRespaldo] NVARCHAR(255) NULL
    );
END

IF OBJECT_ID('dbo.SolicitudesRegistroCliente', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[SolicitudesRegistroCliente] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [RazonSocial] NVARCHAR(200) NOT NULL,
    [Rut] NVARCHAR(200) NOT NULL,
    [NombreContacto] NVARCHAR(200) NOT NULL,
    [Telefono] NVARCHAR(200) NOT NULL,
    [Correo] NVARCHAR(200) NOT NULL,
    [DireccionOficina] NVARCHAR(200) NOT NULL,
    [NombrePlanta] NVARCHAR(200) NULL,
    [IdCiudadPlanta] INT NULL,
    [DireccionPlanta] NVARCHAR(200) NULL,
    [UrlGoogleMapsPlanta] NVARCHAR(MAX) NULL,
    [PasswordHash] NVARCHAR(200) NOT NULL,
    [Estado] NVARCHAR(200) NOT NULL,
    [FechaSolicitud] DATETIME NOT NULL DEFAULT (GETDATE()),
    [IpOrigen] NVARCHAR(200) NULL,
    [FechaRevision] DATETIME NULL,
    [UsuarioRevision] NVARCHAR(200) NULL,
    [MotivoRechazo] NVARCHAR(MAX) NULL,
    [IdClienteCreado] INT NULL,
    [IdUsuarioCreado] INT NULL
    );
END

IF OBJECT_ID('dbo.SolicitudesRegistroEquipo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[SolicitudesRegistroEquipo] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [NombreCompleto] NVARCHAR(200) NOT NULL,
    [Rut] NVARCHAR(200) NOT NULL,
    [Telefono] NVARCHAR(200) NOT NULL,
    [Correo] NVARCHAR(200) NOT NULL,
    [AreaSolicitada] NVARCHAR(200) NULL,
    [PasswordHash] NVARCHAR(200) NOT NULL,
    [Estado] NVARCHAR(200) NOT NULL,
    [FechaSolicitud] DATETIME NOT NULL DEFAULT (GETDATE()),
    [IpOrigen] NVARCHAR(200) NULL,
    [RolAsignado] NVARCHAR(200) NULL,
    [FechaRevision] DATETIME NULL,
    [UsuarioRevision] NVARCHAR(200) NULL,
    [MotivoRechazo] NVARCHAR(MAX) NULL,
    [IdUsuarioCreado] INT NULL
    );
END

IF OBJECT_ID('dbo.PerfilesUsuario', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[PerfilesUsuario] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdUsuario] INT NOT NULL DEFAULT (0),
    [FotoUrl] NVARCHAR(MAX) NULL,
    [Telefono] NVARCHAR(200) NULL,
    [Cargo] NVARCHAR(200) NULL,
    [UltimoAcceso] DATETIME NULL,
    [FechaCambioPassword] DATETIME NULL,
    [FechaModificacion] DATETIME NULL
    );
END

IF OBJECT_ID('dbo.TicketsSoporte', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[TicketsSoporte] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdCliente] INT NULL,
    [IdUsuario] INT NULL,
    [NombreUsuario] NVARCHAR(200) NULL,
    [CorreoUsuario] NVARCHAR(200) NULL,
    [IdOperacion] INT NULL,
    [Referencia] NVARCHAR(200) NULL,
    [Tipo] NVARCHAR(200) NOT NULL,
    [Detalle] NVARCHAR(MAX) NOT NULL,
    [Estado] NVARCHAR(200) NOT NULL,
    [FechaCreacion] DATETIME NOT NULL DEFAULT (GETDATE()),
    [Respuesta] NVARCHAR(MAX) NULL,
    [FechaRespuesta] DATETIME NULL,
    [UsuarioRespuesta] NVARCHAR(200) NULL,
    [FechaCierre] DATETIME NULL,
    [UltimoMensaje] DATETIME NULL,
    [FechaVistaCliente] DATETIME NULL
    );
END

IF OBJECT_ID('dbo.MensajesSoporte', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[MensajesSoporte] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdTicket] INT NOT NULL DEFAULT (0),
    [Autor] NVARCHAR(200) NOT NULL,
    [NombreAutor] NVARCHAR(200) NULL,
    [Texto] NVARCHAR(MAX) NOT NULL,
    [Fecha] DATETIME NOT NULL DEFAULT (GETDATE())
    );
END
GO

PRINT 'Base CargoPortafolio actualizada.';
GO
