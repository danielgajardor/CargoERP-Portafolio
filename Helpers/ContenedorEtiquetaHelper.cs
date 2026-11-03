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
    }
}
