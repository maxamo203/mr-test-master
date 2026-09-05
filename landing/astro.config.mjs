// @ts-check
import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';

// Sitio 100% estático: `npm run build` deja el HTML listo en dist/ para subir a
// cualquier hosting (Netlify, Vercel, GitHub Pages, un bucket…). No hay servidor.
export default defineConfig({
  site: 'https://www.mortuorium.com',
  // Salida en carpetas (/como-funciona/index.html): es lo que entienden por
  // igual Netlify, Vercel, GitHub Pages, nginx o un bucket con documento índice.

  // Genera dist/sitemap-index.xml + dist/sitemap-0.xml a partir de las páginas
  // reales del build (usa `site` de arriba para las URLs absolutas). Es lo que
  // se sube a Google Search Console para que rastree el sitio — sin esto, un
  // dominio nuevo puede tardar semanas en que Google lo indexe por su cuenta.
  integrations: [sitemap()],
});
