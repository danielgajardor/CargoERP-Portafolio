using System.Collections.Generic;
using System.Linq;
using AtilsonCargoSpa.Models;

namespace AtilsonCargoSpa.Helpers
{
    // Empresa de courier (oct-2026): DHL, FedEx o Chilexpress.
    //  - Operaciones la asigna en la pestaña Courier (OperacionesDocumentale.EmpresaCourier).
    //  - Comercial define tarifas por empresa, tanto de costo (TarifasMaestras,
    //    Categoria "Courier") como de venta (TarifasClientes, GrupoCobro "Courier").
    //    En esas filas la empresa se guarda en la columna "Clasificacion", que
    //    para Courier no se usaba (solo la usan los trámites Documentales). Así no
    //    hubo que cambiar el esquema de la base.
    //  - Finanzas toma la tarifa de la empresa asignada; si no hay una específica,
    //    usa la tarifa del concepto sin empresa (las que ya existían).
    public static class CourierHelper
    {
        public static readonly (string Valor, string Texto)[] Empresas =
        {
            ("DHL", "DHL"),
            ("FEDEX", "FedEx"),
            ("CHILEXPRESS", "Chilexpress")
        };

        // Contrato bajo el que se mueve el envío (oct-2026). Se guarda en columnas que
        // Courier no usaba: TarifasMaestras.Codigo (costo) y TarifasClientes.ZonaPlanta (venta).
        public static readonly (string Valor, string Texto)[] Contratos =
        {
            ("STEMBEX", "Contrato Stembex"),
            ("PROPIO", "Contrato propio"),
            ("OTRA_AGA", "Otra AGA")
        };

        public static string? NormalizarContrato(string? contrato)
        {
            var v = (contrato ?? "").Trim().ToUpperInvariant().Replace(" ", "_");
            if (v == "CONTRATO_STEMBEX") v = "STEMBEX";
            if (v == "CONTRATO_PROPIO") v = "PROPIO";
            if (v == "OTRA_AGENCIA" || v == "AGA") v = "OTRA_AGA";
            return Contratos.Any(c => c.Valor == v) ? v : null;
        }

        public static string TextoContrato(string? contrato)
        {
            var v = NormalizarContrato(contrato);
            return v != null ? Contratos.First(c => c.Valor == v).Texto : "—";
        }

        public const string ConceptoNacional = "COURIER NACIONAL";

        public static readonly string[] ConceptosInternacionales =
        {
            "COURIER INTERNACIONAL SUDAMERICA",
            "COURIER INTERNACIONAL NORTE AMERICA Y CARIBE",
            "COURIER INTERNACIONAL EUROPA",
            "COURIER INTERNACIONAL ASIA"
        };

        public static bool EsInternacional(string? concepto) => (concepto ?? "").ToUpperInvariant().Contains("INTERNACIONAL");
        public static bool EsNacional(string? concepto) => (concepto ?? "").ToUpperInvariant().Contains("NACIONAL") && !EsInternacional(concepto);

        // Valor guardado -> código (acepta el "CHILEEXPRESS" que se guardaba antes).
        public static string? Normalizar(string? empresa)
        {
            var v = (empresa ?? "").Trim().ToUpperInvariant().Replace(" ", "");
            if (v == "CHILEEXPRESS") v = "CHILEXPRESS";
            return Empresas.Any(e => e.Valor == v) ? v : null;
        }

        public static string Texto(string? empresa)
        {
            var v = Normalizar(empresa);
            if (v != null) return Empresas.First(e => e.Valor == v).Texto;
            return string.IsNullOrWhiteSpace(empresa) ? "Todas" : empresa!;
        }

        // Tarifa de costo para el concepto y la empresa: primero la de esa empresa,
        // si no, la general (sin empresa).
        public static TarifasMaestra? ElegirTarifaCosto(IEnumerable<TarifasMaestra> tarifas, string concepto, string? empresa)
        {
            var delConcepto = tarifas
                .Where(t => t.EsActiva && t.Categoria == "Courier" && (t.Concepto ?? "").ToUpperInvariant() == concepto.ToUpperInvariant())
                .ToList();
            var emp = Normalizar(empresa);
            return (emp != null ? delConcepto.FirstOrDefault(t => Normalizar(t.Clasificacion) == emp) : null)
                ?? delConcepto.FirstOrDefault(t => string.IsNullOrWhiteSpace(t.Clasificacion))
                ?? (emp == null ? delConcepto.FirstOrDefault() : null);
        }
    }
}
