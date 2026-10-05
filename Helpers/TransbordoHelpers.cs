using AtilsonCargoSpa.Models;

namespace AtilsonCargoSpa.Helpers
{
    public static class TransbordoHelpers
    {
        public static DateTime? FechaLimite(GestionTransbordo? t)
            => (t?.FechaArriboTransbordo.HasValue == true && t.DiasLibres.HasValue)
                ? t.FechaArriboTransbordo.Value.AddDays(t.DiasLibres.Value)
                : null;

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