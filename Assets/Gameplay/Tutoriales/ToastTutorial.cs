using UnityEngine;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Toast de las prácticas del TUTORIAL ("+50% DE BATERÍA", "+1 RELIQUIA"): una tarjeta
    // aparte de la pista, que entra subiendo, dura unos segundos y se va sola, sin frenar el
    // juego ni pedir nada. En pantalla va abajo al centro; en el visor, una copia por ojo
    // donde estaba el cartel de recoger (que se va al recoger), apoyada sobre las barras.
    // IMGUI tras UIScale.Begin: lo dibuja la práctica dueña en su OnGUI.
    public sealed class ToastTutorial
    {
        // Lo que tarda en entrar (sube mientras aparece) y en irse (se desvanece).
        private const float SegundosFundido = 0.25f;
        // Desde cuánto más abajo sube al entrar (px virtuales; en el visor, escalados).
        private const float Subida = 14f;

        private string _titulo, _detalle;
        private Color _acento;
        private float _desde, _hasta;

        public void Mostrar(string titulo, string detalle, Color acento, float segundos)
        {
            _titulo = titulo;
            _detalle = detalle;
            _acento = acento;
            _desde = Time.time;
            _hasta = Time.time + segundos;
        }

        public void Limpiar() => _titulo = null;

        public void Dibujar(float vw, float vh)
        {
            if (_titulo == null || Time.time >= _hasta) return;
            float entrada = Mathf.SmoothStep(0f, 1f, (Time.time - _desde) / SegundosFundido);
            float salida = Mathf.SmoothStep(0f, 1f, (_hasta - Time.time) / SegundosFundido);
            float dy = (1f - entrada) * Subida;

            var prevC = GUI.color;
            GUI.color = new Color(prevC.r, prevC.g, prevC.b, prevC.a * Mathf.Min(entrada, salida));
            if (!MRCardboardController.Activo)
            {
                float w = 300f, h = _detalle != null ? 64f : 44f;
                Tarjeta(new Rect(vw * 0.5f - w * 0.5f, vh - T.HudMargin - h + dy, w, h), 1f);
            }
            else
            {
                var prevM = GUI.matrix;
                GUI.matrix = Matrix4x4.identity;
                float s = Screen.height / 1080f;
                // Mismo lugar que el cartel de recoger (ContextActionController.CartelEnOjos):
                // apoyado sobre la fila más alta de las barras.
                T.HudBarRects(Scanner.UIScale.VirtualWidth, Scanner.UIScale.VirtualHeight,
                              2, out var filaAlta, out _);
                float topeBarras = filaAlta.y * Scanner.UIScale.Factor + Scanner.UIScale.SafeGui.y;
                Rediseno.UI.OjosVisor.Centros(out var izq, out var der);
                float w = 300f * s, h = (_detalle != null ? 92f : 60f) * s;
                for (int i = 0; i < 2; i++)
                {
                    var c = i == 0 ? izq : der;
                    float y = Mathf.Min(c.y + 120f * s, topeBarras - 12f * s - h) + dy * s;
                    Tarjeta(new Rect(c.x - w * 0.5f, y, w, h), s * 1.4f);
                }
                GUI.matrix = prevM;
            }
            GUI.color = prevC;
        }

        private void Tarjeta(Rect r, float s)
        {
            T.Tarjeta2(r, T.Hoja2, _acento, 16f * s, 1.5f * s);
            var stTit = T.Estilo(T.FBebas, Mathf.RoundToInt(26 * s), _acento, TextAnchor.MiddleCenter);
            if (_detalle == null) { GUI.Label(r, _titulo, stTit); return; }
            var stDet = T.Estilo(T.FMono, Mathf.RoundToInt(12 * s), T.CreamDim, TextAnchor.MiddleCenter);
            GUI.Label(new Rect(r.x, r.y + 4f * s, r.width, 32f * s), _titulo, stTit);
            GUI.Label(new Rect(r.x + 8f * s, r.yMax - 28f * s, r.width - 16f * s, 22f * s), _detalle, stDet);
        }
    }
}
