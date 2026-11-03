using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Agencias
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

            [Required(ErrorMessage = "Escribe el nombre de la agencia.")]
            [StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres.")]
            public string? Nombre { get; set; }

            [StringLength(20, ErrorMessage = "El RUT admite hasta 20 caracteres.")]
            public string? Rut { get; set; }

            [StringLength(100, ErrorMessage = "El contacto admite hasta 100 caracteres.")]
            public string? Contacto { get; set; }

            [StringLength(100, ErrorMessage = "El correo admite hasta 100 caracteres.")]
            public string? Correo { get; set; }

            public bool Activo { get; set; } = true;

            public long Version { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public bool EsNuevo => Form.Id == 0;
        public string? NombreOriginal { get; set; }
        public MaestroAuditoriaVm Registro { get; set; } = new();

        private static long VersionDe(AgenciasAduana a) => (a.FechaModificacion ?? a.FechaCreacion).Ticks;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id.HasValue)
            {
                var a = await _context.AgenciasAduanas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value);
                if (a == null)
                {
                    TempData["ToastError"] = "La agencia que intentas abrir no existe.";
                    return RedirectToPage("./Index");
                }
                Form = new Formulario
                {
                    Id = a.Id,
                    Nombre = a.NombreAgencia,
                    Rut = a.Rut,
                    Contacto = a.Contacto,
                    Correo = a.CorreoContacto,
                    Activo = a.Activo == 1,
                    Version = VersionDe(a)
                };
            }
            await CargarAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Form.Nombre = MaestrosAuditoria.Limpio(Form.Nombre);
            Form.Rut = MaestrosAuditoria.Limpio(Form.Rut);
            Form.Contacto = MaestrosAuditoria.Limpio(Form.Contacto);
            Form.Correo = MaestrosAuditoria.Limpio(Form.Correo);

            AgenciasAduana? a = null;
            if (!EsNuevo)
            {
                a = await _context.AgenciasAduanas.FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (a == null)
                {
                    TempData["ToastError"] = "La agencia ya no existe; no se guardó nada.";
                    return RedirectToPage("./Index");
                }
            }

            if (Form.Correo != null)
            {
                var correos = Form.Correo.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (correos.Any(x => !new EmailAddressAttribute().IsValid(x)))
                    ModelState.AddModelError("Form.Correo", "Revisa el correo (si son varios, sepáralos con punto y coma).");
            }

            if (!string.IsNullOrEmpty(Form.Nombre) && (a == null || MaestrosAuditoria.Normalizar(a.NombreAgencia) != MaestrosAuditoria.Normalizar(Form.Nombre)))
            {
                var otros = await _context.AgenciasAduanas.AsNoTracking().Where(x => x.Id != Form.Id).Select(x => new { x.NombreAgencia, x.Activo }).ToListAsync();
                var dup = otros.FirstOrDefault(x => MaestrosAuditoria.Normalizar(x.NombreAgencia) == MaestrosAuditoria.Normalizar(Form.Nombre));
                if (dup != null)
                    ModelState.AddModelError("Form.Nombre", $"Ya existe la agencia «{dup.NombreAgencia}»{(dup.Activo == 1 ? "" : " (inactiva: reactívala desde la lista)")}.");
            }

            if (a != null && VersionDe(a) != Form.Version)
            {
                ModelState.AddModelError(string.Empty,
                    $"{(string.IsNullOrWhiteSpace(a.UsuarioModificador) ? "Otro usuario" : a.UsuarioModificador)} modificó esta agencia el {MaestrosAuditoria.FormatoFecha(a.FechaModificacion)}, mientras la editabas. Revisa el registro de cambios y vuelve a guardar para confirmar tus datos.");
                Form.Version = VersionDe(a);
                ModelState.Remove("Form.Version"); // el formulario debe llevar la versión nueva
            }

            if (!ModelState.IsValid)
            {
                await CargarAsync(a);
                return Page();
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            DateTime ahora = MaestrosAuditoria.Ahora();

            if (a == null)
            {
                a = new AgenciasAduana
                {
                    NombreAgencia = Form.Nombre!,
                    Rut = Form.Rut,
                    Contacto = Form.Contacto,
                    CorreoContacto = Form.Correo,
                    Activo = Form.Activo ? 1 : 0,
                    FechaCreacion = ahora,
                    UsuarioCreador = usuario,
                    FechaModificacion = ahora,
                    UsuarioModificador = usuario
                };
                _context.AgenciasAduanas.Add(a);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntAgencia, a.Id, "Registro creado", usuario, a.NombreAgencia);
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Agencia «{a.NombreAgencia}» creada por {usuario}.";
                return RedirectToPage("./Index", new { resaltar = a.Id });
            }

            var cambios = new MaestrosAuditoria.Cambios();
            cambios.Comparar("Nombre", a.NombreAgencia, Form.Nombre);
            cambios.Comparar("RUT", a.Rut, Form.Rut);
            cambios.Comparar("Contacto", a.Contacto, Form.Contacto);
            cambios.Comparar("Correo", a.CorreoContacto, Form.Correo);
            cambios.Comparar("Estado", a.Activo == 1 ? "Activa" : "Inactiva", Form.Activo ? "Activa" : "Inactiva");

            if (!cambios.Hay)
            {
                TempData["ToastInfo"] = $"No había cambios que guardar en «{a.NombreAgencia}».";
                return RedirectToPage("./Index", new { resaltar = a.Id, estado = a.Activo == 1 ? "activos" : "inactivos" });
            }

            a.NombreAgencia = Form.Nombre!;
            a.Rut = Form.Rut;
            a.Contacto = Form.Contacto;
            a.CorreoContacto = Form.Correo;
            a.Activo = Form.Activo ? 1 : 0;
            a.FechaModificacion = ahora;
            a.UsuarioModificador = usuario;
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntAgencia, a.Id, cambios, usuario, "Edición desde Maestros", ahora);
            await _context.SaveChangesAsync();

            TempData["ToastOk"] = $"Agencia «{a.NombreAgencia}» actualizada ({cambios.Lista.Count} cambio{(cambios.Lista.Count == 1 ? "" : "s")}). Queda registrado a nombre de {usuario}, {MaestrosAuditoria.FormatoFecha(ahora)}.";
            return RedirectToPage("./Index", new { resaltar = a.Id, estado = Form.Activo ? "activos" : "inactivos" });
        }

        private async Task CargarAsync(AgenciasAduana? actual = null)
        {
            if (Form.Id <= 0) return;
            actual ??= await _context.AgenciasAduanas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.Id);
            if (actual == null) return;
            NombreOriginal = actual.NombreAgencia;
            Registro = new MaestroAuditoriaVm
            {
                CreadoPor = actual.UsuarioCreador,
                CreadoEl = actual.FechaCreacion,
                ModificadoPor = actual.UsuarioModificador,
                ModificadoEl = actual.FechaModificacion,
                Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntAgencia, Form.Id)
            };
        }
    }
}
