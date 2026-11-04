using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    /// <summary>
    /// Plantas y depósitos del cliente en el portal (oct-2026).
    /// - Plantas: solo las del cliente (IdCliente = cliente en sesión).
    /// - Depósitos: los de Atilson (sin cliente) más los propios del cliente.
    /// Lo que registra el cliente se puede usar de inmediato y queda "por validar" para Atilson.
    /// </summary>
    public static class PortalUbicacionesHelper
    {
        public static async Task<List<Planta>> PlantasDelClienteAsync(AtilsonContext ctx, int idCliente, bool soloActivas = true)
        {
            return await ctx.Plantas.AsNoTracking()
                .Include(p => p.Ciudad)
                .Where(p => p.IdCliente == idCliente && (!soloActivas || p.Activo))
                .OrderBy(p => p.Nombre)
                .ToListAsync();
        }

        public static async Task<List<Deposito>> DepositosParaClienteAsync(AtilsonContext ctx, int idCliente)
        {
            return await ctx.Depositos.AsNoTracking()
                .Where(d => d.Activo == 1 && (d.IdCliente == null || d.IdCliente == idCliente))
                .OrderBy(d => d.NombreDeposito)
                .ToListAsync();
        }

        /// <summary>Comunas de Chile para elegir la zona (si no hay país Chile cargado, todas).</summary>
        public static async Task<List<SelectListItem>> ZonasAsync(AtilsonContext ctx, int? seleccionada = null)
        {
            var chile = await ctx.Paises.AsNoTracking()
                .Where(p => p.Nombre == "Chile" || p.Nombre == "CHILE")
                .Select(p => (int?)p.Id).FirstOrDefaultAsync();
            var ciudades = await ctx.Ciudades.AsNoTracking()
                .Where(c => chile == null || c.IdPais == chile)
                .Select(c => new { c.Id, c.Nombre })
                .ToListAsync();
            return ciudades.OrderBy(c => c.Nombre)
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Nombre, Selected = c.Id == seleccionada })
                .ToList();
        }

        /// <summary>Motivo por el que el nombre de planta no se puede usar, o null.</summary>
        public static async Task<string?> ValidarNombrePlantaAsync(AtilsonContext ctx, string nombre, int idCliente, int? excluirId = null)
        {
            string clave = MaestrosAuditoria.Normalizar(nombre);
            var existentes = await ctx.Plantas.AsNoTracking()
                .Where(p => excluirId == null || p.Id != excluirId)
                .Select(p => new { p.Nombre, p.IdCliente, p.Activo })
                .ToListAsync();
            var dup = existentes.FirstOrDefault(p => MaestrosAuditoria.Normalizar(p.Nombre) == clave);
            if (dup == null) return null;
            if (dup.IdCliente == idCliente)
                return dup.Activo ? "Ya tienes una planta con ese nombre." : "Ya registraste una planta con ese nombre y la quitaste; escríbenos para reactivarla.";
            return "Ya existe una planta con ese nombre en Atilson. Si es tuya, pídenos que la asociemos a tu empresa.";
        }

        public static async Task<string?> ValidarNombreDepositoAsync(AtilsonContext ctx, string nombre, int idCliente, int? excluirId = null)
        {
            string clave = MaestrosAuditoria.Normalizar(nombre);
            var existentes = await ctx.Depositos.AsNoTracking()
                .Where(d => excluirId == null || d.Id != excluirId)
                .Select(d => new { d.NombreDeposito, d.IdCliente, d.Activo })
                .ToListAsync();
            var dup = existentes.FirstOrDefault(d => MaestrosAuditoria.Normalizar(d.NombreDeposito) == clave);
            if (dup == null) return null;
            if (dup.IdCliente == idCliente) return "Ya tienes un depósito con ese nombre.";
            if (dup.IdCliente == null && dup.Activo == 1) return "Ese depósito ya está en la lista de Atilson: puedes elegirlo directamente en tus solicitudes.";
            return "Ya existe un depósito con ese nombre. Escríbenos si necesitas usarlo.";
        }

        /// <summary>Crea una planta del cliente, por validar. No guarda: el llamador hace SaveChanges.</summary>
        public static Planta NuevaPlanta(int idCliente, string nombre, int idCiudad, string? direccion, string? urlMaps, string usuario)
        {
            var ahora = MaestrosAuditoria.Ahora();
            string creador = usuario.Length > 90 ? usuario.Substring(0, 90) : usuario;
            return new Planta
            {
                Nombre = nombre,
                IdCiudad = idCiudad,
                IdCliente = idCliente,
                Direccion = direccion,
                UrlGoogleMaps = urlMaps,
                Activo = true,
                RegistradaPorCliente = true,
                Validada = false,
                FechaCreacion = ahora,
                UsuarioCreador = creador + " (portal)",
                FechaModificacion = ahora,
                UsuarioModificador = creador + " (portal)"
            };
        }

        /// <summary>Valor del selector de planta que significa "Otra planta".</summary>
        public const string OpcionOtra = "__OTRA__";

        public sealed class PlantaElegida
        {
            public string? Nombre { get; init; }
            public string? Zona { get; init; }
            public string? Error { get; init; }
            /// <summary>Planta nueva para agregar a "Mis plantas" (por validar); el llamador la guarda.</summary>
            public Planta? Nueva { get; init; }
        }

        /// <summary>
        /// Resuelve la planta de una solicitud del portal: una de las plantas del cliente, u
        /// "Otra" con nombre y zona escritos por el cliente (y, si lo pide, se guarda en sus plantas).
        /// </summary>
        public static async Task<PlantaElegida> ResolverPlantaAsync(AtilsonContext ctx, int idCliente, List<Planta> plantasCliente,
            string? valor, string? otraNombre, int? otraIdCiudad, bool guardarOtra, string usuario)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return new PlantaElegida { Error = "Selecciona la planta de carga (o elige «Otra planta»)." };

            if (valor != OpcionOtra)
            {
                var p = plantasCliente.FirstOrDefault(x => x.Nombre == valor);
                if (p == null) return new PlantaElegida { Error = "La planta elegida no es válida." };
                var zonaPlanta = p.Ciudad?.Nombre ?? await ctx.Ciudades.AsNoTracking().Where(c => c.Id == p.IdCiudad).Select(c => c.Nombre).FirstOrDefaultAsync();
                return new PlantaElegida { Nombre = p.Nombre, Zona = zonaPlanta?.Trim().ToUpper() };
            }

            string? nombre = string.IsNullOrWhiteSpace(otraNombre) ? null : otraNombre.Trim();
            if (nombre == null) return new PlantaElegida { Error = "Escribe el nombre de la otra planta." };
            if (nombre.Length > 200) return new PlantaElegida { Error = "El nombre de la planta admite hasta 200 caracteres." };
            if (otraIdCiudad == null) return new PlantaElegida { Error = "Elige la zona (comuna) de la otra planta." };
            var zona = await ctx.Ciudades.AsNoTracking().Where(c => c.Id == otraIdCiudad).Select(c => c.Nombre).FirstOrDefaultAsync();
            if (zona == null) return new PlantaElegida { Error = "Elige una zona (comuna) de la lista." };

            // Si ya es una de sus plantas, se usa esa.
            var propia = plantasCliente.FirstOrDefault(x => MaestrosAuditoria.Normalizar(x.Nombre) == MaestrosAuditoria.Normalizar(nombre));
            if (propia != null) return new PlantaElegida { Nombre = propia.Nombre, Zona = propia.Ciudad?.Nombre?.Trim().ToUpper() };

            Planta? nueva = null;
            if (guardarOtra)
            {
                var motivo = await ValidarNombrePlantaAsync(ctx, nombre, idCliente);
                if (motivo != null) return new PlantaElegida { Error = "Otra planta: " + motivo };
                nueva = NuevaPlanta(idCliente, nombre, otraIdCiudad.Value, null, null, usuario);
            }
            return new PlantaElegida { Nombre = nombre, Zona = zona.Trim().ToUpper(), Nueva = nueva };
        }
    }
}
