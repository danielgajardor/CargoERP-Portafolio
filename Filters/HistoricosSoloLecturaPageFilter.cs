using System;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtilsonCargoSpa.Filters
{
    /// <summary>
    /// Las operaciones importadas desde Excel representan historia anterior al ERP.
    /// Pueden consultarse en /Operaciones/Historicos y en Finanzas, pero jamás deben
    /// entrar a los flujos editables de Operaciones.
    /// </summary>
    public sealed class HistoricosSoloLecturaPageFilter : IAsyncPageFilter
    {
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            string pagina = context.ActionDescriptor.ViewEnginePath ?? "";
            if (!pagina.StartsWith("/Operaciones/", StringComparison.OrdinalIgnoreCase) ||
                pagina.Equals("/Operaciones/Historicos", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var req = context.HttpContext.Request;

            string? Valor(params string[] claves)
            {
                foreach (var k in claves)
                {
                    if (req.HasFormContentType && req.Form.TryGetValue(k, out var f) && !string.IsNullOrWhiteSpace(f.FirstOrDefault()))
                        return f.FirstOrDefault();
                    if (req.Query.TryGetValue(k, out var q) && !string.IsNullOrWhiteSpace(q.FirstOrDefault()))
                        return q.FirstOrDefault();
                    if (context.RouteData.Values.TryGetValue(k, out var r) && r != null && !string.IsNullOrWhiteSpace(r.ToString()))
                        return r.ToString();
                }
                return null;
            }

            int? ParseId(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return null;
                string limpio = raw.Replace("sub-", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("v3-", "", StringComparison.OrdinalIgnoreCase);
                return int.TryParse(limpio, out var id) ? id : null;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<AtilsonContext>();
            int? idOp = ParseId(Valor("IdOperacion", "idOperacion", "operacionId", "idOp", "Operacione.Id", "id"));

            if (!idOp.HasValue && int.TryParse(Valor("idRegistro"), out int idRegistro))
                idOp = await db.RoleosCancelaciones.AsNoTracking()
                    .Where(x => x.Id == idRegistro).Select(x => (int?)x.IdOperacion).FirstOrDefaultAsync();

            if (!idOp.HasValue && int.TryParse(Valor("idCosto"), out int idCosto))
                idOp = await db.ExtracostosOperacions.AsNoTracking()
                    .Where(x => x.Id == idCosto).Select(x => (int?)x.IdOperacion).FirstOrDefaultAsync();

            if (!idOp.HasValue && int.TryParse(Valor("transaccionId"), out int idTx))
                idOp = await db.TransaccionesFinancieras.AsNoTracking()
                    .Where(x => x.Id == idTx).Select(x => (int?)x.IdOperacion).FirstOrDefaultAsync();

            if (!idOp.HasValue)
            {
                await next();
                return;
            }

            bool historica;
            try
            {
                historica = await db.Operaciones.AsNoTracking()
                    .Where(o => o.Id == idOp.Value)
                    .Select(o => o.EsHistorica)
                    .FirstOrDefaultAsync();
            }
            catch
            {
                // La migración todavía no fue ejecutada. No interfiere con la app antigua.
                await next();
                return;
            }

            if (!historica)
            {
                await next();
                return;
            }

            if (HttpMethods.IsGet(req.Method) &&
                (pagina.Equals("/Operaciones/Detalle", StringComparison.OrdinalIgnoreCase) ||
                 pagina.Equals("/Operaciones/Edit", StringComparison.OrdinalIgnoreCase) ||
                 pagina.Equals("/Operaciones/SolicitudProveedor", StringComparison.OrdinalIgnoreCase)))
            {
                context.Result = new RedirectToPageResult("/Operaciones/Historicos");
                return;
            }

            if (!HttpMethods.IsPost(req.Method))
            {
                await next();
                return;
            }

            context.Result = new JsonResult(new
            {
                success = false,
                historica = true,
                message = "Esta operación es histórica y es de solo lectura. No se puede gestionar ni modificar desde Operaciones."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }
    }
}
