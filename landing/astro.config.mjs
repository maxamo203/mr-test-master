// @ts-check
import { defineConfig } from 'astro/config';

// Sitio 100% estático: `npm run build` deja el HTML listo en dist/ para subir a
// cualquier hosting (Netlify, Vercel, GitHub Pages, un bucket…). No hay servidor.
export default defineConfig({
  site: 'https://www.mortuorium.com',
  // Salida en carpetas (/como-funciona/index.html): es lo que entienden por
  // igual Netlify, Vercel, GitHub Pages, nginx o un bucket con documento índice.
});
