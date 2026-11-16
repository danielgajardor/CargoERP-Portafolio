// ==================== MONTOS A LA CHILENA (nov-2026) ====================
// Los campos de monto aceptan "450.000", "1.200.000", "1.200,75" o "2850.50" (antes eran
// <input type=number>: "1.200.000" no dejaba guardar y "380.000" quedaba como 380).
// Misma regla que el servidor (Helpers/MontoHelper.cs).
function leerMontoCl(txt) {
    var t = String(txt == null ? '' : txt).trim().replace(/[\s $]/g, '');
    if (!t) return NaN;
    var tieneComa = t.indexOf(',') >= 0, tienePunto = t.indexOf('.') >= 0;
    if (tieneComa && tienePunto) {
        t = t.lastIndexOf(',') > t.lastIndexOf('.') ? t.replace(/\./g, '').replace(',', '.') : t.replace(/,/g, '');
    } else if (tieneComa) {
        t = t.split(',').length > 2 ? t.replace(/,/g, '') : t.replace(',', '.');
    } else if (/^-?[1-9]\d{0,2}(\.\d{3})+$/.test(t)) {
        t = t.replace(/\./g, '');
    }
    return /^-?(\d+\.?\d*|\.\d+)$/.test(t) ? parseFloat(t) : NaN;
}
window.leerMontoCl = leerMontoCl;

var RE_CAMPO_MONTO = /(Valor|Precio|Tarifa|Monto|Rebate|Costo|Honorarios|VentaDoc|Plug|Profit|ControlSello|BlFee|FalsoFlete|Interplanta|Porteo)/i;
var RE_CAMPO_NO_MONTO = /(Horas|Dias|Días)/i;

function prepararInputMonto(el) {
    var valor = el.value;
    el.type = 'text';
    el.setAttribute('inputmode', 'decimal');
    el.setAttribute('autocomplete', 'off');
    el.setAttribute('pattern', '[0-9 .,$\\-]+');
    if (!el.title) el.title = 'Monto: 450.000 · 1.200,75 · 2850.50';
    el.removeAttribute('step');
    el.classList.add('input-monto-cl');
    el.value = valor;
}

function prepararMontos(raiz) {
    (raiz || document).querySelectorAll('input[type="number"]').forEach(function (el) {
        var clave = (el.name || '') + ' ' + (el.id || '');
        if (RE_CAMPO_MONTO.test(clave) && !RE_CAMPO_NO_MONTO.test(clave)) prepararInputMonto(el);
    });
}
document.addEventListener('DOMContentLoaded', function () { prepararMontos(document); });

// Al salir del campo se muestra el monto como quedará guardado (formato chileno).
document.addEventListener('focusout', function (e) {
    var el = e.target;
    if (!el || !el.classList || !el.classList.contains('input-monto-cl')) return;
    var v = leerMontoCl(el.value);
    if (el.value.trim() === '') { el.setCustomValidity(''); return; }
    if (isNaN(v)) { el.setCustomValidity('Monto no válido. Ejemplos: 450.000 · 1.200,75 · 2850.50'); el.reportValidity(); return; }
    el.setCustomValidity('');
    if (RE_CAMPO_NO_MONTO.test(el.name || '')) return; // horas / días: se dejan tal cual
    var dec = Math.round(v) === v ? 0 : 2;
    el.value = v.toLocaleString('es-CL', { minimumFractionDigits: dec, maximumFractionDigits: 2 });
});
document.addEventListener('input', function (e) {
    if (e.target && e.target.classList && e.target.classList.contains('input-monto-cl')) e.target.setCustomValidity('');
});


// Edición de tarifas directamente debajo de la fila seleccionada.
// Un único editor para proveedores/clientes, todas las familias y tarifas especiales.
let ultimoBotonTarifa = null;
document.addEventListener('click', function (event) {
    const btn = event.target.closest('button, a');
    if (btn) ultimoBotonTarifa = btn;
}, true);

const etiquetasTarifaFila = {
    TarifaUsd: 'Flete USD', RebateUsd: 'Rebate USD', ValorNeto: 'Valor neto',
    PrecioPactado: 'Precio pactado', TarifaBase: 'Tarifa base',
    TarifaConexionReefer: 'Conexión reefer', TarifaPublicaUsd: 'Tarifa pública USD',
    TarifaNetaUsd: 'Tarifa neta USD', Concepto: 'Concepto',
    Moneda: 'Moneda', AplicaIva: 'IVA', FechaInicioVigencia: 'Vigente desde',
    FechaFinVigencia: 'Vigente hasta', FechaVigenciaDesde: 'Vigente desde',
    FechaVigenciaHasta: 'Vigente hasta', TipoContenedor: 'Tipo de contenedor',
    Equipamiento: 'Equipamiento', Unidad: 'Tipo de cobro',
    EsServicioGratuito: 'Servicio gratuito', Pol: 'Origen (POL)', Pod: 'Destino (POD)',
    NombreTramo: 'Tramo', Comentarios: 'Observaciones', Codigo: 'Código naviera',
    TipoMovimiento: 'Tipo de movimiento', CondicionAplicacion: 'Condición de aplicación',
    EventoReferencia: 'Evento de referencia', HorasDesdeEvento: 'Horas desde evento',
    HorasHastaEvento: 'Horas hasta evento', GrupoConcepto: 'Grupo de concepto',
    DiasLibres: 'Días libres', DiasLibresOrigen: 'Días libres origen',
    DiasLibresDestino: 'Días libres destino', HorasLibresPlanta: 'Horas libres planta',
    HorasLibresPuerto: 'Horas libres puerto', Clasificacion: 'Clasificación',
    TipoCarga: 'Tipo de carga', EstadoContenedor: 'Estado del contenedor',
    TipoEmision: 'Tipo de emisión BL', TipoEmisionOtro: 'Otra emisión BL',
    LateArrivalSinCosto: 'Late Arrival sin costo',
    ExtraLateSinCosto: 'Extra Late sin costo',
    CorreccionBlSinCosto: 'Corrección BL sin costo',
    Commodity: 'Commodity', Plug: 'Plug'
};
function cerrarEditorTarifaFila() {
    document.querySelectorAll('.tarifa-inline-editor').forEach(el => el.remove());
    document.querySelectorAll('.tarifa-editando').forEach(el => el.classList.remove('tarifa-editando'));
}
async function abrirEditorTarifaFila(tipo, id) {
    cerrarEditorTarifaFila();
    const boton = ultimoBotonTarifa;
    const fila = boton?.closest('tr');
    const insertarDespues = fila || boton?.closest('.faf-card, .tab-pane, .table-container');
    if (!insertarDespues) {
        alert('No se encontró la fila para editar. Recarga esta pantalla y vuelve a intentarlo.');
        return;
    }
    const url = '?handler=FilaTarifa&tipoEntidad=' + encodeURIComponent(tipo) + '&idRegistro=' + encodeURIComponent(id);
    try {
        const respuesta = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!respuesta.ok) throw new Error('No se pudieron leer los datos de esta tarifa.');
        const datos = await respuesta.json();

        const contenedor = document.createElement('div');
        contenedor.className = 'tarifa-editor-contenido';
        const titulo = document.createElement('div');
        titulo.className = 'tarifa-editor-titulo';
        titulo.textContent = 'Editar tarifa en la fila · ' + tipo;
        contenedor.append(titulo);

        const form = document.createElement('form');
        form.method = 'post';
        form.action = '?handler=GuardarFilaTarifa';
        form.className = 'tarifa-editor-form';
        const campoOculto = (nombre, valor) => {
            const el = document.createElement('input');
            el.type = 'hidden'; el.name = nombre; el.value = String(valor ?? '');
            form.append(el);
        };
        campoOculto('tipoEntidad', tipo);
        campoOculto('idRegistro', id);
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (!token) throw new Error('No se pudo validar tu sesión. Actualiza la pantalla.');
        campoOculto('__RequestVerificationToken', token);
        const grid = document.createElement('div');
        grid.className = 'tarifa-editor-grid';
        (datos.campos || []).forEach(c => {
            const grupo = document.createElement('label');
            grupo.className = 'tarifa-editor-campo';
            const texto = document.createElement('span');
            texto.textContent = etiquetasTarifaFila[c.nombre] || c.nombre;
            grupo.append(texto);
            let control;
            if (c.nombre === 'Moneda' || c.tipo === 'bool' || c.nombre === 'TipoEmision' ||
                c.nombre === 'CorreccionBlSinCosto') {
                control = document.createElement('select');
                const opciones = c.nombre === 'Moneda' ? [['CLP','CLP'],['USD','USD']]
                    : c.nombre === 'TipoEmision' ? [
                        ['ORIGINALES','Originales'],['DESTINO','Destino'],['SEA_WAYBILL','Seawaybill'],
                        ['TELEX_RELEASE','Telex Release'],['EXPRESS_RELEASE','Express Release'],['OTRO','Otro']]
                    : c.nombre === 'CorreccionBlSinCosto' ? [
                        ['','Con costo'],['ANTES_ZARPE','Antes del zarpe'],['24H','Hasta 24h'],['48H','Hasta 48h']]
                    : [['true','Sí'],['false','No']];
                opciones.forEach(([valor,etiqueta]) => {
                    const opt = document.createElement('option');
                    opt.value = valor; opt.textContent = etiqueta;
                    control.append(opt);
                });
                control.value = c.valor || (c.tipo === 'bool' ? 'false' : 'CLP');
            } else if (c.nombre === 'Comentarios' || c.nombre === 'CondicionAplicacion') {
                control = document.createElement('textarea');
                control.rows = 2;
                control.value = c.valor || '';
            } else {
                control = document.createElement('input');
                control.type = c.tipo === 'fecha' ? 'date' : 'text';
                if (c.tipo === 'numero') prepararInputMonto(control);
                control.value = c.valor ?? '';
            }
            control.name = 'f_' + c.nombre;
            control.className = 'form-control form-control-sm';
            control.autocomplete = 'off';
            grupo.append(control);
            grid.append(grupo);
        });
        form.append(grid);
        const motivo = document.createElement('label');
        motivo.className = 'tarifa-editor-motivo';
        const textoMotivo = document.createElement('span');
        textoMotivo.textContent = 'Motivo del cambio (obligatorio · historial de auditoría)';
        const comentario = document.createElement('input');
        comentario.name = 'motivo'; comentario.type = 'text';
        comentario.className = 'form-control form-control-sm';
        comentario.placeholder = 'Describe por qué se modifica esta tarifa';
        comentario.required = true; comentario.minLength = 4;
        motivo.append(textoMotivo, comentario);
        form.append(motivo);
        const acciones = document.createElement('div');
        acciones.className = 'tarifa-editor-acciones';
        const cancelar = document.createElement('button');
        cancelar.type = 'button'; cancelar.className = 'btn btn-outline-secondary btn-sm';
        cancelar.textContent = 'Cancelar';
        cancelar.addEventListener('click', cerrarEditorTarifaFila);
        const guardar = document.createElement('button');
        guardar.type = 'submit'; guardar.className = 'btn btn-dark btn-sm fw-bold';
        guardar.textContent = 'Guardar cambios';
        acciones.append(cancelar, guardar);
        form.append(acciones);
        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            guardar.disabled = true;
            try {
                const res = await fetch(form.action, {
                    method: 'POST', body: new FormData(form), credentials: 'same-origin'
                });
                if (!res.ok) throw new Error((await res.text()).slice(0, 400) || 'No se pudo guardar.');
                window.location.reload();
            } catch (ex) {
                guardar.disabled = false;
                alert('Error al actualizar la tarifa: ' + ex.message);
            }
        });
        contenedor.append(form);
        if (fila) {
            const editor = document.createElement('tr');
            editor.className = 'tarifa-inline-editor';
            const celda = document.createElement('td');
            celda.colSpan = Math.max(1, fila.children.length);
            celda.append(contenedor);
            editor.append(celda);
            fila.insertAdjacentElement('afterend', editor);
            fila.classList.add('tarifa-editando');
        } else {
            const editor = document.createElement('div');
            editor.className = 'tarifa-inline-editor';
            editor.append(contenedor);
            insertarDespues.prepend(editor);
        }
        contenedor.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        comentario.focus({ preventScroll: true });
    } catch (error) {
        alert(error.message || 'No se pudo abrir el editor.');
    }
}

// Botones con la i (incluidos Collect y Emisión BL): resumen al pasar el mouse.
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('button[onclick*="abrirModalHistorial"]').forEach(boton => {
        boton.addEventListener('mouseenter', async () => {
            if (boton.dataset.historialCargado) return;
            const match = boton.getAttribute('onclick')?.match(/abrirModalHistorial\('([^']+)',\s*(\d+)\)/);
            if (!match) return;
            try {
                const res = await fetch('?handler=Historial&tipoEntidad=' + encodeURIComponent(match[1])
                    + '&idRegistro=' + match[2], { credentials: 'same-origin' });
                if (!res.ok) return;
                const registros = await res.json();
                const ultimo = registros[0];
                const resumen = ultimo ? 'Último: ' + ultimo.campo + ' · '
                    + (ultimo.valorAnterior ?? '—') + ' → ' + (ultimo.valorNuevo ?? '—')
                    + ' · ' + ultimo.usuario + ' · ' + ultimo.fecha
                    : 'Sin cambios. Haz clic para ver el historial completo.';
                boton.title = resumen;
                boton.dataset.historialCargado = '1';
                if (window.bootstrap?.Tooltip) {
                    const tt = bootstrap.Tooltip.getOrCreateInstance(boton, { container: 'body', trigger: 'hover focus' });
                    if (tt.setContent) tt.setContent({ '.tooltip-inner': resumen });
                    tt.show();
                }
            } catch (_) { }
        });
    });
});

// La i muestra un resumen al pasar el cursor; al hacer clic abre TODO el historial.
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.info-mod-icon').forEach(icono => {
        const fila = icono.closest('tr');
        const botonHistorial = fila?.querySelector('button[onclick*="abrirModalHistorial"]');
        if (!botonHistorial) return;
        const match = botonHistorial.getAttribute('onclick')?.match(/abrirModalHistorial\('([^']+)',\s*(\d+)\)/);
        if (!match) return;
        const tipo = match[1], id = Number(match[2]);
        icono.classList.remove('d-none');
        icono.style.cursor = 'pointer';
        icono.setAttribute('role', 'button');
        icono.setAttribute('tabindex', '0');
        icono.addEventListener('click', () => abrirModalHistorial(tipo, id));
        icono.addEventListener('keydown', e => {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); abrirModalHistorial(tipo, id); }
        });
        icono.addEventListener('mouseenter', async () => {
            if (icono.dataset.historialCargado) return;
            try {
                const res = await fetch('?handler=Historial&tipoEntidad=' + encodeURIComponent(tipo)
                    + '&idRegistro=' + id);
                if (!res.ok) return;
                const historial = await res.json();
                const ultimo = historial[0];
                const titulo = ultimo ? 'Último cambio: ' + ultimo.campo + ' · '
                    + (ultimo.valorAnterior ?? '—') + ' → ' + (ultimo.valorNuevo ?? '—')
                    + ' · ' + ultimo.usuario + ' · ' + ultimo.fecha
                    : 'Sin cambios registrados. Clic para ver historial.';
                icono.dataset.historialCargado = '1';
                icono.setAttribute('title', titulo);
                icono.setAttribute('data-bs-original-title', titulo);
                const tooltip = window.bootstrap?.Tooltip?.getInstance(icono);
                if (tooltip?.setContent) tooltip.setContent({ '.tooltip-inner': titulo });
            } catch (_) {}
        });
    });
});

        $(document).ready(function () {
            $('.select2-enable, .filtro-select').select2({ width: '100%' });
        });

        function toggleNuevaTarifaForm(id) {
    var row = document.getElementById(id);
    if (!row) return;
    row.classList.toggle('d-none');

    if (!row.classList.contains('d-none')) {
        // Los selectores chicos (moneda junto al monto, ¿aplica IVA?, etc.) quedan
        // nativos: si se convierten a select2 (ancho 100%) aplastan el campo del
        // monto que está al lado y no se puede digitar la tarifa — el bug "aplastado".
        $(row).find('select').not('.faf-money select, [data-iva-select], .no-select2').each(function () {
            if (!$(this).hasClass('select2-hidden-accessible')) {
                $(this).select2({
                    width: '100%',
                    dropdownParent: $('body')
                });
            }
        });
    }
}

                function abrirModalEditarTerrestre(id, valor, fecha) {
            return abrirEditorTarifaFila('Terrestre', id);
            document.getElementById('editTerId').value = id;
            document.getElementById('editTerValor').value = valor;
            document.getElementById('editTerFecha').value = fecha;
            document.getElementById('editTerComentario').value = '';
            new bootstrap.Modal(document.getElementById('modalEditarTerrestre')).show();
        }

        function abrirModalEditarMaritimo(id, valor, fecha, rebate, fechaFin) {
            return abrirEditorTarifaFila('Maritimo', id);
            document.getElementById('editMarId').value = id;
            document.getElementById('editMarValor').value = valor;
            document.getElementById('editMarRebate').value = rebate || '';
            document.getElementById('editMarFecha').value = fecha || '';
            const fechaFinInput = document.getElementById('editMarFechaFin');
            if (fechaFinInput) fechaFinInput.value = fechaFin || '';
            document.getElementById('editMarComentario').value = '';
            new bootstrap.Modal(document.getElementById('modalEditarMaritimo')).show();
        }

        function abrirModalEditarRebateCliente(id, rebate) {
            return abrirEditorTarifaFila('Cliente', id);
            document.getElementById('editRebCliId').value = id;
            document.getElementById('editRebCliValor').value = rebate || '';
            new bootstrap.Modal(document.getElementById('modalEditarRebateCliente')).show();
        }

                                function abrirModalUniversal(id, valor, fecha, tipo, comentariosTarifa, esGratuito) {
            return abrirEditorTarifaFila(tipo, id);
            document.getElementById('uniId').value = id;
            document.getElementById('uniValor').value = valor;
            document.getElementById('uniFecha').value = fecha;
            document.getElementById('uniTipo').value = tipo;
            document.getElementById('uniComentario').value = '';

            const wrapComentarios = document.getElementById('uniComentariosTarifaWrap');
            const inputComentarios = document.getElementById('uniComentariosTarifa');
            if (tipo === 'Almacenamiento') {
                wrapComentarios.style.display = 'block';
                inputComentarios.value = comentariosTarifa || '';
            } else {
                wrapComentarios.style.display = 'none';
                inputComentarios.value = '';
            }

            const wrapGratuito = document.getElementById('uniGratuitoWrap');
            const chkGratuito = document.getElementById('uniEsGratuito');
            if (tipo === 'Cliente' && esGratuito !== undefined) {
                wrapGratuito.style.display = 'block';
                chkGratuito.checked = (esGratuito === true || esGratuito === 'true');
            } else {
                wrapGratuito.style.display = 'none';
                chkGratuito.checked = false;
            }

            new bootstrap.Modal(document.getElementById('modalEditarUniversal')).show();
        }
        function abrirDetalleAlmacenamiento(nombreProveedor, comentarios) {
    document.getElementById('detalleAlmProveedor').innerText = nombreProveedor;
    const body = document.getElementById('detalleAlmBody');

    const texto = comentarios && comentarios.trim() !== '' ? comentarios : 'Sin comentarios registrados.';
    // Se muestra el texto tal cual fue escrito, respetando los saltos de línea
    // (white-space: pre-wrap), sin reinterpretarlo ni reformatearlo.
    body.innerHTML = `<div class="p-3 bg-white border rounded shadow-sm" style="white-space: pre-wrap; font-size:13px; color:#334155; line-height:1.6;">${texto.replace(/</g, '&lt;')}</div>`;

    new bootstrap.Modal(document.getElementById('modalDetalleAlmacenamiento')).show();
}

        // Checklist de "Tarifa Plana" (pestaña dedicada, Documental): tildar un certificado lo
        // agrega de inmediato como GRATIS, destildarlo lo archiva. Como el handler crea/archiva
        // una fila de verdad, recargamos la página para que el estado de los chips y las tablas
        // de detalle de las otras pestañas (SAG/SERNAPESCA/COMUNES) queden al día.
        // Antes de recargar la página tras guardar/cancelar algo de Tarifa Plana, guardamos qué
        // cliente estaba desplegado para volver a abrirlo apenas cargue — ver el bloque de
        // persistencia de pestañas más abajo, que es el que lo reabre.
        function prepararReloadTarifaPlana(idCliente) {
            if (idCliente) localStorage.setItem('tpReopenCliente', idCliente);
            location.reload();
        }

        function toggleCertificadoGratuito(checkbox) {
            const incluir = checkbox.checked;
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('idCliente', checkbox.dataset.idCliente);
            formData.append('clasificacion', checkbox.dataset.clasificacion);
            formData.append('concepto', checkbox.dataset.concepto);
            formData.append('incluir', incluir);
            checkbox.disabled = true;

            fetch('?handler=ToggleCertificadoGratuito', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                .then(res => res.json())
                .then(data => {
                    if (data.success) {
                        prepararReloadTarifaPlana(checkbox.dataset.idCliente);
                    } else {
                        checkbox.disabled = false;
                        checkbox.checked = !incluir;
                        Swal.fire('Error', data.message || 'No se pudo actualizar.', 'error');
                    }
                })
                .catch(() => {
                    checkbox.disabled = false;
                    checkbox.checked = !incluir;
                    Swal.fire('Error', 'Fallo de conexión.', 'error');
                });
        }

        // Pestaña "TARIFA PLANA", modo edición: "Editar" muestra los selectores "+ Agregar" de
        // los 3 grupos (SAG/SERNAPESCA/COMUNES) + los botones Guardar/Cancelar, para poder sumar
        // varios certificados SIN recargar la página en cada uno.
        function toggleEditarTarifaPlana(idCliente) {
            document.querySelectorAll('.tp-add-field[data-id-cliente="' + idCliente + '"]').forEach(function (el) {
                el.classList.remove('d-none');
            });
            var wrap = document.querySelector('.tp-guardar-wrap[data-id-cliente="' + idCliente + '"]');
            if (wrap) wrap.classList.remove('d-none');
            var btn = document.querySelector('.tp-editar-btn[data-id-cliente="' + idCliente + '"]');
            if (btn) btn.classList.add('d-none');
        }

        // Al elegir un certificado en cualquiera de los selectores "+ Agregar", NO se guarda
        // todavía: se agrega como chip "pendiente" (con una X para sacarlo) y se le quita esa
        // opción al selector para que no se pueda elegir 2 veces y quede listo de inmediato para
        // agregar otro. Recién se guarda todo junto al apretar "Guardar".
        function agregarPendienteTarifaPlana(select) {
            const concepto = select.value;
            if (!concepto) return;
            const chip = document.createElement('span');
            chip.className = 'd-flex align-items-center gap-1 px-2 py-1 rounded-pill pending-chip';
            chip.style.background = '#fef9e7';
            chip.style.border = '1px dashed #e9c46a';
            chip.style.fontSize = '10px';
            chip.dataset.idCliente = select.dataset.idCliente;
            chip.dataset.clasificacion = select.dataset.clasificacion;
            chip.dataset.concepto = concepto;
            chip.innerHTML = '<span class="text-uppercase fw-bold">' + concepto + '</span> <i class="bi bi-x-circle-fill" style="cursor:pointer; color:#c0392b;"></i>';
            chip.querySelector('i').addEventListener('click', function () { chip.remove(); });
            select.parentNode.insertBefore(chip, select);

            Array.from(select.options).forEach(function (opt) {
                if (opt.value === concepto) opt.remove();
            });
            select.value = '';
        }

        // "Guardar": manda todos los certificados pendientes (de los 3 grupos juntos) con una
        // llamada por cada uno, y recarga la página una sola vez al final (no por cada ítem).
        function guardarPendientesTarifaPlana(btn, idCliente) {
            const fila = document.getElementById('tp-row-' + idCliente);
            const pendientes = fila ? fila.querySelectorAll('.pending-chip') : [];
            if (pendientes.length === 0) {
                cancelarEdicionTarifaPlana(idCliente);
                return;
            }
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            btn.disabled = true;
            btn.innerText = 'Guardando...';

            const tareas = Array.from(pendientes).map(function (chip) {
                const formData = new FormData();
                formData.append('idCliente', chip.dataset.idCliente);
                formData.append('clasificacion', chip.dataset.clasificacion);
                formData.append('concepto', chip.dataset.concepto);
                formData.append('incluir', true);
                return fetch('?handler=ToggleCertificadoGratuito', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                    .then(res => res.json());
            });

            Promise.all(tareas)
                .then(resultados => {
                    const fallo = resultados.find(r => !r.success);
                    if (fallo) {
                        Swal.fire('Error', fallo.message || 'No se pudieron guardar todos los certificados.', 'error');
                        btn.disabled = false;
                        btn.innerText = 'Guardar';
                    } else {
                        prepararReloadTarifaPlana(idCliente);
                    }
                })
                .catch(() => {
                    Swal.fire('Error', 'Fallo de conexión al guardar.', 'error');
                    btn.disabled = false;
                    btn.innerText = 'Guardar';
                });
        }

        // "Cancelar": como nada se guardó todavía, basta recargar para descartar los pendientes
        // y volver al modo vista — quedando en la misma pestaña y con este cliente desplegado.
        function cancelarEdicionTarifaPlana(idCliente) {
            prepararReloadTarifaPlana(idCliente);
        }

        // Switch de la pestaña "TARIFA PLANA": activa/desactiva Cliente.EsClienteEspecial.
        // Recargamos para que el resto de la pestaña (los 3 grupos de checklist, que solo se
        // muestran si el switch está en ON) se redibuje con el estado correcto desde el servidor.
        function toggleClienteEspecial(checkbox) {
            const activo = checkbox.checked;
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('idCliente', checkbox.dataset.idCliente);
            formData.append('activo', activo);
            checkbox.disabled = true;

            fetch('?handler=ToggleClienteEspecial', { method: 'POST', headers: { 'RequestVerificationToken': token }, body: formData })
                .then(res => res.json())
                .then(data => {
                    if (data.success) {
                        prepararReloadTarifaPlana(checkbox.dataset.idCliente);
                    } else {
                        checkbox.disabled = false;
                        checkbox.checked = !activo;
                        Swal.fire('Error', data.message || 'No se pudo actualizar.', 'error');
                    }
                })
                .catch(() => {
                    checkbox.disabled = false;
                    checkbox.checked = !activo;
                    Swal.fire('Error', 'Fallo de conexión.', 'error');
                });
        }

        function abrirModalHistorial(tipoEntidad, idRegistro) {
            const body = document.getElementById('historialComercialBody');
            body.innerHTML = '<div class="text-center text-muted py-4"><span class="spinner-border spinner-border-sm me-2"></span>Cargando historial...</div>';
            new bootstrap.Modal(document.getElementById('modalHistorialComercial')).show();

            fetch(`?handler=Historial&tipoEntidad=${encodeURIComponent(tipoEntidad)}&idRegistro=${idRegistro}`)
                .then(res => res.json())
                .then(data => {
                    if (!data || data.length === 0) {
                        body.innerHTML = '<div class="text-center text-muted py-4"><i class="bi bi-inbox fs-2 d-block mb-2 opacity-25"></i>Sin cambios registrados aún.</div>';
                        return;
                    }
                    let html = '<div class="d-flex flex-column gap-2">';
                    const esc = (v) => String(v ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
                    data.forEach(h => {
                        h = { usuario: esc(h.usuario), fecha: esc(h.fecha), campo: esc(h.campo), valorAnterior: h.valorAnterior == null ? null : esc(h.valorAnterior), valorNuevo: h.valorNuevo == null ? null : esc(h.valorNuevo), comentario: h.comentario ? esc(h.comentario) : 'Sin comentario' };
                        html += `<div class="border rounded p-3 bg-white shadow-sm">
                                    <div class="d-flex justify-content-between align-items-center mb-2">
                                        <span class="fw-bold text-dark" style="font-size:11px; text-transform:uppercase;"><i class="bi bi-person-fill me-1 text-primary"></i>${h.usuario}</span>
                                        <span class="text-muted" style="font-size:10px;"><i class="bi bi-calendar3 me-1"></i>${h.fecha}</span>
                                    </div>
                                    <div class="mb-2" style="font-size:11px; color:#64748b; font-weight:700; text-transform:uppercase;">${h.campo}</div>
                                    <div class="d-flex align-items-center gap-2 mb-2">
                                        <span class="badge bg-danger bg-opacity-10 text-danger border border-danger border-opacity-25" style="font-size:11px;">${h.valorAnterior ?? '-'}</span>
                                        <i class="bi bi-arrow-right text-muted"></i>
                                        <span class="badge bg-success bg-opacity-10 text-success border border-success border-opacity-25" style="font-size:11px;">${h.valorNuevo ?? '-'}</span>
                                    </div>
                                    <div class="text-dark" style="font-size:12px; font-style:italic; border-left:3px solid #cbd5e1; padding-left:10px;">${h.comentario}</div>
                                </div>`;
                    });
                    html += '</div>';
                    body.innerHTML = html;
                })
                .catch(() => { body.innerHTML = '<div class="text-center text-danger py-4">No se pudo cargar el historial.</div>'; });
        }

        // Al cambiar entre Proveedores y Clientes se abre la misma pestaña del otro lado
        // (ej. Proveedores > Marítimo > RF Fresco Collect -> Clientes > Marítimo > RF Fresco
        // Collect). Las equivalentes tienen el mismo id con prefijo prov- / cli-.
        function sincronizarPestanaEquivalente(desde, hacia) {
            var prefijos = { '#panel-proveedores': 'prov-', '#panel-clientes': 'cli-' };
            if (!desde || !hacia || !prefijos[desde] || !prefijos[hacia]) return;
            var panelDesde = document.querySelector(desde);
            var panelHacia = document.querySelector(hacia);
            if (!panelDesde || !panelHacia) return;
            var traducir = function (target) {
                if (!target) return null;
                if (target === '#panel-almacenamiento' && hacia === '#panel-clientes') return '#cli-almacenamiento';
                if (target === '#cli-almacenamiento' && hacia === '#panel-proveedores') return '#panel-almacenamiento';
                return target.replace('#' + prefijos[desde], '#' + prefijos[hacia]);
            };
            var modActivo = panelDesde.querySelector('.nav-tabs-custom button.active');
            var modDestino = traducir(modActivo && modActivo.getAttribute('data-bs-target'));
            var modBtn = modDestino ? panelHacia.querySelector('.nav-tabs-custom button[data-bs-target="' + modDestino + '"]') : null;
            if (!modBtn) return;
            bootstrap.Tab.getOrCreateInstance(modBtn).show();
            var paneDesde = document.querySelector(modActivo.getAttribute('data-bs-target'));
            var pillActiva = paneDesde ? paneDesde.querySelector('ul.sub-tabs button.active') : null;
            var pillDestino = traducir(pillActiva && pillActiva.getAttribute('data-bs-target'));
            var paneHacia = document.querySelector(modDestino);
            var pillBtn = (pillDestino && paneHacia) ? paneHacia.querySelector('ul.sub-tabs button[data-bs-target="' + pillDestino + '"]') : null;
            if (pillBtn) bootstrap.Tab.getOrCreateInstance(pillBtn).show();
        }

        // Persistencia de pestaña activa (principal y sub-pestañas) entre recargas de página
        document.addEventListener('DOMContentLoaded', function () {
            var savedMain = localStorage.getItem('activeMainTab');
            var savedSub = localStorage.getItem('activeSubTab');
            if (savedMain) {
                var mainBtn = document.querySelector('#mainTabs button[data-bs-target="' + savedMain + '"]');
                if (mainBtn) new bootstrap.Tab(mainBtn).show();
            }
            if (savedSub) {
                var subBtn = document.querySelector('.nav-tabs-custom button[data-bs-target="' + savedSub + '"]');
                if (subBtn) new bootstrap.Tab(subBtn).show();
            }
            document.querySelectorAll('#mainTabs button').forEach(function (btn) {
                btn.addEventListener('shown.bs.tab', function (e) {
                    localStorage.setItem('activeMainTab', this.getAttribute('data-bs-target'));
                    sincronizarPestanaEquivalente(e.relatedTarget ? e.relatedTarget.getAttribute('data-bs-target') : null, this.getAttribute('data-bs-target'));
                });
            });
            document.querySelectorAll('.nav-tabs-custom button').forEach(function (btn) {
                btn.addEventListener('shown.bs.tab', function () {
                    localStorage.setItem('activeSubTab', this.getAttribute('data-bs-target'));
                });
            });

            // Nivel 3: cada grupo de pills (SAG/SERNAPESCA/COMUNES/TARIFA PLANA, Dry/Reefer, etc.
            // — hay uno de estos por cada bloque Marítimo/Terrestre/Documental/Courier, tanto en
            // Proveedores como en Clientes) se restaura por separado, guardado como un mapa
            // {idDelUl: targetActivo} en localStorage. Antes solo se guardaban los 2 primeros
            // niveles, así que al recargar siempre se volvía a SAG/Dry (el primero de cada grupo).
            var pillGroupsState = {};
            try { pillGroupsState = JSON.parse(localStorage.getItem('pillGroupsState') || '{}'); } catch (e) { pillGroupsState = {}; }
            document.querySelectorAll('ul.sub-tabs[id]').forEach(function (ul) {
                var savedTarget = pillGroupsState[ul.id];
                if (savedTarget) {
                    var pillBtn = ul.querySelector('button[data-bs-target="' + savedTarget + '"]');
                    if (pillBtn) new bootstrap.Tab(pillBtn).show();
                }
                ul.querySelectorAll('button').forEach(function (btn) {
                    btn.addEventListener('shown.bs.tab', function () {
                        pillGroupsState[ul.id] = this.getAttribute('data-bs-target');
                        localStorage.setItem('pillGroupsState', JSON.stringify(pillGroupsState));
                    });
                });
            });

            // Si venimos de un Guardar/Cancelar (o un check) de Tarifa Plana, reabrimos la fila
            // del cliente que estaba desplegada — una vez, y se limpia enseguida.
            var tpReopen = localStorage.getItem('tpReopenCliente');
            if (tpReopen) {
                localStorage.removeItem('tpReopenCliente');
                var filaTp = document.getElementById('tp-row-' + tpReopen);
                if (filaTp) {
                    new bootstrap.Collapse(filaTp, { toggle: true });
                    setTimeout(function () { filaTp.scrollIntoView({ behavior: 'smooth', block: 'center' }); }, 300);
                }
            }

            var tooltipList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
            tooltipList.map(function (el) { return new bootstrap.Tooltip(el); });
        });

// Courier (oct-2026): el formulario "Nuevo costo / Nueva venta courier" solo ofrece
// los conceptos de la pestaña activa — Nacional: COURIER NACIONAL; Internacional:
// las 4 zonas. Antes mostraba los 5 en ambas pestañas.
function filtrarConceptosCourier(prefijo) {
    var sel = document.getElementById('conceptoCourier-' + prefijo);
    if (!sel) return;
    if (!sel._todas) {
        sel._todas = Array.prototype.map.call(sel.options, function (o) {
            return { v: o.value, t: o.text, a: o.getAttribute('data-ambito') || '' };
        });
    }
    var activo = document.querySelector('#pills-' + prefijo + '-courier .nav-link.active');
    var destino = activo ? (activo.getAttribute('data-bs-target') || '') : '';
    var ambito = /-int$/.test(destino) ? 'int' : 'nac';
    var actual = sel.value;
    var visibles = sel._todas.filter(function (o) { return !o.a || o.a === ambito; });
    sel.innerHTML = '';
    visibles.forEach(function (o) {
        var op = new Option(o.t, o.v);
        if (o.a) op.setAttribute('data-ambito', o.a);
        sel.add(op);
    });
    var conceptos = visibles.filter(function (o) { return o.a; });
    sel.value = conceptos.some(function (o) { return o.v === actual; }) ? actual : (conceptos.length === 1 ? conceptos[0].v : '');
    if (window.jQuery) jQuery(sel).trigger('change.select2');
}

document.addEventListener('DOMContentLoaded', function () {
    ['prov', 'cli'].forEach(function (p) {
        filtrarConceptosCourier(p);
        document.querySelectorAll('#pills-' + p + '-courier [data-bs-toggle="pill"]').forEach(function (b) {
            b.addEventListener('shown.bs.tab', function () { filtrarConceptosCourier(p); });
        });
    });
});

// "Total con IVA" en los formularios de venta: se recalcula al escribir el monto,
// cambiar la moneda o el ¿Aplica IVA?.
function actualizarTotalIva(form) {
    if (!form) return;
    var destino = form.querySelector('.faf-total-iva');
    if (!destino) return;
    var monto = form.querySelector('.faf-money input');
    var moneda = form.querySelector('.faf-money select');
    var iva = form.querySelector('[data-iva-select]');
    var v = monto ? leerMontoCl(monto.value) : NaN;
    if (isNaN(v)) { destino.textContent = '—'; return; }
    // Componentes que suman al total (Gate CMA: Control Sello + BL Fee).
    form.querySelectorAll('[data-suma-total]').forEach(function (el) {
        var extra = leerMontoCl(el.value);
        if (!isNaN(extra)) v += extra;
    });
    var conIva = !iva || iva.value === 'true';
    var total = conIva ? v * 1.19 : v;
    var mon = moneda ? moneda.value : '';
    var dec = mon === 'CLP' ? 0 : 2;
    // Campo editable "Tarifa + IVA" (venta marítima): se escribe el valor, no un texto.
    if (destino.tagName === 'INPUT') {
        if (document.activeElement !== destino) destino.value = (Math.round(total * Math.pow(10, dec)) / Math.pow(10, dec)).toFixed(dec);
        return;
    }
    destino.textContent = (mon ? mon + ' ' : '') + total.toLocaleString('es-CL', { minimumFractionDigits: dec, maximumFractionDigits: dec }) + (conIva ? '' : ' (exento)');
}

// "Tarifa + IVA" editable: al escribir el valor con IVA se calcula la tarifa neta.
function calcularNetoDesdeIva(input) {
    var form = input.closest('form');
    if (!form) return;
    var monto = form.querySelector('.faf-money input');
    var moneda = form.querySelector('.faf-money select');
    if (!monto) return;
    var t = leerMontoCl(input.value);
    if (isNaN(t)) { monto.value = ''; return; }
    var iva = form.querySelector('[data-iva-select]');
    var conIva = !iva || iva.value === 'true';
    var neto = conIva ? t / 1.19 : t;
    form.querySelectorAll('[data-suma-total]').forEach(function (el) {
        var extra = leerMontoCl(el.value);
        if (!isNaN(extra)) neto -= extra;
    });
    var dec = (moneda && moneda.value === 'CLP') ? 0 : 2;
    monto.value = Math.max(0, Math.round(neto * Math.pow(10, dec)) / Math.pow(10, dec)).toFixed(dec);
}

document.addEventListener('input', function (e) {
    if (!e.target || !e.target.closest) return;
    if (e.target.hasAttribute('data-total-iva-input')) { calcularNetoDesdeIva(e.target); return; }
    if (e.target.closest('.faf-money') || e.target.hasAttribute('data-suma-total')) actualizarTotalIva(e.target.closest('form'));
});
document.addEventListener('change', function (e) {
    if (e.target && e.target.closest && e.target.closest('.faf-money') && e.target.tagName === 'SELECT') actualizarTotalIva(e.target.closest('form'));
});

// Venta documental al cliente: SAG o SERNAPESCA y al menos Honorarios o DUS.
function validarVentaDocumental(form) {
    var hon = leerMontoCl((form.querySelector('[name="VentaDocHonorarios"]') || {}).value || '');
    var dus = leerMontoCl((form.querySelector('[name="VentaDocDus"]') || {}).value || '');
    if (!(hon > 0) && !(dus > 0)) {
        var msg = 'Ingresa el valor de Honorarios (+ IVA) y/o del DUS (sin IVA).';
        if (window.Swal) Swal.fire({ icon: 'warning', title: 'Venta documental', text: msg, confirmButtonColor: '#0f172a' }); else alert(msg);
        return false;
    }
    return true;
}

// Honorarios + IVA: muestra el total con IVA bajo el campo.
document.addEventListener('input', function (e) {
    if (!e.target || !e.target.hasAttribute || !e.target.hasAttribute('data-doc-honorarios')) return;
    var form = e.target.closest('form');
    var hint = form && form.querySelector('[data-doc-honorarios-iva]');
    if (!hint) return;
    var v = leerMontoCl(e.target.value);
    var mon = (form.querySelector('[data-doc-moneda]') || {}).value || 'CLP';
    var dec = mon === 'CLP' ? 0 : 2;
    hint.textContent = isNaN(v) || v <= 0 ? 'por contenedor'
        : 'c/IVA ' + mon + ' ' + (v * 1.19).toLocaleString('es-CL', { minimumFractionDigits: dec, maximumFractionDigits: dec }) + ' · por contenedor';
});

// ==================== GATE OUT / IN ====================
// Filtros de las tablas Gate (proveedores y clientes): depósito, naviera,
// contenedor (solo 20' Dry, 40' Dry, 40' Reefer) y movimiento.
function filtrarGate(contenedor) {
    var ambito = contenedor.getAttribute('data-gate-filtros');
    var tabla = document.querySelector('table[data-gate-tabla="' + ambito + '"]');
    if (!tabla) return;
    var f = {};
    contenedor.querySelectorAll('[data-gate-filtro]').forEach(function (sel) { f[sel.getAttribute('data-gate-filtro')] = sel.value; });
    var visibles = 0;
    tabla.querySelectorAll('tbody tr[data-cont]').forEach(function (tr) {
        var dep = tr.getAttribute('data-dep') || '';
        var ok = (!f.dep || dep === f.dep || dep === 'TODOS')
            && (!f.nav || (tr.getAttribute('data-nav') || '') === f.nav)
            && (!f.cont || (' ' + (tr.getAttribute('data-cont') || '') + ' ').indexOf(' ' + f.cont + ' ') >= 0)
            && (!f.mov || (tr.getAttribute('data-mov') || '') === f.mov);
        tr.classList.toggle('d-none', !ok);
        if (ok) visibles++;
    });
    var vacia = tabla.querySelector('tr.gate-sin-filas');
    if (vacia) vacia.classList.toggle('d-none', visibles > 0);
}
document.addEventListener('change', function (e) {
    var cont = e.target && e.target.closest ? e.target.closest('[data-gate-filtros]') : null;
    if (cont) filtrarGate(cont);
});

// Checkbox Control Sello / BL Fee (CMA CGM): incluye o excluye el componente.
function toggleComponenteGate(chk, idTarifa) {
    var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
    var fd = new FormData();
    fd.append('idTarifa', idTarifa);
    fd.append('activo', chk.checked ? 'true' : 'false');
    chk.disabled = true;
    fetch('?handler=ToggleComponenteGate', { method: 'POST', headers: { 'RequestVerificationToken': tokenEl ? tokenEl.value : '' }, body: fd })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (!res || !res.ok) throw new Error('sin respuesta');
            // Recarga para recalcular el total de la línea con el mismo cálculo del servidor.
            window.location.reload();
        })
        .catch(function () {
            chk.checked = !chk.checked;
            chk.disabled = false;
            alert('No se pudo actualizar el componente. Intenta de nuevo.');
        });
}
document.addEventListener('change', function (e) {
    if (e.target && e.target.closest && (e.target.closest('.faf-money') || e.target.hasAttribute('data-iva-select'))) actualizarTotalIva(e.target.closest('form'));
});

// Tras una carga masiva de ventas, mostrar el panel Clientes donde queda el resultado.
document.addEventListener('DOMContentLoaded', function () {
    if (!document.querySelector('[data-carga-masiva-msg]')) return;
    var btnCli = document.querySelector('#mainTabs button[data-bs-target="#panel-clientes"]');
    if (btnCli && window.bootstrap) new bootstrap.Tab(btnCli).show();
});

// ==================== RF FRESCO COLLECT (oct-2026) ====================
// Rebate = Tarifa Pública − Tarifa Neta, se muestra mientras se escribe.
document.addEventListener('input', function (e) {
    if (!e.target || !e.target.closest) return;
    if (!e.target.hasAttribute('data-collect-publica') && !e.target.hasAttribute('data-collect-neta')) return;
    var form = e.target.closest('form');
    var pub = leerMontoCl((form.querySelector('[data-collect-publica]') || {}).value || '');
    var neta = leerMontoCl((form.querySelector('[data-collect-neta]') || {}).value || '');
    var out = form.querySelector('[data-collect-rebate]');
    if (!out) return;
    if (isNaN(pub) || isNaN(neta)) { out.textContent = '—'; out.style.color = ''; return; }
    var reb = pub - neta;
    out.textContent = 'USD ' + reb.toLocaleString('es-CL', { maximumFractionDigits: 2 });
    out.style.color = reb < 0 ? '#dc2626' : '#16a34a';
});

function validarTarifaCollect(form) {
    var pub = leerMontoCl((form.querySelector('[data-collect-publica]') || {}).value || '');
    var neta = leerMontoCl((form.querySelector('[data-collect-neta]') || {}).value || '');
    var msg = null;
    if (!(pub > 0)) msg = 'Ingresa la Tarifa Pública.';
    else if (isNaN(neta) || neta < 0) msg = 'Ingresa la Tarifa Neta.';
    else if (neta > pub) msg = 'La Tarifa Neta no puede ser mayor que la Pública (el rebate quedaría negativo).';
    if (msg) {
        if (window.Swal) Swal.fire({ icon: 'warning', title: 'Tarifa Collect', text: msg, confirmButtonColor: '#0f172a' }); else alert(msg);
        return false;
    }
    return true;
}

// Pestañas sin mezclarse (oct-2026): con clics rápidos, la animación "fade" de Bootstrap
// dejaba dos paneles activos a la vez en la misma pestaña (ej. Courier visible estando en
// Documental). Tras cada cambio se deja activo solo el panel del último botón elegido de
// esa lista.
(function () {
    function botonesDe(lista) {
        return Array.prototype.filter.call(lista.querySelectorAll('[data-bs-toggle="tab"],[data-bs-toggle="pill"]'),
            function (b) { return b.closest('ul, .nav') === lista; });
    }
    function normalizar(lista) {
        var elegido = lista._ultimoTab || lista.querySelector('[data-bs-toggle].active');
        if (!elegido) return;
        botonesDe(lista).forEach(function (b) {
            var pane = document.querySelector(b.getAttribute('data-bs-target'));
            var activo = b === elegido;
            b.classList.toggle('active', activo);
            b.setAttribute('aria-selected', activo ? 'true' : 'false');
            if (pane) {
                pane.classList.toggle('active', activo);
                pane.classList.toggle('show', activo);
            }
        });
    }
    document.addEventListener('show.bs.tab', function (e) {
        var lista = e.target.closest('ul, .nav');
        if (lista) lista._ultimoTab = e.target;
    });
    document.addEventListener('shown.bs.tab', function (e) {
        var lista = e.target.closest('ul, .nav');
        if (!lista) return;
        normalizar(lista);
        // Por si una transición anterior termina después de esta.
        clearTimeout(lista._timerTab);
        lista._timerTab = setTimeout(function () { normalizar(lista); }, 250);
    });
})();
