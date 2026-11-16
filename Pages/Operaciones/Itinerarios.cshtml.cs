using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Operaciones
{
    // Carga manual de itinerarios de naves (oct-2026, minuta Portal Web de
    // Clientes): mientras no haya API de navieras, las ejecutivas ingresan aquí
    // las salidas (POL, POD, naviera, nave, ETD, ETA) de las próximas 4-5
    // semanas. Es lo que el cliente ve como tarjetas en PortalCliente/Itinerarios
    // y lo que elige como nave al solicitar una reserva.
    public class ItinerariosModel : PageModel
    {
        private readonly AtilsonContext _context;
        private readonly EmailService _email;

        public ItinerariosModel(AtilsonContext context, EmailService email)
        {
            _context = context;
            _email = email;
        }

        [BindProperty(SupportsGet = true)] public int? Pol { get; set; }
        [BindProperty(SupportsGet = true)] public int? Pod { get; set; }
        [BindProperty(SupportsGet = true)] public int? Naviera { get; set; }
        [BindProperty(SupportsGet = true)] public bool VerPasadas { get; set; }
        [BindProperty(SupportsGet = true)] public int? Editar { get; set; }
        public MaestroAuditoriaVm? Registro { get; set; }
        [BindProperty(SupportsGet = true)] public int? Copiar { get; set; }
        [BindProperty(SupportsGet = true)] public string? Aviso { get; set; }

        public List<ItinerarioNave> Salidas { get; set; } = new();
        public List<Naviera> Navieras { get; set; } = new();
        public List<Puerto> Puertos { get; set; } = new();
        public List<string> NavesSugeridas { get; set; } = new();
        public Dictionary<int, int> ReservasPorSalida { get; set; } = new();
        // 13-nov: último registro de Zarpe / Arribo efectivo por salida ("id|Zarpe efectivo" → quién y cuándo).
        public Dictionary<string, HistorialCambiosComercial> UltimaFechaReal { get; set; } = new();
        public bool FormularioAbierto { get; set; }

        public static readonly Dictionary<string, (string Tipo, string Texto)> Avisos = new()
        {
            ["creada"] = ("success", "Salida agregada. Si su ETD está dentro de las próximas 5 semanas, los clientes ya la ven en el portal."),
            ["actualizada"] = ("success", "Salida actualizada."),
            ["eliminada"] = ("success", "Salida eliminada: ya no se muestra a los clientes."),
            ["err_naviera"] = ("danger", "Selecciona la naviera."),
            ["err_nave"] = ("danger", "Ingresa el nombre de la nave."),
            ["err_viaje"] = ("danger", "Ingresa el número de viaje: identifica la salida y es lo que se elige en Operaciones."),
            ["err_duplicada"] = ("danger", "Esa nave y viaje ya están cargados para la misma naviera y ruta. Edita la salida existente en vez de crear otra."),
            ["zarpe"] = ("success", "Zarpe efectivo registrado."),
            ["zarpe_borrado"] = ("success", "Se quitó el zarpe efectivo de la salida."),
            ["arribo"] = ("success", "Arribo efectivo registrado."),
            ["arribo_borrado"] = ("success", "Se quitó el arribo efectivo de la salida."),
            ["err_real_futuro"] = ("danger", "El zarpe y el arribo efectivos no pueden ser fechas futuras."),
            ["err_arribo_antes"] = ("danger", "El arribo efectivo no puede ser anterior al zarpe efectivo."),
            ["err_puertos"] = ("danger", "Selecciona puerto de origen y de destino distintos."),
            ["err_mismo_pais"] = ("danger", "El puerto de origen (POL) y el de destino (POD) son del mismo país: el destino debe ser de otro país."),
            ["err_fechas"] = ("danger", "Revisa las fechas: ETD y ETA son obligatorias y la ETA no puede ser anterior a la ETD."),
            ["err_noexiste"] = ("danger", "No se encontró la salida.")
        };

        public class SalidaInput
        {
            public int? Id { get; set; }
            public int? IdNaviera { get; set; }
            public string? Nave { get; set; }
            public string? NumeroViaje { get; set; }
            public int? IdPuertoOrigen { get; set; }
            public int? IdPuertoDestino { get; set; }
            public string? Etd { get; set; }              // yyyy-MM-ddTHH:mm (o yyyy-MM-dd)
            public string? Eta { get; set; }              // yyyy-MM-ddTHH:mm (o yyyy-MM-dd)
            public string? CorteDocumental { get; set; }  // yyyy-MM-ddTHH:mm
            public string? Transbordo { get; set; }
            public string? Observaciones { get; set; }
            // Stacking (13-nov-2026), opcional: apertura y cierre para Dry y para Reefer.
            public string? StackingInicioDry { get; set; }
            public string? StackingFinDry { get; set; }
            public string? StackingInicioReefer { get; set; }
            public string? StackingFinReefer { get; set; }
        }

        [BindProperty]
        public SalidaInput Input { get; set; } = new();

        // Lectura de fechas: misma implementación que el guardado compartido (ItinerarioServicio).
        private static DateOnly? LeerFecha(string? s) => ItinerarioServicio.LeerFecha(s);

        // ETD / ETA llegan de un datetime-local ("yyyy-MM-ddTHH:mm"); se acepta también solo fecha.
        private static (DateOnly? Fecha, TimeOnly? Hora) LeerFechaYHora(string? s) => ItinerarioServicio.LeerFechaYHora(s);

        private static string FormatoFechaHora(DateOnly fecha, TimeOnly? hora) =>
            hora == null
                ? fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00"
                : fecha.ToDateTime(hora.Value).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

        private static DateTime? LeerFechaHora(string? s) => ItinerarioServicio.LeerFechaHora(s);

        private async Task CargarAsync()
        {
            Navieras = await _context.Navieras.AsNoTracking()
                .Where(n => n.Activo == 1 && !n.IsDeleted)
                .OrderBy(n => n.NombreNaviera)
                .ToListAsync();
            Puertos = await PuertosHelper.ListarUnicosAsync(_context);

            // Sugerencias para el campo Nave: las ya cargadas aquí + las de
            // operaciones recientes (mismo texto que usa operaciones.Nave).
            var navesItinerario = await _context.ItinerariosNaves.AsNoTracking()
                .Select(i => i.Nave).Distinct().Take(300).ToListAsync();
            var desdeOps = DateTime.Today.AddMonths(-6);
            var navesOps = await _context.Operaciones.AsNoTracking()
                .Where(o => o.Nave != null && o.Nave != "" && o.FechaCreacion >= desdeOps)
                .Select(o => o.Nave!).Distinct().Take(300).ToListAsync();
            NavesSugeridas = navesItinerario.Concat(navesOps)
                .Select(n => n.Trim().ToUpper())
                .Where(n => n.Length > 1)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            var hoy = ItinerariosHelper.Hoy;
            var q = _context.ItinerariosNaves.AsNoTracking()
                .Include(i => i.IdNavieraNavigation)
                .Include(i => i.IdPuertoOrigenNavigation)
                .Include(i => i.IdPuertoDestinoNavigation)
                .Where(i => i.Activo);
            q = VerPasadas ? q.Where(i => i.Etd >= hoy.AddDays(-45)) : q.Where(i => i.Etd >= hoy);
            if (Pol != null)
            {
                var ids = await PuertosHelper.IdsEquivalentesAsync(_context, Pol.Value);
                q = q.Where(i => ids.Contains(i.IdPuertoOrigen));
            }
            if (Pod != null)
            {
                var ids = await PuertosHelper.IdsEquivalentesAsync(_context, Pod.Value);
                q = q.Where(i => ids.Contains(i.IdPuertoDestino));
            }
            if (Naviera != null) q = q.Where(i => i.IdNaviera == Naviera);

            Salidas = await q.OrderBy(i => i.Etd).ThenBy(i => i.IdPuertoOrigen).Take(500).ToListAsync();

            var idsSalidas = Salidas.Select(s => s.Id).ToList();
            ReservasPorSalida = await _context.Cotizaciones.AsNoTracking()
                .Where(c => c.IdItinerario != null && idsSalidas.Contains(c.IdItinerario.Value) && c.Activo == true)
                .GroupBy(c => c.IdItinerario!.Value)
                .Select(g => new { g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.N);

            string entidadIt = MaestrosAuditoria.Prefijo + MaestrosAuditoria.EntItinerario;
            var cambiosReales = await _context.HistorialCambiosComercial.AsNoTracking()
                .Where(h => h.TipoEntidad == entidadIt && idsSalidas.Contains(h.IdRegistro) && (h.Campo == "Zarpe efectivo" || h.Campo == "Arribo efectivo"))
                .ToListAsync();
            UltimaFechaReal = cambiosReales.GroupBy(h => h.IdRegistro + "|" + h.Campo)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.FechaCambio).ThenByDescending(h => h.Id).First());
        }

        // ---------------------------------------------------------------------
        // 13-nov: historial de una salida (quién la creó, cada cambio de fechas, zarpe y arribo).
        public async Task<IActionResult> OnGetHistorialAsync(int id)
        {
            var it = await _context.ItinerariosNaves.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
            if (it == null) return NotFound();
            var vm = new MaestroAuditoriaVm
            {
                CreadoPor = it.UsuarioCreador,
                CreadoEl = it.FechaCreacion,
                ModificadoPor = it.UsuarioModificador,
                ModificadoEl = it.FechaModificacion,
                Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntItinerario, it.Id, 200)
            };
            return Partial("/Pages/App/_MaestroRegistro.cshtml", vm);
        }

        // 13-nov: clientes que recibirán la confirmación de zarpe / arribo (se muestran antes de enviar).
        // Por salida (Itinerarios) o por booking (Mainboard: su salida, o solo él si no tiene).
        public async Task<IActionResult> OnGetDestinatariosAsync(int? idSalida, int? idOperacion, string? tipo)
        {
            bool esZarpe = !string.Equals(tipo, "arribo", StringComparison.OrdinalIgnoreCase);
            ItinerarioNave? it = null;
            List<Operacione> ops;
            if (idSalida != null)
                it = await _context.ItinerariosNaves.AsNoTracking().FirstOrDefaultAsync(i => i.Id == idSalida && i.Activo);
            else if (idOperacion != null)
            {
                var op = await _context.Operaciones.AsNoTracking().FirstOrDefaultAsync(o => o.Id == idOperacion && !o.IsDeleted);
                if (op != null) it = await CicloNave.SalidaDeAsync(_context, op);
            }
            if (it != null)
                ops = (await CicloNave.BookingsDeSalidaAsync(_context, it, idOperacion)).Where(o => CicloNave.RecibeFechaDeSalida(o, it, esZarpe, idOperacion)).ToList();
            else
                ops = await _context.Operaciones.AsNoTracking().Include(o => o.IdClienteNavigation)
                    .Where(o => o.Id == idOperacion && !o.IsDeleted).ToListAsync();
            var lista = ops.Where(CicloNave.TieneMaritimo).Select(o => new
            {
                booking = o.NumeroBooking,
                cliente = o.IdClienteNavigation?.RazonSocial ?? "Sin cliente",
                correo = o.IdClienteNavigation?.Correo
            }).ToList();
            return new JsonResult(new { salida = it?.NaveViaje, lista });
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await CargarAsync();

            int? origen = Editar ?? Copiar;
            if (origen != null)
            {
                var it = await _context.ItinerariosNaves.AsNoTracking().FirstOrDefaultAsync(i => i.Id == origen && i.Activo);
                if (it == null)
                    return RedirectToPage(new { aviso = "err_noexiste" });

                // Copiar = misma naviera/ruta/transbordo una semana después; la nave
                // y el viaje se dejan en blanco porque en un servicio semanal cada
                // semana zarpa una nave distinta.
                int dias = Copiar != null ? 7 : 0;
                Input = new SalidaInput
                {
                    Id = Editar != null ? it.Id : null,
                    IdNaviera = it.IdNaviera,
                    Nave = Copiar != null ? null : it.Nave,
                    NumeroViaje = Copiar != null ? null : it.NumeroViaje,
                    IdPuertoOrigen = it.IdPuertoOrigen,
                    IdPuertoDestino = it.IdPuertoDestino,
                    Etd = FormatoFechaHora(it.Etd.AddDays(dias), it.HoraEtd),
                    Eta = FormatoFechaHora(it.Eta.AddDays(dias), it.HoraEta),
                    CorteDocumental = it.CorteDocumental?.AddDays(dias).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                    Transbordo = it.Transbordo,
                    Observaciones = it.Observaciones,
                    StackingInicioDry = it.StackingInicioDry?.AddDays(dias).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                    StackingFinDry = it.StackingFinDry?.AddDays(dias).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                    StackingInicioReefer = it.StackingInicioReefer?.AddDays(dias).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                    StackingFinReefer = it.StackingFinReefer?.AddDays(dias).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)
                };
                FormularioAbierto = true;
                if (Editar != null)
                {
                    // Registro de cambios de la salida (13-nov-2026): quién la creó y cada cambio.
                    var ultimo = await _context.HistorialCambiosComercial.AsNoTracking()
                        .Where(h => h.TipoEntidad == MaestrosAuditoria.Prefijo + MaestrosAuditoria.EntItinerario && h.IdRegistro == it.Id)
                        .OrderByDescending(h => h.FechaCambio).FirstOrDefaultAsync();
                    Registro = new MaestroAuditoriaVm
                    {
                        CreadoPor = it.UsuarioCreador,
                        CreadoEl = it.FechaCreacion,
                        ModificadoPor = it.UsuarioModificador ?? ultimo?.Usuario,
                        ModificadoEl = it.FechaModificacion ?? ultimo?.FechaCambio,
                        Historial = await MaestrosAuditoria.HistorialAsync(_context, MaestrosAuditoria.EntItinerario, it.Id)
                    };
                }
            }
            else
            {
                // Atajo: si se está filtrando una ruta, el alta nueva parte con esa ruta.
                Input.IdPuertoOrigen = Pol;
                Input.IdPuertoDestino = Pod;
                Input.IdNaviera = Naviera;
            }
            return Page();
        }

        public async Task<IActionResult> OnPostGuardarAsync()
        {
            object Volver(string aviso, int? editar = null) => new { aviso, pol = Pol, pod = Pod, naviera = Naviera, verPasadas = VerPasadas ? true : (bool?)null, editar };

            var res = await ItinerarioServicio.GuardarAsync(_context, DatosDe(Input), User.Identity?.Name);
            if (!res.Ok)
                return RedirectToPage(Volver(res.Codigo, res.Codigo == "err_noexiste" ? null : Input.Id));
            if (res.BookingsCorregidos > 0)
                TempData["AvisoItinerario"] = $"Salida actualizada. La nave / viaje se corrigió también en {res.BookingsCorregidos} booking(s) que la tenían elegida.";
            else if (res.BookingsConFechas > 0)
                TempData["AvisoItinerario"] = $"Salida {(res.Codigo == "creada" ? "agregada" : "actualizada")}. Las fechas que cambiaron (ETD, ETA, corte o stacking) se actualizaron en {res.BookingsConFechas} booking(s) de esta nave.";
            else if (res.NaveCreada)
                TempData["AvisoItinerario"] = $"Salida {(res.Codigo == "creada" ? "agregada" : "actualizada")}. La nave {res.Salida!.Nave} quedó también en el catálogo de naves.";
            return RedirectToPage(Volver(res.Codigo));
        }

        private static ItinerarioServicio.Datos DatosDe(SalidaInput r) => new()
        {
            Id = r.Id, IdNaviera = r.IdNaviera, Nave = r.Nave, NumeroViaje = r.NumeroViaje,
            IdPuertoOrigen = r.IdPuertoOrigen, IdPuertoDestino = r.IdPuertoDestino,
            Etd = r.Etd, Eta = r.Eta, CorteDocumental = r.CorteDocumental,
            Transbordo = r.Transbordo, Observaciones = r.Observaciones,
            StackingInicioDry = r.StackingInicioDry, StackingFinDry = r.StackingFinDry,
            StackingInicioReefer = r.StackingInicioReefer, StackingFinReefer = r.StackingFinReefer,
            TraeStacking = true,
        };

        // ---------------------------------------------------------------------
        // "Agregar salida" desde el buscador de Create (nov-2026): mismo guardado, respuesta JSON
        // con la salida en el formato de los resultados, para seleccionarla al tiro.
        public async Task<IActionResult> OnPostGuardarJsonAsync()
        {
            Input.Id = null; // desde Create solo se agregan salidas nuevas
            var res = await ItinerarioServicio.GuardarAsync(_context, DatosDe(Input), User.Identity?.Name);
            if (!res.Ok)
                return new JsonResult(new { success = false, message = Avisos.TryGetValue(res.Codigo, out var a) ? a.Texto : "No se pudo guardar la salida." });
            var salida = await ConsultaSalidas().FirstAsync(x => x.Id == res.Salida!.Id);
            return new JsonResult(new { success = true, naveCreada = res.NaveCreada, salida = SalidaJson(salida) });
        }

        // ---------------------------------------------------------------------
        // Catálogo de naves (nov-2026): lista para el selector de "Agregar salida" (formato select2).
        // Primero las naves de la naviera elegida y después las demás: una salida puede venderla
        // una naviera distinta de la que opera el buque.
        public async Task<IActionResult> OnGetCatalogoNavesAsync(string? q, int? naviera)
        {
            string texto = NavesCatalogoHelper.Normalizar(q);
            var lista = await _context.Naves.AsNoTracking()
                .Where(n => !n.IsDeleted && (texto == "" || n.Nombre.Contains(texto)))
                .OrderBy(n => n.Nombre)
                .Select(n => new
                {
                    n.Nombre,
                    n.IdNaviera,
                    Naviera = n.IdNavieraNavigation != null ? n.IdNavieraNavigation.NombreNaviera : null,
                    Color = n.IdNavieraNavigation != null ? n.IdNavieraNavigation.ColorRepresentativo : null
                })
                .Take(200)
                .ToListAsync();
            string? nombreNaviera = naviera == null ? null : await _context.Navieras.Where(n => n.Id == naviera).Select(n => n.NombreNaviera).FirstOrDefaultAsync();
            // Cada opción lleva su naviera y el color del maestro (se muestran a la derecha).
            object Opcion(string nombre, string? nav, string? color) => new
            {
                id = nombre,
                text = nombre,
                naviera = nav ?? "",
                color = string.IsNullOrEmpty(nav) ? "#94a3b8" : ItinerariosHelper.Color(new Naviera { NombreNaviera = nav, ColorRepresentativo = color })
            };
            var propias = lista.Where(n => naviera != null && n.IdNaviera == naviera).Select(n => Opcion(n.Nombre, n.Naviera, n.Color)).Take(40).ToList();
            var otras = lista.Where(n => naviera == null || n.IdNaviera != naviera)
                .OrderBy(n => n.Naviera ?? "~").ThenBy(n => n.Nombre)
                .Select(n => Opcion(n.Nombre, n.Naviera, n.Color)).Take(40).ToList();
            var grupos = new List<object>();
            if (propias.Count > 0) grupos.Add(new { text = "Naves de " + (nombreNaviera ?? "la naviera"), children = propias });
            if (otras.Count > 0) grupos.Add(new { text = naviera == null ? "Naves" : "Otras navieras", children = otras });
            return new JsonResult(new { results = grupos });
        }

        // Crear una nave nueva en el catálogo. Si hay nombres muy parecidos, primero los muestra.
        public async Task<IActionResult> OnPostCrearNaveAsync(string? nombre, int? idNaviera, bool confirmar = false)
        {
            string n = NavesCatalogoHelper.Normalizar(nombre);
            if (n.Length < 2) return new JsonResult(new { success = false, message = "Escribe el nombre de la nave." });
            if (idNaviera == null || !await _context.Navieras.AnyAsync(x => x.Id == idNaviera))
                return new JsonResult(new { success = false, message = "Elige primero la naviera." });
            var catalogo = await NavesCatalogoHelper.ActivasAsync(_context);
            var existente = NavesCatalogoHelper.Existente(catalogo, n, idNaviera);
            if (existente != null) return new JsonResult(new { success = true, nombre = existente.Nombre, existia = true });
            if (!confirmar)
            {
                var parecidas = NavesCatalogoHelper.Parecidas(catalogo, n);
                if (parecidas.Count > 0)
                    return new JsonResult(new { success = false, parecidas = parecidas.Select(x => x.Nombre).Distinct().ToList() });
            }
            NavesCatalogoHelper.Asegurar(_context, catalogo, n, idNaviera.Value, User.Identity?.Name);
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true, nombre = n });
        }

        // ---------------------------------------------------------------------
        // Buscador de salidas de Create (nov-2026): POL, POD y fecha de inicio, en ventanas de
        // 6 semanas ("ver más" pide la siguiente). Incluye puertos equivalentes (mismo puerto
        // registrado dos veces) y todas las navieras; las salidas zarpadas vienen marcadas.
        public const int SemanasBusqueda = 6;

        public async Task<IActionResult> OnGetBuscarSalidasAsync(int? pol, int? pod, string? desde, int? semanas)
        {
            if (pol == null || pod == null)
                return new JsonResult(new { success = false, message = "Elige el Puerto de Origen (POL) y el Puerto de Destino (POD)." });
            var inicio = LeerFecha(desde) ?? ItinerariosHelper.Hoy;
            int sem = Math.Clamp(semanas ?? SemanasBusqueda, 1, 26);
            var fin = inicio.AddDays(sem * 7);
            var idsPol = await PuertosHelper.IdsEquivalentesAsync(_context, pol.Value);
            var idsPod = await PuertosHelper.IdsEquivalentesAsync(_context, pod.Value);
            var lista = await ConsultaSalidas()
                .Where(x => idsPol.Contains(x.IdPuertoOrigen) && idsPod.Contains(x.IdPuertoDestino) && x.Etd >= inicio && x.Etd < fin)
                .OrderBy(x => x.Etd).ThenBy(x => x.HoraEtd).ThenBy(x => x.Naviera)
                .Take(200)
                .ToListAsync();
            // ¿Hay más salidas después de esta ventana? (para mostrar u ocultar "ver más")
            bool hayMas = await _context.ItinerariosNaves.AsNoTracking()
                .AnyAsync(i => i.Activo && idsPol.Contains(i.IdPuertoOrigen) && idsPod.Contains(i.IdPuertoDestino) && i.Etd >= fin);
            return new JsonResult(new
            {
                success = true,
                desde = inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                hasta = fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                hayMas,
                salidas = lista.Select(SalidaJson).ToList()
            });
        }

        // Una salida por Id (borrador, copia o reserva precargada que ya la traen elegida).
        public async Task<IActionResult> OnGetSalidaAsync(int id)
        {
            var x = await ConsultaSalidas(incluirInactivas: true).FirstOrDefaultAsync(i => i.Id == id);
            return x == null ? new JsonResult(new { success = false }) : new JsonResult(new { success = true, salida = SalidaJson(x) });
        }

        // Una salida por texto "NAVE / VIAJE" (PDF del booking, copia o cotización sin Id).
        public async Task<IActionResult> OnGetSalidaPorTextoAsync(string? nave, int? naviera)
        {
            int? id = await ItinerariosHelper.ResolverSalidaAsync(_context, null, nave, naviera);
            if (id == null) return new JsonResult(new { success = false });
            var x = await ConsultaSalidas().FirstAsync(i => i.Id == id);
            return new JsonResult(new { success = true, salida = SalidaJson(x) });
        }

        private sealed class FilaSalida
        {
            public int Id { get; set; }
            public int IdNaviera { get; set; }
            public string Naviera { get; set; } = "";
            public string Nave { get; set; } = "";
            public string? NumeroViaje { get; set; }
            public int IdPuertoOrigen { get; set; }
            public int IdPuertoDestino { get; set; }
            public string Pol { get; set; } = "";
            public string Pod { get; set; } = "";
            public DateOnly Etd { get; set; }
            public TimeOnly? HoraEtd { get; set; }
            public DateOnly Eta { get; set; }
            public TimeOnly? HoraEta { get; set; }
            public DateTime? CorteDocumental { get; set; }
            public string? Transbordo { get; set; }
            public string? Observaciones { get; set; }
            public DateTime? ZarpeEfectivo { get; set; }
            public DateTime? ArriboEfectivo { get; set; }
            public DateTime? StackingInicioDry { get; set; }
            public DateTime? StackingFinDry { get; set; }
            public DateTime? StackingInicioReefer { get; set; }
            public DateTime? StackingFinReefer { get; set; }
        }

        private IQueryable<FilaSalida> ConsultaSalidas(bool incluirInactivas = false) =>
            _context.ItinerariosNaves.AsNoTracking()
                .Where(i => incluirInactivas || i.Activo)
                .Select(i => new FilaSalida
                {
                    Id = i.Id, IdNaviera = i.IdNaviera,
                    Naviera = i.IdNavieraNavigation != null ? i.IdNavieraNavigation.NombreNaviera : "",
                    Nave = i.Nave, NumeroViaje = i.NumeroViaje,
                    IdPuertoOrigen = i.IdPuertoOrigen, IdPuertoDestino = i.IdPuertoDestino,
                    Pol = i.IdPuertoOrigenNavigation != null ? i.IdPuertoOrigenNavigation.NombrePuerto : "",
                    Pod = i.IdPuertoDestinoNavigation != null ? i.IdPuertoDestinoNavigation.NombrePuerto : "",
                    Etd = i.Etd, HoraEtd = i.HoraEtd, Eta = i.Eta, HoraEta = i.HoraEta,
                    CorteDocumental = i.CorteDocumental, Transbordo = i.Transbordo, Observaciones = i.Observaciones,
                    ZarpeEfectivo = i.ZarpeEfectivo,
                    ArriboEfectivo = i.ArriboEfectivo,
                    StackingInicioDry = i.StackingInicioDry, StackingFinDry = i.StackingFinDry,
                    StackingInicioReefer = i.StackingInicioReefer, StackingFinReefer = i.StackingFinReefer,
                });

        private static object SalidaJson(FilaSalida x)
        {
            var inv = CultureInfo.InvariantCulture;
            string naveViaje = string.IsNullOrWhiteSpace(x.NumeroViaje) ? x.Nave : $"{x.Nave} / {x.NumeroViaje}";
            return new
            {
                id = x.Id,
                idNaviera = x.IdNaviera,
                naviera = x.Naviera,
                nave = naveViaje,
                naveNombre = x.Nave,
                viaje = x.NumeroViaje,
                idPol = x.IdPuertoOrigen,
                idPod = x.IdPuertoDestino,
                pol = x.Pol,
                pod = x.Pod,
                etd = x.Etd.ToString("yyyy-MM-dd", inv),
                etdHora = x.HoraEtd?.ToString("HH:mm", inv),
                eta = x.Eta.ToString("yyyy-MM-dd", inv),
                etaHora = x.HoraEta?.ToString("HH:mm", inv),
                transito = x.Eta.DayNumber - x.Etd.DayNumber,
                corte = x.CorteDocumental?.ToString("yyyy-MM-ddTHH:mm", inv),
                stkDryIni = x.StackingInicioDry?.ToString("yyyy-MM-ddTHH:mm", inv),
                stkDryFin = x.StackingFinDry?.ToString("yyyy-MM-ddTHH:mm", inv),
                stkRfIni = x.StackingInicioReefer?.ToString("yyyy-MM-ddTHH:mm", inv),
                stkRfFin = x.StackingFinReefer?.ToString("yyyy-MM-ddTHH:mm", inv),
                transbordo = x.Transbordo,
                observaciones = x.Observaciones,
                zarpada = x.ZarpeEfectivo != null,
                zarpe = x.ZarpeEfectivo?.ToString("yyyy-MM-ddTHH:mm", inv),
            };
        }

        public async Task<IActionResult> OnPostEliminarAsync(int idSalida)
        {
            var it = await _context.ItinerariosNaves.FirstOrDefaultAsync(i => i.Id == idSalida);
            if (it == null)
                return RedirectToPage(new { aviso = "err_noexiste" });

            // Baja lógica: las reservas que ya la eligieron siguen apuntando a ella.
            it.Activo = false;
            it.FechaModificacion = DateTime.Now;
            it.UsuarioModificador = User.Identity?.Name;
            MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntItinerario, it.Id, "Baja", User.Identity?.Name ?? "Sistema", it.NaveViaje);
            await _context.SaveChangesAsync();
            return RedirectToPage(new { aviso = "eliminada", pol = Pol, pod = Pod, naviera = Naviera });
        }

        // ---------------------------------------------------------------------
        // Zarpe efectivo (nov-2026): se registra después del zarpe, independiente de la ETD.
        // Se copia al "ETD Real (Zarpe)" de los bookings de esta salida que aún no lo tienen:
        // los que la eligieron en Nave / Viaje y, si son anteriores al maestro, los de la
        // misma naviera con el mismo texto "NAVE / VIAJE".
        // ---------------------------------------------------------------------
        public Task<IActionResult> OnPostZarpeAsync(int idSalida, string? zarpe, bool notificar = false) =>
            RegistrarFechaRealAsync(idSalida, zarpe, notificar, esZarpe: true);

        // Arribo efectivo (13-nov-2026, ciclo de la nave): igual que el zarpe, pero se copia al
        // "Arribo Efectivo" (EtaReal) de los bookings; sus contenedores pasan a "Arribado" y
        // aparece la alerta "Retiro pendiente" en el Mainboard.
        public Task<IActionResult> OnPostArriboAsync(int idSalida, string? arribo, bool notificar = false) =>
            RegistrarFechaRealAsync(idSalida, arribo, notificar, esZarpe: false);

        private async Task<IActionResult> RegistrarFechaRealAsync(int idSalida, string? texto, bool notificar, bool esZarpe)
        {
            object Volver(string aviso) => new { aviso, pol = Pol, pod = Pod, naviera = Naviera, verPasadas = VerPasadas ? true : (bool?)null };
            var it = await _context.ItinerariosNaves.FirstOrDefaultAsync(i => i.Id == idSalida && i.Activo);
            if (it == null)
                return RedirectToPage(Volver("err_noexiste"));

            var fecha = LeerFechaHora(texto);
            if (fecha == null && !string.IsNullOrWhiteSpace(texto))
                return RedirectToPage(Volver("err_fechas"));
            if (fecha != null && fecha > DateTime.Now.AddHours(2))
                return RedirectToPage(Volver("err_real_futuro"));
            if (fecha != null && !esZarpe && it.ZarpeEfectivo != null && fecha < it.ZarpeEfectivo)
                return RedirectToPage(Volver("err_arribo_antes"));
            if (fecha != null && esZarpe && it.ArriboEfectivo != null && fecha > it.ArriboEfectivo)
                return RedirectToPage(Volver("err_arribo_antes"));

            string usuario = User.Identity?.Name ?? "Sistema";
            if ((esZarpe ? it.ZarpeEfectivo : it.ArriboEfectivo) == fecha)
                return RedirectToPage(Volver(esZarpe ? "zarpe" : "arribo"));
            // 13-nov: misma lógica que el registro desde el Mainboard (Services/CicloNaveServicio.cs).
            string? copia = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var res = await CicloNave.RegistrarEnSalidaAsync(_context, _email, it, fecha, esZarpe, notificar, usuario, copia, $"Itinerarios, salida #{it.Id}");
            int bookings = res.Bookings, enviados = res.Enviados;
            var sinCorreo = res.SinEnviar;

            await _context.SaveChangesAsync();
            string nombre = esZarpe ? "Zarpe efectivo" : "Arribo efectivo";
            string destino = esZarpe ? "Zarpe Real" : "Arribo Efectivo";
            if (fecha != null && bookings > 0)
                TempData["AvisoItinerario"] = $"{nombre} registrado y copiado al {destino} de {bookings} booking(s) de esta salida."
                    + (notificar ? $" Confirmación enviada a {enviados} cliente(s)." : "")
                    + (sinCorreo.Count > 0 ? $" Sin enviar: {string.Join("; ", sinCorreo.Take(6))}{(sinCorreo.Count > 6 ? "…" : "")}." : "");
            return RedirectToPage(Volver(fecha == null ? (esZarpe ? "zarpe_borrado" : "arribo_borrado") : (esZarpe ? "zarpe" : "arribo")));
        }

        // ---------------------------------------------------------------------
        // Naves programadas para una ruta (nov-2026): alimenta el select de Nave / Viaje de
        // Operaciones (Create y Detalle). Con POL y POD, solo las de esa ruta; con naviera,
        // solo las suyas. Desde 30 días atrás, para bookings que ya zarparon.
        // ---------------------------------------------------------------------
        public async Task<IActionResult> OnGetNavesRutaAsync(int? naviera, int? pol, int? pod, int? idActual)
        {
            var desde = ItinerariosHelper.Hoy.AddDays(-30);
            var q = _context.ItinerariosNaves.AsNoTracking()
                .Include(i => i.IdNavieraNavigation)
                .Include(i => i.IdPuertoOrigenNavigation)
                .Include(i => i.IdPuertoDestinoNavigation)
                .Where(i => i.Activo && (i.Etd >= desde || i.Id == (idActual ?? 0)));
            if (pol != null)
            {
                var ids = await PuertosHelper.IdsEquivalentesAsync(_context, pol.Value);
                q = q.Where(i => ids.Contains(i.IdPuertoOrigen) || i.Id == (idActual ?? 0));
            }
            if (pod != null)
            {
                var ids = await PuertosHelper.IdsEquivalentesAsync(_context, pod.Value);
                q = q.Where(i => ids.Contains(i.IdPuertoDestino) || i.Id == (idActual ?? 0));
            }
            if (naviera != null) q = q.Where(i => i.IdNaviera == naviera || i.Id == (idActual ?? 0));

            var lista = await q.OrderBy(i => i.Etd).ThenBy(i => i.HoraEtd).Take(300).ToListAsync();
            var cl = CultureInfo.GetCultureInfo("es-CL");
            return new JsonResult(new
            {
                filtradoPorRuta = pol != null && pod != null,
                naves = lista.Select(i => new
                {
                    id = i.Id,
                    nave = i.NaveViaje,
                    idNaviera = i.IdNaviera,
                    naviera = i.IdNavieraNavigation?.NombreNaviera,
                    pol = i.IdPuertoOrigenNavigation?.NombrePuerto,
                    pod = i.IdPuertoDestinoNavigation?.NombrePuerto,
                    idPol = i.IdPuertoOrigen,
                    idPod = i.IdPuertoDestino,
                    etd = i.Etd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    eta = i.Eta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    etdTexto = i.EtdFechaHora.ToString(i.HoraEtd == null ? "dd'/'MM" : "dd'/'MM HH:mm", cl),
                    etaTexto = i.EtaFechaHora.ToString(i.HoraEta == null ? "dd'/'MM" : "dd'/'MM HH:mm", cl),
                    transito = i.DiasTransito,
                    zarpada = i.ZarpeEfectivo != null || i.Etd < ItinerariosHelper.Hoy
                })
            });
        }

        // ---------------------------------------------------------------------
        // Carga masiva (nov-2026): plantilla estándar de Excel. Todo o nada: si alguna fila
        // tiene un error no se guarda ninguna, y se listan las filas a corregir. Una salida que
        // ya existe (misma naviera, nave, viaje, POL y POD) se actualiza en vez de duplicarse.
        // ---------------------------------------------------------------------
        private static readonly string[] ColumnasPlantilla =
            { "Naviera", "Nave", "Viaje", "POL", "POD", "ETD fecha", "ETD hora", "ETA fecha", "ETA hora", "Corte documental", "Transbordo", "Observaciones" };

        private static string Norm(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            string d = v.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char ch in d)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        public async Task<IActionResult> OnGetPlantillaAsync()
        {
            var navieras = await _context.Navieras.AsNoTracking().Where(n => n.Activo == 1 && !n.IsDeleted).OrderBy(n => n.NombreNaviera).Select(n => n.NombreNaviera).ToListAsync();
            var puertos = await PuertosHelper.ListarUnicosAsync(_context);
            var naves = (await NavesCatalogoHelper.ActivasAsync(_context)).Select(n => n.Nombre).Distinct().ToList();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Itinerarios");
            for (int i = 0; i < ColumnasPlantilla.Length; i++)
            {
                ws.Cell(1, i + 1).Value = ColumnasPlantilla[i];
                ws.Column(i + 1).Width = i switch { 0 => 22, 1 => 24, 3 or 4 => 22, 9 => 20, 10 => 18, 11 => 40, _ => 13 };
            }
            var enc = ws.Range(1, 1, 1, ColumnasPlantilla.Length);
            enc.Style.Fill.BackgroundColor = XLColor.FromHtml("#2C1B4D");
            enc.Style.Font.FontColor = XLColor.White;
            enc.Style.Font.Bold = true;
            ws.Row(1).Height = 22;
            ws.SheetView.FreezeRows(1);
            ws.Range("F2:F2000").Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Range("H2:H2000").Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Range("G2:G2000").Style.DateFormat.Format = "HH:mm";
            ws.Range("I2:I2000").Style.DateFormat.Format = "HH:mm";
            ws.Range("J2:J2000").Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

            var listas = wb.Worksheets.Add("Listas");
            listas.Cell(1, 1).Value = "Navieras";
            listas.Cell(1, 2).Value = "Puertos";
            for (int i = 0; i < navieras.Count; i++) listas.Cell(i + 2, 1).Value = navieras[i] ?? "";
            for (int i = 0; i < puertos.Count; i++) listas.Cell(i + 2, 2).Value = puertos[i].NombrePuerto ?? "";
            listas.Cell(1, 3).Value = "Naves (catálogo)";
            for (int i = 0; i < naves.Count; i++) listas.Cell(i + 2, 3).Value = naves[i];
            listas.Row(1).Style.Font.Bold = true;
            listas.Column(1).Width = 30;
            listas.Column(2).Width = 30;
            listas.Column(3).Width = 30;
            if (naves.Count > 0)
            {
                // Lista desplegable, pero acepta una nave nueva: se agrega sola al catálogo al cargar.
                var dvNave = ws.Range("B2:B2000").CreateDataValidation();
                dvNave.List(listas.Range(2, 3, naves.Count + 1, 3), true);
                dvNave.ShowErrorMessage = false;
            }
            if (navieras.Count > 0)
                ws.Range("A2:A2000").CreateDataValidation().List(listas.Range(2, 1, navieras.Count + 1, 1), true);
            if (puertos.Count > 0)
            {
                ws.Range("D2:D2000").CreateDataValidation().List(listas.Range(2, 2, puertos.Count + 1, 2), true);
                ws.Range("E2:E2000").CreateDataValidation().List(listas.Range(2, 2, puertos.Count + 1, 2), true);
            }

            var guia = wb.Worksheets.Add("Instrucciones");
            string[] notas =
            {
                "ITINERARIOS DE NAVES · ATILSON",
                "Una fila por salida (nave + viaje + ruta).",
                "Obligatorios: Naviera, Nave, Viaje, POL, POD, ETD fecha y ETA fecha. Las horas son opcionales (HH:mm).",
                "Naviera y puertos se eligen de la lista (pestaña Listas): deben escribirse igual que en el maestro.",
                "Nave: elígela del catálogo (pestaña Listas) o escribe una nueva; las naves nuevas se agregan solas al catálogo. El viaje se escribe siempre.",
                "Fechas en formato dd/MM/yyyy. La ETA no puede ser anterior a la ETD. El tránsito se calcula solo.",
                "Si una salida ya existe (misma naviera, nave, viaje, POL y POD) se actualizan sus fechas; no se duplica.",
                "Si alguna fila tiene un error no se carga ninguna: se muestra la lista de filas a corregir.",
                "El zarpe efectivo no va en la plantilla: se registra en la salida después del zarpe."
            };
            for (int i = 0; i < notas.Length; i++) guia.Cell(i + 1, 1).Value = notas[i];
            guia.Cell(1, 1).Style.Font.Bold = true;
            guia.Cell(1, 1).Style.Font.FontSize = 14;
            guia.Column(1).Width = 110;

            using var mem = new MemoryStream();
            wb.SaveAs(mem);
            return File(mem.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ATILSON_Plantilla_Itinerarios.xlsx");
        }

        [BindProperty] public IFormFile? ArchivoItinerarios { get; set; }

        public async Task<IActionResult> OnPostCargaMasivaAsync()
        {
            if (ArchivoItinerarios == null || ArchivoItinerarios.Length == 0
                || !Path.GetExtension(ArchivoItinerarios.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorItinerario"] = "Selecciona la plantilla de itinerarios en formato .xlsx.";
                return RedirectToPage();
            }

            var navieras = await _context.Navieras.AsNoTracking().Where(n => !n.IsDeleted).ToListAsync();
            var puertos = await _context.Puertos.AsNoTracking().Where(p => p.Activo == 1).ToListAsync();
            var existentes = await _context.ItinerariosNaves.Where(i => i.Activo).ToListAsync();
            var errores = new List<string>();
            var filas = new List<(int Fila, ItinerarioNave Datos)>();

            int? IdPuerto(string texto)
            {
                string n = Norm(texto);
                if (n.Length == 0) return null;
                var p = puertos.Where(x => Norm(x.NombrePuerto) == n || Norm(x.NombrePuerto + x.Pais) == n).OrderBy(x => x.Id).FirstOrDefault();
                return p?.Id;
            }
            DateOnly? Fecha(IXLCell c)
            {
                if (c.IsEmpty()) return null;
                if (c.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(c.GetDateTime());
                if (c.DataType == XLDataType.Number) return DateOnly.FromDateTime(DateTime.FromOADate(c.GetDouble()));
                string t = c.GetFormattedString().Trim();
                string[] f = { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "dd/MM/yy" };
                return DateTime.TryParseExact(t, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? DateOnly.FromDateTime(d) : null;
            }
            (bool Ok, TimeOnly? Hora) Hora(IXLCell c)
            {
                if (c.IsEmpty()) return (true, null);
                if (c.DataType == XLDataType.DateTime) return (true, TimeOnly.FromDateTime(c.GetDateTime()));
                if (c.DataType == XLDataType.TimeSpan) return (true, TimeOnly.FromTimeSpan(c.GetTimeSpan()));
                if (c.DataType == XLDataType.Number) { double v = c.GetDouble() % 1; return (true, TimeOnly.FromTimeSpan(TimeSpan.FromDays(v < 0 ? 0 : v))); }
                string t = c.GetFormattedString().Trim();
                return TimeOnly.TryParseExact(t, new[] { "HH:mm", "H:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var h) ? (true, h) : (false, null);
            }
            DateTime? FechaHora(IXLCell c)
            {
                if (c.IsEmpty()) return null;
                if (c.DataType == XLDataType.DateTime) return c.GetDateTime();
                if (c.DataType == XLDataType.Number) return DateTime.FromOADate(c.GetDouble());
                string t = c.GetFormattedString().Trim();
                string[] f = { "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "dd-MM-yyyy HH:mm", "yyyy-MM-dd HH:mm", "dd/MM/yyyy" };
                return DateTime.TryParseExact(t, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            }

            try
            {
                await using var stream = ArchivoItinerarios.OpenReadStream();
                using var wb = new XLWorkbook(stream);
                var ws = wb.Worksheets.FirstOrDefault(w => Norm(w.Name) == "ITINERARIOS") ?? wb.Worksheet(1);
                var encabezado = Enumerable.Range(1, ColumnasPlantilla.Length).Select(c => Norm(ws.Cell(1, c).GetFormattedString())).ToList();
                if (!encabezado.SequenceEqual(ColumnasPlantilla.Select(Norm)))
                {
                    TempData["ErrorItinerario"] = "El archivo no tiene las columnas de la plantilla (" + string.Join(", ", ColumnasPlantilla) + "). Descarga la plantilla y vuelve a intentarlo.";
                    return RedirectToPage();
                }

                int ultima = ws.LastRowUsed()?.RowNumber() ?? 1;
                for (int r = 2; r <= ultima; r++)
                {
                    var row = ws.Row(r);
                    if (Enumerable.Range(1, ColumnasPlantilla.Length).All(c => row.Cell(c).IsEmpty())) continue;
                    var errs = new List<string>();
                    string navieraTxt = row.Cell(1).GetFormattedString().Trim();
                    var nav = navieras.FirstOrDefault(n => Norm(n.NombreNaviera) == Norm(navieraTxt));
                    if (nav == null) errs.Add(string.IsNullOrEmpty(navieraTxt) ? "falta la naviera" : $"naviera «{navieraTxt}» no está en el maestro");
                    string nave = row.Cell(2).GetFormattedString().Trim().ToUpper();
                    if (nave.Length == 0) errs.Add("falta la nave");
                    if (nave.Length > 150) errs.Add("nave de más de 150 caracteres");
                    string viaje = row.Cell(3).GetFormattedString().Trim().ToUpper();
                    if (viaje.Length == 0) errs.Add("falta el viaje");
                    if (viaje.Length > 40) errs.Add("viaje de más de 40 caracteres");
                    string polTxt = row.Cell(4).GetFormattedString().Trim(), podTxt = row.Cell(5).GetFormattedString().Trim();
                    int? pol = IdPuerto(polTxt), pod = IdPuerto(podTxt);
                    if (pol == null) errs.Add(string.IsNullOrEmpty(polTxt) ? "falta el POL" : $"POL «{polTxt}» no está en el maestro");
                    if (pod == null) errs.Add(string.IsNullOrEmpty(podTxt) ? "falta el POD" : $"POD «{podTxt}» no está en el maestro");
                    if (pol != null && pol == pod) errs.Add("POL y POD son el mismo puerto");
                    else if (pol != null && pod != null)
                    {
                        var pPol = puertos.First(x => x.Id == pol);
                        var pPod = puertos.First(x => x.Id == pod);
                        if (PuertosHelper.MismoPais(pPol.Pais, pPod.Pais)) errs.Add($"POL y POD son de {pPol.Pais!.Trim().ToUpper()} (el destino debe ser de otro país)");
                    }
                    var etd = Fecha(row.Cell(6));
                    var eta = Fecha(row.Cell(8));
                    var hEtd = Hora(row.Cell(7));
                    var hEta = Hora(row.Cell(9));
                    if (etd == null) errs.Add("ETD fecha vacía o inválida (dd/MM/yyyy)");
                    if (eta == null) errs.Add("ETA fecha vacía o inválida (dd/MM/yyyy)");
                    if (!hEtd.Ok) errs.Add("ETD hora inválida (HH:mm)");
                    if (!hEta.Ok) errs.Add("ETA hora inválida (HH:mm)");
                    if (etd != null && eta != null && (eta < etd || (eta == etd && hEtd.Hora != null && hEta.Hora != null && hEta.Hora < hEtd.Hora)))
                        errs.Add("la ETA es anterior a la ETD");
                    DateTime? corte = FechaHora(row.Cell(10));
                    if (corte == null && !row.Cell(10).IsEmpty()) errs.Add("corte documental inválido (dd/MM/yyyy HH:mm)");
                    string transbordo = row.Cell(11).GetFormattedString().Trim().ToUpper();
                    string obs = row.Cell(12).GetFormattedString().Trim();

                    if (errs.Count == 0 && filas.Any(f => f.Datos.IdNaviera == nav!.Id && f.Datos.Nave == nave && f.Datos.NumeroViaje == viaje
                                                      && f.Datos.IdPuertoOrigen == pol && f.Datos.IdPuertoDestino == pod))
                        errs.Add("salida repetida dentro del mismo archivo");

                    if (errs.Count > 0) { errores.Add($"Fila {r}: {string.Join("; ", errs)}."); continue; }
                    filas.Add((r, new ItinerarioNave
                    {
                        IdNaviera = nav!.Id, Nave = nave, NumeroViaje = viaje, IdPuertoOrigen = pol!.Value, IdPuertoDestino = pod!.Value,
                        Etd = etd!.Value, Eta = eta!.Value, HoraEtd = hEtd.Hora, HoraEta = hEta.Hora, CorteDocumental = corte,
                        Transbordo = transbordo.Length == 0 ? null : (transbordo.Length > 150 ? transbordo[..150] : transbordo),
                        Observaciones = obs.Length == 0 ? null : (obs.Length > 500 ? obs[..500] : obs)
                    }));
                }
            }
            catch (Exception)
            {
                TempData["ErrorItinerario"] = "No se pudo leer el archivo. Verifica que sea la plantilla de itinerarios guardada como .xlsx.";
                return RedirectToPage();
            }

            if (errores.Count > 0)
            {
                TempData["ErrorItinerario"] = $"No se cargó ninguna salida: {errores.Count} fila(s) con errores. Corrígelas y vuelve a subir el archivo.";
                TempData["ErroresItinerario"] = string.Join("\n", errores.Take(40)) + (errores.Count > 40 ? $"\n… y {errores.Count - 40} más." : "");
                return RedirectToPage();
            }
            if (filas.Count == 0)
            {
                TempData["ErrorItinerario"] = "El archivo no tiene filas con salidas.";
                return RedirectToPage();
            }

            int nuevas = 0, actualizadas = 0, navesNuevas = 0;
            var ahora = DateTime.Now;
            var catalogo = await NavesCatalogoHelper.ActivasAsync(_context);
            foreach (var (_, d) in filas)
            {
                d.Nave = NavesCatalogoHelper.Normalizar(d.Nave);
                if (NavesCatalogoHelper.Asegurar(_context, catalogo, d.Nave, d.IdNaviera, User.Identity?.Name).Creada) navesNuevas++;
                var ex = existentes.FirstOrDefault(i => i.IdNaviera == d.IdNaviera && i.Nave == d.Nave && i.NumeroViaje == d.NumeroViaje
                                                        && i.IdPuertoOrigen == d.IdPuertoOrigen && i.IdPuertoDestino == d.IdPuertoDestino);
                if (ex != null)
                {
                    var cambiosCm = new MaestrosAuditoria.Cambios();
                    cambiosCm.Comparar("ETD", ItinerarioServicio.FechaHoraTexto(ex.Etd, ex.HoraEtd), ItinerarioServicio.FechaHoraTexto(d.Etd, d.HoraEtd));
                    cambiosCm.Comparar("ETA", ItinerarioServicio.FechaHoraTexto(ex.Eta, ex.HoraEta), ItinerarioServicio.FechaHoraTexto(d.Eta, d.HoraEta));
                    if (cambiosCm.Hay)
                        MaestrosAuditoria.Registrar(_context, MaestrosAuditoria.EntItinerario, ex.Id, cambiosCm, User.Identity?.Name ?? "Sistema", "Carga masiva");
                    ex.Etd = d.Etd; ex.Eta = d.Eta; ex.HoraEtd = d.HoraEtd; ex.HoraEta = d.HoraEta;
                    ex.CorteDocumental = d.CorteDocumental ?? ex.CorteDocumental;
                    ex.Transbordo = d.Transbordo ?? ex.Transbordo;
                    ex.Observaciones = d.Observaciones ?? ex.Observaciones;
                    ex.FechaModificacion = ahora;
                    ex.UsuarioModificador = User.Identity?.Name;
                    actualizadas++;
                }
                else
                {
                    d.Activo = true;
                    d.FechaCreacion = ahora;
                    d.UsuarioCreador = User.Identity?.Name;
                    _context.ItinerariosNaves.Add(d);
                    nuevas++;
                }
            }
            await _context.SaveChangesAsync();
            // Las salidas nuevas ya tienen Id: queda su alta en el historial (13-nov-2026).
            foreach (var (_, d) in filas.Where(f => f.Item2.Id > 0 && f.Item2.FechaCreacion == ahora))
                MaestrosAuditoria.RegistrarEvento(_context, MaestrosAuditoria.EntItinerario, d.Id, "Alta", User.Identity?.Name ?? "Sistema",
                    $"{d.NaveViaje} · ETD {ItinerarioServicio.FechaHoraTexto(d.Etd, d.HoraEtd)} · carga masiva");
            await _context.SaveChangesAsync();
            TempData["AvisoItinerario"] = $"Carga masiva lista: {nuevas} salida(s) nuevas y {actualizadas} actualizada(s)."
                + (navesNuevas > 0 ? $" {navesNuevas} nave(s) nueva(s) agregada(s) al catálogo." : "");
            return RedirectToPage(new { verPasadas = true });
        }
    }
}
