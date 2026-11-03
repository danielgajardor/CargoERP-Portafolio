# Genera Pages/Shared/_EscenaServicio.cshtml (escenas animadas de cada servicio, nov-2026).
# Uso: python3 Herramientas/generar_escena_servicio.py  (editar aquí textos, etapas o dibujos y volver a generar)
import random, math
random.seed(7)
import os
OUT=os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Pages', 'Shared', '_EscenaServicio.cshtml')

def ola(y, amp, per, ancho=1600, alto=200):
    d=f"M0 {y}"
    x=0
    while x<ancho:
        d+=f" q{per/4:.0f} {-amp} {per/2:.0f} 0 t{per/2:.0f} 0"
        x+=per
    d+=f" V{y+alto} H0 Z"
    return d

def estrellas(n, w=800, h=240):
    s=[]
    for i in range(n):
        x=random.randint(5,w-5); y=random.randint(5,h); r=random.choice([0.8,1,1.2,1.6])
        s.append(f'<circle cx="{x}" cy="{y}" r="{r}" style="--d:{random.randint(0,40)/10}s" />')
    return ''.join(s)

COLORES=['#8f7fd6','#c3b6f5','#5b45b8','#e7a86b','#6ad2b3','#443094','#d9d3f2','#7c6ad0']
def contenedores_barco():
    out=[]; i=0
    for fila in range(2):
        for col in range(8):
            x=56+col*24; y=-22-fila*22
            c=COLORES[(col*3+fila*5)%len(COLORES)]
            out.append(f'<g class="mr-cont" style="--i:{i}"><rect x="{x}" y="{y}" width="23" height="21" rx="1.5" fill="{c}"/><path d="M{x+6} {y+3}v15M{x+11.5} {y+3}v15M{x+17} {y+3}v15" stroke="#000" stroke-opacity=".18"/></g>')
            i+=1
    return ''.join(out)

def pila(x0,y0,cols,filas,w=30,h=14,semilla=0):
    out=[]
    for f in range(filas):
        for c in range(cols):
            if random.random()<0.12 and f==filas-1: continue
            col=COLORES[(c+f*2+semilla)%len(COLORES)]
            out.append(f'<rect x="{x0+c*(w+1)}" y="{y0-(f+1)*(h+1)}" width="{w}" height="{h}" rx="1" fill="{col}" opacity=".9"/>')
    return ''.join(out)

def andes(ancho=1600, base=300):
    pts=[(0,base)]; x=0
    while x<ancho:
        x+=random.randint(40,90); y=random.randint(150,235)
        pts.append((x, y))
        x+=random.randint(30,70); pts.append((x, random.randint(240,270)))
    pts.append((ancho,base))
    # repetir para que el bucle sea continuo
    poly=' '.join(f'{px},{py}' for px,py in pts)
    nieve=[]
    for i in range(1,len(pts)-1):
        px,py=pts[i]
        if py<200:
            nieve.append(f'<path d="M{px} {py} l-{9} {14} l5 -3 l4 5 l5 -6 l4 4 Z" />')
    return poly, ''.join(nieve)

ANDES_POLY, ANDES_NIEVE = andes()

def lomas(ancho=1600, y=300):
    d=f"M0 {y}"
    x=0
    while x<ancho:
        d+=f" q50 {-random.randint(18,40)} 100 0"
        x+=100
    d+=f" V360 H0 Z"
    return d
LOMAS=lomas()

def corrugado(x0,x1,y0,y1,paso=10):
    return ''.join(f'<path d="M{x} {y0+3}V{y1-3}"/>' for x in range(x0+paso, x1-4, paso))

PASOS = {
 'maritimo': [
   ('reserva','Reserva','Confirmamos nave, viaje y fechas con la naviera.'),
   ('retiro','Retiro del vacío','El contenedor sale del depósito hacia tu planta.'),
   ('stacking','Stacking','Tu contenedor entra a puerto y se embarca.'),
   ('zarpe','Zarpe','La nave sale de San Antonio con tu carga.'),
   ('transito','Tránsito','Seguimos la nave y te avisamos de cualquier cambio.'),
   ('arribo','Arribo','Tu carga llega a su puerto de destino.'),
 ],
 'terrestre': [
   ('programacion','Programación','Asignamos camión, conductor y hora de retiro.'),
   ('retiro','Retiro en depósito','El contenedor vacío se carga en el camión.'),
   ('carga','Carga en planta','Llenamos y sellamos el contenedor en tu planta.'),
   ('ruta','En ruta','Seguimos el camión en el mapa hasta el puerto.'),
   ('puerto','Entrega en puerto','Entra a puerto a tiempo para el stacking.'),
 ],
 'documental': [
   ('matriz','Matriz','Reunimos los datos del embarque para el BL.'),
   ('borrador','Borrador','Te enviamos el borrador del BL para revisarlo.'),
   ('aprobacion','Aprobación','Lo apruebas con un clic en el portal.'),
   ('emision','Emisión','La naviera emite el BL definitivo.'),
   ('certificados','Certificados','Fitosanitario y de origen, listos a tiempo.'),
   ('envio','Envío','Originales por courier o liberación electrónica.'),
 ],
 'agencia-de-aduanas': [
   ('instrucciones','Instrucciones','Recibimos los datos y documentos de tu embarque.'),
   ('dus','DUS','Tramitamos el Documento Único de Salida.'),
   ('sag','Inspección SAG','Coordinamos la inspección cuando corresponde.'),
   ('aceptacion','Aceptación','Aduana acepta la salida de tu carga.'),
   ('seguro','Seguro de carga','Tu mercancía viaja protegida.'),
 ],
}
CAB = {
 'maritimo': ('Del booking al arribo.', 'Así se mueve tu contenedor por mar. Pasa el mouse o toca cada etapa para recorrerla.'),
 'terrestre': ('De tu planta al puerto.', 'El recorrido del camión, etapa por etapa. Pasa el mouse o toca cada una para verla.'),
 'documental': ('Cada documento, a tiempo.', 'Del primer dato del BL hasta los originales en destino. Pasa el mouse o toca cada etapa.'),
 'agencia-de-aduanas': ('Tu carga, lista para salir.', 'Así trabajamos la aduana de tu exportación. Pasa el mouse o toca cada etapa.'),
}

# ------------------------------------------------------------------ escenas
MARITIMO = f'''
<svg class="es-svg" viewBox="0 0 800 450" preserveAspectRatio="xMidYMid slice">
  <defs>
    <linearGradient id="mrCielo" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#120a2a"/><stop offset=".7" stop-color="#2c1f63"/><stop offset="1" stop-color="#4f3c9e"/></linearGradient>
    <linearGradient id="mrMar" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2e2170"/><stop offset="1" stop-color="#0f0824"/></linearGradient>
    <radialGradient id="mrHalo"><stop offset="0" stop-color="#d9d0ff" stop-opacity=".45"/><stop offset="1" stop-color="#d9d0ff" stop-opacity="0"/></radialGradient>
    <clipPath id="mrClipCable"><rect x="-50" y="152" width="400" height="200"/></clipPath>
  </defs>
  <rect width="800" height="302" fill="url(#mrCielo)"/>
  <g class="es-estrellas" fill="#fff">{estrellas(46)}</g>
  <circle cx="650" cy="72" r="70" fill="url(#mrHalo)"/><circle cx="650" cy="72" r="15" fill="#efeafe"/><circle cx="656" cy="68" r="13" fill="#2a1d60" opacity=".35"/>
  <g class="mr-nubes" fill="#fff" opacity=".07">
    <g class="es-nube" style="--v:70s"><ellipse cx="160" cy="190" rx="70" ry="12"/><ellipse cx="190" cy="182" rx="40" ry="10"/><ellipse cx="960" cy="200" rx="80" ry="12"/></g>
    <g class="es-nube" style="--v:110s"><ellipse cx="480" cy="160" rx="90" ry="10"/><ellipse cx="1280" cy="170" rx="60" ry="9"/></g>
  </g>
  <g class="mr-ruta">
    <path class="mr-ruta-base" d="M90 112 Q400 32 710 112"/>
    <path class="mr-ruta-hecha" d="M90 112 Q400 32 710 112" pathLength="100"/>
    <circle class="mr-ruta-fin" cx="90" cy="112" r="4.5"/><circle class="mr-ruta-fin" cx="710" cy="112" r="4.5"/>
    <text x="90" y="134" text-anchor="middle">San Antonio</text><text x="710" y="134" text-anchor="middle">Rotterdam</text>
    <g class="mr-ruta-punto"><circle r="11" class="halo"/><circle r="5.5"/></g>
  </g>
  <rect y="300" width="800" height="150" fill="url(#mrMar)"/>
  <g class="mr-reflejo" fill="#efeafe" opacity=".18"><rect x="620" y="306" width="60" height="2" rx="1"/><rect x="632" y="316" width="36" height="2" rx="1"/><rect x="640" y="326" width="20" height="2" rx="1"/></g>
  <g class="mr-ola mr-ola-1"><path d="{ola(318,4,90)}" fill="#3a2b85" opacity=".55"/></g>
  <g class="mr-ola mr-ola-2"><path d="{ola(345,5,120)}" fill="#24185a" opacity=".7"/></g>

  <g class="mr-origen">
    <rect x="-40" y="290" width="190" height="160" fill="#231852"/><rect x="-40" y="288" width="190" height="5" fill="#3b2c7d"/>
    <rect x="150" y="293" width="4" height="40" fill="#1a1140"/>
    {pila(-30,288,3,2,30,13,1)}
    <g class="mr-camion"><rect x="0" y="270" width="34" height="14" rx="1" fill="#e7a86b"/><rect x="34" y="273" width="13" height="11" rx="2" fill="#efeafe"/><rect x="40" y="275" width="5" height="4" fill="#2c1f63"/><circle cx="8" cy="286" r="3" fill="#0d0820"/><circle cx="26" cy="286" r="3" fill="#0d0820"/><circle cx="41" cy="286" r="3" fill="#0d0820"/></g>
    <g class="mr-grua">
      <rect x="44" y="150" width="6" height="140" fill="#8f7fd6"/><rect x="102" y="150" width="6" height="140" fill="#8f7fd6"/>
      <path d="M50 200 L102 240 M102 200 L50 240" stroke="#8f7fd6" stroke-width="2.5"/>
      <rect x="38" y="128" width="76" height="20" rx="2" fill="#c3b6f5"/><rect x="46" y="133" width="10" height="6" fill="#2c1f63"/>
      <rect x="-20" y="146" width="262" height="7" fill="#c3b6f5"/>
      <path d="M76 128 L76 96 L242 146 M76 96 L-20 146" stroke="#c3b6f5" stroke-width="2"/>
      <g clip-path="url(#mrClipCable)"><g class="mr-izaje"><rect x="-9" y="151" width="18" height="7" rx="1" fill="#efeafe"/><g class="mr-gancho"><path d="M-5 0 V160 M5 0 V160" stroke="#d9d3f2" stroke-width="1"/><rect x="-16" y="158" width="32" height="3" fill="#efeafe"/><g class="mr-carga"><rect x="-15" y="160" width="30" height="14" rx="1" fill="#e7a86b"/></g></g></g></g>
    </g>
  </g>

  <g class="mr-destino">
    <rect x="640" y="290" width="200" height="160" fill="#231852"/><rect x="640" y="288" width="200" height="5" fill="#3b2c7d"/>
    {pila(690,288,3,3,30,13,4)}
    <g class="mr-grua-d"><rect x="660" y="160" width="5" height="130" fill="#8f7fd6"/><rect x="700" y="160" width="5" height="130" fill="#8f7fd6"/><rect x="600" y="156" width="130" height="6" fill="#c3b6f5"/><rect x="655" y="140" width="56" height="16" rx="2" fill="#c3b6f5"/><circle class="mr-luz" cx="603" cy="159" r="2.5" fill="#fca5a5"/></g>
    <g class="mr-faro"><path d="M772 288 L778 220 L786 220 L792 288 Z" fill="#efeafe"/><rect x="776" y="238" width="12" height="6" fill="#e86a6a"/><rect x="776" y="258" width="14" height="6" fill="#e86a6a"/><rect x="775" y="212" width="14" height="9" fill="#2c1f63"/><circle class="mr-faro-luz" cx="782" cy="216" r="4" fill="#fde68a"/></g>
  </g>

  <g class="mr-barco">
    <g class="mr-flota">
      <g class="mr-estela" stroke="#efeafe" stroke-linecap="round" fill="none"><path d="M10 40 H-60"/><path d="M4 34 H-40"/><path d="M14 46 H-90"/></g>
      <g class="mr-humo" fill="#d9d3f2"><circle cx="29" cy="-84" r="5"/><circle cx="29" cy="-84" r="5"/><circle cx="29" cy="-84" r="5"/></g>
      {contenedores_barco()}
      <path d="M0 0 H262 L284 -10 L262 44 H18 Z" fill="#1d1447"/>
      <path d="M8 31 H268 L262 44 H18 Z" fill="#b84a5c"/>
      <path d="M0 0 H262 L284 -10" stroke="#c3b6f5" stroke-width="1.5" fill="none" opacity=".6"/>
      <text x="200" y="20" class="mr-nombre">ATILSON</text>
      <rect x="12" y="-64" width="38" height="64" rx="2" fill="#efeafe"/>
      <rect x="8" y="-68" width="46" height="6" rx="1" fill="#d9d3f2"/>
      <g fill="#2c1f63"><rect x="17" y="-58" width="6" height="5"/><rect x="27" y="-58" width="6" height="5"/><rect x="37" y="-58" width="6" height="5"/><rect x="17" y="-46" width="6" height="5"/><rect x="27" y="-46" width="6" height="5"/><rect x="37" y="-46" width="6" height="5"/></g>
      <g class="mr-ventanas" fill="#fde68a"><rect x="17" y="-58" width="6" height="5"/><rect x="37" y="-46" width="6" height="5"/></g>
      <rect x="22" y="-82" width="14" height="16" fill="#443094"/><rect x="22" y="-78" width="14" height="4" fill="#c3b6f5"/>
      <circle class="mr-luz-barco" cx="282" cy="-12" r="2.2" fill="#86efac"/>
    </g>
  </g>
  <g class="mr-ola mr-ola-frente"><path d="{ola(300,3,70)}" fill="#2a1e68" opacity=".95"/></g>
  <g class="mr-ola mr-ola-3"><path d="{ola(372,4,150)}" fill="#160d36" opacity=".9"/></g>
</svg>
<div class="es-capa es-tarjeta" data-ver="reserva" style="left:4%;bottom:7%">
  <span class="es-t-ico"><i class="bi bi-bookmark-check"></i></span>
  <div><b>Booking confirmado</b><small>Nave NV2 · zarpe domingo 8</small></div>
</div>
<div class="es-capa es-tarjeta" data-ver="retiro" style="left:4%;bottom:7%">
  <span class="es-t-ico"><i class="bi bi-truck"></i></span>
  <div><b>Contenedor 40' reefer retirado</b><small>Del depósito a tu planta</small></div>
</div>
<div class="es-capa es-tarjeta" data-ver="stacking" style="left:4%;bottom:7%">
  <span class="es-t-ico"><i class="bi bi-box-seam"></i></span>
  <div><b>Ingresó a puerto</b><small>Dentro del plazo de stacking</small></div>
</div>
<div class="es-capa es-chip" data-ver="zarpe" style="left:50%;bottom:7%"><i class="bi bi-water"></i> Zarpó de San Antonio</div>
<div class="es-capa es-chip" data-ver="transito" style="left:50%;bottom:7%"><i class="bi bi-broadcast"></i> En tránsito · día 14 de 28</div>
<div class="es-capa es-chip es-chip-ok" data-ver="arribo" style="left:50%;bottom:7%"><i class="bi bi-check2-circle"></i> Arribó a Rotterdam</div>
'''

TERRESTRE = f'''
<svg class="es-svg" viewBox="0 0 800 450" preserveAspectRatio="xMidYMid slice">
  <defs>
    <linearGradient id="trCielo" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#150d33"/><stop offset=".55" stop-color="#3a2a7e"/><stop offset="1" stop-color="#9b7fd9"/></linearGradient>
    <radialGradient id="trSol"><stop offset="0" stop-color="#ffd9a8" stop-opacity=".9"/><stop offset=".35" stop-color="#ffb77a" stop-opacity=".35"/><stop offset="1" stop-color="#ffb77a" stop-opacity="0"/></radialGradient>
    <linearGradient id="trFaro" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff4c2" stop-opacity=".55"/><stop offset="1" stop-color="#fff4c2" stop-opacity="0"/></linearGradient>
  </defs>
  <rect width="800" height="360" fill="url(#trCielo)"/>
  <g class="es-estrellas" fill="#fff" opacity=".7">{estrellas(26, 800, 140)}</g>
  <circle cx="600" cy="262" r="120" fill="url(#trSol)"/><circle cx="600" cy="262" r="30" fill="#ffe2b8"/>
  <g class="tr-capa tr-andes" style="--v:90s"><polygon points="{ANDES_POLY}" fill="#2b1f63"/><g fill="#efeafe" opacity=".85">{ANDES_NIEVE}</g><g transform="translate(1600 0)"><polygon points="{ANDES_POLY}" fill="#2b1f63"/><g fill="#efeafe" opacity=".85">{ANDES_NIEVE}</g></g></g>
  <g class="tr-capa tr-lomas" style="--v:40s"><path d="{LOMAS}" fill="#21174f"/><path d="{LOMAS}" fill="#21174f" transform="translate(1600 0)"/></g>
  <rect y="340" width="800" height="110" fill="#1a1140"/>

  <g class="tr-estacion tr-deposito" data-etapa="retiro">
    {pila(10,352,5,4,32,15,2)}{pila(612,352,5,3,32,15,5)}
    <rect x="236" y="138" width="10" height="214" fill="#e7a86b"/><rect x="574" y="138" width="10" height="214" fill="#e7a86b"/>
    <rect x="226" y="128" width="368" height="14" rx="2" fill="#f0bd86"/><rect x="380" y="118" width="70" height="12" rx="2" fill="#f0bd86"/>
    <path d="M246 170 L574 170" stroke="#e7a86b" stroke-width="3"/>
    <text x="410" y="138" text-anchor="middle" class="tr-rotulo" fill="#3a2a7e">DEPÓSITO</text>
  </g>
  <g class="tr-estacion tr-planta" data-etapa="carga">
    <rect x="0" y="206" width="222" height="146" fill="#efeafe"/><path d="M-6 210 L111 168 L228 210 Z" fill="#d9d3f2"/>
    <rect x="18" y="226" width="120" height="26" rx="3" fill="#443094"/><text x="78" y="244" text-anchor="middle" class="tr-rotulo" fill="#fff">PLANTA</text>
    <g fill="#8f7fd6" opacity=".5"><rect x="22" y="268" width="26" height="20"/><rect x="58" y="268" width="26" height="20"/><rect x="94" y="268" width="26" height="20"/></g>
    <rect x="160" y="262" width="62" height="90" fill="#2c1f63"/><rect x="160" y="258" width="62" height="6" fill="#8f7fd6"/>
    <rect x="200" y="330" width="44" height="6" fill="#8f7fd6"/>
    <g class="tr-cajas">
      <g class="tr-caja" style="--i:0"><rect x="170" y="306" width="22" height="22" rx="1.5" fill="#e7a86b"/><path d="M170 314h22" stroke="#b9773f"/></g>
      <g class="tr-caja" style="--i:1"><rect x="170" y="306" width="22" height="22" rx="1.5" fill="#f0bd86"/><path d="M170 314h22" stroke="#b9773f"/></g>
      <g class="tr-caja" style="--i:2"><rect x="170" y="306" width="22" height="22" rx="1.5" fill="#e7a86b"/><path d="M170 314h22" stroke="#b9773f"/></g>
    </g>
  </g>
  <g class="tr-estacion tr-puerto" data-etapa="puerto">
    <g opacity=".75"><rect x="470" y="120" width="8" height="232" fill="#6c5bc0"/><rect x="530" y="120" width="8" height="232" fill="#6c5bc0"/><rect x="420" y="112" width="210" height="9" fill="#8f7fd6"/><rect x="470" y="96" width="70" height="16" fill="#8f7fd6"/>
    <rect x="650" y="150" width="7" height="202" fill="#6c5bc0"/><rect x="700" y="150" width="7" height="202" fill="#6c5bc0"/><rect x="610" y="143" width="190" height="8" fill="#8f7fd6"/></g>
    {pila(560,352,4,2,30,14,3)}
    <rect x="618" y="232" width="8" height="120" fill="#efeafe"/><rect x="772" y="232" width="8" height="120" fill="#efeafe"/>
    <rect x="608" y="218" width="182" height="20" rx="3" fill="#443094"/><text x="699" y="232" text-anchor="middle" class="tr-rotulo" fill="#fff">GATE · PUERTO</text>
    <rect x="740" y="300" width="28" height="52" fill="#d9d3f2"/><rect x="745" y="308" width="18" height="12" fill="#2c1f63"/>
    <g class="tr-barrera"><rect x="626" y="322" width="112" height="7" rx="3" fill="#efeafe"/><g fill="#e86a6a"><rect x="640" y="322" width="12" height="7"/><rect x="664" y="322" width="12" height="7"/><rect x="688" y="322" width="12" height="7"/><rect x="712" y="322" width="12" height="7"/></g></g>
    <circle cx="622" cy="326" r="7" fill="#d9d3f2"/>
  </g>

  <rect y="352" width="800" height="58" fill="#0f0a26"/><rect y="352" width="800" height="3" fill="#2c1f63"/><rect y="407" width="800" height="3" fill="#2c1f63"/>
  <path class="tr-pista" d="M-60 382 H860" stroke="#c3b6f5" stroke-width="3" stroke-dasharray="28 24" opacity=".55"/>

  <g class="tr-camion">
    <g class="tr-rebote">
      <path class="tr-luz" d="M376 330 L520 300 L520 372 Z" fill="url(#trFaro)"/>
      <rect x="0" y="336" width="300" height="9" rx="2" fill="#0b0720"/>
      <g class="tr-cables" stroke="#efeafe" stroke-width="1.4"><path d="M120 270 V142 M180 270 V142"/></g>
      <g class="tr-cont">
        <rect x="8" y="270" width="284" height="66" rx="2" fill="#5b45b8"/>
        <g stroke="#000" stroke-opacity=".16">{corrugado(40,280,270,336,11)}</g>
        <rect x="270" y="276" width="20" height="54" rx="2" fill="#efeafe"/><g fill="#8f7fd6"><rect x="274" y="282" width="12" height="3"/><rect x="274" y="288" width="12" height="3"/><rect x="274" y="294" width="12" height="3"/></g>
        <text x="156" y="309" text-anchor="middle" class="tr-marca">ATILSON</text>
        <g class="tr-puerta"><rect x="8" y="270" width="16" height="66" fill="#6c56c9"/><path d="M12 276 V330 M20 276 V330" stroke="#c3b6f5" stroke-opacity=".6"/></g>
        <g class="tr-puerta tr-puerta-2"><rect x="24" y="270" width="16" height="66" fill="#6c56c9"/><path d="M28 276 V330 M36 276 V330" stroke="#c3b6f5" stroke-opacity=".6"/></g>
        <g class="tr-sello"><circle cx="24" cy="306" r="7" fill="#34d399"/><path d="M20.5 306 l2.5 2.5 l4.5 -5" stroke="#063" stroke-width="1.8" fill="none"/></g>
      </g>
      <path d="M300 300 H342 Q352 300 356 312 L372 318 Q378 320 378 328 V345 H300 Z" fill="#efeafe"/>
      <path d="M318 306 H340 Q346 306 349 314 L351 320 H318 Z" fill="#2c1f63"/>
      <rect x="300" y="296" width="6" height="49" fill="#d9d3f2"/><rect x="306" y="286" width="5" height="16" rx="1" fill="#8f7fd6"/>
      <rect x="370" y="330" width="9" height="6" rx="1" fill="#fde68a"/><rect x="296" y="340" width="86" height="7" rx="2" fill="#2c1f63"/>
      <g class="tr-rueda" style="transform-origin:46px 352px"><circle cx="46" cy="352" r="12" fill="#0b0720"/><circle cx="46" cy="352" r="5" fill="#8f7fd6"/><path d="M46 342 V362 M36 352 H56" stroke="#8f7fd6" stroke-width="1.5"/></g>
      <g class="tr-rueda" style="transform-origin:76px 352px"><circle cx="76" cy="352" r="12" fill="#0b0720"/><circle cx="76" cy="352" r="5" fill="#8f7fd6"/><path d="M76 342 V362 M66 352 H86" stroke="#8f7fd6" stroke-width="1.5"/></g>
      <g class="tr-rueda" style="transform-origin:250px 352px"><circle cx="250" cy="352" r="12" fill="#0b0720"/><circle cx="250" cy="352" r="5" fill="#8f7fd6"/><path d="M250 342 V362 M240 352 H260" stroke="#8f7fd6" stroke-width="1.5"/></g>
      <g class="tr-rueda" style="transform-origin:318px 352px"><circle cx="318" cy="352" r="12" fill="#0b0720"/><circle cx="318" cy="352" r="5" fill="#8f7fd6"/><path d="M318 342 V362 M308 352 H328" stroke="#8f7fd6" stroke-width="1.5"/></g>
      <g class="tr-rueda" style="transform-origin:360px 352px"><circle cx="360" cy="352" r="12" fill="#0b0720"/><circle cx="360" cy="352" r="5" fill="#8f7fd6"/><path d="M360 342 V362 M350 352 H370" stroke="#8f7fd6" stroke-width="1.5"/></g>
      <g class="tr-gps"><circle cx="340" cy="268" r="16" class="tr-gps-onda"/><path d="M340 282 C332 272 328 266 328 260 A12 12 0 0 1 352 260 C352 266 348 272 340 282 Z" fill="#34d399"/><circle cx="340" cy="260" r="4.5" fill="#0b3d2c"/></g>
    </g>
  </g>
</svg>
<div class="es-capa es-tarjeta" data-ver="programacion" style="left:5%;top:7%">
  <span class="es-t-ico"><i class="bi bi-calendar2-check"></i></span>
  <div><b>Retiro programado · 08:00</b><small>Camión y conductor asignados</small></div>
</div>
<div class="es-capa es-chip" data-ver="retiro" style="left:50%;top:7%;--retraso:3s"><i class="bi bi-box-seam"></i> Contenedor vacío cargado</div>
<div class="es-capa es-tarjeta" data-ver="carga" style="right:5%;top:7%;--retraso:5.4s">
  <span class="es-t-ico"><i class="bi bi-thermometer-snow"></i></span>
  <div><b>Cargado y sellado</b><small>Reefer a −0,5 °C</small></div>
</div>
<div class="es-capa es-chip" data-ver="ruta" style="left:50%;top:7%"><span class="es-vivo"></span> En ruta a San Antonio · llega 13:40</div>
<div class="es-capa es-chip es-chip-ok" data-ver="puerto" style="left:50%;top:7%;--retraso:2.6s"><i class="bi bi-check2-circle"></i> Ingresó a puerto a tiempo</div>
'''

DOCUMENTAL = '''
<div class="dc-mesa"></div>
<div class="dc-cert dc-cert-1"><span class="dc-cert-ico"><i class="bi bi-flower1"></i></span><b>Certificado fitosanitario</b><i class="dc-linea" style="--w:80%"></i><i class="dc-linea" style="--w:62%"></i><i class="dc-linea" style="--w:72%"></i><span class="dc-cinta"></span></div>
<div class="dc-cert dc-cert-2"><span class="dc-cert-ico"><i class="bi bi-award"></i></span><b>Certificado de origen</b><i class="dc-linea" style="--w:74%"></i><i class="dc-linea" style="--w:58%"></i><i class="dc-linea" style="--w:68%"></i><span class="dc-cinta"></span></div>
<div class="dc-bl">
  <div class="dc-bl-cab">
    <span class="dc-bl-ico"><i class="bi bi-file-earmark-text"></i></span>
    <div><b class="dc-bl-titulo"><span class="t-matriz">Matriz del BL</span><span class="t-borrador">Borrador de BL</span><span class="t-original">BL original</span></b><small>Booking 7731 · San Antonio → Rotterdam</small></div>
  </div>
  <dl class="dc-campos">
    <div style="--i:0"><dt>Embarcador</dt><dd><i style="--w:78%"></i></dd></div>
    <div style="--i:1"><dt>Consignatario</dt><dd><i style="--w:64%"></i></dd></div>
    <div style="--i:2"><dt>Notify</dt><dd><i style="--w:52%"></i></dd></div>
    <div style="--i:3"><dt>Nave / viaje</dt><dd><i style="--w:44%"></i></dd></div>
    <div style="--i:4"><dt>Mercancía</dt><dd><i style="--w:86%"></i></dd></div>
    <div style="--i:5"><dt>Contenedor</dt><dd><i style="--w:58%"></i></dd></div>
  </dl>
  <span class="dc-marca dc-marca-borrador">BORRADOR</span>
  <span class="dc-aprobar">Aprobar BL</span>
  <span class="dc-sello">APROBADO</span>
  <span class="dc-sello-original"><i class="bi bi-patch-check-fill"></i> ORIGINAL</span>
</div>
<span class="dc-cursor"><svg viewBox="0 0 24 24"><path d="M4 2 L4 19 L8.5 15 L11.5 22 L14.5 20.7 L11.6 14 L18 14 Z" fill="#fff" stroke="#160e2f" stroke-width="1.5" stroke-linejoin="round"/></svg></span>
<div class="dc-sobre"><div class="dc-sobre-cuerpo"></div><div class="dc-sobre-tapa"></div><i class="bi bi-airplane"></i></div>
<svg class="dc-estela" viewBox="0 0 800 450" preserveAspectRatio="none"><path d="M560 300 C640 260 680 160 760 70" /></svg>
<div class="es-capa es-chip" data-ver="matriz" style="left:50%;top:5%"><i class="bi bi-pencil-square"></i> Completando la matriz</div>
<div class="es-capa es-chip" data-ver="borrador" style="left:50%;top:5%"><i class="bi bi-envelope-paper"></i> Borrador enviado al portal</div>
<div class="es-capa es-chip es-chip-ok" data-ver="aprobacion" style="left:50%;top:5%;--retraso:1.8s"><i class="bi bi-hand-thumbs-up"></i> Aprobado por el cliente</div>
<div class="es-capa es-chip" data-ver="emision" style="left:50%;top:5%"><i class="bi bi-patch-check"></i> BL emitido por la naviera</div>
<div class="es-capa es-chip" data-ver="certificados" style="left:50%;top:5%"><i class="bi bi-files"></i> Certificados listos</div>
<div class="es-capa es-chip es-chip-ok" data-ver="envio" style="left:50%;top:5%"><i class="bi bi-send-check"></i> Originales en camino</div>
'''

ADUANAS = f'''
<svg class="es-svg" viewBox="0 0 800 450" preserveAspectRatio="xMidYMid slice">
  <defs>
    <linearGradient id="adFondo" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#150d33"/><stop offset="1" stop-color="#2a1d63"/></linearGradient>
    <linearGradient id="adHaz" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#34d399" stop-opacity="0"/><stop offset=".5" stop-color="#34d399" stop-opacity=".55"/><stop offset="1" stop-color="#34d399" stop-opacity="0"/></linearGradient>
    <radialGradient id="adEscudo" cx=".5" cy=".7" r=".7"><stop offset="0" stop-color="#c3b6f5" stop-opacity=".22"/><stop offset="1" stop-color="#c3b6f5" stop-opacity="0"/></radialGradient>
    <clipPath id="adClipCont"><rect x="380" y="232" width="330" height="96" rx="3"/></clipPath>
  </defs>
  <rect width="800" height="450" fill="url(#adFondo)"/>
  <g stroke="#c3b6f5" stroke-opacity=".06">{''.join(f'<path d="M{x} 0 V450"/>' for x in range(0,800,40))}{''.join(f'<path d="M0 {y} H800"/>' for y in range(0,450,40))}</g>
  <g opacity=".55"><rect x="470" y="70" width="8" height="266" fill="#4b3a9c"/><rect x="540" y="70" width="8" height="266" fill="#4b3a9c"/><rect x="420" y="62" width="250" height="9" fill="#5b45b8"/><rect x="474" y="46" width="74" height="16" fill="#5b45b8"/>
    <rect x="690" y="110" width="7" height="226" fill="#4b3a9c"/><rect x="745" y="110" width="7" height="226" fill="#4b3a9c"/><rect x="650" y="103" width="160" height="8" fill="#5b45b8"/></g>
  <rect x="340" y="336" width="440" height="16" rx="3" fill="#3a2b85"/><rect x="300" y="352" width="500" height="98" fill="#1d1447"/>
  <g class="ad-escudo"><path d="M545 96 C615 96 690 140 712 210 L712 300 Q712 336 676 336 H414 Q378 336 378 300 L378 210 C400 140 475 96 545 96 Z" fill="url(#adEscudo)" stroke="#c3b6f5" stroke-width="2" stroke-dasharray="6 6"/></g>
  <g class="ad-cont">
    <rect x="380" y="232" width="330" height="96" rx="3" fill="#443094"/>
    <g stroke="#000" stroke-opacity=".18">{corrugado(390,700,232,328,12)}</g>
    <rect x="380" y="232" width="330" height="6" fill="#5b45b8"/>
    <text x="545" y="290" text-anchor="middle" class="tr-marca">ATILSON</text>
    <g clip-path="url(#adClipCont)"><rect class="ad-haz" x="330" y="225" width="70" height="110" fill="url(#adHaz)"/></g>
  </g>
  <g class="ad-arco"><rect x="352" y="196" width="12" height="140" rx="2" fill="#8f7fd6"/><rect x="726" y="196" width="12" height="140" rx="2" fill="#8f7fd6"/><rect x="346" y="184" width="398" height="18" rx="3" fill="#c3b6f5"/>
    <text x="545" y="197" text-anchor="middle" class="tr-rotulo" fill="#2c1f63">CONTROL ADUANERO</text><circle class="ad-semaforo" cx="358" cy="214" r="4" fill="#fbbf24"/><circle class="ad-semaforo-ok" cx="358" cy="214" r="4" fill="#34d399"/></g>
  <g class="ad-ok"><circle cx="706" cy="232" r="16" fill="#34d399"/><path d="M698 232 l6 6 l10 -11" stroke="#063" stroke-width="3" fill="none" stroke-linecap="round" stroke-linejoin="round"/></g>
</svg>
<div class="ad-dus">
  <div class="ad-dus-cab"><span class="dc-bl-ico"><i class="bi bi-file-earmark-ruled"></i></span><div><b>DUS</b><small>Exportación · Aduana de San Antonio</small></div><span class="ad-estado"><span class="e-borrador">Borrador</span><span class="e-tramite">En trámite</span><span class="e-aceptado">Aceptado</span></span></div>
  <dl class="dc-campos ad-campos">
    <div style="--i:0"><dt>Exportador</dt><dd><i style="--w:72%"></i></dd></div>
    <div style="--i:1"><dt>Mercancía</dt><dd><i style="--w:84%"></i></dd></div>
    <div style="--i:2"><dt>Código arancelario</dt><dd><i style="--w:46%"></i></dd></div>
    <div style="--i:3"><dt>Valor FOB</dt><dd><i style="--w:38%"></i></dd></div>
    <div style="--i:4"><dt>Destino</dt><dd><i style="--w:54%"></i></dd></div>
  </dl>
  <span class="dc-sello ad-sello">ACEPTADO</span>
</div>
<ul class="ad-lista es-capa" data-ver="instrucciones">
  <li style="--i:0"><i class="bi bi-check2"></i> Factura comercial</li>
  <li style="--i:1"><i class="bi bi-check2"></i> Lista de empaque</li>
  <li style="--i:2"><i class="bi bi-check2"></i> Instrucciones de embarque</li>
</ul>
<div class="ad-lupa"><i class="bi bi-search"></i><span><i class="bi bi-flower1"></i></span></div>
<div class="es-capa es-chip" data-ver="instrucciones" style="left:66%;top:6%"><i class="bi bi-inbox"></i> Documentos recibidos</div>
<div class="es-capa es-chip" data-ver="dus" style="left:66%;top:6%"><i class="bi bi-send"></i> DUS enviado a Aduana</div>
<div class="es-capa es-chip" data-ver="sag" style="left:66%;top:6%;--retraso:2.2s"><i class="bi bi-flower1"></i> Inspección SAG sin observaciones</div>
<div class="es-capa es-chip es-chip-ok" data-ver="aceptacion" style="left:66%;top:6%;--retraso:1.1s"><i class="bi bi-check2-circle"></i> Salida autorizada</div>
<div class="es-capa es-chip" data-ver="seguro" style="left:66%;top:6%"><i class="bi bi-shield-check"></i> Seguro de carga activo</div>
'''

ESCENAS = {'maritimo': MARITIMO, 'terrestre': TERRESTRE, 'documental': DOCUMENTAL, 'agencia-de-aduanas': ADUANAS}

def cs_str(t): return t.replace('"','""')

partes=[]
partes.append('''@model string
@* Escena animada de cada servicio (nov-2026), en la misma línea que la vitrina del portal.
   Se usa en /servicios/{slug} (model = slug). A la izquierda las etapas del servicio; a la
   derecha una ilustración (SVG y HTML) que cambia según la etapa: avanza sola mientras está en
   pantalla, se detiene al pasar el mouse o con el teclado. Los datos son ilustrativos.
   Generado con Herramientas/generar_escena_servicio.py (no editar a mano); estilos en wwwroot/css/escena-servicio.css y comportamiento en
   wwwroot/js/escena-servicio.js. *@
@{
    string slug = Model ?? "";
}
''')
for slug, pasos in PASOS.items():
    titulo, bajada = CAB[slug]
    li=[]
    for k,(pid,pt,ptx) in enumerate(pasos):
        li.append(f'''            <li><button type="button" class="es-paso" role="tab" data-paso="{pid}" aria-selected="false" aria-controls="esEstado"><span class="es-num">{k+1}</span><span><b>{pt}</b>{ptx}</span><span class="es-barra" aria-hidden="true"></span></button></li>''')
    escena=ESCENAS[slug].replace('@','@@')
    partes.append(f'''@if (slug == "{slug}")
{{
<section class="es es--{slug}" aria-labelledby="esTitulo">
    <div class="es-fondo" aria-hidden="true"></div>
    <div class="contenedor">
        <header class="es-cab">
            <p class="es-lema"><i class="bi bi-play-circle" aria-hidden="true"></i> Así funciona</p>
            <h2 class="titular" id="esTitulo">{titulo}</h2>
            <p class="es-bajada">{bajada}</p>
        </header>
        <div class="es-grid">
            <ol class="es-pasos" role="tablist" aria-label="Etapas del servicio">
{chr(10).join(li)}
            </ol>
            <div class="es-marco">
                <div class="es-escena" data-paso="{pasos[0][0]}" aria-hidden="true">
{escena}
                </div>
                <p class="es-estado" id="esEstado" aria-live="polite"><span class="es-estado-n">Etapa 1 de {len(pasos)}</span><span class="es-estado-t">{pasos[0][2]}</span></p>
            </div>
        </div>
    </div>
</section>
}}
''')
open(OUT,'w',encoding='utf-8').write('\n'.join(partes))
print('ok', sum(len(p) for p in partes))
