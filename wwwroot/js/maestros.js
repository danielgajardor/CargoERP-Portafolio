/* Maestros (oct-2026): filtro instantáneo de las listas, avisos de la ficha y
   protección contra doble envío o salir con cambios sin guardar. */
(function () {
    'use strict';

    function normalizar(t) {
        return (t || '').toString().normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase().replace(/\s+/g, ' ').trim();
    }

    // ---------- Lista: filtro instantáneo ----------
    function iniciarLista() {
        var input = document.querySelector('[data-mx-filtro]');
        var tabla = document.querySelector('[data-mx-tabla]');
        if (!input || !tabla) return;
        var filas = Array.prototype.slice.call(tabla.querySelectorAll('tbody tr[data-mx-texto]'));
        var vacio = tabla.querySelector('[data-mx-sin-coincidencias]');
        var total = document.querySelector('[data-mx-total]');
        var extra = document.querySelector('[data-mx-filtro-extra]');
        filas.forEach(function (f) { f._mxTexto = normalizar(f.getAttribute('data-mx-texto')); });

        function aplicar() {
            var partes = normalizar(input.value).split(' ').filter(Boolean);
            var valorExtra = extra ? extra.value : '';
            var visibles = 0;
            filas.forEach(function (f) {
                var ok = partes.every(function (p) { return f._mxTexto.indexOf(p) !== -1; });
                if (ok && valorExtra) ok = (f.getAttribute('data-mx-extra') || '') === valorExtra;
                f.hidden = !ok;
                if (ok) visibles++;
            });
            if (vacio) vacio.hidden = visibles !== 0 || filas.length === 0;
            if (total) total.textContent = visibles === filas.length ? filas.length + ' registros' : visibles + ' de ' + filas.length + ' registros';
            try {
                var url = new URL(window.location.href);
                if (input.value.trim()) url.searchParams.set('q', input.value.trim()); else url.searchParams.delete('q');
                if (extra) { if (valorExtra) url.searchParams.set('f', valorExtra); else url.searchParams.delete('f'); }
                window.history.replaceState(null, '', url.toString());
            } catch (e) { /* sin historial: se ignora */ }
        }

        input.addEventListener('input', aplicar);
        if (extra) extra.addEventListener('change', aplicar);
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') { input.value = ''; aplicar(); }
        });
        aplicar();

        // Atajo: "/" enfoca el buscador.
        document.addEventListener('keydown', function (e) {
            if (e.key === '/' && document.activeElement && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) {
                e.preventDefault();
                input.focus();
            }
        });

        var resaltar = tabla.querySelector('tr.mx-resaltado');
        if (resaltar && resaltar.scrollIntoView) resaltar.scrollIntoView({ block: 'center' });
    }

    // ---------- Lista: confirmar activar / desactivar; exportar con el filtro actual ----------
    document.addEventListener('submit', function (e) {
        var form = e.target;
        var msg = form && form.getAttribute('data-mx-confirmar');
        if (msg && !window.confirm(msg)) { e.preventDefault(); return; }
        if (form && form.hasAttribute('data-mx-con-filtro')) {
            var filtro = document.querySelector('[data-mx-filtro]');
            var oculto = form.querySelector('input[name="SearchString"]');
            if (filtro && oculto) oculto.value = filtro.value.trim();
        }
    }, true);

    // ---------- Ficha ----------
    function iniciarFicha() {
        var form = document.querySelector('form[data-mx-ficha]');
        if (!form) return;

        // Aviso al renombrar (las operaciones guardan el nombre como texto).
        form.querySelectorAll('[data-mx-nombre-original]').forEach(function (inp) {
            var original = inp.getAttribute('data-mx-nombre-original');
            var aviso = document.getElementById(inp.getAttribute('data-mx-aviso'));
            if (!aviso || !original) return;
            var revisar = function () { aviso.classList.toggle('visible', normalizar(inp.value) !== normalizar(original)); };
            inp.addEventListener('input', revisar);
            revisar();
        });

        // Cambios sin guardar.
        var inicial = new URLSearchParams(new FormData(form)).toString();
        var enviando = false;
        window.addEventListener('beforeunload', function (e) {
            if (enviando) return;
            if (new URLSearchParams(new FormData(form)).toString() !== inicial) {
                e.preventDefault();
                e.returnValue = '';
            }
        });

        // Evita doble envío.
        form.addEventListener('submit', function (e) {
            if (e.defaultPrevented) return;
            if (enviando) { e.preventDefault(); return; }
            enviando = true;
            var btn = form.querySelector('[data-mx-guardar]');
            if (btn) {
                btn.disabled = true;
                btn.setAttribute('aria-busy', 'true');
                var txt = btn.querySelector('span');
                if (txt) txt.textContent = 'Guardando…';
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        iniciarLista();
        iniciarFicha();
    });
})();
