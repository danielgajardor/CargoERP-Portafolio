    // Limpia avisos antiguos del usuario, nunca solicitudes originales.
    window.descartarTodasNotificacionesPortal = async function () {
        if (!window.confirm('¿Descartar todos los avisos pendientes de tu bandeja? Las solicitudes originales seguirán disponibles en sus módulos.')) return;
        const token = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]')?.value;
        if (!token) { alert('No se pudo validar la sesión. Recarga la página.'); return; }
        try {
            const res = await fetch('/Operaciones/Index?handler=DescartarTodasNotificaciones', {
                method: 'POST', headers: { 'RequestVerificationToken': token }
            });
            if (!res.ok) throw new Error('No se pudieron descartar los avisos.');
            window.location.reload();
        } catch (error) {
            alert(error.message || 'Error al descartar.');
        }
    };

    // Si llegó un aviso nuevo mientras el panel estaba abierto, actualizar el
    // contenido Razor del modal antes de mostrarlo (no abrir datos obsoletos).
    document.addEventListener('click', function (e) {
        const btn = e.target.closest('#btnNotificacionesPortal');
        if (!btn || btn.dataset.desactualizado !== '1') return;
        e.preventDefault();
        e.stopImmediatePropagation();
        try { sessionStorage.setItem('atilson-reabrir-notificaciones', '1'); } catch (_) {}
        location.reload();
    }, true);
    document.addEventListener('DOMContentLoaded', function () {
        try {
            if (sessionStorage.getItem('atilson-reabrir-notificaciones') !== '1') return;
            sessionStorage.removeItem('atilson-reabrir-notificaciones');
            const modal = document.getElementById('modalNotificacionesUnificadas');
            if (modal && window.bootstrap) bootstrap.Modal.getOrCreateInstance(modal).show();
        } catch (_) {}
    });


    // El descarte sólo afecta la notificación visible de este usuario.
    window.descartarAvisoPortal = async function (boton) {
        const tipo = boton.dataset.tipo;
        const idReferencia = Number(boton.dataset.id);
        if (!tipo || !idReferencia) return;
        const token = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]')?.value;
        if (!token) { alert('No se pudo validar la sesión. Recarga la página.'); return; }
        boton.disabled = true;
        try {
            const data = new URLSearchParams({ tipo, idReferencia: String(idReferencia) });
            const res = await fetch('/Operaciones/Index?handler=DescartarNotificacion', {
                method: 'POST',
                headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/x-www-form-urlencoded' },
                body: data.toString()
            });
            if (!res.ok) throw new Error('No se pudo guardar el descarte.');
            window.location.reload();
        } catch (error) {
            boton.disabled = false;
            alert(error.message || 'Error al descartar el aviso.');
        }
    };

    // ==========================================
    // 0. HELPERS (Contexto del Modal)
    // ==========================================
    function modalCtx() { return document.getElementById('modalDynamicBody'); }

    // escapeHtmlAtilson: (movido a wwwroot/js/operaciones-common.js — auditoría oct-2026, dedup JS)

    function isToday(dateString) {
        const d = new Date(dateString);
        const today = new Date();
        return d.getDate() === today.getDate() &&
               d.getMonth() === today.getMonth() &&
               d.getFullYear() === today.getFullYear();
    }

    // ==========================================
    // 0.45 ANILLOS DE PROGRESO POR PESTAÑA (en vivo)
    // ==========================================
    // (movido a wwwroot/js/operaciones-common.js: window.actualizarAnilloPorPct — auditoría oct-2026, dedup JS)
    // (movido a wwwroot/js/operaciones-common.js: window.actualizarAnilloProgreso — auditoría oct-2026, dedup JS)

    window.recalcularProgresoMaritimo = function(idOp) {
        const ctx = modalCtx(); if (!ctx) return;
        const pane = ctx.querySelector(`#maritimo-${idOp}`); if (!pane) return;
        let hechos = 0; const total = 8;
        const v = sel => { const el = pane.querySelector(sel); return el && el.value && el.value.trim() !== '' && el.value !== '0'; };
        if (v('select[name="IdNaviera"]')) hechos++;
        if (v('input[name="Nave"]')) hechos++;
        if (v('select[name="IdPuertoOrigen"]')) hechos++;
        if (v('select[name="IdPuertoDestino"]')) hechos++;
        if (v('input[name="EtdPol"]')) hechos++;
        if (v('input[name="EtaPod"]')) hechos++;
        if (v('input[name="FechaStacking"]')) hechos++;
        if (v('input[name="CutOffMatriz"]')) hechos++;
        window.actualizarAnilloProgreso(`ring-mar-${idOp}`, hechos, total);
    };

    window.recalcularProgresoTransporte = function(idOp) {
        const ctx = modalCtx(); if (!ctx) return;
        const pane = ctx.querySelector(`#terrestre-${idOp}`); if (!pane) return;
        const forms = pane.querySelectorAll('form');
        if (!forms.length) { window.actualizarAnilloPorPct(`ring-ter-${idOp}`, 0); return; }
        let sumaPct = 0;
        forms.forEach(form => {
            let hechos = 0; const total = 7;
            const v = sel => { const el = form.querySelector(sel); return el && el.value && el.value.trim() !== ''; };
            if (v('input[name="EmpresaTransporte"], select[name="EmpresaTransporte"]')) hechos++;
            if (v('input[name="NombreConductor"]')) hechos++;
            if (v('input[name="Patente"]')) hechos++;
            if (v('input[name="DepositoRetiro"], select[name="DepositoRetiro"]')) hechos++;
            if (v('input[name="FechaCarga"]')) hechos++;
            if (v('input[name="NumeroContenedor"]')) hechos++;
            if (v('input[name="NumeroSello"]')) hechos++;
            sumaPct += (hechos / total) * 100;
        });
        window.actualizarAnilloPorPct(`ring-ter-${idOp}`, Math.round(sumaPct / forms.length));
    };

    window.recalcularProgresoDocumental = function(idOp) {
        const ctx = modalCtx(); if (!ctx) return;
        const pane = ctx.querySelector(`#documental-v3-${idOp}`); if (!pane) return;
        const forms = pane.querySelectorAll('form[id^="doc-global-form-"]');
        if (!forms.length) { window.actualizarAnilloPorPct(`ring-doc-${idOp}`, 0); return; }
        let sumaPct = 0;
        forms.forEach(form => {
            let total = 2; let hechos = 0;
            const idAg = form.querySelector('select[name="docUpdates.IdAgenciaAduana"]');
            if (idAg && idAg.value && idAg.value.trim() !== '') hechos++;
            const dus = form.querySelector('input[name="docUpdates.Dus"]');
            const din = form.querySelector('input[name="docUpdates.Din"]');
            if ((dus && dus.value.trim()) || (din && din.value.trim())) hechos++;
            const chkSag = form.querySelector('input[name="docUpdates.AplicaSag"]');
            if (chkSag && chkSag.checked) {
                total++;
                const fit = form.querySelector('input[name="docUpdates.NumFit1"]');
                if (fit && fit.value.trim()) hechos++;
            }
            const chkSerna = form.querySelector('input[name="docUpdates.AplicaSernapesca"]');
            if (chkSerna && chkSerna.checked) {
                total++;
                const san = form.querySelector('input[name="docUpdates.NumSan1"]');
                if (san && san.value.trim()) hechos++;
            }
            sumaPct += (hechos / total) * 100;
        });
        window.actualizarAnilloPorPct(`ring-doc-${idOp}`, Math.round(sumaPct / forms.length));
    };

    window.recalcularProgresoMatriz = function(idOp) {
        const ctx = modalCtx(); if (!ctx) return;
        const pane = ctx.querySelector(`#quick-matriz-${idOp}`); if (!pane) return;
        const ring = ctx.querySelector(`#ring-matriz-${idOp}`); if (!ring) return;
        let hechos = 0; const total = 3;
        const chk = pane.querySelector('input[name="MatrizPresentada"]');
        if (chk && chk.checked) hechos++;
        const fileInp = document.getElementById(`file-bl-${idOp}`) || pane.querySelector(`#file-bl-${idOp}`);
        const yaTeniaArchivo = ring.getAttribute('data-tenia-bl') === 'true';
        if ((fileInp && fileInp.files && fileInp.files.length > 0) || yaTeniaArchivo) hechos++;
        if (ring.getAttribute('data-aprobado') === 'true') hechos++;
        window.actualizarAnilloProgreso(`ring-matriz-${idOp}`, hechos, total);
    };

    window.recalcularProgresoGate = function(idOp) {
        const ctx = modalCtx(); if (!ctx) return;
        const pane = ctx.querySelector(`#gate-${idOp}`); if (!pane) return;
        const selTipo = pane.querySelector('select[name="TipoGate"]');
        const selGestiona = pane.querySelector('select[name="GestionaGate"]');
        const selPago = pane.querySelector('select[name="PagoGate"]');
        const clienteDirecto = selGestiona && selGestiona.value === 'Cliente gestiona directo';
        const total = clienteDirecto ? 2 : 3;
        let hechos = 0;
        if (selTipo && selTipo.value.trim()) hechos++;
        if (selGestiona && selGestiona.value.trim()) hechos++;
        if (!clienteDirecto && selPago && selPago.value.trim()) hechos++;
        window.actualizarAnilloProgreso(`ring-gate-${idOp}`, hechos, total);
    };

    
    // Delegación única: cualquier cambio dentro del modal recalcula el anillo
    // de la pestaña correspondiente, sin necesidad de un listener por campo.
    document.addEventListener('DOMContentLoaded', function () {
        const modalBody = document.getElementById('modalDynamicBody');
        if (!modalBody) return;
        ['input', 'change'].forEach(evtName => {
            modalBody.addEventListener(evtName, function (e) {
                const idOp = modalBody.querySelector('input[name="IdOperacion"]')?.value;
                if (!idOp) return;
                if (e.target.closest(`#maritimo-${idOp}`)) window.recalcularProgresoMaritimo(idOp);
                else if (e.target.closest(`#terrestre-${idOp}`)) window.recalcularProgresoTransporte(idOp);
                else if (e.target.closest(`#documental-v3-${idOp}`)) window.recalcularProgresoDocumental(idOp);
                else if (e.target.closest(`#quick-matriz-${idOp}`)) window.recalcularProgresoMatriz(idOp);
                else if (e.target.closest(`#gate-${idOp}`)) window.recalcularProgresoGate(idOp);
            });
        });
    });

    

    // ==========================================
    // 2. INICIALIZACIÓN, FILTROS Y ALERTAS PROACTIVAS
    // ==========================================
        document.addEventListener("DOMContentLoaded", function () {
        document.querySelectorAll('select[name="EstadoLar"]').forEach(sel => {
            if (sel.value === "V°B° Ingresado a puerto") {
                sel.style.color = "#16a34a";
                sel.style.fontWeight = "bold";
            }
        });
        // Las alertas de Matriz se generan únicamente en el backend para evitar
        // duplicados y para usar siempre la fecha vigente del booking.
        actualizarTooltipsAlertas();

        document.addEventListener('click', function (e) {
            document.querySelectorAll('.autocomplete-items').forEach(list => {
                if (!list.parentElement.contains(e.target)) list.remove();
            });
        });

        document.getElementById('liveSearchInput')?.addEventListener('keyup', doFilters);
        function aplicarFiltrosRapidosServidor() {
            const url = new URL(window.location.href);
            const fm = document.getElementById('filterMatriz')?.value || 'all';
            const fs = document.getElementById('filterStacking')?.value || 'all';
            if (fm === 'all') url.searchParams.delete('FiltroMatriz'); else url.searchParams.set('FiltroMatriz', fm);
            if (fs === 'all') url.searchParams.delete('FiltroStacking'); else url.searchParams.set('FiltroStacking', fs);
            url.searchParams.delete('Pagina');
            window.location.href = url.toString();
        }
        document.getElementById('filterMatriz')?.addEventListener('change', aplicarFiltrosRapidosServidor);
        document.getElementById('filterStacking')?.addEventListener('change', aplicarFiltrosRapidosServidor);
        document.getElementById('btnResetFilters')?.addEventListener('click', function() {
            const url = new URL(window.location.href);
            const tieneServidor = url.searchParams.has('Buscar') || url.searchParams.has('FiltroMatriz') || url.searchParams.has('FiltroStacking') || url.searchParams.has('Pagina');
            if (tieneServidor) {
                url.searchParams.delete('Buscar');
                url.searchParams.delete('FiltroMatriz');
                url.searchParams.delete('FiltroStacking');
                url.searchParams.delete('Pagina');
                window.location.href = url.toString();
                return;
            }
            document.getElementById('liveSearchInput').value = '';
            document.getElementById('filterMatriz').value = 'all';
            document.getElementById('filterStacking').value = 'all';
            doFilters();
        });

        // MAGIA: Bloquear todos los inputs de dinero para Operaciones
        document.querySelectorAll('.doc-val-input').forEach(inp => {
            inp.classList.remove('doc-val-input'); 
            inp.setAttribute('readonly', 'true');
            inp.style.pointerEvents = 'none';
            inp.style.backgroundColor = '#f1f5f9';
            inp.style.color = '#94a3b8';
            inp.type = 'text';
            
            if (inp.value && inp.value !== '0' && inp.value !== '') {
                inp.value = '$ ' + parseInt(inp.value).toLocaleString('es-CL');
            } else {
                inp.placeholder = 'Costeado por Finanzas';
            }
        });
    });

    window.doFilters = function() {
        const term = document.getElementById('liveSearchInput')?.value.toLowerCase() || '';
        const fMatriz = document.getElementById('filterMatriz')?.value || 'all';
        const fStacking = document.getElementById('filterStacking')?.value || 'all';
        
        document.querySelectorAll('.row-main').forEach(row => {
            const text = row.innerText.toLowerCase();
            let show = true;
            
            if (term && !text.includes(term)) show = false;
            
            if (fMatriz === 'pend' && row.getAttribute('data-matriz-pend') !== 'true') show = false;
            if (fMatriz === 'today') {
                const cutOff = row.getAttribute('data-date-matriz');
                if (!cutOff || !isToday(cutOff)) show = false;
            }
            
            if (fStacking === 'pend' && row.getAttribute('data-stacking-pend') !== 'true') show = false;
            if (fStacking === 'today') {
                const outDate = row.getAttribute('data-date-stacking');
                if (!outDate || !isToday(outDate)) show = false;
            }
            
            row.style.display = show ? 'table-row' : 'none';
            
            let next = row.nextElementSibling;
            while(next && next.classList.contains('row-sub')) {
                next.style.display = show ? 'table-row' : 'none';
                next = next.nextElementSibling;
            }
        });
    };


        window.mostrarAlertaHover = function(icon) {
        const row = icon.closest('tr');
        const box = document.getElementById('global-alert-box');
        const content = document.getElementById('global-alert-content');
        if (!row || !box || !content) return;
        const dataSource = row.querySelector('.alert-data-source');
        if (!dataSource) return;

        let html = '';
        dataSource.querySelectorAll('.alert-item').forEach(item => {
            if (item.style.display !== 'none' && item.innerHTML.trim() !== '') {
                html += item.innerHTML;
            }
        });
        if (html.trim() === '') return;

        content.innerHTML = html;

        const rect = icon.getBoundingClientRect();
        const boxWidth = 380;
        let left = rect.right + 14;
        let top = Math.max(8, rect.top - 10);

        if (left + boxWidth > window.innerWidth - 16) {
            left = Math.max(8, rect.left - boxWidth - 14);
        }
        const maxTop = window.innerHeight - 60;
        if (top > maxTop) top = maxTop;

        box.style.left = left + 'px';
        box.style.top = top + 'px';
        box.style.display = 'block';
    };

    window.ocultarAlertaHover = function() {
        const box = document.getElementById('global-alert-box');
        if (box) box.style.display = 'none';
    };

    window.actualizarTooltipsAlertas = function() {
        document.querySelectorAll('tr.op-row').forEach(row => {
            const icon = row.querySelector('.alert-icon-main');
            const dataSource = row.querySelector('.alert-data-source');
            if (!icon || !dataSource) return;

            icon.removeAttribute('title');

            if (icon.dataset.hoverBound === 'true') return;
            icon.dataset.hoverBound = 'true';
            icon.addEventListener('mouseenter', function() { window.mostrarAlertaHover(icon); });
            icon.addEventListener('mouseleave', function() { window.ocultarAlertaHover(); });
        });
    };

    window.abrirOpcionesAlerta = function(element, event) {
        if (event) { event.preventDefault(); event.stopPropagation(); }
        const row = element.closest('tr');
        if (!row) return;

        const idOp = row.getAttribute('data-op-id');
        const booking = row.getAttribute('data-booking') || 'S/N';
        const dataSource = row.querySelector('.alert-data-source');
        const alertBody = document.getElementById('alertDynamicBody');
        if (!idOp || !dataSource || !alertBody) return;

        const config = [
            { selector: '.hover-matriz',     label: 'Matriz documental',       icon: 'bi-file-earmark-text', actionIcon:'bi-arrow-right', action:'Ir a Matriz', kind:'tab', target:`quick-matriz-${idOp}` },
            { selector: '.hover-stacking',   label: 'Tránsito a puerto',       icon: 'bi-water',              actionIcon:'bi-arrow-right', action:'Ir a Marítimo', kind:'tab', target:`maritimo-${idOp}` },
            { selector: '.hover-transporte', label: 'Logística terrestre',     icon: 'bi-truck',              actionIcon:'bi-arrow-right', action:'Ir a Transporte', kind:'tab', target:`terrestre-${idOp}` },
            { selector: '.hover-dhl',        label: 'Documentos post-zarpe',   icon: 'bi-send-check',         actionIcon:'bi-arrow-right', action:'Ir a Documental', kind:'tab', target:`quick-matriz-${idOp}` },
            { selector: '.hover-transbordo', label: 'Transbordo',              icon: 'bi-arrow-left-right',   actionIcon:'bi-box-arrow-up-right', action:'Gestionar transbordo', kind:'detail' },
            { selector: '.hover-arribo',     label: 'Arribo / retiro',         icon: 'bi-box-seam',           actionIcon:'bi-box-arrow-up-right', action:'Gestionar retiro', kind:'detail' },
            { selector: '.hover-devolucion', label: 'Devolución de vacío',     icon: 'bi-arrow-return-left',  actionIcon:'bi-box-arrow-up-right', action:'Confirmar devolución', kind:'detail' },
            // 13-nov: ciclo de la nave (ciclo-nave.js).
            { selector: '.hover-nave-zarpe',     label: 'Zarpe de la nave',  icon: 'bi-water',          actionIcon:'bi-pencil-square', action:'Registrar Zarpe Real',      kind:'fn', fn:`cicloNaveDesdeAlerta('${idOp}', 'zarpe')` },
            { selector: '.hover-nave-aviso',     label: 'Aviso de llegada',  icon: 'bi-envelope-paper', actionIcon:'bi-send',          action:'Enviar aviso de llegada',   kind:'fn', fn:`cicloNaveAvisoLlegada('${idOp}')` },
            { selector: '.hover-nave-revision5', label: 'Revisión de la nave', icon: 'bi-binoculars',   actionIcon:'bi-check2-circle', action:'Revisada: sin demoras',     kind:'fn', fn:`cicloNaveRevision('${idOp}', 5)` },
            { selector: '.hover-nave-revision3', label: 'Revisión de la nave', icon: 'bi-binoculars',   actionIcon:'bi-check2-circle', action:'Revisada: sin demoras',     kind:'fn', fn:`cicloNaveRevision('${idOp}', 3)` },
            { selector: '.hover-nave-arribo',    label: 'Arribo a destino',  icon: 'bi-geo-alt-fill',   actionIcon:'bi-pencil-square', action:'Registrar Arribo Efectivo', kind:'fn', fn:`cicloNaveDesdeAlerta('${idOp}', 'arribo')` }
        ];

        function prioridad(html) {
            const h = (html || '').toLowerCase();
            if (h.includes('border-danger') || h.includes('text-danger') || h.includes('bg-danger')) return 0;
            if (h.includes('border-warning') || h.includes('#d97706') || h.includes('text-warning')) return 1;
            return 2;
        }
        function clasePrioridad(p) { return p === 0 ? 'critical' : p === 1 ? 'warning' : 'info'; }

        const activas = [];
        config.forEach(cfg => {
            const item = dataSource.querySelector(cfg.selector);
            if (!item || item.style.display === 'none' || !item.innerHTML.trim()) return;
            activas.push({ cfg, html:item.innerHTML, priority:prioridad(item.innerHTML) });
        });
        activas.sort((a,b) => a.priority - b.priority);

        let html = `
            <div class="ops-alert-summary">
                <div>
                    <span class="ops-alert-kicker">Booking</span>
                    <strong>${typeof escapeHtmlAtilson === 'function' ? escapeHtmlAtilson(booking) : booking}</strong>
                </div>
                <span class="ops-alert-count"><i class="bi bi-bell"></i> ${activas.length} ${activas.length === 1 ? 'alerta activa' : 'alertas activas'}</span>
            </div>`;

        if (!activas.length) {
            html += `<div class="ops-alert-empty"><i class="bi bi-check-circle-fill"></i><strong>Sin alertas activas</strong><span>Esta operación no requiere una gestión inmediata.</span></div>`;
        } else {
            activas.forEach(a => {
                const cfg = a.cfg;
                const action = cfg.kind === 'detail'
                    ? `<a href="/Operaciones/Detalle/${idOp}" class="ops-alert-action"><i class="bi ${cfg.actionIcon}"></i>${cfg.action}</a>`
                    : cfg.kind === 'fn'
                        ? `<button type="button" class="ops-alert-action" onclick="${cfg.fn}"><i class="bi ${cfg.actionIcon}"></i>${cfg.action}</button>`
                        : `<button type="button" class="ops-alert-action" onclick="abrirDesdeAlerta('${idOp}', '${cfg.target}')"><i class="bi ${cfg.actionIcon}"></i>${cfg.action}</button>`;

                html += `
                    <section class="ops-alert-card ${clasePrioridad(a.priority)}">
                        <div class="ops-alert-card-head"><span><i class="bi ${cfg.icon}"></i>${cfg.label}</span></div>
                        <div class="ops-alert-card-body">${a.html}</div>
                        ${action}
                    </section>`;
            });
        }

        alertBody.innerHTML = html;
        bootstrap.Modal.getOrCreateInstance(document.getElementById('modalAlertas')).show();
    };

        window.abrirDesdeNotificacionRoleo = function(idOp, tabTargetId) {
        let modalNotifEl = document.getElementById('modalNotificacionesUnificadas');
        let modalObj = bootstrap.Modal.getInstance(modalNotifEl);
        if (modalObj) modalObj.hide();
        window.location.href = '/Operaciones/Detalle/' + idOp;
    };

    window.marcarRoleoAtendido = function(idRegistro, btn) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
        const formData = new FormData();
        formData.append('idRegistro', idRegistro);
        fetch('?handler=MarcarRoleoAtendido', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
        .then(res => res.json()).then(data => {
            if (data.success) {
                Swal.fire({ icon: 'success', title: 'Notificación atendida', toast: true, position: 'top-end', timer: 1200, showConfirmButton: false });
                setTimeout(() => window.location.reload(), 700);
            } else {
                Swal.fire('Error', data.message || 'No se pudo marcar como atendido.', 'error');
            }
        }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
    };

        window.abrirDesdeAlerta = function(idOp, tabTargetId) {
        let modalAlertasEl = document.getElementById('modalAlertas');
        if (modalAlertasEl.classList.contains('show')) {
            let modalObj = bootstrap.Modal.getInstance(modalAlertasEl);
            if (modalObj) modalObj.hide();
        }
        document.getElementById('global-alert-box').style.display = 'none';
        window.location.href = '/Operaciones/Detalle/' + idOp;
    };

        // ==========================================
    // 3. CLIC EN FILA → NAVEGAR A DETALLE
    // ==========================================
    // El megaModal fue retirado: abrirModal() ya no construye nada en el DOM,
    // solo decide si el clic debe navegar a la vista de Detalle de la operación.
    window.abrirModal = function(element, event) {
        let e = event || window.event;
        if (e) {
            e.stopPropagation();
            let target = e.target || e.srcElement;
            if (target && (
                target.closest('.dropdown') ||
                target.closest('a') ||
                target.closest('button') ||
                target.closest('.badge-ing-pen') ||
                target.closest('.btn-action-dot') ||
                target.closest('input') ||
                target.closest('label')
            )) {
                return;
            }
        }

        let row = element ? element.closest('tr') : null;
        if (!row) return;
        if (row.getAttribute('data-cerrado') === 'true') return;

        let idOp = row.getAttribute('data-op-id');
        if (!idOp) return;
        window.location.href = '/Operaciones/Detalle/' + idOp;
    };

    document.addEventListener('input', function(e) {
    if (e.target.name === 'docUpdates.Dus' || e.target.name === 'docUpdates.Din') {
        const form = e.target.closest('form');
        if (!form) return;
        const chkLeg = form.querySelector('input[id^="chkLeg-"]');
        if (chkLeg && e.target.value.trim() !== '' && !chkLeg.checked) {
            chkLeg.checked = true;
            chkLeg.dispatchEvent(new Event('change'));
        }
    }
});
        

    // (movido a wwwroot/js/operaciones-common.js: window.sincronizarLogsAntesDeGuardar — auditoría oct-2026, dedup JS)

    // (movido a wwwroot/js/operaciones-common.js: window.abrirHistorial — auditoría oct-2026, dedup JS)

    
    // ==========================================
    // 11. EXTRACOSTOS Y FINANZAS
    // ==========================================
        // (movido a wwwroot/js/operaciones-common.js: window.configurarCamposDinamicosExtracosto — auditoría oct-2026, dedup JS)
        

        // (movido a wwwroot/js/operaciones-common.js: window.actualizarGrupoExtracosto — auditoría oct-2026, dedup JS)
            

    // (movido a wwwroot/js/operaciones-common.js: window.guardarExtracostoAjax — auditoría oct-2026, dedup JS)

    
    // (movido a wwwroot/js/operaciones-common.js: window.copiarCorreoPreview — auditoría oct-2026, dedup JS)

    document.getElementById('btnEnviarCorreoGenerado')?.addEventListener('click', function() {
        let idOp = this.getAttribute('data-id'); let tipo = this.getAttribute('data-tipo');
        let to = document.getElementById('emailToPreview').value; let cc = document.getElementById('emailCcPreview').value; let asunto = document.getElementById('emailAsuntoPreview').value;
        let htmlAttr = document.getElementById('btnEnviarCorreoGenerado').getAttribute('data-html-body'); let cuerpo = htmlAttr || document.getElementById('emailCuerpoPreview').value;
        let token = document.querySelector('input[name="__RequestVerificationToken"]').value;

        let formData = new FormData(); formData.append('id', idOp); formData.append('correoDestino', to); formData.append('copiaCc', cc); formData.append('asunto', asunto); formData.append('cuerpoHtml', cuerpo); formData.append('tipoCorreo', tipo);

        Swal.fire({ title: 'Enviando Correo...', text: 'Procesando notificación y guardando en bitácora...', allowOutsideClick: false, didOpen: () => { Swal.showLoading(); } });

        fetch('?handler=EnviarCorreoTransporte', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
        .then(res => res.json()).then(data => {
            if (data.success) {
                try {
                    let ctx = modalCtx() || document; let idContext = document.getElementById('btnEnviarCorreoGenerado').getAttribute('data-context') || idOp;
                    let logKey = null; let txtAccion = ''; let color = ''; let bg = '';
                    if (tipo === 'guiaAgencia') { logKey = 'guia'; txtAccion = 'ENVIADO A AGENCIA (GUÍA)'; color = '#0ea5e9'; bg = '#f0f9ff'; }
                    else if (tipo === 'instructivoAgencia') { logKey = 'instructivo'; txtAccion = 'ENVIADO A AGENCIA (INSTRUCTIVO)'; color = '#0ea5e9'; bg = '#f0f9ff'; }
                    else if (tipo === 'bookingAgencia') { logKey = 'booking'; txtAccion = 'ENVIADO A AGENCIA (BOOKING)'; color = '#0ea5e9'; bg = '#f0f9ff'; }
                    else if (tipo === 'capturaAgencia') { logKey = 'capturaAga'; txtAccion = 'ENVIADO A AGENCIA (CAPTURA)'; color = '#0ea5e9'; bg = '#f0f9ff'; }
                    else if (tipo === 'librevenAgencia') { logKey = 'libreven'; txtAccion = 'ENVIADO A AGENCIA (LIBRE VENTA)'; color = '#0ea5e9'; bg = '#f0f9ff'; }
                    else if (tipo === 'fullsetCliente') { logKey = 'fullset'; txtAccion = 'ENVIADO AL CLIENTE (FULL SET)'; color = '#eab308'; bg = '#fefce8'; }
                    else if (tipo === 'dhlCliente') { logKey = 'dhl'; txtAccion = 'ENVIADO AL CLIENTE (DHL)'; color = '#eab308'; bg = '#fefce8'; }
                    
                                        if(logKey) {
                        let logEl = ctx.querySelector(`#log-${logKey}-txt-${idContext}`);
                        if (logEl) {
                            if(logEl.innerHTML.includes('Sin registros')) logEl.innerHTML = '';
                            const ahora = new Date(); const div = document.createElement('div');
                            div.style.cssText = `border-left:2px solid ${color}; padding:6px; margin-bottom:6px; background:${bg}; border-top:1px solid #f1f5f9; border-right:1px solid #f1f5f9; border-bottom:1px solid #f1f5f9;`;
                            div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:${color}; text-transform:uppercase; font-size:10px;">${txtAccion}</span><span style="color:#64748b; font-size:9px;">${ahora.toLocaleDateString('es-CL')} ${ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' })}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>Operaciones Atilson</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">Correo generado y enviado con éxito.</div>`;
                            logEl.insertBefore(div, logEl.firstChild);
                        }

                        let stepper = ctx.querySelector(`#stepper-${logKey}-${idContext}`);
                        if (stepper) {
                            let steps = stepper.querySelectorAll('.step-item');
                            if (steps.length > 0 && !steps[0].classList.contains('completed')) {
                                steps[0].classList.add('completed'); let circle = steps[0].querySelector('.step-circle'); if(circle) circle.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>';
                            }
                        }

                        // Full Set y DHL viven en el formulario Courier, no en el documental  
                        // se guardan y sincronizan usando su propio handler.
                        let formContainer = ctx.querySelector(`#doc-base-form-${idContext}`) || ctx.querySelector(`#doc-global-form-${idContext}`);
                        let handlerGuardado = '?handler=QuickEditDocumental';
                        if (tipo === 'fullsetCliente' || tipo === 'dhlCliente') {
                            formContainer = ctx.querySelector(`#courier-form-${idContext}`);
                            handlerGuardado = '?handler=QuickEditCourier';
                        }
                        if (formContainer) {
                            let formEl = formContainer.closest('form');
                            if (formEl) {
                                if (typeof window.sincronizarLogsAntesDeGuardar === 'function') { window.sincronizarLogsAntesDeGuardar(formEl); }
                                let formDataSave = new FormData(formEl);
                                fetch(handlerGuardado, { method: 'POST', body: formDataSave, headers: { 'X-Requested-With': 'XMLHttpRequest' } }).catch(e => console.warn("Aviso auto-guardado:", e));
                            }
                        }
                    }
                } catch (innerError) { console.warn("Hubo un error gráfico, pero el envío se completó:", innerError); }

                Swal.fire({ icon: 'success', title: '¡Enviado y Guardado!', text: 'El correo fue enviado exitosamente. Archivos y progreso guardados.', confirmButtonColor: '#10b981' })
                .then(() => { let modalObj = bootstrap.Modal.getInstance(document.getElementById('modalGeneradorCorreos')); if (modalObj) modalObj.hide(); location.reload(); });
            } else { Swal.fire('Error', data.message || 'No se pudo procesar el envío.', 'error'); }
        }).catch((err) => { console.error(err); Swal.fire('Error de Sistema', 'Error interno: ' + err.message, 'error'); });
    });

    window.verDetalleCotizacionPortal = function(btn) {
        let c = {};
        try { c = JSON.parse(btn.getAttribute('data-cot') || '{}'); } catch (e) { console.error('Datos de cotización inválidos', e); }

        const esc = v => String(v ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
        const vacio = v => v === null || v === undefined || String(v).trim() === '';
        const campo = (label, valor, ancho) => vacio(valor) ? '' :
            `<div class="${ancho || 'col-6'} mb-2"><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">${label}</span><br><strong style="color:#0f172a;">${esc(valor)}</strong></div>`;
        const seccion = (titulo, icono, contenido) => contenido.trim() === '' ? '' :
            `<div class="mb-3 pb-2 border-bottom"><div style="font-size:10px; font-weight:900; color:#6d2d9e; text-transform:uppercase; letter-spacing:.5px; margin-bottom:8px;"><i class="bi ${icono} me-1"></i>${titulo}</div><div class="row g-2">${contenido}</div></div>`;

        const requisitos = [c.sag ? 'SAG' : null, c.sernapesca ? 'Sernapesca' : null, c.seguro ? 'Seguro' : null].filter(Boolean).join(' · ');

        let html = '';
        html += seccion('Cliente', 'bi-person-badge', campo('Cliente', c.cliente) + campo('Correo', c.correoCliente));
        html += seccion('Servicio', 'bi-briefcase', campo('Tipo de servicio', c.servicio) + campo('Modalidad', c.carga) + campo('Fecha solicitud', c.fechaSolicitud));
        html += seccion('Ruta', 'bi-geo-alt', campo('Puerto origen (POL)', c.pol) + campo('Puerto destino (POD)', c.pod) + (c.pol ? '' : campo('País origen', c.origen) + campo('País destino', c.destino)) + campo('Destino final', c.paisFinal) + campo('Mercancía', c.mercancia));
        // Solicitud de Reserva completa (oct-2026): nave elegida del itinerario o
        // embarque informado por el cliente (Terrestre/Documental sin Marítimo).
        html += seccion(c.desdeItinerario ? 'Nave elegida (itinerario)' : 'Embarque', 'bi-water',
            campo('Naviera', c.naviera) + campo('Nave', c.nave) + campo('ETA', c.etaItinerario) + campo('Booking', c.booking) +
            campo('Corte documental', c.corteDoc) + campo('Instructivo', c.instructivo) +
            campo('Cotización de origen', c.cotizacionOrigen ? '#' + c.cotizacionOrigen : null));
        const reeferInfo = [c.condicionReefer, (c.temperatura != null ? c.temperatura + '°C' : null), (c.humedad != null ? c.humedad + '% hum.' : null), c.ventilacion].filter(Boolean).join(' · ');
        html += seccion('Contenedores y Carga', 'bi-boxes', campo('Cantidad', c.contenedores) + campo('Tipo de contenedor', c.tipoContenedor) + campo('Parámetros reefer', reeferInfo, 'col-12'));
        html += seccion('Marítimo', 'bi-water', campo('Fecha tentativa embarque', c.fechaEmbarque));
        html += seccion('Transporte', 'bi-truck', campo('Planta', c.planta) + campo('Zona de carga', c.zona) + campo('Puerto', c.puertoTerrestre) + campo('Zona retiro/entrega', c.zonaEntrega) + campo('Depósito retiro', c.deposito) + campo('Fecha de carga', c.fechaCarga));
        html += seccion('Documental', 'bi-file-earmark-text', campo('Tipo de certificado', c.certificado) + campo('Requisitos', requisitos));
        html += `<div><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Observaciones del cliente</span><br><span style="color:#475569; font-style:italic; white-space:pre-wrap;">${vacio(c.comentarios) ? 'Sin requerimientos adicionales.' : esc(c.comentarios)}</span></div>`;

        const modalNotifEl = document.getElementById('modalNotificacionesUnificadas');
        const modalNotif = modalNotifEl ? bootstrap.Modal.getInstance(modalNotifEl) : null;
        if (modalNotif) modalNotif.hide();

        Swal.fire({
            title: '<i class="bi bi-briefcase text-warning me-2"></i>Solicitud de Reserva #' + esc(c.id),
            html: `<div style="text-align:left; font-size:13px; background:#f8fafc; padding:20px; border-radius:8px; border:1px solid #e2e8f0;">${html}</div>`,
            width: 640,
            showCancelButton: true,
            confirmButtonText: '<i class="bi bi-rocket-takeoff-fill me-1"></i> APROBAR E INICIAR OPERACIÓN',
            cancelButtonText: 'Cerrar',
            confirmButtonColor: '#0ea5e9',
            cancelButtonColor: '#64748b'
        }).then((result) => {
            if (result.isConfirmed) window.location.href = '/Operaciones/Create?cotId=' + c.id;
            else if (modalNotif) modalNotif.show();
        });
    };

    // (movido a wwwroot/js/operaciones-common.js: window.archivarSolicitudCotizacion — auditoría oct-2026, dedup JS)

    
    window.verDetalleCotizacion = function(id, servicio, carga, origen, destino, mercancia, comentarios) {
        let modalUnifObj = bootstrap.Modal.getInstance(document.getElementById('modalNotificacionesUnificadas'));
        if (modalUnifObj) modalUnifObj.hide();
        let modalObj = bootstrap.Modal.getInstance(document.getElementById('modalSolicitudesCotizacion'));
        if (modalObj) modalObj.hide(); 
        
        Swal.fire({
            title: '<i class="bi bi-briefcase text-warning me-2"></i>Detalle de Cotización #' + id,
            html: `
                <div style="text-align: left; font-size: 13px; background: #f8fafc; padding: 20px; border-radius: 8px; border: 1px solid #e2e8f0;">
                    <div class="row g-2 mb-3 pb-2 border-bottom">
                        <div class="col-6"><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Servicio:</span><br><strong style="color:#0f172a;">${servicio}</strong></div>
                        <div class="col-6"><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Modalidad:</span><br><strong style="color:#0f172a;">${carga}</strong></div>
                    </div>
                    <div class="row g-2 mb-3 pb-2 border-bottom">
                        <div class="col-6"><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Origen:</span><br><strong style="color:#0f172a; text-transform:uppercase;">${origen}</strong></div>
                        <div class="col-6"><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Destino:</span><br><strong style="color:#0f172a; text-transform:uppercase;">${destino}</strong></div>
                    </div>
                    <div class="mb-3 pb-2 border-bottom">
                        <span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Mercancía:</span><br>
                        <strong style="color:#0f172a; text-transform:uppercase;">${mercancia || 'No especificada'}</strong>
                    </div>
                    <div>
                        <span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Comentarios del Cliente:</span><br>
                        <span style="color:#475569; font-style:italic;">${comentarios || 'Sin requerimientos adicionales.'}</span>
                    </div>
                </div>
            `,
            width: 600,
            showCancelButton: true,
            confirmButtonText: '<i class="bi bi-rocket-takeoff-fill me-1"></i> APROBAR E INICIAR OPERACIÓN',
            cancelButtonText: 'Cerrar',
            confirmButtonColor: '#0ea5e9',
            cancelButtonColor: '#64748b',
        }).then((result) => {
            if(result.isConfirmed) {
                window.location.href = '/Operaciones/Create?cotId=' + id;
            } else {
                if (modalObj) modalObj.show();
            }
        });
    };

   

    // Reserva: PDF y correo realmente enviado se previsualizan juntos.
    // Backend y vista previa comparten la misma plantilla y validan versión en SQL.
    async function actualizarCorreoBookingIndex(inicial) {
        const botonEnviar = document.getElementById('btnEnviarAlClienteIndex');
        const id = botonEnviar?.getAttribute('data-id');
        const asunto = document.getElementById('emailAsuntoBookingPreview');
        const nota = document.getElementById('emailMensajeBookingPreview');
        const intro = document.getElementById('emailIntroBookingPreview');
        const aviso = document.getElementById('emailEstadoSmtpBooking');
        const frame = document.getElementById('iframeCorreoBookingPreview');
        const versionBadge = document.getElementById('indicadorEnvioIndex');
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        if (!id || !token) throw new Error('No se pudo validar la sesión o la operación.');
        aviso.textContent = 'Preparando la vista previa del mensaje...';
        botonEnviar.disabled = true;
        try {
            const data = new FormData();
            data.append('id', id);
            data.append('mensajePersonalizado', nota.value);
            data.append('introduccion', intro.value);
            data.append('__RequestVerificationToken', token);
            const res = await fetch('?handler=PrevisualizarCorreoReserva', {
                method: 'POST', body: data, credentials: 'same-origin',
                headers: { 'RequestVerificationToken': token }
            });
            const json = await res.json();
            if (!res.ok || !json.success) throw new Error(json.message || 'No se pudo preparar el mensaje.');
            frame.srcdoc = json.html;
            const actual = asunto.value.trim();
            const ultimoDefault = asunto.dataset.default || '';
            if (inicial || !actual || actual === ultimoDefault) asunto.value = json.asunto;
            asunto.dataset.default = json.asunto;
            botonEnviar.dataset.nuevaVersion = String(json.version);
            botonEnviar.dataset.huellaVistaPrevia = json.huella || '';
            if (inicial && !intro.value.trim()) intro.value = json.introduccionDefault || '';
            intro.dataset.previsualizado = intro.value;
            nota.dataset.previsualizado = nota.value;
            const esReenvio = !!json.enviadoAlgunaVez;
            versionBadge.style.display = 'inline-block';
            versionBadge.style.background = esReenvio ? '#dbeafe' : '#fef3c7';
            versionBadge.style.color = esReenvio ? '#1d4ed8' : '#a16207';
            versionBadge.textContent = esReenvio
                ? 'Reenvío: actualización V' + json.version
                : 'Primer envío: V1';
            const destino = document.getElementById('modalCorreoDestino');
            if (inicial && !destino.value.trim()) destino.value = json.correo || '';
            aviso.textContent = json.smtpConfigurado
                ? 'Vista previa generada. Revisa destinatario, asunto y contenido antes de enviar. ' + (json.smtpMensaje || '')
                : (json.smtpMensaje || 'SMTP no configurado: el correo no podrá enviarse.');
            aviso.className = json.smtpConfigurado ? 'small text-success mb-2' : 'small text-danger mb-2';
            botonEnviar.disabled = !json.smtpConfigurado;
        } catch (error) {
            botonEnviar.dataset.nuevaVersion = '';
            botonEnviar.dataset.huellaVistaPrevia = '';
            aviso.textContent = error.message || 'No se pudo preparar el correo.';
            aviso.className = 'small text-danger mb-2';
            botonEnviar.disabled = true;
        }
    }

    // Abre la vista previa editable del correo de booking. Se usa desde el menú
    // de cada fila y desde Detalle (/Operaciones/Index?correoBooking={id}).
    function abrirCorreoBookingIndex(idOp, correoCliente) {
        const iframe = document.getElementById('iframePdfIndex');
        const spinner = document.getElementById('pdfLoadingSpinnerIndex');
        const inputCorreo = document.getElementById('modalCorreoDestino');
        const btnEnviar = document.getElementById('btnEnviarAlClienteIndex');
        const asunto = document.getElementById('emailAsuntoBookingPreview');
        const nota = document.getElementById('emailMensajeBookingPreview');
        const intro = document.getElementById('emailIntroBookingPreview');
        inputCorreo.value = correoCliente;
        asunto.value = '';
        asunto.dataset.default = '';
        nota.value = '';
        nota.dataset.previsualizado = '';
        intro.value = '';
        intro.dataset.previsualizado = '';
        btnEnviar.dataset.id = idOp;
        btnEnviar.dataset.nuevaVersion = '';
        btnEnviar.dataset.huellaVistaPrevia = '';
        btnEnviar.disabled = true;
        if (iframe && spinner) {
            iframe.style.display = 'none';
            spinner.style.display = 'flex';
            iframe.onload = function() { spinner.style.display = 'none'; iframe.style.display = 'block'; };
            iframe.src = '/Operaciones/ReservaPdf/' + encodeURIComponent(idOp);
        }
        bootstrap.Modal.getOrCreateInstance(document.getElementById('modalPdfPreviewIndex')).show();
        actualizarCorreoBookingIndex(true);
    }
    window.abrirCorreoBookingIndex = abrirCorreoBookingIndex;

    document.addEventListener('click', function(e) {
        const btn = e.target.closest('.btn-enviar-correo');
        if (!btn) return;
        e.preventDefault();
        abrirCorreoBookingIndex(btn.getAttribute('data-id'), btn.getAttribute('data-correo') || '');
    }, true);

    // Llegada desde Detalle con ?correoBooking={id}: abre la vista previa y limpia la URL.
    (function () {
        const params = new URLSearchParams(window.location.search);
        const idDesdeUrl = params.get('correoBooking');
        if (!idDesdeUrl || !/^\d+$/.test(idDesdeUrl)) return;
        const abrir = () => {
            const btnFila = document.querySelector(`.btn-enviar-correo[data-id="${idDesdeUrl}"]`);
            abrirCorreoBookingIndex(idDesdeUrl, btnFila?.getAttribute('data-correo') || '');
            params.delete('correoBooking');
            const qs = params.toString();
            history.replaceState(null, '', window.location.pathname + (qs ? '?' + qs : ''));
        };
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', abrir);
        else abrir();
    })();

    document.getElementById('btnActualizarCorreoBookingPreview')?.addEventListener('click', function() {
        actualizarCorreoBookingIndex(false);
    });

    document.getElementById('btnPruebaSmtpBooking')?.addEventListener('click', async function() {
        const confirmacion = await Swal.fire({
            title: 'Prueba de correo real',
            text: 'Se enviará un mensaje únicamente al correo de tu cuenta administrativa. No cambia ningún booking.',
            icon: 'question',
            showCancelButton: true,
            confirmButtonText: 'Enviar prueba',
            cancelButtonText: 'Cancelar',
            confirmButtonColor: '#443094'
        });
        if (!confirmacion.isConfirmed) return;
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        if (!token) {
            Swal.fire('Sesión no validada', 'Actualiza la página para intentarlo.', 'warning');
            return;
        }
        const boton = this;
        const previo = boton.innerHTML;
        boton.disabled = true;
        boton.textContent = 'Comprobando envío...';
        try {
            const fd = new FormData();
            fd.append('__RequestVerificationToken', token);
            const res = await fetch('?handler=ProbarCorreoSmtp', {
                method: 'POST', body: fd, credentials: 'same-origin',
                headers: { 'RequestVerificationToken': token }
            });
            const data = await res.json();
            await Swal.fire({
                icon: res.ok && data.success ? 'success' : 'error',
                title: res.ok && data.success ? 'SMTP aceptó la prueba' : 'Prueba SMTP fallida',
                text: data.message || 'No se obtuvo respuesta del servidor.'
            });
        } catch (_) {
            Swal.fire('Sin respuesta', 'No fue posible contactar al servidor. Revisa la conexión.', 'error');
        } finally {
            boton.disabled = false;
            boton.innerHTML = previo;
        }
    });



    document.getElementById('btnEnviarAlClienteIndex')?.addEventListener('click', async function() {
        const id = this.dataset.id;
        const nuevaVersion = this.dataset.nuevaVersion;
        const destino = document.getElementById('modalCorreoDestino');
        const asunto = document.getElementById('emailAsuntoBookingPreview');
        const nota = document.getElementById('emailMensajeBookingPreview');
        const intro = document.getElementById('emailIntroBookingPreview');
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        if (!id || !nuevaVersion || !this.dataset.huellaVistaPrevia) {
            Swal.fire('Vista previa incompleta', 'Primero actualiza y revisa la vista previa del correo.', 'warning');
            return;
        }
        if (!destino.value.trim() || !destino.checkValidity() || !asunto.value.trim() || !asunto.checkValidity()) {
            Swal.fire('Revisa el correo', 'Completa destinatario y asunto correctamente.', 'warning');
            return;
        }
        if (nota.value !== nota.dataset.previsualizado || intro.value !== intro.dataset.previsualizado) {
            Swal.fire('Actualiza la vista previa', 'Modificaste el mensaje o la introducción. Pulsa «Actualizar vista previa» antes de enviarlo.', 'warning');
            return;
        }
        const old = this.innerHTML;
        this.disabled = true;
        this.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span> Enviando...';
        try {
            const fd = new FormData();
            fd.append('id', id);
            fd.append('correoDestino', destino.value.trim());
            fd.append('nuevaVersion', nuevaVersion);
            fd.append('asunto', asunto.value.trim());
            fd.append('mensajePersonalizado', nota.value);
            fd.append('introduccion', intro.value);
            fd.append('huellaVistaPrevia', this.dataset.huellaVistaPrevia);
            fd.append('__RequestVerificationToken', token);
            const res = await fetch('?handler=EnviarCorreoReserva', {
                method: 'POST', body: fd, credentials: 'same-origin',
                headers: { 'RequestVerificationToken': token }
            });
            const data = await res.json();
            if (!res.ok || !data.success) {
                if (data.enviado) {
                    await Swal.fire('Revisar estado de envío', data.message, 'warning');
                    return;
                }
                throw new Error(data.message || 'No se pudo enviar el correo.');
            }
            await Swal.fire({
                icon: 'success', title: 'Booking enviado',
                text: 'El servidor SMTP aceptó el correo V' + data.version + ' y se actualizó su estado.',
                confirmButtonColor: '#10b981'
            });
            bootstrap.Modal.getInstance(document.getElementById('modalPdfPreviewIndex'))?.hide();
            location.reload();
        } catch (error) {
            Swal.fire('Error al enviar', error.message || 'Comprueba el SMTP y vuelve a revisar.', 'error');
        } finally {
            this.innerHTML = old;
            this.disabled = false;
        }
    });

// ===========================================================
// Funciones movidas desde un <script> inline adicional en Index.cshtml
// (no formaba parte del bloque @section Scripts original; auditoría oct-2026)
// ===========================================================

//=============================================
    // FIX 1: BOTÓN PEN / ING (Control Stacking)
    // ==========================================
    window.toggleIngreso = function(elemento, idOp, event, idUnidadTecnica) {
        if(event) { event.stopPropagation(); event.preventDefault(); }

        // Si actualmente dice ING, pasará a falso (Pendiente) y viceversa
        let isIng = elemento.innerText.trim() === "ING";
        let nuevoEstado = !isIng;

        let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
        let formData = new FormData();
        formData.append('id', idOp);
        formData.append('ingresado', nuevoEstado);
        if (idUnidadTecnica) formData.append('idUnidadTecnica', idUnidadTecnica);

        fetch('?handler=ToggleIngreso', {
            method: 'POST',
            headers: { 'RequestVerificationToken': token },
            body: formData
        })
        .then(res => res.json())
        .then(data => {
            if(data.success) {
                // Cambio visual inmediato
                elemento.innerText = nuevoEstado ? "ING" : "PEN";
                elemento.classList.toggle("ing", nuevoEstado);
                elemento.classList.toggle("pen", !nuevoEstado);
                
                Swal.fire({ icon: 'success', title: 'Ingreso Actualizado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });
                
                // Recargamos rápido para que el backend recalcule y borre la alerta roja de "Atraso a puerto"
                setTimeout(() => window.location.reload(), 800); 
            } else {
                Swal.fire('Error', 'No se pudo actualizar el estado de ingreso.', 'error');
            }
        }).catch(err => Swal.fire('Error', 'Fallo de conexión.', 'error'));
    };

    // ==========================================
    // FIX 2: CAMBIAR ESTADO DE WORKFLOW
    // ==========================================
        window.cambiarEstadoOperacion = function(elemento, idOp, nuevoEstado, requiereFecha, event, idUnidadTecnica) {
        if(event) { event.stopPropagation(); event.preventDefault(); }

        let ejecutarCambio = (fechaVal) => {
            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            let formData = new FormData();
            formData.append('id', idOp);
            if (idUnidadTecnica !== null && idUnidadTecnica !== undefined) formData.append('idUnidadTecnica', idUnidadTecnica);
            formData.append('estado', nuevoEstado);
            if (fechaVal) formData.append('fechaAsociada', fechaVal);

            fetch('?handler=UpdateWorkflow', {
                method: 'POST',
                headers: { 'RequestVerificationToken': token },
                body: formData
            }).then(res => res.json()).then(data => {
                if (data.success) {
                    Swal.fire({ icon: 'success', title: 'Estado Workflow Actualizado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });
                    setTimeout(() => window.location.reload(), 1000);
                } else {
                    Swal.fire('Error', 'No se pudo actualizar el estado.', 'error');
                }
            }).catch(() => Swal.fire('Error', 'Fallo de conexión al servidor', 'error'));
        };

        // Si el estado necesita marcar una fecha (ej: Arribo a Planta, Entregado a Stacking)
        if (requiereFecha) {
            Swal.fire({
                title: 'Confirmar Evento',
                text: `Ingrese la fecha y hora para: ${nuevoEstado}`,
                input: 'datetime-local',
                showCancelButton: true,
                confirmButtonText: '<i class="bi bi-save"></i> Guardar',
                cancelButtonText: 'Cancelar'
            }).then((result) => {
                if (result.isConfirmed && result.value) ejecutarCambio(result.value);
            });
        } 
        // Si el estado es de cancelación, advertimos del extracosto automático
        else if (nuevoEstado.includes('Cancelado')) {
            Swal.fire({
                title: '¿Cancelar Operación?',
                text: 'Esta acción generará un reporte de extracosto automático por cancelación (Falso Flete). ¿Proceder?',
                icon: 'warning',
                showCancelButton: true,
                confirmButtonColor: '#dc2626',
                confirmButtonText: 'Sí, Cancelar'
            }).then(res => {
                if (res.isConfirmed) ejecutarCambio(null);
            });
        } 
        // Estados normales sin fecha
        else {
            ejecutarCambio(null);
        }
    };
    window.verDetalleCotizacion = function(id, servicio, carga, origen, destino, mercancia, comentarios) {
        let modalObj = bootstrap.Modal.getInstance(document.getElementById('modalSolicitudesCotizacion'));
        if (modalObj) modalObj.hide(); // Ocultamos el modal de la tabla para que el SweetAlert tome foco
        
        Swal.fire({
            title: '<i class="bi bi-briefcase text-warning me-2"></i>Detalle de Cotización #' + id,
            html: `
                <div style="text-align: left; font-size: 13px; background: #f8fafc; padding: 20px; border-radius: 8px; border: 1px solid #e2e8f0;">
                    <div class="row g-2 mb-3 pb-2 border-bottom">
                        <div class="col-6"><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Servicio:</span><br><strong style="color:#0f172a;">${servicio}</strong></div>
                        <div class="col-6"><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Modalidad:</span><br><strong style="color:#0f172a;">${carga}</strong></div>
                    </div>
                    <div class="row g-2 mb-3 pb-2 border-bottom">
                        <div class="col-6"><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Origen:</span><br><strong style="color:#0f172a; text-transform:uppercase;">${origen}</strong></div>
                        <div class="col-6"><span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Destino:</span><br><strong style="color:#0f172a; text-transform:uppercase;">${destino}</strong></div>
                    </div>
                    <div class="mb-3 pb-2 border-bottom">
                        <span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Mercancía:</span><br>
                        <strong style="color:#0f172a; text-transform:uppercase;">${mercancia || 'No especificada'}</strong>
                    </div>
                    <div>
                        <span style="font-size:10px; color:#64748b; font-weight:800; text-transform:uppercase;">Comentarios del Cliente:</span><br>
                        <span style="color:#475569; font-style:italic;">${comentarios || 'Sin requerimientos adicionales.'}</span>
                    </div>
                </div>
            `,
            width: 600,
            showCancelButton: true,
            confirmButtonText: '<i class="bi bi-rocket-takeoff-fill me-1"></i> APROBAR E INICIAR OPERACIÓN',
            cancelButtonText: 'Cerrar',
            confirmButtonColor: '#0ea5e9',
            cancelButtonColor: '#64748b',
        }).then((result) => {
            if(result.isConfirmed) {
                // Aquí, si quieres que pase los datos al Create, puedes mandarlos por QueryString
                // Por ejemplo:
                // window.location.href = '/Operaciones/Create?cotId=' + id + '&srv=' + encodeURIComponent(servicio) + '&pol=' + encodeURIComponent(origen) + '&pod=' + encodeURIComponent(destino);
                
                // Por ahora lo redirigimos al Create directamente
                window.location.href = '/Operaciones/Create';
            } else {
                if (modalObj) modalObj.show(); // Si cierra el swal, volvemos a mostrar la tabla
            }
        });
    };

// "Eliminar Registro" del menú de cada booking (Main Board). Es un borrado lógico
// (IsDeleted) y el servidor lo rechaza si el booking está cancelado: los
// cancelados se conservan siempre en el registro operativo.
window.eliminarOperacion = function (idOp, ev) {
    if (ev) { ev.preventDefault(); ev.stopPropagation(); }
    Swal.fire({
        title: '¿Eliminar este booking?',
        text: 'Se ocultará del Main Board. Los bookings cancelados no se pueden eliminar.',
        icon: 'warning',
        showCancelButton: true,
        confirmButtonText: 'Sí, eliminar',
        cancelButtonText: 'Cancelar',
        confirmButtonColor: '#dc2626'
    }).then(function (r) {
        if (!r.isConfirmed) return;
        var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
        var fd = new FormData();
        fd.append('id', idOp);
        fetch('?handler=Delete', { method: 'POST', headers: { 'RequestVerificationToken': tokenEl ? tokenEl.value : '' }, body: fd })
            .then(function (res) { return res.json(); })
            .then(function (data) {
                if (data.success) { Swal.fire({ icon: 'success', title: 'Eliminado', timer: 1200, showConfirmButton: false }); setTimeout(function () { window.location.reload(); }, 900); }
                else Swal.fire('No se puede eliminar', data.message || 'No se pudo eliminar el booking.', 'warning');
            })
            .catch(function () { Swal.fire('Error', 'Fallo de conexión.', 'error'); });
    });
};

/* Menús de estado del contenedor (oct-2026): se arman al abrir el desplegable en vez
   de venir repetidos en el HTML de cada fila (16 opciones × contenedor). */
(function () {
    var ESTADOS = [
        ['1. Solicitado (A Naviera)', 'Solicitado', false],
        ['2. Confirmado (Naviera)', 'Confirmado', false],
        ['3. Envío a Cliente', 'Envío a Cliente', false],
        ['4. En Ruta Planta', 'En Ruta Planta', false],
        ['4.5 Arribado a Planta', 'Arribado a Planta', true],
        ['5. Salida de Planta', 'Salida de Planta', true],
        ['6. En Ruta a Puerto', 'En Ruta a Puerto', false],
        ['7. En Puerto', 'En Puerto', true],
        ['8. Entregado a Stacking', 'Entregado a Stacking', true],
        ['9. Embarcado', 'Embarcado', false],
        ['10. Zarpe', 'Zarpe', false],
        ['11. Próximo Arribo', 'Próximo Arribo', false],
        ['12. Arribado a Destino', 'Arribado', true],
        ['13. Contenedor Retirado', 'Contenedor Retirado', true],
        ['14. Vacío Devuelto', 'Vacío Devuelto', true]
    ];

    function armarMenu(ul) {
        if (ul.getAttribute('data-armado') === '1') return;
        var idOp = parseInt(ul.getAttribute('data-op'), 10);
        var unidadTxt = ul.getAttribute('data-unidad');
        var idUnidad = unidadTxt && unidadTxt !== 'null' ? parseInt(unidadTxt, 10) : null;
        var frag = document.createDocumentFragment();

        var liHead = document.createElement('li');
        var h = document.createElement('h6');
        h.className = 'dropdown-header text-muted fw-bold';
        h.style.fontSize = '10px';
        h.textContent = 'ESTADO — ' + (ul.getAttribute('data-label') || '');
        liHead.appendChild(h);
        frag.appendChild(liHead);

        function item(texto, estado, conFecha, clase, icono) {
            var li = document.createElement('li');
            var a = document.createElement('a');
            a.className = 'dropdown-item py-2' + (clase ? ' ' + clase : '');
            a.href = '#';
            if (icono) { var i0 = document.createElement('i'); i0.className = 'bi ' + icono + ' me-1'; a.appendChild(i0); }
            a.appendChild(document.createTextNode(texto + ' '));
            if (conFecha) { var ic = document.createElement('i'); ic.className = 'bi bi-clock text-muted ms-1'; a.appendChild(ic); }
            a.addEventListener('click', function (ev) {
                window.cambiarEstadoOperacion(a, idOp, estado, conFecha, ev, idUnidad);
            });
            li.appendChild(a);
            return li;
        }

        ESTADOS.forEach(function (e) {
            frag.appendChild(item(e[0], e[1], e[2], e[1] === 'Vacío Devuelto' ? 'text-success fw-bold' : ''));
        });
        var liDiv = document.createElement('li');
        liDiv.innerHTML = '<hr class="dropdown-divider">';
        frag.appendChild(liDiv);
        frag.appendChild(item('Cancelado / Retraso', 'Cancelado', false, 'text-danger', 'bi-x-circle'));

        ul.appendChild(frag);
        ul.setAttribute('data-armado', '1');
    }

    document.addEventListener('show.bs.dropdown', function (e) {
        var toggle = e.target;
        var cont = toggle && toggle.closest ? toggle.closest('.estado-row-line') : null;
        var ul = cont ? cont.querySelector('ul[data-estado-menu]') : null;
        if (ul) armarMenu(ul);
    });
})();


/* ==========================================================
   MAIN BOARD — scrollbar superior sincronizado + botones
   ========================================================== */
(function () {
    function iniciarScrollOperaciones() {
        const real = document.getElementById('opsTableScroll');
        const top = document.getElementById('opsTopScroll');
        const inner = document.getElementById('opsTopScrollInner');
        const left = document.getElementById('opsScrollLeft');
        const right = document.getElementById('opsScrollRight');
        if (!real || !top || !inner) return;

        let sincronizando = false;
        function dimensionar() {
            inner.style.width = Math.max(real.scrollWidth, real.clientWidth) + 'px';
            actualizarBotones();
        }
        function actualizarBotones() {
            const max = Math.max(0, real.scrollWidth - real.clientWidth);
            if (left) left.disabled = real.scrollLeft <= 2;
            if (right) right.disabled = real.scrollLeft >= max - 2;
        }

        top.addEventListener('scroll', function () {
            if (sincronizando) return;
            sincronizando = true;
            real.scrollLeft = top.scrollLeft;
            sincronizando = false;
            actualizarBotones();
        }, { passive:true });

        real.addEventListener('scroll', function () {
            if (sincronizando) return;
            sincronizando = true;
            top.scrollLeft = real.scrollLeft;
            sincronizando = false;
            actualizarBotones();
        }, { passive:true });

        left?.addEventListener('click', () => real.scrollBy({ left:-520, behavior:'smooth' }));
        right?.addEventListener('click', () => real.scrollBy({ left:520, behavior:'smooth' }));

        // Shift + rueda también desplaza horizontalmente para mouse tradicional.
        real.addEventListener('wheel', function (e) {
            if (!e.shiftKey || Math.abs(e.deltaY) < Math.abs(e.deltaX)) return;
            e.preventDefault();
            real.scrollLeft += e.deltaY;
        }, { passive:false });

        if (window.ResizeObserver) new ResizeObserver(dimensionar).observe(real);
        window.addEventListener('resize', dimensionar);
        dimensionar();
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', iniciarScrollOperaciones);
    else
        iniciarScrollOperaciones();
})();

// ==========================================
// AVISO SERVICIO FINALIZADO (bandera verde por contenedor en el Main Board)
// Restaurado nov-2026: la función se perdió al mover el JS inline de
// Index.cshtml a este archivo (oct-2026) y la bandera no hacía nada.
// Abre #modalFinalizarServicio y calcula en vivo sobreestadías y Late Arrival
// solo como referencia visual: el cálculo que vale es el del servidor
// (OnPostFinalizarServicioAsync).
// ==========================================
(function () {
    function horasEntre(a, b) {
        if (!a || !b) return null;
        const ini = new Date(a), fin = new Date(b);
        if (isNaN(ini.getTime()) || isNaN(fin.getTime())) return null;
        return (fin.getTime() - ini.getTime()) / 3600000;
    }
    function formatoHoras(h) {
        if (h === null || h < 0) return '0h 0m';
        const total = Math.round(h * 60);
        return `${Math.floor(total / 60)}h ${total % 60}m`;
    }
    function pintarCaja(boxId, txtId, horas, libres) {
        const box = document.getElementById(boxId), txt = document.getElementById(txtId);
        if (!box || !txt) return;
        const exceso = horas === null ? 0 : Math.max(0, horas - (parseFloat(libres) || 0));
        txt.textContent = formatoHoras(exceso);
        box.style.background = exceso > 0 ? '#fef2f2' : 'var(--bg-soft)';
        box.style.borderColor = exceso > 0 ? '#fca5a5' : '';
        txt.style.color = exceso > 0 ? '#b91c1c' : '';
    }
    function recalcularFinalizar() {
        const v = id => (document.getElementById(id) || {}).value || '';
        pintarCaja('fsSobreestadiaBox', 'fsSobreestadiaTxt', horasEntre(v('fsLlegadaPlanta'), v('fsSalidaPlanta')), v('fsHorasLibresPlanta'));
        pintarCaja('fsSobreestadiaPuertoBox', 'fsSobreestadiaPuertoTxt', horasEntre(v('fsLlegadaPuerto'), v('fsSalidaPuerto')), v('fsHorasLibresPuerto'));
        const lar = document.getElementById('fsLarContainer');
        if (lar) {
            const cut = v('finCutOff'), llegada = v('fsLlegadaPuerto');
            const hayLar = cut && llegada && new Date(llegada) > new Date(cut);
            lar.classList.toggle('d-none', !hayLar);
        }
    }

    window.abrirModalFinalizar = function (idOp, booking, reqTerrestre, cutOff, idUnidadTecnica, containerLabel, llegadaPlanta, salidaPlanta, llegadaPuerto, salidaPuerto, event) {
        if (event) { event.preventDefault(); event.stopPropagation(); }
        const modalEl = document.getElementById('modalFinalizarServicio');
        if (!modalEl || !window.bootstrap) return;

        const set = (id, val) => { const el = document.getElementById(id); if (el) el.value = val || ''; };
        set('finOpId', idOp);
        set('finCutOff', cutOff);
        set('finIdUnidadTecnica', idUnidadTecnica);
        const titulo = document.getElementById('finBookingTitle');
        if (titulo) titulo.textContent = booking || '';

        const labelWrap = document.getElementById('finContainerLabelWrap');
        const labelSpan = document.getElementById('finContainerLabel');
        if (labelWrap && labelSpan) {
            const tieneLabel = containerLabel && String(containerLabel).trim() !== '';
            labelSpan.textContent = tieneLabel ? containerLabel : '';
            labelWrap.classList.toggle('d-none', !tieneLabel);
        }

        set('fsCorreoCliente', '');
        set('fsLlegadaPlanta', llegadaPlanta);
        set('fsSalidaPlanta', salidaPlanta);
        set('fsLlegadaPuerto', llegadaPuerto);
        set('fsSalidaPuerto', salidaPuerto);

        const chkEscaner = document.getElementById('fsSorteoEscaner');
        if (chkEscaner) chkEscaner.checked = false;
        const divEscaner = document.getElementById('fsSorteoEscanerFileDiv');
        if (divEscaner) divEscaner.style.display = 'none';

        const secTerrestre = document.getElementById('fs-sec-terrestre');
        if (secTerrestre) secTerrestre.classList.toggle('d-none', !reqTerrestre);

        recalcularFinalizar();
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    };

    function iniciarModalFinalizar() {
        const modalEl = document.getElementById('modalFinalizarServicio');
        if (!modalEl) return;
        modalEl.querySelectorAll('.fs-calc-planta, .fs-calc-puerto, #fsHorasLibresPlanta, #fsHorasLibresPuerto')
            .forEach(el => { el.addEventListener('input', recalcularFinalizar); el.addEventListener('change', recalcularFinalizar); });
        const chkEscaner = document.getElementById('fsSorteoEscaner');
        const divEscaner = document.getElementById('fsSorteoEscanerFileDiv');
        if (chkEscaner && divEscaner) {
            chkEscaner.addEventListener('change', () => { divEscaner.style.display = chkEscaner.checked ? '' : 'none'; });
        }
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', iniciarModalFinalizar);
    else
        iniciarModalFinalizar();
})();
