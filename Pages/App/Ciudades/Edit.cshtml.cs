using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Ciudades
{
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

            [Required(ErrorMessage = "Escribe el nombre de la ciudad.")]
            [StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres.")]
            public string? Nombre { get; set; }

            [Required(ErrorMessage = "Elige el país.")]
            public int? IdPais { get; set; }

            public long Version { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public bool EsNuevo => Form.Id == 0;
        public string? NombreOriginal { get; set; }
        public List<SelectListItem> Paises { get; set; } = new();
        public int UsoDepositos { get; set; }
        public int UsoPlantas { get; set; }
        public int UsoTarifas { get; set; }
        public MaestroAuditoriaVm Registro { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id.HasValue)
            {
                var c = await _context.Ciudades.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value);
                if (c == null)
                {
                    TempData["ToastError"] = "La ciudad que intentas abrir no existe.";
                    return RedirectToPage("./Index");
                }
                Form = new Formulario { Id = c.Id, Nombre = c.Nombre, IdPais = c.IdPais, Version = c.FechaModificacion.Ticks };
            }
            else
            {
                Form.IdPais = await _context.Paises.AsNoTracking().Where(p => p.Nombre == "Chile" || p.Nombre == "CHILE").Select(p => (int?)p.Id).FirstOrDefaultAsync();
            }
            await CargarAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Form.Nombre = MaestrosAuditoria.Limpio(Form.Nombre);

            Ciudade? c = null;
            if (!EsNuevo)
            {
                c = await _context.Ciudades.FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (c == null)
                {
                    TempData["ToastError"] = "La ciudad ya no existe; no se guardó nada.";
                    return RedirectToPage("./Index");
                }
            }

            if (Form.IdPais.HasValue && !await _context.Paises.AnyAsync(p => p.Id == Form.IdPais.Value))
                ModelState.AddModelError("Form.IdPais", "El país elegido no existe.");

            if (!string.IsNullOrEmpty(Form.Nombre) && Form.IdPais.HasValue
                && (c == null || MaestrosAuditoria.Normalizar(c.Nombre) != MaestrosAuditoria.Normalizar(Form.Nombre) || c.IdPais != Form.IdPais))
            {
                string clave = MaestrosAuditoria.Normalizar(Form.Nombre);
                var otros = await _context.Ciudades.AsNoTracking().Where(x => x.Id != Form.Id && x.IdPais == Form.IdPais.Value).Select(x => new { x.Id, x.Nombre }).ToListAsync();
                var duplicado = otros.FirstOrDefault(x => MaestrosAuditoria.Normalizar(x.Nombre) == clave);
                if (duplicado != null)
                    ModelState.AddModelError("Form.Nombre", $"Ya existe la ciudad «{duplicado.Nombre}» en ese país.");
            }

            if (c != null && c.FechaModificacion.Ticks != Form.Version)
            {
                ModelState.AddModelError(string.Empty,
                    $"{(string.IsNullOrWhiteSpace(c.UsuarioModificador) ? "Otro usuario" : c.UsuarioModificador)} modificó esta ciudad el {MaestrosAuditoria.FormatoFecha(c.FechaModificacion)}, mientras la editabas. Revisa el registro de cambios y vuelve a guardar para confirmar tus datos.");
                Form.Version = c.FechaModificacion.Ticks;
                ModelState.Remove("Form.Version"); // el formulario debe llevar la versión nueva
            }

            if (!ModelState.IsValid)
            {
                await CargarAsync(c);
                return Page();
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            DateTime ahora = MaestrosAuditoria.Ahora();
            string? paisNuevo = await MaestrosListas.NombrePaisAsync(_context, Form.IdPais);

            if (c == null)
            {
                c = new Ciudade
                {
                    Nombre = Form.Nombre!,
                    IdPais = Form.IdPais!.Value,
                    FechaCreacion = ahora,
                    UsuarioCreador = usuario,
                    FechaModificacion = ahora,
                    UsuarioModificador = usuario
                };
                _context.Ciudades.Add(c);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntCiudad, c.Id, "Registro creado", usuario, $"{c.Nombre} · {paisNuevo}");
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Ciudad «{c.Nombre}» creada por {usuario}. Ya puedes elegirla en depósitos, plantas y tarifas.";
                return RedirectToPage("./Index", new { resaltar = c.Id });
            }

            var cambios = new MaestrosAuditoria.Cambios();
            cambios.Comparar("Nombre", c.Nombre, Form.Nombre);
            cambios.Comparar("País", await MaestrosListas.NombrePaisAsync(_context, c.IdPais), paisNuevo);

            if (!cambios.Hay)
            {
                TempData["ToastInfo"] = $"No había cambios que guardar en «{c.Nombre}».";
                return RedirectToPage("./Index", new { resaltar = c.Id });
            }

            c.Nombre = Form.Nombre!;
            c.IdPais = Form.IdPais!.Value;
            c.FechaModificacion = ahora;
            c.UsuarioModificador = usuario;
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntCiudad, c.Id, cambios, usuario, "Edición desde Maestros", ahora);
            await _context.SaveChangesAsync();

            TempData["ToastOk"] = $"Ciudad «{c.Nombre}» actualizada ({cambios.Lista.Count} cambio{(cambios.Lista.Count == 1 ? "" : "s")}). Queda registrado a nombre de {usuario}, {MaestrosAuditoria.FormatoFecha(ahora)}.";
            return RedirectToPage("./Index", new { resaltar = c.Id });
        }

        private async Task CargarAsync(Ciudade? actual = null)
        {
            Paises = await MaestrosListas.PaisesAsync(_context, Form.IdPais);
            if (Form.Id <= 0) return;
            actual ??= await _context.Ciudades.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.Id);
            if (actual == null) return;
            NombreOriginal = actual.Nombre;
            UsoDepositos = await _context.Depositos.AsNoTracking().CountAsync(d => d.IdCiudad == Form.Id);
            UsoPlantas = await _context.Plantas.AsNoTracking().CountAsync(p => p.IdCiudad == Form.Id);
            UsoTarifas = await _context.Tarifasterrestres.AsNoTracking().CountAsync(t => t.IdCiudadOrigen == Form.Id || t.IdCiudadDestino == Form.Id);
            Registro = new MaestroAuditoriaVm
            {
                CreadoPor = actual.UsuarioCreador,
                CreadoEl = actual.FechaCreacion,
                ModificadoPor = actual.UsuarioModificador,
                ModificadoEl = actual.FechaModificacion,
                Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntCiudad, Form.Id)
            };
        }
    }
}
