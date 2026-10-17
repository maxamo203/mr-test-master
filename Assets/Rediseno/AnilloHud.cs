using UnityEngine;

namespace Rediseno
{
    // Anillo de progreso del HUD (IMGUI, en píxeles de pantalla) para los gestos que se
    // "mantienen": abrir la pausa con la ruedita, mirar arriba. Uno en el centro de la
    // pantalla, o uno por ojo en Cardboard (centros en W*0.25 / W*0.75, disparidad 0).
    public static class AnilloHud
    {
        public static void Dibujar(float progreso, Color color, Texture2D icono)
        {
            if (progreso <= 0f || Event.current.type != EventType.Repaint) return;
            var prevM = GUI.matrix; var prevC = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            float s = Screen.height / 1080f;
            if (MRCardboardController.Activo)
            {
                UI.OjosVisor.Centros(out var izq, out var der);
                Uno(izq, s, progreso, color, icono);
                Uno(der, s, progreso, color, icono);
            }
            else Uno(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), s, progreso, color, icono);
            GUI.matrix = prevM; GUI.color = prevC;
        }

        private static void Uno(Vector2 c, float s, float progreso, Color color, Texture2D icono)
        {
            const int N = 32;
            var punto = IconosMirada.Punto;
            float radio = 30f * s, rd = 3.2f * s;
            int on = Mathf.CeilToInt(Mathf.Clamp01(progreso) * N);
            for (int i = 0; i < N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                var p = new Vector2(c.x + Mathf.Sin(a) * radio, c.y - Mathf.Cos(a) * radio);
                GUI.color = i < on ? color : new Color(1f, 1f, 1f, 0.18f);
                GUI.DrawTexture(new Rect(p.x - rd, p.y - rd, rd * 2f, rd * 2f), punto);
            }
            if (icono != null)
            {
                float ri = 13f * s;
                GUI.color = color;
                GUI.DrawTexture(new Rect(c.x - ri, c.y - ri, ri * 2f, ri * 2f), icono);
            }
        }
    }
}
