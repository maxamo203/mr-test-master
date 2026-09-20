# Landing de Mortuorium

Sitio estático (Astro 5) para la landing pública del juego.

## Por qué vive acá y no en `Assets/`

Unity importa **todo** lo que cuelga de `Assets/` y le genera un `.meta` a cada
archivo. Un `node_modules` adentro de `Assets/` serían decenas de miles de
archivos importados y otros tantos `.meta` — por eso la landing va en la raíz del
repo, al lado de `Assets/`, donde Unity ni la mira.

## Correrla

```bash
cd landing
npm install
npm run dev      # http://localhost:4321
```

Otros comandos:

| Comando           | Qué hace                                   |
| ----------------- | ------------------------------------------ |
| `npm run build`   | Genera el sitio estático en `dist/`        |
| `npm run preview` | Sirve el `dist/` ya construido, para probar |

El `dist/` resultante es HTML/CSS/JS plano: se sube tal cual a Netlify, Vercel,
GitHub Pages o un bucket. No necesita servidor de Node.

## Colores: todos en un solo archivo

**Ningún componente define un color.** Ni un hex, ni un `rgba()`, ni un gradiente
con colores adentro. Todo eso está en `src/styles/globals.css` como tokens, y los
`.astro` los consumen con `var(--token)`. Si hace falta un color nuevo, se agrega
ahí — no en el componente.

La paleta base no es inventada: son los mismos valores de `MortuoriumTheme.cs`
(la UI del juego), más los del arte del ícono. Así la web y la app se ven de la
misma familia.

Hay exactamente **dos excepciones**, las dos inevitables porque no son CSS y no
pueden leer variables — ambas están marcadas con un comentario en el código:

- `public/favicon.svg` — archivo de imagen suelto.
- El `<meta name="theme-color">` de `src/layouts/Base.astro` — atributo HTML.

Si cambia la paleta, hay que actualizar esos dos a mano. Para verificar que no se
coló ningún color nuevo:

```bash
grep -rnE '#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(' --include='*.astro' --include='*.ts' src/
```

## Estructura

```
src/
  assets/          imágenes de marca (copiadas del proyecto Unity)
  components/
    Rail.astro         riel lateral (reemplaza al navbar)
    BookHero.astro     hero con el libro que se abre al scrollear
    Pillars.astro      02 — el ritual (intro + pilares + cifras)
    HowItWorks.astro   03 — cómo funciona (resumen + "saber más")
    Stores.astro       04 — descargar (tiendas en "próximamente")
    SiteFooter.astro   pie
  layouts/Base.astro   <head>, riel, scroll-spy y reveal
  nav.ts               ítems del riel de cada página
  pages/
    index.astro
    como-funciona.astro
  styles/globals.css   TODOS los colores + reset + utilidades
```

## Los assets de marca

`src/assets/book-icon.png` y `src/assets/wordmark.png` son copias de:

- `Assets/Logo/icon-logo.png`
- `Assets/Resources/Logo/mortuorium.png`

Si se actualiza el arte en el proyecto Unity, hay que volver a copiarlos. Astro
los optimiza y genera los tamaños responsive en el build, así que no hace falta
redimensionarlos a mano.

## La animación del libro

El hero mide 240vh y adentro hay un escenario `sticky` de 100vh. Un script
calcula cuánto scrolleaste dentro de esa pista (0 → 1) y lo escribe en la
variable CSS `--p`. Todo el movimiento (las tapas girando, el arte que aparece,
el halo) es CSS leyendo esa variable: JS sólo escribe un número por frame.

Las dos tapas son mitades de una misma portada — cada mitad recorta una copia al
doble de ancho, así el sigilo queda partido por el lomo y se separa al abrirse.

Con `prefers-reduced-motion: reduce`, el libro arranca abierto y no se engancha
nada al scroll.

## El video de fondo

Un solo `<video>` en una capa `position: fixed` detrás de todo el contenido
(`src/components/BackdropVideo.astro`), que cumple dos papeles:

1. **De fondo**, del hero en adelante: desenfocado y bajo un velo oscuro, para
   que el texto de las secciones se siga leyendo. Es textura, no contenido.
2. **En la sección "05 — El tráiler"**, al final: la misma capa se acopla al
   marco 16:9 de esa sección, pierde el blur y el velo, y aparecen los
   controles (play/pausa, barra de progreso, volumen, pantalla completa).

No hay dos videos: el script mide el marco vacío, publica su rect en
`--v-top/--v-left/--v-w/--v-h` y la capa fija interpola hacia él con `--dock`
(0 = pantalla completa, 1 = encajado). Duplicar el elemento significaría
decodificar el mismo 1080p dos veces, que en un celular se nota.

De fondo va **muteado** (los navegadores no dejan autoplay con sonido) y en
loop; el sonido lo activa el usuario desde los controles del final. Fuera de
pantalla y con la pestaña en segundo plano se pausa solo.

### Dónde poner el archivo

    landing/public/video/mortuorium.webm    ← el video (obligatorio)
    landing/public/video/mortuorium.mp4     ← respaldo para Safari/iOS
    landing/public/video/mortuorium-poster.jpg  ← primer cuadro (opcional)

Todo lo que está en `public/` se copia tal cual al sitio, sin pasar por el
optimizador: los nombres tienen que ser exactamente esos.

**El `.mp4` no es opcional en la práctica.** Safari — y por lo tanto *todo* iOS,
incluido Chrome en iPhone, que ahí usa WebKit — no reproduce VP9 ni AV1. Sin el
MP4 (H.264 + AAC), en iPhone se ve el póster fijo y nada más. El navegador se
queda con el primer `<source>` que soporta, así que Chrome/Firefox/Edge toman el
WebM y Safari cae al MP4.

### Cómo comprimirlo

El video es 1920×1080. Como corre de fondo, casi siempre desenfocado, no
necesita bitrate alto: apuntá a **3–8 MB** en total. Un archivo de 50 MB hace
que la landing tarde en cargar y se lo va a comer el plan de datos del visitante.

    # WebM (VP9) — el principal
    ffmpeg -i fuente.mp4 -c:v libvpx-vp9 -crf 34 -b:v 0 -an \
           -vf scale=1920:1080 public/video/mortuorium.webm

    # MP4 (H.264) — el respaldo de Safari/iOS
    ffmpeg -i fuente.mp4 -c:v libx264 -crf 26 -preset slow -an \
           -pix_fmt yuv420p -movflags +faststart \
           public/video/mortuorium.mp4

    # Póster: un cuadro representativo (acá, el segundo 3)
    ffmpeg -i fuente.mp4 -ss 3 -vframes 1 -q:v 4 \
           public/video/mortuorium-poster.jpg

Notas de esos comandos:

- **`-an` saca el audio.** Sacalo de la línea si querés que el control de
  volumen del final sirva de algo — de fondo el video va muteado igual.
- **`-movflags +faststart`** mueve el índice al principio del MP4: sin eso el
  video no empieza hasta terminar de bajar entero.
- **`-pix_fmt yuv420p`** es obligatorio para que Safari lo acepte.
- Subí el `-crf` para achicar el archivo (más compresión, menos calidad); 34 y
  26 son puntos de partida razonables para un fondo desenfocado.
- El loop se nota menos si el primer y el último cuadro se parecen.

### Ojo con Git LFS

El `.gitattributes` de la raíz del repo (plantilla de Unity) manda `*.mp4` a
LFS, y el `.gitattributes` de `landing/` lo desactiva a propósito para estos
archivos. **No lo revierta**: el CI hace checkout sin bajar los objetos LFS, así
que el video quedaría como un puntero de texto de 130 bytes y el `<video>` se
vería en negro — sin ningún error en el build que avise. Es el mismo problema
que ya rompió el deploy con los PNG de marca.

La contrapartida es que el video viaja como blob normal y cada versión que
commitees queda entera en la historia del repo: mantenelo comprimido y evitá
subir muchas revisiones.
