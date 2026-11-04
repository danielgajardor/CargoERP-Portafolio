using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Depositos
{
    /// <summary>
    /// Ficha de depósito: crea (sin id) o edita (con id).
    /// Solo se actualizan los campos del formulario; el resto del registro queda intacto.
    /// Cada guardado deja usuario, fecha/hora y el detalle de cambios.
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

            [Required(ErrorMessage = "Escribe el nombre del depósito.")]
            [StringLength(150, ErrorMessage = "El nombre admite hasta 150 caracteres.")]
            [Display(Name = "Nombre")]
            public string? Nombre { get; set; }

            [Required(ErrorMessage = "Elige la ciudad del depósito: sin ella no se encuentra la tarifa de transporte.")]
            public int? IdCiudad { get; set; }

            public int? IdNaviera { get; set; }

            [StringLength(250, ErrorMessage = "La dirección admite hasta 250 caracteres.")]
            public string? Direccion { get; set; }

            [StringLength(80)]
            public string? Pais { get; set; }

            public bool Activo { get; set; } = true;

            /// <summary>Marca de la versión leída (FechaModificacion.Ticks) para detectar ediciones simultáneas.</summary>
            public long Version { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public bool EsNuevo => Form.Id == 0;
        public string? NombreOriginal { get; set; }
        public List<SelectListItem> Ciudades { get; set; } = new();
        public List<SelectListItem> Navieras { get; set; } = new();
        public MaestroAuditoriaVm Registro { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id.HasValue)
            {
                var d = await _context.Depositos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value);
                if (d == null)
                {
                    TempData["ToastError"] = "El depósito que intentas abrir no existe.";
                    return RedirectToPage("./Index");
                }
                Form = new Formulario
                {
                    Id = d.Id,
                    Nombre = d.NombreDeposito,
                    IdCiudad = d.IdCiudad,
                    IdNaviera = d.IdNaviera,
                    Direccion = d.Direccion,
                    Pais = d.Pais,
                    Activo = d.Activo == 1,
                    Version = d.FechaModificacion.Ticks
                };
            }
            await CargarAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Form.Nombre = MaestrosAuditoria.Limpio(Form.Nombre);
            Form.Direccion = MaestrosAuditoria.Limpio(Form.Direccion);
            Form.Pais = MaestrosAuditoria.Limpio(Form.Pais);

            Deposito? d = null;
            if (!EsNuevo)
            {
                d = await _context.Depositos.FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (d == null)
                {
                    TempData["ToastError"] = "El depósito ya no existe; no se guardó nada.";
                    return RedirectToPage("./Index");
                }
            }

            if (Form.IdCiudad.HasValue && !await _context.Ciudades.AnyAsync(c => c.Id == Form.IdCiudad.Value))
                ModelState.AddModelError("Form.IdCiudad", "La ciudad elegida no existe.");
            if (Form.IdNaviera.HasValue && !await _context.Navieras.AnyAsync(n => n.Id == Form.IdNaviera.Value))
                ModelState.AddModelError("Form.IdNaviera", "La naviera elegida no existe.");

            // Duplicados: solo si el nombre es nuevo o cambió (no bloquea editar registros antiguos).
            if (!string.IsNullOrEmpty(Form.Nombre) && (d == null || MaestrosAuditoria.Normalizar(d.NombreDeposito) != MaestrosAuditoria.Normalizar(Form.Nombre)))
            {
                string clave = MaestrosAuditoria.Normalizar(Form.Nombre);
                var otros = await _context.Depositos.AsNoTracking().Where(x => x.Id != Form.Id).Select(x => new { x.Id, x.NombreDeposito, x.Activo }).ToListAsync();
                var duplicado = otros.FirstOrDefault(x => MaestrosAuditoria.Normalizar(x.NombreDeposito) == clave);
                if (duplicado != null)
                    ModelState.AddModelError("Form.Nombre", duplicado.Activo == 1
                        ? $"Ya existe un depósito activo llamado «{duplicado.NombreDeposito}»."
                        : $"Ya existe «{duplicado.NombreDeposito}», pero está inactivo: reactívalo desde la lista en vez de crear otro.");
            }

            if (d != null && d.FechaModificacion.Ticks != Form.Version)
            {
                ModelState.AddModelError(string.Empty,
                    $"{(string.IsNullOrWhiteSpace(d.UsuarioModificador) ? "Otro usuario" : d.UsuarioModificador)} modificó este depósito el {MaestrosAuditoria.FormatoFecha(d.FechaModificacion)}, mientras lo editabas. Revisa el registro de cambios y vuelve a guardar para confirmar tus datos.");
                Form.Version = d.FechaModificacion.Ticks;
                ModelState.Remove("Form.Version"); // el formulario debe llevar la versión nueva
            }

            if (!ModelState.IsValid)
            {
                await CargarAsync(d);
                return Page();
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            DateTime ahora = MaestrosAuditoria.Ahora();
            string ciudadNueva = await MaestrosListas.NombreCiudadAsync(_context, Form.IdCiudad) ?? "";
            string? navieraNueva = await MaestrosListas.NombreNavieraAsync(_context, Form.IdNaviera);

            if (d == null)
            {
                d = new Deposito
                {
                    NombreDeposito = Form.Nombre!,
                    IdCiudad = Form.IdCiudad,
                    IdNaviera = Form.IdNaviera,
                    Direccion = Form.Direccion,
                    Pais = Form.Pais,
                    Activo = (byte)(Form.Activo ? 1 : 0),
                    FechaCreacion = ahora,
                    UsuarioCreador = usuario,
                    FechaModificacion = ahora,
                    UsuarioModificador = usuario
                };
                _context.Depositos.Add(d);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntDeposito, d.Id, "Registro creado", usuario,
                    $"{d.NombreDeposito} · {ciudadNueva}{(navieraNueva != null ? " · " + navieraNueva : "")}");
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Depósito «{d.NombreDeposito}» creado por {usuario}.";
                return RedirectToPage("./Index", new { resaltar = d.Id });
            }

            var cambios = new MaestrosAuditoria.Cambios();
            cambios.Comparar("Nombre", d.NombreDeposito, Form.Nombre);
            cambios.Comparar("Ciudad", await MaestrosListas.NombreCiudadAsync(_context, d.IdCiudad), ciudadNueva);
            cambios.Comparar("Naviera", await MaestrosListas.NombreNavieraAsync(_context, d.IdNaviera), navieraNueva);
            cambios.Comparar("Dirección", d.Direccion, Form.Direccion);
            cambios.Comparar("País", d.Pais, Form.Pais);
            cambios.Comparar("Estado", d.Activo == 1 ? "Activo" : "Inactivo", Form.Activo ? "Activo" : "Inactivo");

            if (!cambios.Hay)
            {
                TempData["ToastInfo"] = $"No había cambios que guardar en «{d.NombreDeposito}».";
                return RedirectToPage("./Index", new { resaltar = d.Id, estado = d.Activo == 1 ? "activos" : "inactivos" });
            }

            d.NombreDeposito = Form.Nombre!;
            d.IdCiudad = Form.IdCiudad;
            d.IdNaviera = Form.IdNaviera;
            d.Direccion = Form.Direccion;
            d.Pais = Form.Pais;
            d.Activo = (byte)(Form.Activo ? 1 : 0);
            d.FechaModificacion = ahora;
            d.UsuarioModificador = usuario;
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntDeposito, d.Id, cambios, usuario, "Edición desde Maestros", ahora);
            await _context.SaveChangesAsync();

            TempData["ToastOk"] = $"Depósito «{d.NombreDeposito}» actualizado ({cambios.Lista.Count} cambio{(cambios.Lista.Count == 1 ? "" : "s")}). Queda registrado a nombre de {usuario}, {MaestrosAuditoria.FormatoFecha(ahora)}.";
            return RedirectToPage("./Index", new { resaltar = d.Id, estado = Form.Activo ? "activos" : "inactivos" });
        }

        private async Task CargarAsync(Deposito? actual = null)
        {
            Ciudades = await MaestrosListas.CiudadesAsync(_context, Form.IdCiudad);
            Navieras = await MaestrosListas.NavierasAsync(_context, Form.IdNaviera);
            if (Form.Id > 0)
            {
                actual ??= await _context.Depositos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (actual != null)
                {
                    NombreOriginal = actual.NombreDeposito;
                    Registro = new MaestroAuditoriaVm
                    {
                        CreadoPor = actual.UsuarioCreador,
                        CreadoEl = actual.FechaCreacion,
                        ModificadoPor = actual.UsuarioModificador,
                        ModificadoEl = actual.FechaModificacion,
                        Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntDeposito, Form.Id)
                    };
                }
            }
        }
    }
}
