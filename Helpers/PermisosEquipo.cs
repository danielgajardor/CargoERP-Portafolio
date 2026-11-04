using System;
using System.Linq;
using System.Security.Claims;

namespace AtilsonCargoSpa.Helpers
{
    // Roles del equipo Atilson y quién administra cuentas (oct-2026).
    public static class PermisosEquipo
    {
        public const string RolAdministrador = "Administrador";

        /// <summary>Roles que un administrador puede asignar a una persona del equipo.</summary>
        public static readonly string[] RolesAsignables = { "Operaciones", "Ventas", "Finanzas", "Informático", RolAdministrador };

        /// <summary>Roles con acceso a las pantallas internas (policy SoloInternos).</summary>
        public static readonly string[] RolesInternos = { "Informático", "Ventas", "Finanzas", "Operaciones", RolAdministrador };

        /// <summary>Áreas que la persona puede indicar al pedir su cuenta (solo referencia para el administrador).</summary>
        public static readonly string[] Areas = { "Operaciones", "Ventas / Comercial", "Finanzas", "Informática", "Gerencia" };

        /// <summary>Jefaturas: ven Finanzas y Comercial en el menú. Antes estaba solo en _Layout.</summary>
        public static readonly string[] Jefaturas = { "Daniel Gajardo", "Cristian Prada", "Jane Diaz", "Ignacio Reyes", "Sistema", "Admin" };

        public static bool EsJefatura(ClaimsPrincipal? user) =>
            user?.Identity?.IsAuthenticated == true
            && (Jefaturas.Contains(user.Identity!.Name ?? "") || user.IsInRole(RolAdministrador));

        /// <summary>Puede validar cuentas y asignar roles: rol Administrador o Informático, o una jefatura.</summary>
        public static bool EsAdmin(ClaimsPrincipal? user) =>
            user?.Identity?.IsAuthenticated == true && !user.IsInRole("Cliente")
            && (user.IsInRole(RolAdministrador) || user.IsInRole("Informático") || Jefaturas.Contains(user.Identity!.Name ?? ""));

        public static string? NormalizarRol(string? rol) =>
            RolesAsignables.FirstOrDefault(r => string.Equals(r, rol?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
