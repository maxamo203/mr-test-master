using UnityEngine;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Flecha que lleva al jugador hasta una pieza del TUTORIAL (la pila, la reliquia). Gira
    // alrededor del centro de la vista apuntando hacia donde hay que girar; cuando la pieza
    // ya queda adelante, la flecha se va y sólo queda la distancia. En el visor, una copia
    // por ojo. IMGUI en píxeles de pantalla (llamar desde OnGUI).
    public static class GuiaTutorial
    {
        // A partir de cuántos grados del centro la pieza cuenta como "no está adelante".
        private const float GradosAdelante = 14f;
        // Ya al alcance: el cartel de RECOGER toma la posta.
        private const float MetrosCerca = 1.1f;

        private static Texture2D _flecha;

        public static void Dibujar(Vector3 objetivo, string nombre)
        {
            if (Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 dir = objetivo - cam.transform.position;
            Vector3 plano = dir; plano.y = 0f;
            float metros = plano.magnitude;
            float angulo = Vector3.Angle(cam.transform.forward, dir);
            bool adelante = angulo <= GradosAdelante;
            if (adelante && metros <= MetrosCerca) return;

            // Hacia dónde girar, en la pantalla (y hacia abajo en IMGUI). Si está justo atrás
            // no hay lado preferido: se apunta a la derecha.
            Vector3 local = cam.transform.InverseTransformDirection(dir);
            var giro = new Vector2(local.x, -local.y);
            if (local.z < 0f) giro.y = 0f;   // atrás: que diga "girá", no "mirá abajo"
            if (giro.sqrMagnitude < 1e-4f) giro = Vector2.right;
            giro.Normalize();

            var prevM = GUI.matrix;
            var prevC = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            float s = Screen.height / 1080f;
            float pulso = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f);
            string texto = $"{nombre} · {metros:0.0} m";
            var estilo = T.Estilo(T.FMono, Mathf.RoundToInt(22 * s), T.Cream, TextAnchor.MiddleCenter);

            if (MRCardboardController.Activo)
            {
                Rediseno.UI.OjosVisor.Centros(out var izq, out var der);
                DibujarEn(izq, s, giro, adelante, pulso, texto, estilo);
                DibujarEn(der, s, giro, adelante, pulso, texto, estilo);
            }
            else
            {
                DibujarEn(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), s * 1.3f, giro, adelante,
                          pulso, texto, estilo);
            }
            GUI.color = prevC;
            GUI.matrix = prevM;
        }

        private static void DibujarEn(Vector2 centro, float s, Vector2 giro, bool adelante, float pulso,
                                      string texto, GUIStyle estilo)
        {
            if (adelante)
            {
                // Adelante: sólo la distancia, debajo de la mira (arriba va la pista).
                var r = new Rect(centro.x - 160f * s, centro.y + 70f * s, 320f * s, 36f * s);
                GUI.color = new Color(1f, 1f, 1f, 0.9f);
                GUI.Label(r, texto, estilo);
                return;
            }

            float radio = 120f * s, lado = 64f * s;
            Vector2 punta = centro + giro * radio;
            float grados = Mathf.Atan2(giro.y, giro.x) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(grados, punta);
            GUI.color = new Color(T.Tan.r, T.Tan.g, T.Tan.b, pulso);
            GUI.DrawTexture(new Rect(punta.x - lado * 0.5f, punta.y - lado * 0.5f, lado, lado), Flecha);
            GUI.matrix = m;

            // El nombre y la distancia, del lado de adentro de la flecha.
            Vector2 c = centro + giro * (radio - 70f * s);
            GUI.color = Color.white;
            GUI.Label(new Rect(c.x - 150f * s, c.y - 18f * s, 300f * s, 36f * s), texto, estilo);
        }

        // Punta de flecha apuntando a +X, blanca con borde suave (se tiñe con GUI.color).
        private static Texture2D Flecha
        {
            get
            {
                if (_flecha != null) return _flecha;
                const int n = 64;
                _flecha = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    name = "GuiaTutorial.Flecha", wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave,
                };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Triángulo (12,8)-(12,56)-(56,32) en coordenadas de textura.
                    float u = x + 0.5f, v = y + 0.5f;
                    float d = Mathf.Min(u - 12f,
                              Mathf.Min(DistLado(u, v, 12f, 56f, 56f, 32f),
                                        DistLado(u, v, 56f, 32f, 12f, 8f)));
                    byte a = (byte)(Mathf.Clamp01(d / 1.5f) * 255f);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
                _flecha.SetPixels32(px);
                _flecha.Apply(false, true);
                return _flecha;
            }
        }

        // Distancia con signo de (u,v) a la recta a→b (positiva del lado de adentro del
        // triángulo, que se recorre en sentido horario en la textura).
        private static float DistLado(float u, float v, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            return ((u - ax) * dy - (v - ay) * dx) / len;
        }
    }
}
