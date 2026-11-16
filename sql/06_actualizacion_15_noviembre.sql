/* =====================================================================
   06_actualizacion_15_noviembre.sql
   Agrega a CargoPortafolio las tablas y columnas nuevas del codigo
   sincronizado el 2026-11-15 (chat interno, notificaciones del portal,
   gestor documental, vistas por usuario, ciclo de naves, stacking,
   datos historicos, late arrival por contenedor).

   Tipos tomados de los scripts originales del proyecto. No inserta datos
   reales. Se puede ejecutar mas de una vez: solo agrega lo que falta.

   Ejecutar en (localdb)\MSSQLLocalDB, despues de 01 a 05.
   ===================================================================== */
USE CargoPortafolio;
GO
SET NOCOUNT ON;
GO

/* ---------- 1. Columnas nuevas en tablas existentes ---------- */

-- operaciones
IF COL_LENGTH('dbo.operaciones', 'IdItinerario')              IS NULL ALTER TABLE dbo.operaciones ADD IdItinerario INT NULL;
IF COL_LENGTH('dbo.operaciones', 'EsHistorica')               IS NULL ALTER TABLE dbo.operaciones ADD EsHistorica BIT NOT NULL CONSTRAINT DF_operaciones_EsHistorica DEFAULT (0);
IF COL_LENGTH('dbo.operaciones', 'OrigenHistorico')           IS NULL ALTER TABLE dbo.operaciones ADD OrigenHistorico NVARCHAR(260) NULL;
IF COL_LENGTH('dbo.operaciones', 'FechaImportacionHistorica') IS NULL ALTER TABLE dbo.operaciones ADD FechaImportacionHistorica DATETIME NULL;
IF COL_LENGTH('dbo.operaciones', 'CorreoZarpeEnviado')        IS NULL ALTER TABLE dbo.operaciones ADD CorreoZarpeEnviado DATETIME2(0) NULL;
IF COL_LENGTH('dbo.operaciones', 'AvisoLlegadaEnviado')       IS NULL ALTER TABLE dbo.operaciones ADD AvisoLlegadaEnviado DATETIME2(0) NULL;
IF COL_LENGTH('dbo.operaciones', 'UsuarioAvisoLlegada')       IS NULL ALTER TABLE dbo.operaciones ADD UsuarioAvisoLlegada NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.operaciones', 'RevisionNave5')             IS NULL ALTER TABLE dbo.operaciones ADD RevisionNave5 DATETIME2(0) NULL;
IF COL_LENGTH('dbo.operaciones', 'RevisionNave3')             IS NULL ALTER TABLE dbo.operaciones ADD RevisionNave3 DATETIME2(0) NULL;
IF COL_LENGTH('dbo.operaciones', 'UsuarioRevisionNave')       IS NULL ALTER TABLE dbo.operaciones ADD UsuarioRevisionNave NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.operaciones', 'CorreoArriboEnviado')       IS NULL ALTER TABLE dbo.operaciones ADD CorreoArriboEnviado DATETIME2(0) NULL;

-- ItinerariosNaves
IF COL_LENGTH('dbo.ItinerariosNaves', 'HoraEtd')              IS NULL ALTER TABLE dbo.ItinerariosNaves ADD HoraEtd TIME(0) NULL;
IF COL_LENGTH('dbo.ItinerariosNaves', 'HoraEta')              IS NULL ALTER TABLE dbo.ItinerariosNaves ADD HoraEta TIME(0) NULL;
IF COL_LENGTH('dbo.ItinerariosNaves', 'ZarpeEfectivo')        IS NULL ALTER TABLE dbo.ItinerariosNaves ADD ZarpeEfectivo DATETIME2(0) NULL;
IF COL_LENGTH('dbo.ItinerariosNaves', 'ArriboEfectivo')       IS NULL ALTER TABLE dbo.ItinerariosNaves ADD ArriboEfectivo DATETIME2(0) NULL;
IF COL_LENGTH('dbo.ItinerariosNaves', 'StackingInicioDry')    IS NULL ALTER TABLE dbo.ItinerariosNaves ADD StackingInicioDry DATETIME2 NULL;
IF COL_LENGTH('dbo.ItinerariosNaves', 'StackingFinDry')       IS NULL ALTER TABLE dbo.ItinerariosNaves ADD StackingFinDry DATETIME2 NULL;
IF COL_LENGTH('dbo.ItinerariosNaves', 'StackingInicioReefer') IS NULL ALTER TABLE dbo.ItinerariosNaves ADD StackingInicioReefer DATETIME2 NULL;
IF COL_LENGTH('dbo.ItinerariosNaves', 'StackingFinReefer')    IS NULL ALTER TABLE dbo.ItinerariosNaves ADD StackingFinReefer DATETIME2 NULL;

-- TransaccionesFinancieras
IF COL_LENGTH('dbo.TransaccionesFinancieras', 'NumeroDocumentoDocumental') IS NULL ALTER TABLE dbo.TransaccionesFinancieras ADD NumeroDocumentoDocumental NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.TransaccionesFinancieras', 'ProveedorOrigen')           IS NULL ALTER TABLE dbo.TransaccionesFinancieras ADD ProveedorOrigen NVARCHAR(150) NULL;
IF COL_LENGTH('dbo.TransaccionesFinancieras', 'NumeroDocumentoOrigen')     IS NULL ALTER TABLE dbo.TransaccionesFinancieras ADD NumeroDocumentoOrigen NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.TransaccionesFinancieras', 'RutaFacturaOrigen')         IS NULL ALTER TABLE dbo.TransaccionesFinancieras ADD RutaFacturaOrigen NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.TransaccionesFinancieras', 'ContraparteHistorica')      IS NULL ALTER TABLE dbo.TransaccionesFinancieras ADD ContraparteHistorica NVARCHAR(200) NULL;

-- ExtracostosOperacion
IF COL_LENGTH('dbo.ExtracostosOperacion', 'IdUnidadTecnica') IS NULL ALTER TABLE dbo.ExtracostosOperacion ADD IdUnidadTecnica INT NULL;

-- Usuarios
IF COL_LENGTH('dbo.Usuarios', 'PermisoTarifasTransporte') IS NULL
    ALTER TABLE dbo.Usuarios ADD PermisoTarifasTransporte BIT NOT NULL
        CONSTRAINT DF_Usuarios_PermisoTarifasTransporte DEFAULT (0);
GO

/* ---------- 2. Tablas nuevas ---------- */

-- Gestor documental de operaciones
IF OBJECT_ID('dbo.ArchivosOperaciones', 'U') IS NULL
    CREATE TABLE dbo.ArchivosOperaciones (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ArchivosOperaciones PRIMARY KEY,
        IdOperacion    INT            NULL,
        NombreArchivo  NVARCHAR(260)  NULL,
        RutaArchivo    NVARCHAR(500)  NULL,
        FechaSubida    DATETIME2      NULL,
        TipoDocumento  NVARCHAR(100)  NULL,
        UsuarioSubida  NVARCHAR(150)  NULL,
        ArchivoFisico  VARBINARY(MAX) NULL
    );

-- Avisos descartados por usuario interno
IF OBJECT_ID('dbo.NotificacionesDescartadas', 'U') IS NULL
    CREATE TABLE dbo.NotificacionesDescartadas (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NotificacionesDescartadas PRIMARY KEY,
        Usuario      NVARCHAR(256) NOT NULL,
        Tipo         NVARCHAR(32)  NOT NULL,
        IdReferencia INT           NOT NULL,
        Fecha        DATETIME2     NOT NULL CONSTRAINT DF_NotificacionesDescartadas_Fecha DEFAULT (SYSDATETIME()),
        CONSTRAINT UQ_NotificacionesDescartadas_Usuario_Tipo_Referencia UNIQUE (Usuario, Tipo, IdReferencia)
    );

-- Notificaciones del portal de clientes
IF OBJECT_ID('dbo.NotificacionesCliente', 'U') IS NULL
    CREATE TABLE dbo.NotificacionesCliente (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NotificacionesCliente PRIMARY KEY,
        IdCliente     INT            NOT NULL,
        IdOperacion   INT            NULL,
        Tipo          NVARCHAR(30)   NOT NULL,
        Titulo        NVARCHAR(200)  NOT NULL,
        Mensaje       NVARCHAR(2000) NULL,
        Url           NVARCHAR(300)  NULL,
        Clave         NVARCHAR(120)  NULL,
        Fecha         DATETIME2      NOT NULL CONSTRAINT DF_NotificacionesCliente_Fecha DEFAULT (SYSDATETIME()),
        FechaLeida    DATETIME2      NULL,
        CorreoEstado  NVARCHAR(20)   NULL,
        FechaCorreo   DATETIME2      NULL
    );

IF OBJECT_ID('dbo.PreferenciasAvisoCliente', 'U') IS NULL
    CREATE TABLE dbo.PreferenciasAvisoCliente (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreferenciasAvisoCliente PRIMARY KEY,
        Correo            NVARCHAR(256) NOT NULL,
        TiposCorreo       NVARCHAR(300) NOT NULL CONSTRAINT DF_PreferenciasAvisoCliente_Tipos DEFAULT (''),
        FechaModificacion DATETIME2     NOT NULL CONSTRAINT DF_PreferenciasAvisoCliente_Fecha DEFAULT (SYSDATETIME())
    );

-- Vistas guardadas por usuario
IF OBJECT_ID('dbo.UsuariosVistas', 'U') IS NULL
    CREATE TABLE dbo.UsuariosVistas (
        IdUsuario          INT           NOT NULL CONSTRAINT PK_UsuariosVistas PRIMARY KEY,
        Vistas             NVARCHAR(MAX) NOT NULL,
        FechaModificacion  DATETIME2     NOT NULL CONSTRAINT DF_UsuariosVistas_Fecha DEFAULT (SYSDATETIME()),
        UsuarioModificador NVARCHAR(150) NULL,
        CONSTRAINT FK_UsuariosVistas_Usuarios FOREIGN KEY (IdUsuario) REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE
    );

-- Chat interno del equipo
IF OBJECT_ID('dbo.ChatConversaciones', 'U') IS NULL
    CREATE TABLE dbo.ChatConversaciones (
        Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatConversaciones PRIMARY KEY,
        Tipo               NVARCHAR(20)  NOT NULL,
        Nombre             NVARCHAR(100) NULL,
        ClavePrivada       NVARCHAR(40)  NULL,
        IdCreador          INT           NULL,
        FechaCreacion      DATETIME      NOT NULL CONSTRAINT DF_ChatConversaciones_Fecha DEFAULT (GETDATE()),
        FechaUltimoMensaje DATETIME      NULL
    );

IF OBJECT_ID('dbo.ChatParticipantes', 'U') IS NULL
    CREATE TABLE dbo.ChatParticipantes (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatParticipantes PRIMARY KEY,
        IdConversacion INT      NOT NULL CONSTRAINT FK_ChatParticipantes_Conversacion REFERENCES dbo.ChatConversaciones (Id),
        IdUsuario      INT      NOT NULL,
        UltimoLeidoId  INT      NOT NULL CONSTRAINT DF_ChatParticipantes_Leido DEFAULT (0),
        FechaIngreso   DATETIME NOT NULL CONSTRAINT DF_ChatParticipantes_Fecha DEFAULT (GETDATE())
    );

IF OBJECT_ID('dbo.ChatMensajes', 'U') IS NULL
    CREATE TABLE dbo.ChatMensajes (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatMensajes PRIMARY KEY,
        IdConversacion INT            NOT NULL CONSTRAINT FK_ChatMensajes_Conversacion REFERENCES dbo.ChatConversaciones (Id),
        IdUsuario      INT            NOT NULL,
        Texto          NVARCHAR(2000) NOT NULL,
        FechaEnvio     DATETIME       NOT NULL CONSTRAINT DF_ChatMensajes_Fecha DEFAULT (GETDATE())
    );
GO

/* ---------- 3. Llaves foraneas e indices ---------- */

IF OBJECT_ID('dbo.FK_operaciones_ItinerariosNaves', 'F') IS NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.operaciones o
                   WHERE o.IdItinerario IS NOT NULL
                     AND NOT EXISTS (SELECT 1 FROM dbo.ItinerariosNaves i WHERE i.Id = o.IdItinerario))
    ALTER TABLE dbo.operaciones WITH CHECK
        ADD CONSTRAINT FK_operaciones_ItinerariosNaves FOREIGN KEY (IdItinerario) REFERENCES dbo.ItinerariosNaves (Id);

IF OBJECT_ID('dbo.FK_ArchivosOperaciones_Operaciones', 'F') IS NULL
    ALTER TABLE dbo.ArchivosOperaciones ADD CONSTRAINT FK_ArchivosOperaciones_Operaciones
        FOREIGN KEY (IdOperacion) REFERENCES dbo.operaciones (Id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ArchivosOperaciones_IdOperacion')
    CREATE INDEX IX_ArchivosOperaciones_IdOperacion ON dbo.ArchivosOperaciones (IdOperacion);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NotificacionesCliente_Cliente')
    CREATE INDEX IX_NotificacionesCliente_Cliente ON dbo.NotificacionesCliente (IdCliente, Fecha DESC) INCLUDE (FechaLeida, Tipo);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NotificacionesCliente_Correo')
    CREATE INDEX IX_NotificacionesCliente_Correo ON dbo.NotificacionesCliente (CorreoEstado, Fecha) WHERE CorreoEstado IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreferenciasAvisoCliente_Correo')
    CREATE UNIQUE INDEX UX_PreferenciasAvisoCliente_Correo ON dbo.PreferenciasAvisoCliente (Correo);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ChatConversaciones_ClavePrivada')
    CREATE UNIQUE INDEX UX_ChatConversaciones_ClavePrivada ON dbo.ChatConversaciones (ClavePrivada) WHERE ClavePrivada IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ChatParticipantes')
    CREATE UNIQUE INDEX UX_ChatParticipantes ON dbo.ChatParticipantes (IdConversacion, IdUsuario);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChatParticipantes_Usuario')
    CREATE INDEX IX_ChatParticipantes_Usuario ON dbo.ChatParticipantes (IdUsuario) INCLUDE (IdConversacion, UltimoLeidoId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChatMensajes_Conversacion')
    CREATE INDEX IX_ChatMensajes_Conversacion ON dbo.ChatMensajes (IdConversacion, Id) INCLUDE (IdUsuario, FechaEnvio);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_operaciones_EsHistorica')
    CREATE INDEX IX_operaciones_EsHistorica ON dbo.operaciones (EsHistorica, IsDeleted, Id DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_operaciones_IdItinerario')
    CREATE INDEX IX_operaciones_IdItinerario ON dbo.operaciones (IdItinerario) WHERE IdItinerario IS NOT NULL;
GO

/* ---------- 4. Dato inicial: canal general del chat ---------- */

IF NOT EXISTS (SELECT 1 FROM dbo.ChatConversaciones WHERE Tipo = 'GENERAL')
    INSERT INTO dbo.ChatConversaciones (Tipo, Nombre, FechaCreacion) VALUES ('GENERAL', N'Equipo CargoFlow', GETDATE());
GO

PRINT 'Base CargoPortafolio actualizada (06).';
GO
