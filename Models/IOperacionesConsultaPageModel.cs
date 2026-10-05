namespace AtilsonCargoSpa.Models
{
    public interface IOperacionesConsultaPageModel
    {
        (string? PagadoPor, string? Responsable, bool PendienteTarifa, string ConceptoReal) ObtenerEstadoConcepto(Operacione op, string conceptoBusqueda);
        (string? PagadoPor, string? Responsable, bool PendienteTarifa, string ConceptoReal) ObtenerEstadoConcepto(Operacione op, string conceptoBusqueda, int idUnidadTecnica);
        bool HayTarifaPendiente(Operacione op, params string[] conceptosBusqueda);
        bool HayTarifaPendiente(Operacione op, int idUnidadTecnica, params string[] conceptosBusqueda);
        (string? PagadoPor, string? Responsable, bool PendienteTarifa, string ConceptoReal) ObtenerEstadoConceptoExacto(Operacione op, string conceptoExacto);
        bool ModalidadCertBloqueada(Operacione op, string certKey);
    }
}