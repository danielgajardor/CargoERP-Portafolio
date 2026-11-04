using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Plantas
{
    /// <summary>
    /// Ficha de planta: crea (sin id) o edita (con id) en la tabla Plantas, que es la que
    /// usan Operaciones y el cruce de tarifas (antes "Nueva planta" grababa en la tabla
    /// antigua Origenescarga y la planta nunca aparecía).
    /// </summary>
    public class EditModel : PageModel
    {
        private readonly AtilsonContext _context;

        public EditModel(AtilsonContext context)
        {
            _context = context;
        }

        public sealed class Formulario
        {
            public int Id { get; set; }

            [Required(ErrorMessage = "Escribe el nombre de la planta.")]
            [StringLength(200, ErrorMessage = "El nombre admite hasta 200 caracteres.")]
            public string? Nombre { get; set; }

            [Required(ErrorMessage = "Elige la ciudad de la planta: sin ella no se encuentra la tarifa de transporte.")]
            public int? IdCiudad { get; set; }

            public int? IdCliente { get; set; }

            [StringLength(500, ErrorMessage = "La dirección admite hasta 500 caracteres.")]
            public string? Direccion { get; set; }

            [StringLength(1000, ErrorMessage = "El enlace admite hasta 1000 caracteres.")]
            public string? UrlGoogleMaps { get; set; }

            public bool Activo { get; set; } = true;

            public long Version { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public bool EsNuevo => Form.Id == 0;
        public string? NombreOriginal { get; set; }
        public List<SelectListItem> Ciudades { get; set; } = new();
        public List<SelectListItem> Clientes { get; set; } = new();
        public MaestroAuditoriaVm Registro { get; set; } = new();

        private static long VersionDe(Planta p) => (p.FechaModificacion ?? p.FechaCreacion).Ticks;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id.HasValue)
            {
                var p = await _context.Plantas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value);
                if (p == null)
                {
                    TempData["ToastError"] = "La planta que intentas abrir no existe.";
                    return RedirectToPage("./Index");
                }
                Form = new Formulario
                {
                    Id = p.Id,
                    Nombre = p.Nombre,
                    IdCiudad = p.IdCiudad > 0 ? p.IdCiudad : null,
                    IdCliente = p.IdCliente,
                    Direccion = p.Direccion,
                    UrlGoogleMaps = p.UrlGoogleMaps,
                    Activo = p.Activo,
                    Version = VersionDe(p)
                };
            }
            await CargarAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Form.Nombre = MaestrosAuditoria.Limpio(Form.Nombre);
            Form.Direccion = MaestrosAuditoria.Limpio(Form.Direccion);
            Form.UrlGoogleMaps = MaestrosAuditoria.Limpio(Form.UrlGoogleMaps);

            Planta? p = null;
            if (!EsNuevo)
            {
                p = await _context.Plantas.FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (p == null)
                {
                    TempData["ToastError"] = "La planta ya no existe; no se guardó nada.";
                    return RedirectToPage("./Index");
                }
            }

            if (Form.IdCiudad.HasValue && !await _context.Ciudades.AnyAsync(c => c.Id == Form.IdCiudad.Value))
                ModelState.AddModelError("Form.IdCiudad", "La ciudad elegida no existe.");
            if (Form.IdCliente.HasValue && !await _context.Clientes.AnyAsync(c => c.Id == Form.IdCliente.Value))
                ModelState.AddModelError("Form.IdCliente", "El cliente elegido no existe.");
            if (Form.UrlGoogleMaps != null
                && !(Uri.TryCreate(Form.UrlGoogleMaps, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)))
                ModelState.AddModelError("Form.UrlGoogleMaps", "Pega el enlace completo de Google Maps (empieza con https://).");

            // Duplicados: solo si el nombre es nuevo o cambió (no bloquea editar registros antiguos).
            if (!string.IsNullOrEmpty(Form.Nombre) && (p == null || MaestrosAuditoria.Normalizar(p.Nombre) != MaestrosAuditoria.Normalizar(Form.Nombre)))
            {
                string clave = MaestrosAuditoria.Normalizar(Form.Nombre);
                var otros = await _context.Plantas.AsNoTracking().Where(x => x.Id != Form.Id).Select(x => new { x.Id, x.Nombre, x.Activo }).ToListAsync();
                var duplicado = otros.FirstOrDefault(x => MaestrosAuditoria.Normalizar(x.Nombre) == clave);
                if (duplicado != null)
                    ModelState.AddModelError("Form.Nombre", duplicado.Activo
                        ? $"Ya existe una planta activa llamada «{duplicado.Nombre}»."
                        : $"Ya existe «{duplicado.Nombre}», pero está inactiva: reactívala desde la lista en vez de crear otra.");
            }

            if (p != null && VersionDe(p) != Form.Version)
            {
                ModelState.AddModelError(string.Empty,
                    $"{(string.IsNullOrWhiteSpace(p.UsuarioModificador) ? "Otro usuario" : p.UsuarioModificador)} modificó esta planta el {MaestrosAuditoria.FormatoFecha(p.FechaModificacion)}, mientras la editabas. Revisa el registro de cambios y vuelve a guardar para confirmar tus datos.");
                Form.Version = VersionDe(p);
                ModelState.Remove("Form.Version"); // el formulario debe llevar la versión nueva
            }

            if (!ModelState.IsValid)
            {
                await CargarAsync(p);
                return Page();
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            DateTime ahora = MaestrosAuditoria.Ahora();
            string ciudadNueva = await MaestrosListas.NombreCiudadAsync(_context, Form.IdCiudad) ?? "";
            string? clienteNuevo = await MaestrosListas.NombreClienteAsync(_context, Form.IdCliente);

            if (p == null)
            {
                p = new Planta
                {
                    Nombre = Form.Nombre!,
                    IdCiudad = Form.IdCiudad!.Value,
                    IdCliente = Form.IdCliente,
                    Direccion = Form.Direccion,
                    UrlGoogleMaps = Form.UrlGoogleMaps,
                    Activo = Form.Activo,
                    FechaCreacion = ahora,
                    UsuarioCreador = usuario,
                    FechaModificacion = ahora,
                    UsuarioModificador = usuario
                };
                _context.Plantas.Add(p);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntPlanta, p.Id, "Registro creado", usuario,
                    $"{p.Nombre} · {ciudadNueva}{(clienteNuevo != null ? " · " + clienteNuevo : "")}");
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Planta «{p.Nombre}» creada por {usuario}.";
                return RedirectToPage("./Index", new { resaltar = p.Id });
            }

            var cambios = new MaestrosAuditoria.Cambios();
            cambios.Comparar("Nombre", p.Nombre, Form.Nombre);
            cambios.Comparar("Ciudad", p.IdCiudad > 0 ? await MaestrosListas.NombreCiudadAsync(_context, p.IdCiudad) : null, ciudadNueva);
            cambios.Comparar("Cliente", await MaestrosListas.NombreClienteAsync(_context, p.IdCliente), clienteNuevo);
            cambios.Comparar("Dirección", p.Direccion, Form.Direccion);
            cambios.Comparar("Enlace Google Maps", p.UrlGoogleMaps, Form.UrlGoogleMaps);
            cambios.Comparar("Estado", p.Activo ? "Activa" : "Inactiva", Form.Activo ? "Activa" : "Inactiva");

            if (!cambios.Hay)
            {
                TempData["ToastInfo"] = $"No había cambios que guardar en «{p.Nombre}».";
                return RedirectToPage("./Index", new { resaltar = p.Id, estado = p.Activo ? "activos" : "inactivos" });
            }

            p.Nombre = Form.Nombre!;
            p.IdCiudad = Form.IdCiudad!.Value;
            p.IdCliente = Form.IdCliente;
            p.Direccion = Form.Direccion;
            p.UrlGoogleMaps = Form.UrlGoogleMaps;
            p.Activo = Form.Activo;
            p.FechaModificacion = ahora;
            p.UsuarioModificador = usuario;
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntPlanta, p.Id, cambios, usuario, "Edición desde Maestros", ahora);
            await _context.SaveChangesAsync();

            TempData["ToastOk"] = $"Planta «{p.Nombre}» actualizada ({cambios.Lista.Count} cambio{(cambios.Lista.Count == 1 ? "" : "s")}). Queda registrado a nombre de {usuario}, {MaestrosAuditoria.FormatoFecha(ahora)}.";
            return RedirectToPage("./Index", new { resaltar = p.Id, estado = Form.Activo ? "activos" : "inactivos" });
        }

        private async Task CargarAsync(Planta? actual = null)
        {
            Ciudades = await MaestrosListas.CiudadesAsync(_context, Form.IdCiudad);
            Clientes = await MaestrosListas.ClientesAsync(_context, Form.IdCliente);
            if (Form.Id > 0)
            {
                actual ??= await _context.Plantas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (actual != null)
                {
                    NombreOriginal = actual.Nombre;
                    Registro = new MaestroAuditoriaVm
                    {
                        CreadoPor = actual.UsuarioCreador,
                        CreadoEl = actual.FechaCreacion,
                        ModificadoPor = actual.UsuarioModificador,
                        ModificadoEl = actual.FechaModificacion,
                        Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntPlanta, Form.Id)
                    };
                }
            }
        }
    }
}
