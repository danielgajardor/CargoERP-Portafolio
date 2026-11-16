// =============================================================================
// Nave / Viaje desde el maestro de itinerarios (nov-2026)
// -----------------------------------------------------------------------------
// En Operaciones (Create y Detalle) la nave ya no se escribe a mano: se elige de
// las salidas cargadas en Operaciones › Itinerarios de naves. Con POL y POD
// elegidos, el select muestra solo las naves programadas para esa ruta (y de la
// naviera, si está elegida).
//
// El <select> guarda el texto "NAVE / VIAJE" (lo que siempre se guardó en
// operaciones.Nave) y un campo oculto guarda el Id de la salida (IdItinerario).
// Una nave que llega de un documento (PDF del booking, copia de otra operación)
// y no está en el maestro se conserva como opción marcada "no está en el maestro".
// =============================================================================
window.NavesItinerario = (function () {
    const URL = '/Operaciones/Itinerarios?handler=NavesRuta';

    function leer(el) {
        if (!el) return '';
        if (window.jQuery && jQuery(el).data('select2')) return jQuery(el).val() || '';
        return el.value || '';
    }

    function refrescarSelect2(select) {
        if (window.jQuery && jQuery(select).data('select2')) jQuery(select).trigger('change.select2');
    }

    function etiqueta(n) {
        return `${n.nave} · ${n.pol || '?'} → ${n.pod || '?'} · ETD ${n.etdTexto} · ETA ${n.etaTexto} (${n.transito} días)${n.zarpada ? ' · zarpada' : ''}`;
    }

    function opcionLibre(select, texto) {
        const op = document.createElement('option');
        op.value = texto;
        op.textContent = `${texto} (no está en el maestro de itinerarios)`;
        op.dataset.libre = '1';
        select.appendChild(op);
        return op;
    }

    // cfg: { select, idInput, naviera, pol, pod, etd, eta, hint, alElegir }
    // naviera/pol/pod/etd/eta son elementos (o null). hint: elemento para el aviso.
    function enlazar(cfg) {
        const select = cfg.select;
        if (!select || select.dataset.navesEnlazado) return null;
        select.dataset.navesEnlazado = '1';
        let pedido = 0;

        function fijarHint(html, tipo) {
            if (!cfg.hint) return;
            cfg.hint.innerHTML = html;
            cfg.hint.className = 'naves-hint' + (tipo ? ' naves-hint--' + tipo : '');
        }

        async function recargar(opciones) {
            const idPedido = cfg.idInput ? cfg.idInput.value : '';
            const naviera = leer(cfg.naviera), pol = leer(cfg.pol), pod = leer(cfg.pod);
            const p = new URLSearchParams();
            if (naviera) p.set('naviera', naviera);
            if (pol) p.set('pol', pol);
            if (pod) p.set('pod', pod);
            if (idPedido) p.set('idActual', idPedido);
            const n = ++pedido;
            fijarHint('<i class="bi bi-arrow-repeat"></i> Buscando naves programadas…', '');
            let data;
            try {
                const r = await fetch(URL + '&' + p.toString(), { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' });
                if (!r.ok) throw new Error('HTTP ' + r.status);
                data = await r.json();
            } catch (e) {
                if (n !== pedido) return;
                fijarHint('<i class="bi bi-exclamation-triangle"></i> No se pudieron cargar las naves del maestro. <a href="#" data-naves-reintentar>Reintentar</a>', 'error');
                return;
            }
            if (n !== pedido) return; // llegó una respuesta más nueva

            // Lo elegido se lee al llegar la respuesta (pudo fijarse mientras se buscaba).
            const actualTexto = (opciones && opciones.texto !== undefined) ? opciones.texto : leer(select);
            const actualId = (opciones && opciones.id !== undefined) ? opciones.id : (cfg.idInput ? cfg.idInput.value : '');

            select.innerHTML = '';
            const vacio = document.createElement('option');
            vacio.value = '';
            vacio.textContent = '-- Seleccionar nave / viaje --';
            select.appendChild(vacio);
            select._naves = {};
            (data.naves || []).forEach(nv => {
                const op = document.createElement('option');
                op.value = nv.nave;
                op.textContent = etiqueta(nv);
                op.dataset.id = nv.id;
                select._naves[nv.id] = nv;
                select.appendChild(op);
            });

            // Restaurar lo que estaba: primero por Id de salida, después por texto.
            // El Id solo manda si su salida sigue siendo la nave guardada: un roleo antiguo pudo
            // cambiar la nave sin cambiar el Id, y entonces vale el texto.
            let elegida = null;
            if (actualId) {
                const porId = Array.from(select.options).find(o => o.dataset.id === String(actualId));
                if (porId && (!actualTexto || porId.value.toUpperCase() === String(actualTexto).toUpperCase())) elegida = porId;
            }
            if (!elegida && actualTexto) elegida = Array.from(select.options).find(o => o.value.toUpperCase() === String(actualTexto).toUpperCase());
            if (!elegida && actualTexto) elegida = opcionLibre(select, String(actualTexto).toUpperCase());
            select.value = elegida ? elegida.value : '';
            if (elegida) elegida.selected = true;
            if (cfg.idInput) cfg.idInput.value = elegida && elegida.dataset.id ? elegida.dataset.id : '';
            refrescarSelect2(select);

            const total = (data.naves || []).length;
            const enlace = '<a href="/Operaciones/Itinerarios" target="_blank" rel="noopener">Itinerarios de naves</a>';
            if (!data.filtradoPorRuta) {
                fijarHint(`<i class="bi bi-info-circle"></i> Elige POL y POD para ver solo las naves de esa ruta. ${total ? `Hay ${total} salida(s) próximas${naviera ? ' de la naviera' : ''}.` : ''}`, '');
            } else if (total === 0) {
                fijarHint(`<i class="bi bi-exclamation-triangle"></i> No hay naves programadas para esta ruta${naviera ? ' y naviera' : ''}. Cárgala en ${enlace} y <a href="#" data-naves-reintentar>vuelve a buscar</a>.`, 'error');
            } else {
                fijarHint(`<i class="bi bi-check2-circle"></i> ${total} nave(s) programada(s) para esta ruta${naviera ? ' y naviera' : ''}.`, 'ok');
            }
        }

        function alCambiar() {
            const op = select.options[select.selectedIndex];
            const id = op && op.dataset.id ? op.dataset.id : '';
            if (cfg.idInput) cfg.idInput.value = id;
            const nv = id && select._naves ? select._naves[id] : null;
            if (nv) {
                // La salida define las fechas estimadas: se copian para no digitarlas.
                if (cfg.etd && nv.etd) { cfg.etd.value = nv.etd; cfg.etd.dispatchEvent(new Event('change', { bubbles: true })); }
                if (cfg.eta && nv.eta) { cfg.eta.value = nv.eta; cfg.eta.dispatchEvent(new Event('change', { bubbles: true })); }
                if (cfg.naviera && !leer(cfg.naviera) && nv.idNaviera) {
                    if (window.jQuery) jQuery(cfg.naviera).val(String(nv.idNaviera)).trigger('change');
                    else cfg.naviera.value = String(nv.idNaviera);
                }
            }
            if (typeof cfg.alElegir === 'function') cfg.alElegir(nv);
        }

        if (window.jQuery) jQuery(select).on('change', alCambiar);
        else select.addEventListener('change', alCambiar);

        [cfg.naviera, cfg.pol, cfg.pod].forEach(el => {
            if (!el) return;
            const f = () => recargar();
            if (window.jQuery) jQuery(el).on('change', f); else el.addEventListener('change', f);
        });
        if (cfg.hint) cfg.hint.addEventListener('click', ev => {
            if (ev.target.closest('[data-naves-reintentar]')) { ev.preventDefault(); recargar(); }
        });

        const api = {
            recargar,
            // Fija una nave por texto (PDF del booking, copia de otra operación).
            fijarTexto(texto, idSalida) {
                texto = (texto || '').trim().toUpperCase();
                if (!texto) return;
                let op = idSalida ? Array.from(select.options).find(o => o.dataset.id === String(idSalida)) : null;
                if (!op) op = Array.from(select.options).find(o => o.value.toUpperCase() === texto);
                const libre = !op;
                if (!op) op = opcionLibre(select, texto);
                select.value = op.value;
                op.selected = true;
                if (cfg.idInput) cfg.idInput.value = op.dataset.id || idSalida || '';
                refrescarSelect2(select);
                // Si venía con su salida del maestro pero aún no estaba en la lista, se vuelve a buscar.
                if (libre && idSalida) recargar();
            }
        };
        select._navesApi = api;
        recargar();
        return api;
    }

    return { enlazar };
})();
