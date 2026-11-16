using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Finanzas
{
    public class ConsolidadoClienteModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly LiquidacionExportService _exportService;

        public ConsolidadoClienteModel(AtilsonContext context, LiquidacionExportService exportService)
        {
            _context = context;
            _exportService = exportService;
        }

        [BindProperty(SupportsGet = true)] public int? IdCliente { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? Hasta { get; set; }
        [BindProperty(SupportsGet = true)] public string? Buscar { get; set; }
        [BindProperty(SupportsGet = true)] public string Origen { get; set; } = "TODOS";
        [BindProperty(SupportsGet = true)] public string Estado { get; set; } = "TODOS";

        public SelectList ListaClientes { get; set; } = default!;
        public ConsolidadoClienteDto? Resultado { get; set; }

        public async Task OnGetAsync()
        {
            var clientes = await _context.Clientes.AsNoTracking().Where(c => c.Activo == 1).OrderBy(c => c.RazonSocial).ToListAsync();
            ListaClientes = new SelectList(clientes, "Id", "RazonSocial", IdCliente);

            Origen = (Origen ?? "TODOS").Trim().ToUpperInvariant();
            if (Origen != "SISTEMA" && Origen != "HISTORICO") Origen = "TODOS";
            Estado = (Estado ?? "TODOS").Trim().ToUpperInvariant();
            if (!new[] { "TODOS", "PENDIENTE", "FACTURADO", "PAGADO" }.Contains(Estado)) Estado = "TODOS";

            if (IdCliente.HasValue)
            {
                DateTime desde = Desde?.Date ?? new DateTime(1900, 1, 1);
                DateTime hasta = Hasta?.Date.AddDays(1).AddTicks(-1) ?? DateTime.Today.AddDays(1).AddTicks(-1);
                Resultado = await ConstruirAsync(IdCliente.Value, desde, hasta, Buscar, Origen, Estado);
            }
        }

        public async Task<IActionResult> OnGetExportarPdfAsync(int idCliente, DateTime desde, DateTime hasta)
        {
            var dto = await ConstruirAsync(idCliente, desde, hasta.Date.AddDays(1).AddTicks(-1), Buscar, Origen, Estado);
            if (dto == null) return NotFound();
            var pdf = _exportService.GenerarPdfConsolidado(dto);
            return File(pdf, "application/pdf", $"Consolidado_{dto.ClienteNombre}_{desde:yyyyMM}.pdf");
        }

        public async Task<IActionResult> OnGetExportarExcelAsync(int idCliente, DateTime desde, DateTime hasta)
        {
            var dto = await ConstruirAsync(idCliente, desde, hasta.Date.AddDays(1).AddSeconds(-1));
            if (dto == null) return NotFound();
            var excel = _exportService.GenerarExcelConsolidado(dto);
            return File(excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Consolidado_{dto.ClienteNombre}_{desde:yyyyMM}.xlsx");
        }

        // Se agrupa filtrando por FechaCreacion de la Operación dentro del rango —
        // el criterio más simple y transparente: "las operaciones abiertas para
        // este cliente en este período", sin depender de si cada transacción
        // individual ya fue pagada o facturada.
        private async Task<ConsolidadoClienteDto?> ConstruirAsync(int idCliente, DateTime desde, DateTime hasta, string? buscar = null, string origen = "TODOS", string estado = "TODOS")
        {
            // Solo lectura (esto solo arma un reporte/export): AsNoTracking evita que EF Core
            // arme el change-tracker para estas entidades.
            var cliente = await _context.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == idCliente);
            if (cliente == null) return null;

            var query = _context.Operaciones
                .AsNoTracking()
                .Where(o => o.IdCliente == idCliente && !o.IsDeleted &&
                            o.FechaCreacion.HasValue && o.FechaCreacion.Value >= desde && o.FechaCreacion.Value <= hasta);

            if (origen == "HISTORICO") query = query.Where(o => o.EsHistorica);
            else if (origen == "SISTEMA") query = query.Where(o => !o.EsHistorica);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string s = buscar.Trim();
                query = query.Where(o =>
                    o.NumeroBooking.Contains(s) ||
                    o.TransaccionesFinancieras.Any(t =>
                        (t.NumeroDocumento != null && t.NumeroDocumento.Contains(s)) ||
                        (t.Concepto != null && t.Concepto.Contains(s))));
            }

            var operaciones = await query
                .Include(o => o.TransaccionesFinancieras)
                .OrderBy(o => o.FechaCreacion)
                .ThenBy(o => o.Id)
                .ToListAsync();

            var dto = new ConsolidadoClienteDto
            {
                ClienteNombre = cliente.RazonSocial ?? "S/N",
                Desde = desde,
                Hasta = hasta
            };

            foreach (var op in operaciones)
            {
                IEnumerable<TransaccionesFinanciera> txs = op.TransaccionesFinancieras.Where(t => t.MontoNeto > 0);
                if (estado == "PAGADO")
                    txs = txs.Where(t => t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO");
                else if (estado == "FACTURADO")
                    txs = txs.Where(t => t.EstadoFila == "FACTURADO");
                else if (estado == "PENDIENTE")
                    txs = txs.Where(t => t.EstadoFila != "PAGADO" && t.EstadoFila != "CONCILIADO" && t.EstadoFila != "NO FACTURABLE");

                if (!txs.Any()) continue;

                var ingresos = txs.Where(t => t.TipoMovimiento == "INGRESO");
                var egresos = txs.Where(t => t.TipoMovimiento == "EGRESO");

                dto.Operaciones.Add(new ConsolidadoOperacionDto
                {
                    NumeroBooking = string.IsNullOrWhiteSpace(op.NumeroBooking) ? $"OP-{op.Id}" : op.NumeroBooking,
                    EsHistorica = op.EsHistorica,
                    Fecha = op.FechaCreacion,
                    IngresoUsd = ingresos.Where(t => t.Moneda == "USD").Sum(t => t.MontoNeto),
                    IngresoClp = ingresos.Where(t => t.Moneda == "CLP").Sum(t => t.MontoNeto),
                    EgresoUsd = egresos.Where(t => t.Moneda == "USD").Sum(t => t.MontoNeto),
                    EgresoClp = egresos.Where(t => t.Moneda == "CLP").Sum(t => t.MontoNeto)
                });
            }

            return dto;
        }
    }
}