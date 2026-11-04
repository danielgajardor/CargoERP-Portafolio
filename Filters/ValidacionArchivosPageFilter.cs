using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AtilsonCargoSpa.Filters
{
    // Validación CENTRALIZADA de cualquier archivo subido, en cualquier página/handler de la
    // aplicación. Antes de este filtro, cada handler de Operaciones (~15 de ellos, entre
    // Index.cshtml.cs, Detalle.cshtml.cs, Create.cshtml.cs, Edit.cshtml.cs y Archivos.cshtml.cs)
    // guardaba el IFormFile recibido directamente en wwwroot/uploads/... sin revisar su
    // extensión, tipo ni tamaño. Como wwwroot se sirve como contenido estático (UseStaticFiles),
    // eso permitía subir un .html o .svg con <script> embebido y que quedara servido tal cual
    // desde el propio sitio (XSS almacenado), además de no tener ningún tope de tamaño por
    // archivo (riesgo de llenar el disco).
    //
    // Se aplica una sola vez, de forma global, como convención de Razor Pages (ver Program.cs:
    // options.Conventions.ConfigureFilter(new ValidacionArchivosPageFilter())), en vez de tocar
    // cada uno de esos handlers por separado — así ningún handler nuevo puede "olvidarse" de
    // validar. Si algún día se necesita un tipo de archivo fuera de esta lista para un flujo
    // puntual, es mejor ampliar ExtensionesPermitidas aquí que desactivar la validación.
    public class ValidacionArchivosPageFilter : IAsyncPageFilter
    {
        private static readonly HashSet<string> ExtensionesPermitidas = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
            ".doc", ".docx", ".xls", ".xlsx", ".msg", ".eml"
        };

        private const long TamanoMaximoBytes = 20L * 1024 * 1024; // 20 MB por archivo

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var request = context.HttpContext.Request;

            if (request.HasFormContentType)
            {
                var archivos = request.Form.Files;
                foreach (var archivo in archivos)
                {
                    if (archivo == null || archivo.Length == 0) continue;

                    if (archivo.Length > TamanoMaximoBytes)
                    {
                        context.Result = ResultadoRechazo(context,
                            $"El archivo '{archivo.FileName}' supera el tamaño máximo permitido ({TamanoMaximoBytes / 1024 / 1024} MB).");
                        return;
                    }

                    string extension = Path.GetExtension(archivo.FileName);
                    if (string.IsNullOrEmpty(extension) || !ExtensionesPermitidas.Contains(extension))
                    {
                        context.Result = ResultadoRechazo(context,
                            $"Tipo de archivo no permitido para '{archivo.FileName}'. Extensiones aceptadas: {string.Join(", ", ExtensionesPermitidas)}.");
                        return;
                    }
                }
            }

            await next();
        }

        private static IActionResult ResultadoRechazo(PageHandlerExecutingContext context, string mensaje)
        {
            // La mayoría de los handlers de subida en este proyecto son llamados vía fetch/AJAX
            // y ya saben interpretar { success:false, message:"..." }. Para un POST de formulario
            // tradicional (sin JS), el navegador mostrará este JSON como texto plano en vez de la
            // página de error habitual — no es la UX ideal, pero bloquea el archivo igual.
            return new JsonResult(new { success = false, message = mensaje }) { StatusCode = 400 };
        }
    }
}
