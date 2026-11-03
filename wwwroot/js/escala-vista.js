/* Densidad de la vista del ERP interno (oct-2026).
 * El layout aplica `zoom: 0.9` al <html> antes de pintar: con el navegador al 100%, todo
 * se ve como con el zoom del navegador en 90%. Este archivo corrige la posición de los
 * desplegables de Select2, que calculan su posición sin considerar ese zoom.
 */
(function () {
    'use strict';

    function escalaActual() {
        var z = parseFloat(document.documentElement.getAttribute('data-escala') || '1');
        return isNaN(z) || z <= 0 ? 1 : z;
    }

    // Select2 (adjunto al body) posiciona el desplegable con coordenadas de pantalla;
    // con zoom en la página hay que llevarlas a coordenadas de la página.
    function parchearSelect2(jq) {
        if (!jq || !jq.fn || !jq.fn.select2 || !jq.fn.select2.amd || jq.fn.select2.__escalaParcheado) return;
        jq.fn.select2.__escalaParcheado = true;
        try {
            var AttachBody = jq.fn.select2.amd.require('select2/dropdown/attachBody');
            var original = AttachBody.prototype._positionDropdown;
            AttachBody.prototype._positionDropdown = function () {
                original.apply(this, arguments);
                var z = escalaActual();
                if (z === 1 || !this.$dropdownContainer) return;
                // Select2 suma posiciones de pantalla (con zoom) y alturas de la página (sin
                // zoom): solo la parte de pantalla se divide por el zoom.
                var el = this.$dropdownContainer[0];
                var top = parseFloat(el.style.top), left = parseFloat(el.style.left);
                if (!isNaN(top)) {
                    var arriba = this.$dropdown && this.$dropdown.hasClass('select2-dropdown--above');
                    if (arriba) {
                        var hD = this.$dropdown.outerHeight(false);
                        el.style.top = ((top + hD) / z - hD) + 'px';
                    } else {
                        var hC = this.$container.outerHeight(false);
                        el.style.top = ((top - hC) / z + hC) + 'px';
                    }
                }
                if (!isNaN(left)) el.style.left = (left / z) + 'px';
            };
        } catch (err) {
            if (window.console) console.warn('Escala de vista: no se pudo ajustar Select2', err);
        }
    }

    function parchearTodo() {
        if (escalaActual() === 1) return;
        parchearSelect2(window.jQuery);
        if (window.$ && window.$ !== window.jQuery) parchearSelect2(window.$);
    }

    window.escalaVistaParchear = parchearTodo;
    parchearTodo();
    document.addEventListener('DOMContentLoaded', parchearTodo);
    window.addEventListener('load', parchearTodo);
})();
