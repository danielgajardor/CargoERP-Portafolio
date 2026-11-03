using AtilsonCargoSpa.Models;

namespace AtilsonCargoSpa.Helpers
{
    public static class TransaccionesHelper
    {
        /// <summary>
        /// Línea en $0 que de verdad espera tarifa ("Por asignar"). No lo son: las ajustadas a
        /// mano, las que no se le cobran al cliente (NO FACTURABLE) y las "sin costo" por
        /// acuerdo Collect o por una tarifa cargada en $0 (oct-2026).
        /// </summary>
        public static bool EsTarifaPendiente(TransaccionesFinanciera t) =>
            t.MontoNeto == 0m && !t.TarifaManual && t.EstadoFila != "NO FACTURABLE"
            && !AcuerdoCollectHelper.EsMarcaSinCosto(t.JustificacionManual);
    }
}
