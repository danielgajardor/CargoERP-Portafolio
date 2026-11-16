using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Proveedores
{
    /// <summary>
    /// Maestro de proveedores (oct-2026): transportistas y depósitos. Antes solo se podían
    /// crear desde Comercial y no había forma de corregirlos.
    /// </summary>
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        /// <summary>El ERP trata como transportista a todo proveedor que no sea depósito (tipo 12).</summary>
        public static string NombreTipo(int tipo) => tipo == TransportistaHelper.TipoDeposito ? "Depósito" : "Transporte";

        public sealed class Fila
        {
            public Proveedore Proveedor { get; init; } = null!;
            public string? Ciudad { get; init; }
            public int Tarifas { get; init; }
            public string? ModificadoPor { get; init; }
            public DateTime? ModificadoEl { get; init; }
            public bool Activo => Proveedor.Activo == 1;
        }

        public List<Fila> Filas { get; set; } = new();
        public int TotalActivos { get; set; }
        public int TotalInactivos { get; set; }
        public int TotalTransportistas { get; set; }
        public int TotalDepositos { get; set; }
        public bool EsDeposito => Tipo == "deposito";
        public string NombreSeccion => EsDeposito ? "Depósitos de almacenamiento" : "Transportistas";

        [BindProperty(SupportsGet = true, Name = "tipo")]
        public string Tipo { get; set; } = "transporte";

        [BindProperty(SupportsGet = true)]
        public string Estado { get; set; } = "activos";

        [BindProperty(SupportsGet = true, Name = "q")]
        public string? Q { get; set; }

        [BindProperty(SupportsGet = true, Name = "f")]
        public string? F { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? Resaltar { get; set; }

        public async Task OnGetAsync()
        {
            Tipo = Tipo == "deposito" ? "deposito" : "transporte";
            Estado = Estado is "inactivos" or "todos" ? Estado : "activos";
            var ciudades = await _context.Ciudades.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre);
            var tarifas = (await _context.Tarifasterrestres.AsNoTracking().Where(t => t.EsActiva && t.IdProveedor != null).Select(t => t.IdProveedor!.Value).ToListAsync())
                .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var tarifasAlmacenamiento = (await _context.TarifasAlmacenamientos.AsNoTracking().Where(t => t.EsActiva).Select(t => t.IdProveedor).ToListAsync())
                .Concat(await _context.TarifasAlmacenamientoDetalles.AsNoTracking().Where(t => t.EsActiva).Select(t => t.IdProveedor).ToListAsync())
                .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var ultimos = await MaestrosAuditoria.UltimosCambiosAsync(_context, MaestrosAuditoria.EntProveedor);
            var todos = await _context.Proveedores.AsNoTracking().ToListAsync();
            TotalTransportistas = todos.Count(p => p.IdTipoProveedor != TransportistaHelper.TipoDeposito);
            TotalDepositos = todos.Count(p => p.IdTipoProveedor == TransportistaHelper.TipoDeposito);
            var seccion = todos.Where(p => EsDeposito
                ? p.IdTipoProveedor == TransportistaHelper.TipoDeposito
                : p.IdTipoProveedor != TransportistaHelper.TipoDeposito).ToList();
            TotalActivos = seccion.Count(p => p.Activo == 1);
            TotalInactivos = seccion.Count - TotalActivos;

            Filas = (Estado switch
            {
                "inactivos" => seccion.Where(p => p.Activo != 1),
                "todos" => seccion,
                _ => seccion.Where(p => p.Activo == 1)
            })
            .Select(p => new Fila
            {
                Proveedor = p,
                Ciudad = p.IdCiudad.HasValue ? ciudades.GetValueOrDefault(p.IdCiudad.Value) : null,
                Tarifas = EsDeposito ? tarifasAlmacenamiento.GetValueOrDefault(p.Id) : tarifas.GetValueOrDefault(p.Id),
                ModificadoPor = ultimos.TryGetValue(p.Id, out var u) ? u.Usuario : null,
                ModificadoEl = ultimos.TryGetValue(p.Id, out var u2) ? u2.Fecha : null
            })
            .OrderBy(f => f.Proveedor.NombreProveedor)
            .ToList();

         }

        public async Task<IActionResult> OnPostCambiarEstadoAsync(int id, bool activar)
        {
            var proveedor = await _context.Proveedores.FindAsync(id);
            if (proveedor == null)
            {
                TempData["ToastError"] = "El proveedor ya no existe.";
                return RedirectToPage("./Index", new { Estado, tipo = Tipo });
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            byte nuevo = (byte)(activar ? 1 : 0);
            if (proveedor.Activo != nuevo)
            {
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Estado", proveedor.Activo == 1 ? "Activo" : "Inactivo", activar ? "Activo" : "Inactivo");
                proveedor.Activo = nuevo;
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntProveedor, proveedor.Id, cambios, usuario, "Cambio de estado desde la lista");
                await _context.SaveChangesAsync();
            }

            TempData["ToastOk"] = activar
                ? $"Proveedor «{proveedor.NombreProveedor}» reactivado."
                : $"Proveedor «{proveedor.NombreProveedor}» desactivado. Puedes reactivarlo desde «Inactivos».";
            return RedirectToPage("./Index", new { Estado, tipo = Tipo, resaltar = proveedor.Id });
        }
    }
}
