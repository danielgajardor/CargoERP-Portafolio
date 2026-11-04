/* Traspaso Operaciones -> Finanzas (oct-2026)
 * - finalizarTarea(id, booking): Operaciones finaliza la tarea (contenedores embarcados
 *   y nave zarpada) y Finanzas recibe el booking como NUEVO.
 * - abrirReabrirTarea(id, booking): reabre con un motivo y, opcional, un archivo de respaldo
 *   (correo del cliente, factura, BL corregido...); Finanzas lo ve como REABIERTA.
 * - Si el servidor rechaza un cambio porque la tarea está finalizada (409 con el
 *   encabezado X-Tarea-Bloqueada), se muestra el aviso con el botón para reabrir.
 */
(function () {
    'use strict';

    var MOTIVOS = ['Corrección de BL', 'Nuevo gasto o extracosto', 'Cambio de contenedor', 'Rolleo', 'Otro'];
    var EXT_RESPALDO = ['.pdf', '.jpg', '.jpeg', '.png', '.webp', '.xlsx', '.xls', '.csv', '.docx', '.doc', '.msg', '.eml', '.txt', '.zip'];
    var MAX_RESPALDO = 15 * 1024 * 1024;

    function tamano(b) {
        return b >= 1048576 ? (b / 1048576).toFixed(1).replace('.', ',') + ' MB' : Math.max(1, Math.round(b / 1024)) + ' KB';
    }

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function aviso(icono, titulo, texto) {
        if (window.Swal) return Swal.fire({ icon: icono, title: titulo, html: esc(texto), confirmButtonColor: '#0f172a' });
        alert(titulo + '\n\n' + texto);
        return Promise.resolve();
    }

    function post(handler, datos) {
        var fd = new FormData();
        Object.keys(datos).forEach(function (k) { fd.append(k, datos[k] == null ? '' : datos[k]); });
        fd.append('__RequestVerificationToken', token());
        return fetch('/Operaciones/Index?handler=' + handler, {
            method: 'POST',
            body: fd,
            headers: { 'RequestVerificationToken': token(), 'X-Requested-With': 'XMLHttpRequest' }
        }).then(function (r) { return r.json(); });
    }

    window.finalizarTarea = function (id, booking, ev) {
        if (ev) { ev.preventDefault(); ev.stopPropagation(); }
        var confirmar = window.Swal
            ? Swal.fire({
                icon: 'question',
                title: 'Finalizar tarea · ' + esc(booking || ''),
                html: '<div style="text-align:left;font-size:13px;line-height:1.5;">'
                    + 'Confirmas que los contenedores están <b>embarcados</b> y la nave <b>zarpó</b>.<br>'
                    + 'Finanzas recibirá el booking con la alerta <b>NUEVO</b> y los cambios de costos y cobros '
                    + 'quedarán bloqueados. Si después hay que corregir algo (p. ej. una corrección de BL), '
                    + 'usa <b>Reabrir operación</b>.</div>',
                showCancelButton: true,
                confirmButtonText: 'Finalizar tarea',
                cancelButtonText: 'Cancelar',
                confirmButtonColor: '#15803d'
            }).then(function (r) { return r.isConfirmed; })
            : Promise.resolve(confirm('¿Finalizar la tarea y traspasar el booking a Finanzas?'));

        confirmar.then(function (ok) {
            if (!ok) return;
            post('FinalizarTarea', { id: id }).then(function (d) {
                if (d.success) aviso('success', 'Tarea finalizada', d.message).then(function () { location.reload(); });
                else aviso('warning', 'No se pudo finalizar', d.message || 'Revisa los requisitos.');
            }).catch(function () { aviso('error', 'Error', 'No se pudo conectar con el servidor.'); });
        });
    };

    window.abrirReabrirTarea = function (id, booking, ev) {
        if (ev) { ev.preventDefault(); ev.stopPropagation(); }
        if (!window.Swal) {
            var m = prompt('Motivo de la reapertura (' + MOTIVOS.join(', ') + '):', MOTIVOS[0]);
            if (!m) return;
            post('ReabrirTarea', { id: id, motivo: m, comentario: '' }).then(function (d) {
                alert(d.message); if (d.success) location.reload();
            });
            return;
        }
        var opciones = MOTIVOS.map(function (m) { return '<option value="' + esc(m) + '">' + esc(m) + '</option>'; }).join('');
        Swal.fire({
            title: 'Reabrir operación · ' + esc(booking || ''),
            html: '<div style="text-align:left;font-size:13px;">'
                + '<p style="margin:0 0 10px;color:#475569;">Finanzas verá el booking como <b>REABIERTA</b> con este motivo. '
                + 'Cuando termines los cambios, vuelve a <b>Finalizar tarea</b>.</p>'
                + '<label style="font-size:11px;font-weight:800;text-transform:uppercase;color:#64748b;">Motivo</label>'
                + '<select id="tfMotivo" class="form-select form-select-sm mb-2"><option value="">Elige el motivo…</option>' + opciones + '</select>'
                + '<label style="font-size:11px;font-weight:800;text-transform:uppercase;color:#64748b;">Comentario <span id="tfComReq" style="color:#dc2626;display:none;">*</span></label>'
                + '<textarea id="tfComentario" class="form-control form-control-sm mb-2" rows="3" maxlength="400" placeholder="Qué hay que cambiar (opcional, obligatorio si es Otro)"></textarea>'
                + '<label style="font-size:11px;font-weight:800;text-transform:uppercase;color:#64748b;">Archivo de respaldo <span style="font-weight:600;text-transform:none;">(opcional)</span></label>'
                + '<label for="tfRespaldo" class="tf-respaldo" id="tfRespaldoZona">'
                + '<i class="bi bi-paperclip"></i><span id="tfRespaldoTxt">Adjunta el correo, factura o BL que respalda el cambio</span>'
                + '<button type="button" id="tfRespaldoQuitar" class="tf-respaldo__quitar" title="Quitar archivo" style="display:none;">&times;</button>'
                + '</label>'
                + '<input type="file" id="tfRespaldo" style="display:none;" accept="' + EXT_RESPALDO.join(',') + '">'
                + '<div style="font-size:11px;color:#94a3b8;margin-top:4px;">PDF, imagen, Excel, Word, correo (.msg/.eml), texto o ZIP · hasta 15 MB. Finanzas lo verá junto al aviso de reapertura.</div>'
                + '</div>',
            showCancelButton: true,
            confirmButtonText: 'Reabrir operación',
            cancelButtonText: 'Cancelar',
            confirmButtonColor: '#c2410c',
            didOpen: function () {
                var sel = document.getElementById('tfMotivo');
                sel.addEventListener('change', function () {
                    document.getElementById('tfComReq').style.display = sel.value === 'Otro' ? 'inline' : 'none';
                });
                var inp = document.getElementById('tfRespaldo');
                var zona = document.getElementById('tfRespaldoZona');
                var txt = document.getElementById('tfRespaldoTxt');
                var quitar = document.getElementById('tfRespaldoQuitar');
                var textoInicial = txt.textContent;
                function pintar() {
                    var f = inp.files && inp.files[0];
                    zona.classList.toggle('con-archivo', !!f);
                    txt.textContent = f ? f.name + ' · ' + tamano(f.size) : textoInicial;
                    quitar.style.display = f ? 'inline-flex' : 'none';
                    Swal.resetValidationMessage();
                }
                inp.addEventListener('change', pintar);
                quitar.addEventListener('click', function (e) { e.preventDefault(); e.stopPropagation(); inp.value = ''; pintar(); });
                ['dragenter', 'dragover'].forEach(function (t) {
                    zona.addEventListener(t, function (e) { e.preventDefault(); zona.classList.add('arrastrando'); });
                });
                ['dragleave', 'drop'].forEach(function (t) {
                    zona.addEventListener(t, function (e) { e.preventDefault(); zona.classList.remove('arrastrando'); });
                });
                zona.addEventListener('drop', function (e) {
                    if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length) {
                        try { inp.files = e.dataTransfer.files; } catch (x) { }
                        pintar();
                    }
                });
            },
            preConfirm: function () {
                var motivo = document.getElementById('tfMotivo').value;
                var comentario = document.getElementById('tfComentario').value.trim();
                if (!motivo) { Swal.showValidationMessage('Elige el motivo.'); return false; }
                if (motivo === 'Otro' && !comentario) { Swal.showValidationMessage('Con "Otro" escribe el comentario.'); return false; }
                var inp = document.getElementById('tfRespaldo');
                var archivo = inp.files && inp.files[0] ? inp.files[0] : null;
                if (archivo) {
                    var nombre = archivo.name.toLowerCase();
                    var ext = nombre.lastIndexOf('.') >= 0 ? nombre.substring(nombre.lastIndexOf('.')) : '';
                    if (EXT_RESPALDO.indexOf(ext) < 0) { Swal.showValidationMessage('Tipo de archivo no permitido. Usa PDF, imagen, Excel, Word, correo, texto o ZIP.'); return false; }
                    if (archivo.size > MAX_RESPALDO) { Swal.showValidationMessage('El archivo supera los 15 MB.'); return false; }
                    if (archivo.size === 0) { Swal.showValidationMessage('El archivo está vacío.'); return false; }
                }
                return { motivo: motivo, comentario: comentario, archivo: archivo };
            }
        }).then(function (r) {
            if (!r.isConfirmed) return;
            var datos = { id: id, motivo: r.value.motivo, comentario: r.value.comentario };
            if (r.value.archivo) datos.respaldo = r.value.archivo;
            Swal.fire({ title: 'Reabriendo…', allowOutsideClick: false, showConfirmButton: false, didOpen: function () { Swal.showLoading(); } });
            post('ReabrirTarea', datos).then(function (d) {
                if (d.success) aviso('success', 'Operación reabierta', d.message).then(function () { location.reload(); });
                else aviso('warning', 'No se pudo reabrir', d.message);
            }).catch(function () { aviso('error', 'Error', 'No se pudo conectar con el servidor.'); });
        });
    };

    function mostrarBloqueo(mensaje, idOperacion) {
        var el = document.querySelector('[data-tarea-booking]');
        var booking = el ? el.getAttribute('data-tarea-booking') : '';
        if (!window.Swal) { alert(mensaje); return; }
        Swal.fire({
            icon: 'info',
            title: 'Tarea finalizada',
            html: esc(mensaje),
            showCancelButton: !!idOperacion,
            confirmButtonText: 'Entendido',
            cancelButtonText: 'Reabrir operación',
            confirmButtonColor: '#0f172a',
            cancelButtonColor: '#c2410c'
        }).then(function (r) {
            if (idOperacion && r.dismiss === Swal.DismissReason.cancel) window.abrirReabrirTarea(idOperacion, booking);
        });
    }
    window.mostrarBloqueoTarea = mostrarBloqueo;

    // Respuestas 409 del filtro de bloqueo (fetch y jQuery).
    if (window.fetch && !window.__traspasoFetch) {
        window.__traspasoFetch = true;
        var fetchOriginal = window.fetch;
        window.fetch = function () {
            return fetchOriginal.apply(this, arguments).then(function (resp) {
                if (resp.status === 409 && resp.headers.get('X-Tarea-Bloqueada')) {
                    return resp.clone().json().catch(function () { return {}; }).then(function (d) {
                        mostrarBloqueo(d.message || 'La tarea está finalizada.', d.idOperacion || parseInt(resp.headers.get('X-Tarea-Bloqueada'), 10));
                        return new Promise(function () { }); // corta la cadena: no recarga ni muestra otro error
                    });
                }
                return resp;
            });
        };
    }
    document.addEventListener('DOMContentLoaded', function () {
        if (window.jQuery) {
            jQuery(document).ajaxError(function (e, xhr) {
                if (xhr && xhr.status === 409 && xhr.getResponseHeader('X-Tarea-Bloqueada')) {
                    var d = xhr.responseJSON || {};
                    mostrarBloqueo(d.message || 'La tarea está finalizada.', d.idOperacion);
                }
            });
        }
    });
})();
