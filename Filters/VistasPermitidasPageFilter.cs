using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AtilsonCargoSpa.Filters
{
    // Vistas por usuario (13-nov-2026): una persona del equipo solo entra a las vistas que tiene
    // (las elegidas por el administrador o, sin selección, las de su rol). Las consultas GET con
    // handler (datos que una página pide a otra, p. ej. las naves de una ruta) no se bloquean.
    public class VistasPermitidasPageFilter : IAsyncPageFilter
    {
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true && !user.IsInRole("Cliente"))
            {
                var claves = VistasCatalogo.ClavesDePagina(context.ActionDescriptor.ViewEnginePath);
                bool consultaAuxiliar = HttpMethods.IsGet(context.HttpContext.Request.Method) && !string.IsNullOrEmpty(context.HandlerMethod?.Name);
                if (claves != null && !consultaAuxiliar && !claves.Any(c => PermisosEquipo.PuedeVer(user, c)))
                {
                    context.Result = new RedirectResult("/App/Dashboard?sinAcceso=1");
                    return;
                }
            }
            await next();
        }
    }
}
