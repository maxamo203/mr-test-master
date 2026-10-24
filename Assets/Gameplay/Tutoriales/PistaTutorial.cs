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
            if (SinMando) return "mirala de frente: la luz se concentra sola";
            var gm = Gamepad.GamepadManager.Instance;
            return gm != null && gm.UsesMouseInput ? "palanca arriba" : "mantené apretado el botón";
        }

        public static string ComoApagar()
        {
            if (SinMando) return "mirá arriba hasta cruzar la línea de la linterna y volvé";
            var gm = Gamepad.GamepadManager.Instance;
            if (gm != null && gm.UsesMouseInput) return "palanca abajo";
            if (gm != null && gm.IsConnected) return "un toque al botón A";
            return "un toque en la pantalla";
        }

        public static string ComoPrender()
        {
            if (SinMando) return "volvé a cruzar la línea de arriba";
            var gm = Gamepad.GamepadManager.Instance;
            if (gm != null && gm.UsesMouseInput) return "soltá la palanca o movela a la derecha";
            if (gm != null && gm.IsConnected) return "un toque al botón A";
            return "un toque en la pantalla";
        }

        public static void Dibujar(float vw, string texto)
        {
            if (!MRCardboardController.Activo)
            {
                var r = new Rect(28f, 64f, vw - 56f, 52f);
                T.Tarjeta2(r, T.Hoja2, null, 14f);
                GUI.Label(new Rect(r.x + 14f, r.y, r.width - 28f, r.height), texto,
                          T.Estilo(T.FMono, 14, T.Cream, TextAnchor.MiddleCenter, wrap: true));
                return;
            }

            var prevM = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            float s = Screen.height / 1080f;
            var estilo = T.Estilo(T.FMono, Mathf.RoundToInt(20 * s), T.Cream, TextAnchor.MiddleCenter, wrap: true);
            Rediseno.UI.OjosVisor.Centros(out var izq, out var der);
            for (int i = 0; i < 2; i++)
            {
                var c = i == 0 ? izq : der;
                float w = 320f * s, h = 76f * s;
                var r = new Rect(c.x - w * 0.5f, c.y - 200f * s - h, w, h);
                T.Tarjeta2(r, T.Hoja2, null, 18f * s);
                GUI.Label(new Rect(r.x + 10f * s, r.y, w - 20f * s, h), texto, estilo);
            }
            GUI.matrix = prevM;
        }
    }
}
