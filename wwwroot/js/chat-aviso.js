// Chat del equipo (nov-2026): en cualquier página del ERP, cada 25 s (con la pestaña visible)
// actualiza el contador "Chat del equipo" del menú y muestra un aviso abajo a la derecha
// cuando llega un mensaje nuevo. En la página del chat no corre: ahí lo maneja chat-interno.js.
(function () {
    'use strict';
    if (/^\/App\/Chat/i.test(location.pathname)) return;
    const badge = document.getElementById('navChatBadge');
    if (!badge) return;
    const CLAVE = 'atilson-chat-avisado';
    const tituloBase = document.title.replace(/^\(\d+\)\s*/, '');

    function leer() { try { const v = localStorage.getItem(CLAVE); return v == null ? null : parseInt(v, 10) || 0; } catch (e) { return null; } }
    function guardar(v) { try { localStorage.setItem(CLAVE, String(v)); } catch (e) { /* sin almacenamiento */ } }

    function mostrarAviso(n) {
        const viejo = document.querySelector('.chat-toast');
        if (viejo) viejo.remove();
        const a = document.createElement('a');
        a.className = 'chat-toast';
        a.href = '/App/Chat?c=' + n.idConversacion;
        a.setAttribute('role', 'status');
        const i = document.createElement('i'); i.className = 'bi bi-chat-dots-fill'; i.setAttribute('aria-hidden', 'true');
        const b = document.createElement('b'); b.textContent = n.autor;
        const cerrar = document.createElement('button'); cerrar.type = 'button'; cerrar.setAttribute('aria-label', 'Cerrar aviso');
        cerrar.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
        cerrar.addEventListener('click', e => { e.preventDefault(); e.stopPropagation(); a.remove(); });
        const t = document.createElement('span'); t.textContent = n.texto;
        a.append(i, b, cerrar, t);
        document.body.appendChild(a);
        // Los avisos del portal/soporte pueden coexistir: apilar en vez de taparlos.
        const portal = document.getElementById('avisoRegistros');
        const portalVisible = portal && !portal.hidden;
        let altura = portalVisible ? portal.offsetHeight + 12 : 0;
        const soporte = document.querySelector('.soporte-toast');
        if (soporte) altura += soporte.offsetHeight + 12;
        a.style.bottom = (20 + altura) + 'px';
        requestAnimationFrame(() => a.classList.add('visible'));
        setTimeout(() => { a.classList.remove('visible'); setTimeout(() => a.remove(), 300); }, 8000);
    }

    async function revisar() {
        if (document.hidden) return;
        const avisado = leer();
        try {
            const r = await fetch('/App/Chat?handler=Estado&despues=' + (avisado || 0), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } });
            if (!r.ok || r.redirected) return;
            const j = await r.json();
            const total = j.total || 0;
            badge.textContent = total > 99 ? '99+' : total;
            badge.hidden = total === 0;
            document.title = (total > 0 ? '(' + total + ') ' : '') + tituloBase;
            if (j.nuevo) {
                // La primera vez en este navegador solo se toma la referencia, sin aviso.
                if (avisado !== null && j.nuevo.id > avisado) mostrarAviso(j.nuevo);
                guardar(Math.max(j.nuevo.id, avisado || 0));
            } else if (avisado === null) guardar(0);
        } catch (e) { /* sin conexión: se reintenta */ }
    }

    setTimeout(revisar, 1500);
    setInterval(revisar, 25000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) revisar(); });
})();
