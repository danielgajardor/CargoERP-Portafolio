using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    /// <summary>
    /// Mis plantas y depósitos (oct-2026): el cliente registra sus plantas y depósitos para
    /// elegirlos en sus solicitudes. Se pueden usar de inmediato; Atilson los valida en
    /// Maestros. Mientras no estén validados, el cliente los puede corregir o quitar.
    /// </summary>
    public class MisUbicacionesModel : PageModel
    {
        private readonly AtilsonContext _context;

        public MisUbicacionesModel(AtilsonContext context)
        {
            _context = context;
        }

        public sealed class FormPlanta
        {
            public int Id { get; set; }

            [Required(ErrorMessage = "Escribe el nombre de la planta.")]
            [StringLength(200, ErrorMessage = "El nombre admite hasta 200 caracteres.")]
            public string? Nombre { get; set; }

            [Required(ErrorMessage = "Elige la comuna (zona) de la planta.")]
            public int? IdCiudad { get; set; }

            [Required(ErrorMessage = "Escribe la dirección de la planta.")]
            [StringLength(500, ErrorMessage = "La dirección admite hasta 500 caracteres.")]
            public string? Direccion { get; set; }

            [Required(ErrorMessage = "Pega el enlace de Google Maps de la planta.")]
            [StringLength(1000, ErrorMessage = "El enlace admite hasta 1000 caracteres.")]
            public string? UrlGoogleMaps { get; set; }
        }

        

        [BindProperty]
        public FormPlanta Planta { get; set; } = new();

       

        public List<Planta> Plantas { get; set; } = new();
        public Dictionary<int, string> Ciudades { get; set; } = new();
        public List<SelectListItem> Zonas { get; set; } = new();

        /// <summary>Qué formulario abrir: "planta" o "deposito".</summary>
        /// <summary> Cu
        public string? FormularioAbierto { get; set; }

        private int? IdCliente()
        {
            var claim = User.FindFirst("IdCliente")?.Value;
            return int.TryParse(claim, out var id) && id > 0 ? id : null;
        }

        private string Usuario() => User.Identity?.Name?.Trim() is { Length: > 0 } n ? n : "Cliente";

        public async Task<IActionResult> OnGetAsync(int? editarPlanta, string? nuevo)
        {
            var idCliente = IdCliente();
            if (idCliente == null) return RedirectToPage("/Login");

            if (editarPlanta.HasValue)
            {
                var p = await _context.Plantas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == editarPlanta && x.IdCliente == idCliente && x.Activo);
                if (p != null && !p.Validada)
                {
                    Planta = new FormPlanta { Id = p.Id, Nombre = p.Nombre, IdCiudad = p.IdCiudad, Direccion = p.Direccion, UrlGoogleMaps = p.UrlGoogleMaps };
                    FormularioAbierto = "planta";
                }
            }
            if (nuevo == "planta") FormularioAbierto ??= nuevo;

            await CargarAsync(idCliente.Value);
            return Page();
        }

        public async Task<IActionResult> OnPostGuardarPlantaAsync()
        {
            var idCliente = IdCliente();
            if (idCliente == null) return RedirectToPage("/Login");
            SoloValidar("Planta");

            Planta.Nombre = Limpio(Planta.Nombre);
            Planta.Direccion = Limpio(Planta.Direccion);
            Planta.UrlGoogleMaps = Limpio(Planta.UrlGoogleMaps);

            Planta? existente = null;
            if (Planta.Id > 0)
            {
                existente = await _context.Plantas.FirstOrDefaultAsync(x => x.Id == Planta.Id && x.IdCliente == idCliente && x.Activo);
                if (existente == null) return NotFound();
                if (existente.Validada)
                {
                    TempData["ToastError"] = "Esta planta ya fue validada por Atilson. Para cambiarla, escríbenos.";
                    return RedirectToPage();
                }
            }

            if (Planta.IdCiudad.HasValue && !await _context.Ciudades.AnyAsync(c => c.Id == Planta.IdCiudad.Value))
                ModelState.AddModelError("Planta.IdCiudad", "Elige una comuna de la lista.");
            if (Planta.UrlGoogleMaps != null && !EsUrl(Planta.UrlGoogleMaps))
                ModelState.AddModelError("Planta.UrlGoogleMaps", "Pega el enlace completo de Google Maps (empieza con https://).");
            if (Planta.Nombre != null)
            {
                var motivo = await PortalUbicacionesHelper.ValidarNombrePlantaAsync(_context, Planta.Nombre, idCliente.Value, existente?.Id);
                if (motivo != null) ModelState.AddModelError("Planta.Nombre", motivo);
            }

            if (!ModelState.IsValid)
            {
                FormularioAbierto = "planta";
                await CargarAsync(idCliente.Value);
                return Page();
            }

            string usuario = Usuario();
            if (existente == null)
            {
                var nueva = PortalUbicacionesHelper.NuevaPlanta(idCliente.Value, Planta.Nombre!, Planta.IdCiudad!.Value, Planta.Direccion, Planta.UrlGoogleMaps, usuario);
                _context.Plantas.Add(nueva);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntPlanta, nueva.Id, "Registro creado", $"{usuario} (portal)", "Registrada por el cliente en el portal; por validar");
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Planta «{nueva.Nombre}» agregada. Ya puedes elegirla en tus solicitudes; Atilson revisará sus datos.";
            }
            else
            {
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Comparar("Nombre", existente.Nombre, Planta.Nombre);
                cambios.Comparar("Ciudad", await NombreCiudad(existente.IdCiudad), await NombreCiudad(Planta.IdCiudad));
                cambios.Comparar("Dirección", existente.Direccion, Planta.Direccion);
                cambios.Comparar("Enlace Google Maps", existente.UrlGoogleMaps, Planta.UrlGoogleMaps);
                existente.Nombre = Planta.Nombre!;
                existente.IdCiudad = Planta.IdCiudad!.Value;
                existente.Direccion = Planta.Direccion;
                existente.UrlGoogleMaps = Planta.UrlGoogleMaps;
                existente.FechaModificacion = MaestrosAuditoria.Ahora();
                existente.UsuarioModificador = Recortar(usuario, 90) + " (portal)";
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntPlanta, existente.Id, cambios, $"{usuario} (portal)", "Edición del cliente en el portal");
                await _context.SaveChangesAsync();
                TempData[cambios.Hay ? "ToastOk" : "ToastInfo"] = cambios.Hay ? $"Planta «{existente.Nombre}» actualizada." : "No había cambios que guardar.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostQuitarPlantaAsync(int id)
        {
            var idCliente = IdCliente();
            if (idCliente == null) return RedirectToPage("/Login");
            var p = await _context.Plantas.FirstOrDefaultAsync(x => x.Id == id && x.IdCliente == idCliente && x.Activo);
            if (p == null) return NotFound();
            p.Activo = false;
            p.FechaModificacion = MaestrosAuditoria.Ahora();
            p.UsuarioModificador = Recortar(Usuario(), 90) + " (portal)";
            MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntPlanta, p.Id, "Quitada por el cliente", $"{Usuario()} (portal)", "El cliente la quitó de sus plantas en el portal");
            await _context.SaveChangesAsync();
            TempData["ToastOk"] = $"Planta «{p.Nombre}» quitada de tus plantas.";
            return RedirectToPage();
        }

        

        private async Task CargarAsync(int idCliente)
        {
            Plantas = await PortalUbicacionesHelper.PlantasDelClienteAsync(_context, idCliente);
           
            Ciudades = await _context.Ciudades.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre);
            Zonas = await PortalUbicacionesHelper.ZonasAsync(_context);
        }

        /// <summary>
        /// La página tiene dos formularios (planta y depósito); al enviar uno, el otro llega
        /// vacío y el binder lo valida sin prefijo. Solo cuentan los campos del formulario enviado.
        /// Ultimoas vistas a la página diferenciar los servicios (marítimo, terrestre y documentoa
        /// Configurar credenciales.
        /// Chatbot automatizado landing page
      
        private void SoloValidar(string prefijo)
        {
            foreach (var k in ModelState.Keys.Where(k => !k.StartsWith(prefijo + ".")).ToList()) ModelState.Remove(k);
        }

        private async Task<string?> NombreCiudad(int? id) => id.HasValue ? await _context.Ciudades.AsNoTracking().Where(c => c.Id == id).Select(c => c.Nombre).FirstOrDefaultAsync() : null;

        private static bool EsUrl(string s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
        private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static string Recortar(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
