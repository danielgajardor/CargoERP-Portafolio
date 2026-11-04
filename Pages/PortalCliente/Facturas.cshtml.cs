using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    public class FacturasModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly LiquidacionExportService _exportService;

        public FacturasModel(AtilsonContext context, LiquidacionExportService exportService)
        {
            _context = context;
            _exportService = exportService;
        }

        public IList<Operacione> OperacionesFinancieras { get; set; } = default!;

        [BindProperty(SupportsGet = true)]
        public DateTime? FechaDesde { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? FechaHasta { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SearchString { get; set; }

        public decimal TotalFacturadoGlobal { get; set; }
        public decimal TotalPendienteGlobal { get; set; }

        private int? ObtenerIdClienteSesion()
        {
            var claim = User.FindFirst("IdCliente")?.Value;
            if (!string.IsNullOrEmpty(claim) && int.TryParse(claim, out int id)) return id;
            return null;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var idClienteLogueado = ObtenerIdClienteSesion();
            if (idClienteLogueado == null) return RedirectToPage("/Login");

            var query = _context.Operaciones
                .AsNoTracking()
                .Include(o => o.IdPuertoOrigenNavigation)
                .Include(o => o.IdPuertoDestinoNavigation)
                .Include(o => o.TransaccionesFinancieras)
                .Where(o => o.IdCliente == idClienteLogueado.Value && !o.IsDeleted &&
                            o.TransaccionesFinancieras.Any(t => t.TipoMovimiento == "INGRESO"));

            if (FechaDesde.HasValue) query = query.Where(o => o.FechaCreacion >= FechaDesde.Value);
            if (FechaHasta.HasValue) query = query.Where(o => o.FechaCreacion <= FechaHasta.Value.AddDays(1).AddTicks(-1));

            if (!string.IsNullOrEmpty(SearchString))
            {
                string s = SearchString.Trim();
                query = query.Where(o =>
                    (o.NumeroBooking != null && o.NumeroBooking.Contains(s)) ||
                    o.TransaccionesFinancieras.Any(t => t.NumeroDocumento != null && t.NumeroDocumento.Contains(s)));
            }

            OperacionesFinancieras = await query.OrderByDescending(o => o.Id).ToListAsync();

            var txs = OperacionesFinancieras.SelectMany(o => o.TransaccionesFinancieras.Where(t => t.TipoMovimiento == "INGRESO" && t.Moneda == "USD")).ToList();

            TotalFacturadoGlobal = txs.Where(t => t.EstadoFila == "FACTURADO" || t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO").Sum(t => t.MontoNeto);
            TotalPendienteGlobal = txs.Where(t => t.EstadoFila != "PAGADO" && t.EstadoFila != "CONCILIADO").Sum(t => t.MontoNeto);

            return Page();
        }

        public async Task<IActionResult> OnGetDescargarPdfAsync(int id)
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null) return RedirectToPage("/Login");

            var op = await CargarOperacionAsync(id, idCliente.Value);
            if (op == null) return NotFound();

            var pdf = _exportService.GenerarPdfOperacion(ConstruirLiquidacionDto(op));
            return File(pdf, "application/pdf", $"Liquidacion_{op.NumeroBooking}.pdf");
        }

        public async Task<IActionResult> OnGetDescargarExcelAsync(int id)
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null) return RedirectToPage("/Login");

            var op = await CargarOperacionAsync(id, idCliente.Value);
            if (op == null) return NotFound();

            var excel = _exportService.GenerarExcelOperacion(ConstruirLiquidacionDto(op));
            return File(excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Liquidacion_{op.NumeroBooking}.xlsx");
        }

        public async Task<IActionResult> OnGetDescargarConsolidadoPdfAsync(DateTime desde, DateTime hasta)
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null) return RedirectToPage("/Login");

            var dto = await ConstruirEstadoCuentaAsync(idCliente.Value, desde, hasta.Date.AddDays(1).AddSeconds(-1));
            if (dto == null) return NotFound();

            var pdf = _exportService.GenerarPdfEstadoCuentaCliente(dto);
            return File(pdf, "application/pdf", $"EstadoCuenta_{desde:yyyyMM}.pdf");
        }

        public async Task<IActionResult> OnGetDescargarConsolidadoExcelAsync(DateTime desde, DateTime hasta)
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null) return RedirectToPage("/Login");

            var dto = await ConstruirEstadoCuentaAsync(idCliente.Value, desde, hasta.Date.AddDays(1).AddSeconds(-1));
            if (dto == null) return NotFound();

            var excel = _exportService.GenerarExcelEstadoCuentaCliente(dto);
            return File(excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"EstadoCuenta_{desde:yyyyMM}.xlsx");
        }

        // Aislamiento de seguridad: la operación debe pertenecer al cliente en
        // sesión, o se rechaza con 404 sin revelar que la operación existe.
        private async Task<Operacione?> CargarOperacionAsync(int id, int idCliente)
        {
            return await _context.Operaciones
                .AsNoTracking()
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.TransaccionesFinancieras)
                .FirstOrDefaultAsync(o => o.Id == id && o.IdCliente == idCliente && !o.IsDeleted);
        }

        private LiquidacionOperacionDto ConstruirLiquidacionDto(Operacione op)
        {
            var ingresos = op.TransaccionesFinancieras.Where(t => t.TipoMovimiento == "INGRESO" && t.MontoNeto > 0).ToList();
            var baseTx = ingresos.Where(t => t.GrupoCobro != "Extracosto").ToList();
            var extras = ingresos.Where(t => t.GrupoCobro == "Extracosto").ToList();

            decimal totalUsd = ingresos.Where(t => t.Moneda == "USD").Sum(t => t.MontoNeto);
            decimal totalClp = ingresos.Where(t => t.Moneda == "CLP").Sum(t => t.MontoNeto);
            decimal pagadoUsd = ingresos.Where(t => t.Moneda == "USD" && (t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO")).Sum(t => t.MontoNeto);
            decimal pagadoClp = ingresos.Where(t => t.Moneda == "CLP" && (t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO")).Sum(t => t.MontoNeto);

            var dto = new LiquidacionOperacionDto
            {
                TituloDocumento = "SET DE COBRO / LIQUIDACIÓN",
                NumeroBooking = op.NumeroBooking ?? "S/N",
                LabelContraparte = "Cliente",
                NombreContraparte = op.IdClienteNavigation?.RazonSocial ?? "S/N",
                Naviera = op.IdNavieraNavigation?.NombreNaviera ?? "N/A",
                Contenedor = op.NumeroContenedor ?? "Por asignar",
                Sello = op.NumeroSello ?? "Por asignar",
                Planta = op.OperacionesTerrestres?.FirstOrDefault()?.PlantaCarga ?? "Terminal Portuario",
                CondicionPago = (op.CondicionPago == 1 || op.CondicionPago == 2) ? "PREPAID" : "COLLECT",
                TotalUSD = totalUsd,
                PagadoUSD = pagadoUsd,
                SaldoUSD = totalUsd - pagadoUsd,
                TotalCLP = totalClp,
                PagadoCLP = pagadoClp,
                SaldoCLP = totalClp - pagadoClp
            };
            dto.Base = baseTx.Select(MapLinea).ToList();
            dto.Extras = extras.Select(MapLinea).ToList();
            return dto;
        }

        private LiquidacionLineaDto MapLinea(TransaccionesFinanciera tx) => new LiquidacionLineaDto
        {
            Concepto = tx.Concepto,
            NumeroDocumento = tx.NumeroDocumento,
            Pagado = tx.EstadoFila == "PAGADO" || tx.EstadoFila == "CONCILIADO",
            Moneda = tx.Moneda,
            MontoNeto = tx.MontoNeto
        };

        private async Task<EstadoCuentaClienteDto?> ConstruirEstadoCuentaAsync(int idCliente, DateTime desde, DateTime hasta)
        {
            var cliente = await _context.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == idCliente);
            if (cliente == null) return null;

            var operaciones = await _context.Operaciones
                .AsNoTracking()
                .Where(o => o.IdCliente == idCliente && !o.IsDeleted &&
                            o.FechaCreacion.HasValue && o.FechaCreacion.Value >= desde && o.FechaCreacion.Value <= hasta)
                .Include(o => o.TransaccionesFinancieras)
                .OrderBy(o => o.FechaCreacion)
                .ToListAsync();

            var dto = new EstadoCuentaClienteDto { ClienteNombre = cliente.RazonSocial ?? "S/N", Desde = desde, Hasta = hasta };
            foreach (var op in operaciones)
            {
                var ing = op.TransaccionesFinancieras.Where(t => t.TipoMovimiento == "INGRESO" && t.MontoNeto > 0).ToList();
                decimal totalUsd = ing.Where(t => t.Moneda == "USD").Sum(t => t.MontoNeto);
                decimal totalClp = ing.Where(t => t.Moneda == "CLP").Sum(t => t.MontoNeto);
                decimal pagadoUsd = ing.Where(t => t.Moneda == "USD" && (t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO")).Sum(t => t.MontoNeto);
                decimal pagadoClp = ing.Where(t => t.Moneda == "CLP" && (t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO")).Sum(t => t.MontoNeto);

                dto.Operaciones.Add(new EstadoCuentaOperacionDto
                {
                    NumeroBooking = string.IsNullOrWhiteSpace(op.NumeroBooking) ? $"OP-{op.Id}" : op.NumeroBooking,
                    Fecha = op.FechaCreacion,
                    TotalUsd = totalUsd,
                    PagadoUsd = pagadoUsd,
                    SaldoUsd = totalUsd - pagadoUsd,
                    TotalClp = totalClp,
                    PagadoClp = pagadoClp,
                    SaldoClp = totalClp - pagadoClp
                });
            }
            return dto;
        }
    }
}