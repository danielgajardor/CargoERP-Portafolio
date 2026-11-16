using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace AtilsonCargoSpa.Pages.Integraciones
{
    public class IndexModel : PageModel
    {
        private readonly IConfiguration _config;
        public IndexModel(IConfiguration config) => _config = config;

        public bool TieneClientId { get; private set; }
        public bool TieneClientSecret { get; private set; }
        public bool CredencialesConfiguradas => TieneClientId && TieneClientSecret;

        public List<ApiEstado> Apis { get; private set; } = new();

        public class ApiEstado
        {
            public string Nombre { get; set; } = "";
            public string Version { get; set; } = "";
            public string Descripcion { get; set; } = "";
            public string Icono { get; set; } = "";
            public bool EndpointConfigurado { get; set; }
        }

        private string? Valor(params string[] claves)
        {
            foreach (var clave in claves)
            {
                var v = _config[clave];
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }

        public void OnGet()
        {
            TieneClientId = Valor("HapagLloyd:ClientId", "Hapag:ClientId") != null;
            TieneClientSecret = Valor("HapagLloyd:ClientSecret", "Hapag:ClientSecret") != null;

            Apis = new()
            {
                new ApiEstado
                {
                    Nombre = "Commercial Schedule",
                    Version = "v3",
                    Descripcion = "Consulta de itinerarios y alternativas de ruta en fase pre-booking.",
                    Icono = "bi-calendar3",
                    EndpointConfigurado = Valor("HapagLloyd:CommercialScheduleUrl", "Hapag:CommercialScheduleUrl") != null
                },
                new ApiEstado
                {
                    Nombre = "Offers",
                    Version = "v4",
                    Descripcion = "Consulta de ofertas y tarifas disponibles para las rutas habilitadas.",
                    Icono = "bi-tags",
                    EndpointConfigurado = Valor("HapagLloyd:OffersUrl", "Hapag:OffersUrl") != null
                },
                new ApiEstado
                {
                    Nombre = "Track & Trace",
                    Version = "v2",
                    Descripcion = "Seguimiento por booking o contenedor y consulta de eventos de transporte.",
                    Icono = "bi-geo-alt",
                    EndpointConfigurado = Valor("HapagLloyd:TrackAndTraceUrl", "Hapag:TrackAndTraceUrl") != null
                }
            };
        }
    }
}
