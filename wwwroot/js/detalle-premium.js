/* =========================================================================
   DETALLE DE OPERACIÓN — INTERACCIONES PREMIUM
   - Recuerda la pestaña activa y el scroll entre recargas (cada guardado
     hace window.location.reload(), antes siempre volvía a "Marítimo").
   - Animación del avance global, módulos y barco de la ruta.
   - Cuentas regresivas en vivo para las ventanas críticas.
   - Atajos: [data-goto-tab], [data-copy], Alt + 1..9 para cambiar de pestaña.
   ========================================================================= */
(function () {
    'use strict';

    const store = {
        get(k) { try { return sessionStorage.getItem(k); } catch (e) { return null; } },
        set(k, v) { try { sessionStorage.setItem(k, v); } catch (e) { /* storage bloqueado */ } }
    };

    function relativo(fecha) {
        let ms = fecha - new Date();
        const futuro = ms >= 0;
        ms = Math.abs(ms);
        const min = Math.floor(ms / 60000), h = Math.floor(min / 60), d = Math.floor(h / 24);
        const s = d >= 1 ? `${d}d ${h % 24}h` : (h >= 1 ? `${h}h ${min % 60}m` : `${Math.max(1, min)}m`);
        return futuro ? `en ${s}` : `hace ${s}`;
    }

    function urgencia(fecha) {
        const h = (fecha - new Date()) / 3600000;
        if (h < 0) return 'past';
        if (h <= 24) return 'crit';
        if (h <= 72) return 'warn';
        return 'ok';
    }

    document.addEventListener('DOMContentLoaded', function () {
        const root = document.querySelector('.dt-premium');
        if (!root) return;

        const opId = root.getAttribute('data-op-id') || 'x';
        const KEY_TAB = `dt-tab-${opId}`;
        const KEY_SCROLL = `dt-scroll-${opId}`;
        const tabsBar = document.getElementById('detalleTabs');
        const tabBtns = tabsBar ? Array.from(tabsBar.querySelectorAll('[data-bs-toggle="tab"]')) : [];

        function mostrarTab(target, scroll) {
            const btn = tabBtns.find(b => b.getAttribute('data-bs-target') === target);
            if (!btn || !window.bootstrap) return false;
            bootstrap.Tab.getOrCreateInstance(btn).show();
            if (scroll && tabsBar) {
                const y = tabsBar.getBoundingClientRect().top + window.scrollY - 16;
                window.scrollTo({ top: y, behavior: 'smooth' });
            }
            return true;
        }

        // ===== 1. Restaurar pestaña y scroll tras recarga =====
        if ('scrollRestoration' in history) history.scrollRestoration = 'manual';
        const desdeHash = location.hash && location.hash.indexOf('#det-') === 0 ? location.hash : null;
        const guardada = desdeHash || store.get(KEY_TAB);
        if (guardada) mostrarTab(guardada, false);

        tabBtns.forEach(btn => btn.addEventListener('shown.bs.tab', () => store.set(KEY_TAB, btn.getAttribute('data-bs-target'))));

        let scrollPrevio = null;
        try { scrollPrevio = JSON.parse(store.get(KEY_SCROLL) || 'null'); } catch (e) { scrollPrevio = null; }
        if (scrollPrevio && Date.now() - scrollPrevio.t < 30000) {
            setTimeout(() => window.scrollTo(0, scrollPrevio.y), 80);
        }
        window.addEventListener('beforeunload', () => store.set(KEY_SCROLL, JSON.stringify({ y: window.scrollY, t: Date.now() })));

        // ===== 1b. Minimizar / expandir el panel de avance =====
        // Arranca siempre minimizado (ver script al inicio de _DetalleHero.cshtml);
        // el botón solo lo despliega para esta vista, sin recordar el estado.
        const dashToggle = document.getElementById('dtDashToggle');
        if (dashToggle) {
            const setDashLabel = (colapsado) => {
                dashToggle.setAttribute('aria-expanded', String(!colapsado));
                dashToggle.querySelector('i').className = 'bi ' + (colapsado ? 'bi-arrows-expand' : 'bi-arrows-collapse');
                dashToggle.querySelector('span').textContent = colapsado ? 'Ver panel completo' : 'Minimizar panel';
            };
            dashToggle.addEventListener('click', () => {
                const colapsado = root.classList.toggle('dt-dash-collapsed');
                setDashLabel(colapsado);
            });
        }

        // ===== 1c. Detalle de "Tarifa pendiente" (chip del hero e íconos junto a cada título) =====
        document.addEventListener('click', function (e) {
            const chip = e.target.closest('[data-tarifa-detalle]');
            if (!chip) return;
            const items = chip.getAttribute('data-tarifa-detalle').split('|').filter(Boolean);
            if (!items.length || !window.Swal) return;
            const html = items.length === 1
                ? `<p style="margin:0;font-size:13px;color:#6b6880;">Falta que Comercial agregue el valor de <b>${items[0]}</b> en Acuerdos Operativos. Puedes seguir editando mientras tanto.</p>`
                : '<p style="margin:0 0 10px;font-size:13px;color:#6b6880;">Falta que Comercial agregue el valor de estos conceptos en Acuerdos Operativos. Puedes seguir editando mientras tanto.</p>'
                    + '<ul style="text-align:left;margin:0;padding-left:20px;font-size:13px;">'
                    + items.map(i => `<li><b>${i}</b></li>`).join('')
                    + '</ul>';
            Swal.fire({ icon: 'warning', title: 'Tarifa pendiente', html });
        });

        // ===== 2. Navegación rápida =====
        document.addEventListener('click', function (e) {
            const go = e.target.closest('[data-goto-tab]');
            if (go) { e.preventDefault(); mostrarTab(go.getAttribute('data-goto-tab'), true); }
        });

        document.addEventListener('keydown', function (e) {
            if (!e.altKey || e.ctrlKey || e.metaKey) return;
            const n = parseInt(e.key, 10);
            if (n >= 1 && n <= tabBtns.length) {
                e.preventDefault();
                mostrarTab(tabBtns[n - 1].getAttribute('data-bs-target'), true);
            }
        });

        // ===== 3. Copiar booking =====
        document.querySelectorAll('[data-copy]').forEach(btn => btn.addEventListener('click', () => {
            const txt = btn.getAttribute('data-copy');
            if (!navigator.clipboard) return;
            navigator.clipboard.writeText(txt).then(() => {
                btn.classList.add('is-copied');
                setTimeout(() => btn.classList.remove('is-copied'), 1400);
                if (window.Swal) Swal.fire({ icon: 'success', title: 'Booking copiado', text: txt, toast: true, position: 'top-end', timer: 1400, showConfirmButton: false });
            });
        }));

        // ===== 4. Animaciones de progreso =====
        const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        const aplicar = () => {
            document.querySelectorAll('.dt-ring[data-pct]').forEach(r => r.style.setProperty('--dt-pct', r.getAttribute('data-pct')));
            document.querySelectorAll('.dt-premium [data-w]').forEach(el => { el.style.width = Math.min(100, Math.max(0, +el.getAttribute('data-w'))) + '%'; });
            document.querySelectorAll('.dt-premium [data-left]').forEach(el => { el.style.left = Math.min(100, Math.max(0, +el.getAttribute('data-left'))) + '%'; });
        };
        if (reduce) aplicar(); else requestAnimationFrame(() => setTimeout(aplicar, 120));

        // ===== 5. Cuentas regresivas en vivo =====
        const actualizarPlazos = () => {
            document.querySelectorAll('[data-deadline]').forEach(el => {
                const f = new Date(el.getAttribute('data-deadline'));
                if (isNaN(f)) return;
                el.textContent = relativo(f);
                const card = el.closest('.dt-stat-next');
                if (card) {
                    card.classList.remove('is-ok', 'is-warn', 'is-crit', 'is-past');
                    card.classList.add('is-' + urgencia(f));
                }
            });
        };
        setInterval(actualizarPlazos, 60000);
    });
})();
