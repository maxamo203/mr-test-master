using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gameplay
{
    // Pantalla de muerte del jugador local. Se muestra cuando LocalDeath.IsDead.
    // "Volver al menu" limpia los singletons cross-scene (como SceneNavUI) y carga el
    // menu de noche.
    public class DeathScreenUI : MonoBehaviour
    {
        [SerializeField] private string _menuScene = "NightMenuScene";

        private GUIStyle _title, _btn;
        private bool _ready;
        private readonly Gamepad.ImguiGamepadMenu _nav = new();

        private void Awake() => LocalDeath.Ensure();

        private void Update() => _nav.Update();

        private void EnsureStyles()
        {
            if (_ready) return;
            _title = new GUIStyle(GUI.skin.label)  { fontSize = 54, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color(0.8f, 0.05f, 0.05f);
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 26 };
            _ready = true;
        }

        private void OnGUI()
        {
            _nav.Begin();
            var ld = LocalDeath.Instance;
            if (ld == null || !ld.IsDead) return;

            EnsureStyles();

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.88f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            var sa = Scanner.SafeArea.GuiRect;
            GUI.Label(new Rect(sa.x, sa.y + sa.height * 0.28f, sa.width, 80f), "MORISTE", _title);

            float w = 300f, h = 74f;
            var r = new Rect(sa.x + (sa.width - w) * 0.5f, sa.y + sa.height * 0.52f, w, h);
            _nav.Button(r, "Volver al menú", _btn, ReturnToMenu);
            _nav.End();
        }

        private void ReturnToMenu()
        {
            // Teardown de la sesion (mismo criterio que SceneNavUI): salir de la partida.
            if (NetworkManager.Instance != null) Destroy(NetworkManager.Instance.gameObject);
            if (EntityRegistry.Instance != null) Destroy(EntityRegistry.Instance.gameObject);
            if (WorldOrigin.Instance    != null) Destroy(WorldOrigin.Instance.gameObject);
            SceneManager.LoadScene(_menuScene);
        }
    }
}
