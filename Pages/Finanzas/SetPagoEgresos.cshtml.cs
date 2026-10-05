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
    public class SetPagoEgresosModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly LiquidacionExportService _exportService;

        public SetPagoEgresosModel(AtilsonContext context, LiquidacionExportService exportService)
        {
            _context = context;
            _exportService = exportService;
        }

        public Operacione Operacion { get; set; } = default!;
        public List<(TransaccionesFinanciera Tx, string Contraparte)> CostosBase { get; set; } = new();
        public List<(TransaccionesFinanciera Tx, string Contraparte)> ExtracostosPago { get; set; } = new();

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
            return File(pdf, "application/pdf", $"LiquidacionEgresos_{Operacion.NumeroBooking}.pdf");
        }

        public async Task<IActionResult> OnGetExportarExcelAsync(int id)
        {
            var dto = await CargarAsync(id);
            if (dto == null) return NotFound();
            var excel = _exportService.GenerarExcelOperacion(dto);
            return File(excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"LiquidacionEgresos_{Operacion.NumeroBooking}.xlsx");
        }

        private async Task<LiquidacionOperacionDto?> CargarAsync(int id)
        {
            Operacion = await _context.Operaciones
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.OperacionesTerrestres)
                .Include(o => o.OperacionesDocumentales).ThenInclude(d => d.IdAgenciaAduanaNavigation)
                .Include(o => o.OperacionesAlmacenamientos).ThenInclude(a => a.IdProveedorNavigation)
                .Include(o => o.TransaccionesFinancieras).ThenInclude(t => t.IdProveedorNavigation)
                .FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);

            if (Operacion == null) return null;

            var egresos = Operacion.TransaccionesFinancieras
                .Where(t => t.TipoMovimiento == "EGRESO" && t.MontoNeto > 0)
                .ToList();

            var conContraparte = egresos.Select(t => (Tx: t, Contraparte: ResolverContraparte(t))).ToList();
            CostosBase = conContraparte.Where(x => x.Tx.GrupoCobro != "Extracosto").ToList();
            ExtracostosPago = conContraparte.Where(x => x.Tx.GrupoCobro == "Extracosto").ToList();

            GranTotalUSD = egresos.Where(t => t.Moneda == "USD").Sum(t => t.MontoNeto);
            GranTotalCLP = egresos.Where(t => t.Moneda == "CLP").Sum(t => t.MontoNeto);
            TotalPagadoUSD = egresos.Where(t => t.Moneda == "USD" && (t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO")).Sum(t => t.MontoNeto);
            TotalPagadoCLP = egresos.Where(t => t.Moneda == "CLP" && (t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO")).Sum(t => t.MontoNeto);
            SaldoPendienteUSD = GranTotalUSD - TotalPagadoUSD;
            SaldoPendienteCLP = GranTotalCLP - TotalPagadoCLP;

            var dto = new LiquidacionOperacionDto
            {
                TituloDocumento = "LIQUIDACIÓN DE EGRESOS / ORDEN DE PAGO",
                NumeroBooking = Operacion.NumeroBooking ?? "S/N",
                LabelContraparte = "Proveedor / Beneficiario",
                NombreContraparte = "Múltiples (ver detalle)",
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
            dto.Base = CostosBase.Select(x => MapLinea(x.Tx, x.Contraparte)).ToList();
            dto.Extras = ExtracostosPago.Select(x => MapLinea(x.Tx, x.Contraparte)).ToList();
            return dto;
        }

        private LiquidacionLineaDto MapLinea(TransaccionesFinanciera tx, string contraparte) => new LiquidacionLineaDto
        {
            Concepto = tx.Concepto,
            Contraparte = contraparte,
            NumeroDocumento = tx.NumeroDocumento,
            Pagado = tx.EstadoFila == "PAGADO" || tx.EstadoFila == "CONCILIADO",
            Moneda = tx.Moneda,
            MontoNeto = tx.MontoNeto
        };

        // Misma lógica de resolución "a quién le paga Atilson" que usa
        // ControlFinanciero.cshtml en la pestaña Egresos, centralizada aquí
        // para no duplicarla ni desalinearla entre ambas pantallas.
        private string ResolverContraparte(TransaccionesFinanciera tx)
        {
            string gKey = (tx.GrupoCobro ?? "").ToUpper();
            string? nombre = tx.IdProveedorNavigation?.NombreProveedor;

            if (gKey.Contains("DOCUMENTAL") || gKey.Contains("ADUANA"))
            {
                var doc = Operacion.OperacionesDocumentales?.FirstOrDefault();
                nombre = doc?.IdAgenciaAduanaNavigation?.NombreAgencia ?? doc?.AgenciaAduana;
            }
            else if (gKey.Contains("GATE") || gKey.Contains("DEPÓSITO") || gKey.Contains("DEPOSITO"))
            {
                string pagoGateUpper = (Operacion.PagoGate ?? "").ToUpper();
                if (pagoGateUpper.Contains("AGENCIA"))
                {
                    var docGate = Operacion.OperacionesDocumentales?.FirstOrDefault();
                    nombre = docGate?.IdAgenciaAduanaNavigation?.NombreAgencia ?? docGate?.AgenciaAduana ?? "Agencia (Sin Asignar)";
                }
                else if (pagoGateUpper.Contains("ATILSON PAGA DIRECTAMENTE"))
                {
                    nombre = "Atilson (Pago Directo)";
                }
                else
                {
                    nombre = "Por Definir (Pestaña Gate)";
                }
            }
            else if (string.IsNullOrWhiteSpace(nombre))
            {
                if (gKey.Contains("MARÍTIMO") || gKey.Contains("MARITIMO")) nombre = Operacion.IdNavieraNavigation?.NombreNaviera;
                else if (gKey.Contains("TERRESTRE") || gKey.Contains("TRANSPORTE")) nombre = Operacion.OperacionesTerrestres?.FirstOrDefault(x => x.IdUnidadTecnica == null)?.EmpresaTransporte;
                else if (gKey.Contains("ALMACENAMIENTO") || gKey.Contains("BODEGA") || gKey.Contains("PATIO")) nombre = Operacion.OperacionesAlmacenamientos?.FirstOrDefault()?.IdProveedorNavigation?.NombreProveedor;
            }

            return string.IsNullOrWhiteSpace(nombre) ? "Por Asignar" : nombre;
        }
    }
}