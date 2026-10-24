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
            if (gm != null && gm.UsesMouseInput) return "palanca a la derecha";
            if (gm != null && gm.IsConnected) return "botón A";
            return "tocá la pantalla";
        }

        // La tarjeta crece en alto con el texto (mínimo una línea y media), así nada queda
        // cortado aunque el gesto del control sume palabras.
        public static void Dibujar(float vw, string texto)
        {
            var contenido = new GUIContent(texto);
            if (!MRCardboardController.Activo)
            {
                var estiloPantalla = T.Estilo(T.FMono, 14, T.Cream, TextAnchor.MiddleCenter, wrap: true);
                float ancho = vw - 56f;
                float alto = Mathf.Max(52f, estiloPantalla.CalcHeight(contenido, ancho - 28f) + 18f);
                var r = new Rect(28f, 64f, ancho, alto);
                T.Tarjeta2(r, T.Hoja2, null, 14f);
                GUI.Label(new Rect(r.x + 14f, r.y, r.width - 28f, r.height), contenido, estiloPantalla);
                return;
            }

            var prevM = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            float s = Screen.height / 1080f;
            var estilo = T.Estilo(T.FMono, Mathf.RoundToInt(20 * s), T.Cream, TextAnchor.MiddleCenter, wrap: true);
            float w = 360f * s;
            float h = Mathf.Max(76f * s, estilo.CalcHeight(contenido, w - 20f * s) + 22f * s);
            Rediseno.UI.OjosVisor.Centros(out var izq, out var der);
            for (int i = 0; i < 2; i++)
            {
                var c = i == 0 ? izq : der;
                // Crece hacia arriba: el borde de abajo queda siempre a la misma distancia de la mira.
                var r = new Rect(c.x - w * 0.5f, c.y - 200f * s - h, w, h);
                T.Tarjeta2(r, T.Hoja2, null, 18f * s);
                GUI.Label(new Rect(r.x + 10f * s, r.y, w - 20f * s, h), contenido, estilo);
            }
            GUI.matrix = prevM;
        }
    }
}
