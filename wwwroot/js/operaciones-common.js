// =========================================================================
// FUNCIONES COMPARTIDAS: Pages/Operaciones/Index.cshtml y Detalle.cshtml
// =========================================================================
// Estas funciones estaban duplicadas byte a byte (o casi) en ambos archivos
// (hallazgo de la auditoría oct-2026: "JS duplicado byte a byte... 9
// funciones globales, confirmado idéntico en window.sincronizarLogsAntesDeGuardar").
// Se centralizan aquí para que un fix futuro se aplique una sola vez en
// lugar de tener que recordar tocar los dos archivos (el mismo riesgo que
// ya causó antes el bug de RequiereSag/RequiereSernapesca corregido en otra
// parte de esta auditoría).
//
// Dependen de "modalCtx()" y "escapeHtmlAtilson()" (ver abajo), y de
// variables globales como "Swal"/"bootstrap" que ya cargan ambas páginas.
// "modalCtx()" SÍ sigue siendo local a cada página (en Index.cshtml acota
// la búsqueda al modal dinámico "#modalDynamicBody"; en Detalle.cshtml
// devuelve "document" porque toda la página es el detalle) — no se unifica
// porque representa una diferencia real de arquitectura entre ambas vistas,
// no una duplicación accidental.
// =========================================================================

// Escapa texto libre antes de insertarlo en innerHTML armado a mano (ver
// abrirHistorial más abajo). Sin esto, un comentario/correo guardado con
// HTML/script queda como XSS persistente para cualquiera que abra el
// historial de esta operación.
function escapeHtmlAtilson(valor) {
    return String(valor ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

window.actualizarAnilloPorPct = function(ringId, pct) {
    const ring = modalCtx()?.querySelector(`#${ringId}`) || document.getElementById(ringId);
    if (!ring) return;
    ring.style.setProperty('--pct', pct);
    ring.setAttribute('data-complete', pct >= 100 ? 'true' : 'false');
};

window.actualizarAnilloProgreso = function(ringId, hechos, total) {
    window.actualizarAnilloPorPct(ringId, total > 0 ? Math.round((hechos / total) * 100) : 0);
};

// Historial de hitos de la operación (ver RegistrarHito en el backend). El
// texto libre (autor/fecha/mensaje/línea) viene de comentarios guardados por
// usuarios — se escapa con escapeHtmlAtilson antes de interpolarlo en el
// innerHTML del timeline (fix de XSS almacenado de esta auditoría).
window.abrirHistorial = function(btn) {
    const booking = btn.getAttribute('data-booking'); const comentariosBrutos = btn.getAttribute('data-comentarios');
    document.getElementById('historialBookingRef').innerText = booking; const body = document.getElementById('historialCuerpo'); body.innerHTML = '';
    if (!comentariosBrutos || comentariosBrutos.trim() === '') {
        body.innerHTML = '<div class="text-center text-muted p-5"><i class="bi bi-inboxes fs-1 opacity-50 mb-2 d-block"></i><span class="fw-bold" style="font-size:12px;">Sin registros</span><p style="font-size:10px;">Esta operación aún no tiene eventos en su historial.</p></div>';
    } else {
        const lineas = comentariosBrutos.split('\n').filter(l => l.trim() !== ''); let html = '<div class="timeline-atilson">'; const regex = /\[(\d{2}\/\d{2}\/\d{4}\s\d{2}:\d{2})\s+(\w+)\]\s+(.*)/;
        lineas.forEach(linea => {
            let match = linea.match(regex);
            if(match) {
                let fecha = match[1]; let autor = match[2].toUpperCase(); let mensaje = match[3];
                let colorStr = 'secondary'; let bgBadge = '#e2e8f0';
                if (autor.includes('FINANZAS')) { colorStr = 'success'; bgBadge = '#10b981'; } else if (autor.includes('OPERACIONES')) { colorStr = 'primary'; bgBadge = '#3b82f6'; }
                html += `<div class="timeline-item"><div class="timeline-badge" style="background-color: ${bgBadge};"></div><div class="timeline-content"><div class="d-flex justify-content-between align-items-center mb-1"><span class="badge bg-${colorStr} bg-opacity-10 text-${colorStr} border border-${colorStr}" style="font-size:8px; letter-spacing:0.5px;"><i class="bi bi-person-fill me-1"></i> ${escapeHtmlAtilson(autor)}</span><span class="text-muted fw-bold" style="font-size:9px;">${escapeHtmlAtilson(fecha)}</span></div><p class="mb-0 fw-bold text-dark" style="font-size:11px; line-height:1.4;">${escapeHtmlAtilson(mensaje)}</p></div></div>`;
            } else {
                html += `<div class="timeline-item"><div class="timeline-badge bg-secondary"></div><div class="timeline-content"><p class="mb-0 text-muted" style="font-size:11px;">${escapeHtmlAtilson(linea)}</p></div></div>`;
            }
        });
        html += '</div>'; body.innerHTML = html;
    }
    new bootstrap.Modal(document.getElementById('modalHistorial')).show();
};

window.actualizarGrupoExtracosto = function(select) {
    const opcionSeleccionada = select.options[select.selectedIndex];
    const grupo = opcionSeleccionada ? opcionSeleccionada.getAttribute('data-grupo') : '';
    const hidden = document.getElementById('extracostoGrupoOperativoFinal');
    if (hidden) hidden.value = grupo || '';
};

window.archivarSolicitudCotizacion = function(idCotizacion, btn, event) {
    if (event) { event.stopPropagation(); event.preventDefault(); }

    Swal.fire({
        title: '¿Descartar Solicitud?',
        text: "Esta solicitud se marcará como gestionada y desaparecerá.",
        icon: 'warning',
        showCancelButton: true,
        confirmButtonColor: '#dc2626',
        cancelButtonColor: '#64748b',
        confirmButtonText: 'Sí, descartar'
    }).then((result) => {
        if (result.isConfirmed) {
            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            let formData = new FormData();
            formData.append('idCotizacion', idCotizacion);

            fetch('?handler=ArchivarCotizacion', {
                method: 'POST',
                headers: { 'RequestVerificationToken': token },
                body: formData
            })
            .then(res => res.json())
            .then(data => {
                if (data.success) {
                    let row = btn.closest('tr');
                    if (row) row.remove();
                    Swal.fire({ icon: 'success', title: 'Gestionada', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });

                    // Si no quedan más filas, recargamos para ocultar el botón general de la campana
                    let tbody = document.querySelector('#modalSolicitudesCotizacion tbody');
                    if (tbody && tbody.children.length === 0) {
                        setTimeout(() => window.location.reload(), 1500);
                    }
                } else {
                    Swal.fire('Error', data.message || 'No se pudo procesar.', 'error');
                }
            })
            .catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
        }
    });
};

window.configurarCamposDinamicosExtracosto = function() {
    const select = document.getElementById('extracostoTipoSelect');
    const area = document.getElementById('extracostoDynamicArea');
    const val = (select.value || '').toUpperCase();
    let html = '';

    // Referencia automática: si el concepto elegido es de una naviera específica
    // y tiene tarifa cargada en Comercial, se muestra como dato de referencia —
    // no reemplaza el monto final, que sigue siendo competencia de Finanzas.
    const opcionSeleccionada = select.options[select.selectedIndex];
    const valorRef = opcionSeleccionada ? opcionSeleccionada.getAttribute('data-valor') : null;
    const monedaRef = opcionSeleccionada ? opcionSeleccionada.getAttribute('data-moneda') : null;
    if (valorRef && parseFloat(valorRef) > 0) {
        html += `<div class="p-2 mb-2 rounded" style="background:#f0fdf4; border:1px solid #bbf7d0;"><span class="erp-label text-success m-0"><i class="bi bi-check-circle-fill me-1"></i> Tarifa de referencia (Comercial)</span><div class="fw-bold text-success" style="font-size:14px;">${monedaRef} ${parseFloat(valorRef).toLocaleString('es-CL')}</div><small class="text-muted" style="font-size:10px;">Finanzas confirmará el monto final al procesar el extracosto.</small></div>`;
    }

    if (val.includes('SOBREESTADIA') || val.includes('SOBREESTADÍA') || val.includes('REEFER') || val.includes('LATE ARRIVAL') || val.includes('LAR')) {
        html = `<label class="erp-label text-info"><i class="bi bi-clock-history me-1"></i> Tiempo Total / Cantidad</label><input type="number" id="dynValorCantidad" class="form-control erp-input fw-bold text-info" placeholder="Ej: 5" min="1" required /><small class="text-muted" style="font-size:10px; display:block; margin-top:4px;">Ingrese la cantidad total. Finanzas calculará la tarifa.</small>`;
    } else if (val.includes('FALSO FLETE EN RUTA')) {
        html = `<label class="erp-label text-info"><i class="bi bi-signpost-split me-1"></i> Distancia Recorrida</label><select id="dynValorCantidad" class="form-select erp-input fw-bold text-info" required><option value="">-- Seleccione Dónde Ocurrió --</option><option value="Menos de la mitad">Menos de la mitad del trayecto</option><option value="Mas de la mitad">Más de la mitad del trayecto</option></select><small class="text-muted" style="font-size:10px; display:block; margin-top:4px;">Finanzas aplicará el porcentaje pactado según el tramo recorrido.</small>`;
    } else if (val.includes('ALMACENAMIENTO')) {
        html = `<label class="erp-label text-info"><i class="bi bi-calendar-event me-1"></i> Días de Almacenaje</label><input type="number" id="dynValorCantidad" class="form-control erp-input fw-bold text-info" placeholder="Ej: 3" min="1" required /><small class="text-muted" style="font-size:10px; display:block; margin-top:4px;">Días que el contenedor pernoctó en zona segura extra-portuaria.</small>`;
    } else if (val.includes('INTERPLANTA')) {
         html = `<label class="erp-label text-info"><i class="bi bi-geo-alt me-1"></i> Distancia Recorrida (Kilómetros)</label><input type="number" id="dynValorCantidad" class="form-control erp-input fw-bold text-info" placeholder="Ej: 25" min="1" required /><small class="text-muted" style="font-size:10px; display:block; margin-top:4px;">Si la distancia es menor a 20km se aplica tarifa plana. Si es mayor, Finanzas cobra 2 tramos.</small>`;
    }

    if (html !== '') { area.innerHTML = html; area.classList.remove('d-none'); } else { area.innerHTML = ''; area.classList.add('d-none'); }
};

window.copiarCorreoPreview = function() {
    const texto = document.getElementById('emailCuerpoPreview').value;
    navigator.clipboard.writeText(texto).then(() => { Swal.fire({ icon: 'success', title: 'Copiado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false }); });
};

window.guardarExtracostoAjax = function(form, btn) {
    if (!form) return;
    let dynVal = form.querySelector('#dynValorCantidad'); let justificacion = form.querySelector('#extracostoJustificacionTxt').value; let motivoFinal = form.querySelector('#extracostoMotivoFinal');
    let prefijo = "";
    if (dynVal) { let labelNodes = form.querySelector('#extracostoDynamicArea label').childNodes; let labelName = labelNodes[labelNodes.length - 1].textContent.trim(); prefijo = `[${labelName.toUpperCase()}: ${dynVal.value}] - `; }
    motivoFinal.value = prefijo + justificacion;
    const originalHtml = btn.innerHTML; btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Enviando...'; btn.disabled = true;

    fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
    .then(res => res.json()).then(data => {
        btn.innerHTML = originalHtml; btn.disabled = false;
        if (data.success) {
            let modalObj = bootstrap.Modal.getInstance(document.getElementById('modalAddCostoGlobal')); if (modalObj) modalObj.hide(); form.reset();
            Swal.fire({ icon: 'success', title: 'Extracosto reportado', text: 'Vuelve a abrir esta operación para verlo reflejado.', toast: true, position: 'top-end', timer: 2500, showConfirmButton: false });
        } else { Swal.fire('Error', data.message || 'No se pudo reportar el extracosto.', 'error'); }
    }).catch(() => { btn.innerHTML = originalHtml; btn.disabled = false; Swal.fire('Error', 'Fallo de conexión al reportar el extracosto.', 'error'); });
};

window.sincronizarLogsAntesDeGuardar = function(form) {
    const mapeoLogs = {
        'ori': 'Origen', 'fit': 'Fitosanitario', 'san': 'Sanitario', 'cap': 'Captura', 'coa': 'Coa', 'dt':  'Dt', 'cod': 'Codaut', 'cla': 'Clave', 'nep': 'Neppex', 'factura': 'Factura', 'instructivo': 'Instructivo', 'packing': 'Packing', 'extra': 'CertExtra', 'libreven': 'LibreVenta', 'guia': 'Guia', 'booking': 'BookingAtilson', 'fullset': 'FullSet', 'dhl': 'Dhl', 'capturaAga': 'CapturaAga', 'matriz': 'Matriz'
    };
    for (let tipo in mapeoLogs) {
        let nombreCompleto = mapeoLogs[tipo]; let logDivs = form.querySelectorAll(`[id^="log-${tipo}-txt"]`); let mejorHtml = '';
        logDivs.forEach(logDiv => {
            let htmlContent = logDiv.innerHTML.trim(); if (htmlContent.includes('Sin registros.')) htmlContent = '';
            if (htmlContent.length > mejorHtml.length) mejorHtml = htmlContent;
        });
        let hiddenInputs = form.querySelectorAll(`input[name="docUpdates.Log${nombreCompleto}"]`);
        hiddenInputs.forEach(inp => { inp.value = mejorHtml; });
    }
};
