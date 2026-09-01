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
