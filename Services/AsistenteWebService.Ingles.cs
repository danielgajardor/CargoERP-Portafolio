using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtilsonCargoSpa.Helpers;
using AtilsonCargoSpa.Models;

namespace AtilsonCargoSpa.Services
{
    // Asistente del sitio (nov-2026): conversión de monedas con los valores del Banco Central
    // y la conversación en inglés (sitio en inglés o mensajes escritos en inglés).
    public partial class AsistenteWebService
    {
        private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

        // =====================================================================
        // Idioma del mensaje
        // =====================================================================
        private static readonly HashSet<string> PalabrasIngles = new()
        {
            "the", "what", "when", "where", "which", "who", "how", "is", "are", "do", "does", "can", "could", "would", "you", "your", "my", "i", "we",
            "to", "from", "for", "with", "and", "next", "ship", "vessel", "shipping", "price", "quote", "need", "want", "hello", "hi", "thanks", "thank",
            "please", "much", "long", "take", "there", "have", "any", "about", "tell", "it", "of", "in", "on", "an", "this", "that", "be", "get"
        };
        private static readonly HashSet<string> PalabrasEspanol = new()
        {
            "que", "cuando", "donde", "como", "cual", "quien", "es", "son", "el", "la", "los", "las", "de", "del", "para", "con", "y", "mi", "tu", "un", "una",
            "hola", "gracias", "quiero", "necesito", "cuanto", "hay", "tienen", "puedo", "por", "favor", "sale", "nave", "carga", "precio", "envio"
        };
        private static bool EsIngles(string texto, string n)
        {
            if (Regex.IsMatch(texto, "[áéíóúñ¿¡]", RegexOptions.IgnoreCase)) return false;
            var t = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) return false;
            int en = t.Count(PalabrasIngles.Contains), es = t.Count(PalabrasEspanol.Contains);
            if (t.Length <= 2) return t.Any(x => x is "hello" or "hi" or "thanks" or "help" or "quote" or "schedule" or "schedules");
            return en >= 2 && en > es * 1.5;
        }
        private static bool EsEspanol(string n)
        {
            var t = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int es = t.Count(PalabrasEspanol.Contains), en = t.Count(PalabrasIngles.Contains);
            return t.Length >= 3 && es >= 2 && es > en * 1.5;
        }

        // =====================================================================
        // Conversión de monedas (CLP, USD, UF, EUR) con los valores del Banco Central
        // =====================================================================
        private static readonly Regex RxMontoCl = new(@"(?<n>\d{1,3}(?:\.\d{3})+|\d+)(?:,(?<d>\d{1,4}))?(?:\s*(?<mult>mil|millones|millon|millón|lucas|luca|palos|palo|k)\b)?", RegexOptions.IgnoreCase);
        private static readonly Regex RxMontoEn = new(@"(?<n>\d{1,3}(?:,\d{3})+|\d+)(?:\.(?<d>\d{1,4}))?(?:\s*(?<mult>thousand|million|k)\b)?", RegexOptions.IgnoreCase);

        private static string? MonedaDe(string token) => token switch
        {
            "dolar" or "dolares" or "usd" or "dollar" or "dollars" => "USD",
            "uf" or "ufs" => "UF",
            "euro" or "euros" or "eur" => "EUR",
            "peso" or "pesos" or "clp" or "luca" or "lucas" or "palo" or "palos" => "CLP",
            _ => null
        };

        private async Task<Respuesta?> ConvertirAsync(string texto, string n, EstadoAsistente e, bool en)
        {
            var tokens = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var monedas = tokens.Select(MonedaDe).Where(m => m != null).Select(m => m!).ToList();
            if (monedas.Count == 0) return null;
            var m = (en ? RxMontoEn : RxMontoCl).Match(texto.ToLowerInvariant());
            if (!m.Success) return null;
            bool pideConversion = Hay(n, "cuanto es", "cuanto son", "cuantos pesos", "cuantos dolares", "cuantas uf", "convierte", "convertir", "conversion", "equivale", "equivalen",
                    "en pesos", "a pesos", "en dolares", "a dolares", "en uf", "a uf", "en euros", "a euros", "pasar", "pasame", "calcula",
                    "how much", "convert", "conversion", "in pesos", "to pesos", "in dollars", "to dollars", "in clp", "to clp", "in usd", "to usd", "in uf", "to uf", "in euros", "to euros", "worth")
                || tokens.Length <= 3;
            if (!pideConversion || Hay(n, "flete", "contenedor", "cotizar", "cotizacion", "tarifa", "freight", "container")) return null;

            // Monto
            string ent = m.Groups["n"].Value.Replace(".", "").Replace(",", "");
            if (!decimal.TryParse(ent, NumberStyles.Number, CultureInfo.InvariantCulture, out var monto)) return null;
            if (m.Groups["d"].Success) monto += decimal.Parse("0." + m.Groups["d"].Value, CultureInfo.InvariantCulture);
            string mult = m.Groups["mult"].Value.ToLowerInvariant();
            if (mult is "mil" or "k" or "thousand" or "luca" or "lucas") monto *= 1000;
            if (mult is "millones" or "millon" or "millón" or "million" or "palo" or "palos") monto *= 1_000_000;
            if (monto <= 0 || monto > 1_000_000_000_000m) return null;

            string origen = mult is "luca" or "lucas" or "palo" or "palos" ? "CLP" : monedas[0];
            string destino = monedas.FirstOrDefault(x => x != origen) ?? (origen == "CLP" ? "USD" : "CLP");

            var usd = await _indicadores.ObtenerDolarAsync();
            var uf = await _indicadores.ObtenerUfAsync();
            var eur = await _indicadores.ObtenerEuroAsync();
            IndicadorValor? Valor(string mon) => mon switch { "USD" => usd, "UF" => uf, "EUR" => eur, _ => null };
            decimal Tasa(string mon) => Valor(mon)?.Valor ?? 1m;
            decimal clp = monto * Tasa(origen);
            decimal res = clp / Tasa(destino);

            var cult = en ? En : Cl;
            string F(decimal v, string mon) => mon switch
            {
                "CLP" => "$" + Math.Round(v).ToString("N0", cult),
                "USD" => "US$" + v.ToString("N2", cult),
                "UF" => "UF " + v.ToString("N2", cult),
                _ => "€" + v.ToString("N2", cult)
            };
            string NombreTasa(string mon, IndicadorValor v)
            {
                string nombre = en ? (mon == "USD" ? "the observed dollar" : mon == "UF" ? "the UF" : "the euro") : (mon == "USD" ? "el dólar observado" : mon == "UF" ? "la UF" : "el euro");
                string fecha = v.Fecha.HasValue ? (en ? " of " + v.Fecha.Value.ToString("MMMM d", En) : " del " + v.Fecha.Value.ToString("d 'de' MMMM", Cl)) : "";
                return $"{nombre}{fecha}: $" + v.Valor.ToString("N2", cult);
            }
            var usadas = new[] { origen, destino }.Where(x => x != "CLP").Distinct().Select(x => NombreTasa(x, Valor(x)!)).ToList();
            bool bc = new[] { origen, destino }.Where(x => x != "CLP").All(x => Valor(x)!.Fuente == IndicadoresEconomicosService.FuenteBancoCentral && !Valor(x)!.EsRespaldo);

            e.Tema = "indicadores"; e.Grupo = "empresa"; e.Fallos = 0;
            string txt = en
                ? $"{F(monto, origen)} is about {F(res, destino)}.\nUsing {string.Join(" and ", usadas)}{(bc ? ", from the Central Bank of Chile" : "")}. It's a reference: invoices use the exchange rate shown on each one."
                : $"{F(monto, origen)} son aproximadamente {F(res, destino)}.\nCon {string.Join(" y ", usadas)}{(bc ? ", según el Banco Central" : "")}. Es una referencia: en las facturas se usa el tipo de cambio que indica cada una.";
            return R(txt, e, new(), en ? new() { "Today's exchange rates", "I want a quote" } : new() { "Valores de hoy", "Quiero cotizar" });
        }

        // =====================================================================
        // Conversación en inglés
        // =====================================================================
        private Accion? AEn(string id) => id switch
        {
            "cotizar" => new("Get a quote in the portal", "/PortalCliente/SolicitarCotizacion", "bi-calculator"),
            "solicitar" => new("Request a service", "/PortalCliente/SolicitarReserva", "bi-clipboard-check"),
            "itinerarios" => new("View schedules", "/Itinerarios", "bi-calendar2-week"),
            "portal" => new("Portal sign-in", "/Login", "bi-shield-lock"),
            "registro" => new("Register my company", "/Registro", "bi-building-add"),
            "formulario" => new("Leave my details", "/#contacto", "bi-envelope"),
            "wsp" => WhatsApp != null ? new("Message us on WhatsApp", "https://wa.me/" + WhatsApp, "bi-whatsapp") : null,
            "pag:maritimo" => new("Ocean service", "/servicios/maritimo", "bi-water"),
            "pag:terrestre" => new("Trucking service", "/servicios/terrestre", "bi-truck"),
            "pag:documental" => new("Documentation service", "/servicios/documental", "bi-file-earmark-text"),
            "pag:agencia-de-aduanas" => new("Customs brokerage", "/servicios/agencia-de-aduanas", "bi-shield-check"),
            "pag:portal-de-clientes" => new("Explore the portal", "/servicios/portal-de-clientes", "bi-display"),
            _ => null
        };

        private Respuesta DesdeTemaEn(TemaAsistente t, EstadoAsistente e)
        {
            e.Tema = t.Id; e.Grupo = t.Grupo; e.Fallos = 0;
            string texto = t.Respuestas[Azar.Next(t.Respuestas.Length)].Replace("{correo}", Correo).Replace("{saludo}", SaludoEn());
            if (t.Id is "hours" or "contact" && HoraChile().DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                texto += " It's the weekend in Chile right now, so the team will reply on the next business day.";
            return R(texto, e, (t.Acciones ?? Array.Empty<string>()).Select(AEn).ToList(), (t.Sugerencias ?? Array.Empty<string>()).ToList());
        }
        private static string SaludoEn() { var h = HoraChile().Hour; return h < 12 ? "Good morning" : h < 19 ? "Good afternoon" : "Good evening"; }

        private async Task<Respuesta> ResponderInglesAsync(List<Mensaje> historial, EstadoAsistente e, string texto, string n, CancellationToken ct)
        {
            try
            {
                // Una cotización guiada en curso (en español) no se continúa en inglés.
                if (e.Flujo != null) { e.Flujo = null; e.Datos.Clear(); e.Paso = 0; }

                if (e.Esperando is "puerto_itinerario" or "puerto_transito")
                {
                    var ps = await DetectarPuertosAsync(n);
                    if (ps.Count > 0) { bool tr = e.Esperando == "puerto_transito"; e.Esperando = null; return await ItinerariosEnAsync(ps, n, e, tr); }
                }
                if (e.Esperando == "codigo" && CodigoSeguimiento(texto) is string c0) { e.Esperando = null; return SeguimientoEn(c0, e); }
                e.Esperando = null;

                if (CodigoSeguimiento(texto) is string cod && (Hay(n, "track", "tracking", "booking", "bl", "container", "where is", "status") || n.Split(' ').Length <= 2))
                    return SeguimientoEn(cod, e);
                if (Hay(n, "where is my", "track my", "tracking", "track a", "status of my", "my shipment", "my container", "my booking", "my cargo"))
                {
                    e.Esperando = "codigo"; e.Tema = "seguimiento"; e.Grupo = "portal"; e.Fallos = 0;
                    return R("Sure. Every shipment's details are in the customer portal. Send me the booking, BL or container number and I'll give you a direct link.", e,
                        new() { AEn("portal"), AEn("pag:portal-de-clientes") }, new() { "I don't have an account", "Talk to a person" });
                }
                if (await ConvertirAsync(texto, n, e, true) is Respuesta conv) return conv;
                if (Hay(n, "exchange rate", "exchange rates", "dollar", "usd", "uf", "euro", "euros", "rates today", "today s exchange rates", "currency"))
                    return await IndicadoresEnAsync(n, e);
                if (Hay(n, "which carriers", "what carriers", "shipping lines", "carriers do you", "work with msc", "work with maersk", "carriers"))
                    return await NavierasEnAsync(e);

                bool pideItinerario = Hay(n, "schedule", "schedules", "sailing", "sailings", "next vessel", "next ship", "departure", "departures", "when does", "vessel to", "ship to", "vessels to", "when is the next", "when do", "leaves");
                bool pideTransito = Hay(n, "how long", "transit time", "how many days", "takes to", "days to get", "transit");
                if (pideItinerario || pideTransito)
                {
                    var ps = await DetectarPuertosAsync(n);
                    if (ps.Count == 0 && e.Datos.TryGetValue("destino", out var dPrev)) ps = await DetectarPuertosAsync(Norm("to " + dPrev));
                    if (ps.Count > 0) return await ItinerariosEnAsync(ps, n, e, pideTransito && !pideItinerario);
                    e.Esperando = pideTransito ? "puerto_transito" : "puerto_itinerario"; e.Tema = "itinerarios"; e.Grupo = "maritimo"; e.Fallos = 0;
                    return R(pideTransito
                            ? "It depends on the route, the carrier and whether it's direct or with transshipment. Where is it going? I'll tell you how many days the next sailings take."
                            : "Sure! Where do you want to ship to? I'll show you the next vessels. You can also see every sailing in Schedules, no sign-up needed.",
                        e, new() { AEn("itinerarios") }, await DestinosFrecuentesAsync());
                }

                var (tema, puntaje, segundo) = MejorTema(n, e, AsistenteConocimiento.TemasIngles);
                if (tema != null && puntaje >= 0.7)
                {
                    var r = DesdeTemaEn(tema, e);
                    if (OtraPregunta(texto, tema, AsistenteConocimiento.TemasIngles) is TemaAsistente otro)
                    {
                        var r2 = DesdeTemaEn(otro, new EstadoAsistente());
                        var acc = r.Acciones.Concat(r2.Acciones).GroupBy(a => a.Url).Select(g => g.First()).Take(4).Cast<Accion?>().ToList();
                        return R(r.Texto + "\n\nAnd about your other question: " + r2.Texto, e, acc, r.Sugerencias);
                    }
                    if ((await DetectarPuertosAsync(n)).FirstOrDefault() is PuertoDetectado pd && r.Sugerencias.Count < 4 && !r.Sugerencias.Contains("Sailings to " + pd.Nombre))
                        r.Sugerencias.Insert(0, "Sailings to " + pd.Nombre);
                    return r;
                }

                var sueltos = await DetectarPuertosAsync(n);
                if (sueltos.Count > 0 && n.Split(' ').Length <= 6) return await ItinerariosEnAsync(sueltos, n, e, false);

                e.Fallos++;
                if (e.Fallos >= 2)
                {
                    e.Fallos = 0;
                    return R("I think someone from our team can help you better with this. Leave your details or write to us and we'll get back to you shortly.", e,
                        new() { AEn("wsp"), AEn("formulario") }, AsistenteConocimiento.SugerenciasInicioIngles.ToList());
                }
                return R("Sorry, I didn't quite get that. Could you say it another way? For example, where you want to ship or what you'd like to know.", e,
                    new(), AsistenteConocimiento.SugerenciasInicioIngles.ToList());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error del asistente (inglés) con el mensaje {Texto}", texto);
                return R("Sorry, I had a problem answering that. Email us at " + Correo + " or leave your details and we'll help you.", new EstadoAsistente(), new() { AEn("formulario") }, new());
            }
        }

        private Respuesta SeguimientoEn(string codigo, EstadoAsistente e)
        {
            e.Tema = "seguimiento"; e.Grupo = "portal"; e.Fallos = 0;
            return R($"Here's a direct link to {codigo} in the portal. You'll see the vessel, dates, milestones and documents (you'll be asked to sign in).", e,
                new() { new Accion("View " + codigo + " in the portal", "/PortalCliente/Index?SearchString=" + Uri.EscapeDataString(codigo), "bi-search"), AEn("registro") },
                new() { "What is a booking?", "Talk to a person" });
        }

        private async Task<Respuesta> IndicadoresEnAsync(string n, EstadoAsistente e)
        {
            bool todos = !(Hay(n, "dollar", "usd") || Hay(n, "uf") || Hay(n, "euro", "euros")) || Hay(n, "exchange rates", "rates");
            var lineas = new List<string>(); bool bc = true;
            async Task Linea(string nombre, Func<Task<IndicadorValor>> f)
            {
                var v = await f();
                if (v.EsRespaldo || v.Fuente != IndicadoresEconomicosService.FuenteBancoCentral) bc = false;
                lineas.Add($"• {nombre}: CLP $" + v.Valor.ToString("N2", En) + (v.Fecha.HasValue ? " (" + v.Fecha.Value.ToString("MMMM d", En) + ")" : "") + (v.EsRespaldo ? " — latest available value" : ""));
            }
            if (todos || Hay(n, "dollar", "usd")) await Linea("Observed dollar (USD)", _indicadores.ObtenerDolarAsync);
            if (todos || Hay(n, "uf")) await Linea("UF", _indicadores.ObtenerUfAsync);
            if (todos || Hay(n, "euro", "euros")) await Linea("Euro", _indicadores.ObtenerEuroAsync);
            e.Tema = "indicadores"; e.Grupo = "empresa"; e.Fallos = 0;
            return R("Here are today's values" + (bc ? " from the Central Bank of Chile" : "") + ":\n" + string.Join("\n", lineas) + "\nWe use them to convert foreign-currency amounts into Chilean pesos.",
                e, new(), new() { "Convert 1,000 USD to CLP", "I want a quote" });
        }

        private async Task<Respuesta> NavierasEnAsync(EstadoAsistente e)
        {
            e.Tema = "navieras"; e.Grupo = "maritimo"; e.Fallos = 0;
            var salidas = await ItinerariosHelper.ProximasSalidasAsync(_context, max: 500);
            var nombres = salidas.Where(s => s.IdNavieraNavigation != null).Select(s => s.IdNavieraNavigation!.NombreNaviera.Trim()).Distinct().OrderBy(x => x).ToList();
            string lista = nombres.Count == 0 ? "the main carriers serving Chile" : string.Join(", ", nombres.Take(10));
            return R($"We work with {lista}, among others. We choose the carrier with you based on route, date and rate; you can see their next sailings in Schedules.", e,
                new() { AEn("itinerarios") }, new() { "Sailings to Shanghai", "I want a quote" });
        }

        private async Task<Respuesta> ItinerariosEnAsync(List<PuertoDetectado> puertos, string n, EstadoAsistente e, bool enfoqueTransito)
        {
            e.Tema = "itinerarios"; e.Grupo = "maritimo"; e.Fallos = 0;
            var (pol, pod) = Roles(puertos, Hay(n, "import", "importing", "bring"));
            var salidas = await ItinerariosHelper.ProximasSalidasAsync(_context, max: 500);
            string? cPol = pol == null ? null : PuertosHelper.Clave(pol.Puerto), cPod = pod == null ? null : PuertosHelper.Clave(pod.Puerto);
            var lista = salidas
                .Where(s => cPol == null || (s.IdPuertoOrigenNavigation != null && PuertosHelper.Clave(s.IdPuertoOrigenNavigation) == cPol))
                .Where(s => cPod == null || (s.IdPuertoDestinoNavigation != null && PuertosHelper.Clave(s.IdPuertoDestinoNavigation) == cPod))
                .OrderBy(s => s.Etd).ToList();
            string ruta = (pol != null ? "from " + pol.Nombre : "") + (pol != null && pod != null ? " " : "") + (pod != null ? "to " + pod.Nombre : "");
            string url = "/Itinerarios?" + string.Join("&", new[] { pol != null ? "pol=" + pol.Puerto.Id : null, pod != null ? "pod=" + pod.Puerto.Id : null }.Where(x => x != null));
            e.Datos.Clear();
            if (pol != null) e.Datos["origen"] = pol.Nombre;
            if (pod != null) e.Datos["destino"] = pod.Nombre;
            if (lista.Count == 0)
                return R($"There are no published sailings {ruta} in the next {ItinerarioNave.SemanasHorizonte} weeks, but we can still quote that route: an account manager will suggest vessels and dates.", e,
                    new() { AEn("itinerarios"), AEn("formulario") }, new() { "Talk to a person", "View all sailings" });
            var sb = new StringBuilder();
            if (enfoqueTransito)
            {
                int min = lista.Min(s => s.DiasTransito), max = lista.Max(s => s.DiasTransito);
                sb.Append($"Based on the published sailings {ruta}, the voyage takes {(min == max ? min + " days" : $"between {min} and {max} days")}. These are the next ones:\n");
            }
            else sb.Append($"These are the next sailings {ruta}:\n");
            foreach (var s in lista.Take(4))
            {
                string nav = s.IdNavieraNavigation?.NombreNaviera ?? "Carrier";
                sb.Append($"• {nav} · {s.NaveViaje}: departs {s.Etd.ToString("ddd MMM d", En)}");
                if (cPol == null) sb.Append($" from {Titulo(s.IdPuertoOrigenNavigation?.NombrePuerto)}");
                sb.Append($", arrives {s.Eta.ToString("ddd MMM d", En)}");
                if (cPod == null) sb.Append($" in {Titulo(s.IdPuertoDestinoNavigation?.NombrePuerto)}");
                sb.Append($" ({s.DiasTransito} days{(string.IsNullOrWhiteSpace(s.Transbordo) ? ", direct" : ", via " + s.Transbordo)})\n");
            }
            if (lista.Count > 4) sb.Append($"…and {lista.Count - 4} more in Schedules.\n");
            sb.Append("Dates are provided by each carrier and may shift slightly. Would you like a quote?");
            return R(sb.ToString(), e, new() { new Accion("View these sailings", url, "bi-calendar2-week"), AEn("cotizar") },
                new() { "I want a quote", "What is transshipment?" });
        }
    }
}
