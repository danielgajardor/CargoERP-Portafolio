using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    public class AprobarBLModel : PageModel
    {
        private readonly AtilsonContext _context;

        public AprobarBLModel(AtilsonContext context)
        {
            _context = context;
        }

        public Operacione Operacion { get; set; } = default!;
        public OperacionesDocumentale? Doc { get; set; }

        private int? ObtenerIdClienteSesion()
        {
            var claim = User.FindFirst("IdCliente")?.Value;
            if (!string.IsNullOrEmpty(claim) && int.TryParse(claim, out int id)) return id;
            return null;
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null) return RedirectToPage("/Login");

            var op = await _context.Operaciones
                .Include(o => o.OperacionesDocumentales)
                .FirstOrDefaultAsync(o => o.Id == id && o.IdCliente == idCliente.Value && !o.IsDeleted);

            if (op == null) return NotFound();

            Operacion = op;
            Doc = op.OperacionesDocumentales.OrderBy(x => x.Id).FirstOrDefault();
            return Page();
        }

        public async Task<IActionResult> OnPostAprobarAsync(int id)
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null) return RedirectToPage("/Login");

            var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales)
                .FirstOrDefaultAsync(o => o.Id == id && o.IdCliente == idCliente.Value && !o.IsDeleted);
            if (op == null) return NotFound();

            var doc = op.OperacionesDocumentales.OrderBy(x => x.Id).FirstOrDefault();
            if (doc != null) doc.EstadoDocumental = "B/L APROBADO";

            RegistrarHitoCliente(op, "El cliente aprobó el Borrador de B/L desde el Portal.");

            await _context.SaveChangesAsync();
            TempData["BLResultado"] = "aprobado";
            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostRechazarAsync(int id, string comentario)
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null) return RedirectToPage("/Login");

            if (string.IsNullOrWhiteSpace(comentario))
            {
                ModelState.AddModelError(string.Empty, "Debe indicar el motivo de los cambios solicitados.");
                return await OnGetAsync(id);
            }

            var op = await _context.Operaciones.Include(o => o.OperacionesDocumentales)
                .FirstOrDefaultAsync(o => o.Id == id && o.IdCliente == idCliente.Value && !o.IsDeleted);
            if (op == null) return NotFound();

            var doc = op.OperacionesDocumentales.OrderBy(x => x.Id).FirstOrDefault();
            if (doc != null) doc.EstadoDocumental = "CAMBIOS B/L";

            RegistrarHitoCliente(op, $"El cliente ha RECHAZADO el Borrador de B/L desde el Portal y solicita los siguientes cambios: \"{comentario.Trim()}\"");

            await _context.SaveChangesAsync();
            TempData["BLResultado"] = "rechazado";
            return RedirectToPage("./Index");
        }

        // Mismo formato pipe-delimited que usa Operaciones/IndexModel.RegistrarHito,
        // para que el timeline interno se vea idéntico sin importar si la respuesta
        // vino del portal logueado o del link de correo anónimo.
        private void RegistrarHitoCliente(Operacione op, string descripcion)
        {
            string fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            string linea = $"{fecha}|Cliente (Portal)|DOCUMENTAL|{descripcion}\n";
            op.HistorialCorreos = linea + (op.HistorialCorreos ?? "");
        }
    }
}