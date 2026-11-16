// Sitio público sin servidor (Netlify), nov-2026. Lo agrega Herramientas/exportar_sitio_estatico.py
// al comienzo de cada página exportada y hace lo que en el sitio real hace el servidor:
//   - Dólar, UF y euro en vivo desde mindicador.cl (datos del Banco Central); si no responde,
//     los valores guardados al exportar.
//   - Asistente: busca la respuesta entre las respuestas reales guardadas al exportar.
//   - Contacto y registros: misma validación que el servidor y envío a Netlify Forms (sin contraseñas).
//   - Login: igual al real, pero nunca deja entrar.
//   - Itinerarios: la búsqueda filtra las salidas en la misma página.
(function () {
    'use strict';
    var DATOS = '/datos-sitio/';
    var fetchOriginal = window.fetch.bind(window);
    var ingles = function () { return document.documentElement.lang === 'en'; };
    var normalizar = function (s) {
        return (s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
            .replace(/[^a-z0-9 ]/g, ' ').replace(/\s+/g, ' ').trim();
    };
    var json = function (obj, estado) {
        return new Response(JSON.stringify(obj), { status: estado || 200, headers: { 'Content-Type': 'application/json' } });
    };
    var esperar = function (ms) { return new Promise(function (ok) { setTimeout(ok, ms); }); };
    function correoContacto() {
        var f = document.getElementById('asisForm');
        if (f && f.dataset.correo) return f.dataset.correo;
        var a = document.querySelector('a[href^="mailto:"]');
        return a ? a.getAttribute('href').replace(/^mailto:/, '').split('?')[0] : '';
    }

    // ------------------------------------------------------------- indicadores
    var indicadoresPromesa = null;
    function indicadores() {
        if (indicadoresPromesa) return indicadoresPromesa;
        var guardados = fetchOriginal(DATOS + 'indicadores.json').then(function (r) { return r.ok ? r.json() : {}; }).catch(function () { return {}; });
        var control = typeof AbortController === 'function' ? new AbortController() : null;
        var plazo = setTimeout(function () { if (control) control.abort(); }, 4500);
        var enVivo = fetchOriginal('https://mindicador.cl/api', control ? { signal: control.signal } : {})
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
            .then(function (d) {
                var dato = function (x) {
                    if (!x || !x.valor) return null;
                    var f = new Date(x.fecha);
                    var dd = ('0' + f.getDate()).slice(-2) + '-' + ('0' + (f.getMonth() + 1)).slice(-2) + '-' + f.getFullYear();
                    return { valor: x.valor, fecha: dd, fuente: 'mindicador.cl', respaldo: false };
                };
                var r = { uf: dato(d.uf), dolar: dato(d.dolar), euro: dato(d.euro) };
                if (!r.uf || !r.dolar) throw new Error('incompleto');
                return r;
            })
            .finally(function () { clearTimeout(plazo); });
        indicadoresPromesa = enVivo.catch(function () { return guardados; });
        return indicadoresPromesa;
    }

    // --------------------------------------------------------------- asistente
    var VACIAS = {};
    ('que cual cuales como cuando donde cuanto cuanta cuantos para por con los las del una uno unos unas este esta esto ese esa hay tiene tienen tengo quiero puedo pueden puede ustedes usted ser son esta estan mas muy pero sus mis tus nos les sin sobre entre hasta desde '
        + 'the and for are you your what which when where how can does with this that have has from about there please want need tell').split(' ').forEach(function (p) { VACIAS[p] = true; });
    var bases = {};
    function baseAsistente(idioma) {
        if (!bases[idioma]) bases[idioma] = fetchOriginal(DATOS + 'asistente-' + idioma + '.json').then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); });
        return bases[idioma];
    }
    // Pregunta ya respondida más parecida (palabras en común), con su puntaje.
    function parecida(base, q) {
        var palabrasQ = q.split(' ').filter(function (p) { return p.length >= 3 && !VACIAS[p]; });
        var mejor = null, puntaje = 0, comunesMejor = 0;
        Object.keys(base.preguntas).forEach(function (k) {
            var pk = k.split(' ').filter(function (p) { return p.length >= 3 && !VACIAS[p]; });
            if (!pk.length) return;
            var comunes = pk.filter(function (p) { return palabrasQ.indexOf(p) >= 0; });
            if (!comunes.some(function (p) { return p.length >= 4; })) return;
            var s = comunes.length / pk.length + comunes.length * 0.01;
            if (s > puntaje) { puntaje = s; mejor = k; comunesMejor = comunes.length; }
        });
        return { clave: mejor, puntaje: puntaje, comunes: comunesMejor };
    }
    function buscarRespuesta(base, pregunta) {
        var q = normalizar(pregunta);
        var r = function (i) { return base.respuestas[i]; };
        // 1. La misma pregunta (una sugerencia o un tema).
        if (base.preguntas[q] !== undefined) return r(base.preguntas[q]);
        // 2. Casi la misma pregunta (dos o más palabras en común y casi todas las de la guardada).
        var p = parecida(base, q);
        if (p.clave && p.comunes >= 2 && p.puntaje >= 0.75) return r(base.preguntas[p.clave]);
        // 3. Palabras clave de los temas: gana la clave más larga que aparezca completa.
        var rodeada = ' ' + q + ' ', mejor = null, largo = 0;
        base.temas.forEach(function (t) {
            t.claves.forEach(function (c) {
                if (c && rodeada.indexOf(' ' + c + ' ') >= 0 && c.length > largo) { largo = c.length; mejor = t; }
            });
        });
        if (mejor) return r(mejor.r);
        // 4. Una pregunta guardada bastante parecida.
        if (p.clave && p.puntaje >= 0.6) return r(base.preguntas[p.clave]);
        // 5. Palabras con errores menores o en plural: se compara el comienzo de cada palabra.
        var palabras = q.split(' ').filter(function (x) { return x.length >= 5; });
        base.temas.forEach(function (t) {
            t.claves.forEach(function (c) {
                if (c.indexOf(' ') >= 0 || c.length < 5) return;
                palabras.forEach(function (x) {
                    var n = Math.min(6, c.length, x.length);
                    if (x.slice(0, n) === c.slice(0, n) && c.length > largo) { largo = c.length; mejor = t; }
                });
            });
        });
        if (mejor) return r(mejor.r);
        return base.otra !== null && base.otra !== undefined ? r(base.otra) : null;
    }
    function completar(texto, valores, idioma) {
        var h = new Date().getHours();
        var saludo = idioma === 'en' ? (h < 12 ? 'Good morning' : h < 19 ? 'Good afternoon' : 'Good evening')
            : (h < 12 ? 'Buenos días' : h < 20 ? 'Buenas tardes' : 'Buenas noches');
        var fmt = new Intl.NumberFormat(idioma === 'en' ? 'en-US' : 'es-CL', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        var MESES = idioma === 'en'
            ? ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']
            : ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
        return texto.replace(/\{saludo\}/g, saludo).replace(/\{(uf|dolar|euro)\}/g, function (_, k) {
            return valores && valores[k] && valores[k].valor ? fmt.format(valores[k].valor) : 'N/D';
        }).replace(/\{f-(uf|dolar|euro)\}/g, function (_, k) {
            var m = valores && valores[k] && /^(\d{2})-(\d{2})/.exec(valores[k].fecha || '');
            if (!m) return idioma === 'en' ? 'today' : 'hoy';
            var d = +m[1], mes = MESES[+m[2] - 1];
            return idioma === 'en' ? mes + ' ' + d : d + ' de ' + mes;
        });
    }
    function responderAsistente(cuerpo) {
        var historial = [], idioma = 'es';
        try { historial = JSON.parse(cuerpo.get('historial') || '[]'); idioma = cuerpo.get('idioma') === 'en' ? 'en' : 'es'; } catch (e) { /* vacío */ }
        var ultima = historial.filter(function (m) { return m.rol === 'user'; }).pop();
        if (!ultima) return Promise.resolve(json({}, 400));
        return Promise.all([baseAsistente(idioma), indicadores()]).then(function (res) {
            var r = buscarRespuesta(res[0], ultima.texto);
            if (!r) throw new Error('sin respuesta');
            return json({ texto: completar(r.texto, res[1], idioma), acciones: r.acciones || [], sugerencias: r.sugerencias || [], estado: null, ia: false });
        });
    }

    // ------------------------------------------------------- Netlify Forms
    function enviarANetlify(nombre, campos) {
        var cuerpo = new URLSearchParams();
        cuerpo.append('form-name', nombre);
        Object.keys(campos).forEach(function (k) { cuerpo.append(k, campos[k] == null ? '' : campos[k]); });
        return fetchOriginal('/', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: cuerpo.toString() })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return true; });
    }

    // ---------------------------------------------------------------- contacto
    function responderContacto(cuerpo) {
        var v = function (k) { return String(cuerpo.get(k) || '').trim(); };
        var nombre = v('Nombre'), empresa = v('Empresa'), correo = v('Correo'), mensaje = v('Mensaje');
        var correoSitio = correoContacto();
        var error = function (m, campo) { return json({ ok: false, mensaje: m, campo: campo }); };
        if (v('SitioWeb')) return Promise.resolve(json({ ok: true, mensaje: 'Recibimos tu mensaje. Te responderemos a la brevedad.' }));
        if (nombre.length < 2 || nombre.length > 100) return Promise.resolve(error('Escribe tu nombre.', 'Nombre'));
        if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(correo) || correo.length > 120) return Promise.resolve(error('Revisa el correo: debe tener el formato nombre@empresa.cl.', 'Correo'));
        if (mensaje.length < 10) return Promise.resolve(error('Cuéntanos un poco más en el mensaje (al menos 10 caracteres).', 'Mensaje'));
        if (mensaje.length > 3000) return Promise.resolve(error('El mensaje es muy largo: resúmelo en menos de 3.000 caracteres.', 'Mensaje'));
        return enviarANetlify('contacto', { Nombre: nombre, Empresa: empresa.slice(0, 120), Correo: correo, Mensaje: mensaje })
            .then(function () { return json({ ok: true, mensaje: 'Recibimos tu mensaje. Te responderemos a la brevedad.' }); })
            .catch(function () {
                return json({ ok: false, mensaje: 'No pudimos enviar tu mensaje en este momento. Escríbenos directo a ' + correoSitio + '.', mailto: correoSitio ? 'mailto:' + correoSitio : null });
            });
    }

    // ------------------------------------------- llamadas al servidor (fetch)
    window.fetch = function (recurso, opciones) {
        var url = typeof recurso === 'string' ? recurso : (recurso && recurso.url) || '';
        var cuerpo = opciones && opciones.body;
        if (/[?&]handler=asistente/i.test(url) && cuerpo instanceof FormData) return responderAsistente(cuerpo);
        if (/[?&]handler=contacto/i.test(url) && cuerpo instanceof FormData) return responderContacto(cuerpo);
        if (url.indexOf(DATOS + 'indicadores.json') >= 0) return indicadores().then(function (d) { return json(d); });
        return fetchOriginal.apply(window, arguments);
    };

    // ------------------------------------------------------------------- login
    function prepararLogin() {
        var form = document.getElementById('loginForm');
        if (!form) return;
        // El script de la página muestra el cargador y llama a form.submit(): aquí se responde
        // como el servidor cuando la clave no corresponde. Las credenciales no salen del navegador.
        form.submit = function () {
            esperar(500).then(function () {
                var loader = document.getElementById('loader-overlay');
                if (loader) loader.classList.remove('active');
                var aviso = document.querySelector('.login-aviso.error');
                if (!aviso) {
                    aviso = document.createElement('div');
                    aviso.className = 'login-aviso error';
                    aviso.setAttribute('role', 'alert');
                    aviso.innerHTML = '<i class="bi bi-exclamation-circle"></i><span></span>';
                    var info = document.getElementById('avisoBooking');
                    form.parentNode.insertBefore(aviso, info || form);
                }
                aviso.querySelector('span').textContent = 'Credenciales incorrectas o cuenta inactiva.';
                var clave = document.getElementById('inpPass');
                if (clave) clave.value = '';
                var correo = document.getElementById('inpCorreo');
                if (correo) correo.focus();
            });
        };
    }

    // --------------------------------------------------------------- registros
    var REGLAS = {
        'registro-empresa': [
            ['Form.RazonSocial', 'Escribe la razón social.', 100, 'La razón social admite hasta 100 caracteres.'],
            ['Form.Rut', 'Escribe el RUT de la empresa.', 12, 'Revisa el RUT (ej: 76.123.456-7).'],
            ['Form.DireccionOficina', 'Escribe la dirección de la oficina.', 255, 'La dirección admite hasta 255 caracteres.'],
            ['Form.NombreContacto', 'Escribe el nombre de la persona de contacto.', 100, 'El nombre admite hasta 100 caracteres.'],
            ['Form.Telefono', 'Escribe un teléfono de contacto.', 50, 'El teléfono admite hasta 50 caracteres.'],
            ['Form.Correo', 'Escribe tu correo.', 100, 'El correo admite hasta 100 caracteres.'],
            ['Form.Password', 'Elige una contraseña.', 100, 'La contraseña debe tener al menos 8 caracteres.'],
            ['Form.ConfirmarPassword', 'Repite la contraseña.', 0, '']
        ],
        'registro-equipo': [
            ['Form.NombreCompleto', 'Escribe tu nombre y apellido.', 100, 'El nombre admite hasta 100 caracteres.'],
            ['Form.Rut', 'Escribe tu RUT.', 12, 'Revisa el RUT (ej: 12.345.678-5).'],
            ['Form.Telefono', 'Escribe tu teléfono.', 50, 'El teléfono admite hasta 50 caracteres.'],
            ['Form.Correo', 'Escribe tu correo.', 100, 'El correo admite hasta 100 caracteres.'],
            ['Form.Password', 'Elige una contraseña.', 100, 'La contraseña debe tener al menos 8 caracteres.'],
            ['Form.ConfirmarPassword', 'Repite la contraseña.', 0, '']
        ]
    };
    var EXITO = {
        'registro-empresa': {
            texto: function (c) { return 'Atilson revisará los datos de tu empresa. Te avisaremos ' + (c ? 'a ' + c : 'por correo') + ' cuando tu cuenta esté aprobada; desde ese momento podrás entrar con tu correo y la contraseña que elegiste.'; },
            pasos: [['Solicitud recibida', 'Ya le avisamos al equipo de Atilson.'], ['Revisamos los datos de tu empresa', 'Te avisamos apenas quede revisada.'], ['Te avisamos y entras al portal', 'Con tu correo y la contraseña que elegiste.']],
            error: 'No pudimos guardar tu solicitud por un problema de nuestro sistema. Inténtalo más tarde o escríbenos.'
        },
        'registro-equipo': {
            texto: function (c) { return 'Un administrador revisará tus datos y te asignará el rol que corresponde. Te avisaremos ' + (c ? 'a ' + c : 'por correo') + ' cuando tu cuenta esté validada; desde ese momento podrás entrar con tu correo y la contraseña que elegiste.'; },
            pasos: [['Solicitud recibida', 'Ya les avisamos a los administradores.'], ['Un administrador te asigna tu rol', 'Operaciones, Ventas, Finanzas u otro.'], ['Te avisamos y entras al ERP', 'Con tu correo y la contraseña que elegiste.']],
            error: 'No pudimos guardar tu solicitud por un problema de nuestro sistema. Avísale a tu jefatura.'
        }
    };
    function rutValido(rut) {
        var l = String(rut || '').replace(/[^0-9kK]/g, '').toUpperCase();
        if (l.length < 8 || l.length > 9) return false;
        var cuerpo = l.slice(0, -1), dv = l.slice(-1);
        if (!/^\d+$/.test(cuerpo) || !/^[\dK]$/.test(dv)) return false;
        var suma = 0, factor = 2;
        for (var i = cuerpo.length - 1; i >= 0; i--) { suma += (+cuerpo[i]) * factor; factor = factor === 7 ? 2 : factor + 1; }
        var resto = 11 - (suma % 11);
        return (resto === 11 ? '0' : resto === 10 ? 'K' : String(resto)) === dv;
    }
    var esc = function (s) { return String(s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); };

    function enviarRegistro(form, tipo) {
        var campo = function (n) { return form.querySelector('[name="' + n + '"]'); };
        var val = function (n) { var c = campo(n); return c ? c.value.trim() : ''; };
        var errores = [];
        form.querySelectorAll('.input-validation-error').forEach(function (x) { x.classList.remove('input-validation-error'); });
        form.querySelectorAll('.rg-error').forEach(function (x) { x.textContent = ''; x.className = 'rg-error field-validation-valid'; });
        var marcar = function (n, m) {
            errores.push(m);
            var c = campo(n); if (c) c.classList.add('input-validation-error');
            var s = form.querySelector('[data-valmsg-for="' + n + '"]');
            if (s) { s.textContent = m; s.className = 'rg-error field-validation-error'; }
        };
        REGLAS[tipo].forEach(function (r) {
            var v = val(r[0]);
            if (!v) marcar(r[0], r[1]);
            else if (r[0] === 'Form.Password' && v.length < 8) marcar(r[0], r[3]);
            else if (r[2] && v.length > r[2]) marcar(r[0], r[3]);
            else if (r[0] === 'Form.Correo' && !/^[^@\s]+@[^@\s]+$/.test(v)) marcar(r[0], 'Revisa el correo.');
            else if (r[0] === 'Form.ConfirmarPassword' && v !== val('Form.Password')) marcar(r[0], 'Las contraseñas no coinciden.');
        });
        var rut = val('Form.Rut'), clave = val('Form.Password');
        if (rut && rut.length <= 12 && !rutValido(rut)) marcar('Form.Rut', 'El RUT no es válido. Revisa los números y el dígito verificador.');
        if (clave.length >= 8 && (!/[a-záéíóúñü]/i.test(clave) || !/\d/.test(clave))) marcar('Form.Password', 'La contraseña debe tener letras y números.');

        var resumen = form.querySelector('.rg-errores[role="alert"]');
        var mostrarErrores = function (lista) {
            if (!resumen) return;
            resumen.className = 'rg-errores validation-summary-errors';
            resumen.innerHTML = '<strong>Revisa lo siguiente:</strong><ul>' + lista.map(function (m) { return '<li>' + esc(m) + '</li>'; }).join('') + '</ul>';
            resumen.scrollIntoView({ block: 'start' });
        };
        if (errores.length) {
            // Como al volver del servidor: las contraseñas quedan en blanco.
            ['Form.Password', 'Form.ConfirmarPassword'].forEach(function (n) { var c = campo(n); if (c) c.value = ''; });
            mostrarErrores(errores);
            return;
        }
        if (val('Form.SitioWeb')) return; // campo trampa: un robot

        var btn = document.getElementById('btnRegistro');
        if (btn) { btn.disabled = true; var sp = btn.querySelector('span'); if (sp) sp.textContent = 'Enviando…'; }
        // A Netlify van los datos de la solicitud, nunca las contraseñas.
        var datos = {};
        form.querySelectorAll('input, select, textarea').forEach(function (c) {
            if (!c.name || c.type === 'password' || c.type === 'submit' || /Mostrado$|SitioWeb$|__RequestVerificationToken/.test(c.name)) return;
            datos[c.name.replace(/^Form\./, '')] = c.value.trim();
        });
        enviarANetlify(tipo, datos).then(function () {
            var main = form.closest('.rg-main') || form.parentNode;
            Array.prototype.slice.call(main.children).forEach(function (n) { if (!n.classList.contains('rg-top')) n.remove(); });
            var e = EXITO[tipo];
            var ok = document.createElement('div');
            ok.className = 'rg-ok'; ok.setAttribute('role', 'status');
            ok.innerHTML = '<svg class="rg-ok-check" viewBox="0 0 64 64" aria-hidden="true"><circle cx="32" cy="32" r="30" /><path d="M20 33 l8 8 l16 -17" /></svg>'
                + '<h1>¡Listo! Recibimos tu solicitud</h1><p>' + esc(e.texto(datos.Correo || '')) + '</p><ol class="rg-ok-pasos">'
                + e.pasos.map(function (p, i) { return '<li' + (i === 0 ? ' class="hecho"' : '') + '><span>' + (i === 0 ? '<i class="bi bi-check-lg"></i>' : (i + 1)) + '</span><div><b>' + esc(p[0]) + '</b>' + esc(p[1]) + '</div></li>'; }).join('')
                + '</ol><div class="rg-ok-acciones"><a href="/Login" class="rg-btn"><i class="bi bi-box-arrow-in-right"></i> Ir a iniciar sesión</a><a href="/" class="rg-btn rg-btn-sec"><i class="bi bi-house"></i> Volver al sitio</a></div>';
            main.appendChild(ok);
            window.scrollTo(0, 0);
        }).catch(function () {
            ['Form.Password', 'Form.ConfirmarPassword'].forEach(function (n) { var c = campo(n); if (c) c.value = ''; });
            mostrarErrores([EXITO[tipo].error]);
        }).finally(function () {
            if (btn) { btn.disabled = false; var sp = btn.querySelector('span'); if (sp) sp.textContent = 'Enviar solicitud'; }
        });
    }

    // -------------------------------------------------------------- itinerarios
    function filtrarItinerarios(form, actualizarUrl) {
        var items = document.querySelectorAll('.it-salida');
        if (!items.length) return;
        var sel = function (id) { var s = document.getElementById(id); return s && s.selectedIndex > 0 ? s.options[s.selectedIndex] : null; };
        var pol = sel('busPol'), pod = sel('busPod'), nav = sel('busNav');
        var nombre = function (o) { return o ? o.textContent.split(',')[0].trim() : null; };
        var n = 0;
        items.forEach(function (it) {
            var ver = (!pol || it.dataset.pol === pol.dataset.clave) && (!pod || it.dataset.pod === pod.dataset.clave) && (!nav || it.dataset.naviera === nav.value);
            it.style.display = ver ? 'contents' : 'none';
            if (ver) n++;
        });
        document.querySelectorAll('.it-pweek').forEach(function (s) {
            s.hidden = !Array.prototype.some.call(s.querySelectorAll('.it-salida'), function (it) { return it.style.display !== 'none'; });
        });
        var filtrando = !!(pol || pod || nav);
        var ruta = (nombre(pol) || 'cualquier origen') + ' → ' + (nombre(pod) || 'cualquier destino');
        var cab = document.querySelector('.it-cab'), aviso = document.querySelector('.it-aviso'), cuerpo = document.querySelector('.it-cuerpo');
        var vacio = document.getElementById('itVacioFiltro');
        if (cab) {
            var h2 = cab.querySelector('h2');
            if (h2) h2.textContent = n + ' salida' + (n === 1 ? '' : 's') + ' ' + (filtrando ? 'para ' + ruta : 'disponibles');
            var todas = cab.querySelector('a[href="/Itinerarios"]');
            if (filtrando && !todas) { todas = document.createElement('a'); todas.href = '/Itinerarios'; todas.textContent = 'Ver todas las salidas'; cab.appendChild(todas); }
            if (!filtrando && todas) todas.remove();
            cab.hidden = n === 0;
        }
        if (aviso) aviso.hidden = n === 0;
        if (n === 0 && cuerpo) {
            if (!vacio) { vacio = document.createElement('div'); vacio.className = 'it-vacio'; vacio.id = 'itVacioFiltro'; cuerpo.insertBefore(vacio, cuerpo.firstChild); }
            vacio.innerHTML = '<i class="bi bi-calendar-x" aria-hidden="true"></i><h3>No encontramos salidas para ' + esc(ruta) + '</h3>'
                + '<p>Prueba otra combinación o pídenos la ruta: te respondemos con tarifa y opciones de nave.</p><div class="acciones">'
                + '<a href="/Itinerarios" class="btn-hero btn-hero-borde">Ver todas las salidas</a>'
                + '<a href="/PortalCliente/SolicitarCotizacion" class="btn-hero btn-hero-indigo"><i class="bi bi-calculator"></i> Cotizar servicio</a>'
                + '<a href="/#contacto" class="btn-hero btn-hero-borde"><i class="bi bi-envelope"></i> Escribirnos</a></div>';
        } else if (vacio) vacio.remove();
        if (actualizarUrl) {
            var q = new URLSearchParams();
            if (pol) q.set('pol', pol.value); if (pod) q.set('pod', pod.value); if (nav) q.set('naviera', nav.value);
            try { history.replaceState(null, '', '/Itinerarios' + (q.toString() ? '?' + q : '')); } catch (e) { /* nada */ }
        }
    }
    function prepararItinerarios() {
        var form = document.querySelector('.it-filtro');
        if (!form || !document.querySelector('.it-salida')) return;
        var q = new URLSearchParams(location.search);
        [['busPol', 'pol'], ['busPod', 'pod'], ['busNav', 'naviera']].forEach(function (p) {
            var s = document.getElementById(p[0]), v = q.get(p[1]);
            if (s && v) s.value = v;
        });
        if (q.has('pol') || q.has('pod') || q.has('naviera')) filtrarItinerarios(form, false);
    }

    // ------------------------------------------------- envío de formularios
    window.addEventListener('submit', function (e) {
        var f = e.target;
        if (!(f instanceof HTMLFormElement)) return;
        if (f.classList.contains('it-filtro')) {
            e.preventDefault(); e.stopImmediatePropagation();
            filtrarItinerarios(f, true);
            return;
        }
        if (f.id === 'formRegistro') {
            e.preventDefault(); e.stopImmediatePropagation();
            enviarRegistro(f, /registroequipo/i.test(location.pathname) ? 'registro-equipo' : 'registro-empresa');
        }
        // Login, contacto y asistente siguen con el código de la página (ver arriba).
    }, true);

    function alCargar() { prepararLogin(); prepararItinerarios(); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', alCargar); else alCargar();
})();
