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

        private void OnGUI()
        {
            var ld = LocalDeath.Instance;
            if (ld == null || !ld.IsDead) return;

            UIScale.Begin();
            _nav.Begin();

            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

            // Overlay oscuro que bloquea la escena.
            var full = new Rect(0, 0, vw, vh);
            T.Fill(full, new Color(T.Bg.r, T.Bg.g, T.Bg.b, 0.9f));
            UIBlocker.AddVirtualRect(full);

            // Título "MORISTE" (rojo, con leve glitch cromático).
            var titRect = new Rect(0, vh * 0.28f, vw, 90f);
            GUI.Label(new Rect(titRect.x + 2f, titRect.y, titRect.width, titRect.height), "MORISTE",
                      T.Estilo(T.FBebas, 64, new Color(0f, 0.22f, 0.20f, 0.5f), TextAnchor.MiddleCenter));
            GUI.Label(titRect, "MORISTE", T.Estilo(T.FBebas, 64, T.Red, TextAnchor.MiddleCenter));

            GUI.Label(new Rect(Pad, vh * 0.28f + 96f, vw - Pad * 2f, 26f),
                      "el ritual te reclamó… por ahora.",
                      T.Estilo(T.FElite, 14, T.Muted, TextAnchor.MiddleCenter));

            // Botón estilizado.
            T.Boton(_nav, new Rect(Pad, vh - 44f - 58f, vw - Pad * 2f, 58f),
                    "VOLVER AL MENÚ", primario: true, ReturnToMenu);

            _nav.End();
        }

        private void ReturnToMenu()
        {
            // Teardown de la sesión (mismo criterio que SceneFlow): salir de la partida.
            if (NetworkManager.Instance != null) Destroy(NetworkManager.Instance.gameObject);
            if (EntityRegistry.Instance != null) Destroy(EntityRegistry.Instance.gameObject);
            if (WorldOrigin.Instance    != null) Destroy(WorldOrigin.Instance.gameObject);
            SceneManager.LoadScene(_menuScene);
        }
    }
}
