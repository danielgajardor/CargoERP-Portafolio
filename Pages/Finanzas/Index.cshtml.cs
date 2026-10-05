using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.Finanzas
{
    public class IndexModel : PageModel
    {
        private readonly AtilsonContext _context;

        public IndexModel(AtilsonContext context)
        {
            _context = context;
        }

        public class AjustePendienteDTO
        {
            public int IdOperacion { get; set; }
            public string NumeroBooking { get; set; } = string.Empty;
            public string Cliente { get; set; } = string.Empty;
            public List<string> ConceptosPendientes { get; set; } = new();
        }

        public List<AjustePendienteDTO> AjustesPendientes { get; set; } = new();

        public class ConceptoBloqueoDTO
        {
            public string CertKey { get; set; } = "";
            public string NombreConcepto { get; set; } = "";
            public int? IdUnidadTecnica { get; set; }
            public string EtiquetaContenedor { get; set; } = "";
            public bool Bloqueado { get; set; }
            public bool SolicitudPendiente { get; set; }
            public string? ComentarioSolicitud { get; set; }
            public string? UsuarioSolicitante { get; set; }
            public DateTime? FechaSolicitud { get; set; }
        }

        public class BookingBloqueosDTO
        {
            public int IdOperacion { get; set; }
            public string NumeroBooking { get; set; } = "";
            public string Cliente { get; set; } = "";
            public List<ConceptoBloqueoDTO> Conceptos { get; set; } = new();
        }

        public List<BookingBloqueosDTO> BookingsConBloqueos { get; set; } = new();
        public int TotalSolicitudesPendientes { get; set; }

        // Nombres visibles por certKey — todos pertenecen hoy al grupo Documental,
        // único módulo que implementa bloqueo por certificado (CertsBloqueados).
        // Si en el futuro se agrega bloqueo a Marítimo/Transporte/Gate, este mismo
        // diccionario y la lógica de abajo se extienden agregando esas claves.
        private static readonly Dictionary<string, string> NombresCert = new()
        {
            { "ori", "Certificado de Origen" },
            { "fit", "Certificado Fitosanitario" },
            { "san", "Certificado Sanitario" },
            { "cap", "Certificado de Captura" },
            { "cod", "Trámite CODAUT" },
            { "cla", "Trámite CLAVE" },
            { "nep", "Trámite NEPPEX" },
            { "coa", "COA" },
            { "dt", "DT" },
            { "aduana", "Aduana y Trámites" },
            { "gate", "Gate Out / In" }
        };

        private static string BaseCertKey(string key) => (key.Length > 0 && char.IsDigit(key[^1])) ? key[..^1] : key;

        public async Task OnGetAsync()
        {
            var operacionesConPendientes = await _context.Operaciones
                .Where(o => !o.IsDeleted && o.TransaccionesFinancieras.Any(t => t.MontoNeto == 0m && !t.TarifaManual))
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.TransaccionesFinancieras)
                .OrderByDescending(o => o.Id)
                .ToListAsync();

            AjustesPendientes = operacionesConPendientes.Select(o => new AjustePendienteDTO
            {
                IdOperacion = o.Id,
                NumeroBooking = string.IsNullOrWhiteSpace(o.NumeroBooking) ? $"OP-{o.Id}" : o.NumeroBooking,
                Cliente = o.IdClienteNavigation?.RazonSocial ?? "SIN MANDANTE",
                ConceptosPendientes = o.TransaccionesFinancieras
                    .Where(t => t.MontoNeto == 0m && !t.TarifaManual)
                    .Select(t => t.Concepto)
                    .Distinct()
                    .ToList()
            }).ToList();

            var operacionesConBloqueos = await _context.Operaciones
    .Where(o => !o.IsDeleted &&
        (o.OperacionesDocumentales.Any(d => d.CertsBloqueados != null && d.CertsBloqueados != "") ||
         o.SolicitudesDesbloqueos.Any(s => s.Estado == "PENDIENTE") ||
         o.GateBloqueado))
    .Include(o => o.IdClienteNavigation)
    .Include(o => o.OperacionesDocumentales)
    .Include(o => o.SolicitudesDesbloqueos)
    .OrderByDescending(o => o.Id)
    .ToListAsync();

            foreach (var op in operacionesConBloqueos)
            {
                var conceptos = new List<ConceptoBloqueoDTO>();
                var docsPorUnidad = op.OperacionesDocumentales.GroupBy(d => d.IdUnidadTecnica).Select(g => g.OrderBy(x => x.Id).First());

                foreach (var doc in docsPorUnidad)
                {
                    var keysBloqueados = (doc.CertsBloqueados ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(BaseCertKey).Distinct().ToList();
                    var solicitudesUnidad = op.SolicitudesDesbloqueos.Where(s => s.IdUnidadTecnica == doc.IdUnidadTecnica && s.Estado == "PENDIENTE" && s.CertKey != "gate").ToList();
                    var todasLasClaves = keysBloqueados.Concat(solicitudesUnidad.Select(s => s.CertKey)).Distinct();
                    string etiqueta = doc.IdUnidadTecnica.HasValue ? $"U{doc.IdUnidadTecnica}" : "Principal";

                    foreach (var key in todasLasClaves)
                    {
                        var solicitud = solicitudesUnidad.FirstOrDefault(s => s.CertKey == key);
                        conceptos.Add(new ConceptoBloqueoDTO
                        {
                            CertKey = key,
                            NombreConcepto = NombresCert.TryGetValue(key, out var nombre) ? nombre : key.ToUpper(),
                            IdUnidadTecnica = doc.IdUnidadTecnica,
                            EtiquetaContenedor = etiqueta,
                            Bloqueado = keysBloqueados.Contains(key),
                            SolicitudPendiente = solicitud != null,
                            ComentarioSolicitud = solicitud?.Comentario,
                            UsuarioSolicitante = solicitud?.UsuarioSolicitante,
                            FechaSolicitud = solicitud?.FechaSolicitud
                        });
                    }
                }

                // Gate no vive en OperacionesDocumentales — es un flag directo en
                // Operacione, así que se agrega aparte, con IdUnidadTecnica null
                // (el Gate del modal es a nivel de booking, no por contenedor).
                var solicitudGate = op.SolicitudesDesbloqueos.FirstOrDefault(s => s.CertKey == "gate" && s.IdUnidadTecnica == null && s.Estado == "PENDIENTE");
                if (op.GateBloqueado || solicitudGate != null)
                {
                    conceptos.Add(new ConceptoBloqueoDTO
                    {
                        CertKey = "gate",
                        NombreConcepto = NombresCert["gate"],
                        IdUnidadTecnica = null,
                        EtiquetaContenedor = "Booking",
                        Bloqueado = op.GateBloqueado,
                        SolicitudPendiente = solicitudGate != null,
                        ComentarioSolicitud = solicitudGate?.Comentario,
                        UsuarioSolicitante = solicitudGate?.UsuarioSolicitante,
                        FechaSolicitud = solicitudGate?.FechaSolicitud
                    });
                }

                if (conceptos.Any())
                {
                    BookingsConBloqueos.Add(new BookingBloqueosDTO
                    {
                        IdOperacion = op.Id,
                        NumeroBooking = string.IsNullOrWhiteSpace(op.NumeroBooking) ? $"OP-{op.Id}" : op.NumeroBooking,
                        Cliente = op.IdClienteNavigation?.RazonSocial ?? "SIN MANDANTE",
                        Conceptos = conceptos
                    });
                }
            }

            TotalSolicitudesPendientes = BookingsConBloqueos.SelectMany(b => b.Conceptos).Count(c => c.SolicitudPendiente);
        }

        public async Task<IActionResult> OnPostDesbloquearConceptoAsync(int idOperacion, int? idUnidadTecnica, string certKey)
        {
            if (string.IsNullOrWhiteSpace(certKey))
                return new JsonResult(new { success = false, message = "Concepto inválido." });

            string usuario = User.Identity?.Name ?? "Finanzas";
            string nombreCert = NombresCert.TryGetValue(certKey, out var nom) ? nom : certKey.ToUpper();

            // Gate es un flag directo en Operacione, no vive en OperacionesDocumentale
            // (CertsBloqueados) — se maneja aparte del resto de certificados.
            if (certKey == "gate")
            {
                var opGate = await _context.Operaciones.FindAsync(idOperacion);
                if (opGate == null) return new JsonResult(new { success = false, message = "Operación no encontrada." });

                bool huboCambioGate = opGate.GateBloqueado;
                opGate.GateBloqueado = false;

                var solicitudesGate = await _context.SolicitudesDesbloqueos
                    .Where(s => s.IdOperacion == idOperacion && s.IdUnidadTecnica == null && s.CertKey == "gate" && s.Estado == "PENDIENTE")
                    .ToListAsync();
                foreach (var sol in solicitudesGate)
                {
                    sol.Estado = "RESUELTA";
                    sol.UsuarioResolucion = usuario;
                    sol.FechaResolucion = DateTime.Now;
                }

                if (!huboCambioGate && !solicitudesGate.Any())
                    return new JsonResult(new { success = false, message = "No hay nada que resolver para este concepto." });

                string fechaGate = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                string lineaGate = $"{fechaGate}|{usuario}|GENERAL|Gate desbloqueado desde Finanzas.\n";
                opGate.HistorialCorreos = lineaGate + (opGate.HistorialCorreos ?? "");

                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }

            var doc = await _context.OperacionesDocumentales
                .FirstOrDefaultAsync(d => d.IdOperacion == idOperacion && d.IdUnidadTecnica == idUnidadTecnica);

            bool huboCambioBloqueo = false;

            if (doc != null)
            {
                var set = (doc.CertsBloqueados ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                var aQuitar = set.Where(k => k == certKey || (k.StartsWith(certKey) && k.Length == certKey.Length + 1 && char.IsDigit(k[^1]))).ToList();

                if (aQuitar.Any())
                {
                    foreach (var k in aQuitar) set.Remove(k);
                    doc.CertsBloqueados = string.Join(",", set);
                    huboCambioBloqueo = true;

                    string fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                    string entrada = $"<div style='border-left:2px solid #0ea5e9; padding:6px; margin-bottom:6px; background:#f0f9ff; border-top:1px solid #bae6fd; border-right:1px solid #bae6fd; border-bottom:1px solid #bae6fd;'>" +
                        $"<div style='display:flex; justify-content:space-between; align-items:center; margin-bottom:4px;'>" +
                        $"<span style='font-weight:800; color:#0284c7; text-transform:uppercase; font-size:10px;'>DESBLOQUEADO DESDE FINANZAS</span>" +
                        $"<span style='color:#64748b; font-size:9px;'>{fecha}</span></div>" +
                        $"<div style='color:#475569; font-size:10px; margin-bottom:3px;'>Por: <strong>{usuario}</strong></div>" +
                        $"<div style='color:#1e293b; font-size:11px; line-height:1.2;'>Se desbloqueó <strong>{nombreCert}</strong> para permitir su edición nuevamente en Operaciones.</div></div>\n";
                    doc.LogDesbloqueos = entrada + (doc.LogDesbloqueos ?? "");
                }
            }

            var solicitudes = await _context.SolicitudesDesbloqueos
                .Where(s => s.IdOperacion == idOperacion && s.IdUnidadTecnica == idUnidadTecnica && s.CertKey == certKey && s.Estado == "PENDIENTE")
                .ToListAsync();
            foreach (var sol in solicitudes)
            {
                sol.Estado = "RESUELTA";
                sol.UsuarioResolucion = usuario;
                sol.FechaResolucion = DateTime.Now;
            }

            if (!huboCambioBloqueo && !solicitudes.Any())
                return new JsonResult(new { success = false, message = "No hay nada que resolver para este concepto." });

            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }
    }
}