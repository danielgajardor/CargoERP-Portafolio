using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AtilsonCargoSpa.Models
{
    // Pestaña "RF Fresco Collect" de Comercial (proveedores y clientes), oct-2026.
    public class TarifasCollectPanelVm
    {
        public string Tipo { get; set; } = TarifaCollect.TipoNaviera;
        public List<TarifaCollect> Tarifas { get; set; } = new();
        // Tarifas de las navieras (para mostrar el profit en la vista de clientes).
        public List<TarifaCollect> TarifasNaviera { get; set; } = new();
        public SelectList Navieras { get; set; } = default!;
        public SelectList? Clientes { get; set; }
        public SelectList Puertos { get; set; } = default!;
        public Dictionary<int, string> NombresNavieras { get; set; } = new();
    }
}
