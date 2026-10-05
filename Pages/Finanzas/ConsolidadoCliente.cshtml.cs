using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using System;
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

        public SelectList ListaClientes { get; set; } = default!;
        public ConsolidadoClienteDto? Resultado { get; set; }

        public async Task OnGetAsync()
        {
            var clientes = await _context.Clientes.Where(c => c.Activo == 1).OrderBy(c => c.RazonSocial).ToListAsync();
            ListaClientes = new SelectList(clientes, "Id", "RazonSocial", IdCliente);

            if (IdCliente.HasValue && Desde.HasValue && Hasta.HasValue)
            {
                Resultado = await ConstruirAsync(IdCliente.Value, Desde.Value, Hasta.Value.Date.AddDays(1).AddSeconds(-1));
            }
        }

        public async Task<IActionResult> OnGetExportarPdfAsync(int idCliente, DateTime desde, DateTime hasta)
        {
            var dto = await ConstruirAsync(idCliente, desde, hasta.Date.AddDays(1).AddSeconds(-1));
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
        private async Task<ConsolidadoClienteDto?> ConstruirAsync(int idCliente, DateTime desde, DateTime hasta)
        {
            var cliente = await _context.Clientes.FindAsync(idCliente);
            if (cliente == null) return null;

            var operaciones = await _context.Operaciones
                .Where(o => o.IdCliente == idCliente && !o.IsDeleted &&
                            o.FechaCreacion.HasValue && o.FechaCreacion.Value >= desde && o.FechaCreacion.Value <= hasta)
                .Include(o => o.TransaccionesFinancieras)
                .OrderBy(o => o.FechaCreacion)
                .ToListAsync();

            var dto = new ConsolidadoClienteDto
            {
                ClienteNombre = cliente.RazonSocial ?? "S/N",
                Desde = desde,
                Hasta = hasta
            };

            foreach (var op in operaciones)
            {
                var ingresos = op.TransaccionesFinancieras.Where(t => t.TipoMovimiento == "INGRESO" && t.MontoNeto > 0);
                var egresos = op.TransaccionesFinancieras.Where(t => t.TipoMovimiento == "EGRESO" && t.MontoNeto > 0);

                dto.Operaciones.Add(new ConsolidadoOperacionDto
                {
                    NumeroBooking = string.IsNullOrWhiteSpace(op.NumeroBooking) ? $"OP-{op.Id}" : op.NumeroBooking,
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