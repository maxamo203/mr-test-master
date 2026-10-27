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

        // Hasta cuándo (Time.unscaledTime) la fila de la cordura se dibuja resaltada: la
        // práctica del Arbmos la presenta así la primera vez que baja.
        public static float ResaltarHasta;

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

            // Espectando: la cordura ya no es suya. Ni distorsión (la partida tiene que
            // verse clara) ni barra; sólo queda el reloj del amanecer.
            bool espectando = LocalDeath.Instance != null && LocalDeath.Instance.Espectando;

            // ── Distorsion a cordura 0 (full-screen, sin la matriz de UIScale) ──
            if (pct <= 0f && !espectando) DrawDistortion();
            else                          Shader.SetGlobalFloat(ID_DISTORT, 0f);

            // ── Barra (fila 1: encima de la batería) ──────────────────────────
            // En las prácticas donde nada baja la cordura (todas menos ARBMOS) esa fila la usa
            // el objetivo de la práctica (TutorialPilasReliquias / Sorken / Libro / Veleth).
            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            var tutorial = GameSession.Instance != null ? GameSession.Instance.Tutorial : TutorialTipo.Ninguno;
            bool practica = tutorial != TutorialTipo.Ninguno && tutorial != TutorialTipo.Arbmos;
            if (!espectando && !practica) DibujarCordura(vw, vh, pct);

            // ── Noche (fila 2): cuánto queda hasta el amanecer ────────────────
            // Sin esto la condición de victoria es invisible. La barra muestra lo que QUEDA
            // (se vacía hacia el amanecer). Las noches con nightDurationSeconds = 0 no tienen
            // reloj y la fila no se dibuja.
            if (NightResult.HayReloj)
                T.HudFila(vw, vh, 2, 1f - NightResult.Progreso01, Plata,
                          new T.IconoHud { tex = Rediseno.IconosHud.Luna, color = Plata, nivel = -1f },
                          "NOCHE", NightResult.RestanteMmSs);
        }

        // ── Cordura: el cerebro se deteriora y el color se mancha hacia el rojo ──
        // Hueso (sana) → ocre (fisura) → óxido (agrietada) → rojo latiendo (al borde), con el
        // ícono del cerebro en la etapa que toca (Rediseno.IconosHud). Un golpe deja lo perdido
        // en rojo un instante (fantasma) y, si cambia de etapa, el cerebro tiembla.
        private static readonly Color Plata = new(0.663f, 0.678f, 0.722f, 1f);   // #a9adb8
        private static readonly Color[] ColoresCordura =
        {
            new(0.839f, 0.796f, 0.722f, 1f),   // #d6cbb8 hueso
            new(0.706f, 0.604f, 0.416f, 1f),   // #b49a6a ocre apagado
            new(0.659f, 0.337f, 0.180f, 1f),   // #a8562e óxido
            new(0.784f, 0.196f, 0.243f, 1f),   // #c8323e rojo sangre
        };
        private const float EsperaFantasma = 0.35f, VelFantasma = 0.6f, SegundosTemblor = 0.45f;

        private float _fantasma = -1f, _fantasmaDesde;
        private int _etapa = -1;
        private float _temblorHasta;
        private int _ultimoFrame = -1;

        private void DibujarCordura(float vw, float vh, float pct)
        {
            float t = Time.unscaledTime;
            int etapa = Rediseno.IconosHud.EtapaCordura(pct);
            // OnGUI corre varias veces por frame: el estado avanza una sola vez por frame.
            if (Time.frameCount != _ultimoFrame)
            {
                _ultimoFrame = Time.frameCount;
                if (_fantasma < pct || _fantasma < 0f) _fantasma = pct;        // sube o arranca: sin fantasma
                else if (_fantasma > pct + 0.001f && t - _fantasmaDesde > EsperaFantasma)
                    _fantasma = Mathf.MoveTowards(_fantasma, pct, VelFantasma * Time.unscaledDeltaTime);
                if (_fantasma <= pct + 0.001f) _fantasmaDesde = t;           // el próximo golpe espera de nuevo

                if (_etapa >= 0 && etapa != _etapa) _temblorHasta = t + SegundosTemblor;
                _etapa = etapa;
            }

            Color color = ColoresCordura[etapa];
            if (etapa == 3) color.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 4f));
            var icono = new T.IconoHud
            {
                tex = Rediseno.IconosHud.Cerebro(etapa), color = color, nivel = -1f,
                capa = Rediseno.IconosHud.Grietas(etapa), colorCapa = ColoresCordura[3],
                sacudir = t < _temblorHasta ? 2.5f : 0f,
            };
            T.HudFila(vw, vh, 1, pct, color, icono, "CORDURA", FlashlightHUD.Porcentaje(pct),
                      fantasma01: _fantasma, resaltar: t < ResaltarHasta);
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
