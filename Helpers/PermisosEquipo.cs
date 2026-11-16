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

        /// <summary>
        /// Acceso total por persona (además del rol Administrador). Minuta de roles (13-nov-2026):
        /// las jefaturas con acceso total. Un usuario de operaciones pasa a Operaciones (sin Finanzas); su acceso a
        /// las tarifas de transporte es un permiso propio (Usuarios.PermisoTarifasTransporte).
        /// </summary>
        public static readonly string[] Jefaturas = { "Gerente Demo", "Jefe Operaciones Demo", "Jefa Finanzas Demo", "Sistema", "Admin" };

        /// <summary>Claim del permiso por persona de tarifas de transporte (se agrega al iniciar sesión).</summary>
        public const string ClaimPermiso = "Permiso";
        public const string PermisoTarifasTransporte = "TarifasTransporte";

        private static bool Interno(ClaimsPrincipal? user) =>
            user?.Identity?.IsAuthenticated == true && !user.IsInRole("Cliente");

        /// <summary>Jefaturas: ven Finanzas y Comercial en el menú (acceso total).</summary>
        public static bool EsJefatura(ClaimsPrincipal? user) =>
            Interno(user) && (Jefaturas.Contains(user!.Identity!.Name ?? "") || user.IsInRole(RolAdministrador));

        // -------------------------------------------------------------------------------------
        // Permisos por módulo (13-nov-2026):
        //   Administrador / Informático / jefaturas → todo.
        //   Finanzas → todo (incluye Finanzas y Comercial).
        //   Ventas (Comercial) → todo menos Finanzas.
        //   Operaciones → gestión operativa: sin Finanzas, sin Tarifas ni Cotizaciones.
        //   Permiso "Tarifas de transporte" (por persona) → costos de transportistas en Comercial.
        // -------------------------------------------------------------------------------------
        //
        // Vistas por usuario (13-nov-2026): si el administrador eligió vistas para la persona
        // (Maestros › Equipo › Permisos por usuario), mandan esas (claims "Vista", ver
        // Services/VistasClaimsTransformation.cs); si no, lo de su rol, como hasta ahora.
        // -------------------------------------------------------------------------------------
        public const string ClaimVista = "Vista";
        public const string ClaimVistasPersonalizadas = "VistasPersonalizadas";

        private static bool Personalizado(ClaimsPrincipal? user) =>
            user?.HasClaim(ClaimVistasPersonalizadas, "1") == true;

        private static bool TieneVista(ClaimsPrincipal? user, Func<string, bool> condicion) =>
            user?.FindAll(ClaimVista).Any(c => condicion(c.Value)) == true;

        public static bool PuedeFinanzasPorRol(ClaimsPrincipal? user) =>
            Interno(user) && (EsJefatura(user) || user!.IsInRole("Informático") || user.IsInRole("Finanzas"));

        public static bool PuedeComercialPorRol(ClaimsPrincipal? user) =>
            PuedeFinanzasPorRol(user) || (Interno(user) && user!.IsInRole("Ventas"));

        /// <summary>Lo que da el rol (y el permiso de tarifas de transporte) para una vista del catálogo.</summary>
        public static bool VistaPorRol(ClaimsPrincipal? user, string clave)
        {
            if (!Interno(user)) return false;
            if (clave.StartsWith("finanzas.", StringComparison.Ordinal)) return PuedeFinanzasPorRol(user);
            if (clave == "comercial.transporte") return PuedeComercialPorRol(user) || TienePermisoTarifasTransporte(user);
            if (clave == "operaciones.cotizaciones" || clave.StartsWith("comercial.", StringComparison.Ordinal)) return PuedeComercialPorRol(user);
            return true;
        }

        /// <summary>¿La persona puede entrar a esta vista del catálogo (Helpers/VistasCatalogo)?</summary>
        public static bool PuedeVer(ClaimsPrincipal? user, string clave)
        {
            if (!Interno(user)) return false;
            return Personalizado(user) ? user!.HasClaim(ClaimVista, clave) : VistaPorRol(user, clave);
        }

        /// <summary>Módulo Finanzas (alguna de sus vistas).</summary>
        public static bool PuedeFinanzas(ClaimsPrincipal? user) =>
            Personalizado(user) ? Interno(user) && TieneVista(user, v => v.StartsWith("finanzas.", StringComparison.Ordinal)) : PuedeFinanzasPorRol(user);

        /// <summary>Comercial completo (o, con vistas elegidas, alguna vista comercial o Cotizaciones).</summary>
        public static bool PuedeComercial(ClaimsPrincipal? user) =>
            Personalizado(user)
                ? Interno(user) && TieneVista(user, v => v == "comercial.tarifas" || v == "comercial.acuerdos" || v == "operaciones.cotizaciones")
                : PuedeComercialPorRol(user);

        public static bool PuedeCotizaciones(ClaimsPrincipal? user) => PuedeVer(user, "operaciones.cotizaciones");

        public static bool TienePermisoTarifasTransporte(ClaimsPrincipal? user) =>
            Interno(user) && user!.HasClaim(ClaimPermiso, PermisoTarifasTransporte);

        /// <summary>Entra a Comercial › Tarifas (completo, o solo transporte).</summary>
        public static bool PuedeTarifas(ClaimsPrincipal? user) =>
            PuedeVer(user, "comercial.tarifas") || PuedeVer(user, "comercial.transporte");

        /// <summary>Ve solo los costos de transportistas en Comercial.</summary>
        public static bool SoloTarifasTransporte(ClaimsPrincipal? user) =>
            !PuedeVer(user, "comercial.tarifas") && PuedeVer(user, "comercial.transporte");

        /// <summary>Puede validar cuentas y asignar roles: rol Administrador o Informático, o una jefatura.</summary>
        public static bool EsAdmin(ClaimsPrincipal? user) =>
            user?.Identity?.IsAuthenticated == true && !user.IsInRole("Cliente")
            && (user.IsInRole(RolAdministrador) || user.IsInRole("Informático") || Jefaturas.Contains(user.Identity!.Name ?? ""));

        public static string? NormalizarRol(string? rol) =>
            RolesAsignables.FirstOrDefault(r => string.Equals(r, rol?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
