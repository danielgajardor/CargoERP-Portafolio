using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    // Catálogo ÚNICO de tipos de emisión de B/L (oct-2026).
    //
    // Antes cada pantalla usaba sus propios códigos (Create y Comercial
    // "ORIGINALES_DESTINO", Detalle "ORIGINALES"/"DESTINO"...) y Finanzas no
    // encontraba la tarifa. Ahora todas las pantallas y FinanzasSyncService usan
    // estos códigos:
    //   ORIGINALES, DESTINO, SEA_WAYBILL, TELEX_RELEASE, EXPRESS_RELEASE, OTRO
    // Se elige UNO solo por booking (Operacione.TipoEmisionBl). "Otro" lleva el
    // texto en Operacione.TipoEmisionBlOtro. Solo con Originales se indica el
    // Lugar de emisión (Operacione.UbicacionOriginalesDestino), una de las 4 sedes.
    public static class EmisionBlHelper
    {
        public const string Originales = "ORIGINALES";
        public const string Destino = "DESTINO";
        public const string SeaWaybill = "SEA_WAYBILL";
        public const string TelexRelease = "TELEX_RELEASE";
        public const string ExpressRelease = "EXPRESS_RELEASE";
        public const string Otro = "OTRO";

        public sealed record Opcion(string Codigo, string Etiqueta);

        public static readonly Opcion[] Opciones =
        {
            new(Originales, "Originales"),
            new(Destino, "Destino"),
            new(SeaWaybill, "Seawaybill (SWB)"),
            new(TelexRelease, "Telex Release"),
            new(ExpressRelease, "Express Release"),
            new(Otro, "Otro"),
        };

        // Sedes donde se emiten los Originales.
        public static readonly string[] LugaresEmision = { "VALPARAISO", "SAN ANTONIO", "SANTIAGO", "CORONEL" };

        // Nombre en el concepto financiero ("Emisión BL - X"). Son los mismos textos
        // que ya usaba Finanzas, para que las filas existentes sigan calzando.
        private static readonly Dictionary<string, string> NombreFinanzasFijo = new()
        {
            { Originales, "Originales" },
            { Destino, "Destino" },
            { SeaWaybill, "Sea Waybill" },
            { TelexRelease, "Telex Release" },
            { ExpressRelease, "Express Release" },
        };

        public const string PrefijoConcepto = "Emisión BL - ";

        // Mayúsculas, sin comas, espacios simples, máx. 60.
        public static string NormalizarNombre(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var limpio = Regex.Replace(texto.Replace(",", " ").Trim().ToUpperInvariant(), @"\s+", " ");
            return limpio.Length > 60 ? limpio[..60].Trim() : limpio;
        }

        // Convierte cualquier valor (actual o heredado) a su código.
        public static string Canonico(string? valor)
        {
            var v = NormalizarNombre(valor);
            switch (v)
            {
                case "": return "";
                case "ORIGINALES":
                case "ORIGINALES_DESTINO":       // valor que dejó Create antes (oct-2026)
                case "ORIGINALES - EN DESTINO":
                    return Originales;
                case "DESTINO":
                    return Destino;
                case "SEA_WAYBILL":
                case "SEA WAYBILL":
                case "SEAWAYBILL":
                case "SEAWAYBILL (SWB)":
                case "SWB":
                    return SeaWaybill;
                case "TELEX_RELEASE":
                case "TELEX RELEASE":
                    return TelexRelease;
                case "EXPRESS_RELEASE":
                case "EXPRESS RELEASE":
                    return ExpressRelease;
                case "OTRO":
                    return Otro;
                default:
                    return v;
            }
        }

        public static bool EsCodigo(string codigo) => Opciones.Any(o => o.Codigo == codigo);

        // El tipo vigente (uno solo). Si quedó una lista de antes, se toma el primero.
        public static string? TipoActual(string? guardado)
        {
            foreach (var parte in (guardado ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var c = Canonico(parte);
                if (EsCodigo(c)) return c;
            }
            return null;
        }

        public static string Etiqueta(string? codigo)
        {
            var c = Canonico(codigo);
            return Opciones.FirstOrDefault(o => o.Codigo == c)?.Etiqueta ?? c;
        }

        // "Originales (SANTIAGO)", "Telex Release", "Otro: XYZ"
        public static string Resumen(string? guardado, string? otro, string? lugar)
        {
            var tipo = TipoActual(guardado);
            if (tipo == null) return "";
            if (tipo == Otro) return string.IsNullOrWhiteSpace(otro) ? "Otro" : $"Otro: {NormalizarNombre(otro)}";
            var txt = Etiqueta(tipo);
            if (tipo == Originales && !string.IsNullOrWhiteSpace(lugar)) txt += $" ({lugar})";
            return txt;
        }

        public static string Resumen(Operacione op) => Resumen(op.TipoEmisionBl, op.TipoEmisionBlOtro, op.UbicacionOriginalesDestino);

        public sealed record Seleccion(string? Tipo, string? Otro, string? Lugar)
        {
            public bool TieneOriginales => Tipo == EmisionBlHelper.Originales;
            public bool TieneOtro => Tipo == EmisionBlHelper.Otro;
        }

        // Lee el tipo (radio) + "Otro" + lugar de un formulario. Los nombres de campo
        // cambian según dónde se use (Matriz, Documental, Corrección de BL, Create).
        public static Seleccion LeerFormulario(IFormCollection form, string campoTipo = "TipoEmisionBl", string campoOtro = "TipoEmisionBlOtro", string campoLugar = "UbicacionOriginalesDestino")
        {
            string? tipo = form[campoTipo].Select(v => Canonico(v)).FirstOrDefault(EsCodigo);
            string? otro = tipo == Otro ? NormalizarNombre(form[campoOtro].ToString()) : null;
            if (string.IsNullOrWhiteSpace(otro)) otro = null;
            string? lugar = tipo == Originales ? NormalizarNombre(form[campoLugar].ToString()) : null;
            if (string.IsNullOrWhiteSpace(lugar) || !LugaresEmision.Contains(lugar)) lugar = null;
            return new Seleccion(tipo, otro, lugar);
        }

        // null = válido; si no, el mensaje para la ejecutiva.
        public static string? Validar(Seleccion s, bool tipoObligatorio)
        {
            if (tipoObligatorio && s.Tipo == null) return "Selecciona el Tipo de emisión del B/L.";
            if (s.TieneOtro && s.Otro == null) return "Indica cuál es el otro tipo de emisión.";
            if (s.TieneOriginales && s.Lugar == null) return "Selecciona el Lugar de emisión de los Originales.";
            return null;
        }

        public static void Aplicar(Operacione op, Seleccion s)
        {
            op.TipoEmisionBl = s.Tipo;
            op.TipoEmisionBlOtro = s.TieneOtro ? s.Otro : null;
            op.UbicacionOriginalesDestino = s.TieneOriginales ? s.Lugar : null;
        }

        public static Seleccion DesdeOperacion(Operacione op)
        {
            var tipo = TipoActual(op.TipoEmisionBl);
            return new Seleccion(tipo, tipo == Otro ? op.TipoEmisionBlOtro : null, tipo == Originales ? op.UbicacionOriginalesDestino : null);
        }

        // ===== Finanzas =====

        // Nombre para el concepto "Emisión BL - X"; para "Otro" es el texto escrito.
        public static string NombreFinanzas(string codigo, string? otro) =>
            NombreFinanzasFijo.TryGetValue(codigo, out var n) ? n : NormalizarNombre(otro);

        // Tarifa de la naviera: por TipoEmision; para "Otro", la tarifa "Otro" cuyo
        // nombre coincide con el texto escrito por Operaciones.
        public static TarifaBl? BuscarTarifa(IEnumerable<TarifaBl> tarifasNaviera, string codigo, string? otro)
        {
            if (codigo != Otro) return tarifasNaviera.FirstOrDefault(t => Canonico(t.TipoEmision) == codigo);
            var nombre = NormalizarNombre(otro);
            if (nombre == "") return null;
            return tarifasNaviera.FirstOrDefault(t => Canonico(t.TipoEmision) == Otro && NormalizarNombre(t.TipoEmisionOtro) == nombre);
        }

        // ===== Corrección de BL =====

        public const string MotivoCambioEmision = "Cambio emisión";
        public const string MotivoCambioLugar = "Cambio lugar de emisión";
        public const string MotivoOtros = "Otros";

        public static readonly string[] MotivosCorreccion =
        {
            "Corrección cuerpo BL",
            "Cambio shipper",
            "Cambio nave",
            "Cambio dirección",
            MotivoCambioEmision,
            MotivoCambioLugar,
            "Aplica devolución o reimpresión",
            MotivoOtros
        };

        // Textos que se guardaban antes con otra redacción.
        public static string? NormalizarMotivo(string? motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo)) return null;
            var m = motivo.Trim();
            if (m == "Corrección cuerpo OBL") return "Corrección cuerpo BL";
            if (m == "Aplica devolución BL y reimpresión") return "Aplica devolución o reimpresión";
            return MotivosCorreccion.FirstOrDefault(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase)) ?? m;
        }
    }
}
