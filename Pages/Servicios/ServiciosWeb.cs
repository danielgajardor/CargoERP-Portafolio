using System.Collections.Generic;
using System.Linq;

namespace AtilsonCargoSpa.Pages.Servicios
{
    // Contenido de las páginas de servicio del sitio público (nov-2026):
    // /servicios/maritimo, /terrestre, /documental, /agencia-de-aduanas y /portal-de-clientes.
    // El menú "Servicios", la landing y el pie leen esta misma lista.
    // Para cambiar un texto basta con editarlo aquí; la vista (Servicio.cshtml) arma la página.
    public record ItemServicio(string Icono, string Titulo, string Texto);
    public record BloqueServicio(string Icono, string Titulo, string Texto, string[] Puntos);
    public record PasoServicio(string Titulo, string Texto);
    public record DatoFicha(string Etiqueta, string Valor);

    public record ServicioWeb(
        string Slug, string Nombre, string Icono, string Resumen,
        string Lema, string Titular, string Bajada,
        DatoFicha[] Ficha, string[] Intro,
        ItemServicio[] Incluye, BloqueServicio[] Bloques, PasoServicio[] Pasos,
        string[] EnElPortal)
    {
        public string Url => "/servicios/" + Slug;
        public bool EsPortal => Slug == ServiciosWeb.SlugPortal;
        /// <summary>Descripción de una línea para el menú "Servicios" (nov-2026).</summary>
        public string Corto => ServiciosWeb.Cortos.TryGetValue(Slug, out var c) ? c : Resumen;
        /// <summary>Nombre sin "Servicio" para el menú: Marítimo, Terrestre…</summary>
        public string NombreMenu => Nombre.StartsWith("Servicio ") ? char.ToUpper(Nombre[9]) + Nombre[10..] : Nombre;
    }

    public static class ServiciosWeb
    {
        public const string SlugPortal = "portal-de-clientes";

        // Textos cortos y parejos para el menú desplegable (los largos lo desordenaban).
        public static readonly IReadOnlyDictionary<string, string> Cortos = new Dictionary<string, string>
        {
            ["maritimo"] = "FCL dry y reefer, exportación e importación",
            ["terrestre"] = "Depósito, planta y puerto, con GPS",
            ["documental"] = "BL, certificados y originales",
            ["agencia-de-aduanas"] = "DUS, DIN, SAG y seguro de carga",
            [SlugPortal] = "Tus embarques en línea, las 24 horas",
        };

        public static ServicioWeb? Buscar(string? slug) =>
            Todos.FirstOrDefault(s => s.Slug == (slug ?? "").Trim().ToLowerInvariant());

        public static readonly IReadOnlyList<ServicioWeb> Todos = new List<ServicioWeb>
        {
            new(
                "maritimo", "Servicio marítimo", "bi-water",
                "Exportación e importación en contenedor completo, dry y reefer, con las principales navieras.",
                "Exportación e importación por mar",
                "Tu carga, en la nave correcta.",
                "Reservamos tu espacio con las principales navieras y seguimos cada contenedor desde el retiro del vacío hasta el arribo en destino.",
                new[]
                {
                    new DatoFicha("Contenedores", "20' y 40' dry, 40' high cube, 20' y 40' reefer"),
                    new DatoFicha("Modalidad", "Exportación e importación FCL"),
                    new DatoFicha("Pago del flete", "Prepaid o collect"),
                    new DatoFicha("Tarifa", "Spot o contrato con la naviera"),
                    new DatoFicha("Itinerarios", "Salidas de las próximas 5 semanas"),
                },
                new[]
                {
                    "Coordinamos la carga de contenedor completo (FCL) entre Chile y el mundo. Elegimos contigo la naviera, la nave y la ruta, directa o con transbordo, según tu fecha de producción, tu destino y tu presupuesto.",
                    "Operaciones controla cada hito del embarque: reserva, retiro del contenedor, stacking, zarpe, transbordo y arribo. Tú lo ves todo, al mismo tiempo que nosotros, en el portal de clientes.",
                },
                new[]
                {
                    new ItemServicio("bi-bookmark-check", "Reserva de espacio", "Gestionamos el booking con la naviera y te confirmamos nave, viaje y fechas por escrito."),
                    new ItemServicio("bi-snow", "Carga refrigerada", "Contenedores reefer con temperatura, atmósfera controlada, ventilación y humedad definidas para tu producto."),
                    new ItemServicio("bi-calendar2-week", "Itinerarios publicados", "Salidas de las próximas semanas con su corte documental, para planificar tu producción."),
                    new ItemServicio("bi-stopwatch", "Plazos bajo control", "Stacking, cut-off y late arrival vigilados para que el contenedor no pierda su nave."),
                    new ItemServicio("bi-arrow-repeat", "Roleos y cambios de nave", "Si la naviera rolea la carga o cambia la nave, te avisamos y gestionamos la alternativa."),
                    new ItemServicio("bi-hourglass-split", "Días libres", "Negociamos y controlamos los días libres de demurrage y detention en origen y destino."),
                },
                new[]
                {
                    new BloqueServicio("bi-box-seam", "Carga seca en contenedor completo",
                        "El contenedor viaja solo con tu carga: menos manipulación, más control sobre los tiempos de carga y una tarifa que aprovecha todo el espacio.",
                        new[] { "Contenedores de 20', 40' y 40' high cube", "Exportación e importación", "Rutas directas o con transbordo", "Un solo responsable de punta a punta" }),
                    new BloqueServicio("bi-thermometer-snow", "Carga refrigerada (reefer)",
                        "Fruta fresca, alimentos congelados y productos sensibles a la temperatura viajan con las condiciones definidas desde la reserva. En temporada alta adelantamos la planificación de espacios con las navieras.",
                        new[] { "Temperatura y atmósfera controlada definidas en la reserva", "Ventilación, humedad, CO₂ y O₂ según el producto", "Genset para el traslado terrestre cuando corresponde", "Seguimiento del ingreso del contenedor a puerto" }),
                    new BloqueServicio("bi-cash-coin", "Prepaid o collect, spot o contrato",
                        "Adaptamos el cobro del flete a tu negociación con el comprador: pagado en origen (prepaid) o en destino (collect), con tarifa spot o bajo contrato con la naviera.",
                        new[] { "Flete prepaid o collect", "Tarifas spot o de contrato", "Costos de cada embarque detallados en el portal", "Tipo de cambio del día informado en cada conversión" }),
                },
                new[]
                {
                    new PasoServicio("Cotización", "Te enviamos la tarifa según ruta, contenedor y fecha estimada de embarque."),
                    new PasoServicio("Reserva", "Confirmamos el booking con la naviera: nave, viaje y fechas de stacking."),
                    new PasoServicio("Retiro y carga", "El contenedor vacío sale del depósito y se carga en tu planta."),
                    new PasoServicio("Ingreso a puerto", "Entregamos el contenedor en el terminal dentro del stacking."),
                    new PasoServicio("Zarpe y documentos", "Emitimos el BL y los certificados para que la carga zarpe con todo en regla."),
                    new PasoServicio("Arribo", "Seguimos el viaje y el transbordo hasta el puerto de destino."),
                },
                new[] { "Nave, viaje y naviera de cada booking", "Fechas de zarpe y arribo actualizadas", "Estado de cada contenedor", "BL y documentos del embarque", "Costos del flete y recargos" }),

            new(
                "terrestre", "Servicio terrestre", "bi-truck",
                "Retiro en depósito, carga en planta y entrega en puerto, con el recorrido bajo control.",
                "Depósito, planta y puerto en un solo traslado",
                "Del depósito a la nave, sin escalas.",
                "Programamos el retiro del contenedor vacío, la carga en tu planta y la entrega en el terminal portuario dentro del stacking.",
                new[]
                {
                    new DatoFicha("Recorrido", "Depósito, planta y puerto"),
                    new DatoFicha("Plantas", "Una o varias cargas en el mismo viaje"),
                    new DatoFicha("Puertos", "San Antonio, Valparaíso y Biobío"),
                    new DatoFicha("Reefer", "Genset cuando el traslado lo requiere"),
                    new DatoFicha("Seguimiento", "Recorrido controlado en el mapa"),
                },
                new[]
                {
                    "El transporte terrestre es donde un embarque se gana o se pierde: un contenedor que llega tarde al terminal no sube a su nave. Por eso lo coordinamos con la misma precisión que el flete marítimo.",
                    "Asignamos el transportista, informamos conductor y patente, y seguimos el recorrido desde el depósito hasta el terminal portuario.",
                },
                new[]
                {
                    new ItemServicio("bi-calendar-check", "Programación del viaje", "Fecha y hora de retiro y de carga acordadas con tu planta, con transportista, conductor y patente informados."),
                    new ItemServicio("bi-box-arrow-up-right", "Retiro en depósito", "Retiramos el contenedor vacío que asigna la naviera, en el depósito que corresponde a tu reserva."),
                    new ItemServicio("bi-buildings", "Carga en planta", "Una o varias plantas en el mismo viaje, con la ubicación exacta de cada una."),
                    new ItemServicio("bi-box-arrow-in-down", "Entrega en puerto", "Ingreso al terminal dentro del stacking: STI, Puerto Central, TPS, Lirquén, Coronel y otros."),
                    new ItemServicio("bi-geo-alt", "Seguimiento del camión", "Nuestro equipo sigue el recorrido con depósito, plantas y puerto marcados en el mapa."),
                    new ItemServicio("bi-lightning-charge", "Genset para reefer", "Equipo generador para mantener la temperatura del contenedor refrigerado durante el traslado."),
                },
                new[]
                {
                    new BloqueServicio("bi-buildings", "Coordinación con tu planta",
                        "Acordamos contigo la ventana de carga y la informamos al transportista. Si el embarque necesita cargar en más de una planta, armamos el recorrido completo en un solo viaje.",
                        new[] { "Ventana de carga acordada con la planta", "Una o varias plantas por contenedor", "Plantas y depósitos registrados con su ubicación exacta", "Conductor y patente informados antes del retiro" }),
                    new BloqueServicio("bi-stopwatch", "Stacking y late arrival bajo control",
                        "Cada terminal abre y cierra el stacking de la nave en horarios definidos. Planificamos el viaje hacia atrás desde ese cierre, para que el contenedor entre a tiempo y evites el recargo por late arrival.",
                        new[] { "Fechas de stacking de cada nave a la vista", "Alertas cuando el plazo se acerca", "Gestión de late arrival cuando es inevitable", "Registro del ingreso del contenedor al terminal" }),
                    new BloqueServicio("bi-arrow-left-right", "También en importación",
                        "Retiramos tu contenedor en el puerto de llegada, lo llevamos a tu bodega y devolvemos el vacío al depósito dentro de los días libres.",
                        new[] { "Retiro en terminal portuario", "Entrega en tu bodega o planta", "Devolución del vacío dentro de plazo", "Control de días libres de detention" }),
                },
                new[]
                {
                    new PasoServicio("Programación", "Acordamos fecha y hora de carga con tu planta."),
                    new PasoServicio("Retiro del vacío", "El camión retira el contenedor en el depósito de la naviera."),
                    new PasoServicio("Carga en planta", "Tu equipo carga el contenedor; registramos sello y peso."),
                    new PasoServicio("Entrega en puerto", "El contenedor ingresa al terminal dentro del stacking."),
                },
                new[] { "Transportista, conductor y patente", "Contenedor y sello de cada booking", "Fechas de retiro, carga e ingreso a puerto", "Costos del transporte" }),

            new(
                "documental", "Servicio documental", "bi-file-earmark-text",
                "Matriz y emisión del BL, certificados y envío de originales, sin errores ni atrasos.",
                "Los papeles del embarque, a tiempo",
                "Un BL correcto, a la primera.",
                "Preparamos la matriz del BL, coordinamos los certificados y te enviamos los documentos originales donde los necesites.",
                new[]
                {
                    new DatoFicha("BL", "Original, seawaybill o telex release"),
                    new DatoFicha("Matriz", "Presentada antes del cut-off documental"),
                    new DatoFicha("Certificados", "Fitosanitario, de origen y sanitario"),
                    new DatoFicha("Correcciones", "Gestionadas con la naviera"),
                    new DatoFicha("Originales", "Envío por courier"),
                },
                new[]
                {
                    "Un error en el BL puede retener la carga en destino o costar una corrección con la naviera. Revisamos cada dato antes de presentarlo y te pedimos aprobar el borrador antes de la emisión final.",
                    "Coordinamos también los certificados que exige cada mercado y el envío de los originales, para que tu comprador reciba la carga sin trabas.",
                },
                new[]
                {
                    new ItemServicio("bi-file-earmark-ruled", "Matriz de BL", "Preparamos la instrucción de embarque con los datos de embarcador, consignatario, notify y carga."),
                    new ItemServicio("bi-file-earmark-check", "Aprobación del borrador", "Revisas el borrador del BL desde el portal y lo apruebas o pides cambios antes de la emisión."),
                    new ItemServicio("bi-file-earmark-richtext", "Emisión del BL", "Original, seawaybill o telex release, según lo que necesite tu comprador o tu banco."),
                    new ItemServicio("bi-patch-check", "Certificados", "Fitosanitario (SAG), certificado de origen para acceder a tratados de libre comercio y sanitario."),
                    new ItemServicio("bi-pencil-square", "Correcciones de BL", "Si algo cambia después de la emisión, gestionamos la corrección con la naviera."),
                    new ItemServicio("bi-send", "Courier de originales", "Enviamos los documentos originales a destino y te informamos el número de seguimiento."),
                },
                new[]
                {
                    new BloqueServicio("bi-file-earmark-diff", "Del borrador al BL final",
                        "Con los datos de la reserva preparamos la matriz y la presentamos a la naviera antes del cut-off documental. Cuando llega el borrador, lo publicamos en el portal para que lo revises y apruebes.",
                        new[] { "Matriz presentada antes del cut-off", "Borrador del BL disponible en el portal", "Aprobación o cambios con un clic", "Emisión del BL final" }),
                    new BloqueServicio("bi-globe2", "Certificados para cada mercado",
                        "Cada destino pide documentos distintos. Coordinamos el certificado fitosanitario con el SAG, el certificado de origen para aplicar las preferencias arancelarias de los tratados y los certificados sanitarios.",
                        new[] { "Fitosanitario (SAG)", "Certificado de origen para TLC", "Certificado sanitario", "Otros según el mercado de destino" }),
                    new BloqueServicio("bi-folder2-open", "Documentos siempre a mano",
                        "BL, certificados y facturas quedan guardados en cada operación del portal, listos para descargar cuando los pida tu cliente, tu banco o tu auditor.",
                        new[] { "Documentos ordenados por embarque", "Descarga en cualquier momento", "Historial de correcciones", "Seguimiento del courier" }),
                },
                new[]
                {
                    new PasoServicio("Datos del embarque", "Reunimos la información de embarcador, consignatario y carga."),
                    new PasoServicio("Matriz", "Presentamos la instrucción a la naviera antes del cut-off."),
                    new PasoServicio("Borrador", "Publicamos el borrador del BL en el portal."),
                    new PasoServicio("Aprobación", "Lo revisas y apruebas, o nos pides cambios."),
                    new PasoServicio("Emisión y envío", "Emitimos el BL y enviamos los originales."),
                },
                new[] { "Borrador del BL para aprobar", "BL emitido y certificados", "Estado de la matriz y del cut-off", "Seguimiento del courier" }),

            new(
                "agencia-de-aduanas", "Agencia de aduanas", "bi-shield-check",
                "DUS de exportación, DIN de importación, SAG y seguro de carga, coordinados con tu embarque.",
                "Despacho aduanero coordinado",
                "Aduana resuelta antes del zarpe.",
                "Gestionamos tu DUS de exportación o tu DIN de importación junto a agentes de aduana habilitados, alineados con la nave y los plazos del embarque.",
                new[]
                {
                    new DatoFicha("Exportación", "DUS y su legalización"),
                    new DatoFicha("Importación", "DIN y retiro de la carga"),
                    new DatoFicha("SAG", "Inspección cuando corresponde"),
                    new DatoFicha("Tratados", "Certificado de origen para TLC"),
                    new DatoFicha("Seguro", "Póliza para tu carga"),
                },
                new[]
                {
                    "El despacho aduanero tiene que calzar con la nave: si el DUS no está legalizado a tiempo, el contenedor no embarca. Coordinamos la agencia de aduana, la naviera y el transporte desde un mismo equipo.",
                    "Trabajamos con agentes de aduana habilitados y también con la agencia que tú prefieras. En ambos casos, nosotros nos encargamos de que los plazos y los datos coincidan.",
                },
                new[]
                {
                    new ItemServicio("bi-box-arrow-up", "DUS de exportación", "Preparación, presentación y legalización del Documento Único de Salida."),
                    new ItemServicio("bi-box-arrow-in-down", "DIN de importación", "Declaración de Ingreso y pago de derechos para retirar tu carga del puerto."),
                    new ItemServicio("bi-search", "Inspección SAG", "Coordinamos la inspección del Servicio Agrícola y Ganadero cuando el producto lo exige."),
                    new ItemServicio("bi-award", "Certificado de origen", "Para que tu comprador acceda a las rebajas arancelarias de los tratados de libre comercio."),
                    new ItemServicio("bi-check2-square", "Datos consistentes", "Factura, peso, bultos y valores revisados para que coincidan con el DUS y el BL."),
                    new ItemServicio("bi-umbrella", "Seguro de carga", "Gestionamos una póliza que protege tu mercancía durante el traslado."),
                },
                new[]
                {
                    new BloqueServicio("bi-box-arrow-up", "Exportar sin trabas",
                        "Con la factura y el packing list preparamos el DUS, coordinamos las inspecciones que correspondan y lo legalizamos antes del embarque.",
                        new[] { "DUS preparado con los datos de la reserva", "Inspección SAG cuando aplica", "Legalización antes del zarpe", "Certificado de origen para TLC" }),
                    new BloqueServicio("bi-box-arrow-in-down", "Importar con respaldo",
                        "Revisamos los antecedentes antes de que la carga llegue, presentamos la DIN y coordinamos el retiro del contenedor apenas se liberan los derechos.",
                        new[] { "Revisión anticipada de documentos", "DIN y pago de derechos", "Retiro coordinado con el transporte", "Devolución del vacío dentro de plazo" }),
                    new BloqueServicio("bi-people", "Una sola coordinación",
                        "La agencia de aduana, la naviera y el transportista hablan con un mismo equipo. Tú recibes un solo seguimiento y los costos de aduana en el mismo detalle del embarque.",
                        new[] { "Un ejecutivo para todo el embarque", "Costos de aduana detallados por operación", "Documentos en el portal", "Con nuestra red de agencias o la tuya" }),
                },
                new[]
                {
                    new PasoServicio("Antecedentes", "Recibimos factura, packing list y datos de la carga."),
                    new PasoServicio("Declaración", "La agencia prepara el DUS o la DIN."),
                    new PasoServicio("Inspección", "Coordinamos SAG u otras inspecciones si corresponden."),
                    new PasoServicio("Legalización", "Queda aprobada la declaración y la carga puede embarcar o salir del puerto."),
                },
                new[] { "DUS o DIN de cada embarque", "Certificados de origen y fitosanitarios", "Costos de la agencia de aduana", "Estado de la documentación" }),

            new(
                SlugPortal, "Portal de clientes", "bi-display",
                "Seguimiento, documentos, costos e itinerarios de tus embarques, las 24 horas.",
                "Plataforma propia de Atilson",
                "Tu logística, en tiempo real.",
                "Una plataforma propia para seguir cada embarque, revisar documentos y costos, y pedir nuevos servicios sin llamadas ni planillas.",
                new[]
                {
                    new DatoFicha("Disponible", "24/7, desde el computador o el celular"),
                    new DatoFicha("Seguimiento", "Hitos de cada contenedor"),
                    new DatoFicha("Documentos", "BL, certificados y facturas"),
                    new DatoFicha("Solicitudes", "Cotizaciones y reservas en línea"),
                    new DatoFicha("Soporte", "Conversación directa con el equipo"),
                },
                new[]
                {
                    "El portal muestra la misma información con la que trabaja nuestro equipo de Operaciones y Finanzas. No hay reportes que esperar ni planillas desactualizadas: lo que cambia en la operación, cambia en tu pantalla.",
                    "Lo desarrollamos nosotros, para nuestros clientes. Por eso podemos mejorarlo con lo que nos piden.",
                },
                new[]
                {
                    new ItemServicio("bi-geo", "Seguimiento de embarques", "Hitos de cada contenedor: reserva, retiro, stacking, zarpe y arribo."),
                    new ItemServicio("bi-calendar2-week", "Itinerarios y reservas", "Elige una salida publicada y solicita tu servicio con esa nave."),
                    new ItemServicio("bi-calculator", "Cotizaciones", "Pide una cotización y revisa sus respuestas en un solo lugar."),
                    new ItemServicio("bi-folder2-open", "Documentos", "BL, certificados y facturas de cada embarque, listos para descargar."),
                    new ItemServicio("bi-file-earmark-check", "Aprobación de BL", "Revisa el borrador del BL y apruébalo o pide cambios."),
                    new ItemServicio("bi-receipt", "Facturas", "Facturas y estado de pago de cada operación."),
                    new ItemServicio("bi-pin-map", "Mis ubicaciones", "Registra tus plantas y depósitos para programar el transporte."),
                    new ItemServicio("bi-headset", "Soporte en línea", "Escribe al equipo desde el portal y sigue la conversación ahí mismo."),
                },
                new[]
                {
                    new BloqueServicio("bi-eye", "Visibilidad sin intermediarios",
                        "Ves el estado de tu carga apenas Operaciones lo registra. Si la naviera cambia una fecha, lo ves en el portal sin tener que preguntar.",
                        new[] { "Fechas de zarpe y arribo actualizadas", "Nave y viaje de cada booking", "Contenedores y sellos", "Conductor y patente del transporte" }),
                    new BloqueServicio("bi-receipt-cutoff", "Costos claros",
                        "Cada embarque muestra sus costos y facturas, con el tipo de cambio usado cuando corresponde. Sin sorpresas a fin de mes.",
                        new[] { "Detalle por concepto", "Montos en pesos y en dólares", "Estado de cada factura", "Historial de embarques" }),
                    new BloqueServicio("bi-lightning", "Pide servicios en minutos",
                        "Solicita una cotización o reserva una salida de los itinerarios publicados. Te respondemos en el mismo portal.",
                        new[] { "Cotizaciones en línea", "Reserva sobre itinerarios publicados", "Seguimiento de cada solicitud", "Conversación con el equipo" }),
                },
                new[]
                {
                    new PasoServicio("Registra tu empresa", "Completa el formulario con los datos de tu empresa."),
                    new PasoServicio("Validamos tu cuenta", "Nuestro equipo revisa los datos y activa tu acceso."),
                    new PasoServicio("Ingresa al portal", "Entra con tu correo y contraseña y sigue tus embarques."),
                },
                new string[0]),
        };
    }
}
