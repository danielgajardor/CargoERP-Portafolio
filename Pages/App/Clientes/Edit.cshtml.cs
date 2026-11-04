using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Clientes
{
    /// <summary>
    /// Ficha de cliente. Antes la edición reemplazaba el registro completo: se perdían
    /// teléfono, correo y las marcas de Comercial/Cobranza (cliente especial, suscripción).
    /// Ahora solo cambian los campos del formulario y cada cambio queda en el historial.
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

            [StringLength(20, ErrorMessage = "El RUT admite hasta 20 caracteres.")]
            public string? Rut { get; set; }

            [Required(ErrorMessage = "Escribe la razón social.")]
            [StringLength(200, ErrorMessage = "La razón social admite hasta 200 caracteres.")]
            public string? RazonSocial { get; set; }

            [StringLength(200, ErrorMessage = "El nombre de fantasía admite hasta 200 caracteres.")]
            public string? NombreCliente { get; set; }

            [StringLength(250, ErrorMessage = "La dirección admite hasta 250 caracteres.")]
            public string? Direccion { get; set; }

            public int? IdCiudad { get; set; }

            [StringLength(150, ErrorMessage = "El contacto admite hasta 150 caracteres.")]
            public string? Contacto { get; set; }

            [StringLength(50, ErrorMessage = "El teléfono admite hasta 50 caracteres.")]
            public string? Telefono { get; set; }

            [StringLength(250, ErrorMessage = "El correo admite hasta 250 caracteres.")]
            public string? Correo { get; set; }

            public bool Activo { get; set; } = true;

            public long Version { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public bool EsNuevo => Form.Id == 0;
        public string? NombreOriginal { get; set; }
        public bool EsClienteEspecial { get; set; }
        public bool SuscripcionCobranzaActiva { get; set; }
        public List<SelectListItem> Ciudades { get; set; } = new();
        public List<TarifasCliente> Tarifas { get; set; } = new();
        public MaestroAuditoriaVm Registro { get; set; } = new();

        private static long VersionDe(Cliente c) => (c.FechaModificacion ?? c.FechaCreacion ?? DateTime.MinValue).Ticks;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id.HasValue)
            {
                var c = await _context.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value && !x.IsDeleted);
                if (c == null)
                {
                    TempData["ToastError"] = "El cliente que intentas abrir no existe.";
                    return RedirectToPage("./Index");
                }
                Form = new Formulario
                {
                    Id = c.Id,
                    Rut = c.Rut,
                    RazonSocial = c.RazonSocial,
                    NombreCliente = c.NombreCliente,
                    Direccion = c.Direccion,
                    IdCiudad = c.IdCiudad,
                    Contacto = c.Contacto,
                    Telefono = c.Telefono,
                    Correo = c.Correo,
                    Activo = c.Activo == 1,
                    Version = VersionDe(c)
                };
            }
            await CargarAsync();
            return Page();
        }

        /// <summary>RUT sin puntos ni guion, en mayúsculas (para comparar duplicados).</summary>
        private static string ClaveRut(string? rut) => new string((rut ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        /// <summary>Si parece un RUT chileno (números + dígito verificador), comprueba el dígito.</summary>
        private static bool RutChilenoInvalido(string rut)
        {
            var limpio = ClaveRut(rut);
            if (limpio.Length < 7 || limpio.Length > 9) return false;
            var cuerpo = limpio[..^1];
            var dv = limpio[^1];
            if (!cuerpo.All(char.IsDigit) || !(char.IsDigit(dv) || dv == 'K')) return false;
            if (!rut.Contains('-')) return false; // Tax ID extranjero u otro formato: no se valida.
            int suma = 0, factor = 2;
            for (int i = cuerpo.Length - 1; i >= 0; i--)
            {
                suma += (cuerpo[i] - '0') * factor;
                factor = factor == 7 ? 2 : factor + 1;
            }
            int resto = 11 - (suma % 11);
            char esperado = resto == 11 ? '0' : resto == 10 ? 'K' : (char)('0' + resto);
            return esperado != dv;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Form.Rut = MaestrosAuditoria.Limpio(Form.Rut)?.ToUpperInvariant();
            Form.RazonSocial = MaestrosAuditoria.Limpio(Form.RazonSocial);
            Form.NombreCliente = MaestrosAuditoria.Limpio(Form.NombreCliente);
            Form.Direccion = MaestrosAuditoria.Limpio(Form.Direccion);
            Form.Contacto = MaestrosAuditoria.Limpio(Form.Contacto);
            Form.Telefono = MaestrosAuditoria.Limpio(Form.Telefono);
            Form.Correo = MaestrosAuditoria.Limpio(Form.Correo);

            Cliente? c = null;
            if (!EsNuevo)
            {
                c = await _context.Clientes.FirstOrDefaultAsync(x => x.Id == Form.Id && !x.IsDeleted);
                if (c == null)
                {
                    TempData["ToastError"] = "El cliente ya no existe; no se guardó nada.";
                    return RedirectToPage("./Index");
                }
            }

            if (Form.IdCiudad.HasValue && !await _context.Ciudades.AnyAsync(x => x.Id == Form.IdCiudad.Value))
                ModelState.AddModelError("Form.IdCiudad", "La ciudad elegida no existe.");
            if (Form.Rut != null && RutChilenoInvalido(Form.Rut) && (c == null || ClaveRut(c.Rut) != ClaveRut(Form.Rut)))
                ModelState.AddModelError("Form.Rut", "El dígito verificador del RUT no es correcto.");
            if (Form.Correo != null)
            {
                var correos = Form.Correo.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (correos.Any(x => !new EmailAddressAttribute().IsValid(x)))
                    ModelState.AddModelError("Form.Correo", "Revisa el correo (si son varios, sepáralos con punto y coma).");
            }

            var otros = await _context.Clientes.AsNoTracking().Where(x => x.Id != Form.Id && !x.IsDeleted)
                .Select(x => new { x.Id, x.RazonSocial, x.Rut, x.Activo }).ToListAsync();
            // Duplicados: solo si el dato es nuevo o cambió (no bloquea editar registros antiguos).
            if (Form.Rut != null && ClaveRut(Form.Rut).Length > 0 && (c == null || ClaveRut(c.Rut) != ClaveRut(Form.Rut)))
            {
                var mismoRut = otros.FirstOrDefault(x => ClaveRut(x.Rut) == ClaveRut(Form.Rut));
                if (mismoRut != null)
                    ModelState.AddModelError("Form.Rut", $"El RUT ya está registrado para «{mismoRut.RazonSocial}»{(mismoRut.Activo == 1 ? "" : " (inactivo: reactívalo desde la lista)")}.");
            }
            if (Form.RazonSocial != null && (c == null || MaestrosAuditoria.Normalizar(c.RazonSocial) != MaestrosAuditoria.Normalizar(Form.RazonSocial)))
            {
                var mismaRazon = otros.FirstOrDefault(x => MaestrosAuditoria.Normalizar(x.RazonSocial) == MaestrosAuditoria.Normalizar(Form.RazonSocial));
                if (mismaRazon != null)
                    ModelState.AddModelError("Form.RazonSocial", $"Ya existe un cliente con la razón social «{mismaRazon.RazonSocial}»{(mismaRazon.Activo == 1 ? "" : " (inactivo: reactívalo desde la lista)")}.");
            }

            if (c != null && VersionDe(c) != Form.Version)
            {
                ModelState.AddModelError(string.Empty,
                    $"{(string.IsNullOrWhiteSpace(c.UsuarioModificador) ? "Otro usuario" : c.UsuarioModificador)} modificó este cliente el {MaestrosAuditoria.FormatoFecha(c.FechaModificacion)}, mientras lo editabas. Revisa el registro de cambios y vuelve a guardar para confirmar tus datos.");
                Form.Version = VersionDe(c);
                ModelState.Remove("Form.Version"); // el formulario debe llevar la versión nueva
            }

            if (!ModelState.IsValid)
            {
                await CargarAsync(c);
                return Page();
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            DateTime ahora = MaestrosAuditoria.Ahora();
            string? ciudadNueva = await MaestrosListas.NombreCiudadAsync(_context, Form.IdCiudad);

            if (c == null)
            {
                c = new Cliente
                {
                    Rut = Form.Rut,
                    RazonSocial = Form.RazonSocial!,
                    NombreCliente = Form.NombreCliente,
                    Direccion = Form.Direccion,
                    IdCiudad = Form.IdCiudad,
                    Contacto = Form.Contacto,
                    Telefono = Form.Telefono,
                    Correo = Form.Correo,
                    Activo = (byte)(Form.Activo ? 1 : 0),
                    FechaCreacion = ahora,
                    UsuarioCreador = usuario,
                    FechaModificacion = ahora,
                    UsuarioModificador = usuario
                };
                _context.Clientes.Add(c);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntCliente, c.Id, "Registro creado", usuario, $"{c.RazonSocial}{(c.Rut != null ? " · " + c.Rut : "")}");
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Cliente «{c.RazonSocial}» creado por {usuario}.";
                return RedirectToPage("./Index", new { resaltar = c.Id });
            }

            var cambios = new MaestrosAuditoria.Cambios();
            cambios.Comparar("RUT", c.Rut, Form.Rut);
            cambios.Comparar("Razón social", c.RazonSocial, Form.RazonSocial);
            cambios.Comparar("Nombre de fantasía", c.NombreCliente, Form.NombreCliente);
            cambios.Comparar("Dirección", c.Direccion, Form.Direccion);
            cambios.Comparar("Ciudad", await MaestrosListas.NombreCiudadAsync(_context, c.IdCiudad), ciudadNueva);
            cambios.Comparar("Contacto", c.Contacto, Form.Contacto);
            cambios.Comparar("Teléfono", c.Telefono, Form.Telefono);
            cambios.Comparar("Correo", c.Correo, Form.Correo);
            cambios.Comparar("Estado", c.Activo == 1 ? "Activo" : "Inactivo", Form.Activo ? "Activo" : "Inactivo");

            if (!cambios.Hay)
            {
                TempData["ToastInfo"] = $"No había cambios que guardar en «{c.RazonSocial}».";
                return RedirectToPage("./Index", new { resaltar = c.Id, estado = c.Activo == 1 ? "activos" : "inactivos" });
            }

            c.Rut = Form.Rut;
            c.RazonSocial = Form.RazonSocial!;
            c.NombreCliente = Form.NombreCliente;
            c.Direccion = Form.Direccion;
            c.IdCiudad = Form.IdCiudad;
            c.Contacto = Form.Contacto;
            c.Telefono = Form.Telefono;
            c.Correo = Form.Correo;
            c.Activo = (byte)(Form.Activo ? 1 : 0);
            c.FechaModificacion = ahora;
            c.UsuarioModificador = usuario;
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntCliente, c.Id, cambios, usuario, "Edición desde Maestros", ahora);
            await _context.SaveChangesAsync();

            TempData["ToastOk"] = $"Cliente «{c.RazonSocial}» actualizado ({cambios.Lista.Count} cambio{(cambios.Lista.Count == 1 ? "" : "s")}). Queda registrado a nombre de {usuario}, {MaestrosAuditoria.FormatoFecha(ahora)}.";
            return RedirectToPage("./Index", new { resaltar = c.Id, estado = Form.Activo ? "activos" : "inactivos" });
        }

        private async Task CargarAsync(Cliente? actual = null)
        {
            Ciudades = await MaestrosListas.CiudadesAsync(_context, Form.IdCiudad);
            if (Form.Id <= 0) return;
            actual ??= await _context.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.Id);
            if (actual == null) return;
            NombreOriginal = actual.RazonSocial;
            EsClienteEspecial = actual.EsClienteEspecial;
            SuscripcionCobranzaActiva = actual.SuscripcionCobranzaActiva;
            Tarifas = await _context.TarifasClientes.AsNoTracking()
                .Where(t => t.IdCliente == Form.Id && t.EsActiva == true)
                .OrderBy(t => t.GrupoCobro).ThenBy(t => t.Concepto)
                .ToListAsync();
            Registro = new MaestroAuditoriaVm
            {
                CreadoPor = actual.UsuarioCreador,
                CreadoEl = actual.FechaCreacion,
                ModificadoPor = actual.UsuarioModificador,
                ModificadoEl = actual.FechaModificacion,
                Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntCliente, Form.Id)
            };
        }
    }
}
