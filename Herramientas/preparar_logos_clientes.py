# Prepara los logos de clientes para la landing (nov-2026).
# Uso: python3 Herramientas/preparar_logos_clientes.py
#
# Toma cada archivo de Herramientas/logos_clientes_originales/ (png, jpg o webp) y deja en
# wwwroot/img/clientes/<nombre>.png una versión pareja:
#   - quita el fondo blanco o gris claro que toca los bordes (los jpg no traen transparencia);
#   - recorta el espacio sobrante;
#   - ajusta el tamaño para que todos "pesen" lo mismo a la vista (misma área, no mismo ancho);
#   - lo centra en un lienzo fijo de 440 x 200 px (se muestra a 220 x 100).
# Los logos blancos pensados para fondo oscuro se pasan a color oscuro (lista OSCURECER).
# Para agregar un cliente: copiar su logo a la carpeta de originales, correr este script y
# sumarlo a la lista de Pages/Shared/_Clientes.cshtml.
import os
from collections import deque
from PIL import Image

RAIZ = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
ORIG = os.path.join(RAIZ, 'Herramientas', 'logos_clientes_originales')
DEST = os.path.join(RAIZ, 'wwwroot', 'img', 'clientes')
LIENZO = (440, 200)
AREA = 44000          # área objetivo del logo (px² en el lienzo 2x)
MAX_W, MAX_H = 420, 188
# Logos blancos sobre transparente: el blanco se pinta de este color. Se puede limitar a una
# franja vertical (0 a 1) para no tocar letras blancas dentro de otras formas.
OSCURECER = {'promar': ((27, 36, 84), 0.0, 0.565)}

def quitar_fondo(im):
    """Vuelve transparente el fondo claro conectado con los bordes (relleno por inundación)."""
    im = im.convert('RGBA')
    w, h = im.size
    px = im.load()
    def claro(c):
        r, g, b, a = c
        return a > 0 and min(r, g, b) >= 200 and max(r, g, b) - min(r, g, b) <= 26
    vistos = bytearray(w * h)
    cola = deque()
    for x in range(w):
        for y in (0, h - 1):
            cola.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            cola.append((x, y))
    while cola:
        x, y = cola.popleft()
        i = y * w + x
        if vistos[i]:
            continue
        vistos[i] = 1
        c = px[x, y]
        if c[3] == 0 or claro(c):
            px[x, y] = (255, 255, 255, 0)
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and not vistos[ny * w + nx]:
                    cola.append((nx, ny))
    return im

def oscurecer(im, color, y0, y1):
    px = im.load(); w, h = im.size
    for y in range(int(h * y0), int(h * y1)):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a > 0 and min(r, g, b) > 200:
                px[x, y] = (*color, a)
    return im

def preparar(ruta, nombre):
    im = Image.open(ruta).convert('RGBA')
    w0, h0 = im.size
    esquinas = [im.getpixel(p)[3] for p in ((0, 0), (w0 - 1, 0), (0, h0 - 1), (w0 - 1, h0 - 1))]
    if max(esquinas) > 0:          # sin transparencia: se quita el fondo claro
        im = quitar_fondo(im)
    if nombre in OSCURECER:
        color, y0, y1 = OSCURECER[nombre]
        im = oscurecer(im, color, y0, y1)
    caja = im.split()[3].point(lambda a: 255 if a > 12 else 0).getbbox()
    if caja:
        im = im.crop(caja)
    w, h = im.size
    escala = (AREA / (w * h)) ** 0.5
    escala = min(escala, MAX_W / w, MAX_H / h)
    nuevo = (max(1, round(w * escala)), max(1, round(h * escala)))
    im = im.resize(nuevo, Image.LANCZOS)
    lienzo = Image.new('RGBA', LIENZO, (255, 255, 255, 0))
    lienzo.alpha_composite(im, ((LIENZO[0] - nuevo[0]) // 2, (LIENZO[1] - nuevo[1]) // 2))
    lienzo.save(os.path.join(DEST, nombre + '.png'), optimize=True)
    return nuevo

if __name__ == '__main__':
    os.makedirs(DEST, exist_ok=True)
    for archivo in sorted(os.listdir(ORIG)):
        nombre, ext = os.path.splitext(archivo)
        if ext.lower() not in ('.png', '.jpg', '.jpeg', '.webp'):
            continue
        print(nombre, preparar(os.path.join(ORIG, archivo), nombre))
