namespace AtilsonCargoSpa.Models
{
    // Modelo del partial _EmisionBlCampos: el mismo bloque "Tipo de emisión +
    // Lugar de emisión" se usa en Create, en Gestión de Matriz, en la sección
    // Documental y en Corrección de BL (con otros nombres de campo).
    public class EmisionBlCamposVm
    {
        public string IdPrefijo { get; set; } = "embl";
        public string CampoTipo { get; set; } = "TipoEmisionBl";
        public string CampoOtro { get; set; } = "TipoEmisionBlOtro";
        public string CampoLugar { get; set; } = "UbicacionOriginalesDestino";

        // Valores actuales (uno solo).
        public string? Tipo { get; set; }
        public string? Otro { get; set; }
        public string? Lugar { get; set; }

        // Corrección de BL: no se ofrece el valor vigente como "nuevo".
        public string? ExcluirTipo { get; set; }
        public string? ExcluirLugar { get; set; }

        public bool TipoObligatorio { get; set; }
        public string TituloTipo { get; set; } = "Tipo de emisión";
        public string TituloLugar { get; set; } = "Lugar de emisión";
        // Para "Cambio lugar de emisión": se muestra solo el lugar.
        public bool SoloLugar { get; set; }
        // Campos deshabilitados al cargar (los activa el JS de Corrección de BL).
        public bool Deshabilitado { get; set; }
        // Estilo visual: "erp" (Detalle) o "create".
        public string Estilo { get; set; } = "erp";
    }
}
