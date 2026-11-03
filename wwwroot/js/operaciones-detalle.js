// detalleOpData(): lee los datos de la operación expuestos como data-attributes en
// #detalle-op-data (ver Pages/Operaciones/Detalle.cshtml) — reemplaza la interpolación
// directa de @Model.Op.* que tenía este script cuando vivía inline en la vista.
function detalleOpData() { return document.getElementById('detalle-op-data').dataset; }

                function modalCtx() { return document; }
        function mq(selector) { return document.querySelector(selector); }
        function mqAll(selector) { return document.querySelectorAll(selector); }

        // escapeHtmlAtilson: (movido a wwwroot/js/operaciones-common.js — auditoría oct-2026, dedup JS)

        $(document).ready(function () {
            $('.select2-enable').select2({ width: '100%' });
        });

                window.toggleEditMode = function(formId, btnTrigger) {
    let formContainer = mq(`#${formId}`);
    if (!formContainer) return;

    // La tarifa pendiente ya NO bloquea la pestaña (antes se impedía editar por
    // completo con este mismo check). Ahora solo se informa: el aviso
    // ".op-tarifa-alert" ya queda visible arriba, junto al título de la
    // sección, tanto en modo vista como en modo edición — así se puede seguir
    // trabajando el resto de los datos mientras Comercial cierra la tarifa.

    formContainer.classList.add('edit-mode');

    // Vista resumen (premium): si esta sección tiene un panel de resumen (se muestra
    // cuando está 100% completa en vez del formulario plano — ver Marítimo/Transporte
    // en Detalle.cshtml / _PanelTransporte.cshtml), al Editar se oculta el resumen y se
    // muestra el formulario real (.op-form-core), que es el que este mismo toggle ya
    // desbloquea abajo con la lógica existente.
    const resumenPanelId = formContainer.getAttribute('data-resumen-panel');
    if (resumenPanelId) {
        const resumenPanelEl = document.getElementById(resumenPanelId);
        if (resumenPanelEl) resumenPanelEl.classList.add('d-none');
        const corePanelId = formContainer.getAttribute('data-core-panel');
        const corePanelEl = corePanelId ? document.getElementById(corePanelId) : null;
        if (corePanelEl) corePanelEl.classList.remove('d-none');
    }

    formContainer.querySelectorAll(
        'input.inline-field, input.banner-input, input.erp-input, ' +
        'input.doc-val-input, .pro-input, ' +
        'textarea.inline-field, textarea.erp-input, textarea.style-xs'
    ).forEach(inp => {
        if (!inp.classList.contains('doc-val-input') &&
            !inp.classList.contains('view-only-depositoretiro') &&
            !inp.classList.contains('view-only-puertoentrega') &&
            !inp.classList.contains('view-only-terminalterrestre') &&
            !inp.classList.contains('view-only-terminalmar') &&
            !inp.classList.contains('view-only-planta') &&
            !inp.classList.contains('view-only-zonacarga') &&
            !inp.classList.contains('view-only-transbordo') &&
            !inp.classList.contains('view-only-link')) {
            inp.removeAttribute('readonly');
            inp.removeAttribute('disabled');
        }
    });

    formContainer.querySelectorAll('select.inline-field, select.banner-input, select.erp-input, select.select-sm, select.pro-input').forEach(sel => {
        sel.removeAttribute('disabled');
        sel.style.pointerEvents = 'auto';
        sel.style.appearance = '';
        sel.style.webkitAppearance = '';
    });
    if (typeof $ !== 'undefined') { $(formContainer).find('select.select2-enable').prop('disabled', false); }

    formContainer.querySelectorAll('input[type="checkbox"].inline-field, input[type="radio"].inline-field, .pro-check').forEach(chk => {
        chk.removeAttribute('disabled');
    });

    formContainer.querySelectorAll('.edit-only-file, input[type="file"]').forEach(el => {
        el.removeAttribute('disabled');
    });

    const pairs = [
        { view: 'view-only-depositoretiro', edit: 'edit-only-depositoretiro' },
        { view: 'view-only-puertoentrega',  edit: 'edit-only-puertoentrega'  },
        { view: 'view-only-terminalterrestre', edit: 'edit-only-terminalterrestre' },
        { view: 'view-only-terminalmar',    edit: 'edit-only-terminalmar'    },
        { view: 'view-only-planta',         edit: 'edit-only-planta'         },
        { view: 'view-only-empresatransporte', edit: 'edit-only-empresatransporte' },
        { view: 'view-only-zonacarga',      edit: 'edit-only-zonacarga'      },
        { view: 'view-only-link',           edit: 'edit-only-link'           },
        { view: 'view-only-transbordo',     edit: 'edit-only-transbordo'     }
    ];

    pairs.forEach(pair => {
        formContainer.querySelectorAll(`.${pair.view}`).forEach(el => { el.style.display = 'none'; });
        formContainer.querySelectorAll(`.${pair.edit}`).forEach(el => {
            el.style.removeProperty('display');
            el.style.display = '';
            el.removeAttribute('disabled');
        });
    });

    formContainer.querySelectorAll('select.edit-only-transbordo, select.edit-only-puertoentrega, select[name="IdPuertoOrigen"], select[name="IdPuertoDestino"]').forEach(sel => {
        sel.dispatchEvent(new Event('change'));
    });
    formContainer.querySelectorAll('input[type="radio"][name^="TipoDec_"]:checked').forEach(r => r.dispatchEvent(new Event('change')));
    formContainer.querySelectorAll('input[id^="chkCan-"], input[id^="chkAcl-"], input[id^="chkLeg-"]').forEach(chk => {
        chk.dispatchEvent(new Event('change'));
    });

    let saveBtnWrapper = formContainer.querySelector('.btn-save-wrapper');
    if (saveBtnWrapper) saveBtnWrapper.classList.remove('d-none');
    if (btnTrigger) btnTrigger.style.display = 'none';

    formContainer.querySelectorAll('.btn-save-cert').forEach(btn => { btn.style.display = 'inline-block'; });
};

window.cancelEditMode = function(formId) {
    let formContainer = mq(`#${formId}`);
    if (!formContainer) return;

    formContainer.classList.remove('edit-mode');

    // Vista resumen (premium): al cancelar, si la sección sigue completa (nadie la dejó
    // a medio llenar), vuelve a mostrarse el resumen en vez del formulario plano.
    const resumenPanelIdCancel = formContainer.getAttribute('data-resumen-panel');
    if (resumenPanelIdCancel && formContainer.getAttribute('data-resumen-completo') === 'true') {
        const resumenPanelElCancel = document.getElementById(resumenPanelIdCancel);
        if (resumenPanelElCancel) resumenPanelElCancel.classList.remove('d-none');
        const corePanelIdCancel = formContainer.getAttribute('data-core-panel');
        const corePanelElCancel = corePanelIdCancel ? document.getElementById(corePanelIdCancel) : null;
        if (corePanelElCancel) corePanelElCancel.classList.add('d-none');
    }

    formContainer.querySelectorAll('input.inline-field, input.banner-input, input.erp-input, input.doc-val-input, textarea.inline-field, textarea.erp-input, textarea.style-xs, .pro-input').forEach(inp => {
        if (inp.type !== 'checkbox' && inp.type !== 'radio' && inp.type !== 'file') {
            inp.setAttribute('readonly', true);
        }
    });

    formContainer.querySelectorAll('select.inline-field, select.banner-input, select.erp-input, select.select-sm, .pro-input').forEach(sel => {
        if (sel.tagName === 'SELECT') {
            sel.setAttribute('disabled', true);
            sel.style.pointerEvents = 'none';
            sel.style.appearance = 'none';
        }
    });
    if (typeof $ !== 'undefined') { $(formContainer).find('select.select2-enable').prop('disabled', true); }

    formContainer.querySelectorAll('input[type="checkbox"].inline-field, input[type="radio"].inline-field, .pro-check').forEach(chk => {
        chk.setAttribute('disabled', true);
    });

    formContainer.querySelectorAll('.edit-only-file, input[type="file"]').forEach(el => {
        el.setAttribute('disabled', true);
    });

    const pairs = [
        { view: 'view-only-depositoretiro', edit: 'edit-only-depositoretiro' },
        { view: 'view-only-puertoentrega',  edit: 'edit-only-puertoentrega'  },
        { view: 'view-only-terminalterrestre', edit: 'edit-only-terminalterrestre' },
        { view: 'view-only-terminalmar',    edit: 'edit-only-terminalmar'    },
        { view: 'view-only-planta',         edit: 'edit-only-planta'         },
        { view: 'view-only-empresatransporte', edit: 'edit-only-empresatransporte' },
        { view: 'view-only-zonacarga',      edit: 'edit-only-zonacarga'      },
        { view: 'view-only-link',           edit: 'edit-only-link'           },
        { view: 'view-only-transbordo',     edit: 'edit-only-transbordo'     }
    ];

    pairs.forEach(pair => {
        formContainer.querySelectorAll(`.${pair.view}`).forEach(el => { el.style.display = ''; });
        formContainer.querySelectorAll(`.${pair.edit}`).forEach(el => { el.style.display = 'none'; el.setAttribute('disabled', true); });
    });

    let saveBtnWrapper = formContainer.querySelector('.btn-save-wrapper');
    if (saveBtnWrapper) saveBtnWrapper.classList.add('d-none');

    let tabPane = formContainer.closest('.tab-pane') || formContainer.parentElement;
    if (tabPane) {
        tabPane.querySelectorAll('.btn-edit-inline, .btn-edit-banner, .btn-outline-purple').forEach(btn => { btn.style.display = ''; });
    }

    formContainer.querySelectorAll('.btn-save-cert').forEach(btn => { btn.style.display = 'none'; });
};

        window.guardarFormularioDetalle = function (form, btn) {
            if (!form) return;
            const original = btn.innerHTML;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Guardando...';
            btn.disabled = true;

            fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(res => res.json()).then(data => {
                    if (data.success) {
                        window.location.reload();
                    } else {
                        btn.innerHTML = original; btn.disabled = false;
                        Swal.fire('Error', data.message || 'No se pudo guardar la información.', 'error');
                    }
                })
                .catch(() => { btn.innerHTML = original; btn.disabled = false; Swal.fire('Error', 'Fallo de conexión.', 'error'); });
        };

        document.getElementById('gateGestionSelect')?.addEventListener('change', function () {
            const wrapper = document.getElementById('gatePagoWrapper');
            if (wrapper) wrapper.style.display = this.value === 'Cliente gestiona directo' ? 'none' : '';
        });
                window.toggleTransbordoDetalle = function (select) {
            const viewInp = document.getElementById('view-transbordo');
            const inpTransbordo = document.getElementById('inp-transbordo');
            if (select.value === 'TRANSBORDO') { inpTransbordo.style.display = 'inline-block'; inpTransbordo.removeAttribute('disabled'); }
            else { inpTransbordo.style.display = 'none'; inpTransbordo.value = ''; }
        };

        window.guardarResponsabilidadDetalle = function (idOperacion, concepto, selectPagado, selectResponsable) {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('idOperacion', idOperacion);
            formData.append('concepto', concepto);
            formData.append('pagadoPor', selectPagado.value);
            formData.append('responsable', selectResponsable.value);

            fetch('?handler=GuardarResponsabilidad', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                .then(res => res.json()).then(data => {
                    if (data.success) { Swal.fire({ icon: 'success', title: 'Actualizado', toast: true, position: 'top-end', timer: 1200, showConfirmButton: false }); }
                    else { Swal.fire('Error', data.message || 'No se pudo guardar.', 'error'); }
                }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
        };

        // Roleo / Cancelación / No Show (oct-2026):
        //  - Spot: si faltan "diasUmbral" días o menos para el zarpe (ETD), la
        //    penalidad se aplica sola; la ejecutiva puede ajustar el monto USD por
        //    contenedor y registrar la nueva nave / zarpe.
        //  - Contrato: sin costo (salvo navieras marcadas "aplica a todo evento").
        //  - La penalidad queda en USD en Finanzas (no se convierte a CLP).
        window.registrarRoleoCancelacionDetalle = function (idOp, tipoTarifaActual, nombreNaviera, diasUmbral, siempreAplica, totalContenedores, etdIso, montoNavieraUsd) {
            const umbral = parseInt(diasUmbral, 10) || 7;
            const navSiempre = siempreAplica === 'true';
            let diasAlZarpe = null;
            if (etdIso) {
                const etd = new Date(etdIso);
                if (!isNaN(etd.getTime())) diasAlZarpe = Math.ceil((etd.getTime() - Date.now()) / 86400000);
            }
            const lbl = 'font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:6px;';
            const lblSm = 'font-size:9px; font-weight:800; color:#94a3b8;';

            Swal.fire({
                title: 'Registrar Roleo / Cancelación / No Show',
                width: 600,
                html: `
                    <div style="text-align:left; font-size:13px;">
                        <div class="alert alert-light border py-2 px-3 mb-3" style="font-size:10.5px;">
                            Naviera: <strong>${nombreNaviera || '-'}</strong> · Zarpe (ETD): <strong>${diasAlZarpe === null ? 'sin fecha' : (diasAlZarpe + ' día(s)')}</strong> · Umbral: <strong>${umbral} días</strong>${navSiempre ? ' <span class="text-danger fw-bold">(naviera cobra a todo evento)</span>' : ''}
                        </div>
                        <div class="row g-2 mb-3">
                            <div class="col-6">
                                <label style="${lbl}">Tipo</label>
                                <select id="swal-roleo-tipo" class="swal2-select" style="margin:0; width:100%; font-weight:700;">
                                    <option value="ROLEO">Roleo</option>
                                    <option value="CANCELACION_NOSHOW">Cancelación / No Show</option>
                                </select>
                            </div>
                            <div class="col-6">
                                <label style="${lbl}">Condición de tarifa</label>
                                <select id="swal-roleo-tarifa" class="swal2-select" style="margin:0; width:100%; font-weight:700;">
                                    <option value="">-- Seleccionar --</option>
                                    <option value="SPOT" ${tipoTarifaActual === 'SPOT' ? 'selected' : ''}>Spot</option>
                                    <option value="CONTRATO" ${tipoTarifaActual === 'CONTRATO' ? 'selected' : ''}>Contrato</option>
                                </select>
                            </div>
                        </div>
                        <div id="swal-roleo-estado" class="px-3 py-2 mb-3 rounded" style="font-size:11px; font-weight:700;"></div>
                        <div id="swal-roleo-spot" class="row g-2 mb-3">
                            <div class="col-6">
                                <label style="${lbl}">Penalidad</label>
                                <select id="swal-roleo-penalidad" class="swal2-select" style="margin:0; width:100%; font-weight:700;">
                                    <option value="AUTOMATICO" selected>Automático (regla de ${umbral} días)</option>
                                    <option value="SI">Forzar: CON penalidad</option>
                                    <option value="NO">Forzar: SIN penalidad</option>
                                    <option value="POR_DETERMINAR">Por determinar</option>
                                </select>
                            </div>
                            <div class="col-6">
                                <label style="${lbl}">Monto por contenedor (USD)</label>
                                <input type="number" id="swal-roleo-monto" class="swal2-input" style="margin:0; width:100%;" min="0" step="0.01" value="${montoNavieraUsd || ''}" placeholder="Ej: 150" />
                            </div>
                        </div>
                        <div class="row g-2 mb-3">
                            <div class="col-6">
                                <label style="${lbl}">N° Contenedores Afectados</label>
                                <input type="number" id="swal-roleo-cant" class="swal2-input" style="margin:0; width:100%;" value="${totalContenedores}" min="1" />
                            </div>
                        </div>
                        <label style="${lbl}">Comentario (opcional)</label>
                        <textarea id="swal-roleo-comentario" class="swal2-textarea" style="margin:0 0 14px; width:100%; height:60px;"></textarea>
                        <div id="swal-roleo-itinerario-wrap">
                            <div class="form-check form-switch mb-2">
                                <input class="form-check-input" type="checkbox" id="swal-roleo-pendiente" checked>
                                <label class="form-check-label" for="swal-roleo-pendiente" style="font-size:11px; font-weight:700;">Aún no se conoce la nueva nave / zarpe</label>
                            </div>
                            <div id="swal-roleo-itinerario" style="display:none;">
                                <div class="row g-2">
                                    <div class="col-6"><label style="${lblSm}">Nueva Nave</label><input type="text" id="swal-roleo-nave" class="swal2-input" style="margin:0; width:100%;" /></div>
                                    <div class="col-6"><label style="${lblSm}">Nuevo ETD (zarpe)</label><input type="datetime-local" id="swal-roleo-etd" class="swal2-input" style="margin:0; width:100%;" /></div>
                                    <div class="col-6"><label style="${lblSm}">Nuevo ETA</label><input type="datetime-local" id="swal-roleo-eta" class="swal2-input" style="margin:0; width:100%;" /></div>
                                    <div class="col-6"><label style="${lblSm}">Nuevo Stacking</label><input type="datetime-local" id="swal-roleo-stacking" class="swal2-input" style="margin:0; width:100%;" /></div>
                                    <div class="col-6"><label style="${lblSm}">Nuevo Cierre Documental</label><input type="datetime-local" id="swal-roleo-cierre" class="swal2-input" style="margin:0; width:100%;" /></div>
                                </div>
                            </div>
                        </div>
                    </div>`,
                showCancelButton: true, confirmButtonText: 'Registrar', confirmButtonColor: '#0f172a', cancelButtonText: 'Cancelar',
                didOpen: () => {
                    const $ = (id) => document.getElementById(id);
                    const refrescar = () => {
                        const tarifa = $('swal-roleo-tarifa').value;
                        const tipo = $('swal-roleo-tipo').value;
                        const dentroUmbral = diasAlZarpe !== null && diasAlZarpe <= umbral;
                        const conCosto = tarifa === 'SPOT' || (tarifa === 'CONTRATO' && navSiempre);
                        $('swal-roleo-spot').style.display = conCosto ? '' : 'none';
                        $('swal-roleo-itinerario-wrap').style.display = tipo === 'ROLEO' ? '' : 'none';

                        const estado = $('swal-roleo-estado');
                        let txt, bg, color;
                        if (!tarifa) { txt = 'Selecciona la condición de tarifa (Spot / Contrato).'; bg = '#fffbeb'; color = '#b45309'; }
                        else if (!conCosto) { txt = 'Contrato: sin costo. No se genera penalidad.'; bg = '#f0fdf4'; color = '#15803d'; }
                        else if (diasAlZarpe === null) { txt = 'El booking no tiene ETD: define la penalidad manualmente.'; bg = '#fffbeb'; color = '#b45309'; }
                        else if (dentroUmbral) { txt = `Spot a ${diasAlZarpe} día(s) del zarpe (≤ ${umbral}): se genera la penalidad automáticamente.`; bg = '#fef2f2'; color = '#b91c1c'; }
                        else { txt = `Spot a ${diasAlZarpe} día(s) del zarpe (> ${umbral}): sin penalidad.`; bg = '#f0fdf4'; color = '#15803d'; }
                        estado.textContent = txt; estado.style.background = bg; estado.style.color = color;
                    };
                    $('swal-roleo-tarifa').addEventListener('change', refrescar);
                    $('swal-roleo-tipo').addEventListener('change', refrescar);
                    $('swal-roleo-pendiente').addEventListener('change', function () {
                        $('swal-roleo-itinerario').style.display = this.checked ? 'none' : 'block';
                    });
                    refrescar();
                },
                preConfirm: () => {
                    const $ = (id) => document.getElementById(id);
                    const tarifa = $('swal-roleo-tarifa').value;
                    if (!tarifa) { Swal.showValidationMessage('Selecciona la condición de tarifa (Spot / Contrato).'); return false; }
                    const monto = $('swal-roleo-monto').value;
                    if (monto && (isNaN(parseFloat(monto)) || parseFloat(monto) < 0)) { Swal.showValidationMessage('El monto debe ser un número positivo.'); return false; }
                    const tipo = $('swal-roleo-tipo').value;
                    const pendiente = tipo !== 'ROLEO' || $('swal-roleo-pendiente').checked;
                    return {
                        tipo, tarifa, monto,
                        penalidad: $('swal-roleo-penalidad').value,
                        cantidad: $('swal-roleo-cant').value || 1,
                        comentario: $('swal-roleo-comentario').value,
                        pendiente,
                        nave: $('swal-roleo-nave').value,
                        etd: $('swal-roleo-etd').value,
                        eta: $('swal-roleo-eta').value,
                        stacking: $('swal-roleo-stacking').value,
                        cierre: $('swal-roleo-cierre').value
                    };
                }
            }).then((result) => {
                if (!result.isConfirmed) return;
                const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                const formData = new FormData();
                formData.append('idOperacion', idOp);
                formData.append('tipo', result.value.tipo);
                formData.append('tipoTarifa', result.value.tarifa);
                formData.append('penalidadOverride', result.value.penalidad);
                formData.append('montoPenalidadUsd', result.value.monto || '');
                formData.append('cantidadContenedoresAfectados', result.value.cantidad);
                formData.append('comentario', result.value.comentario);
                formData.append('datosNuevosPendientes', result.value.pendiente);
                if (!result.value.pendiente) {
                    formData.append('nuevaNave', result.value.nave);
                    formData.append('nuevoEtd', result.value.etd);
                    formData.append('nuevoEta', result.value.eta);
                    formData.append('nuevoStacking', result.value.stacking);
                    formData.append('nuevoCierreDocumental', result.value.cierre);
                }
                fetch('?handler=RegistrarRoleoCancelacion', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                    .then(res => res.json()).then(data => {
                        if (data.success) { Swal.fire({ icon: 'success', title: 'Registrado', timer: 2000, showConfirmButton: false }); setTimeout(() => window.location.reload(), 1000); }
                        else Swal.fire('Atención', data.message || 'No se pudo registrar.', 'warning');
                    }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
            });
        };

        window.confirmarItinerarioRoleoDetalle = function (idRegistro) {
            Swal.fire({
                title: 'Confirmar Nuevo Itinerario',
                html: `<div class="row g-2" style="text-align:left;">
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nueva Nave</label><input type="text" id="swal-conf-nave" class="swal2-input" style="margin:0; width:100%;" /></div>
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo ETD</label><input type="datetime-local" id="swal-conf-etd" class="swal2-input" style="margin:0; width:100%;" /></div>
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo ETA</label><input type="datetime-local" id="swal-conf-eta" class="swal2-input" style="margin:0; width:100%;" /></div>
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo Stacking</label><input type="datetime-local" id="swal-conf-stacking" class="swal2-input" style="margin:0; width:100%;" /></div>
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo Cierre Documental</label><input type="datetime-local" id="swal-conf-cierre" class="swal2-input" style="margin:0; width:100%;" /></div>
                </div>`,
                showCancelButton: true, confirmButtonText: 'Confirmar', confirmButtonColor: '#0f172a',
                preConfirm: () => ({
                    nave: document.getElementById('swal-conf-nave').value, etd: document.getElementById('swal-conf-etd').value,
                    eta: document.getElementById('swal-conf-eta').value, stacking: document.getElementById('swal-conf-stacking').value,
                    cierre: document.getElementById('swal-conf-cierre').value
                })
            }).then((result) => {
                if (!result.isConfirmed) return;
                const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                const formData = new FormData();
                formData.append('idRegistro', idRegistro);
                formData.append('nuevaNave', result.value.nave);
                formData.append('nuevoEtd', result.value.etd);
                formData.append('nuevoEta', result.value.eta);
                formData.append('nuevoStacking', result.value.stacking);
                formData.append('nuevoCierreDocumental', result.value.cierre);
                fetch('?handler=ConfirmarItinerarioRoleo', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                    .then(res => res.json()).then(data => {
                        if (data.success) { Swal.fire({ icon: 'success', title: 'Itinerario confirmado', timer: 1500, showConfirmButton: false }); setTimeout(() => window.location.reload(), 800); }
                        else Swal.fire('Error', data.message || 'No se pudo confirmar.', 'error');
                    }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
            });
        };

        window.actualizarPenalidadRoleoDetalle = function (idRegistro, montoSugeridoUsd) {
            Swal.fire({
                title: 'Confirmar Penalidad',
                html: `<div style="text-align:left;">
                    <label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:6px;">¿Genera penalidad?</label>
                    <select id="swal-pen-sel" class="swal2-select" style="margin:0 0 12px; width:100%; font-weight:700;">
                        <option value="">-- Seleccionar --</option>
                        <option value="SI">Sí, genera penalidad</option>
                        <option value="NO">No genera penalidad</option>
                    </select>
                    <div id="swal-pen-monto-wrap" style="display:none;">
                        <label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:6px;">Monto por contenedor (USD)</label>
                        <input type="number" id="swal-pen-monto" class="swal2-input" style="margin:0; width:100%;" min="0" step="0.01" value="${montoSugeridoUsd || ''}" />
                    </div>
                </div>`,
                showCancelButton: true, confirmButtonText: 'Actualizar', confirmButtonColor: '#0f172a',
                didOpen: () => {
                    document.getElementById('swal-pen-sel').addEventListener('change', function () {
                        document.getElementById('swal-pen-monto-wrap').style.display = this.value === 'SI' ? 'block' : 'none';
                    });
                },
                preConfirm: () => {
                    const val = document.getElementById('swal-pen-sel').value;
                    if (!val) { Swal.showValidationMessage('Selecciona una opción.'); return false; }
                    return { penalidad: val, monto: document.getElementById('swal-pen-monto').value };
                }
            }).then((result) => {
                if (!result.isConfirmed || !result.value) return;
                const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                const formData = new FormData();
                formData.append('idRegistro', idRegistro);
                formData.append('penalidad', result.value.penalidad);
                formData.append('montoPenalidadUsd', result.value.monto || '');
                fetch('?handler=ActualizarPenalidadRoleo', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                    .then(res => res.json()).then(data => {
                        if (data.success) { Swal.fire({ icon: 'success', title: 'Actualizado', timer: 1500, showConfirmButton: false }); setTimeout(() => window.location.reload(), 800); }
                        else Swal.fire('Atención', data.message || 'No se pudo actualizar.', 'warning');
                    }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
            });
        };
                 
                window.subirRespaldoAlmacenDetalle = function (input, idOp) {
            if (!input.files || input.files.length === 0) return;
            let formData = new FormData();
            formData.append('IdOperacion', idOp);
            formData.append('ArchivoTransferenciaAlmacen', input.files[0]);
            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;

            fetch('?handler=SubirRespaldoAlmacen', { method: 'POST', body: formData, headers: { 'RequestVerificationToken': token } })
                .then(res => res.json()).then(data => {
                    if (data.success) {
                        Swal.fire({ icon: 'success', title: '¡Guardado!', text: 'Comprobante subido.', timer: 1500, showConfirmButton: false });
                        let linkDiv = document.getElementById('link-respaldo-almacen-det');
                        if (linkDiv) { linkDiv.style.display = 'block'; let a = linkDiv.querySelector('a'); if (a) a.href = data.ruta; }
                    } else {
                        Swal.fire('Error', data.message || 'No se pudo subir el archivo.', 'error'); input.value = '';
                    }
                }).catch(() => { Swal.fire('Error', 'Problema de conexión.', 'error'); input.value = ''; });
        };
                window.toggleCourierBloqueDetalle = function (checked) {
            const div = document.getElementById('courier-fields-det');
            if (div) div.style.display = checked ? 'block' : 'none';
        };

        window.setTipoCourierDetalle = function (tipo) {
            const hidden = document.getElementById('hidden-tipo-courier-det');
            if (hidden) hidden.value = tipo;
            document.getElementById('btn-courier-nac-det')?.classList.toggle('active', tipo === 'Nacional');
            document.getElementById('btn-courier-int-det')?.classList.toggle('active', tipo === 'Internacional');
            const zonaDiv = document.getElementById('zona-courier-det');
            if (zonaDiv) zonaDiv.style.display = tipo === 'Internacional' ? 'block' : 'none';
        };
                window.actualizarGrupoExtracostoDetalle = function (select) {
            const opcionSeleccionada = select.options[select.selectedIndex];
            const grupo = opcionSeleccionada ? opcionSeleccionada.getAttribute('data-grupo') : '';
            document.getElementById('extracostoGrupoOperativoFinalDet').value = grupo || '';
        };

        window.filtrarTarifasMaritimoPorNavieraDetalle = function (idNaviera) {
            const optgroup = document.getElementById('optgroupCostosMaritimosDet');
            if (!optgroup) return;
            optgroup.querySelectorAll('option').forEach(opt => {
                const navOpt = opt.getAttribute('data-naviera');
                if (!navOpt || navOpt === '') { opt.style.display = ''; return; }
                opt.style.display = (idNaviera && navOpt === idNaviera.toString()) ? '' : 'none';
            });
        };

        window.abrirModalExtracostosDetalle = function (idOp, booking, idNaviera) {
            document.getElementById('extracostoOpIdDet').value = idOp;
            document.getElementById('extracostoBookingTitleDet').innerText = booking;
            document.getElementById('extracostoTipoSelectDet').value = '';
            document.getElementById('extracostoJustificacionTxtDet').value = '';
            document.getElementById('extracostoGrupoOperativoFinalDet').value = '';
            window.filtrarTarifasMaritimoPorNavieraDetalle(idNaviera);
            new bootstrap.Modal(document.getElementById('modalAddCostoGlobalDet')).show();
        };

        window.guardarExtracostoAjaxDetalle = function (btn) {
            const form = document.getElementById('form-extracosto-det');
            const justificacion = document.getElementById('extracostoJustificacionTxtDet').value;
            document.getElementById('extracostoMotivoFinalDet').value = justificacion;

            const original = btn.innerHTML;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Enviando...'; btn.disabled = true;

            fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(res => res.json()).then(data => {
                    if (data.success) {
                        window.location.reload();
                    } else {
                        btn.innerHTML = original; btn.disabled = false;
                        Swal.fire('Error', data.message || 'No se pudo reportar el extracosto.', 'error');
                    }
                }).catch(() => { btn.innerHTML = original; btn.disabled = false; Swal.fire('Error', 'Fallo de conexión.', 'error'); });
        };
                window.abrirGeneradorCorreosDet = function(tipo) {
            const ctx = document;
            let formTerrestre = ctx.querySelector('#ter-form-main');
            let formGeneral = ctx.querySelector('#mar-form-' + detalleOpData().id);

            function obtenerValor(nombre, claseVista) {
                let val = "";
                if (nombre && formTerrestre) {
                    let sel = formTerrestre.querySelector(`select[name="${nombre}"]`);
                    if (sel && sel.value && sel.value.trim() !== "") val = sel.value;
                    if (!val) { let inp = formTerrestre.querySelector(`input[name="${nombre}"]`); if (inp && inp.value && inp.value.trim() !== "") val = inp.value; }
                }
                if (!val && claseVista && formTerrestre) { let vista = formTerrestre.querySelector(`.${claseVista}`); if (vista && vista.value && vista.value.trim() !== "-" && vista.value.trim() !== "") val = vista.value; }
                return val ? val.trim() : null;
            }

            let empresa = obtenerValor("EmpresaTransporte") || "[EMPRESA]";
            let correoProv = obtenerValor("CorreoTransporte") || "";
            let conductor = obtenerValor("NombreConductor") || "[CONDUCTOR]";
            let rutCond = obtenerValor("RutConductor") || "[RUT]";
            let telefono = obtenerValor("TelefonoConductor") || "[TELEFONO]";
            let patente = obtenerValor("Patente") || "[PATENTE]";
            let planta = obtenerValor("PlantaCarga", "view-only-planta") || "[PLANTA]";
            let deposito = obtenerValor("DepositoRetiro", "view-only-depositoretiro") || "[DEPÓSITO]";
            let terminal = obtenerValor(null, "view-only-terminalterrestre") || "[TERMINAL]";
            let puerto = obtenerValor("PuertoEntrega", "view-only-puertoentrega") || "[PUERTO]";

            let inputFecha = formTerrestre ? formTerrestre.querySelector('input[name="FechaCarga"]') : null;
            let fecha = (inputFecha && inputFecha.value) ? inputFecha.value.replace("T", " ") : "[FECHA]";
            let booking = detalleOpData().booking;

            let correoCliente = detalleOpData().correoCliente;
            let to = "", cc = "", asunto = "", cuerpo = "";

            if (tipo === 'solicitud') {
                document.getElementById('tipoCorreoTxt').innerText = "Enviando Solicitud al Proveedor";
                to = correoProv !== "" ? correoProv : "proveedor@transporte.cl";
                cc = "usuario@atilson.cl; finanzas@atilson.cl";
                asunto = `SOLICITUD DE SERVICIO TRANSPORTE // BKG: ${booking}`;
                cuerpo = `Estimado equipo de ${empresa}, buenos días:\n\nFavor confirmar servicio de transporte para la siguiente carga:\n\nBooking: N° ${booking}\nDepósito Retiro Vacío: ${deposito}\nFecha de Presentación: ${fecha} Hrs.\nPlanta: ${planta}\nPuerto: ${puerto} - ${terminal}\n\nQuedamos atentos a su confirmación y datos del conductor.\n\nSaludos Cordiales,\nOperaciones Atilson Cargo`;
            } else if (tipo === 'preliminar') {
                document.getElementById('tipoCorreoTxt').innerText = "Enviando Asignación Preliminar al Cliente";
                to = correoCliente; cc = "operaciones@atilson.cl";
                asunto = `ASIGNACIÓN PRELIMINAR // BKG: ${booking}`;
                cuerpo = `Estimado Cliente, buenos días:\n\nLe enviamos los datos del conductor asignado (Booking: N° ${booking}):\n\nConductor: ${conductor}\nRUT: ${rutCond}\nTeléfono: ${telefono}\nPatente: ${patente}\nFecha de Presentación: ${fecha}\nDepósito Retiro Vacío: ${deposito}\nPuerto: ${puerto} - ${terminal}\n\n*El N° de contenedor se enviará en la asignación completa.*\n\nSaludos Cordiales,\nOperaciones Atilson Cargo`;
                        } else if (tipo === 'completa') {
                let contenedor = obtenerValor("NumeroContenedor", "") || detalleOpData().numeroContenedor || "[S/N CONTENEDOR]";
                let sello = obtenerValor("NumeroSello", "") || detalleOpData().numeroSello || "[S/N SELLO]";
                if (contenedor === "[S/N CONTENEDOR]" || sello === "[S/N SELLO]") { Swal.fire('Datos Faltantes', 'Debes guardar primero el N° de Contenedor y Sello antes de generar el envío definitivo.', 'error'); return; }
                document.getElementById('tipoCorreoTxt').innerText = "Enviando Asignación Completa al Cliente";
                to = correoCliente; cc = "operaciones@atilson.cl";
                asunto = `ASIGNACIÓN COMPLETA // BKG: ${booking} // CONTENEDOR: ${contenedor}`;
                cuerpo = `Estimado Cliente, buenos días:\n\nLe enviamos la asignación completa para su embarque:\n\nBooking: N° ${booking}\nContenedor: ${contenedor}\nSello: ${sello}\n\nDatos de Transporte:\nConductor: ${conductor}\nRUT: ${rutCond}\nTeléfono: ${telefono}\nPatente: ${patente}\nFecha de Presentación: ${fecha}\nDepósito Retiro Vacío: ${deposito}\nPuerto: ${puerto} - ${terminal}\n\nSaludos Cordiales,\nOperaciones Atilson Cargo`;
            } else if (tipo === 'guiaAgencia' || tipo === 'instructivoAgencia' || tipo === 'bookingAgencia' || tipo === 'capturaAgencia' || tipo === 'librevenAgencia') {
                const aplicaSerna = detalleOpData().aplicaSernapesca === 'true';
                let destStembex = aplicaSerna ? "a.barrera@stembex.cl; javier.traverso@stembex.cl" : "agencia@aduanas.cl";
                const polUpper = (detalleOpData().puertoOrigen || '').toUpperCase();
                if (polUpper.includes("SAN ANTONIO")) destStembex += "; stembexsai@stembex.cl; manuel.vera@stembex.cl; francisco.roman@stembex.cl; renata.aguilar@stembex.cl";
                else if (polUpper.includes("VALPARAISO") || polUpper.includes("VALPARAÍSO")) destStembex += "; Zeal@stembex.cl";

                to = destStembex; cc = "operaciones@atilson.cl";
                const naveStr = detalleOpData().nave || "[NAVE]";
                const navieraStr = detalleOpData().naviera || "[NAVIERA]";
                const tipoCont = detalleOpData().tipoContenedor || "[TIPO CONTENEDOR]";
                const clienteAsociado = detalleOpData().clienteRazonSocial || "[CLIENTE]";
                const puertoPol = detalleOpData().puertoOrigen || "[POL]";

                if (tipo === 'guiaAgencia') { document.getElementById('tipoCorreoTxt').innerText = 'Enviando Guía de Despacho a Agencia'; asunto = `GUÍA DE DESPACHO N°BOOKING ${booking} // ${naveStr} // ${tipoCont} // ${puertoPol} - [POD] // ${clienteAsociado} // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Buenas tardes, equipo Stembex Zeal\n\nSe adjunta Guía de Despacho.\nPor favor proceder con visado de unidad.\n\nGracias!`; }
                else if (tipo === 'instructivoAgencia') { document.getElementById('tipoCorreoTxt').innerText = 'Enviando Instructivo a Agencia'; const numInst = document.getElementById('docbase-val-instructivo')?.value || 'XXXXX'; asunto = `INSTRUCTIVO N°${numInst} // ${naveStr} // ${booking} // ${clienteAsociado} // ${puertoPol} - [POD] // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Buenas tardes,\n\nSe adjunta instructivo de embarque en mención del asunto para proceder con la coordinación del respectivo proceso documental del embarque.\n\nINST: ${numInst}`; }
                else if (tipo === 'bookingAgencia') { document.getElementById('tipoCorreoTxt').innerText = 'Enviando Booking a Agencia'; asunto = `CONFIRMACIÓN N°BOOKING ${booking} // ${naveStr} // ${tipoCont} // ${puertoPol} - [POD] // ${clienteAsociado} // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Estimado Cliente, junto con saludar se adjunta confirmación de embarque, notar detalle:\n\nBOOKING: ${booking}\nNAVIERA: ${navieraStr}\nNAVE: ${naveStr}\nTIPO CONTENEDOR: ${tipoCont}\nPOL: ${puertoPol}\nPOD: [POD]`; }
                else if (tipo === 'capturaAgencia') { document.getElementById('tipoCorreoTxt').innerText = 'Enviando Captura AGA a Agencia'; asunto = `CAPTURA AGA N°${booking} // ${naveStr} // ${tipoCont} // ${puertoPol} - [POD] // ${clienteAsociado} // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Buenas tardes,\n\nSe adjunta captura AGA en mención del asunto para proceder con la coordinación del respectivo proceso documental del embarque.\n\nCAPTURA: ${booking}`; }
                else if (tipo === 'librevenAgencia') { document.getElementById('tipoCorreoTxt').innerText = 'Enviando Libre Venta a Agencia'; asunto = `LIBRE VENTA N°BOOKING ${booking} // ${naveStr} // ${tipoCont} // ${puertoPol} - [POD] // ${clienteAsociado} // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Buenas tardes, equipo Stembex Zeal\n\nSe adjunta Certificado de Libre Venta.\nPor favor proceder con su revisión e ingreso.\n\nGracias!`; }
            }

                    document.getElementById('emailToPreview').value = to;
        document.getElementById('emailCcPreview').value = cc;
        document.getElementById('emailAsuntoPreview').value = asunto;
        document.getElementById('emailCuerpoPreview').value = cuerpo;

                        let btnEnviar = document.getElementById('btnEnviarCorreoGenerado');
            btnEnviar.setAttribute('data-id', detalleOpData().id);
            btnEnviar.setAttribute('data-tipo', tipo);

            new bootstrap.Modal(document.getElementById('modalGeneradorCorreos')).show();
        };

        window.copiarCorreoPreviewDet = function() {
            const texto = document.getElementById('emailCuerpoPreview').value;
            navigator.clipboard.writeText(texto).then(() => { Swal.fire({ icon: 'success', title: 'Copiado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false }); });
        };

        document.getElementById('btnEnviarCorreoGenerado')?.addEventListener('click', function() {
            let idOp = this.getAttribute('data-id'); let tipo = this.getAttribute('data-tipo');
            let to = document.getElementById('emailToPreview').value;
            let cc = document.getElementById('emailCcPreview').value;
            let asunto = document.getElementById('emailAsuntoPreview').value;
            let cuerpo = document.getElementById('emailCuerpoPreview').value;
            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;

            let formData = new FormData();
            formData.append('id', idOp); formData.append('correoDestino', to); formData.append('copiaCc', cc);
            formData.append('asunto', asunto); formData.append('cuerpoHtml', cuerpo); formData.append('tipoCorreo', tipo);

            fetch('?handler=EnviarCorreoTransporte', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                .then(res => res.json()).then(data => {
                    if (data.success) {
                        Swal.fire({ icon: 'success', title: '¡Enviado!', text: 'El correo fue enviado exitosamente.', confirmButtonColor: '#10b981' })
                            .then(() => { let modalObj = bootstrap.Modal.getInstance(document.getElementById('modalGeneradorCorreos')); if (modalObj) modalObj.hide(); window.location.reload(); });
                    } else { Swal.fire('Error', data.message || 'No se pudo procesar el envío.', 'error'); }
                }).catch((err) => Swal.fire('Error de Sistema', err.message, 'error'));
        });
                

    

                window.onEvidenciaNavieraChangeDet = function (input) {
            const wrapper = document.getElementById('btn-envio-naviera-wrapper-det');
            if (!wrapper) return;
            wrapper.classList.toggle('d-none', !(input.files && input.files.length > 0));
        };

        window.registrarArchivoBlMatrizDet = function (input) {
            const statusDiv = document.getElementById('bl-file-status-det');
            const btnEnvio = document.getElementById('btn-envio-borrador-det');
            if (input.files && input.files.length > 0) {
                if (statusDiv) { statusDiv.textContent = input.files[0].name; statusDiv.style.color = '#16a34a'; statusDiv.style.fontWeight = 'bold'; }
                if (btnEnvio) btnEnvio.classList.remove('d-none');
            } else {
                if (statusDiv) { statusDiv.textContent = 'Sin archivo adjunto'; statusDiv.style.color = '#94a3b8'; statusDiv.style.fontWeight = 'normal'; }
                if (btnEnvio) btnEnvio.classList.add('d-none');
            }
        };

        window.enviarBorradorClienteDet = function () {
            const idOp = detalleOpData().id;
            const booking = detalleOpData().booking;
            const correoPredefinido = detalleOpData().correoCliente;

            Swal.fire({
                title: 'Enviar Borrador BL', text: `Se enviará el borrador del Booking ${booking} al cliente para su revisión y aprobación.`,
                input: 'email', inputValue: correoPredefinido, inputPlaceholder: 'correo@cliente.com',
                showCancelButton: true, confirmButtonText: '<i class="bi bi-send-fill"></i> Enviar a Cliente', cancelButtonText: 'Cancelar', confirmButtonColor: '#6d2d9e'
            }).then((result) => {
                if (result.isConfirmed && result.value) {
                    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                    const formData = new FormData();
                    formData.append('idOperacion', idOp); formData.append('correoDestino', result.value);
                    const fileInput = document.getElementById('file-bl-det');
                    if (fileInput && fileInput.files.length > 0) formData.append('ArchivoBL', fileInput.files[0]);

                    Swal.fire({ title: 'Enviando Borrador...', allowOutsideClick: false, didOpen: () => Swal.showLoading() });

                    fetch('?handler=EnviarBorradorBL', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                        .then(res => res.json()).then(data => {
                            if (data.success) { Swal.fire({ title: '¡Enviado!', text: 'El borrador fue enviado exitosamente al cliente.', icon: 'success' }).then(() => window.location.reload()); }
                            else { Swal.fire({ title: 'Error', text: data.message, icon: 'error' }); }
                        }).catch(() => Swal.fire({ title: 'Error', text: 'Fallo de conexión.', icon: 'error' }));
                }
            });
        };

                // ===== Emisión del B/L y Corrección de BL (oct-2026) =====
                // Los bloques "Tipo/Lugar de emisión" los maneja wwwroot/js/emision-bl.js.
                // Corrección de BL: puede haber varias (Primera, Segunda...), cada una con uno
                // o más motivos (checkboxes) y su propio costo. Se guardan con su propio botón
                // (handler RegistrarCorreccionBl), aparte de "Guardar Matriz y B/L".
                const MOTIVO_CAMBIO_EMISION = 'Cambio emisión';
                const MOTIVO_CAMBIO_LUGAR = 'Cambio lugar de emisión';
                const MOTIVO_OTROS = 'Otros';

                const motivosCorrBlMarcados = (idOp) => Array.from(document.querySelectorAll(`#corr-bl-motivos-${idOp} input[name="MotivosCorreccionBl"]:checked`)).map(c => c.value);

                window.toggleMotivosCorreccionBlDet = function (idOp, origen) {
                    // "Cambio emisión" y "Cambio lugar de emisión" se excluyen: el primero es
                    // pasar a otro tipo; el segundo, mismo Originales con otro lugar.
                    if (origen && origen.checked && (origen.value === MOTIVO_CAMBIO_EMISION || origen.value === MOTIVO_CAMBIO_LUGAR)) {
                        const otro = origen.value === MOTIVO_CAMBIO_EMISION ? MOTIVO_CAMBIO_LUGAR : MOTIVO_CAMBIO_EMISION;
                        document.querySelectorAll(`#corr-bl-motivos-${idOp} input[name="MotivosCorreccionBl"]`).forEach(c => { if (c.value === otro) c.checked = false; });
                    }
                    const motivos = motivosCorrBlMarcados(idOp);
                    const esEmision = motivos.includes(MOTIVO_CAMBIO_EMISION);
                    const esLugar = motivos.includes(MOTIVO_CAMBIO_LUGAR) && !esEmision;
                    const wrapEm = document.getElementById(`wrapCorrEmision-${idOp}`);
                    const wrapLu = document.getElementById(`wrapCorrLugar-${idOp}`);
                    if (wrapEm) { wrapEm.classList.toggle('d-none', !esEmision); window.emisionBlHabilitar(wrapEm.querySelector('[data-embl]'), esEmision); }
                    if (wrapLu) { wrapLu.classList.toggle('d-none', !esLugar); window.emisionBlHabilitar(wrapLu.querySelector('[data-embl]'), esLugar); }

                    document.querySelectorAll(`#corr-bl-motivos-${idOp} label.corr-bl-motivo`).forEach(l => {
                        const c = l.querySelector('input'); l.classList.toggle('is-on', !!(c && c.checked));
                    });

                    const lbl = document.getElementById(`lblObsCorrBl-${idOp}`);
                    const obs = document.getElementById(`obsCorrBl-${idOp}`);
                    const esOtros = motivos.includes(MOTIVO_OTROS);
                    if (lbl) lbl.innerHTML = esOtros ? 'Observaciones <span class="text-danger">*</span>' : 'Comentario (opcional)';
                    if (obs) { obs.placeholder = esOtros ? 'Describe el motivo de la corrección...' : 'Detalle de la corrección...'; if (!esOtros) obs.classList.remove('is-invalid'); }
                    const box = document.getElementById(`corr-bl-motivos-${idOp}`);
                    if (box && motivos.length) box.classList.remove('is-invalid');
                };

                window.abrirNuevaCorreccionBlDet = function (idOp) {
                    const panel = document.getElementById(`corr-bl-nueva-${idOp}`);
                    const btn = document.getElementById(`btn-nueva-corr-bl-${idOp}`);
                    if (!panel) return;
                    panel.classList.remove('d-none');
                    if (btn) btn.classList.add('d-none');
                    window.toggleMotivosCorreccionBlDet(idOp);
                    panel.scrollIntoView({ behavior: 'smooth', block: 'center' });
                };

                window.cancelarNuevaCorreccionBlDet = function (idOp) {
                    const panel = document.getElementById(`corr-bl-nueva-${idOp}`);
                    const btn = document.getElementById(`btn-nueva-corr-bl-${idOp}`);
                    if (!panel) return;
                    panel.querySelectorAll('input[name="MotivosCorreccionBl"]').forEach(c => { c.checked = false; });
                    const obs = document.getElementById(`obsCorrBl-${idOp}`); if (obs) { obs.value = ''; obs.classList.remove('is-invalid'); }
                    const evid = panel.querySelector('input[name="EvidenciaCorreccionBl"]'); if (evid) evid.value = '';
                    window.toggleMotivosCorreccionBlDet(idOp);
                    panel.classList.add('d-none');
                    if (btn) btn.classList.remove('d-none');
                };

                window.guardarCorreccionBlDet = function (btn, idOp) {
                    const form = document.getElementById(`form-correccion-bl-${idOp}`);
                    const card = document.getElementById(`correccion-bl-form-${idOp}`);
                    if (!form) return;
                    const motivos = motivosCorrBlMarcados(idOp);
                    const aviso = (texto) => Swal.fire({ icon: 'warning', title: 'Corrección de BL', text: texto, confirmButtonColor: '#0f172a' });
                    if (!motivos.length) {
                        const box = document.getElementById(`corr-bl-motivos-${idOp}`); if (box) box.classList.add('is-invalid');
                        aviso('Marca al menos un motivo de la corrección.');
                        return;
                    }
                    const esEmision = motivos.includes(MOTIVO_CAMBIO_EMISION);
                    const esLugar = motivos.includes(MOTIVO_CAMBIO_LUGAR);
                    const bloqueEm = document.getElementById(`embl-corr-${idOp}`);
                    let err = null;
                    if (esEmision && esLugar) {
                        err = 'Marca solo uno entre "Cambio emisión" y "Cambio lugar de emisión".';
                    } else if (esEmision) {
                        err = window.emisionBlValidar(bloqueEm);
                    } else if (esLugar) {
                        err = window.emisionBlValidar(document.getElementById(`embl-corr-lugar-${idOp}`));
                    }
                    const obs = document.getElementById(`obsCorrBl-${idOp}`);
                    if (!err && motivos.includes(MOTIVO_OTROS) && obs && !obs.value.trim()) { obs.classList.add('is-invalid'); err = 'Con el motivo "Otros" debes escribir las observaciones.'; }
                    if (err) { aviso(err); return; }

                    const monto = document.getElementById(`montoCorrBl-${idOp}`);
                    const moneda = document.getElementById(`monedaCorrBl-${idOp}`);
                    const esc = (v) => String(v || '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
                    const valorCosto = monto ? parseFloat(monto.value) : NaN;
                    const monedaCosto = moneda ? moneda.value : 'USD';
                    const textoCosto = !isNaN(valorCosto) && valorCosto > 0
                        ? (monedaCosto === 'CLP'
                            ? `$ ${valorCosto.toLocaleString('es-CL', { maximumFractionDigits: 0 })} CLP + IVA`
                            : `${monedaCosto} ${valorCosto.toLocaleString('es-CL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} + IVA`)
                        : (monto && monto.value.trim() === '0' ? 'Sin costo' : 'Por valorizar en Finanzas');
                    const titulo = (card && card.getAttribute('data-siguiente')) || 'Corrección de BL';
                    Swal.fire({
                        icon: 'question',
                        title: `Registrar ${titulo.toLowerCase()}`,
                        html: `<div style="text-align:left; font-size:13px;">
                                <div style="margin-bottom:6px;"><strong>Motivos:</strong> ${motivos.map(esc).join(', ')}</div>
                                <div><strong>Costo:</strong> ${esc(textoCosto)}</div>
                                <div style="margin-top:8px; color:#64748b; font-size:11px;">Quedará registrada con fecha, hora y usuario, y se agregará como línea en Control Financiero (Marítimo).</div>
                               </div>`,
                        showCancelButton: true, confirmButtonText: 'Guardar corrección', cancelButtonText: 'Volver', confirmButtonColor: '#0f172a'
                    }).then(r => {
                        if (!r.isConfirmed) return;
                        try { sessionStorage.setItem('detReabrirCorrBl', String(idOp)); } catch (e) { }
                        window.guardarFormularioDetalle(form, btn);
                    });
                };

                // Al volver de guardar una corrección: abrir "Gestión de Matriz y B/L" y
                // mostrar la lista de correcciones.
                const reabrirCorreccionBlDet = function () {
                    let idOp = null;
                    try { idOp = sessionStorage.getItem('detReabrirCorrBl'); sessionStorage.removeItem('detReabrirCorrBl'); } catch (e) { idOp = null; }
                    if (!idOp) return;
                    const col = document.getElementById(`matriz-bl-collapse-${idOp}`);
                    if (col && window.bootstrap && bootstrap.Collapse) bootstrap.Collapse.getOrCreateInstance(col, { toggle: false }).show();
                    else if (col) col.classList.add('show');
                    setTimeout(() => {
                        const card = document.getElementById(`correccion-bl-form-${idOp}`);
                        if (card) { card.scrollIntoView({ behavior: 'smooth', block: 'start' }); card.classList.add('corr-bl-flash'); setTimeout(() => card.classList.remove('corr-bl-flash'), 1800); }
                    }, 350);
                };
                if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', reabrirCorreccionBlDet);
                else reabrirCorreccionBlDet();

                window.guardarMatrizBlDet = function (btn, idOp) {
                    const form = btn.closest('form');
                    const errMatriz = window.emisionBlValidar(document.getElementById(`embl-matriz-${idOp}`));
                    if (errMatriz) {
                        Swal.fire({ icon: 'warning', title: 'Gestión de Matriz', text: errMatriz, confirmButtonColor: '#0f172a' });
                        return;
                    }
                    // La corrección de BL se guarda con su propio botón: avisar si quedó una a medias.
                    const panel = document.getElementById(`corr-bl-nueva-${idOp}`);
                    if (panel && !panel.classList.contains('d-none') && motivosCorrBlMarcados(idOp).length) {
                        Swal.fire({
                            icon: 'info', title: 'Corrección de BL sin guardar',
                            text: 'La corrección se guarda con su botón "Guardar corrección". ¿Guardar igual la Matriz y el B/L ahora?',
                            showCancelButton: true, confirmButtonText: 'Guardar Matriz y B/L', cancelButtonText: 'Volver', confirmButtonColor: '#0f172a'
                        }).then(r => { if (r.isConfirmed) window.guardarFormularioDetalle(form, btn); });
                        return;
                    }
                    window.guardarFormularioDetalle(form, btn);
                };

                // Depósito de retiro vacío en Documental (servicio solo documental, oct-2026).
                window.guardarDepositoRetiroDocDet = function (btn) {
                    const form = btn.closest('form');
                    const sel = form ? form.querySelector('select[name="DepositoRetiro"]') : null;
                    if (!sel || !sel.value) {
                        Swal.fire({ icon: 'warning', title: 'Depósito retiro vacío', text: 'Selecciona el depósito.', confirmButtonColor: '#0f172a' });
                        return;
                    }
                    window.guardarFormularioDetalle(form, btn);
                };

                window.guardarEmisionBlDocDet = function (btn) {
                    const form = btn.closest('form');
                    const err = window.emisionBlValidar(form.querySelector('[data-embl]'));
                    if (err) {
                        Swal.fire({ icon: 'warning', title: 'Emisión del B/L', text: err, confirmButtonColor: '#0f172a' });
                        return;
                    }
                    window.guardarFormularioDetalle(form, btn);
                };

                // Datos del embarque (Booking, Naviera, Nave, Propietario de Contrato),
                // editable desde el encabezado para todos los servicios.
                window.editarEmbarqueDetalle = function (btn) {
                    const d = btn.dataset;
                    const tplNav = document.getElementById('tplNavierasDet');
                    const tplProp = document.getElementById('tplPropietariosDet');
                    const tplCom = document.getElementById('tplCommoditiesDet');
                    const lbl = 'font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:6px;';
                    const esc = (v) => (v || '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
                    Swal.fire({
                        title: 'Datos del embarque',
                        width: 560,
                        html: `<div style="text-align:left;">
                            <div class="row g-2">
                                <div class="col-6"><label style="${lbl}">Booking *</label><input id="swal-emb-booking" class="swal2-input" style="margin:0; width:100%; text-transform:uppercase;" value="${esc(d.booking)}" /></div>
                                <div class="col-6"><label style="${lbl}">N° instructivo de embarque</label><input id="swal-emb-instructivo" class="swal2-input" style="margin:0; width:100%; text-transform:uppercase;" maxlength="60" placeholder="Ej: INS-2026-0412" value="${esc(d.instructivo)}" /></div>
                                <div class="col-6"><label style="${lbl}">Naviera *</label><select id="swal-emb-naviera" class="swal2-select" style="margin:0; width:100%;"><option value="">-- Seleccionar --</option>${tplNav ? tplNav.innerHTML : ''}</select></div>
                                <div class="col-6"><label style="${lbl}">Nave / Viaje</label><input id="swal-emb-nave" class="swal2-input" style="margin:0; width:100%; text-transform:uppercase;" value="${esc(d.nave)}" /></div>
                                <div class="col-6"><label style="${lbl}">Propietario de Contrato *</label><select id="swal-emb-prop" class="swal2-select" style="margin:0; width:100%;"><option value="">-- Seleccionar --</option>${tplProp ? tplProp.innerHTML : ''}</select></div>
                                <div class="col-12" id="swal-emb-otro-wrap" style="display:none;"><label style="${lbl}">Cliente / recibidor (opcional)</label><input id="swal-emb-otro" class="swal2-input" style="margin:0; width:100%; text-transform:uppercase;" maxlength="100" value="${esc(d.propOtro)}" /></div>
                                <div class="col-6"><label style="${lbl}">Commodity</label><select id="swal-emb-commodity" class="swal2-select" style="margin:0; width:100%;"><option value="">-- Seleccionar --</option>${tplCom ? tplCom.innerHTML : ''}</select></div>
                                <div class="col-6"><label style="${lbl}">Condición de pago</label><select id="swal-emb-condpago" class="swal2-select" style="margin:0; width:100%;"><option value="">-- Seleccionar --</option><option value="1">Prepaid</option><option value="2">Full Prepaid</option><option value="3">Collect</option><option value="4">Full Collect</option><option value="5">Elsewhere</option></select></div>
                            </div></div>`,
                        showCancelButton: true, confirmButtonText: 'Guardar', cancelButtonText: 'Cancelar', confirmButtonColor: '#0f172a',
                        didOpen: () => {
                            const nav = document.getElementById('swal-emb-naviera');
                            const prop = document.getElementById('swal-emb-prop');
                            nav.value = d.naviera || '';
                            prop.value = d.prop || '';
                            document.getElementById('swal-emb-commodity').value = d.commodity || '';
                            document.getElementById('swal-emb-condpago').value = d.condPago || '';
                            const sync = () => { document.getElementById('swal-emb-otro-wrap').style.display = prop.value === 'OTROS' ? '' : 'none'; };
                            prop.addEventListener('change', sync); sync();
                        },
                        preConfirm: () => {
                            const v = (id) => document.getElementById(id).value.trim();
                            if (!v('swal-emb-booking')) { Swal.showValidationMessage('El Booking es obligatorio.'); return false; }
                            if (!v('swal-emb-naviera')) { Swal.showValidationMessage('Selecciona la Naviera.'); return false; }
                            if (!v('swal-emb-prop')) { Swal.showValidationMessage('Selecciona el Propietario de Contrato.'); return false; }
                            return { booking: v('swal-emb-booking'), instructivo: v('swal-emb-instructivo'), naviera: v('swal-emb-naviera'), nave: v('swal-emb-nave'), prop: v('swal-emb-prop'), otro: v('swal-emb-otro'), commodity: v('swal-emb-commodity'), condPago: v('swal-emb-condpago') };
                        }
                    }).then((r) => {
                        if (!r.isConfirmed) return;
                        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                        const fd = new FormData();
                        fd.append('idOperacion', d.id);
                        fd.append('numeroBooking', r.value.booking);
                        fd.append('numeroInstructivo', r.value.instructivo);
                        fd.append('idNaviera', r.value.naviera);
                        fd.append('nave', r.value.nave);
                        fd.append('propietarioContrato', r.value.prop);
                        fd.append('propietarioContratoOtro', r.value.otro);
                        fd.append('commodity', r.value.commodity);
                        fd.append('condicionPago', r.value.condPago);
                        fetch('?handler=EditarEmbarque', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: fd })
                            .then(res => res.json()).then(data => {
                                if (data.success) { Swal.fire({ icon: 'success', title: 'Guardado', timer: 1200, showConfirmButton: false }); setTimeout(() => window.location.reload(), 800); }
                                else Swal.fire('Atención', data.message || 'No se pudo guardar.', 'warning');
                            }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
                    });
                };

                window.abrirModalCorreccionBlNaviera = function (idOp, booking) {
                    const card = document.getElementById(`correccion-bl-form-${idOp}`);
                    const panel = document.getElementById(`corr-bl-nueva-${idOp}`);
                    const marcados = panel && !panel.classList.contains('d-none') ? motivosCorrBlMarcados(idOp).join(', ') : '';
                    const motivoTexto = marcados || (card && card.getAttribute('data-ultimo-motivo')) || 'Corrección BL';
                    abrirModalEnvioNavieraDet(`Corrección BL — ${motivoTexto} — BKG: ${booking}`);
                };

                        window.abrirModalEnvioNavieraDet = function (asuntoOverride) {
            const idOp = detalleOpData().id;
            const booking = detalleOpData().booking;
            const navieraUpper = (detalleOpData().naviera || '').toUpperCase();
            const navieras = [
                { nombre: 'Wan Hai', email: 'jcovarrubia@navepac.com,docexpo@navepac.com', detectar: ['WAN HAI', 'WAN-HAI'] },
                { nombre: 'Yang Ming', email: 'digcentral@agunsa.cl,luis.sotoc@agunsa.com', detectar: ['YANG MING', 'YM'] },
            ];
            let emailAutodetectado = ''; navieras.forEach(n => { if (n.detectar.some(k => navieraUpper.includes(k))) emailAutodetectado = n.email; });
            const opcionesHtml = navieras.map(n => `<option value="${n.email}" ${emailAutodetectado === n.email ? 'selected' : ''}>${n.nombre} — ${n.email}</option>`).join('') + `<option value="custom">Otro destinatario...</option>`;

            Swal.fire({
                title: 'Enviar evidencia a naviera',
                html: `<div style="text-align:left; font-size:13px;"><p style="color:#64748b; margin:0 0 14px;"><strong style="color:#0f172a;">BKG: ${booking}</strong> — se enviará el archivo de evidencia adjunto.</p><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Destinatario</label><select id="swal-naviera-select-det" class="swal2-input" style="margin:0 0 12px; width:100%; font-size:12px; font-weight:700;" onchange="onNavieraSelectChangeDet(this)">${opcionesHtml}</select><div id="swal-custom-email-wrapper-det" style="display:none;"><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Correo personalizado</label><input type="email" id="swal-custom-email-det" class="swal2-input" style="margin:0 0 12px; width:100%;" placeholder="correo@naviera.com" /></div><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Asunto</label><input type="text" id="swal-asunto-naviera-det" class="swal2-input" style="margin:0 0 12px; width:100%; font-weight:700;" value="${asuntoOverride || ('[ATILSON CARGO] Evidencia Envío Documentos — BKG: ' + booking)}" /><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Mensaje adicional (opcional)</label><textarea id="swal-msg-naviera-det" class="swal2-textarea" style="margin:0; width:100%; font-size:12px; height:80px;" placeholder="Ej: Adjuntamos evidencia de envío..."></textarea></div>`,
                showCancelButton: true, confirmButtonText: '<i class="bi bi-send-fill"></i> Enviar correo', cancelButtonText: 'Cancelar', confirmButtonColor: '#0f172a', width: 500,
                preConfirm: () => {
                    const select = document.getElementById('swal-naviera-select-det'); let email = select.value;
                    if (email === 'custom') { email = document.getElementById('swal-custom-email-det')?.value?.trim(); if (!email || !email.includes('@')) { Swal.showValidationMessage('Ingresa un correo válido.'); return false; } }
                    if (!email) { Swal.showValidationMessage('Selecciona o ingresa un destinatario.'); return false; }
                    return { email, asunto: document.getElementById('swal-asunto-naviera-det').value, mensaje: document.getElementById('swal-msg-naviera-det').value };
                }
            }).then((result) => { if (result.isConfirmed && result.value) { enviarCorreoEvidenciaNavieraDet(idOp, booking, result.value.email, result.value.asunto, result.value.mensaje); } });
        };

        window.onNavieraSelectChangeDet = function (select) {
            const wrapper = document.getElementById('swal-custom-email-wrapper-det');
            if (wrapper) wrapper.style.display = select.value === 'custom' ? 'block' : 'none';
        };

        window.enviarCorreoEvidenciaNavieraDet = function (idOp, booking, correoDestino, asunto, mensajeExtra) {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const cuerpoHtml = `<div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;"><div style="background:linear-gradient(135deg,#0f172a 0%,#6d2d9e 100%);padding:25px;text-align:center;border-radius:8px 8px 0 0;"><h2 style="color:#fff;margin:0;">Envío de Documentos</h2><p style="color:#cbd5e1;margin:6px 0 0;">Booking: <strong style="color:#fff;">${booking}</strong></p></div><div style="background:#fff;padding:28px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;"><p style="font-size:14px;color:#475569;line-height:1.6;">Estimado equipo,<br><br>Adjuntamos la evidencia de envío de documentos correspondiente al Booking <strong>${booking}</strong>.</p>${mensajeExtra ? `<p style="font-size:13px;color:#475569;border-left:3px solid #6d2d9e;padding-left:12px;margin-top:16px;">${escapeHtmlAtilson(mensajeExtra)}</p>` : ''}<hr style="border:0;border-top:1px solid #e2e8f0;margin:24px 0;"><p style="font-size:11px;color:#94a3b8;text-align:center;margin:0;">Atilson Cargo SpA · Operaciones Logísticas</p></div></div>`;

            const formData = new FormData();
            formData.append('id', idOp); formData.append('correoDestino', correoDestino); formData.append('copiaCc', 'operaciones@atilson.cl');
            formData.append('asunto', asunto); formData.append('cuerpoHtml', cuerpoHtml); formData.append('tipoCorreo', 'evidenciaNaviera');

            Swal.fire({ title: 'Enviando...', allowOutsideClick: false, didOpen: () => Swal.showLoading() });

            fetch('?handler=EnviarCorreoTransporte', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                .then(r => r.json()).then(data => {
                    if (data.success) { Swal.fire({ icon: 'success', title: '¡Enviado!', text: `Correo enviado correctamente a ${correoDestino}`, confirmButtonColor: '#10b981' }).then(() => window.location.reload()); }
                    else { Swal.fire({ icon: 'error', title: 'Error', text: data.message }); }
                }).catch(err => Swal.fire({ icon: 'error', title: 'Error de conexión', text: err.message }));
        };
        
        window.openProTab = function(opId, tabName) {
    if (tabName !== 'aduana') {
        const ctx = modalCtx() || document;
        const paneAduana = ctx.querySelector(`#pane-aduana-${opId}`);
        const selAgencia = paneAduana ? paneAduana.querySelector('select[name="docUpdates.IdAgenciaAduana"]') : null;
        const tieneAgencia = selAgencia && selAgencia.value && selAgencia.value.trim() !== "";
        if (!tieneAgencia) {
            Swal.fire({
                icon: 'warning',
                title: 'Falta Agencia de Aduanas',
                text: 'Debe seleccionar primero la Agencia de Aduanas para poder acceder a los certificados y trámites.',
                confirmButtonColor: '#0f172a',
                target: document.body
            });
            return;
        }
    }

    document.querySelectorAll('.pro-pane-' + opId).forEach(el => el.style.display = 'none');
    document.getElementById('pane-' + tabName + '-' + opId).style.display = 'block';
    document.querySelectorAll('.pro-nav-btn-' + opId).forEach(el => el.classList.remove('active'));

    // Fix: algunos botones usan el prefijo "sidebar-btn-" (fito, sani, cap)
    let btnActivo = document.getElementById('btn-' + tabName + '-' + opId) || document.getElementById('sidebar-btn-' + tabName + '-' + opId);
    if (btnActivo) btnActivo.classList.add('active');
};

window.validarBloqueoAgencia = function(idBase) {
    const ctx = modalCtx() || document;
    const paneAduana = ctx.querySelector(`#pane-aduana-${idBase}`);
    if (!paneAduana) return;

    const selAgencia = paneAduana.querySelector('select[name="docUpdates.IdAgenciaAduana"]');
    const tieneAgencia = selAgencia && selAgencia.value && selAgencia.value.trim() !== "";

    ['origen', 'fito', 'sani', 'cap', 'sub', 'base'].forEach(tab => {
        let btn = ctx.querySelector(`#btn-${tab}-${idBase}`) || ctx.querySelector(`#sidebar-btn-${tab}-${idBase}`);
        if (btn) btn.classList.toggle('disabled', !tieneAgencia);
    });

    let aviso = paneAduana.querySelector('.aviso-sin-agencia');
    if (!tieneAgencia) {
        if (!aviso) {
            aviso = document.createElement('div');
            aviso.className = 'aviso-sin-agencia alert alert-warning py-2 px-3 mb-3';
            aviso.style.cssText = 'font-size:11px; font-weight:700; border-left:4px solid #f59e0b; border-radius:6px;';
            aviso.innerHTML = '<i class="bi bi-exclamation-triangle-fill me-2"></i> Seleccione la Agencia de Aduanas para habilitar el resto de los trámites documentales.';
            const header = paneAduana.querySelector('.d-flex.justify-content-between.align-items-center.mb-4.pb-3');
            if (header) header.insertAdjacentElement('afterend', aviso);
        }
    } else if (aviso) {
        aviso.remove();
    }
};

    window.togglePanelPro = function(wrapperId, isChecked) {
        let wrapper = document.getElementById(wrapperId);
        if(wrapper) {
            wrapper.style.opacity = isChecked ? "1" : "0.3";
            wrapper.style.pointerEvents = isChecked ? "auto" : "none";
        }
    };

    const PANE_ID_MAP = {
        aduana: id => `pane-aduana-${id.replace(/^v3-/, '').replace(/^sub-/, '')}`,
        ori: id => `pane-origen-${id.replace(/^v3-/, '').replace(/^sub-/, '')}`,
        fit: id => `pane-fito-${id.replace(/^v3-/, '').replace(/^sub-/, '')}`,
        san: id => `pane-sani-${id.replace(/^v3-/, '').replace(/^sub-/, '')}`,
        cap: id => `pane-cap-${id.replace(/^v3-/, '').replace(/^sub-/, '')}`,
        coa: id => `row-coa-${id}`, dt:  id => `row-dt-${id}`, cod: id => `row-cod-${id}`, cla: id => `row-cla-${id}`, nep: id => `row-nep-${id}`
    };
    const SUFIJO_CAMPO = { ori:'Ori', fit:'Fit', san:'San', cap:'Cap', coa:'Coa', dt:'Dt', cod:'Cod', cla:'Cla', nep:'Nep' };

    window.toggleEditCert = function(certKey, idBase, btn) { toggleEditMode(PANE_ID_MAP[certKey](idBase), btn); };
    window.descartarCert = function(certKey, idBase) { cancelEditMode(PANE_ID_MAP[certKey](idBase)); };

    // ==========================================
    // 6. GUARDADO AJAX Y OPERACIONES
    // ==========================================
    window.syncFormToTemplate = function(form) {
        if (!window.activeModalTemplate) return;
        let targetContent = window.activeModalTemplate.content;
        form.querySelectorAll('input, select, textarea').forEach(inp => {
            if (inp.name && inp.type !== 'file' && inp.type !== 'hidden') {
                if (inp.type === 'checkbox' || inp.type === 'radio') {
                    let tempInp = targetContent.querySelector(`[name="${inp.name}"][value="${inp.value}"]`);
                    if (tempInp) { if (inp.checked) tempInp.setAttribute('checked', 'checked'); else tempInp.removeAttribute('checked'); }
                } else {
                    let tempInp = targetContent.querySelector(`[name="${inp.name}"]`);
                    if (tempInp) {
                        if (inp.tagName === 'SELECT') {
                            tempInp.querySelectorAll('option').forEach(opt => {
                                if (opt.value == inp.value) opt.setAttribute('selected', 'selected'); else opt.removeAttribute('selected');
                            });
                        } else {
                            tempInp.setAttribute('value', inp.value);
                            if (inp.tagName === 'TEXTAREA') tempInp.textContent = inp.value;
                        }
                    }
                }
            }
        });
    };

    window.guardarFormularioModal = function(form, btn, formContainerId) {
        if (!form) { Swal.fire('Error', 'No se encontró el formulario a guardar.', 'error'); return; }
        if (typeof sincronizarLogsAntesDeGuardar === 'function') { try { sincronizarLogsAntesDeGuardar(form); } catch (e) {} }

        const originalHtml = btn ? btn.innerHTML : null;
        if (btn) { btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Guardando...'; btn.disabled = true; }

        fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
        .then(res => res.json()).then(data => {
            if (btn) { btn.innerHTML = originalHtml; btn.disabled = false; }
            if (data.success) {
                // ESTA ES LA LÍNEA QUE FALTABA PARA RECARGAR
                window.location.reload(); 
            } else {
                Swal.fire('Error', data.message || 'No se pudo guardar la información.', 'error');
            }
        })
        .catch(() => { 
            if (btn) { btn.innerHTML = originalHtml; btn.disabled = false; } 
            window.location.reload(); // También recargar si hay un error de conexión para limpiar la vista
        });
    };

            window.guardarTransporteAjax = function(btn, idSuffix) {
        const form = document.querySelector(`#ter-form-${idSuffix}`)?.closest('form');
        if (!form) { Swal.fire('Error', 'No se encontró el formulario de transporte.', 'error'); return; }
        const idOp = form.querySelector('input[name="IdOperacion"]')?.value;
        const originalHtml = btn.innerHTML;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Guardando...'; btn.disabled = true;

        fetch('?handler=QuickEditTransporte', { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
        .then(res => res.json()).then(data => {
            btn.innerHTML = originalHtml; btn.disabled = false;
            if (data.success) {
                                let depoSel = form.querySelector('select[name="DepositoRetiro"]'); let viewDepo = form.querySelector('.view-only-depositoretiro'); if (depoSel && viewDepo) viewDepo.value = depoSel.value || '-';
                let empSel = form.querySelector('select[name="EmpresaTransporte"]'); let viewEmp = form.querySelector('.view-only-empresatransporte'); if (empSel && viewEmp) viewEmp.value = empSel.value || '-';
                let ptoSel = form.querySelector('select[name="PuertoEntrega"]'); let viewPto = form.querySelector('.view-only-puertoentrega'); if (ptoSel && viewPto) viewPto.value = ptoSel.value || '-';
                let termInp = form.querySelector('input[name="TerminalTerrestreStr"]'); let viewTerm = form.querySelector('.view-only-terminalterrestre'); if (termInp && viewTerm) viewTerm.value = termInp.value || '-';
                let planSel = form.querySelector('select[name="PlantaCarga"]'); let viewPlan = form.querySelector('.view-only-planta'); if (planSel && viewPlan) viewPlan.value = planSel.value || '-';
                let zonaInp = form.querySelector('input[name="ZonaCarga"]'); let viewZona = form.querySelector('.view-only-zonacarga'); if (zonaInp && viewZona) viewZona.value = zonaInp.value || '-';
                let linkInp = form.querySelector('input[name="LinkTracking"]'); let viewLink = form.querySelector('.view-only-link');
                if (linkInp && viewLink) { viewLink.value = linkInp.value || ''; let btnMaps = form.querySelector('.view-only-maps-btn'); if(btnMaps) { btnMaps.href = linkInp.value; btnMaps.style.display = linkInp.value ? 'inline-block' : 'none'; } }

                const dotTer = document.getElementById(`dot-ter-${idOp}`);
                if (dotTer) {
                    const empresaOk = form.querySelector('[name="EmpresaTransporte"]')?.value?.trim();
                    const conductorOk = form.querySelector('input[name="NombreConductor"]')?.value?.trim();
                    const patenteOk = form.querySelector('input[name="Patente"]')?.value?.trim();
                    const depositoOk = form.querySelector('.view-only-depositoretiro')?.value?.trim();
                    const fechaOk = form.querySelector('input[name="FechaCarga"]')?.value?.trim();
                    const completo = empresaOk && conductorOk && patenteOk && depositoOk && fechaOk;
                    dotTer.className = 'tab-status-dot ' + (completo ? 'status-green' : ((empresaOk || conductorOk || depositoOk || fechaOk) ? 'status-orange' : ''));
                }

                syncFormToTemplate(form); cancelEditMode(`ter-form-${idSuffix}`);
                if (typeof marcarBookingComoModificado === 'function') marcarBookingComoModificado(idOp);
                if (typeof actualizarVisibilidadSeguimiento === 'function') actualizarVisibilidadSeguimiento(idSuffix);
                Swal.fire({ icon: 'success', title: 'Transporte Guardado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });
            } else { Swal.fire('Error', data.message || 'No se pudo guardar la información.', 'error'); }
        })
        .catch((err) => { console.error('ERROR REAL GUARDAR TRANSPORTE:', err); btn.innerHTML = originalHtml; btn.disabled = false; Swal.fire('Error', 'Fallo de conexión.', 'error'); });
    };

    window.guardarTransporteYEnviar = function(e, idOp, tipoCorreo) {
        const btnOriginal = e.currentTarget; const htmlOriginal = btnOriginal.innerHTML;
        btnOriginal.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Guardando...'; btnOriginal.disabled = true;
        let form = document.querySelector(`#ter-form-${idOp}`)?.closest('form');
        
                if (form) {
            fetch('?handler=QuickEditTransporte', { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(res => res.json()).then(data => {
                btnOriginal.innerHTML = htmlOriginal; btnOriginal.disabled = false;
                if (data.success) {
                    syncFormToTemplate(form);
                    if (typeof actualizarVisibilidadSeguimiento === 'function') actualizarVisibilidadSeguimiento(idOp);
                    abrirGeneradorCorreos(idOp, tipoCorreo);
                } else { Swal.fire('Error', 'No se pudo guardar la información: ' + (data.message || ''), 'error'); }
            }).catch(err => { btnOriginal.innerHTML = htmlOriginal; btnOriginal.disabled = false; Swal.fire('Error', 'Fallo de conexión.', 'error'); });
        } else {
            btnOriginal.innerHTML = htmlOriginal; btnOriginal.disabled = false;
            abrirGeneradorCorreos(idOp, tipoCorreo);
        }
    };

    // ==========================================
    // "SEGUIR CAMIÓN": ruta planificada Depósito -> Planta(s) -> Puerto dibujada en un mapa
    // (Leaflet + OSRM). No es tracking GPS en vivo — no hay esa fuente de datos en el
    // sistema — es la ruta esperada según las ubicaciones ya configuradas, calculada en el
    // servidor por OnGetCalcularRutaTransporteAsync (Operaciones/Index.cshtml.cs).
    // ==========================================
    function obtenerValorCampoTransporte(form, nombre, claseVista) {
        let val = "";
        let sel = form.querySelector(`select[name="${nombre}"]`);
        if (sel && sel.value && sel.value.trim() !== "") val = sel.value;
        if (!val) {
            let inp = form.querySelector(`input[name="${nombre}"]`);
            if (inp && inp.value && inp.value.trim() !== "") val = inp.value;
        }
        if (!val && claseVista) {
            let vista = form.querySelector(`.${claseVista}`);
            if (vista && vista.value && vista.value.trim() !== "-" && vista.value.trim() !== "") val = vista.value;
        }
        return val ? val.trim() : "";
    }

    // Muestra/oculta el bloque "Seguir Camión" según si ya hay Depósito + Planta + Puerto
    // — se llama tras guardar el panel de Transporte, sin necesitar recargar la página.
    window.actualizarVisibilidadSeguimiento = function(idSuffix) {
        const formContainer = document.getElementById(`ter-form-${idSuffix}`);
        const form = formContainer?.closest('form');
        const wrap = document.getElementById(`seguimiento-wrap-${idSuffix}`);
        if (!form || !wrap) return;
        const deposito = obtenerValorCampoTransporte(form, 'DepositoRetiro', 'view-only-depositoretiro');
        const planta = obtenerValorCampoTransporte(form, 'PlantaCarga', 'view-only-planta');
        const puerto = obtenerValorCampoTransporte(form, 'PuertoEntrega', 'view-only-puertoentrega');
        const completo = !!(deposito && planta && puerto);
        wrap.classList.toggle('d-none', !completo);

        // Vista resumen (premium): el servidor calcula esto solo al cargar la página, así
        // que tras un guardado por AJAX (sin recargar) hay que recalcularlo acá y, si no se
        // está editando ahora mismo, alternar entre el resumen y el formulario. También se
        // refrescan los 3 nombres de la ruta (los únicos campos de los que depende esto),
        // para que no se vean los valores viejos hasta el próximo reload de la página.
        if (formContainer) {
            formContainer.setAttribute('data-resumen-completo', completo ? 'true' : 'false');
            const elDeposito = document.getElementById(`resumen-deposito-nombre-${idSuffix}`);
            if (elDeposito) elDeposito.textContent = deposito || '-';
            const elPlanta = document.getElementById(`resumen-planta-nombre-${idSuffix}`);
            if (elPlanta) elPlanta.textContent = planta || '-';
            const elPuerto = document.getElementById(`resumen-puerto-nombre-${idSuffix}`);
            if (elPuerto) elPuerto.textContent = puerto || '-';

            if (!formContainer.classList.contains('edit-mode')) {
                const resumenPanelEl = document.getElementById(`ter-resumen-${idSuffix}`);
                const corePanelEl = document.getElementById(`ter-form-core-${idSuffix}`);
                if (resumenPanelEl) resumenPanelEl.classList.toggle('d-none', !completo);
                if (corePanelEl) corePanelEl.classList.toggle('d-none', completo);
            }
        }
    };

    // Una instancia de mapa Leaflet por cada unidad técnica (idSuffix) — el desplegable
    // vive inline en la página (ya no es un modal compartido), así que puede haber varias
    // instancias abiertas a la vez si el booking tiene más de un contenedor.
    window._mapasSeguimientoTransporte = window._mapasSeguimientoTransporte || {};

    function cargarRutaSeguimientoTransporte(idSuffix) {
        const form = document.querySelector(`#ter-form-${idSuffix}`)?.closest('form');
        if (!form) return;
        const deposito = obtenerValorCampoTransporte(form, 'DepositoRetiro', 'view-only-depositoretiro');
        const planta = obtenerValorCampoTransporte(form, 'PlantaCarga', 'view-only-planta');
        const puerto = obtenerValorCampoTransporte(form, 'PuertoEntrega', 'view-only-puertoentrega');
        const terminal = obtenerValorCampoTransporte(form, 'TerminalTerrestreStr', 'view-only-terminalterrestre');
        let planta2 = "", planta3 = "";
        const chkInter = form.querySelector('input[name="AplicaInterplanta"]');
        if (chkInter && chkInter.checked) {
            planta2 = form.querySelector('select[name="PlantaCarga2"]')?.value || "";
            planta3 = form.querySelector('select[name="PlantaCarga3"]')?.value || "";
        }

        const resumen = document.getElementById(`seguimiento-resumen-${idSuffix}`);
        const mapaDiv = document.getElementById(`seguimiento-mapa-${idSuffix}`);
        if (resumen) resumen.textContent = 'Calculando ruta...';
        if (mapaDiv) mapaDiv.innerHTML = '<div class="d-flex align-items-center justify-content-center h-100"><span class="spinner-border text-dark"></span></div>';

        const params = new URLSearchParams({ deposito, planta, puerto });
        if (terminal && terminal !== '-') params.append('terminal', terminal);
        if (planta2) params.append('planta2', planta2);
        if (planta3) params.append('planta3', planta3);

        fetch(`/Operaciones/Index?handler=CalcularRutaTransporte&${params.toString()}`)
            .then(r => r.json())
            .then(data => {
                if (!data.success) {
                    if (mapaDiv) mapaDiv.innerHTML = `<div class="d-flex align-items-center justify-content-center h-100 text-muted text-center p-4" style="font-size:12px;"><div><i class="bi bi-exclamation-triangle text-warning fs-3 d-block mb-2"></i>${data.mensaje || 'No se pudo calcular la ruta.'}</div></div>`;
                    if (resumen) resumen.textContent = 'No se pudo trazar la ruta';
                    return;
                }
                if (resumen) resumen.textContent = data.trazadoPorCarretera
                    ? `${data.distanciaKm} km estimados · ${formatearDuracion(data.duracionMin)} de viaje aprox.`
                    : 'Paradas ubicadas · el trazado por carretera no está disponible ahora';
                if (mapaDiv) mapaDiv.innerHTML = '';
                if (window._mapasSeguimientoTransporte[idSuffix]) {
                    window._mapasSeguimientoTransporte[idSuffix].remove();
                    delete window._mapasSeguimientoTransporte[idSuffix];
                }

                const mapa = L.map(`seguimiento-mapa-${idSuffix}`);
                window._mapasSeguimientoTransporte[idSuffix] = mapa;
                L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                    attribution: '&copy; OpenStreetMap',
                    maxZoom: 18
                }).addTo(mapa);

                const linea = L.polyline(data.ruta, { color: '#443094', weight: 5, opacity: 0.85, dashArray: data.trazadoPorCarretera ? null : '6 8' }).addTo(mapa);

                // Una marca por parada: D = depósito, P1/P2/P3 = plantas, ⚓ = puerto.
                const estilos = { deposito: { color: '#0f766e', letra: 'D' }, planta: { color: '#443094', letra: 'P' }, puerto: { color: '#b91c1c', letra: '⚓' } };
                const lista = document.getElementById(`seguimiento-paradas-${idSuffix}`);
                if (lista) lista.innerHTML = '';
                let nPlanta = 0;
                const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
                data.puntos.forEach(function (p) {
                    const e = estilos[p.tipo] || estilos.planta;
                    const letra = p.tipo === 'planta' ? 'P' + (++nPlanta) : e.letra;
                    const icono = L.divIcon({ className: 'ter-ruta-pin', html: `<span style="background:${e.color}">${letra}</span>`, iconSize: [30, 30], iconAnchor: [15, 15] });
                    L.marker([parseFloat(p.lat), parseFloat(p.lon)], { icon: icono, title: p.etiqueta })
                        .addTo(mapa)
                        .bindTooltip(`<b>${esc(p.etiqueta)}</b><br>${esc(p.precision)}`);
                    if (lista) lista.insertAdjacentHTML('beforeend',
                        `<li class="${p.aproximada ? 'aprox' : ''}"><span class="pin" style="background:${e.color}">${letra}</span><span><b>${esc(p.etiqueta)}</b><small>${p.aproximada ? '<i class="bi bi-exclamation-triangle-fill"></i> ' : '<i class="bi bi-geo-alt-fill"></i> '}${esc(p.precision)}</small></span></li>`);
                });
                (data.avisos || []).forEach(function (a) {
                    if (lista) lista.insertAdjacentHTML('beforeend', `<li class="falta"><span class="pin" style="background:#9ca3af">?</span><span><b>Parada sin ubicar</b><small><i class="bi bi-exclamation-triangle-fill"></i> ${esc(a)}</small></span></li>`);
                });

                mapa.fitBounds(linea.getBounds(), { padding: [30, 30] });
                setTimeout(function () {
                    if (window._mapasSeguimientoTransporte[idSuffix]) window._mapasSeguimientoTransporte[idSuffix].invalidateSize();
                }, 350);
            })
            .catch(() => {
                if (mapaDiv) mapaDiv.innerHTML = '<div class="d-flex align-items-center justify-content-center h-100 text-danger" style="font-size:12px;">Fallo de conexión al calcular la ruta.</div>';
                if (resumen) resumen.textContent = 'Error de conexión';
            });
    }

    // El botón conserva data-bs-toggle/data-bs-target solo para que Bootstrap lo registre
    // como "trigger" de este collapse y mantenga aria-expanded (y por lo tanto el chevron)
    // sincronizado automáticamente. El show/hide en sí lo controla esta función a través
    // de la API de Bootstrap (bootstrap.Collapse), no del data-api — evitábamos antes una
    // carrera entre el listener delegado de Bootstrap y este onclick que a veces dejaba el
    // panel sin abrirse aunque la ruta sí se calculara. event.stopPropagation() evita que
    // el listener delegado de Bootstrap vuelva a togglear el mismo clic por su cuenta.
    function formatearDuracion(min) {
        min = Math.round(min || 0);
        if (min < 60) return `${min} min`;
        const h = Math.floor(min / 60), m = min % 60;
        return m ? `${h} h ${m} min` : `${h} h`;
    }

    window.toggleSeguimientoTransporte = function(ev, idSuffix) {
        if (ev) ev.stopPropagation();
        const collapseEl = document.getElementById(`seguimiento-collapse-${idSuffix}`);
        if (!collapseEl || typeof bootstrap === 'undefined') return;
        const bsCollapse = bootstrap.Collapse.getOrCreateInstance(collapseEl, { toggle: false });
        const estaAbierto = collapseEl.classList.contains('show');

        if (estaAbierto) { bsCollapse.hide(); return; }

        bsCollapse.show();
        cargarRutaSeguimientoTransporte(idSuffix);
        collapseEl.addEventListener('shown.bs.collapse', function onShown() {
            collapseEl.removeEventListener('shown.bs.collapse', onShown);
            if (window._mapasSeguimientoTransporte[idSuffix]) window._mapasSeguimientoTransporte[idSuffix].invalidateSize();
        }, { once: true });
    };

    window.guardarAlmacenYEnviar = function(idOp, tipo) {
    const form = document.getElementById(`almacen-form-${idOp}`);
    if (!form) { abrirGeneradorCorreos(idOp, tipo); return; }
    fetch('?handler=GuardarAlmacenamiento', { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
    .then(() => abrirGeneradorCorreos(idOp, tipo))
    .catch(() => abrirGeneradorCorreos(idOp, tipo));
};

    window.guardarAlmacenamientoAjax = function(idOp, btn) {
    const form = document.getElementById(`almacen-form-${idOp}`);
    if (!form) return;
    const original = btn.innerHTML;
    btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Guardando...'; btn.disabled = true;

    fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
    .then(res => res.json()).then(data => {
        btn.innerHTML = original; btn.disabled = false;
        if (data.success) {
            Swal.fire({ icon: 'success', title: 'Almacenamiento guardado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });
        } else {
            Swal.fire('Error', data.message || 'No se pudo guardar.', 'error');
        }
    }).catch(() => { btn.innerHTML = original; btn.disabled = false; Swal.fire('Error', 'Fallo de conexión.', 'error'); });
};

window.subirRespaldoAlmacen = function(input, idOp) {
    if (!input.files || input.files.length === 0) return;
    let formData = new FormData();
    formData.append('IdOperacion', idOp);
    formData.append('ArchivoTransferenciaAlmacen', input.files[0]);
    let token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    fetch('?handler=SubirRespaldoAlmacen', { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest', 'RequestVerificationToken': token } })
    .then(res => res.json()).then(data => {
        if (data.success) {
            Swal.fire({ icon: 'success', title: '¡Guardado!', text: 'Comprobante subido exitosamente.', timer: 1500, showConfirmButton: false });
            let linkDiv = document.getElementById(`link-respaldo-almacen-${idOp}`);
            if (linkDiv) { linkDiv.style.display = 'block'; let a = linkDiv.querySelector('a'); if (a) a.href = data.ruta; }
        } else {
            Swal.fire('Error', data.message || 'No se pudo subir el archivo.', 'error'); input.value = '';
        }
    }).catch(() => { Swal.fire('Error', 'Problema de conexión.', 'error'); input.value = ''; });
};

    window.ejecutarGuardadoCertificado = function(pane, certKey, idBase, enviarFinanzas, sinNumero, btn) {
    const ctx = modalCtx() || document;
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const idOperacionReal = idBase.toString().replace('sub-', '').replace('v3-', '');
    const formData = new FormData();
    
    formData.append('IdOperacion', idOperacionReal); 
    formData.append('CertKey', certKey);
    formData.append('EnviarFinanzas', enviarFinanzas); 
    formData.append('SinNumero', sinNumero);

    const slotActivoBtn = pane.querySelector('.slot-btn.active');
    let slotActivo = '1';
    if (slotActivoBtn) { const m = slotActivoBtn.getAttribute('onclick')?.match(/'(\d)'\)/); if (m) slotActivo = m[1]; }
    formData.append('SlotActivo', slotActivo);

    // Solo mandamos el estado de los switches que sí existen
    const chkSag = ctx.querySelector(`#chkSag-${idBase}`);
    if (chkSag) formData.append('docUpdates.AplicaSag', chkSag.checked ? 'true' : 'false');

    const chkSerna = ctx.querySelector(`#chkSerna-${idBase}`);
    if (chkSerna) formData.append('docUpdates.AplicaSernapesca', chkSerna.checked ? 'true' : 'false');

    pane.querySelectorAll('input[name^="docUpdates."], textarea[name^="docUpdates."], select[name^="docUpdates."]').forEach(el => {
        if (el.type === 'checkbox') formData.append(el.name, el.checked ? 'true' : 'false');
        else if (el.type === 'radio') { if (el.checked) formData.append(el.name, el.value); }
        else if (el.type === 'file') { /* los <input type="file"> de este flujo no se llaman "docUpdates.*" — se buscan aparte, más abajo */ }
        else formData.append(el.name, el.value);
    });

    // oct-2026 (bug real corregido): el archivo adjuntado con el botón del clip nunca
    // llegaba a subirse desde este botón "Guardar". Los <input type="file"> de este
    // flujo (UploadOri1, UploadCertFito, UploadCoa, etc.) nunca coincidían con el
    // selector 'input[name^="docUpdates."]' de arriba, así que el archivo quedaba
    // seleccionado en el navegador pero jamás viajaba en el FormData — el aviso
    // "Archivo Registrado" que se ve al adjuntar es solo un registro visual local, no
    // una subida real. El backend (OnPostGuardarCertificadoAsync) siempre espera el
    // archivo bajo la misma clave fija "UploadCertA1" sin importar el certificado o la
    // reemisión — es el propio servidor el que decide a qué slot pertenece. Por eso acá
    // basta con mandar el único <input type="file"> de este pane que efectivamente
    // tenga un archivo elegido (nunca hay más de uno a la vez: Grupal/Individual son
    // ramas que se excluyen entre sí, y de las reemisiones solo la del slot activo
    // queda habilitada para elegir archivo).
    const fileInputConArchivo = Array.from(pane.querySelectorAll('input[type="file"]')).find(f => f.files && f.files.length > 0);
    if (fileInputConArchivo) formData.append('UploadCertA1', fileInputConArchivo.files[0]);

    const original = btn ? btn.innerHTML : null;
    if (btn) { btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Guardando...'; btn.disabled = true; }

    fetch('?handler=GuardarCertificado', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
    .then(r => r.json()).then(data => {
        if (btn) { btn.innerHTML = original; btn.disabled = false; }
        if (data.success) {
            Swal.fire({ icon: 'success', title: 'Guardado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });
            setTimeout(() => window.location.reload(), 800);
        } else { Swal.fire('Atención', data.message || 'No se pudo guardar.', 'warning'); }
    }).catch(() => { 
        if (btn) { btn.innerHTML = original; btn.disabled = false; } 
        Swal.fire('Error', 'Fallo de conexión.', 'error');
    });
};

    window.guardarCertificadoAjax = function(certKey, idBase, enviarFinanzas, btn) {
        const ctx = modalCtx() || document; const paneId = PANE_ID_MAP[certKey](idBase);
        const pane = ctx.querySelector(`#${paneId}`) || document.getElementById(paneId); if (!pane) return;
        const chkSinNum = ctx.querySelector(`#chkSinNum-${certKey}-${idBase}`); const sinNumero = chkSinNum ? chkSinNum.checked : false;
        const inputPrincipal = pane.querySelector(`[name="docUpdates.Num${SUFIJO_CAMPO[certKey]}1"]`);

        if (enviarFinanzas && inputPrincipal && !inputPrincipal.value.trim() && !sinNumero) {
            Swal.fire({ icon: 'question', title: 'Sin número de documento', text: 'No hay N° ingresado. ¿Enviar igual a Finanzas marcando "Sin número"?', showCancelButton: true, confirmButtonText: 'Sí, enviar sin número', target: document.body })
            .then((r) => { if (r.isConfirmed) { if (chkSinNum) chkSinNum.checked = true; ejecutarGuardadoCertificado(pane, certKey, idBase, enviarFinanzas, true, btn); } });
            return;
        }
        ejecutarGuardadoCertificado(pane, certKey, idBase, enviarFinanzas, sinNumero, btn);
    };

    window.aplicarBloqueoNuevasReemisiones = function(certKey, idBase) {
        const ctx = modalCtx() || document; const pane = ctx.querySelector(`#${PANE_ID_MAP[certKey](idBase)}`); if (!pane) return;
        ['2','3','4'].forEach(slot => {
            const numInput = pane.querySelector(`[name="docUpdates.Num${SUFIJO_CAMPO[certKey]}${slot}"]`);
            if (numInput && !numInput.value.trim()) {
                pane.querySelectorAll('.slot-btn').forEach(b => {
                    if (b.getAttribute('onclick')?.includes(`'${slot}')`)) { b.disabled = true; b.title = 'Certificado ya enviado a Finanzas — no se pueden agregar nuevas reemisiones'; }
                });
            }
        });
    };

    window.marcarBookingComoModificado = function(idOp) {
        if (!idOp) return;
        const realId = idOp.toString().replace('sub-', ''); const btn = document.querySelector(`.btn-enviar-correo[data-id="${realId}"]`); if (!btn) return;
        const yaEnviado = btn.getAttribute('data-enviado') === 'true'; if (!yaEnviado) return; 
        const version = parseInt(btn.getAttribute('data-version')) || 1; btn.setAttribute('data-enviado', 'false');
        const row = btn.closest('tr.row-main'); const badgeSpan = row ? row.querySelector('.email-badge') : null;
        if (badgeSpan && badgeSpan.parentElement) { badgeSpan.parentElement.innerHTML = `<span class="email-badge pend-red" title="Modificado tras el envío — pendiente de reenviar v${version + 1}"><i class="bi bi-circle-fill"></i> P (v${version + 1})</span>`; }
    };

    // ==========================================
    // 7. FLUJOS DE RESOLUCIÓN Y CLIENTE
    // ==========================================
    window.registrarResolucionDoc = function(tipo, certKey, idOp, btn) {
        const ctx = modalCtx(); if (!ctx) return;
        const logEl = ctx.querySelector(`#log-${certKey}-txt-${idOp}`);
        const ahora = new Date(); const fecha = ahora.toLocaleDateString('es-CL'); const hora  = ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' });

        const agregarLog = (texto, color) => {
            if (!logEl) return;
            if (logEl.textContent.trim() === 'Sin registros.') logEl.innerHTML = '';
            const div = document.createElement('div');
            div.style.cssText = 'display:flex; align-items:flex-start; gap:6px; padding:4px 0; border-bottom:1px solid #f1f5f9;';
            div.innerHTML = `<i class="bi bi-circle-fill" style="font-size:7px; color:${color}; margin-top:3px; flex-shrink:0;"></i><div style="line-height:1.2;"><span style="font-size:9px; font-weight:800; color:${color}; text-transform:uppercase;">${texto}</span><span style="font-size:9px; color:#94a3b8; margin-left:4px;">${fecha} ${hora}</span><span style="font-size:9px; font-weight:700; color:#64748b; margin-left:4px;">· Operaciones</span></div>`;
            logEl.insertBefore(div, logEl.firstChild);
        };

        if (tipo === 'aprobado') {
            const stepAprob = ctx.querySelector(`#step-aprob-${certKey}-${idOp}`);
            if (stepAprob) {
                stepAprob.classList.remove('rejected'); stepAprob.classList.add('completed');
                const circle = stepAprob.querySelector('.step-circle') || stepAprob.querySelector('.pro-step-icon');
                if (circle) circle.innerHTML = '<i class="bi bi-check-lg" style="font-size:16px;"></i>';
                const titleEl = stepAprob.querySelector('.step-title') || stepAprob.querySelector('.pro-step-text');
                if (titleEl) titleEl.innerText = 'APROBADO';
            }
            ['paso-pago','paso-scan'].forEach(prefix => { const paso = ctx.querySelector(`#${prefix}-${certKey}-${idOp}`); if (paso) paso.style.opacity = '1'; });
            const obsPanel = ctx.querySelector(`#obs-${certKey}-${idOp}`); if (obsPanel) obsPanel.classList.add('d-none');
            agregarLog('Aprobado', '#16a34a');
        } else if (tipo === 'rechazado') {
            const obsPanel = ctx.querySelector(`#obs-${certKey}-${idOp}`) || ctx.querySelector(`#v3-rechazo-${certKey}-${idOp.replace('v3-', '')}`);
            if (obsPanel) obsPanel.classList.remove('d-none');
        }
    };

    window.confirmarRechazoDoc = function(certKey, idOp) {
        const ctx = modalCtx(); if (!ctx) return;
        const txtObs = ctx.querySelector(`#txt-obs-${certKey}-${idOp}`);
        if (!txtObs || !txtObs.value.trim()) { txtObs?.classList.add('border-danger'); txtObs?.focus(); return; }

        const stepAprob = ctx.querySelector(`#step-aprob-${certKey}-${idOp}`);
        if (stepAprob) {
            stepAprob.classList.remove('completed'); stepAprob.classList.add('rejected');
            const circle = stepAprob.querySelector('.step-circle') || stepAprob.querySelector('.pro-step-icon');
            if (circle) circle.innerHTML = '<i class="bi bi-x-lg" style="font-size:14px;"></i>';
            const titleEl = stepAprob.querySelector('.step-title') || stepAprob.querySelector('.pro-step-text');
            if (titleEl) titleEl.innerText = 'RECHAZADO';
        }
        ['paso-pago','paso-scan'].forEach(prefix => { const paso = ctx.querySelector(`#${prefix}-${certKey}-${idOp}`); if (paso) { paso.style.opacity = '0.3'; paso.style.pointerEvents = 'none'; } });

        const logEl = ctx.querySelector(`#log-${certKey}-txt-${idOp}`);
        if (logEl) {
            if (logEl.textContent.trim() === 'Sin registros.') logEl.innerHTML = '';
            const ahora = new Date(); const fecha = ahora.toLocaleDateString('es-CL'); const hora  = ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' });
            const div = document.createElement('div');
            div.style.cssText = 'display:flex; align-items:flex-start; gap:6px; padding:4px 0; border-bottom:1px solid #f1f5f9;';
            div.innerHTML = `<i class="bi bi-circle-fill" style="font-size:7px; color:#dc2626; margin-top:3px; flex-shrink:0;"></i><div style="line-height:1.2;"><span style="font-size:9px; font-weight:800; color:#dc2626; text-transform:uppercase;">Rechazado</span><span style="font-size:9px; color:#94a3b8; margin-left:4px;">${fecha} ${hora}</span><span style="font-size:9px; color:#64748b; margin-left:4px;">· Operaciones</span><div style="font-size:9px; color:#475569; margin-top:2px; font-style:italic;">${txtObs.value.trim()}</div></div>`;
            logEl.insertBefore(div, logEl.firstChild);
        }
        const obsPanel = ctx.querySelector(`#obs-${certKey}-${idOp}`) || ctx.querySelector(`#v3-rechazo-${certKey}-${idOp.replace('v3-', '')}`);
        if (obsPanel) obsPanel.classList.add('d-none');
        if (txtObs) { txtObs.value = ''; txtObs.classList.remove('border-danger'); }
    };

    window.registrarRespuestaClienteManual = function(tipo, certKey, idOp, btn) {
        const ctx = modalCtx(); if (!ctx) return; if (tipo !== 'aprobado') return; 
        const step = ctx.querySelector(`#step-cliente-${certKey}-${idOp}`);
        if (step) {
            step.classList.remove('rejected'); step.classList.add('completed');
            const icon = step.querySelector('.pro-step-icon'); if (icon) icon.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>';
            const title = step.querySelector('.pro-step-text'); if (title) title.innerText = 'Aprobado';
        }
        const logEl = ctx.querySelector(`#log-${certKey}-txt-${idOp}`);
        if (logEl) {
            if (logEl.textContent.trim() === 'Sin registros.') logEl.innerHTML = '';
            const ahora = new Date(); const div = document.createElement('div');
            div.style.cssText = 'border-left:2px solid #16a34a; padding:6px; margin-bottom:6px; background:#f0fdf4; border-top:1px solid #bbf7d0; border-right:1px solid #bbf7d0; border-bottom:1px solid #bbf7d0;';
            div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:#16a34a; text-transform:uppercase; font-size:10px;">RESPUESTA CLIENTE</span><span style="color:#64748b; font-size:9px;">${ahora.toLocaleDateString('es-CL')} ${ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' })}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>Operaciones Atilson (registro manual)</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">El cliente aprobó el certificado por un medio distinto al correo (WhatsApp / llamada).</div>`;
            logEl.insertBefore(div, logEl.firstChild);
        }
    };

    window.confirmarRespuestaClienteManualRechazo = function(certKey, idOp) {
        const ctx = modalCtx(); if (!ctx) return;
        const txtObs = ctx.querySelector(`#txt-obs-${certKey}-${idOp}`);
        if (!txtObs || !txtObs.value.trim()) { txtObs?.classList.add('border-danger'); txtObs?.focus(); return; }

        const step = ctx.querySelector(`#step-cliente-${certKey}-${idOp}`);
        if (step) {
            step.classList.remove('completed'); step.classList.add('rejected');
            const icon = step.querySelector('.pro-step-icon'); if (icon) icon.innerHTML = '<i class="bi bi-x-lg" style="font-size:14px;"></i>';
            const title = step.querySelector('.pro-step-text'); if (title) title.innerText = 'Rechazado';
        }
        const logEl = ctx.querySelector(`#log-${certKey}-txt-${idOp}`);
        if (logEl) {
            if (logEl.textContent.trim() === 'Sin registros.') logEl.innerHTML = '';
            const ahora = new Date(); const div = document.createElement('div');
            div.style.cssText = 'border-left:2px solid #dc2626; padding:6px; margin-bottom:6px; background:#fef2f2; border-top:1px solid #fecaca; border-right:1px solid #fecaca; border-bottom:1px solid #fecaca;';
            div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:#dc2626; text-transform:uppercase; font-size:10px;">RESPUESTA CLIENTE (RECHAZO)</span><span style="color:#64748b; font-size:9px;">${ahora.toLocaleDateString('es-CL')} ${ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' })}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>Operaciones Atilson (registro manual)</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">El cliente solicitó cambios por un medio distinto al correo:<br><em>"${txtObs.value.trim()}"</em></div>`;
            logEl.insertBefore(div, logEl.firstChild);
        }
        const rechazoPanel = ctx.querySelector(`#v3-rechazo-${certKey}-${idOp.replace(/^v3-/, '')}`) || ctx.querySelector(`#obs-${certKey}-${idOp}`);
        if (rechazoPanel) rechazoPanel.classList.add('d-none');
        txtObs.value = ''; txtObs.classList.remove('border-danger');
    };

    window.marcarCertificadoFinalCompletado = function(certKey, idOp) {
        const ctx = modalCtx() || document; const step = ctx.querySelector(`#step-aprob-${certKey}-${idOp}`); if (!step) return;
        step.classList.remove('rejected'); step.classList.add('completed');
        const icon = step.querySelector('.pro-step-icon') || step.querySelector('.step-circle');
        if (icon) icon.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>';
    };

    window.verCambiosCliente = function(elemento) {
        let base64 = elemento.getAttribute('data-comment'); let comentarios = "No se ingresaron comentarios detallados.";
        if (base64 && base64.trim() !== '') {
            try {
                let binaryString = window.atob(base64); let binaryLen = binaryString.length; let bytes = new Uint8Array(binaryLen);
                for (let i = 0; i < binaryLen; i++) { bytes[i] = binaryString.charCodeAt(i); }
                comentarios = new TextDecoder("utf-8").decode(bytes);
            } catch (e) { console.error("Error leyendo datos:", e); comentarios = "Error al intentar leer los comentarios del cliente."; }
        }
        Swal.fire({ title: 'Correcciones Solicitadas', html: `<div style="text-align: left; padding: 15px; background: #f8fafc; border-left: 4px solid #dc2626; border-radius: 4px; font-size: 13px; color: #334155; line-height: 1.5; white-space: pre-wrap; margin-top: 10px;">${comentarios}</div>`, icon: 'warning', iconColor: '#dc2626', confirmButtonText: 'Entendido', confirmButtonColor: '#0f172a', target: document.body });
    };

                window.forzarAprobacionBL = function(idOp, btn) {
        let formContainer = btn.closest('.form-section') || btn.closest('form');
        let inputEstado = formContainer.querySelector('input[name="docUpdates.EstadoDocumental"]');
        if (inputEstado) inputEstado.value = "B/L APROBADO";
        registrarResolucionDoc('aprobado', 'matriz', idOp, btn);
        const ring = modalCtx()?.querySelector(`#ring-matriz-${idOp}`);
        if (ring) ring.setAttribute('data-aprobado', 'true');
        window.recalcularProgresoMatriz(idOp);
        Swal.fire({ icon: 'success', title: 'Aprobación Forzada', text: 'Presione el botón negro "Guardar Matriz y B/L" abajo para confirmar los cambios en la base de datos.', target: document.body });
    };

    window.togglePagoScan = function(element, opId) {
        if (element.classList.contains('rejected') || element.style.cursor === 'default') return;
        element.classList.toggle('completed'); const circle = element.querySelector('.step-circle'); const isCompleted = element.classList.contains('completed');
        if (isCompleted) { circle.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; } else { circle.innerHTML = '<i class="bi bi-check"></i>'; }
        const sendBtns = document.querySelectorAll('.btn-enviar-cli-cap-' + opId);
        sendBtns.forEach(btn => { if (isCompleted) { btn.classList.remove('d-none'); } else { btn.classList.add('d-none'); } });
        let logEl = modalCtx()?.querySelector(`#log-cap-txt-${opId}`);
        if (logEl) {
            if(logEl.innerHTML.includes('Sin registros')) logEl.innerHTML = '';
            const ahora = new Date(); const div = document.createElement('div');
            div.style.cssText = 'border-left:2px solid #dc2626; padding:6px; margin-bottom:6px; background:#fef2f2; border-top:1px solid #fecaca; border-right:1px solid #fecaca; border-bottom:1px solid #fecaca;';
            div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:#dc2626; text-transform:uppercase; font-size:10px;">${isCompleted ? 'PAGO / SCAN CONFIRMADO' : 'PAGO / SCAN REVERTIDO'}</span><span style="color:#64748b; font-size:9px;">${ahora.toLocaleDateString('es-CL')} ${ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' })}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>Operaciones Atilson</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">Se habilitó el botón para enviar documento al cliente.</div><span class="badge mt-2 d-inline-block" style="font-size:8px; background:#fef2f2; color:#dc2626; border:1px solid #fca5a5;">PENDIENTE GUARDADO</span>`;
            logEl.insertBefore(div, logEl.firstChild);
        }
    };

    // ==========================================
    // 8. HISTORIAL E INCIDENCIAS 
    // ==========================================
    window.registrarIncidenciaSubcert = function(tipo, idOp) {
        const nombresTipo = { 'coa': 'COA', 'dt': 'DT', 'cod': 'CODAUT', 'cla': 'CLAVE', 'nep': 'NEPPEX', 'libreven': 'Libre Venta', 'ori': 'Origen / TLC', 'fit': 'Fitosanitario', 'san': 'Sanitario', 'cap': 'Captura', 'matriz': 'Matriz / BL', 'factura': 'Factura', 'guia': 'Guía', 'instructivo': 'Instructivo', 'packing': 'Packing List', 'extra': 'Cert. Extra', 'dhl': 'DHL', 'capturaAga': 'Captura AGA' };
        Swal.fire({ title: `Incidencia en ${nombresTipo[tipo] || tipo.toUpperCase()}`, input: 'textarea', inputPlaceholder: 'Escriba el detalle del cambio o incidencia...', showCancelButton: true, confirmButtonText: '<i class="bi bi-check-circle"></i> Agregar', cancelButtonText: 'Cancelar', confirmButtonColor: '#6d2d9e', cancelButtonColor: '#64748b' })
        .then((result) => {
            if (result.isConfirmed && result.value.trim() !== '') {
                const ahora = new Date(); const fecha = ahora.toLocaleDateString('es-CL'); const hora  = ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' }); const usuario = 'Operaciones Atilson'; 
                const logEl = modalCtx()?.querySelector(`#log-${tipo}-txt-${idOp}`);
                if (logEl) {
                    if(logEl.innerHTML.includes('Sin registros')) logEl.innerHTML = '';
                    const div = document.createElement('div');
                    div.style.cssText = 'border-left:2px solid #6d2d9e; padding:6px; margin-bottom:6px; background:#f8fafc; border-top:1px solid #f1f5f9; border-right:1px solid #f1f5f9; border-bottom:1px solid #f1f5f9;';
                    div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:#0f172a; text-transform:uppercase; font-size:10px;">${nombresTipo[tipo] || tipo}</span><span style="color:#64748b; font-size:9px;">${fecha} ${hora}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>${usuario}</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">${result.value}</div><span class="badge mt-2 d-inline-block" style="font-size:8px; background:#f3e8ff; color:#6d2d9e; border:1px solid #d8b4fe;">PENDIENTE GUARDADO</span>`;
                    logEl.insertBefore(div, logEl.firstChild);
                }
                Swal.fire({ icon: 'success', title: 'Añadido', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });
            }
        });
    };

    window.marcarEstadoRapidoVisual = function(tipo, idOp, titulo, color, detalle) {
        const ctx = modalCtx() || document; const logEl = ctx.querySelector(`#log-${tipo}-txt-${idOp}`); if (!logEl) return;
        if(logEl.innerHTML.includes('Sin registros')) logEl.innerHTML = '';
        const ahora = new Date(); const fecha = ahora.toLocaleDateString('es-CL'); const hora  = ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' });
        let bgColor = color === '#16a34a' ? '#f0fdf4' : (color === '#0ea5e9' ? '#f0f9ff' : (color === '#dc2626' ? '#fef2f2' : '#f8fafc'));
        let borderColor = color === '#16a34a' ? '#bbf7d0' : (color === '#0ea5e9' ? '#bae6fd' : (color === '#dc2626' ? '#fca5a5' : '#e2e8f0'));
        const div = document.createElement('div');
        div.style.cssText = `border-left:2px solid ${color}; padding:6px; margin-bottom:6px; background:${bgColor}; border-top:1px solid ${borderColor}; border-right:1px solid ${borderColor}; border-bottom:1px solid ${borderColor};`;
        div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:${color}; text-transform:uppercase; font-size:10px;">${titulo}</span><span style="color:#64748b; font-size:9px;">${fecha} ${hora}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>Operaciones Atilson</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">${detalle}</div><span class="badge mt-2 d-inline-block" style="font-size:8px; background:#e2e8f0; color:#475569; border:1px solid #cbd5e1;">PENDIENTE GUARDADO</span>`;
        logEl.insertBefore(div, logEl.firstChild);
    };

    window.marcarEstadoRapido = function(tipo, idOp, estado, color) {
        const ctx = modalCtx(); const logEl = ctx ? ctx.querySelector(`#log-${tipo}-txt-${idOp}`) : document.getElementById(`log-${tipo}-txt-${idOp}`); if (!logEl) return;
        if(logEl.innerHTML.includes('Sin registros')) logEl.innerHTML = '';
        const ahora = new Date(); const fecha = ahora.toLocaleDateString('es-CL'); const hora  = ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' });
        let bgColor = color === '#16a34a' ? '#f0fdf4' : (color === '#0ea5e9' ? '#f0f9ff' : '#f8fafc'); let borderColor = color === '#16a34a' ? '#bbf7d0' : (color === '#0ea5e9' ? '#bae6fd' : '#e2e8f0');
        const div = document.createElement('div');
        div.style.cssText = `border-left:2px solid ${color}; padding:6px; margin-bottom:6px; background:${bgColor}; border-top:1px solid ${borderColor}; border-right:1px solid ${borderColor}; border-bottom:1px solid ${borderColor};`;
        div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:${color}; text-transform:uppercase; font-size:10px;">${estado}</span><span style="color:#64748b; font-size:9px;">${fecha} ${hora}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>Operaciones Atilson</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">Se actualizó el estado a: <strong style="color:${color};">${estado}</strong></div><span class="badge mt-2 d-inline-block" style="font-size:8px; background:#e2e8f0; color:#475569; border:1px solid #cbd5e1;">PENDIENTE GUARDADO</span>`;
        logEl.insertBefore(div, logEl.firstChild);
        const stepper = ctx ? ctx.querySelector(`#stepper-${tipo}-${idOp}`) : document.getElementById(`stepper-${tipo}-${idOp}`);
        if (stepper) {
            let steps = stepper.querySelectorAll('.step-item');
            if (estado.toUpperCase().includes("PROCESO") && steps.length > 1) { steps[1].classList.add('completed'); let circle = steps[1].querySelector('.step-circle'); if(circle) circle.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; }
            if (estado.toUpperCase().includes("VISADO") && steps.length > 2) { steps[1].classList.add('completed'); steps[2].classList.add('completed'); let c1 = steps[1].querySelector('.step-circle'); let c2 = steps[2].querySelector('.step-circle'); if(c1) c1.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; if(c2) c2.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; }
            if (estado.toUpperCase().includes("RECEPCIÓN") && steps.length > 1) { steps[1].classList.add('completed'); let circle = steps[1].querySelector('.step-circle'); if(circle) circle.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; }
        }
    };

    window.logMatrizConfirmada = function(idOp, checkbox) {
        const estado = checkbox.checked ? "MATRIZ CONFIRMADA" : "MATRIZ DESMARCADA"; const color = checkbox.checked ? "#16a34a" : "#dc2626"; const detalle = checkbox.checked ? "Se ha confirmado la matriz." : "Se ha quitado la confirmación de la matriz.";
        marcarEstadoRapidoVisual('matriz', idOp, estado, color, detalle);
    };

    window.registrarIncidenciaMatriz = function(tipo, opId) {
        const ahora = new Date(); const fechaHora = ahora.toLocaleDateString('es-CL') + ' ' + ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' }); const esCorreccion = tipo === 'correccion-bl';
        const columnaId  = esCorreccion ? `log-correccion-bl-${opId}` : `log-fuera-plazo-${opId}`; const vacioId    = esCorreccion ? `log-cbl-vacio-${opId}`      : `log-fp-vacio-${opId}`;
        const columna    = mq(`#${columnaId}`); const vacioBadge = mq(`#${vacioId}`);
        if (!columna) return; if (vacioBadge) vacioBadge.style.display = 'none';
        let motivo = esCorreccion ? (mq(`#panel-correccion-bl-${opId} input[name="MotivoCorreccionBL"]`)?.value || '(sin motivo)') : '';
        const html = `<div class="d-flex align-items-start gap-2 p-2 mb-2 rounded" style="border:1px solid #fca5a5; background:#fef2f2; border-left:3px solid #ef4444;"><i class="bi ${esCorreccion ? 'bi-pencil-square' : 'bi-clock-history'} text-danger mt-1 flex-shrink-0"></i><div class="flex-grow-1 overflow-hidden"><div class="d-flex justify-content-between align-items-center"><span class="fw-bold text-danger" style="font-size:10px; text-transform:uppercase;">${esCorreccion ? 'Corrección BL' : 'Fuera de Plazo'}</span><span class="text-muted" style="font-size:9px;">${fechaHora}</span></div><div class="text-muted mt-1" style="font-size:10px;"><strong>Operaciones Atilson</strong>${motivo ? `<div>${motivo}</div>` : ''}</div><span class="mt-1 d-inline-block" style="font-size:9px; font-weight:800; padding:2px 6px; border-radius:4px; background:#fee2e2; color:#dc2626; border:1px solid #fca5a5;">PENDIENTE GUARDADO</span></div></div>`;
        columna.insertAdjacentHTML('beforeend', html); cerrarPanelMatriz(esCorreccion ? `panel-correccion-bl-${opId}` : `panel-fuera-plazo-${opId}`);
    };

    // (movido a wwwroot/js/operaciones-common.js: window.sincronizarLogsAntesDeGuardar — auditoría oct-2026, dedup JS)

    // (movido a wwwroot/js/operaciones-common.js: window.abrirHistorial — auditoría oct-2026, dedup JS)

    window.cancelarInterplanta = function(idOp) {
    // 1. Desactivamos el switch visualmente
    let chk = document.getElementById('chkInterplanta-' + idOp);
    if (chk) chk.checked = false;

    // 2. Ocultamos la caja de interplanta y vaciamos los campos
    let box = document.getElementById('interplanta-box-' + idOp);
    if(box) {
        box.style.display = 'none';

        // Vaciamos los selects y gatillamos el "change" para evitar cruces con el motor de rutas
        box.querySelectorAll('select').forEach(s => { 
            s.value = ''; 
            s.dispatchEvent(new Event('change')); 
        });

        // Vaciamos los inputs de links
        box.querySelectorAll('input[type="url"]').forEach(i => i.value = '');

        // Ocultamos la 3ra planta si el usuario la había desplegado
        let boxP3 = document.getElementById('planta3-box-' + idOp);
        if (boxP3) boxP3.style.display = 'none';

        // Restauramos el estado visual del botón "Añadir 3ra Planta"
        let btnP3 = document.getElementById('btn-add-p3-' + idOp);
        if (btnP3) {
             btnP3.style.display = 'block'; 
             let btnChild = btnP3.querySelector('button');
             if(btnChild) btnChild.classList.remove('hide-if-has-p3');
        }
    }
};

    // ==========================================
    // 9. EVENTOS AUTOMÁTICOS UI (FILE, SELECTS)
    // ==========================================
    window.toggleMatrizCheck = function(wrapper) {
        const isChecked = wrapper.getAttribute('data-checked') === 'true'; const newVal = !isChecked; wrapper.setAttribute('data-checked', newVal ? 'true' : 'false');
        const form = wrapper.closest('form'); if (form) { const hidden = form.querySelector('input[name="MatrizPresentada"]'); if (hidden) hidden.value = newVal ? 'true' : 'false'; }
        const box = wrapper.querySelector('div'); const icon = wrapper.querySelector('i'); const label = wrapper.querySelector('span');
        if (newVal) { wrapper.style.border = '1px solid #bbf7d0'; wrapper.style.background = '#f0fdf4'; box.style.border = '1.5px solid #16a34a'; box.style.background = '#16a34a'; icon.style.display = 'block'; label.style.color = '#16a34a'; label.innerText = 'Confirmada'; } else { wrapper.style.border = '1px solid #e2e8f0'; wrapper.style.background = '#f8fafc'; box.style.border = '1.5px solid #cbd5e1'; box.style.background = '#fff'; icon.style.display = 'none'; label.style.color = '#94a3b8'; label.innerText = 'Pendiente'; }
    };

    window.gestionGateChanged = function(select) {
        const form = select.closest('form'); if (!form) return;
        const pagoWrapper = form.querySelector('[id^="pago-gate-wrapper-"]');
        if (pagoWrapper) pagoWrapper.style.display = select.value === 'Cliente gestiona directo' ? 'none' : '';
    };

    window.onEvidenciaNavieraChange = function(input, idOp, booking, naviera) {
        const ctx = modalCtx() || document; const wrapper = ctx.querySelector(`#btn-envio-naviera-wrapper-${idOp}`); if (!wrapper) return;
        if (input.files && input.files.length > 0) {
            wrapper.classList.remove('d-none');
            if(typeof marcarEstadoRapidoVisual === 'function') { marcarEstadoRapidoVisual('matriz', idOp, 'EVIDENCIA NAVIERA ADJUNTA', '#10b981', `Se adjuntó archivo: <em>${escapeHtmlAtilson(input.files[0].name)}</em>`); }
        } else { wrapper.classList.add('d-none'); }
    };

    window.registrarArchivoBlMatriz = function(input, id) {
        const ctx = modalCtx() || document; const statusDiv = ctx.querySelector(`#bl-file-status-${id}`); const btnEnvio = ctx.querySelector(`#btn-envio-borrador-${id}`);
        if (input.files && input.files.length > 0) {
            if (statusDiv) { statusDiv.textContent = input.files[0].name; statusDiv.style.color = '#16a34a';  statusDiv.style.fontWeight = 'bold'; }
            if (btnEnvio) btnEnvio.classList.remove('d-none');
            if(typeof marcarEstadoRapidoVisual === 'function') { marcarEstadoRapidoVisual('matriz', id, 'ARCHIVO B/L ADJUNTO', '#10b981', `Se adjuntó borrador B/L: <em>${escapeHtmlAtilson(input.files[0].name)}</em>`); }
        } else {
            if (statusDiv) { statusDiv.textContent = 'Sin archivo adjunto'; statusDiv.style.color = '#94a3b8';  statusDiv.style.fontWeight = 'normal'; }
            if (btnEnvio) btnEnvio.classList.add('d-none');
        }
    };

    // (window.registrarActividadSubcert: oct-2026, dedup — esta función estaba definida
    // dos veces, byte a byte idéntica, acá y más abajo junto a enviarCertificadoCliente.
    // La de acá nunca se ejecutaba de verdad: al cargar el script, la segunda asignación
    // a window.registrarActividadSubcert pisaba esta sin que nadie lo notara, porque el
    // resultado final era el mismo. Queda solo la de más abajo.)

    window.toggleTransbordoIndex = function(select, idOp) {
        const inputTransbordo = document.getElementById(`inpTransbordo-${idOp}`);
        if (inputTransbordo) {
            if (select.value === 'TRANSBORDO') { inputTransbordo.style.display = 'block'; inputTransbordo.disabled = false; inputTransbordo.focus(); } 
            else { inputTransbordo.style.display = 'none'; inputTransbordo.value = ''; inputTransbordo.disabled = true; }
        }
    };

    window.toggleReporteMatriz = function(panelId, btn) {
        const panel = mq(`#${panelId}`); if (!panel) return;
        const isOpen = !panel.classList.contains('d-none');
        mqAll('.reporte-matriz-panel').forEach(p => p.classList.add('d-none'));
        if (!isOpen) panel.classList.remove('d-none');
    };

    window.cerrarPanelMatriz = function(panelId) { const panel = mq(`#${panelId}`); if (panel) panel.classList.add('d-none'); };

    window.actualizarTerminalTerrestreIndex = function(select, idOp) {
        const val = select.value.toUpperCase(); const selTerm = document.getElementById(`selTermTer-${idOp}`); const inpTerm = document.getElementById(`inpTermTer-${idOp}`); const viewOnlyTerm = document.querySelector(`#ter-form-${idOp} .view-only-terminalterrestre`);
        if (!selTerm || !inpTerm) return;
        if (val.includes('SAN ANTONIO')) { selTerm.style.display = 'block'; selTerm.disabled = false; inpTerm.style.display = 'none'; inpTerm.disabled = true; if (viewOnlyTerm && !['STI', 'DPW'].includes(viewOnlyTerm.value)) viewOnlyTerm.value = '-'; } 
        else {
            selTerm.style.display = 'none'; selTerm.disabled = true; inpTerm.style.display = 'block'; inpTerm.disabled = false;
            if (val.includes('VALPARAISO') || val.includes('VALPARAÍSO')) { inpTerm.value = 'TPS'; if (viewOnlyTerm) viewOnlyTerm.value = 'TPS'; } 
            else if (val.includes('VICENTE')) { inpTerm.value = 'SVTI'; if (viewOnlyTerm) viewOnlyTerm.value = 'SVTI'; } 
            else if (val.includes('LIRQUEN') || val.includes('LIRQUÉN')) { inpTerm.value = 'DPW LIRQUEN'; if (viewOnlyTerm) viewOnlyTerm.value = 'DPW LIRQUEN'; } 
            else if (val.includes('CORONEL')) { inpTerm.value = 'CORONEL'; if (viewOnlyTerm) viewOnlyTerm.value = 'CORONEL'; } 
            else { inpTerm.value = ''; if (viewOnlyTerm) viewOnlyTerm.value = '-'; }
        }
    };

    window.actualizarTerminalMaritimoIndex = function(select, idOp) {
        const option = select.options[select.selectedIndex]; const val = option ? (option.getAttribute('data-nombre') || '').toUpperCase() : ''; const selTerm = document.getElementById(`selTermMar-${idOp}`); const inpTerm = document.getElementById(`inpTermMar-${idOp}`); const viewOnlyTerm = document.querySelector(`#mar-form-${idOp} .view-only-terminalmar`);
        if (!selTerm || !inpTerm) return;
        if (val.includes('SAN ANTONIO')) { selTerm.style.display = 'block'; selTerm.disabled = false; inpTerm.style.display = 'none'; inpTerm.disabled = true; if (viewOnlyTerm && !['STI', 'DPW'].includes(viewOnlyTerm.value)) viewOnlyTerm.value = '-'; } 
        else {
            selTerm.style.display = 'none'; selTerm.disabled = true; inpTerm.style.display = 'block'; inpTerm.disabled = false;
            if (val.includes('VALPARAISO') || val.includes('VALPARAÍSO')) { inpTerm.value = 'TPS'; if (viewOnlyTerm) viewOnlyTerm.value = 'TPS'; } 
            else if (val.includes('VICENTE')) { inpTerm.value = 'SVTI'; if (viewOnlyTerm) viewOnlyTerm.value = 'SVTI'; } 
            else if (val.includes('LIRQUEN') || val.includes('LIRQUÉN')) { inpTerm.value = 'DPW LIRQUEN'; if (viewOnlyTerm) viewOnlyTerm.value = 'DPW LIRQUEN'; } 
            else if (val.includes('CORONEL')) { inpTerm.value = 'CORONEL'; if (viewOnlyTerm) viewOnlyTerm.value = 'CORONEL'; } 
            else { inpTerm.value = ''; if (viewOnlyTerm) viewOnlyTerm.value = '-'; }
        }
    };

    // Empresa de transporte (oct-2026): al elegirla del listado (Proveedores, sin
    // depósitos) se completan solos el RUT y los correos del proveedor.
    window.autoCompletarTransportista = function(select) {
        if (!select) return;
        const option = select.options[select.selectedIndex];
        const ctx = select.closest('.tab-pane') || select.closest('form');
        if (!ctx) return;
        const rut = option ? (option.getAttribute('data-rut') || '') : '';
        const correo = option ? (option.getAttribute('data-correo') || '') : '';
        const inpRut = ctx.querySelector('input[name="RutTransporte"]');
        const inpCorreo = ctx.querySelector('input[name="CorreoTransporte"]');
        if (inpRut) inpRut.value = select.value ? rut : '';
        if (inpCorreo) inpCorreo.value = select.value ? correo : '';
        const vista = ctx.querySelector('.view-only-empresatransporte');
        if (vista) vista.value = select.value ? select.value.toUpperCase() : '-';
    };

    window.autoCompletarPlanta = function(select) {
        const option = select.options[select.selectedIndex]; const ciudad = option.getAttribute('data-ciudad') || ''; const linkMaps = option.getAttribute('data-url') || '';
        const form = select.closest('form');
        if (form) {
            const inputZona = form.querySelector('input[name="ZonaCarga"]'); if (inputZona) inputZona.value = ciudad;
            const inputLink = form.querySelector('input[name="LinkTracking"]'); if (inputLink) inputLink.value = linkMaps;
        }
    };

    window.sincronizarPuertoTerrestre = function(select, idOp) {
        let ctx = document.getElementById(`ter-form-${idOp}`); if (!ctx) return;
        let selPuertoEntrega = ctx.querySelector(`select[name="PuertoEntrega"]`); let inputVista = ctx.querySelector(`.view-only-puertoentrega`); if (!selPuertoEntrega) return;
        let puertoNombre = select.options[select.selectedIndex].text.toUpperCase(); if (puertoNombre.includes("--")) puertoNombre = "";
        let found = false;
        for (let i = 0; i < selPuertoEntrega.options.length; i++) { if (selPuertoEntrega.options[i].text.toUpperCase() === puertoNombre) { selPuertoEntrega.selectedIndex = i; found = true; break; } }
        if (found) { if (inputVista) inputVista.value = puertoNombre; window.actualizarTerminalTerrestreIndex(selPuertoEntrega, idOp); } 
        else if (puertoNombre === "") { selPuertoEntrega.value = ""; if (inputVista) inputVista.value = "-"; window.actualizarTerminalTerrestreIndex(selPuertoEntrega, idOp); }
    };

    // ==========================================
    // 10. BÚSQUEDA Y AUTOCOMPLETADO 
    // ==========================================
    function inicializarBuscadorTransporte() {
        mqAll('input[name="EmpresaTransporte"]').forEach(input => {
            let newInp = input.cloneNode(true); input.parentNode.replaceChild(newInp, input); let container = newInp.parentElement;
            if (!container.classList.contains('position-relative')) container.classList.add('position-relative');
            newInp.addEventListener('input', async function() {
                let val = this.value; let form = this.closest('.tab-pane'); let oldList = form.querySelector('.autocomplete-items');
                if (oldList) oldList.remove(); if (!val || val.length < 2) return;
                try {
                    let response = await fetch(`?handler=BuscarEmpresa&term=${encodeURIComponent(val)}`);
                    if (response.ok) {
                        let data = await response.json(); if (data.length === 0) return;
                        let listNode = document.createElement("DIV"); listNode.setAttribute("class", "autocomplete-items shadow-sm border rounded"); listNode.style.cssText = "position:absolute; background:#fff; z-index:99; width:100%; max-height:200px; overflow-y:auto;"; container.appendChild(listNode);
                        data.forEach(item => {
                            let option = document.createElement("DIV"); option.className = "autocomplete-item p-2 border-bottom style-xs"; option.style.cursor = "pointer"; option.innerHTML = `<strong class="text-primary">${item.empresa}</strong><br><span class='text-muted'>RUT: ${item.rut} | ${item.correo || 'Sin correo'}</span>`;
                            option.addEventListener("click", function() {
                                form.querySelector('input[name="EmpresaTransporte"]').value = item.empresa; form.querySelector('input[name="RutTransporte"]').value = item.rut;
                                let correoInp = form.querySelector('input[name="CorreoTransporte"]'); if(correoInp) correoInp.value = item.correo || "";
                                listNode.remove();
                            });
                            listNode.appendChild(option);
                        });
                    }
                } catch (err) {}
            });
        });

        mqAll('input[name="NombreConductor"]').forEach(input => {
            let newInp = input.cloneNode(true); input.parentNode.replaceChild(newInp, input); let container = newInp.parentElement;
            if (!container.classList.contains('position-relative')) container.classList.add('position-relative');
            newInp.addEventListener('input', async function() {
                let val = this.value; let form = this.closest('.tab-pane'); let oldList = form.querySelector('.autocomplete-items');
                if (oldList) oldList.remove(); if (!val || val.length < 2) return;
                try {
                    let response = await fetch(`?handler=BuscarConductor&term=${encodeURIComponent(val)}`);
                    if (response.ok) {
                        let data = await response.json(); if (data.length === 0) return;
                        let listNode = document.createElement("DIV"); listNode.setAttribute("class", "autocomplete-items shadow-sm border rounded"); listNode.style.cssText = "position:absolute; background:#fff; z-index:99; width:100%; max-height:200px; overflow-y:auto;"; container.appendChild(listNode);
                        data.forEach(item => {
                            let option = document.createElement("DIV"); option.className = "autocomplete-item p-2 border-bottom style-xs"; option.style.cursor = "pointer"; option.innerHTML = `<strong class="text-dark">${item.conductor}</strong> (${item.patente})<br><span class='text-muted'>RUT: ${item.rut} | Tel: ${item.telefono}</span>`;
                            option.addEventListener("click", function() {
                                form.querySelector('input[name="NombreConductor"]').value = item.conductor; let rutInp = form.querySelector('input[name="RutConductor"]'); if(rutInp) rutInp.value = item.rut || "";
                                let telInp = form.querySelector('input[name="TelefonoConductor"]'); if(telInp) telInp.value = item.telefono || "";
                                let patInp = form.querySelector('input[name="Patente"]'); if(patInp) patInp.value = item.patente || "";
                                listNode.remove();
                            });
                            listNode.appendChild(option);
                        });
                    }
                } catch (err) {}
            });
        });
    }
            inicializarBuscadorTransporte();


        document.addEventListener('DOMContentLoaded', function () {
            const realToken = document.querySelector('input[name="__RequestVerificationToken"]:not(.anti-forgery-token-input)');
            if (realToken) {
                document.querySelectorAll('.anti-forgery-token-input').forEach(inp => inp.value = realToken.value);
            }
        });

    window.solicitarDesbloqueo = function(idOp, idUnidad, certKey) {
        Swal.fire({
            title: 'Solicitar Desbloqueo',
            input: 'textarea',
            inputPlaceholder: 'Explica por qué necesitas editar este certificado nuevamente...',
            inputValidator: (value) => { if (!value || !value.trim()) return 'El comentario es obligatorio.'; },
            showCancelButton: true,
            confirmButtonText: 'Enviar Solicitud',
            confirmButtonColor: '#0f172a',
            target: document.body
        }).then((result) => {
            if (!result.isConfirmed) return;
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('idOperacion', idOp);
            if (idUnidad !== null) formData.append('idUnidadTecnica', idUnidad);
            formData.append('certKey', certKey);
            formData.append('comentario', result.value.trim());

            fetch('?handler=SolicitarDesbloqueo', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
            .then(res => res.json()).then(data => {
                if (data.success) {
                    Swal.fire({ icon: 'success', title: 'Solicitud Enviada', text: 'Finanzas recibirá tu solicitud de desbloqueo.', target: document.body })
                    .then(() => window.location.reload());
                } else {
                    Swal.fire('Atención', data.message || 'No se pudo enviar la solicitud.', 'warning');
                }
            }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
        });
    };

    window.guardarResponsabilidad = function(idOperacion, concepto, selectPagado, selectResponsable) {
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const formData = new FormData();
    formData.append('idOperacion', idOperacion);
    formData.append('concepto', concepto);
    formData.append('pagadoPor', selectPagado.value);
    formData.append('responsable', selectResponsable.value);

    fetch('?handler=GuardarResponsabilidad', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
    .then(res => res.json())
    .then(data => {
        if (data.success) {
            Swal.fire({ icon: 'success', title: 'Actualizado', toast: true, position: 'top-end', timer: 1200, showConfirmButton: false });
        } else {
            Swal.fire('Error', data.message || 'No se pudo guardar.', 'error');
        }
    }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
};

            window.registrarRoleoCancelacion = function(idOp, tipoTarifaActual, nombreNaviera, diasUmbral, siempreAplica, totalContenedores) {
        Swal.fire({
            title: 'Registrar Roleo / Cancelación',
            width: 560,
            html: `
                <div style="text-align:left; font-size:13px;">
                    <div class="alert alert-light border py-2 px-3 mb-3" style="font-size:10px;">
                        Naviera: <strong>${nombreNaviera || '-'}</strong> · Umbral de penalidad: <strong>${diasUmbral} días</strong>${siempreAplica === 'true' ? ' <span class="text-danger fw-bold">(aplica a todo evento)</span>' : ''}
                    </div>
                    <label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:6px;">Tipo</label>
                    <select id="swal-roleo-tipo" class="swal2-select" style="margin:0 0 14px; width:100%; font-weight:700;">
                        <option value="ROLEO">Roleo</option>
                        <option value="CANCELACION_NOSHOW">Cancelación / No Show</option>
                    </select>
                    <div class="row g-2 mb-3">
                        <div class="col-6">
                            <label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:6px;">Penalidad</label>
                            <select id="swal-roleo-penalidad" class="swal2-select" style="margin:0; width:100%; font-weight:700;">
                                <option value="AUTOMATICO" selected>Automático (según sistema)</option>
                                <option value="SI">Forzar: CON penalidad</option>
                                <option value="NO">Forzar: SIN penalidad</option>
                                <option value="POR_DETERMINAR">Por determinar</option>
                            </select>
                        </div>
                                                <div class="col-6">
                            <label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:6px;">N° Contenedores Afectados</label>
                            <input type="number" id="swal-roleo-cant" class="swal2-input" style="margin:0; width:100%;" value="${totalContenedores}" min="1" max="${totalContenedores}" />
                            <small style="font-size:9px; color:#94a3b8; display:block; margin-top:3px;">Este booking tiene <strong>${totalContenedores}</strong> contenedor(es) en total. Ajusta este número solo si el roleo/cancelación afecta a menos.</small>
                        </div>
                    </div>
                    <label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:6px;">Comentario (opcional)</label>
                    <textarea id="swal-roleo-comentario" class="swal2-textarea" style="margin:0 0 14px; width:100%; font-size:12px; height:60px;" placeholder="Detalle adicional..."></textarea>

                    <div class="form-check form-switch mb-2">
                        <input class="form-check-input" type="checkbox" id="swal-roleo-pendiente" checked>
                        <label class="form-check-label" for="swal-roleo-pendiente" style="font-size:11px; font-weight:700;">Aún no se conoce el nuevo itinerario (dejar pendiente)</label>
                    </div>
                    <div id="swal-roleo-itinerario" style="display:none;">
                        <div class="row g-2">
                            <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nueva Nave</label><input type="text" id="swal-roleo-nave" class="swal2-input" style="margin:0; width:100%;" /></div>
                            <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo ETD</label><input type="datetime-local" id="swal-roleo-etd" class="swal2-input" style="margin:0; width:100%;" /></div>
                            <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo ETA</label><input type="datetime-local" id="swal-roleo-eta" class="swal2-input" style="margin:0; width:100%;" /></div>
                            <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo Stacking</label><input type="datetime-local" id="swal-roleo-stacking" class="swal2-input" style="margin:0; width:100%;" /></div>
                            <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo Cierre Documental</label><input type="datetime-local" id="swal-roleo-cierre" class="swal2-input" style="margin:0; width:100%;" /></div>
                        </div>
                    </div>
                </div>`,
            showCancelButton: true,
            confirmButtonText: 'Registrar',
            confirmButtonColor: '#0f172a',
            cancelButtonText: 'Cancelar',
            target: document.body,
            didOpen: () => {
                document.getElementById('swal-roleo-pendiente').addEventListener('change', function() {
                    document.getElementById('swal-roleo-itinerario').style.display = this.checked ? 'none' : 'block';
                });
            },
            preConfirm: () => ({
                tipo: document.getElementById('swal-roleo-tipo').value,
                penalidad: document.getElementById('swal-roleo-penalidad').value,
                cantidad: document.getElementById('swal-roleo-cant').value || 1,
                comentario: document.getElementById('swal-roleo-comentario').value,
                pendiente: document.getElementById('swal-roleo-pendiente').checked,
                nave: document.getElementById('swal-roleo-nave').value,
                etd: document.getElementById('swal-roleo-etd').value,
                eta: document.getElementById('swal-roleo-eta').value,
                stacking: document.getElementById('swal-roleo-stacking').value,
                cierre: document.getElementById('swal-roleo-cierre').value
            })
        }).then((result) => {
            if (!result.isConfirmed) return;
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('idOperacion', idOp);
            formData.append('tipo', result.value.tipo);
            formData.append('penalidadOverride', result.value.penalidad);
            formData.append('cantidadContenedoresAfectados', result.value.cantidad);
            formData.append('comentario', result.value.comentario);
            formData.append('datosNuevosPendientes', result.value.pendiente);
            if (!result.value.pendiente) {
                formData.append('nuevaNave', result.value.nave);
                formData.append('nuevoEtd', result.value.etd);
                formData.append('nuevoEta', result.value.eta);
                formData.append('nuevoStacking', result.value.stacking);
                formData.append('nuevoCierreDocumental', result.value.cierre);
            }

            fetch('?handler=RegistrarRoleoCancelacion', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
            .then(res => res.json()).then(data => {
                if (data.success) {
                    Swal.fire({ icon: 'success', title: 'Registrado', text: data.penalidadSugerida !== undefined ? `Sistema sugirió: ${data.penalidadSugerida ? 'CON' : 'SIN'} penalidad (${data.dias ?? '-'} días de anticipación). Quedó confirmado: ${data.penalidadConfirmada}.` : '', toast: false, timer: 3500, showConfirmButton: false });
                    setTimeout(() => window.location.reload(), 1200);
                } else {
                    Swal.fire('Atención', data.message || 'No se pudo registrar.', 'warning');
                }
            }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
        });
    };

    window.confirmarItinerarioRoleo = function(idRegistro) {
        Swal.fire({
            title: 'Confirmar Nuevo Itinerario',
            html: `
                <div class="row g-2" style="text-align:left;">
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nueva Nave</label><input type="text" id="swal-conf-nave" class="swal2-input" style="margin:0; width:100%;" /></div>
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo ETD</label><input type="datetime-local" id="swal-conf-etd" class="swal2-input" style="margin:0; width:100%;" /></div>
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo ETA</label><input type="datetime-local" id="swal-conf-eta" class="swal2-input" style="margin:0; width:100%;" /></div>
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo Stacking</label><input type="datetime-local" id="swal-conf-stacking" class="swal2-input" style="margin:0; width:100%;" /></div>
                    <div class="col-6"><label style="font-size:9px; font-weight:800; color:#94a3b8;">Nuevo Cierre Documental</label><input type="datetime-local" id="swal-conf-cierre" class="swal2-input" style="margin:0; width:100%;" /></div>
                </div>`,
            showCancelButton: true, confirmButtonText: 'Confirmar', confirmButtonColor: '#0f172a', target: document.body,
            preConfirm: () => ({
                nave: document.getElementById('swal-conf-nave').value,
                etd: document.getElementById('swal-conf-etd').value,
                eta: document.getElementById('swal-conf-eta').value,
                stacking: document.getElementById('swal-conf-stacking').value,
                cierre: document.getElementById('swal-conf-cierre').value
            })
        }).then((result) => {
            if (!result.isConfirmed) return;
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('idRegistro', idRegistro);
            formData.append('nuevaNave', result.value.nave);
            formData.append('nuevoEtd', result.value.etd);
            formData.append('nuevoEta', result.value.eta);
            formData.append('nuevoStacking', result.value.stacking);
            formData.append('nuevoCierreDocumental', result.value.cierre);

            fetch('?handler=ConfirmarItinerarioRoleo', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
            .then(res => res.json()).then(data => {
                if (data.success) { Swal.fire({ icon: 'success', title: 'Itinerario confirmado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false }); setTimeout(() => window.location.reload(), 800); }
                else Swal.fire('Error', data.message || 'No se pudo confirmar.', 'error');
            }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
        });
    };

    window.actualizarPenalidadRoleo = function(idOp) {
        Swal.fire({
            title: 'Confirmar Penalidad',
            input: 'select',
            inputOptions: { SI: 'Sí, genera penalidad', NO: 'No genera penalidad' },
            inputPlaceholder: '-- Seleccionar --',
            showCancelButton: true,
            confirmButtonText: 'Actualizar',
            confirmButtonColor: '#0f172a',
            cancelButtonText: 'Cancelar',
            target: document.body,
            inputValidator: (value) => { if (!value) return 'Selecciona una opción.'; }
        }).then((result) => {
            if (!result.isConfirmed || !result.value) return;
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                        const formData = new FormData();
            formData.append('idRegistro', idOp);
            formData.append('penalidad', result.value);

            fetch('?handler=ActualizarPenalidadRoleo', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
            .then(res => res.json()).then(data => {
                if (data.success) {
                    Swal.fire({ icon: 'success', title: 'Actualizado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });
                    setTimeout(() => window.location.reload(), 800);
                } else {
                    Swal.fire('Atención', data.message || 'No se pudo actualizar.', 'warning');
                }
            }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
        });
    };

    // ==========================================
    // 11. EXTRACOSTOS Y FINANZAS
    // ==========================================
        // (movido a wwwroot/js/operaciones-common.js: window.configurarCamposDinamicosExtracosto — auditoría oct-2026, dedup JS)
        window.filtrarTarifasMaritimoPorNaviera = function(idNaviera) {
        const optgroup = document.getElementById('optgroupCostosMaritimos');
        if (!optgroup) return;
        optgroup.querySelectorAll('option').forEach(opt => {
            const navOpt = opt.getAttribute('data-naviera');
            // Opciones sin data-naviera (genéricas: LAR, ELAR, Demurrage, etc.) siempre visibles.
            // Opciones con data-naviera solo se muestran si coinciden con la naviera del booking.
            if (!navOpt || navOpt === '') { opt.style.display = ''; return; }
            opt.style.display = (idNaviera && navOpt === idNaviera.toString()) ? '' : 'none';
        });
    };

        // (movido a wwwroot/js/operaciones-common.js: window.actualizarGrupoExtracosto — auditoría oct-2026, dedup JS)
            window.abrirModalExtracostos = function(idOp, booking, event, idNaviera) {
        if (event) { event.stopPropagation(); event.preventDefault(); }
        document.getElementById('extracostoOpId').value = idOp; document.getElementById('extracostoBookingTitle').innerText = booking; document.getElementById('extracostoTipoSelect').value = ''; document.getElementById('extracostoJustificacionTxt').value = '';
        document.getElementById('extracostoGrupoOperativoFinal').value = '';
        window.filtrarTarifasMaritimoPorNaviera(idNaviera);
        configurarCamposDinamicosExtracosto();
        document.getElementById('modalAddCostoGlobal').querySelector('.anti-forgery-token-input').value = document.querySelector('input[name="__RequestVerificationToken"]').value;

        let megaModalEl = document.getElementById('megaModal');
        if (megaModalEl && megaModalEl.classList.contains('show')) {
            let megaModalObj = bootstrap.Modal.getInstance(megaModalEl);
            if (megaModalObj) megaModalObj.hide();
        }

        setTimeout(() => {
            new bootstrap.Modal(document.getElementById('modalAddCostoGlobal')).show();
        }, 400);
    };

    window.abrirModalExtracostosMatriz = function(idOp, booking, tipoDefecto, event) {
        if (event) { event.stopPropagation(); event.preventDefault(); }
        document.getElementById('extracostoOpId').value = idOp; 
        document.getElementById('extracostoBookingTitle').innerText = booking;
        
        // Vaciamos el select para que Operaciones elija la tarifa exacta de la BD
        let selectTipo = document.getElementById('extracostoTipoSelect'); 
        if (selectTipo) selectTipo.value = '';
        
        let textoMotivo = "";
        switch (tipoDefecto) {
            case 'Matriz fuera de plazo': textoMotivo = "Ingreso de Matriz posterior al horario de Cut-Off establecido por naviera."; break;
            case 'Correccion de Bl': textoMotivo = "Modificación de datos posterior a la emisión del draft."; break;
            case 'Scanner': textoMotivo = "Unidad seleccionada para revisión de escáner por aduana."; break;
            case 'Costo de Terminal Aforo fisico o retiro contenedor': textoMotivo = "Unidad seleccionada para revisión de aforo físico por aduana."; break;
            case 'Gestion Providencia Aduanera': textoMotivo = "Costo asociado a tramitación de providencia aduanera."; break;
            case 'Gestion retiro sello naviera': textoMotivo = "Cobro logístico por gestión de retiro de nuevo sello en oficinas de la naviera."; break;
            case 'LAR/ELAR': textoMotivo = "Ingreso a puerto fuera de plazo normal, con recargo LAR."; break;
            case 'Almacenamiento por aforo u otro motivo': textoMotivo = "Costo de almacenamiento en depósito extra-portuario por aforo."; break;
            default: textoMotivo = ""; break;
        }
        document.getElementById('extracostoJustificacionTxt').value = textoMotivo; 
        configurarCamposDinamicosExtracosto(); 
        document.getElementById('modalAddCostoGlobal').querySelector('.anti-forgery-token-input').value = document.querySelector('input[name="__RequestVerificationToken"]').value;
        new bootstrap.Modal(document.getElementById('modalAddCostoGlobal')).show();
    };

    // (movido a wwwroot/js/operaciones-common.js: window.guardarExtracostoAjax — auditoría oct-2026, dedup JS)

    window.actualizarResponsablePago = function(idCosto, responsable, checkbox) {
        if (!checkbox.checked) return;
        let otherResp = responsable === 'Atilson' ? 'Cliente' : 'Atilson'; let otherCheckbox = document.getElementById(`chk${otherResp}-${idCosto}`);
        if (otherCheckbox) { otherCheckbox.checked = false; }
        let token = document.querySelector('input[name="__RequestVerificationToken"]').value; let formData = new FormData(); formData.append('idCosto', idCosto); formData.append('responsable', responsable);

        fetch('?handler=UpdateResponsableCosto', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData }).then(res => res.json()).then(data => {
            if(data.success) {
                Swal.fire({ icon: 'success', title: 'Responsabilidad Actualizada', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });
                checkbox.disabled = true; checkbox.nextElementSibling.style.pointerEvents = 'none';
                if (otherCheckbox) { otherCheckbox.closest('.form-check').remove(); }
            } else { Swal.fire({ icon: 'error', title: 'Error', text: 'No se pudo actualizar.', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false }); checkbox.checked = false; }
        }).catch(err => { console.error(err); checkbox.checked = false; });
    };

    // ==========================================
    // 12. CORREOS Y NOTIFICACIONES
    // ==========================================
    window.abrirGeneradorCorreos = function(idOp, tipo) {
        const ctx = modalCtx(); if (!ctx) return;
        const idOperacionReal = ctx.querySelector('input[name="IdOperacion"]')?.value || idOp.toString().replace('sub-', '');
                let formTerrestre = ctx.querySelector(`#ter-form-${idOp}`) || ctx.querySelector(`form[action*="QuickEditTransporte"]`);
        let formGeneral   = ctx.querySelector(`#mar-form-${idOp}`)  || ctx.querySelector(`#mar-form-sub-${idOperacionReal}`) || ctx.querySelector(`form[action*="QuickEditMaritimo"]`);
        let formDoc       = ctx.querySelector(`#doc-global-form-${idOp}`) || ctx.querySelector(`#doc-global-form-sub-${idOperacionReal}`);
        if (!formTerrestre) { formTerrestre = ctx; }

        let btnGenerado = document.getElementById('btnEnviarCorreoGenerado'); if(btnGenerado) btnGenerado.removeAttribute('data-html-body');

        function obtenerValor(nombre, claseVista) {
            let val = "";
            if (nombre) { let sel = formTerrestre.querySelector(`select[name="${nombre}"]`); if (sel && sel.value && sel.value.trim() !== "") val = sel.value; if (!val) { let inp = formTerrestre.querySelector(`input[name="${nombre}"]`); if (inp && inp.value && inp.value.trim() !== "") val = inp.value; } }
            if (!val && claseVista) { let vista = formTerrestre.querySelector(`.${claseVista}`); if (vista && vista.value && vista.value.trim() !== "-" && vista.value.trim() !== "") val = vista.value; }
            return val ? val.trim() : null;
        }

        let empresa    = obtenerValor("EmpresaTransporte") || "[EMPRESA]";
        let correoProv = obtenerValor("CorreoTransporte") || "";
        let conductor  = obtenerValor("NombreConductor") || "[CONDUCTOR]";
        let rutCond    = obtenerValor("RutConductor") || "[RUT]";
        let telefono   = obtenerValor("TelefonoConductor") || "[TELEFONO]";
        let patente    = obtenerValor("Patente") || "[PATENTE]";
        let planta     = obtenerValor("PlantaCarga", "view-only-planta") || "[PLANTA]";
        let deposito   = obtenerValor("DepositoRetiro", "view-only-depositoretiro") || "[DEPÓSITO]";
        let terminal   = obtenerValor(null, "view-only-terminalterrestre") || "[TERMINAL]"; 
        let puerto     = obtenerValor("PuertoEntrega", "view-only-puertoentrega") || "[PUERTO]"; 
        
        let inputFecha = formTerrestre.querySelector('input[name="FechaCarga"]'); let fecha = (inputFecha && inputFecha.value) ? inputFecha.value.replace("T", " ") : "[FECHA]";
        let bookingEl  = ctx.querySelector('.atilson-banner .fw-bold'); let booking = bookingEl ? bookingEl.innerText.replace("N° BOOKING:", "").replace("BOOKING:", "").trim() : "[BOOKING]";
        
        let chkInter = formTerrestre.querySelector('input[name="AplicaInterplanta"]'); let aplicaInterplanta = chkInter ? chkInter.checked : false;
        let planta2 = formTerrestre.querySelector('select[name="PlantaCarga2"]')?.value || ""; let linkMaps2 = formTerrestre.querySelector('input[name="LinkMaps2"]')?.value || "";
        let planta3 = formTerrestre.querySelector('select[name="PlantaCarga3"]')?.value || ""; let linkMaps3 = formTerrestre.querySelector('input[name="LinkMaps3"]')?.value || "";

        let interplantaMsg = "";
        if(aplicaInterplanta) {
            if(planta2) interplantaMsg += `\nSEGUNDA PLANTA DE CARGA: ${planta2}\nENLACE MAPS: ${linkMaps2 || 'Pendiente'}\n`;
            if(planta3) interplantaMsg += `\nTERCERA PLANTA DE CARGA: ${planta3}\nENLACE MAPS: ${linkMaps3 || 'Pendiente'}\n`;
        }

        let clienteAsociado = ""; let clienteDiv = Array.from(ctx.querySelectorAll('.banner-label')).find(el => el.textContent.includes('Cliente'))?.nextElementSibling;
        if(clienteDiv) clienteAsociado = clienteDiv.innerText.trim(); if(!clienteAsociado) clienteAsociado = "[CLIENTE]";

        let naveStr = ""; let navieraStr = "";
        if(formGeneral) { let inpNave = formGeneral.querySelector('input[name="Nave"]'); if(inpNave) naveStr = inpNave.value; let selNav = formGeneral.querySelector('select[name="IdNaviera"]'); if(selNav && selNav.options.length > 0) navieraStr = selNav.options[selNav.selectedIndex].text; }
        if(!naveStr) naveStr = "[NAVE]"; if(!navieraStr) navieraStr = "[NAVIERA]";

        let tipoCont = "[TIPO CONTENEDOR]"; let selCont = formGeneral ? formGeneral.querySelector('select[name="TipoContenedor"]') : null; if(selCont) tipoCont = selCont.value;

        let cutOffDate = formGeneral?.querySelector('input[name="CutOffMatriz"]')?.value?.replace("T", " ") || "[CORTE DOCUMENTAL]";
        let stackingDate = formGeneral?.querySelector('input[name="FechaStacking"]')?.value?.replace("T", " ") || "[STACKING]";
        let lateArrivalDate = formGeneral?.querySelector('input[name="LateArrival"]')?.value?.replace("T", " ") || "[LATE ARRIVAL]";
        let tempReefer = formGeneral?.querySelector('input[name="Temperatura"]')?.value; let tempStr = tempReefer ? tempReefer + "°C" : "N/A (Dry)";

        let correoCliente = ctx.querySelector('input[name="CorreosCliente"]')?.value || "";
        let to = "", cc = "", asunto = "", cuerpo = "";

        if (tipo === 'solicitud') {
            document.getElementById('tipoCorreoTxt').innerText = "Enviando Solicitud al Proveedor";
            to = correoProv !== "" ? correoProv : "proveedor@transporte.cl"; 
            cc = "usuario@atilson.cl; finanzas@atilson.cl";
            asunto = `SOLICITUD DE SERVICIO TRANSPORTE // BKG: ${booking}`;
            cuerpo = `Estimado equipo de ${empresa}, buenos días:\n\nFavor confirmar servicio de transporte para la siguiente carga:\n\nBooking: N° ${booking}\nDepósito Retiro Vacío: ${deposito}\nFecha de Presentación: ${fecha} Hrs.\nPlanta: ${planta}${interplantaMsg}\nPuerto: ${puerto} - ${terminal}\n\nQuedamos atentos a su confirmación y datos del conductor.\n\nSaludos Cordiales,\nOperaciones Atilson Cargo`;
            
        } else if (tipo === 'preliminar') {
            document.getElementById('tipoCorreoTxt').innerText = "Enviando Asignación Preliminar al Cliente";
            to = correoCliente; 
            cc = "operaciones@atilson.cl";
            asunto = `ASIGNACIÓN PRELIMINAR // BKG: ${booking}`;
            cuerpo = `Estimado Cliente, buenos días:\n\nLe enviamos los datos del conductor asignado (Booking: N° ${booking}):\n\nConductor: ${conductor}\nRUT: ${rutCond}\nTeléfono: ${telefono}\nPatente: ${patente}\nFecha de Presentación: ${fecha}\nDepósito Retiro Vacío: ${deposito}\nPuerto: ${puerto} - ${terminal}\n\n*El N° de contenedor se enviará en la asignación completa.*\n\nSaludos Cordiales,\nOperaciones Atilson Cargo`;
            
        } else if (tipo === 'completa') {
            let contenedor = obtenerValor("NumeroContenedor", "") || (formGeneral && formGeneral.querySelector('input[name="NumeroContenedor"]')?.value) || "[S/N CONTENEDOR]";
            let sello      = obtenerValor("NumeroSello", "") || (formGeneral && formGeneral.querySelector('input[name="NumeroSello"]')?.value) || "[S/N SELLO]";

            if (contenedor === "[S/N CONTENEDOR]" || sello === "[S/N SELLO]") { Swal.fire('Datos Faltantes', 'Debes guardar primero el N° de Contenedor y Sello antes de generar el envío definitivo.', 'error'); return; }
            document.getElementById('tipoCorreoTxt').innerText = "Enviando Asignación Completa al Cliente";
            to = correoCliente; cc = "operaciones@atilson.cl";
            asunto = `ASIGNACIÓN COMPLETA // BKG: ${booking} // CONTENEDOR: ${contenedor}`;
            cuerpo = `Estimado Cliente, buenos días:\n\nLe enviamos la asignación completa para su embarque:\n\nBooking: N° ${booking}\nContenedor: ${contenedor}\nSello: ${sello}\n\nDatos de Transporte:\nConductor: ${conductor}\nRUT: ${rutCond}\nTeléfono: ${telefono}\nPatente: ${patente}\nFecha de Presentación: ${fecha}\nDepósito Retiro Vacío: ${deposito}\nPuerto: ${puerto} - ${terminal}\n\nSaludos Cordiales,\nOperaciones Atilson Cargo`;
            
        } else if (tipo === 'guiaAgencia' || tipo === 'instructivoAgencia' || tipo === 'bookingAgencia' || tipo === 'capturaAgencia' || tipo === 'librevenAgencia') {
            let destStembex = ""; let chkSerna = formDoc?.querySelector('input[name="docUpdates.AplicaSernapesca"]'); let isSernapesca = chkSerna ? chkSerna.checked : false;

            if(isSernapesca) { destStembex = "a.barrera@stembex.cl; javier.traverso@stembex.cl"; } else { destStembex = "agencia@aduanas.cl"; }

            let polUpper = puerto.toUpperCase();
            if(polUpper.includes("SAN ANTONIO")) { destStembex += (destStembex ? "; " : "") + "stembexsai@stembex.cl; manuel.vera@stembex.cl; francisco.roman@stembex.cl; renata.aguilar@stembex.cl"; } 
            else if(polUpper.includes("VALPARAISO") || polUpper.includes("VALPARAÍSO")) { destStembex += (destStembex ? "; " : "") + "Zeal@stembex.cl"; }
            
            to = destStembex; cc = "operaciones@atilson.cl";
            let nombreDocEmail = ""; let docParam = "";
            
            if (tipo === 'guiaAgencia') { nombreDocEmail = "Guía de Despacho"; docParam = "guia"; document.getElementById('tipoCorreoTxt').innerText = `Enviando Guía de Despacho a Agencia`; asunto = `GUÍA DE DESPACHO N°BOOKING ${booking} // ${naveStr} // ${tipoCont} // ${puerto} - [POD] // ${clienteAsociado} // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Buenas tardes, equipo Stembex Zeal\n\nSe adjunta Guía de Despacho.\nPor favor proceder con visado de unidad.\n\nGracias!`; }
            else if (tipo === 'instructivoAgencia') { nombreDocEmail = "Instructivo de Embarque"; docParam = "instructivo"; document.getElementById('tipoCorreoTxt').innerText = `Enviando Instructivo a Agencia`; let numInst = formDoc?.querySelector('input[name="docUpdates.InstructivoCliente"]')?.value || "XXXXX"; asunto = `INSTRUCTIVO N°${numInst} // ${naveStr} // ${booking} // ${clienteAsociado} // ${puerto} - [POD] // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Buenas tardes,\n\nSe adjunta instructivo de embarque en mención del asunto para proceder con la coordinación del respectivo proceso documental del embarque.\n\nINST: ${numInst}`; }
            else if (tipo === 'bookingAgencia') { nombreDocEmail = "Booking Atilson"; docParam = "booking"; document.getElementById('tipoCorreoTxt').innerText = `Enviando Booking a Agencia`; asunto = `CONFIRMACIÓN N°BOOKING ${booking} // ${naveStr} // ${tipoCont} // ${puerto} - [POD] // ${clienteAsociado} // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Estimado Cliente, junto con saludar se adjunta confirmación de embarque, notar detalle:\n\nBOOKING: ${booking}\nNAVIERA: ${navieraStr}\nNAVE: ${naveStr}\nTIPO CONTENEDOR: ${tipoCont}\nTEMP: ${tempStr}\nPOL: ${puerto}\nPOD: [POD]\nDEPÓSITO RETIRO VACÍO: ${deposito}\nTERMINAL: ${terminal}\n\nSTACKING: ${stackingDate}\nCORTE DOCUMENTAL: ${cutOffDate}\nLATE ARRIVAL: ${lateArrivalDate}`; }
            else if (tipo === 'capturaAgencia') { nombreDocEmail = "Captura AGA"; docParam = "capturaAga"; document.getElementById('tipoCorreoTxt').innerText = `Enviando Captura AGA a Agencia`; asunto = `CAPTURA AGA N°${booking} // ${naveStr} // ${tipoCont} // ${puerto} - [POD] // ${clienteAsociado} // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Buenas tardes,\n\nSe adjunta captura AGA en mención del asunto para proceder con la coordinación del respectivo proceso documental del embarque.\n\nCAPTURA: ${booking}`; }
            else if (tipo === 'librevenAgencia') { nombreDocEmail = "Certificado de Libre Venta"; docParam = "libreven"; document.getElementById('tipoCorreoTxt').innerText = `Enviando Libre Venta a Agencia`; asunto = `LIBRE VENTA N°BOOKING ${booking} // ${naveStr} // ${tipoCont} // ${puerto} - [POD] // ${clienteAsociado} // ${navieraStr} // CARGOFLOW DEMO SPA`; cuerpo = `Buenas tardes, equipo Stembex Zeal\n\nSe adjunta Certificado de Libre Venta.\nPor favor proceder con su revisión e ingreso.\n\nGracias!`; }

            const baseOrigin = window.location.origin;
            const linkProceso = `${baseOrigin}/Operaciones/Index?handler=RespuestaAgencia&id=${idOperacionReal}&estado=proceso&tipoDoc=${docParam}`;
            const linkVisado  = `${baseOrigin}/Operaciones/Index?handler=RespuestaAgencia&id=${idOperacionReal}&estado=visado&tipoDoc=${docParam}`;

            let botonesHTML = '';
            if (docParam === 'guia') {
                botonesHTML = `<a href="${linkProceso}" style="background:#0ea5e9;color:#fff;padding:14px 22px;text-decoration:none;border-radius:6px;font-weight:800;font-size:13px;display:inline-block;letter-spacing:0.3px;">📋 EN PROCESO DE VISACIÓN</a><a href="${linkVisado}" style="background:#16a34a;color:#fff;padding:14px 22px;text-decoration:none;border-radius:6px;font-weight:800;font-size:13px;display:inline-block;letter-spacing:0.3px;">✅ DOCUMENTO VISADO</a>`;
            } else {
                botonesHTML = `<a href="${linkProceso}" style="background:#0ea5e9;color:#fff;padding:14px 22px;text-decoration:none;border-radius:6px;font-weight:800;font-size:13px;display:inline-block;letter-spacing:0.3px;">📋 CONFIRMAR RECEPCIÓN / EN PROCESO</a>`;
            }

            let tituloEmail = docParam === 'guia' ? 'Solicitud de Visación' : 'Envío de Documento';
            let htmlCuerpo = `
            <div style="font-family:Arial,sans-serif;max-width:580px;margin:0 auto;">
              <div style="background:linear-gradient(135deg,#0f172a 0%,#6d2d9e 100%);padding:24px 28px;border-radius:8px 8px 0 0;">
                <h2 style="color:#fff;margin:0;font-size:18px;">${nombreDocEmail} — ${tituloEmail}</h2>
                <p style="color:#cbd5e1;margin:6px 0 0;font-size:13px;">Booking: <strong style="color:#fff;">${booking}</strong></p>
              </div>
              <div style="background:#fff;padding:28px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;">
                <p style="font-size:14px;color:#475569;line-height:1.6; white-space: pre-wrap;">${cuerpo}</p>
                <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:20px;margin:24px 0;">
                  <p style="font-size:13px;font-weight:700;color:#0f172a;text-transform:uppercase;letter-spacing:0.5px;margin:0 0 14px;">Por favor, indique el estado haciendo clic en el botón correspondiente. Esto notificará automáticamente a nuestros operadores:</p>
                  <div style="display:flex;gap:12px;flex-wrap:wrap;">${botonesHTML}</div>
                </div>
                <p style="font-size:12px;color:#94a3b8;margin:0;">Ante cualquier consulta responda este correo. Quedamos atentos.</p>
                <hr style="border:0;border-top:1px solid #e2e8f0;margin:24px 0;">
                <p style="font-size:11px;color:#94a3b8;text-align:center;margin:0;">Atilson Cargo SpA · Operaciones Logísticas</p>
              </div>
            </div>`;
            
            let btnEnviar2 = document.getElementById('btnEnviarCorreoGenerado'); if (btnEnviar2) btnEnviar2.setAttribute('data-html-body', htmlCuerpo);
            
                    } else if (tipo === 'almacenIngreso' || tipo === 'almacenRetiro') {
            const esIngresoAlmacen = tipo === 'almacenIngreso';
            document.getElementById('tipoCorreoTxt').innerText = esIngresoAlmacen ? "Enviando Aviso de Ingreso a Patio" : "Enviando Aviso de Retiro de Patio";

            const almacenForm = ctx.querySelector(`#almacen-form-${idOp}`);
            let selProv = almacenForm?.querySelector(esIngresoAlmacen ? 'select[name="IdProveedor"]' : 'select[name="IdProveedorRetiro"]');
            let proveedorNombreAlmacen = (selProv && selProv.selectedIndex > 0) ? selProv.options[selProv.selectedIndex].text : '[DEPÓSITO]';

            let prefAlmacen = esIngresoAlmacen ? 'Ingreso' : 'Salida';
            let fechaAlmacen = almacenForm?.querySelector(`input[name="Fecha${prefAlmacen}"]`)?.value?.replace("T", " ") || "Pendiente de asignación";
            let conductorNAlmacen = almacenForm?.querySelector(`input[name="Conductor${prefAlmacen}Nombre"]`)?.value || "No asignado aún";
            let conductorRAlmacen = almacenForm?.querySelector(`input[name="Conductor${prefAlmacen}Rut"]`)?.value || "S/N";
            let conductorTAlmacen = almacenForm?.querySelector(`input[name="Conductor${prefAlmacen}Telefono"]`)?.value || "S/N";
            let patenteAlmacen = almacenForm?.querySelector(`input[name="Camion${prefAlmacen}Patente"]`)?.value || "S/N";
            let contenedorAlmacen = ctx.querySelector('input[name="NumeroContenedor"]')?.value || "Sin Asignar";

            to = ""; cc = "operaciones@atilson.cl";
            asunto = `Aviso de ${esIngresoAlmacen ? 'INGRESO' : 'RETIRO'} a Patio - Booking ${booking}`;
            cuerpo = `Estimados ${proveedorNombreAlmacen},\n\nJunto con saludar cordialmente, enviamos los datos del conductor y unidades para el proceso de ${esIngresoAlmacen ? 'INGRESO' : 'RETIRO'} de almacenamiento.\n\n-- DATOS DE OPERACIÓN --\nCliente: ${clienteAsociado}\nBooking / Reserva: ${booking}\nContenedor / Equipo: ${contenedorAlmacen}\n\n-- DATOS DEL CONDUCTOR Y TRANSPORTE --\nNombre Conductor: ${conductorNAlmacen}\nRUT Conductor: ${conductorRAlmacen}\nTeléfono de Contacto: ${conductorTAlmacen}\nPatente del Camión: ${patenteAlmacen}\n\nFecha y Hora Programada: ${fechaAlmacen}\n\nFavor gestionar recepción en sistema.\n\nSaludos cordiales.`;
        } else if (tipo === 'dhlCliente' || tipo === 'fullsetCliente') {
            let nombreDocEmail = tipo === 'dhlCliente' ? "DHL" : "Full Set";
            document.getElementById('tipoCorreoTxt').innerText = `Enviando ${nombreDocEmail} al Cliente`;
            to = correoCliente; cc = "operaciones@atilson.cl";
            asunto = `ENVÍO ${nombreDocEmail.toUpperCase()} // BKG: ${booking}`;
            cuerpo = `Estimado Cliente, buenos días:\n\nJunto con saludar, le hacemos entrega de los documentos correspondientes a su embarque (Booking: ${booking}).\n\nQuedamos atentos a cualquier consulta.\n\nSaludos Cordiales,\nOperaciones Atilson Cargo`;
        }

        let inputTo = document.getElementById('emailToPreview'); let inputCc = document.getElementById('emailCcPreview'); let inputAsunto = document.getElementById('emailAsuntoPreview'); let inputCuerpo = document.getElementById('emailCuerpoPreview');
        [inputTo, inputCc, inputAsunto, inputCuerpo].forEach(inp => { if (inp) { inp.removeAttribute('readonly'); inp.removeAttribute('disabled'); } });

        inputTo.value = to; inputCc.value = cc; inputAsunto.value = asunto; inputCuerpo.value = cuerpo;

        let btnEnviar = document.getElementById('btnEnviarCorreoGenerado');
        if (btnEnviar) { btnEnviar.setAttribute('data-id', idOperacionReal); btnEnviar.setAttribute('data-context', idOp); btnEnviar.setAttribute('data-tipo', tipo); }

        if (document.activeElement) document.activeElement.blur();

        let megaModalEl = document.getElementById('megaModal');
        if (megaModalEl && megaModalEl.classList.contains('show')) { let megaModalObj = bootstrap.Modal.getInstance(megaModalEl); if (megaModalObj) megaModalObj.hide(); }

        setTimeout(() => {
            let emailModalElement = document.getElementById('modalGeneradorCorreos');
            let emailModal = bootstrap.Modal.getInstance(emailModalElement) || new bootstrap.Modal(emailModalElement, { focus: false });
            emailModal.show();
        }, 400);
    };

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
                                    handlerGuardado = '?handler=QuickEditDocumental';
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

    // (movido a wwwroot/js/operaciones-common.js: window.archivarSolicitudCotizacion — auditoría oct-2026, dedup JS)

    window.enviarBorradorCliente = function(idOp, booking) {
        let correoPredefinido = document.querySelector('#asig-pre-' + idOp + ' input[name="CorreosCliente"]')?.value || document.querySelector('#asig-pre-sub-' + idOp.replace('sub-', '') + ' input[name="CorreosCliente"]')?.value || "";

        Swal.fire({
            title: `Enviar Borrador BL`, text: `Se enviará el borrador del Booking ${booking} al cliente para su revisión y aprobación.`, input: 'email', inputValue: correoPredefinido, inputPlaceholder: 'correo@cliente.com', showCancelButton: true, confirmButtonText: '<i class="bi bi-send-fill"></i> Enviar a Cliente', cancelButtonText: 'Cancelar', confirmButtonColor: '#6d2d9e', target: document.body
        }).then((result) => {
            if (result.isConfirmed && result.value) {
                let token = document.querySelector('input[name="__RequestVerificationToken"]').value; let formData = new FormData(); formData.append('idOperacion', idOp); formData.append('correoDestino', result.value);
                let fileInput = document.getElementById(`file-bl-${idOp}`) || modalCtx()?.querySelector(`#file-bl-${idOp}`);
                if (fileInput && fileInput.files.length > 0) { formData.append('ArchivoBL', fileInput.files[0]); }

                Swal.fire({ title: 'Enviando Borrador...', text: 'Por favor espere', allowOutsideClick: false, target: document.body, didOpen: () => { Swal.showLoading(); } });

                fetch('?handler=EnviarBorradorBL', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                .then(res => res.json()).then(data => {
                    if (data.success) {
                        Swal.fire({ title: '¡Enviado!', text: 'El borrador fue enviado exitosamente al cliente.', icon: 'success', target: document.body });
                        let stepper = document.getElementById(`stepper-matriz-${idOp}`) || modalCtx()?.querySelector(`#stepper-matriz-${idOp}`);
                        if(stepper) { let steps = stepper.querySelectorAll('.step-item'); if(steps.length > 1) { steps[1].classList.add('completed'); steps[1].querySelector('.step-circle').innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; } }
                        if(typeof marcarEstadoRapidoVisual === 'function') { marcarEstadoRapidoVisual('matriz', idOp, 'BORRADOR ENVIADO', '#0ea5e9', `Se envió el borrador B/L para revisión a: <strong>${escapeHtmlAtilson(result.value)}</strong>`); }
                    } else { Swal.fire({ title: 'Error', text: data.message, icon: 'error', target: document.body }); }
                }).catch(() => Swal.fire({ title: 'Error', text: 'Fallo de conexión.', icon: 'error', target: document.body }));
            }
        });
    };

    window.enviarCertificadoCliente = function(idOp, tipo, booking) {
        let nombreCert = tipo === 'san' ? 'Sanitario' : 'de Captura';
        let correoPredefinido = document.querySelector('#asig-pre-' + idOp + ' input[name="CorreosCliente"]')?.value || document.querySelector('#asig-pre-sub-' + idOp.replace('sub-', '') + ' input[name="CorreosCliente"]')?.value || "";
        
        Swal.fire({
            title: `Enviar Certificado ${nombreCert}`, text: `Se enviará el certificado del Booking ${booking} al cliente para su revisión y aprobación.`, input: 'email', inputValue: correoPredefinido, inputPlaceholder: 'correo@cliente.com', showCancelButton: true, confirmButtonText: '<i class="bi bi-send-fill"></i> Enviar a Cliente', cancelButtonText: 'Cancelar', confirmButtonColor: '#6d2d9e', target: document.body
        }).then((result) => {
            if (result.isConfirmed && result.value) {
                let token = document.querySelector('input[name="__RequestVerificationToken"]').value; let formData = new FormData(); formData.append('idOperacion', idOp); formData.append('tipo', tipo); formData.append('correoDestino', result.value);

                Swal.fire({ title: 'Enviando...', text: 'Procesando notificación y guardando en bitácora...', allowOutsideClick: false, target: document.body, didOpen: () => { Swal.showLoading(); } });

                fetch('?handler=EnviarCertificadoCliente', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                .then(res => res.json()).then(data => {
                    if (data.success) {
                        Swal.fire({ title: '¡Enviado!', text: 'El certificado fue enviado exitosamente.', icon: 'success', target: document.body });

                        let stepper = document.getElementById(`stepper-${tipo}-${idOp}`) || modalCtx()?.querySelector(`#stepper-${tipo}-${idOp}`);
                        if(stepper) { let steps = stepper.querySelectorAll('.step-item, .pro-step'); if(steps.length > 0) { steps[0].classList.remove('rejected'); steps[0].classList.add('completed'); let circle = steps[0].querySelector('.step-circle, .pro-step-icon'); if(circle) circle.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; } }

                        let btnEnviarCli = document.getElementById(`btn-enviar-cli-${tipo}-${idOp}`) || modalCtx()?.querySelector(`#btn-enviar-cli-${tipo}-${idOp}`);
                        if (btnEnviarCli) btnEnviarCli.style.setProperty('display', 'inline-flex', 'important');

                        let logEl = modalCtx()?.querySelector(`#log-${tipo}-txt-${idOp}`);
                        if (logEl) {
                            if(logEl.innerHTML.includes('Sin registros')) logEl.innerHTML = '';
                            const ahora = new Date(); const fecha = ahora.toLocaleDateString('es-CL'); const hora = ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' });
                            const div = document.createElement('div');
                            div.style.cssText = 'border-left:2px solid #0ea5e9; padding:6px; margin-bottom:6px; background:#f0f9ff; border-top:1px solid #bae6fd; border-right:1px solid #bae6fd; border-bottom:1px solid #bae6fd;';
                            div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:#0284c7; text-transform:uppercase; font-size:10px;">ENVÍO A CLIENTE</span><span style="color:#94a3b8; font-size:9px;">${fecha} ${hora}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>Operaciones Atilson</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">Certificado enviado para revisión a: <strong>${escapeHtmlAtilson(result.value)}</strong></div><span class="badge mt-2 d-inline-block" style="font-size:8px; background:#e0f2fe; color:#0284c7; border:1px solid #bae6fd;">GUARDADO EN BD</span>`;
                            logEl.insertBefore(div, logEl.firstChild);
                        }
                    } else { Swal.fire({ title: 'Error', text: data.message, icon: 'error', target: document.body }); }
                }).catch(() => Swal.fire({ title: 'Error', text: 'Fallo de conexión.', icon: 'error', target: document.body }));
            }
        });
    };

    window.enviarSanitarioAgencia = function(idOp, booking) {
        const correoPredefinido = "a.barrera@stembex.cl; javier.traverso@stembex.cl";
        Swal.fire({
            title: 'Enviar Certificado Sanitario a Aga-Sern',
            html: `<div style="text-align:left; font-size:13px;"><p style="color:#64748b; margin:0 0 14px;"><strong style="color:#0f172a;">BKG: ${booking}</strong> — Se enviará el certificado sanitario a la agencia Sernapesca.</p><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Destinatario (Aga-Sern)</label><input type="text" id="swal-san-email" class="swal2-input" style="margin:0 0 12px; width:100%; font-size:12px;" value="${correoPredefinido}" /><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Asunto</label><input type="text" id="swal-san-asunto" class="swal2-input" style="margin:0 0 12px; width:100%; font-weight:700;" value="[ATILSON CARGO] Certificado Sanitario — BKG: ${booking}" /><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Mensaje adicional (opcional)</label><textarea id="swal-san-msg" class="swal2-textarea" style="margin:0; width:100%; font-size:12px; height:70px;" placeholder="Ej: Adjuntamos certificado sanitario para su revisión..."></textarea></div>`,
            showCancelButton: true, confirmButtonText: '<i class="bi bi-send-fill"></i> Enviar a Aga-Sern', cancelButtonText: 'Cancelar', confirmButtonColor: '#0284c7', cancelButtonColor: '#64748b', width: 500, target: document.body,
            preConfirm: () => {
                const email = document.getElementById('swal-san-email').value.trim();
                if (!email) { Swal.showValidationMessage('Ingresa un destinatario.'); return false; }
                return { email, asunto: document.getElementById('swal-san-asunto').value, mensaje: document.getElementById('swal-san-msg').value };
            }
        }).then((result) => {
            if (!result.isConfirmed || !result.value) return;
            const idOperacionReal = idOp.toString().replace('sub-', '').replace('v3-', '');
            const baseUrl = `${window.location.protocol}//${window.location.host}`;
            const urlRecibido = `${baseUrl}/Operaciones/Index?handler=RespuestaSanitarioAgencia&id=${idOperacionReal}`;

            const htmlCuerpo = `<div style="font-family:Arial,sans-serif;max-width:580px;margin:0 auto;"><div style="background:linear-gradient(135deg,#0f172a 0%,#0ea5e9 100%);padding:24px 28px;border-radius:8px 8px 0 0;"><h2 style="color:#fff;margin:0;font-size:18px;">Envío Certificado Sanitario</h2><p style="color:#bae6fd;margin:6px 0 0;font-size:13px;">Booking: <strong style="color:#fff;">${booking}</strong></p></div><div style="background:#fff;padding:28px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;"><p style="font-size:14px;color:#475569;line-height:1.6;white-space:pre-wrap;">${result.value.mensaje || 'Junto con saludar, adjuntamos el Certificado Sanitario para su trámite ante Sernapesca.'}</p><div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:20px;margin:24px 0;"><p style="font-size:13px;font-weight:700;color:#0f172a;text-transform:uppercase;margin:0 0 14px;">Por favor confirme la recepción de este documento:</p><a href="${urlRecibido}" style="background:#0ea5e9;color:#fff;padding:14px 22px;text-decoration:none;border-radius:6px;font-weight:800;font-size:13px;display:inline-block;">📋 CONFIRMAR RECEPCIÓN</a></div><p style="font-size:11px;color:#94a3b8;text-align:center;margin:0;">Atilson Cargo SpA · Operaciones Logísticas</p></div></div>`;

            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData(); formData.append('id', idOperacionReal); formData.append('correoDestino', result.value.email); formData.append('copiaCc', 'operaciones@atilson.cl'); formData.append('asunto', result.value.asunto); formData.append('cuerpoHtml', htmlCuerpo); formData.append('tipoCorreo', 'sanitarioAgencia');

            Swal.fire({ title: 'Enviando...', allowOutsideClick: false, target: document.body, didOpen: () => Swal.showLoading() });

            fetch('?handler=EnviarCorreoTransporte', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
            .then(r => r.json()).then(data => {
                if (data.success) {
                    const ctx = modalCtx() || document; const stepAga = ctx.querySelector(`#step-aga-san-${idOp}`);
                    if (stepAga) { stepAga.classList.add('completed'); const circle = stepAga.querySelector('.step-circle, .pro-step-icon'); if (circle) circle.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; }
                    marcarEstadoRapidoVisual('san', idOp, 'ENVIADO A AGA-SERN', '#0ea5e9', `Certificado Sanitario enviado a: <strong>${escapeHtmlAtilson(result.value.email)}</strong>`);

                    const formContainer = ctx.querySelector(`#doc-global-form-${idOp}`) || ctx.querySelector(`#doc-base-form-${idOp}`);
                    const formEl = formContainer ? formContainer.closest('form') : null;
                    if (formEl) {
                        if (typeof window.sincronizarLogsAntesDeGuardar === 'function') { window.sincronizarLogsAntesDeGuardar(formEl); }
                        const formDataSave = new FormData(formEl); fetch('/Operaciones/Index?handler=QuickEditDocumental', { method: 'POST', body: formDataSave, headers: { 'X-Requested-With': 'XMLHttpRequest' } }).catch(e => console.warn("Aviso auto-guardado Sernapesca:", e));
                    }
                    Swal.fire({ icon: 'success', title: '¡Enviado!', text: `Correo enviado a ${result.value.email}`, confirmButtonColor: '#10b981', target: document.body });
                } else { Swal.fire({ icon: 'error', title: 'Error', text: data.message, target: document.body }); }
            }).catch(err => Swal.fire({ icon: 'error', title: 'Error', text: err.message, target: document.body }));
        });
    };

    window.abrirModalEnvioNaviera = function(idOp, booking, naviera) {
        const navieraUpper = (naviera || '').toUpperCase();
        const navieras = [
            { nombre: 'Wan Hai',  email: 'jcovarrubia@navepac.com,docexpo@navepac.com',         detectar: ['WAN HAI', 'WAN-HAI'] },
            { nombre: 'Yang Ming',  email: 'digcentral@agunsa.cl,luis.sotoc@agunsa.com',      detectar: ['YANG MING', 'YM']    },
        ];

        let emailAutodetectado = ''; navieras.forEach(n => { if (n.detectar.some(k => navieraUpper.includes(k))) emailAutodetectado = n.email; });
        const opcionesHtml = navieras.map(n => `<option value="${n.email}" ${emailAutodetectado === n.email ? 'selected' : ''}>${n.nombre} — ${n.email}</option>`).join('') + `<option value="custom">Otro destinatario...</option>`;

        Swal.fire({
            title: 'Enviar evidencia a naviera',
            html: `<div style="text-align:left; font-size:13px;"><p style="color:#64748b; margin:0 0 14px;"><strong style="color:#0f172a;">BKG: ${booking}</strong> — se enviará el archivo de evidencia adjunto.</p><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; letter-spacing:0.5px; display:block; margin-bottom:5px;">Destinatario</label><select id="swal-naviera-select" class="swal2-input" style="margin:0 0 12px; width:100%; font-size:12px; font-weight:700;" onchange="onNavieraSelectChange(this)">${opcionesHtml}</select><div id="swal-custom-email-wrapper" style="display:none;"><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Correo personalizado</label><input type="email" id="swal-custom-email" class="swal2-input" style="margin:0 0 12px; width:100%;" placeholder="correo@naviera.com" /></div><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; letter-spacing:0.5px; display:block; margin-bottom:5px;">Asunto</label><input type="text" id="swal-asunto-naviera" class="swal2-input" style="margin:0 0 12px; width:100%; font-weight:700;" value="[ATILSON CARGO] Evidencia Envío Documentos — BKG: ${booking}" /><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; letter-spacing:0.5px; display:block; margin-bottom:5px;">Mensaje adicional (opcional)</label><textarea id="swal-msg-naviera" class="swal2-textarea" style="margin:0; width:100%; font-size:12px; height:80px;" placeholder="Ej: Adjuntamos evidencia de envío..."></textarea></div>`,
            showCancelButton: true, confirmButtonText: '<i class="bi bi-send-fill"></i> Enviar correo', cancelButtonText: 'Cancelar', confirmButtonColor: '#0f172a', cancelButtonColor: '#64748b', width: 500, target: document.body,
            preConfirm: () => {
                const select = document.getElementById('swal-naviera-select'); let email = select.value;
                if (email === 'custom') { email = document.getElementById('swal-custom-email')?.value?.trim(); if (!email || !email.includes('@')) { Swal.showValidationMessage('Ingresa un correo válido.'); return false; } }
                if (!email) { Swal.showValidationMessage('Selecciona o ingresa un destinatario.'); return false; }
                return { email, asunto: document.getElementById('swal-asunto-naviera').value, mensaje: document.getElementById('swal-msg-naviera').value };
            }
        }).then((result) => { if (result.isConfirmed && result.value) { enviarCorreoEvidenciaNaviera(idOp, booking, result.value.email, result.value.asunto, result.value.mensaje); } });
    };

    window.onNavieraSelectChange = function(select) { const wrapper = document.getElementById('swal-custom-email-wrapper'); if (wrapper) wrapper.style.display = select.value === 'custom' ? 'block' : 'none'; };

    window.enviarCorreoEvidenciaNaviera = function(idOp, booking, correoDestino, asunto, mensajeExtra) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
        const cuerpoHtml = `<div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;"><div style="background:linear-gradient(135deg,#0f172a 0%,#6d2d9e 100%);padding:25px;text-align:center;border-radius:8px 8px 0 0;"><h2 style="color:#fff;margin:0;">Envío de Documentos</h2><p style="color:#cbd5e1;margin:6px 0 0;">Booking: <strong style="color:#fff;">${booking}</strong></p></div><div style="background:#fff;padding:28px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;"><p style="font-size:14px;color:#475569;line-height:1.6;">Estimado equipo,<br><br>Adjuntamos la evidencia de envío de documentos correspondiente al Booking <strong>${booking}</strong>.</p>${mensajeExtra ? `<p style="font-size:13px;color:#475569;border-left:3px solid #6d2d9e;padding-left:12px;margin-top:16px;">${escapeHtmlAtilson(mensajeExtra)}</p>` : ''}<hr style="border:0;border-top:1px solid #e2e8f0;margin:24px 0;"><p style="font-size:11px;color:#94a3b8;text-align:center;margin:0;">Atilson Cargo SpA · Operaciones Logísticas</p></div></div>`;

        const formData = new FormData(); formData.append('id', idOp.toString().replace('sub-', '')); formData.append('correoDestino', correoDestino); formData.append('copiaCc', 'operaciones@atilson.cl'); formData.append('asunto', asunto); formData.append('cuerpoHtml', cuerpoHtml); formData.append('tipoCorreo', 'evidenciaNaviera');

        Swal.fire({ title: 'Enviando...', allowOutsideClick: false, target: document.body, didOpen: () => Swal.showLoading() });

        fetch('?handler=EnviarCorreoTransporte', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
        .then(r => r.json()).then(data => {
            if (data.success) {
                Swal.fire({ icon: 'success', title: '¡Enviado!', text: `Correo enviado correctamente a ${correoDestino}`, confirmButtonColor: '#10b981', target: document.body });
                if(typeof marcarEstadoRapidoVisual === 'function') { marcarEstadoRapidoVisual('matriz', idOp, 'EVIDENCIA ENVIADA A NAVIERA', '#0ea5e9', `Se envió la evidencia al correo: <strong>${escapeHtmlAtilson(correoDestino)}</strong>.`); }
            } else { Swal.fire({ icon: 'error', title: 'Error', text: data.message, target: document.body }); }
        }).catch(err => Swal.fire({ icon: 'error', title: 'Error de conexión', text: err.message, target: document.body }));
    };

    window.toggleAplicaCert = function(certKey, idOp, valor) {
    const ctx = modalCtx() || document;
    const row = ctx.querySelector(`#row-${certKey}-${idOp}`);
    if (!row) return;
    const aplica = (valor === 'si');
    row.style.opacity = aplica ? '1' : '0.5';
    if (certKey === 'fit') {
        const chkSag = ctx.querySelector(`#chkSag-${idOp}`);
        if (chkSag && chkSag.checked !== aplica) { chkSag.checked = aplica; handleTipoTramite(idOp, 'sag'); }
    } else if (certKey === 'san' || certKey === 'cap') {
        const chkSerna = ctx.querySelector(`#chkSerna-${idOp}`);
        if (chkSerna && chkSerna.checked !== aplica) { chkSerna.checked = aplica; handleTipoTramite(idOp, 'sernapesca'); }
    }
};

window.previsualizarSagModalidad = function(valor, idRef) {
    const ctx = modalCtx() || document;
    const banner = ctx.querySelector(`#sag-preview-${idRef}`);
    if (!banner) return;
    if (valor === 'Planta') {
        banner.style.cssText = 'font-size:10px; font-weight:800; padding:4px 10px; border-radius:6px; background:#f0fdf4; color:#16a34a; border:1px solid #bbf7d0; white-space:nowrap;';
        banner.innerHTML = '<i class="bi bi-check-circle-fill me-1"></i> Sin costo (Atilson / Agencia / Cliente)';
    } else {
        banner.style.cssText = 'font-size:10px; font-weight:800; padding:4px 10px; border-radius:6px; background:#fffbeb; color:#d97706; border:1px solid #fde68a; white-space:nowrap;';
        banner.innerHTML = '<i class="bi bi-currency-dollar me-1"></i> Aplica tarifa (Atilson / Agencia / Cliente)';
    }
};

window.handleTipoTramite = function(idOp, tipoModificado) {
    const ctx = modalCtx() || document;
    let baseId = idOp.replace('v3-', '').replace('sub-', '');
    const chkSag   = ctx.querySelector(`#chkSag-${idOp}`);
    const chkSerna = ctx.querySelector(`#chkSerna-${idOp}`);

    if (tipoModificado === 'sag' && chkSag && chkSag.checked) {
        if (chkSerna) { chkSerna.checked = false; }
    } else if (tipoModificado === 'sernapesca' && chkSerna && chkSerna.checked) {
        if (chkSag) { chkSag.checked = false; }
    }

    const sagActivo   = chkSag   && chkSag.checked;
    const sernaActivo = chkSerna && chkSerna.checked;

    if (idOp.includes('v3-') || idOp.includes('sub-')) {
        const btnFito = ctx.querySelector(`#sidebar-btn-fito-${baseId}`);
        if (btnFito) btnFito.style.display = sagActivo ? 'flex' : 'none';
        const btnSani = ctx.querySelector(`#sidebar-btn-sani-${baseId}`);
        if (btnSani) btnSani.style.display = sernaActivo ? 'flex' : 'none';
        const btnCap = ctx.querySelector(`#sidebar-btn-cap-${baseId}`);
        if (btnCap) btnCap.style.display = sernaActivo ? 'flex' : 'none';

        const panelActivoActual = ctx.querySelector(`.pro-pane-${baseId}[style*="display: block"]`);
        if (!sagActivo && panelActivoActual && panelActivoActual.id === `pane-fito-${baseId}`) openProTab(baseId, 'aduana');
        if (!sernaActivo && panelActivoActual && (panelActivoActual.id === `pane-sani-${baseId}` || panelActivoActual.id === `pane-cap-${baseId}`)) openProTab(baseId, 'aduana');

        togglePanelPro(`v3-fit-wrapper-${baseId}`, sagActivo);
        togglePanelPro(`v3-san-wrapper-${baseId}`, sernaActivo);
        togglePanelPro(`v3-cap-wrapper-${baseId}`, sernaActivo);

        const formContainer = ctx.querySelector(`#doc-global-form-${idOp}`) || ctx.querySelector(`#doc-global-form-sub-${baseId}`);
        if (formContainer) {
            const formEl = formContainer.closest('form');
            if (formEl) {
                const formDataSave = new FormData(formEl);
                fetch('/Operaciones/Index?handler=QuickEditDocumental', { method: 'POST', body: formDataSave, headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .catch(e => console.warn("Aviso auto-guardado Trámites:", e));
            }
        }
        return;
    }
};

window.toggleSubCert = function(rowId, checkbox) {
    const ctx = modalCtx();
    if (!ctx) return;
    const row = ctx.querySelector(`#${rowId}`);
    if (row) row.style.display = checkbox.checked ? 'block' : 'none';
};

window.toggleInputsRow = function(chk) {
    const row = chk.closest('tr');
    const inputs = row.querySelectorAll('.row-inputs');
    const numInput = row.querySelector('input[type="text"]');
    if (chk.checked) {
        inputs.forEach(el => el.style.display = 'block');
        if (numInput && !numInput.value.trim()) numInput.placeholder = "Pendiente...";
    } else {
        inputs.forEach(el => el.style.display = 'none');
        if (numInput) { numInput.value = ""; numInput.placeholder = ""; }
    }
};

window.toggleDeclaracion = function(idOp) {
    const radioDus = document.getElementById(`radioDus_${idOp}`);
    const radioDin = document.getElementById(`radioDin_${idOp}`);
    if (!radioDus || !radioDin) return;
    const isDus = radioDus.checked;
    document.getElementById(`container-dus-${idOp}`).style.display = isDus ? 'flex' : 'none';
    document.getElementById(`container-din-${idOp}`).style.display = isDus ? 'none' : 'flex';
    radioDus.closest('.form-check').style.opacity = isDus ? '1' : '0.4';
    radioDin.closest('.form-check').style.opacity = isDus ? '0.4' : '1';
};

window.toggleExclusionTramites = function(idBase) {
    const ctx = modalCtx() || document;
    const chkAcl = ctx.querySelector(`#chkAcl-${idBase}`);
    const chkCan = ctx.querySelector(`#chkCan-${idBase}`);
    if (!chkAcl || !chkCan) return;
    const formContainer = chkAcl.closest('.form-section') || chkAcl.closest('.pro-card-red');
    const isEditMode = formContainer && formContainer.classList.contains('edit-mode');

    if (chkCan.checked) {
        chkAcl.checked = false;
        chkAcl.closest('.d-flex').style.opacity = '0.4'; chkCan.closest('.d-flex').style.opacity = '1';
        if (isEditMode) { chkAcl.disabled = true; chkCan.disabled = false; }
    } else if (chkAcl.checked) {
        chkCan.checked = false;
        chkCan.closest('.d-flex').style.opacity = '0.4'; chkAcl.closest('.d-flex').style.opacity = '1';
        if (isEditMode) { chkCan.disabled = true; chkAcl.disabled = false; }
    } else {
        chkAcl.closest('.d-flex').style.opacity = '1'; chkCan.closest('.d-flex').style.opacity = '1';
        if (isEditMode) { chkAcl.disabled = false; chkCan.disabled = false; }
    }
};
        window.cambiarModalidadCertPorCertificado = function(idOp, certKey, modalidad) {
            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            let formData = new FormData();
            formData.append('idOperacion', idOp);
            formData.append('certKey', certKey);
            formData.append('modalidad', modalidad);

            fetch('?handler=ActualizarModalidadCert', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
            .then(res => res.json()).then(data => {
                if (data.success) {
                    Swal.fire({ icon: 'success', title: 'Modalidad actualizada', toast: true, position: 'top-end', timer: 1200, showConfirmButton: false });
                    setTimeout(() => window.location.reload(), 700);
                } else {
                    Swal.fire('No se pudo cambiar', data.message || 'Error desconocido.', 'warning').then(() => window.location.reload());
                }
            }).catch(() => { Swal.fire('Error', 'Fallo de conexión.', 'error'); window.location.reload(); });
        };

        window.guardarCertOrigenContenedor = function(idOp, idUnidad, containerId) {
            const ctx = modalCtx() || document;
            const container = ctx.querySelector(`#${containerId}`);
            if (!container) return;
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('IdOperacion', idOp); formData.append('IdUnidadTecnica', idUnidad);
            formData.append('CertKey', 'ori'); formData.append('EnviarFinanzas', 'true'); formData.append('SinNumero', 'false');
            container.querySelectorAll('[data-field]').forEach(el => formData.append(`docUpdates.${el.getAttribute('data-field')}`, el.value));
            const fileEl = container.querySelector('[data-field-file]');
            if (fileEl && fileEl.files.length > 0) formData.append('UploadCertA1', fileEl.files[0]);

            fetch('?handler=GuardarCertificado', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
            .then(r => r.json()).then(data => {
                if (data.success) { Swal.fire({ icon: 'success', title: 'Guardado', toast: true, position: 'top-end', timer: 1200, showConfirmButton: false }); setTimeout(() => window.location.reload(), 700); }
                else Swal.fire('Atención', data.message || 'No se pudo guardar.', 'warning');
            }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
        };

        window.guardarCertContenedor = function(idOp, idUnidad, certKey, containerId) {
            const ctx = modalCtx() || document;
            const container = ctx.querySelector(`#${containerId}`);
            if (!container) return;
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('IdOperacion', idOp); formData.append('IdUnidadTecnica', idUnidad);
            formData.append('CertKey', certKey); formData.append('EnviarFinanzas', 'true'); formData.append('SinNumero', 'false');
            container.querySelectorAll('[data-field]').forEach(el => formData.append(`docUpdates.${el.getAttribute('data-field')}`, el.value));
            const fileEl = container.querySelector('[data-field-file]');
            if (fileEl && fileEl.files.length > 0) formData.append('UploadCertA1', fileEl.files[0]);

            fetch('?handler=GuardarCertificado', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
            .then(r => r.json()).then(data => {
                if (data.success) { Swal.fire({ icon: 'success', title: 'Guardado', toast: true, position: 'top-end', timer: 1200, showConfirmButton: false }); setTimeout(() => window.location.reload(), 700); }
                else Swal.fire('Atención', data.message || 'No se pudo guardar.', 'warning');
            }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
        };

        
        window.guardarDocBaseDetalle = function (campo) {
            const inputVal = document.getElementById('docbase-val-' + campo);
            const inputFile = document.getElementById('docbase-file-' + campo);
            const formData = new FormData();
            formData.append('IdOperacion', detalleOpData().id);
            formData.append('Campo', campo);
            formData.append('Valor', inputVal ? inputVal.value : '');
            if (inputFile && inputFile.files.length > 0) formData.append('Archivo', inputFile.files[0]);

            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            fetch('?handler=GuardarDocBase', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                .then(res => res.json()).then(data => {
                    if (data.success) { Swal.fire({ icon: 'success', title: 'Guardado', toast: true, position: 'top-end', timer: 1200, showConfirmButton: false }); setTimeout(() => window.location.reload(), 700); }
                    else { Swal.fire('Atención', data.message || 'No se pudo guardar.', 'warning'); }
                }).catch(() => Swal.fire('Error', 'Fallo de conexión.', 'error'));
        };

        window.mostrarNombreArchivo = function(input, spanId) {
            const span = document.getElementById(spanId);
            if (!span) return;
            if (input.files && input.files.length > 0) {
                span.textContent = input.files[0].name;
                span.style.color = '#16a34a';
                span.style.fontWeight = 'bold';
            } else {
                span.textContent = 'Sin archivo';
                span.style.color = '';
                span.style.fontWeight = '';
            }
        };

                window.procesarCambioInterplanta = function(select, idSuffix, numPlanta) {
            const option = select.options[select.selectedIndex];
            const url = option ? (option.getAttribute('data-url') || '') : '';
            const linkInput = document.querySelector(`#ter-form-${idSuffix} input[name="LinkMaps${numPlanta}"]`);
            if (linkInput) linkInput.value = url;

            if (!select.value) return; // se limpió la selección (Cancelar Despliegue), no hay nada que calcular

            const ciudadDestino = option ? (option.getAttribute('data-ciudad') || '') : '';
            const form = document.getElementById(`ter-form-${idSuffix}`);
            const selPlanta1 = form ? form.querySelector('select[name="PlantaCarga"]') : null;
            const opcionPlanta1 = selPlanta1 ? selPlanta1.options[selPlanta1.selectedIndex] : null;
            const ciudadOrigen = opcionPlanta1 ? (opcionPlanta1.getAttribute('data-ciudad') || '') : '';
            const nombrePlanta1 = selPlanta1 ? selPlanta1.value : '';
            const nombrePlantaN = select.value;

            if (!nombrePlanta1) {
                Swal.fire({ icon: 'warning', title: 'Falta la Planta de Cliente principal', text: 'Selecciona primero la Planta de Cliente (arriba) para poder calcular si aplica tarifa adicional.', toast: true, position: 'top-end', timer: 3000, showConfirmButton: false });
                return;
            }
            if (!ciudadOrigen || !ciudadDestino) {
                Swal.fire({ icon: 'warning', title: 'No se pudo verificar la tarifa', text: 'A una de las plantas le falta la ciudad configurada — revísalo en App/Plantas.', toast: true, position: 'top-end', timer: 3500, showConfirmButton: false });
                return;
            }

            // Siempre se calcula la distancia real (incluso si ambas plantas están en la misma
            // ciudad): una ciudad grande puede igual superar los 20 km internamente, así que no
            // basta con comparar el nombre de la ciudad.
            Swal.fire({ title: 'Calculando distancia...', html: `${ciudadOrigen} &rarr; ${ciudadDestino}`, toast: true, position: 'top-end', showConfirmButton: false, didOpen: () => Swal.showLoading() });

            fetch(`/Operaciones/Index?handler=CalcularDistanciaLogistica&origen=${encodeURIComponent(ciudadOrigen)}&destino=${encodeURIComponent(ciudadDestino)}`)
                .then(r => r.json())
                .then(data => {
                    Swal.close();
                    if (!data.success) {
                        Swal.fire({ icon: 'warning', title: 'No se pudo calcular la distancia', text: data.mensaje || 'Intenta de nuevo en unos segundos.', toast: true, position: 'top-end', timer: 3500, showConfirmButton: false });
                        return;
                    }
                    const km = data.distanciaKm;
                    if (km > 20) {
                        Swal.fire({ icon: 'info', title: 'Aplica tarifa adicional', html: `<strong>${nombrePlanta1}</strong> (${ciudadOrigen}) &rarr; <strong>${nombrePlantaN}</strong> (${ciudadDestino}): <strong>${km} km</strong>, supera los 20 km.<br>Se calcularán <strong>dos tarifas terrestres independientes</strong> en Finanzas.`, confirmButtonText: 'Entendido' });
                    } else {
                        Swal.fire({ icon: 'success', title: 'No aplica tarifa adicional', html: `<strong>${nombrePlanta1}</strong> (${ciudadOrigen}) &rarr; <strong>${nombrePlantaN}</strong> (${ciudadDestino}): <strong>${km} km</strong>, 20 km o menos.<br>Aplica la <strong>tarifa plana de porteo</strong>.`, confirmButtonText: 'Entendido' });
                    }
                })
                .catch(() => { Swal.close(); Swal.fire({ icon: 'error', title: 'Error de conexión', text: 'No se pudo calcular la distancia entre plantas.', toast: true, position: 'top-end', timer: 3000, showConfirmButton: false }); });
        };

        // ===== Fase 6: funciones de soporte para _PanelDocumental / _PanelTransporte =====
        // Faltaban porque Detalle.cshtml se armó copiando solo una parte del script de Index.

        window.handleTipoTramite = function(idOp, tipoModificado) {
            const ctx = modalCtx() || document;
            let baseId = idOp.replace('v3-', '').replace('sub-', '');
            const chkSag   = ctx.querySelector(`#chkSag-${idOp}`);
            const chkSerna = ctx.querySelector(`#chkSerna-${idOp}`);

            if (tipoModificado === 'sag' && chkSag && chkSag.checked) { if (chkSerna) chkSerna.checked = false; }
            else if (tipoModificado === 'sernapesca' && chkSerna && chkSerna.checked) { if (chkSag) chkSag.checked = false; }

            const sagActivo   = chkSag   && chkSag.checked;
            const sernaActivo = chkSerna && chkSerna.checked;

            if (idOp.includes('v3-') || idOp.includes('sub-')) {
                const btnFito = ctx.querySelector(`#sidebar-btn-fito-${baseId}`);
                if (btnFito) btnFito.style.display = sagActivo ? 'flex' : 'none';
                const btnSani = ctx.querySelector(`#sidebar-btn-sani-${baseId}`);
                if (btnSani) btnSani.style.display = sernaActivo ? 'flex' : 'none';
                const btnCap = ctx.querySelector(`#sidebar-btn-cap-${baseId}`);
                if (btnCap) btnCap.style.display = sernaActivo ? 'flex' : 'none';

                const panelActivoActual = ctx.querySelector(`.pro-pane-${baseId}[style*="display: block"]`);
                if (!sagActivo && panelActivoActual && panelActivoActual.id === `pane-fito-${baseId}`) openProTab(baseId, 'aduana');
                if (!sernaActivo && panelActivoActual && (panelActivoActual.id === `pane-sani-${baseId}` || panelActivoActual.id === `pane-cap-${baseId}`)) openProTab(baseId, 'aduana');

                togglePanelPro(`v3-fit-wrapper-${baseId}`, sagActivo);
                togglePanelPro(`v3-san-wrapper-${baseId}`, sernaActivo);
                togglePanelPro(`v3-cap-wrapper-${baseId}`, sernaActivo);

                const formContainer = ctx.querySelector(`#doc-global-form-${idOp}`) || ctx.querySelector(`#doc-global-form-sub-${baseId}`);
                if (formContainer) {
                    const formEl = formContainer.closest('form');
                    if (formEl) {
                        const formDataSave = new FormData(formEl);
                        fetch('?handler=QuickEditDocumental', { method: 'POST', body: formDataSave, headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                        .catch(e => console.warn("Aviso auto-guardado Trámites:", e));
                    }
                }
                return;
            }
        };

        window.previsualizarSagModalidad = function(valor, idRef) {
            const ctx = modalCtx() || document;
            const banner = ctx.querySelector(`#sag-preview-${idRef}`);
            if (!banner) return;
            if (valor === 'Planta') {
                banner.style.cssText = 'font-size:10px; font-weight:800; padding:4px 10px; border-radius:6px; background:#f0fdf4; color:#16a34a; border:1px solid #bbf7d0; white-space:nowrap;';
                banner.innerHTML = '<i class="bi bi-check-circle-fill me-1"></i> Sin costo (Atilson / Agencia / Cliente)';
            } else {
                banner.style.cssText = 'font-size:10px; font-weight:800; padding:4px 10px; border-radius:6px; background:#fffbeb; color:#d97706; border:1px solid #fde68a; white-space:nowrap;';
                banner.innerHTML = '<i class="bi bi-currency-dollar me-1"></i> Aplica tarifa (Atilson / Agencia / Cliente)';
            }
        };

        window.toggleAplicaCert = function(certKey, idOp, valor) {
            const ctx = modalCtx() || document;
            const row = ctx.querySelector(`#row-${certKey}-${idOp}`);
            if (!row) return;
            const aplica = (valor === 'si');
            row.style.opacity = aplica ? '1' : '0.5';
            const tdToggle = row.querySelector('[data-bs-toggle="collapse"]');
            if (tdToggle) tdToggle.style.pointerEvents = aplica ? 'auto' : 'none';
            if (!aplica && tdToggle) {
                const panelId = tdToggle.getAttribute('data-bs-target');
                if (panelId) {
                    const panel = ctx.querySelector(panelId);
                    if (panel && panel.classList.contains('show')) {
                        const bsCol = bootstrap.Collapse.getInstance(panel);
                        if (bsCol) bsCol.hide(); else panel.classList.remove('show');
                    }
                }
            }
            if (certKey === 'fit') {
                const chkSag = ctx.querySelector(`#chkSag-${idOp}`);
                if (chkSag && chkSag.checked !== aplica) { chkSag.checked = aplica; handleTipoTramite(idOp, 'sag'); }
            } else if (certKey === 'san' || certKey === 'cap') {
                const chkSerna = ctx.querySelector(`#chkSerna-${idOp}`);
                if (chkSerna && chkSerna.checked !== aplica) { chkSerna.checked = aplica; handleTipoTramite(idOp, 'sernapesca'); }
            }
        };

        window.switchSlot = function(btn, opId, certType, slotNum) {
            let container = btn.closest('.slots-container');
            container.querySelectorAll('.slot-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            container.querySelectorAll('.slot-pane, .slot-paneDoc').forEach(p => p.classList.add('d-none'));
            let targetPane = container.querySelector(`.${certType}-${slotNum}`);
            if (targetPane) targetPane.classList.remove('d-none');
        };

        window.toggleSlots = function(idOp, certType) {
            const ctx = modalCtx() || document;
            const selectedRadio = ctx.querySelector(`input[name="${certType.charAt(0).toUpperCase() + certType.slice(1)}Tipo-${idOp}"]:checked`) || ctx.querySelector(`input[name="${certType.charAt(0).toUpperCase() + certType.slice(1)}Tipo-sub-${idOp}"]:checked`);
            if (!selectedRadio) return;
            const valor = selectedRadio.value;
            const containerSlots = ctx.querySelector(`#slots-${certType}-${idOp}`) || ctx.querySelector(`#slots-${certType}-sub-${idOp}`);
            const formContainer = ctx.querySelector(`#doc-global-form-${idOp}`) || ctx.querySelector(`#doc-global-form-sub-${idOp}`);
            const isEditMode = formContainer?.classList.contains('edit-mode');
            if (valor === 'aplica' || valor === 'rem' || valor === 'si') {
                if (containerSlots) {
                    containerSlots.style.display = 'block';
                    if (isEditMode) {
                        containerSlots.querySelectorAll('.inline-field').forEach(i => i.removeAttribute('readonly'));
                        containerSlots.querySelectorAll('.edit-only-file').forEach(i => i.removeAttribute('disabled'));
                    }
                }
            } else if (containerSlots) {
                containerSlots.style.display = 'none';
                containerSlots.querySelectorAll('input[type="text"]').forEach(i => { i.value = ""; i.setAttribute('readonly', true); });
                containerSlots.querySelectorAll('input[type="file"]').forEach(i => { i.value = ""; i.setAttribute('disabled', true); });
            }
        };

        window.toggleSlotsOrigen = function(idOp, certType, valor) {
            const ctx = modalCtx() || document;
            const aplica  = ctx.querySelector(`#ori-aplica-${idOp}`);
            const eventos = ctx.querySelector(`#slots-ori-${idOp}`);
            if (aplica)  aplica.style.display  = (valor === 'si')  ? 'block' : 'none';
            if (eventos) eventos.style.display = (valor === 'rem') ? 'block' : 'none';
        };

        window.toggleValorTramite = function(checkbox, divId) {
            const ctx = modalCtx() || document;
            const div = ctx.querySelector(`#${divId}`) || document.getElementById(divId);
            if (!div) return;
            div.style.display = checkbox.checked ? 'block' : 'none';
            const input = div.querySelector('input');
            if (input) { if (checkbox.checked) input.removeAttribute('readonly'); else { input.setAttribute('readonly', true); input.value = ''; } }
        };

        window.toggleDeclaracion = function(idOp) {
            const radioDus = document.getElementById(`radioDus_${idOp}`);
            const radioDin = document.getElementById(`radioDin_${idOp}`);
            if (!radioDus || !radioDin) return;
            const isDus = radioDus.checked;
            document.getElementById(`container-dus-${idOp}`).style.display = isDus ? 'flex' : 'none';
            document.getElementById(`container-din-${idOp}`).style.display = isDus ? 'none' : 'flex';
            radioDus.closest('.form-check').style.opacity = isDus ? '1' : '0.4';
            radioDin.closest('.form-check').style.opacity = isDus ? '0.4' : '1';
        };

        window.toggleSubcertsDependientes = function(idOp, isChecked) {
            const subCerts = ['chkCoa', 'chkDt', 'chkCod', 'chkCla', 'chkNep'];
            subCerts.forEach(cert => {
                const chk = document.getElementById(`${cert}-${idOp}`);
                if (chk && !isChecked) { chk.checked = false; chk.dispatchEvent(new Event('change')); }
            });
        };

        window.registrarActividadSubcert = function(tipo, idOp, input) {
            if (input.files && input.files.length > 0) {
                const nombreArchivo = input.files[0].name; const ahora = new Date(); const fecha = ahora.toLocaleDateString('es-CL'); const hora = ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' }); const usuario = 'Operaciones Atilson';
                const logEl = modalCtx()?.querySelector(`#log-${tipo}-txt-${idOp}`) || document.getElementById(`log-${tipo}-txt-${idOp}`);
                if (logEl) {
                    if (logEl.innerHTML.includes('Sin registros')) logEl.innerHTML = '';
                    const div = document.createElement('div');
                    div.style.cssText = 'border-left:2px solid #10b981; padding:6px; margin-bottom:6px; background:#f8fafc; border-top:1px solid #f1f5f9; border-right:1px solid #f1f5f9; border-bottom:1px solid #f1f5f9;';
                    div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:#0f172a; text-transform:uppercase; font-size:10px;">ARCHIVO ADJUNTO</span><span style="color:#64748b; font-size:9px;">${fecha} ${hora}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>${usuario}</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">Se adjuntó el documento: <em>${escapeHtmlAtilson(nombreArchivo)}</em></div><span class="badge mt-2 d-inline-block" style="font-size:8px; background:#dcfce7; color:#16a34a; border:1px solid #bbf7d0;">PENDIENTE GUARDADO</span>`;
                    logEl.insertBefore(div, logEl.firstChild);
                    Swal.fire({ icon: 'success', title: 'Archivo Registrado', toast: true, position: 'top-end', timer: 1500, showConfirmButton: false });
                }
                if (tipo === 'san' || tipo === 'cap') {
                    let ctx = modalCtx() || document;
                    let btnCli1 = ctx.querySelector(`#btn-enviar-cli-${tipo}-v3-${idOp}`); let btnCli2 = ctx.querySelector(`#btn-enviar-cli-${tipo}-main-v3-${idOp}`); let btnCli3 = ctx.querySelector(`#btn-enviar-cli-${tipo}-sub-${idOp}`); let btnCli4 = ctx.querySelector(`#btn-enviar-cli-${tipo}-sub-v3-${idOp}`);
                    if (btnCli1) btnCli1.style.setProperty('display', 'inline-flex', 'important');
                    if (btnCli2) btnCli2.style.setProperty('display', 'inline-flex', 'important');
                    if (btnCli3) btnCli3.style.setProperty('display', 'inline-flex', 'important');
                    if (btnCli4) btnCli4.style.setProperty('display', 'inline-block', 'important');
                }
            }
        };

                        window.enviarCertificadoCliente = function(idOp, tipo, booking) {
                    let nombreCert = tipo === 'san' ? 'Sanitario' : 'de Captura';
                    let correoPredefinido = document.querySelector('input[name="CorreosCliente"]')?.value || detalleOpData().correoCliente;

                    Swal.fire({
                        title: `Enviar Certificado ${nombreCert}`, text: `Se enviará el certificado del Booking ${booking} al cliente para su revisión y aprobación.`, input: 'email', inputValue: correoPredefinido, inputPlaceholder: 'correo@cliente.com', showCancelButton: true, confirmButtonText: '<i class="bi bi-send-fill"></i> Enviar a Cliente', cancelButtonText: 'Cancelar', confirmButtonColor: '#6d2d9e'
                    }).then((result) => {
                        if (result.isConfirmed && result.value) {
                            let token = document.querySelector('input[name="__RequestVerificationToken"]').value; let formData = new FormData(); formData.append('idOperacion', idOp); formData.append('tipo', tipo); formData.append('correoDestino', result.value);

                            Swal.fire({ title: 'Enviando...', text: 'Procesando notificación y guardando en bitácora...', allowOutsideClick: false, didOpen: () => { Swal.showLoading(); } });

                            fetch('?handler=EnviarCertificadoCliente', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                            .then(res => res.json()).then(data => {
                                if (data.success) {
                                    Swal.fire({ title: '¡Enviado!', text: 'El certificado fue enviado exitosamente.', icon: 'success' });
                                    let stepper = document.getElementById(`stepper-${tipo}-${idOp}`);
                                    if (stepper) { let steps = stepper.querySelectorAll('.step-item, .pro-step'); if (steps.length > 0) { steps[0].classList.remove('rejected'); steps[0].classList.add('completed'); let circle = steps[0].querySelector('.step-circle, .pro-step-icon'); if (circle) circle.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; } }
                                    let btnEnviarCli = document.getElementById(`btn-enviar-cli-${tipo}-${idOp}`);
                                    if (btnEnviarCli) btnEnviarCli.style.setProperty('display', 'inline-flex', 'important');
                                    let logEl = document.getElementById(`log-${tipo}-txt-${idOp}`);
                                    if (logEl) {
                                        if (logEl.innerHTML.includes('Sin registros')) logEl.innerHTML = '';
                                        const ahora = new Date(); const fecha = ahora.toLocaleDateString('es-CL'); const hora = ahora.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' });
                                        const div = document.createElement('div');
                                        div.style.cssText = 'border-left:2px solid #0ea5e9; padding:6px; margin-bottom:6px; background:#f0f9ff; border-top:1px solid #bae6fd; border-right:1px solid #bae6fd; border-bottom:1px solid #bae6fd;';
                                        div.innerHTML = `<div class="d-flex justify-content-between align-items-center mb-1"><span style="font-weight:800; color:#0284c7; text-transform:uppercase; font-size:10px;">ENVÍO A CLIENTE</span><span style="color:#94a3b8; font-size:9px;">${fecha} ${hora}</span></div><div style="color:#475569; font-size:10px; margin-bottom:3px;">Por: <strong>Operaciones Atilson</strong></div><div style="color:#1e293b; font-size:11px; line-height:1.2;">Certificado enviado para revisión a: <strong>${escapeHtmlAtilson(result.value)}</strong></div><span class="badge mt-2 d-inline-block" style="font-size:8px; background:#e0f2fe; color:#0284c7; border:1px solid #bae6fd;">GUARDADO EN BD</span>`;
                                        logEl.insertBefore(div, logEl.firstChild);
                                    }
                                                                } else { Swal.fire({ title: 'Error', text: data.message, icon: 'error' }); }
                            }).catch(() => Swal.fire({ title: 'Error', text: 'Fallo de conexión.', icon: 'error' }));
                        }
                    });
                };

                window.enviarSanitarioAgencia = function(idOp, booking) {
                    const correoPredefinido = "a.barrera@stembex.cl; javier.traverso@stembex.cl";
                    Swal.fire({
                        title: 'Enviar Certificado Sanitario a Aga-Sern',
                        html: `<div style="text-align:left; font-size:13px;"><p style="color:#64748b; margin:0 0 14px;"><strong style="color:#0f172a;">BKG: ${booking}</strong> — Se enviará el certificado sanitario a la agencia Sernapesca.</p><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Destinatario (Aga-Sern)</label><input type="text" id="swal-san-email-det" class="swal2-input" style="margin:0 0 12px; width:100%; font-size:12px;" value="${correoPredefinido}" /><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Asunto</label><input type="text" id="swal-san-asunto-det" class="swal2-input" style="margin:0 0 12px; width:100%; font-weight:700;" value="[ATILSON CARGO] Certificado Sanitario — BKG: ${booking}" /><label style="font-size:10px; font-weight:800; color:#94a3b8; text-transform:uppercase; display:block; margin-bottom:5px;">Mensaje adicional (opcional)</label><textarea id="swal-san-msg-det" class="swal2-textarea" style="margin:0; width:100%; font-size:12px; height:70px;" placeholder="Ej: Adjuntamos certificado sanitario para su revisión..."></textarea></div>`,
                        showCancelButton: true, confirmButtonText: '<i class="bi bi-send-fill"></i> Enviar a Aga-Sern', cancelButtonText: 'Cancelar', confirmButtonColor: '#0284c7', cancelButtonColor: '#64748b', width: 500,
                        preConfirm: () => {
                            const email = document.getElementById('swal-san-email-det').value.trim();
                            if (!email) { Swal.showValidationMessage('Ingresa un destinatario.'); return false; }
                            return { email, asunto: document.getElementById('swal-san-asunto-det').value, mensaje: document.getElementById('swal-san-msg-det').value };
                        }
                    }).then((result) => {
                        if (!result.isConfirmed || !result.value) return;
                        const idOperacionReal = idOp.toString().replace('sub-', '').replace('v3-', '');
                        const baseUrl = `${window.location.protocol}//${window.location.host}`;
                        const urlRecibido = `${baseUrl}/Operaciones/Index?handler=RespuestaSanitarioAgencia&id=${idOperacionReal}`;

                        const htmlCuerpo = `<div style="font-family:Arial,sans-serif;max-width:580px;margin:0 auto;"><div style="background:linear-gradient(135deg,#0f172a 0%,#0ea5e9 100%);padding:24px 28px;border-radius:8px 8px 0 0;"><h2 style="color:#fff;margin:0;font-size:18px;">Envío Certificado Sanitario</h2><p style="color:#bae6fd;margin:6px 0 0;font-size:13px;">Booking: <strong style="color:#fff;">${booking}</strong></p></div><div style="background:#fff;padding:28px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;"><p style="font-size:14px;color:#475569;line-height:1.6;white-space:pre-wrap;">${result.value.mensaje || 'Junto con saludar, adjuntamos el Certificado Sanitario para su trámite ante Sernapesca.'}</p><div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:20px;margin:24px 0;"><p style="font-size:13px;font-weight:700;color:#0f172a;text-transform:uppercase;margin:0 0 14px;">Por favor confirme la recepción de este documento:</p><a href="${urlRecibido}" style="background:#0ea5e9;color:#fff;padding:14px 22px;text-decoration:none;border-radius:6px;font-weight:800;font-size:13px;display:inline-block;">📋 CONFIRMAR RECEPCIÓN</a></div><p style="font-size:11px;color:#94a3b8;text-align:center;margin:0;">Atilson Cargo SpA · Operaciones Logísticas</p></div></div>`;

                        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                        const formData = new FormData(); formData.append('id', idOperacionReal); formData.append('correoDestino', result.value.email); formData.append('copiaCc', 'operaciones@atilson.cl'); formData.append('asunto', result.value.asunto); formData.append('cuerpoHtml', htmlCuerpo); formData.append('tipoCorreo', 'sanitarioAgencia');

                        Swal.fire({ title: 'Enviando...', allowOutsideClick: false, didOpen: () => Swal.showLoading() });

                        fetch('?handler=EnviarCorreoTransporte', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                        .then(r => r.json()).then(data => {
                            if (data.success) {
                                const stepAga = document.getElementById(`step-aga-san-${idOp}`);
                                if (stepAga) { stepAga.classList.add('completed'); const circle = stepAga.querySelector('.step-circle, .pro-step-icon'); if (circle) circle.innerHTML = '<i class="bi bi-check-lg" style="font-size:14px;"></i>'; }
                                if (typeof window.marcarEstadoRapidoVisual === 'function') window.marcarEstadoRapidoVisual('san', idOp, 'ENVIADO A AGA-SERN', '#0ea5e9', `Certificado Sanitario enviado a: <strong>${escapeHtmlAtilson(result.value.email)}</strong>`);

                                const formContainer = document.getElementById(`doc-global-form-${idOp}`) || document.getElementById(`doc-base-form-${idOp}`);
                                const formEl = formContainer ? formContainer.closest('form') : null;
                                if (formEl) {
                                    if (typeof window.sincronizarLogsAntesDeGuardar === 'function') window.sincronizarLogsAntesDeGuardar(formEl);
                                    const formDataSave = new FormData(formEl);
                                    fetch('?handler=QuickEditDocumental', { method: 'POST', body: formDataSave, headers: { 'X-Requested-With': 'XMLHttpRequest' } }).catch(e => console.warn("Aviso auto-guardado Sernapesca:", e));
                                }
                                Swal.fire({ icon: 'success', title: '¡Enviado!', text: `Correo enviado a ${result.value.email}`, confirmButtonColor: '#10b981' });
                            } else { Swal.fire({ icon: 'error', title: 'Error', text: data.message }); }
                        }).catch(err => Swal.fire({ icon: 'error', title: 'Error', text: err.message }));
                    });
                };

                window.syncEstadoLar = function (selectElement, elementId) {
                    const hiddenInput = document.getElementById('hidden-ingresado-' + elementId);
                    if (hiddenInput) hiddenInput.value = (selectElement.value === "V°B° Ingresado a puerto") ? "true" : "false";
                    selectElement.style.color = (selectElement.value === "V°B° Ingresado a puerto") ? "#16a34a" : "";
                    selectElement.style.fontWeight = (selectElement.value === "V°B° Ingresado a puerto") ? "bold" : "";
                };

                window.syncEstadoLarConFecha = function (selectElement, elementId) {
                    window.syncEstadoLar(selectElement, elementId);
                    if (selectElement.value === "V°B° Ingresado a puerto") {
                        const ahora = new Date();
                        const pad = n => String(n).padStart(2, '0');
                        const fechaLocal = `${ahora.getFullYear()}-${pad(ahora.getMonth()+1)}-${pad(ahora.getDate())}T${pad(ahora.getHours())}:${pad(ahora.getMinutes())}`;
                        const form = selectElement.closest('form');
                        if (form) {
                            const inputFecha = form.querySelector('input[name="LlegadaPuertoReal"]');
                            if (inputFecha && !inputFecha.value) {
                                inputFecha.value = fechaLocal;
                                inputFecha.style.borderColor = "#16a34a";
                                inputFecha.style.background = "#f0fdf4";
                                setTimeout(() => { inputFecha.style.borderColor = ""; inputFecha.style.background = ""; }, 2000);
                            }
                        }
                    }
                };

                // Anillos de progreso en vivo (mismo motor que Index, adaptado a Detalle)
                // (movido a wwwroot/js/operaciones-common.js: window.actualizarAnilloPorPct — auditoría oct-2026, dedup JS)
                // (movido a wwwroot/js/operaciones-common.js: window.actualizarAnilloProgreso — auditoría oct-2026, dedup JS)

                window.recalcularProgresoMaritimoDet = function(idOp) {
                    const pane = document.getElementById(`det-maritimo`); if (!pane) return;
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

                window.recalcularProgresoGateDet = function(idOp) {
                    const pane = document.getElementById('det-gate'); if (!pane) return;
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

                document.addEventListener('DOMContentLoaded', function () {
                    const idOpRing = document.querySelector('input[name="IdOperacion"]')?.value || detalleOpData().id;
                    document.addEventListener('input', function (e) {
                        if (e.target.closest('#det-maritimo')) window.recalcularProgresoMaritimoDet(idOpRing);
                        else if (e.target.closest('#det-gate')) window.recalcularProgresoGateDet(idOpRing);
                    });
                    document.addEventListener('change', function (e) {
                        if (e.target.closest('#det-maritimo')) window.recalcularProgresoMaritimoDet(idOpRing);
                        else if (e.target.closest('#det-gate')) window.recalcularProgresoGateDet(idOpRing);
                    });
                });

