using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AtilsonCargoSpa.Models;

namespace AtilsonCargoSpa.Helpers
{
    // Elige la tarifa de VENTA al cliente (TarifasClientes) que corresponde a una
    // operación, para que Finanzas valorice el Ingreso con lo pactado (oct-2026).
    // Reglas comunes:
    //  - Un campo vacío en la tarifa (POL, naviera, depósito, planta...) significa
    //    "aplica a todos"; uno lleno tiene que coincidir con la operación.
    //  - Entre varias tarifas válidas gana la más específica y, a igualdad, la más
    //    nueva (la lista ya viene ordenada por Id descendente).
    public static class VentaClienteHelper
    {
        public static string Norm(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var formD = texto.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (var c in formD)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // -1 = no coincide, 0 = la tarifa aplica a todos, 1 = coincide explícitamente.
        public static int Coincide(string? valorTarifa, string? valorOperacion)
        {
            var t = Norm(valorTarifa);
            if (t == "" || t == "ALL" || t == "TODOS") return 0;
            var o = Norm(valorOperacion);
            if (o == "") return -1;
            return (t.Contains(o) || o.Contains(t)) ? 1 : -1;
        }

        // "DRY", "RF" o "" (sin tipo / todos).
        public static string FamiliaEquipo(string? tipoContenedor)
        {
            var t = Norm(tipoContenedor).Replace(" ", "").Replace("'", "");
            if (t == "" || t == "ALL" || t == "AMBOS" || t == "TODOS") return "";
            if (t.Contains("RF") || t.Contains("REEFER")) return "RF";
            if (t.Contains("DRY") || t.Contains("STD") || t.Contains("HC") || t.Contains("20") || t.Contains("40")) return "DRY";
            return "";
        }

        // "20", "40" o "" (sin tamaño / 20 y 40).
        public static string TamanoEquipo(string? tipoContenedor)
        {
            var t = Norm(tipoContenedor).Replace(" ", "").Replace("'", "");
            bool v20 = t.Contains("20"), v40 = t.Contains("40");
            if (v20 && !v40) return "20";
            if (v40 && !v20) return "40";
            return "";
        }

        // Equipo de la operación: el TipoContenedor más la marca Reefer si la carga es
        // refrigerada (IdTipoCarga 2) aunque el texto no lo diga.
        public static string EquipoOperacion(string? tipoContenedor, int? idTipoCarga)
        {
            var tc = tipoContenedor ?? "";
            if (idTipoCarga == 2 && FamiliaEquipo(tc) != "RF") tc += " RF";
            return tc;
        }

        // -1 = no aplica, 0 = tarifa para todo equipo, 1 = mismo tipo, 2 = mismo tipo y tamaño.
        public static int PuntajeEquipo(string? equipoTarifa, string? equipoOperacion)
        {
            var famT = FamiliaEquipo(equipoTarifa);
            if (famT == "") return 0;
            var famO = FamiliaEquipo(equipoOperacion);
            if (famO == "") famO = "DRY";
            if (famT != famO) return -1;
            var tamT = TamanoEquipo(equipoTarifa);
            if (tamT == "") return 1;
            var tamO = TamanoEquipo(equipoOperacion);
            if (tamO == "") return 1;
            return tamT == tamO ? 2 : -1;
        }

        private static bool EsGrupo(TarifasCliente t, string grupo) => Norm(t.GrupoCobro) == Norm(grupo);

        private static bool EsTarifaPlana(TarifasCliente t) => Norm(t.Concepto).Contains("TARIFA PLANA");

        public const string ClasificacionFresco = "FRESCO";
        public const string ClasificacionCongelado = "CONGELADO";

        // Condición reefer de la operación (FRESH / FROZEN, o en español) a la
        // clasificación de la venta marítima RF (FRESCO / CONGELADO). null = no informada.
        public static string? ClasificacionReefer(string? condicionReefer)
        {
            var c = Norm(condicionReefer);
            if (c == "") return null;
            if (c.Contains("FROZ") || c.Contains("CONGEL")) return ClasificacionCongelado;
            if (c.Contains("FRESH") || c.Contains("FRESC") || c.Contains("CHILL")) return ClasificacionFresco;
            return null;
        }

        // 1 = misma condición, 0 = la venta no distingue (o la operación no la informa),
        // -1 = condición distinta, o venta reefer clasificada para una operación no reefer.
        private static int PuntajeClasificacion(TarifasCliente t, string equipo, string? clasificacionOp)
        {
            var clasT = Norm(t.Clasificacion);
            if (clasT != ClasificacionFresco && clasT != ClasificacionCongelado) return 0;
            if (FamiliaEquipo(equipo) != "RF") return -1;
            if (clasificacionOp == null) return 0;
            return clasT == clasificacionOp ? 1 : -1;
        }

        public static TarifasCliente? ElegirMaritima(IEnumerable<TarifasCliente> ventas, int? idNaviera, string? pol, string? pod, string equipo, string? condicionReefer = null)
        {
            var clasOp = ClasificacionReefer(condicionReefer);
            return ventas
                .Where(t => EsGrupo(t, "Marítimo") && !EsTarifaPlana(t))
                .Select(t => new
                {
                    T = t,
                    Nav = t.IdNaviera == null ? 0 : (t.IdNaviera == idNaviera ? 1 : -1),
                    Pol = Coincide(t.Pol, pol),
                    Pod = Coincide(t.Pod, pod),
                    Eq = PuntajeEquipo(t.TipoContenedor, equipo),
                    Clas = PuntajeClasificacion(t, equipo, clasOp)
                })
                .Where(x => x.Nav >= 0 && x.Pol >= 0 && x.Pod >= 0 && x.Eq >= 0 && x.Clas >= 0)
                .OrderByDescending(x => x.Pol + x.Pod)
                .ThenByDescending(x => x.Nav)
                .ThenByDescending(x => x.Clas)
                .ThenByDescending(x => x.Eq)
                .Select(x => x.T)
                .FirstOrDefault();
        }

        public static TarifasCliente? ElegirTerrestre(IEnumerable<TarifasCliente> ventas, string? retiro, string? planta, string? entrega, string equipo)
        {
            return ventas
                .Where(t => EsGrupo(t, "Terrestre") && !EsTarifaPlana(t))
                .Select(t => new
                {
                    T = t,
                    Ret = Coincide(t.Pol, retiro),
                    Pla = Coincide(t.ZonaPlanta, planta),
                    Ent = Coincide(t.Pod, entrega),
                    Eq = PuntajeEquipo(t.TipoContenedor, equipo)
                })
                .Where(x => x.Ret >= 0 && x.Pla >= 0 && x.Ent >= 0 && x.Eq >= 0)
                .OrderByDescending(x => x.Ret + x.Pla + x.Ent)
                .ThenByDescending(x => x.Eq)
                .Select(x => x.T)
                .FirstOrDefault();
        }

        // Venta Gate: Concepto "GATE OUT" / "GATE IN", depósito en Pol (vacío = cualquiera).
        public static TarifasCliente? ElegirGate(IEnumerable<TarifasCliente> ventas, string movimiento, string? deposito, int? idNaviera, string equipo)
        {
            string mov = movimiento.ToUpperInvariant();
            return ventas
                .Where(t => EsGrupo(t, "Gate") && Norm(t.Concepto).Replace(" ", "").Contains("GATE" + mov))
                .Select(t => new
                {
                    T = t,
                    Dep = Coincide(t.Pol, deposito),
                    Nav = t.IdNaviera == null ? 0 : (t.IdNaviera == idNaviera ? 1 : -1),
                    Eq = PuntajeEquipo(t.TipoContenedor, equipo)
                })
                .Where(x => x.Dep >= 0 && x.Nav >= 0 && x.Eq >= 0)
                .OrderByDescending(x => x.Dep)
                .ThenByDescending(x => x.Nav)
                .ThenByDescending(x => x.Eq)
                .Select(x => x.T)
                .FirstOrDefault();
        }

        // Venta Courier: mismo concepto (zona) y, si se cargó por empresa, la misma empresa.
        public static TarifasCliente? ElegirCourier(IEnumerable<TarifasCliente> ventas, string concepto, string? empresa)
        {
            var delConcepto = ventas.Where(t => EsGrupo(t, "Courier") && Norm(t.Concepto) == Norm(concepto)).ToList();
            var emp = CourierHelper.Normalizar(empresa);
            return (emp != null ? delConcepto.FirstOrDefault(t => CourierHelper.Normalizar(t.Clasificacion) == emp) : null)
                ?? delConcepto.FirstOrDefault(t => string.IsNullOrWhiteSpace(t.Clasificacion));
        }

        // ===== Venta Documental (oct-2026) =====
        // Comercial carga la venta documental al cliente por servicio (SAG o SERNAPESCA)
        // con dos conceptos: Honorarios (+ IVA) y DUS (sin IVA). Ambos se cobran por
        // contenedor, o en un solo cobro grupal si el booking lo indica.
        public const string ConceptoVentaHonorarios = "HONORARIOS AGENCIA DE ADUANAS";
        public const string ConceptoVentaDus = "LEGALIZACIÓN / EMISIÓN DUS";

        public static string? ClasificacionDocumental(OperacionesDocumentale? doc) =>
            doc == null ? null
            : doc.AplicaSernapesca == true ? "SERNAPESCA"
            : doc.AplicaSag == true ? "SAG"
            : null;

        // esDus = false → Honorarios. La Tarifa Plana Paquetizada (ej. Promar) no entra
        // acá: tiene su propio cálculo. Primero la venta del mismo servicio (SAG o
        // SERNAPESCA); si no hay, una sin clasificación (o las antiguas "COMUNES").
        public static TarifasCliente? ElegirDocumental(IEnumerable<TarifasCliente> ventas, bool esDus, string? clasificacion)
        {
            var candidatas = ventas.Where(t => EsGrupo(t, "Documental"))
                .Where(t => !Norm(t.Concepto).Contains("TARIFA PLANA"))
                .Where(t => esDus ? Norm(t.Concepto).Contains("DUS") : Norm(t.Concepto).Contains("HONORARIO"))
                .ToList();
            string clas = Norm(clasificacion);
            if (clas != "")
            {
                var exacta = candidatas.FirstOrDefault(t => Norm(t.Clasificacion) == clas);
                if (exacta != null) return exacta;
            }
            return candidatas.FirstOrDefault(t => string.IsNullOrWhiteSpace(t.Clasificacion) || Norm(t.Clasificacion) == "COMUN");
        }
    }
}
