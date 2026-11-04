/* Vitrina interactiva del portal (nov-2026). Ver Pages/Shared/_VitrinaPortal.cshtml. */
(function () {
    'use strict';
    var reducido = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var TITULOS = { seguimiento: 'Seguimiento', itinerarios: 'Itinerarios', cotizaciones: 'Nueva cotización', ubicaciones: 'Mis ubicaciones', documentos: 'Documentos', bl: 'Aprobar BL', facturas: 'Costos y facturas', soporte: 'Soporte' };
    var NOTIS = { seguimiento: 'Tu contenedor ingresó a puerto', itinerarios: 'Nueva salida publicada a Rotterdam', cotizaciones: 'Tu cotización fue respondida', ubicaciones: 'Planta registrada y validada', documentos: 'Certificado de origen disponible', bl: 'Tu BL está listo para aprobar', facturas: 'Nueva factura disponible', soporte: 'Nueva respuesta del equipo' };
    var ICONOS = { seguimiento: 'seguimiento', itinerarios: 'itinerarios', cotizaciones: 'cotizaciones', documentos: 'documentos', bl: 'documentos', facturas: 'facturas', soporte: 'soporte' };
    var CICLO = 5200;

    document.querySelectorAll('.vp').forEach(function (vp) {
        var botones = Array.prototype.slice.call(vp.querySelectorAll('.vp-fn'));
        var vistas = vp.querySelectorAll('.vp-vista');
        var titulo = vp.querySelector('#vpTituloVista'), noti = vp.querySelector('.vp-cel-noti'), notiTxt = vp.querySelector('#vpNoti');
        var vivo = vp.querySelector('#vpPantalla'), escena = vp.querySelector('.vp-escena'), disp = vp.querySelector('.vp-dispositivos');
        var orden = botones.map(function (b) { return b.dataset.vista; });
        var actual = -1, timer = 0, pausa = false, enPantalla = false, escrituras = [];
        vp.style.setProperty('--vp-ciclo', CICLO + 'ms');

        // Retraso escalonado de las piezas de cada vista (hitos, documentos, salidas).
        vistas.forEach(function (v) { v.querySelectorAll('li, .vp-salida').forEach(function (el, i) { el.style.setProperty('--i', i); }); });

        function escribir(vista) {
            escrituras.forEach(clearTimeout); escrituras = [];
            var campos = vista.querySelectorAll('[data-escribe]'), retraso = 250;
            campos.forEach(function (c) { c.textContent = ''; c.classList.remove('escribiendo'); });
            campos.forEach(function (c) {
                var txt = c.dataset.escribe;
                escrituras.push(setTimeout(function () { c.classList.add('escribiendo'); }, retraso));
                for (var k = 1; k <= txt.length; k++) {
                    (function (k) { escrituras.push(setTimeout(function () { c.textContent = txt.slice(0, k); }, retraso + k * 45)); })(k);
                }
                retraso += txt.length * 45 + 180;
                escrituras.push(setTimeout(function () { c.classList.remove('escribiendo'); }, retraso));
            });
        }

        function mostrar(i, porUsuario) {
            if (i === actual) return;
            actual = i;
            var id = orden[i];
            botones.forEach(function (b, k) { b.setAttribute('aria-selected', k === i ? 'true' : 'false'); b.tabIndex = k === i ? 0 : -1; });
            vistas.forEach(function (v) {
                var on = v.dataset.vista === id;
                if (on) { v.classList.remove('on'); void v.offsetWidth; v.classList.add('on'); } else v.classList.remove('on');
                if (on && id === 'cotizaciones') { if (reducido) v.querySelectorAll('[data-escribe]').forEach(function (c) { c.textContent = c.dataset.escribe; }); else escribir(v); }
            });
            if (titulo) titulo.textContent = TITULOS[id] || '';
            vp.querySelectorAll('.vp-app-lateral [data-ico]').forEach(function (ic) { ic.classList.toggle('act', ic.dataset.ico === ICONOS[id]); });
            if (notiTxt) { notiTxt.textContent = NOTIS[id] || ''; noti.classList.remove('nueva'); void noti.offsetWidth; noti.classList.add('nueva'); }
            if (vivo && porUsuario) vivo.textContent = 'Portal: ' + (TITULOS[id] || '');
            // reinicia la barra de avance
            var barra = botones[i].querySelector('.vp-fn-barra');
            if (barra) { barra.style.display = 'none'; void barra.offsetWidth; barra.style.display = ''; }
        }

        function programar() {
            clearTimeout(timer);
            var auto = !reducido && !pausa && enPantalla;
            vp.classList.toggle('auto', auto);
            if (auto) timer = setTimeout(function () { mostrar((actual + 1) % orden.length); programar(); }, CICLO);
        }

        botones.forEach(function (b, i) {
            b.addEventListener('click', function () { mostrar(i, true); });
            b.addEventListener('mouseenter', function () { mostrar(i, true); });
            b.addEventListener('keydown', function (e) {
                var n = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1 }[e.key];
                if (!n) return;
                e.preventDefault();
                var j = (actual + n + orden.length) % orden.length;
                mostrar(j, true); botones[j].focus();
            });
        });

        // Se detiene mientras la persona explora y sigue sola al salir.
        var grid = vp.querySelector('.vp-grid');
        grid.addEventListener('mouseenter', function () { pausa = true; programar(); });
        grid.addEventListener('mouseleave', function () { pausa = false; programar(); });
        grid.addEventListener('focusin', function () { pausa = true; programar(); });
        grid.addEventListener('focusout', function (e) { if (!grid.contains(e.relatedTarget)) { pausa = false; programar(); } });

        if ('IntersectionObserver' in window) {
            new IntersectionObserver(function (e) { enPantalla = e[0].isIntersecting; programar(); }, { threshold: 0.35 }).observe(vp);
        } else { enPantalla = true; }

        // Inclinación 3D suave según el mouse.
        if (!reducido && disp) {
            var raf = 0, rx = 6, ry = -8;
            vp.addEventListener('pointermove', function (e) {
                var r = escena.getBoundingClientRect();
                var x = (e.clientX - (r.left + r.width / 2)) / r.width, y = (e.clientY - (r.top + r.height / 2)) / r.height;
                rx = 6 - Math.max(-1, Math.min(1, y)) * 7; ry = -8 + Math.max(-1, Math.min(1, x)) * 12;
                if (!raf) raf = requestAnimationFrame(function () { raf = 0; disp.style.setProperty('--rx', rx.toFixed(2) + 'deg'); disp.style.setProperty('--ry', ry.toFixed(2) + 'deg'); });
            });
            vp.addEventListener('pointerleave', function () { disp.style.removeProperty('--rx'); disp.style.removeProperty('--ry'); });
        }

        mostrar(0);
        programar();
    });
})();
