using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Finanzas
{
    public class SetCobroModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly LiquidacionExportService _exportService;

        public SetCobroModel(AtilsonContext context, LiquidacionExportService exportService)
        {
            _context = context;
            _exportService = exportService;
        }

        public Operacione Operacion { get; set; } = default!;

        public List<TransaccionesFinanciera> FacturasBase { get; set; } = new();
        public List<TransaccionesFinanciera> NotasCobroExtras { get; set; } = new();

        public decimal GranTotalUSD { get; set; }
        public decimal GranTotalCLP { get; set; }
        public decimal TotalPagadoUSD { get; set; }
        public decimal TotalPagadoCLP { get; set; }
        public decimal SaldoPendienteUSD { get; set; }
        public decimal SaldoPendienteCLP { get; set; }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null) return NotFound();
            var dto = await CargarAsync(id.Value);
            if (dto == null) return NotFound();
            return Page();
        }

        public async Task<IActionResult> OnGetExportarPdfAsync(int id)
        {
            var dto = await CargarAsync(id);
            if (dto == null) return NotFound();
            var pdf = _exportService.GenerarPdfOperacion(dto);
            return File(pdf, "application/pdf", $"SetCobro_{Operacion.NumeroBooking}.pdf");
        }

        public async Task<IActionResult> OnGetExportarExcelAsync(int id)
        {
            var dto = await CargarAsync(id);
            if (dto == null) return NotFound();
            var excel = _exportService.GenerarExcelOperacion(dto);
            return File(excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"SetCobro_{Operacion.NumeroBooking}.xlsx");
        }

        private async Task<LiquidacionOperacionDto?> CargarAsync(int id)
        {
            Operacion = await _context.Operaciones
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.TransaccionesFinancieras)
                .FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);

            if (Operacion == null) return null;

            var ingresos = Operacion.TransaccionesFinancieras
                .Where(t => t.TipoMovimiento == "INGRESO" && t.MontoNeto > 0)
                .ToList();

            FacturasBase = ingresos.Where(t => t.GrupoCobro != "Extracosto").ToList();
            NotasCobroExtras = ingresos.Where(t => t.GrupoCobro == "Extracosto").ToList();

            GranTotalUSD = ingresos.Where(t => t.Moneda == "USD").Sum(t => t.MontoNeto);
            GranTotalCLP = ingresos.Where(t => t.Moneda == "CLP").Sum(t => t.MontoNeto);
            TotalPagadoUSD = ingresos.Where(t => t.Moneda == "USD" && (t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO")).Sum(t => t.MontoNeto);
            TotalPagadoCLP = ingresos.Where(t => t.Moneda == "CLP" && (t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO")).Sum(t => t.MontoNeto);
            SaldoPendienteUSD = GranTotalUSD - TotalPagadoUSD;
            SaldoPendienteCLP = GranTotalCLP - TotalPagadoCLP;

            var dto = new LiquidacionOperacionDto
            {
                TituloDocumento = "SET DE COBRO / LIQUIDACIÓN",
                NumeroBooking = Operacion.NumeroBooking ?? "S/N",
                LabelContraparte = "Cliente",
                NombreContraparte = Operacion.IdClienteNavigation?.RazonSocial ?? "S/N",
                Naviera = Operacion.IdNavieraNavigation?.NombreNaviera ?? "N/A",
                Contenedor = Operacion.NumeroContenedor ?? "Por asignar",
                Sello = Operacion.NumeroSello ?? "Por asignar",
                Planta = Operacion.OperacionesTerrestres?.FirstOrDefault()?.PlantaCarga ?? "Terminal Portuario",
                CondicionPago = (Operacion.CondicionPago == 1 || Operacion.CondicionPago == 2) ? "PREPAID" : "COLLECT",
                TotalUSD = GranTotalUSD,
                PagadoUSD = TotalPagadoUSD,
                SaldoUSD = SaldoPendienteUSD,
                TotalCLP = GranTotalCLP,
                PagadoCLP = TotalPagadoCLP,
                SaldoCLP = SaldoPendienteCLP
            };
            dto.Base = FacturasBase.Select(MapLinea).ToList();
            dto.Extras = NotasCobroExtras.Select(MapLinea).ToList();
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
    }
}