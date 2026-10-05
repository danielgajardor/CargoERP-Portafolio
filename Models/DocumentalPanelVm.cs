using AtilsonCargoSpa.Pages.Operaciones;
using System.Collections.Generic;

namespace AtilsonCargoSpa.Models
{
    public class DocumentalPanelVm
    {
        public Operacione Op { get; set; } = null!;
        public OperacionesDocumentale? Doc { get; set; }
        public int? IdUnidadTecnica { get; set; }

        // "v3-{op.Id}" para el contenedor principal, "sub-{u.Id}" para cada adicional.
        // Debe coincidir EXACTO con el esquema que ya usa el JS (openProTab, PANE_ID_MAP).
        public string IdSuffix { get; set; } = null!;

        public string? Label { get; set; }
        public bool MostrarLabelEnTitulos { get; set; }

        public bool ReqTerrestre { get; set; }

        public IOperacionesConsultaPageModel PageModel { get; set; } = null!;

        // Lista de todos los contenedores del booking (principal + Unidadestecnicas),
        // con su propio registro documental si ya existe. Cada sección de certificado
        // (Origen/Fito/Sanitario/Captura) la usa para renderizar en modo Individual —
        // un bloque por contenedor — sin depender de que Index.cshtml duplique el
        // panel completo. Aduana, Documentos Base y Subcerts ignoran esta lista y
        // siguen usando Doc/IdUnidadTecnica de arriba (siempre únicos por booking).
        public List<ContenedorDocVm> Contenedores { get; set; } = new();
    }

    public class ContenedorDocVm
    {
        public string Suffix { get; set; } = "";
        public string Label { get; set; } = "";
        public int? IdUnidad { get; set; }
        public OperacionesDocumentale? Doc { get; set; }
    }
}