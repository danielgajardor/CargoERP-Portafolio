using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AtilsonCargoSpa.Services
{
    // Vistas por usuario (13-nov-2026): en cada petición agrega a la sesión las vistas que el
    // administrador eligió para la persona (claims "Vista"), leídas de UsuariosVistas con caché.
    // Al guardar en Permisos por usuario se borra la caché: aplica sin volver a iniciar sesión.
    public class VistasClaimsTransformation : IClaimsTransformation
    {
        private const string SinSeleccion = "\u0000";
        private readonly AtilsonContext _db;
        private readonly IMemoryCache _cache;

        public VistasClaimsTransformation(AtilsonContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public static string ClaveCache(string correo) => "vistas-usuario:" + (correo ?? "").Trim().ToLowerInvariant();

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity?.IsAuthenticated != true || principal.IsInRole("Cliente")) return principal;
            if (principal.HasClaim(c => c.Type == PermisosEquipo.ClaimVistasPersonalizadas)) return principal;
            string? correo = principal.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(correo)) return principal;

            string vistas = await _cache.GetOrCreateAsync(ClaveCache(correo), async entrada =>
            {
                entrada.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                try
                {
                    var fila = await (from u in _db.Usuarios.AsNoTracking()
                                      join v in _db.UsuariosVistas.AsNoTracking() on u.Id equals v.IdUsuario
                                      where u.Correo == correo
                                      select v.Vistas).FirstOrDefaultAsync();
                    return fila ?? SinSeleccion;
                }
                catch
                {
                    // Tabla aún no creada (falta Sql/2026-11_vistas_por_usuario.sql): según rol.
                    entrada.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                    return SinSeleccion;
                }
            }) ?? SinSeleccion;
            if (vistas == SinSeleccion) return principal;

            var identidad = new ClaimsIdentity();
            identidad.AddClaim(new Claim(PermisosEquipo.ClaimVistasPersonalizadas, "1"));
            foreach (var clave in vistas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
                identidad.AddClaim(new Claim(PermisosEquipo.ClaimVista, clave));
            var copia = principal.Clone();
            copia.AddIdentity(identidad);
            return copia;
        }
    }
}
