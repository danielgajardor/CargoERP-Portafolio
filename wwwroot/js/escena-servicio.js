/* Escena animada de cada servicio (nov-2026). Ver Pages/Shared/_EscenaServicio.cshtml.
   Cambia data-paso en la ilustración; avanza sola mientras está en pantalla y se detiene
   mientras la persona explora las etapas (mouse, foco o teclado). */
(function () {
    'use strict';
    var reducido = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    document.querySelectorAll('.es').forEach(function (es) {
        var botones = Array.prototype.slice.call(es.querySelectorAll('.es-paso'));
        var escena = es.querySelector('.es-escena');
        var estadoN = es.querySelector('.es-estado-n'), estadoT = es.querySelector('.es-estado-t');
        var orden = botones.map(function (b) { return b.dataset.paso; });
        var textos = botones.map(function (b) { var s = b.querySelectorAll('span')[1]; return s ? s.childNodes[s.childNodes.length - 1].textContent.trim() : ''; });
        // La escena terrestre y la documental necesitan algo más de tiempo por etapa.
        var CICLO = es.classList.contains('es--terrestre') ? 6600 : es.classList.contains('es--documental') ? 5600 : 5400;
        var actual = -1, timer = 0, pausa = false, enPantalla = false, viaje = 0;
        es.style.setProperty('--es-ciclo', CICLO + 'ms');

        function mostrar(i) {
            if (i === actual) return;
            actual = i;
            var id = orden[i];
            botones.forEach(function (b, k) {
                b.setAttribute('aria-selected', k === i ? 'true' : 'false');
                b.tabIndex = k === i ? 0 : -1;
                b.classList.toggle('hecho', k < i);
            });
            escena.setAttribute('data-paso', id);
            escena.querySelectorAll('[data-ver]').forEach(function (el) {
                el.classList.toggle('on', el.dataset.ver.split(' ').indexOf(id) >= 0);
            });
            // Estaciones (terrestre): las anteriores salen por la izquierda, las siguientes esperan a la derecha.
            escena.querySelectorAll('[data-etapa]').forEach(function (el) {
                var j = orden.indexOf(el.dataset.etapa);
                el.setAttribute('data-pos', j === i ? 'ahora' : j < i ? 'antes' : 'despues');
            });
            clearTimeout(viaje);
            escena.classList.add('viajando');
            viaje = setTimeout(function () { escena.classList.remove('viajando'); }, 1400);
            if (estadoN) estadoN.textContent = 'Etapa ' + (i + 1) + ' de ' + orden.length;
            if (estadoT) estadoT.textContent = textos[i];
            var barra = botones[i].querySelector('.es-barra');
            if (barra) { barra.style.display = 'none'; void barra.offsetWidth; barra.style.display = ''; }
        }

        function programar() {
            clearTimeout(timer);
            var auto = !reducido && !pausa && enPantalla;
            es.classList.toggle('auto', auto);
            if (auto) timer = setTimeout(function () { mostrar((actual + 1) % orden.length); programar(); }, CICLO);
        }

        botones.forEach(function (b, i) {
            b.addEventListener('click', function () { mostrar(i); });
            b.addEventListener('mouseenter', function () { mostrar(i); });
            b.addEventListener('keydown', function (e) {
                var n = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1 }[e.key];
                if (!n) return;
                e.preventDefault();
                var j = (actual + n + orden.length) % orden.length;
                mostrar(j); botones[j].focus();
            });
        });

        var grid = es.querySelector('.es-grid');
        grid.addEventListener('mouseenter', function () { pausa = true; programar(); });
        grid.addEventListener('mouseleave', function () { pausa = false; programar(); });
        grid.addEventListener('focusin', function () { pausa = true; programar(); });
        grid.addEventListener('focusout', function (e) { if (!grid.contains(e.relatedTarget)) { pausa = false; programar(); } });

        if ('IntersectionObserver' in window) {
            new IntersectionObserver(function (e) { enPantalla = e[0].isIntersecting; programar(); }, { threshold: 0.35 }).observe(escena);
        } else { enPantalla = true; }

        mostrar(0);
        programar();
    });
})();
