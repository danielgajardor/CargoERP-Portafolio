/* =====================================================================
   07_mas_operaciones_demo.sql
   Agrega 30 operaciones de demostracion en distintas etapas del flujo,
   incluidas 10 con el VACIO DEVUELTO (se ven como filas verdes en el
   tablero de Operaciones).

   - Requiere haber ejecutado antes los scripts 01 a 06 y los datos falsos
     (04), porque copia puertos, naviera, tipo de carga, transporte y agencia
     de operaciones que ya existen, para que todo quede coherente.
   - Las fechas se calculan desde HOY.
   - Se puede volver a ejecutar: si las operaciones ya existen, NO las
     duplica; solo corre sus fechas para que vuelvan a quedar recientes.
     (El tablero oculta las operaciones con vacio devuelto hace mas de
     30 dias: antes de una presentacion, ejecutalo de nuevo.)

   Ejecutar en (localdb)\MSSQLLocalDB.
   ===================================================================== */
USE CargoPortafolio;
GO
SET NOCOUNT ON;
GO

/* ---------- 0. Validaciones ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.operaciones WHERE NumeroBooking NOT LIKE 'CFD%' AND IsDeleted = 0)
   OR NOT EXISTS (SELECT 1 FROM dbo.clientes WHERE IsDeleted = 0)
BEGIN
    RAISERROR('Faltan datos base: ejecuta primero el script de datos falsos (04).', 16, 1);
    SET NOEXEC ON;
END
GO

/* ---------- 1. Si ya existen: solo actualizar fechas ---------- */
IF EXISTS (SELECT 1 FROM dbo.operaciones WHERE NumeroBooking LIKE 'CFD%')
BEGIN
    DECLARE @d INT = DATEDIFF(DAY,
        (SELECT CAST(MIN(FechaCreacion) AS DATE) FROM dbo.operaciones WHERE NumeroBooking LIKE 'CFD%'),
        CAST(GETDATE() AS DATE));

    IF @d > 0
    BEGIN
        UPDATE dbo.operaciones SET
            EtdPol = DATEADD(DAY, @d, EtdPol),             EtaPod = DATEADD(DAY, @d, EtaPod),
            FechaStacking = DATEADD(DAY, @d, FechaStacking), CutOffMatriz = DATEADD(DAY, @d, CutOffMatriz),
            LateArrival = DATEADD(DAY, @d, LateArrival),     EtdReal = DATEADD(DAY, @d, EtdReal),
            EtaReal = DATEADD(DAY, @d, EtaReal),             FechaArriboDestino = DATEADD(DAY, @d, FechaArriboDestino),
            FechaRetiroDestino = DATEADD(DAY, @d, FechaRetiroDestino),
            FechaDevolucionVacio = DATEADD(DAY, @d, FechaDevolucionVacio),
            FechaCreacion = DATEADD(DAY, @d, FechaCreacion), FechaModificacion = DATEADD(DAY, @d, FechaModificacion)
        WHERE NumeroBooking LIKE 'CFD%';

        UPDATE u SET
            FechaArriboDestino = DATEADD(DAY, @d, u.FechaArriboDestino),
            FechaRetiroDestino = DATEADD(DAY, @d, u.FechaRetiroDestino),
            FechaDevolucionVacio = DATEADD(DAY, @d, u.FechaDevolucionVacio)
        FROM dbo.unidadestecnicas u JOIN dbo.operaciones o ON o.Id = u.IdOperacion
        WHERE o.NumeroBooking LIKE 'CFD%';

        UPDATE t SET
            FechaCarga = DATEADD(DAY, @d, t.FechaCarga),       LlegadaPlanta = DATEADD(DAY, @d, t.LlegadaPlanta),
            SalidaPlanta = DATEADD(DAY, @d, t.SalidaPlanta),   LlegadaPuerto = DATEADD(DAY, @d, t.LlegadaPuerto),
            SalidaPuerto = DATEADD(DAY, @d, t.SalidaPuerto)
        FROM dbo.OperacionesTerrestres t JOIN dbo.operaciones o ON o.Id = t.IdOperacion
        WHERE o.NumeroBooking LIKE 'CFD%';
    END

    PRINT CONCAT('Las operaciones demo ya existian: fechas corridas ', @d, ' dias.');
    SET NOEXEC ON;
END
GO

/* ---------- 2. Plan de operaciones: estado y dias respecto de hoy ---------- */
DECLARE @plan TABLE (n INT IDENTITY(1,1), Estado NVARCHAR(50), Etd INT, Dev INT NULL, Ret INT NULL, Extra BIT);
--                                  ETD (dias desde hoy)  devolucion  retiro  2do contenedor
INSERT INTO @plan (Estado, Etd, Dev, Ret, Extra) VALUES
 (N'Vacío Devuelto', -30, -3,  -6, 1), (N'Vacío Devuelto', -32, -4,  -7, 0),
 (N'Vacío Devuelto', -35, -6,  -9, 0), (N'Vacío Devuelto', -36, -8, -10, 1),
 (N'Vacío Devuelto', -38, -10, -13, 0), (N'Vacío Devuelto', -40, -12, -15, 0),
 (N'Vacío Devuelto', -42, -15, -18, 0), (N'Vacío Devuelto', -44, -18, -20, 0),
 (N'Vacío Devuelto', -46, -21, -23, 0), (N'Vacío Devuelto', -48, -24, -26, 0),
 (N'Contenedor Retirado', -26, NULL, -1, 0), (N'Contenedor Retirado', -27, NULL, -2, 0),
 (N'Contenedor Retirado', -28, NULL, -3, 0),
 (N'Arribado', -22, NULL, NULL, 0), (N'Arribado', -23, NULL, NULL, 0), (N'Arribado', -24, NULL, NULL, 0),
 (N'Zarpe', -3, NULL, NULL, 0), (N'Zarpe', -6, NULL, NULL, 0),
 (N'Embarcado', -1, NULL, NULL, 0), (N'Embarcado', -9, NULL, NULL, 0),
 (N'Entregado a Stacking', 2, NULL, NULL, 0), (N'Entregado a Stacking', 3, NULL, NULL, 0),
 (N'Entregado a Stacking', 5, NULL, NULL, 0),
 (N'En Ruta Planta', 6, NULL, NULL, 0), (N'En Ruta Planta', 8, NULL, NULL, 0),
 (N'Confirmado', 11, NULL, NULL, 0), (N'Confirmado', 14, NULL, NULL, 0), (N'Confirmado', 17, NULL, NULL, 0),
 (N'Solicitado', 19, NULL, NULL, 0), (N'Solicitado', 22, NULL, NULL, 0);

DECLARE @prefijos TABLE (i INT, p CHAR(4));
INSERT INTO @prefijos VALUES (0,'MSKU'),(1,'HLXU'),(2,'CMAU'),(3,'MEDU'),(4,'TGHU'),(5,'SEGU');

DECLARE @hoy DATETIME = DATEADD(HOUR, 10, CAST(CAST(GETDATE() AS DATE) AS DATETIME));
DECLARE @n INT = 1, @total INT = (SELECT COUNT(*) FROM @plan);

WHILE @n <= @total
BEGIN
    DECLARE @estado NVARCHAR(50), @etdDias INT, @devDias INT, @retDias INT, @extra BIT;
    SELECT @estado = Estado, @etdDias = Etd, @devDias = Dev, @retDias = Ret, @extra = Extra FROM @plan WHERE n = @n;

    -- operacion existente que sirve de plantilla (puertos, naviera, carga, etc.)
    DECLARE @tpl INT = (SELECT TOP 1 Id FROM dbo.operaciones
                        WHERE NumeroBooking NOT LIKE 'CFD%' AND IsDeleted = 0 AND EsHistorica = 0
                        ORDER BY NEWID());
    DECLARE @cliente INT = (SELECT TOP 1 Id FROM dbo.clientes WHERE IsDeleted = 0 ORDER BY NEWID());

    DECLARE @etd DATETIME = DATEADD(DAY, @etdDias, @hoy);
    DECLARE @eta DATETIME = DATEADD(DAY, 22, @etd);
    DECLARE @ret DATETIME = DATEADD(DAY, @retDias, @hoy);
    DECLARE @dev DATETIME = DATEADD(HOUR, 5, DATEADD(DAY, @devDias, @hoy));
    DECLARE @arribo BIT = CASE WHEN @estado IN (N'Arribado', N'Contenedor Retirado', N'Vacío Devuelto') THEN 1 ELSE 0 END;
    DECLARE @zarpo  BIT = CASE WHEN @arribo = 1 OR @estado IN (N'Zarpe', N'Embarcado') THEN 1 ELSE 0 END;
    -- para las arribadas, el ETA queda en el pasado aunque el ETD no calce exacto
    IF @arribo = 1 AND @eta > DATEADD(DAY, -1, ISNULL(@ret, @hoy)) SET @eta = DATEADD(DAY, -2, ISNULL(@ret, @hoy));
    IF @estado = N'Arribado' SET @eta = DATEADD(DAY, -(@n % 3) - 1, @hoy);

    DECLARE @booking NVARCHAR(45) = CONCAT('CFD', CONVERT(CHAR(4), @hoy, 12), RIGHT(CONCAT('000', @n), 3));
    DECLARE @cont NVARCHAR(50) = CONCAT((SELECT p FROM @prefijos WHERE i = @n % 6), RIGHT(CONCAT('0000000', 4100000 + @n * 7919), 7));

    INSERT INTO dbo.operaciones (
        NumeroBooking, IdCliente, IdNaviera, IdOrigen, IdDeposito, IdPuertoOrigen, IdPuertoDestino,
        IdTipoCarga, IdTipoServicio, IdTipoMovimiento, IdEstadoOperacion, IdNave, Nave,
        Commodity, TipoContenedor, Temperatura, Ventilacion, CondicionReefer, TerminalPortuario, TipoViaje,
        ModalidadCertificado, Kilos, CantidadBultos,
        FechaStacking, CutOffMatriz, LateArrival, EtdPol, EtaPod, EtdReal, EtaReal,
        NumeroContenedor, SelloNaviera, CodigoAtilson, EstadoWorkflow, EstadoLar, ContenedorIngresado,
        FechaArriboDestino, UsuarioArriboDestino, FechaRetiroDestino, UsuarioRetiroDestino,
        FechaDevolucionVacio, UsuarioDevolucionVacio,
        Comentarios, FechaCreacion, UsuarioCreador, FechaModificacion, UsuarioModificador)
    SELECT
        @booking, @cliente, t.IdNaviera, t.IdOrigen, t.IdDeposito, t.IdPuertoOrigen, t.IdPuertoDestino,
        t.IdTipoCarga, t.IdTipoServicio, t.IdTipoMovimiento, t.IdEstadoOperacion, t.IdNave, t.Nave,
        t.Commodity, t.TipoContenedor, t.Temperatura, t.Ventilacion, t.CondicionReefer, t.TerminalPortuario, t.TipoViaje,
        t.ModalidadCertificado, ISNULL(t.Kilos, 21000) + (@n * 37) % 2500, ISNULL(t.CantidadBultos, 1800),
        DATEADD(DAY, -5, @etd), DATEADD(DAY, -3, @etd), DATEADD(DAY, -2, @etd), @etd, @eta,
        CASE WHEN @zarpo = 1 THEN @etd END, CASE WHEN @arribo = 1 THEN @eta END,
        CASE WHEN @etdDias <= 5 THEN @cont END,
        CASE WHEN @etdDias <= 5 THEN CONCAT('SL', 880000 + @n * 131) END,
        CONCAT('CF-', 2000 + @n), @estado,
        CASE WHEN @etdDias <= 3 THEN 'INGRESADO CON LAR' END,
        CASE WHEN @etdDias <= 3 THEN 1 ELSE 0 END,
        CASE WHEN @arribo = 1 THEN @eta END,               CASE WHEN @arribo = 1 THEN N'Operaciones Demo' END,
        CASE WHEN @retDias IS NOT NULL THEN @ret END,      CASE WHEN @retDias IS NOT NULL THEN N'Operaciones Demo' END,
        CASE WHEN @devDias IS NOT NULL THEN @dev END,      CASE WHEN @devDias IS NOT NULL THEN N'Operaciones Demo' END,
        N'Operación de demostración.',
        DATEADD(DAY, -12, @etd), N'Operaciones Demo', @hoy, N'Operaciones Demo'
    FROM dbo.operaciones t WHERE t.Id = @tpl;

    DECLARE @id INT = SCOPE_IDENTITY();

    -- Segundo contenedor (tambien devuelto, para que la fila quede verde)
    IF @extra = 1
        INSERT INTO dbo.unidadestecnicas (IdOperacion, NroContenedor, SelloNaviera, TipoContenedor, Commodity, IdTipoCarga,
            Temperatura, PesoCarga, Estado, ContenedorIngresado,
            FechaArriboDestino, UsuarioArriboDestino, FechaRetiroDestino, UsuarioRetiroDestino,
            FechaDevolucionVacio, UsuarioDevolucionVacio, FechaCreacion, UsuarioCreador, FechaModificacion, UsuarioModificador)
        SELECT @id, CONCAT('CFDU', RIGHT(CONCAT('0000000', 5200000 + @n * 6113), 7)), CONCAT('SL', 990000 + @n * 17),
            o.TipoContenedor, o.Commodity, o.IdTipoCarga, o.Temperatura, o.Kilos, N'Vacío Devuelto', 1,
            @eta, N'Operaciones Demo', @ret, N'Operaciones Demo',
            DATEADD(HOUR, 3, @dev), N'Operaciones Demo', o.FechaCreacion, N'Operaciones Demo', @hoy, N'Operaciones Demo'
        FROM dbo.operaciones o WHERE o.Id = @id;

    -- Transporte terrestre (copiado de la plantilla, con fechas propias)
    INSERT INTO dbo.OperacionesTerrestres (IdOperacion, EmpresaTransporte, RutTransporte, CorreoTransporte,
        NombreConductor, RutConductor, TelefonoConductor, Patente, Rampla, TipoUnidadTransporte,
        DepositoRetiro, PlantaCarga, ZonaEmbarque, DepositoDevolucion,
        FechaCarga, LlegadaPlanta, SalidaPlanta, LlegadaPuerto, SalidaPuerto,
        SorteoEscaner, SolicitudEnviada, AsignacionEnviada, Activo, FechaCreacion, UsuarioCreador)
    SELECT @id, t.EmpresaTransporte, t.RutTransporte, t.CorreoTransporte,
        t.NombreConductor, t.RutConductor, t.TelefonoConductor, t.Patente, t.Rampla, t.TipoUnidadTransporte,
        t.DepositoRetiro, t.PlantaCarga, t.ZonaEmbarque, t.DepositoDevolucion,
        DATEADD(DAY, -6, @etd),
        CASE WHEN @etdDias <= 6 THEN DATEADD(HOUR, -140, @etd) END,
        CASE WHEN @etdDias <= 6 THEN DATEADD(HOUR, -134, @etd) END,
        CASE WHEN @etdDias <= 5 THEN DATEADD(HOUR, -126, @etd) END,
        CASE WHEN @devDias IS NOT NULL THEN @dev END,
        0, CASE WHEN @etdDias <= 14 THEN 1 ELSE 0 END, CASE WHEN @etdDias <= 8 THEN 1 ELSE 0 END,
        1, DATEADD(DAY, -12, @etd), N'Operaciones Demo'
    FROM (SELECT TOP 1 * FROM dbo.OperacionesTerrestres WHERE IdOperacion = @tpl) t;

    IF NOT EXISTS (SELECT 1 FROM dbo.OperacionesTerrestres WHERE IdOperacion = @id)
        INSERT INTO dbo.OperacionesTerrestres (IdOperacion, FechaCarga, SorteoEscaner, SolicitudEnviada, AsignacionEnviada, Activo, UsuarioCreador)
        VALUES (@id, DATEADD(DAY, -6, @etd), 0, 0, 0, 1, N'Operaciones Demo');

    -- Documental (agencia y consignatario de la plantilla)
    INSERT INTO dbo.OperacionesDocumentales (IdOperacion, AgenciaAduana, IdAgenciaAduana, Consignatario, NotificarA,
        DusDin, EstadoDocumental, MatrizPresentada, ExtensionDocumental, GuiaVisado,
        CertificadoOrigen, CertificadoFito, AplicaSernapesca, AplicaSAG, Activo, FechaCreacion, UsuarioCreador)
    SELECT @id, d.AgenciaAduana, d.IdAgenciaAduana, d.Consignatario, d.NotificarA,
        CASE WHEN @etdDias <= 3 THEN CONCAT('DUS-', 260000 + @n * 41) END,
        CASE WHEN @zarpo = 1 THEN 'B/L APROBADO' WHEN @etdDias <= 5 THEN 'BORRADOR ENVIADO' ELSE 'PENDIENTE' END,
        CASE WHEN @etdDias <= 3 THEN 1 ELSE 0 END, 0, 0,
        d.CertificadoOrigen, d.CertificadoFito, ISNULL(d.AplicaSernapesca, 0), ISNULL(d.AplicaSAG, 0),
        1, DATEADD(DAY, -12, @etd), N'Operaciones Demo'
    FROM (SELECT TOP 1 * FROM dbo.OperacionesDocumentales WHERE IdOperacion = @tpl) d;

    IF NOT EXISTS (SELECT 1 FROM dbo.OperacionesDocumentales WHERE IdOperacion = @id)
        INSERT INTO dbo.OperacionesDocumentales (IdOperacion, EstadoDocumental, MatrizPresentada, ExtensionDocumental, GuiaVisado, AplicaSernapesca, AplicaSAG, Activo, UsuarioCreador)
        VALUES (@id, 'PENDIENTE', 0, 0, 0, 0, 0, 1, N'Operaciones Demo');

    -- Finanzas (valores de la plantilla, con una pequena variacion)
    INSERT INTO dbo.finanzasoperacion (IdOperacion, VentaMaritimo, VentaTerrestre, VentaDocumental,
        CostoMaritimoNeto, CostoTerrestreNeto, CostoAgenciaNeto, IdCondicionFlete, DiasLibresOrigen, DiasLibresDestino,
        CostoGateManual, FechaCreacion, UsuarioCreador, FechaModificacion, UsuarioModificador)
    SELECT @id,
        ISNULL(f.VentaMaritimo, 2400) + (@n % 5) * 50, ISNULL(f.VentaTerrestre, 450000) + (@n % 4) * 15000, ISNULL(f.VentaDocumental, 180),
        ISNULL(f.CostoMaritimoNeto, 2100) + (@n % 5) * 40, ISNULL(f.CostoTerrestreNeto, 380000) + (@n % 4) * 12000, ISNULL(f.CostoAgenciaNeto, 120),
        f.IdCondicionFlete, f.DiasLibresOrigen, f.DiasLibresDestino,
        0, DATEADD(DAY, -12, @etd), N'Operaciones Demo', @hoy, N'Operaciones Demo'
    FROM (SELECT 1 AS x) s
    LEFT JOIN (SELECT TOP 1 * FROM dbo.finanzasoperacion WHERE IdOperacion = @tpl) f ON 1 = 1;

    SET @n += 1;
END

PRINT CONCAT('Listo: ', @total, ' operaciones demo creadas (10 con vacio devuelto).');
GO

SET NOEXEC OFF;
GO
