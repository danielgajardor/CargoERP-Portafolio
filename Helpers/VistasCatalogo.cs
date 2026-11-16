using System;
using System.Collections.Generic;
using System.Linq;

namespace AtilsonCargoSpa.Helpers
{
    // =====================================================================================
    // Vistas por usuario (13-nov-2026). Catálogo de las vistas del menú lateral, agrupadas por
    // módulo, que el administrador marca por persona en Maestros › Equipo › Permisos por usuario.
    // Sin selección propia, cada persona ve lo que da su rol (PermisosEquipo.VistaPorRol).
    // El Dashboard y Equipo no se configuran aquí (Dashboard: todos; Equipo: administradores).
    // =====================================================================================
    public static class VistasCatalogo
    {
        public sealed record Vista(string Clave, string Nombre);
        public sealed record Modulo(string Clave, string Nombre, string Icono, Vista[] Vistas);

        public static readonly Modulo[] Modulos =
        {
            new("operaciones", "Operaciones", "bi-briefcase-fill", new Vista[]
            {
                new("operaciones.mainboard", "Main Board (bookings, detalle y creación)"),
                new("operaciones.cotizaciones", "Cotizaciones"),
                new("operaciones.itinerarios", "Itinerarios de Naves"),
            }),
            new("finanzas", "Finanzas", "bi-currency-dollar", new Vista[]
            {
                new("finanzas.mainboard", "Main Board"),
                new("finanzas.control", "Control Financiero (con sets de cobro y liquidaciones)"),
                new("finanzas.pagos", "Pagos"),
                new("finanzas.respaldos", "Respaldos"),
                new("finanzas.historicos", "Datos Históricos"),
                new("finanzas.cobranza", "Cobranza"),
                new("finanzas.ordenes", "Órdenes de Compra"),
                new("finanzas.consolidado", "Consolidado Cliente"),
            }),
            new("comercial", "Comercial", "bi-tags-fill", new Vista[]
            {
                new("comercial.tarifas", "Tarifas (todas)"),
                new("comercial.transporte", "Tarifas de transporte (solo costos de transportistas)"),
                new("comercial.acuerdos", "Acuerdos por Booking"),
            }),
            new("integraciones", "Integraciones", "bi-plug-fill", new Vista[]
            {
                new("integraciones", "Integraciones"),
            }),
            new("equipo", "Clientes y equipo", "bi-people-fill", new Vista[]
            {
                new("equipo.soporte", "Soporte clientes"),
                new("equipo.chat", "Chat del equipo"),
                new("equipo.directorio", "Directorio del equipo"),
            }),
            new("maestros", "Configuración base (Maestros)", "bi-buildings-fill", new Vista[]
            {
                new("maestros.clientes", "Mis Clientes"),
                new("maestros.solicitudes", "Solicitudes de registro"),
                new("maestros.plantas", "Plantas"),
                new("maestros.depositos", "Depósitos"),
                new("maestros.proveedores", "Transportistas y depósitos de almacenamiento"),
                new("maestros.agencias", "Agencias de aduana"),
                new("maestros.navieras", "Navieras"),
                new("maestros.naves", "Naves"),
                new("maestros.puertos", "Puertos"),
                new("maestros.ciudades", "Ciudades"),
            }),
        };

        public static IEnumerable<string> Todas => Modulos.SelectMany(m => m.Vistas).Select(v => v.Clave);

        public static bool Existe(string? clave) => Todas.Contains(clave ?? "", StringComparer.Ordinal);

        /// <summary>
        /// Vistas que habilitan una página (basta una). null = la página no se controla por vista.
        /// </summary>
        public static string[]? ClavesDePagina(string? ruta)
        {
            string r = ruta ?? "";
            bool P(string prefijo) => r.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase);

            if (P("/Operaciones/Cotizaciones")) return new[] { "operaciones.cotizaciones" };
            if (P("/Operaciones/Itinerarios")) return new[] { "operaciones.itinerarios" };
            // PDFs que también se abren desde Finanzas o Cotizaciones.
            if (P("/Operaciones/OrdenCompraPdf")) return new[] { "operaciones.mainboard", "finanzas.control", "finanzas.ordenes" };
            if (P("/Operaciones/ProformaPdf") || P("/Operaciones/ReservaPdf")) return new[] { "operaciones.mainboard", "operaciones.cotizaciones" };
            if (P("/Operaciones/")) return new[] { "operaciones.mainboard" };

            if (P("/Finanzas/Index")) return new[] { "finanzas.mainboard" };
            if (P("/Finanzas/Pagos")) return new[] { "finanzas.pagos" };
            if (P("/Finanzas/Respaldos")) return new[] { "finanzas.respaldos" };
            if (P("/Finanzas/DatosHistoricos")) return new[] { "finanzas.historicos" };
            if (P("/Finanzas/Cobranza")) return new[] { "finanzas.cobranza" };
            if (P("/Finanzas/OrdenesCompra")) return new[] { "finanzas.ordenes" };
            if (P("/Finanzas/ConsolidadoCliente")) return new[] { "finanzas.consolidado" };
            if (P("/Finanzas/")) return new[] { "finanzas.control" }; // Control Financiero, Edit, sets, nota de cobro

            if (P("/Comercial/AcuerdosOperativos")) return new[] { "comercial.acuerdos" };
            if (P("/Comercial/")) return new[] { "comercial.tarifas", "comercial.transporte" };

            if (P("/Integraciones/") || P("/App/Integraciones/")) return new[] { "integraciones" };

            if (P("/App/Soporte")) return new[] { "equipo.soporte" };
            if (P("/App/Chat")) return new[] { "equipo.chat" };
            if (P("/App/Directorio")) return new[] { "equipo.directorio" };

            if (P("/App/Clientes/Solicitudes")) return new[] { "maestros.solicitudes" };
            if (P("/App/Clientes/")) return new[] { "maestros.clientes" };
            if (P("/App/Plantas/")) return new[] { "maestros.plantas" };
            if (P("/App/Depositos/")) return new[] { "maestros.depositos" };
            if (P("/App/Proveedores/")) return new[] { "maestros.proveedores" };
            if (P("/App/Agencias/")) return new[] { "maestros.agencias" };
            if (P("/App/Navieras/")) return new[] { "maestros.navieras" };
            if (P("/App/Naves/")) return new[] { "maestros.naves" };
            if (P("/App/Puertos/")) return new[] { "maestros.puertos" };
            if (P("/App/Ciudades/")) return new[] { "maestros.ciudades" };
            return null;
        }
    }
}
