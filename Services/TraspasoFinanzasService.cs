using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Services
{
    // ============================================================================
    // Traspaso Operaciones -> Finanzas (oct-2026)
    // ----------------------------------------------------------------------------
    // 1) Operaciones "finaliza la tarea" cuando los contenedores están embarcados y
    //    la nave zarpó. Desde ahí Finanzas ve el booking arriba con la alerta NUEVO.
    // 2) Una tarea finalizada bloquea los cambios que afectan costos o cobros (ver
    //    Filters/TraspasoFinanzasPageFilter). Para corregir algo (p. ej. una
    //    corrección de BL), Operaciones la "reabre" con un motivo y Finanzas la ve
    //    como REABIERTA. Al finalizar de nuevo vuelve a salir como NUEVO, con las
    //    filas que cambiaron desde la finalización anterior.
    // 3) "Vacío devuelto" es otro hito, posterior, y no depende de esto.
    // Finanzas sigue viendo las provisiones como siempre; la alerta solo ordena su
    // cola de trabajo.
    // ============================================================================
    public static class TraspasoFinanzas
    {
        public const string EstadoFinalizada = "FINALIZADA";
        public const string EstadoReabierta = "REABIERTA";

        public static readonly string[] MotivosReapertura =
        {
            "Corrección de BL",
            "Nuevo gasto o extracosto",
            "Cambio de contenedor",
            "Rolleo",
            "Otro"
        };

        public static bool EstaFinalizada(Operacione op) => op.EstadoTarea == EstadoFinalizada;
        public static bool EstaReabierta(Operacione op) => op.EstadoTarea == EstadoReabierta;

        // Finanzas aún no abre el booking desde la última finalización.
        public static bool EsNuevaParaFinanzas(Operacione op) =>
            EstaFinalizada(op) && op.FechaFinalizacionTarea.HasValue
            && (!op.FechaVistoFinanzas.HasValue || op.FechaVistoFinanzas.Value < op.FechaFinalizacionTarea.Value);

        // Reabierta y Finanzas aún no la ha visto desde la reapertura.
        public static bool EsReaperturaSinVer(Operacione op) =>
            EstaReabierta(op) && op.FechaReaperturaTarea.HasValue
            && (!op.FechaVistoFinanzas.HasValue || op.FechaVistoFinanzas.Value < op.FechaReaperturaTarea.Value);

        // Orden de la cola de Finanzas: 0 = NUEVO, 1 = REABIERTA sin ver, 2 = REABIERTA vista, 3 = resto.
        public static int PrioridadFinanzas(Operacione op) =>
            EsNuevaParaFinanzas(op) ? 0 : EsReaperturaSinVer(op) ? 1 : EstaReabierta(op) ? 2 : 3;

        // ------------------------------------------------------------------ estados
        private static string Norm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var formD = s.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (var c in formD)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // Posición del estado del contenedor en el flujo del tablero de Operaciones.
        // -1 = cancelado, 0 = sin estado / desconocido.
        public static int OrdenEstado(string? estado)
        {
            switch (Norm(estado))
            {
                case "SOLICITADO": return 10;
                case "CONFIRMADO": return 20;
                case "ENVIO A CLIENTE": return 30;
                case "EN RUTA PLANTA": return 40;
                case "ARRIBADO A PLANTA": return 45;
                case "SALIDA DE PLANTA": return 50;
                case "EN RUTA A PUERTO": return 60;
                case "EN PUERTO": return 70;
                case "ENTREGADO A STACKING": return 80;
                case "EMBARCADO": return 90;
                case "ZARPE": return 100;
                case "PROXIMO ARRIBO": return 110;
                case "ARRIBADO": return 120;
                case "CONTENEDOR RETIRADO": return 130;
                case "VACIO DEVUELTO":
                case "FINALIZADO":
                case "FACTURADO":
                case "CERRADO": return 140;
                case "CANCELADO": return -1;
                default: return 0;
            }
        }

        public const int OrdenEntregadoStacking = 80;
        public const int OrdenEmbarcado = 90;
        public const int OrdenZarpe = 100;
        public const int OrdenProximoArribo = 110;

        private static readonly int[] ServiciosMaritimos = { 1, 2, 3, 5, 8, 9, 10, 12 };
        private static readonly int[] ServiciosTerrestres = { 1, 2, 4, 6, 8, 9, 11, 13 };

        public sealed class Requisitos
        {
            public bool Cumple => Faltantes.Count == 0;
            public List<string> Faltantes { get; } = new();
            public List<string> Cumplidos { get; } = new();
            public string Resumen => Cumple ? "Lista para finalizar" : string.Join(" · ", Faltantes);
            // Servicio solo documental: se puede finalizar en cualquier momento, pero no se
            // destaca como "Lista para finalizar" (sería todo el tiempo).
            public bool SinRequisitos { get; set; }
        }

        // Contenedores del booking tal como los muestra el tablero: el 1° vive en la
        // operación (EstadoWorkflow) y los demás en Unidadestecnicas (Estado).
        public static List<(string Etiqueta, int Orden)> Contenedores(Operacione op)
        {
            var lista = new List<(string, int)>
            {
                (string.IsNullOrWhiteSpace(op.NumeroContenedor) ? "1° contenedor" : op.NumeroContenedor!, OrdenEstado(op.EstadoWorkflow))
            };
            if (op.Unidadestecnicas != null)
                foreach (var u in op.Unidadestecnicas.OrderBy(u => u.Id))
                    lista.Add((string.IsNullOrWhiteSpace(u.NroContenedor) ? $"Contenedor U{u.Id}" : u.NroContenedor!, OrdenEstado(u.Estado)));
            return lista;
        }

        // Requisitos para finalizar la tarea:
        //   - Servicio con marítimo: todos los contenedores Embarcados (o más allá) y zarpe
        //     confirmado (ETD real cargado o contenedores en "Zarpe" o más allá).
        //   - Solo terrestre: todos los contenedores entregados en puerto (Entregado a Stacking).
        //   - Solo documental: sin requisitos de contenedores.
        // Los contenedores cancelados no cuentan.
        public static Requisitos EvaluarRequisitos(Operacione op)
        {
            var r = new Requisitos();
            int serv = op.IdTipoServicio ?? 0;
            bool maritimo = ServiciosMaritimos.Contains(serv);
            bool terrestre = ServiciosTerrestres.Contains(serv);

            var conts = Contenedores(op).Where(c => c.Orden >= 0).ToList();
            if (!conts.Any())
            {
                r.Faltantes.Add("El booking está cancelado");
                return r;
            }

            if (maritimo)
            {
                int sinEmbarcar = conts.Count(c => c.Orden < OrdenEmbarcado);
                if (sinEmbarcar > 0) r.Faltantes.Add(sinEmbarcar == conts.Count && conts.Count == 1
                    ? "Contenedor sin embarcar"
                    : $"{sinEmbarcar} de {conts.Count} contenedores sin embarcar");
                else r.Cumplidos.Add(conts.Count == 1 ? "Contenedor embarcado" : $"{conts.Count} contenedores embarcados");

                bool zarpe = op.EtdReal.HasValue || conts.All(c => c.Orden >= OrdenZarpe);
                if (!zarpe) r.Faltantes.Add("Falta confirmar el zarpe (ETD real o estado Zarpe)");
                else r.Cumplidos.Add(op.EtdReal.HasValue ? $"Zarpe confirmado {op.EtdReal.Value:dd/MM/yyyy}" : "Nave zarpada");
            }
            else if (terrestre)
            {
                int sinEntregar = conts.Count(c => c.Orden < OrdenEntregadoStacking);
                if (sinEntregar > 0) r.Faltantes.Add($"{sinEntregar} de {conts.Count} contenedores sin entregar en puerto");
                else r.Cumplidos.Add("Contenedores entregados en puerto");
            }
            else
            {
                r.SinRequisitos = true;
                r.Cumplidos.Add("Servicio documental: sin requisitos de contenedores");
            }
            return r;
        }

        // ------------------------------------------------------------------ bloqueo
        // Handlers de /Operaciones que cambian costos o cobros: con la tarea finalizada
        // se rechazan hasta que Operaciones la reabra. Los correos, certificados,
        // documentos, transbordos y los hitos de destino (arribo, retiro, vacío
        // devuelto) siguen permitidos.
        public static readonly HashSet<string> HandlersBloqueados = new(StringComparer.OrdinalIgnoreCase)
        {
            "AddCosto", "DeleteCosto", "UpdateResponsableCosto", "GuardarResponsabilidad",
            "RegistrarCorreccionBl", "RegistrarRoleoCancelacion", "ConfirmarItinerarioRoleo", "ActualizarPenalidadRoleo",
            "UpdateGate", "QuickEditMaritimo", "QuickEditTransporte", "QuickEditDocumental", "QuickEditGenerales",
            "QuickEditCourier", "EditarEmbarque", "GuardarAlmacenamiento", "SubirRespaldoAlmacen",
            "GuardarDepositoRetiro", "GuardarEmisionBl", "FinalizarServicio", "UpdateLar", "ToggleIngreso",
            "ActualizarModalidadCert", "Delete", "EnviarFinanzas"
        };

        // Cambio de estado del contenedor: con la tarea finalizada solo se permiten los
        // hitos de destino (Próximo Arribo en adelante).
        public static bool EstadoPermitidoConTareaFinalizada(string? estado) => OrdenEstado(estado) >= OrdenProximoArribo;

        public const string MensajeBloqueo =
            "La tarea de este booking está finalizada y Finanzas ya lo tomó. Para cambiar costos o cobros, primero usa \"Reabrir operación\" e indica el motivo.";

        // ------------------------------------------------------------------ foto financiera
        // Una línea por fila financiera; se compara entre finalizaciones.
        public static string Foto(IEnumerable<TransaccionesFinanciera> txs) =>
            string.Join("\n", txs
                .Select(t => $"{t.TipoMovimiento}|{t.Concepto}|{t.Moneda}|{t.MontoNeto.ToString("0.##", CultureInfo.InvariantCulture)}")
                .OrderBy(x => x, StringComparer.Ordinal));

        // Diferencias entre dos fotos: "+ agregado", "− quitado", "~ cambio de monto".
        public static List<string> Diferencias(string? antes, string? despues)
        {
            static Dictionary<string, List<(string Moneda, string Monto)>> Parse(string? foto)
            {
                var d = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
                foreach (var linea in (foto ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = linea.Split('|');
                    if (p.Length < 4) continue;
                    string clave = $"{p[0]}|{p[1]}";
                    if (!d.TryGetValue(clave, out var l)) d[clave] = l = new List<(string, string)>();
                    l.Add((p[2], p[3]));
                }
                return d;
            }
            static string Tipo(string clave) => clave.StartsWith("INGRESO") ? "cobro" : "costo";
            static string Concepto(string clave) => clave.Substring(clave.IndexOf('|') + 1);
            static string Monto((string Moneda, string Monto) m)
            {
                decimal.TryParse(m.Monto, NumberStyles.Any, CultureInfo.InvariantCulture, out var v);
                return $"{m.Moneda} {v.ToString(m.Moneda == "CLP" ? "N0" : "N2", new CultureInfo("es-CL"))}";
            }

            var a = Parse(antes);
            var b = Parse(despues);
            // Una línea por concepto, juntando costo y cobro: "+ Corrección de BL N°3: costo CLP 95.000 · cobro CLP 95.000".
            var porConcepto = new Dictionary<string, List<(char Signo, string Texto)>>(StringComparer.Ordinal);
            void Agregar(string clave, char signo, string texto)
            {
                string c = Concepto(clave);
                if (!porConcepto.TryGetValue(c, out var l)) porConcepto[c] = l = new List<(char, string)>();
                l.Add((signo, $"{Tipo(clave)} {texto}"));
            }
            foreach (var kv in b)
            {
                if (!a.TryGetValue(kv.Key, out var previo))
                    Agregar(kv.Key, '+', string.Join(" + ", kv.Value.Select(Monto)));
                else if (!previo.Select(Monto).OrderBy(x => x).SequenceEqual(kv.Value.Select(Monto).OrderBy(x => x)))
                    Agregar(kv.Key, '~', $"{string.Join(" + ", previo.Select(Monto))} → {string.Join(" + ", kv.Value.Select(Monto))}");
            }
            foreach (var kv in a)
                if (!b.ContainsKey(kv.Key))
                    Agregar(kv.Key, '−', string.Join(" + ", kv.Value.Select(Monto)));

            var res = new List<string>();
            foreach (var kv in porConcepto)
            {
                char signo = kv.Value.All(x => x.Signo == '+') ? '+' : kv.Value.All(x => x.Signo == '−') ? '−' : '~';
                res.Add($"{signo} {kv.Key}: {string.Join(" · ", kv.Value.OrderBy(x => x.Texto.StartsWith("costo") ? 0 : 1).Select(x => x.Texto))}");
            }
            return res;
        }
    }

    public class TraspasoFinanzasService
    {
        private readonly AtilsonContext _context;

        public TraspasoFinanzasService(AtilsonContext context) => _context = context;

        public sealed class Resultado
        {
            public bool Ok { get; init; }
            public string Mensaje { get; init; } = "";
        }

        private static string Usuario(string? usuario) => string.IsNullOrWhiteSpace(usuario) ? "Operaciones" : usuario!;

        private static void Hito(Operacione op, string usuario, string descripcion)
        {
            string linea = $"{DateTime.Now:dd/MM/yyyy HH:mm}|{usuario}|GENERAL|{descripcion}\n";
            op.HistorialCorreos = linea + (op.HistorialCorreos ?? "");
        }

        public async Task<Resultado> FinalizarAsync(int idOperacion, string? usuario)
        {
            var op = await _context.Operaciones
                .Include(o => o.Unidadestecnicas)
                .FirstOrDefaultAsync(o => o.Id == idOperacion && !o.IsDeleted);
            if (op == null) return new Resultado { Ok = false, Mensaje = "No se encontró la operación." };
            if (TraspasoFinanzas.EstaFinalizada(op))
                return new Resultado { Ok = false, Mensaje = "La tarea ya está finalizada." };

            var req = TraspasoFinanzas.EvaluarRequisitos(op);
            if (!req.Cumple)
                return new Resultado { Ok = false, Mensaje = "Aún no se puede finalizar: " + string.Join("; ", req.Faltantes) + "." };

            string u = Usuario(usuario);
            var ahora = DateTime.Now;
            var foto = TraspasoFinanzas.Foto(await _context.TransaccionesFinancieras.AsNoTracking()
                .Where(t => t.IdOperacion == op.Id).ToListAsync());

            bool eraReabierta = TraspasoFinanzas.EstaReabierta(op);
            op.EstadoTarea = TraspasoFinanzas.EstadoFinalizada;
            op.FechaFinalizacionTarea = ahora;
            op.UsuarioFinalizacionTarea = u;
            op.VecesFinalizadaTarea = op.VecesFinalizadaTarea + 1;
            op.FechaModificacion = ahora;
            op.UsuarioModificador = u;
            Hito(op, u, eraReabierta
                ? "Tarea finalizada nuevamente: los cambios de la reapertura quedan traspasados a Finanzas."
                : "Tarea finalizada: contenedores embarcados y nave zarpada. Traspasado a Finanzas.");

            _context.OperacionesTraspasoFinanzas.Add(new OperacionTraspasoFinanzas
            {
                IdOperacion = op.Id,
                Evento = OperacionTraspasoFinanzas.EventoFinalizada,
                Usuario = u,
                Fecha = ahora,
                Resumen = foto
            });
            await _context.SaveChangesAsync();
            return new Resultado
            {
                Ok = true,
                Mensaje = eraReabierta
                    ? $"Tarea del booking {op.NumeroBooking} finalizada nuevamente. Finanzas la verá como NUEVO con los cambios."
                    : $"Tarea del booking {op.NumeroBooking} finalizada. Finanzas ya la ve como NUEVO."
            };
        }

        /// <summary>Extensiones aceptadas para el archivo de respaldo de una reapertura.</summary>
        public static readonly string[] ExtensionesRespaldo = { ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".xlsx", ".xls", ".csv", ".docx", ".doc", ".msg", ".eml", ".txt", ".zip" };
        public const long TamanoMaximoRespaldo = 15 * 1024 * 1024;

        /// <summary>Revisa el archivo de respaldo antes de guardarlo. Devuelve null si está bien.</summary>
        public static string? ValidarRespaldo(string? nombre, long largo)
        {
            if (string.IsNullOrWhiteSpace(nombre) || largo <= 0) return "El archivo de respaldo está vacío.";
            if (largo > TamanoMaximoRespaldo) return "El archivo de respaldo supera los 15 MB.";
            string ext = System.IO.Path.GetExtension(nombre).ToLowerInvariant();
            if (!ExtensionesRespaldo.Contains(ext))
                return "Tipo de archivo no permitido para el respaldo. Usa PDF, imagen, Excel, Word, correo (.msg/.eml), texto o ZIP.";
            return null;
        }

        /// <summary>Validación previa a guardar el respaldo (misma regla que ReabrirAsync).</summary>
        public async Task<string?> PuedeReabrirAsync(int idOperacion, string? motivo, string? comentario)
        {
            var op = await _context.Operaciones.AsNoTracking().FirstOrDefaultAsync(o => o.Id == idOperacion && !o.IsDeleted);
            if (op == null) return "No se encontró la operación.";
            if (!TraspasoFinanzas.EstaFinalizada(op)) return "Solo se puede reabrir una tarea finalizada.";
            motivo = (motivo ?? "").Trim();
            if (!TraspasoFinanzas.MotivosReapertura.Contains(motivo)) return "Elige el motivo de la reapertura.";
            if (motivo == "Otro" && string.IsNullOrWhiteSpace(comentario)) return "Con el motivo \"Otro\" el comentario es obligatorio.";
            return null;
        }

        public async Task<Resultado> ReabrirAsync(int idOperacion, string? motivo, string? comentario, string? usuario,
            string? rutaRespaldo = null, string? nombreRespaldo = null)
        {
            var op = await _context.Operaciones.FirstOrDefaultAsync(o => o.Id == idOperacion && !o.IsDeleted);
            if (op == null) return new Resultado { Ok = false, Mensaje = "No se encontró la operación." };
            if (!TraspasoFinanzas.EstaFinalizada(op))
                return new Resultado { Ok = false, Mensaje = "Solo se puede reabrir una tarea finalizada." };

            motivo = (motivo ?? "").Trim();
            comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
            if (!TraspasoFinanzas.MotivosReapertura.Contains(motivo))
                return new Resultado { Ok = false, Mensaje = "Elige el motivo de la reapertura." };
            if (motivo == "Otro" && comentario == null)
                return new Resultado { Ok = false, Mensaje = "Con el motivo \"Otro\" el comentario es obligatorio." };
            if (comentario != null && comentario.Length > 400) comentario = comentario.Substring(0, 400);

            string u = Usuario(usuario);
            var ahora = DateTime.Now;
            string motivoCompleto = comentario == null ? motivo : $"{motivo}: {comentario}";
            op.EstadoTarea = TraspasoFinanzas.EstadoReabierta;
            op.FechaReaperturaTarea = ahora;
            op.UsuarioReaperturaTarea = u;
            op.MotivoReaperturaTarea = motivoCompleto;
            op.FechaModificacion = ahora;
            op.UsuarioModificador = u;
            if (string.IsNullOrWhiteSpace(rutaRespaldo)) { rutaRespaldo = null; nombreRespaldo = null; }
            if (nombreRespaldo != null && nombreRespaldo.Length > 255) nombreRespaldo = nombreRespaldo.Substring(nombreRespaldo.Length - 255);
            Hito(op, u, $"Operación reabierta ({motivoCompleto}). Finanzas la ve como REABIERTA."
                + (rutaRespaldo != null ? $" Respaldo adjunto: {nombreRespaldo}." : ""));

            _context.OperacionesTraspasoFinanzas.Add(new OperacionTraspasoFinanzas
            {
                IdOperacion = op.Id,
                Evento = OperacionTraspasoFinanzas.EventoReabierta,
                Motivo = motivo,
                Comentario = comentario,
                Usuario = u,
                Fecha = ahora,
                RutaRespaldo = rutaRespaldo,
                NombreRespaldo = nombreRespaldo
            });
            await _context.SaveChangesAsync();
            return new Resultado { Ok = true, Mensaje = $"Booking {op.NumeroBooking} reabierto. Cuando termines los cambios, vuelve a \"Finalizar tarea\"." };
        }

        // Finanzas abrió el booking: deja de verse como NUEVO / reabierta sin ver.
        public async Task<bool> MarcarVistoAsync(int idOperacion, string? usuario)
        {
            var op = await _context.Operaciones.FirstOrDefaultAsync(o => o.Id == idOperacion && !o.IsDeleted);
            if (op == null) return false;
            if (!TraspasoFinanzas.EsNuevaParaFinanzas(op) && !TraspasoFinanzas.EsReaperturaSinVer(op)) return true;

            string u = string.IsNullOrWhiteSpace(usuario) ? "Finanzas" : usuario!;
            var ahora = DateTime.Now;
            op.FechaVistoFinanzas = ahora;
            op.UsuarioVistoFinanzas = u;
            _context.OperacionesTraspasoFinanzas.Add(new OperacionTraspasoFinanzas
            {
                IdOperacion = op.Id,
                Evento = OperacionTraspasoFinanzas.EventoVisto,
                Usuario = u,
                Fecha = ahora
            });
            await _context.SaveChangesAsync();
            return true;
        }

        // Cantidad de bookings que Finanzas debe mirar (NUEVO + reabiertos sin ver).
        // Si las columnas aún no existen (script sin correr), devuelve 0 en vez de romper el menú.
        public async Task<int> ContarPendientesAsync()
        {
            try
            {
                return await _context.Operaciones.AsNoTracking()
                    .Where(o => !o.IsDeleted && (
                        (o.EstadoTarea == TraspasoFinanzas.EstadoFinalizada && o.FechaFinalizacionTarea != null
                            && (o.FechaVistoFinanzas == null || o.FechaVistoFinanzas < o.FechaFinalizacionTarea))
                        || (o.EstadoTarea == TraspasoFinanzas.EstadoReabierta && o.FechaReaperturaTarea != null
                            && (o.FechaVistoFinanzas == null || o.FechaVistoFinanzas < o.FechaReaperturaTarea))))
                    .CountAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"Traspaso Ops->Finanzas: no se pudo contar (¿falta correr el script?): {ex.Message}");
                return 0;
            }
        }

        // Cambios entre la finalización anterior y la última, por operación (solo las
        // finalizadas más de una vez). Clave = IdOperacion.
        public async Task<Dictionary<int, List<string>>> CambiosUltimaFinalizacionAsync(IEnumerable<int> idsOperacion)
        {
            var ids = idsOperacion.Distinct().ToList();
            var res = new Dictionary<int, List<string>>();
            if (!ids.Any()) return res;
            List<OperacionTraspasoFinanzas> eventos;
            try
            {
                eventos = await _context.OperacionesTraspasoFinanzas.AsNoTracking()
                    .Where(e => ids.Contains(e.IdOperacion) && e.Evento == OperacionTraspasoFinanzas.EventoFinalizada)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"Traspaso Ops->Finanzas: no se pudo leer el historial: {ex.Message}");
                return res;
            }
            foreach (var g in eventos.GroupBy(e => e.IdOperacion))
            {
                var orden = g.OrderByDescending(e => e.Fecha).ThenByDescending(e => e.Id).Take(2).ToList();
                if (orden.Count < 2) continue;
                res[g.Key] = TraspasoFinanzas.Diferencias(orden[1].Resumen, orden[0].Resumen);
            }
            return res;
        }

        /// <summary>
        /// Respaldo de la última reapertura de cada operación (si la reapertura trajo archivo).
        /// Clave = IdOperacion. Si falta el script SQL devuelve vacío sin romper la pantalla.
        /// </summary>
        public async Task<Dictionary<int, (string Ruta, string Nombre)>> RespaldosUltimaReaperturaAsync(IEnumerable<int> idsOperacion)
        {
            var ids = idsOperacion.Distinct().ToList();
            var res = new Dictionary<int, (string Ruta, string Nombre)>();
            if (!ids.Any()) return res;
            try
            {
                var eventos = await _context.OperacionesTraspasoFinanzas.AsNoTracking()
                    .Where(e => ids.Contains(e.IdOperacion) && e.Evento == OperacionTraspasoFinanzas.EventoReabierta)
                    .Select(e => new { e.Id, e.IdOperacion, e.Fecha, e.RutaRespaldo, e.NombreRespaldo })
                    .ToListAsync();
                foreach (var g in eventos.GroupBy(e => e.IdOperacion))
                {
                    var ultima = g.OrderByDescending(e => e.Fecha).ThenByDescending(e => e.Id).First();
                    if (!string.IsNullOrWhiteSpace(ultima.RutaRespaldo))
                        res[g.Key] = (ultima.RutaRespaldo!, string.IsNullOrWhiteSpace(ultima.NombreRespaldo) ? "Respaldo" : ultima.NombreRespaldo!);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"Traspaso Ops->Finanzas: no se pudieron leer los respaldos (¿falta correr el script?): {ex.Message}");
            }
            return res;
        }

        public async Task<List<OperacionTraspasoFinanzas>> HistorialAsync(int idOperacion)
        {
            try
            {
                return await _context.OperacionesTraspasoFinanzas.AsNoTracking()
                    .Where(e => e.IdOperacion == idOperacion)
                    .OrderByDescending(e => e.Fecha).ThenByDescending(e => e.Id)
                    .ToListAsync();
            }
            catch
            {
                return new List<OperacionTraspasoFinanzas>();
            }
        }
    }
}
