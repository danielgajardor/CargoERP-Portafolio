using AtilsonCargoSpa.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Services
{
    // =====================================================================================
    // Ciclo de la nave en el Mainboard (13-nov-2026, minuta "Ciclo de naves, trazabilidad y
    // alertas"). Reúne en un solo lugar lo que pasa con el Zarpe Real y el Arribo Efectivo,
    // venga de Itinerarios (por salida) o del Mainboard (por booking):
    //
    //   ETD (día de zarpe) ─► alerta "Zarpe por confirmar" hasta que se guarda el Zarpe Real.
    //   Zarpe Real        ─► se apaga; los contenedores pasan a "Zarpe" y se envía al cliente
    //                        la confirmación de zarpe.
    //   ETA − 10 días     ─► alerta "Aviso de llegada" hasta que se envía el aviso al cliente.
    //   ETA − 5 y − 3     ─► chequeo "revisar demoras de la nave", se cierra con un clic.
    //   ETA (día)         ─► alerta "Arribado a destino" hasta que se guarda el Arribo Efectivo.
    //   Arribo Efectivo   ─► se apaga; los contenedores pasan a "Arribado" (con su fecha) y
    //                        aparece "Retiro pendiente" (alerta que ya existía); se envía al
    //                        cliente la confirmación de arribo.
    //
    // Zarpe Real = Operacione.EtdReal y Arribo Efectivo = Operacione.EtaReal (columnas que ya
    // existían). Las marcas de correo y chequeos: Sql/2026-11_ciclo_naves_pagos_permisos.sql.
    // =====================================================================================
    public static class CicloNave
    {
        // Orden del workflow del Mainboard (wwwroot/js/operaciones-index.js, ESTADOS).
        private static readonly string[] Orden =
        {
            "SOLICITADO", "CONFIRMADO", "ENVIO A CLIENTE", "EN RUTA PLANTA", "ARRIBADO A PLANTA",
            "SALIDA DE PLANTA", "EN RUTA A PUERTO", "EN PUERTO", "ENTREGADO A STACKING", "EMBARCADO",
            "ZARPE", "PROXIMO ARRIBO", "ARRIBADO", "CONTENEDOR RETIRADO", "VACIO DEVUELTO"
        };

        private static string Norm(string? s)
        {
            string t = (s ?? "").Trim().ToUpperInvariant();
            return t.Replace("Á", "A").Replace("É", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ú", "U");
        }

        /// <summary>Posición del estado en el workflow; -1 si está vacío o no se reconoce.</summary>
        public static int PosicionEstado(string? estado) => Array.IndexOf(Orden, Norm(estado));

        private static bool Cancelado(string? estado) => Norm(estado).StartsWith("CANCELADO");

        /// <summary>Lleva el estado a <paramref name="destino"/> solo si va antes en el workflow.</summary>
        private static string? Avanzar(string? actual, string destino)
        {
            if (Cancelado(actual)) return actual;
            return PosicionEstado(actual) < PosicionEstado(destino) ? destino : actual;
        }

        public static bool AplicaCiclo(Operacione op) =>
            !op.IsDeleted && !op.EsHistorica && !Cancelado(op.EstadoWorkflow) && TieneMaritimo(op);

        // Tipos de servicio con tramo marítimo (mismo criterio que FinanzasSyncService). El ciclo
        // de la nave (fechas reales, alertas y correos) solo aplica a esas operaciones.
        private static readonly int[] ServiciosMaritimos = { 1, 2, 3, 5, 8, 9, 10, 12 };
        public static bool TieneMaritimo(Operacione op) => ServiciosMaritimos.Contains(op.IdTipoServicio ?? 0);

        public static void RegistrarHito(Operacione op, string usuario, string modulo, string descripcion)
        {
            string linea = $"{DateTime.Now:dd/MM/yyyy HH:mm}|{usuario}|{modulo}|{descripcion}\n";
            op.HistorialCorreos = linea + (op.HistorialCorreos ?? "");
        }

        /// <summary>
        /// Guarda (o borra, con null) el Zarpe Real del booking. Con fecha, los contenedores que
        /// aún no llegaban a "Zarpe" pasan a ese estado. Devuelve true si el dato cambió.
        /// </summary>
        public static bool AplicarZarpe(Operacione op, DateTime? fecha, string usuario, string origen)
        {
            if (op.EtdReal == fecha) return false;
            var anterior = op.EtdReal;
            op.EtdReal = fecha;
            if (fecha != null)
            {
                op.EstadoWorkflow = Avanzar(op.EstadoWorkflow, "Zarpe");
                foreach (var u in op.Unidadestecnicas ?? new List<Unidadestecnica>())
                {
                    var nuevo = Avanzar(u.Estado, "Zarpe");
                    if (nuevo != u.Estado) { u.Estado = nuevo; u.FechaModificacion = DateTime.Now; u.UsuarioModificador = usuario; }
                }
            }
            op.FechaModificacion = DateTime.Now;
            op.UsuarioModificador = usuario;
            RegistrarHito(op, usuario, "MARÍTIMO", fecha == null
                ? $"Zarpe Real borrado ({origen}). Antes: {anterior:dd/MM/yyyy HH:mm}."
                : $"Zarpe Real {fecha:dd/MM/yyyy HH:mm} ({origen})" + (anterior != null ? $". Antes: {anterior:dd/MM/yyyy HH:mm}." : "."));
            return true;
        }

        /// <summary>
        /// Guarda (o borra) el Arribo Efectivo. Con fecha, los contenedores que aún no llegaban a
        /// "Arribado" pasan a ese estado con su fecha de arribo a destino: así se apaga la alerta
        /// de arribo y aparece la de "Retiro pendiente".
        /// </summary>
        public static bool AplicarArribo(Operacione op, DateTime? fecha, string usuario, string origen)
        {
            if (op.EtaReal == fecha) return false;
            var anterior = op.EtaReal;
            op.EtaReal = fecha;
            if (fecha != null)
            {
                if (PosicionEstado(op.EstadoWorkflow) < PosicionEstado("Arribado") && !Cancelado(op.EstadoWorkflow))
                    op.EstadoWorkflow = "Arribado";
                if (!op.FechaArriboDestino.HasValue)
                {
                    op.FechaArriboDestino = fecha;
                    op.UsuarioArriboDestino = usuario;
                }
                foreach (var u in op.Unidadestecnicas ?? new List<Unidadestecnica>())
                {
                    bool cambio = false;
                    if (PosicionEstado(u.Estado) < PosicionEstado("Arribado") && !Cancelado(u.Estado)) { u.Estado = "Arribado"; cambio = true; }
                    if (!u.FechaArriboDestino.HasValue) { u.FechaArriboDestino = fecha; u.UsuarioArriboDestino = usuario; cambio = true; }
                    if (cambio) { u.FechaModificacion = DateTime.Now; u.UsuarioModificador = usuario; }
                }
            }
            op.FechaModificacion = DateTime.Now;
            op.UsuarioModificador = usuario;
            RegistrarHito(op, usuario, "MARÍTIMO", fecha == null
                ? $"Arribo Efectivo borrado ({origen}). Antes: {anterior:dd/MM/yyyy HH:mm}."
                : $"Arribo Efectivo {fecha:dd/MM/yyyy HH:mm} ({origen})" + (anterior != null ? $". Antes: {anterior:dd/MM/yyyy HH:mm}." : "."));
            return true;
        }

        // ---------------------------------------------------------------------------------
        // Registro por salida (13-nov-2026): el Zarpe / Arribo es de la NAVE, así que vale para
        // todos los bookings de la salida. Lo usan Itinerarios (por salida) y el Mainboard (un
        // booking ligado a una salida): en ambos casos queda en la salida, se copia a sus bookings
        // que aún no lo tienen (o que tenían el valor copiado antes) y a cada uno se le envía la
        // confirmación si se pidió. El booking desde el que se registra se actualiza siempre.
        // ---------------------------------------------------------------------------------
        public sealed class ResultadoSalida
        {
            public int Bookings { get; set; }
            public int Enviados { get; set; }
            public List<string> SinEnviar { get; } = new();
            public List<Operacione> Actualizados { get; } = new();
        }

        /// <summary>Bookings con servicio marítimo de una salida: los que la eligieron, los de la misma naviera y
        /// "NAVE / VIAJE" sin salida elegida y, si se indica, el booking desde el que se registra.</summary>
        public static async Task<List<Operacione>> BookingsDeSalidaAsync(AtilsonContext db, ItinerarioNave it, int? idOperacionOrigen = null)
        {
            string naveViaje = it.NaveViaje;
            var ops = await db.Operaciones
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.IdPuertoOrigenNavigation)
                .Include(o => o.IdPuertoDestinoNavigation)
                .Include(o => o.Unidadestecnicas)
                .Where(o => !o.IsDeleted && !o.EsHistorica
                    && (o.IdItinerario == it.Id || (o.IdItinerario == null && o.IdNaviera == it.IdNaviera && o.Nave == naveViaje)
                        || (idOperacionOrigen != null && o.Id == idOperacionOrigen)))
                .ToListAsync();
            return ops.Where(o => TieneMaritimo(o) && !Cancelado(o.EstadoWorkflow)).OrderBy(o => o.NumeroBooking).ToList();
        }

        /// <summary>¿El booking recibe la fecha de la salida? Sin dato real todavía, con el que se había
        /// copiado desde la salida antes, o el booking desde el que se registra.</summary>
        public static bool RecibeFechaDeSalida(Operacione o, ItinerarioNave it, bool esZarpe, int? idOperacionOrigen = null)
        {
            if (idOperacionOrigen != null && o.Id == idOperacionOrigen) return true;
            var actual = esZarpe ? o.EtdReal : o.EtaReal;
            var anterior = esZarpe ? it.ZarpeEfectivo : it.ArriboEfectivo;
            if (actual == null) return true;
            return anterior != null && actual >= anterior.Value.Date && actual < anterior.Value.Date.AddDays(1);
        }

        public static async Task<ResultadoSalida> RegistrarEnSalidaAsync(AtilsonContext db, EmailService email, ItinerarioNave it,
            DateTime? fecha, bool esZarpe, bool notificar, string usuario, string? copiaCc, string origen, int? idOperacionOrigen = null)
        {
            var res = new ResultadoSalida();
            // Qué bookings reciben la fecha se decide con el valor anterior de la salida.
            var reciben = fecha == null ? new List<Operacione>()
                : (await BookingsDeSalidaAsync(db, it, idOperacionOrigen)).Where(o => RecibeFechaDeSalida(o, it, esZarpe, idOperacionOrigen)).ToList();

            var anterior = esZarpe ? it.ZarpeEfectivo : it.ArriboEfectivo;
            if (anterior != fecha)
            {
                if (esZarpe) it.ZarpeEfectivo = fecha; else it.ArriboEfectivo = fecha;
                it.FechaModificacion = DateTime.Now;
                it.UsuarioModificador = usuario;
                var cambios = new MaestrosAuditoria.Cambios();
                cambios.Comparar(esZarpe ? "Zarpe efectivo" : "Arribo efectivo",
                    anterior?.ToString("dd-MM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    fecha?.ToString("dd-MM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture));
                MaestrosAuditoria.Registrar(db, MaestrosAuditoria.EntItinerario, it.Id, cambios, usuario, (esZarpe ? "Zarpe efectivo" : "Arribo efectivo") + " · " + origen);
            }
            // Quitar la fecha de la salida no se la quita a los bookings (cada uno se corrige aparte).
            if (fecha == null) return res;

            foreach (var o in reciben)
            {
                bool cambio = esZarpe ? AplicarZarpe(o, fecha, usuario, origen) : AplicarArribo(o, fecha, usuario, origen);
                if (!cambio) continue;
                res.Bookings++;
                res.Actualizados.Add(o);
                if (notificar)
                {
                    var error = await NotificarAsync(email, o, esZarpe, usuario, copiaCc);
                    if (error == null) res.Enviados++;
                    else res.SinEnviar.Add($"{o.NumeroBooking} ({error})");
                }
            }
            return res;
        }

        /// <summary>Salida del maestro de un booking: la elegida, o la de su misma naviera y "NAVE / VIAJE".</summary>
        public static async Task<ItinerarioNave?> SalidaDeAsync(AtilsonContext db, Operacione op)
        {
            if (op.IdItinerario != null)
                return await db.ItinerariosNaves.FirstOrDefaultAsync(i => i.Id == op.IdItinerario && i.Activo);
            if (string.IsNullOrWhiteSpace(op.Nave)) return null;
            var candidatas = await db.ItinerariosNaves.Where(i => i.Activo && i.IdNaviera == op.IdNaviera).ToListAsync();
            var iguales = candidatas.Where(i => string.Equals(i.NaveViaje, op.Nave!.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            return iguales.Count == 1 ? iguales[0] : null;
        }

        // ---------------------------------------------------------------------------------
        // Correos al cliente
        // ---------------------------------------------------------------------------------
        private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

        private static string FechaTexto(DateTime? f) =>
            f == null ? "—" : (f.Value.TimeOfDay == TimeSpan.Zero ? f.Value.ToString("dd/MM/yyyy") : f.Value.ToString("dd/MM/yyyy HH:mm"));

        private static string TablaBooking(Operacione op, params (string Etiqueta, string Valor)[] extra)
        {
            var filas = new List<(string, string)>
            {
                ("Booking", op.NumeroBooking),
                ("Naviera", op.IdNavieraNavigation?.NombreNaviera ?? "—"),
                ("Nave / Viaje", string.IsNullOrWhiteSpace(op.Nave) ? "—" : op.Nave!),
                ("Origen (POL)", op.IdPuertoOrigenNavigation?.NombrePuerto ?? "—"),
                ("Destino (POD)", op.IdPuertoDestinoNavigation?.NombrePuerto ?? "—"),
            };
            var conts = new List<string>();
            if (!string.IsNullOrWhiteSpace(op.NumeroContenedor)) conts.Add(op.NumeroContenedor!);
            conts.AddRange((op.Unidadestecnicas ?? new List<Unidadestecnica>()).OrderBy(u => u.Id)
                .Select(u => u.NroContenedor).Where(x => !string.IsNullOrWhiteSpace(x))!);
            if (conts.Any()) filas.Add(("Contenedor(es)", string.Join(", ", conts.Distinct())));
            filas.AddRange(extra);
            return "<table style=\"width:100%;border-collapse:collapse;font-size:13px;margin:12px 0;\">" +
                string.Concat(filas.Select(f =>
                    $"<tr><td style=\"padding:6px 8px;color:#64748b;border-bottom:1px solid #eef0f4;width:40%;\">{H(f.Item1)}</td>" +
                    $"<td style=\"padding:6px 8px;color:#0f172a;font-weight:600;border-bottom:1px solid #eef0f4;\">{H(f.Item2)}</td></tr>")) +
                "</table>";
        }

        public static (string Asunto, string Html) CorreoZarpe(Operacione op)
        {
            string asunto = $"[ATILSON CARGO] Confirmación de zarpe · BKG {op.NumeroBooking}";
            string cuerpo = $"<p>Estimado cliente:</p><p>Confirmamos el <strong>zarpe de la nave</strong> de su booking <strong>{H(op.NumeroBooking)}</strong>.</p>" +
                TablaBooking(op, ("Zarpe real", FechaTexto(op.EtdReal)), ("Arribo estimado (ETA)", FechaTexto(op.EtaPod))) +
                "<p>Le avisaremos antes de la llegada a destino. Ante cualquier consulta, responda este correo.</p>";
            return (asunto, RegistroClientesService.Plantilla("Confirmación de zarpe", cuerpo, encabezado: "ATILSON CARGO · SEGUIMIENTO DE NAVE"));
        }

        public static (string Asunto, string Html) CorreoArribo(Operacione op)
        {
            string asunto = $"[ATILSON CARGO] Confirmación de arribo · BKG {op.NumeroBooking}";
            string cuerpo = $"<p>Estimado cliente:</p><p>Confirmamos el <strong>arribo a destino</strong> de su booking <strong>{H(op.NumeroBooking)}</strong>.</p>" +
                TablaBooking(op, ("Zarpe real", FechaTexto(op.EtdReal)), ("Arribo efectivo", FechaTexto(op.EtaReal))) +
                "<p>Le recomendamos coordinar el <strong>retiro del contenedor</strong> del terminal a la brevedad, antes de que comiencen a correr cobros por sobreestadía (demurrage).</p>";
            return (asunto, RegistroClientesService.Plantilla("Confirmación de arribo", cuerpo, encabezado: "ATILSON CARGO · SEGUIMIENTO DE NAVE"));
        }

        public const string IntroAvisoLlegada =
            "Le informamos que la nave de su booking se aproxima a destino. Le sugerimos preparar con anticipación la documentación y el retiro del contenedor.";

        public static (string Asunto, string Html) CorreoAvisoLlegada(Operacione op, string? mensaje)
        {
            string asunto = $"[ATILSON CARGO] Aviso de llegada · BKG {op.NumeroBooking}";
            string texto = string.IsNullOrWhiteSpace(mensaje) ? IntroAvisoLlegada : mensaje.Trim();
            string parrafos = string.Concat(texto.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(p => $"<p>{H(p)}</p>"));
            string cuerpo = "<p>Estimado cliente:</p>" + parrafos +
                TablaBooking(op, ("Zarpe real", FechaTexto(op.EtdReal ?? op.EtdPol)), ("Arribo estimado (ETA)", FechaTexto(op.EtaPod)));
            return (asunto, RegistroClientesService.Plantilla("Aviso de llegada", cuerpo, encabezado: "ATILSON CARGO · SEGUIMIENTO DE NAVE"));
        }

        /// <summary>
        /// Envía la confirmación de zarpe o de arribo al correo del cliente. No lanza: devuelve el
        /// motivo si no se pudo (sin correo del cliente, SMTP sin configurar o rechazo).
        /// </summary>
        public static async Task<string?> NotificarAsync(EmailService email, Operacione op, bool esZarpe, string usuario, string? copiaCc)
        {
            string? correo = op.IdClienteNavigation?.Correo?.Trim();
            if (string.IsNullOrWhiteSpace(correo))
                return "el cliente no tiene correo registrado";
            var (valido, mensaje) = email.DiagnosticarConfiguracion();
            if (!valido) return mensaje;
            var (asunto, html) = esZarpe ? CorreoZarpe(op) : CorreoArribo(op);
            try
            {
                await email.EnviarCorreoAsync(correo, asunto, html, string.IsNullOrWhiteSpace(copiaCc) ? null : copiaCc);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            if (esZarpe) op.CorreoZarpeEnviado = DateTime.Now; else op.CorreoArriboEnviado = DateTime.Now;
            RegistrarHito(op, usuario, "CORREO", $"{(esZarpe ? "Confirmación de zarpe" : "Confirmación de arribo")} enviada a: {correo}");
            return null;
        }

        // ---------------------------------------------------------------------------------
        // Alertas del Mainboard
        // ---------------------------------------------------------------------------------
        public sealed record Alerta(string Clave, string Html);

        private static string Caja(string tono, string icono, string titulo, string texto)
        {
            (string borde, string color, string fondo) = tono switch
            {
                "danger" => ("border-danger", "text-danger", "bg-danger bg-opacity-10"),
                "warning" => ("border-warning", "", "bg-light"),
                "success" => ("border-success", "text-success", "bg-light"),
                _ => ("border-info", "text-info", "bg-light"),
            };
            string estiloTitulo = tono == "warning" ? " style='color:#d97706;'" : "";
            return $"<div class='p-2 mb-1 rounded {fondo} border-start border-3 {borde} text-dark shadow-sm' style='font-size:11px;'>" +
                   $"<strong class='d-block {color}'{estiloTitulo}><i class='bi {icono} me-1'></i> {titulo}</strong>{texto}</div>";
        }

        /// <summary>Alertas del ciclo de la nave vigentes para el booking (vacía si no aplica).</summary>
        public static List<Alerta> Alertas(Operacione op, DateTime ahora)
        {
            var lista = new List<Alerta>();
            if (!AplicaCiclo(op)) return lista;
            // Si el destino ya se cerró (retirado / vacío devuelto) no queda nada que vigilar.
            if (PosicionEstado(op.EstadoWorkflow) >= PosicionEstado("Contenedor Retirado")) return lista;
            var hoy = ahora.Date;
            string nave = H(string.IsNullOrWhiteSpace(op.Nave) ? "la nave" : op.Nave);

            // Zarpe: activa desde el día de la ETD hasta que se guarda el Zarpe Real.
            if (op.EtdReal == null && op.EtdPol.HasValue && op.EtdPol.Value.Date <= hoy)
            {
                int atraso = (hoy - op.EtdPol.Value.Date).Days;
                lista.Add(new Alerta("zarpe", atraso == 0
                    ? Caja("warning", "bi-water", "ZARPE HOY · POR CONFIRMAR", $"ETD {op.EtdPol:dd/MM}. Revisa que {nave} zarpe sin retrasos ni roleos y registra el Zarpe Real.")
                    : Caja("danger", "bi-water", "ZARPE SIN CONFIRMAR", $"La ETD fue el {op.EtdPol:dd/MM} (hace {atraso} día{(atraso == 1 ? "" : "s")}). Confirma el zarpe de {nave} y registra el Zarpe Real.")));
            }

            // Llegada: mientras no haya Arribo Efectivo.
            if (op.EtaReal == null && op.EtaPod.HasValue)
            {
                int dias = (op.EtaPod.Value.Date - hoy).Days;
                if (dias <= 0)
                {
                    lista.Add(new Alerta("arribo", Caja(dias < 0 ? "danger" : "warning", "bi-geo-alt-fill", "ARRIBADO A DESTINO · POR CONFIRMAR",
                        $"ETA {op.EtaPod:dd/MM}. Verifica la descarga efectiva y registra el Arribo Efectivo (se envía la confirmación al cliente).")));
                }
                else
                {
                    if (dias <= 10 && op.AvisoLlegadaEnviado == null)
                        lista.Add(new Alerta("aviso", Caja(dias <= 3 ? "danger" : "warning", "bi-envelope-paper", "AVISO DE LLEGADA PENDIENTE",
                            $"Faltan {dias} día{(dias == 1 ? "" : "s")} para la ETA ({op.EtaPod:dd/MM}). Revisa el tracking y envía el aviso de llegada al cliente.")));
                    if (dias <= 3 && op.RevisionNave3 == null)
                        lista.Add(new Alerta("revision3", Caja("info", "bi-binoculars", "REVISAR NAVE · 3 DÍAS",
                            $"Confirma en el tracking que {nave} no tenga demoras antes del arribo ({op.EtaPod:dd/MM}).")));
                    else if (dias <= 5 && dias > 3 && op.RevisionNave5 == null)
                        lista.Add(new Alerta("revision5", Caja("info", "bi-binoculars", "REVISAR NAVE · 5 DÍAS",
                            $"Confirma en el tracking que {nave} no tenga demoras antes del arribo ({op.EtaPod:dd/MM}).")));
                }
            }
            return lista;
        }
    }
}
