using UnityEngine;

namespace Gameplay
{
    // HUD de cordura del jugador local: una barra discreta arriba-centro (pensada para
    // no tapar el centro de la accion) y, al llegar a cordura 0, una distorsion visual
    // de la interfaz. Solo lee LocalSanity (el server es autoritativo).
    //
    // Wiring: poner en un GameObject de SampleScene. Se muestra solo en partida.
    public class SanityHUD : MonoBehaviour
    {
        [Tooltip("Ancho de la barra de cordura (px virtuales de pantalla).")]
        [SerializeField] private float _barWidth = 220f;

        private GUIStyle _label;
        private bool _styleReady;
        private static readonly int ID_DISTORT = Shader.PropertyToID("_SanityDistort");

        private void Awake() => LocalSanity.Ensure();

        private void EnsureStyle()
        {
            if (_styleReady) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            _label.normal.textColor = Color.white;
            _styleReady = true;
        }

        private void OnGUI()
        {
            var net = NetworkManager.Instance;
            if (net == null || !net.GameStarted) return;
            var ls = LocalSanity.Instance;
            if (ls == null) return;

            EnsureStyle();
            float pct = ls.Value01;

            // ── Barra (arriba-centro) ─────────────────────────────────────────
            float w = _barWidth, h = 16f;
            var box  = new Rect((Screen.width - w) * 0.5f, 18f, w, h);
            var prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);

            var fill = new Rect(box.x + 2f, box.y + 2f, (box.width - 4f) * pct, box.height - 4f);
            GUI.color = Color.Lerp(new Color(0.8f, 0.1f, 0.1f), new Color(0.5f, 0.85f, 1f), pct);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);

            GUI.color = prev;
            GUI.Label(new Rect(box.x, box.y - 2f, box.width, box.height), "Cordura", _label);

            // ── Distorsion a cordura 0 (no recuperable) ───────────────────────
            if (pct <= 0f) DrawDistortion();
            else           Shader.SetGlobalFloat(ID_DISTORT, 0f);
        }

        // Distorsion IMGUI sin shader: tinte rojo-oscuro pulsante + bandas de glitch.
        // Ademas publica _SanityDistort (0/1) por si luego se agrega un post-effect real.
        private void DrawDistortion()
        {
            Shader.SetGlobalFloat(ID_DISTORT, 1f);
            var prev = GUI.color;
            float t = Time.unscaledTime;

            float a = 0.22f + 0.14f * Mathf.Sin(t * 6f);
            GUI.color = new Color(0.06f, 0f, 0f, Mathf.Clamp01(a));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            var rng = new System.Random((int)(t * 12f));
            for (int i = 0; i < 5; i++)
            {
                float y  = (float)rng.NextDouble() * Screen.height;
                float bh = 3f + (float)rng.NextDouble() * 20f;
                GUI.color = new Color(1f, 1f, 1f, 0.04f + (float)rng.NextDouble() * 0.12f);
                GUI.DrawTexture(new Rect(0, y, Screen.width, bh), Texture2D.whiteTexture);
            }

            GUI.color = prev;
        }

        private void OnDisable() => Shader.SetGlobalFloat(ID_DISTORT, 0f);
    }
}
