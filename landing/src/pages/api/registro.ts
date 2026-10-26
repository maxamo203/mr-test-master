/**
 * Alta a la prueba cerrada. Corre como función serverless en Vercel (el resto
 * del sitio sigue siendo estático). Valida el reCAPTCHA de Google con nuestra
 * secret key y recién entonces reenvía a Web3Forms, que queda sólo como servicio
 * de mail (sin captcha: hay que desactivarlo en su panel).
 *
 * Variables de entorno (Vercel → Settings → Environment Variables):
 *  - RECAPTCHA_SECRET: secret key de reCAPTCHA v2 casilla.
 *  - WEB3FORMS_ACCESS_KEY: access key de Web3Forms (ya no viaja en el HTML).
 */
import type { APIRoute } from "astro";

export const prerender = false;

const json = (body: Record<string, unknown>, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });

export const POST: APIRoute = async ({ request, clientAddress }) => {
  const secret = import.meta.env.RECAPTCHA_SECRET;
  const accessKey = import.meta.env.WEB3FORMS_ACCESS_KEY;
  if (!secret || !accessKey)
    return json({ success: false, message: "Servidor sin configurar." }, 500);

  const datos = await request.json().catch(() => null);
  if (!datos) return json({ success: false, message: "Pedido inválido." }, 400);

  const nombre = String(datos.nombre ?? "").trim();
  const email = String(datos.email ?? "").trim();
  const token = String(datos.captcha ?? "");

  // Honeypot: una persona nunca lo marca. Se responde "ok" para no dar pistas.
  if (datos.botcheck) return json({ success: true });

  if (nombre.length < 3 || nombre.length > 80)
    return json({ success: false, message: "Nombre inválido." }, 400);
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || email.length > 120)
    return json({ success: false, message: "Email inválido." }, 400);
  if (!token)
    return json({ success: false, message: "Falta el captcha." }, 400);

  const verif = await fetch("https://www.google.com/recaptcha/api/siteverify", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      secret,
      response: token,
      remoteip: clientAddress ?? "",
    }),
  })
    .then((r) => r.json())
    .catch(() => null);

  if (!verif?.success)
    return json({ success: false, message: "Captcha inválido." }, 400);

  const res = await fetch("https://api.web3forms.com/submit", {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({
      access_key: accessKey,
      subject: "Mortuorium — nuevo tester de la prueba cerrada",
      from_name: "Mortuorium — prueba cerrada",
      nombre,
      email,
    }),
  });
  const data = await res.json().catch(() => null);

  if (res.ok && data?.success) return json({ success: true });
  return json({ success: false, message: "No se pudo registrar." }, 502);
};
