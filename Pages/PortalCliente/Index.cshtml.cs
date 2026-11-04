using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        // 1. LA LISTA BLINDADA DE OPERACIONES DEL CLIENTE
        public IList<Operacione> OperacionesCliente { get; set; } = default!;

        // 2. VARIABLES PARA LAS TARJETAS (KPIs) SUPERIORES
        public int CargasActivas { get; set; }
        public int EmbarquesFinalizados { get; set; }
        public int AccionesRequeridas { get; set; }
        public decimal SaldoPendienteTotal { get; set; }

        // Mismo criterio que IndexModel.OperacionCerradaPorRetiro / PendienteGestionRetiro
        // del lado de Operaciones — se duplica aquí (en vez de llamar cross-PageModel)
        // para no acoplar el portal de clientes al módulo interno.
        public bool OperacionCerradaPorRetiro(Operacione op)
        {
            if (op.Unidadestecnicas != null && op.Unidadestecnicas.Any())
                return op.FechaDevolucionVacio.HasValue && op.Unidadestecnicas.All(u => u.FechaDevolucionVacio.HasValue);
            return op.FechaDevolucionVacio.HasValue;
        }

        public bool PendienteGestionRetiro(Operacione op)
        {
            if (OperacionCerradaPorRetiro(op)) return false;
            bool mainPendiente = op.EstadoWorkflow == "Arribado";
            bool unidadPendiente = op.Unidadestecnicas != null && op.Unidadestecnicas.Any(u => u.Estado == "Arribado");
            return mainPendiente || unidadPendiente;
        }

        // 3. BUSCADOR MINIMALISTA
        [BindProperty(SupportsGet = true)]
        public string? SearchString { get; set; }

        private int? ObtenerIdClienteSesion()
        {
            var claimCliente = User.FindFirst("IdCliente")?.Value;
            if (!string.IsNullOrEmpty(claimCliente) && int.TryParse(claimCliente, out int id)) return id;
            return null;
        }

        public async Task<IActionResult> OnPostSolicitarRoleoAsync(int idOperacion, string tipo, string comentario)
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null) return new JsonResult(new { success = false, message = "Sesión expirada." });

            if (tipo != "ROLEO" && tipo != "CANCELACION_NOSHOW")
                return new JsonResult(new { success = false, message = "Tipo no reconocido." });

            // Aislamiento: la operación debe pertenecer al cliente en sesión.
            var op = await _context.Operaciones.FirstOrDefaultAsync(o => o.Id == idOperacion && o.IdCliente == idCliente.Value && !o.IsDeleted);
            if (op == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

            bool yaExisteSolicitudPendiente = await _context.RoleosCancelaciones
                .AnyAsync(r => r.IdOperacion == idOperacion && r.SolicitadoPorCliente && !r.Atendido);
            if (yaExisteSolicitudPendiente)
                return new JsonResult(new { success = false, message = "Ya tienes una solicitud pendiente de revisión para este booking. Nuestro equipo te contactará pronto." });

            string nombreCliente = User.Identity?.Name ?? "Cliente";

            _context.RoleosCancelaciones.Add(new RoleoCancelacion
            {
                IdOperacion = idOperacion,
                Tipo = tipo,
                FechaSolicitud = DateTime.Now,
                PenalidadConfirmada = "POR_DETERMINAR",
                CantidadContenedoresAfectados = 1,
                Comentario = comentario?.Trim(),
                DatosNuevosPendientes = true,
                SolicitadoPorCliente = true,
                Atendido = false,
                Usuario = nombreCliente,
                FechaCreacion = DateTime.Now
            });

            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnGetAsync()
        {
            // --- EL ESCUDO DE SEGURIDAD AUTOMATIZADO ---
            int idClienteLogueado = 0;

            // Leemos el Claim "IdCliente" que guardamos en la cookie al iniciar sesión
            var claimCliente = User.FindFirst("IdCliente")?.Value;

            if (!string.IsNullOrEmpty(claimCliente) && int.TryParse(claimCliente, out int parsedId))
            {
                idClienteLogueado = parsedId;
            }
            else
            {
                // Si por alguna razón el usuario no tiene cliente asociado, lo mandamos al Login por seguridad
                return RedirectToPage("/Login");
            }

            // Construimos la consulta trayendo solo lo que le pertenece a ESTE cliente en sesión
            // AsNoTracking: esta página solo lee y muestra datos, nunca los modifica, así que no
            // hace falta que EF Core arme el change-tracker para estas entidades (más memoria y
            // CPU por nada en un listado que puede tener bastantes operaciones + sus relaciones).
            var query = _context.Operaciones
.AsNoTracking()
.AsSplitQuery()
.Include(o => o.IdNavieraNavigation)
.Include(o => o.IdPuertoOrigenNavigation)
.Include(o => o.IdPuertoDestinoNavigation)
.Include(o => o.OperacionesTerrestres)
.Include(o => o.OperacionesDocumentales)
.Include(o => o.TransaccionesFinancieras)
.Include(o => o.Unidadestecnicas)
.Include(o => o.RoleosCancelaciones)
.Include(o => o.GestionTransbordo)
.Where(o => o.IdCliente == idClienteLogueado && o.IsDeleted == false); // AISLAMIENTO TOTAL

            // --- LÓGICA DEL BUSCADOR ---
            if (!string.IsNullOrEmpty(SearchString))
            {
                query = query.Where(o =>
                    (o.NumeroBooking != null && o.NumeroBooking.Contains(SearchString)) ||
                    (o.NumeroContenedor != null && o.NumeroContenedor.Contains(SearchString)) ||
                    (o.NumeroSello != null && o.NumeroSello.Contains(SearchString))
                );
            }

            OperacionesCliente = await query.OrderByDescending(o => o.Id).ToListAsync();

            // --- CÁLCULO AUTOMÁTICO DE LOS KPIs ---
            CargasActivas = OperacionesCliente.Count(o => o.EstadoWorkflow != "Cerrado" && o.EstadoWorkflow != "Cancelado" && o.EstadoWorkflow != "Finalizado" && !OperacionCerradaPorRetiro(o));
            EmbarquesFinalizados = OperacionesCliente.Count(o => o.EstadoWorkflow == "Cerrado" || o.EstadoWorkflow == "Finalizado" || OperacionCerradaPorRetiro(o));

            // Calculamos acciones requeridas (Ej: Borrador BL por aprobar)
            AccionesRequeridas = OperacionesCliente.Count(o =>
                o.OperacionesDocumentales.Any(d => d.EstadoDocumental == "BORRADOR ENVIADO" || d.LogSanitario != null && d.LogSanitario.Contains("ENVÍO A CLIENTE") && !d.LogSanitario.Contains("RESPUESTA")));

            // Calculamos Saldo Pendiente del Cliente (Solo facturas de INGRESO emitidas que no están pagadas)
            SaldoPendienteTotal = OperacionesCliente.SelectMany(o => o.TransaccionesFinancieras)
                .Where(t => t.TipoMovimiento == "INGRESO" && t.Moneda == "USD" && t.EstadoFila == "FACTURADO")
                .Sum(t => t.MontoNeto);

            return Page();
        }
    }
}