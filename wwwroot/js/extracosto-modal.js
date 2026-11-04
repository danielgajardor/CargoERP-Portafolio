/* Ventana "Reportar extracosto" (oct-2026) — Pages/Operaciones/_ModalExtracosto.cshtml
 *
 * - Servicio (Marítimo / Transporte / Documental / Otros): filtra la lista de conceptos y
 *   define el bloque de Control Financiero donde queda el extracosto.
 * - "➕ Agregar concepto nuevo…": muestra el campo para escribirlo (pide el servicio).
 * - Vista previa de cómo llegará a Finanzas y validación antes de enviar.
 * Convive con los scripts existentes (abrirModalExtracostos…, guardarExtracostoAjax…,
 * filtrarTarifasMaritimoPorNaviera…), que siguen usando los mismos id.
 */
(function () {
    'use strict';

    var NUEVO = '__NUEVO__';

    function fmt(n, moneda) {
        var v = Number(n);
        if (!isFinite(v) || v <= 0) return null;
        return (moneda || 'CLP') + ' ' + v.toLocaleString('es-CL', { maximumFractionDigits: moneda === 'USD' ? 2 : 0 });
    }

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function refrescarSelect2(sel) {
        if (window.jQuery && window.jQuery(sel).data('select2')) window.jQuery(sel).trigger('change.select2');
    }

    function iniciar(modal) {
        var s = modal.getAttribute('data-extracosto-modal') || '';
        var $ = function (id) { return document.getElementById(id + s); };
        var form = modal.querySelector('form');
        var sel = $('extracostoTipoSelect');
        var hidGrupo = $('extracostoGrupoOperativoFinal');
        var wrapNuevo = $('extracostoNuevoWrap');
        var inpNuevo = $('extracostoConceptoNuevo');
        var monto = $('extracostoValorInput');
        var moneda = $('extracostoMonedaSelect');
        var paga = $('extracostoPaga');
        var resp = $('extracostoResponsable');
        var prov = $('extracostoProveedor');
        var just = $('extracostoJustificacionTxt');
        var evid = $('extracostoEvidencia');
        var previa = $('extracostoPrevia');
        var error = $('extracostoError');
        var botones = modal.querySelectorAll('.ext-servicio');
        if (!form || !sel) return;

        // Copia de la lista completa: al elegir servicio se arma solo con su grupo.
        var placeholder = sel.querySelector('option[value=""]');
        var optNuevo = sel.querySelector('option[data-nuevo]');
        var grupos = Array.prototype.map.call(sel.querySelectorAll('optgroup'), function (g) { return g.cloneNode(true); });
        var ocultos = {};          // opciones escondidas por la naviera del booking
        var servicio = '';

        function reconstruir() {
            var actual = sel.value;
            while (sel.firstChild) sel.removeChild(sel.firstChild);
            sel.appendChild(placeholder);
            grupos.forEach(function (g) {
                if (servicio && g.getAttribute('data-grupo') !== servicio) return;
                var copia = g.cloneNode(false);
                Array.prototype.forEach.call(g.children, function (o) {
                    if (!ocultos[o.value]) copia.appendChild(o.cloneNode(true));
                });
                if (copia.children.length) sel.appendChild(copia);
            });
            sel.appendChild(optNuevo);
            var sigue = Array.prototype.some.call(sel.options, function (o) { return o.value === actual; });
            sel.value = sigue ? actual : '';
            refrescarSelect2(sel);
        }

        function marcarServicio(valor) {
            servicio = valor || '';
            botones.forEach(function (b) {
                var activo = b.getAttribute('data-servicio') === servicio;
                b.classList.toggle('activo', activo);
                b.setAttribute('aria-checked', activo ? 'true' : 'false');
            });
            hidGrupo.value = servicio;
        }

        function opcionActual() { return sel.options[sel.selectedIndex] || null; }

        function concepto() {
            var o = opcionActual();
            if (!o || !o.value) return '';
            if (o.value === NUEVO) return (inpNuevo.value || '').trim();
            return o.value;
        }

        function pintarPrevia() {
            var c = concepto();
            var o = opcionActual();
            var partes = [];
            var ref = o && o.getAttribute('data-valor');
            if (ref && Number(ref) > 0 && o.value !== NUEVO) {
                partes.push('<div class="ext-previa__ref"><i class="bi bi-tag"></i> Tarifa de referencia (Comercial): <b>' + esc(fmt(ref, o.getAttribute('data-moneda') || 'USD')) + '</b></div>');
            }
            if (!c || !servicio) {
                previa.innerHTML = partes.join('') + '<div class="ext-previa__txt text-muted">Elige el servicio y el concepto para ver cómo llegará a Control Financiero.</div>';
                return;
            }
            var valor = fmt(monto.value, moneda.value) || 'valor por indicar';
            var r = resp.value;
            var cobro = r === 'Cliente' ? 'Se cobra al cliente el mismo valor.'
                : r === 'Atilson' ? 'Atilson lo absorbe: no se cobra al cliente.'
                : (r === 'Proveedor' || r === 'Agencia') ? 'Se descuenta a ' + (r === 'Proveedor' ? 'el proveedor' : 'la agencia') + ': no se cobra al cliente.'
                : 'Indica el responsable para saber si se cobra al cliente.';
            var nombreProv = prov && prov.value ? prov.options[prov.selectedIndex].text : 'por asignar';
            partes.push('<div class="ext-previa__txt"><i class="bi bi-arrow-return-right"></i> Control Financiero › <b>' + esc(servicio) + '</b>: «' + esc(c) + ' (EXT-…)» · costo <b>' + esc(valor) + '</b>'
                + ' · proveedor ' + esc(nombreProv) + (paga.value ? ' · pagado por ' + esc(paga.value) : '') + '. ' + esc(cobro)
                + (moneda.value === 'USD' ? ' <span class="text-muted">(los montos en USD se muestran también convertidos a CLP)</span>' : '') + '</div>');
            previa.innerHTML = partes.join('');
        }

        function mostrarError(texto) {
            if (!texto) { error.classList.add('d-none'); error.textContent = ''; return; }
            error.textContent = texto;
            error.classList.remove('d-none');
        }

        function alCambiarConcepto() {
            var o = opcionActual();
            var esNuevo = !!(o && o.value === NUEVO);
            wrapNuevo.classList.toggle('d-none', !esNuevo);
            if (esNuevo) {
                setTimeout(function () { inpNuevo.focus(); }, 50);
            } else if (o && o.value) {
                var g = o.getAttribute('data-grupo') || (o.parentElement && o.parentElement.getAttribute('data-grupo')) || '';
                if (g && g !== servicio) { marcarServicio(g); reconstruir(); sel.value = o.value; refrescarSelect2(sel); }
                else hidGrupo.value = g || servicio;
            }
            mostrarError('');
            pintarPrevia();
        }

        botones.forEach(function (b) {
            b.addEventListener('click', function () {
                var v = b.getAttribute('data-servicio');
                marcarServicio(v === servicio && sel.value !== NUEVO ? '' : v);
                reconstruir();
                mostrarError('');
                pintarPrevia();
            });
        });

        // jQuery/Select2 avisa con su propio "change"; el nativo cubre el resto.
        sel.addEventListener('change', alCambiarConcepto);
        if (window.jQuery) window.jQuery(sel).on('select2:select', alCambiarConcepto);
        [inpNuevo, monto, moneda, paga, resp, prov].forEach(function (el) {
            if (!el) return;
            el.addEventListener('input', pintarPrevia);
            el.addEventListener('change', pintarPrevia);
        });
        if (prov && window.jQuery) window.jQuery(prov).on('select2:select select2:clear', pintarPrevia);

        modal.addEventListener('show.bs.modal', function () {
            // La naviera del booking pudo esconder tarifas de otras navieras (style.display).
            ocultos = {};
            Array.prototype.forEach.call(sel.options, function (o) { if (o.style.display === 'none') ocultos[o.value] = true; });
            var preset = sel.value;
            marcarServicio('');
            reconstruir();
            if (preset) { sel.value = preset; alCambiarConcepto(); }
            wrapNuevo.classList.toggle('d-none', sel.value !== NUEVO);
            if (sel.value !== NUEVO) inpNuevo.value = '';
            mostrarError('');
            pintarPrevia();
        });
        modal.addEventListener('hidden.bs.modal', function () {
            // Vuelve a la lista completa para que el próximo filtro por naviera la encuentre.
            ocultos = {};
            marcarServicio('');
            reconstruir();
            inpNuevo.value = '';
            wrapNuevo.classList.add('d-none');
            if (prov) { prov.value = ''; refrescarSelect2(prov); }
            mostrarError('');
        });

        // Validación antes de que corra guardarExtracostoAjax / guardarExtracostoAjaxDetalle.
        form.addEventListener('click', function (e) {
            var btn = e.target.closest('.ext-modal__pie .btn-danger');
            if (!btn) return;
            var o = opcionActual();
            var faltas = null;
            if (!servicio && !(o && o.getAttribute('data-grupo'))) faltas = 'Elige el servicio (Marítimo, Transporte, Documental u Otros).';
            else if (!o || !o.value) faltas = 'Elige el concepto o agrega uno nuevo.';
            else if (o.value === NUEVO && !(inpNuevo.value || '').trim()) faltas = 'Escribe el nombre del concepto nuevo.';
            else if (!(Number(monto.value) > 0)) faltas = 'Indica el valor neto (mayor a 0).';
            else if (!paga.value) faltas = 'Indica quién paga.';
            else if (!resp.value) faltas = 'Indica el responsable.';
            else if (!(just.value || '').trim()) faltas = 'Escribe la justificación.';
            else if (!evid.files || !evid.files.length) faltas = 'Adjunta la evidencia.';
            else if (evid.files[0].size > 15 * 1024 * 1024) faltas = 'La evidencia supera los 15 MB.';
            if (faltas) {
                e.preventDefault();
                e.stopPropagation();
                mostrarError(faltas);
                return;
            }
            if (!hidGrupo.value) hidGrupo.value = servicio || o.getAttribute('data-grupo') || '';
            mostrarError('');
        }, true);

        pintarPrevia();
    }

    function iniciarTodos() {
        document.querySelectorAll('[data-extracosto-modal]').forEach(function (m) {
            if (m.__extIniciado) return;
            m.__extIniciado = true;
            iniciar(m);
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', iniciarTodos);
    else iniciarTodos();
})();
