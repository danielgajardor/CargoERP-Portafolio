// Soporte clientes: contador de mensajes sin respuesta y aviso inferior derecho.
// Funciona en todas las pantallas internas, además del aviso del chat del equipo.
(function () {
    'use strict';
    const badge = document.getElementById('navSoporteBadge');
    if (!badge) return;

    const llave = 'atilson-soporte-avisado-' + (badge.dataset.usuario || 'interno');
    let revisando = false;

    function leerFecha() {
        try { return localStorage.getItem(llave); } catch (_) { return null; }
    }
    function guardarFecha(fecha) {
        if (!fecha) return;
        try { localStorage.setItem(llave, fecha); } catch (_) {}
    }
    function notificar(item) {
        // Si el usuario ya está respondiendo en Soporte, se actualiza el contador,
        // pero no se superpone un popup a la conversación abierta.
        if (/^\/App\/Soporte(?:\/|$)/i.test(location.pathname)) return;
        document.querySelector('.soporte-toast')?.remove();

        const aviso = document.createElement('a');
        aviso.className = 'chat-toast soporte-toast';
        aviso.href = '/App/Soporte?t=' + encodeURIComponent(String(item.ultimoTicketId));
        aviso.setAttribute('role', 'status');
        aviso.setAttribute('aria-live', 'polite');

        const icono = document.createElement('i');
        icono.className = 'bi bi-headset';
        icono.setAttribute('aria-hidden', 'true');
        const titulo = document.createElement('b');
        titulo.textContent = 'Nuevo mensaje de soporte · ' + (item.titulo || 'Cliente');
        const texto = document.createElement('span');
        texto.textContent = item.texto || 'El cliente envió un mensaje que requiere respuesta.';
        const cerrar = document.createElement('button');
        cerrar.type = 'button';
        cerrar.setAttribute('aria-label', 'Cerrar aviso');
        cerrar.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
        cerrar.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            aviso.remove();
        });
        aviso.append(icono, titulo, cerrar, texto);
        document.body.appendChild(aviso);
        // Apilar por encima de las alertas que ya estén visibles en la esquina.
        const portal = document.getElementById('avisoRegistros');
        const portalVisible = portal && !portal.hidden;
        const chatVisible = document.querySelector('.chat-toast:not(.soporte-toast)');
        let base = 20;
        if (portalVisible) base += portal.offsetHeight + 12;
        if (chatVisible) base += chatVisible.offsetHeight + 12;
        aviso.style.bottom = base + 'px';
        requestAnimationFrame(() => aviso.classList.add('visible'));
        setTimeout(() => {
            if (!aviso.isConnected) return;
            aviso.classList.remove('visible');
            setTimeout(() => aviso.remove(), 300);
        }, 8500);
    }

    async function revisar() {
        if (document.hidden || revisando) return;
        revisando = true;
        try {
            const r = await fetch('/App/Soporte?handler=EstadoAvisos', {
                credentials: 'same-origin', cache: 'no-store',
                headers: { 'Accept': 'application/json' }
            });
            if (!r.ok || r.redirected) return;
            const datos = await r.json();
            const total = Math.max(0, Number(datos.total) || 0);
            badge.textContent = total > 99 ? '99+' : String(total);
            badge.hidden = total === 0;

            const anterior = leerFecha();
            const reciente = datos.ultimaFecha;
            // La primera lectura establece una referencia sin mostrar avisos viejos.
            if (reciente && anterior && Date.parse(reciente) > Date.parse(anterior) &&
                Number(datos.ultimoTicketId) > 0) {
                notificar(datos);
            }
            if (reciente && (!anterior || Date.parse(reciente) > Date.parse(anterior)))
                guardarFecha(reciente);
        } catch (_) { /* Sin conexión: no interrumpir la navegación del ERP. */ }
        finally { revisando = false; }
    }
    setTimeout(revisar, 1700);
    setInterval(revisar, 25000);
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) revisar();
    });
})();
