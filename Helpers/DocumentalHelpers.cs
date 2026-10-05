using AtilsonCargoSpa.Models;
using System;
using System.Linq;

namespace AtilsonCargoSpa.Helpers
{
    public static class DocumentalHelpers
    {
        public static string GetProDot(bool? aplica, string? log)
        {
            if (aplica == false) return "bg-secondary opacity-25";
            if (string.IsNullOrEmpty(log)) return "bg-warning";
            if (log.ToUpper().Contains("RECHAZADO") || log.ToUpper().Contains("❌")) return "bg-danger";
            if (log.ToUpper().Contains("APROBADO") || log.ToUpper().Contains("RECEPCIÓN CONFIRMADA")) return "bg-success";
            return "bg-info";
        }

        public static string GetSagModalidadBadgeClass(string? modalidad)
        {
            if (modalidad == "Planta") return "bg-success bg-opacity-10 text-success border-success";
            if (modalidad == "Puerto") return "bg-warning bg-opacity-10 text-warning border-warning";
            return "bg-danger bg-opacity-10 text-danger border-danger";
        }

        public static string GetSagModalidadTexto(string? modalidad)
        {
            if (modalidad == "Planta") return "SAG EN PLANTA · SIN COSTO";
            if (modalidad == "Puerto") return "SAG FUERA DE PLANTA · APLICA TARIFA";
            return "MODALIDAD SAG PENDIENTE";
        }

        public static string GetSagModalidadIcono(string? modalidad)
        {
            if (modalidad == "Planta") return "bi-house-check-fill";
            if (modalidad == "Puerto") return "bi-geo-alt-fill";
            return "bi-question-circle-fill";
        }

        // Certificados con reemisiones (ori/fit/san/cap): true si ese slot específico
        // (1=Principal, 2=Reemisión 1, 3=Reemisión 2, 4=Reemisión 3) ya fue enviado a
        // Finanzas y por lo tanto debe mostrarse bloqueado/solo-lectura en Operaciones,
        // y ya debe generar su propio concepto financiero independiente.
        public static bool SlotBloqueado(OperacionesDocumentale? doc, string certKey, int slot)
        {
            var bloqueados = (doc?.CertsBloqueados ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            if (bloqueados.Contains($"{certKey}{slot}")) return true;

            // Migración: antes de este mecanismo, CADA guardado de Origen/Fito/Sanitario/
            // Captura enviaba a Finanzas de inmediato (no existía modo borrador), así que
            // cualquier slot que ya tenga N° o evidencia cargada se trata como ya
            // bloqueado, sin esperar a que alguien lo re-guarde para "migrar" la marca.
            if (!bloqueados.Contains(certKey)) return false;
            return SlotTieneDatos(doc, certKey, slot);
        }

        public static int SiguienteSlotEditable(OperacionesDocumentale? doc, string certKey)
        {
            for (int slot = 1; slot <= 4; slot++)
                if (!SlotBloqueado(doc, certKey, slot)) return slot;
            return 5; // Las 4 reemisiones ya están bloqueadas.
        }

        private static bool SlotTieneDatos(OperacionesDocumentale? doc, string certKey, int slot)
        {
            if (doc == null) return false;
            return (certKey, slot) switch
            {
                ("ori", 1) => !string.IsNullOrWhiteSpace(doc.RemisionOrigen1) || !string.IsNullOrWhiteSpace(doc.EvidenciaOrigen1),
                ("ori", 2) => !string.IsNullOrWhiteSpace(doc.RemisionOrigen2) || !string.IsNullOrWhiteSpace(doc.EvidenciaOrigen2),
                ("ori", 3) => !string.IsNullOrWhiteSpace(doc.NumOri3) || !string.IsNullOrWhiteSpace(doc.EvidenciaOrigen3),
                ("ori", 4) => !string.IsNullOrWhiteSpace(doc.NumOri4) || !string.IsNullOrWhiteSpace(doc.EvidenciaOrigen4),
                ("fit", 1) => !string.IsNullOrWhiteSpace(doc.CertFitosanitario) || !string.IsNullOrWhiteSpace(doc.EvidenciaFito1),
                ("fit", 2) => !string.IsNullOrWhiteSpace(doc.NumFit2) || !string.IsNullOrWhiteSpace(doc.EvidenciaFito2),
                ("fit", 3) => !string.IsNullOrWhiteSpace(doc.NumFit3) || !string.IsNullOrWhiteSpace(doc.EvidenciaFito3),
                ("fit", 4) => !string.IsNullOrWhiteSpace(doc.NumFit4) || !string.IsNullOrWhiteSpace(doc.EvidenciaFito4),
                ("san", 1) => !string.IsNullOrWhiteSpace(doc.CertSanitario) || !string.IsNullOrWhiteSpace(doc.EvidenciaSanitario1),
                ("san", 2) => !string.IsNullOrWhiteSpace(doc.NumSan2) || !string.IsNullOrWhiteSpace(doc.EvidenciaSanitario2),
                ("san", 3) => !string.IsNullOrWhiteSpace(doc.NumSan3) || !string.IsNullOrWhiteSpace(doc.EvidenciaSanitario3),
                ("san", 4) => !string.IsNullOrWhiteSpace(doc.NumSan4) || !string.IsNullOrWhiteSpace(doc.EvidenciaSanitario4),
                ("cap", 1) => !string.IsNullOrWhiteSpace(doc.NumCap1) || !string.IsNullOrWhiteSpace(doc.EvidenciaCap1),
                ("cap", 2) => !string.IsNullOrWhiteSpace(doc.NumCap2) || !string.IsNullOrWhiteSpace(doc.EvidenciaCap2),
                ("cap", 3) => !string.IsNullOrWhiteSpace(doc.NumCap3) || !string.IsNullOrWhiteSpace(doc.EvidenciaCap3),
                ("cap", 4) => !string.IsNullOrWhiteSpace(doc.NumCap4) || !string.IsNullOrWhiteSpace(doc.EvidenciaCap4),
                _ => false
            };
        }
    }
}