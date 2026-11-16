using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Clientes
{
    /// <summary>Solicitudes de registro de empresas hechas desde el portal (oct-2026).</summary>
    public class SolicitudesModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly RegistroClientesService _registro;

        public SolicitudesModel(AtilsonContext context, RegistroClientesService registro)
        {
            _context = context;
            _registro = registro;
        }

        public sealed class Fila
        {
            public SolicitudRegistroCliente Solicitud { get; init; } = null!;
            public string? Comuna { get; init; }
            public string? Bloqueo { get; init; }
        }

        public List<Fila> Filas { get; set; } = new();
        public int Pendientes { get; set; }

        /// <summary>pendientes | revisadas</summary>
        [BindProperty(SupportsGet = true)]
        public string Estado { get; set; } = "pendientes";

        public async Task OnGetAsync()
        {
            Estado = Estado == "revisadas" ? "revisadas" : "pendientes";
            var ciudades = await _context.Ciudades.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre);
            var todas = await _context.SolicitudesRegistroCliente.AsNoTracking().ToListAsync();
            Pendientes = todas.Count(s => s.Estado == SolicitudRegistroCliente.EstadoPendiente);
            var visibles = Estado == "pendientes"
                ? todas.Where(s => s.Estado == SolicitudRegistroCliente.EstadoPendiente).OrderBy(s => s.FechaSolicitud)
                : todas.Where(s => s.Estado != SolicitudRegistroCliente.EstadoPendiente).OrderByDescending(s => s.FechaRevision ?? s.FechaSolicitud);
            foreach (var s in visibles)
            {
                Filas.Add(new Fila
                {
                    Solicitud = s,
                    Comuna = s.IdCiudadPlanta.HasValue ? ciudades.GetValueOrDefault(s.IdCiudadPlanta.Value) : null,
                    Bloqueo = s.Estado == SolicitudRegistroCliente.EstadoPendiente ? await _registro.MotivoBloqueoAsync(s.Rut, s.Correo, s.Id) : null
                });
            }
        }

        public async Task<IActionResult> OnPostCompletarAsync(int id, string? razonSocial, string? rut, string? direccionOficina, string? passwordTemporal)
        {
            var s = await _context.SolicitudesRegistroCliente.FirstOrDefaultAsync(x => x.Id == id);
            if (s == null)
            {
                TempData["ToastError"] = "La solicitud ya no existe.";
                return RedirectToPage();
            }
            if (s.Estado != SolicitudRegistroCliente.EstadoPendiente)
            {
                TempData["ToastError"] = "Solo se pueden completar solicitudes pendientes.";
                return RedirectToPage();
            }

            razonSocial = string.IsNullOrWhiteSpace(razonSocial) ? "" : razonSocial.Trim();
            rut = string.IsNullOrWhiteSpace(rut) ? "" : rut.Trim();
            direccionOficina = string.IsNullOrWhiteSpace(direccionOficina) ? "" : direccionOficina.Trim();
            passwordTemporal = string.IsNullOrWhiteSpace(passwordTemporal) ? null : passwordTemporal;

            if (razonSocial.Length > 100)
            {
                TempData["ToastError"] = "La razón social admite hasta 100 caracteres.";
                return RedirectToPage();
            }
            if (rut.Length > 12)
            {
                TempData["ToastError"] = "El RUT admite hasta 12 caracteres.";
                return RedirectToPage();
            }
            if (direccionOficina.Length > 255)
            {
                TempData["ToastError"] = "La dirección admite hasta 255 caracteres.";
                return RedirectToPage();
            }
            if (rut.Length > 0 && !AtilsonCargoSpa.Helpers.RutHelper.EsValido(rut))
            {
                TempData["ToastError"] = "El RUT ingresado no es válido.";
                return RedirectToPage();
            }
            if (passwordTemporal != null &&
                (passwordTemporal.Length < 8 || !passwordTemporal.Any(char.IsLetter) || !passwordTemporal.Any(char.IsDigit)))
            {
                TempData["ToastError"] = "La contraseña temporal debe tener al menos 8 caracteres, con letras y números.";
                return RedirectToPage();
            }

            var bloqueo = await _registro.MotivoBloqueoAsync(rut, s.Correo, s.Id);
            if (bloqueo != null)
            {
                TempData["ToastError"] = bloqueo;
                return RedirectToPage();
            }

            s.RazonSocial = razonSocial;
            s.Rut = rut.Length == 0 ? "" : AtilsonCargoSpa.Helpers.RutHelper.Formatear(rut);
            s.DireccionOficina = direccionOficina;
            if (passwordTemporal != null)
                s.PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordTemporal);

            await _context.SaveChangesAsync();
            TempData["ToastOk"] = passwordTemporal != null
                ? "Datos actualizados. Comunica la contraseña temporal al cliente por un canal seguro: el correo de aprobación no la incluye."
                : "Datos de la solicitud actualizados.";
            return RedirectToPage(new { Estado });
        }

        public async Task<IActionResult> OnPostAprobarAsync(int id)
        {
            var (ok, mensaje) = await _registro.AprobarAsync(id, MaestrosAuditoria.Usuario(User), Url.Page("/Login", null, null, Request.Scheme) ?? "/Login");
            TempData[ok ? "ToastOk" : "ToastError"] = mensaje;
            return RedirectToPage();
        }

        /// <summary>Correo que recibe el aviso de cada registro nuevo (para mostrarlo en pantalla).</summary>
        public string? CorreoAviso => _registro.CorreoAvisoInterno;

        /// <summary>Envía un correo de prueba al usuario conectado para revisar la configuración SMTP.</summary>
        public async Task<IActionResult> OnPostProbarCorreoAsync()
        {
            string? destino = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(destino)) destino = _registro.CorreoAvisoInterno;
            if (string.IsNullOrWhiteSpace(destino))
            {
                TempData["ToastError"] = "No hay un correo de destino: tu usuario no tiene correo y no está configurado Smtp:Sender.";
                return RedirectToPage();
            }
            var (ok, mensaje) = await _registro.ProbarCorreoAsync(destino);
            TempData[ok ? "ToastOk" : "ToastError"] = mensaje;
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRechazarAsync(int id, string? motivo)
        {
            var (ok, mensaje) = await _registro.RechazarAsync(id, motivo ?? "", MaestrosAuditoria.Usuario(User));
            TempData[ok ? "ToastOk" : "ToastError"] = mensaje;
            return RedirectToPage();
        }
    }
}
