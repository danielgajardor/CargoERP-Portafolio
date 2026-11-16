using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    // Consulta de salidas de nave para el Portal Cliente (oct-2026). Un solo lugar
    // para "próximas salidas de una ruta", usado por la página de Itinerarios, Mis
    // Cotizaciones y el selector de nave de Solicitar Reserva.
    public static class ItinerariosHelper
    {
        public static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Today);

        // Salidas activas con ETD desde hoy hasta el horizonte (por defecto 5
        // semanas). Si se pasa POL/POD, también acepta los Id equivalentes del
        // mismo puerto (ver PuertosHelper).
        public static async Task<List<ItinerarioNave>> ProximasSalidasAsync(
            AtilsonContext context, int? idPol = null, int? idPod = null, int? idNaviera = null,
            int semanas = ItinerarioNave.SemanasHorizonte, int max = 200)
        {
            var desde = Hoy;
            var hasta = desde.AddDays(semanas * 7);

            var q = context.ItinerariosNaves.AsNoTracking()
                .Include(i => i.IdNavieraNavigation)
                .Include(i => i.IdPuertoOrigenNavigation)
                .Include(i => i.IdPuertoDestinoNavigation)
                .Where(i => i.Activo && i.Etd >= desde && i.Etd <= hasta);

            if (idPol != null)
            {
                var ids = await PuertosHelper.IdsEquivalentesAsync(context, idPol.Value);
                q = q.Where(i => ids.Contains(i.IdPuertoOrigen));
            }
            if (idPod != null)
            {
                var ids = await PuertosHelper.IdsEquivalentesAsync(context, idPod.Value);
                q = q.Where(i => ids.Contains(i.IdPuertoDestino));
            }
            if (idNaviera != null)
                q = q.Where(i => i.IdNaviera == idNaviera);

            return await q.OrderBy(i => i.Etd).ThenBy(i => i.Eta).Take(max).ToListAsync();
        }

        // Salida del maestro que corresponde a la nave elegida en Operaciones (nov-2026). Primero
        // el Id que mandó el formulario, si coincide con el texto "NAVE / VIAJE"; si no (Id vacío
        // o desfasado), la única salida activa con esa nave y viaje de la misma naviera. Si no hay
        // una sola, null: la nave queda como texto, como antes del maestro.
        public static async Task<int?> ResolverSalidaAsync(AtilsonContext context, int? idPropuesto, string? nave, int? idNaviera)
        {
            if (string.IsNullOrWhiteSpace(nave)) return null;
            string texto = nave.Trim();
            if (idPropuesto != null)
            {
                var s = await context.ItinerariosNaves.AsNoTracking().FirstOrDefaultAsync(i => i.Id == idPropuesto.Value);
                if (s != null && string.Equals(s.NaveViaje, texto, StringComparison.OrdinalIgnoreCase)) return s.Id;
            }
            int sep = texto.LastIndexOf(" / ", StringComparison.Ordinal);
            if (sep <= 0) return null;
            string naveParte = texto[..sep].Trim().ToUpper(), viajeParte = texto[(sep + 3)..].Trim().ToUpper();
            var ids = await context.ItinerariosNaves.AsNoTracking()
                .Where(i => i.Activo && i.Nave == naveParte && i.NumeroViaje == viajeParte && (idNaviera == null || i.IdNaviera == idNaviera))
                .Select(i => i.Id).Take(2).ToListAsync();
            return ids.Count == 1 ? ids[0] : null;
        }

        // Cuando la nave del booking cambia por otro camino que no es el select de Nave / Viaje
        // (roleo, edición desde el Main Board, pantallas antiguas), el vínculo con la salida del
        // maestro se vuelve a calcular: se conserva solo si sigue siendo la misma nave y viaje.
        // Si no, la salida anterior registraría su zarpe en un booking que ya no va en ella.
        public static async Task AlinearConNaveAsync(AtilsonContext context, Operacione op)
        {
            if (op.IdItinerario == null && string.IsNullOrWhiteSpace(op.Nave)) return;
            int? salidaAnterior = op.IdItinerario;
            DateTime? etdRealAnterior = op.EtdReal;
            op.IdItinerario = await ResolverSalidaAsync(context, op.IdItinerario, op.Nave, op.IdNaviera > 0 ? op.IdNaviera : null);
            await AjustarZarpeAlCambiarSalidaAsync(context, op, salidaAnterior, etdRealAnterior);
        }

        // Zarpe efectivo cuando el booking cambia de salida (roleo o corrección de la nave): el
        // ETD Real que se había copiado del zarpe de la salida anterior se quita, porque el
        // contenedor no fue en esa nave; y si la nueva salida ya zarpó, se copia su zarpe (la
        // misma regla que al registrarlo en Itinerarios). Un ETD Real escrito a mano en la misma
        // edición no se toca. Se compara por fecha: Marítimo guarda el ETD Real sin hora.
        public static async Task AjustarZarpeAlCambiarSalidaAsync(AtilsonContext context, Operacione op, int? salidaAnterior, DateTime? etdRealAnterior)
        {
            if (op.IdItinerario == salidaAnterior) return;
            if (op.EtdReal?.Date != etdRealAnterior?.Date) return;
            if (op.EtdReal != null && salidaAnterior != null)
            {
                var zarpeAnterior = await context.ItinerariosNaves.AsNoTracking()
                    .Where(i => i.Id == salidaAnterior.Value).Select(i => i.ZarpeEfectivo).FirstOrDefaultAsync();
                if (zarpeAnterior != null && zarpeAnterior.Value.Date == op.EtdReal.Value.Date) op.EtdReal = null;
            }
            if (op.EtdReal == null && op.IdItinerario != null)
            {
                var zarpeNuevo = await context.ItinerariosNaves.AsNoTracking()
                    .Where(i => i.Id == op.IdItinerario.Value).Select(i => i.ZarpeEfectivo).FirstOrDefaultAsync();
                if (zarpeNuevo != null) op.EtdReal = zarpeNuevo;
            }
        }

        // Lunes de la semana de una fecha (semana Lun-Dom, como en Chile).
        public static DateOnly InicioSemana(DateOnly fecha)
        {
            int delta = ((int)fecha.DayOfWeek + 6) % 7;
            return fecha.AddDays(-delta);
        }

        public static string EtiquetaSemana(DateOnly lunes)
        {
            var cl = CultureInfo.GetCultureInfo("es-CL");
            var domingo = lunes.AddDays(6);
            int semanaIso = ISOWeek.GetWeekOfYear(lunes.ToDateTime(TimeOnly.MinValue));
            string rango = lunes.Month == domingo.Month
                ? $"{lunes.Day} al {domingo.ToString("d 'de' MMMM", cl)}"
                : $"{lunes.ToString("d 'de' MMM", cl)} al {domingo.ToString("d 'de' MMM", cl)}";
            return $"Semana {semanaIso} · {rango}";
        }

        // Iniciales para el distintivo de la naviera en las tarjetas (ej. "HAPAG
        // LLOYD" → "HL", "MSC" → "MSC").
        public static string Sigla(string? naviera)
        {
            if (string.IsNullOrWhiteSpace(naviera)) return "?";
            var partes = naviera.Trim().Split(new[] { ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 1) return partes[0].Length <= 4 ? partes[0].ToUpper() : partes[0][..3].ToUpper();
            return string.Concat(partes.Take(3).Select(p => char.ToUpper(p[0])));
        }

        // Color de la naviera (navieras.ColorRepresentativo) si es un hex válido;
        // si no, uno estable derivado del nombre, para que cada naviera se
        // reconozca igual en todas las tarjetas.
        public static string Color(Naviera? n)
        {
            var c = n?.ColorRepresentativo?.Trim();
            if (!string.IsNullOrEmpty(c) && System.Text.RegularExpressions.Regex.IsMatch(c, "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$"))
                return c;
            string[] paleta = { "#1d4ed8", "#c2410c", "#0f766e", "#7c3aed", "#be123c", "#0369a1", "#4d7c0f", "#a16207" };
            int h = 0;
            foreach (var ch in n?.NombreNaviera ?? "") h = (h * 31 + ch) & 0x7fffffff;
            return paleta[h % paleta.Length];
        }
    }
}
