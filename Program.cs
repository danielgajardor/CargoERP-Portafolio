using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


// 2. Registra el contexto de la base de datos.
// En producción la variable ConnectionStrings__AtilsonContext debe reemplazar
// el valor local de appsettings. No existe fallback silencioso dentro del DbContext.
string? atilsonConnectionString = builder.Configuration.GetConnectionString("AtilsonContext");
if (string.IsNullOrWhiteSpace(atilsonConnectionString))
    throw new InvalidOperationException("Falta configurar ConnectionStrings:AtilsonContext.");
// Avisos del portal de clientes (15-nov-2026): el interceptor crea los avisos al guardar.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AtilsonCargoSpa.Services.NotificacionesClienteInterceptor>();
builder.Services.AddDbContext<AtilsonContext>((sp, options) =>
    options.UseSqlServer(atilsonConnectionString)
           .AddInterceptors(sp.GetRequiredService<AtilsonCargoSpa.Services.NotificacionesClienteInterceptor>()));

// Add services to the container.
// --- REGLAS DE ACCESO ESTRICTO (EL CANDADO ATILSON) ---
builder.Services.AddRazorPages(options =>

{
    // 1. Zona Exclusiva Clientes
    options.Conventions.AuthorizeFolder("/PortalCliente", "SoloClientes");

    // 2. Zona Exclusiva Internos (ATILSON) - Bloqueamos a los clientes
    options.Conventions.AuthorizeFolder("/Operaciones", "SoloInternos");
    options.Conventions.AuthorizeFolder("/Finanzas", "SoloInternos");
    options.Conventions.AuthorizeFolder("/Comercial", "SoloInternos");
    options.Conventions.AuthorizeFolder("/Integraciones", "SoloInternos");
    // Depositos, Plantas, Puertos y Dashboard viven dentro de /App (ver reorganización
    // oct-2026), así que ya quedan cubiertos por esta única línea — no hace falta repetirlas.
    options.Conventions.AuthorizeFolder("/App", "SoloInternos");
    // Equipo Atilson (oct-2026): validar cuentas y asignar roles, solo administradores.
    options.Conventions.AuthorizeFolder("/App/Equipo", "SoloAdmin");
    // Roles y permisos (13-nov-2026, minuta): Finanzas solo para acceso total / Finanzas;
    // Comercial y Cotizaciones para Ventas y acceso total; Comercial › Tarifas también con el
    // permiso por persona "Tarifas de transporte" (ahí ve solo los costos de transportistas).
    options.Conventions.AuthorizeFolder("/Finanzas", "AccesoFinanzas");
    options.Conventions.AuthorizePage("/Comercial/AcuerdosOperativos", "AccesoComercial");
    options.Conventions.AuthorizePage("/Comercial/Index", "AccesoTarifas");
    options.Conventions.AuthorizePage("/Operaciones/Cotizaciones", "AccesoComercial");

    // Validación centralizada de archivos subidos (extensión + tamaño) para TODA la app.
    // Ver Filters/ValidacionArchivosPageFilter.cs para el detalle y el por qué.
    options.Conventions.ConfigureFilter(new AtilsonCargoSpa.Filters.ValidacionArchivosPageFilter());

    // Traspaso Ops -> Finanzas (oct-2026): con la tarea finalizada se bloquean los cambios
    // de costos/cobros en /Operaciones hasta que se reabra. Ver Filters/TraspasoFinanzasPageFilter.cs.
    options.Conventions.ConfigureFilter(new AtilsonCargoSpa.Filters.TraspasoFinanzasPageFilter());

    // Históricos importados desde Excel: visibles para consulta/Finanzas, pero
    // totalmente de solo lectura dentro de Operaciones.
    options.Conventions.ConfigureFilter(new AtilsonCargoSpa.Filters.HistoricosSoloLecturaPageFilter());

    // Aviso de guardado (oct-2026): mensaje de éxito/error tras cada guardado en Comercial y
    // Maestros. Ver Filters/AvisoGuardadoPageFilter.cs.
    options.Conventions.ConfigureFilter(new AtilsonCargoSpa.Filters.AvisoGuardadoPageFilter());

    // Vistas por usuario (13-nov-2026): cada persona entra solo a sus vistas (las elegidas por el
    // administrador o las de su rol). Ver Filters/VistasPermitidasPageFilter.cs.
    options.Conventions.ConfigureFilter(new AtilsonCargoSpa.Filters.VistasPermitidasPageFilter());
});

builder.Services.AddAuthorization(options =>
{
    // Define quién es "Cliente"
    options.AddPolicy("SoloClientes", policy =>
        policy.RequireRole("Cliente"));

    // Define quiénes son "Internos" según tu tabla SQL exacta
    options.AddPolicy("SoloInternos", policy =>
        policy.RequireRole(AtilsonCargoSpa.Helpers.PermisosEquipo.RolesInternos));

    // Equipo Atilson (oct-2026): validar cuentas del equipo y asignar roles.
    options.AddPolicy("SoloAdmin", policy =>
        policy.RequireAssertion(ctx => AtilsonCargoSpa.Helpers.PermisosEquipo.EsAdmin(ctx.User)));

    // Permisos por módulo (13-nov-2026). Ver Helpers/PermisosEquipo.cs.
    options.AddPolicy("AccesoFinanzas", policy =>
        policy.RequireAssertion(ctx => AtilsonCargoSpa.Helpers.PermisosEquipo.PuedeFinanzas(ctx.User)));
    options.AddPolicy("AccesoComercial", policy =>
        policy.RequireAssertion(ctx => AtilsonCargoSpa.Helpers.PermisosEquipo.PuedeComercial(ctx.User)));
    options.AddPolicy("AccesoTarifas", policy =>
        policy.RequireAssertion(ctx => AtilsonCargoSpa.Helpers.PermisosEquipo.PuedeTarifas(ctx.User)));
});

// Añade el servicio de correos
builder.Services.AddScoped<AtilsonCargoSpa.Services.EmailService>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation, AtilsonCargoSpa.Services.VistasClaimsTransformation>(); // vistas por usuario (13-nov-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.FinanzasSyncService>();
builder.Services.AddScoped<AtilsonCargoSpa.Services.TraspasoFinanzasService>();
builder.Services.AddScoped<AtilsonCargoSpa.Services.RegistroClientesService>(); // registro de empresas desde el portal (oct-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.RegistroEquipoService>(); // cuentas del equipo Atilson (oct-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.PerfilUsuarioService>(); // Mi perfil (oct-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.SoporteClientesService>(); // consultas del portal (oct-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.NotificacionesClienteService>(); // avisos del portal (15-nov-2026)
builder.Services.AddSingleton<AtilsonCargoSpa.Services.NotificacionesCorreoWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AtilsonCargoSpa.Services.NotificacionesCorreoWorker>());
builder.Services.AddScoped<AtilsonCargoSpa.Services.ChatInternoService>(); // chat del equipo (nov-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.LiquidacionExportService>();
builder.Services.AddHttpClient();
// Conectores de navieras: servicios de servidor (nunca credenciales en JavaScript).
// Primera implementación Hapag-Lloyd: contratos comunes y adaptadores independientes.
builder.Services.AddHttpClient(AtilsonCargoSpa.Services.ShippingLines.HapagLloydScheduleProvider.HttpClientName, c =>
{
    c.BaseAddress = new Uri("https://api.hlag.com/hlag/external/v1/");
    c.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddSingleton<AtilsonCargoSpa.Services.ShippingLines.IShippingScheduleProvider,
    AtilsonCargoSpa.Services.ShippingLines.HapagLloydScheduleProvider>();
builder.Services.AddHttpClient(AtilsonCargoSpa.Services.ShippingLines.HapagLloydTrackingProvider.HttpClientName, c =>
{
    // SIN barra final: GET ?equipmentReference conserva la ruta /events.
    c.BaseAddress = new Uri("https://api.hlag.com/hlag/external/v2/events");
    c.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddSingleton<AtilsonCargoSpa.Services.ShippingLines.IShippingTrackingProvider,
    AtilsonCargoSpa.Services.ShippingLines.HapagLloydTrackingProvider>();
builder.Logging.AddFilter("System.Net.Http.HttpClient." +
    AtilsonCargoSpa.Services.ShippingLines.HapagLloydScheduleProvider.HttpClientName, LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient." +
    AtilsonCargoSpa.Services.ShippingLines.HapagLloydTrackingProvider.HttpClientName, LogLevel.Warning);
// Noticias de Mundo Marítimo en la landing (oct-2026): se leen desde el servidor y se guardan
// en memoria 30 min. Ver Services/NoticiasMaritimasService.cs.
builder.Services.AddMemoryCache();
void ClienteMundoMaritimo(HttpClient c)
{
    c.Timeout = TimeSpan.FromSeconds(8);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AtilsonCargoWeb/1.0; +https://www.cargoflow-demo.cl)");
    c.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/rss+xml,application/xml;q=0.9,image/*;q=0.8,*/*;q=0.5");
}
builder.Services.AddHttpClient(AtilsonCargoSpa.Services.NoticiasMaritimasService.ClienteHttp, ClienteMundoMaritimo);
builder.Services.AddHttpClient(AtilsonCargoSpa.Services.NoticiasMaritimasService.ClienteImagenes, ClienteMundoMaritimo)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<AtilsonCargoSpa.Services.NoticiasMaritimasService>();
// "Seguir camión" (oct-2026): ubicación de depósito, planta y puerto (OpenStreetMap + enlaces de Google Maps).
builder.Services.AddHttpClient("osm", c => { c.Timeout = TimeSpan.FromSeconds(8); c.DefaultRequestHeaders.UserAgent.ParseAdd("AtilsonCargoERP/1.0 (+https://www.cargoflow-demo.cl)"); });
builder.Services.AddHttpClient("osm-sinredir", c => { c.Timeout = TimeSpan.FromSeconds(8); c.DefaultRequestHeaders.UserAgent.ParseAdd("AtilsonCargoERP/1.0 (+https://www.cargoflow-demo.cl)"); })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<AtilsonCargoSpa.Services.UbicacionesRutaService>();
builder.Services.AddScoped<AtilsonCargoSpa.Services.IndicadoresEconomicosService>();
// Banco Central BDE: el token viaja en el querystring HTTP; no registrar URL ni
// credenciales. Configurar BancoCentral__Token como secreto en el servidor.
builder.Services.AddHttpClient(AtilsonCargoSpa.Services.IndicadoresEconomicosService.ClienteBancoCentral);
builder.Logging.AddFilter("System.Net.Http.HttpClient." + AtilsonCargoSpa.Services.IndicadoresEconomicosService.ClienteBancoCentral, LogLevel.Warning);
builder.Services.AddHostedService<AtilsonCargoSpa.Services.IndicadoresActualizador>(); // dólar, UF y euro al día en segundo plano
// Asistente del sitio web (nov-2026): con "AsistenteIA:ApiKey" responde con IA; sin clave, con respuestas preparadas.
builder.Services.AddHttpClient(AtilsonCargoSpa.Services.AsistenteWebService.ClienteHttp, c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<AtilsonCargoSpa.Services.AsistenteWebService>();
builder.Services.AddSingleton<AtilsonCargoSpa.Services.SitioWebInfo>(); // datos de contacto del sitio público (_LayoutPublico)
// DECISIÓN DE NEGOCIO PENDIENTE: este servicio en segundo plano envía recordatorios de cobranza
// automáticamente. Quedó deshabilitado (comentado) en el código original; no lo reactivamos en
// esta auditoría porque activar envíos automáticos a clientes es una decisión de negocio, no
// una corrección técnica. Si se decide reactivarlo, descomentar la línea de abajo Y confirmar
// que la lógica interna del servicio (horarios, destinatarios, contenido) esté validada primero.
//builder.Services.AddHostedService<CobranzaRecordatorioService>();

// --- CONFIGURACIÓN DE SEGURIDAD ATILSON ---
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login"; // Si no están logueados, los manda aquí
        options.AccessDeniedPath = "/AccesoDenegado"; // Si entran donde no deben
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
    }); 

// Compresión de respuestas (oct-2026): las páginas grandes (tablero de Operaciones,
// Control Financiero) pesan cientos de KB de HTML; comprimidas bajan ~85%.
// Los tokens antiforgery de ASP.NET Core cambian en cada render, lo que mitiga BREACH.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

var app = builder.Build();


// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
// En Development sin compresión (nov-2026): si una página falla a mitad de camino, la
// compresión cortaba la respuesta y el navegador mostraba una página en blanco
// (ERR_CONTENT_DECODING_FAILED) en vez de la página de error con el detalle.
if (!app.Environment.IsDevelopment())
    app.UseResponseCompression();
// Autenticamos antes de servir adjuntos: los documentos operativos se
// guardan actualmente bajo wwwroot/uploads y no deben quedar públicos.
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    // nov-2026: también los archivos financieros (comprobantes de pago, facturas de
    // proveedores, respaldos de reapertura) e instructivos. Los clientes reciben sus
    // documentos por /PortalCliente/Documentos (handler que valida el IdCliente).
    string[] carpetasInternas = { "/uploads/documentos", "/uploads/evidencias", "/uploads/comprobantes",
        "/uploads/facturas", "/uploads/finanzas", "/uploads/instructivos", "/uploads/respaldos-reapertura" };
    bool documental = carpetasInternas.Any(c => context.Request.Path.StartsWithSegments(c, StringComparison.OrdinalIgnoreCase));
    if (documental && (context.User.Identity?.IsAuthenticated != true ||
        !AtilsonCargoSpa.Helpers.PermisosEquipo.RolesInternos.Any(context.User.IsInRole)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});
app.UseStaticFiles();
app.UseAuthorization();

// Medición del tiempo de generación de HTML (no recopila datos de usuarios).
// Chrome/Edge F12 > Red > documento > Server-Timing muestra el tiempo
// transcurrido en el servidor: permite separar lentitud SQL de red/render.
app.Use(async (ctx, next) =>
{
    if (!HttpMethods.IsGet(ctx.Request.Method))
    {
        await next();
        return;
    }
    long inicio = System.Diagnostics.Stopwatch.GetTimestamp();
    ctx.Response.OnStarting(() =>
    {
        if (ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
            ctx.Response.Headers["Server-Timing"] = "app;dur=" +
                ms.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }
        return Task.CompletedTask;
    });
    await next();
    if (ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
    {
        var duracion = System.Diagnostics.Stopwatch.GetElapsedTime(inicio);
        if (duracion.TotalMilliseconds >= 1500)
            app.Logger.LogWarning("ATILSON vista lenta: {Metodo} {Ruta} tardó {Milisegundos:N0} ms (HTTP {Codigo})",
                ctx.Request.Method, ctx.Request.Path.Value, duracion.TotalMilliseconds, ctx.Response.StatusCode);
    }
});

app.MapRazorPages();

app.Run();

