using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using UglyToad.PdfPig;
using System.Text.RegularExpressions;
using AtilsonCargoSpa.Services;

namespace AtilsonCargoSpa.Pages.Operaciones
{
    public class CreateModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly FinanzasSyncService _finanzasSync;

        public CreateModel(AtilsonContext context, IWebHostEnvironment env, FinanzasSyncService finanzasSync)
        {
            _context = context;
            _env = env;
            _finanzasSync = finanzasSync;
        }

        // --- MAGIA ATILSON: LECTOR DE PDF GRATUITO V3 (EXTRACCIÓN COMPLETA) ---
        public async Task<IActionResult> OnPostProcesarPdfAsync(IFormFile archivoPdf)
        {
            if (archivoPdf == null || archivoPdf.Length == 0)
                return new JsonResult(new { success = false, message = "No se recibió ningún archivo." });

            try
            {
                string textoCompleto = "";

                using (var stream = archivoPdf.OpenReadStream())
                using (var pdf = PdfDocument.Open(stream))
                {
                    foreach (var page in pdf.GetPages())
                    {
                        textoCompleto += page.Text + " ";
                    }
                }

                // Limpiar espacios en blanco dobles para facilitar la lectura
                textoCompleto = Regex.Replace(textoCompleto, @"\s+", " ");

                // Variables a extraer
                string booking = ""; string nave = ""; string naviera = "";
                string pol = ""; string pod = ""; string etd = ""; string eta = "";
                string tipoCarga = "1"; string tipoContenedor = ""; string cantidadContenedores = "1";
                string cliente = ""; string commodity = "";

                // 1. Identificar la Naviera
                if (textoCompleto.Contains("Hapag-Lloyd", StringComparison.OrdinalIgnoreCase)) naviera = "Hapag-Lloyd";
                else if (textoCompleto.Contains("CMA CGM", StringComparison.OrdinalIgnoreCase)) naviera = "CMA CGM";
                else if (textoCompleto.Contains("MSC") || textoCompleto.Contains("MEDITERRANEAN SHIPPING", StringComparison.OrdinalIgnoreCase)) naviera = "MSC";
                else if (textoCompleto.Contains("MAERSK", StringComparison.OrdinalIgnoreCase)) naviera = "Maersk";
                else if (textoCompleto.Contains("EVERGREEN", StringComparison.OrdinalIgnoreCase)) naviera = "Evergreen";
                else if (textoCompleto.Contains("OCEAN NETWORK EXPRESS", StringComparison.OrdinalIgnoreCase) || textoCompleto.Contains(" ONE ")) naviera = "ONE";
                else if (textoCompleto.Contains("OOCL", StringComparison.OrdinalIgnoreCase)) naviera = "OOCL";
                else if (textoCompleto.Contains("AGUNSA", StringComparison.OrdinalIgnoreCase)) naviera = "AGUNSA";

                // 2. Número de Booking
                Match matchBooking = Regex.Match(textoCompleto, @"(?:Booking No|Booking Ref|Booking #|Our Reference|NUMERO DE RESERVA|Número de Booking|Booking Number|Turn-In Reference)[\s\.:#]+([A-Za-z0-9]{6,15})", RegexOptions.IgnoreCase);
                if (matchBooking.Success) booking = matchBooking.Groups[1].Value.Trim();

                // 3. Nombre de Nave
                Match matchNave = Regex.Match(textoCompleto, @"(?:Vessel\/Voyage|Nave-Viaje|Vessel|Trunk Vessel|VESSEL AND VOYAGE NUMBER)[\s\.:]+([A-Za-z0-9\s\-]+?)(?=\s+(?:POL|Port|From|Date|Origin|Voyage|Pto|PORT|202|POD|Transhipment))", RegexOptions.IgnoreCase);
                if (matchNave.Success) nave = matchNave.Groups[1].Value.Trim();

                // 4. Extraer Cliente (Shipper / Booked by)
                Match matchCliente = Regex.Match(textoCompleto, @"(?:SHIPPER|Booked by Party:|Booking Party|Attn:|Customer|FROM:)[\s\*:]+([A-Z\s\.\,]{5,40})(?=\s|$|RUT|Contact|Telephone)", RegexOptions.IgnoreCase);
                if (matchCliente.Success) cliente = matchCliente.Groups[1].Value.Trim();

                // Refuerzo para clientes si la naviera lo pone en un formato extraño
                if (string.IsNullOrEmpty(cliente) || cliente.Length < 4)
                {
                    if (textoCompleto.Contains("PROMAR", StringComparison.OrdinalIgnoreCase)) cliente = "PROMAR INVERSIONES";
                    else if (textoCompleto.Contains("ATILSON", StringComparison.OrdinalIgnoreCase)) cliente = "ATILSON";
                }

                // 5. Extraer Mercancía (Commodity)
                Match matchCommodity = Regex.Match(textoCompleto, @"(?:Commodity Description:|Commodity|Description)[\s\*:]+([A-Za-z0-9\s\,\-]{4,40})(?=\s|$|Temp|App|OOG)", RegexOptions.IgnoreCase);
                if (matchCommodity.Success) commodity = matchCommodity.Groups[1].Value.Trim();

                // Refuerzo de commodity basado en tus PDFs de muestra
                if (string.IsNullOrEmpty(commodity))
                {
                    if (textoCompleto.Contains("Kiwifruit", StringComparison.OrdinalIgnoreCase)) commodity = "Kiwifruit";
                    else if (textoCompleto.Contains("Molluscs", StringComparison.OrdinalIgnoreCase)) commodity = "Molluscs";
                    else if (textoCompleto.Contains("Nueces", StringComparison.OrdinalIgnoreCase) || textoCompleto.Contains("Walnuts", StringComparison.OrdinalIgnoreCase)) commodity = "Nueces";
                }

                // 6. Fechas (ETD y ETA) - Parseo a formato HTML5 (YYYY-MM-DD)
                Match matchEtd = Regex.Match(textoCompleto, @"(?:ETD|ETS|Departure|Sailing Date|Cargo Cut Off)[\s\.:]*(\d{1,2}[\-\/\.][A-Za-z]{3,}[\-\/\.]\d{2,4}|\d{1,2}[\-\/\.]\d{1,2}[\-\/\.]\d{2,4})", RegexOptions.IgnoreCase);
                if (matchEtd.Success)
                {
                    string dateStr = matchEtd.Groups[1].Value.Replace(".", "/").Replace("-", "/");
                    if (DateTime.TryParse(dateStr, out DateTime parsedEtd)) etd = parsedEtd.ToString("yyyy-MM-dd");
                }

                Match matchEta = Regex.Match(textoCompleto, @"(?:ETA|Arrival Date|Discharge Date ETA)[\s\.:]*(\d{1,2}[\-\/\.][A-Za-z]{3,}[\-\/\.]\d{2,4}|\d{1,2}[\-\/\.]\d{1,2}[\-\/\.]\d{2,4})", RegexOptions.IgnoreCase);
                if (matchEta.Success)
                {
                    string dateStr = matchEta.Groups[1].Value.Replace(".", "/").Replace("-", "/");
                    if (DateTime.TryParse(dateStr, out DateTime parsedEta)) eta = parsedEta.ToString("yyyy-MM-dd");
                }

                // 7. Puertos (POL y POD)
                Match matchPol = Regex.Match(textoCompleto, @"(?:Port of Loading|POL|Origin|From|Sailing|Pto\. Embarque)[\s\*:]+([A-Za-z\s]{4,20})(?=\s|,|-|CHILE|PERU)", RegexOptions.IgnoreCase);
                if (matchPol.Success) pol = matchPol.Groups[1].Value.Replace("CHILE", "").Trim();

                Match matchPod = Regex.Match(textoCompleto, @"(?:Port of Discharge|POD|Destination|To|Discharge)[\s\*:]+([A-Za-z\s]{4,20})(?=\s|,|-|SPAIN|MOROCCO|HONG KONG|INDIA)", RegexOptions.IgnoreCase);
                if (matchPod.Success) pod = matchPod.Groups[1].Value.Trim();

                // 8. Contenedores y Carga (Reefer vs Dry)
                if (textoCompleto.Contains("REEFER", StringComparison.OrdinalIgnoreCase) || textoCompleto.Contains(" RQ") || textoCompleto.Contains(" RF") || textoCompleto.Contains("Refrigerated"))
                {
                    tipoCarga = "2"; // REEFER
                    if (textoCompleto.Contains("40' HIGH CUBE", StringComparison.OrdinalIgnoreCase) || textoCompleto.Contains("40' Reefer", StringComparison.OrdinalIgnoreCase) || textoCompleto.Contains("40RQ") || textoCompleto.Contains("40 RH") || textoCompleto.Contains("40' Hi-Cube")) tipoContenedor = "40' REEFER";
                    else tipoContenedor = "20' REEFER";
                }
                else
                {
                    tipoCarga = "1"; // DRY
                    if (textoCompleto.Contains("40' HIGH CUBE", StringComparison.OrdinalIgnoreCase) || textoCompleto.Contains("40HC") || textoCompleto.Contains("40' HC")) tipoContenedor = "40' DRY HC";
                    else if (textoCompleto.Contains("40' DRY") || textoCompleto.Contains("40DV") || textoCompleto.Contains("40' DV")) tipoContenedor = "40' DRY STD";
                    else tipoContenedor = "20' DRY STD";
                }

                // Cantidad (Ej: "1 x 40", "2x20")
                Match matchCant = Regex.Match(textoCompleto, @"(\d+)\s*[xX]\s*(?:20|40|45)");
                if (matchCant.Success) cantidadContenedores = matchCant.Groups[1].Value;

                return new JsonResult(new
                {
                    success = true,
                    data = new
                    {
                        booking = booking,
                        naviera = naviera,
                        nave = nave,
                        cliente = cliente,
                        commodity = commodity,
                        etd = etd,
                        eta = eta,
                        pol = pol,
                        pod = pod,
                        tipoCarga = tipoCarga,
                        tipoContenedor = tipoContenedor,
                        cantidadContenedores = cantidadContenedores
                    }
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Error leyendo el PDF: " + ex.Message });
            }
        }


        // --- MAGIA ATILSON: Carga asíncrona de datos maestros ---
        private async Task CargarSelectListsAsync()
        {
            ViewData["IdCliente"] = new SelectList(await _context.Clientes.Where(c => c.Activo == 1).OrderBy(c => c.RazonSocial).ToListAsync(), "Id", "RazonSocial");
            ViewData["IdNaviera"] = new SelectList(await _context.Navieras.Where(n => n.Activo == 1).OrderBy(n => n.NombreNaviera).ToListAsync(), "Id", "NombreNaviera");

            var tipoMovimientos = new List<object>
    {
        new { Id = 1, Valor = "CY/CY (Contenedor Completo / FCL)" },
        new { Id = 3, Valor = "CFS/CY (Desconsolidado)" },
        new { Id = 4, Valor = "CFS/CFS (LCL)" }
    };
            ViewData["IdTipoMovimiento"] = new SelectList(tipoMovimientos, "Id", "Valor");

            // Listas completas para autocompletado en el frontend
            ViewData["PlantasList"] = await _context.Plantas.Include(p => p.Ciudad).Where(p => p.Activo).OrderBy(p => p.Nombre).ToListAsync();
            ViewData["PuertosList"] = await _context.Puertos.Where(p => p.Activo == 1).OrderBy(p => p.NombrePuerto).ToListAsync();
            ViewData["DepositosList"] = await _context.Depositos.Where(d => d.Activo == 1).OrderBy(d => d.NombreDeposito).ToListAsync();

            // 👇 NUEVA LISTA DE AGENCIAS DE ADUANA 👇
            ViewData["AgenciasList"] = await _context.AgenciasAduanas.OrderBy(a => a.NombreAgencia).ToListAsync();
        }

        // oct-2026: cuando el ejecutivo aprueba una cotización desde las
        // notificaciones del Panel Admin (ver verDetalleCotizacionPortal en
        // operaciones-index.js), llega aquí con ?cotId=N. CotId solo viaja
        // hasta la vista — la carga real de datos pasa por
        // OnGetCotizacionDataAsync, igual que OnPostProcesarPdfAsync.
        [BindProperty(SupportsGet = true)]
        public int? CotId { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            await CargarSelectListsAsync();
            return Page();
        }

        // Misma idea que OnPostProcesarPdfAsync (lector de PDF): devuelve los
        // datos de la cotización como JSON para que el JS del formulario los
        // inyecte en los mismos campos, sin que el operador tenga que
        // reseleccionar cliente/ruta/tipo de servicio ni volver a preguntarle
        // al cliente los datos de contenedor/reefer que ya entregó.
        public async Task<IActionResult> OnGetCotizacionDataAsync(int id)
        {
            try
            {
                var cot = await _context.Cotizaciones
                    .Include(c => c.IdClienteNavigation)
                    .Include(c => c.IdPuertoOrigenNavigation)
                    .Include(c => c.IdPuertoDestinoNavigation)
                    .Include(c => c.IdPuertoTerrestreNavigation)
                    .Include(c => c.IdItinerarioNavigation)
                    .Include(c => c.IdNavieraNavigation)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (cot == null)
                    return new JsonResult(new { success = false, message = "No se encontró la cotización #" + id + "." });

                bool esReefer = !string.IsNullOrWhiteSpace(cot.TipoContenedor) &&
                                 cot.TipoContenedor.Contains("REEFER", StringComparison.OrdinalIgnoreCase);

                // oct-2026 (Solicitar Reserva completa): nave elegida del itinerario,
                // puertos, booking/naviera del cliente y tarifa de la cotización de
                // origen, para precargar también Marítimo/Terrestre/Documental.
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var it = cot.IdItinerarioNavigation;
                DateTime? etd = it != null ? it.Etd.ToDateTime(TimeOnly.MinValue) : cot.FechaTentativaEmbarque;
                DateTime? eta = it?.Eta.ToDateTime(TimeOnly.MinValue);
                DateTime? corteDoc = cot.FechaCorteDocumental ?? it?.CorteDocumental;

                var tarifa = new List<string>();
                if (cot.IdCotizacionOrigen != null)
                {
                    var origen = await _context.Cotizaciones.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cot.IdCotizacionOrigen);
                    if (origen != null)
                    {
                        if (origen.MontoMaritimo != null) tarifa.Add("Marítimo " + CotizacionesModel.FormatearMonto(origen.MontoMaritimo, origen.MonedaMaritimo) + "/cont.");
                        if (origen.MontoTerrestre != null) tarifa.Add("Terrestre " + CotizacionesModel.FormatearMonto(origen.MontoTerrestre, origen.MonedaTerrestre));
                        if (origen.MontoDocumental != null) tarifa.Add("Documental " + CotizacionesModel.FormatearMonto(origen.MontoDocumental, origen.MonedaDocumental));
                    }
                }

                return new JsonResult(new
                {
                    success = true,
                    data = new
                    {
                        idCotizacion = cot.Id,
                        idCliente = cot.IdCliente,
                        cliente = !string.IsNullOrWhiteSpace(cot.IdClienteNavigation?.NombreCliente) ? cot.IdClienteNavigation!.NombreCliente : cot.IdClienteNavigation?.RazonSocial,
                        idTipoServicio = MapearIdTipoServicio(cot.TipoServicio),
                        tipoServicioTexto = cot.TipoServicio,
                        origen = cot.Origen,
                        destino = cot.Destino,
                        mercancia = cot.Mercancia,
                        plantaCarga = cot.PlantaCarga,
                        depositoRetiro = cot.DepositoRetiro,
                        tipoContenedor = cot.TipoContenedor,
                        esReefer = esReefer,
                        condicionReefer = cot.CondicionReefer,
                        temperatura = cot.Temperatura,
                        humedad = cot.Humedad,
                        ventilacion = cot.Ventilacion,
                        cantidadContenedores = cot.CantidadContenedores ?? 1,
                        comentarios = cot.Comentarios,
                        polNombre = cot.IdPuertoOrigenNavigation?.NombrePuerto,
                        podNombre = cot.IdPuertoDestinoNavigation?.NombrePuerto,
                        puertoTerrestre = cot.IdPuertoTerrestreNavigation?.NombrePuerto,
                        zonaEntrega = cot.ZonaEntrega,
                        idNaviera = cot.IdNaviera,
                        naviera = cot.IdNavieraNavigation?.NombreNaviera,
                        nave = cot.Nave,
                        booking = cot.NumeroBooking,
                        instructivo = cot.NumeroInstructivo,
                        etd = etd?.ToString("yyyy-MM-dd", inv),
                        eta = eta?.ToString("yyyy-MM-dd", inv),
                        corteDoc = corteDoc?.ToString("yyyy-MM-dd'T'HH:mm", inv),
                        fechaCarga = cot.FechaCarga?.ToString("yyyy-MM-dd'T'HH:mm", inv),
                        desdeItinerario = it != null,
                        idCotizacionOrigen = cot.IdCotizacionOrigen,
                        tarifaCotizada = tarifa
                    }
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Error leyendo la cotización: " + ex.Message });
            }
        }

        // Traduce el texto libre que genera SolicitarReserva (ej. "EXP MAR+TER",
        // "IMP INTEGRAL (MAR+TER+DOC)") al Id de las 14 combinaciones fijas del
        // <select> de este formulario. El único punto de diferencia entre
        // ambos formatos es el espaciado alrededor de "+", así que basta
        // normalizar (mayúsculas, sin espacios) antes de comparar.
        private static int? MapearIdTipoServicio(string? tipoServicioCotizacion)
        {
            if (string.IsNullOrWhiteSpace(tipoServicioCotizacion)) return null;

            string Normalizar(string s) => s.ToUpperInvariant().Replace(" ", "");

            var combos = new Dictionary<int, string>
            {
                { 1, "EXP INTEGRAL (MAR+TER+DOC)" },
                { 2, "EXP MAR+TER" },
                { 3, "EXP MAR+DOC" },
                { 4, "EXP TER+DOC" },
                { 5, "EXP MAR" },
                { 6, "EXP TER" },
                { 7, "EXP DOC" },
                { 8, "IMP INTEGRAL (MAR+TER+DOC)" },
                { 9, "IMP MAR+TER" },
                { 10, "IMP MAR+DOC" },
                { 11, "IMP TER+DOC" },
                { 12, "IMP MAR" },
                { 13, "IMP TER" },
                { 14, "IMP DOC" },
            };

            string normalizado = Normalizar(tipoServicioCotizacion);
            foreach (var kv in combos)
            {
                if (Normalizar(kv.Value) == normalizado) return kv.Key;
            }
            return null;
        }

        [BindProperty]
        public Operacione Operacione { get; set; } = default!;

        public async Task<IActionResult> OnPostAsync(
            IFormFile? ArchivoInstructivoTerrestre,
            IFormFile? DocInstructivo,
            IFormFile? DocMatriz,
            IFormFile? DocBookingCliente,
            IFormFile? EvidenciaInstructivo)
        {
            ModelState.Clear();

            // --- VALIDAR BOOKING DUPLICADO ---
            bool bookingExiste = await _context.Operaciones
                .AnyAsync(o => o.NumeroBooking == Operacione.NumeroBooking);

            if (bookingExiste)
            {
                TempData["ErrorBooking"] = $"El número de Booking/Referencia '{Operacione.NumeroBooking}' ya se encuentra registrado. Verifique el dato.";
                await CargarSelectListsAsync();
                return Page();
            }

            string nombreUsuario = User.Identity?.Name ?? "Sistema";

            var serv = Operacione.IdTipoServicio ?? 0;
            bool esMaritimo = new[] { 1, 2, 3, 5, 8, 9, 10, 12 }.Contains(serv);
            bool esTerrestre = new[] { 1, 2, 4, 6, 8, 9, 11, 13 }.Contains(serv);
            bool esDocumental = new[] { 1, 3, 4, 7, 8, 10, 11, 14 }.Contains(serv);
            bool esSoloTerrestre = esTerrestre && !esMaritimo;

            // --- NORMALIZAR CAMPOS OBLIGATORIOS ---
            if (!esMaritimo)
            {
                // oct-2026 (minuta Portal Web de Clientes): las operaciones sin tramo
                // marítimo (Terrestre y/o Documental) también registran Naviera y Nave
                // del embarque del cliente — vienen de "Datos del Embarque", el mismo
                // bloque que usa el marítimo (Operacione.IdNaviera / Operacione.Nave).
                if (Operacione.IdNaviera <= 0 || !await _context.Navieras.AnyAsync(n => n.Id == Operacione.IdNaviera))
                    Operacione.IdNaviera = _context.Navieras.FirstOrDefault()?.Id ?? 1;
                Operacione.Nave = string.IsNullOrWhiteSpace(Operacione.Nave) ? null : Operacione.Nave.Trim().ToUpper();
                Operacione.IdTipoCarga = 1;
                Operacione.IdPuertoOrigen = null;
                Operacione.IdPuertoDestino = null;
            }

            else
            {
                if (Operacione.IdNaviera == 0)
                    Operacione.IdNaviera = _context.Navieras.FirstOrDefault()?.Id ?? 1;
                if (Operacione.IdTipoCarga == 0)
                    Operacione.IdTipoCarga = 1;
            }

            // Propietario de Contrato: solo valores de la lista; "Otros" guarda el texto.
            Operacione.PropietarioContrato = AtilsonCargoSpa.Models.Operacione.NormalizarPropietarioContrato(Operacione.PropietarioContrato);
            Operacione.PropietarioContratoOtro = Operacione.PropietarioContrato == "OTROS" && !string.IsNullOrWhiteSpace(Operacione.PropietarioContratoOtro)
                ? Operacione.PropietarioContratoOtro.Trim().ToUpper()
                : null;

            if (Operacione.IdCliente == 0)
                Operacione.IdCliente = _context.Clientes.FirstOrDefault()?.Id ?? 1;

            // --- CAMPOS AUDITORÍA Y CONTROL ---
            Operacione.FechaCreacion = DateTime.Now;
            Operacione.UsuarioCreador = nombreUsuario;
            Operacione.Activo = 1;
            Operacione.IsDeleted = false;
            Operacione.ContenedorIngresado = false;
            Operacione.EstadoLar = "No Solicitado";
            Operacione.EvidenciaLar = null;

            // Terminal portuario dinámico (Marítimo o Terrestre)
            string? termMaritimo = Request.Form["TerminalMaritimoDDL"];
            if (string.IsNullOrWhiteSpace(termMaritimo)) termMaritimo = Request.Form["TerminalMaritimoStr"];

            string? termTerrestre = Request.Form["TerminalTerrestreDDL"];
            if (string.IsNullOrWhiteSpace(termTerrestre)) termTerrestre = Request.Form["TerminalTerrestreStr"];

            Operacione.TerminalPortuario = esMaritimo ? termMaritimo : termTerrestre;

            // --- CORRELATIVO ATL-YYYYMM-XXX ---
            int anioActual = DateTime.Now.Year;
            int mesActual = DateTime.Now.Month;
            int conteoMes = await _context.Operaciones.CountAsync(o =>
                o.FechaCreacion != null &&
                o.FechaCreacion.Value.Year == anioActual &&
                o.FechaCreacion.Value.Month == mesActual) + 1;
            Operacione.CodigoAtilson = $"ATL-{anioActual}{mesActual:D2}-{conteoMes:D3}";
            Operacione.EstadoWorkflow = "EN PROCESO";

            if (string.IsNullOrWhiteSpace(Operacione.NumeroContenedor))
                Operacione.NumeroContenedor = null;

            // --- PARÁMETROS REEFER ---
            if (!esSoloTerrestre)
            {
                Operacione.CondicionReefer = Request.Form["CondicionReefer"];
                Operacione.MarcaAc = Request.Form["MarcaAc"];
                Operacione.Atmosfera = Request.Form["TipoAtmosfera"];
                if (double.TryParse(Request.Form["TempSeteada"], out double temp)) Operacione.Temperatura = temp;
                if (double.TryParse(Request.Form["Ventilacion"], out double vent)) Operacione.Ventilacion = vent;
                if (double.TryParse(Request.Form["Humedad"], out double hum)) Operacione.Humedad = hum;
                if (double.TryParse(Request.Form["NivelO2"], out double o2)) Operacione.O2 = o2;
                if (double.TryParse(Request.Form["NivelCO2"], out double co2)) Operacione.Co2 = co2;
            }
            else
            {
                Operacione.CondicionReefer = null;
                Operacione.MarcaAc = null;
                Operacione.Atmosfera = null;
                Operacione.Temperatura = null;
                Operacione.Ventilacion = null;
                Operacione.Humedad = null;
                Operacione.O2 = null;
                Operacione.Co2 = null;
            }

            // --- TIPO DE EMISIÓN B/L (opcional al crear, solo aplica si es marítimo) ---
            // Mismo catálogo que Detalle/Comercial/Finanzas (EmisionBlHelper). Con
            // Originales se guarda también el Lugar de emisión. Si falta un dato
            // (Lugar u "Otro" sin texto) no se bloquea la creación: se guarda lo
            // válido y el resto se completa en Gestión de Matriz, donde es obligatorio.
            if (esMaritimo)
            {
                var emisionBl = AtilsonCargoSpa.Helpers.EmisionBlHelper.LeerFormulario(Request.Form);
                if (emisionBl.TieneOtro && emisionBl.Otro == null) emisionBl = emisionBl with { Tipo = null };
                AtilsonCargoSpa.Helpers.EmisionBlHelper.Aplicar(Operacione, emisionBl);
                Operacione.TipoTarifaMaritima = Operacione.TipoTarifaMaritima == "SPOT" || Operacione.TipoTarifaMaritima == "CONTRATO" ? Operacione.TipoTarifaMaritima : null;
            }
            else
            {
                Operacione.TipoEmisionBl = null;
                Operacione.TipoEmisionBlOtro = null;
                Operacione.UbicacionOriginalesDestino = null;
                Operacione.TipoTarifaMaritima = null;
            }
            Operacione.OperacionesTerrestres ??= new List<OperacionesTerrestre>();
            Operacione.OperacionesDocumentales ??= new List<OperacionesDocumentale>();
            Operacione.Unidadestecnicas ??= new List<Unidadestecnica>();

            // --- SERVICIO TERRESTRE ---
            if (esTerrestre)
            {
                string? rutaInstructivoTerrestre = null;
                if (EvidenciaInstructivo != null && EvidenciaInstructivo.Length > 0)
                {
                    string carpeta = Path.Combine(_env.WebRootPath, "uploads", "instructivos");
                    if (!Directory.Exists(carpeta)) Directory.CreateDirectory(carpeta);
                    string uniqueName = $"TerrestreInstructivo_{DateTime.Now.Ticks}_{Path.GetFileName(EvidenciaInstructivo.FileName).Replace(" ", "_")}";
                    using (var stream = new FileStream(Path.Combine(carpeta, uniqueName), FileMode.Create))
                        await EvidenciaInstructivo.CopyToAsync(stream);
                    rutaInstructivoTerrestre = $"/uploads/instructivos/{uniqueName}";
                }

                DateTime? fechaCarga = DateTime.TryParse(Request.Form["FechaCarga"], out DateTime dtCarga) ? dtCarga : null;

                Operacione.OperacionesTerrestres.Add(new OperacionesTerrestre
                {
                    EmpresaTransporte = Request.Form["EmpresaTransporte"],
                    RutTransporte = Request.Form["RutTransporte"],
                    PuertoEntrega = Request.Form["PuertoEntrega"],
                    TerminalTerrestreStr = termTerrestre,
                    DepositoRetiro = Request.Form["DepositoRetiro"],
                    PlantaCarga = Request.Form["PlantaCarga"],
                    ZonaCarga = Request.Form["ZonaCarga"],
                    FechaCarga = fechaCarga,
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = nombreUsuario,
                    Activo = true,
                    SorteoEscaner = false
                });
            }
            else if (esMaritimo)
            {
                // Sin logística terrestre contratada, pero el motor de Finanzas necesita
                // saber el terminal portuario para calcular ISPS (Seguridad de la Carga)
                // automáticamente — igual que si se hubiese completado desde el Index.
                Operacione.OperacionesTerrestres.Add(new OperacionesTerrestre
                {
                    TerminalTerrestreStr = termMaritimo,
                    // Depósito de retiro vacío (oct-2026): sin terrestre se elige en Marítimo,
                    // para la tarifa del Gate del depósito.
                    DepositoRetiro = string.IsNullOrWhiteSpace(Request.Form["DepositoRetiroGateMar"]) ? null : Request.Form["DepositoRetiroGateMar"].ToString().Trim(),
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = nombreUsuario,
                    Activo = true,
                    SorteoEscaner = false
                });
            }

            // Solo documental (oct-2026): el depósito de retiro vacío se elige en Documental
            // y se guarda en un registro terrestre principal (lo usa la tarifa del Gate).
            if (!esTerrestre && !esMaritimo && !string.IsNullOrWhiteSpace(Request.Form["DepositoRetiroGateDoc"]))
            {
                Operacione.OperacionesTerrestres.Add(new OperacionesTerrestre
                {
                    DepositoRetiro = Request.Form["DepositoRetiroGateDoc"].ToString().Trim(),
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = nombreUsuario,
                    Activo = true,
                    SorteoEscaner = false
                });
            }

            // --- SERVICIO DOCUMENTAL ---
            if (esDocumental)
            {
                string carpeta = Path.Combine(_env.WebRootPath, "uploads", "evidencias");
                if (!Directory.Exists(carpeta)) Directory.CreateDirectory(carpeta);

                string? rInstructivo = null;
                if (DocInstructivo != null && DocInstructivo.Length > 0)
                {
                    string u = $"DocInst_{DateTime.Now.Ticks}_{Path.GetFileName(DocInstructivo.FileName).Replace(" ", "_")}";
                    using (var s = new FileStream(Path.Combine(carpeta, u), FileMode.Create)) { await DocInstructivo.CopyToAsync(s); }
                    rInstructivo = $"/uploads/evidencias/{u}";
                }

                string? rMatriz = null;
                if (DocMatriz != null && DocMatriz.Length > 0)
                {
                    string u = $"DocMatriz_{DateTime.Now.Ticks}_{Path.GetFileName(DocMatriz.FileName).Replace(" ", "_")}";
                    using (var s = new FileStream(Path.Combine(carpeta, u), FileMode.Create)) { await DocMatriz.CopyToAsync(s); }
                    rMatriz = $"/uploads/evidencias/{u}";
                }

                string? rBooking = null;
                if (DocBookingCliente != null && DocBookingCliente.Length > 0)
                {
                    string u = $"DocBkgCli_{DateTime.Now.Ticks}_{Path.GetFileName(DocBookingCliente.FileName).Replace(" ", "_")}";
                    using (var s = new FileStream(Path.Combine(carpeta, u), FileMode.Create)) { await DocBookingCliente.CopyToAsync(s); }
                    rBooking = $"/uploads/evidencias/{u}";
                }

                // La agencia ahora viaja por Id (FK real), no por nombre en texto libre —
                // así el motor de Finanzas puede cruzarla contra TarifasDocumentales
                // exactamente igual que cuando se selecciona desde el Index.
                int? idAgenciaSeleccionada = int.TryParse(Request.Form["DocAgencia"], out int idAgTmp) ? idAgTmp : (int?)null;
                bool esExpoDoc = new[] { 1, 2, 3, 4, 5, 6, 7 }.Contains(serv);

                var nuevoDoc = new OperacionesDocumentale
                {
                    IdAgenciaAduana = idAgenciaSeleccionada,
                    GuiaVisado = false,
                    EvidenciaMatriz = rMatriz,
                    MatrizPresentada = rMatriz != null,
                    Mandato = rInstructivo,
                    RutaInstructivo = rInstructivo,
                    DocBkgCli = rBooking,
                    EstadoDocumental = "PENDIENTE",
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = nombreUsuario,
                    Activo = true,
                    ExtensionDocumental = false
                };

                // Exportación usa DUS, Importación usa DIN — igual que la lógica del Index
                // (radioDus/radioDin son mutuamente excluyentes según Din esté vacío o no).
                if (esExpoDoc) nuevoDoc.DusDin = Request.Form["DocDus"];
                else nuevoDoc.Din = Request.Form["DocDus"];

                Operacione.OperacionesDocumentales.Add(nuevoDoc);
            }

            // --- UNIDADES TÉCNICAS ADICIONALES ---
            var mapaUnidadesExtra = new List<(int Indice, Unidadestecnica Unidad)>();
            for (int i = 2; i <= 20; i++)
            {
                var tipoCargaKey = $"ContenedoresExtra[{i}].IdTipoCarga";
                if (!Request.Form.ContainsKey(tipoCargaKey) || string.IsNullOrWhiteSpace(Request.Form[tipoCargaKey]))
                    continue;

                var nuevaUnidad = new Unidadestecnica
                {
                    IdTipoCarga = int.TryParse(Request.Form[tipoCargaKey], out int tc) ? tc : 1,
                    TipoContenedor = Request.Form[$"ContenedoresExtra[{i}].TipoContenedor"],
                    Commodity = Request.Form[$"ContenedoresExtra[{i}].Commodity"],
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = nombreUsuario
                };

                if (!esSoloTerrestre)
                {
                    nuevaUnidad.CondicionReefer = Request.Form[$"ContenedoresExtra[{i}].CondicionReefer"];
                    nuevaUnidad.TipoAtmosfera = Request.Form[$"ContenedoresExtra[{i}].TipoAtmosfera"];
                    nuevaUnidad.MarcaAc = Request.Form[$"ContenedoresExtra[{i}].MarcaAc"];
                    if (decimal.TryParse(Request.Form[$"ContenedoresExtra[{i}].TempSeteada"], out decimal tempExt)) nuevaUnidad.Temperatura = tempExt;
                    if (int.TryParse(Request.Form[$"ContenedoresExtra[{i}].Humedad"], out int humExt)) nuevaUnidad.Humedad = humExt;
                    if (int.TryParse(Request.Form[$"ContenedoresExtra[{i}].Ventilacion"], out int ventExt)) nuevaUnidad.Ventilacion = ventExt;
                    if (decimal.TryParse(Request.Form[$"ContenedoresExtra[{i}].NivelO2"], out decimal o2Ext)) nuevaUnidad.NivelO2 = o2Ext;
                    if (decimal.TryParse(Request.Form[$"ContenedoresExtra[{i}].NivelCO2"], out decimal co2Ext)) nuevaUnidad.NivelCo2 = co2Ext;
                }

                Operacione.Unidadestecnicas.Add(nuevaUnidad);
                mapaUnidadesExtra.Add((i, nuevaUnidad));
            }

            _context.Operaciones.Add(Operacione);
            await _context.SaveChangesAsync();

            // --- TRANSPORTE POR CONTENEDOR ADICIONAL ---
            if (esTerrestre && mapaUnidadesExtra.Any())
            {
                foreach (var (i, unidad) in mapaUnidadesExtra)
                {
                    string prefijo = $"ContenedoresExtra[{i}].";
                    string? empresaU = Request.Form[prefijo + "EmpresaTransporte"];
                    string? depositoU = Request.Form[prefijo + "DepositoRetiro"];
                    string? puertoU = Request.Form[prefijo + "PuertoEntrega"];
                    string? terminalU = Request.Form[prefijo + "TerminalTerrestreStr"];
                    string? plantaU = Request.Form[prefijo + "PlantaCarga"];
                    string? zonaU = Request.Form[prefijo + "ZonaCarga"];
                    string? fechaCargaUStr = Request.Form[prefijo + "FechaCarga"];

                    // Si no se cargó ningún dato de transporte para este contenedor,
                    // no se crea la fila — queda pendiente de completar desde el Board
                    // Operativo, igual que ya funciona hoy para bookings simples.
                    bool tieneAlgo = !string.IsNullOrWhiteSpace(empresaU) || !string.IsNullOrWhiteSpace(depositoU) ||
                                      !string.IsNullOrWhiteSpace(puertoU) || !string.IsNullOrWhiteSpace(plantaU) ||
                                      !string.IsNullOrWhiteSpace(fechaCargaUStr);
                    if (!tieneAlgo) continue;

                    DateTime? fechaCargaU = DateTime.TryParse(fechaCargaUStr, out DateTime dtCargaU) ? dtCargaU : null;

                    _context.OperacionesTerrestres.Add(new OperacionesTerrestre
                    {
                        IdOperacion = Operacione.Id,
                        IdUnidadTecnica = unidad.Id,
                        EmpresaTransporte = empresaU,
                        DepositoRetiro = depositoU,
                        PuertoEntrega = puertoU,
                        TerminalTerrestreStr = terminalU,
                        PlantaCarga = plantaU,
                        ZonaCarga = zonaU,
                        FechaCarga = fechaCargaU,
                        FechaCreacion = DateTime.Now,
                        UsuarioCreador = nombreUsuario,
                        Activo = true,
                        SorteoEscaner = false
                    });
                }
                await _context.SaveChangesAsync();
            }

            // Ejecuta el mismo motor que usa el Index, para que ISPS, Gate, tarifas
            // documentales por agencia y fletes queden reflejados en Finanzas desde
            // el instante en que se crea la operación — no solo tras el primer edit.
            await _finanzasSync.SincronizarAsync(Operacione.Id, nombreUsuario);

            // Si la operación nace de una solicitud de reserva del Portal
            // (Create?cotId=N), se marca como convertida: deja de aparecer en el
            // Centro de Notificaciones y no se vuelve a crear por error.
            if (CotId != null)
            {
                var solicitud = await _context.Cotizaciones.FirstOrDefaultAsync(c => c.Id == CotId && c.TipoSolicitud != Cotizacion.TipoCotizacion);
                if (solicitud != null && solicitud.Estado == "NUEVA SOLICITUD")
                {
                    solicitud.Estado = Cotizacion.EstadoConvertida;
                    await _context.SaveChangesAsync();
                }
            }

            TempData["SuccessMsg"] = $"La operación {Operacione.NumeroBooking} fue creada exitosamente.";

            bool esAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            bool generarPdf = Request.Form["GenerarPdfDirecto"] == "true";

            if (esAjax && generarPdf)
            {
                string pdfUrl = Url.Page("/Operaciones/ReservaPdf", new { id = Operacione.Id });
                string? correoCliente = Operacione.IdCliente == null ? null
                    : await _context.Clientes.Where(c => c.Id == Operacione.IdCliente).Select(c => c.Correo).FirstOrDefaultAsync();
                return new JsonResult(new { success = true, id = Operacione.Id, pdfUrl, correoCliente });
            }

            if (generarPdf)
            {
                TempData["OpenPdfId"] = Operacione.Id;
            }

            return RedirectToPage("./Index");
        }
    }
}