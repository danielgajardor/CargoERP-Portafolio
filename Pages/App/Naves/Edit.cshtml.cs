using System.ComponentModel.DataAnnotations;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Naves
{
    /// <summary>
    /// Ficha de nave del catálogo (dbo.naves). Al corregir el nombre, las salidas de Itinerarios
    /// de esa naviera con el nombre anterior pasan al nuevo y, con ellas, el texto «NAVE / VIAJE»
    /// de los bookings que las tienen elegidas (igual que al corregir una salida en Itinerarios),
    /// para no perder el vínculo. Cambiar la naviera solo cambia el catálogo: cada salida
    /// conserva la naviera con la que se registró.
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

            [Required(ErrorMessage = "Escribe el nombre de la nave.")]
            [StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres.")]
            public string? Nombre { get; set; }

            [Required(ErrorMessage = "Elige la naviera.")]
            public int? IdNaviera { get; set; }

            public bool Activo { get; set; } = true;

            public long Version { get; set; }
        }

        [BindProperty]
        public Formulario Form { get; set; } = new();

        public bool EsNuevo => Form.Id == 0;
        public string? NombreOriginal { get; set; }
        public List<Naviera> Navieras { get; set; } = new();
        public int SalidasUsan { get; set; }
        public int BookingsUsan { get; set; }
        public List<string> Parecidas { get; set; } = new();
        public MaestroAuditoriaVm Registro { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id.HasValue)
            {
                var n = await _context.Naves.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value);
                if (n == null)
                {
                    TempData["ToastError"] = "La nave que intentas abrir no existe.";
                    return RedirectToPage("./Index");
                }
                Form = new Formulario
                {
                    Id = n.Id,
                    Nombre = n.Nombre,
                    IdNaviera = n.IdNaviera,
                    Activo = !n.IsDeleted,
                    Version = n.FechaModificacion.Ticks
                };
            }
            await CargarAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Form.Nombre = string.IsNullOrWhiteSpace(Form.Nombre) ? null : NavesCatalogoHelper.Normalizar(Form.Nombre);
            ModelState.Remove("Form.Nombre");
            if (Form.Nombre == null) ModelState.AddModelError("Form.Nombre", "Escribe el nombre de la nave.");
            else if (Form.Nombre.Length < 2) ModelState.AddModelError("Form.Nombre", "El nombre es demasiado corto.");

            Nafe? n = null;
            if (!EsNuevo)
            {
                n = await _context.Naves.FirstOrDefaultAsync(x => x.Id == Form.Id);
                if (n == null)
                {
                    TempData["ToastError"] = "La nave ya no existe; no se guardó nada.";
                    return RedirectToPage("./Index");
                }
            }

            Naviera? naviera = Form.IdNaviera == null ? null : await _context.Navieras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.IdNaviera && !x.IsDeleted);
            if (Form.IdNaviera != null && naviera == null) ModelState.AddModelError("Form.IdNaviera", "La naviera elegida no existe.");

            // Duplicado: misma nave activa en la misma naviera (el índice único lo exige).
            if (Form.Nombre != null && naviera != null && Form.Activo)
            {
                var mismas = await _context.Naves.AsNoTracking()
                    .Where(x => x.Id != Form.Id && !x.IsDeleted && x.IdNaviera == naviera.Id).ToListAsync();
                var dup = mismas.FirstOrDefault(x => NavesCatalogoHelper.Normalizar(x.Nombre) == Form.Nombre);
                if (dup != null)
                    ModelState.AddModelError("Form.Nombre", $"Ya existe la nave «{dup.Nombre}» en {naviera.NombreNaviera}. Si esta está repetida, desactívala desde la lista.");
            }

            if (n != null && n.FechaModificacion.Ticks != Form.Version)
            {
                ModelState.AddModelError(string.Empty,
                    $"{(string.IsNullOrWhiteSpace(n.UsuarioModificador) ? "Otro usuario" : n.UsuarioModificador)} modificó esta nave el {MaestrosAuditoria.FormatoFecha(n.FechaModificacion)}, mientras la editabas. Revisa el registro de cambios y vuelve a guardar para confirmar tus datos.");
                Form.Version = n.FechaModificacion.Ticks;
                ModelState.Remove("Form.Version");
            }

            // Corrección de nombre: las salidas renombradas no pueden chocar con otra salida igual.
            List<ItinerarioNave> salidasRenombrar = new();
            if (ModelState.IsValid && n != null && Form.Nombre != NavesCatalogoHelper.Normalizar(n.Nombre))
            {
                salidasRenombrar = await SalidasDeAsync(n);
                foreach (var it in salidasRenombrar)
                {
                    bool choca = await _context.ItinerariosNaves.AnyAsync(o => o.Activo && o.Id != it.Id
                        && o.IdNaviera == it.IdNaviera && o.IdPuertoOrigen == it.IdPuertoOrigen && o.IdPuertoDestino == it.IdPuertoDestino
                        && o.Nave == Form.Nombre && o.NumeroViaje == it.NumeroViaje);
                    if (choca)
                    {
                        ModelState.AddModelError("Form.Nombre",
                            $"Ya existe la salida «{Form.Nombre} / {it.NumeroViaje}» en la misma ruta (salida #{it.Id} quedaría repetida). Corrige o elimina una de las dos en Itinerarios antes de renombrar.");
                        break;
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                await CargarAsync(n);
                return Page();
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            string usuarioCorto = IndexModel.Recortar(usuario);
            DateTime ahora = MaestrosAuditoria.Ahora();

            if (n == null)
            {
                n = new Nafe
                {
                    Nombre = Form.Nombre!,
                    IdNaviera = naviera!.Id,
                    IsDeleted = !Form.Activo,
                    FechaCreacion = ahora,
                    UsuarioCreador = usuarioCorto,
                    FechaModificacion = ahora,
                    UsuarioModificador = usuarioCorto
                };
                _context.Naves.Add(n);
                await _context.SaveChangesAsync();
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntNave, n.Id, "Registro creado", usuario, n.Nombre + " · " + naviera.NombreNaviera);
                await _context.SaveChangesAsync();
                TempData["ToastOk"] = $"Nave «{n.Nombre}» creada por {usuario}.";
                return RedirectToPage("./Index", new { resaltar = n.Id });
            }

            string? navieraAntes = n.IdNaviera == null ? null : await _context.Navieras.Where(x => x.Id == n.IdNaviera).Select(x => x.NombreNaviera).FirstOrDefaultAsync();
            var cambios = new MaestrosAuditoria.Cambios();
            cambios.Comparar("Nombre", n.Nombre, Form.Nombre);
            cambios.Comparar("Naviera", navieraAntes, naviera!.NombreNaviera);
            cambios.Comparar("Estado", n.IsDeleted ? "Inactiva" : "Activa", Form.Activo ? "Activa" : "Inactiva");

            if (!cambios.Hay)
            {
                TempData["ToastInfo"] = $"No había cambios que guardar en «{n.Nombre}».";
                return RedirectToPage("./Index", new { resaltar = n.Id, estado = n.IsDeleted ? "inactivos" : "activos" });
            }

            // Propagación del nombre corregido a las salidas y a los bookings que las tienen elegidas.
            int bookings = 0;
            foreach (var it in salidasRenombrar)
            {
                string antes = it.NaveViaje;
                it.Nave = Form.Nombre!;
                it.FechaModificacion = ahora;
                it.UsuarioModificador = usuario;
                string despues = it.NaveViaje;
                var vinculados = await _context.Operaciones
                    .Where(o => o.IdItinerario == it.Id && !o.IsDeleted && o.Nave == antes)
                    .ToListAsync();
                foreach (var o in vinculados)
                {
                    o.Nave = despues;
                    o.FechaModificacion = ahora;
                    o.UsuarioModificador = usuario;
                }
                bookings += vinculados.Count;
            }

            n.Nombre = Form.Nombre!;
            n.IdNaviera = naviera.Id;
            n.IsDeleted = !Form.Activo;
            n.FechaModificacion = ahora;
            n.UsuarioModificador = usuarioCorto;
            string comentario = salidasRenombrar.Count > 0
                ? $"Edición desde Maestros · {salidasRenombrar.Count} salida(s) y {bookings} booking(s) con el nombre nuevo"
                : "Edición desde Maestros";
            MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntNave, n.Id, cambios, usuario, comentario, ahora);
            await _context.SaveChangesAsync();

            string extra = salidasRenombrar.Count > 0
                ? $" También se actualizaron {salidasRenombrar.Count} salida{(salidasRenombrar.Count == 1 ? "" : "s")} de Itinerarios y {bookings} booking{(bookings == 1 ? "" : "s")}."
                : "";
            TempData["ToastOk"] = $"Nave «{n.Nombre}» actualizada ({cambios.Lista.Count} cambio{(cambios.Lista.Count == 1 ? "" : "s")}).{extra}";
            return RedirectToPage("./Index", new { resaltar = n.Id, estado = Form.Activo ? "activos" : "inactivos" });
        }

        // Salidas que usan esta nave del catálogo: mismo nombre y su naviera. Si la nave no tiene
        // naviera, las de cualquier naviera que no tenga su propia nave con ese nombre.
        private async Task<List<ItinerarioNave>> SalidasDeAsync(Nafe n)
        {
            string nombre = NavesCatalogoHelper.Normalizar(n.Nombre);
            var candidatas = await _context.ItinerariosNaves.Where(i => i.Nave == nombre).ToListAsync();
            candidatas = candidatas.Where(i => NavesCatalogoHelper.Normalizar(i.Nave) == nombre).ToList();
            if (n.IdNaviera != null) return candidatas.Where(i => i.IdNaviera == n.IdNaviera).ToList();
            var conPropia = (await _context.Naves.AsNoTracking().Where(x => !x.IsDeleted && x.Id != n.Id && x.IdNaviera != null).ToListAsync())
                .Where(x => NavesCatalogoHelper.Normalizar(x.Nombre) == nombre).Select(x => x.IdNaviera!.Value).ToHashSet();
            return candidatas.Where(i => !conPropia.Contains(i.IdNaviera)).ToList();
        }

        private async Task CargarAsync(Nafe? actual = null)
        {
            Navieras = await _context.Navieras.AsNoTracking().Where(x => !x.IsDeleted && (x.Activo == 1 || x.Id == Form.IdNaviera))
                .OrderBy(x => x.NombreNaviera).ToListAsync();
            if (Form.Id <= 0) return;
            actual ??= await _context.Naves.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Form.Id);
            if (actual == null) return;
            NombreOriginal = actual.Nombre;
            var salidas = await SalidasDeAsync(actual);
            SalidasUsan = salidas.Count(s => s.Activo);
            var ids = salidas.Select(s => s.Id).ToList();
            BookingsUsan = ids.Count == 0 ? 0 : await _context.Operaciones.CountAsync(o => o.IdItinerario != null && ids.Contains(o.IdItinerario.Value) && !o.IsDeleted);
            var catalogo = await NavesCatalogoHelper.ActivasAsync(_context);
            Parecidas = NavesCatalogoHelper.Parecidas(catalogo.Where(x => x.Id != actual.Id), actual.Nombre)
                .Select(x => x.Nombre).Distinct().ToList();
            Registro = new MaestroAuditoriaVm
            {
                CreadoPor = actual.UsuarioCreador,
                CreadoEl = actual.FechaCreacion,
                ModificadoPor = actual.UsuarioModificador,
                ModificadoEl = actual.FechaModificacion,
                Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntNave, Form.Id)
            };
        }
    }
}
