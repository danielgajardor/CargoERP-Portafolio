/* =========================================================================
   Buscador predictivo en listas largas (oct-2026)

   Toda lista desplegable con muchas opciones (depósitos, plantas, navieras,
   ciudades, clientes, puertos, proveedores...) pasa a tener un buscador: se
   escribe parte del nombre y la lista se filtra al instante, sin importar
   tildes, mayúsculas ni el orden de las palabras. Las coincidencias que
   empiezan con lo escrito aparecen primero.

   - Se activa solo en <select> de una opción con UMBRAL o más opciones, o en
     los que tengan data-buscador="si". data-buscador="no" lo desactiva.
   - No toca las listas que la pantalla ya convirtió con Select2 a su manera.
   - Sigue funcionando todo el código existente: los cambios se avisan como un
     evento "change" normal y si un script cambia el valor (select.value = x)
     la lista se actualiza sola.
   ========================================================================= */
(function () {
    'use strict';

    var UMBRAL = 12;
    var SELECT2_JS = 'https://cdn.jsdelivr.net/npm/select2@4.1.0-rc.0/dist/js/select2.min.js';
    var SELECT2_CSS = 'https://cdn.jsdelivr.net/npm/select2@4.1.0-rc.0/dist/css/select2.min.css';
    var EXCLUIR = '.faf-money select, select[data-iva-select], select.no-select2, [data-sin-buscador] select';
    var EDICION_EN_LINEA = '.inline-field, .banner-input, .erp-input, .select-sm, .pro-input';

    function normalizar(t) {
        return (t || '').toString().normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase().replace(/\s+/g, ' ').trim();
    }

    function esCandidato(sel) {
        if (!sel || sel.tagName !== 'SELECT') return false;
        if (sel.multiple || (sel.size && sel.size > 1)) return false;
        if (sel.classList.contains('select2-hidden-accessible')) return false;
        if (sel.getAttribute('data-buscador') === 'no') return false;
        if (sel.matches(EXCLUIR)) return false;
        if (sel.getAttribute('data-buscador') === 'si') return true;
        // Campos de Operaciones que se muestran como texto en modo lectura (deshabilitados)
        // y pasan a lista al pulsar Editar: se dejan como están.
        if (sel.disabled && sel.matches(EDICION_EN_LINEA)) return false;
        return sel.options.length >= UMBRAL;
    }

    function coincide(texto, partes) {
        var t = normalizar(texto);
        for (var i = 0; i < partes.length; i++) {
            if (t.indexOf(partes[i]) === -1) return false;
        }
        return true;
    }

    function matcher(params, data) {
        ultimoTermino = params.term || '';
        var termino = normalizar(params.term);
        if (!termino) return data;
        var partes = termino.split(' ');
        // Grupos (por ejemplo ciudades agrupadas por país): se filtran sus opciones;
        // si el nombre del grupo coincide, se muestran todas.
        if (data.children && data.children.length) {
            if (coincide(data.text, partes)) return data;
            var hijos = data.children.filter(function (h) { return h.text && coincide(h.text, partes); });
            if (!hijos.length) return null;
            var copia = window.jQuery.extend({}, data, true);
            copia.children = hijos;
            return copia;
        }
        if (!data.text) return null;
        return coincide(data.text, partes) ? data : null;
    }

    var ultimoTermino = '';
    var contadorIds = 0;

    function sorter(resultados) {
        var t = normalizar(ultimoTermino);
        if (!t) return resultados;
        var ordenar = function (lista) {
            return lista.map(function (r, i) { return { r: r, i: i }; }).sort(function (a, b) {
                var pa = normalizar(a.r.text).indexOf(t) === 0 ? 0 : 1;
                var pb = normalizar(b.r.text).indexOf(t) === 0 ? 0 : 1;
                return pa - pb || a.i - b.i;
            }).map(function (x) {
                if (x.r.children && x.r.children.length) x.r.children = ordenar(x.r.children);
                return x.r;
            });
        };
        return ordenar(resultados.slice());
    }

    function templateResult(data) {
        if (!data.id && data.loading) return data.text;
        var t = normalizar(ultimoTermino);
        var texto = data.text || '';
        if (!t) return texto;
        // Resalta la primera palabra buscada (comparando sin tildes ni mayúsculas).
        var primera = t.split(' ')[0];
        var normal = normalizar(texto);
        var pos = normal.indexOf(primera);
        if (pos < 0 || normal.length !== texto.length) return texto;
        var span = document.createElement('span');
        span.appendChild(document.createTextNode(texto.substring(0, pos)));
        var b = document.createElement('span');
        b.className = 'bsq-coincide';
        b.textContent = texto.substring(pos, pos + primera.length);
        span.appendChild(b);
        span.appendChild(document.createTextNode(texto.substring(pos + primera.length)));
        return window.jQuery(span);
    }

    // Select2 avisa sus cambios con eventos jQuery, que no llegan a los
    // addEventListener('change') del código existente. Se reemplazan por eventos
    // nativos (que jQuery y los onchange="" también reciben, una sola vez).
    function avisarComoEventoNativo(sel, instancia) {
        if (!instancia || !instancia.$element) return;
        var $el = instancia.$element;
        var original = $el.trigger;
        $el.trigger = function (tipo) {
            if (tipo === 'change' || tipo === 'input') {
                sel.dispatchEvent(new Event(tipo, { bubbles: true }));
                return this;
            }
            return original.apply(this, arguments);
        };
    }

    // Si un script cambia el valor directamente, la lista visible se actualiza.
    function sincronizarCambiosProgramaticos(sel) {
        var $ = window.jQuery;
        var refrescar = function () { if (sel.classList.contains('select2-hidden-accessible')) $(sel).trigger('change.select2'); };
        ['value', 'selectedIndex'].forEach(function (prop) {
            var desc = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, prop);
            if (!desc || !desc.set) return;
            try {
                Object.defineProperty(sel, prop, {
                    configurable: true,
                    get: function () { return desc.get.call(this); },
                    set: function (v) { desc.set.call(this, v); refrescar(); }
                });
            } catch (e) { /* navegador sin soporte: se ignora */ }
        });
        // Si la pantalla oculta la lista (d-none o display:none), se oculta también el buscador.
        var reflejarVisibilidad = function () {
            var inst = window.jQuery(sel).data('select2');
            if (!inst || !inst.$container) return;
            var oculta = sel.classList.contains('d-none') || sel.style.display === 'none';
            inst.$container[0].style.display = oculta ? 'none' : '';
        };
        reflejarVisibilidad();
        if (window.MutationObserver) {
            var pendiente = false;
            new MutationObserver(function (muts) {
                var verVisibilidad = muts.some(function (m) { return m.type === 'attributes' && (m.attributeName === 'class' || m.attributeName === 'style') && m.target === sel; });
                if (verVisibilidad) reflejarVisibilidad();
                if (pendiente) return;
                pendiente = true;
                setTimeout(function () { pendiente = false; refrescar(); }, 0);
            }).observe(sel, { childList: true, subtree: true, attributes: true, attributeFilter: ['selected', 'class', 'style'] });
        }
    }

    function ancho(sel) {
        if (sel.style.width) return sel.style.width;
        if (sel.classList.contains('form-select') || sel.classList.contains('form-control') || sel.classList.contains('input-modern') || sel.classList.contains('filter-input')) return '100%';
        var cs = window.getComputedStyle(sel);
        if (cs.width && cs.width.indexOf('%') > 0) return cs.width;
        if (sel.offsetWidth > 0) return 'resolve';
        return '100%';
    }

    function mejorar(sel) {
        var $ = window.jQuery;
        if (!$ || !$.fn.select2 || !esCandidato(sel)) return;
        var $sel = $(sel);
        var modal = sel.closest('.modal, .offcanvas');
        // Select2 guarda su instancia según el id del elemento: si hay varias listas con el
        // mismo id (formularios repetidos en pestañas), cada una destruía a la anterior.
        if (sel.id && !sel.hasAttribute('data-select2-id') && window.CSS && CSS.escape
            && document.querySelectorAll('#' + CSS.escape(sel.id)).length > 1) {
            sel.setAttribute('data-select2-id', 'select2-data-bsq-' + (++contadorIds));
        }
        try {
            $sel.select2({
                width: ancho(sel),
                dropdownParent: modal ? $(modal) : $(document.body),
                matcher: matcher,
                sorter: sorter,
                templateResult: templateResult,
                language: {
                    noResults: function () { return 'Sin coincidencias'; },
                    searching: function () { return 'Buscando…'; }
                }
            });
        } catch (e) {
            if (window.console) console.warn('Buscador: no se pudo activar en', sel, e);
            return;
        }
        var inst = $sel.data('select2');
        if (inst) {
            inst.$container.addClass('bsq-auto');
            if (sel.classList.contains('input-modern')) inst.$container.addClass('bsq-modern');
            if (sel.classList.contains('form-select-sm')) inst.$container.addClass('bsq-sm');
            if (inst.$dropdown) inst.$dropdown.addClass('bsq-dropdown');
            avisarComoEventoNativo(sel, inst);
        }
        sincronizarCambiosProgramaticos(sel);
    }

    function mejorarEn(raiz) {
        if (!raiz || !raiz.querySelectorAll) return;
        if (raiz.tagName === 'SELECT') { mejorar(raiz); return; }
        raiz.querySelectorAll('select').forEach(mejorar);
    }

    function cargarSelect2(listo) {
        var $ = window.jQuery;
        if (!$) return;
        if ($.fn.select2) { listo(); return; }
        if (!document.querySelector('link[href*="select2"]')) {
            var css = document.createElement('link');
            css.rel = 'stylesheet';
            css.href = SELECT2_CSS;
            document.head.appendChild(css);
        }
        var s = document.createElement('script');
        s.src = SELECT2_JS;
        s.onload = function () {
            if (window.escalaVistaParchear) window.escalaVistaParchear();
            listo();
        };
        document.head.appendChild(s);
    }

    function iniciar() {
        var $ = window.jQuery;
        if (!$) return;
        // Solo hace falta cargar Select2 si en la pantalla hay alguna lista larga.
        var hay = Array.prototype.some.call(document.querySelectorAll('select'), esCandidato);
        var activar = function () {
            mejorarEn(document);
            $(document).on('select2:open', function (e) {
                ultimoTermino = '';
                // Con jQuery 3.6 el cursor no entra solo al buscador.
                setTimeout(function () {
                    var campo = document.querySelector('.select2-container--open .select2-search__field');
                    if (campo) campo.focus();
                }, 0);
            });
            if (window.MutationObserver) {
                var cola = [];
                var programado = false;
                new MutationObserver(function (muts) {
                    muts.forEach(function (m) {
                        m.addedNodes.forEach(function (n) {
                            if (n.nodeType !== 1) return;
                            // Opciones cargadas después (listas que se llenan por script): se revisa su lista.
                            if (n.tagName === 'OPTION' || n.tagName === 'OPTGROUP') { var sel = n.closest('select'); if (sel) cola.push(sel); return; }
                            if (n.tagName === 'SELECT' || (n.querySelector && n.querySelector('select'))) cola.push(n);
                        });
                    });
                    if (!cola.length || programado) return;
                    programado = true;
                    // Espera un momento: la pantalla puede querer configurar sus listas primero.
                    setTimeout(function () {
                        programado = false;
                        var nodos = cola.splice(0);
                        nodos.forEach(function (n) { if (document.body.contains(n)) mejorarEn(n); });
                    }, 120);
                }).observe(document.body, { childList: true, subtree: true });
            }
        };
        if (hay) cargarSelect2(activar);
        else {
            // Igual se vigilan listas que aparezcan después (paneles cargados por fetch).
            if (!window.MutationObserver) return;
            var obs = new MutationObserver(function () {
                var ahora = Array.prototype.some.call(document.querySelectorAll('select'), esCandidato);
                if (ahora) { obs.disconnect(); cargarSelect2(activar); }
            });
            obs.observe(document.body, { childList: true, subtree: true });
        }
    }

    window.buscadorSelects = { mejorar: mejorarEn };

    // Después de los scripts de la pantalla, para respetar sus propias listas Select2.
    if (document.readyState === 'complete') setTimeout(iniciar, 0);
    else window.addEventListener('load', function () { setTimeout(iniciar, 0); });
})();
