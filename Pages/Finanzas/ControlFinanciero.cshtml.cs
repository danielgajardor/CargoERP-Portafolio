using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Finanzas
{
    public class ControlFinancieroModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly IWebHostEnvironment _env;

        private readonly FinanzasSyncService _sync;

        // Valor esperado en la factura: neto + IVA (desde oct-2026 también todo lo Marítimo).
        // El rebate Collect va sin IVA (devolución de flete).
        // Flete Marítimo, Roleo, Cancelación y No Show: siempre en USD y sin IVA.
        public static bool EsMaritimoUsdSinIva(string? concepto)
        {
            string c = (concepto ?? "").ToUpperInvariant();
            return c.StartsWith("FLETE MARÍTIMO") || c.StartsWith("FLETE MARITIMO")
                || c.StartsWith("ROLEO MARÍTIMO") || c.StartsWith("ROLEO MARITIMO")
                || c.StartsWith("CANCELACIÓN / NO SHOW") || c.StartsWith("CANCELACION / NO SHOW");
        }

        private static decimal ValorCompararAga(TransaccionesFinanciera tx)
            => (EsMaritimoUsdSinIva(tx.Concepto)
                || ((tx.Concepto ?? "").StartsWith("Rebate Marítimo", StringComparison.OrdinalIgnoreCase) && tx.IvaExento != false))
                ? tx.MontoNeto
                : Math.Round(tx.MontoNeto * 1.19m, tx.Moneda == "CLP" ? 0 : 2);

        private static decimal TolAga(TransaccionesFinanciera tx) => tx.Moneda == "CLP" ? 1m : 0.01m;

        public ControlFinancieroModel(AtilsonContext context, IWebHostEnvironment env, AtilsonCargoSpa.Services.FinanzasSyncService sync)
        {
            _context = context;
            _env = env;
            _sync = sync;
        }

        public List<TarifaGate> ComponentesGate { get; set; } = new();

        // Conceptos marcados "Servicio Gratuito" en TarifasClientes (Comercial),
        // agrupados por cliente — mismo patrón que usa AcuerdosOperativosModel,
        // para saber si un Ingreso en $0 es un ajuste real pendiente o un $0
        // intencional por tarifa plana paquetizada (ej. Promar).
        public Dictionary<int, List<string>> TarifasGratuitasPorCliente { get; set; } = new();

        public static bool ConceptoCubiertoPorTarifaPlana(string? conceptoCompleto, List<string> conceptosGratuitos)
        {
            if (conceptosGratuitos == null || !conceptosGratuitos.Any() || string.IsNullOrWhiteSpace(conceptoCompleto)) return false;
            string baseConcepto = System.Text.RegularExpressions.Regex.Replace(conceptoCompleto, @"\s*\(U\d+\)", "").Trim().ToUpper();
            return conceptosGratuitos.Any(c => !string.IsNullOrWhiteSpace(c) &&
                (baseConcepto.Contains(c.ToUpper()) || c.ToUpper().Contains(baseConcepto)));
        }

        public IList<Operacione> Operaciones { get; set; } = default!;

        // Equivalencia en CLP (oct-2026): dólar observado y UF del día (Banco Central, con
        // respaldo si no responde). Solo para mostrar referencias y totales consolidados;
        // no cambia ningún monto guardado.
        public decimal DolarHoy { get; set; }
        public decimal UfHoy { get; set; }
        public AtilsonCargoSpa.Services.IndicadorValor? DolarInfo { get; set; }
        public AtilsonCargoSpa.Services.IndicadorValor? UfInfo { get; set; }

        // Nombre de cada agencia de aduanas (por Id), para el proveedor de las filas
        // documentales cuando la navegación no viene cargada.
        public Dictionary<int, string> NombresAgencias { get; set; } = new();

        // Traspaso Ops -> Finanzas (oct-2026): filas que cambiaron entre la finalización
        // anterior y la última (solo bookings finalizados más de una vez).
        public Dictionary<int, List<string>> CambiosTraspaso { get; set; } = new();
        /// <summary>Respaldo adjunto a la última reapertura de cada operación reabierta.</summary>
        public Dictionary<int, (string Ruta, string Nombre)> RespaldosReapertura { get; set; } = new();

        // ?abrir=ID desde el Main Board: despliega ese booking al cargar.
        [BindProperty(SupportsGet = true)]
        public int? Abrir { get; set; }

        public async Task<IActionResult> OnPostGuardarComponentesGateAsync(int transaccionId, List<string> componentes, string todos)
        {
            var tx = await _context.TransaccionesFinancieras.FindAsync(transaccionId);
            if (tx == null) return RedirectToPage("./ControlFinanciero");

            if (tx.EstadoFila == "PAGADO" || tx.EstadoFila == "CONCILIADO")
            {
                TempData["ErrorMsg"] = "No se pueden cambiar los componentes de un Gate ya pagado.";
                return RedirectToPage("./ControlFinanciero");
            }
            if (componentes == null || !componentes.Any())
            {
                TempData["ErrorMsg"] = "Debe quedar al menos un componente de Gate aplicado.";
                return RedirectToPage("./ControlFinanciero");
            }

            var excluidos = (todos ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(c => !componentes.Contains(c)).ToList();
            string? valor = excluidos.Any() ? string.Join(",", excluidos) : null;

            // Costo (EGRESO) e ingreso (INGRESO) del mismo Gate comparten exenciones
            var hermanas = await _context.TransaccionesFinancieras
                .Where(t => t.IdOperacion == tx.IdOperacion && t.Concepto == tx.Concepto &&
                            (t.GrupoCobro == "Depósito" || t.GrupoCobro == "Deposito"))
                .ToListAsync();
            foreach (var h in hermanas) { h.ComponentesGateExcluidos = valor; h.FechaModificacion = DateTime.Now; h.UsuarioModificador = User.Identity?.Name ?? "Finanzas"; }
            await _context.SaveChangesAsync();

            await _sync.SincronizarAsync(tx.IdOperacion, User.Identity?.Name ?? "Finanzas");
            return RedirectToPage("./ControlFinanciero");
        }
        public async Task OnGetAsync()
        {
            if (HttpContext.RequestServices.GetService(typeof(AtilsonCargoSpa.Services.IndicadoresEconomicosService)) is AtilsonCargoSpa.Services.IndicadoresEconomicosService ind)
            {
                DolarInfo = await ind.ObtenerDolarAsync();
                UfInfo = await ind.ObtenerUfAsync();
                DolarHoy = DolarInfo.Valor;
                UfHoy = UfInfo.Valor;
            }
            // Se restauran los Include completos: la pestaña Egresos sigue en línea
            // (no vía AJAX/partial), así que necesita las mismas navegaciones de
            // siempre para resolver el nombre del proveedor por categoría.
            Operaciones = await _context.Operaciones
    .AsNoTracking()
    .AsSplitQuery()
    .Include(o => o.IdClienteNavigation)
    .Include(o => o.IdNavieraNavigation)
    .Include(o => o.OperacionesDocumentales)
    .ThenInclude(d => d.IdAgenciaAduanaNavigation)
    .Include(o => o.OperacionesTerrestres)
    .Include(o => o.OperacionesAlmacenamientos)
    .ThenInclude(a => a.IdProveedorNavigation)
    .Include(o => o.Unidadestecnicas)
    .Include(o => o.TransaccionesFinancieras)
    .ThenInclude(t => t.IdProveedorNavigation)
    .OrderByDescending(o => o.Id)
    .ToListAsync();

            // Primero lo que Operaciones acaba de traspasar (NUEVO) y lo reabierto.
            Operaciones = Operaciones
                .OrderBy(o => TraspasoFinanzas.PrioridadFinanzas(o))
                .ThenByDescending(o => o.Id)
                .ToList();
            CambiosTraspaso = await new TraspasoFinanzasService(_context).CambiosUltimaFinalizacionAsync(
                Operaciones.Where(o => o.VecesFinalizadaTarea > 1).Select(o => o.Id));
            RespaldosReapertura = await new TraspasoFinanzasService(_context).RespaldosUltimaReaperturaAsync(
                Operaciones.Where(TraspasoFinanzas.EstaReabierta).Select(o => o.Id));

            NombresAgencias = await _context.AgenciasAduanas.AsNoTracking()
                .Where(a => a.NombreAgencia != null)
                .ToDictionaryAsync(a => a.Id, a => a.NombreAgencia!);

            ComponentesGate = await _context.TarifasGate
.AsNoTracking()
.Where(t => t.EsActiva && t.Componente != null && t.IdNaviera != null)
.ToListAsync();

            TarifasGratuitasPorCliente = await _context.TarifasClientes
    .AsNoTracking()
    .Where(t => t.EsActiva && t.EsServicioGratuito && t.IdCliente != null)
    .GroupBy(t => t.IdCliente!.Value)
    .ToDictionaryAsync(g => g.Key, g => g.Select(t => t.Concepto!).ToList());
        }
        public async Task<IActionResult> OnGetEgresosPartialAsync()
        {
            var operacionesEgresos = await _context.Operaciones
    .AsNoTracking()
    .AsSplitQuery()
    .Where(o => o.TransaccionesFinancieras.Any(t => t.TipoMovimiento == "EGRESO"))
    .Include(o => o.IdClienteNavigation)
    .Include(o => o.IdNavieraNavigation)
    .Include(o => o.OperacionesDocumentales)
    .ThenInclude(d => d.IdAgenciaAduanaNavigation)
    .Include(o => o.OperacionesTerrestres)
    .Include(o => o.OperacionesAlmacenamientos)
    .ThenInclude(a => a.IdProveedorNavigation)
    .Include(o => o.TransaccionesFinancieras)
    .ThenInclude(t => t.IdProveedorNavigation)
    .OrderByDescending(o => o.Id)
    .ToListAsync();

            return Partial("_EgresosPartial", operacionesEgresos);
        }

        public async Task<IActionResult> OnPostGuardarVerificacionAgaAsync(int transaccionId, string accion, decimal? valorAga)
        {
            var tx = await _context.TransaccionesFinancieras.FindAsync(transaccionId);
            if (tx == null) return RedirectToPage("./ControlFinanciero");

            if (tx.EstadoFila == "PAGADO" || tx.EstadoFila == "CONCILIADO")
            {
                TempData["ErrorMsg"] = "No se puede modificar la verificación de un concepto ya pagado.";
                return RedirectToPage("./ControlFinanciero");
            }

            decimal original = ValorCompararAga(tx);

            switch (accion)
            {
                case "confirmar":
                    tx.ConfirmadoAga = true;
                    tx.ValorAga = original;
                    tx.AceptaDiferenciaPago = false;
                    tx.NotaDiferenciaAga = null;
                    break;

                case "desconfirmar":
                    tx.ConfirmadoAga = false;
                    tx.ValorAga = null;
                    tx.AceptaDiferenciaPago = false;
                    tx.NotaDiferenciaAga = null;
                    break;

                case "discrepancia":
                    if (!valorAga.HasValue || valorAga.Value <= 0)
                    {
                        TempData["ErrorMsg"] = "Ingresa el valor cobrado en la factura AGA.";
                        return RedirectToPage("./ControlFinanciero");
                    }
                    bool cambio = tx.ValorAga != valorAga.Value;
                    tx.ConfirmadoAga = true;
                    tx.ValorAga = valorAga.Value;
                    // Si el valor cambió (o ahora coincide), la justificación anterior deja de valer
                    if (cambio || Math.Abs(valorAga.Value - original) <= TolAga(tx))
                    {
                        tx.AceptaDiferenciaPago = false;
                        tx.NotaDiferenciaAga = null;
                    }
                    break;

                default:
                    return RedirectToPage("./ControlFinanciero");
            }

            tx.FechaModificacion = DateTime.Now;
            tx.UsuarioModificador = User.Identity?.Name ?? "Finanzas";
            await _context.SaveChangesAsync();
            return RedirectToPage("./ControlFinanciero");
        }

        // "¿Por qué no hay tarifa?" en un flete terrestre sin valor: corre el mismo cruce que
        // FinanzasSync y devuelve el detalle (transportista, tramo y tarifas que no calzan).
        public async Task<IActionResult> OnGetDiagnosticoTransporteAsync(int idOperacion, int? idUnidad)
        {
            var op = await _context.Operaciones.AsNoTracking()
                .Include(o => o.OperacionesTerrestres)
                .FirstOrDefaultAsync(o => o.Id == idOperacion);
            if (op == null) return new JsonResult(new { ok = false, lineas = new[] { "No se encontró la operación." } });
            var terr = op.OperacionesTerrestres.Where(t => t.IdUnidadTecnica == idUnidad).OrderBy(t => t.Id).FirstOrDefault();
            if (terr == null)
                return new JsonResult(new { ok = false, lineas = new[] { "Este contenedor no tiene datos de transporte guardados (pestaña Transporte del Detalle)." } });
            var res = await AtilsonCargoSpa.Helpers.TarifaTransporteHelper.BuscarAsync(_context, terr, op.IdTipoCarga,
                terr.DepositoRetiro, terr.PlantaCarga, terr.PuertoEntrega, DateTime.Now);
            var lineas = new List<string>();
            if (res.Tarifa != null)
            {
                lineas.Add($"Sí hay tarifa: {res.NombreProveedor}, {res.Tarifa.IdCiudadOrigenNavigation?.Nombre} → {res.Tarifa.IdCiudadPlantaNavigation?.Nombre} → {res.Tarifa.IdCiudadDestinoNavigation?.Nombre}, $ {res.Tarifa.ValorNeto:N0}.");
                lineas.Add("Usa \"Aplicar tarifa\" para recalcular este booking en Finanzas.");
            }
            lineas.AddRange(res.Diagnostico);
            return new JsonResult(new { ok = res.Tarifa != null, lineas });
        }

        // Recalcula las filas financieras de un booking (botón "Aplicar tarifa" del diagnóstico).
        public async Task<IActionResult> OnPostRecalcularOperacionAsync(int idOperacion)
        {
            await _sync.SincronizarAsync(idOperacion, User.Identity?.Name ?? "Finanzas");
            return new JsonResult(new { success = true });
        }

        // Finanzas abrió (desplegó) el booking: deja de verse como NUEVO / reabierta sin ver.
        public async Task<IActionResult> OnPostMarcarVistoTraspasoAsync(int id)
        {
            bool ok = await new TraspasoFinanzasService(_context).MarcarVistoAsync(id, User.Identity?.Name);
            return new JsonResult(new { success = ok });
        }

        public async Task<IActionResult> OnPostGuardarNumeroDocAsync(int transaccionId, string numeroDoc)
        {
            if (transaccionId > 0 && !string.IsNullOrWhiteSpace(numeroDoc))
            {
                var transaccion = await _context.TransaccionesFinancieras.FindAsync(transaccionId);
                if (transaccion != null)
                {
                    transaccion.NumeroDocumento = numeroDoc.Trim().ToUpper();
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage("./ControlFinanciero");
        }


        public async Task<IActionResult> OnPostEmitirNotaCobroAsync(int operacionId, List<int> idsExentos)
        {
            bool tienePendientes = await _context.TransaccionesFinancieras
                .AnyAsync(t => t.IdOperacion == operacionId && t.TipoMovimiento == "INGRESO" && t.MontoNeto == 0m && !t.TarifaManual && t.EstadoFila != "NO FACTURABLE" && (t.JustificacionManual == null || !t.JustificacionManual.StartsWith("Sin costo")));

            bool tieneIncompletos = await _context.TransaccionesFinancieras
                .AnyAsync(t => t.IdOperacion == operacionId && t.TipoMovimiento == "INGRESO" &&
                               string.IsNullOrWhiteSpace(t.NumeroDocumento) &&
                               (t.Concepto.Contains("Origen") || t.Concepto.Contains("Fitosanitario") || t.Concepto.Contains("Sanitario") || t.Concepto.Contains("Captura")));

            if (tienePendientes || tieneIncompletos)
            {
                TempData["ErrorMsg"] = "Bloqueo de seguridad: Existen montos sin asignar o certificados INCOMPLETOS. Operaciones debe ingresar el Número de Documento.";
                return RedirectToPage("./ControlFinanciero");
            }

            if (idsExentos == null || !idsExentos.Any()) return RedirectToPage("./ControlFinanciero");

            var ultimasNc = await _context.TransaccionesFinancieras
                .Where(t => t.NumeroDocumento != null && t.NumeroDocumento.StartsWith("NC-"))
                .Select(t => t.NumeroDocumento)
                .ToListAsync();

            int maxCorrelativo = 3500;
            foreach (var nc in ultimasNc)
            {
                if (int.TryParse(nc.Replace("NC-", ""), out int num))
                {
                    if (num > maxCorrelativo) maxCorrelativo = num;
                }
            }

            string nuevoFolio = $"NC-{maxCorrelativo + 1}";

            var transacciones = await _context.TransaccionesFinancieras
                .Where(t => idsExentos.Contains(t.Id) && t.IdOperacion == operacionId)
                .ToListAsync();

            foreach (var tx in transacciones) tx.NumeroDocumento = nuevoFolio;
            await _context.SaveChangesAsync();

            return RedirectToPage("/Finanzas/NotaCobro", new { operacionId = operacionId, numeroDoc = nuevoFolio });
        }

        // --- BOTÓN 1: SUBIR FACTURA PROVEEDOR (PDF) ---
        public async Task<IActionResult> OnPostSubirFacturaAsync(int transaccionId, IFormFile archivoFactura)
        {
            if (archivoFactura != null && archivoFactura.Length > 0)
            {
                var transaccion = await _context.TransaccionesFinancieras
                    .Include(t => t.IdOperacionNavigation)
                    .FirstOrDefaultAsync(t => t.Id == transaccionId);

                if (transaccion != null)
                {
                    string safeBookingName = string.Join("_", (transaccion.IdOperacionNavigation?.NumeroBooking ?? "SIN-BOOKING").Split(Path.GetInvalidFileNameChars()));
                    string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "facturas", safeBookingName);

                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    string uniqueFileName = $"FACT_{Guid.NewGuid().ToString().Substring(0, 8)}_{archivoFactura.FileName}";
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await archivoFactura.CopyToAsync(fileStream);
                    }

                    // Se guarda el archivo visual pero NO cambia el estado a PAGADO
                    transaccion.RutaFactura = $"/uploads/facturas/{safeBookingName}/{uniqueFileName}";
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage("./ControlFinanciero");
        }

        public async Task<IActionResult> OnPostGuardarAceptaDiferenciaAsync(int transaccionId, bool aceptaDiferencia, string? notaDiferenciaAga)
        {
            if (transaccionId > 0)
            {
                if (aceptaDiferencia && string.IsNullOrWhiteSpace(notaDiferenciaAga))
                {
                    TempData["ErrorMsg"] = "Debes explicar el motivo de la diferencia antes de confirmarla.";
                    return RedirectToPage("./ControlFinanciero");
                }

                var tx = await _context.TransaccionesFinancieras.FindAsync(transaccionId);
                if (tx != null)
                {
                    tx.AceptaDiferenciaPago = aceptaDiferencia;
                    tx.NotaDiferenciaAga = aceptaDiferencia ? notaDiferenciaAga!.Trim() : null;
                    tx.FechaModificacion = DateTime.Now;
                    tx.UsuarioModificador = User.Identity?.Name ?? "Finanzas";
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage("./ControlFinanciero");
        }

        // --- BOTÓN 2: SUBIR COMPROBANTE DE PAGO (TRANSFERENCIA) ---
        public async Task<IActionResult> OnPostSubirComprobanteAsync(int transaccionId, IFormFile archivoRespaldo)
        {
            if (archivoRespaldo != null && archivoRespaldo.Length > 0)
            {
                var transaccion = await _context.TransaccionesFinancieras
                    .Include(t => t.IdOperacionNavigation)
                    .FirstOrDefaultAsync(t => t.Id == transaccionId);

                if (transaccion != null)
                {
                    // CANDADO: en Egresos, todo concepto con monto exige verificar la factura AGA
                    // (confirmar el valor o registrar la discrepancia justificada) antes de pagar.
                    if (transaccion.TipoMovimiento == "EGRESO" && transaccion.MontoNeto > 0m)
                    {
                        if (!(transaccion.ConfirmadoAga && transaccion.ValorAga.HasValue))
                        {
                            TempData["ErrorMsg"] = "No se puede confirmar el pago: falta verificar el valor de la factura AGA (confirmar o ingresar el Valor Actualizado IVA Incluido).";
                            return RedirectToPage("./ControlFinanciero");
                        }

                        decimal original = ValorCompararAga(transaccion);
                        bool coincide = Math.Abs(transaccion.ValorAga.Value - original) <= TolAga(transaccion);
                        if (!coincide && (!transaccion.AceptaDiferenciaPago || string.IsNullOrWhiteSpace(transaccion.NotaDiferenciaAga)))
                        {
                            TempData["ErrorMsg"] = "Existe una discrepancia con la factura AGA. Registra el motivo y marca 'Consciente pago diferencia' para continuar.";
                            return RedirectToPage("./ControlFinanciero");
                        }
                    }

                    string safeBookingName = string.Join("_", (transaccion.IdOperacionNavigation?.NumeroBooking ?? "SIN-BOOKING").Split(Path.GetInvalidFileNameChars()));
                    string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "comprobantes", safeBookingName);

                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    string uniqueFileName = $"PAGO_{Guid.NewGuid().ToString().Substring(0, 8)}_{archivoRespaldo.FileName}";
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await archivoRespaldo.CopyToAsync(fileStream);
                    }

                    // El escudo: Sube el comprobante de transferencia y GATILLA el estado a PAGADO
                    transaccion.EstadoFila = "PAGADO";
                    transaccion.RutaComprobante = $"/uploads/comprobantes/{safeBookingName}/{uniqueFileName}";

                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage("./ControlFinanciero");
        }
    }
}