/* Sitio público de Atilson (nov-2026): navegación, indicadores, ticker de noticias,
 * fondo animado de puntos conectados y asistente. Lo usa Pages/Shared/_LayoutPublico.cshtml. */
(function () {
    'use strict';
    var reducido = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // ---------- 1. Navegación: fija y blanca al bajar ----------
    var navbar = document.getElementById('navbar');
    var logo = document.getElementById('logo-img');
    function navbarSegunScroll() {
        if (!navbar) return;
        var bajo = window.scrollY > 40;
        navbar.classList.toggle('scrolled', bajo);
        if (logo) {
            var src = bajo ? logo.dataset.logoColor : logo.dataset.logoClaro;
            if (src && logo.getAttribute('src') !== src) logo.setAttribute('src', src);
        }
    }
    window.addEventListener('scroll', navbarSegunScroll, { passive: true });
    navbarSegunScroll();

    // Menú "Servicios": se abre con clic o teclado, se cierra con Escape o clic afuera.
    document.querySelectorAll('[data-menu]').forEach(function (btn) {
        var panel = document.getElementById(btn.getAttribute('aria-controls'));
        if (!panel) return;
        function abrir(si) { panel.hidden = !si; btn.setAttribute('aria-expanded', si ? 'true' : 'false'); }
        var conMouse = window.matchMedia && window.matchMedia('(hover: hover) and (pointer: fine)').matches;
        // Con mouse el panel ya se abrió al pasar: el clic no lo vuelve a cerrar (salvo con teclado).
        btn.addEventListener('click', function (e) { e.stopPropagation(); abrir(conMouse && e.detail > 0 ? true : panel.hidden); });
        document.addEventListener('click', function (e) { if (!panel.hidden && !panel.contains(e.target)) abrir(false); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !panel.hidden) { abrir(false); btn.focus(); } });
        // Con mouse también se abre al pasar por encima (y se cierra al salir, con una pausa corta).
        var cont = btn.parentElement, t = 0;
        if (cont && conMouse) {
            cont.addEventListener('mouseenter', function () { clearTimeout(t); t = setTimeout(function () { abrir(true); }, 80); });
            cont.addEventListener('mouseleave', function () { clearTimeout(t); t = setTimeout(function () { abrir(false); }, 220); });
        }
    });

    // Menú en celular (pantalla completa).
    var menuMovil = document.getElementById('menuMovil');
    var btnMenu = document.getElementById('btnMenuMovil');
    if (menuMovil && btnMenu) {
        var cerrarMenu = document.getElementById('btnCerrarMenu');
        var mostrarMenu = function (si) {
            menuMovil.hidden = !si; btnMenu.setAttribute('aria-expanded', si ? 'true' : 'false');
            document.body.style.overflow = si ? 'hidden' : '';
            (si ? cerrarMenu : btnMenu).focus();
        };
        btnMenu.addEventListener('click', function () { mostrarMenu(true); });
        cerrarMenu.addEventListener('click', function () { mostrarMenu(false); });
        menuMovil.querySelectorAll('a').forEach(function (a) { a.addEventListener('click', function () { mostrarMenu(false); }); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !menuMovil.hidden) mostrarMenu(false); });
    }

    // ---------- 2. Indicadores (UF, USD, EUR) desde el servidor ----------
    // Mismos valores que usa Control Financiero (Services/IndicadoresEconomicosService.cs).
    var ind = document.querySelector('[data-indicadores]');
    if (ind) {
        var fmt = new Intl.NumberFormat('es-CL', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        fetch(ind.getAttribute('data-indicadores'), { headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
            .then(function (d) {
                var respaldo = false, fechas = [], fuentes = [];
                [['uf', 'top-uf'], ['dolar', 'top-usd'], ['euro', 'top-eur']].forEach(function (p) {
                    var v = d[p[0]], el = document.getElementById(p[1]);
                    if (!el) return;
                    el.textContent = v && v.valor ? '$' + fmt.format(v.valor) : 'N/D';
                    if (v && v.respaldo) respaldo = true;
                    if (v && v.fecha) fechas.push(v.fecha);
                    if (v && v.fuente && fuentes.indexOf(v.fuente) < 0) fuentes.push(v.fuente);
                });
                ind.classList.toggle('respaldo', respaldo);
                ind.title = (respaldo ? 'Último valor disponible (sin conexión con la fuente)' : 'Valores del día' + (fechas.length ? ' (' + fechas[0] + ')' : ''))
                    + (fuentes.length === 1 && !respaldo ? ' · Fuente: ' + fuentes[0] : '');
            })
            .catch(function () {
                ['top-uf', 'top-usd', 'top-eur'].forEach(function (id) { var el = document.getElementById(id); if (el) el.textContent = 'N/D'; });
            });
    }

    // ---------- 3. Ticker de la última noticia (Mundo Marítimo) ----------
    var ticker = document.getElementById('top-news-ticker');
    if (ticker && ticker.dataset.url) {
        fetch(ticker.dataset.url, { headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
            .then(function (d) {
                var lista = (d && d.noticias || []).filter(function (n) { return n.titulo && n.enlace; }).slice(0, 5);
                if (!lista.length) throw new Error('sin noticias');
                var i = 0;
                var mostrar = function () { ticker.textContent = lista[i].titulo; ticker.href = lista[i].enlace; };
                mostrar();
                if (lista.length > 1 && !reducido) setInterval(function () {
                    ticker.style.opacity = 0;
                    setTimeout(function () { i = (i + 1) % lista.length; mostrar(); ticker.style.opacity = 1; }, 400);
                }, 6000);
            })
            .catch(function () { document.querySelectorAll('[data-ticker]').forEach(function (el) { el.style.display = 'none'; }); });
    }

    // ---------- 4. Fondo animado: red de rutas ----------
    // Una red de puertos en tres planos de profundidad (los lejanos más tenues y lentos;
    // al mover el mouse los planos se desplazan distinto y la red gana volumen). Cada puerto
    // se une a sus vecinos por rutas curvas punteadas que fluyen; algunos puertos llevan su
    // código (SAI, RTM, SHA…) y entre ellos viajan envíos de larga distancia que dibujan su
    // ruta al avanzar. Por las rutas cortas circulan cargas con estela que hacen latir el
    // puerto de llegada. El mouse atrae suavemente los puertos cercanos y traza rutas hacia
    // él; un clic dispara envíos desde el puerto más cercano. Se detiene fuera de pantalla o
    // con la pestaña oculta; con "reducir movimiento" queda una imagen fija.
    function Plexus(cont) {
        var canvas = document.createElement('canvas');
        cont.appendChild(canvas);
        var ctx = canvas.getContext('2d');
        var dpr = Math.min(window.devicePixelRatio || 1, 2);
        var w = 0, h = 0, puntos = [], rutas = [], cargas = [], pulsos = [], largos = [];
        var visible = true, raf = 0, ultimo = 0, puntero = null, mira = { x: 0, y: 0 }, proxCarga = 0, proxLargo = 2, reloj = 0, entrada = 0;
        var dens = parseFloat(cont.dataset.plexus || '1') || 1;
        var LILA = '195,182,245', CLARO = '236,232,255';
        var CODIGOS = ['SAI', 'VAP', 'RTM', 'SHA', 'LAX', 'NGB', 'HAM', 'BUS', 'CLL', 'ANR', 'YTN', 'MZT'];
        var PLANOS = [0.45, 0.72, 1];

        function iniciar() {
            var r = cont.getBoundingClientRect();
            w = Math.max(1, r.width); h = Math.max(1, r.height);
            canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            var n = Math.round(Math.min(120, Math.max(28, (w * h) / 10500)) * dens);
            puntos = []; cargas = []; pulsos = []; largos = [];
            var codigos = CODIGOS.slice().sort(function () { return Math.random() - 0.5; });
            var hubs = Math.min(codigos.length, Math.max(4, Math.round(n / 14)));
            for (var i = 0; i < n; i++) {
                var z = PLANOS[i < hubs ? 2 : Math.floor(Math.random() * 3)];
                var ang = Math.random() * Math.PI * 2, vel = (4 + Math.random() * 7) * z;
                puntos.push({ x: Math.random() * w, y: Math.random() * h, vx: Math.cos(ang) * vel, vy: Math.sin(ang) * vel, z: z,
                    hub: i < hubs ? codigos[i] : null, puerto: i < hubs || Math.random() < 0.12, r: (0.8 + Math.random() * 0.8) * (0.6 + z * 0.6),
                    brillo: 0, fase: Math.random() * 6.28, px: 0, py: 0 });
            }
        }

        // Posición en pantalla con el desplazamiento de su plano (paralaje).
        function pos(p) { return { x: p.x + mira.x * p.z, y: p.y + mira.y * p.z }; }

        function calcularRutas(dist) {
            var vistas = {};
            rutas = [];
            for (var i = 0; i < puntos.length; i++) {
                var a = puntos[i], m1 = -1, d1 = dist * a.z, m2 = -1, d2 = dist * a.z;
                for (var j = 0; j < puntos.length; j++) {
                    if (i === j || Math.abs(puntos[j].z - a.z) > 0.3) continue;
                    var dx = a.px - puntos[j].px, dy = a.py - puntos[j].py, d = Math.sqrt(dx * dx + dy * dy);
                    if (d < d1) { m2 = m1; d2 = d1; m1 = j; d1 = d; } else if (d < d2) { m2 = j; d2 = d; }
                }
                [[m1, d1], [m2, d2]].forEach(function (par) {
                    if (par[0] < 0) return;
                    var k = Math.min(i, par[0]) + '-' + Math.max(i, par[0]);
                    if (vistas[k]) return;
                    vistas[k] = 1;
                    rutas.push({ a: Math.min(i, par[0]), b: Math.max(i, par[0]), d: par[1], lim: dist * Math.min(a.z, puntos[par[0]].z) });
                });
            }
        }

        function control(a, b, ia, ib, curva) {
            var mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2, dx = b.x - a.x, dy = b.y - a.y;
            var s = ((ia * 7 + ib * 13) % 2 ? 1 : -1) * (curva || 0.22);
            return { x: mx - dy * s, y: my + dx * s };
        }
        function enCurva(a, c, b, t) {
            var u = 1 - t;
            return { x: u * u * a.x + 2 * u * t * c.x + t * t * b.x, y: u * u * a.y + 2 * u * t * c.y + t * t * b.y };
        }
        function punto(i) { var p = puntos[i]; return { x: p.px, y: p.py }; }

        function lanzarCarga(desde) {
            if (!rutas.length) return;
            var cand = desde != null ? rutas.filter(function (r) { return r.a === desde || r.b === desde; }) : rutas;
            if (desde == null && puntero) {
                var cerca = rutas.filter(function (r) { var p = puntos[r.a]; return Math.hypot(p.px - puntero.x, p.py - puntero.y) < 220; });
                if (cerca.length) cand = cerca;
            }
            if (!cand.length) return;
            var r = cand[Math.floor(Math.random() * cand.length)];
            var ida = desde != null ? r.a === desde : Math.random() < 0.5;
            cargas.push({ de: ida ? r.a : r.b, a: ida ? r.b : r.a, t: 0, dur: 1.6 + Math.random() * 1.4 });
        }
        function lanzarLargo() {
            var hubs = [];
            puntos.forEach(function (p, i) { if (p.hub) hubs.push(i); });
            if (hubs.length < 2) return;
            var a = hubs[Math.floor(Math.random() * hubs.length)], b = a;
            while (b === a) b = hubs[Math.floor(Math.random() * hubs.length)];
            largos.push({ de: a, a: b, t: 0, dur: 4 + Math.random() * 2, fin: 0 });
        }

        function dibujar() {
            var dist = Math.min(200, Math.max(130, w / 7.5));
            for (var q = 0; q < puntos.length; q++) { var pp0 = pos(puntos[q]); puntos[q].px = pp0.x; puntos[q].py = pp0.y; }
            calcularRutas(dist);
            ctx.clearRect(0, 0, w, h);
            ctx.globalAlpha = Math.min(1, entrada);

            // Rutas cortas: curvas punteadas que avanzan (más tenues en los planos lejanos).
            ctx.setLineDash([3, 5]);
            ctx.lineDashOffset = -reloj * 14;
            for (var i = 0; i < rutas.length; i++) {
                var r = rutas[i], A = punto(r.a), B = punto(r.b), C = control(A, B, r.a, r.b);
                var z = Math.min(puntos[r.a].z, puntos[r.b].z);
                var br = Math.max(puntos[r.a].brillo, puntos[r.b].brillo);
                var al = ((1 - r.d / r.lim) * 0.55 + 0.24) * (0.5 + z * 0.5) + br * 0.3;
                ctx.lineWidth = 0.7 + z * 0.7;
                ctx.strokeStyle = 'rgba(' + LILA + ',' + Math.min(0.95, al).toFixed(3) + ')';
                ctx.beginPath(); ctx.moveTo(A.x, A.y); ctx.quadraticCurveTo(C.x, C.y, B.x, B.y); ctx.stroke();
            }
            if (puntero) {
                ctx.lineWidth = 1.1;
                for (var k = 0; k < puntos.length; k++) {
                    var p = puntos[k], dp = Math.hypot(p.px - puntero.x, p.py - puntero.y);
                    if (dp < 170 && p.z > 0.5) {
                        var cp = control({ x: p.px, y: p.py }, puntero, k, 3);
                        ctx.strokeStyle = 'rgba(' + LILA + ',' + ((1 - dp / 170) * 0.75).toFixed(3) + ')';
                        ctx.beginPath(); ctx.moveTo(p.px, p.py); ctx.quadraticCurveTo(cp.x, cp.y, puntero.x, puntero.y); ctx.stroke();
                    }
                }
            }
            ctx.setLineDash([]);

            // Envíos de larga distancia entre puertos con código: la ruta se dibuja al avanzar.
            for (var L = 0; L < largos.length; L++) {
                var lg = largos[L], LA = punto(lg.de), LB = punto(lg.a), LC = control(LA, LB, lg.de, lg.a, 0.32);
                var hasta = Math.min(1, lg.t), desvanecer = lg.fin;
                ctx.lineWidth = 1.4;
                ctx.strokeStyle = 'rgba(' + CLARO + ',' + (0.55 * (1 - desvanecer)).toFixed(3) + ')';
                ctx.beginPath();
                for (var st = 0; st <= 40; st++) { var pt = enCurva(LA, LC, LB, (st / 40) * hasta); if (st === 0) ctx.moveTo(pt.x, pt.y); else ctx.lineTo(pt.x, pt.y); }
                ctx.stroke();
                if (lg.t < 1) {
                    var cab = enCurva(LA, LC, LB, hasta);
                    ctx.fillStyle = 'rgba(' + LILA + ',0.3)'; ctx.beginPath(); ctx.arc(cab.x, cab.y, 8, 0, 6.283); ctx.fill();
                    ctx.fillStyle = 'rgba(' + CLARO + ',1)'; ctx.beginPath(); ctx.arc(cab.x, cab.y, 3, 0, 6.283); ctx.fill();
                }
            }

            // Pulsos de llegada (y ondas de un clic).
            for (var u = 0; u < pulsos.length; u++) {
                var pu = pulsos[u], P = punto(pu.i), tt = pu.t;
                ctx.lineWidth = 1.2;
                ctx.strokeStyle = 'rgba(' + LILA + ',' + (0.8 * (1 - tt)).toFixed(3) + ')';
                ctx.beginPath(); ctx.arc(P.x, P.y, 3 + tt * (pu.grande ? 60 : 16), 0, 6.283); ctx.stroke();
            }

            // Cargas en rutas cortas, con estela.
            for (var m = 0; m < cargas.length; m++) {
                var cg = cargas[m], CA = punto(cg.de), CB = punto(cg.a), CC = control(CA, CB, Math.min(cg.de, cg.a), Math.max(cg.de, cg.a));
                for (var e2 = 5; e2 >= 0; e2--) {
                    var te = Math.max(0, cg.t - e2 * 0.035), pe = enCurva(CA, CC, CB, te);
                    if (e2 === 0) { ctx.fillStyle = 'rgba(' + LILA + ',0.25)'; ctx.beginPath(); ctx.arc(pe.x, pe.y, 6, 0, 6.283); ctx.fill(); }
                    ctx.fillStyle = 'rgba(' + CLARO + ',' + (e2 === 0 ? 1 : 0.6 * (1 - e2 / 6)).toFixed(3) + ')';
                    ctx.beginPath(); ctx.arc(pe.x, pe.y, e2 === 0 ? 2.5 : 1.9 - e2 * 0.2, 0, 6.283); ctx.fill();
                }
            }

            // Puertos y puntos; los puertos con código muestran su sigla.
            ctx.font = '600 10px Archivo, system-ui, sans-serif';
            for (var zi = 0; zi < puntos.length; zi++) {
                var pp = puntos[zi], b2 = pp.brillo, tw = 0.85 + 0.15 * Math.sin(reloj * 1.6 + pp.fase);
                if (pp.puerto) {
                    ctx.lineWidth = 1 + pp.z * 0.5;
                    ctx.strokeStyle = 'rgba(' + LILA + ',' + ((0.45 + pp.z * 0.35) * tw + b2 * 0.3).toFixed(3) + ')';
                    ctx.beginPath(); ctx.arc(pp.px, pp.py, (2.4 + pp.z * 1.6) + b2 * 2, 0, 6.283); ctx.stroke();
                    ctx.fillStyle = 'rgba(' + CLARO + ',' + (0.6 + pp.z * 0.4).toFixed(3) + ')';
                    ctx.beginPath(); ctx.arc(pp.px, pp.py, 1.2, 0, 6.283); ctx.fill();
                    if (pp.hub) {
                        ctx.fillStyle = 'rgba(' + LILA + ',' + (0.7 + b2 * 0.3).toFixed(3) + ')';
                        ctx.fillText(pp.hub, pp.px + 8, pp.py - 7);
                    }
                } else {
                    ctx.fillStyle = 'rgba(' + LILA + ',' + ((0.5 + pp.z * 0.45) * tw + b2 * 0.3).toFixed(3) + ')';
                    ctx.beginPath(); ctx.arc(pp.px, pp.py, pp.r + b2, 0, 6.283); ctx.fill();
                }
            }
            ctx.globalAlpha = 1;
        }

        var objetivo = { x: 0, y: 0 };
        function mover(dt) {
            reloj += dt; entrada += dt / 1.4;
            // Paralaje: los planos siguen al mouse con suavidad.
            mira.x += (objetivo.x - mira.x) * Math.min(1, dt * 3);
            mira.y += (objetivo.y - mira.y) * Math.min(1, dt * 3);
            for (var i = 0; i < puntos.length; i++) {
                var p = puntos[i];
                if (puntero && p.z > 0.5) { // atracción suave hacia el mouse
                    var dx = puntero.x - p.px, dy = puntero.y - p.py, d = Math.hypot(dx, dy);
                    if (d < 180 && d > 30) { p.vx += dx / d * 14 * dt * p.z; p.vy += dy / d * 14 * dt * p.z; p.brillo = Math.max(p.brillo, (1 - d / 180) * 0.8); }
                }
                var v = Math.hypot(p.vx, p.vy), max = 16 * p.z + 4, min = 3 * p.z;
                if (v > max) { p.vx *= 0.97; p.vy *= 0.97; } else if (v < min) { p.vx *= 1.02; p.vy *= 1.02; }
                p.x += p.vx * dt; p.y += p.vy * dt;
                if (p.x < -30) p.x = w + 30; else if (p.x > w + 30) p.x = -30;
                if (p.y < -30) p.y = h + 30; else if (p.y > h + 30) p.y = -30;
                p.brillo = Math.max(0, p.brillo - dt * 1.4);
            }
            for (var c = cargas.length - 1; c >= 0; c--) {
                var cg = cargas[c];
                cg.t += dt / cg.dur;
                if (cg.t >= 1) { pulsos.push({ i: cg.a, t: 0 }); puntos[cg.a].brillo = 1; cargas.splice(c, 1); }
            }
            for (var l = largos.length - 1; l >= 0; l--) {
                var lg = largos[l];
                if (lg.t < 1) { lg.t += dt / lg.dur; if (lg.t >= 1) { pulsos.push({ i: lg.a, t: 0, grande: true }); puntos[lg.a].brillo = 1; } }
                else { lg.fin += dt / 1.2; if (lg.fin >= 1) largos.splice(l, 1); }
            }
            for (var q = pulsos.length - 1; q >= 0; q--) { pulsos[q].t += dt / (pulsos[q].grande ? 1.6 : 1.1); if (pulsos[q].t >= 1) pulsos.splice(q, 1); }
            proxCarga -= dt; proxLargo -= dt;
            if (proxCarga <= 0 && cargas.length < 8) { lanzarCarga(); proxCarga = 0.4 + Math.random() * 0.6; }
            if (proxLargo <= 0 && largos.length < 2) { lanzarLargo(); proxLargo = 3.5 + Math.random() * 3; }
        }

        function cuadro(t) {
            raf = 0;
            if (!visible || document.hidden) return;
            var dt = ultimo ? Math.min(0.05, (t - ultimo) / 1000) : 0;
            ultimo = t;
            mover(dt); dibujar();
            raf = requestAnimationFrame(cuadro);
        }
        function arrancar() { if (!raf && !reducido) { ultimo = 0; raf = requestAnimationFrame(cuadro); } }

        iniciar();
        if (reducido) { entrada = 1; dibujar(); return; }
        dibujar();
        if ('IntersectionObserver' in window) {
            new IntersectionObserver(function (e) { visible = e[0].isIntersecting; if (visible) arrancar(); }).observe(cont);
        }
        document.addEventListener('visibilitychange', function () { if (!document.hidden) arrancar(); });
        var tr;
        window.addEventListener('resize', function () { clearTimeout(tr); tr = setTimeout(function () { iniciar(); entrada = 1; dibujar(); }, 150); });
        var zona = cont.parentElement;
        zona.addEventListener('pointermove', function (e) {
            var r = cont.getBoundingClientRect();
            puntero = { x: e.clientX - r.left, y: e.clientY - r.top };
            objetivo = { x: -(puntero.x - w / 2) * 0.03, y: -(puntero.y - h / 2) * 0.03 };
        });
        zona.addEventListener('pointerleave', function () { puntero = null; objetivo = { x: 0, y: 0 }; });
        // Clic en el fondo: envíos desde el puerto más cercano y una onda que empuja la red.
        zona.addEventListener('click', function (e) {
            if (e.target.closest('a, button, input, select, textarea, label, form')) return;
            var r = cont.getBoundingClientRect(), x = e.clientX - r.left, y = e.clientY - r.top, mejor = -1, md = 1e9;
            puntos.forEach(function (p, i) { var d = Math.hypot(p.px - x, p.py - y); if (d < md && p.z > 0.5) { md = d; mejor = i; } });
            if (mejor < 0) return;
            pulsos.push({ i: mejor, t: 0, grande: true }); puntos[mejor].brillo = 1;
            for (var k = 0; k < 4; k++) lanzarCarga(mejor);
            puntos.forEach(function (p) { var dx = p.px - x, dy = p.py - y, d = Math.hypot(dx, dy); if (d < 220 && d > 1) { p.vx += dx / d * 40 * (1 - d / 220); p.vy += dy / d * 40 * (1 - d / 220); } });
            arrancar();
        });
        arrancar();
    }
    document.querySelectorAll('[data-plexus]').forEach(function (el) { try { Plexus(el); } catch (e) { /* sin canvas: queda el fondo */ } });

    // ---------- Video de la empresa: YouTube se carga recién al presionar "Reproducir" ----------
    document.querySelectorAll('[data-video-youtube]').forEach(function (marco) {
        var btn = marco.querySelector('.video-emp-portada');
        if (!btn) return;
        btn.addEventListener('click', function () {
            var f = document.createElement('iframe');
            f.src = 'https://www.youtube-nocookie.com/embed/' + encodeURIComponent(marco.dataset.videoYoutube) + '?autoplay=1&rel=0&modestbranding=1&playsinline=1';
            f.title = btn.getAttribute('aria-label') || 'Video';
            f.allow = 'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share';
            f.allowFullscreen = true;
            f.referrerPolicy = 'strict-origin-when-cross-origin';
            marco.replaceChildren(f);
            f.focus();
        });
    });

    // ---------- 5. Asistente del sitio ----------
    // Responde el servidor (Services/AsistenteWebService.cs). La conversación y su "memoria"
    // (estado) se guardan en sessionStorage para seguir al cambiar de página del sitio.
    (function () {
        var panel = document.getElementById('asistente'), cont = document.getElementById('flotantes');
        var abrir = document.getElementById('asisAbrir'), cerrar = document.getElementById('asisCerrar');
        var reiniciar = document.getElementById('asisReiniciar');
        var msgs = document.getElementById('asisMsgs'), form = document.getElementById('asisForm');
        var txt = document.getElementById('asisTexto'), enviar = document.getElementById('asisEnviar');
        var sug = document.getElementById('asisSugerencias');
        if (!panel || !form) return;
        var CLAVE = 'atilson-asistente-v2';
        var bienvenida = msgs.innerHTML, sugInicio = Array.prototype.map.call(sug.querySelectorAll('button'), function (b) { return b.textContent; });
        var historial = [], estado = null, registro = [], ocupado = false;

        function guardar() {
            try { sessionStorage.setItem(CLAVE, JSON.stringify({ historial: historial.slice(-12), estado: estado, registro: registro.slice(-40), abierto: !panel.hidden })); } catch (e) { }
        }
        function mostrar(si, sinFoco) {
            panel.hidden = !si; cont.classList.toggle('abierto', si);
            abrir.setAttribute('aria-expanded', si ? 'true' : 'false');
            abrir.setAttribute('aria-label', si ? 'Cerrar asistente' : 'Abrir asistente');
            if (!sinFoco) { if (si) setTimeout(function () { txt.focus(); }, 50); else abrir.focus(); }
            guardar();
        }
        abrir.addEventListener('click', function () { mostrar(panel.hidden); });
        cerrar.addEventListener('click', function () { mostrar(false); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !panel.hidden) mostrar(false); });

        function bajar() { msgs.scrollTop = msgs.scrollHeight; }
        function burbuja(texto, clase) {
            var d = document.createElement('div'); d.className = 'asis-msg ' + clase; d.textContent = texto;
            msgs.appendChild(d); bajar(); return d;
        }
        function acciones(lista) {
            if (!lista || !lista.length) return;
            var w = document.createElement('div'); w.className = 'asis-acciones';
            lista.forEach(function (a) {
                var el = document.createElement('a'); el.className = 'asis-accion'; el.href = a.url;
                if (/^https?:/.test(a.url)) { el.target = '_blank'; el.rel = 'noopener'; }
                if (a.url.indexOf('#') >= 0) el.addEventListener('click', function () { mostrar(false); });
                var i = document.createElement('i'); i.className = 'bi ' + (a.icono || 'bi-arrow-right'); i.setAttribute('aria-hidden', 'true');
                el.appendChild(i); el.appendChild(document.createTextNode(a.texto)); w.appendChild(el);
            });
            msgs.appendChild(w); bajar();
        }
        function sugerencias(lista) {
            sug.innerHTML = '';
            (lista || []).slice(0, 6).forEach(function (t) {
                var b = document.createElement('button'); b.type = 'button'; b.textContent = t;
                b.addEventListener('click', function () { preguntar(b.textContent); }); // ya traducida si el sitio está en inglés
                sug.appendChild(b);
            });
            sug.hidden = !sug.children.length;
            bajar(); // las sugerencias ocupan espacio: el último mensaje debe quedar a la vista
        }
        function pintar(item) {
            if (item.tipo === 'yo') burbuja(item.texto, 'yo');
            else { burbuja(item.texto, 'bot'); acciones(item.acciones); }
        }

        function preguntar(texto) {
            texto = (texto || '').trim();
            if (!texto || ocupado) return;
            ocupado = true; enviar.disabled = true; sugerencias([]);
            burbuja(texto, 'yo'); historial.push({ rol: 'user', texto: texto }); registro.push({ tipo: 'yo', texto: texto });
            txt.value = ''; txt.style.height = '';
            var esc = document.createElement('div'); esc.className = 'asis-escribiendo'; esc.setAttribute('aria-label', 'Escribiendo');
            esc.innerHTML = '<i></i><i></i><i></i>'; msgs.appendChild(esc); bajar();
            var fd = new FormData();
            fd.append('historial', JSON.stringify(historial.slice(-12)));
            fd.append('idioma', document.documentElement.lang === 'en' ? 'en' : 'es');
            if (estado) fd.append('estado', JSON.stringify(estado));
            var tok = form.querySelector('input[name="__RequestVerificationToken"]');
            if (tok) fd.append('__RequestVerificationToken', tok.value);
            var inicio = Date.now();
            fetch(form.action, { method: 'POST', body: fd, headers: { 'Accept': 'application/json' } })
                .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
                .then(function (d) {
                    // Una pausa breve, como quien escribe, para que la respuesta no aparezca de golpe.
                    var espera = reducido ? 0 : Math.max(0, Math.min(700, 250 + (d.texto || '').length * 2) - (Date.now() - inicio));
                    return new Promise(function (ok) { setTimeout(function () { ok(d); }, espera); });
                })
                .then(function (d) {
                    esc.remove();
                    burbuja(d.texto, 'bot'); acciones(d.acciones); sugerencias(d.sugerencias);
                    historial.push({ rol: 'assistant', texto: d.texto });
                    registro.push({ tipo: 'bot', texto: d.texto, acciones: d.acciones || [], sugerencias: d.sugerencias || [] });
                    estado = d.estado || null;
                    guardar();
                })
                .catch(function () {
                    esc.remove();
                    var ingles = document.documentElement.lang === 'en';
                    burbuja(ingles ? "I couldn't reply right now. Email us at " + (form.dataset.correo || 'our address') + ' or leave your details in the form.'
                        : 'No pude responder en este momento. Escríbenos a ' + (form.dataset.correo || 'nuestro correo') + ' o déjanos tus datos en el formulario.', 'bot');
                    acciones([{ texto: ingles ? 'Leave my details' : 'Dejar mis datos', url: '/#contacto', icono: 'bi-envelope' }]);
                })
                .finally(function () { ocupado = false; enviar.disabled = false; if (!panel.hidden) txt.focus(); });
        }

        function empezarDeNuevo() {
            historial = []; estado = null; registro = [];
            msgs.innerHTML = bienvenida; sugerencias(sugInicio); guardar(); txt.focus();
        }
        if (reiniciar) reiniciar.addEventListener('click', empezarDeNuevo);

        // Recupera la conversación de esta pestaña.
        try {
            var g = JSON.parse(sessionStorage.getItem(CLAVE) || 'null');
            if (g && g.registro && g.registro.length) {
                historial = g.historial || []; estado = g.estado || null; registro = g.registro;
                registro.forEach(pintar);
                var ultimo = registro[registro.length - 1];
                sugerencias(ultimo && ultimo.tipo === 'bot' ? ultimo.sugerencias : []);
                if (g.abierto) mostrar(true, true);
            } else sugerencias(sugInicio);
        } catch (e) { sugerencias(sugInicio); }

        form.addEventListener('submit', function (e) { e.preventDefault(); preguntar(txt.value); });
        txt.addEventListener('keydown', function (e) { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); preguntar(txt.value); } });
        txt.addEventListener('input', function () { txt.style.height = 'auto'; txt.style.height = Math.min(txt.scrollHeight, 120) + 'px'; });
    })();
})();
