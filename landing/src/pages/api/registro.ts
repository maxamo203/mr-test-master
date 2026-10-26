/**
 * Alta a la prueba cerrada. Corre como función serverless en Vercel (el resto
 * del sitio sigue siendo estático). Valida el reCAPTCHA de Google con nuestra
 * secret key y recién entonces manda el alta por mail desde una cuenta de Gmail
 * (SMTP con nodemailer).
 *
 * Variables de entorno (Vercel → Settings → Environment Variables):
 *  - RECAPTCHA_SECRET: secret key de reCAPTCHA v2 casilla.
 *  - GMAIL_USER / GMAIL_APP_PASSWORD: cuenta Gmail que envía y su contraseña de
 *    aplicación (Cuenta de Google → Seguridad → Verificación en 2 pasos →
 *    Contraseñas de aplicaciones). No es la contraseña normal.
 *  - MAIL_DESTINO (opcional): a dónde llegan las altas; por defecto GMAIL_USER.
 */
import type { APIRoute } from "astro";
import nodemailer from "nodemailer";

export const prerender = false;

const json = (body: Record<string, unknown>, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });

export const POST: APIRoute = async ({ request, clientAddress }) => {
  const secret = import.meta.env.RECAPTCHA_SECRET;
  const gmailUser = import.meta.env.GMAIL_USER;
  const gmailPass = import.meta.env.GMAIL_APP_PASSWORD;
  const destino = import.meta.env.MAIL_DESTINO;
  if (!secret || !gmailUser || !gmailPass)
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

  try {
    const transporte = nodemailer.createTransport({
      service: "gmail",
      auth: { user: gmailUser, pass: gmailPass },
    });
    await transporte.sendMail({
      from: `"Mortuorium — prueba cerrada" <${gmailUser}>`,
      to: destino || gmailUser,
      replyTo: email,
      subject: "Mortuorium — nuevo tester de la prueba cerrada",
      text: `Nombre: ${nombre}
Email: ${email}`,
    });
    return json({ success: true });
  } catch (err) {
    // Queda en los logs de Vercel (Deployments → Functions) para diagnosticar.
    console.error("No se pudo enviar el mail del alta", err);
    return json({ success: false, message: "No se pudo registrar." }, 502);
  }
};
