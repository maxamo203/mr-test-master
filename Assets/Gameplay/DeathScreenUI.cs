using UnityEngine;
using UnityEngine.SceneManagement;
using Scanner;   // UIScale, UIBlocker
using T = MortuoriumTheme;

namespace Gameplay
{
    // Pantalla de muerte del jugador local (estilo Mortuorium). Se muestra cuando
    // LocalDeath.IsDead. "VOLVER AL MENÚ" limpia los singletons cross-scene (mismo
    // criterio que SceneFlow) y carga el menú de noche.
    public class DeathScreenUI : MonoBehaviour
    {
        [SerializeField] private string _menuScene = "NightMenuScene";

        private readonly Gamepad.ImguiGamepadMenu _nav = new();

        private const float Pad = 28f;

        private void Awake() => LocalDeath.Ensure();

        private void Update() => _nav.Update();

        // Modal para elegir cualquier noche del catálogo (no sólo la siguiente).
        private bool _selectorAbierto;

        private void OnGUI()
        {
            var ld = LocalDeath.Instance;
            bool muerto     = ld != null && ld.IsDead;
            bool sobrevivio = NightResult.Sobrevivio;
            if (!muerto && !sobrevivio) { _selectorAbierto = false; return; }

            UIScale.Begin();
            _nav.Begin();

            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

            // Overlay oscuro en toda la pantalla (incluye fuera del área segura).
            T.FillScreen(new Color(T.Bg.r, T.Bg.g, T.Bg.b, 0.92f));
            UIBlocker.AddVirtualRect(new Rect(0, 0, vw, vh));

            // El modal reemplaza a la pantalla de muerte: si dibujáramos las dos, los
            // botones de abajo seguirían recibiendo taps a través del overlay.
            if (_selectorAbierto)
            {
                DrawSelectorNoches(vw, vh);
                _nav.End();
                return;
            }

            // Título (con leve glitch cromático), según cómo terminó la noche.
            string titulo  = sobrevivio ? "SOBREVIVISTE" : "MORISTE";
            Color  colorTit = sobrevivio ? T.Green : T.Red;
            var titRect = new Rect(0, vh * 0.28f, vw, 90f);
            GUI.Label(new Rect(titRect.x + 2f, titRect.y, titRect.width, titRect.height), titulo,
                      T.Estilo(T.FBebas, 64, new Color(0f, 0.22f, 0.20f, 0.5f), TextAnchor.MiddleCenter));
            GUI.Label(titRect, titulo, T.Estilo(T.FBebas, 64, colorTit, TextAnchor.MiddleCenter));

            GUI.Label(new Rect(Pad, vh * 0.28f + 96f, vw - Pad * 2f, 26f),
                      sobrevivio ? "amaneció. el ritual sigue, pero no esta noche."
                                 : "el ritual te reclamó… por ahora.",
                      T.Estilo(T.FElite, 14, T.Muted, TextAnchor.MiddleCenter));

            // Aviso de desbloqueo (sólo si esta victoria movió de verdad la progresión;
            // rejugar una noche ya superada no desbloquea nada).
            if (sobrevivio && NightResult.NocheDesbloqueada > 0)
                GUI.Label(new Rect(Pad, vh * 0.28f + 126f, vw - Pad * 2f, 24f),
                          $"noche {NightResult.NocheDesbloqueada} desbloqueada",
                          T.Estilo(T.FMono, 12, T.Tan, TextAnchor.MiddleCenter));

            // Reintentar / cambiar de noche sólo las decide el host: reinician la noche
            // para TODOS. El cliente espera (al llegar el ResetNight revive).
            bool host = NightTransition.PuedeReiniciar;
            float y = vh - 44f - 58f;

            T.Boton(_nav, new Rect(Pad, y, vw - Pad * 2f, 58f),
                    "VOLVER AL MENÚ", primario: !host, ReturnToMenu);

            if (host)
            {
                y -= 66f;
                T.Boton(_nav, new Rect(Pad, y, vw - Pad * 2f, 58f),
                        "ELEGIR NOCHE", primario: false,
                        () => _selectorAbierto = true,
                        enabled: NightTransition.HayCatalogoDeNoches, fontSize: 18);

                y -= 66f;
                // Tras ganar lo natural es seguir; tras morir, repetir.
                if (sobrevivio && SiguienteDesbloqueada(out int idxSiguiente))
                    T.Boton(_nav, new Rect(Pad, y, vw - Pad * 2f, 58f),
                            "SIGUIENTE NOCHE", primario: true,
                            () => NightTransition.IrANoche(idxSiguiente));
                else
                    T.Boton(_nav, new Rect(Pad, y, vw - Pad * 2f, 58f),
                            "REINTENTAR NOCHE", primario: true, NightTransition.Reintentar);
            }
            else
            {
                GUI.Label(new Rect(Pad, y - 34f, vw - Pad * 2f, 24f),
                          "esperando a que el host reinicie la noche…",
                          T.Estilo(T.FMono, 11, T.Dim, TextAnchor.MiddleCenter));
            }

            _nav.End();
        }

        // ¿La noche recién superada dejó desbloqueada una siguiente que existe?
        private static bool SiguienteDesbloqueada(out int index)
        {
            index = -1;
            var s = GameSession.Instance;
            if (s == null || s.NightIndex < 0) return false;
            int sig = s.NightIndex + 1;
            if (!s.NocheDisponible(sig) || !NightProgress.Desbloqueada(sig)) return false;
            index = sig;
            return true;
        }


        // Selector de noche: grilla con TODAS las del catálogo, marcando la que se
        // acaba de jugar. Mismo lenguaje visual que la grilla del menú principal
        // (NightMenuUI.DrawNoches): número grande + estado, celda bloqueada con candado
        // donde no hay NightConfig configurada.
        private void DrawSelectorNoches(float vw, float vh)
        {
            var s = GameSession.Instance;
            var nights = s != null ? s.Nights : null;

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "ELEGÍ LA NOCHE",
                      T.Estilo(T.FBebas, 28, T.Cream));
            GUI.Label(new Rect(Pad, 130f, vw - Pad * 2f, 22f),
                      "la sesión no se reinicia: tus anclas siguen puestas",
                      T.Estilo(T.FMono, 11, T.Dim));

            int total = Mathf.Max(6, nights != null ? nights.Length : 0);
            const float gap = 12f, ch = 86f, y0 = 170f;
            float cw = (vw - Pad * 2f - gap) / 2f;

            for (int i = 0; i < total; i++)
            {
                int col = i % 2, fila = i / 2;
                var r = new Rect(Pad + col * (cw + gap), y0 + fila * (ch + gap), cw, ch);

                // Sin NightConfig configurada, o todavía no desbloqueada: candado.
                if (s == null || !s.NocheDisponible(i) || !NightProgress.Desbloqueada(i))
                {
                    T.Fill(r, new Color(1f, 1f, 1f, 0.02f));
                    T.Borde(r, T.BorderDim);
                    GUI.Label(new Rect(r.x, r.y + 12f, r.width, 34f), (i + 1).ToString(),
                              T.Estilo(T.FBebas, 26, T.Disabled, TextAnchor.MiddleCenter));
                    T.Candado(new Rect(r.center.x - 8f, r.y + 50f, 16f, 18f), T.Disabled);
                    continue;
                }

                int idx = i;                       // captura por iteración para el closure
                bool actual = s.NightIndex == i;   // la que acabás de jugar

                T.Celda(_nav, r, primario: false,
                        () => { _selectorAbierto = false; NightTransition.IrANoche(idx); },
                        bordeOverride: actual ? T.Red : (Color?)null,
                        fillOverride: actual ? new Color(T.Red.r, T.Red.g, T.Red.b, 0.14f) : (Color?)null);

                GUI.Label(new Rect(r.x, r.y + 10f, r.width, 32f), (i + 1).ToString(),
                          T.Estilo(T.FBebas, 26, T.Cream, TextAnchor.MiddleCenter));

                var noche = s.Nights[i];
                string nombre = noche != null && !string.IsNullOrEmpty(noche.displayName)
                    ? noche.displayName : $"noche {i + 1}";
                GUI.Label(new Rect(r.x + 6f, r.y + 42f, r.width - 12f, 18f), nombre.ToUpperInvariant(),
                          T.Estilo(T.FMono, 10, T.CreamDim, TextAnchor.MiddleCenter));

                GUI.Label(new Rect(r.x, r.y + 62f, r.width, 18f),
                          actual ? "recién jugada" : "disponible",
                          T.Estilo(T.FMono, 10, actual ? T.Red : T.Dim, TextAnchor.MiddleCenter));
            }

            T.Boton(_nav, new Rect(Pad, vh - 44f - 58f, vw - Pad * 2f, 58f),
                    "VOLVER", primario: false, () => _selectorAbierto = false, fontSize: 18);
        }

        private void ReturnToMenu()
        {
            // NightResult es estático y sobrevive el cambio de escena: sin esto, la
            // próxima partida arrancaría mostrando ya la pantalla de fin de noche.
            NightResult.Limpiar();

            // Teardown de la sesión (mismo criterio que SceneFlow): salir de la partida.
            if (NetworkManager.Instance != null) Destroy(NetworkManager.Instance.gameObject);
            if (EntityRegistry.Instance != null) Destroy(EntityRegistry.Instance.gameObject);
            if (WorldOrigin.Instance    != null) Destroy(WorldOrigin.Instance.gameObject);
            SceneManager.LoadScene(_menuScene);
        }
    }
}
