using System;
using System.Collections.Generic;

namespace AtilsonCargoSpa.Services
{
    public static class CourierZonas
    {
        public static readonly Dictionary<string, string> PaisAZona = new(StringComparer.OrdinalIgnoreCase)
        {
            // Sudamérica
            { "CHILE", "SUDAMERICA" }, { "PERU", "SUDAMERICA" }, { "ARGENTINA", "SUDAMERICA" },
            { "BRASIL", "SUDAMERICA" }, { "COLOMBIA", "SUDAMERICA" }, { "ECUADOR", "SUDAMERICA" },
            { "BOLIVIA", "SUDAMERICA" }, { "URUGUAY", "SUDAMERICA" }, { "PARAGUAY", "SUDAMERICA" },
            { "VENEZUELA", "SUDAMERICA" },
            // Norteamérica y Caribe
            { "ESTADOS UNIDOS", "NORTE AMERICA Y CARIBE" }, { "CANADA", "NORTE AMERICA Y CARIBE" },
            { "MEXICO", "NORTE AMERICA Y CARIBE" }, { "REPUBLICA DOMINICANA", "NORTE AMERICA Y CARIBE" },
            { "HAITI", "NORTE AMERICA Y CARIBE" }, { "JAMAICA", "NORTE AMERICA Y CARIBE" },
            { "COSTA RICA", "NORTE AMERICA Y CARIBE" }, { "GUATEMALA", "NORTE AMERICA Y CARIBE" },
            { "EL SALVADOR", "NORTE AMERICA Y CARIBE" }, { "HONDURAS", "NORTE AMERICA Y CARIBE" },
            { "NICARAGUA", "NORTE AMERICA Y CARIBE" }, { "PANAMA", "NORTE AMERICA Y CARIBE" },
            // Europa
            { "ESPAÑA", "EUROPA" }, { "FRANCIA", "EUROPA" }, { "ALEMANIA", "EUROPA" },
            { "ITALIA", "EUROPA" }, { "REINO UNIDO", "EUROPA" }, { "PAISES BAJOS", "EUROPA" },
            { "PORTUGAL", "EUROPA" }, { "DINAMARCA", "EUROPA" }, { "BELGICA", "EUROPA" },
            { "POLONIA", "EUROPA" }, { "IRLANDA", "EUROPA" }, { "CROACIA", "EUROPA" },
            { "ESTONIA", "EUROPA" }, { "LETONIA", "EUROPA" }, { "LITUANIA", "EUROPA" },
            { "FINLANDIA", "EUROPA" }, { "NORUEGA", "EUROPA" }, { "SUECIA", "EUROPA" },
            { "ALBANIA", "EUROPA" }, { "RUSIA", "EUROPA" },
            // Asia / India / Oceanía / Medio Oriente / África (agrupados como "ASIA" según tu tarifario)
            { "CHINA", "ASIA" }, { "JAPON", "ASIA" }, { "INDIA", "ASIA" },
            { "COREA DEL SUR", "ASIA" }, { "VIETNAM", "ASIA" }, { "TAILANDIA", "ASIA" },
            { "HONG KONG", "ASIA" }, { "TAIWAN", "ASIA" }, { "SINGAPUR", "ASIA" },
            { "INDONESIA", "ASIA" }, { "EMIRATOS ARABES UNIDOS", "ASIA" }, { "ARABIA SAUDITA", "ASIA" },
            { "TURQUIA", "ASIA" }, { "IRAK", "ASIA" }, { "JORDANIA", "ASIA" },
            { "SIRIA", "ASIA" }, { "LIBANO", "ASIA" }, { "KUWAIT", "ASIA" },
            { "GEORGIA", "ASIA" }, { "NUEVA ZELANDA", "ASIA" }, { "MARRUECOS", "ASIA" },
            { "ARGELIA", "ASIA" }, { "CAMERUN", "ASIA" }, { "CONGO", "ASIA" },
            { "COSTA DE MARFIL", "ASIA" },
        };

        public static string? ObtenerZona(string? pais)
            => string.IsNullOrWhiteSpace(pais) ? null : (PaisAZona.TryGetValue(pais.Trim(), out var z) ? z : null);
    }
}