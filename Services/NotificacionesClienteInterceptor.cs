using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    // =====================================================================================
    // Crea los avisos del portal de clientes al guardar (15-nov-2026).
    // Mira lo que cambia en cada SaveChanges, venga de la pantalla que venga (Mainboard,
    // Detalle, Marítimo, Documental, Itinerarios, Finanzas, Soporte, procesos automáticos):
    //   - Operación: fechas de la nave (ETD, ETA, stacking, corte), cambio de nave, estado del
    //     workflow, zarpe/arribo reales, contenedor asignado, LAR autorizado, retiro y devolución.
    //   - Contenedores (Unidadestecnicas): estado y fechas en destino.
    //   - Documental: archivos nuevos y borrador de BL enviado para aprobación.
    //   - Facturas al cliente, respuestas de Soporte, cotizaciones respondidas, roleos atendidos.
    // Al confirmar el guardado escribe los avisos con un DbContext propio. Si algo falla, el
    // guardado original no se ve afectado (solo queda en el log). Lo que hace el propio cliente
    // en el portal no le genera avisos.
    // =====================================================================================
    public sealed class NotificacionesClienteInterceptor : SaveChangesInterceptor
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<NotificacionesClienteInterceptor> _log;
        private readonly ConditionalWeakTable<DbContext, List<Evento>> _pendientes = new();
        private static DateTime _pausaHasta = DateTime.MinValue; // tabla ausente: no reintentar en cada guardado

        public NotificacionesClienteInterceptor(IServiceScopeFactory scopes, IHttpContextAccessor http, ILogger<NotificacionesClienteInterceptor> log)
        {
            _scopes = scopes; _http = http; _log = log;
        }

        private sealed class Evento
        {
            public string Tipo = "";
            public int? IdOperacion;
            public Operacione? OpNueva;          // operación recién creada (el Id llega al guardar)
            public int? IdCliente;
            public int? IdTicket;
            public string Titulo = "";           // {booking} se reemplaza al guardar
            public List<string> Lineas = new();
            public string? Url;                  // {id} = IdOperacion
            public string? Clave;                // agrupa avisos iguales (fechas de la nave)
            public string? Nave;
            public bool Correo = true;
        }

        // ---------------------------------------------------------------------------------
        // Catálogos
        // ---------------------------------------------------------------------------------
        private static readonly (string Prop, string Nombre)[] Documentos =
        {
            ("EvidenciaMatriz", "Matriz documental"), ("RutaBL", "Bill of Lading (BL)"),
            ("EvidenciaOrigen1", "Certificado de origen"), ("EvidenciaOrigen2", "Certificado de origen"), ("EvidenciaOrigen3", "Certificado de origen"), ("EvidenciaOrigen4", "Certificado de origen"),
            ("RutaCertificadoOrigen", "Certificado de origen"),
            ("EvidenciaFito1", "Certificado fitosanitario (SAG)"), ("EvidenciaFito2", "Certificado fitosanitario (SAG)"), ("EvidenciaFito3", "Certificado fitosanitario (SAG)"), ("EvidenciaFito4", "Certificado fitosanitario (SAG)"),
            ("EvidenciaSanitario1", "Certificado sanitario"), ("EvidenciaSanitario2", "Certificado sanitario"), ("EvidenciaSanitario3", "Certificado sanitario"), ("EvidenciaSanitario4", "Certificado sanitario"),
            ("EvidenciaCap1", "Certificado de captura"), ("EvidenciaCap2", "Certificado de captura"), ("EvidenciaCap3", "Certificado de captura"), ("EvidenciaCap4", "Certificado de captura"),
            ("RutaFactura", "Factura de exportación"), ("RutaGuia", "Guía de despacho"), ("RutaPacking", "Packing list"),
            ("RutaInstructivo", "Instructivo"), ("RutaBookingAtilson", "Booking Atilson"), ("RutaLibreVenta", "Certificado de libre venta"),
            ("RutaCertExtra", "Certificado extra (HACCP)"), ("RutaFullSet", "Full set documental"), ("RutaDhl", "Envío courier (DHL)"),
            ("RutaCapturaAga", "Captura agencia"), ("EvidenciaCoa1", "COA"), ("EvidenciaDt1", "DT"),
            ("EvidenciaCod1", "CODAUT"), ("EvidenciaCod2", "CODAUT"), ("EvidenciaCod3", "CODAUT"), ("EvidenciaCod4", "CODAUT"),
            ("EvidenciaCla1", "Clave"), ("EvidenciaCla2", "Clave"), ("EvidenciaCla3", "Clave"), ("EvidenciaCla4", "Clave"),
            ("EvidenciaNep1", "NEPPEX"), ("EvidenciaNep2", "NEPPEX"), ("EvidenciaNep3", "NEPPEX"), ("EvidenciaNep4", "NEPPEX"),
        };

        private static string Norm(string? s)
        {
            string t = (s ?? "").Trim().ToUpperInvariant();
            return t.Replace("Á", "A").Replace("É", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ú", "U");
        }

        /// <summary>Estado del workflow en palabras del cliente; null si no se avisa.</summary>
        public static string? EstadoCliente(string? estado) => Norm(estado) switch
        {
            "CONFIRMADO" => "Reserva confirmada",
            "EN RUTA PLANTA" => "Camión en ruta a tu planta",
            "ARRIBADO A PLANTA" => "Camión en tu planta",
            "SALIDA DE PLANTA" => "Carga salió de planta",
            "EN RUTA A PUERTO" => "Carga en ruta a puerto",
            "EN PUERTO" => "Carga en puerto",
            "ENTREGADO A STACKING" => "Contenedor ingresado a stacking",
            "EMBARCADO" => "Contenedor embarcado",
            "ZARPE" => "Zarpe de la nave",
            "PROXIMO ARRIBO" => "Próximo arribo",
            "ARRIBADO" => "Arribo a destino",
            "CONTENEDOR RETIRADO" => "Contenedor retirado en destino",
            "VACIO DEVUELTO" => "Contenedor vacío devuelto",
            var e when e.StartsWith("CANCELADO") => "Operación cancelada",
            _ => null
        };

        private static string F(DateTime? d) => FechasNave.Texto(d);

        private static string Clave(string texto)
        {
            var h = SHA1.HashData(Encoding.UTF8.GetBytes(texto));
            return Convert.ToHexString(h, 0, 12);
        }

        // ---------------------------------------------------------------------------------
        // Antes de guardar: qué cambió
        // ---------------------------------------------------------------------------------
        private bool HechoPorCliente()
        {
            var u = _http.HttpContext?.User;
            return u?.Identity?.IsAuthenticated == true && u.IsInRole("Cliente");
        }

        private void AnotarOrigen()
        {
            var req = _http.HttpContext?.Request;
            if (req?.Host.HasValue == true) NotificacionesClienteService.OrigenConocido = $"{req.Scheme}://{req.Host.Value}";
        }

        private static bool Cambio<T>(EntityEntry e, string prop, out T? antes, out T? ahora)
        {
            antes = default; ahora = default;
            if (e.Metadata.FindProperty(prop) == null) return false;
            var p = e.Property(prop);
            ahora = (T?)p.CurrentValue;
            if (e.State == EntityState.Added) return ahora != null;
            antes = (T?)p.OriginalValue;
            return !Equals(antes, ahora);
        }

        private List<Evento> Detectar(DbContext ctx)
        {
            var ev = new List<Evento>();
            ctx.ChangeTracker.DetectChanges();
            var ops = new Dictionary<int, Evento>(); // SEGUIMIENTO por operación (para juntar líneas)
            Evento Seg(int idOp)
            {
                if (!ops.TryGetValue(idOp, out var e))
                {
                    e = new Evento { Tipo = NotificacionesClienteService.TSeguimiento, IdOperacion = idOp, Url = "/PortalCliente/Operaciones?abrir={id}", Correo = false };
                    ops[idOp] = e; ev.Add(e);
                }
                return e;
            }

            // Primero las operaciones: así un contenedor no repite el estado que ya avisa su booking.
            foreach (var e in ctx.ChangeTracker.Entries().OrderBy(x => x.Entity is Operacione ? 0 : 1).ToList())
            {
                if (e.State is not (EntityState.Added or EntityState.Modified)) continue;
                switch (e.Entity)
                {
                    case Operacione op:
                        DetectarOperacion(e, op, ev, Seg);
                        break;
                    case Unidadestecnica u when e.State == EntityState.Modified:
                        DetectarContenedor(e, u, Seg);
                        break;
                    case OperacionesDocumentale d:
                        DetectarDocumental(e, d, ev);
                        break;
                    case TransaccionesFinanciera tx:
                        DetectarFactura(e, tx, ev);
                        break;
                    case MensajeSoporte m when e.State == EntityState.Added && m.Autor == MensajeSoporte.AutorEquipo:
                        string texto = (m.Texto ?? "").Trim();
                        ev.Add(new Evento
                        {
                            Tipo = NotificacionesClienteService.TSoporte, IdTicket = m.IdTicket, Correo = false,
                            Titulo = "Respuesta del equipo a tu consulta",
                            Lineas = { texto.Length > 300 ? texto[..300] + "…" : texto },
                            Url = $"/PortalCliente/Soporte?t={m.IdTicket}"
                        });
                        break;
                    case Cotizacion c when e.State == EntityState.Modified && c.IdCliente > 0:
                        if (Cambio<string>(e, nameof(Cotizacion.Estado), out var estAntes, out var estAhora) && estAhora == Cotizacion.EstadoRespondida)
                            ev.Add(new Evento
                            {
                                Tipo = NotificacionesClienteService.TSolicitud, IdCliente = c.IdCliente, Correo = false,
                                Titulo = c.EsCotizacion ? $"Tu cotización N° {c.Id} fue respondida" : $"Tu solicitud de servicio N° {c.Id} fue respondida",
                                Lineas = { estAntes == Cotizacion.EstadoRespondida ? "Se actualizó la respuesta." : "Revisa la propuesta y sus condiciones." },
                                Url = "/PortalCliente/MisCotizaciones"
                            });
                        break;
                    case RoleoCancelacion r when e.State == EntityState.Modified && r.SolicitadoPorCliente:
                        if (Cambio<bool>(e, nameof(RoleoCancelacion.Atendido), out _, out var atendido) && atendido)
                        {
                            string que = r.Tipo switch { "CANCELACION" => "cancelación", "NO_SHOW" => "No Show", _ => "roleo" };
                            ev.Add(new Evento
                            {
                                Tipo = NotificacionesClienteService.TSolicitud, IdOperacion = r.IdOperacion,
                                Titulo = $"Tu solicitud de {que} fue atendida · Booking {{booking}}",
                                Lineas = { r.PenalidadConfirmada == "SI" ? "Aplica penalidad según condiciones de la naviera." : "Revisa el detalle del booking en el portal." },
                                Url = "/PortalCliente/Operaciones?abrir={id}"
                            });
                        }
                        break;
                }
            }
            ev.RemoveAll(x => x.Lineas.Count == 0 && x.Tipo == NotificacionesClienteService.TSeguimiento);
            return ev;
        }

        private static void DetectarOperacion(EntityEntry e, Operacione op, List<Evento> ev, Func<int, Evento> seg)
        {
            if (op.EsHistorica || op.IsDeleted) return;
            if (e.State == EntityState.Added)
            {
                if (op.IdCliente <= 0) return;
                ev.Add(new Evento
                {
                    Tipo = NotificacionesClienteService.TSolicitud, OpNueva = op, IdCliente = op.IdCliente,
                    Titulo = $"Nuevo booking registrado · {op.NumeroBooking ?? "S/N"}",
                    Lineas =
                    {
                        string.Join(" · ", new[] { op.Nave, op.EtdPol != null ? "ETD " + F(op.EtdPol) : null, op.EtaPod != null ? "ETA " + F(op.EtaPod) : null }.Where(x => !string.IsNullOrWhiteSpace(x)))
                    },
                    Url = "/PortalCliente/Operaciones?abrir={id}"
                });
                return;
            }

            // Fechas de la nave y cambio de nave.
            var lineas = new List<string>();
            var nuevos = new List<string>(); // la clave usa solo los valores nuevos (agrupa bookings con distinto valor anterior)
            void Fecha(string prop, string etiqueta)
            {
                if (!Cambio<DateTime?>(e, prop, out var a, out var b) || FechasNave.Igual(a, b)) return;
                nuevos.Add(etiqueta + "=" + (b?.ToString("yyyyMMddHHmm") ?? "-"));
                lineas.Add(b == null ? $"{etiqueta}: se quitó (era {F(a)})"
                         : a == null ? $"{etiqueta}: {F(b)}"
                         : $"{etiqueta}: {F(a)} → {F(b)}");
            }
            if (Cambio<string>(e, nameof(Operacione.Nave), out var naveAntes, out var naveAhora)
                && !string.IsNullOrWhiteSpace(naveAntes) && !string.IsNullOrWhiteSpace(naveAhora)
                && !string.Equals(naveAntes.Trim(), naveAhora.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                lineas.Add($"Nave: {naveAntes.Trim()} → {naveAhora.Trim()}");
                nuevos.Add("Nave=" + naveAhora.Trim().ToUpperInvariant());
            }
            Fecha(nameof(Operacione.FechaStacking), "Inicio de stacking");
            Fecha(nameof(Operacione.LateArrival), FechasNave.LarEnTramite(op) ? "Cierre de stacking (Late Arrival)" : "Cierre de stacking");
            Fecha(nameof(Operacione.CutOffMatriz), "Corte documental");
            Fecha(nameof(Operacione.EtdPol), "ETD (zarpe estimado)");
            Fecha(nameof(Operacione.EtaPod), "ETA (arribo estimado)");
            if (lineas.Count > 0)
            {
                bool soloStacking = lineas.All(l => l.StartsWith("Inicio de stacking") || l.StartsWith("Cierre de stacking"));
                string nave = (op.Nave ?? "").Trim();
                ev.Add(new Evento
                {
                    Tipo = NotificacionesClienteService.TFechas, IdOperacion = op.Id, IdCliente = op.IdCliente > 0 ? op.IdCliente : null,
                    Nave = nave,
                    Titulo = (soloStacking ? "Stacking actualizado" : lineas.Any(l => l.StartsWith("Nave:")) ? "Cambio de nave" : "Cambio de fechas")
                             + (nave.Length > 0 ? " · " + nave : " · Booking {booking}"),
                    Lineas = lineas,
                    Clave = "fechas:" + Clave(op.IdCliente + "|" + nave.ToUpperInvariant() + "|" + string.Join("|", nuevos)),
                    Url = "/PortalCliente/Operaciones?abrir={id}"
                });
            }

            // Seguimiento.
            var s = new List<(string Linea, bool Correo)>();
            if (Cambio<string>(e, nameof(Operacione.EstadoWorkflow), out _, out var estado) && EstadoCliente(estado) is string est)
            {
                bool zarpeArribo = Norm(estado) is "ZARPE" or "ARRIBADO";
                s.Add((est, !zarpeArribo));
            }
            if (Cambio<DateTime?>(e, nameof(Operacione.EtdReal), out var etdRealAntes, out var etdReal) && etdReal != null && etdRealAntes == null)
                s.Add(($"Zarpe confirmado: {F(etdReal)}", false));
            if (Cambio<DateTime?>(e, nameof(Operacione.EtaReal), out var etaRealAntes, out var etaReal) && etaReal != null && etaRealAntes == null)
                s.Add(($"Arribo confirmado: {F(etaReal)}", false));
            if (Cambio<string>(e, nameof(Operacione.NumeroContenedor), out var contAntes, out var cont) && !string.IsNullOrWhiteSpace(cont) && string.IsNullOrWhiteSpace(contAntes))
                s.Add(($"Contenedor asignado: {cont}", true));
            if (Cambio<string>(e, nameof(Operacione.EstadoLar), out _, out var lar) && Norm(lar).Contains("AUTORIZADO"))
                s.Add(($"Late Arrival autorizado{(op.LateArrival != null ? ": nuevo cierre " + F(op.LateArrival) : "")}", true));
            if (Cambio<DateTime?>(e, nameof(Operacione.FechaArriboDestino), out var arrAntes, out var arr) && arr != null && arrAntes == null)
                s.Add(($"Contenedor en destino: {F(arr)}", true));
            if (Cambio<DateTime?>(e, nameof(Operacione.FechaRetiroDestino), out var retAntes, out var ret) && ret != null && retAntes == null)
                s.Add(($"Contenedor retirado en destino: {F(ret)}", true));
            if (Cambio<DateTime?>(e, nameof(Operacione.FechaDevolucionVacio), out var devAntes, out var dev) && dev != null && devAntes == null)
                s.Add(($"Contenedor vacío devuelto: {F(dev)}", true));
            if (s.Count > 0)
            {
                var g = seg(op.Id);
                if (op.IdCliente > 0) g.IdCliente = op.IdCliente;
                foreach (var (linea, _) in s) if (!g.Lineas.Contains(linea)) g.Lineas.Add(linea);
                // Zarpe y arribo ya los confirma por correo el ciclo de la nave: solo campana.
                g.Correo |= s.Any(x => x.Correo);
            }
        }

        private static void DetectarContenedor(EntityEntry e, Unidadestecnica u, Func<int, Evento> seg)
        {
            string cont = string.IsNullOrWhiteSpace(u.NroContenedor) ? $"Contenedor {u.Id}" : $"Contenedor {u.NroContenedor.Trim()}";
            var s = new List<string>();
            bool correo = false;
            if (Cambio<string>(e, nameof(Unidadestecnica.Estado), out _, out var estado) && EstadoCliente(estado) is string est)
            {
                s.Add($"{cont}: {est}");
                correo = Norm(estado) is not ("ZARPE" or "ARRIBADO");
            }
            if (Cambio<string>(e, nameof(Unidadestecnica.NroContenedor), out var nAntes, out var n) && !string.IsNullOrWhiteSpace(n) && string.IsNullOrWhiteSpace(nAntes))
                s.Add($"Contenedor asignado: {n.Trim()}");
            if (s.Count > 1 || s.Any(l => !l.StartsWith(cont))) correo = true;
            if (Cambio<DateTime?>(e, nameof(Unidadestecnica.FechaArriboDestino), out var a0, out var a) && a != null && a0 == null) { s.Add($"{cont}: en destino {F(a)}"); correo = true; }
            if (Cambio<DateTime?>(e, nameof(Unidadestecnica.FechaRetiroDestino), out var r0, out var r) && r != null && r0 == null) { s.Add($"{cont}: retirado en destino {F(r)}"); correo = true; }
            if (Cambio<DateTime?>(e, nameof(Unidadestecnica.FechaDevolucionVacio), out var d0, out var d) && d != null && d0 == null) { s.Add($"{cont}: vacío devuelto {F(d)}"); correo = true; }
            if (s.Count == 0) return;
            var g = seg(u.IdOperacion);
            bool agrego = false;
            foreach (var l in s)
            {
                // Si la operación ya avisa el mismo estado, no se repite por cada contenedor.
                string estadoTxt = l.Contains(": ") ? l[(l.IndexOf(": ", StringComparison.Ordinal) + 2)..] : l;
                if (g.Lineas.Contains(estadoTxt) || g.Lineas.Contains(l)) continue;
                g.Lineas.Add(l); agrego = true;
            }
            if (agrego) g.Correo |= correo;
        }

        private static void DetectarDocumental(EntityEntry e, OperacionesDocumentale d, List<Evento> ev)
        {
            var nuevos = new List<string>();
            foreach (var (prop, nombre) in Documentos)
                if (Cambio<string>(e, prop, out _, out var ruta) && !string.IsNullOrWhiteSpace(ruta) && !nuevos.Contains(nombre))
                    nuevos.Add(nombre);
            if (nuevos.Count > 0)
                ev.Add(new Evento
                {
                    Tipo = NotificacionesClienteService.TDocumento, IdOperacion = d.IdOperacion,
                    Titulo = (nuevos.Count == 1 ? $"Nuevo documento: {nuevos[0]}" : $"{nuevos.Count} documentos nuevos") + " · Booking {booking}",
                    Lineas = { "Disponible en Matriz y documentos: " + string.Join(", ", nuevos) + "." },
                    Url = "/PortalCliente/Documentos#docs-{id}"
                });
            if (Cambio<string>(e, nameof(OperacionesDocumentale.EstadoDocumental), out _, out var est) && Norm(est) == "BORRADOR ENVIADO")
                ev.Add(new Evento
                {
                    Tipo = NotificacionesClienteService.TAccion, IdOperacion = d.IdOperacion,
                    Titulo = "Borrador de BL para tu revisión · Booking {booking}",
                    Lineas = { "Revisa el borrador y apruébalo o indica las correcciones." },
                    Url = "/PortalCliente/AprobarBL/{id}"
                });
        }

        private static void DetectarFactura(EntityEntry e, TransaccionesFinanciera tx, List<Evento> ev)
        {
            if (tx.TipoMovimiento != "INGRESO" || tx.EstadoFila != "FACTURADO") return;
            bool nueva = e.State == EntityState.Added
                || (Cambio<string>(e, nameof(TransaccionesFinanciera.EstadoFila), out _, out _))
                || (Cambio<string>(e, nameof(TransaccionesFinanciera.NumeroDocumento), out var nAntes, out var n) && !string.IsNullOrWhiteSpace(n) && string.IsNullOrWhiteSpace(nAntes));
            if (!nueva) return;
            string numero = string.IsNullOrWhiteSpace(tx.NumeroDocumento) ? "" : $"N° {tx.NumeroDocumento.Trim()} · ";
            string linea = $"Factura {numero}{tx.Concepto} · {tx.Moneda} {tx.MontoNeto.ToString("N2", new CultureInfo("es-CL"))}";
            var g = ev.FirstOrDefault(x => x.Tipo == NotificacionesClienteService.TFactura && x.IdOperacion == tx.IdOperacion && x.IdCliente == tx.IdCliente);
            if (g == null)
            {
                g = new Evento
                {
                    Tipo = NotificacionesClienteService.TFactura, IdOperacion = tx.IdOperacion, IdCliente = tx.IdCliente,
                    Titulo = "Factura emitida · Booking {booking}", Url = "/PortalCliente/Facturas#fact-{id}"
                };
                ev.Add(g);
            }
            if (!g.Lineas.Contains(linea)) g.Lineas.Add(linea);
            if (g.Lineas.Count > 1) g.Titulo = $"{g.Lineas.Count} facturas emitidas · Booking {{booking}}";
        }

        // ---------------------------------------------------------------------------------
        // Interceptor
        // ---------------------------------------------------------------------------------
        private void Antes(DbContext? ctx)
        {
            if (ctx == null || DateTime.Now < _pausaHasta) return;
            try
            {
                AnotarOrigen();
                if (HechoPorCliente()) return;
                var ev = Detectar(ctx);
                if (ev.Count == 0) return;
                _pendientes.AddOrUpdate(ctx, ev);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Avisos del portal: no se pudieron preparar."); }
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Antes(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Antes(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (eventData.Context != null && _pendientes.TryGetValue(eventData.Context, out var ev))
            {
                _pendientes.Remove(eventData.Context);
                EscribirAsync(ev).GetAwaiter().GetResult();
            }
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null && _pendientes.TryGetValue(eventData.Context, out var ev))
            {
                _pendientes.Remove(eventData.Context);
                await EscribirAsync(ev);
            }
            return result;
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            if (eventData.Context != null) _pendientes.Remove(eventData.Context);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null) _pendientes.Remove(eventData.Context);
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------------------------
        // Después de guardar: escribir los avisos
        // ---------------------------------------------------------------------------------
        private async Task EscribirAsync(List<Evento> eventos)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AtilsonContext>();
                foreach (var e in eventos) if (e.OpNueva != null) e.IdOperacion = e.OpNueva.Id;

                var idsOp = eventos.Where(e => e.IdOperacion > 0).Select(e => e.IdOperacion!.Value).Distinct().ToList();
                var ops = new Dictionary<int, (int IdCliente, string? Booking, bool Visible)>();
                if (idsOp.Count > 0)
                    foreach (var o in await db.Operaciones.AsNoTracking().Where(o => idsOp.Contains(o.Id))
                                 .Select(o => new { o.Id, o.IdCliente, o.NumeroBooking, o.IsDeleted, o.EsHistorica }).ToListAsync())
                        ops[o.Id] = (o.IdCliente, o.NumeroBooking, !o.IsDeleted && !o.EsHistorica);
                var idsTicket = eventos.Where(e => e.IdTicket != null).Select(e => e.IdTicket!.Value).Distinct().ToList();
                var tickets = idsTicket.Count == 0 ? new Dictionary<int, int?>()
                    : await db.TicketsSoporte.AsNoTracking().Where(t => idsTicket.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.IdCliente);

                var nuevas = new List<NotificacionCliente>();
                foreach (var e in eventos)
                {
                    string? booking = null;
                    if (e.IdOperacion > 0)
                    {
                        if (!ops.TryGetValue(e.IdOperacion!.Value, out var o) || !o.Visible) continue;
                        e.IdCliente ??= o.IdCliente;
                        booking = o.Booking;
                    }
                    if (e.IdTicket != null && tickets.TryGetValue(e.IdTicket.Value, out var cliTicket)) e.IdCliente ??= cliTicket;
                    if (e.IdCliente is not > 0 || e.Lineas.Count == 0 && string.IsNullOrWhiteSpace(e.Titulo)) continue;

                    string b = string.IsNullOrWhiteSpace(booking) ? "S/N" : booking.Trim();
                    if (string.IsNullOrWhiteSpace(e.Titulo) && e.Lineas.Count > 0)
                    {
                        string primera = e.Lineas[0].Length > 110 ? e.Lineas[0][..110] + "…" : e.Lineas[0];
                        e.Titulo = primera + (e.Lineas.Count > 1 ? $" (+{e.Lineas.Count - 1})" : "") + " · Booking {booking}";
                    }
                    string url = (e.Url ?? "").Replace("{id}", e.IdOperacion?.ToString() ?? "");
                    string titulo = e.Titulo.Replace("{booking}", b);
                    var lineas = e.Lineas.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

                    // Fechas de la nave: mismo cambio en varios bookings del cliente = un solo aviso.
                    if (e.Tipo == NotificacionesClienteService.TFechas)
                    {
                        var igual = nuevas.FirstOrDefault(n => n.Clave == e.Clave && n.IdCliente == e.IdCliente)
                            ?? await db.NotificacionesCliente.FirstOrDefaultAsync(n => n.Clave == e.Clave && n.IdCliente == e.IdCliente
                                && n.FechaLeida == null && n.Fecha >= DateTime.Now.AddMinutes(-30));
                        if (igual != null)
                        {
                            igual.Mensaje = AgregarBooking(igual.Mensaje, b);
                            igual.Fecha = DateTime.Now;
                            if (igual.CorreoEstado != NotificacionCliente.CorreoPendiente && igual.CorreoEstado != null) igual.CorreoEstado = NotificacionCliente.CorreoPendiente;
                            continue;
                        }
                        lineas.Add("Booking: " + b);
                    }

                    string mensaje = string.Join("\n", lineas);
                    nuevas.Add(new NotificacionCliente
                    {
                        IdCliente = e.IdCliente!.Value,
                        IdOperacion = e.IdOperacion > 0 ? e.IdOperacion : null,
                        Tipo = e.Tipo,
                        Titulo = titulo.Length > 200 ? titulo[..200] : titulo,
                        Mensaje = mensaje.Length > 2000 ? mensaje[..2000] : mensaje,
                        Url = url.Length > 300 ? url[..300] : url,
                        Clave = e.Clave,
                        Fecha = DateTime.Now,
                        CorreoEstado = e.Correo ? NotificacionCliente.CorreoPendiente : null
                    });
                }
                db.NotificacionesCliente.AddRange(nuevas.Where(n => n.Id == 0));
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                if ((ex.InnerException?.Message ?? ex.Message).Contains("NotificacionesCliente", StringComparison.OrdinalIgnoreCase))
                {
                    _pausaHasta = DateTime.Now.AddMinutes(10);
                    _log.LogWarning("Avisos del portal en pausa 10 min: falta la tabla (correr Sql/2026-11_notificaciones_portal.sql).");
                }
                else _log.LogWarning(ex, "Avisos del portal: no se pudieron guardar.");
            }
        }

        // "Booking: A" → "Bookings: A, B"
        private static string AgregarBooking(string? mensaje, string booking)
        {
            var lineas = (mensaje ?? "").Split('\n').ToList();
            int i = lineas.FindIndex(l => l.StartsWith("Booking: ") || l.StartsWith("Bookings: "));
            if (i < 0) { lineas.Add("Booking: " + booking); return string.Join("\n", lineas); }
            var lista = lineas[i][(lineas[i].IndexOf(": ", StringComparison.Ordinal) + 2)..].Split(", ").ToList();
            if (!lista.Contains(booking)) lista.Add(booking);
            lineas[i] = (lista.Count == 1 ? "Booking: " : "Bookings: ") + string.Join(", ", lista);
            return string.Join("\n", lineas);
        }
    }
}
