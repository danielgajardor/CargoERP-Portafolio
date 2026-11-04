// Soporte (oct-2026): conversación cliente ↔ equipo. Usado por Portal › Soporte y ERP › Soporte clientes.
(function () {
    const caja = document.getElementById('soMensajes');
    const abajo = () => { if (caja) caja.scrollTop = caja.scrollHeight; };
    abajo();

    // Enter envía, Shift+Enter hace salto de línea; el cuadro crece con el texto.
    document.querySelectorAll('.so-escribir textarea').forEach(function (ta) {
        const crecer = () => { ta.style.height = 'auto'; ta.style.height = Math.min(ta.scrollHeight, 160) + 'px'; };
        ta.addEventListener('input', crecer);
        ta.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
                e.preventDefault();
                if (ta.value.trim()) ta.form.requestSubmit(ta.form.querySelector('button[type=submit]:not([value="true"])') || undefined);
            }
        });
        if (ta.dataset.foco === 'si') ta.focus();
    });
    // Evita el doble envío.
    document.querySelectorAll('.so form').forEach(function (f) {
        f.addEventListener('submit', function () { setTimeout(() => f.querySelectorAll('button[type=submit]').forEach(b => b.disabled = true), 0); });
    });

    // Mensajes nuevos sin recargar (cada 15 s mientras la pestaña está visible).
    if (!caja || !caja.dataset.url) return;
    const soy = caja.dataset.soy; // "cliente" | "equipo"
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    async function revisar() {
        if (document.hidden) return;
        try {
            const r = await fetch(caja.dataset.url + '&desde=' + (caja.dataset.ultimo || 0), { headers: { 'Accept': 'application/json' } });
            if (!r.ok) return;
            const d = await r.json();
            (d.mensajes || []).forEach(function (m) {
                const mio = (soy === 'cliente') === m.esCliente;
                caja.insertAdjacentHTML('beforeend',
                    `<div class="so-msg ${mio ? 'mio' : 'otro'}"><div class="burbuja">${esc(m.texto)}</div><div class="pie">${esc(m.nombre)} · ${esc(m.fecha)}</div></div>`);
                caja.dataset.ultimo = m.id;
            });
            if ((d.mensajes || []).length) abajo();
        } catch (e) { /* sin conexión: se reintenta en la próxima vuelta */ }
    }
    setInterval(revisar, 15000);
})();
