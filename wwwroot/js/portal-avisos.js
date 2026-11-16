// Campana de avisos del portal de clientes (15-nov-2026).
// Consulta /PortalCliente/Notificaciones?handler=Resumen al cargar y cada minuto (si la pestaña
// está visible), actualiza los contadores y muestra un aviso flotante cuando llega algo nuevo.
(function () {
    const url = '/PortalCliente/Notificaciones';
    const botones = Array.from(document.querySelectorAll('[data-pa-campana]'));
    const panel = document.getElementById('paPanel');
    if (!botones.length || !panel) return;
    const lista = panel.querySelector('.pa-panel__lista');
    const btnTodas = panel.querySelector('[data-pa-todas]');
    const token = document.querySelector('#paToken input[name="__RequestVerificationToken"]')?.value || '';
    let ultimoMax = null, abierto = false, cargando = false;

    function esc(s) { return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

    function fijarContador(n) {
        n = Math.max(0, n | 0);
        document.querySelectorAll('[data-pa-n]').forEach(el => { el.textContent = n > 0 ? (n > 99 ? '99+' : n) : ''; el.dataset.n = n; el.hidden = n === 0; });
        botones.forEach(b => {
            b.classList.toggle('tiene', n > 0);
            b.setAttribute('aria-label', n > 0 ? `Avisos: ${n} sin leer` : 'Avisos');
        });
        if (btnTodas) btnTodas.disabled = n === 0;
    }

    function pintar(avisos) {
        if (!avisos.length) {
            lista.innerHTML = '<li class="pa-panel__vacio"><i class="bi bi-bell"></i>Sin avisos por ahora. Aquí verás documentos nuevos, cambios de stacking y fechas, y el avance de tu carga.</li>';
            return;
        }
        lista.innerHTML = avisos.map(a => `
            <li class="pa-panel__item ${a.leida ? '' : 'is-nueva'}">
                <a href="${esc(a.url)}">
                    <span class="av-ico" style="--c:${esc(a.color)}"><i class="bi ${esc(a.icono)}"></i></span>
                    <span class="pa-panel__txt">
                        <span class="pa-panel__tit">${esc(a.titulo)}</span>
                        ${a.mensaje ? `<span class="pa-panel__msg">${esc(a.mensaje)}</span>` : ''}
                        <span class="pa-panel__hace">${esc(a.tipo)} · ${esc(a.hace)}</span>
                    </span>
                </a>
            </li>`).join('');
    }

    async function cargar(avisarNuevos) {
        if (cargando) return;
        cargando = true;
        try {
            const r = await fetch(url + '?handler=Resumen', { headers: { 'X-Requested-With': 'XMLHttpRequest' }, cache: 'no-store' });
            if (!r.ok) return;
            const d = await r.json();
            if (!d.ok) return;
            fijarContador(d.noLeidas);
            pintar(d.avisos || []);
            const max = (d.avisos || []).reduce((m, a) => Math.max(m, a.id), 0);
            if (avisarNuevos && ultimoMax !== null && max > ultimoMax) {
                const nuevos = (d.avisos || []).filter(a => a.id > ultimoMax && !a.leida);
                if (nuevos.length) {
                    botones.forEach(b => { b.classList.remove('timbre'); void b.offsetWidth; b.classList.add('timbre'); });
                    if (typeof window.avisoFlash === 'function')
                        window.avisoFlash('info', nuevos.length === 1 ? nuevos[0].titulo : `${nuevos.length} avisos nuevos`, 'Nuevo aviso');
                }
            }
            ultimoMax = max;
        } catch (e) { /* sin conexión: se reintenta en la próxima vuelta */ }
        finally { cargando = false; }
    }

    function abrir(b) {
        abierto = true;
        panel.hidden = false;
        botones.forEach(x => x.setAttribute('aria-expanded', 'true'));
        const r = b.getBoundingClientRect();
        panel.style.top = Math.round(r.bottom + 8) + 'px';
        panel.style.right = Math.max(10, Math.round(window.innerWidth - r.right)) + 'px';
        cargar(false);
    }
    function cerrar() {
        abierto = false;
        panel.hidden = true;
        botones.forEach(x => x.setAttribute('aria-expanded', 'false'));
    }

    botones.forEach(b => b.addEventListener('click', e => { e.stopPropagation(); abierto ? cerrar() : abrir(b); }));
    document.addEventListener('click', e => { if (abierto && !panel.contains(e.target)) cerrar(); });
    document.addEventListener('keydown', e => { if (e.key === 'Escape' && abierto) { cerrar(); botones[0].focus(); } });

    btnTodas?.addEventListener('click', async () => {
        const fd = new FormData();
        const r = await fetch(url + '?handler=LeerTodas', { method: 'POST', headers: { 'RequestVerificationToken': token, 'X-Requested-With': 'XMLHttpRequest' }, body: fd });
        if (r.ok) { fijarContador(0); lista.querySelectorAll('.is-nueva').forEach(li => li.classList.remove('is-nueva')); }
    });

    window.portalAvisos = { fijarContador, recargar: () => cargar(true) };
    fijarContador(parseInt(botones[0].dataset.inicial || '0', 10));
    cargar(false);
    setInterval(() => { if (!document.hidden) cargar(true); }, 60000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) cargar(true); });
})();
