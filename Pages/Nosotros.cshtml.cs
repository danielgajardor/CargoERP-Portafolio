using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtilsonCargoSpa.Pages
{
    /// <summary>Página pública "Nosotros" (nov-2026). El contenido está en <see cref="NosotrosContenido"/>.</summary>
    [AllowAnonymous]
    public class NosotrosModel : PageModel
    {
        public void OnGet() { }
    }

    public record HitoHistoria(string Anio, string Titulo, string Texto);
    public record Valor(string Icono, string Titulo, string Texto);
    public record Persona(string Nombre, string Cargo, string Area, string? Foto = null, string? LinkedIn = null);

    /// <summary>
    /// Textos de la página "Nosotros" (nov-2026). Todo lo que se ve en /Nosotros se edita aquí.
    ///
    /// CONTENIDO DE EJEMPLO: los hitos de la historia, la misión, la visión y el equipo son un
    /// borrador para reemplazar con la información real. Mientras no esté lista, se puede ocultar
    /// el enlace del menú con appsettings › SitioWeb:MostrarNosotros = false (la página sigue
    /// disponible en /Nosotros para revisarla).
    ///
    /// Fotos: dejar las imágenes en wwwroot/img/nosotros/ y poner la ruta, por ejemplo
    /// "/img/nosotros/equipo.jpg". Sin foto se muestra un fondo de la marca o las iniciales.
    /// Si se cambia un texto, actualizar también su traducción en wwwroot/js/i18n/en.json.
    /// </summary>
    public static class NosotrosContenido
    {
        public const string Titular = "Personas que mueven tu carga.";
        public const string Bajada = "Somos un freight forwarder de Valparaíso. Un mismo equipo coordina el flete marítimo, el transporte, los documentos y la aduana de cada embarque, y tú lo sigues todo en nuestro portal.";

        // Ficha del encabezado (datos que ya están en el sitio).
        public static readonly (string Etiqueta, string Valor)[] Ficha =
        {
            ("Oficina central", "A. Prat 814, of. 503, Valparaíso"),
            ("Servicios", "Marítimo, terrestre, documental y aduanas"),
            ("Puertos", "San Antonio, Valparaíso y Biobío"),
            ("Portal", "Propio, disponible las 24 horas"),
        };

        // Quiénes somos
        public const string IntroDestacada = "Nacimos para que exportar e importar sea más simple: una sola contraparte para todo el embarque y la información a la vista, al mismo tiempo que nosotros.";
        public static readonly string[] Intro =
        {
            "Trabajamos con las principales navieras, transportistas certificados y agentes de aduana habilitados. Nuestro trabajo es que cada pieza calce: la reserva con la nave, el camión con el stacking, los documentos con el cut-off.",
            "Por eso desarrollamos nuestro propio portal de clientes: lo que cambia en la operación, cambia en tu pantalla.",
        };
        public static readonly string? FotoEquipo = null; // por ejemplo "/img/nosotros/equipo.jpg"

        // Historia (EJEMPLO: reemplazar años y textos)
        public static readonly HitoHistoria[] Historia =
        {
            new("Año", "Nace Atilson Cargo", "Abrimos nuestra oficina en Valparaíso para coordinar exportaciones con una sola contraparte."),
            new("Año", "Primeras rutas", "Las primeras exportaciones en contenedor hacia Asia y Europa."),
            new("Año", "Transporte y aduanas", "Sumamos el transporte terrestre y la coordinación de aduanas al mismo equipo."),
            new("Año", "Portal de clientes", "Lanzamos nuestro portal: seguimiento, documentos y costos de cada embarque en línea."),
            new("Hoy", "Seguimos creciendo", "Más rutas, más clientes y el mismo compromiso con cada contenedor."),
        };

        // Misión y visión (EJEMPLO)
        public const string Mision = "Mover la carga de nuestros clientes con precisión, cumpliendo cada plazo y dándoles visibilidad completa de su embarque.";
        public const string Vision = "Ser el freight forwarder de referencia en Chile por nuestra forma de trabajar: cercana, ordenada y transparente.";

        public static readonly Valor[] Valores =
        {
            new("bi-eye", "Transparencia", "Ves lo mismo que nuestro equipo: hitos, documentos y costos."),
            new("bi-stopwatch", "Precisión", "Cada plazo cuenta: stacking, cut-off y zarpe bajo control."),
            new("bi-people", "Cercanía", "Un ejecutivo que conoce tu carga y responde por todo el embarque."),
            new("bi-lightbulb", "Mejora continua", "Desarrollamos nuestras propias herramientas y las mejoramos con lo que nos piden."),
        };

        // Equipo (EJEMPLO: nombres, cargos y fotos por completar)
        public static readonly Persona[] Equipo =
        {
            new("Nombre Apellido", "Gerente general", "Gerencia"),
            new("Nombre Apellido", "Jefe de operaciones", "Operaciones"),
            new("Nombre Apellido", "Ejecutivo comercial", "Comercial"),
            new("Nombre Apellido", "Encargada de finanzas", "Finanzas"),
            new("Nombre Apellido", "Coordinadora documental", "Documentación"),
            new("Nombre Apellido", "Desarrollo y sistemas", "Tecnología"),
        };

        public const string MapaUrl = "https://www.google.com/maps/search/?api=1&query=Prat+814,+Valpara%C3%ADso,+Chile";
    }
}
