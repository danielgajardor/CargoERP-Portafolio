using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Services;
using AtilsonCargoSpa.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    // Solicitud de Reserva / Servicio (cliente con tarifa ya acordada). Llega al
    // Centro de Notificaciones de Operaciones y la ejecutiva la abre en Create
    // precargado (Create?cotId=N) para completar la operación.
    //
    // oct-2026 (minuta Portal Web de Clientes):
    //  - Marítimo: POL, POD, tipo de contenedor (+ reefer) y la nave elegida del
    //    calendario de salidas a 4-5 semanas (ItinerariosNaves). Si la salida no
    //    está publicada, fecha tentativa de embarque.
    //  - Terrestre: planta (zona de carga), puerto de salida y zona de
    //    retiro/entrega del contenedor. Con Marítimo el puerto sale del POL/POD.
    //  - Documental: booking, nave, naviera e instructivo / corte documental.
    //  - Integral o Marítimo + Documental: el documental es solo un check; usa la
    //    misma nave y booking de la reserva (no se piden dos veces).
    //  - Sin Marítimo (Terrestre y/o Documental): el cliente informa Booking,
    //    Naviera y Nave de su embarque, que la operación necesita igual.
    // Misma tabla Cotizaciones que la cotización real, con TipoSolicitud = RESERVA.
    public class SolicitarReservaModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly ILogger<SolicitarReservaModel> _logger;

        public SolicitarReservaModel(AtilsonContext context, ILogger<SolicitarReservaModel> logger)
        {
            _context = context;
            _logger = logger;
        }

        [BindProperty]
        public Cotizacion NuevaCotizacion { get; set; } = new Cotizacion();

        // Atajos de entrada: desde una tarjeta de Itinerarios (?itinerario=N) o
        // desde una cotización respondida en Mis Cotizaciones (?cotizacion=N).
        [BindProperty(SupportsGet = true, Name = "itinerario")]
        public int? IdItinerarioQ { get; set; }

        [BindProperty(SupportsGet = true, Name = "cotizacion")]
        public int? IdCotizacionQ { get; set; }

        // oct-2026: reemplaza TempData para el aviso de éxito — TempData depende de una
        // cookie protegida con las claves de Data Protection del proceso, y en este
        // servidor esa cookie no estaba sobreviviendo el redirect (quedaba vacía en el
        // GET siguiente aunque la cotización sí se guardara bien). Pasar el flag como
        // query param en la propia URL del redirect no depende de ninguna cookie.
        [BindProperty(SupportsGet = true)]
        public bool Exito { get; set; }

        // "Otra planta" (oct-2026): nombre y zona escritos por el cliente; si marca
        // "Guardar en mis plantas" queda en sus plantas, por validar por Atilson.
        [BindProperty]
        public string? OtraPlantaNombre { get; set; }

        [BindProperty]
        public int? OtraPlantaIdCiudad { get; set; }

        [BindProperty]
        public bool GuardarOtraPlanta { get; set; } = true;

        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Zonas { get; set; } = new();

        public List<Puerto> Puertos { get; set; } = new();
        public List<Puerto> PuertosChile { get; set; } = new();
        public List<string> ZonasPuerto { get; set; } = new();
        public List<Planta> Plantas { get; set; } = new();
        public List<Deposito> Depositos { get; set; } = new();
        public List<Naviera> Navieras { get; set; } = new();

        // Cotización respondida desde la que se reserva (se muestra como banner).
        public Cotizacion? CotizacionOrigen { get; set; }

        public static readonly string[] TiposContenedor = SolicitarCotizacionModel.TiposContenedor;

        private int? ObtenerIdClienteSesion()
        {
            var claimCliente = User.FindFirst("IdCliente")?.Value;
            return int.TryParse(claimCliente, out int idCliente) ? idCliente : null;
        }

        // Plantas y Depósitos reales (no texto libre) — mismas tablas que usa
        // Operaciones/Create para sus selects "Planta de cliente" y "Depósito
        // de Retiro Vacío", para que la precarga en Create encuentre el mismo
        // texto exacto. Plantas se acota a las del cliente de la sesión (o sin
        // cliente asignado = genéricas): no se le exponen a un cliente las
        // plantas de otra empresa. Depósitos es un catálogo genérico.
        private async Task CargarCatalogosAsync()
        {
            var idCliente = ObtenerIdClienteSesion();

            // Portal (oct-2026): solo las plantas registradas por el cliente (+ "Otra planta"
            // en la vista); depósitos de Atilson más los propios del cliente.
            Plantas = idCliente == null ? new List<Planta>() : await PortalUbicacionesHelper.PlantasDelClienteAsync(_context, idCliente.Value);
            Depositos = idCliente == null ? new List<Deposito>() : await PortalUbicacionesHelper.DepositosParaClienteAsync(_context, idCliente.Value);
            Zonas = await PortalUbicacionesHelper.ZonasAsync(_context, OtraPlantaIdCiudad);

            Navieras = await _context.Navieras.AsNoTracking()
                .Where(n => n.Activo == 1 && !n.IsDeleted)
                .OrderBy(n => n.NombreNaviera)
                .ToListAsync();

            Puertos = await PuertosHelper.ListarUnicosAsync(_context);
            PuertosChile = Puertos.Where(PuertosHelper.EsChileno).ToList();
            ZonasPuerto = PuertosChile.Select(p => p.NombrePuerto.Trim().ToUpper()).Distinct().OrderBy(n => n).ToList();
        }

        private async Task<Cotizacion?> BuscarCotizacionOrigenAsync(int? id, int idCliente)
        {
            if (id == null) return null;
            return await _context.Cotizaciones.AsNoTracking()
                .Include(c => c.IdPuertoOrigenNavigation)
                .Include(c => c.IdPuertoDestinoNavigation)
                .Include(c => c.IdPuertoTerrestreNavigation)
                .FirstOrDefaultAsync(c => c.Id == id && c.IdCliente == idCliente
                                          && c.TipoSolicitud == Cotizacion.TipoCotizacion
                                          && c.Estado == Cotizacion.EstadoRespondida && c.Activo == true);
        }

        // Id del puerto tal como aparece en los <select> (uno por nombre+país).
        private int? NormalizarPuerto(Puerto? p) =>
            p == null ? null : Puertos.FirstOrDefault(x => PuertosHelper.Clave(x) == PuertosHelper.Clave(p))?.Id ?? p.Id;

        public async Task<IActionResult> OnGetAsync()
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null)
                return RedirectToPage("/Login");

            await CargarCatalogosAsync();
            var c = NuevaCotizacion;

            // 1) Desde una cotización respondida: misma ruta, servicios y equipo.
            CotizacionOrigen = await BuscarCotizacionOrigenAsync(IdCotizacionQ, idCliente.Value);
            if (CotizacionOrigen != null)
            {
                c.IdCotizacionOrigen = CotizacionOrigen.Id;
                c.TipoServicio = CotizacionOrigen.TipoServicio;
                c.IdPuertoOrigen = NormalizarPuerto(CotizacionOrigen.IdPuertoOrigenNavigation);
                c.IdPuertoDestino = NormalizarPuerto(CotizacionOrigen.IdPuertoDestinoNavigation);
                c.TipoContenedor = CotizacionOrigen.TipoContenedor;
                c.IdPuertoTerrestre = NormalizarPuerto(CotizacionOrigen.IdPuertoTerrestreNavigation);
                // La planta cotizada: si es una de sus plantas, queda elegida; si no, como "Otra planta".
                if (!string.IsNullOrWhiteSpace(CotizacionOrigen.PlantaCarga))
                {
                    if (Plantas.Any(p => p.Nombre == CotizacionOrigen.PlantaCarga))
                        c.PlantaCarga = CotizacionOrigen.PlantaCarga;
                    else
                    {
                        c.PlantaCarga = PortalUbicacionesHelper.OpcionOtra;
                        OtraPlantaNombre = CotizacionOrigen.PlantaCarga;
                        var zonaTxt = MaestrosAuditoria.Normalizar(CotizacionOrigen.ZonaCarga);
                        OtraPlantaIdCiudad = Zonas.Where(z => MaestrosAuditoria.Normalizar(z.Text) == zonaTxt).Select(z => (int?)int.Parse(z.Value)).FirstOrDefault();
                        Zonas = await PortalUbicacionesHelper.ZonasAsync(_context, OtraPlantaIdCiudad);
                    }
                }
                c.CantidadContenedores = 1;
            }

            // 2) Desde una tarjeta de itinerario: esa salida queda elegida.
            if (IdItinerarioQ != null)
            {
                var it = await _context.ItinerariosNaves.AsNoTracking()
                    .Include(i => i.IdPuertoOrigenNavigation)
                    .Include(i => i.IdPuertoDestinoNavigation)
                    .FirstOrDefaultAsync(i => i.Id == IdItinerarioQ && i.Activo && i.Etd >= ItinerariosHelper.Hoy);
                if (it != null)
                {
                    c.IdItinerario = it.Id;
                    c.IdPuertoOrigen = NormalizarPuerto(it.IdPuertoOrigenNavigation);
                    c.IdPuertoDestino = NormalizarPuerto(it.IdPuertoDestinoNavigation);
                    if (string.IsNullOrWhiteSpace(c.TipoServicio) || !SolicitarCotizacionModel.IncluyeModulo(c.TipoServicio, "MAR"))
                    {
                        bool polChile = it.IdPuertoOrigenNavigation != null && PuertosHelper.EsChileno(it.IdPuertoOrigenNavigation);
                        c.TipoServicio = (polChile ? "EXP" : "IMP") + " MAR";
                    }
                    c.CantidadContenedores ??= 1;
                }
            }

            return Page();
        }

        // Selector de nave: tarjetas de las salidas de la ruta elegida (se pide
        // por fetch cuando el cliente cambia POL/POD). Devuelve HTML del partial,
        // así las tarjetas son idénticas a las de la página Itinerarios.
        public async Task<IActionResult> OnGetSalidasAsync(int? pol, int? pod, int? seleccionada)
        {
            if (ObtenerIdClienteSesion() == null) return Unauthorized();
            var salidas = (pol == null || pod == null)
                ? new List<ItinerarioNave>()
                : await ItinerariosHelper.ProximasSalidasAsync(_context, pol, pod, max: 40);
            return Partial("_SalidasSelector", new SalidasSelectorVm
            {
                Salidas = salidas,
                Seleccionada = seleccionada,
                RutaElegida = pol != null && pod != null
            });
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null)
                return RedirectToPage("/Login");

            await CargarCatalogosAsync();
            var c = NuevaCotizacion;

            // TipoServicio es string no-nullable: se reemplaza el "is required"
            // del binder (en inglés) por los mensajes propios de abajo.
            ModelState.Remove("NuevaCotizacion.TipoServicio");
            // Temperatura se lee a mano con InvariantCulture (<input type=number>
            // manda "-18.5"; con cultura es-CL el binder lo leería como -185).
            ModelState.Remove("NuevaCotizacion.Temperatura");

            bool mar = SolicitarCotizacionModel.IncluyeModulo(c.TipoServicio, "MAR");
            bool ter = SolicitarCotizacionModel.IncluyeModulo(c.TipoServicio, "TER");
            bool doc = SolicitarCotizacionModel.IncluyeModulo(c.TipoServicio, "DOC");
            bool exp = !(c.TipoServicio ?? "").TrimStart().StartsWith("IMP", StringComparison.OrdinalIgnoreCase);
            void Error(string m) => ModelState.AddModelError(string.Empty, m);

            if (!mar && !ter && !doc)
                Error("Selecciona al menos un servicio.");

            // El puerto terrestre que escribió el cliente se guarda antes de
            // limpiar los campos de respuesta (IdPuertoTerrestre se comparte:
            // en una cotización lo responde el ejecutivo, en una reserva lo
            // informa el cliente).
            int? puertoTerrestreInput = c.IdPuertoTerrestre;
            c.LimpiarCamposRespuesta();

            Puerto? pol = null, pod = null;
            ItinerarioNave? salida = null;

            // ---------- Marítimo ----------
            if (mar)
            {
                pol = Puertos.FirstOrDefault(p => p.Id == c.IdPuertoOrigen);
                pod = Puertos.FirstOrDefault(p => p.Id == c.IdPuertoDestino);
                if (pol == null) Error("Selecciona el puerto de origen.");
                if (pod == null) Error("Selecciona el puerto de destino.");
                if (pol != null && pod != null && PuertosHelper.Clave(pol) == PuertosHelper.Clave(pod))
                    Error("El puerto de origen y el de destino no pueden ser el mismo.");
                if (string.IsNullOrWhiteSpace(c.TipoContenedor) || !TiposContenedor.Contains(c.TipoContenedor))
                    Error("Selecciona el tipo de contenedor.");
                if (c.CantidadContenedores is null or < 1 or > 200)
                    Error("Indica cuántos contenedores necesitas.");

                if (c.IdItinerario != null)
                {
                    salida = await _context.ItinerariosNaves.AsNoTracking()
                        .Include(i => i.IdPuertoOrigenNavigation)
                        .Include(i => i.IdPuertoDestinoNavigation)
                        .FirstOrDefaultAsync(i => i.Id == c.IdItinerario && i.Activo);
                    bool rutaCalza = salida != null && pol != null && pod != null
                        && salida.IdPuertoOrigenNavigation != null && salida.IdPuertoDestinoNavigation != null
                        && PuertosHelper.Clave(salida.IdPuertoOrigenNavigation) == PuertosHelper.Clave(pol)
                        && PuertosHelper.Clave(salida.IdPuertoDestinoNavigation) == PuertosHelper.Clave(pod);
                    if (salida == null || salida.Etd < ItinerariosHelper.Hoy)
                        Error("La salida elegida ya no está disponible. Elige otra.");
                    else if (!rutaCalza)
                        Error("La salida elegida no corresponde a los puertos seleccionados.");
                }
                else if (c.FechaTentativaEmbarque == null)
                {
                    Error("Elige una salida o indica la fecha tentativa de embarque.");
                }

                bool esReefer = c.TipoContenedor?.Contains("REEFER", StringComparison.OrdinalIgnoreCase) == true;
                if (esReefer)
                {
                    c.Temperatura = decimal.TryParse(Request.Form["NuevaCotizacion.Temperatura"].ToString().Trim(),
                        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var t) ? t : null;
                    if (string.IsNullOrWhiteSpace(c.CondicionReefer)) c.CondicionReefer = null;
                    if (string.IsNullOrWhiteSpace(c.Ventilacion)) c.Ventilacion = null;
                }
                else
                {
                    c.CondicionReefer = null; c.Temperatura = null; c.Humedad = null; c.Ventilacion = null;
                }
            }
            else
            {
                c.IdPuertoOrigen = null; c.IdPuertoDestino = null; c.IdItinerario = null;
                c.TipoContenedor = null; c.CantidadContenedores = null; c.FechaTentativaEmbarque = null;
                c.CondicionReefer = null; c.Temperatura = null; c.Humedad = null; c.Ventilacion = null;
            }

            // ---------- Embarque informado por el cliente (sin Marítimo) ----------
            if (!mar && (ter || doc))
            {
                c.NumeroBooking = c.NumeroBooking?.Trim().ToUpper();
                c.Nave = c.Nave?.Trim().ToUpper();
                if (string.IsNullOrWhiteSpace(c.NumeroBooking)) Error("Ingresa el número de booking de tu embarque.");
                if (c.IdNaviera == null || Navieras.All(n => n.Id != c.IdNaviera)) Error("Selecciona la naviera de tu embarque.");
                if (string.IsNullOrWhiteSpace(c.Nave)) Error("Ingresa el nombre de la nave de tu embarque.");
            }

            // ---------- Terrestre ----------
            Planta? plantaNueva = null;
            if (ter)
            {
                var elegida = await PortalUbicacionesHelper.ResolverPlantaAsync(_context, idCliente.Value, Plantas,
                    c.PlantaCarga, OtraPlantaNombre, OtraPlantaIdCiudad, GuardarOtraPlanta, User.Identity?.Name ?? "Cliente");
                if (elegida.Error != null) Error(elegida.Error);
                c.PlantaCarga = elegida.Nombre ?? (c.PlantaCarga == PortalUbicacionesHelper.OpcionOtra ? PortalUbicacionesHelper.OpcionOtra : c.PlantaCarga);
                c.ZonaCarga = elegida.Zona;
                plantaNueva = elegida.Nueva;

                // La "zona de retiro / entrega" se eliminó (oct-2026): el puerto del tramo
                // ya indica dónde se entrega o retira el contenedor.
                c.ZonaEntrega = null;

                if (string.IsNullOrWhiteSpace(c.DepositoRetiro)) c.DepositoRetiro = null;
                else if (Depositos.All(d => d.NombreDeposito != c.DepositoRetiro)) Error("El depósito elegido no es válido.");

                if (mar)
                {
                    // Con Marítimo el puerto terrestre es el mismo del embarque:
                    // POL en exportación, POD en importación.
                    c.IdPuertoTerrestre = exp ? pol?.Id : pod?.Id;
                }
                else
                {
                    var pt = PuertosChile.FirstOrDefault(p => p.Id == puertoTerrestreInput);
                    if (pt == null) Error(exp ? "Selecciona el puerto de salida." : "Selecciona el puerto de llegada.");
                    c.IdPuertoTerrestre = pt?.Id;
                }
            }
            else
            {
                c.PlantaCarga = null; c.ZonaCarga = null; c.ZonaEntrega = null;
                c.DepositoRetiro = null; c.FechaCarga = null; c.IdPuertoTerrestre = null;
            }

            // ---------- Documental ----------
            if (doc && !mar)
            {
                c.NumeroInstructivo = string.IsNullOrWhiteSpace(c.NumeroInstructivo) ? null : c.NumeroInstructivo.Trim().ToUpper();
            }
            else if (!doc)
            {
                c.NumeroInstructivo = null;
                c.FechaCorteDocumental = null;
            }

            if (!ModelState.IsValid)
            {
                CotizacionOrigen = await BuscarCotizacionOrigenAsync(c.IdCotizacionOrigen, idCliente.Value);
                return Page();
            }

            try
            {
                // Datos de la salida elegida: naviera, nave, fecha de embarque y,
                // si va con documental, el corte documental.
                if (mar && salida != null)
                {
                    c.IdNaviera = salida.IdNaviera;
                    c.Nave = salida.NaveViaje;
                    c.FechaTentativaEmbarque = salida.Etd.ToDateTime(TimeOnly.MinValue);
                    if (doc) c.FechaCorteDocumental = salida.CorteDocumental;
                    c.NumeroBooking = null; // el booking lo asigna la naviera después
                }
                else if (mar)
                {
                    c.IdNaviera = null; c.Nave = null; c.NumeroBooking = null;
                    if (doc) c.FechaCorteDocumental = null;
                }
                if (mar) c.NumeroInstructivo = doc ? c.NumeroInstructivo?.Trim().ToUpper() : null;

                // Origen/Destino (texto) = país del puerto, mismo vocabulario que
                // ya usa la precarga de Create y el panel de notificaciones.
                c.Origen = mar ? pol?.Pais : null;
                c.Destino = mar ? pod?.Pais : null;

                // La cotización de origen tiene que ser del mismo cliente y estar
                // respondida; si no, se ignora el vínculo.
                if (c.IdCotizacionOrigen != null && await BuscarCotizacionOrigenAsync(c.IdCotizacionOrigen, idCliente.Value) == null)
                    c.IdCotizacionOrigen = null;

                // Campos del formulario anterior que ya no aplican a la reserva.
                c.TipoCertificado = null; c.RequiereSag = null; c.RequiereSernapesca = null;
                c.PaisDestinoFinal = null; c.RequiereSeguro = null; c.TipoCargaDocumental = null;
                c.Mercancia = string.IsNullOrWhiteSpace(c.Mercancia) ? null : c.Mercancia.Trim();
                c.Comentarios = string.IsNullOrWhiteSpace(c.Comentarios) ? null : c.Comentarios.Trim();

                c.TipoSolicitud = Cotizacion.TipoReserva;
                c.FechaSolicitud = DateTime.Now;
                c.Estado = "NUEVA SOLICITUD";
                c.Activo = true;
                c.IdCliente = idCliente.Value;
                if (plantaNueva != null) _context.Plantas.Add(plantaNueva);

                _context.Cotizaciones.Add(c);
                await _context.SaveChangesAsync();
                if (plantaNueva != null)
                {
                    AtilsonCargoSpa.Services.MaestrosAuditoria.RegistrarEvento(_context, AtilsonCargoSpa.Services.MaestrosAuditoria.EntPlanta, plantaNueva.Id,
                        "Registro creado", $"{User.Identity?.Name ?? "Cliente"} (portal)", $"Agregada como «Otra planta» en la solicitud de servicio #{c.Id}; por validar");
                    await _context.SaveChangesAsync();
                }

                return RedirectToPage("./SolicitarReserva", new { exito = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error guardando solicitud de reserva del cliente {IdCliente}", idCliente);
                ModelState.AddModelError(string.Empty, "No pudimos guardar tu solicitud. Intenta de nuevo en unos minutos.");
                CotizacionOrigen = await BuscarCotizacionOrigenAsync(c.IdCotizacionOrigen, idCliente.Value);
                return Page();
            }
        }
    }

    public class SalidasSelectorVm
    {
        public List<ItinerarioNave> Salidas { get; set; } = new();
        public int? Seleccionada { get; set; }
        public bool RutaElegida { get; set; }
    }
}
