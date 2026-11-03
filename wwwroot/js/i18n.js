/* Idioma del sitio público: español / inglés (nov-2026).
   El sitio se escribe en español. Al elegir inglés (botón con la bandera, [data-idioma]) se
   reemplaza cada texto visible por su traducción de wwwroot/js/i18n/en.json, y los textos
   que cambian después (avisos, carruseles, chat) se traducen al aparecer. Al volver a
   español se restauran los originales. La elección se recuerda en este navegador.

   Para traducir un texto nuevo: agregarlo a en.json › "textos" con el texto en español
   exactamente como se ve (los espacios repetidos no importan). Los textos con datos
   (fechas, días, números) se traducen con las reglas de abajo.
   Para que algo no se traduzca: atributo data-no-traducir en el elemento. */
(function () {
    'use strict';
    var CLAVE = 'atilson-idioma';
    var ATRIBUTOS = ['placeholder', 'title', 'aria-label', 'alt', 'data-escribe'];
    var html = document.documentElement;
    var dic = null, idioma = 'es', obs = null;
    var nodos = new Set();          // nodos de texto traducidos (para volver a español)
    var orig = new WeakMap();       // nodo -> { es, en }
    var atrs = new Map();           // elemento -> { atributo: { es, en } }

    function leer() { try { return localStorage.getItem(CLAVE) === 'en' ? 'en' : 'es'; } catch (e) { return 'es'; } }
    function recordar(v) { try { localStorage.setItem(CLAVE, v); } catch (e) { } }
    function norm(s) { return (s || '').replace(/\s+/g, ' ').trim(); }

    // ---------- Reglas para textos con datos ----------
    function mes(m) { var k = (m || '').toLowerCase(); return (dic.meses[k] || m); }
    function dia(d) { return dic.dias[d] || dic.dias[(d || '').toLowerCase()] || d; }
    function dias(n) { return n + (n === '1' ? ' day' : ' days'); }
    var REGLAS = [
        [/^Etapa (\d+) de (\d+)$/, function (m) { return 'Step ' + m[1] + ' of ' + m[2]; }],
        [/^(\d+) días?$/, function (m) { return dias(m[1]); }],
        [/^Zarpa en (\d+) días?$/, function (m) { return 'Sails in ' + dias(m[1]); }],
        [/^Zarpa hoy$/, function () { return 'Sails today'; }],
        [/^Zarpa mañana$/, function () { return 'Sails tomorrow'; }],
        [/^(\d+) salidas? disponibles?$/, function (m) { return m[1] + (m[1] === '1' ? ' sailing available' : ' sailings available'); }],
        [/^Corte doc\. (\d+) (\S+), (\d\d:\d\d)$/, function (m) { return 'Doc. cut-off ' + mes(m[2]) + ' ' + m[1] + ', ' + m[3]; }],
        [/^(lun|mar|mié|jue|vie|sáb|dom) (\d+) (\S+)$/i, function (m) { return dia(m[1]) + ' ' + m[2] + ' ' + mes(m[3]); }],
        [/^Semana (\d+) · (\d+) al (\d+) de (\S+)$/, function (m) { return 'Week ' + m[1] + ' · ' + mes(m[4]) + ' ' + m[2] + '–' + m[3]; }],
        [/^Semana (\d+) · (\d+) de (\S+) al (\d+) de (\S+)$/, function (m) { return 'Week ' + m[1] + ' · ' + mes(m[3]) + ' ' + m[2] + ' – ' + mes(m[5]) + ' ' + m[4]; }],
        [/^(.+) → (.+) · (\d+) días$/, function (m) { return m[1] + ' → ' + m[2] + ' · ' + dias(m[3]); }],
        [/^Valores del día \((.+)\) · Fuente: (.+)$/, function (m) { return "Today's rates (" + m[1] + ') · Source: ' + m[2]; }],
        [/^Valores del día \((.+)\)$/, function (m) { return "Today's rates (" + m[1] + ')'; }],
        [/^Último valor disponible \(sin conexión con la fuente\)(.*)$/, function (m) { return 'Latest available value (source offline)' + m[1]; }],
        [/^· (\d+) de (\S+), (\d{4})$/, function (m) { return '· ' + mes(m[2]) + ' ' + m[1] + ', ' + m[3]; }],
        [/^(\d+) de (\S+), (\d{4})$/, function (m) { return mes(m[2]) + ' ' + m[1] + ', ' + m[3]; }],
        [/^Vía (.+)$/, function (m) { return 'Via ' + m[1]; }],
        [/^Ya recibimos varios mensajes desde tu conexión\. Escríbenos directo a (\S+)\.$/, function (m) { return "We've already received several messages from your connection. Email us directly at " + m[1] + '.'; }],
        [/^No pudimos enviar tu mensaje desde aquí\. Escríbenos a (\S+) y te respondemos a la brevedad\.$/, function (m) { return "We couldn't send your message from here. Email us at " + m[1] + " and we'll reply shortly."; }],
        [/^Portal: (.+)$/, function (m) { return 'Portal: ' + (traducir(m[1]) || m[1]); }],
        [/^(.+), nave (.+)\. Zarpa de (.+) el (\d+) de (\S+) y llega a (.+) el (\d+) de (\S+)\. (\d+) días, (.+)\.$/, function (m) {
            var esc = m[10] === 'directo' ? 'direct' : m[10].replace(/^vía /, 'via ');
            return m[1] + ', vessel ' + m[2] + '. Departs ' + m[3] + ' on ' + mes(m[5]) + ' ' + m[4] + ' and arrives in ' + m[6] + ' on ' + mes(m[8]) + ' ' + m[7] + '. ' + dias(m[9]) + ', ' + esc + '.';
        }],
        [/^(.+), ([A-ZÁÉÍÓÚÑ ]{3,})$/, function (m) { return dic.paises[m[2]] ? m[1] + ', ' + dic.paises[m[2]] : null; }]
    ];

    function traducir(texto) {
        var k = norm(texto);
        if (!k || !dic) return null;
        if (Object.prototype.hasOwnProperty.call(dic.textos, k)) return dic.textos[k];
        for (var i = 0; i < REGLAS.length; i++) {
            var m = k.match(REGLAS[i][0]);
            if (m) { var r = REGLAS[i][1](m); if (r) return r; }
        }
        return null;
    }

    function omitir(el) {
        return !el || !!el.closest('script, style, noscript, textarea, code, [data-no-traducir], .asis-msg.yo');
    }

    // ---------- Aplicar / revertir ----------
    function textoEn(n) {
        if (omitir(n.parentElement)) return;
        var r = orig.get(n), actual = n.nodeValue;
        if (r && actual === r.en) return;                 // ya traducido por nosotros
        var es = actual;                                    // texto nuevo (o cambiado por la página)
        var en = traducir(es);
        if (en == null) { if (r) { orig.delete(n); nodos.delete(n); } return; }
        var ini = es.match(/^\s*/)[0], fin = es.match(/\s*$/)[0];
        var valor = ini + en + fin;
        orig.set(n, { es: es, en: valor }); nodos.add(n);
        if (actual !== valor) n.nodeValue = valor;
    }
    function atributosEn(el) {
        // Los atributos (placeholder, title…) sí se traducen también en campos de texto.
        if (!el || el.closest('script, style, noscript, [data-no-traducir], .asis-msg.yo')) return;
        ATRIBUTOS.forEach(function (a) {
            if (!el.hasAttribute(a)) return;
            var actual = el.getAttribute(a), mapa = atrs.get(el) || {}, r = mapa[a];
            if (r && actual === r.en) return;
            var en = traducir(actual);
            if (en == null) return;
            mapa[a] = { es: actual, en: en }; atrs.set(el, mapa);
            el.setAttribute(a, en);
        });
    }
    function recorrer(raiz) {
        if (!raiz) return;
        if (raiz.nodeType === 3) { textoEn(raiz); return; }
        if (raiz.nodeType !== 1) return;
        atributosEn(raiz);
        var w = document.createTreeWalker(raiz, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT, null), n;
        while ((n = w.nextNode())) { if (n.nodeType === 3) textoEn(n); else atributosEn(n); }
    }
    function observar(si) {
        if (obs) { obs.disconnect(); obs = null; }
        if (!si || !('MutationObserver' in window)) return;
        obs = new MutationObserver(function (lista) {
            lista.forEach(function (m) {
                if (m.type === 'characterData') textoEn(m.target);
                else if (m.type === 'attributes') atributosEn(m.target);
                else m.addedNodes.forEach(recorrer);
            });
        });
        obs.observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ATRIBUTOS });
    }
    function aEspanol() {
        observar(false);
        nodos.forEach(function (n) { var r = orig.get(n); if (r && n.nodeValue === r.en) n.nodeValue = r.es; });
        nodos.clear(); orig = new WeakMap();
        atrs.forEach(function (mapa, el) { Object.keys(mapa).forEach(function (a) { if (el.getAttribute(a) === mapa[a].en) el.setAttribute(a, mapa[a].es); }); });
        atrs.clear();
        if (document.__tituloEs) document.title = document.__tituloEs;
    }
    function aIngles() {
        recorrer(document.body);
        document.__tituloEs = document.__tituloEs || document.title;
        var t = traducir(document.__tituloEs); if (t) document.title = t;
        observar(true);
    }

    // ---------- Botón ----------
    function pintarBotones() {
        document.querySelectorAll('[data-idioma]').forEach(function (b) {
            var en = idioma === 'en';
            b.setAttribute('data-actual', idioma);
            b.setAttribute('aria-label', en ? 'Ver en español' : 'View in English');
            b.setAttribute('title', en ? 'Ver en español' : 'View in English');
            var c = b.querySelector('.idioma-cod'); if (c) c.textContent = en ? 'ES' : 'EN';
        });
    }
    function cargar() {
        if (dic) return Promise.resolve(dic);
        var url = (document.querySelector('meta[name="i18n-en"]') || {}).content || '/js/i18n/en.json';
        return fetch(url).then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); }).then(function (d) { dic = d; return d; });
    }
    function poner(nuevo, guardar) {
        idioma = nuevo;
        if (guardar) recordar(nuevo);
        html.lang = nuevo;
        var asis = document.getElementById('asisIdioma'); if (asis) asis.value = nuevo;
        if (nuevo === 'en') {
            return cargar().then(function () { if (idioma === 'en') aIngles(); pintarBotones(); })
                .catch(function () { idioma = 'es'; html.lang = 'es'; pintarBotones(); })
                .finally(function () { html.classList.remove('trad-pend'); });
        }
        aEspanol(); pintarBotones(); html.classList.remove('trad-pend');
        return Promise.resolve();
    }

    document.addEventListener('click', function (e) {
        var b = e.target.closest && e.target.closest('[data-idioma]');
        if (!b) return;
        e.preventDefault();
        poner(idioma === 'en' ? 'es' : 'en', true);
    });

    window.AtilsonIdioma = { actual: function () { return idioma; }, traducir: function (t) { return idioma === 'en' ? (traducir(t) || t) : t; } };
    pintarBotones();
    if (leer() === 'en') poner('en', false); else html.classList.remove('trad-pend');
})();
