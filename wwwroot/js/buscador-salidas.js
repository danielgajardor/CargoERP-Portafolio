// Buscador de salidas · Datos del Embarque en Operaciones › Create (nov-2026).
//
//   1) Puerto de Origen (POL), Puerto de Destino (POD) y Fecha de inicio → Buscar.
//   2) Las salidas del maestro de itinerarios en tarjetas, ordenadas por ETD, en ventanas de
//      6 semanas ("Ver 6 semanas más"), con filtro por naviera.
//   3) Al seleccionar una salida aparece la tarjeta «Salida elegida»: Naviera y Nave / Viaje
//      se muestran como dato (son de la salida; para cambiarlas se elige otra) y se rellenan
//      Modalidad de Tránsito, ETD (Zarpe), ETA (Arribo) y Corte Documental (Matriz BL), que
//      quedan editables. Si la nave no está, "Agregar salida" la crea en Itinerarios sin
//      salir de Create.
//
// Se mantiene la interfaz que ya usaban el PDF del booking, la copia, la cotización y el
// borrador: inpNave._navesApi.fijarTexto(texto, idSalida).
(function () {
    'use strict';
    const URL_ITINERARIOS = '/Operaciones/Itinerarios';
    const SEMANAS = 6;

    const $id = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const norm = (s) => String(s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').trim().toUpperCase();
    const token = () => (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';

    function hoyIso() {
        const d = new Date();
        return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
    }
    // "2026-11-17" → "17-11-2026"; con hora → "17-11-2026 · 10:30"
    function fecha(iso, hora) {
        if (!iso) return '—';
        const [a, m, d] = String(iso).slice(0, 10).split('-');
        return d + '-' + m + '-' + a + (hora ? ' · ' + hora : '');
    }
    function fechaHoraIso(isoDT) {
        if (!isoDT) return '—';
        const [f, h] = String(isoDT).split('T');
        return fecha(f, h ? h.slice(0, 5) : null);
    }

    // Cambia un campo del formulario como lo haría el usuario: corren los listeners de la página.
    function fijarCampo(el, valor) {
        if (!el) return;
        el.value = valor ?? '';
        el.dispatchEvent(new Event('change', { bubbles: true }));
        if (window.jQuery && window.jQuery(el).data('select2')) window.jQuery(el).trigger('change.select2');
    }
    function opcionPorTexto(sel, texto) {
        if (!sel || !texto) return null;
        const t = norm(texto);
        return Array.from(sel.options).find(o => o.value && norm(o.text) === t) || null;
    }
    const textoSel = (sel) => (sel && sel.selectedIndex > 0 ? sel.options[sel.selectedIndex].text : '');

    let c = null;              // elementos
    const est = { salidas: [], desde: null, hasta: null, hayMas: false, filtro: '', elegida: null, pendiente: null, cargando: false, ruta: null };
    let fijando = false;       // true mientras el propio buscador cambia campos (no limpia la elección)

    function aviso(html, tipo) {
        if (!c.aviso) return;
        if (!html) { c.aviso.classList.add('d-none'); c.aviso.innerHTML = ''; return; }
        c.aviso.className = 'emb-aviso' + (tipo === 'info' ? ' emb-aviso--info' : '');
        c.aviso.innerHTML = html;
    }
    function hint(html, tipo) {
        if (!c.hint) return;
        c.hint.className = 'naves-hint' + (tipo ? ' naves-hint--' + tipo : '');
        c.hint.innerHTML = html || '';
    }

    // ------------------------------------------------------------------ búsqueda
    async function buscar(mas) {
        const pol = c.pol.value, pod = c.pod.value;
        if (!pol || !pod) {
            aviso('<i class="bi bi-info-circle me-1"></i> Elige el Puerto de Origen (POL) y el Puerto de Destino (POD) para buscar.', 'info');
            return;
        }
        if (est.cargando) return;
        est.cargando = true;
        aviso(null);
        const desde = mas ? est.hasta : (c.fecha.value || hoyIso());
        if (!mas) {
            est.salidas = []; est.filtro = ''; est.desde = desde;
            est.ruta = { pol: textoSel(c.pol), pod: textoSel(c.pod) };
            c.resultados.classList.remove('d-none');
            c.lista.innerHTML = '<div class="emb-cargando"><span class="spinner-border spinner-border-sm me-2"></span>Buscando salidas…</div>';
        }
        c.btnBuscar.disabled = true; c.verMas.disabled = true;
        try {
            const r = await fetch(URL_ITINERARIOS + '?handler=BuscarSalidas&pol=' + encodeURIComponent(pol) + '&pod=' + encodeURIComponent(pod)
                + '&desde=' + encodeURIComponent(desde) + '&semanas=' + SEMANAS, { headers: { 'Accept': 'application/json' } });
            const d = await r.json();
            if (!d.success) { aviso('<i class="bi bi-exclamation-triangle me-1"></i> ' + esc(d.message || 'No se pudo buscar.')); return; }
            const vistos = new Set(est.salidas.map(s => s.id));
            est.salidas = est.salidas.concat((d.salidas || []).filter(s => !vistos.has(s.id)));
            est.hasta = d.hasta;
            est.hayMas = !!d.hayMas;
        } catch (e) {
            aviso('<i class="bi bi-wifi-off me-1"></i> No se pudo buscar. Revisa la conexión e inténtalo de nuevo.');
        } finally {
            est.cargando = false;
            c.btnBuscar.disabled = false; c.verMas.disabled = false;
            pintar();
        }
    }

    function tarjeta(s) {
        const elegida = est.elegida && est.elegida.id === s.id;
        const modalidad = s.transbordo
            ? '<span class="emb-pill emb-pill--transbordo"><i class="bi bi-shuffle"></i> Transbordo · ' + esc(s.transbordo) + '</span>'
            : '<span class="emb-pill emb-pill--directo"><i class="bi bi-arrow-right"></i> Directo</span>';
        const zarpe = s.zarpada ? '<span class="emb-pill emb-pill--zarpada" title="Zarpe efectivo registrado"><i class="bi bi-check2"></i> Zarpó ' + esc(fechaHoraIso(s.zarpe).split(' ·')[0]) + '</span>' : '';
        const accion = elegida
            ? '<span class="emb-elegida-tag"><i class="bi bi-check2"></i> Elegida</span>'
            : '<button type="button" class="btn emb-btn-elegir" data-elegir="' + s.id + '">Seleccionar</button>';
        return '<div class="emb-salida' + (elegida ? ' elegida' : '') + '" data-id="' + s.id + '">'
            + '<div><div class="emb-naviera">' + esc(s.naviera) + '</div><div class="emb-nave-txt">' + esc(s.nave) + '</div>'
            + '<div class="emb-pills">' + modalidad + zarpe + '</div></div>'
            + '<div class="emb-ruta">'
            + '<div><div class="emb-lbl">ETD (Zarpe)</div><div class="emb-fecha">' + esc(fecha(s.etd, s.etdHora)) + '</div><div class="emb-puerto" title="' + esc(s.pol) + '">' + esc(s.pol) + '</div></div>'
            + '<div class="emb-transito">' + s.transito + ' día' + (s.transito === 1 ? '' : 's') + '<i class="bi bi-arrow-right"></i></div>'
            + '<div><div class="emb-lbl">ETA (Arribo)</div><div class="emb-fecha">' + esc(fecha(s.eta, s.etaHora)) + '</div><div class="emb-puerto" title="' + esc(s.pod) + '">' + esc(s.pod) + '</div></div>'
            + '</div>'
            + '<div><div class="emb-lbl">Corte Documental</div><div class="emb-corte">' + esc(fechaHoraIso(s.corte)) + '</div></div>'
            + '<div class="emb-accion">' + accion + '</div>'
            + (s.observaciones ? '<div class="emb-obs"><i class="bi bi-chat-left-text me-1"></i>' + esc(s.observaciones) + '</div>' : '')
            + '</div>';
    }

    function pintar() {
        if (!est.ruta) return;
        const visibles = est.salidas.filter(s => !est.filtro || String(s.idNaviera) === est.filtro);
        const n = est.salidas.length;
        c.titulo.textContent = n + ' salida' + (n === 1 ? '' : 's') + ' de ' + (est.ruta.pol || 'POL') + ' a ' + (est.ruta.pod || 'POD') + ' desde el ' + fecha(est.desde);

        // Filtro por naviera (solo si hay más de una).
        const navieras = [];
        est.salidas.forEach(s => { if (!navieras.some(x => x.id === String(s.idNaviera))) navieras.push({ id: String(s.idNaviera), nombre: s.naviera }); });
        c.filtros.innerHTML = navieras.length > 1
            ? ['<button type="button" class="emb-chip' + (est.filtro ? '' : ' activo') + '" data-filtro="">Todas</button>']
                .concat(navieras.map(x => '<button type="button" class="emb-chip' + (est.filtro === x.id ? ' activo' : '') + '" data-filtro="' + esc(x.id) + '">' + esc(x.nombre) + ' · ' + est.salidas.filter(s => String(s.idNaviera) === x.id).length + '</button>')).join('')
            : '';

        c.lista.innerHTML = visibles.length
            ? visibles.map(tarjeta).join('')
            : '<div class="emb-vacio">No hay salidas de <strong>' + esc(est.ruta.pol) + '</strong> a <strong>' + esc(est.ruta.pod) + '</strong> entre el '
                + esc(fecha(est.desde)) + ' y el ' + esc(fecha(est.hasta)) + '.<br>' + (est.hayMas ? 'Hay salidas más adelante: usa «Ver 6 semanas más». ' : '')
                + 'Si la nave no está, agrégala con «Agregar salida».</div>';
        c.verMas.classList.toggle('d-none', !est.hayMas);
    }

    // ------------------------------------------------------------------ selección
    // Tarjeta «Salida elegida»: solo se ve con una salida elegida.
    function mostrarSeleccion(s) {
        if (c.buscador) c.buscador.classList.remove('emb-card--falta');
        if (!c.seleccion) return;
        c.seleccion.classList.toggle('d-none', !s);
        if (!s) return;
        if (c.infoNaviera) c.infoNaviera.textContent = s.naviera || textoSel(c.naviera) || '—';
        if (c.infoNave) c.infoNave.textContent = s.nave || '—';
        if (c.infoRuta) {
            c.infoRuta.innerHTML = esc(s.pol) + ' <i class="bi bi-arrow-right mx-1"></i> ' + esc(s.pod);
            c.infoRuta.title = s.pol + ' → ' + s.pod + (s.transbordo ? ' (transbordo en ' + s.transbordo + ')' : '');
        }
        if (c.infoNave) c.infoNave.title = s.nave || '';
        if (c.infoSalida) c.infoSalida.textContent = 'Salida #' + s.id;
    }

    // 13-nov: stacking de la salida según la carga del booking (Dry o Reefer). Solo se llena un
    // campo vacío o con el valor que la salida había puesto antes (no pisa lo escrito a mano).
    function aplicarStacking(s) {
        if (!s || !c.stackIn || !c.stackOut) return;
        const rf = typeof c.esReefer === 'function' && c.esReefer();
        const ini = rf ? s.stkRfIni : s.stkDryIni, fin = rf ? s.stkRfFin : s.stkDryFin;
        [[c.stackIn, ini], [c.stackOut, fin]].forEach(([campo, valor]) => {
            if (!campo) return;
            const auto = campo.dataset.autoSalida || '';
            if (campo.value && campo.value !== auto) return;
            campo.value = valor || '';
            campo.dataset.autoSalida = valor || '';
            campo.classList.toggle('cr-desde-salida', !!valor);
            campo.dispatchEvent(new Event('change', { bubbles: true }));
        });
        if (c.stackAviso) {
            c.stackAviso.innerHTML = (ini || fin)
                ? '<i class="bi bi-magic me-1"></i>Stacking ' + (rf ? 'Reefer' : 'Dry') + ' de la salida ' + esc(s.nave) + '.'
                : '<i class="bi bi-info-circle me-1"></i>La salida aún no tiene stacking ' + (rf ? 'Reefer' : 'Dry') + ': si lo ingresas, queda en la salida y se copia a los demás bookings de la nave.';
        }
    }

    function elegir(s, desdeServidor) {
        if (!s) return;
        est.elegida = s;
        est.pendiente = null;
        fijando = true;
        try {
            c.nave.value = s.nave;
            c.nave.classList.remove('is-invalid');
            c.idItinerario.value = s.id;
            // La naviera es la de la salida (si no está en la lista, p. ej. inactiva, se agrega).
            if (c.naviera && s.idNaviera && !Array.from(c.naviera.options).some(o => o.value === String(s.idNaviera)))
                c.naviera.add(new Option(s.naviera || ('Naviera #' + s.idNaviera), String(s.idNaviera)));
            if (c.naviera && s.idNaviera && c.naviera.value !== String(s.idNaviera))
                fijarCampo(c.naviera, String(s.idNaviera));
            // POL / POD: si están vacíos o son otros (salida elegida por precarga), quedan los de la salida.
            if (norm(textoSel(c.pol)) !== norm(s.pol)) { const o = opcionPorTexto(c.pol, s.pol); if (o) fijarCampo(c.pol, o.value); }
            if (norm(textoSel(c.pod)) !== norm(s.pod)) { const o = opcionPorTexto(c.pod, s.pod); if (o) fijarCampo(c.pod, o.value); }
            fijarCampo(c.etd, s.etd);
            fijarCampo(c.eta, s.eta);
            if (s.corte) fijarCampo(c.corte, s.corte);
            aplicarStacking(s);
            if (c.modalidad) {
                fijarCampo(c.modalidad, s.transbordo ? 'TRANSBORDO' : 'DIRECTO');
                if (c.puertoTransbordo && s.transbordo) { c.puertoTransbordo.value = s.transbordo; c.puertoTransbordo.dispatchEvent(new Event('change', { bubbles: true })); }
            }
        } finally { fijando = false; }
        hint('', null);
        mostrarSeleccion(s);
        aviso(null);
        actualizarEspejos();
        if (!desdeServidor) pintar();
        else if (est.ruta) pintar();
    }

    // La salida elegida deja de corresponder (se cambió la ruta o la naviera): se oculta la tarjeta
    // y se vacían los datos que venían de ella, para que no se guarden datos de otra salida.
    function soltar(mensaje, quitarNaviera) {
        if (!est.elegida && !c.nave.value) return;
        est.elegida = null;
        c.nave.value = '';
        c.idItinerario.value = '';
        fijando = true;
        try {
            if (quitarNaviera && c.naviera) fijarCampo(c.naviera, '');
            fijarCampo(c.etd, '');
            fijarCampo(c.eta, '');
            fijarCampo(c.corte, '');
            [c.stackIn, c.stackOut].forEach(campo => { if (campo && campo.value && campo.value === (campo.dataset.autoSalida || '')) { campo.value = ''; campo.dataset.autoSalida = ''; campo.classList.remove('cr-desde-salida'); } });
            if (c.stackAviso) c.stackAviso.innerHTML = '';
            if (c.modalidad) fijarCampo(c.modalidad, 'DIRECTO');
            if (c.puertoTransbordo) { c.puertoTransbordo.value = ''; c.puertoTransbordo.dispatchEvent(new Event('change', { bubbles: true })); }
        } finally { fijando = false; }
        hint('', null);
        mostrarSeleccion(null);
        aviso(mensaje ? '<i class="bi bi-exclamation-circle me-1"></i> ' + esc(mensaje) : null);
        pintar();
    }

    async function obtener(url) {
        try { const r = await fetch(url, { headers: { 'Accept': 'application/json' } }); return await r.json(); }
        catch (e) { return { success: false }; }
    }

    // Nave que llega de afuera (PDF, copia, cotización, borrador): se busca en el maestro.
    async function fijarTexto(texto, idSalida) {
        texto = String(texto || '').trim().toUpperCase();
        if (idSalida) {
            const d = await obtener(URL_ITINERARIOS + '?handler=Salida&id=' + encodeURIComponent(idSalida));
            if (d.success) { elegir(d.salida, true); return; }
        }
        if (!texto) return;
        const nav = c.naviera && c.naviera.value ? '&naviera=' + encodeURIComponent(c.naviera.value) : '';
        const d = await obtener(URL_ITINERARIOS + '?handler=SalidaPorTexto&nave=' + encodeURIComponent(texto) + nav);
        if (d.success) { elegir(d.salida, true); return; }
        // No está en Itinerarios: no se guarda a mano; se ofrece agregarla con los datos que llegaron.
        est.elegida = null;
        est.pendiente = { nave: texto };
        c.nave.value = '';
        c.idItinerario.value = '';
        hint('', null);
        mostrarSeleccion(null);
        aviso('<i class="bi bi-exclamation-triangle me-1"></i> La nave <strong>' + esc(texto) + '</strong> no está en Itinerarios. '
            + '<button type="button" class="emb-link" data-agregar-salida>Agrégala como salida</button> o búscala con POL, POD y fecha.');
        if (c.pol.value && c.pod.value) buscar(false);
    }

    // ------------------------------------------------------------------ Agregar salida
    let modal = null, modalListo = false;
    function m(id) { return $id(id); }

    function prepararModal() {
        if (modalListo) return;
        modalListo = true;
        const $ = window.jQuery;
        if ($ && $.fn.select2) {
            const parent = $('#modalAgregarSalida');
            ['embNvNaviera', 'embNvPol', 'embNvPod'].forEach(id => $('#' + id).select2({ width: '100%', dropdownParent: parent, templateResult: id === 'embNvNaviera' ? undefined : plantillaPuerto, language: { noResults: () => 'Sin resultados' } }));
        }
        if (window.NavesCatalogo) NavesCatalogo.enlazar(m('embNvNave'), m('embNvNaviera'));
        const calcular = () => {
            const a = m('embNvEtd').value, b = m('embNvEta').value, t = m('embNvTransito');
            if (!a || !b) { t.textContent = ''; return; }
            const dias = Math.round((new Date(b.slice(0, 10)) - new Date(a.slice(0, 10))) / 86400000);
            t.textContent = dias < 0 ? 'La ETA no puede ser anterior a la ETD.' : 'Tránsito: ' + dias + ' día' + (dias === 1 ? '' : 's');
            t.classList.toggle('text-danger', dias < 0);
        };
        m('embNvEtd').addEventListener('change', calcular);
        m('embNvEta').addEventListener('change', calcular);
        m('embNvGuardar').addEventListener('click', guardarSalida);
    }

    function abrirAgregar() {
        prepararModal();
        const $ = window.jQuery;
        const set = (id, v) => { const el = m(id); if (!el) return; el.value = v ?? ''; if ($ && $(el).data('select2')) $(el).trigger('change.select2'); };
        const pend = est.pendiente || {};
        const partes = (pend.nave || '').split(' / ');
        const viaje = partes.length > 1 ? partes.pop() : '';
        const nombreNave = partes.join(' / ');
        set('embNvNaviera', c.naviera ? c.naviera.value : '');
        set('embNvPol', c.pol.value);
        set('embNvPod', c.pod.value);
        set('embNvViaje', viaje);
        set('embNvEtd', c.etd.value ? c.etd.value + 'T00:00' : '');
        set('embNvEta', c.eta.value ? c.eta.value + 'T00:00' : '');
        set('embNvCorte', c.corte.value || '');
        set('embNvTransbordo', c.modalidad && c.modalidad.value === 'TRANSBORDO' && c.puertoTransbordo ? c.puertoTransbordo.value : '');
        set('embNvObs', '');
        // Stacking: si el booking ya lo trae (escrito a mano), se propone en la columna de su carga.
        const rf = typeof c.esReefer === 'function' && c.esReefer();
        const propio = campo => campo && campo.value && campo.value !== (campo.dataset.autoSalida || '') ? campo.value : '';
        set('embNvStkDryIni', rf ? '' : propio(c.stackIn));
        set('embNvStkDryFin', rf ? '' : propio(c.stackOut));
        set('embNvStkRfIni', rf ? propio(c.stackIn) : '');
        set('embNvStkRfFin', rf ? propio(c.stackOut) : '');
        const selNave = m('embNvNave');
        Array.from(selNave.options).forEach(o => o.remove());
        if (nombreNave && window.NavesCatalogo) NavesCatalogo.fijar(selNave, nombreNave); else if ($) $(selNave).val(null).trigger('change');
        m('embNvEtd').dispatchEvent(new Event('change'));
        m('embNvError').classList.add('d-none');
        modal = modal || new bootstrap.Modal(m('modalAgregarSalida'));
        modal.show();
    }

    async function guardarSalida() {
        const err = m('embNvError');
        const btn = m('embNvGuardar');
        const v = id => (m(id).value || '').trim();
        err.classList.add('d-none');
        const faltan = [];
        if (!v('embNvNaviera')) faltan.push('Naviera');
        if (!v('embNvNave')) faltan.push('Nave');
        if (!v('embNvViaje')) faltan.push('N° viaje');
        if (!v('embNvPol')) faltan.push('Puerto de origen (POL)');
        if (!v('embNvPod')) faltan.push('Puerto de destino (POD)');
        if (!v('embNvEtd')) faltan.push('ETD');
        if (!v('embNvEta')) faltan.push('ETA');
        if (faltan.length) { err.textContent = 'Completa: ' + faltan.join(', ') + '.'; err.classList.remove('d-none'); return; }
        btn.disabled = true;
        try {
            // Nave que no está en el catálogo (p. ej. la del PDF): se confirma antes, con aviso de parecidas.
            let nombre = v('embNvNave').toUpperCase();
            if (window.NavesCatalogo && NavesCatalogo.asegurar) {
                nombre = await NavesCatalogo.asegurar(nombre, v('embNvNaviera'));
                if (!nombre) return;
                NavesCatalogo.fijar(m('embNvNave'), nombre);
            }
            const fd = new FormData();
            fd.append('Input.IdNaviera', v('embNvNaviera'));
            fd.append('Input.Nave', nombre);
            fd.append('Input.NumeroViaje', v('embNvViaje'));
            fd.append('Input.IdPuertoOrigen', v('embNvPol'));
            fd.append('Input.IdPuertoDestino', v('embNvPod'));
            fd.append('Input.Etd', v('embNvEtd'));
            fd.append('Input.Eta', v('embNvEta'));
            fd.append('Input.CorteDocumental', v('embNvCorte'));
            [['embNvStkDryIni', 'StackingInicioDry'], ['embNvStkDryFin', 'StackingFinDry'], ['embNvStkRfIni', 'StackingInicioReefer'], ['embNvStkRfFin', 'StackingFinReefer']]
                .forEach(([id, campo]) => { if (m(id)) fd.append('Input.' + campo, v(id)); });
            fd.append('Input.Transbordo', v('embNvTransbordo'));
            fd.append('Input.Observaciones', v('embNvObs'));
            const r = await fetch(URL_ITINERARIOS + '?handler=GuardarJson', { method: 'POST', headers: { 'RequestVerificationToken': token() }, body: fd });
            const d = await r.json();
            if (!d.success) { err.textContent = d.message || 'No se pudo guardar la salida.'; err.classList.remove('d-none'); return; }
            modal.hide();
            elegir(d.salida, true);
            if (est.ruta && norm(est.ruta.pol) === norm(d.salida.pol) && norm(est.ruta.pod) === norm(d.salida.pod)
                && !est.salidas.some(s => s.id === d.salida.id)) {
                est.salidas.push(d.salida);
                est.salidas.sort((a, b) => (a.etd + (a.etdHora || '')).localeCompare(b.etd + (b.etdHora || '')));
                pintar();
            }
            aviso('<i class="bi bi-check-circle me-1"></i> Salida agregada a Itinerarios y elegida para esta operación.'
                + (d.naveCreada ? ' La nave quedó también en el catálogo.' : ''), 'info');
        } catch (e) {
            err.textContent = 'No se pudo guardar. Revisa la conexión e inténtalo de nuevo.';
            err.classList.remove('d-none');
        } finally { btn.disabled = false; }
    }

    // Opción de puerto en las listas de POL / POD: nombre a la izquierda y país entre paréntesis,
    // gris y más pequeño, alineado a la derecha (queda en columna). El país sale de data-pais.
    function plantillaPuerto(o) {
        if (!o || !o.id || !o.element) return o ? o.text : '';
        const pais = String(o.element.getAttribute('data-pais') || '').trim();
        if (!pais) return o.text;
        const fila = document.createElement('span');
        fila.className = 'pto-op';
        const nombre = document.createElement('span');
        nombre.className = 'pto-op-nombre';
        nombre.textContent = o.text;
        const p = document.createElement('span');
        p.className = 'pto-op-pais';
        p.textContent = '(' + pais + ')';
        fila.appendChild(nombre);
        fila.appendChild(p);
        return window.jQuery ? window.jQuery(fila) : fila;
    }

    // ------------------------------------------------------------------ espejos en Marítimo
    function actualizarEspejos() {
        if (c.espejoPol) c.espejoPol.value = textoSel(c.pol);
        if (c.espejoPod) c.espejoPod.value = textoSel(c.pod);
    }

    // ------------------------------------------------------------------ inicio
    function iniciar(cfg) {
        c = cfg;
        if (!c || !c.nave || !c.pol || !c.pod) return;
        if (c.fecha && !c.fecha.value) c.fecha.value = hoyIso();

        c.btnBuscar.addEventListener('click', () => buscar(false));
        c.fecha.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); buscar(false); } });
        c.verMas.addEventListener('click', () => buscar(true));
        c.btnAgregar.addEventListener('click', abrirAgregar);
        c.resultados.addEventListener('click', (e) => {
            const b = e.target.closest('[data-elegir]');
            if (b) { const s = est.salidas.find(x => String(x.id) === b.dataset.elegir); if (s) elegir(s); return; }
            const f = e.target.closest('[data-filtro]');
            if (f) { est.filtro = f.dataset.filtro; pintar(); }
        });
        document.addEventListener('click', (e) => {
            if (e.target.closest('[data-agregar-salida]')) { e.preventDefault(); abrirAgregar(); }
            if (e.target.closest('[data-cambiar-salida]')) {
                e.preventDefault();
                c.buscador.scrollIntoView({ behavior: 'smooth', block: 'center' });
                if (!est.ruta && c.pol.value && c.pod.value) buscar(false);
            }
        });

        // Si se cambia a mano la naviera o la ruta, la salida elegida deja de corresponder.
        const $ = window.jQuery;
        const alCambiarNaviera = () => {
            if (fijando || !est.elegida) return;
            if (c.naviera.value !== String(est.elegida.idNaviera)) soltar('Cambiaste la naviera: elige de nuevo la salida.');
        };
        const alCambiarRuta = () => {
            actualizarEspejos();
            if (fijando) return;
            if (est.elegida && (norm(textoSel(c.pol)) !== norm(est.elegida.pol) || norm(textoSel(c.pod)) !== norm(est.elegida.pod)))
                soltar('Cambiaste la ruta: vuelve a buscar y elige la salida.', true);
            if (est.ruta && (norm(textoSel(c.pol)) !== norm(est.ruta.pol) || norm(textoSel(c.pod)) !== norm(est.ruta.pod))) {
                c.resultados.classList.add('d-none');
                est.ruta = null; est.salidas = [];
            }
        };
        if ($) {
            $(c.naviera).on('change', alCambiarNaviera);
            $(c.pol).on('change', alCambiarRuta);
            $(c.pod).on('change', alCambiarRuta);
        } else {
            c.naviera.addEventListener('change', alCambiarNaviera);
            c.pol.addEventListener('change', alCambiarRuta);
            c.pod.addEventListener('change', alCambiarRuta);
        }

        c.nave._navesApi = { fijarTexto, recargar: () => { } };
        actualizarEspejos();

        // Vuelta del servidor con datos (p. ej. error al guardar): se recupera la salida elegida.
        if (c.idItinerario.value) fijarTexto(c.nave.value, c.idItinerario.value);
        else if (c.nave.value) fijarTexto(c.nave.value);
    }

    // Cambió la carga del booking (Dry ↔ Reefer): se vuelve a tomar el stacking de la salida elegida.
    function reaplicarStacking() { if (est.elegida) aplicarStacking(est.elegida); }

    window.BuscadorSalidas = { iniciar, buscar, fijarTexto, plantillaPuerto, reaplicarStacking };
})();
