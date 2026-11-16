// Selector de naves del catálogo (nov-2026). Lo usan "Agregar salida" en Itinerarios y la
// ventana "Agregar salida" del buscador de Create.
//  - Muestra primero las naves de la naviera elegida y después "Otras navieras".
//  - Si la nave no existe, la última opción es "+ Crear nave «…»": se registra en el catálogo
//    (avisando si hay nombres muy parecidos) y queda elegida.
//  - El valor del select es el nombre de la nave (lo que guarda la salida).
(function () {
    'use strict';
    const URL_PAGINA = '/Operaciones/Itinerarios';

    function token() {
        const t = document.querySelector('input[name="__RequestVerificationToken"]');
        return t ? t.value : '';
    }
    function norm(s) { return String(s || '').trim().replace(/\s+/g, ' ').toUpperCase(); }
    function esc(s) { return String(s || '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

    async function crearEnCatalogo(nombre, idNaviera, confirmar) {
        const fd = new FormData();
        fd.append('nombre', nombre);
        if (idNaviera) fd.append('idNaviera', idNaviera);
        if (confirmar) fd.append('confirmar', 'true');
        const r = await fetch(URL_PAGINA + '?handler=CrearNave', { method: 'POST', headers: { 'RequestVerificationToken': token() }, body: fd });
        return r.json();
    }

    // Devuelve el nombre que finalmente queda elegido, o null si se canceló.
    async function resolverCreacion(nombre, idNaviera) {
        let d = await crearEnCatalogo(nombre, idNaviera, false);
        if (!d.success && d.parecidas && d.parecidas.length && window.Swal) {
            const opciones = d.parecidas.map((p, i) =>
                '<button type="button" class="nc-parecida" data-i="' + i + '">' + esc(p) + '</button>').join('');
            let elegida = null;
            const r = await Swal.fire({
                title: '¿Es alguna de estas naves?', width: 520,
                html: '<div class="nc-swal">Ya existen naves con un nombre parecido a <strong>' + esc(nombre) + '</strong>. Elige una para no duplicarla o créala igual.<div class="nc-parecidas">' + opciones + '</div></div>',
                showCancelButton: true, confirmButtonText: 'Crear «' + nombre + '» igual', cancelButtonText: 'Cancelar', confirmButtonColor: '#0f172a',
                didOpen: (el) => el.querySelectorAll('.nc-parecida').forEach(b => b.addEventListener('click', () => { elegida = d.parecidas[+b.dataset.i]; Swal.clickConfirm(); })),
            });
            if (elegida) return elegida;
            if (!r.isConfirmed) return null;
            d = await crearEnCatalogo(nombre, idNaviera, true);
        }
        if (!d.success) {
            if (window.Swal) Swal.fire('No se pudo crear la nave', d.message || '', 'warning'); else alert(d.message || 'No se pudo crear la nave.');
            return null;
        }
        return d.nombre;
    }

    // Opción de la lista: nombre de la nave a la izquierda y la naviera a la derecha, con el
    // color de su maestro. Los títulos de grupo van discretos; "+ Crear nave" destacado.
    function plantilla(d) {
        if (!d || d.loading) return d ? d.text : '';
        const $ = window.jQuery;
        if (d.children) return $('<span class="nc-grupo"></span>').text(d.text);
        if (d.nueva) return $('<span class="nc-crear"><i class="bi bi-plus-circle"></i></span>').append($('<span></span>').text(' Crear nave «' + d.id + '»'));
        if (!d.id) return d.text;
        const fila = $('<span class="nc-op"></span>');
        fila.append($('<span class="nc-op-nombre"></span>').text(d.id));
        if (d.naviera) {
            const color = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test(d.color || '') ? d.color : '#94a3b8';
            fila.append($('<span class="nc-op-naviera"></span>').css('color', color).text(d.naviera));
        }
        return fila;
    }

    function fijar(select, nombre) {
        const $s = window.jQuery(select);
        if (!Array.from(select.options).some(o => o.value === nombre)) select.add(new Option(nombre, nombre, true, true));
        $s.val(nombre).trigger('change');
    }

    function enlazar(select, navieraEl) {
        if (!select || select.dataset.ncEnlazado || !window.jQuery || !window.jQuery.fn.select2) return;
        select.dataset.ncEnlazado = '1';
        const $s = window.jQuery(select);
        const leerNaviera = () => (navieraEl && navieraEl.value) || '';
        $s.select2({
            width: '100%',
            dropdownParent: $s.closest('.modal').length ? $s.closest('.modal') : window.jQuery(document.body),
            placeholder: 'Escribe para buscar la nave…',
            allowClear: false,
            tags: true,
            minimumInputLength: 0,
            language: {
                noResults: () => 'Sin naves en el catálogo',
                searching: () => 'Buscando…',
                inputTooShort: () => 'Escribe el nombre de la nave',
            },
            ajax: {
                url: URL_PAGINA + '?handler=CatalogoNaves',
                dataType: 'json',
                delay: 200,
                data: p => ({ q: p.term || '', naviera: leerNaviera() }),
                processResults: d => d,
                cache: false,
            },
            // La opción de crear aparece al final, solo si el texto no coincide con una nave existente.
            createTag: p => {
                const t = norm(p.term);
                if (t.length < 2) return null;
                return { id: t, text: '+ Crear nave «' + t + '»', nueva: true };
            },
            insertTag: (data, tag) => {
                const existe = data.some(g => (g.children || [g]).some(o => norm(o.id) === tag.id));
                if (!existe) data.push(tag);
            },
            templateResult: plantilla,
            templateSelection: d => d.nueva ? d.id : (d.id || d.text),
        });
        $s.on('select2:select', async (e) => {
            const d = e.params.data || {};
            if (!d.nueva) return;
            if (!leerNaviera()) {
                if (window.Swal) Swal.fire('Elige la naviera', 'La nave nueva queda registrada con la naviera de la salida.', 'info');
                $s.val(null).trigger('change');
                return;
            }
            const nombre = await resolverCreacion(d.id, leerNaviera());
            // Se quita la opción temporal "+ Crear…" y se deja la nave final.
            Array.from(select.options).forEach(o => { if (o.textContent.startsWith('+ Crear nave')) o.remove(); });
            if (nombre) fijar(select, nombre);
            else $s.val(null).trigger('change');
        });
    }

    // Para una nave que llega escrita (p. ej. la del PDF del booking): si ya está en el catálogo
    // se usa tal cual; si no, se crea con el mismo aviso de nombres parecidos. Null = cancelado.
    async function asegurar(nombre, idNaviera) {
        const t = norm(nombre);
        if (t.length < 2) return null;
        try {
            const r = await fetch(URL_PAGINA + '?handler=CatalogoNaves&q=' + encodeURIComponent(t) + '&naviera=' + encodeURIComponent(idNaviera || ''), { headers: { 'Accept': 'application/json' } });
            const d = await r.json();
            const existe = (d.results || []).some(g => (g.children || [g]).some(o => norm(o.id) === t));
            if (existe) return t;
        } catch (e) { /* si falla la consulta, se intenta crear: el servidor no duplica */ }
        return resolverCreacion(t, idNaviera);
    }

    window.NavesCatalogo = { enlazar, fijar, asegurar };
})();
