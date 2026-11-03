using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


// 2. Registra el contexto de la base de datos
builder.Services.AddDbContext<AtilsonContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AtilsonContext")));

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
    // Depositos, Plantas, Puertos y Dashboard viven dentro de /App (ver reorganización
    // oct-2026), así que ya quedan cubiertos por esta única línea — no hace falta repetirlas.
    options.Conventions.AuthorizeFolder("/App", "SoloInternos");
    // Equipo Atilson (oct-2026): validar cuentas y asignar roles, solo administradores.
    options.Conventions.AuthorizeFolder("/App/Equipo", "SoloAdmin");

    // Validación centralizada de archivos subidos (extensión + tamaño) para TODA la app.
    // Ver Filters/ValidacionArchivosPageFilter.cs para el detalle y el por qué.
    options.Conventions.ConfigureFilter(new AtilsonCargoSpa.Filters.ValidacionArchivosPageFilter());

    // Traspaso Ops -> Finanzas (oct-2026): con la tarea finalizada se bloquean los cambios
    // de costos/cobros en /Operaciones hasta que se reabra. Ver Filters/TraspasoFinanzasPageFilter.cs.
    options.Conventions.ConfigureFilter(new AtilsonCargoSpa.Filters.TraspasoFinanzasPageFilter());

    // Aviso de guardado (oct-2026): mensaje de éxito/error tras cada guardado en Comercial y
    // Maestros. Ver Filters/AvisoGuardadoPageFilter.cs.
    options.Conventions.ConfigureFilter(new AtilsonCargoSpa.Filters.AvisoGuardadoPageFilter());
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
});

// Añade el servicio de correos
builder.Services.AddScoped<AtilsonCargoSpa.Services.EmailService>();
builder.Services.AddScoped<AtilsonCargoSpa.Services.FinanzasSyncService>();
builder.Services.AddScoped<AtilsonCargoSpa.Services.TraspasoFinanzasService>();
builder.Services.AddScoped<AtilsonCargoSpa.Services.RegistroClientesService>(); // registro de empresas desde el portal (oct-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.RegistroEquipoService>(); // cuentas del equipo Atilson (oct-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.PerfilUsuarioService>(); // Mi perfil (oct-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.SoporteClientesService>(); // consultas del portal (oct-2026)
builder.Services.AddScoped<AtilsonCargoSpa.Services.LiquidacionExportService>();
builder.Services.AddHttpClient();
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
// API del Banco Central (fuente principal de dólar, UF y euro). Su token va en la URL, así que
// este cliente no escribe la URL en el log (solo advertencias). Token en appsettings.json ›
// BancoCentral:Token (la variable de entorno BancoCentral__Token lo reemplaza).
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
        options.ExpireTimeSpan = TimeSpan.FromHours(8); // La sesión dura 8 horas3
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
app.UseResponseCompression();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication(); // <-- Agregar esta (Identifica QUIÉN es)

app.UseAuthorization();

app.MapRazorPages();

app.Run();

