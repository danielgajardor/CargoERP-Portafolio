// Tipo de emisión + Lugar de emisión del B/L (oct-2026).
// Comportamiento común de los bloques de Pages/Operaciones/_EmisionBlCampos.cshtml
// (data-embl="1"):
//  - Se elige UN tipo: Originales, Destino, Seawaybill, Telex Release, Express Release u Otro.
//  - Originales muestra y exige el Lugar de emisión (Valparaíso, San Antonio, Santiago, Coronel).
//  - Otro muestra y exige el texto.
//  - window.emisionBlValidar(bloque) devuelve null o el mensaje de error.
(function () {
    'use strict';

    var ORIGINALES = 'ORIGINALES';
    var OTRO = 'OTRO';

    function tipo(bloque) {
        var r = bloque.querySelector('input[data-embl-tipo]:checked');
        return r ? r.value : '';
    }

    function pintar(bloque) {
        bloque.querySelectorAll('label.embl-chip').forEach(function (chip) {
            var inp = chip.querySelector('input');
            var on = !!(inp && inp.checked);
            chip.classList.toggle('is-on', on);
            var ic = chip.querySelector('i');
            if (ic) ic.className = 'bi ' + (on ? 'bi-check-circle-fill' : 'bi-circle');
        });
        bloque.querySelectorAll('label.embl-pill').forEach(function (pill) {
            var inp = pill.querySelector('input');
            pill.classList.toggle('is-on', !!(inp && inp.checked));
        });
    }

    function refrescar(bloque) {
        if (!bloque) return;
        var soloLugar = bloque.getAttribute('data-solo-lugar') === 'true';
        var apagado = bloque.getAttribute('data-embl-off') === 'true';
        var t = tipo(bloque);

        var otro = bloque.querySelector('.embl-otro');
        if (otro) {
            otro.classList.toggle('d-none', t !== OTRO);
            otro.disabled = apagado || t !== OTRO;
        }

        var conLugar = soloLugar || t === ORIGINALES;
        var lugarWrap = bloque.querySelector('.embl-lugar');
        if (lugarWrap) lugarWrap.classList.toggle('d-none', !conLugar);
        bloque.querySelectorAll('input[data-embl-lugar]').forEach(function (i) {
            i.disabled = apagado || !conLugar;
            if (!conLugar) i.checked = false;
        });

        pintar(bloque);
        bloque.dispatchEvent(new CustomEvent('embl:change', { bubbles: true, detail: { tipo: t } }));
    }

    // Activa/desactiva todo el bloque (lo usa Corrección de BL al cambiar el motivo).
    window.emisionBlHabilitar = function (bloque, habilitar) {
        if (!bloque) return;
        bloque.setAttribute('data-embl-off', habilitar ? 'false' : 'true');
        bloque.querySelectorAll('input[data-embl-tipo]').forEach(function (i) { i.disabled = !habilitar; });
        refrescar(bloque);
    };

    window.emisionBlTipo = function (bloque) { return bloque ? tipo(bloque) : ''; };

    window.emisionBlValidar = function (bloque) {
        if (!bloque || bloque.getAttribute('data-embl-off') === 'true') return null;
        var soloLugar = bloque.getAttribute('data-solo-lugar') === 'true';
        var t = tipo(bloque);
        bloque.querySelectorAll('.is-invalid').forEach(function (el) { el.classList.remove('is-invalid'); });

        if (!soloLugar && bloque.getAttribute('data-obligatorio') === 'true' && !t) {
            var chips = bloque.querySelector('.embl-chips');
            if (chips) chips.classList.add('is-invalid');
            return 'Selecciona el Tipo de emisión del B/L.';
        }
        var otro = bloque.querySelector('.embl-otro');
        if (!soloLugar && t === OTRO && otro && !otro.value.trim()) {
            otro.classList.add('is-invalid');
            return 'Indica cuál es el otro tipo de emisión.';
        }
        if ((soloLugar || t === ORIGINALES) && !bloque.querySelector('input[data-embl-lugar]:checked')) {
            var pills = bloque.querySelector('.embl-chips--lugar');
            if (pills) pills.classList.add('is-invalid');
            return soloLugar ? 'Selecciona el nuevo Lugar de emisión.' : 'Selecciona el Lugar de emisión de los Originales.';
        }
        return null;
    };

    // Texto legible para resúmenes: "Originales (SANTIAGO)", "Otro: XYZ".
    window.emisionBlResumen = function (bloque) {
        if (!bloque) return '';
        var r = bloque.querySelector('input[data-embl-tipo]:checked');
        if (!r) return '';
        if (r.value === OTRO) {
            var o = bloque.querySelector('.embl-otro');
            return 'Otro: ' + ((o && o.value.trim().toUpperCase()) || '-');
        }
        var txt = (r.closest('label') && r.closest('label').innerText.trim()) || r.value;
        if (r.value === ORIGINALES) {
            var l = bloque.querySelector('input[data-embl-lugar]:checked');
            if (l) txt += ' (' + l.value + ')';
        }
        return txt;
    };

    window.emisionBlRefrescar = refrescar;

    // Fase de captura (true): se ejecuta antes que cualquier otro script de la página
    // (ej. el asistente de Create) pueda cortar la propagación del evento, que es lo que
    // dejaba el Lugar de emisión sin aparecer al marcar Originales.
    function alCambiar(e) {
        var t = e.target;
        if (!t || !t.closest) return;
        var bloque = t.closest('[data-embl]');
        if (!bloque) return;
        if (t.matches('input[data-embl-tipo]')) refrescar(bloque);
        if (t.matches('input[data-embl-lugar]')) pintar(bloque);
        if (t.classList.contains('is-invalid') && t.value) t.classList.remove('is-invalid');
        var grupo = t.closest('.embl-chips.is-invalid');
        if (grupo) grupo.classList.remove('is-invalid');
    }
    document.addEventListener('change', alCambiar, true);
    // Respaldo: al hacer clic en un chip, se refresca en el siguiente ciclo (por si el
    // navegador o un script marca el radio sin disparar "change").
    document.addEventListener('click', function (e) {
        var chip = e.target && e.target.closest ? e.target.closest('[data-embl] label.embl-chip, [data-embl] label.embl-pill') : null;
        if (!chip) return;
        var bloque = chip.closest('[data-embl]');
        setTimeout(function () { refrescar(bloque); }, 0);
    }, true);

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-embl]').forEach(refrescar);
    });
})();
