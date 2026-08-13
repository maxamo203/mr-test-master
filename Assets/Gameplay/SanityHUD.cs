using UnityEngine;
using Scanner;   // UIScale
using T = MortuoriumTheme;

namespace Gameplay
{
    // HUD de cordura del jugador local: una barra abajo-izquierda, APILADA encima de
    // la barra de batería (misma estética), y al llegar a cordura 0 una distorsion
    // visual de la interfaz. Solo lee LocalSanity (el server es autoritativo).
    //
    // Wiring: poner en un GameObject de SampleScene. Se muestra solo en partida.
    public class SanityHUD : MonoBehaviour
    {
        private static readonly int ID_DISTORT = Shader.PropertyToID("_SanityDistort");

        private void Awake()
        {
            LocalSanity.Ensure();
            TensionSystem.Ensure();
            CameraFXOverlay.Ensure();
        }

        private void OnGUI()
        {
            var net = NetworkManager.Instance;
            if (net == null || !net.GameStarted) return;
            var ls = LocalSanity.Instance;
            if (ls == null) return;

            float pct = ls.Value01;

            // ── Distorsion a cordura 0 (full-screen, sin la matriz de UIScale) ──
            if (pct <= 0f) DrawDistortion();
            else           Shader.SetGlobalFloat(ID_DISTORT, 0f);

            // ── Barra (fila 1: encima de la batería) ──────────────────────────
            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            var r = T.HudBarRect(vw, vh, fila: 1);
            Color fill = Color.Lerp(T.Red, T.Blue, pct);
            T.Barra(r, pct, fill, "CORDURA", $"{Mathf.RoundToInt(pct * 100f)}%");

            // ── Amanecer (fila 2): cuánto falta para ganar la noche ───────────
            // Sin esto la condición de victoria es invisible. Las noches con
            // nightDurationSeconds = 0 no tienen reloj y la barra no se dibuja.
            if (NightResult.HayReloj)
                T.Barra(T.HudBarRect(vw, vh, fila: 2), NightResult.Progreso01, T.Tan,
                        "AMANECE", NightResult.RestanteMmSs);
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
