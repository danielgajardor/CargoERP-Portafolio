using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Pages.App.Depositos
{
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        public sealed class Fila
        {
            public Deposito Deposito { get; init; } = null!;
            public string? Naviera { get; init; }
            public string? Cliente { get; init; }
            public string? Ciudad { get; init; }
            public bool Activo => Deposito.Activo == 1;
        }

        public List<Fila> Filas { get; set; } = new();
        public int TotalActivos { get; set; }
        public int TotalInactivos { get; set; }
        public int SinCiudad { get; set; }
        public int PorValidar { get; set; }
        public List<(string Valor, string Texto)> OpcionesCiudad { get; set; } = new();

        /// <summary>activos | inactivos | todos</summary>
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
            Estado = Estado is "inactivos" or "todos" or "porvalidar" ? Estado : "activos";
            var navieras = await _context.Navieras.AsNoTracking().ToDictionaryAsync(n => n.Id, n => n.NombreNaviera);
            var ciudades = await _context.Ciudades.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre);
            var clientes = await _context.Clientes.AsNoTracking().Select(c => new { c.Id, c.RazonSocial }).ToDictionaryAsync(c => c.Id, c => c.RazonSocial);
            var todos = await _context.Depositos.AsNoTracking().ToListAsync();
            PorValidar = todos.Count(d => d.Activo == 1 && !d.Validado);

            TotalActivos = todos.Count(d => d.Activo == 1);
            TotalInactivos = todos.Count - TotalActivos;

            var visibles = Estado switch
            {
                "inactivos" => todos.Where(d => d.Activo != 1),
                "porvalidar" => todos.Where(d => d.Activo == 1 && !d.Validado),
                "todos" => todos,
                _ => todos.Where(d => d.Activo == 1)
            };

            Filas = visibles
                .Select(d => new Fila
                {
                    Deposito = d,
                    Naviera = d.IdNaviera.HasValue ? navieras.GetValueOrDefault(d.IdNaviera.Value) : null,
                    Ciudad = d.IdCiudad.HasValue ? ciudades.GetValueOrDefault(d.IdCiudad.Value) : null,
                    Cliente = d.IdCliente.HasValue ? clientes.GetValueOrDefault(d.IdCliente.Value) : null
                })
                .OrderBy(f => f.Deposito.NombreDeposito)
                .ToList();

            SinCiudad = Filas.Count(f => string.IsNullOrEmpty(f.Ciudad));
            OpcionesCiudad = Filas.Where(f => !string.IsNullOrEmpty(f.Ciudad))
                .Select(f => (f.Deposito.IdCiudad!.Value.ToString(), f.Ciudad!))
                .Distinct()
                .OrderBy(x => x.Item2)
                .ToList();
        }

        /// <summary>Atilson revisó un depósito registrado por un cliente en el portal.</summary>
        public async Task<IActionResult> OnPostValidarAsync(int id)
        {
            var deposito = await _context.Depositos.FindAsync(id);
            if (deposito == null)
            {
                TempData["ToastError"] = "El depósito ya no existe.";
                return RedirectToPage("./Index", new { Estado });
            }
            if (!deposito.Validado)
            {
                string usuario = MaestrosAuditoria.Usuario(User);
                var ahora = MaestrosAuditoria.Ahora();
                deposito.Validado = true;
                deposito.FechaValidacion = ahora;
                deposito.UsuarioValidacion = usuario;
                deposito.FechaModificacion = ahora;
                deposito.UsuarioModificador = usuario.Length > 50 ? usuario.Substring(0, 50) : usuario;
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Validación", "Por validar", "Validado");
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntDeposito, deposito.Id, cambios, usuario, "Validado en Maestros", ahora);
                await _context.SaveChangesAsync();
            }
            TempData["ToastOk"] = $"Depósito «{deposito.NombreDeposito}» validado.";
            return RedirectToPage("./Index", new { Estado, resaltar = deposito.Id });
        }

        public async Task<IActionResult> OnPostCambiarEstadoAsync(int id, bool activar)
        {
            var deposito = await _context.Depositos.FindAsync(id);
            if (deposito == null)
            {
                TempData["ToastError"] = "El depósito ya no existe.";
                return RedirectToPage("./Index", new { Estado });
            }

            string usuario = MaestrosAuditoria.Usuario(User);
            byte nuevo = (byte)(activar ? 1 : 0);
            if (deposito.Activo != nuevo)
            {
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Agregar("Estado", deposito.Activo == 1 ? "Activo" : "Inactivo", activar ? "Activo" : "Inactivo");
                deposito.Activo = nuevo;
                deposito.FechaModificacion = MaestrosAuditoria.Ahora();
                deposito.UsuarioModificador = usuario;
                MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntDeposito, deposito.Id, cambios, usuario, "Cambio de estado desde la lista");
                await _context.SaveChangesAsync();
            }

            TempData["ToastOk"] = activar
                ? $"Depósito «{deposito.NombreDeposito}» reactivado."
                : $"Depósito «{deposito.NombreDeposito}» desactivado. Puedes reactivarlo desde «Inactivos».";
            return RedirectToPage("./Index", new { Estado, resaltar = deposito.Id });
        }
    }
}
