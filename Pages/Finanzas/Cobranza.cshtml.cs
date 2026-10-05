using Microsoft.AspNetCore.Mvc;
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
    public class CobranzaModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly EmailService _emailService;
        private readonly LiquidacionExportService _exportService;

        public CobranzaModel(AtilsonContext context, EmailService emailService, LiquidacionExportService exportService)
        {
            _context = context;
            _emailService = emailService;
            _exportService = exportService;
        }

        public class CobranzaOperacionDto
        {
            public int IdOperacion { get; set; }
            public string Booking { get; set; } = "";
            public decimal TotalUsd { get; set; }
            public decimal PagadoUsd { get; set; }
            public decimal SaldoUsd { get; set; }
            public decimal TotalClp { get; set; }
            public decimal PagadoClp { get; set; }
            public decimal SaldoClp { get; set; }
        }

        public class CobranzaClienteDto
        {
            public int IdCliente { get; set; }
            public string NombreCliente { get; set; } = "";
            public string? Correo { get; set; }
            public bool SuscripcionActiva { get; set; }

            public decimal TotalFacturadoUsd { get; set; }
            public decimal TotalPagadoUsd { get; set; }
            public decimal SaldoUsd { get; set; } // positivo = debe, negativo = a favor

            public decimal TotalFacturadoClp { get; set; }
            public decimal TotalPagadoClp { get; set; }
            public decimal SaldoClp { get; set; }

            public int CantidadOperaciones { get; set; }
            public DateTime? UltimoRecordatorio { get; set; }
            public List<CobranzaOperacionDto> Operaciones { get; set; } = new();
        }

        public List<CobranzaClienteDto> ClientesConMovimiento { get; set; } = new();

        public async Task OnGetAsync()
        {
            ClientesConMovimiento = await ArmarListaCobranzaAsync();
        }

        // Trae TODOS los clientes con al menos un INGRESO registrado (pagado o
        // pendiente) — no solo los que deben, para que Finanzas también vea el
        // saldo a favor cuando corresponda (pagado > facturado).
        private async Task<List<CobranzaClienteDto>> ArmarListaCobranzaAsync()
        {
            var todosIngresos = await _context.TransaccionesFinancieras
    .Include(t => t.IdOperacionNavigation)
    .Where(t => t.TipoMovimiento == "INGRESO" && t.MontoNeto > 0 &&
                t.IdOperacionNavigation.IdCliente > 0)
    .ToListAsync();

            if (!todosIngresos.Any()) return new List<CobranzaClienteDto>();

            var idsClientes = todosIngresos.Select(t => t.IdOperacionNavigation.IdCliente).Distinct().ToList();
            var clientes = await _context.Clientes.Where(c => idsClientes.Contains(c.Id)).ToListAsync();

            var ultimosRecordatorios = await _context.RecordatoriosCobranza
                .Where(r => idsClientes.Contains(r.IdCliente))
                .GroupBy(r => r.IdCliente)
                .Select(g => new { IdCliente = g.Key, Ultimo = g.Max(x => x.FechaEnvio) })
                .ToListAsync();

            bool EsPagado(string? estado) => estado == "PAGADO" || estado == "CONCILIADO";

            var resultado = clientes.Select(c =>
            {
                var txsCliente = todosIngresos.Where(t => t.IdOperacionNavigation.IdCliente == c.Id).ToList();

                decimal totalUsd = txsCliente.Where(t => t.Moneda == "USD").Sum(t => t.MontoNeto);
                decimal pagadoUsd = txsCliente.Where(t => t.Moneda == "USD" && EsPagado(t.EstadoFila)).Sum(t => t.MontoNeto);
                decimal totalClp = txsCliente.Where(t => t.Moneda == "CLP").Sum(t => t.MontoNeto);
                decimal pagadoClp = txsCliente.Where(t => t.Moneda == "CLP" && EsPagado(t.EstadoFila)).Sum(t => t.MontoNeto);

                return new CobranzaClienteDto
                {
                    IdCliente = c.Id,
                    NombreCliente = c.RazonSocial ?? c.NombreCliente ?? "S/N",
                    Correo = c.Correo,
                    SuscripcionActiva = c.SuscripcionCobranzaActiva,
                    TotalFacturadoUsd = totalUsd,
                    TotalPagadoUsd = pagadoUsd,
                    SaldoUsd = totalUsd - pagadoUsd,
                    TotalFacturadoClp = totalClp,
                    TotalPagadoClp = pagadoClp,
                    SaldoClp = totalClp - pagadoClp,
                    CantidadOperaciones = txsCliente.Select(t => t.IdOperacion).Distinct().Count(),
                    UltimoRecordatorio = ultimosRecordatorios.FirstOrDefault(r => r.IdCliente == c.Id)?.Ultimo,
                    Operaciones = txsCliente.GroupBy(t => t.IdOperacion).Select(g => new CobranzaOperacionDto
                    {
                        IdOperacion = g.Key,
                        Booking = g.First().IdOperacionNavigation?.NumeroBooking ?? $"OP-{g.Key}",
                        TotalUsd = g.Where(t => t.Moneda == "USD").Sum(t => t.MontoNeto),
                        PagadoUsd = g.Where(t => t.Moneda == "USD" && EsPagado(t.EstadoFila)).Sum(t => t.MontoNeto),
                        SaldoUsd = g.Where(t => t.Moneda == "USD").Sum(t => t.MontoNeto) - g.Where(t => t.Moneda == "USD" && EsPagado(t.EstadoFila)).Sum(t => t.MontoNeto),
                        TotalClp = g.Where(t => t.Moneda == "CLP").Sum(t => t.MontoNeto),
                        PagadoClp = g.Where(t => t.Moneda == "CLP" && EsPagado(t.EstadoFila)).Sum(t => t.MontoNeto),
                        SaldoClp = g.Where(t => t.Moneda == "CLP").Sum(t => t.MontoNeto) - g.Where(t => t.Moneda == "CLP" && EsPagado(t.EstadoFila)).Sum(t => t.MontoNeto)
                    }).ToList()
                };
            })
            .Where(d => d.TotalFacturadoUsd > 0 || d.TotalFacturadoClp > 0)
            .OrderByDescending(d => d.SaldoUsd)
            .ToList();

            return resultado;
        }

        // Activa/desactiva la suscripción semanal automática para este cliente.
        // El job (CobranzaRecordatorioService) solo procesa clientes con este flag en true.
        public async Task<IActionResult> OnPostToggleSuscripcionAsync(int idCliente)
        {
            var cliente = await _context.Clientes.FindAsync(idCliente);
            if (cliente == null) return new JsonResult(new { success = false, message = "Cliente no encontrado." });

            cliente.SuscripcionCobranzaActiva = !cliente.SuscripcionCobranzaActiva;
            await _context.SaveChangesAsync();

            return new JsonResult(new { success = true, activa = cliente.SuscripcionCobranzaActiva });
        }

        // Envío manual inmediato — adjunta PDF y Excel del consolidado, igual que
        // hará el job semanal automático.
        public async Task<IActionResult> OnPostEnviarRecordatorioAsync(int idCliente)
        {
            var cliente = await _context.Clientes.FindAsync(idCliente);
            if (cliente == null || string.IsNullOrWhiteSpace(cliente.Correo))
                return new JsonResult(new { success = false, message = "El cliente no tiene correo registrado." });

            var resultado = await EnviarRecordatorioClienteAsync(cliente, "MANUAL", User.Identity?.Name ?? "Finanzas");
            if (!resultado.Success) return new JsonResult(new { success = false, message = resultado.Mensaje });

            return new JsonResult(new { success = true });
        }

        // Compartido con el job automático (Services/CobranzaRecordatorioService.cs)
        // para que ambos generen exactamente el mismo correo y adjuntos.
        public async Task<(bool Success, string Mensaje)> EnviarRecordatorioClienteAsync(Cliente cliente, string tipoEnvio, string? usuario)
        {
            var lista = await ArmarListaCobranzaAsync();
            var dto = lista.FirstOrDefault(d => d.IdCliente == cliente.Id);
            if (dto == null || (dto.SaldoUsd <= 0 && dto.SaldoClp <= 0))
                return (false, "Este cliente no tiene saldo pendiente.");

            var estadoCuentaDto = ConstruirEstadoCuentaDto(cliente, dto);
            byte[] pdf = _exportService.GenerarPdfEstadoCuentaCliente(estadoCuentaDto);
            byte[] excel = _exportService.GenerarExcelEstadoCuentaCliente(estadoCuentaDto);

            string filas = string.Join("", dto.Operaciones.Where(o => o.SaldoUsd > 0 || o.SaldoClp > 0).Select(o =>
                $"<tr><td style='padding:8px 12px; border-bottom:1px solid #e2e8f0;'>{o.Booking}</td>" +
                $"<td style='padding:8px 12px; border-bottom:1px solid #e2e8f0; text-align:right;'>{(o.SaldoUsd > 0 ? $"USD {o.SaldoUsd:N0}" : "")} {(o.SaldoClp > 0 ? $"CLP {o.SaldoClp:N0}" : "")}</td></tr>"));

            string html = $@"<div style='font-family:Arial,sans-serif;max-width:580px;margin:0 auto;'>
                <div style='background:linear-gradient(135deg,#0f172a 0%,#6d2d9e 100%);padding:24px 28px;border-radius:8px 8px 0 0;'>
                    <h2 style='color:#fff;margin:0;font-size:18px;'>Recordatorio de Cuenta Corriente</h2>
                </div>
                <div style='background:#fff;padding:28px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;'>
                    <p style='font-size:14px;color:#475569;'>Estimado(a) {dto.NombreCliente},</p>
                    <p style='font-size:14px;color:#475569;'>Le recordamos que registra un saldo pendiente de pago por los siguientes embarques:</p>
                    <table style='width:100%;border-collapse:collapse;margin:16px 0;'>
                        <thead><tr><th style='text-align:left;padding:8px 12px;border-bottom:2px solid #0f172a;font-size:11px;text-transform:uppercase;'>Booking</th><th style='text-align:right;padding:8px 12px;border-bottom:2px solid #0f172a;font-size:11px;text-transform:uppercase;'>Saldo</th></tr></thead>
                        <tbody>{filas}</tbody>
                    </table>
                    <p style='font-size:14px;font-weight:700;color:#0f172a;'>Total pendiente: {(dto.SaldoUsd > 0 ? $"USD {dto.SaldoUsd:N0}" : "")} {(dto.SaldoClp > 0 ? $"CLP {dto.SaldoClp:N0}" : "")}</p>
                    <p style='font-size:12px;color:#94a3b8;'>Adjuntamos el detalle de su cuenta corriente en formato PDF y Excel. También puede revisarlo en cualquier momento desde su Portal Atilson, sección Facturas.</p>
                    <hr style='border:0;border-top:1px solid #e2e8f0;margin:24px 0;'>
                    <p style='font-size:11px;color:#94a3b8;text-align:center;'>Atilson Cargo SpA · Administración y Finanzas</p>
                </div></div>";

            var adjuntos = new List<(string NombreArchivo, byte[] Contenido, string TipoMime)>
            {
                ($"EstadoCuenta_{dto.NombreCliente}.pdf", pdf, "application/pdf"),
                ($"EstadoCuenta_{dto.NombreCliente}.xlsx", excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            };

            await _emailService.EnviarCorreoAsync(cliente.Correo!, "Recordatorio de Cuenta Corriente — Atilson Cargo", html, adjuntos);

            _context.RecordatoriosCobranza.Add(new RecordatorioCobranza
            {
                IdCliente = cliente.Id,
                FechaEnvio = DateTime.Now,
                MontoPendienteUsd = dto.SaldoUsd,
                TipoEnvio = tipoEnvio,
                Usuario = usuario
            });
            await _context.SaveChangesAsync();

            return (true, "");
        }

        private EstadoCuentaClienteDto ConstruirEstadoCuentaDto(Cliente cliente, CobranzaClienteDto dto)
        {
            var estadoCuenta = new EstadoCuentaClienteDto
            {
                ClienteNombre = dto.NombreCliente,
                Desde = DateTime.Now.AddYears(-5), // consolidado histórico completo, no acotado por fecha
                Hasta = DateTime.Now
            };
            foreach (var op in dto.Operaciones)
            {
                estadoCuenta.Operaciones.Add(new EstadoCuentaOperacionDto
                {
                    NumeroBooking = op.Booking,
                    Fecha = null,
                    TotalUsd = op.TotalUsd,
                    PagadoUsd = op.PagadoUsd,
                    SaldoUsd = op.SaldoUsd,
                    TotalClp = op.TotalClp,
                    PagadoClp = op.PagadoClp,
                    SaldoClp = op.SaldoClp
                });
            }
            return estadoCuenta;
        }
    }
}