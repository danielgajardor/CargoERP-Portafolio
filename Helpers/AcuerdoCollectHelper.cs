using System;
using System.Collections.Generic;
using System.Linq;
using AtilsonCargoSpa.Models;

namespace AtilsonCargoSpa.Helpers
{
    // Commodity (mercancía) como lista fija (oct-2026): se usa en Create, Detalle y en
    // las tarifas COLLECT para hacer match con el acuerdo comercial (ej. cerezas y
    // ciruelas con Late Arrival sin costo). Los textos antiguos (texto libre) se
    // reconocen igual ("CEREZA", "Cerezas frescas", "KIWI"...).
    public static class CommodityHelper
    {
        public static readonly string[] Lista =
        {
            "CEREZAS", "CIRUELAS", "KIWIS", "MANZANAS", "PERAS", "UVAS", "ARÁNDANOS",
            "LIMONES", "NARANJAS", "MANDARINAS", "PALTAS", "CAROZOS (DURAZNOS / NECTARINES)",
            "NUECES", "ALMENDRAS", "VINO", "CONGELADOS", "CARGA GENERAL", "OTROS"
        };

        private static readonly Dictionary<string, string> Sinonimos = new()
        {
            ["CEREZA"] = "CEREZAS", ["CHERRY"] = "CEREZAS", ["CHERRIES"] = "CEREZAS",
            ["CIRUELA"] = "CIRUELAS", ["PLUM"] = "CIRUELAS", ["PLUMS"] = "CIRUELAS",
            ["KIWI"] = "KIWIS", ["MANZANA"] = "MANZANAS", ["APPLE"] = "MANZANAS", ["APPLES"] = "MANZANAS",
            ["PERA"] = "PERAS", ["UVA"] = "UVAS", ["GRAPE"] = "UVAS", ["GRAPES"] = "UVAS",
            ["ARANDANO"] = "ARÁNDANOS", ["ARANDANOS"] = "ARÁNDANOS", ["BLUEBERRY"] = "ARÁNDANOS", ["BLUEBERRIES"] = "ARÁNDANOS",
            ["LIMON"] = "LIMONES", ["LEMON"] = "LIMONES", ["LEMONS"] = "LIMONES",
            ["NARANJA"] = "NARANJAS", ["MANDARINA"] = "MANDARINAS", ["PALTA"] = "PALTAS", ["AVOCADO"] = "PALTAS",
            ["DURAZNO"] = "CAROZOS (DURAZNOS / NECTARINES)", ["DURAZNOS"] = "CAROZOS (DURAZNOS / NECTARINES)",
            ["NECTARIN"] = "CAROZOS (DURAZNOS / NECTARINES)", ["NECTARINES"] = "CAROZOS (DURAZNOS / NECTARINES)", ["CAROZOS"] = "CAROZOS (DURAZNOS / NECTARINES)",
            ["NUEZ"] = "NUECES", ["WALNUT"] = "NUECES", ["WALNUTS"] = "NUECES",
            ["ALMENDRA"] = "ALMENDRAS",
        };

        // Devuelve el commodity de la lista que corresponde al texto, o el texto en
        // mayúsculas si no se reconoce (para no perder datos antiguos).
        public static string? Normalizar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            string n = VentaClienteHelper.Norm(texto);
            foreach (var c in Lista) if (VentaClienteHelper.Norm(c) == n) return c;
            foreach (var palabra in n.Split(new[] { ' ', ',', '/', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Sinonimos.TryGetValue(palabra, out var canon)) return canon;
                foreach (var c in Lista) if (VentaClienteHelper.Norm(c) == palabra) return c;
            }
            return texto.Trim().ToUpperInvariant();
        }

        public static List<string> Separar(string? lista) =>
            (lista ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Normalizar).Where(x => x != null).Select(x => x!).Distinct().ToList();

        public static string? Unir(IEnumerable<string>? items)
        {
            var l = (items ?? Enumerable.Empty<string>()).Select(Normalizar).Where(x => x != null).Select(x => x!).Distinct().ToList();
            return l.Count == 0 ? null : string.Join(", ", l);
        }
    }

    public static class AcuerdoCollectHelper
    {
        // Marca en TransaccionesFinanciera.JustificacionManual de las líneas que quedan
        // en $0 por un acuerdo Collect (no son "Por asignar").
        public const string MarcaSinCosto = "Sin costo por acuerdo Collect";
        // Oct-2026: tarifa cargada en $0 (ej. Gate de un depósito que no cobra). Tampoco es "Por asignar".
        public const string MarcaSinCostoTarifa = "Sin costo según tarifa";

        public static bool EsMarcaSinCosto(string? justificacion) =>
            justificacion != null && (justificacion.StartsWith(MarcaSinCosto) || justificacion.StartsWith(MarcaSinCostoTarifa));

        public static bool EsCollect(Operacione op) => op.CondicionPago == 3 || op.CondicionPago == 4;

        public static string TextoCondicionPago(int? condicionPago) => condicionPago switch
        {
            1 => "PREPAID", 2 => "FULL PREPAID", 3 => "COLLECT", 4 => "FULL COLLECT", 5 => "ELSEWHERE", _ => ""
        };

        private static bool Contiene(string? a, string? b)
        {
            string x = VentaClienteHelper.Norm(a), y = VentaClienteHelper.Norm(b);
            if (x == "" || y == "") return false;
            return x == y || x.Contains(y) || y.Contains(x);
        }

        // Acuerdo vigente que aplica a la operación. tipo: NAVIERA o CLIENTE.
        // Requisitos: misma naviera (y cliente si es CLIENTE), destino (POD) igual, POL
        // igual si la tarifa lo indica, y commodity incluido en la tarifa (o tarifa sin
        // commodity = todos). Prioriza commodity explícito y POL explícito; luego la más nueva.
        public static TarifaCollect? Elegir(IEnumerable<TarifaCollect> tarifas, string tipo, int? idCliente, int idNaviera,
            string? pol, string? pod, string? commodity, DateTime fecha)
        {
            string? com = CommodityHelper.Normalizar(commodity);
            var candidatas = tarifas.Where(t => t.EsActiva && t.TipoTarifa == tipo && t.IdNaviera == idNaviera
                    && (tipo != TarifaCollect.TipoCliente || t.IdCliente == idCliente)
                    && t.FechaInicioVigencia.Date <= fecha.Date
                    && (!t.FechaFinVigencia.HasValue || t.FechaFinVigencia.Value.Date >= fecha.Date)
                    && Contiene(t.Pod, pod)
                    && (string.IsNullOrWhiteSpace(t.Pol) || Contiene(t.Pol, pol)))
                .Select(t => new { T = t, Coms = CommodityHelper.Separar(t.Commodity) })
                .Where(x => x.Coms.Count == 0 || (com != null && x.Coms.Contains(com)))
                .ToList();
            return candidatas
                .OrderByDescending(x => x.Coms.Count > 0)
                .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.T.Pol))
                .ThenByDescending(x => x.T.FechaInicioVigencia)
                .ThenByDescending(x => x.T.Id)
                .Select(x => x.T)
                .FirstOrDefault();
        }

        // ¿La corrección de BL registrada en "fechaCorreccion" cae en el plazo sin costo?
        // Aplica a todas las correcciones dentro del plazo. Sin ETD se considera antes del zarpe.
        public static bool CorreccionSinCosto(TarifaCollect? t, DateTime fechaCorreccion, DateTime? etd)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.CorreccionBlSinCosto)) return false;
            if (!etd.HasValue) return true;
            double horas = (fechaCorreccion - etd.Value).TotalHours;
            return t.CorreccionBlSinCosto switch
            {
                TarifaCollect.CorreccionAntesZarpe => horas < 0,
                TarifaCollect.Correccion24h => horas <= 24,
                TarifaCollect.Correccion48h => horas <= 48,
                _ => false
            };
        }

        public static string TextoCorreccion(string? codigo) => codigo switch
        {
            TarifaCollect.CorreccionAntesZarpe => "antes del zarpe",
            TarifaCollect.Correccion24h => "hasta 24 h post zarpe",
            TarifaCollect.Correccion48h => "hasta 48 h post zarpe",
            _ => ""
        };

        public static bool EsLateArrival(string? tipoCosto)
        {
            string t = (tipoCosto ?? "").ToUpperInvariant();
            return (t.Contains("LATE ARRIVAL") || t.Contains("(LAR)")) && !t.Contains("EXTRA") && !t.Contains("(ELAR)");
        }

        public static bool EsExtraLate(string? tipoCosto)
        {
            string t = (tipoCosto ?? "").ToUpperInvariant();
            return t.Contains("EXTRA LATE") || t.Contains("(ELAR)");
        }
    }
}
