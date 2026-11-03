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
