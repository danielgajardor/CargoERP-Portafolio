/* Ruta POL → POD por país (nov-2026).
 *
 * El puerto de origen (POL) y el de destino (POD) no pueden ser del mismo país: al elegir un
 * POL de Chile, la lista de POD deja de ofrecer puertos chilenos. Va en un solo sentido (el
 * POL manda): así se puede invertir una ruta cambiando primero el POL; si el POD que estaba
 * elegido queda del mismo país, se limpia con un aviso. Sirve para
 * todas las pantallas: basta marcar las listas con data-ruta="pol" y data-ruta="pod" dentro
 * del mismo formulario, ventana o bloque con data-ruta-grupo.
 *
 * País de cada opción, en este orden: data-pais de la opción, el grupo (<optgroup label>) o
 * el mapa window.PuertosPais { porId, porNombre } (partial _PuertosPais).
 * Las opciones del mismo país quedan deshabilitadas y ocultas (también en Select2). Si el
 * puerto que ya estaba elegido en la otra lista queda fuera, se limpia con un aviso.
 */
(function () {
    'use strict';

    const norm = (t) => String(t || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase().replace(/\s+/g, ' ').trim();
    const SEL = 'select[data-ruta="pol"], select[data-ruta="pod"]';

    function paisDe(opt) {
        if (!opt || !opt.value) return '';
        let p = opt.getAttribute('data-pais');
        if (!p && opt.parentElement && opt.parentElement.tagName === 'OPTGROUP') p = opt.parentElement.label;
        const mapa = window.PuertosPais;
        if (!p && mapa) p = (mapa.porId && mapa.porId[opt.value]) || (mapa.porNombre && (mapa.porNombre[norm(opt.value)] || mapa.porNombre[norm(opt.text)])) || '';
        p = norm(p);
        return p === 'OTROS' ? '' : p;
    }

    function grupo(sel) {
        return sel.closest('[data-ruta-grupo]') || sel.closest('form') || sel.closest('.modal') || document;
    }

    function pareja(sel) {
        const buscar = sel.getAttribute('data-ruta') === 'pol' ? 'pod' : 'pol';
        return grupo(sel).querySelector('select[data-ruta="' + buscar + '"]');
    }

    function refrescarSelect2(sel) {
        const $ = window.jQuery;
        if ($ && $(sel).data('select2')) $(sel).trigger('change.select2');
    }

    function avisar(sel, texto) {
        const cont = sel.closest('.faf-g, .af-field, .mx-campo, [class*="col-"]') || sel.parentElement;
        if (!cont) return;
        let a = cont.querySelector(':scope > .ruta-aviso');
        if (!a) {
            a = document.createElement('div');
            a.className = 'ruta-aviso';
            a.setAttribute('role', 'status');
            cont.appendChild(a);
        }
        a.textContent = texto;
        clearTimeout(a._t);
        a._t = setTimeout(() => a.remove(), 6000);
    }

    // Deja en "otro" solo los puertos de un país distinto al elegido en "base".
    // limpiar = true: si el elegido en "otro" queda fuera, se vacía (cambio del usuario).
    function filtrar(base, otro, limpiar) {
        if (!base || !otro) return;
        const pais = paisDe(base.options[base.selectedIndex]);
        let quitar = false;
        Array.from(otro.options).forEach(o => {
            if (!o.value) return;
            const bloquear = pais !== '' && paisDe(o) === pais;
            if (bloquear) {
                if (o.selected && !limpiar) return;   // dato guardado antes de la regla: se respeta
                if (o.selected) quitar = true;
                if (!o.disabled) { o.disabled = true; o.hidden = true; o.dataset.rutaBloq = '1'; }
            } else if (o.dataset.rutaBloq === '1') {
                o.disabled = false; o.hidden = false; delete o.dataset.rutaBloq;
            }
        });
        if (quitar) {
            const nombre = otro.options[otro.selectedIndex] ? otro.options[otro.selectedIndex].text : '';
            otro.value = '';
            otro.dispatchEvent(new Event('change', { bubbles: true }));
            avisar(otro, (nombre ? nombre + ' es' : 'Ese puerto es') + ' de ' + pais + ', igual que el origen: elige un destino de otro país.');
        }
        refrescarSelect2(otro);
    }

    // usuario = true solo cuando lo eligió la persona (no al cargar datos, abrir la edición o
    // rehacer la lista): así un dato antiguo no se borra solo por abrir la pantalla.
    function alCambiar(sel, usuario) {
        if (!sel || !sel.matches || !sel.matches(SEL)) return;
        const otro = pareja(sel);
        if (!otro) return;
        if (sel.getAttribute('data-ruta') === 'pol') filtrar(sel, otro, !!usuario);
        else filtrar(otro, sel, false);   // la lista de POD se rehízo o cambió: se vuelve a marcar
    }

    // En Select2 no basta "hidden": la lista de esas listas oculta sus opciones deshabilitadas
    // (en estas listas solo las deshabilita esta regla).
    function estilo() {
        if (document.getElementById('rutaPaisesCss')) return;
        const st = document.createElement('style');
        st.id = 'rutaPaisesCss';
        st.textContent = '.ruta-aviso{font-size:11.5px;font-weight:600;color:#b45309;margin-top:4px}'
            + '.ruta-lista .select2-results__option[aria-disabled="true"],.ruta-lista .select2-results__option--disabled{display:none}'
            // Grupos por país (Itinerarios, portal) que quedan sin opciones: sin título vacío.
            + '.ruta-lista .select2-results__option[role="group"]:not(:has(.select2-results__option:not([aria-disabled="true"]):not(.select2-results__option--disabled))){display:none}';
        document.head.appendChild(st);
    }

    function marcarLista() {
        const $ = window.jQuery;
        const inst = $ && $(this).data('select2');
        const ul = inst && inst.$results && inst.$results[0];
        if (ul) ul.classList.add('ruta-lista');
    }

    function iniciar() {
        estilo();
        const $ = window.jQuery;
        // jQuery recibe tanto el evento nativo como el que dispara Select2 al elegir.
        if ($) {
            $(document).off('change.ruta').on('change.ruta', SEL, function (ev) { alCambiar(this, !!(ev.originalEvent && ev.originalEvent.isTrusted)); });
            $(document).off('select2:select.ruta').on('select2:select.ruta', SEL, function () { alCambiar(this, true); });
        }
        else if (!window.__rutaNativo) { window.__rutaNativo = true; document.addEventListener('change', (e) => alCambiar(e.target, e.isTrusted), true); }
        if ($) $(document).off('select2:open.ruta').on('select2:open.ruta', SEL, marcarLista);
        // Estado inicial (edición): se filtra sin tocar lo ya elegido.
        document.querySelectorAll('select[data-ruta="pol"]').forEach(pol => {
            const pod = pareja(pol);
            if (pod) filtrar(pol, pod, false);
        });
    }

    window.RutaPaises = { iniciar, alCambiar, paisDe };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', iniciar);
    else iniciar();
    // Pantallas que arman Select2 o rehacen las listas al cargar: se vuelve a aplicar.
    window.addEventListener('load', iniciar);
})();
