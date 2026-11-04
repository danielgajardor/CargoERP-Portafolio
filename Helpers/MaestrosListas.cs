using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    /// <summary>
    /// Listas desplegables de los maestros (oct-2026). Ordenadas alfabéticamente, sin
    /// registros inactivos salvo el que ya tiene asignado el registro que se edita.
    /// Todas llevan buscador (data-buscador="si" en la vista).
    /// </summary>
    public static class MaestrosListas
    {
        /// <summary>Ciudades agrupadas por país (Chile primero).</summary>
        public static async Task<List<SelectListItem>> CiudadesAsync(AtilsonContext ctx, int? seleccionada)
        {
            var paises = await ctx.Paises.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
            var ciudades = await ctx.Ciudades.AsNoTracking().Select(c => new { c.Id, c.Nombre, c.IdPais }).ToListAsync();
            var grupos = new Dictionary<int, SelectListGroup>();
            string NombrePais(int id) => paises.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : "Sin país";
            bool EsChile(int id) => MaestrosAuditoria.Normalizar(NombrePais(id)) == "CHILE";

            return ciudades
                .OrderBy(c => EsChile(c.IdPais) ? 0 : 1)
                .ThenBy(c => NombrePais(c.IdPais))
                .ThenBy(c => c.Nombre)
                .Select(c =>
                {
                    if (!grupos.TryGetValue(c.IdPais, out var g)) { g = new SelectListGroup { Name = NombrePais(c.IdPais) }; grupos[c.IdPais] = g; }
                    return new SelectListItem { Value = c.Id.ToString(), Text = c.Nombre, Group = g, Selected = c.Id == seleccionada };
                })
                .ToList();
        }

        public static async Task<List<SelectListItem>> NavierasAsync(AtilsonContext ctx, int? seleccionada)
        {
            return await ctx.Navieras.AsNoTracking()
                .Where(n => (n.Activo == 1 && !n.IsDeleted) || n.Id == seleccionada)
                .OrderBy(n => n.NombreNaviera)
                .Select(n => new SelectListItem { Value = n.Id.ToString(), Text = n.Activo == 1 ? n.NombreNaviera : n.NombreNaviera + " (inactiva)", Selected = n.Id == seleccionada })
                .ToListAsync();
        }

        public static async Task<List<SelectListItem>> ClientesAsync(AtilsonContext ctx, int? seleccionado)
        {
            var lista = await ctx.Clientes.AsNoTracking()
                .Where(c => (c.Activo == 1 && !c.IsDeleted) || c.Id == seleccionado)
                .Select(c => new { c.Id, c.RazonSocial, c.NombreCliente, c.Rut, c.Activo })
                .ToListAsync();
            return lista
                .OrderBy(c => c.RazonSocial)
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.RazonSocial
                           + (!string.IsNullOrWhiteSpace(c.NombreCliente) && MaestrosAuditoria.Normalizar(c.NombreCliente) != MaestrosAuditoria.Normalizar(c.RazonSocial) ? $" ({c.NombreCliente})" : "")
                           + (!string.IsNullOrWhiteSpace(c.Rut) ? $" · {c.Rut}" : "")
                           + (c.Activo == 1 ? "" : " (inactivo)"),
                    Selected = c.Id == seleccionado
                })
                .ToList();
        }

        public static async Task<List<SelectListItem>> PaisesAsync(AtilsonContext ctx, int? seleccionado)
        {
            var lista = await ctx.Paises.AsNoTracking().Select(p => new { p.Id, p.Nombre }).ToListAsync();
            return lista
                .OrderBy(p => MaestrosAuditoria.Normalizar(p.Nombre) == "CHILE" ? 0 : 1)
                .ThenBy(p => p.Nombre)
                .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Nombre, Selected = p.Id == seleccionado })
                .ToList();
        }

        /// <summary>Nombre visible de una ciudad (para el historial).</summary>
        public static async Task<string?> NombreCiudadAsync(AtilsonContext ctx, int? id)
        {
            if (!id.HasValue) return null;
            return await ctx.Ciudades.AsNoTracking().Where(c => c.Id == id.Value).Select(c => c.Nombre).FirstOrDefaultAsync();
        }

        public static async Task<string?> NombreNavieraAsync(AtilsonContext ctx, int? id)
        {
            if (!id.HasValue) return null;
            return await ctx.Navieras.AsNoTracking().Where(n => n.Id == id.Value).Select(n => n.NombreNaviera).FirstOrDefaultAsync();
        }

        public static async Task<string?> NombreClienteAsync(AtilsonContext ctx, int? id)
        {
            if (!id.HasValue) return null;
            return await ctx.Clientes.AsNoTracking().Where(c => c.Id == id.Value).Select(c => c.RazonSocial).FirstOrDefaultAsync();
        }

        public static async Task<string?> NombrePaisAsync(AtilsonContext ctx, int? id)
        {
            if (!id.HasValue) return null;
            return await ctx.Paises.AsNoTracking().Where(p => p.Id == id.Value).Select(p => p.Nombre).FirstOrDefaultAsync();
        }
    }
}
