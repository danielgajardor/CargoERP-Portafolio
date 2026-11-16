using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Services
{
    // Guardado de una salida del maestro de itinerarios (nov-2026). Lo usan el formulario de
    // Itinerarios y la ventana "Agregar salida" del buscador de Create: mismas validaciones.
    public static class ItinerarioServicio
    {
        public class Datos
        {
            public int? Id { get; set; }
            public int? IdNaviera { get; set; }
            public string? Nave { get; set; }
            public string? NumeroViaje { get; set; }
            public int? IdPuertoOrigen { get; set; }
            public int? IdPuertoDestino { get; set; }
            public string? Etd { get; set; }              // yyyy-MM-ddTHH:mm (o yyyy-MM-dd)
            public string? Eta { get; set; }
            public string? CorteDocumental { get; set; }  // yyyy-MM-ddTHH:mm
            // Stacking (13-nov-2026), opcional. null = no viene en este guardado (no se toca).
            public string? StackingInicioDry { get; set; }
            public string? StackingFinDry { get; set; }
            public string? StackingInicioReefer { get; set; }
            public string? StackingFinReefer { get; set; }
            public bool TraeStacking { get; set; }
            public string? Transbordo { get; set; }
            public string? Observaciones { get; set; }
        }

        public class Resultado
        {
            public bool Ok { get; init; }
            // Código del aviso de Itinerarios (err_naviera, err_duplicada, creada, actualizada...).
            public string Codigo { get; init; } = "";
            public ItinerarioNave? Salida { get; init; }
            public int BookingsCorregidos { get; init; }
            public bool NaveCreada { get; init; }
            public int BookingsConFechas { get; init; }
        }

        public static async Task<Resultado> GuardarAsync(AtilsonContext db, Datos r, string? usuario)
        {
            Resultado Error(string codigo) => new() { Ok = false, Codigo = codigo };

            if (r.IdNaviera == null || !await db.Navieras.AnyAsync(n => n.Id == r.IdNaviera))
                return Error("err_naviera");
            if (string.IsNullOrWhiteSpace(r.Nave))
                return Error("err_nave");
            if (string.IsNullOrWhiteSpace(r.NumeroViaje))
                return Error("err_viaje");
            if (r.IdPuertoOrigen == null || r.IdPuertoDestino == null || r.IdPuertoOrigen == r.IdPuertoDestino
                || !await db.Puertos.AnyAsync(p => p.Id == r.IdPuertoOrigen)
                || !await db.Puertos.AnyAsync(p => p.Id == r.IdPuertoDestino))
                return Error("err_puertos");
            // Origen y destino no pueden ser del mismo país (p. ej. Valparaíso → San Antonio).
            if (await PuertosHelper.MismoPaisAsync(db, r.IdPuertoOrigen, r.IdPuertoDestino) != null)
                return Error("err_mismo_pais");

            var (etd, horaEtd) = LeerFechaYHora(r.Etd);
            var (eta, horaEta) = LeerFechaYHora(r.Eta);
            if (etd == null || eta == null || eta < etd
                || (eta == etd && horaEtd != null && horaEta != null && horaEta < horaEtd))
                return Error("err_fechas");

            string naveNorm = NavesCatalogoHelper.Normalizar(r.Nave);
            string viajeNorm = r.NumeroViaje.Trim().ToUpper();
            // Nave + viaje es único por servicio (naviera y ruta).
            bool duplicada = await db.ItinerariosNaves.AnyAsync(i => i.Activo && i.Id != (r.Id ?? 0)
                && i.IdNaviera == r.IdNaviera && i.IdPuertoOrigen == r.IdPuertoOrigen && i.IdPuertoDestino == r.IdPuertoDestino
                && i.Nave == naveNorm && i.NumeroViaje == viajeNorm);
            if (duplicada)
                return Error("err_duplicada");

            ItinerarioNave it;
            bool nueva = r.Id == null;
            if (nueva)
            {
                it = new ItinerarioNave { FechaCreacion = DateTime.Now, UsuarioCreador = usuario, Activo = true };
                db.ItinerariosNaves.Add(it);
            }
            else
            {
                var existente = await db.ItinerariosNaves.FirstOrDefaultAsync(i => i.Id == r.Id && i.Activo);
                if (existente == null)
                    return Error("err_noexiste");
                it = existente;
                it.FechaModificacion = DateTime.Now;
                it.UsuarioModificador = usuario;
            }

            string? naveViajeAnterior = nueva ? null : it.NaveViaje;
            // Auditoría (13-nov-2026): cada alta o cambio queda con usuario, fecha y hora en el
            // historial de maestros (HistorialCambiosComercial, "Maestro:Itinerario"), que no se edita.
            var antes = nueva ? null : new
            {
                it.IdNaviera, Nave = it.NaveViaje, it.IdPuertoOrigen, it.IdPuertoDestino,
                Etd = FechaHoraTexto(it.Etd, it.HoraEtd), Eta = FechaHoraTexto(it.Eta, it.HoraEta),
                Corte = it.CorteDocumental?.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture),
                it.Transbordo, it.Observaciones
            };
            var fechasAntes = nueva ? FechasNave.FotoSalida.Vacia : FechasNave.De(it);
            it.IdNaviera = r.IdNaviera.Value;
            it.Nave = naveNorm;
            it.NumeroViaje = viajeNorm;
            it.IdPuertoOrigen = r.IdPuertoOrigen.Value;
            it.IdPuertoDestino = r.IdPuertoDestino.Value;
            it.Etd = etd.Value;
            it.Eta = eta.Value;
            it.HoraEtd = horaEtd;
            it.HoraEta = horaEta;
            it.CorteDocumental = LeerFechaHora(r.CorteDocumental);
            if (r.TraeStacking)
            {
                it.StackingInicioDry = LeerFechaHora(r.StackingInicioDry);
                it.StackingFinDry = LeerFechaHora(r.StackingFinDry);
                it.StackingInicioReefer = LeerFechaHora(r.StackingInicioReefer);
                it.StackingFinReefer = LeerFechaHora(r.StackingFinReefer);
            }
            it.Transbordo = string.IsNullOrWhiteSpace(r.Transbordo) ? null : r.Transbordo.Trim().ToUpper();
            it.Observaciones = string.IsNullOrWhiteSpace(r.Observaciones) ? null : r.Observaciones.Trim();

            // La nave queda en el catálogo (si alguien la escribió a mano o vino de otra pantalla).
            var catalogo = await NavesCatalogoHelper.ActivasAsync(db);
            var (_, naveCreada) = NavesCatalogoHelper.Asegurar(db, catalogo, naveNorm, it.IdNaviera, usuario);

            // Corrección de la nave o el viaje de una salida ya elegida en Operaciones: los bookings
            // vinculados que todavía tienen el texto anterior pasan al nuevo (si no, al guardar
            // Marítimo el texto ya no coincidiría con la salida y se perdería el vínculo).
            int corregidos = 0;
            if (naveViajeAnterior != null && !string.Equals(naveViajeAnterior, it.NaveViaje, StringComparison.Ordinal))
            {
                var vinculados = await db.Operaciones
                    .Where(o => o.IdItinerario == it.Id && !o.IsDeleted && o.Nave == naveViajeAnterior)
                    .ToListAsync();
                foreach (var o in vinculados)
                {
                    o.Nave = it.NaveViaje;
                    o.FechaModificacion = DateTime.Now;
                    o.UsuarioModificador = usuario;
                }
                corregidos = vinculados.Count;
            }

            await db.SaveChangesAsync();

            string quien = string.IsNullOrWhiteSpace(usuario) ? "Sistema" : usuario;
            if (antes == null)
            {
                MaestrosAuditoria.RegistrarEvento(db, MaestrosAuditoria.EntItinerario, it.Id, "Alta", quien,
                    $"{it.NaveViaje} · ETD {FechaHoraTexto(it.Etd, it.HoraEtd)} · ETA {FechaHoraTexto(it.Eta, it.HoraEta)}");
            }
            else
            {
                var cambios = new MaestrosAuditoria.Cambios();
                if (antes.IdNaviera != it.IdNaviera)
                    cambios.Agregar("Naviera", await NombreNavieraAsync(db, antes.IdNaviera), await NombreNavieraAsync(db, it.IdNaviera));
                cambios.Comparar("Nave / viaje", antes.Nave, it.NaveViaje);
                if (antes.IdPuertoOrigen != it.IdPuertoOrigen)
                    cambios.Agregar("POL", await NombrePuertoAsync(db, antes.IdPuertoOrigen), await NombrePuertoAsync(db, it.IdPuertoOrigen));
                if (antes.IdPuertoDestino != it.IdPuertoDestino)
                    cambios.Agregar("POD", await NombrePuertoAsync(db, antes.IdPuertoDestino), await NombrePuertoAsync(db, it.IdPuertoDestino));
                cambios.Comparar("ETD", antes.Etd, FechaHoraTexto(it.Etd, it.HoraEtd));
                cambios.Comparar("ETA", antes.Eta, FechaHoraTexto(it.Eta, it.HoraEta));
                cambios.Comparar("Corte documental", antes.Corte, it.CorteDocumental?.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture));
                string? Tx(DateTime? d) => d?.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
                cambios.Comparar("Stacking Dry apertura", Tx(fechasAntes.IniDry), Tx(it.StackingInicioDry));
                cambios.Comparar("Stacking Dry cierre", Tx(fechasAntes.FinDry), Tx(it.StackingFinDry));
                cambios.Comparar("Stacking Reefer apertura", Tx(fechasAntes.IniRf), Tx(it.StackingInicioReefer));
                cambios.Comparar("Stacking Reefer cierre", Tx(fechasAntes.FinRf), Tx(it.StackingFinReefer));
                cambios.Comparar("Transbordo", antes.Transbordo, it.Transbordo);
                cambios.Comparar("Observaciones", antes.Observaciones, it.Observaciones);
                if (cambios.Hay)
                    MaestrosAuditoria.Registrar(db, MaestrosAuditoria.EntItinerario, it.Id, cambios, quien, "Edición de la salida");
            }
            await db.SaveChangesAsync();

            // Fechas de la salida (ETD, ETA, corte, stacking) → todos sus bookings marítimos.
            int conFechas = (await FechasNave.DesdeSalidaAsync(db, it, fechasAntes, quien)).Otros;

            return new Resultado { Ok = true, Codigo = nueva ? "creada" : "actualizada", Salida = it, BookingsCorregidos = corregidos, NaveCreada = naveCreada, BookingsConFechas = conFechas };
        }

        public static string FechaHoraTexto(DateOnly fecha, TimeOnly? hora) =>
            fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) + (hora != null ? " " + hora.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "");

        private static Task<string?> NombreNavieraAsync(AtilsonContext db, int id) =>
            db.Navieras.Where(n => n.Id == id).Select(n => (string?)n.NombreNaviera).FirstOrDefaultAsync();

        private static Task<string?> NombrePuertoAsync(AtilsonContext db, int id) =>
            db.Puertos.Where(p => p.Id == id).Select(p => (string?)p.NombrePuerto).FirstOrDefaultAsync();

        public static DateOnly? LeerFecha(string? s) =>
            DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        // ETD / ETA llegan de un datetime-local ("yyyy-MM-ddTHH:mm"); se acepta también solo fecha.
        public static (DateOnly? Fecha, TimeOnly? Hora) LeerFechaYHora(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return (null, null);
            var dt = LeerFechaHora(s);
            if (dt != null) return (DateOnly.FromDateTime(dt.Value), TimeOnly.FromDateTime(dt.Value));
            return (LeerFecha(s), null);
        }

        public static DateTime? LeerFechaHora(string? s) =>
            DateTime.TryParseExact(s, new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
