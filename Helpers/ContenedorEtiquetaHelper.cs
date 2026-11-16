namespace AtilsonCargoSpa.Helpers
{
    // Nombre visible de cada contenedor en las pestañas del Detalle (oct-2026): su
    // número si ya está cargado; si no, el orden dentro del booking ("1° Contenedor",
    // "2° Contenedor"...), en vez de "Contenedor Principal" / "Contenedor U123".
    public static class ContenedorEtiquetaHelper
    {
        public static string Etiqueta(string? numeroContenedor, int posicion)
            => string.IsNullOrWhiteSpace(numeroContenedor)
                ? $"{posicion}° Contenedor"
                : numeroContenedor.Trim().ToUpperInvariant();

        // Para cobros por contenedor en Finanzas y Comercial (nov-2026): siempre el orden
        // dentro del booking y, si ya está cargado, el número ("2° · MSKU1234567").
        public static string EtiquetaCompleta(string? numeroContenedor, int posicion)
            => string.IsNullOrWhiteSpace(numeroContenedor)
                ? $"{posicion}° Contenedor"
                : $"{posicion}° · {numeroContenedor.Trim().ToUpperInvariant()}";
    }
}
