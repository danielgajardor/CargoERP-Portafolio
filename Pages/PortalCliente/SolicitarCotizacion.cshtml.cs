using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.PortalCliente
{
    // Solicitud de Cotización real (oct-2026, minuta Portal Web de Clientes):
    // el cliente todavía no tiene tarifa y quiere saber cuánto cuesta. Pide solo
    // lo mínimo por tramo — Marítimo: POL/POD + tipo de contenedor; Terrestre:
    // zona de planta + zona de puerto/depósito; Documental: tipo de carga — y el
    // ejecutivo responde los montos desde Operaciones/Cotizaciones.
    // Se guarda en la misma tabla Cotizaciones que SolicitarReserva, con
    // TipoSolicitud = COTIZACION.
    public class SolicitarCotizacionModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly ILogger<SolicitarCotizacionModel> _logger;

        public SolicitarCotizacionModel(AtilsonContext context, ILogger<SolicitarCotizacionModel> logger)
        {
            _context = context;
            _logger = logger;
        }

        [BindProperty]
        public Cotizacion NuevaCotizacion { get; set; } = new Cotizacion();

        // Precarga desde los atajos de rutas del Dashboard del portal
        // (?pol=SAN ANTONIO&pod=CALLAO): se busca el puerto por nombre.
        [BindProperty(SupportsGet = true)]
        public string? Pol { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Pod { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool Exito { get; set; }

        public List<Puerto> Puertos { get; set; } = new();
        public List<string> ZonasPlanta { get; set; } = new();
        public List<string> ZonasPuerto { get; set; } = new();

        // Transporte (oct-2026): plantas del cliente + "Otra planta", y el puerto del tramo.
        public List<Planta> Plantas { get; set; } = new();
        public List<Puerto> PuertosChile { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Zonas { get; set; } = new();

        [BindProperty]
        public string? OtraPlantaNombre { get; set; }

        [BindProperty]
        public int? OtraPlantaIdCiudad { get; set; }

        [BindProperty]
        public bool GuardarOtraPlanta { get; set; } = true;

        public static readonly string[] TiposCargaDocumental =
        {
            "FRUTA FRESCA", "FRUTA CONGELADA", "FRUTA DESHIDRATADA", "PRODUCTOS DEL MAR"
        };

        public static readonly string[] TiposContenedor =
        {
            "20' DRY STD", "40' DRY STD", "40' DRY HC", "20' REEFER", "40' REEFER", "PALLETS"
        };

        private int? ObtenerIdClienteSesion()
        {
            var claimCliente = User.FindFirst("IdCliente")?.Value;
            return int.TryParse(claimCliente, out int idCliente) ? idCliente : null;
        }

        // Catálogos reales de la base, no texto libre:
        //  - Puertos: tabla puertos activa. Hay nombres repetidos (ej. dos
        //    "SAN ANTONIO / CHILE" activos), así que se deja uno por nombre+país
        //    (el de menor Id) para no confundir al cliente.
        //  - Zona de planta: ciudades de las plantas activas (Buin, Paine...).
        //  - Zona de puerto/depósito: nombres de los puertos chilenos activos —
        //    los depósitos no tienen ciudad cargada en su mayoría, y las zonas
        //    que pide la minuta (Valparaíso, San Antonio, Coronel...) son
        //    justamente las ciudades puerto.
        private async Task CargarCatalogosAsync()
        {
            Puertos = await PuertosHelper.ListarUnicosAsync(_context);

            ZonasPuerto = Puertos
                .Where(p => (p.Pais ?? "").Trim().ToUpper() == "CHILE")
                .Select(p => p.NombrePuerto.Trim().ToUpper())
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            var idCliente = ObtenerIdClienteSesion();
            Plantas = idCliente == null ? new List<Planta>() : await PortalUbicacionesHelper.PlantasDelClienteAsync(_context, idCliente.Value);
            PuertosChile = Puertos.Where(PuertosHelper.EsChileno).ToList();
            Zonas = await PortalUbicacionesHelper.ZonasAsync(_context, OtraPlantaIdCiudad);
        }

        // "EXP MAR+TER", "IMP INTEGRAL (MAR+TER+DOC)", etc. — mismo formato que
        // SolicitarReserva para que el panel de Operaciones los muestre igual.
        public static bool IncluyeModulo(string? tipoServicio, string modulo)
        {
            if (string.IsNullOrWhiteSpace(tipoServicio)) return false;
            var t = tipoServicio.ToUpperInvariant();
            return t.Contains("INTEGRAL") || t.Contains(modulo);
        }

        private int? BuscarPuertoPorNombre(string? nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return null;
            var n = nombre.Split(',')[0].Trim().ToUpper();
            return Puertos.FirstOrDefault(p => p.NombrePuerto.Trim().ToUpper() == n)?.Id;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (ObtenerIdClienteSesion() == null)
                return RedirectToPage("/Login");

            await CargarCatalogosAsync();

            NuevaCotizacion.IdPuertoOrigen = BuscarPuertoPorNombre(Pol);
            NuevaCotizacion.IdPuertoDestino = BuscarPuertoPorNombre(Pod);
            if (NuevaCotizacion.IdPuertoOrigen != null || NuevaCotizacion.IdPuertoDestino != null)
                NuevaCotizacion.TipoServicio = "EXP MAR";

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var idCliente = ObtenerIdClienteSesion();
            if (idCliente == null)
                return RedirectToPage("/Login");

            await CargarCatalogosAsync();

            var c = NuevaCotizacion;
            bool mar = IncluyeModulo(c.TipoServicio, "MAR");
            bool ter = IncluyeModulo(c.TipoServicio, "TER");
            bool doc = IncluyeModulo(c.TipoServicio, "DOC");

            // TipoServicio es string no-nullable: el binder ya agrega su propio
            // "is required" en inglés; se reemplaza por el mensaje de abajo.
            ModelState.Remove("NuevaCotizacion.TipoServicio");
            if (!mar && !ter && !doc)
                ModelState.AddModelError(string.Empty, "Selecciona al menos un servicio.");

            // Solo se guarda lo que corresponde a los tramos elegidos: si el
            // cliente llenó un tramo y después lo desmarcó, eso no se guarda.
            if (mar)
            {
                if (c.IdPuertoOrigen == null) ModelState.AddModelError(string.Empty, "Selecciona el puerto de origen.");
                if (c.IdPuertoDestino == null) ModelState.AddModelError(string.Empty, "Selecciona el puerto de destino.");
                if (c.IdPuertoOrigen != null && c.IdPuertoOrigen == c.IdPuertoDestino)
                    ModelState.AddModelError(string.Empty, "El puerto de origen y el de destino no pueden ser el mismo.");
                if (string.IsNullOrWhiteSpace(c.TipoContenedor) || !TiposContenedor.Contains(c.TipoContenedor))
                    ModelState.AddModelError(string.Empty, "Selecciona el tipo de contenedor.");
                if (c.IdPuertoOrigen != null && Puertos.All(p => p.Id != c.IdPuertoOrigen))
                    ModelState.AddModelError(string.Empty, "El puerto de origen no es válido.");
                if (c.IdPuertoDestino != null && Puertos.All(p => p.Id != c.IdPuertoDestino))
                    ModelState.AddModelError(string.Empty, "El puerto de destino no es válido.");
            }
            else
            {
                c.IdPuertoOrigen = null;
                c.IdPuertoDestino = null;
                c.TipoContenedor = null;
            }

            Planta? plantaNueva = null;
            if (ter)
            {
                // Planta: una de las del cliente u "Otra planta" con nombre y zona.
                var elegida = await PortalUbicacionesHelper.ResolverPlantaAsync(_context, idCliente.Value, Plantas,
                    c.PlantaCarga, OtraPlantaNombre, OtraPlantaIdCiudad, GuardarOtraPlanta, User.Identity?.Name ?? "Cliente");
                if (elegida.Error != null) ModelState.AddModelError(string.Empty, elegida.Error);
                c.PlantaCarga = elegida.Nombre ?? c.PlantaCarga;
                c.ZonaCarga = elegida.Zona;
                plantaNueva = elegida.Nueva;

                // Puerto del tramo: con Marítimo es el POL (exportación) o el POD
                // (importación); sin Marítimo lo elige el cliente. La antigua "zona de
                // puerto / depósito" se eliminó.
                bool exp = !(c.TipoServicio ?? "").TrimStart().StartsWith("IMP", StringComparison.OrdinalIgnoreCase);
                if (mar)
                    c.IdPuertoTerrestre = exp ? c.IdPuertoOrigen : c.IdPuertoDestino;
                else if (c.IdPuertoTerrestre == null || PuertosChile.All(p => p.Id != c.IdPuertoTerrestre))
                    ModelState.AddModelError(string.Empty, exp ? "Selecciona el puerto de salida." : "Selecciona el puerto de llegada.");
                c.ZonaEntrega = null;
            }
            else
            {
                c.ZonaCarga = null;
                c.ZonaEntrega = null;
                c.PlantaCarga = null;
                c.IdPuertoTerrestre = null;
            }

            if (doc)
            {
                if (string.IsNullOrWhiteSpace(c.TipoCargaDocumental) || !TiposCargaDocumental.Contains(c.TipoCargaDocumental))
                    ModelState.AddModelError(string.Empty, "Selecciona el tipo de carga para el servicio documental.");
            }
            else
            {
                c.TipoCargaDocumental = null;
            }

            if (!ModelState.IsValid)
                return Page();

            try
            {
                // Campos de la Solicitud de Reserva que no aplican a una cotización
                // (la minuta pide sacarlos de este formulario). Se limpian por si
                // llegan posteados a mano.
                c.CantidadContenedores = null;
                c.DepositoRetiro = null;
                c.FechaCarga = null;
                c.FechaTentativaEmbarque = null;
                c.RequiereSeguro = null;
                c.TipoCertificado = null;
                c.PaisDestinoFinal = null;
                c.RequiereSag = null;
                c.RequiereSernapesca = null;
                c.CondicionReefer = null;
                c.Temperatura = null;
                c.Humedad = null;
                c.Ventilacion = null;
                c.Mercancia = string.IsNullOrWhiteSpace(c.Mercancia) ? null : c.Mercancia.Trim();
                c.Comentarios = string.IsNullOrWhiteSpace(c.Comentarios) ? null : c.Comentarios.Trim();
                // El puerto del tramo terrestre ahora lo indica el cliente (o sale del POL/POD):
                // se conserva al limpiar los campos que llena Comercial al responder.
                var puertoTerrestre = c.IdPuertoTerrestre;
                c.LimpiarCamposRespuesta();
                c.IdPuertoTerrestre = puertoTerrestre;

                // Origen/Destino (texto) se siguen llenando con el país del
                // puerto: el panel de notificaciones de Operaciones los usa como
                // subtítulo y así no hay que tocar esa lógica.
                if (mar)
                {
                    c.Origen = Puertos.First(p => p.Id == c.IdPuertoOrigen).Pais;
                    c.Destino = Puertos.First(p => p.Id == c.IdPuertoDestino).Pais;
                }
                else
                {
                    c.Origen = null;
                    c.Destino = null;
                }

                c.TipoSolicitud = Cotizacion.TipoCotizacion;
                c.FechaSolicitud = DateTime.Now;
                c.Estado = "NUEVA SOLICITUD";
                c.Activo = true;
                c.IdCliente = idCliente.Value;

                _context.Cotizaciones.Add(c);
                if (plantaNueva != null) _context.Plantas.Add(plantaNueva);
                await _context.SaveChangesAsync();
                if (plantaNueva != null)
                {
                    AtilsonCargoSpa.Services.MaestrosAuditoria.RegistrarEvento(_context, AtilsonCargoSpa.Services.MaestrosAuditoria.EntPlanta, plantaNueva.Id,
                        "Registro creado", $"{User.Identity?.Name ?? "Cliente"} (portal)", $"Agregada como «Otra planta» en la cotización #{c.Id}; por validar");
                    await _context.SaveChangesAsync();
                }

                return RedirectToPage("./SolicitarCotizacion", new { exito = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error guardando solicitud de cotización del cliente {IdCliente}", idCliente);
                ModelState.AddModelError(string.Empty, "No pudimos guardar tu solicitud. Intenta de nuevo en unos minutos.");
                return Page();
            }
        }
    }
}
