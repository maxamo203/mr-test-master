using UnityEngine;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Pista de las prácticas de TUTORIALES ("qué hago ahora"), en IMGUI tras UIScale.Begin.
    // En pantalla va arriba, centrada bajo la barra superior; en el visor, una copia por
    // ojo por encima del centro (abajo van las barras de la noche y el cartel de recoger).
    public static class PistaTutorial
    {
        // Cómo se hace cada gesto de la linterna con el control que se está usando. En el
        // visor sin mando (Rediseno.ControlMirada) no hay botones: la intensa es automática
        // al mirar de frente el objetivo y prender/apagar es cruzar la franja de arriba.
        private static bool SinMando => Rediseno.ControlMirada.Activo;

        public static string ComoLuzIntensa()
        {
            if (SinMando) return "mirala de frente";
            var gm = Gamepad.GamepadManager.Instance;
            return gm != null && gm.UsesMouseInput ? "palanca arriba" : "mantené apretado";
        }

        public static string ComoApagar()
        {
            if (SinMando) return "cruzá la línea de arriba";
            var gm = Gamepad.GamepadManager.Instance;
            if (gm != null && gm.UsesMouseInput) return "palanca abajo";
            if (gm != null && gm.IsConnected) return "botón A";
            return "tocá la pantalla";
        }

        public static string ComoPrender()
        {
            if (SinMando) return "cruzá la línea de arriba";
            var gm = Gamepad.GamepadManager.Instance;
            if (gm != null && gm.UsesMouseInput) return "botón A";
            if (gm != null && gm.IsConnected) return "botón A";
            return "tocá la pantalla";
        }

        // Lo que el jugador TIENE QUE HACER va entre asteriscos en los textos de las prácticas
        // ("*apagala y prendela*") y se pinta en naranja fuerte, para leerlo de un vistazo.
        // Sirve igual para IMGUI (estilo con richText) y para las etiquetas de UI Toolkit (los
        // avisos con tarjeta), que entienden la misma etiqueta <color>.
        public const string ColorAccion = "#FF8A1E";

        public static string Resaltar(string texto) => Resaltar(texto, 1f);

        // alfa < 1: la etiqueta <color> pisa el color de GUI.color, así que el fundido del
        // naranja va en el propio color (#RRGGBBAA).
        private static string Resaltar(string texto, float alfa)
        {
            if (string.IsNullOrEmpty(texto) || texto.IndexOf('*') < 0) return texto;
            string abre = alfa >= 1f ? $"<color={ColorAccion}>"
                : $"<color={ColorAccion}{Mathf.RoundToInt(Mathf.Clamp01(alfa) * 255f):X2}>";
            var sb = new System.Text.StringBuilder(texto.Length + 32);
            bool abierto = false;
            foreach (char ch in texto)
            {
                if (ch != '*') { sb.Append(ch); continue; }
                sb.Append(abierto ? "</color>" : abre);
                abierto = !abierto;
            }
            if (abierto) sb.Append("</color>");
            return sb.ToString();
        }

        // Copias con richText de los estilos del tema, por tamaño (no se toca el estilo
        // compartido que cachea MortuoriumTheme.Estilo).
        private static readonly System.Collections.Generic.Dictionary<int, GUIStyle> _estilos = new();

        private static GUIStyle EstiloRico(int tamano)
        {
            if (_estilos.TryGetValue(tamano, out var e)) return e;
            e = new GUIStyle(T.Estilo(T.FMono, tamano, T.Cream, TextAnchor.MiddleCenter, wrap: true)) { richText = true };
            _estilos[tamano] = e;
            return e;
        }

        // Aviso pasajero ("¡Bien! …", "+50% de batería") que tapa la pista unos segundos.
        public sealed class Aviso
        {
            internal string Texto;
            internal float Desde, Hasta;

            public void Mostrar(string texto, float segundos)
            {
                Texto = texto;
                Desde = Time.time;
                Hasta = Time.time + segundos;
            }

            public void Limpiar() => Texto = null;
        }

        // Lo que tarda el aviso en entrar (sube desde abajo mientras aparece) y en irse
        // (se desvanece y vuelve la pista).
        private const float SegundosFundido = 0.25f;
        // Desde cuánto más abajo sube al entrar (px virtuales en pantalla; en el visor,
        // escalados al alto de la pantalla).
        private const float SubidaEntrada = 14f;

        // La pista, o el aviso encima mientras dure, fundiéndose entre los dos.
        public static void Dibujar(float vw, string pista, Aviso aviso)
        {
            float a = 0f, dy = 0f;
            if (aviso != null && aviso.Texto != null && Time.time < aviso.Hasta)
            {
                float entrada = Mathf.SmoothStep(0f, 1f, (Time.time - aviso.Desde) / SegundosFundido);
                float salida = Mathf.SmoothStep(0f, 1f, (aviso.Hasta - Time.time) / SegundosFundido);
                a = Mathf.Min(entrada, salida);
                dy = (1f - entrada) * SubidaEntrada;
            }
            if (a < 0.999f) Dibujar(vw, pista, 1f - a, 0f);
            if (a > 0.001f) Dibujar(vw, aviso.Texto, a, dy);
        }

        public static void Dibujar(float vw, string texto) => Dibujar(vw, texto, 1f, 0f);

        // La tarjeta crece en alto con el texto (mínimo una línea y media), así nada queda
        // cortado aunque el gesto del control sume palabras.
        private static void Dibujar(float vw, string texto, float alfa, float dy)
        {
            var contenido = new GUIContent(Resaltar(texto, alfa));
            var prevC = GUI.color;
            GUI.color = new Color(prevC.r, prevC.g, prevC.b, prevC.a * alfa);
            if (!MRCardboardController.Activo)
            {
                var estiloPantalla = EstiloRico(14);
                float ancho = vw - 56f;
                float alto = Mathf.Max(52f, estiloPantalla.CalcHeight(contenido, ancho - 28f) + 18f);
                var r = new Rect(28f, 64f + dy, ancho, alto);
                T.Tarjeta2(r, T.Hoja2, null, 14f);
                GUI.Label(new Rect(r.x + 14f, r.y, r.width - 28f, r.height), contenido, estiloPantalla);
                GUI.color = prevC;
                return;
            }

            var prevM = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            float s = Screen.height / 1080f;
            var estilo = EstiloRico(Mathf.RoundToInt(20 * s));
            float w = 360f * s;
            float h = Mathf.Max(76f * s, estilo.CalcHeight(contenido, w - 20f * s) + 22f * s);
            Rediseno.UI.OjosVisor.Centros(out var izq, out var der);
            for (int i = 0; i < 2; i++)
            {
                var c = i == 0 ? izq : der;
                // Crece hacia arriba: el borde de abajo queda siempre a la misma distancia de la mira.
                var r = new Rect(c.x - w * 0.5f, c.y - 200f * s - h + dy * s, w, h);
                T.Tarjeta2(r, T.Hoja2, null, 18f * s);
                GUI.Label(new Rect(r.x + 10f * s, r.y, w - 20f * s, h), contenido, estilo);
            }
            GUI.matrix = prevM;
            GUI.color = prevC;
        }
    }
}
