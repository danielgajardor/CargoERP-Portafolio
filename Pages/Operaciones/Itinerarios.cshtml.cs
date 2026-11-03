using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        public ItinerariosModel(AtilsonContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)] public int? Pol { get; set; }
        [BindProperty(SupportsGet = true)] public int? Pod { get; set; }
        [BindProperty(SupportsGet = true)] public int? Naviera { get; set; }
        [BindProperty(SupportsGet = true)] public bool VerPasadas { get; set; }
        [BindProperty(SupportsGet = true)] public int? Editar { get; set; }
        [BindProperty(SupportsGet = true)] public int? Copiar { get; set; }
        [BindProperty(SupportsGet = true)] public string? Aviso { get; set; }

        public List<ItinerarioNave> Salidas { get; set; } = new();
        public List<Naviera> Navieras { get; set; } = new();
        public List<Puerto> Puertos { get; set; } = new();
        public List<string> NavesSugeridas { get; set; } = new();
        public Dictionary<int, int> ReservasPorSalida { get; set; } = new();
        public bool FormularioAbierto { get; set; }

        public static readonly Dictionary<string, (string Tipo, string Texto)> Avisos = new()
        {
            ["creada"] = ("success", "Salida agregada. Si su ETD está dentro de las próximas 5 semanas, los clientes ya la ven en el portal."),
            ["actualizada"] = ("success", "Salida actualizada."),
            ["eliminada"] = ("success", "Salida eliminada: ya no se muestra a los clientes."),
            ["err_naviera"] = ("danger", "Selecciona la naviera."),
            ["err_nave"] = ("danger", "Ingresa el nombre de la nave."),
            ["err_puertos"] = ("danger", "Selecciona puerto de origen y de destino distintos."),
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
            public string? Etd { get; set; }              // yyyy-MM-dd
            public string? Eta { get; set; }              // yyyy-MM-dd
            public string? CorteDocumental { get; set; }  // yyyy-MM-ddTHH:mm
            public string? Transbordo { get; set; }
            public string? Observaciones { get; set; }
        }

        [BindProperty]
        public SalidaInput Input { get; set; } = new();

        private static DateOnly? LeerFecha(string? s) =>
            DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        private static DateTime? LeerFechaHora(string? s) =>
            DateTime.TryParseExact(s, new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

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
                    Etd = it.Etd.AddDays(dias).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Eta = it.Eta.AddDays(dias).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CorteDocumental = it.CorteDocumental?.AddDays(dias).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                    Transbordo = it.Transbordo,
                    Observaciones = it.Observaciones
                };
                FormularioAbierto = true;
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
            var r = Input;
            object Volver(string aviso, int? editar = null) => new { aviso, pol = Pol, pod = Pod, naviera = Naviera, verPasadas = VerPasadas ? true : (bool?)null, editar };

            if (r.IdNaviera == null || !await _context.Navieras.AnyAsync(n => n.Id == r.IdNaviera))
                return RedirectToPage(Volver("err_naviera", r.Id));
            if (string.IsNullOrWhiteSpace(r.Nave))
                return RedirectToPage(Volver("err_nave", r.Id));
            if (r.IdPuertoOrigen == null || r.IdPuertoDestino == null || r.IdPuertoOrigen == r.IdPuertoDestino
                || !await _context.Puertos.AnyAsync(p => p.Id == r.IdPuertoOrigen)
                || !await _context.Puertos.AnyAsync(p => p.Id == r.IdPuertoDestino))
                return RedirectToPage(Volver("err_puertos", r.Id));

            var etd = LeerFecha(r.Etd);
            var eta = LeerFecha(r.Eta);
            if (etd == null || eta == null || eta < etd)
                return RedirectToPage(Volver("err_fechas", r.Id));

            ItinerarioNave it;
            bool nueva = r.Id == null;
            if (nueva)
            {
                it = new ItinerarioNave
                {
                    FechaCreacion = DateTime.Now,
                    UsuarioCreador = User.Identity?.Name,
                    Activo = true
                };
                _context.ItinerariosNaves.Add(it);
            }
            else
            {
                var existente = await _context.ItinerariosNaves.FirstOrDefaultAsync(i => i.Id == r.Id && i.Activo);
                if (existente == null)
                    return RedirectToPage(Volver("err_noexiste"));
                it = existente;
                it.FechaModificacion = DateTime.Now;
                it.UsuarioModificador = User.Identity?.Name;
            }

            it.IdNaviera = r.IdNaviera.Value;
            it.Nave = r.Nave.Trim().ToUpper();
            it.NumeroViaje = string.IsNullOrWhiteSpace(r.NumeroViaje) ? null : r.NumeroViaje.Trim().ToUpper();
            it.IdPuertoOrigen = r.IdPuertoOrigen.Value;
            it.IdPuertoDestino = r.IdPuertoDestino.Value;
            it.Etd = etd.Value;
            it.Eta = eta.Value;
            it.CorteDocumental = LeerFechaHora(r.CorteDocumental);
            it.Transbordo = string.IsNullOrWhiteSpace(r.Transbordo) ? null : r.Transbordo.Trim().ToUpper();
            it.Observaciones = string.IsNullOrWhiteSpace(r.Observaciones) ? null : r.Observaciones.Trim();

            await _context.SaveChangesAsync();
            return RedirectToPage(Volver(nueva ? "creada" : "actualizada"));
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
            await _context.SaveChangesAsync();
            return RedirectToPage(new { aviso = "eliminada", pol = Pol, pod = Pod, naviera = Naviera });
        }
    }
}
