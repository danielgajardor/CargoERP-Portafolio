using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    // ============================================================================
    // Ventana "Reportar extracosto" (oct-2026)
    // ----------------------------------------------------------------------------
    // Una sola lectura/validación del formulario para el tablero (Operaciones/Index)
    // y el Detalle. Campos:
    //   Servicio  (NuevoCosto.GrupoOperativo)  Marítimo / Transporte / Documental / Otros:
    //             define en qué bloque de Control Financiero queda el concepto.
    //   Concepto  (NuevoCosto.TipoCosto) o, con "__NUEVO__", un concepto nuevo escrito
    //             a mano (NuevoCosto.ConceptoNuevo).
    //   Proveedor (NuevoCosto.IdProveedor, opcional): queda como proveedor del costo.
    //   ¿Quién paga?  (NuevoCosto.ResponsableGestion) -> columna "Pagado por" en CF.
    //   Responsable   (NuevoCosto.Responsable)        -> columna "Responsable" en CF
    //             (Cliente = se le cobra; Atilson = lo absorbe; Proveedor/Agencia = a descontar).
    //   Valor neto + moneda, justificación y evidencia.
    // FinanzasSyncService genera la fila "<concepto> (EXT-n)" en el grupo del servicio.
    // ============================================================================
    public static class ExtracostoFormulario
    {
        public const string ValorConceptoNuevo = "__NUEVO__";

        /// <summary>Servicios (grupos de Control Financiero) donde puede quedar un extracosto.</summary>
        public static readonly string[] Servicios = { "Marítimo", "Transporte", "Documental", "Otros" };

        public static readonly string[] Responsables = { "Cliente", "Atilson", "Proveedor", "Agencia" };

        private static readonly string[] ExtensionesEvidencia = { ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".xlsx", ".xls", ".docx", ".msg", ".eml" };

        public sealed class Resultado
        {
            public bool Ok { get; init; }
            public string Mensaje { get; init; } = "";
            public ExtracostosOperacion? Costo { get; init; }
            public string? NombreProveedor { get; init; }

            /// <summary>Texto para la bitácora de la operación.</summary>
            public string TextoHito => Costo == null ? "" :
                $"Extracosto reportado — Servicio: {Costo.GrupoOperativo ?? "-"} | Concepto: {Costo.TipoCosto} | Valor neto: {Costo.Moneda} {Costo.Monto.ToString("N0", new CultureInfo("es-CL"))}"
                + $" | Proveedor: {NombreProveedor ?? "por asignar"} | Paga: {Costo.ResponsableGestion} | Responsable: {Costo.Responsable} | Motivo: {Costo.Motivo}";
        }

        private static Resultado Error(string m) => new() { Ok = false, Mensaje = m };

        /// <summary>Normaliza el servicio escrito (sin tildes, mayúsculas) a uno de <see cref="Servicios"/>.</summary>
        public static string? NormalizarServicio(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return null;
            string v = valor.Trim().ToUpperInvariant().Replace("Í", "I");
            return v switch
            {
                "MARITIMO" => "Marítimo",
                "TRANSPORTE" or "TERRESTRE" => "Transporte",
                "DOCUMENTAL" => "Documental",
                "OTROS" or "OTRO" => "Otros",
                _ => null
            };
        }

        private static string? NormalizarResponsable(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return null;
            return Responsables.FirstOrDefault(r => r.Equals(valor.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Acepta "1234.5", "1234,5" y "1.234.567" (miles con punto).</summary>
        public static decimal? LeerMonto(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            string t = texto.Trim().Replace(" ", "").Replace("$", "");
            if (t.Contains(',') && t.Contains('.')) t = t.Replace(".", "").Replace(',', '.');
            else if (t.Contains(',')) t = t.Replace(',', '.');
            else if (t.Count(c => c == '.') > 1) t = t.Replace(".", "");
            return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) ? m : null;
        }

        /// <summary>
        /// Lee y valida el formulario, guarda la evidencia y agrega el extracosto al contexto
        /// (sin SaveChanges: quien llama registra el hito, guarda y sincroniza Finanzas).
        /// </summary>
        public static async Task<Resultado> CrearAsync(AtilsonContext context, IWebHostEnvironment env, IFormCollection form, int idOperacion, string usuario)
        {
            string? tipo = form["NuevoCosto.TipoCosto"].ToString().Trim();
            string? conceptoNuevo = form["NuevoCosto.ConceptoNuevo"].ToString().Trim();
            string? servicio = NormalizarServicio(form["NuevoCosto.GrupoOperativo"]);
            string motivo = form["NuevoCosto.Motivo"].ToString().Trim();
            string? paga = NormalizarResponsable(form["NuevoCosto.ResponsableGestion"]);
            string? responsable = NormalizarResponsable(form["NuevoCosto.Responsable"]);
            string moneda = form["NuevoCosto.Moneda"].ToString().Trim().ToUpperInvariant();
            decimal? monto = LeerMonto(form["NuevoCosto.Monto"]);
            int? idProveedor = int.TryParse(form["NuevoCosto.IdProveedor"], out var idp) && idp > 0 ? idp : null;
            var evidencia = form.Files.GetFile("NuevoCosto.Evidencia");

            bool esNuevo = tipo == ValorConceptoNuevo;
            if (esNuevo)
            {
                if (string.IsNullOrWhiteSpace(conceptoNuevo)) return Error("Escribe el nombre del concepto nuevo.");
                if (servicio == null) return Error("Para un concepto nuevo elige el servicio (Marítimo, Transporte, Documental u Otros).");
                tipo = string.Join(' ', conceptoNuevo.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
            if (string.IsNullOrWhiteSpace(tipo)) return Error("Elige el concepto del extracosto.");
            if (tipo.Length > 120) return Error("El nombre del concepto admite hasta 120 caracteres.");
            if (tipo.Contains("(EXT-", StringComparison.OrdinalIgnoreCase)) return Error("El nombre del concepto no puede contener \"(EXT-\".");
            if (monto is null || monto <= 0) return Error("Indica el valor neto del extracosto (mayor a 0).");
            if (moneda != "CLP" && moneda != "USD") moneda = "CLP";
            if (paga == null) return Error("Indica quién paga el extracosto.");
            if (responsable == null) return Error("Indica el responsable del extracosto.");
            if (string.IsNullOrWhiteSpace(motivo)) return Error("Escribe la justificación del extracosto.");
            if (motivo.Length > 500) motivo = motivo.Substring(0, 500);
            if (evidencia == null || evidencia.Length == 0) return Error("Adjunta la evidencia (PDF, imagen o correo).");
            if (evidencia.Length > 15 * 1024 * 1024) return Error("La evidencia supera los 15 MB.");
            string ext = Path.GetExtension(evidencia.FileName).ToLowerInvariant();
            if (!ExtensionesEvidencia.Contains(ext)) return Error("Tipo de archivo no permitido para la evidencia. Usa PDF, imagen, Excel, Word o correo (.msg/.eml).");

            if (!await context.Operaciones.AnyAsync(o => o.Id == idOperacion))
                return Error("No se encontró la operación.");

            string? nombreProveedor = null;
            if (idProveedor.HasValue)
            {
                nombreProveedor = await context.Proveedores.Where(p => p.Id == idProveedor.Value)
                    .Select(p => p.NombreProveedor).FirstOrDefaultAsync();
                if (nombreProveedor == null) return Error("El proveedor elegido no existe.");
            }

            string carpeta = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "evidencias");
            Directory.CreateDirectory(carpeta);
            string baseSegura = new string(Path.GetFileNameWithoutExtension(evidencia.FileName)
                .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
            if (baseSegura.Length > 60) baseSegura = baseSegura.Substring(0, 60);
            string archivo = $"{idOperacion}_Costo_{DateTime.Now.Ticks}_{baseSegura}{ext}";
            await using (var fs = new FileStream(Path.Combine(carpeta, archivo), FileMode.CreateNew))
                await evidencia.CopyToAsync(fs);

            var costo = new ExtracostosOperacion
            {
                IdOperacion = idOperacion,
                TipoCosto = tipo,
                Motivo = motivo,
                Responsable = responsable,
                ResponsableGestion = paga,
                GrupoOperativo = servicio,
                IdProveedor = idProveedor,
                Moneda = moneda,
                Monto = monto.Value,
                Evidencia = $"/uploads/evidencias/{archivo}",
                FechaCreacion = DateTime.Now,
                UsuarioCreador = usuario
            };
            context.ExtracostosOperacions.Add(costo);
            return new Resultado
            {
                Ok = true,
                Costo = costo,
                NombreProveedor = nombreProveedor,
                Mensaje = $"Extracosto «{tipo}» reportado. Finanzas lo verá en Control Financiero › {servicio ?? "según su concepto"}."
            };
        }

        /// <summary>Parámetros de la vista parcial _ModalExtracosto (tablero o Detalle).</summary>
        public sealed class ModalVista
        {
            /// <summary>"" en el tablero, "Det" en el Detalle (sufijo de los id).</summary>
            public string Sufijo { get; init; } = "";
            public string Accion { get; init; } = "";
            public string? FormId { get; init; }
            public string OnclickGuardar { get; init; } = "";
            /// <summary>Tablero: muestra el área de datos extra (cantidad, distancia...).</summary>
            public bool AreaDinamica { get; init; }
        }

        /// <summary>
        /// Listas de la ventana: proveedores activos y conceptos agregados antes. Usa las
        /// tarifas que la página ya dejó en ViewData (TarifasMaritimo/Terrestre/Documental).
        /// </summary>
        public static async Task CargarListasAsync(AtilsonContext context, Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary viewData)
        {
            try
            {
                var provs = await context.Proveedores.AsNoTracking()
                    .Where(p => p.Activo == null || p.Activo == 1)
                    .OrderBy(p => p.NombreProveedor)
                    .Select(p => new { p.Id, p.NombreProveedor })
                    .ToListAsync();
                viewData["ExtracostoProveedores"] = provs.Select(p => (p.Id, p.NombreProveedor)).ToList();
            }
            catch
            {
                viewData["ExtracostoProveedores"] = new List<(int, string)>();
            }
            var deLista = new List<string>
            {
                "Late Arrival (LAR)", "Extra Late Arrival (ELAR)", "Corrector B/L", "Plug (Reefer)", "Demurrage", "Matriz fuera de plazo",
                "Demora / Penalidad Transbordo", "Falso Flete Planta", "Falso Flete en Ruta", "Falso Flete Depósito", "Porteo", "Interplanta",
                "Sobreestadía Planta", "Sobreestadía Puerto", "Almacenamiento por aforo u otro motivo", "Scanner",
                "Costo de Terminal Aforo fisico o retiro contenedor", "Gestion Providencia Aduanera", "Gestion retiro sello naviera"
            };
            foreach (var clave in new[] { "TarifasMaritimo", "TarifasTerrestre", "TarifasDocumental" })
                if (viewData[clave] is IEnumerable<TarifasMaestra> t) deLista.AddRange(t.Select(x => x.Concepto));
            viewData["ExtracostoAgregados"] = await ConceptosAgregadosAsync(context, deLista);
        }

        /// <summary>Conceptos nuevos que ya se usaron antes (para ofrecerlos en la lista), por servicio.</summary>
        public static async Task<List<(string Concepto, string Servicio)>> ConceptosAgregadosAsync(AtilsonContext context, IEnumerable<string> conceptosDeLista)
        {
            var conocidos = new HashSet<string>(conceptosDeLista.Where(c => !string.IsNullOrWhiteSpace(c)), StringComparer.OrdinalIgnoreCase);
            try
            {
                // Solo los reportados a mano desde esta ventana (su evidencia se guarda como
                // "<id>_Costo_..."); los que crea el sistema (Corrección de BL N°x, ELAR...) no.
                var usados = await context.ExtracostosOperacions.AsNoTracking()
                    .Where(e => e.GrupoOperativo != null && e.TipoCosto != null && e.Evidencia.Contains("_Costo_")
                                && !e.TipoCosto.StartsWith("Corrección de BL"))
                    .OrderByDescending(e => e.Id)
                    .Select(e => new { e.TipoCosto, e.GrupoOperativo })
                    .Take(500)
                    .ToListAsync();
                return usados
                    .Where(u => !conocidos.Contains(u.TipoCosto) && NormalizarServicio(u.GrupoOperativo) != null)
                    .GroupBy(u => u.TipoCosto.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => (g.Key, NormalizarServicio(g.First().GrupoOperativo)!))
                    .OrderBy(x => x.Item1)
                    .Take(80)
                    .ToList();
            }
            catch
            {
                return new List<(string, string)>();
            }
        }
    }
}
