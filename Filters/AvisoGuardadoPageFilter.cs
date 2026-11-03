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

            if (ejecutado.Exception is DbUpdateException dbEx && !ejecutado.ExceptionHandled)
            {
                var log = http.RequestServices.GetService<ILogger<AvisoGuardadoPageFilter>>();
                log?.LogError(dbEx, "Guardado rechazado por la base de datos en {Pagina}", pagina);
                string detalle = (dbEx.InnerException ?? dbEx).Message;
                if (detalle.Length > 220) detalle = detalle.Substring(0, 220) + "…";
                tempData["ToastError"] = "La base de datos rechazó el guardado. Detalle: " + detalle;
                ejecutado.ExceptionHandled = true;
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
