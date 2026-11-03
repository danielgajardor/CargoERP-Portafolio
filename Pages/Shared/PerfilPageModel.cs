using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.Shared
{
    // Base de "Mi perfil" (oct-2026). La usan /App/Perfil (equipo) y /PortalCliente/Perfil
    // (clientes); la vista común está en Pages/Shared/_Perfil.cshtml.
    public abstract class PerfilPageModel : PageModel
    {
        protected readonly AtilsonContext Context;
        protected readonly PerfilUsuarioService Perfiles;

        protected PerfilPageModel(AtilsonContext context, PerfilUsuarioService perfiles)
        {
            Context = context;
            Perfiles = perfiles;
        }

        /// <summary>true en el portal de clientes (muestra los datos de la empresa).</summary>
        public abstract bool EsCliente { get; }

        public Usuario Usuario { get; private set; } = null!;
        public PerfilUsuario? Perfil { get; private set; }
        public bool FaltaScript { get; private set; }
        public Cliente? Empresa { get; private set; }

        [TempData] public string? Aviso { get; set; }
        [TempData] public string? AvisoError { get; set; }
        [TempData] public string? Seccion { get; set; }
        public string? CampoError { get; set; }
        public string? ErrorPassword { get; set; }

        private async Task<bool> CargarAsync()
        {
            var u = await Perfiles.UsuarioActualAsync(User);
            if (u == null) return false;
            Usuario = u;
            Perfil = await Perfiles.ObtenerAsync(u.Id);
            FaltaScript = !await Perfiles.TablaDisponibleAsync();
            if (EsCliente && u.IdCliente is int idCli && idCli > 0)
                Empresa = await Context.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == idCli);
            return true;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await CargarAsync()) return RedirectToPage("/Login");
            return Page();
        }

        public async Task<IActionResult> OnPostDatosAsync(string? nombre, string? telefono, string? cargo)
        {
            if (!await CargarAsync()) return RedirectToPage("/Login");
            var r = await Perfiles.GuardarDatosAsync(Usuario, nombre, telefono, cargo);
            if (r.Ok) await RefrescarSesionAsync(Usuario.NombreCompleto);
            return Volver(r.Ok, r.Mensaje, "datos");
        }

        public async Task<IActionResult> OnPostFotoAsync(IFormFile? foto)
        {
            if (!await CargarAsync()) return RedirectToPage("/Login");
            var r = await Perfiles.GuardarFotoAsync(Usuario, foto);
            return Volver(r.Ok, r.Mensaje, "foto");
        }

        public async Task<IActionResult> OnPostQuitarFotoAsync()
        {
            if (!await CargarAsync()) return RedirectToPage("/Login");
            var r = await Perfiles.QuitarFotoAsync(Usuario);
            return Volver(r.Ok, r.Mensaje, "foto");
        }

        public async Task<IActionResult> OnPostPasswordAsync(string? passwordActual, string? passwordNueva, string? passwordConfirmar)
        {
            if (!await CargarAsync()) return RedirectToPage("/Login");
            var r = await Perfiles.CambiarPasswordAsync(Usuario, passwordActual, passwordNueva, passwordConfirmar);
            if (!r.Ok)
            {
                // Se queda en la página (sin redirigir) para marcar el campo con error.
                ErrorPassword = r.Mensaje;
                CampoError = r.Campo;
                return Page();
            }
            return Volver(true, r.Mensaje, "seguridad");
        }

        private IActionResult Volver(bool ok, string mensaje, string seccion)
        {
            if (ok) Aviso = mensaje; else AvisoError = mensaje;
            Seccion = seccion;
            return RedirectToPage(null, null, null, seccion);
        }

        /// <summary>Vuelve a emitir la cookie para que el menú muestre el nombre nuevo sin cerrar sesión.</summary>
        private async Task RefrescarSesionAsync(string nombre)
        {
            if (User.Identity is not ClaimsIdentity actual || !User.Identity.IsAuthenticated) return;
            var claims = actual.Claims.Where(c => c.Type != ClaimTypes.Name).ToList();
            claims.Add(new Claim(ClaimTypes.Name, nombre));
            try
            {
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            }
            catch (InvalidOperationException)
            {
                // Sin esquema de cookies (entorno de pruebas): el nombre se verá al volver a entrar.
            }
        }
    }
}
