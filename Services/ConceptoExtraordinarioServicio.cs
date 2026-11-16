using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Services
{
    /// <summary>
    /// Alta de un concepto extraordinario (nov-2026), común a Control Financiero ("Agregar
    /// concepto") y Datos Históricos ("Creación de operación"). Valida los datos y agrega la
    /// línea al contexto; quien llama hace SaveChanges.
    /// </summary>
    public static class ConceptoExtraordinarioServicio
    {
        public sealed record Datos(string? Tipo, string? Seccion, string? Concepto, int? IdProveedor, string? ProveedorTexto,
            string? Monto, string? Moneda, string? Iva, string? Documentos, string? ProveedorOrigen, string? NumeroOrigen);

        public sealed class Resultado
        {
            public bool Ok { get; init; }
            public string? Mensaje { get; init; }
            public List<string> Repetidos { get; init; } = new();
            public TransaccionesFinanciera? Linea { get; init; }
            public static Resultado Error(string m) => new() { Ok = false, Mensaje = m };
        }

        public static async Task<Resultado> CrearAsync(AtilsonContext db, Operacione op, Datos d, string origen, string? usuario, bool confirmarRespaldoRepetido)
        {
            string tipo = (d.Tipo ?? "").Trim().ToUpperInvariant();
            if (tipo is not ("EGRESO" or "INGRESO")) return Resultado.Error("Indica si es un costo (egreso) o un cobro (ingreso).");

            string? grupo = ConceptoExtraordinarioHelper.GrupoDeSeccion(d.Seccion);
            if (grupo == null) return Resultado.Error("Elige la sección: Marítimo, Terrestre, Documental, Almacenaje u Otros.");

            string concepto = (d.Concepto ?? "").Trim();
            if (concepto.Length < 2) return Resultado.Error("Escribe el concepto.");
            if (concepto.Length > 200) concepto = concepto[..200];

            if (!MontoHelper.TryParse(d.Monto, out decimal monto) || monto == 0m)
                return Resultado.Error("Ingresa el monto neto (ej. 45.000 o 120,50).");

            string moneda = (d.Moneda ?? "").Trim().ToUpperInvariant();
            if (moneda is not ("CLP" or "USD")) return Resultado.Error("Elige la moneda: CLP o USD.");

            string ivaTxt = (d.Iva ?? "").Trim().ToUpperInvariant();
            if (ivaTxt is not ("AFECTO" or "EXENTO")) return Resultado.Error("Indica si es afecto a IVA o exento.");

            Proveedore? proveedor = null;
            string? proveedorTexto = string.IsNullOrWhiteSpace(d.ProveedorTexto) ? null : d.ProveedorTexto.Trim().ToUpperInvariant();
            if (d.IdProveedor is > 0)
            {
                proveedor = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == d.IdProveedor.Value);
                if (proveedor == null) return Resultado.Error("El proveedor elegido no existe.");
            }
            if (tipo == "EGRESO" && proveedor == null && proveedorTexto == null)
                return Resultado.Error("Elige el proveedor (o escribe su nombre si no está en el maestro).");

            string? numOrigen = string.IsNullOrWhiteSpace(d.NumeroOrigen) ? null : d.NumeroOrigen.Trim().ToUpperInvariant();
            string? provOrigen = string.IsNullOrWhiteSpace(d.ProveedorOrigen) ? null : d.ProveedorOrigen.Trim().ToUpperInvariant();
            if (numOrigen != null && provOrigen == null) return Resultado.Error("Indica el proveedor de origen del documento de respaldo (ej. TPS, STI).");
            if (tipo == "EGRESO" && numOrigen != null && !confirmarRespaldoRepetido)
            {
                var repetidos = await ConceptoExtraordinarioHelper.RespaldosRepetidosAsync(db, numOrigen, provOrigen, Array.Empty<int>());
                if (repetidos.Count > 0)
                    return new Resultado { Ok = false, Mensaje = "La factura de respaldo ya está registrada en otro egreso.", Repetidos = repetidos };
            }

            var ahora = DateTime.Now;
            var linea = new TransaccionesFinanciera
            {
                IdOperacion = op.Id,
                GrupoCobro = grupo,
                TipoMovimiento = tipo,
                ResponsablePago = tipo == "INGRESO" ? FinanzasSyncService.ResponsabilidadValores.Cliente : FinanzasSyncService.ResponsabilidadValores.Atilson,
                Responsable = tipo == "INGRESO" ? FinanzasSyncService.ResponsabilidadValores.Cliente : FinanzasSyncService.ResponsabilidadValores.Atilson,
                Concepto = concepto,
                IdProveedor = tipo == "EGRESO" ? proveedor?.Id : null,
                IdCliente = tipo == "INGRESO" ? op.IdCliente : null,
                ContraparteHistorica = tipo == "EGRESO" && proveedor == null ? (proveedorTexto!.Length > 200 ? proveedorTexto[..200] : proveedorTexto) : null,
                MontoNeto = monto,
                ProfitHandling = 0m,
                Moneda = moneda,
                IvaExento = ivaTxt == "EXENTO",
                EstadoFila = "PROVISIÓN",
                NumeroDocumento = ConceptoExtraordinarioHelper.UnirDocumentos(d.Documentos),
                ProveedorOrigen = tipo == "EGRESO" && numOrigen != null ? (provOrigen!.Length > 150 ? provOrigen[..150] : provOrigen) : null,
                NumeroDocumentoOrigen = tipo == "EGRESO" ? (numOrigen != null && numOrigen.Length > 100 ? numOrigen[..100] : numOrigen) : null,
                TarifaManual = true,
                JustificacionManual = ConceptoExtraordinarioHelper.Justificacion(origen, usuario),
                FechaCreacion = ahora,
                UsuarioCreador = usuario ?? "Finanzas",
                FechaModificacion = ahora,
                UsuarioModificador = usuario ?? "Finanzas"
            };
            db.TransaccionesFinancieras.Add(linea);
            return new Resultado { Ok = true, Linea = linea };
        }
    }
}
