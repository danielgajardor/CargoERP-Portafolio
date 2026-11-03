using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtilsonCargoSpa.Services
{
    /// <summary>Valor de un indicador con su origen, para mostrar de dónde salió.</summary>
    /// <param name="Valor">Pesos chilenos por unidad (1 USD o 1 UF).</param>
    /// <param name="Fecha">Fecha a la que corresponde el valor publicado.</param>
    /// <param name="Fuente">"Banco Central", "CMF", "mindicador.cl", "último valor guardado" o "valor de respaldo".</param>
    /// <param name="EsRespaldo">true si NO es el valor del día obtenido en línea.</param>
    public record IndicadorValor(decimal Valor, DateTime? Fecha, string Fuente, bool EsRespaldo);

    /// <summary>
    /// UF, Dólar Observado y Euro del día para convertir montos a CLP, para el Main Board de
    /// Finanzas, el asistente y la barra superior del sitio web (nov-2026).
    ///
    /// El Banco Central de Chile es la fuente principal y prioritaria (nov-2026). Con el token
    /// de su API configurado ("BancoCentral:Token", ver más abajo):
    ///   1) Se consulta la API REST del Banco Central (BDE, SieteRestWS), con un reintento.
    ///   2) Si no responde justo en ese momento, se usa el último valor publicado por el Banco
    ///      Central que ya se había obtenido (hasta 5 días: cubre fines de semana y feriados).
    ///   3) Solo si no hay ninguno, se usa otra fuente de forma provisoria (CMF, mindicador.cl,
    ///      SII, mercado) y el Banco Central se vuelve a consultar a los 30 minutos.
    /// Sin token, el orden es CMF → mindicador.cl → SII → mercado, como antes.
    /// Al final siempre queda el último valor guardado (App_Data/indicadores.json) y, si nunca
    /// se obtuvo uno real, un valor fijo de respaldo.
    ///
    /// El token está en appsettings.json › BancoCentral:Token (repositorio privado; vigencia de
    /// un año: se renueva en si3.bcentral.cl › Mi Cuenta › "Renovar token"). Una variable de
    /// entorno BancoCentral__Token lo reemplaza sin tocar el archivo. BancoCentral:Usuario y
    /// BancoCentral:Clave (email y contraseña de la BDE) se usan con el servicio SOAP cuando el
    /// Banco Central rechaza el token o no hay token.
    /// </summary>
    public class IndicadoresEconomicosService
    {
        public const string Dolar = "dolar";
        public const string Uf = "uf";
        public const string Euro = "euro";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IndicadoresEconomicosService> _logger;
        private readonly IConfiguration _config;
        private readonly string _archivo;

        /// <summary>Fuente principal y prioritaria (nov-2026).</summary>
        public const string FuenteBancoCentral = "Banco Central";
        /// <summary>Cliente HTTP con nombre propio: su registro en el log queda en nivel Warning para no escribir la URL con el token.</summary>
        public const string ClienteBancoCentral = "BancoCentral";
        private const string Api = "https://si3.bcentral.cl/SieteRestWS/SieteRestWS.ashx";

        // Vence: hasta cuándo sirve sin volver a consultar. Definitivo: no hace falta reintentar.
        private static readonly Dictionary<string, (IndicadorValor Valor, DateTime Vence, bool Definitivo)> _cache = new();
        private static readonly object _lock = new();

        // Solo se usan si nunca se ha podido obtener un valor real (ni hay uno guardado).
        private const decimal UF_RESPALDO = 39500m;
        private const decimal DOLAR_RESPALDO = 950m;
        private const decimal EURO_RESPALDO = 1030m;

        public IndicadoresEconomicosService(IHttpClientFactory httpClientFactory, ILogger<IndicadoresEconomicosService> logger,
            IConfiguration config, IWebHostEnvironment env)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _config = config;
            _archivo = Path.Combine(env.ContentRootPath, "App_Data", "indicadores.json");
        }

        public async Task<decimal> ObtenerValorUfAsync() => (await ObtenerAsync(Uf)).Valor;
        public async Task<decimal> ObtenerValorDolarAsync() => (await ObtenerAsync(Dolar)).Valor;
        public Task<IndicadorValor> ObtenerUfAsync() => ObtenerAsync(Uf);
        public Task<IndicadorValor> ObtenerDolarAsync() => ObtenerAsync(Dolar);
        public Task<IndicadorValor> ObtenerEuroAsync() => ObtenerAsync(Euro);

        /// <summary>
        /// Valor del indicador. Las páginas no esperan a internet si el actualizador en segundo
        /// plano (IndicadoresActualizador) ya trajo el valor de hoy; si no, se intenta en línea.
        /// </summary>
        public Task<IndicadorValor> ObtenerAsync(string codigo) => ObtenerAsync(codigo, forzar: false);

        /// <summary>Consulta las fuentes aunque haya fallado hace poco (lo usa el actualizador en segundo plano).</summary>
        public Task<IndicadorValor> ActualizarAsync(string codigo) => ObtenerAsync(codigo, forzar: true);

        /// <summary>true si está configurado el token de la API del Banco Central.</summary>
        public bool BancoCentralConfigurado => !string.IsNullOrWhiteSpace(_config["BancoCentral:Token"])
            || (!string.IsNullOrWhiteSpace(_config["BancoCentral:Usuario"]) && !string.IsNullOrWhiteSpace(_config["BancoCentral:Clave"]));

        /// <summary>
        /// true si el valor vigente del indicador es el definitivo del día: obtenido recién del
        /// Banco Central o, sin token del Banco Central, de cualquier fuente en línea. Si no lo
        /// es, el actualizador en segundo plano vuelve a intentar en pocos minutos.
        /// </summary>
        public bool EsDefinitivo(string codigo)
        {
            lock (_lock) { return _cache.TryGetValue(codigo, out var c) && c.Definitivo && DateTime.Now < c.Vence; }
        }

        private async Task<IndicadorValor> ObtenerAsync(string codigo, bool forzar)
        {
            lock (_lock)
            {
                // Valor vigente: el del Banco Central (o en línea) sirve hasta mañana; uno
                // provisorio, unos minutos. El actualizador (forzar) no espera a que venza.
                if (_cache.TryGetValue(codigo, out var c) && DateTime.Now < c.Vence && !(forzar && !c.Definitivo))
                    return c.Valor;
            }

            _plazo = forzar ? 20 : 8;
            IndicadorValor? enLinea = null;
            bool conBancoCentral = BancoCentralConfigurado;

            // 1) Banco Central. El actualizador en segundo plano reintenta una vez; una página no
            //    espera dos veces (si falla, toma el último valor del Banco Central ya obtenido).
            if (conBancoCentral)
            {
                for (int intento = 0; intento < (forzar ? 2 : 1) && enLinea == null; intento++)
                {
                    try { enLinea = await DesdeBancoCentralAsync(codigo); }
                    catch (Exception ex) { _logger.LogWarning("Banco Central no respondió para {Codigo} (intento {Intento}): {Error}", codigo, intento + 1, ex.Message); }
                }
                if (enLinea != null)
                {
                    lock (_lock) { _cache[codigo] = (enLinea, DateTime.Today.AddDays(1), true); }
                    Guardar(codigo, enLinea);
                    Guardar(ClaveBancoCentral(codigo), enLinea);
                    return enLinea;
                }

                // 2) Último valor publicado por el Banco Central ya obtenido, si es reciente.
                var bc = Leer(ClaveBancoCentral(codigo));
                if (bc != null && bc.Fecha != null && bc.Fecha.Value.Date >= DateTime.Today.AddDays(-5))
                {
                    var ultimo = bc with { EsRespaldo = false };
                    lock (_lock) { _cache[codigo] = (ultimo, DateTime.Now.AddMinutes(30), false); }
                    return ultimo;
                }
            }

            // 3) Otras fuentes (provisorias si hay token del Banco Central).
            foreach (var fuente in new Func<string, Task<IndicadorValor?>>[] { DesdeCmfAsync, DesdeMindicadorAsync, DesdeSiiAsync, DesdeMercadoAsync })
            {
                try
                {
                    enLinea = await fuente(codigo);
                    if (enLinea != null && enLinea.Valor > 0) break;
                    enLinea = null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo obtener {Codigo} desde una de las fuentes.", codigo);
                }
            }

            if (enLinea != null)
            {
                if (conBancoCentral) _logger.LogWarning("Sin respuesta del Banco Central para {Codigo}: se usa {Fuente} de forma provisoria.", codigo, enLinea.Fuente);
                lock (_lock) { _cache[codigo] = (enLinea, conBancoCentral ? DateTime.Now.AddMinutes(30) : DateTime.Today.AddDays(1), !conBancoCentral); }
                Guardar(codigo, enLinea);
                return enLinea;
            }

            _logger.LogWarning("Sin {Codigo} en línea: se usa el último valor guardado o el de respaldo.", codigo);
            // Entre los guardados se prefiere el del Banco Central.
            var guardado = (conBancoCentral ? Leer(ClaveBancoCentral(codigo)) : null) ?? Leer(codigo);
            var respaldo = guardado != null
                ? guardado with { Fuente = "último valor guardado (" + guardado.Fuente + ")", EsRespaldo = true }
                : new IndicadorValor(codigo == Uf ? UF_RESPALDO : codigo == Euro ? EURO_RESPALDO : DOLAR_RESPALDO, null, "valor de respaldo", true);
            // Una falla no se reintenta en cada página: 15 minutos (el actualizador sí reintenta).
            lock (_lock) { _cache[codigo] = (respaldo, DateTime.Now.AddMinutes(15), false); }
            return respaldo;
        }

        private static string ClaveBancoCentral(string codigo) => "bc:" + codigo;

        private int _plazo = 8;
        private HttpClient Cliente()
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(_plazo);
            // Agente de navegador común: algunos sitios rechazan clientes que no se identifican así.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/html;q=0.9, */*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es-CL,es;q=0.9");
            return client;
        }

        // SII: tablas anuales oficiales de la UF y del dólar observado (sin registro).
        //    Cada mes está en un bloque id="mes_enero"... con pares <th>día</th><td>valor</td>.
        private static readonly string[] Meses = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
        private async Task<IndicadorValor?> DesdeSiiAsync(string codigo)
        {
            if (codigo == Euro) return null;
            var hoy = DateTime.Today;
            for (int intento = 0; intento < 2; intento++) // si el día de hoy no tiene valor (fin de semana), el último publicado
            {
                var fechaBase = intento == 0 ? hoy : new DateTime(hoy.Year, hoy.Month, 1).AddDays(-1);
                string url = $"https://www.sii.cl/valores_y_fechas/{(codigo == Uf ? "uf/uf" : "dolar/dolar")}{fechaBase.Year}.htm";
                string html = await Cliente().GetStringAsync(url);
                string mes = Meses[fechaBase.Month - 1];
                var bloque = System.Text.RegularExpressions.Regex.Match(html, "id=[\"']mes_" + mes + "[\"'](.*?)(?=id=[\"']mes_|$)", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!bloque.Success) continue;
                var valores = new SortedDictionary<int, decimal>();
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(bloque.Groups[1].Value,
                    "<th[^>]*>\\s*(?:<[^>]+>\\s*)*(\\d{1,2})\\s*(?:<[^>]+>\\s*)*</th>\\s*<td[^>]*>\\s*(?:<[^>]+>\\s*)*([\\d\\.]+,\\d{1,2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if (decimal.TryParse(m.Groups[2].Value, NumberStyles.Number, new CultureInfo("es-CL"), out var v) && v > 0)
                        valores[int.Parse(m.Groups[1].Value)] = v;
                }
                var dia = valores.Keys.Where(d => d <= (fechaBase.Month == hoy.Month && fechaBase.Year == hoy.Year ? hoy.Day : 31)).DefaultIfEmpty(0).Max();
                if (dia > 0) return new IndicadorValor(valores[dia], new DateTime(fechaBase.Year, fechaBase.Month, dia), "SII", false);
            }
            return null;
        }

        // Tipo de cambio de mercado (open.er-api.com, sin registro): referencia para dólar y euro
        //    cuando ninguna fuente oficial responde. No existe para la UF.
        private async Task<IndicadorValor?> DesdeMercadoAsync(string codigo)
        {
            if (codigo == Uf) return null;
            using var doc = JsonDocument.Parse(await Cliente().GetStringAsync("https://open.er-api.com/v6/latest/" + (codigo == Euro ? "EUR" : "USD")));
            if (!doc.RootElement.TryGetProperty("rates", out var rates) || !rates.TryGetProperty("CLP", out var clp)) return null;
            return new IndicadorValor(Math.Round(clp.GetDecimal(), 2), DateTime.Today, "tipo de cambio de mercado", false);
        }

        // Banco Central (API REST de la Base de Datos Estadísticos, SieteRestWS): series diarias
        // del dólar observado, la UF y el euro. Se autentica con el token de la cuenta en la URL.
        // Respuesta: { "Codigo": 0, "Descripcion": "Success", "Series": { "Obs": [ { "indexDateString":
        // "dd-MM-yyyy", "value": "943.12", "statusCode": "OK" | "ND" } ] } }.
        public static string SerieBancoCentral(string codigo) =>
            codigo == Uf ? "F073.UFF.PRE.Z.D" : codigo == Euro ? "F072.CLP.EUR.N.O.D" : "F073.TCO.PRE.Z.D";

        private async Task<IndicadorValor?> DesdeBancoCentralAsync(string codigo)
        {
            // Autenticación según la documentación del Banco Central:
            //   - servicio REST: API Key Token en la URL (BancoCentral:Token);
            //   - servicio SOAP: email y contraseña de la BDE (BancoCentral:Usuario / Clave).
            // Se usa el token y, si el Banco Central lo rechaza o no está, el usuario de la BDE.
            string? token = _config["BancoCentral:Token"], usuario = _config["BancoCentral:Usuario"], clave = _config["BancoCentral:Clave"];
            var hoy = DateTime.Today;
            string desde = hoy.AddDays(-15).ToString("yyyy-MM-dd"), hasta = hoy.ToString("yyyy-MM-dd");

            if (!string.IsNullOrWhiteSpace(token))
            {
                var client = ClienteBc();
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                using var resp = await client.GetAsync(Api + "?token=" + Uri.EscapeDataString(token.Trim())
                    + "&function=GetSeries&timeseries=" + SerieBancoCentral(codigo) + "&firstdate=" + desde + "&lastdate=" + hasta);
                if (!resp.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)resp.StatusCode);
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var raiz = doc.RootElement;
                // Codigo distinto de 0: credencial rechazada (-5), serie inexistente, etc.
                if (raiz.TryGetProperty("Codigo", out var cod) && cod.ValueKind == JsonValueKind.Number && cod.GetInt32() != 0)
                    Rechazo(codigo, "token (REST)", cod.GetInt32(), raiz.TryGetProperty("Descripcion", out var d) ? d.GetString() : null);
                else
                    return UltimaObservacion(raiz, hoy);
            }

            if (!string.IsNullOrWhiteSpace(usuario) && !string.IsNullOrWhiteSpace(clave))
                return await DesdeBancoCentralSoapAsync(codigo, usuario.Trim(), clave, desde, hasta, hoy);
            return null;
        }

        private HttpClient ClienteBc()
        {
            var client = _httpClientFactory.CreateClient(ClienteBancoCentral);
            client.Timeout = TimeSpan.FromSeconds(_plazo);
            return client;
        }

        private void Rechazo(string codigo, string credencial, int cod, string? descripcion) =>
            _logger.LogWarning("El Banco Central rechazó la consulta de {Codigo} con {Credencial} (código {Cod}): {Descripcion}. "
                + "Revise en si3.bcentral.cl que la API esté activada (\"Activar el uso de la API\") y que la credencial esté vigente.", codigo, credencial, cod, descripcion ?? "");

        // Servicio SOAP (SieteWS.asmx) con email y contraseña de la BDE. Misma información que REST.
        private async Task<IndicadorValor?> DesdeBancoCentralSoapAsync(string codigo, string usuario, string clave, string desde, string hasta, DateTime hoy)
        {
            static string X(string v) => System.Security.SecurityElement.Escape(v) ?? "";
            string sobre = "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">"
                + "<soap:Body><GetSeries xmlns=\"http://bancocentral.org/\">"
                + "<user>" + X(usuario) + "</user><password>" + X(clave) + "</password>"
                + "<firstDate>" + desde + "</firstDate><lastDate>" + hasta + "</lastDate>"
                + "<seriesIds><string>" + SerieBancoCentral(codigo) + "</string></seriesIds>"
                + "</GetSeries></soap:Body></soap:Envelope>";
            using var pedido = new HttpRequestMessage(HttpMethod.Post, "https://si3.bcentral.cl/SieteWS/SieteWS.asmx")
            {
                Content = new StringContent(sobre, System.Text.Encoding.UTF8, "text/xml")
            };
            pedido.Headers.Add("SOAPAction", "\"http://bancocentral.org/GetSeries\"");
            using var resp = await ClienteBc().SendAsync(pedido);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)resp.StatusCode);
            var xml = System.Xml.Linq.XDocument.Parse(await resp.Content.ReadAsStringAsync());

            string? Hijo(System.Xml.Linq.XElement e, string nombre) => e.Elements().FirstOrDefault(x => x.Name.LocalName == nombre)?.Value;
            var resultado = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "GetSeriesResult");
            if (resultado == null) return null;
            if (int.TryParse(Hijo(resultado, "Codigo"), out var cod) && cod != 0)
            {
                Rechazo(codigo, "usuario BDE (SOAP)", cod, Hijo(resultado, "Descripcion"));
                return null;
            }

            IndicadorValor? ultimo = null;
            // Cada observación tiene indexDateString, statusCode y value (número con punto, o NaN).
            foreach (var o in resultado.Descendants().Where(e => e.Elements().Any(x => x.Name.LocalName == "indexDateString")))
            {
                if ((Hijo(o, "statusCode") ?? "OK") != "OK") continue;
                if (!decimal.TryParse(Hijo(o, "value"), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var v) || v <= 0) continue;
                if (!DateTime.TryParseExact(Hijo(o, "indexDateString"), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) || f > hoy) continue;
                if (ultimo == null || f >= ultimo.Fecha) ultimo = new IndicadorValor(Math.Round(v, 2), f, FuenteBancoCentral, false);
            }
            return ultimo;
        }

        private static IndicadorValor? UltimaObservacion(JsonElement raiz, DateTime hoy)
        {
            if (!raiz.TryGetProperty("Series", out var series) || !series.TryGetProperty("Obs", out var obs) || obs.ValueKind != JsonValueKind.Array) return null;
            IndicadorValor? ultimo = null;
            foreach (var o in obs.EnumerateArray())
            {
                // Días sin publicación (fines de semana, feriados) vienen como "NaN" con statusCode "ND".
                if (o.TryGetProperty("statusCode", out var st) && st.GetString() != "OK") continue;
                if (!o.TryGetProperty("value", out var val)) continue;
                string? texto = val.ValueKind == JsonValueKind.Number ? val.GetRawText() : val.GetString();
                if (!decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) || v <= 0) continue;
                if (!DateTime.TryParseExact(o.GetProperty("indexDateString").GetString(), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)) continue;
                // La UF se publica por adelantado (hasta el día 9 del mes siguiente): se usa la de hoy.
                if (f > hoy) continue;
                if (ultimo == null || f >= ultimo.Fecha) ultimo = new IndicadorValor(v, f, FuenteBancoCentral, false);
            }
            return ultimo;
        }

        // CMF: valores con formato chileno ("943,12" / "39.512,34").
        private async Task<IndicadorValor?> DesdeCmfAsync(string codigo)
        {
            string? key = _config["Cmf:ApiKey"];
            if (string.IsNullOrWhiteSpace(key)) return null;
            string recurso = codigo == Uf ? "uf" : codigo == Euro ? "euro" : "dolar";
            string url = $"https://api.cmfchile.cl/api-sbifv3/recursos_api/{recurso}?apikey={Uri.EscapeDataString(key)}&formato=json";
            using var doc = JsonDocument.Parse(await Cliente().GetStringAsync(url));
            string lista = codigo == Uf ? "UFs" : codigo == Euro ? "Euros" : "Dolares";
            if (!doc.RootElement.TryGetProperty(lista, out var arr) || arr.GetArrayLength() == 0) return null;
            var e = arr[0];
            if (!decimal.TryParse(e.GetProperty("Valor").GetString(), NumberStyles.Number, new CultureInfo("es-CL"), out var v) || v <= 0) return null;
            DateTime.TryParse(e.GetProperty("Fecha").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var f);
            return new IndicadorValor(v, f == default ? null : f, "CMF", false);
        }

        // mindicador.cl: "serie" viene del más reciente al más antiguo.
        private async Task<IndicadorValor?> DesdeMindicadorAsync(string codigo)
        {
            using var doc = JsonDocument.Parse(await Cliente().GetStringAsync("https://mindicador.cl/api/" + codigo));
            var serie = doc.RootElement.GetProperty("serie");
            if (serie.GetArrayLength() == 0) return null;
            var e = serie[0];
            decimal v = e.GetProperty("valor").GetDecimal();
            DateTime? f = e.TryGetProperty("fecha", out var fe) && fe.TryGetDateTime(out var d) ? d.ToLocalTime().Date : null;
            return new IndicadorValor(v, f, "mindicador.cl", false);
        }

        private record Guardado(decimal Valor, DateTime? Fecha, string Fuente);

        private void Guardar(string codigo, IndicadorValor v)
        {
            try
            {
                lock (_lock)
                {
                    var todos = LeerArchivo();
                    todos[codigo] = new Guardado(v.Valor, v.Fecha, v.Fuente);
                    Directory.CreateDirectory(Path.GetDirectoryName(_archivo)!);
                    File.WriteAllText(_archivo, JsonSerializer.Serialize(todos));
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "No se pudo guardar {Archivo}", _archivo); }
        }

        private IndicadorValor? Leer(string codigo)
        {
            try
            {
                lock (_lock)
                {
                    return LeerArchivo().TryGetValue(codigo, out var g) && g.Valor > 0 ? new IndicadorValor(g.Valor, g.Fecha, g.Fuente, true) : null;
                }
            }
            catch { return null; }
        }

        private Dictionary<string, Guardado> LeerArchivo()
        {
            if (!File.Exists(_archivo)) return new();
            try { return JsonSerializer.Deserialize<Dictionary<string, Guardado>>(File.ReadAllText(_archivo)) ?? new(); }
            catch { return new(); }
        }
    }
}
