using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gameplay
{
    // Menu inicial (en su propia escena, ANTES del lobby): el jugador elige la NOCHE y
    // pasa al lobby (SampleScene), donde se conecta y se comparte el mapa. La noche
    // elegida viaja en GameSession; el host la aplica al arrancar la partida.
    //
    // Wiring: poner este componente en un GameObject de la escena del menu. Arrastrar
    // los assets NightConfig a _nights. La escena del menu y SampleScene deben estar en
    // Build Settings > Scenes In Build.
    public class NightMenuUI : MonoBehaviour
    {
        [Tooltip("Noches disponibles (arrastra los assets NightConfig).")]
        [SerializeField] private NightConfig[] _nights;
        [Tooltip("Escena del lobby/gameplay a cargar al continuar.")]
        [SerializeField] private string _lobbyScene = "SampleScene";

        private int _selected;
        private GUIStyle _title, _btn, _btnSel;
        private bool _styleReady;

        // Navegación con gamepad de las noches + "Continuar".
        private readonly Gamepad.ImguiGamepadMenu _nav = new();

        private void Awake() => GameSession.Ensure();

        private void Update() => _nav.Update();

        private void EnsureStyles()
        {
            if (_styleReady) return;
            _title  = new GUIStyle(GUI.skin.label)  { fontSize = 40, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = Color.white;
            _btn    = new GUIStyle(GUI.skin.button) { fontSize = 26 };
            _btnSel = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = FontStyle.Bold };
            _btnSel.normal.textColor = Color.yellow;
            _styleReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            _nav.Begin();

            // Fondo negro.
            var prev = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            var sa = Scanner.SafeArea.GuiRect;
            float w = Mathf.Min(560f, sa.width - 40f);
            float x = sa.x + (sa.width - w) * 0.5f;
            float y = sa.y + sa.height * 0.10f;

            GUI.Label(new Rect(x, y, w, 60f), "Elegí la noche", _title);
            y += 90f;

            if (_nights == null || _nights.Length == 0)
            {
                GUI.Label(new Rect(x, y, w, 40f), "Sin noches configuradas (asigná NightConfig).", _title);
                _nav.End();
                return;
            }

            _selected = Mathf.Clamp(_selected, 0, _nights.Length - 1);
            for (int i = 0; i < _nights.Length; i++)
            {
                var n = _nights[i];
                string label = n != null
                    ? (string.IsNullOrEmpty(n.displayName) ? n.name : n.displayName)
                    : "(vacío)";
                int idx = i;   // captura por iteración para el closure
                _nav.Button(new Rect(x, y, w, 60f), label, idx == _selected ? _btnSel : _btn,
                            () => _selected = idx);
                y += 72f;
            }

            y += 20f;
            GUI.enabled = _nights[_selected] != null;
            _nav.Button(new Rect(x, y, w, 80f), "Continuar", _btn, () =>
            {
                GameSession.Ensure().SelectedNight = _nights[_selected];
                SceneManager.LoadScene(_lobbyScene);
            });
            GUI.enabled = true;

            _nav.End();
        }
    }
}
