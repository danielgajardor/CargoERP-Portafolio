# Exporta el sitio público como sitio estático para Netlify (nov-2026).
#
# Netlify solo sirve archivos (no corre ASP.NET). Esta herramienta toma las páginas
# públicas de la app en marcha y las deja funcionando igual que en el sitio real, sin servidor:
#   - Landing, servicios, Nosotros, Itinerarios, Login, Registro y Registro del equipo.
#   - Dólar, UF y euro: se cargan en vivo desde mindicador.cl (datos del Banco Central);
#     si esa fuente no responde, se muestran los valores del momento de la exportación.
#   - Noticias de Mundo Marítimo: las del momento de la exportación, con sus imágenes.
#   - Asistente: responde con las respuestas reales del asistente, guardadas al exportar
#     (todas las sugerencias y todos los temas de su base de conocimiento, en español e inglés).
#   - Itinerarios: las salidas del momento de la exportación, con la búsqueda funcionando.
#   - Formularios de contacto y de registro: se validan igual que en el servidor y llegan a
#     Netlify Forms (panel de Netlify › Forms), sin las contraseñas.
#   - Login: se ve y se comporta como el real, pero nunca deja entrar (siempre
#     "Credenciales incorrectas o cuenta inactiva."). No envía las credenciales a ningún lado.
#   - Las rutas del sistema (/App, /PortalCliente, /Operaciones, ...) llevan al login.
#   - No se copia wwwroot/uploads ni nada que las páginas públicas no usen.
#     Las páginas llevan "noindex" para que Google no las confunda con el sitio oficial.
#
# Uso (con la app corriendo en el mismo computador, por ejemplo con `dotnet run --launch-profile https`):
#   python Herramientas/exportar_sitio_estatico.py --url https://localhost:7046 --inseguro
# --inseguro acepta el certificado de desarrollo de localhost. Deja la carpeta "sitio-netlify"
# (o la de --salida) y un .zip al lado. Para publicar: arrastrar la carpeta a
# https://app.netlify.com/drop, o `npx netlify-cli deploy --dir sitio-netlify --prod`.
# Para que lleguen los formularios: en Netlify, Site configuration › Forms › Enable form detection
# y volver a subir la carpeta.
import argparse
import html as html_mod
import http.cookiejar
import json
import os
import re
import shutil
import ssl
import sys
import unicodedata
import urllib.error
import urllib.parse
import urllib.request

RAIZ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
WWWROOT = os.path.join(RAIZ, 'wwwroot')
JS_ESTATICO = os.path.join(RAIZ, 'Herramientas', 'sitio_estatico', 'sitio-estatico.js')
CONOCIMIENTO = os.path.join(RAIZ, 'Services', 'AsistenteConocimiento.cs')

INICIO = ['/', '/Nosotros', '/Itinerarios', '/Login', '/Registro', '/RegistroEquipo']
# Partes del sistema: nunca se exportan; en Netlify llevan al login.
PRIVADAS = ['/App', '/PortalCliente', '/Portal', '/Operaciones', '/Finanzas', '/Comercial', '/Logout', '/__']
HANDLERS_POST = {'asistente', 'contacto', 'imagen'}
EXTRA = ['/js/i18n/en.json', '/favicon.ico']
DATOS = 'datos-sitio'
# Formularios que llegan a Netlify Forms: (página o None para cualquiera, id del formulario) -> nombre en Netlify.
FORMULARIOS = {(None, 'formContacto'): 'contacto', ('/Registro', 'formRegistro'): 'registro-empresa', ('/RegistroEquipo', 'formRegistro'): 'registro-equipo'}
MAX_PREGUNTAS = 400  # por idioma

_abridor = None


def preparar_red(inseguro, base, cookie=None):
    global _abridor
    ctx = ssl.create_default_context()
    if inseguro:
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
    jarra = http.cookiejar.CookieJar()
    # La sesión (y la cookie del antiforgery que pide el asistente) se mantiene entre pedidos.
    host = urllib.parse.urlparse(base).hostname or 'localhost'
    for par in (cookie or '').split(';'):
        if '=' in par:
            k, v = par.strip().split('=', 1)
            jarra.set_cookie(http.cookiejar.Cookie(0, k, v, None, False, host, False, False, '/', True, False, None, False, None, None, {}))
    _abridor = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ctx), urllib.request.HTTPCookieProcessor(jarra))


def pedir(url, datos=None, tipo=None):
    cab = {'User-Agent': 'AtilsonExportador/1.0', 'Accept-Language': 'es-CL,es;q=0.9'}
    if tipo:
        cab['Content-Type'] = tipo
    req = urllib.request.Request(url, data=datos, headers=cab)
    with _abridor.open(req, timeout=60) as r:
        return r.status, r.geturl(), r.headers.get('Content-Type', ''), r.read()


def es_privada(ruta):
    r = ruta.lower()
    return any(r == p.lower() or r.startswith(p.lower() + '/') or r.startswith(p.lower() + '?') for p in PRIVADAS)


def archivo_de_pagina(salida, ruta):
    return os.path.join(salida, *[p for p in ruta.strip('/').split('/') if p], 'index.html')


def enlaces_internos(texto):
    for m in re.finditer(r'href\s*=\s*"(/[^"#?]*)(?:[?#][^"]*)?"', texto):
        ruta = urllib.parse.unquote(m.group(1)) or '/'
        if not ruta.startswith('//'):
            yield ruta.rstrip('/') or '/'


def recursos(texto):
    """Archivos locales que menciona un HTML o un CSS (sin ?v=)."""
    encontrados = set()
    for p in (r'(?:src|href|poster|content|data-[\w-]+)\s*=\s*"(/[^"]+)"', r'srcset\s*=\s*"([^"]+)"', r'url\(\s*[\'"]?(/[^\'")]+)[\'"]?\s*\)'):
        for m in re.finditer(p, texto):
            candidatos = [c.strip().split(' ')[0] for c in m.group(1).split(',')] if p.startswith('srcset') else [m.group(1)]
            for c in candidatos:
                if c.startswith('/') and not c.startswith('//'):
                    encontrados.add(c.split('?')[0].split('#')[0])
    return encontrados


def normalizar(texto):
    t = unicodedata.normalize('NFD', (texto or '').lower())
    t = ''.join(ch for ch in t if unicodedata.category(ch) != 'Mn')
    return re.sub(r'\s+', ' ', re.sub(r'[^a-z0-9 ]', ' ', t)).strip()


# ------------------------------------------------------------------ asistente

def temas_del_conocimiento():
    """Lee las palabras clave de cada tema en Services/AsistenteConocimiento.cs."""
    with open(CONOCIMIENTO, encoding='utf-8-sig') as f:
        codigo = f.read()
    i_en = codigo.find('TemasIngles = new()')
    i_es = codigo.find('List<TemaAsistente> Temas = new()')

    def temas(trozo):
        lista = []
        for m in re.finditer(r'new\(\s*"([^"]+)"\s*,\s*"[^"]*"\s*,\s*new\[\]\s*\{([^}]*)\}', trozo):
            claves = re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(2))
            if claves:
                lista.append((m.group(1), claves))
        return lista

    def sugerencias(nombre):
        m = re.search(r'\b' + nombre + r'\s*=\s*\{([^}]*)\}', codigo)
        return re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1)) if m else []

    fin_en = i_es if i_es > i_en else len(codigo)
    return {
        'es': (temas(codigo[i_es:]) if i_es >= 0 else [], sugerencias('SugerenciasInicio')),
        'en': (temas(codigo[i_en:fin_en]) if i_en >= 0 else [], sugerencias('SugerenciasInicioIngles')),
    }


def preguntar_asistente(base, token, accion, texto, idioma):
    cuerpo = urllib.parse.urlencode({
        'historial': json.dumps([{'rol': 'user', 'texto': texto}], ensure_ascii=False),
        'idioma': idioma,
        '__RequestVerificationToken': token,
    }).encode('utf-8')
    _, _, _, r = pedir(base + accion, cuerpo, 'application/x-www-form-urlencoded; charset=utf-8')
    d = json.loads(r.decode('utf-8'))
    return {'texto': d.get('texto') or '', 'acciones': d.get('acciones') or [], 'sugerencias': d.get('sugerencias') or []}


MESES_ES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre']
MESES_EN = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']


def plantilla(texto, indicadores):
    """Deja como {uf}/{dolar}/{euro} (y su fecha {f-uf}...) y {saludo} lo que cambia con el día,
    para completarlo en el navegador con los valores del día."""
    lineas = texto.split('\n')
    for clave in ('uf', 'dolar', 'euro'):
        dato = indicadores.get(clave) or {}
        v = dato.get('valor')
        if not v:
            continue
        es = f'{v:,.2f}'.replace(',', '#').replace('.', ',').replace('#', '.')
        en = f'{v:,.2f}'
        m = re.match(r'(\d{2})-(\d{2})-(\d{4})', dato.get('fecha') or '')
        fechas = [f'{int(m.group(1))} de {MESES_ES[int(m.group(2)) - 1]}', f'{MESES_EN[int(m.group(2)) - 1]} {int(m.group(1))}'] if m else []
        for i, linea in enumerate(lineas):
            nueva = linea
            for forma in (es, en):
                # Solo el número exacto (no dentro de otro, como 992.660,00).
                nueva = re.sub(r'(?<![\d.,])' + re.escape(forma) + r'(?![\d]|[.,]\d)', '{' + clave + '}', nueva)
            if nueva != linea:
                for f in fechas:
                    nueva = nueva.replace(f, '{f-' + clave + '}')
            lineas[i] = nueva
    texto = '\n'.join(lineas)
    return re.sub(r'\b(Buenos días|Buenas tardes|Buenas noches|Good morning|Good afternoon|Good evening)\b', '{saludo}', texto)


def exportar_asistente(base, html_inicio, indicadores, salida):
    m = re.search(r'<form[^>]*id="asisForm"[^>]*>', html_inicio)
    t = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html_inicio[m.start():]) if m else None
    if not m or not t:
        print('  asistente: no se encontró el formulario; queda sin respuestas guardadas')
        return
    accion = html_mod.unescape(re.search(r'action="([^"]+)"', m.group(0)).group(1))
    token = t.group(1)
    for idioma, (temas, sug_inicio) in temas_del_conocimiento().items():
        respuestas, indice, preguntas, lista_temas = [], {}, {}, []

        def guardar(r):
            r = dict(r, texto=plantilla(r['texto'], indicadores))
            k = json.dumps(r, sort_keys=True, ensure_ascii=False)
            if k not in indice:
                indice[k] = len(respuestas)
                respuestas.append(r)
            return indice[k]

        def preguntar(q):
            try:
                return preguntar_asistente(base, token, accion, q, idioma)
            except Exception as e:  # noqa: BLE001 - se informa y se sigue
                print(f'  asistente: falló "{q}" ({e})')
                return None

        pendientes, n = list(sug_inicio), 0
        for id_tema, claves in temas:
            r = preguntar(claves[0])
            if r is None:
                continue
            n += 1
            i = guardar(r)
            preguntas.setdefault(normalizar(claves[0]), i)
            lista_temas.append({'id': id_tema, 'claves': [normalizar(c) for c in claves if normalizar(c)], 'r': i})
            pendientes.extend(r['sugerencias'])
        while pendientes and n < MAX_PREGUNTAS:
            q = pendientes.pop(0)
            k = normalizar(q)
            if not k or k in preguntas:
                continue
            r = preguntar(q)
            if r is None:
                continue
            n += 1
            preguntas[k] = guardar(r)
            pendientes.extend(r['sugerencias'])
        r = preguntar('qwzx')
        otra = guardar(r) if r else None
        with open(os.path.join(salida, DATOS, f'asistente-{idioma}.json'), 'w', encoding='utf-8') as f:
            json.dump({'respuestas': respuestas, 'preguntas': preguntas, 'temas': lista_temas, 'otra': otra}, f, ensure_ascii=False, separators=(',', ':'))
        print(f'  asistente ({idioma}): {n} preguntas, {len(respuestas)} respuestas distintas')


# ------------------------------------------------------------------ formularios

def declarar_formulario(html, id_form, nombre_netlify):
    """Formulario oculto para que Netlify detecte los campos al publicar (sin contraseñas)."""
    m = re.search(r'<form\b[^>]*id="' + re.escape(id_form) + r'"[^>]*>(.*?)</form>', html, re.S)
    if not m:
        return html
    campos = []
    for c in re.finditer(r'<(?:input|select|textarea)\b([^>]*)>', m.group(1)):
        attrs = c.group(1)
        nombre = re.search(r'\bname="([^"]+)"', attrs)
        tipo = re.search(r'\btype="([^"]+)"', attrs)
        if not nombre:
            continue
        n = nombre.group(1)
        if n == '__RequestVerificationToken' or (tipo and tipo.group(1) in ('password', 'submit', 'button')) or n.endswith('Mostrado') or n.endswith('SitioWeb'):
            continue
        n = n.split('.', 1)[1] if n.startswith('Form.') else n
        if n not in campos:
            campos.append(n)
    oculto = (f'<form name="{nombre_netlify}" data-netlify="true" netlify-honeypot="bot-field" hidden>'
              + '<input name="bot-field" />' + ''.join(f'<input name="{c}" />' for c in campos) + '</form>')
    return html.replace('</body>', oculto + '\n</body>', 1)


# ------------------------------------------------------------------ principal

def main():
    ap = argparse.ArgumentParser(description='Exporta el sitio público de Atilson como sitio estático para Netlify.')
    ap.add_argument('--url', required=True, help='Dirección de la app en marcha, por ejemplo https://localhost:7046')
    ap.add_argument('--salida', default=os.path.join(RAIZ, 'sitio-netlify'))
    ap.add_argument('--inseguro', action='store_true', help='Aceptar el certificado de desarrollo de localhost.')
    ap.add_argument('--sin-asistente', action='store_true', help='No guardar las respuestas del asistente (más rápido).')
    ap.add_argument('--cookie', help=argparse.SUPPRESS)  # solo para pruebas
    a = ap.parse_args()
    base = a.url.rstrip('/')
    preparar_red(a.inseguro, base, a.cookie)
    salida = os.path.abspath(a.salida)
    if os.path.isdir(salida):
        shutil.rmtree(salida)
    os.makedirs(os.path.join(salida, DATOS, 'noticias'))

    # 1. Páginas públicas (siguiendo los enlaces internos).
    paginas, pendientes, vistas = {}, list(INICIO), set()
    while pendientes:
        ruta = pendientes.pop(0)
        clave = ruta.lower()
        if clave in vistas or es_privada(ruta):
            continue
        vistas.add(clave)
        try:
            _, final, tipo, cuerpo = pedir(base + urllib.parse.quote(ruta))
        except urllib.error.HTTPError as e:
            print(f'  omitida {ruta} ({e.code})')
            continue
        except urllib.error.URLError as e:
            sys.exit(f'No se pudo conectar con {base}: {e.reason}. ¿Está corriendo la app? (localhost con certificado de desarrollo: agrega --inseguro)')
        if 'text/html' not in tipo or (urllib.parse.urlparse(final).path.rstrip('/') or '/').lower() != clave:
            print(f'  omitida {ruta} (pide iniciar sesión o no es una página)')
            continue
        paginas[ruta] = cuerpo.decode('utf-8')
        print(f'  página  {ruta}')
        for enlace in enlaces_internos(paginas[ruta]):
            if enlace.lower() not in vistas and not es_privada(enlace) and not re.search(r'\.\w{2,5}$', enlace):
                pendientes.append(enlace)
    if '/' not in paginas:
        sys.exit('No se pudo leer la página de inicio.')

    # 2. Datos que las páginas piden al servidor con GET (indicadores, noticias).
    datos, indicadores = {}, {}
    for html in list(paginas.values()):
        for m in re.finditer(r'["\'](/[^"\'\s]*\?handler=([A-Za-z]+)[^"\'\s]*)["\']', html):
            original, handler = m.group(1), m.group(2).lower()
            if handler in HANDLERS_POST or original in datos:
                continue
            try:
                _, _, tipo, cuerpo = pedir(base + html_mod.unescape(original))
            except urllib.error.HTTPError:
                continue
            if 'json' not in tipo:
                continue
            d = json.loads(cuerpo.decode('utf-8'))
            if handler == 'indicadores':
                indicadores = d
            if handler == 'noticias':
                # Las imágenes pasan por el servidor (Mundo Marítimo las publica por http): se descargan.
                for i, n in enumerate(d.get('noticias') or []):
                    img = n.get('imagen')
                    if img and img.startswith('/'):
                        try:
                            _, _, timg, bimg = pedir(base + img)
                            ext = '.png' if 'png' in timg else '.webp' if 'webp' in timg else '.gif' if 'gif' in timg else '.jpg'
                            with open(os.path.join(salida, DATOS, 'noticias', f'{i + 1}{ext}'), 'wb') as f:
                                f.write(bimg)
                            n['imagen'] = f'/{DATOS}/noticias/{i + 1}{ext}'
                        except urllib.error.HTTPError:
                            n['imagen'] = None
                    elif img and img.startswith('http://'):
                        n['imagen'] = None  # el navegador la bloquearía en un sitio https
            destino = f'/{DATOS}/{handler}.json'
            with open(os.path.join(salida, DATOS, f'{handler}.json'), 'w', encoding='utf-8') as f:
                json.dump(d, f, ensure_ascii=False)
            datos[original] = destino
            print(f'  datos   {original} -> {destino}')

    # 3. Respuestas del asistente.
    if not a.sin_asistente:
        exportar_asistente(base, paginas['/'], indicadores, salida)

    # 4. Páginas con sus ajustes.
    necesarios = set(EXTRA)
    for ruta, html in paginas.items():
        for original, destino in datos.items():
            html = html.replace(original, destino)
        for (pagina, id_form), nombre in FORMULARIOS.items():
            if (pagina is None or pagina.lower() == ruta.lower()) and f'id="{id_form}"' in html:
                html = declarar_formulario(html, id_form, nombre)
        html = re.sub(r'<input[^>]*name="__RequestVerificationToken"[^>]*>', '', html)
        # Primero en <head>: el script tiene que cargar antes que los de la página.
        html = re.sub(r'(<head[^>]*>)', r'\1\n    <script src="/sitio-estatico/sitio-estatico.js"></script>\n    <meta name="robots" content="noindex, nofollow" />', html, count=1)
        necesarios |= recursos(html)
        archivo = archivo_de_pagina(salida, ruta)
        os.makedirs(os.path.dirname(archivo), exist_ok=True)
        with open(archivo, 'w', encoding='utf-8', newline='\n') as f:
            f.write(html)

    # 5. Solo los archivos de wwwroot que se usan (y lo que piden sus CSS).
    copiados, revisar, faltantes = set(), sorted(necesarios), []
    while revisar:
        rel = urllib.parse.unquote(revisar.pop())
        if rel in copiados or rel.startswith(f'/{DATOS}/') or rel.startswith('/sitio-estatico/'):
            continue
        copiados.add(rel)
        if rel.lower().startswith('/uploads/'):
            faltantes.append(rel + ' (uploads no se publica)')
            continue
        origen = os.path.normpath(os.path.join(WWWROOT, *rel.strip('/').split('/')))
        if not origen.startswith(WWWROOT) or not os.path.isfile(origen):
            if re.search(r'\.\w{2,5}$', rel):
                faltantes.append(rel)
            continue
        destino = os.path.join(salida, *rel.strip('/').split('/'))
        os.makedirs(os.path.dirname(destino), exist_ok=True)
        shutil.copy2(origen, destino)
        if origen.endswith('.css'):
            with open(origen, encoding='utf-8', errors='ignore') as f:
                css = f.read()
            revisar.extend(recursos(css))
            carpeta = '/' + '/'.join(rel.strip('/').split('/')[:-1])
            for m in re.finditer(r'url\(\s*[\'"]?(?!data:|https?:|/)([^\'")?#]+)', css):
                revisar.append(os.path.normpath(carpeta + '/' + m.group(1)).replace('\\', '/'))

    # 6. Archivos de Netlify.
    os.makedirs(os.path.join(salida, 'sitio-estatico'), exist_ok=True)
    shutil.copy2(JS_ESTATICO, os.path.join(salida, 'sitio-estatico', 'sitio-estatico.js'))
    with open(os.path.join(salida, '_redirects'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('# Partes del sistema: llevan al login, como en el sitio real sin sesión.\n')
        for p in PRIVADAS:
            if p == '/Logout':
                f.write('/Logout  /  302\n')
            elif p != '/__':
                f.write(f'{p}/*  /Login/  302\n')
        f.write('/Index  /  301\n/Privacy  /  301\n')
    with open(os.path.join(salida, '_headers'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('/*\n  X-Robots-Tag: noindex, nofollow\n  X-Content-Type-Options: nosniff\n  Referrer-Policy: strict-origin-when-cross-origin\n')
    with open(os.path.join(salida, '404.html'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('<!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">'
                '<meta name="robots" content="noindex"><title>Página no encontrada · Atilson Cargo</title><link rel="icon" href="/favicon.ico">'
                '<style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#160e2f;color:#fff;font-family:system-ui,sans-serif;text-align:center;padding:24px}'
                'h1{font-size:28px;margin:0 0 8px}p{color:#d6d0ec}a{color:#c3b6f5;font-weight:700}</style></head><body><main><h1>No encontramos esta página</h1>'
                '<p>Puede que la dirección haya cambiado.</p><p><a href="/">Volver al inicio</a></p></main></body></html>')

    zip_base = salida.rstrip('/\\')
    shutil.make_archive(zip_base, 'zip', salida)
    total = sum(len(fs) for _, _, fs in os.walk(salida))
    peso = sum(os.path.getsize(os.path.join(d, x)) for d, _, fs in os.walk(salida) for x in fs) / 1024 / 1024
    print(f'\nListo: {len(paginas)} páginas, {total} archivos ({peso:.1f} MB) en {salida}')
    print(f'Zip: {zip_base}.zip')
    if faltantes:
        print('Aviso, no se encontraron o no se publican:')
        for x in sorted(set(faltantes)):
            print('  ', x)


if __name__ == '__main__':
    main()
