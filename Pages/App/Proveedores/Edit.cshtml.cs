using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Proveedores
{
    /// <summary>
    /// Ficha de proveedor. La tabla no tiene columnas de auditoría: quién y cuándo se toma
    /// del historial de cambios (HistorialCambiosComercial, "Maestro:Proveedor").
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

            [Required(ErrorMessage = "Escribe el nombre del proveedor.")]
            [StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres.")]
            public string? Nombre { get; set; }

            [StringLength(12, ErrorMessage = "El RUT admite hasta 12 caracteres (ej: 76.123.456-7).")]
            public string? Rut { get; set; }

            public bool EsDeposito { get; set; }

            [StringLength(100, ErrorMessage = "El contacto admite hasta 100 caracteres.")]
            public string? Contacto { get; set; }

            [StringLength(100, ErrorMessage = "El correo admite hasta 100 caracteres.")]
            public string? Correo { get; set; }

            public int? IdCiudad { get; set; }

            public bool Activo { get; set; } = true;

            public long Version { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public bool EsNuevo => Form.Id == 0;
        public string? NombreOriginal { get; set; }
        public int TarifasTransporte { get; set; }
        public List<SelectListItem> Ciudades { get; set; } = new();
        public MaestroAuditoriaVm Registro { get; set; } = new();

        private async Task<long> VersionAsync(int id)
        {
            var tipo = MaestrosAuditoria.Prefijo + MaestrosAuditoria.EntProveedor;
            var ultima = await _context.HistorialCambiosComercial.AsNoTracking()
                .Where(h => h.TipoEntidad == tipo && h.IdRegistro == id)
                .OrderByDescending(h => h.FechaCambio)
                .Select(h => (DateTime?)h.FechaCambio)
                .FirstOrDefaultAsync();
            return ultima?.Ticks ?? 0;
        }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id.HasValue)
            {
                var p = await _context.Proveedores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value);
                if (p == null)
                {
                    TempData["ToastError"] = "El proveedor que intentas abrir no existe.";
                    return RedirectToPage("./Index");
                }
                Form = new Formulario
                {
                    Id = p.Id,
                    Nombre = p.NombreProveedor,
                    Rut = p.Rut,
                    EsDeposito = p.IdTipoProveedor == TransportistaHelper.TipoDeposito,
                    Contacto = p.Contacto,
                    Correo = p.CorreoOperativo,
                    IdCiudad = p.IdCiudad,
                    Activo = p.Activo == 1,
                    Version = await VersionAsync(p.Id)
                };
            }
            await CargarAsync();
            return Page();
        }

        private static string ClaveRut(string? rut) => new string((rut ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        public async Task<IActionResult> OnPostAsync()
        {
            Form.Nombre = MaestrosAuditoria.Limpio(Form.Nombre);
            Form.Rut = MaestrosAuditoria.Limpio(Form.Rut)?.ToUpperInvariant();
            Form.Contacto = MaestrosAuditoria.Limpio(Form.Contacto);
            Form.Correo = MaestrosAuditoria.Limpio(Form.Correo);

            Proveedore? p = null;
            if (!EsNuevo)
            {
                p = await _context.Proveedores.FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (p == null)
                {
                    TempData["ToastError"] = "El proveedor ya no existe; no se guardó nada.";
                    return RedirectToPage("./Index");
                }
            }

            if (Form.IdCiudad.HasValue && !await _context.Ciudades.AnyAsync(c => c.Id == Form.IdCiudad.Value))
                ModelState.AddModelError("Form.IdCiudad", "La ciudad elegida no existe.");
            if (Form.Correo != null)
            {
                var correos = Form.Correo.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (correos.Any(x => !new EmailAddressAttribute().IsValid(x)))
                    ModelState.AddModelError("Form.Correo", "Revisa el correo (si son varios, sepáralos con punto y coma).");
            }

            var otros = await _context.Proveedores.AsNoTracking().Where(x => x.Id != Form.Id)
                .Select(x => new { x.Id, x.NombreProveedor, x.Rut, x.Activo }).ToListAsync();
            if (!string.IsNullOrEmpty(Form.Nombre) && (p == null || MaestrosAuditoria.Normalizar(p.NombreProveedor) != MaestrosAuditoria.Normalizar(Form.Nombre)))
            {
                var dup = otros.FirstOrDefault(x => MaestrosAuditoria.Normalizar(x.NombreProveedor) == MaestrosAuditoria.Normalizar(Form.Nombre));
                if (dup != null)
                    ModelState.AddModelError("Form.Nombre", $"Ya existe el proveedor «{dup.NombreProveedor}»{(dup.Activo == 1 ? "" : " (inactivo: reactívalo desde la lista)")}.");
            }
            if (Form.Rut != null && ClaveRut(Form.Rut).Length > 0 && (p == null || ClaveRut(p.Rut) != ClaveRut(Form.Rut)))
            {
                var dup = otros.FirstOrDefault(x => ClaveRut(x.Rut) == ClaveRut(Form.Rut));
                if (dup != null)
                    ModelState.AddModelError("Form.Rut", $"El RUT ya está registrado para «{dup.NombreProveedor}».");
            }

            if (p != null)
            {
                long version = await VersionAsync(p.Id);
                if (version != Form.Version)
                {
                    ModelState.AddModelError(string.Empty, "Otro usuario modificó este proveedor mientras lo editabas. Revisa el registro de cambios y vuelve a guardar para confirmar tus datos.");
                    Form.Version = version;
                    ModelState.Remove("Form.Version"); // el formulario debe llevar la versión nueva
                }
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
                p = new Proveedore
                {
                    NombreProveedor = Form.Nombre!,
                    Rut = Form.Rut,
                    IdTipoProveedor = Form.EsDeposito ? TransportistaHelper.TipoDeposito : 2,
                    Contacto = Form.Contacto,
                    CorreoOperativo = Form.Correo,
                    IdCiudad = Form.IdCiudad,
                    Activo = (byte)(Form.Activo ? 1 : 0)
                };
                _context.Proveedores.Add(p);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntProveedor, p.Id, "Registro creado", usuario,
                    $"{p.NombreProveedor} · {(Form.EsDeposito ? "Depósito" : "Transporte")}");
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Proveedor «{p.NombreProveedor}» creado por {usuario}.";
                return RedirectToPage("./Index", new { resaltar = p.Id });
            }

            bool eraDeposito = p.IdTipoProveedor == TransportistaHelper.TipoDeposito;
            var cambios = new MaestrosAuditoria.Cambios();
            cambios.Comparar("Nombre", p.NombreProveedor, Form.Nombre);
            cambios.Comparar("RUT", p.Rut, Form.Rut);
            cambios.Comparar("Tipo", eraDeposito ? "Depósito" : "Transporte", Form.EsDeposito ? "Depósito" : "Transporte");
            cambios.Comparar("Contacto", p.Contacto, Form.Contacto);
            cambios.Comparar("Correo operativo", p.CorreoOperativo, Form.Correo);
            cambios.Comparar("Ciudad", await MaestrosListas.NombreCiudadAsync(_context, p.IdCiudad), ciudadNueva);
            cambios.Comparar("Estado", p.Activo == 1 ? "Activo" : "Inactivo", Form.Activo ? "Activo" : "Inactivo");

            if (!cambios.Hay)
            {
                TempData["ToastInfo"] = $"No había cambios que guardar en «{p.NombreProveedor}».";
                return RedirectToPage("./Index", new { resaltar = p.Id, estado = p.Activo == 1 ? "activos" : "inactivos" });
            }

            p.NombreProveedor = Form.Nombre!;
            p.Rut = Form.Rut;
            // Se conserva el código de tipo original de los transportistas (2, 10...): solo
            // cambia si pasa de transporte a depósito o al revés.
            if (eraDeposito != Form.EsDeposito) p.IdTipoProveedor = Form.EsDeposito ? TransportistaHelper.TipoDeposito : 2;
            p.Contacto = Form.Contacto;
            p.CorreoOperativo = Form.Correo;
            p.IdCiudad = Form.IdCiudad;
            p.Activo = (byte)(Form.Activo ? 1 : 0);
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntProveedor, p.Id, cambios, usuario, "Edición desde Maestros", ahora);
            await _context.SaveChangesAsync();

            TempData["ToastOk"] = $"Proveedor «{p.NombreProveedor}» actualizado ({cambios.Lista.Count} cambio{(cambios.Lista.Count == 1 ? "" : "s")}). Queda registrado a nombre de {usuario}, {MaestrosAuditoria.FormatoFecha(ahora)}.";
            return RedirectToPage("./Index", new { resaltar = p.Id, estado = Form.Activo ? "activos" : "inactivos" });
        }

        private async Task CargarAsync(Proveedore? actual = null)
        {
            Ciudades = await MaestrosListas.CiudadesAsync(_context, Form.IdCiudad);
            if (Form.Id <= 0) return;
            actual ??= await _context.Proveedores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.Id);
            if (actual == null) return;
            NombreOriginal = actual.NombreProveedor;
            TarifasTransporte = await _context.Tarifasterrestres.AsNoTracking().CountAsync(t => t.IdProveedor == Form.Id && t.EsActiva);
            var historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntProveedor, Form.Id);
            var creado = historial.LastOrDefault(h => h.Campo == "Registro creado");
            var ultimo = historial.FirstOrDefault();
            Registro = new MaestroAuditoriaVm
            {
                CreadoPor = creado?.Usuario,
                CreadoEl = creado?.FechaCambio,
                ModificadoPor = ultimo?.Usuario,
                ModificadoEl = ultimo?.FechaCambio,
                Historial = historial
            };
        }
    }
}
