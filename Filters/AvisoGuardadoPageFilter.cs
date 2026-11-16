using System;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Filters
{
    // Aviso de guardado (oct-2026) para Comercial y Maestros (/Comercial y /App).
    //
    // Cuando un formulario se envía y la página redirige, este filtro deja el aviso que
    // muestra _AvisosFlash en la pantalla siguiente:
    //   - si el handler ya dejó su propio mensaje (ToastOk/ToastError/ToastInfo), se respeta;
    //   - si dejó TempData["ErrorMsg"] (mensajes antiguos de Comercial que no se veían),
    //     se muestra como error;
    //   - si no dejó mensaje: "Cambios guardados" cuando realmente se grabó algo en la base
    //     de datos, o "No hubo cambios que guardar" cuando no se grabó nada.
    // Si la base de datos rechaza el guardado (DbUpdateException), en vez de la página de
    // error se vuelve a la pantalla anterior con el motivo.
    public class AvisoGuardadoPageFilter : IAsyncPageFilter
    {
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        private static bool Aplica(string pagina) =>
            pagina.StartsWith("/Comercial/", StringComparison.OrdinalIgnoreCase)
            || pagina.StartsWith("/App/", StringComparison.OrdinalIgnoreCase);

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var http = context.HttpContext;
            string pagina = context.ActionDescriptor.ViewEnginePath ?? "";
            if (!HttpMethods.IsPost(http.Request.Method) || !Aplica(pagina))
            {
                await next();
                return;
            }

            var db = http.RequestServices.GetService<AtilsonContext>();
            int guardados = 0;
            EventHandler<SavedChangesEventArgs>? contar = (_, e) => guardados += e.EntitiesSavedCount;
            if (db != null) db.SavedChanges += contar;

            PageHandlerExecutedContext ejecutado;
            try
            {
                ejecutado = await next();
            }
            finally
            {
                if (db != null) db.SavedChanges -= contar;
            }

            var tempData = http.RequestServices.GetService<ITempDataDictionaryFactory>()?.GetTempData(http);
            if (tempData == null) return;

            if (ejecutado.Exception != null && !ejecutado.ExceptionHandled)
            {
                // nov-2026: además del rechazo de la base de datos, cualquier error al guardar
                // vuelve a la pantalla con un mensaje entendible en vez de la página de error.
                var log = http.RequestServices.GetService<ILogger<AvisoGuardadoPageFilter>>();
                log?.LogError(ejecutado.Exception, "Error al guardar en {Pagina}", pagina);
                string mensaje = ejecutado.Exception is DbUpdateException dbEx
                    ? "No se pudo guardar: " + TraducirErrorBd(dbEx)
                    : "No se pudo guardar por un error inesperado. Revisa los datos e inténtalo de nuevo; si se repite, avisa a Informática.";
                ejecutado.ExceptionHandled = true;
                if (EsPeticionFetch(http))
                {
                    ejecutado.Result = new JsonResult(new { success = false, message = mensaje }) { StatusCode = 200 };
                    return;
                }
                tempData["ToastError"] = mensaje;
                ejecutado.Result = new RedirectResult(UrlDeVuelta(http));
                return;
            }

            bool redirige = ejecutado.Result is RedirectToPageResult || ejecutado.Result is RedirectResult
                            || ejecutado.Result is LocalRedirectResult || ejecutado.Result is RedirectToActionResult;
            if (!redirige) return;

            if (tempData.ContainsKey("ToastOk") || tempData.ContainsKey("ToastError") || tempData.ContainsKey("ToastInfo")) return;

            if (tempData.Peek("ErrorMsg") is string error && !string.IsNullOrWhiteSpace(error))
            {
                tempData.Remove("ErrorMsg");
                tempData["ToastError"] = error;
                return;
            }

            if (tempData.Peek("SuccessMsg") is string ok && !string.IsNullOrWhiteSpace(ok))
            {
                tempData.Remove("SuccessMsg");
                tempData["ToastOk"] = ok;
                return;
            }

            if (guardados > 0) tempData["ToastOk"] = "Cambios guardados correctamente.";
            else tempData["ToastInfo"] = "No hubo cambios que guardar.";
        }

        // Las llamadas fetch() de las vistas envían el token en el header y esperan JSON.
        private static bool EsPeticionFetch(HttpContext http) =>
            http.Request.Headers.ContainsKey("RequestVerificationToken")
            || http.Request.Headers.XRequestedWith.ToString() == "XMLHttpRequest"
            || http.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

        // Mensajes de SQL Server más comunes, traducidos a lo que el usuario puede corregir.
        private static string TraducirErrorBd(DbUpdateException ex)
        {
            string sql = (ex.InnerException ?? ex).Message;
            string Columna(string patron)
            {
                var m = System.Text.RegularExpressions.Regex.Match(sql, patron);
                return m.Success ? m.Groups[1].Value : "";
            }
            if (sql.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
                return "falta seleccionar un dato obligatorio de las listas (naviera, cliente, proveedor, agencia o ciudad).";
            if (sql.Contains("Cannot insert the value NULL", StringComparison.OrdinalIgnoreCase))
            {
                string col = Columna(@"column '([^']+)'");
                return col == "" ? "falta completar un campo obligatorio." : $"falta completar el campo «{col}».";
            }
            if (sql.Contains("would be truncated", StringComparison.OrdinalIgnoreCase))
            {
                string col = Columna(@"column '([^']+)'");
                return col == "" ? "un texto es demasiado largo." : $"el texto del campo «{col}» es demasiado largo.";
            }
            if (sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || sql.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                return "ya existe un registro con esos mismos datos.";
            if (sql.Contains("out-of-range", StringComparison.OrdinalIgnoreCase) || sql.Contains("Arithmetic overflow", StringComparison.OrdinalIgnoreCase))
                return "una fecha o un monto está fuera de rango. Revisa las fechas de vigencia y los valores.";
            if (sql.Length > 220) sql = sql.Substring(0, 220) + "…";
            return "la base de datos rechazó el guardado. Detalle: " + sql;
        }

        private static string UrlDeVuelta(HttpContext http)
        {
            var referer = http.Request.Headers.Referer.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)
                && string.Equals(uri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
                return uri.PathAndQuery;
            return http.Request.Path.HasValue ? http.Request.Path.Value! : "/";
        }
    }
}
