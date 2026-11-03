using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Puertos
{
    /// <summary>
    /// Ficha de puerto. Antes la edición borraba país y ciudad (no venían en el formulario)
    /// y fallaba sin aviso; ahora solo cambian los campos del formulario.
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

            [Required(ErrorMessage = "Escribe el nombre del puerto.")]
            [StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres.")]
            public string? Nombre { get; set; }

            [StringLength(100, ErrorMessage = "La terminal admite hasta 100 caracteres.")]
            public string? Terminal { get; set; }

            [Required(ErrorMessage = "Escribe el país del puerto.")]
            [StringLength(80, ErrorMessage = "El país admite hasta 80 caracteres.")]
            public string? Pais { get; set; }

            public int? IdCiudad { get; set; }

            public bool Activo { get; set; } = true;

            public long Version { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public bool EsNuevo => Form.Id == 0;
        public string? NombreOriginal { get; set; }
        public List<SelectListItem> Ciudades { get; set; } = new();
        public List<string> PaisesSugeridos { get; set; } = new();
        public MaestroAuditoriaVm Registro { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id.HasValue)
            {
                var p = await _context.Puertos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value);
                if (p == null)
                {
                    TempData["ToastError"] = "El puerto que intentas abrir no existe.";
                    return RedirectToPage("./Index");
                }
                Form = new Formulario
                {
                    Id = p.Id,
                    Nombre = p.NombrePuerto,
                    Terminal = p.TerminalPortuario,
                    Pais = p.Pais,
                    IdCiudad = p.IdCiudad,
                    Activo = p.Activo == 1,
                    Version = p.FechaModificacion.Ticks
                };
            }
            await CargarAsync();
            return Page();
        }

        private static string Clave(string? nombre, string? terminal, string? pais) =>
            $"{MaestrosAuditoria.Normalizar(nombre)}|{MaestrosAuditoria.Normalizar(terminal)}|{MaestrosAuditoria.Normalizar(pais)}";

        public async Task<IActionResult> OnPostAsync()
        {
            Form.Nombre = MaestrosAuditoria.Limpio(Form.Nombre);
            Form.Terminal = MaestrosAuditoria.Limpio(Form.Terminal);
            Form.Pais = MaestrosAuditoria.Limpio(Form.Pais);

            Puerto? p = null;
            if (!EsNuevo)
            {
                p = await _context.Puertos.FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (p == null)
                {
                    TempData["ToastError"] = "El puerto ya no existe; no se guardó nada.";
                    return RedirectToPage("./Index");
                }
            }

            if (Form.IdCiudad.HasValue && !await _context.Ciudades.AnyAsync(c => c.Id == Form.IdCiudad.Value))
                ModelState.AddModelError("Form.IdCiudad", "La ciudad elegida no existe.");

            // Duplicados: solo si nombre/terminal/país es nuevo o cambió (no bloquea editar registros antiguos).
            if (!string.IsNullOrEmpty(Form.Nombre) && (p == null || Clave(p.NombrePuerto, p.TerminalPortuario, p.Pais) != Clave(Form.Nombre, Form.Terminal, Form.Pais)))
            {
                string clave = Clave(Form.Nombre, Form.Terminal, Form.Pais);
                var otros = await _context.Puertos.AsNoTracking().Where(x => x.Id != Form.Id)
                    .Select(x => new { x.Id, x.NombrePuerto, x.TerminalPortuario, x.Pais, x.Activo }).ToListAsync();
                var duplicado = otros.FirstOrDefault(x => Clave(x.NombrePuerto, x.TerminalPortuario, x.Pais) == clave);
                if (duplicado != null)
                    ModelState.AddModelError("Form.Nombre", duplicado.Activo == 1
                        ? $"Ya existe el puerto «{duplicado.NombrePuerto}» ({duplicado.Pais}) con la misma terminal."
                        : $"Ya existe «{duplicado.NombrePuerto}» ({duplicado.Pais}), pero está inactivo: reactívalo desde la lista en vez de crear otro.");
            }

            if (p != null && p.FechaModificacion.Ticks != Form.Version)
            {
                ModelState.AddModelError(string.Empty,
                    $"{(string.IsNullOrWhiteSpace(p.UsuarioModificador) ? "Otro usuario" : p.UsuarioModificador)} modificó este puerto el {MaestrosAuditoria.FormatoFecha(p.FechaModificacion)}, mientras lo editabas. Revisa el registro de cambios y vuelve a guardar para confirmar tus datos.");
                Form.Version = p.FechaModificacion.Ticks;
                ModelState.Remove("Form.Version"); // el formulario debe llevar la versión nueva
            }

            if (!ModelState.IsValid)
            {
                await CargarAsync(p);
                return Page();
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            DateTime ahora = MaestrosAuditoria.Ahora();
            string? ciudadNueva = await MaestrosListas.NombreCiudadAsync(_context, Form.IdCiudad);

            if (p == null)
            {
                p = new Puerto
                {
                    NombrePuerto = Form.Nombre!,
                    TerminalPortuario = Form.Terminal,
                    Pais = Form.Pais,
                    IdCiudad = Form.IdCiudad,
                    Activo = (byte)(Form.Activo ? 1 : 0),
                    FechaCreacion = ahora,
                    UsuarioCreador = usuario,
                    FechaModificacion = ahora,
                    UsuarioModificador = usuario
                };
                _context.Puertos.Add(p);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntPuerto, p.Id, "Registro creado", usuario, $"{p.NombrePuerto} · {p.Pais}");
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Puerto «{p.NombrePuerto}» creado por {usuario}.";
                return RedirectToPage("./Index", new { resaltar = p.Id });
            }

            var cambios = new MaestrosAuditoria.Cambios();
            cambios.Comparar("Nombre", p.NombrePuerto, Form.Nombre);
            cambios.Comparar("Terminal", p.TerminalPortuario, Form.Terminal);
            cambios.Comparar("País", p.Pais, Form.Pais);
            cambios.Comparar("Ciudad", await MaestrosListas.NombreCiudadAsync(_context, p.IdCiudad), ciudadNueva);
            cambios.Comparar("Estado", p.Activo == 1 ? "Activo" : "Inactivo", Form.Activo ? "Activo" : "Inactivo");

            if (!cambios.Hay)
            {
                TempData["ToastInfo"] = $"No había cambios que guardar en «{p.NombrePuerto}».";
                return RedirectToPage("./Index", new { resaltar = p.Id, estado = p.Activo == 1 ? "activos" : "inactivos" });
            }

            p.NombrePuerto = Form.Nombre!;
            p.TerminalPortuario = Form.Terminal;
            p.Pais = Form.Pais;
            p.IdCiudad = Form.IdCiudad;
            p.Activo = (byte)(Form.Activo ? 1 : 0);
            p.FechaModificacion = ahora;
            p.UsuarioModificador = usuario;
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntPuerto, p.Id, cambios, usuario, "Edición desde Maestros", ahora);
            await _context.SaveChangesAsync();

            TempData["ToastOk"] = $"Puerto «{p.NombrePuerto}» actualizado ({cambios.Lista.Count} cambio{(cambios.Lista.Count == 1 ? "" : "s")}). Queda registrado a nombre de {usuario}, {MaestrosAuditoria.FormatoFecha(ahora)}.";
            return RedirectToPage("./Index", new { resaltar = p.Id, estado = Form.Activo ? "activos" : "inactivos" });
        }

        private async Task CargarAsync(Puerto? actual = null)
        {
            Ciudades = await MaestrosListas.CiudadesAsync(_context, Form.IdCiudad);
            var deMaestro = await _context.Paises.AsNoTracking().Select(x => x.Nombre).ToListAsync();
            var dePuertos = await _context.Puertos.AsNoTracking().Where(x => x.Pais != null).Select(x => x.Pais!).ToListAsync();
            PaisesSugeridos = deMaestro.Concat(dePuertos)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct()
                .OrderBy(x => x)
                .ToList();
            if (Form.Id > 0)
            {
                actual ??= await _context.Puertos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (actual != null)
                {
                    NombreOriginal = actual.NombrePuerto;
                    Registro = new MaestroAuditoriaVm
                    {
                        CreadoPor = actual.UsuarioCreador,
                        CreadoEl = actual.FechaCreacion,
                        ModificadoPor = actual.UsuarioModificador,
                        ModificadoEl = actual.FechaModificacion,
                        Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntPuerto, Form.Id)
                    };
                }
            }
        }
    }
}
