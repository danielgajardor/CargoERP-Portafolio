using System;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtilsonCargoSpa.Filters
{
    // Traspaso Ops -> Finanzas (oct-2026): con la tarea finalizada, los handlers de
    // /Operaciones que cambian costos o cobros se rechazan hasta que Operaciones
    // reabra el booking (ver TraspasoFinanzas.HandlersBloqueados). Se aplica de forma
    // global, como ValidacionArchivosPageFilter, para que ningún handler nuevo o
    // pantalla se salte el bloqueo.
    //   - Petición desde JS (fetch): responde 409 con { success = false, message } y el
    //     encabezado X-Tarea-Bloqueada; wwwroot/js/traspaso-finanzas.js muestra el aviso.
    //   - Envío normal de formulario: vuelve a la página anterior con el mensaje.
    public class TraspasoFinanzasPageFilter : IAsyncPageFilter
    {
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var http = context.HttpContext;
            var request = http.Request;
            string pagina = context.ActionDescriptor.ViewEnginePath ?? "";

            if (!HttpMethods.IsPost(request.Method) || !pagina.StartsWith("/Operaciones/", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            string handler = context.HandlerMethod?.Name ?? "";
            bool esEditCompleto = pagina.Equals("/Operaciones/Edit", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(handler);
            bool esCambioEstado = handler.Equals("UpdateWorkflow", StringComparison.OrdinalIgnoreCase);
            if (!esEditCompleto && !esCambioEstado && !TraspasoFinanzas.HandlersBloqueados.Contains(handler))
            {
                await next();
                return;
            }

            string? Valor(params string[] claves)
            {
                foreach (var k in claves)
                {
                    if (request.HasFormContentType && request.Form.TryGetValue(k, out var f) && !string.IsNullOrWhiteSpace(f.FirstOrDefault())) return f.FirstOrDefault();
                    if (request.Query.TryGetValue(k, out var q) && !string.IsNullOrWhiteSpace(q.FirstOrDefault())) return q.FirstOrDefault();
                    if (context.RouteData.Values.TryGetValue(k, out var r) && r != null && !string.IsNullOrWhiteSpace(r.ToString())) return r.ToString();
                }
                return null;
            }

            if (esCambioEstado && TraspasoFinanzas.EstadoPermitidoConTareaFinalizada(Valor("estado")))
            {
                await next();
                return;
            }

            var db = http.RequestServices.GetRequiredService<AtilsonContext>();
            int? idOp = null;
            if (int.TryParse(Valor("IdOperacion", "idOp", "Operacione.Id", "id"), out var id)) idOp = id;
            else if (int.TryParse(Valor("idRegistro"), out var idRegistro))
                idOp = await db.RoleosCancelaciones.AsNoTracking().Where(r => r.Id == idRegistro).Select(r => (int?)r.IdOperacion).FirstOrDefaultAsync();
            else if (int.TryParse(Valor("idCosto"), out var idCosto))
                idOp = await db.ExtracostosOperacions.AsNoTracking().Where(c => c.Id == idCosto).Select(c => (int?)c.IdOperacion).FirstOrDefaultAsync();

            if (idOp == null)
            {
                await next();
                return;
            }

            string? estadoTarea;
            try
            {
                estadoTarea = await db.Operaciones.AsNoTracking().Where(o => o.Id == idOp.Value).Select(o => o.EstadoTarea).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                // Script sin correr: no bloquea nada (la app tampoco podría leer la operación).
                System.Diagnostics.Trace.TraceWarning($"Traspaso Ops->Finanzas: no se pudo leer EstadoTarea: {ex.Message}");
                await next();
                return;
            }

            if (estadoTarea != TraspasoFinanzas.EstadoFinalizada)
            {
                await next();
                return;
            }

            string mensaje = TraspasoFinanzas.MensajeBloqueo;
            bool esNavegacion = string.Equals(request.Headers["Sec-Fetch-Mode"], "navigate", StringComparison.OrdinalIgnoreCase);
            if (esNavegacion)
            {
                var tempData = http.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
                tempData["ErrorMsg"] = mensaje;
                string referer = request.Headers["Referer"].ToString();
                context.Result = Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == request.Host.Host
                    ? new RedirectResult(uri.PathAndQuery)
                    : new RedirectResult($"/Operaciones/Detalle/{idOp.Value}");
                return;
            }

            http.Response.Headers["X-Tarea-Bloqueada"] = idOp.Value.ToString();
            context.Result = new JsonResult(new { success = false, bloqueadaPorTraspaso = true, idOperacion = idOp.Value, message = mensaje })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }
    }
}
