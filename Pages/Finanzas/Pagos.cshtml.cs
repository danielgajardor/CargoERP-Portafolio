using AtilsonCargoSpa.Models;
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
    public class PagosModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly IWebHostEnvironment _env;

        public PagosModel(AtilsonContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [BindProperty(SupportsGet = true)] public string? Booking { get; set; }
        [BindProperty(SupportsGet = true)] public int? ClienteId { get; set; }
        [BindProperty(SupportsGet = true)] public int? NavieraId { get; set; }
        [BindProperty(SupportsGet = true)] public string? Servicio { get; set; }
        [BindProperty(SupportsGet = true)] public string? Proveedor { get; set; }
        [BindProperty(SupportsGet = true)] public string? Estado { get; set; }
        [BindProperty(SupportsGet = true)] public string? Concepto { get; set; }
        [BindProperty(SupportsGet = true)] public string? Movimiento { get; set; }
        [BindProperty(SupportsGet = true)] public string? Nave { get; set; }
        public List<string> NavesLista { get; set; } = new();
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
        public const int TamanoPagina = 20;
        public int TotalBookings { get; set; }
        public int TotalPaginas { get; set; }

        public List<TransaccionesFinanciera> Pagos { get; set; } = new();
        public List<Cliente> Clientes { get; set; } = new();
        public List<Naviera> Navieras { get; set; } = new();
        public List<string> Proveedores { get; set; } = new();
        public List<string> Servicios { get; set; } = new();
        // 13-nov: conceptos existentes (sin el sufijo del contenedor) para la lista del filtro.
        public List<string> ConceptosLista { get; set; } = new();

        public static string ConceptoBase(string? concepto) =>
            System.Text.RegularExpressions.Regex.Replace(concepto ?? "", @"\s*\(U\d+\)", "").Trim();

        // Posición del contenedor de una línea "(Uxx)" (1 = principal) y su etiqueta "C2 · ABCD1234567".
        public static int PosicionContenedor(TransaccionesFinanciera tx)
        {
            int? idU = UnidadDeConcepto(tx.Concepto);
            if (idU == null) return 1;
            var unidades = (tx.IdOperacionNavigation?.Unidadestecnicas ?? new List<Unidadestecnica>()).OrderBy(u => u.Id).ToList();
            int idx = unidades.FindIndex(u => u.Id == idU);
            return idx < 0 ? 999 : idx + 2;
        }
        public static string EtiquetaContenedor(TransaccionesFinanciera tx)
        {
            int pos = PosicionContenedor(tx);
            string? num = pos == 1 ? tx.IdOperacionNavigation?.NumeroContenedor
                : (tx.IdOperacionNavigation?.Unidadestecnicas ?? new List<Unidadestecnica>()).OrderBy(u => u.Id).Skip(pos - 2).FirstOrDefault()?.NroContenedor;
            string c = pos == 999 ? "C?" : "C" + pos;
            return string.IsNullOrWhiteSpace(num) ? c : $"{c} · {num!.Trim()}";
        }
        // Modalidad de cobro de la agencia al cliente (Operaciones): por booking (GRUPAL) o por contenedor.
        public static bool CobroPorBooking(Operacione? op) =>
            string.Equals(op?.ModalidadCobroAgencia, "GRUPAL", StringComparison.OrdinalIgnoreCase);
        public HashSet<string> BookingsCompletos { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool BookingCompleto(int idOperacion, string movimiento) =>
            BookingsCompletos.Contains($"{idOperacion}:{movimiento}");

        public static string GrupoVisible(string? grupo)
        {
            string g = (grupo ?? "").Trim();
            if (g.Equals("Depósito", StringComparison.OrdinalIgnoreCase) ||
                g.Equals("Deposito", StringComparison.OrdinalIgnoreCase) ||
                g.Equals("Gate-Out/In", StringComparison.OrdinalIgnoreCase)) return "Gate-Out/In";
            if (g.Equals("Terrestre", StringComparison.OrdinalIgnoreCase) ||
                g.Equals("Transporte", StringComparison.OrdinalIgnoreCase)) return "Transporte";
            if (g.Equals("Aduana", StringComparison.OrdinalIgnoreCase) ||
                g.Equals("Documental", StringComparison.OrdinalIgnoreCase)) return "Documental";
            if (g.Equals("Maritimo", StringComparison.OrdinalIgnoreCase) ||
                g.Equals("Marítimo", StringComparison.OrdinalIgnoreCase)) return "Marítimo";
            if (g.Equals("Bodega", StringComparison.OrdinalIgnoreCase) ||
                g.Equals("Patio", StringComparison.OrdinalIgnoreCase) ||
                g.Equals("Almacenamiento", StringComparison.OrdinalIgnoreCase)) return "Almacenamiento";
            return string.IsNullOrWhiteSpace(g) ? "Otros" : g;
        }

        public static int OrdenServicio(string? grupo)
        {
            string g = GrupoVisible(grupo);
            return g switch
            {
                "Marítimo" => 1,
                "Transporte" => 2,
                "Documental" => 3,
                "Gate-Out/In" => 4,
                "Almacenamiento" => 5,
                _ => 99
            };
        }

        public async Task OnGetAsync()
        {
            Clientes = await _context.Clientes.AsNoTracking().OrderBy(x => x.RazonSocial).ToListAsync();
            NavesLista = await AtilsonCargoSpa.Helpers.FiltroNaveHelper.OpcionesAsync(_context);
            Navieras = await _context.Navieras.AsNoTracking().Where(x => x.Activo == 1).OrderBy(x => x.NombreNaviera).ToListAsync();
            var proveedoresMaestro = await _context.Proveedores.AsNoTracking()
                .Where(x => x.Activo == 1)
                .Select(x => x.NombreProveedor)
                .ToListAsync();
            var transportistas = await _context.OperacionesTerrestres.AsNoTracking()
                .Where(x => x.IdOperacionNavigation != null && !x.IdOperacionNavigation.IsDeleted &&
                            x.EmpresaTransporte != null && x.EmpresaTransporte != "")
                .Select(x => x.EmpresaTransporte!)
                .Distinct()
                .ToListAsync();
            var agencias = await _context.AgenciasAduanas.AsNoTracking()
                .Where(x => x.NombreAgencia != null && x.NombreAgencia != "")
                .Select(x => x.NombreAgencia!)
                .ToListAsync();
            var clientesContraparte = Clientes
                .Where(x => !string.IsNullOrWhiteSpace(x.RazonSocial))
                .Select(x => x.RazonSocial!)
                .ToList();
            var contrapartesHistoricas = await _context.TransaccionesFinancieras.AsNoTracking()
                .Where(x => !x.IdOperacionNavigation.IsDeleted &&
                            x.ContraparteHistorica != null && x.ContraparteHistorica != "")
                .Select(x => x.ContraparteHistorica!)
                .Distinct()
                .ToListAsync();
            Proveedores = proveedoresMaestro.Concat(transportistas).Concat(agencias).Concat(clientesContraparte).Concat(contrapartesHistoricas)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
            Servicios = (await _context.TransaccionesFinancieras.AsNoTracking()
                .Where(x => !x.IdOperacionNavigation.IsDeleted &&
                            (x.TipoMovimiento == "EGRESO" || x.TipoMovimiento == "INGRESO") && x.GrupoCobro != null)
                .Select(x => x.GrupoCobro).Distinct().ToListAsync())
                .Select(GrupoVisible)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(OrdenServicio).ThenBy(x => x)
                .ToList();

            var q = _context.TransaccionesFinancieras
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.IdClienteNavigation)
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.IdNavieraNavigation)
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.OperacionesTerrestres)
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.OperacionesDocumentales).ThenInclude(d => d.IdAgenciaAduanaNavigation)
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.OperacionesAlmacenamientos).ThenInclude(a => a.IdProveedorNavigation)
                .Include(x => x.IdOperacionNavigation).ThenInclude(o => o.Unidadestecnicas)
                .Include(x => x.IdProveedorNavigation)
                .Where(x => (x.TipoMovimiento == "EGRESO" || x.TipoMovimiento == "INGRESO") && !x.IdOperacionNavigation.IsDeleted);

            ConceptosLista = (await _context.TransaccionesFinancieras.AsNoTracking()
                    .Where(x => !x.IdOperacionNavigation.IsDeleted && (x.TipoMovimiento == "EGRESO" || x.TipoMovimiento == "INGRESO") && x.Concepto != null)
                    .Select(x => x.Concepto).Distinct().ToListAsync())
                .Select(ConceptoBase).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

            if (!string.IsNullOrWhiteSpace(Booking))
            {
                string b = Booking.Trim();
                q = q.Where(x => x.IdOperacionNavigation.NumeroBooking != null && x.IdOperacionNavigation.NumeroBooking.Contains(b));
            }
            if (ClienteId.HasValue) q = q.Where(x => x.IdOperacionNavigation.IdCliente == ClienteId.Value);
            if (NavieraId.HasValue) q = q.Where(x => x.IdOperacionNavigation.IdNaviera == NavieraId.Value);
            string? nave = AtilsonCargoSpa.Helpers.FiltroNaveHelper.Normalizar(Nave);
            if (nave != null) q = q.Where(x => x.IdOperacionNavigation.Nave != null && x.IdOperacionNavigation.Nave.Contains(nave));
            if (!string.IsNullOrWhiteSpace(Servicio))
            {
                Servicio = GrupoVisible(Servicio);
                if (Servicio == "Marítimo")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Marít") || x.GrupoCobro.Contains("Marit")));
                else if (Servicio == "Transporte")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Terrest") || x.GrupoCobro.Contains("Transp")));
                else if (Servicio == "Documental")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Document") || x.GrupoCobro.Contains("Aduana")));
                else if (Servicio == "Gate-Out/In")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Gate") || x.GrupoCobro.Contains("Depósito") || x.GrupoCobro.Contains("Deposito")));
                else if (Servicio == "Almacenamiento")
                    q = q.Where(x => x.GrupoCobro != null && (x.GrupoCobro.Contains("Almacen") || x.GrupoCobro.Contains("Bodega") || x.GrupoCobro.Contains("Patio")));
                else
                    q = q.Where(x => x.GrupoCobro == Servicio);
            }
            if (!string.IsNullOrWhiteSpace(Concepto))
            {
                string concepto = Concepto.Trim();
                q = q.Where(x => x.Concepto != null && x.Concepto.Contains(concepto));
            }
            if (!string.IsNullOrWhiteSpace(Proveedor))
            {
                string proveedor = Proveedor.Trim();
                q = q.Where(x =>
                    (x.TipoMovimiento == "INGRESO" &&
                        x.IdOperacionNavigation.IdClienteNavigation != null &&
                        x.IdOperacionNavigation.IdClienteNavigation.RazonSocial != null &&
                        x.IdOperacionNavigation.IdClienteNavigation.RazonSocial.Contains(proveedor)) ||
                    (x.TipoMovimiento == "EGRESO" && (
                        (x.IdProveedorNavigation != null && x.IdProveedorNavigation.NombreProveedor.Contains(proveedor)) ||
                        (x.ContraparteHistorica != null && x.ContraparteHistorica.Contains(proveedor)) ||
                        (x.IdOperacionNavigation.IdNavieraNavigation != null && x.IdOperacionNavigation.IdNavieraNavigation.NombreNaviera.Contains(proveedor)) ||
                        x.IdOperacionNavigation.OperacionesTerrestres.Any(t => t.EmpresaTransporte != null && t.EmpresaTransporte.Contains(proveedor)) ||
                        x.IdOperacionNavigation.OperacionesDocumentales.Any(d =>
                            (d.IdAgenciaAduanaNavigation != null && d.IdAgenciaAduanaNavigation.NombreAgencia.Contains(proveedor)) ||
                            (d.AgenciaAduana != null && d.AgenciaAduana.Contains(proveedor))) ||
                        x.IdOperacionNavigation.OperacionesAlmacenamientos.Any(a =>
                            a.IdProveedorNavigation != null && a.IdProveedorNavigation.NombreProveedor.Contains(proveedor)))));
            }
            if (Estado == "PAGADO") q = q.Where(x => x.EstadoFila == "PAGADO" || x.EstadoFila == "CONCILIADO");
            else if (Estado == "PENDIENTE") q = q.Where(x => x.EstadoFila != "PAGADO" && x.EstadoFila != "CONCILIADO");
            else if (Estado == "LISTOS")
            {
                // 13-nov: listos para pagar = con N° y documento subido y, en egresos, el valor
                // confirmado (las mismas reglas que habilitan el botón de pago).
                var idsFiltrados = q.Select(y => y.Id);
                var candidatos = await _context.TransaccionesFinancieras.AsNoTracking()
                    .Include(x => x.IdOperacionNavigation)
                    .Where(x => idsFiltrados.Contains(x.Id) && x.EstadoFila != "PAGADO" && x.EstadoFila != "CONCILIADO"
                        && x.NumeroDocumento != null && x.NumeroDocumento != "" && x.RutaFactura != null && x.RutaFactura != "")
                    .ToListAsync();
                var idsListos = candidatos.Where(t => MotivoBloqueoPago(t) == null).Select(t => t.Id).ToList();
                q = q.Where(x => idsListos.Contains(x.Id));
            }

            Movimiento = string.Equals(Movimiento, "INGRESO", StringComparison.OrdinalIgnoreCase) ? "INGRESO" : "EGRESO";

            var idsQuery = q.Select(x => x.IdOperacion).Distinct();
            TotalBookings = await idsQuery.CountAsync();
            TotalPaginas = Math.Max(1, (int)Math.Ceiling(TotalBookings / (double)TamanoPagina));
            Pagina = Math.Clamp(Pagina, 1, TotalPaginas);

            var idsVisibles = await idsQuery
                .OrderByDescending(id => id)
                .Skip((Pagina - 1) * TamanoPagina)
                .Take(TamanoPagina)
                .ToListAsync();

            q = q.Where(x => idsVisibles.Contains(x.IdOperacion));

            Pagos = await q.OrderBy(x => x.EstadoFila == "PAGADO" || x.EstadoFila == "CONCILIADO")
                .ThenByDescending(x => x.IdOperacion)
                .ThenBy(x => x.GrupoCobro)
                .ThenBy(x => x.Concepto)
                .ToListAsync();

            
            if (idsVisibles.Count > 0)
            {
                var cierre = await _context.TransaccionesFinancieras.AsNoTracking()
                    .Where(t => idsVisibles.Contains(t.IdOperacion) &&
                                (t.TipoMovimiento == "EGRESO" || t.TipoMovimiento == "INGRESO"))
                    .Select(t => new { t.IdOperacion, t.TipoMovimiento, t.EstadoFila, t.NumeroDocumento, t.RutaFactura, t.IdOperacionNavigation.EsHistorica })
                    .ToListAsync();

                BookingsCompletos = cierre
                    .GroupBy(t => new { t.IdOperacion, t.TipoMovimiento })
                    .Where(g => g.Any() && g.All(t =>
                        t.EstadoFila == "NO FACTURABLE" ||
                        ((t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO") &&
                         t.NumeroDocumento != null && t.NumeroDocumento != "" &&
                         (t.RutaFactura != null && t.RutaFactura != ""))))
                    .Select(g => $"{g.Key.IdOperacion}:{g.Key.TipoMovimiento}")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static int? UnidadDeConcepto(string? concepto)
        {
            var m = System.Text.RegularExpressions.Regex.Match(concepto ?? "", @"\(U(\d+)\)");
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }

        public string ResolverContraparte(TransaccionesFinanciera tx)
        {
            var op = tx.IdOperacionNavigation;
            if (tx.TipoMovimiento == "INGRESO")
                return op.IdClienteNavigation?.RazonSocial ?? "Cliente";

            string grupo = (tx.GrupoCobro ?? "").ToUpperInvariant();
            int? unidad = UnidadDeConcepto(tx.Concepto);
            string? nombre = tx.IdProveedorNavigation?.NombreProveedor ?? tx.ContraparteHistorica;

            if (grupo.Contains("DOCUMENTAL") || grupo.Contains("ADUANA"))
            {
                var docs = op.OperacionesDocumentales ?? new List<OperacionesDocumentale>();
                var doc = docs.FirstOrDefault(x => x.IdUnidadTecnica == unidad)
                       ?? docs.FirstOrDefault(x => x.IdUnidadTecnica == null)
                       ?? docs.FirstOrDefault();
                nombre = doc?.IdAgenciaAduanaNavigation?.NombreAgencia ?? doc?.AgenciaAduana ?? nombre;
            }
            else if (grupo.Contains("GATE") || grupo.Contains("DEPOS"))
            {
                if (op.IdNavieraNavigation?.AplicaTarifaGatePlana == true)
                    nombre = op.IdNavieraNavigation.NombreNaviera;
                else
                    nombre = op.OperacionesTerrestres?.FirstOrDefault(x => x.IdUnidadTecnica == unidad)?.DepositoRetiro
                          ?? op.OperacionesTerrestres?.FirstOrDefault(x => x.IdUnidadTecnica == null)?.DepositoRetiro
                          ?? nombre;
            }
            else if (grupo.Contains("TERREST") || grupo.Contains("TRANSP"))
            {
                nombre = op.OperacionesTerrestres?.FirstOrDefault(x => x.IdUnidadTecnica == unidad)?.EmpresaTransporte
                      ?? op.OperacionesTerrestres?.FirstOrDefault(x => x.IdUnidadTecnica == null)?.EmpresaTransporte
                      ?? nombre;
            }
            else if (grupo.Contains("MARIT"))
            {
                nombre = op.IdNavieraNavigation?.NombreNaviera ?? nombre;
            }
            else if (grupo.Contains("ALMAC") || grupo.Contains("BODEGA") || grupo.Contains("PATIO"))
            {
                nombre = op.OperacionesAlmacenamientos?.FirstOrDefault()?.IdProveedorNavigation?.NombreProveedor ?? nombre;
            }

            return string.IsNullOrWhiteSpace(nombre) ? "Por asignar" : nombre;
        }

        private static decimal ValorCompararAga(TransaccionesFinanciera tx)
            => ((tx.GrupoCobro == "Marítimo" && ControlFinancieroModel.EsMaritimoExento(tx.Concepto, tx.IvaExento))
                || (tx.GrupoCobro != "Marítimo" && ControlFinancieroModel.EsMaritimoUsdSinIva(tx.Concepto)))
                ? tx.MontoNeto
                : Math.Round(tx.MontoNeto * 1.19m, tx.Moneda == "CLP" ? 0 : 2);

        private static decimal TolAga(TransaccionesFinanciera tx) => tx.Moneda == "CLP" ? 1m : 0.01m;

        public string? MotivoBloqueoPago(TransaccionesFinanciera tx)
        {
            if (tx.EstadoFila == "PAGADO" || tx.EstadoFila == "CONCILIADO")
                return null;

            if (string.IsNullOrWhiteSpace(tx.NumeroDocumento))
                return "Falta guardar el N° de factura / documento.";

            if (string.IsNullOrWhiteSpace(tx.RutaFactura))
                return "Falta subir la factura / nota de cobro / documento.";

            // La confirmación del valor pertenece al flujo operativo nuevo. Para una deuda histórica
            // pre-ERP no existe ese hito; basta con documento + respaldo para poder pagarla.
            if (tx.TipoMovimiento == "EGRESO" && tx.MontoNeto > 0m && tx.IdOperacionNavigation?.EsHistorica != true)
            {
                if (!(tx.ConfirmadoAga && tx.ValorAga.HasValue))
                    return "Falta confirmar el valor en Control Financiero.";

                decimal esperado = ValorCompararAga(tx);
                bool coincide = Math.Abs(tx.ValorAga!.Value - esperado) <= TolAga(tx);
                if (!coincide && (!tx.AceptaDiferenciaPago || string.IsNullOrWhiteSpace(tx.NotaDiferenciaAga)))
                    return "La diferencia de valor todavía no está justificada.";
            }

            return null;
        }

        public bool DocumentoListo(TransaccionesFinanciera tx) =>
            !string.IsNullOrWhiteSpace(tx.NumeroDocumento) &&
            !string.IsNullOrWhiteSpace(tx.RutaFactura);

        // =====================================================================================
        // Pago agrupado (13-nov-2026): se marcan varios conceptos (de uno o varios bookings, naves
        // o contenedores), se adjunta UN comprobante y todos quedan pagados con ese respaldo.
        // Cada concepto pasa las mismas validaciones que el pago individual; si alguno no puede
        // pagarse, no se registra ninguno.
        // =====================================================================================
        public async Task<IActionResult> OnPostRegistrarPagoMultipleAsync(List<int> ids, IFormFile? archivoPago)
        {
            object Volver(string? mov) => new { Booking, ClienteId, NavieraId, Servicio, Proveedor, Estado, Concepto, Pagina, Nave, Movimiento = mov ?? Movimiento };
            var lista = (ids ?? new List<int>()).Where(i => i > 0).Distinct().ToList();
            if (lista.Count == 0)
            {
                TempData["ErrorMsg"] = "No marcaste conceptos para pagar.";
                return RedirectToPage(Volver(null));
            }
            var txs = await _context.TransaccionesFinancieras
                .Include(x => x.IdOperacionNavigation)
                .Where(x => lista.Contains(x.Id) && (x.TipoMovimiento == "EGRESO" || x.TipoMovimiento == "INGRESO") && !x.IdOperacionNavigation.IsDeleted)
                .ToListAsync();
            if (txs.Count != lista.Count)
            {
                TempData["ErrorMsg"] = "Algunos de los conceptos marcados ya no existen. Actualiza la página e inténtalo de nuevo.";
                return RedirectToPage(Volver(null));
            }
            string mov = txs[0].TipoMovimiento;
            if (txs.Any(t => t.TipoMovimiento != mov))
            {
                TempData["ErrorMsg"] = "No se pueden mezclar egresos e ingresos en un mismo comprobante.";
                return RedirectToPage(Volver(null));
            }
            if (txs.Select(t => t.Moneda).Distinct().Count() > 1)
            {
                TempData["ErrorMsg"] = "Los conceptos marcados están en monedas distintas (CLP y USD): regístralos en comprobantes separados.";
                return RedirectToPage(Volver(mov));
            }
            var problemas = new List<string>();
            foreach (var t in txs)
            {
                string etiqueta = $"{t.IdOperacionNavigation?.NumeroBooking} · {t.Concepto}";
                if (t.EstadoFila == "PAGADO" || t.EstadoFila == "CONCILIADO") problemas.Add($"{etiqueta}: ya está pagado");
                else if (MotivoBloqueoPago(t) is string motivo) problemas.Add($"{etiqueta}: {motivo}");
            }
            if (problemas.Any())
            {
                TempData["ErrorMsg"] = "No se registró el pago. " + string.Join(" · ", problemas.Take(5)) + (problemas.Count > 5 ? $" (y {problemas.Count - 5} más)" : "");
                return RedirectToPage(Volver(mov));
            }
            if (archivoPago == null || archivoPago.Length == 0)
            {
                TempData["ErrorMsg"] = "Debes adjuntar el comprobante del pago / cobro.";
                return RedirectToPage(Volver(mov));
            }
            string ext = Path.GetExtension(archivoPago.FileName).ToLowerInvariant();
            if (!new[] { ".pdf", ".png", ".jpg", ".jpeg", ".webp" }.Contains(ext))
            {
                TempData["ErrorMsg"] = "Formato no permitido. Usa PDF, JPG, PNG o WEBP.";
                return RedirectToPage(Volver(mov));
            }

            // Un solo archivo para todo el lote (carpeta propia; cada concepto apunta a él).
            string folder = Path.Combine(_env.WebRootPath, "uploads", "comprobantes", "_lotes");
            Directory.CreateDirectory(folder);
            string nombre = $"PAGO_LOTE_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}{ext}";
            using (var fs = new FileStream(Path.Combine(folder, nombre), FileMode.Create))
                await archivoPago.CopyToAsync(fs);
            string ruta = $"/uploads/comprobantes/_lotes/{nombre}";

            string usuario = User.Identity?.Name ?? "Finanzas";
            foreach (var t in txs)
            {
                t.RutaComprobante = ruta;
                t.EstadoFila = "PAGADO";
                t.FechaModificacion = DateTime.Now;
                t.UsuarioModificador = usuario;
            }
            await _context.SaveChangesAsync();

            int bookings = txs.Select(t => t.IdOperacion).Distinct().Count();
            decimal total = txs.Sum(t => t.MontoNeto);
            string moneda = txs[0].Moneda;
            TempData["SuccessMsg"] = $"{(mov == "INGRESO" ? "Cobro" : "Pago")} registrado con un solo comprobante: {txs.Count} concepto(s) de {bookings} booking(s) · {moneda} {total.ToString(moneda == "CLP" ? "N0" : "N2")} neto.";
            return RedirectToPage(Volver(mov));
        }

        public async Task<IActionResult> OnPostRegistrarPagoAsync(int transaccionId, IFormFile archivoPago)
        {
            var tx = await _context.TransaccionesFinancieras
                .Include(x => x.IdOperacionNavigation)
                .FirstOrDefaultAsync(x => x.Id == transaccionId && (x.TipoMovimiento == "EGRESO" || x.TipoMovimiento == "INGRESO"));

            if (tx == null)
            {
                TempData["ErrorMsg"] = "No se encontró el movimiento seleccionado.";
                return RedirectToPage(new { Booking, ClienteId, NavieraId, Servicio, Proveedor, Estado, Concepto, Pagina, Nave, Movimiento });
            }
            if (tx.EstadoFila == "PAGADO" || tx.EstadoFila == "CONCILIADO")
            {
                TempData["ErrorMsg"] = "Este movimiento ya está pagado.";
                return RedirectToPage(new { Booking, ClienteId, NavieraId, Servicio, Proveedor, Estado, Concepto, Pagina, Nave, Movimiento = tx.TipoMovimiento });
            }

            string? bloqueoPago = MotivoBloqueoPago(tx);
            if (bloqueoPago != null)
            {
                TempData["ErrorMsg"] = "No se puede registrar el pago: " + bloqueoPago;
                return RedirectToPage(new { Booking, ClienteId, NavieraId, Servicio, Proveedor, Estado, Concepto, Pagina, Nave, Movimiento = tx.TipoMovimiento });
            }

            if (archivoPago == null || archivoPago.Length == 0)
            {
                TempData["ErrorMsg"] = "Debes adjuntar el comprobante del pago / cobro.";
                return RedirectToPage(new { Booking, ClienteId, NavieraId, Servicio, Proveedor, Estado, Concepto, Pagina, Nave, Movimiento = tx.TipoMovimiento });
            }

            string ext = Path.GetExtension(archivoPago.FileName).ToLowerInvariant();
            string[] permitidas = { ".pdf", ".png", ".jpg", ".jpeg", ".webp" };
            if (!permitidas.Contains(ext))
            {
                TempData["ErrorMsg"] = "Formato no permitido. Usa PDF, JPG, PNG o WEBP.";
                return RedirectToPage(new { Booking, ClienteId, NavieraId, Servicio, Proveedor, Estado, Concepto, Pagina, Nave, Movimiento = tx.TipoMovimiento });
            }

            string booking = tx.IdOperacionNavigation?.NumeroBooking ?? "SIN-BOOKING";
            string safeBooking = string.Concat(booking.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            string folder = Path.Combine(_env.WebRootPath, "uploads", "comprobantes", safeBooking);
            Directory.CreateDirectory(folder);

            string nombre = $"PAGO_{tx.Id}_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}{ext}";
            using (var fs = new FileStream(Path.Combine(folder, nombre), FileMode.Create))
                await archivoPago.CopyToAsync(fs);

            tx.RutaComprobante = $"/uploads/comprobantes/{safeBooking}/{nombre}";
            tx.EstadoFila = "PAGADO";
            tx.FechaModificacion = DateTime.Now;
            tx.UsuarioModificador = User.Identity?.Name ?? "Finanzas";
            await _context.SaveChangesAsync();

            TempData["SuccessMsg"] = tx.TipoMovimiento == "INGRESO"
                ? $"Cobro registrado: {booking} · {tx.Concepto}."
                : $"Pago registrado: {booking} · {tx.Concepto}.";
            return RedirectToPage(new { Booking, ClienteId, NavieraId, Servicio, Proveedor, Estado, Concepto, Pagina, Nave, Movimiento = tx.TipoMovimiento });
        }
    }
}
