using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Navieras
{
    /// <summary>
    /// Ficha de naviera. Antes la edición reemplazaba el registro completo con lo que venía
    /// del formulario: se perdían la fecha de creación y las reglas de roleo/Gate, y el
    /// guardado fallaba sin aviso. Ahora solo se actualizan los campos del formulario.
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

            [Required(ErrorMessage = "Escribe el nombre de la naviera.")]
            [StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres.")]
            public string? Nombre { get; set; }

            public string? Color { get; set; }

            [StringLength(500, ErrorMessage = "El enlace admite hasta 500 caracteres.")]
            public string? LinkTracking { get; set; }

            [Range(0, 90, ErrorMessage = "La ventana de roleo debe estar entre 0 y 90 días.")]
            public int DiasPenalidadRoleo { get; set; } = 7;

            public bool PenalidadRoleoSiempreAplica { get; set; }

            /// <summary>Texto para leerlo sin depender del separador decimal del servidor.</summary>
            public string? MontoPenalidadRoleoUsd { get; set; }

            public bool AplicaTarifaGatePlana { get; set; }

            public bool Activo { get; set; } = true;

            public long Version { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public bool EsNuevo => Form.Id == 0;
        public string? NombreOriginal { get; set; }
        public MaestroAuditoriaVm Registro { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id.HasValue)
            {
                var n = await _context.Navieras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value && !x.IsDeleted);
                if (n == null)
                {
                    TempData["ToastError"] = "La naviera que intentas abrir no existe.";
                    return RedirectToPage("./Index");
                }
                Form = new Formulario
                {
                    Id = n.Id,
                    Nombre = n.NombreNaviera,
                    Color = MaestrosAuditoria.ColorValido(n.ColorRepresentativo) ?? "#6c757d",
                    LinkTracking = n.LinkTracking,
                    DiasPenalidadRoleo = n.DiasPenalidadRoleo,
                    PenalidadRoleoSiempreAplica = n.PenalidadRoleoSiempreAplica,
                    MontoPenalidadRoleoUsd = n.MontoPenalidadRoleoUsd?.ToString("0.##", CultureInfo.InvariantCulture),
                    AplicaTarifaGatePlana = n.AplicaTarifaGatePlana,
                    Activo = n.Activo == 1,
                    Version = n.FechaModificacion.Ticks
                };
            }
            else
            {
                Form.Color = "#6c757d";
            }
            await CargarAsync();
            return Page();
        }

        private static bool LeerMonto(string? texto, out decimal? monto)
        {
            monto = null;
            if (string.IsNullOrWhiteSpace(texto)) return true;
            var t = texto.Trim().Replace(" ", "");
            // "150,50" o "150.50" -> 150.50 (sin separador de miles).
            if (t.Count(c => c == ',' || c == '.') > 1) return false;
            t = t.Replace(',', '.');
            if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) || v < 0) return false;
            monto = Math.Round(v, 2);
            return true;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Form.Nombre = MaestrosAuditoria.Limpio(Form.Nombre);
            Form.LinkTracking = MaestrosAuditoria.Limpio(Form.LinkTracking);

            Naviera? n = null;
            if (!EsNuevo)
            {
                n = await _context.Navieras.FirstOrDefaultAsync(x => x.Id == Form.Id && !x.IsDeleted);
                if (n == null)
                {
                    TempData["ToastError"] = "La naviera ya no existe; no se guardó nada.";
                    return RedirectToPage("./Index");
                }
            }

            string? color = MaestrosAuditoria.ColorValido(Form.Color);
            if (color == null) ModelState.AddModelError("Form.Color", "Elige un color válido.");
            if (!LeerMonto(Form.MontoPenalidadRoleoUsd, out var monto))
                ModelState.AddModelError("Form.MontoPenalidadRoleoUsd", "Escribe el monto en USD con punto o coma decimal (ej: 150 o 150,50).");
            if (Form.LinkTracking != null
                && !(Uri.TryCreate(Form.LinkTracking, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)))
                ModelState.AddModelError("Form.LinkTracking", "Pega el enlace completo (empieza con https://).");

            // Duplicados: solo si el nombre es nuevo o cambió (no bloquea editar registros antiguos).
            if (!string.IsNullOrEmpty(Form.Nombre) && (n == null || MaestrosAuditoria.Normalizar(n.NombreNaviera) != MaestrosAuditoria.Normalizar(Form.Nombre)))
            {
                string clave = MaestrosAuditoria.Normalizar(Form.Nombre);
                var otros = await _context.Navieras.AsNoTracking().Where(x => x.Id != Form.Id && !x.IsDeleted).Select(x => new { x.Id, x.NombreNaviera, x.Activo }).ToListAsync();
                var duplicado = otros.FirstOrDefault(x => MaestrosAuditoria.Normalizar(x.NombreNaviera) == clave);
                if (duplicado != null)
                    ModelState.AddModelError("Form.Nombre", duplicado.Activo == 1
                        ? $"Ya existe una naviera activa llamada «{duplicado.NombreNaviera}»."
                        : $"Ya existe «{duplicado.NombreNaviera}», pero está inactiva: reactívala desde la lista en vez de crear otra.");
            }

            if (n != null && n.FechaModificacion.Ticks != Form.Version)
            {
                ModelState.AddModelError(string.Empty,
                    $"{(string.IsNullOrWhiteSpace(n.UsuarioModificador) ? "Otro usuario" : n.UsuarioModificador)} modificó esta naviera el {MaestrosAuditoria.FormatoFecha(n.FechaModificacion)}, mientras la editabas. Revisa el registro de cambios y vuelve a guardar para confirmar tus datos.");
                Form.Version = n.FechaModificacion.Ticks;
                ModelState.Remove("Form.Version"); // el formulario debe llevar la versión nueva
            }

            if (!ModelState.IsValid)
            {
                await CargarAsync(n);
                return Page();
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            DateTime ahora = MaestrosAuditoria.Ahora();

            if (n == null)
            {
                n = new Naviera
                {
                    NombreNaviera = Form.Nombre!,
                    ColorRepresentativo = color,
                    LinkTracking = Form.LinkTracking,
                    DiasPenalidadRoleo = Form.DiasPenalidadRoleo,
                    PenalidadRoleoSiempreAplica = Form.PenalidadRoleoSiempreAplica,
                    MontoPenalidadRoleoUsd = monto,
                    AplicaTarifaGatePlana = Form.AplicaTarifaGatePlana,
                    Activo = (byte)(Form.Activo ? 1 : 0),
                    FechaCreacion = ahora,
                    UsuarioCreador = usuario,
                    FechaModificacion = ahora,
                    UsuarioModificador = usuario
                };
                _context.Navieras.Add(n);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntNaviera, n.Id, "Registro creado", usuario, n.NombreNaviera);
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Naviera «{n.NombreNaviera}» creada por {usuario}.";
                return RedirectToPage("./Index", new { resaltar = n.Id });
            }

            var cambios = new MaestrosAuditoria.Cambios();
            cambios.Comparar("Nombre", n.NombreNaviera, Form.Nombre);
            cambios.Comparar("Color", MaestrosAuditoria.ColorValido(n.ColorRepresentativo) ?? "#6c757d", color);
            cambios.Comparar("Enlace de seguimiento", n.LinkTracking, Form.LinkTracking);
            cambios.Comparar("Ventana de roleo (días)", n.DiasPenalidadRoleo.ToString(), Form.DiasPenalidadRoleo.ToString());
            cambios.Comparar("Penalidad siempre aplica", n.PenalidadRoleoSiempreAplica, Form.PenalidadRoleoSiempreAplica);
            cambios.Comparar("Monto penalidad (USD)", n.MontoPenalidadRoleoUsd, monto);
            cambios.Comparar("Gate con tarifa plana", n.AplicaTarifaGatePlana, Form.AplicaTarifaGatePlana);
            cambios.Comparar("Estado", n.Activo == 1 ? "Activa" : "Inactiva", Form.Activo ? "Activa" : "Inactiva");

            if (!cambios.Hay)
            {
                TempData["ToastInfo"] = $"No había cambios que guardar en «{n.NombreNaviera}».";
                return RedirectToPage("./Index", new { resaltar = n.Id, estado = n.Activo == 1 ? "activos" : "inactivos" });
            }

            n.NombreNaviera = Form.Nombre!;
            if (MaestrosAuditoria.ColorValido(n.ColorRepresentativo) != null || color != "#6c757d") n.ColorRepresentativo = color;
            n.LinkTracking = Form.LinkTracking;
            n.DiasPenalidadRoleo = Form.DiasPenalidadRoleo;
            n.PenalidadRoleoSiempreAplica = Form.PenalidadRoleoSiempreAplica;
            n.MontoPenalidadRoleoUsd = monto;
            n.AplicaTarifaGatePlana = Form.AplicaTarifaGatePlana;
            n.Activo = (byte)(Form.Activo ? 1 : 0);
            n.FechaModificacion = ahora;
            n.UsuarioModificador = usuario;
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntNaviera, n.Id, cambios, usuario, "Edición desde Maestros", ahora);
            await _context.SaveChangesAsync();

            TempData["ToastOk"] = $"Naviera «{n.NombreNaviera}» actualizada ({cambios.Lista.Count} cambio{(cambios.Lista.Count == 1 ? "" : "s")}). Queda registrado a nombre de {usuario}, {MaestrosAuditoria.FormatoFecha(ahora)}.";
            return RedirectToPage("./Index", new { resaltar = n.Id, estado = Form.Activo ? "activos" : "inactivos" });
        }

        private async Task CargarAsync(Naviera? actual = null)
        {
            if (Form.Id <= 0) return;
            actual ??= await _context.Navieras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.Id);
            if (actual == null) return;
            NombreOriginal = actual.NombreNaviera;
            Registro = new MaestroAuditoriaVm
            {
                CreadoPor = actual.UsuarioCreador,
                CreadoEl = actual.FechaCreacion,
                ModificadoPor = actual.UsuarioModificador,
                ModificadoEl = actual.FechaModificacion,
                Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntNaviera, Form.Id)
            };
        }
    }
}
