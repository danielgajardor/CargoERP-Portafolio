using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    // Empresas de transporte para el selector de Operaciones (oct-2026): todos los
    // proveedores activos de la tabla Proveedores menos los depósitos de almacenamiento
    // (IdTipoProveedor 12: Las Cadenas, Honu, Küpal, TLS, Castellini, Don Beto, Soto,
    // Vigovan...). Antes solo se listaban los tipos 2 y 10, y los transportistas creados
    // desde Comercial (que quedaban con tipo 0) no aparecían.
    public static class TransportistaHelper
    {
        public const int TipoDeposito = 12;

        public static async Task<List<Proveedore>> ListarAsync(AtilsonContext context)
        {
            var raw = await context.Proveedores.AsNoTracking()
                .Where(p => p.Activo == 1 && p.IdTipoProveedor != TipoDeposito)
                .ToListAsync();
            return raw
                .Where(p => !string.IsNullOrWhiteSpace(p.NombreProveedor))
                .GroupBy(p => p.NombreProveedor.Trim().ToUpperInvariant())
                // Si hay duplicados por nombre, el que tiene RUT y correo.
                .Select(g => g.OrderByDescending(p => !string.IsNullOrWhiteSpace(p.Rut)).ThenByDescending(p => !string.IsNullOrWhiteSpace(p.CorreoOperativo)).ThenBy(p => p.Id).First())
                .OrderBy(p => p.NombreProveedor.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
