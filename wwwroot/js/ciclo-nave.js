/* =====================================================================================
   Ciclo de la nave en el Mainboard (13-nov-2026)
   - Línea "Real" bajo la ETD (Zarpe Real) y bajo la ETA (Arribo Efectivo): un clic abre el
     registro de la fecha, con la opción de enviar la confirmación al cliente.
   - Alertas: aviso de llegada (correo con vista previa) y chequeos de demoras a 5 y 3 días.
   La lógica de servidor está en Services/CicloNaveServicio.cs.
   ===================================================================================== */
(function () {
    'use strict';

    function token() {
        const el = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]')
            || document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    async function post(handler, datos) {
        const body = new URLSearchParams();
        Object.keys(datos).forEach(k => { if (datos[k] !== undefined && datos[k] !== null) body.append(k, datos[k]); });
        const r = await fetch('/Operaciones/Index?handler=' + handler, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token(), 'Content-Type': 'application/x-www-form-urlencoded' },
            body
        });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        return r.json();
    }

    function aviso(tipo, texto) {
        if (typeof window.avisoFlash === 'function') window.avisoFlash(tipo, texto);
        else if (window.Swal) Swal.fire({ icon: tipo === 'error' ? 'error' : (tipo === 'info' ? 'info' : 'success'), text: texto, timer: 2600, showConfirmButton: false });
    }

    function ahoraLocal() {
        const d = new Date();
        d.setSeconds(0, 0);
        const p = n => String(n).padStart(2, '0');
        return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
    }

    // Recarga con los mismos filtros (la fila vuelve con su fecha y sus alertas al día).
    function recargar() {
        setTimeout(() => window.location.reload(), 900);
    }

    function botonDe(idOp, tipo) {
        return document.querySelector(`.mb-fecha-real[data-op="${idOp}"][data-tipo="${tipo}"]`);
    }

    // ---------------------------------------------------------------- Zarpe / Arribo
    window.cicloNaveEditar = function (ev, btn) {
        if (ev) { ev.preventDefault(); ev.stopPropagation(); }
        if (!btn) return;
        const tipo = btn.dataset.tipo, idOp = btn.dataset.op;
        const esZarpe = tipo === 'zarpe';
        const valor = btn.dataset.valor || '';
        const correo = btn.dataset.correo || '';
        const modalEl = document.getElementById('modalCicloNave');
        if (!modalEl) return;

        document.getElementById('cnKicker').textContent = 'Ciclo de la nave · BKG ' + (btn.dataset.booking || '');
        document.getElementById('cnTitulo').textContent = esZarpe ? 'Zarpe Real' : 'Arribo Efectivo';
        document.getElementById('cnEtiqueta').textContent = esZarpe ? 'Fecha y hora real del zarpe' : 'Fecha y hora real del arribo a destino';
        document.getElementById('cnAyuda').textContent = esZarpe
            ? 'Al guardar se apaga la alerta de la ETD y los contenedores pasan a «Zarpe».'
            : 'Al guardar se apaga la alerta de arribo, los contenedores pasan a «Arribado» y se activa «Retiro pendiente».';
        const fecha = document.getElementById('cnFecha');
        fecha.value = valor || ahoraLocal();
        fecha.max = ahoraLocal();
        const notif = document.getElementById('cnNotificar');
        document.getElementById('cnNotificarTxt').textContent = esZarpe ? 'Enviar la confirmación de zarpe a los clientes' : 'Enviar la confirmación de arribo a los clientes';
        notif.checked = !valor;
        notif.disabled = false;
        // 13-nov: la fecha es de la nave: se lista a quién le llega (los bookings marítimos de su salida).
        const cajaCorreo = document.getElementById('cnCorreo');
        let conCorreo = correo ? 1 : 0;
        cajaCorreo.textContent = 'Buscando los clientes de la nave…';
        const esc = t => String(t ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
        fetch('/Operaciones/Itinerarios?handler=Destinatarios&tipo=' + tipo + '&idOperacion=' + encodeURIComponent(idOp))
            .then(r => r.json())
            .then(d => {
                const lista = d.lista || [];
                conCorreo = lista.filter(x => x.correo).length;
                notif.disabled = conCorreo === 0;
                if (notif.disabled) notif.checked = false;
                cajaCorreo.innerHTML = (d.salida ? 'Nave <strong>' + esc(d.salida) + '</strong>: ' : '')
                    + (lista.length ? lista.length + ' booking(s) reciben la fecha:' : 'ningún booking pendiente de esta fecha.')
                    + (lista.length ? '<ul class="cn-dest">' + lista.map(x => '<li><span>' + esc(x.booking) + ' · ' + esc(x.cliente) + '</span><span class="' + (x.correo ? '' : 'text-danger fw-bold') + '">' + esc(x.correo || 'sin correo') + '</span></li>').join('') + '</ul>' : '');
            })
            .catch(() => {
                cajaCorreo.innerHTML = correo ? 'Se envía a <strong>' + esc(correo) + '</strong>.' : '<span class="text-danger">El cliente no tiene correo registrado.</span>';
                notif.disabled = !correo;
            });

        const borrar = document.getElementById('cnBorrar');
        borrar.classList.toggle('d-none', !valor);
        const guardar = document.getElementById('cnGuardar');

        async function enviar(valorFecha, notificar) {
            guardar.disabled = borrar.disabled = true;
            try {
                const d = await post('CicloNaveFecha', { id: idOp, tipo, fecha: valorFecha, notificar: notificar ? 'true' : 'false' });
                if (!d.success) { aviso('error', d.message || 'No se pudo guardar.'); return; }
                bootstrap.Modal.getOrCreateInstance(modalEl).hide();
                if (d.avisoCorreo && window.Swal) await Swal.fire({ icon: 'warning', title: 'Fecha guardada', text: d.message });
                else aviso('ok', d.message);
                recargar(idOp);
            } catch (e) {
                aviso('error', 'Fallo de conexión.');
            } finally {
                guardar.disabled = borrar.disabled = false;
            }
        }

        guardar.onclick = async () => {
            if (!fecha.value) { fecha.classList.add('is-invalid'); fecha.focus(); return; }
            fecha.classList.remove('is-invalid');
            let notificar = notif.checked && !notif.disabled;
            // Se pregunta antes de enviar a los clientes.
            if (notificar && window.Swal) {
                const q = await Swal.fire({
                    icon: 'question', title: esZarpe ? '¿Enviar la confirmación de zarpe?' : '¿Enviar la confirmación de arribo?',
                    text: 'Se enviará un correo a ' + conCorreo + ' cliente(s) de la nave.',
                    showCancelButton: true, showDenyButton: true, confirmButtonText: 'Sí, guardar y enviar', denyButtonText: 'Guardar sin enviar', cancelButtonText: 'Cancelar', confirmButtonColor: '#0f172a'
                });
                if (q.isDismissed) return;
                if (q.isDenied) notificar = false;
            }
            enviar(fecha.value, notificar);
        };
        borrar.onclick = async () => {
            if (window.Swal) {
                const q = await Swal.fire({
                    icon: 'question', title: esZarpe ? '¿Borrar el Zarpe Real?' : '¿Borrar el Arribo Efectivo?',
                    text: 'La alerta vuelve a aparecer. Los estados de los contenedores no se retroceden.',
                    showCancelButton: true, confirmButtonText: 'Borrar', cancelButtonText: 'Cancelar', confirmButtonColor: '#dc2626'
                });
                if (!q.isConfirmed) return;
            }
            enviar('', false);
        };
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    };

    // Desde el panel de alertas: abre el mismo registro usando el botón de la fila.
    window.cicloNaveDesdeAlerta = function (idOp, tipo) {
        const btn = botonDe(idOp, tipo);
        const modalAlertas = document.getElementById('modalAlertas');
        if (modalAlertas) bootstrap.Modal.getOrCreateInstance(modalAlertas).hide();
        if (btn) setTimeout(() => window.cicloNaveEditar(null, btn), 250);
    };

    // ---------------------------------------------------------------- Aviso de llegada
    window.cicloNaveAvisoLlegada = async function (idOp) {
        const modalAlertas = document.getElementById('modalAlertas');
        if (modalAlertas) bootstrap.Modal.getOrCreateInstance(modalAlertas).hide();
        const modalEl = document.getElementById('modalAvisoLlegada');
        const correo = document.getElementById('alCorreo');
        const mensaje = document.getElementById('alMensaje');
        const previa = document.getElementById('alPrevia');
        const enviarBtn = document.getElementById('alEnviar');
        let d;
        try { d = await post('PrevisualizarAvisoLlegada', { id: idOp }); }
        catch (e) { aviso('error', 'Fallo de conexión.'); return; }
        if (!d.success) { aviso('error', d.message || 'No se pudo preparar el aviso.'); return; }

        const btn = botonDe(idOp, 'arribo');
        document.getElementById('alTitulo').textContent = 'Aviso de llegada · BKG ' + ((btn && btn.dataset.booking) || '');
        correo.value = d.correo || '';
        mensaje.value = d.mensajeDefault || '';
        previa.srcdoc = d.html;
        const smtp = document.getElementById('alSmtp');
        smtp.textContent = d.smtpConfigurado ? '' : d.smtpMensaje;
        smtp.classList.toggle('text-danger', !d.smtpConfigurado);

        let t = null;
        mensaje.oninput = () => {
            clearTimeout(t);
            t = setTimeout(async () => {
                try {
                    const p = await post('PrevisualizarAvisoLlegada', { id: idOp, mensaje: mensaje.value });
                    if (p.success) previa.srcdoc = p.html;
                } catch (e) { /* la vista previa se actualiza en el próximo cambio */ }
            }, 450);
        };
        enviarBtn.onclick = async () => {
            if (!correo.value.trim() || !correo.checkValidity()) { correo.classList.add('is-invalid'); correo.focus(); return; }
            correo.classList.remove('is-invalid');
            enviarBtn.disabled = true;
            try {
                const r = await post('EnviarAvisoLlegada', { id: idOp, correoDestino: correo.value.trim(), mensaje: mensaje.value });
                if (!r.success) { aviso('error', r.message || 'No se pudo enviar.'); return; }
                bootstrap.Modal.getOrCreateInstance(modalEl).hide();
                aviso('ok', r.message);
                recargar(idOp);
            } catch (e) {
                aviso('error', 'Fallo de conexión.');
            } finally {
                enviarBtn.disabled = false;
            }
        };
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    };

    // ---------------------------------------------------------------- Chequeos 5 / 3 días
    window.cicloNaveRevision = async function (idOp, dias) {
        try {
            const d = await post('RevisionNave', { id: idOp, dias });
            if (!d.success) { aviso('error', d.message || 'No se pudo registrar.'); return; }
            const modalAlertas = document.getElementById('modalAlertas');
            if (modalAlertas) bootstrap.Modal.getOrCreateInstance(modalAlertas).hide();
            aviso('ok', 'Revisión registrada: la nave va sin demoras.');
            recargar(idOp);
        } catch (e) {
            aviso('error', 'Fallo de conexión.');
        }
    };
})();
