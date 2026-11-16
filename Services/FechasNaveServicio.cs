using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Services
{
    // =====================================================================================
    // Fechas de la nave compartidas por todos sus bookings (13 y 15-nov-2026).
    // Los bookings de una misma salida (nave + viaje del maestro de Itinerarios) comparten:
    //   ETD (EtdPol), ETA (EtaPod), corte documental (CutOffMatriz) y el stacking:
    //   apertura = FechaStacking y cierre = LateArrival, distinto para Dry y Reefer.
    // Regla: el último cambio manda, venga de donde venga.
    //   - Salida → bookings: lo que cambia en Itinerarios se copia a todos sus bookings.
    //   - Booking → salida → demás bookings: lo que se cambia en una operación (Create,
    //     Mainboard, Detalle, Marítimo, Editar, roleo) queda en la salida y en todas las
    //     operaciones de esa nave y viaje.
    // Excepciones: un Late Arrival en trámite (LAR / ELAR / V°B°) es un plazo propio del booking
    // y no se pisa ni se sube; la ETA de un booking con nueva nave de transbordo confirmada
    // (ETA a destino final propia) tampoco se pisa desde la salida.
    // Las fechas sin hora (00:00) se comparan por día: "03/12" y "03/12 10:00" son lo mismo.
    // =====================================================================================
    public static class FechasNave
    {
        public const string Etd = "ETD", Eta = "ETA", Corte = "Corte documental",
                            StkIni = "Stacking apertura", StkFin = "Stacking cierre";

        public static bool EsReefer(Operacione op)
        {
            string tc = (op.TipoContenedor ?? "").ToUpperInvariant();
            return op.IdTipoCarga == 2 || tc.Contains("RF") || tc.Contains("REEFER");
        }

        // Un Late Arrival en trámite (Solicitado / Autorizado LAR o ELAR, Ingresado con LAR, V°B°) es un
        // plazo propio del booking. "No Solicitado" (lo que deja Create) no cuenta.
        public static bool LarEnTramite(Operacione op)
        {
            string e = (op.EstadoLar ?? "").Trim().ToUpperInvariant();
            return e.Length > 0 && !e.StartsWith("NO", StringComparison.Ordinal);
        }

        /// <summary>Iguales al minuto; si una de las dos no tiene hora (00:00), basta el mismo día.</summary>
        public static bool Igual(DateTime? a, DateTime? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Value.TimeOfDay == TimeSpan.Zero || b.Value.TimeOfDay == TimeSpan.Zero) return a.Value.Date == b.Value.Date;
            return Math.Abs((a.Value - b.Value).TotalMinutes) < 1;
        }

        /// <summary>
        /// Los campos ETD / ETA de las pantallas son de fecha (sin hora). Al guardar el mismo día que ya
        /// tenía, se conserva la hora guardada (la de la salida), en vez de dejarla en 00:00.
        /// </summary>
        public static DateTime? ConservarHora(DateTime? nuevo, DateTime? actual) =>
            nuevo != null && actual != null && nuevo.Value.TimeOfDay == TimeSpan.Zero && nuevo.Value.Date == actual.Value.Date ? actual : nuevo;

        public static string Texto(DateTime? d) =>
            d == null ? "—" : d.Value.ToString(d.Value.TimeOfDay == TimeSpan.Zero ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        private static string? TextoAud(DateTime? d) => d?.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);

        // -------------------------------------------------------------------------------
        // Fotos (antes / después)
        // -------------------------------------------------------------------------------
        /// <summary>Fechas de nave de un booking antes de editarlo.</summary>
        public sealed record Foto(int? IdItinerario, string? Nave, int IdNaviera, DateTime? Etd, DateTime? Eta,
                                  DateTime? StkIni, DateTime? StkFin, DateTime? Corte);

        public static Foto De(Operacione op) =>
            new(op.IdItinerario, op.Nave?.Trim(), op.IdNaviera, op.EtdPol, op.EtaPod, op.FechaStacking, op.LateArrival, op.CutOffMatriz);

        /// <summary>Fechas de una salida antes de editarla.</summary>
        public sealed record FotoSalida(DateTime? Etd, DateTime? Eta, DateTime? IniDry, DateTime? FinDry,
                                        DateTime? IniRf, DateTime? FinRf, DateTime? Corte)
        {
            public static readonly FotoSalida Vacia = new(null, null, null, null, null, null, null);
        }

        public static DateTime EtdDe(ItinerarioNave it) => it.Etd.ToDateTime(it.HoraEtd ?? TimeOnly.MinValue);
        public static DateTime EtaDe(ItinerarioNave it) => it.Eta.ToDateTime(it.HoraEta ?? TimeOnly.MinValue);

        public static FotoSalida De(ItinerarioNave it) =>
            new(EtdDe(it), EtaDe(it), it.StackingInicioDry, it.StackingFinDry, it.StackingInicioReefer, it.StackingFinReefer, it.CorteDocumental);

        /// <summary>Para la tabla de Itinerarios: ("Dry", "27/11→30/11"), ("RF", "28/11→29/11") (? = sin dato).</summary>
        public static List<(string Tipo, string Rango)> Resumen(DateTime? iniDry, DateTime? finDry, DateTime? iniRf, DateTime? finRf)
        {
            static string D(DateTime? d) => d?.ToString("dd'/'MM", CultureInfo.InvariantCulture) ?? "?";
            var lineas = new List<(string, string)>();
            if (iniDry != null || finDry != null) lineas.Add(("Dry", $"{D(iniDry)}→{D(finDry)}"));
            if (iniRf != null || finRf != null) lineas.Add(("RF", $"{D(iniRf)}→{D(finRf)}"));
            return lineas;
        }

        // -------------------------------------------------------------------------------
        // Resultado
        // -------------------------------------------------------------------------------
        public sealed class Resultado
        {
            /// <summary>Bookings (distintos del que originó el cambio) que recibieron fechas nuevas.</summary>
            public List<string> Bookings { get; } = new();
            /// <summary>Campos de la salida que cambiaron desde el booking.</summary>
            public List<string> CamposSalida { get; } = new();
            public int Otros => Bookings.Count;

            public string Mensaje() =>
                Otros == 0 && CamposSalida.Count == 0 ? ""
                : (CamposSalida.Count > 0 ? $" {string.Join(", ", CamposSalida)} quedó en la salida de la nave" : " Fechas de la nave")
                  + (Otros > 0 ? $" y se actualizó en {Otros} booking(s) más ({string.Join(", ", Bookings.Take(6))}{(Otros > 6 ? "…" : "")})." : ".");
        }

        // Cambios a aplicar a los bookings: qué campo, valor nuevo y (si se limpió) el valor anterior.
        private sealed class Cambio
        {
            public DateTime? Etd, Eta, Corte, IniDry, FinDry, IniRf, FinRf;
            public bool HayEtd, HayEta, HayCorte, HayIniDry, HayFinDry, HayIniRf, HayFinRf;
            public FotoSalida Antes = FotoSalida.Vacia;
            public bool Algo => HayEtd || HayEta || HayCorte || HayIniDry || HayFinDry || HayIniRf || HayFinRf;
        }

        /// <summary>Aplica los cambios de la salida a un booking. Devuelve la descripción de lo que cambió.</summary>
        private static List<string> AplicarABooking(Operacione o, Cambio c, bool etaPropia)
        {
            var hecho = new List<string>();
            void Poner(string campo, DateTime? actual, DateTime? nuevo, DateTime? anterior, Action<DateTime?> set)
            {
                // Valor nuevo: se copia a todos. Valor borrado en la salida: se borra solo donde era el mismo.
                if (nuevo != null ? !Igual(actual, nuevo) : (actual != null && Igual(actual, anterior)))
                {
                    hecho.Add($"{campo}: {Texto(actual)} → {Texto(nuevo)}");
                    set(nuevo);
                }
            }
            bool rf = EsReefer(o);
            if (c.HayEtd && c.Etd != null) Poner(Etd, o.EtdPol, c.Etd, c.Antes.Etd, v => o.EtdPol = v);
            if (c.HayEta && c.Eta != null && !etaPropia) Poner(Eta, o.EtaPod, c.Eta, c.Antes.Eta, v => o.EtaPod = v);
            if (c.HayCorte) Poner(Corte, o.CutOffMatriz, c.Corte, c.Antes.Corte, v => o.CutOffMatriz = v);
            if (rf ? c.HayIniRf : c.HayIniDry)
                Poner(StkIni, o.FechaStacking, rf ? c.IniRf : c.IniDry, rf ? c.Antes.IniRf : c.Antes.IniDry, v => o.FechaStacking = v);
            if ((rf ? c.HayFinRf : c.HayFinDry) && !LarEnTramite(o))
                Poner(StkFin, o.LateArrival, rf ? c.FinRf : c.FinDry, rf ? c.Antes.FinRf : c.Antes.FinDry, v => o.LateArrival = v);
            return hecho;
        }

        // Bookings con nueva nave de transbordo confirmada: su ETA es la del destino final, propia.
        private static async Task<HashSet<int>> EtaPropiaAsync(AtilsonContext db, List<Operacione> ops)
        {
            var ids = ops.Select(o => o.Id).ToList();
            if (ids.Count == 0) return new HashSet<int>();
            var lista = await db.GestionesTransbordo.AsNoTracking()
                .Where(g => ids.Contains(g.IdOperacion) && g.NuevoEtaDestinoFinal != null)
                .Select(g => g.IdOperacion).ToListAsync();
            return lista.ToHashSet();
        }

        private static async Task<List<string>> CopiarATodosAsync(AtilsonContext db, ItinerarioNave it, Cambio c, int? excluir, string usuario, string origen)
        {
            var bookings = new List<string>();
            if (!c.Algo) return bookings;
            var ops = await CicloNave.BookingsDeSalidaAsync(db, it);
            var etaPropia = await EtaPropiaAsync(db, ops);
            foreach (var o in ops)
            {
                if (excluir != null && o.Id == excluir) continue;
                var hecho = AplicarABooking(o, c, etaPropia.Contains(o.Id));
                if (hecho.Count == 0) continue;
                o.FechaModificacion = DateTime.Now;
                o.UsuarioModificador = usuario;
                o.CorreoClienteEnviado = false;
                CicloNave.RegistrarHito(o, usuario, "MARITIMO", $"Fechas de la nave {it.NaveViaje} ({origen}) — {string.Join(" | ", hecho)}");
                bookings.Add(o.NumeroBooking ?? $"#{o.Id}");
            }
            return bookings;
        }

        // -------------------------------------------------------------------------------
        // Salida → bookings
        // -------------------------------------------------------------------------------
        /// <summary>
        /// Después de guardar la salida en Itinerarios: lo que cambió se copia a todos sus bookings
        /// marítimos (según la carga de cada uno). Una salida nueva entrega todas sus fechas.
        /// </summary>
        public static async Task<Resultado> DesdeSalidaAsync(AtilsonContext db, ItinerarioNave it, FotoSalida antes, string usuario)
        {
            var r = new Resultado();
            var ahora = De(it);
            var c = new Cambio
            {
                Antes = antes,
                Etd = ahora.Etd, HayEtd = !Igual(antes.Etd, ahora.Etd),
                Eta = ahora.Eta, HayEta = !Igual(antes.Eta, ahora.Eta),
                Corte = ahora.Corte, HayCorte = !Igual(antes.Corte, ahora.Corte),
                IniDry = ahora.IniDry, HayIniDry = !Igual(antes.IniDry, ahora.IniDry),
                FinDry = ahora.FinDry, HayFinDry = !Igual(antes.FinDry, ahora.FinDry),
                IniRf = ahora.IniRf, HayIniRf = !Igual(antes.IniRf, ahora.IniRf),
                FinRf = ahora.FinRf, HayFinRf = !Igual(antes.FinRf, ahora.FinRf),
            };
            r.Bookings.AddRange(await CopiarATodosAsync(db, it, c, null, usuario, "Itinerarios"));
            if (r.Otros > 0) await db.SaveChangesAsync();
            return r;
        }

        // -------------------------------------------------------------------------------
        // Booking → salida → demás bookings
        // -------------------------------------------------------------------------------
        /// <summary>
        /// Después de guardar un booking. <paramref name="antes"/> es la foto previa a la edición
        /// (null al crear). Lo que el usuario cambió en este booking queda en la salida y en los
        /// demás bookings de la nave. Si el booking pasó a otra nave en esta edición (roleo o
        /// corrección), las fechas que no tocó se toman de su nueva salida.
        /// </summary>
        public static async Task<Resultado> DesdeBookingAsync(AtilsonContext db, Operacione op, Foto? antes, string usuario, string origen)
        {
            var r = new Resultado();
            if (!CicloNave.TieneMaritimo(op) || op.IsDeleted) return r;
            if ((op.EstadoWorkflow ?? "").Trim().StartsWith("CANCELADO", StringComparison.OrdinalIgnoreCase)) return r;
            var it = await CicloNave.SalidaDeAsync(db, op);
            if (it == null) return r;

            bool mismaNave = antes != null
                && antes.IdNaviera == op.IdNaviera
                && string.Equals(antes.Nave ?? "", (op.Nave ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                && (antes.IdItinerario == op.IdItinerario || antes.IdItinerario == null || op.IdItinerario == null);

            // ¿Qué tocó el usuario en esta edición? (al crear: todo lo que trae el booking)
            bool Toco(DateTime? valor, DateTime? previo) => valor != null && (antes == null || !Igual(valor, previo));
            bool rf = EsReefer(op);
            bool tEtd = Toco(op.EtdPol, antes?.Etd), tEta = Toco(op.EtaPod, antes?.Eta), tCorte = Toco(op.CutOffMatriz, antes?.Corte);
            bool tIni = Toco(op.FechaStacking, antes?.StkIni), tFin = Toco(op.LateArrival, antes?.StkFin) && !LarEnTramite(op);

            var antesSalida = De(it);
            // Mismo día que la salida pero sin hora (campo de fecha): el booking toma la hora de la salida.
            if (op.EtdPol?.TimeOfDay == TimeSpan.Zero && Igual(op.EtdPol, antesSalida.Etd)) op.EtdPol = antesSalida.Etd;
            if (op.EtaPod?.TimeOfDay == TimeSpan.Zero && Igual(op.EtaPod, antesSalida.Eta)) op.EtaPod = antesSalida.Eta;
            var aud = new MaestrosAuditoria.Cambios();
            var c = new Cambio { Antes = antesSalida };

            // 1) Booking → salida: lo que el usuario cambió y difiere de la salida.
            if (tEtd && !Igual(op.EtdPol, antesSalida.Etd) && (op.EtaPod == null || op.EtdPol <= (tEta ? op.EtaPod : antesSalida.Eta)))
            {
                it.Etd = DateOnly.FromDateTime(op.EtdPol!.Value);
                // Booking sin hora: se cambia el día y se conserva la hora que tenía la salida.
                if (op.EtdPol.Value.TimeOfDay != TimeSpan.Zero) it.HoraEtd = TimeOnly.FromDateTime(op.EtdPol.Value);
                aud.Comparar("ETD", TextoAud(antesSalida.Etd), TextoAud(EtdDe(it)));
                c.HayEtd = true; c.Etd = EtdDe(it); r.CamposSalida.Add("ETD");
                if (op.EtdPol.Value.TimeOfDay == TimeSpan.Zero) op.EtdPol = c.Etd;
            }
            if (tEta && !Igual(op.EtaPod, antesSalida.Eta) && op.EtaPod >= EtdDe(it))
            {
                it.Eta = DateOnly.FromDateTime(op.EtaPod!.Value);
                if (op.EtaPod.Value.TimeOfDay != TimeSpan.Zero) it.HoraEta = TimeOnly.FromDateTime(op.EtaPod.Value);
                aud.Comparar("ETA", TextoAud(antesSalida.Eta), TextoAud(EtaDe(it)));
                c.HayEta = true; c.Eta = EtaDe(it); r.CamposSalida.Add("ETA");
                if (op.EtaPod.Value.TimeOfDay == TimeSpan.Zero) op.EtaPod = c.Eta;
            }
            if (tCorte && !Igual(op.CutOffMatriz, antesSalida.Corte))
            {
                it.CorteDocumental = op.CutOffMatriz;
                aud.Comparar("Corte documental", TextoAud(antesSalida.Corte), TextoAud(op.CutOffMatriz));
                c.HayCorte = true; c.Corte = op.CutOffMatriz; r.CamposSalida.Add("Corte documental");
            }
            if (tIni && !Igual(op.FechaStacking, rf ? antesSalida.IniRf : antesSalida.IniDry))
            {
                if (rf) { it.StackingInicioReefer = op.FechaStacking; c.HayIniRf = true; c.IniRf = op.FechaStacking; }
                else { it.StackingInicioDry = op.FechaStacking; c.HayIniDry = true; c.IniDry = op.FechaStacking; }
                aud.Comparar($"Stacking {(rf ? "Reefer" : "Dry")} apertura", TextoAud(rf ? antesSalida.IniRf : antesSalida.IniDry), TextoAud(op.FechaStacking));
                r.CamposSalida.Add($"Stacking {(rf ? "Reefer" : "Dry")} apertura");
            }
            if (tFin && !Igual(op.LateArrival, rf ? antesSalida.FinRf : antesSalida.FinDry))
            {
                if (rf) { it.StackingFinReefer = op.LateArrival; c.HayFinRf = true; c.FinRf = op.LateArrival; }
                else { it.StackingFinDry = op.LateArrival; c.HayFinDry = true; c.FinDry = op.LateArrival; }
                aud.Comparar($"Stacking {(rf ? "Reefer" : "Dry")} cierre", TextoAud(rf ? antesSalida.FinRf : antesSalida.FinDry), TextoAud(op.LateArrival));
                r.CamposSalida.Add($"Stacking {(rf ? "Reefer" : "Dry")} cierre");
            }

            // 2) El booking cambió de nave en esta edición: lo que no tocó viene de la nueva salida, y
            //    lo que traía de la nave anterior (y la nueva no tiene) se quita: era de la otra nave.
            if (antes != null && !mismaNave)
            {
                FotoSalida anterior = FotoSalida.Vacia;
                int? idAnterior = await AtilsonCargoSpa.Helpers.ItinerariosHelper.ResolverSalidaAsync(db, antes.IdItinerario, antes.Nave, antes.IdNaviera > 0 ? antes.IdNaviera : null);
                if (idAnterior != null && idAnterior != it.Id)
                {
                    var vieja = await db.ItinerariosNaves.AsNoTracking().FirstOrDefaultAsync(i => i.Id == idAnterior);
                    if (vieja != null) anterior = De(vieja);
                }
                var propia = new Cambio
                {
                    Antes = anterior,
                    // Sin dato en la nueva salida: se borra solo si el booking tenía el de la nave anterior.
                    HayEtd = !tEtd, Etd = antesSalida.Etd, HayEta = !tEta, Eta = antesSalida.Eta,
                    HayCorte = !tCorte, Corte = antesSalida.Corte,
                    HayIniDry = !tIni, IniDry = antesSalida.IniDry,
                    HayIniRf = !tIni, IniRf = antesSalida.IniRf,
                    HayFinDry = !tFin, FinDry = antesSalida.FinDry,
                    HayFinRf = !tFin, FinRf = antesSalida.FinRf,
                };
                bool etaPropia = (await EtaPropiaAsync(db, new List<Operacione> { op })).Count > 0;
                var hecho = AplicarABooking(op, propia, etaPropia);
                if (hecho.Count > 0) CicloNave.RegistrarHito(op, usuario, "MARITIMO", $"Fechas tomadas de la salida {it.NaveViaje} — {string.Join(" | ", hecho)}");
            }

            if (aud.Hay)
            {
                it.FechaModificacion = DateTime.Now;
                it.UsuarioModificador = usuario;
                MaestrosAuditoria.Registrar(db, MaestrosAuditoria.EntItinerario, it.Id, aud, usuario, $"Desde el booking {op.NumeroBooking} ({origen})");
            }

            // 3) Salida → demás bookings de la nave.
            r.Bookings.AddRange(await CopiarATodosAsync(db, it, c, op.Id, usuario, $"desde el booking {op.NumeroBooking}"));
            await db.SaveChangesAsync();
            return r;
        }
    }
}
