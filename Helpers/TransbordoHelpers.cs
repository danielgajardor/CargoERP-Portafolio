using AtilsonCargoSpa.Models;

namespace AtilsonCargoSpa.Helpers
{
    public static class TransbordoHelpers
    {
        // Días libres "reales": una vez confirmada la nueva nave, el puerto ya
        // definió las fechas de conexión exactas (Fecha In = arribo nave 1,
        // Fecha Out = zarpe nave 2) — en transbordo no existen días libres como
        // concepto propio, son simplemente la diferencia entre esas dos fechas.
        // Mientras la nueva nave no esté confirmada, se usa el valor que la
        // ejecutiva ingresó como estimación (lo que le informó el puerto/naviera
        // verbalmente), solo para poder mostrar una alerta temprana.
        public static int? DiasLibresCalculados(GestionTransbordo? t)
        {
            if (t?.FechaArriboTransbordo.HasValue == true && t.NuevoEtdTransbordo.HasValue)
                return (int)Math.Round((t.NuevoEtdTransbordo.Value - t.FechaArriboTransbordo.Value).TotalDays);
            return t?.DiasLibres;
        }

        public static DateTime? FechaLimite(GestionTransbordo? t)
        {
            if (t?.FechaArriboTransbordo.HasValue != true) return null;
            var dias = DiasLibresCalculados(t);
            return dias.HasValue ? t.FechaArriboTransbordo.Value.AddDays(dias.Value) : null;
        }

        public static bool EstaResuelto(GestionTransbordo? t) => t?.NuevoEtdTransbordo.HasValue == true;

        // SIN_DATOS: aún no se registró arribo. PENDIENTE: en plazo.
        // CRITICO: quedan 2 días o menos. VENCIDO: plazo pasado sin nueva nave.
        // RESUELTO: ya hay nueva nave con ETD confirmado.
        public static string EstadoTransbordo(GestionTransbordo? t)
        {
            if (EstaResuelto(t)) return "RESUELTO";
            if (t == null || !t.FechaArriboTransbordo.HasValue) return "SIN_DATOS";
            var limite = FechaLimite(t);
            if (!limite.HasValue) return "PENDIENTE";
            var dias = (limite.Value - DateTime.Now).TotalDays;
            if (dias < 0) return "VENCIDO";
            if (dias <= 2) return "CRITICO";
            return "PENDIENTE";
        }
    }
}