// Chat del equipo (nov-2026): ERP › Chat del equipo (Pages/App/Chat.cshtml).
// La conversación abierta pregunta por mensajes nuevos cada 3 s y la lista cada 8 s,
// solo mientras la pestaña está visible. Todo el texto se inserta como texto (nunca HTML).
(function () {
    'use strict';
    const nodoDatos = document.getElementById('chatDatos');
    if (!nodoDatos) return;
    const datos = JSON.parse(nodoDatos.textContent);
    const YO = datos.yo;
    const EQUIPO = datos.equipo || [];
    const ISOTIPO = '/img/marca/atilson-isotipo-blanco.svg';
    const token = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';
    const esMovil = () => window.matchMedia('(max-width: 900px)').matches;
    const $ = id => document.getElementById(id);

    const app = $('chatApp'), items = $('chatItems'), filtro = $('chatFiltro');
    const panelPersonas = $('chatPersonas'), listaPersonas = $('chatPersonasLista'), btnNuevo = $('chatNuevo');
    const vacio = $('chatVacio'), cab = $('chatCab'), cabAv = $('chatCabAv'), cabNombre = $('chatCabNombre'), cabSub = $('chatCabSub');
    const zona = $('chatMensajes'), form = $('chatForm'), caja = $('chatTexto'), btnEnviar = $('chatEnviar'), ayuda = $('chatAyuda');

    let convs = datos.conversaciones || [];
    let actual = null;          // conversación abierta (resumen)
    let mensajes = [];          // mensajes cargados de la conversación abierta
    let leidoPorOtro = 0, hayMas = false, cargando = false, tempId = -1;
    let leidoEnviado = 0;

    // ------------------------------------------------------------ utilidades
    function el(tag, clase, texto) {
        const e = document.createElement(tag);
        if (clase) e.className = clase;
        if (texto != null) e.textContent = texto;
        return e;
    }
    const fecha = s => new Date(s);
    const mismoDia = (a, b) => a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
    const hora = d => d.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit', hour12: false });
    function fechaLista(s) {
        if (!s) return '';
        const d = fecha(s), hoy = new Date(), ayer = new Date(); ayer.setDate(hoy.getDate() - 1);
        if (mismoDia(d, hoy)) return hora(d);
        if (mismoDia(d, ayer)) return 'Ayer';
        if ((hoy - d) < 6 * 864e5) return d.toLocaleDateString('es-CL', { weekday: 'short' }).replace('.', '');
        return d.toLocaleDateString('es-CL', { day: '2-digit', month: '2-digit', year: '2-digit' });
    }
    function fechaDia(d) {
        const hoy = new Date(), ayer = new Date(); ayer.setDate(hoy.getDate() - 1);
        if (mismoDia(d, hoy)) return 'Hoy';
        if (mismoDia(d, ayer)) return 'Ayer';
        const t = d.toLocaleDateString('es-CL', { weekday: 'long', day: 'numeric', month: 'long', year: d.getFullYear() === hoy.getFullYear() ? undefined : 'numeric' });
        return t.charAt(0).toUpperCase() + t.slice(1);
    }
    function sinTildes(s) { return (s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''); }

    function avatar(foto, iniciales, grupo, chico) {
        const a = el('div', 'ch-av' + (grupo ? ' grupo' : '') + (chico ? ' chico' : ''));
        a.setAttribute('aria-hidden', 'true');
        if (grupo) { const i = el('img'); i.src = ISOTIPO; i.alt = ''; a.appendChild(i); }
        else if (foto) { const i = el('img'); i.src = foto; i.alt = ''; i.loading = 'lazy'; a.appendChild(i); }
        else a.textContent = iniciales || '?';
        return a;
    }

    // Texto con enlaces clicables (solo http/https), sin interpretar HTML.
    function textoConEnlaces(nodo, texto) {
        const re = /\bhttps?:\/\/[^\s<>"']+/gi;
        let ultimo = 0, m;
        while ((m = re.exec(texto))) {
            let url = m[0].replace(/[).,;:!?]+$/, '');
            if (m.index > ultimo) nodo.appendChild(document.createTextNode(texto.slice(ultimo, m.index)));
            const a = el('a', null, url); a.href = url; a.target = '_blank'; a.rel = 'noopener noreferrer';
            nodo.appendChild(a);
            ultimo = m.index + url.length;
            re.lastIndex = ultimo;
        }
        if (ultimo < texto.length) nodo.appendChild(document.createTextNode(texto.slice(ultimo)));
    }

    async function pedir(url, opciones) {
        const r = await fetch(url, Object.assign({ credentials: 'same-origin', headers: { 'Accept': 'application/json' } }, opciones || {}));
        if (!r.ok) {
            let error = 'No se pudo completar la acción. Intenta de nuevo.';
            try { const j = await r.json(); if (j && j.error) error = j.error; } catch (e) { /* sin cuerpo */ }
            if (r.status === 401 || r.redirected) error = 'Tu sesión terminó. Recarga la página para seguir.';
            throw new Error(error);
        }
        return r.json();
    }
    function enviarForm(handler, campos) {
        const fd = new FormData();
        Object.keys(campos).forEach(k => fd.append(k, campos[k]));
        return pedir('/App/Chat?handler=' + handler, { method: 'POST', body: fd, headers: { 'RequestVerificationToken': token, 'Accept': 'application/json' } });
    }

    // Contador del menú lateral y del título de la pestaña.
    const tituloBase = document.title.replace(/^\(\d+\)\s*/, '');
    function actualizarTotal() {
        const total = convs.reduce((s, c) => s + (c.noLeidos || 0), 0);
        const badge = document.getElementById('navChatBadge');
        if (badge) { badge.textContent = total > 99 ? '99+' : total; badge.hidden = total === 0; }
        document.title = (total > 0 ? '(' + total + ') ' : '') + tituloBase;
    }

    // ------------------------------------------------------------ lista
    function pintarLista() {
        const q = sinTildes(filtro.value.trim());
        items.textContent = '';
        const visibles = convs.filter(c => !q || sinTildes(c.titulo).includes(q));
        visibles.forEach(c => {
            const b = el('button', 'ch-item' + (actual && actual.id === c.id ? ' activo' : '') + (c.noLeidos ? ' sin-leer' : ''));
            b.type = 'button'; b.setAttribute('role', 'listitem'); b.dataset.id = c.id;
            b.appendChild(avatar(c.fotoUrl, c.iniciales, c.tipo === 'GENERAL'));
            b.appendChild(el('b', null, c.titulo));
            b.appendChild(el('time', null, fechaLista(c.ultimaFecha)));
            let prev = c.ultimoTexto ? (c.tipo === 'GENERAL' || c.ultimoMio ? c.ultimoAutor + ': ' : '') + c.ultimoTexto.split('\n')[0] : (c.tipo === 'GENERAL' ? 'Grupo de todo el equipo' : 'Sin mensajes todavía');
            b.appendChild(el('span', 'prev', prev));
            if (c.noLeidos) {
                const n = el('span', 'ch-n', c.noLeidos > 99 ? '99+' : String(c.noLeidos));
                n.setAttribute('aria-label', c.noLeidos + ' sin leer');
                b.appendChild(n);
            } else b.appendChild(el('span'));
            b.addEventListener('click', () => abrir(c.id));
            items.appendChild(b);
        });
        // Al buscar, también aparecen las personas con las que aún no hay conversación.
        if (q) {
            const conConv = new Set(convs.filter(c => c.idOtro).map(c => c.idOtro));
            const personas = EQUIPO.filter(p => !conConv.has(p.id) && sinTildes(p.nombre + ' ' + (p.cargo || '')).includes(q));
            personas.forEach(p => items.appendChild(botonPersona(p)));
            if (!visibles.length && !personas.length) items.appendChild(el('div', 'ch-sin-resultados', 'Nadie coincide con la búsqueda.'));
        }
        actualizarTotal();
    }

    function botonPersona(p) {
        const b = el('button', 'ch-persona');
        b.type = 'button'; b.setAttribute('role', 'listitem');
        b.appendChild(avatar(p.fotoUrl, p.iniciales));
        b.appendChild(el('b', null, p.nombre));
        b.appendChild(el('small', null, p.cargo || p.correo));
        b.addEventListener('click', () => conversarCon(p.id));
        return b;
    }

    function pintarPersonas() {
        listaPersonas.textContent = '';
        if (!EQUIPO.length) { listaPersonas.appendChild(el('div', 'ch-sin-resultados', 'Aún no hay más personas en el equipo.')); return; }
        EQUIPO.forEach(p => listaPersonas.appendChild(botonPersona(p)));
    }

    function mostrarPersonas(ver) {
        panelPersonas.hidden = !ver;
        btnNuevo.setAttribute('aria-expanded', ver ? 'true' : 'false');
        if (ver) { pintarPersonas(); const b = listaPersonas.querySelector('button'); if (b) b.focus(); }
    }

    async function conversarCon(idUsuario) {
        try {
            const r = await enviarForm('Abrir', { idUsuario });
            mostrarPersonas(false);
            filtro.value = '';
            await refrescarLista(r.id);
            abrir(r.id);
        } catch (e) { alertaBreve(e.message); }
    }

    async function refrescarLista(incluir) {
        try {
            const inc = incluir || (actual ? actual.id : '');
            convs = await pedir('/App/Chat?handler=Conversaciones' + (inc ? '&incluir=' + inc : ''));
            if (actual) {
                const a = convs.find(c => c.id === actual.id);
                if (a) { actual = a; if (document.hasFocus() && cercaDelFinal()) a.noLeidos = 0; }
            }
            pintarLista();
        } catch (e) { /* se reintenta en la próxima vuelta */ }
    }

    // ------------------------------------------------------------ conversación
    async function abrir(id) {
        const c = convs.find(x => x.id === id);
        if (!c) return;
        actual = c; mensajes = []; leidoPorOtro = 0; hayMas = false; leidoEnviado = 0;
        app.classList.add('con-hilo');
        vacio.hidden = true; cab.hidden = false; zona.hidden = false; form.hidden = false; ayuda.hidden = false;
        const av = avatar(c.fotoUrl, c.iniciales, c.tipo === 'GENERAL');
        cabAv.className = av.className;
        cabAv.textContent = '';
        while (av.firstChild) cabAv.appendChild(av.firstChild);
        cabNombre.textContent = c.titulo;
        cabSub.textContent = '';
        if (c.tipo === 'GENERAL') cabSub.textContent = 'Todo el equipo, ' + (c.subtitulo || '');
        else {
            const candado = el('i', 'bi bi-lock-fill'); candado.setAttribute('aria-hidden', 'true');
            cabSub.appendChild(candado);
            cabSub.appendChild(document.createTextNode((c.subtitulo ? c.subtitulo + '. ' : '') + 'Conversación privada'));
        }
        zona.textContent = '';
        zona.appendChild(el('div', 'ch-inicio', 'Cargando mensajes…'));
        caja.placeholder = c.tipo === 'GENERAL' ? 'Escribe al equipo' : 'Escribe a ' + c.titulo.split(' ')[0];
        pintarLista();
        try { history.replaceState(null, '', '/App/Chat?c=' + id); } catch (e) { /* sin historial */ }
        try {
            const r = await pedir('/App/Chat?handler=Mensajes&id=' + id);
            if (!actual || actual.id !== id) return;
            mensajes = r.mensajes; leidoPorOtro = r.leidoPorOtro; hayMas = r.hayMas;
            pintarMensajes(true);
            marcarLeido();
        } catch (e) {
            zona.textContent = '';
            zona.appendChild(el('div', 'ch-inicio', e.message));
        }
        if (!esMovil()) caja.focus();
    }

    function cercaDelFinal() { return zona.scrollHeight - zona.scrollTop - zona.clientHeight < 80; }

    function pintarMensajes(alFinal) {
        const estabaAbajo = alFinal || cercaDelFinal();
        zona.textContent = '';
        if (hayMas) {
            const mas = el('button', 'ch-mas', 'Ver mensajes anteriores');
            mas.type = 'button';
            mas.addEventListener('click', cargarAnteriores);
            zona.appendChild(mas);
        }
        if (!mensajes.length) {
            zona.appendChild(el('div', 'ch-inicio', actual.tipo === 'GENERAL'
                ? 'Todavía no hay mensajes en el grupo. Lo que escribas aquí lo ve todo el equipo.'
                : 'Esta es una conversación privada entre tú y ' + actual.titulo + '. Escribe el primer mensaje.'));
        }
        let anterior = null;
        mensajes.forEach((m, i) => {
            const d = fecha(m.fecha);
            const siguiente = mensajes[i + 1];
            if (!anterior || !mismoDia(fecha(anterior.fecha), d)) zona.appendChild(el('div', 'ch-dia', fechaDia(d)));
            const continua = a => a && a.idUsuario === m.idUsuario && mismoDia(fecha(a.fecha), d) && Math.abs(fecha(a.fecha) - d) < 5 * 60e3;
            const primero = !continua(anterior), ultimo = !continua(siguiente);
            const fila = el('div', 'ch-msg ' + (m.mio ? 'mio' : 'otro') + (primero ? ' primero' : '') + (ultimo ? ' ultimo' : '') + (m.estado ? ' ' + m.estado : ''));
            fila.dataset.id = m.id;
            if (!m.mio) fila.appendChild(avatar(m.fotoUrl, m.iniciales, false, true));
            const cuerpo = el('div', 'cuerpo');
            if (!m.mio && primero && actual.tipo === 'GENERAL') cuerpo.appendChild(el('span', 'autor', m.autor));
            const burbuja = el('div', 'burbuja');
            textoConEnlaces(burbuja, m.texto);
            cuerpo.appendChild(burbuja);
            if (ultimo || m.estado) {
                let pie = m.estado === 'enviando' ? 'Enviando…' : m.estado === 'error' ? (m.error || 'No se envió') + '. Toca para reintentar.' : hora(d);
                const t = el('span', 'hora', pie);
                if (m.estado !== 'enviando' && m.estado !== 'error') { t.title = d.toLocaleString('es-CL'); }
                cuerpo.appendChild(t);
            }
            if (m.estado === 'error') { fila.style.cursor = 'pointer'; fila.addEventListener('click', () => reintentar(m)); }
            fila.appendChild(cuerpo);
            zona.appendChild(fila);
            anterior = m;
        });
        // "Visto" bajo mi último mensaje, si la otra persona ya lo leyó (solo privadas).
        if (actual.tipo !== 'GENERAL') {
            const misMensajes = mensajes.filter(m => m.mio && m.id > 0);
            const ultimoMio = misMensajes[misMensajes.length - 1];
            const ultimoTotal = mensajes[mensajes.length - 1];
            if (ultimoMio && ultimoTotal === ultimoMio && leidoPorOtro >= ultimoMio.id) zona.appendChild(el('div', 'ch-visto', 'Visto'));
        }
        if (estabaAbajo) zona.scrollTop = zona.scrollHeight;
    }

    async function cargarAnteriores() {
        if (cargando || !mensajes.length) return;
        cargando = true;
        const altoAntes = zona.scrollHeight;
        try {
            const primero = mensajes.find(m => m.id > 0);
            const r = await pedir('/App/Chat?handler=Mensajes&id=' + actual.id + '&antes=' + (primero ? primero.id : 0));
            mensajes = r.mensajes.concat(mensajes);
            hayMas = r.hayMas;
            pintarMensajes(false);
            zona.scrollTop = zona.scrollHeight - altoAntes;
        } catch (e) { alertaBreve(e.message); }
        cargando = false;
    }

    const ultimoIdConfirmado = () => mensajes.reduce((mx, m) => m.id > mx ? m.id : mx, 0);

    function marcarLeido() {
        if (!actual || document.hidden) return;
        const hasta = ultimoIdConfirmado();
        if (hasta <= leidoEnviado) return;
        leidoEnviado = hasta;
        const c = convs.find(x => x.id === actual.id);
        if (c) { c.noLeidos = 0; pintarLista(); }
        enviarForm('Leido', { id: actual.id, hasta }).catch(() => { leidoEnviado = 0; });
    }

    async function buscarNuevos() {
        if (!actual || document.hidden) return;
        const id = actual.id;
        try {
            const r = await pedir('/App/Chat?handler=Mensajes&id=' + id + '&despues=' + ultimoIdConfirmado());
            if (!actual || actual.id !== id) return;
            const conocidos = new Set(mensajes.map(m => m.id));
            const nuevos = r.mensajes.filter(m => !conocidos.has(m.id));
            const cambioVisto = r.leidoPorOtro !== leidoPorOtro;
            leidoPorOtro = r.leidoPorOtro;
            if (nuevos.length || cambioVisto) {
                // Los pendientes (id negativo) quedan al final.
                const pendientes = mensajes.filter(m => m.id < 0);
                mensajes = mensajes.filter(m => m.id > 0).concat(nuevos).sort((a, b) => a.id - b.id).concat(pendientes);
                pintarMensajes(false);
            }
            if (nuevos.length && cercaDelFinal() && document.hasFocus()) marcarLeido();
        } catch (e) { /* se reintenta */ }
    }

    // ------------------------------------------------------------ enviar
    async function enviar(texto, existente) {
        if (!actual) return;
        const id = actual.id;
        const m = existente || { id: tempId--, idConversacion: id, idUsuario: YO, autor: 'Tú', texto, fecha: new Date().toISOString(), mio: true };
        m.estado = 'enviando'; delete m.error;
        if (!existente) mensajes.push(m);
        pintarMensajes(true);
        try {
            const r = await enviarForm('Enviar', { id, texto });
            if (!actual || actual.id !== id) return;
            mensajes = mensajes.filter(x => x !== m && x.id !== r.id);
            mensajes.push(r);
            mensajes.sort((a, b) => (a.id < 0) - (b.id < 0) || a.id - b.id);
            leidoEnviado = Math.max(leidoEnviado, r.id);
            pintarMensajes(true);
            refrescarLista();
        } catch (e) {
            m.estado = 'error'; m.error = e.message;
            pintarMensajes(true);
        }
    }
    function reintentar(m) { enviar(m.texto, m); }

    function ajustarAlto() {
        caja.style.height = 'auto';
        caja.style.height = Math.min(caja.scrollHeight + 2, 160) + 'px';
    }
    caja.addEventListener('input', ajustarAlto);
    caja.addEventListener('keydown', e => {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); form.requestSubmit ? form.requestSubmit() : form.dispatchEvent(new Event('submit', { cancelable: true })); }
    });
    form.addEventListener('submit', e => {
        e.preventDefault();
        const texto = caja.value.trim();
        if (!texto) return;
        if (texto.length > 2000) { alertaBreve('El mensaje es muy largo (máximo 2.000 caracteres).'); return; }
        caja.value = ''; ajustarAlto();
        enviar(texto);
        caja.focus();
    });

    function alertaBreve(texto) {
        const a = el('div', 'ch-aviso error');
        a.setAttribute('role', 'alert');
        a.textContent = texto;
        app.parentNode.insertBefore(a, app);
        setTimeout(() => a.remove(), 5000);
    }

    // ------------------------------------------------------------ eventos
    filtro.addEventListener('input', pintarLista);
    btnNuevo.addEventListener('click', () => mostrarPersonas(panelPersonas.hidden));
    $('chatPersonasCerrar').addEventListener('click', () => { mostrarPersonas(false); btnNuevo.focus(); });
    panelPersonas.addEventListener('keydown', e => { if (e.key === 'Escape') { mostrarPersonas(false); btnNuevo.focus(); } });
    $('chatVolver').addEventListener('click', () => {
        app.classList.remove('con-hilo');
        actual = null;
        pintarLista();
        try { history.replaceState(null, '', '/App/Chat'); } catch (e) { /* sin historial */ }
    });
    zona.addEventListener('scroll', () => { if (cercaDelFinal()) marcarLeido(); });
    window.addEventListener('focus', () => { buscarNuevos(); if (cercaDelFinal()) marcarLeido(); });
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { buscarNuevos(); refrescarLista(); } });

    setInterval(buscarNuevos, 3000);
    setInterval(() => { if (!document.hidden) refrescarLista(); }, 8000);

    pintarLista();
    // En celular se parte en la lista; en escritorio se abre la conversación indicada (o la primera).
    const pedida = new URLSearchParams(location.search).has('c') || new URLSearchParams(location.search).has('con');
    if (datos.abrir && (!esMovil() || pedida)) abrir(datos.abrir);
})();
