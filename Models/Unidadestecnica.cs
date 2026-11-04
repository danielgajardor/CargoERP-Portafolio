using System;
using System.Collections.Generic;

namespace AtilsonCargoSpa.Models;

public partial class Unidadestecnica
{
    public int Id { get; set; }

    public int IdOperacion { get; set; }

    public string? NroContenedor { get; set; }

    public string? SelloNaviera { get; set; }

    public decimal? Tara { get; set; }

    public decimal? PesoCarga { get; set; }

    public decimal? VgmTotal { get; set; }

    public decimal? Temperatura { get; set; }

    public int? Humedad { get; set; }

    public int? Ventilacion { get; set; }

    public byte? AtmosferaControlada { get; set; }

    public DateTime FechaCreacion { get; set; }

    public string UsuarioCreador { get; set; } = null!;

    public DateTime FechaModificacion { get; set; }

    public string UsuarioModificador { get; set; } = null!;

    public string? TipoAtmosfera { get; set; }

    public decimal? NivelO2 { get; set; }

    public decimal? NivelCo2 { get; set; }

    public int? IdTipoCarga { get; set; }

    public string? TipoContenedor { get; set; }

    public string? Commodity { get; set; }

    public string? CondicionReefer { get; set; }

    public string? MarcaAc { get; set; }

    // Mismo patrón que Operacione.cs: ciclo de cierre en destino con fecha real
    // + usuario/fecha de registro para cada hito (Arribado → Retirado → Vacío
    // Devuelto), por contenedor cuando el booking tiene más de uno.
    public DateTime? FechaArriboDestino { get; set; }
    public string? UsuarioArriboDestino { get; set; }
    public DateTime? FechaRetiroDestino { get; set; }
    public string? UsuarioRetiroDestino { get; set; }
    public DateTime? FechaDevolucionVacio { get; set; }
    public string? UsuarioDevolucionVacio { get; set; }

    public string? EvidenciaContenedor { get; set; }
    public string? NumeroSello { get; set; }
    public string? FotoContenedor { get; set; }
    public string? FotoSello { get; set; }

    public string? Estado { get; set; }
    public string? EstadoLar { get; set; }
    public DateTime? LateArrival { get; set; }
    public DateTime? ElarDate { get; set; }
    public bool ContenedorIngresado { get; set; }
    public string? EvidenciaLar { get; set; }

    public virtual Operacione IdOperacionNavigation { get; set; } = null!;
}
