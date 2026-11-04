using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;

namespace AtilsonCargoSpa.Helpers
{
    // Cruce de la tarifa de COSTO del flete terrestre (oct-2026).
    // Una tarifa de Comercial (Tarifasterrestres) es: transportista + ciudad de retiro +
    // ciudad de la planta + ciudad del puerto de entrega + tipo de carga (DRY/REEFER).
    // La operación trae nombres (depósito, planta, puerto); aquí se llevan a ciudades
    // con los maestros y se busca el tramo. Si no hay tarifa, Diagnostico explica por qué
    // (lo muestra Control Financiero en "¿Por qué no hay tarifa?").
    //
    // Reglas (oct-2026, revisadas a pedido: "tiene que ser exacto, sin inventar valores"):
    //   - Los TRES puntos tienen que calzar exactamente con la tarifa: ciudad del depósito
    //     (maestro de Depósitos), ciudad de la planta (maestro de Plantas o Zona de carga) y
    //     ciudad del puerto (maestro de Puertos o el nombre del puerto). Sin "contiene", sin
    //     deducir la ciudad del texto y sin elegir un tramo "parecido" cuando falta un dato:
    //     si no hay tarifa exacta, el flete queda Por asignar.
    //   - El transportista se busca en todos los registros del maestro que son esa empresa
    //     (mismo nombre, mismo nombre sin "Transportes"/LTDA/SPA, o el RUT de la pestaña):
    //     si el maestro tiene la empresa duplicada, la tarifa puede estar en cualquiera.
    //   - Una tarifa sin tipo de carga aplica a DRY y REEFER.
    public static class TarifaTransporteHelper
    {
        public sealed class Resultado
        {
            public Tarifasterrestre? Tarifa { get; set; }
            public int? IdProveedor { get; set; }
            public string? NombreProveedor { get; set; }
            public List<string> Diagnostico { get; } = new();
            public decimal? Valor => Tarifa?.ValorNeto;
        }

        private static readonly string[] SufijosEmpresa =
            { "LTDA", "LIMITADA", "SPA", "S A", "SA", "EIRL", "E I R L", "CIA", "Y CIA", "SOCIEDAD", "COMERCIAL" };

        public static string Norm(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var formD = texto.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (var c in formD)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            return string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        // "TRANSPORTES TEMPESTI LTDA." → "TEMPESTI" (para comparar transportistas).
        public static string NormEmpresa(string? nombre)
        {
            var n = " " + Norm(nombre) + " ";
            foreach (var suf in SufijosEmpresa.OrderByDescending(s => s.Length))
                n = n.Replace(" " + suf + " ", " ");
            n = n.Replace(" TRANSPORTES ", " ").Replace(" TRANSPORTE ", " ").Replace(" TRANSP ", " ");
            return n.Trim();
        }

        private static string NormRut(string? rut) => (rut ?? "").Replace(".", "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

        // Igualdad estricta de ciudad: mismo Id del maestro, o mismo nombre (sin tildes ni
        // mayúsculas). Sin "contiene" ni adivinar por el texto del depósito o la planta.
        private static bool MismaCiudad(Ciudade? ciudadTarifa, int? idCiudadTarifa, int? idCiudadReal, string nombreCiudadReal)
        {
            if (idCiudadTarifa.HasValue && idCiudadReal.HasValue) return idCiudadTarifa == idCiudadReal;
            string c = Norm(ciudadTarifa?.Nombre);
            return c != "" && nombreCiudadReal != "" && c == nombreCiudadReal;
        }

        /// <summary>
        /// Ciudad del depósito de retiro, de la planta (o su zona de carga) y del puerto de
        /// entrega, desde sus maestros. La usa la venta terrestre al cliente (Comercial guarda
        /// la venta por ciudades, mientras la operación tiene nombres de depósito y planta).
        /// </summary>
        public static async Task<(string Retiro, string Planta, string Puerto)> CiudadesTramoAsync(AtilsonContext context, OperacionesTerrestre? terr, string? retiro, string? planta, string? entrega)
        {
            string retNorm = Norm(retiro), plaNorm = Norm(planta), entNorm = Norm(entrega);
            var depositos = await context.Depositos.AsNoTracking().Include(d => d.IdCiudadNavigation).ToListAsync();
            var deposito = depositos.Where(d => Norm(d.NombreDeposito) == retNorm).OrderByDescending(d => d.IdCiudad.HasValue).FirstOrDefault();
            var plantas = await context.Plantas.AsNoTracking().Include(p => p.Ciudad).ToListAsync();
            var plantaDb = plantas.Where(p => Norm(p.Nombre) == plaNorm).OrderByDescending(p => p.Activo).FirstOrDefault();
            var puertos = await context.Puertos.AsNoTracking().Include(p => p.IdCiudadNavigation).ToListAsync();
            var puertoDb = puertos.Where(p => Norm(p.NombrePuerto) == entNorm).OrderByDescending(p => p.Activo == 1).ThenByDescending(p => p.IdCiudad.HasValue).FirstOrDefault();
            string ciudadPuerto = Norm(puertoDb?.IdCiudadNavigation?.Nombre);
            return (Norm(deposito?.IdCiudadNavigation?.Nombre), Norm(plantaDb?.Ciudad?.Nombre ?? terr?.ZonaCarga), ciudadPuerto == "" ? entNorm : ciudadPuerto);
        }

        public static async Task<Resultado> BuscarAsync(AtilsonContext context, OperacionesTerrestre? terr, int idTipoCarga,
            string? retiro, string? planta, string? entrega, DateTime fecha)
        {
            var r = new Resultado();
            if (terr == null || string.IsNullOrWhiteSpace(terr.EmpresaTransporte))
            {
                r.Diagnostico.Add("El contenedor no tiene empresa de transporte asignada.");
                return r;
            }

            // 1) Transportista: los registros del maestro que son ESA empresa (mismo nombre,
            //    mismo nombre sin "Transportes"/LTDA/SPA, o el RUT de la pestaña Transporte).
            //    Si el maestro tiene la empresa duplicada, la tarifa puede estar en cualquiera.
            string empresaNorm = Norm(terr.EmpresaTransporte);
            string empresaCorta = NormEmpresa(terr.EmpresaTransporte);
            string rut = NormRut(terr.RutTransporte);
            var proveedores = await context.Proveedores.AsNoTracking().Where(p => p.IdTipoProveedor != TransportistaHelper.TipoDeposito).ToListAsync();
            var coincidentes = proveedores.Where(p => Norm(p.NombreProveedor) == empresaNorm
                || (empresaCorta.Length >= 3 && NormEmpresa(p.NombreProveedor) == empresaCorta)
                || (rut != "" && NormRut(p.Rut) == rut)).ToList();
            if (!coincidentes.Any())
            {
                r.Diagnostico.Add($"La empresa \"{terr.EmpresaTransporte}\" no existe en el maestro de proveedores (ni por nombre ni por RUT).");
                return r;
            }
            var principal = coincidentes.OrderByDescending(p => Norm(p.NombreProveedor) == empresaNorm).ThenByDescending(p => p.Activo == 1).ThenByDescending(p => !string.IsNullOrWhiteSpace(p.Rut)).ThenBy(p => p.Id).First();
            r.IdProveedor = principal.Id;
            r.NombreProveedor = principal.NombreProveedor;
            var idsProveedor = coincidentes.Select(p => p.Id).ToList();

            // 2) Ciudad real de cada punto, desde sus maestros.
            string retNorm = Norm(retiro), plaNorm = Norm(planta), entNorm = Norm(entrega);

            var depositos = await context.Depositos.AsNoTracking().Include(d => d.IdCiudadNavigation).ToListAsync();
            var deposito = depositos.Where(d => Norm(d.NombreDeposito) == retNorm).OrderByDescending(d => d.IdCiudad.HasValue).FirstOrDefault();
            var plantas = await context.Plantas.AsNoTracking().Include(p => p.Ciudad).ToListAsync();
            var plantaDb = plantas.Where(p => Norm(p.Nombre) == plaNorm).OrderByDescending(p => p.Activo).FirstOrDefault();
            var puertos = await context.Puertos.AsNoTracking().Include(p => p.IdCiudadNavigation).ToListAsync();
            var puertoDb = puertos.Where(p => Norm(p.NombrePuerto) == entNorm).OrderByDescending(p => p.Activo == 1).ThenByDescending(p => p.IdCiudad.HasValue).FirstOrDefault();

            string ciudadRetiro = Norm(deposito?.IdCiudadNavigation?.Nombre);
            // Planta: la ciudad del maestro; si la planta no está en el maestro, la Zona de carga
            // (es la ciudad de la planta que se autocompleta en la pestaña).
            string ciudadPlanta = Norm(plantaDb?.Ciudad?.Nombre ?? terr.ZonaCarga);
            // Puerto: la ciudad del maestro si la tiene; si no, el nombre del puerto (en Chile
            // los puertos se llaman como su ciudad: VALPARAISO, SAN ANTONIO...).
            string ciudadPuerto = Norm(puertoDb?.IdCiudadNavigation?.Nombre);
            if (ciudadPuerto == "") ciudadPuerto = entNorm;

            var faltanDatos = new List<string>();
            if (string.IsNullOrWhiteSpace(retiro) || retNorm == "RETIRO") faltanDatos.Add("el contenedor no tiene depósito de retiro");
            else if (deposito == null) faltanDatos.Add($"el depósito \"{retiro}\" no está en el maestro de Depósitos");
            else if (!deposito.IdCiudad.HasValue) faltanDatos.Add($"el depósito \"{deposito.NombreDeposito}\" no tiene ciudad en el maestro de Depósitos");
            if (string.IsNullOrWhiteSpace(planta) || plaNorm == "PLANTA") faltanDatos.Add("el contenedor no tiene planta");
            else if (ciudadPlanta == "") faltanDatos.Add($"la planta \"{planta}\" no está en el maestro de Plantas y no tiene zona de carga");
            if (string.IsNullOrWhiteSpace(entrega) || entNorm == "DESTINO") faltanDatos.Add("el contenedor no tiene puerto de entrega");
            if (faltanDatos.Any())
            {
                r.Diagnostico.Add("No se puede buscar la tarifa porque " + string.Join("; ", faltanDatos) + ".");
                return r;
            }

            // 3) Tarifas del transportista para el tipo de carga
            var tarifas = await context.Tarifasterrestres.AsNoTracking()
                .Include(t => t.IdCiudadOrigenNavigation)
                .Include(t => t.IdCiudadPlantaNavigation)
                .Include(t => t.IdCiudadDestinoNavigation)
                .Where(t => t.IdProveedor != null && idsProveedor.Contains(t.IdProveedor.Value) && t.EsActiva)
                .ToListAsync();
            if (!tarifas.Any())
            {
                r.Diagnostico.Add($"{principal.NombreProveedor} no tiene tarifas de transporte activas en Comercial.");
                return r;
            }
            string tipoTxt = idTipoCarga == 2 ? "REEFER" : "DRY";
            var delTipo = tarifas.Where(t => t.IdTipoCarga == null || t.IdTipoCarga == idTipoCarga).ToList();
            if (!delTipo.Any())
            {
                r.Diagnostico.Add($"{principal.NombreProveedor} tiene {tarifas.Count} tarifa(s), pero ninguna para carga {tipoTxt}.");
                return r;
            }

            // Los tres puntos tienen que calzar exactamente: retiro, planta y puerto.
            bool Ret(Tarifasterrestre t) => MismaCiudad(t.IdCiudadOrigenNavigation, t.IdCiudadOrigen, deposito?.IdCiudad, ciudadRetiro);
            bool Pla(Tarifasterrestre t) => MismaCiudad(t.IdCiudadPlantaNavigation, t.IdCiudadPlanta, plantaDb?.IdCiudad, ciudadPlanta);
            bool Pue(Tarifasterrestre t) => MismaCiudad(t.IdCiudadDestinoNavigation, t.IdCiudadDestino, puertoDb?.IdCiudad, ciudadPuerto);

            var hoy = DateOnly.FromDateTime(fecha);
            IEnumerable<Tarifasterrestre> Ordenar(IEnumerable<Tarifasterrestre> l) => l
                .OrderByDescending(t => !t.FechaFinVigencia.HasValue || t.FechaFinVigencia.Value >= hoy)
                .ThenByDescending(t => t.Id);

            var exacta = Ordenar(delTipo.Where(t => Ret(t) && Pla(t) && Pue(t))).FirstOrDefault();

            if (exacta != null)
            {
                r.Tarifa = exacta;
                var provTarifa = coincidentes.FirstOrDefault(p => p.Id == exacta.IdProveedor);
                if (provTarifa != null) { r.IdProveedor = provTarifa.Id; r.NombreProveedor = provTarifa.NombreProveedor; }
                if (exacta.FechaFinVigencia.HasValue && exacta.FechaFinVigencia.Value < hoy)
                    r.Diagnostico.Add($"Se usó una tarifa vencida el {exacta.FechaFinVigencia:dd/MM/yyyy} (no hay una vigente para ese tramo).");
                return r;
            }

            // 4) Sin tarifa: explicar qué falta
            string Ciu(Ciudade? c) => c?.Nombre ?? "?";
            r.Diagnostico.Add($"Tramo de la operación: retiro {retiro ?? "-"} ({(ciudadRetiro == "" ? "depósito sin ciudad en el maestro" : ciudadRetiro)}) → planta {planta ?? "-"} ({(ciudadPlanta == "" ? "planta sin ciudad" : ciudadPlanta)}) → puerto {entrega ?? "-"}{(ciudadPuerto != "" ? $" ({ciudadPuerto})" : "")}, carga {tipoTxt}.");
            r.Diagnostico.Add($"Tarifas {tipoTxt} de {principal.NombreProveedor} en Comercial:");
            foreach (var t in Ordenar(delTipo).Take(8))
            {
                var fallas = new List<string>();
                if (!Ret(t)) fallas.Add("retiro");
                if (!Pla(t)) fallas.Add("planta");
                if (!Pue(t)) fallas.Add("puerto");
                r.Diagnostico.Add($"• {Ciu(t.IdCiudadOrigenNavigation)} → {Ciu(t.IdCiudadPlantaNavigation)} → {Ciu(t.IdCiudadDestinoNavigation)}: no coincide en {string.Join(", ", fallas)}.");
            }
            if (delTipo.Count > 8) r.Diagnostico.Add($"… y {delTipo.Count - 8} más.");
            return r;
        }
    }
}
