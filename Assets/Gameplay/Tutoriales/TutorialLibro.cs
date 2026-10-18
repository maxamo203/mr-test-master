using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica LIBRO RITUAL: el libro (el 0,0 del escenario, donde está la imagen) se
    // oscurece cada tanto; alumbrarlo con la luz intensa unos segundos lo salva. Hay que
    // salvarlo MetaSalvar veces. Si se apaga del todo, en una noche sale Veleth; acá no
    // (RitualBookDirector lo restaura sin invocarla) y se sigue intentando. Al completar, la
    // noche se gana (ServerNightSurvived) y FinDeNoche2 la muestra como PRÁCTICA COMPLETA.
    public class TutorialLibro : MonoBehaviour
    {
        public const int MetaSalvar = 2;
        private const float DemoraFin = 2f;
        private const float SegundosAviso = 4f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            var sesion = GameSession.Instance;
            if (s.name != SceneFlow.EscenaJuego || sesion == null ||
                sesion.Tutorial != TutorialTipo.Libro || Instance != null) return;
            new GameObject("TutorialLibro").AddComponent<TutorialLibro>();
        }

        public static TutorialLibro Instance { get; private set; }

        private int _salvados;
        private bool _enPartida;
        private float _finEn = -1f;
        private string _aviso;
        private float _avisoHasta;

        private bool Completa => _salvados >= MetaSalvar;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            RitualBookDirector.IntentoResuelto += AlResolver;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            RitualBookDirector.IntentoResuelto -= AlResolver;
            Instance = null;
        }

        private void AlResolver(RitualBookDirector.ResultadoLibro r)
        {
            if (!_enPartida || Completa) return;
            if (r == RitualBookDirector.ResultadoLibro.Salvado)
            {
                _salvados++;
                Avisar("¡Bien! Volvió a brillar: lo salvaste.");
            }
            else
            {
                Avisar("Se apagó del todo. En una noche de verdad, de ahí sale Veleth. Volvé a intentarlo.");
            }
        }

        private void Avisar(string texto)
        {
            _aviso = texto;
            _avisoHasta = Time.time + SegundosAviso;
        }

        private void Update()
        {
            var net = NetworkManager.Instance;
            bool enPartida = net != null && net.GameStarted;
            // Flanco de subida: arranque en frío y cada REPETIR (no recarga la escena).
            if (enPartida && !_enPartida)
            {
                _salvados = 0;
                _finEn = -1f;
                _aviso = null;
            }
            _enPartida = enPartida;
            if (!enPartida || !net.IsServer) return;

            if (_finEn < 0f)
            {
                if (Completa) _finEn = Time.time + DemoraFin;
            }
            else if (Time.time >= _finEn)
            {
                _finEn = -1f;
                net.ServerNightSurvived();
            }
        }

        private void OnGUI()
        {
            if (!_enPartida || Gamepad.PauseMenuController.IsOpen) return;

            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            // Fila 1: la de la cordura, que en esta práctica no se usa (SanityHUD la oculta).
            int n = Mathf.Min(_salvados, MetaSalvar);
            T.HudBarra(vw, vh, 1, (float)n / MetaSalvar, n >= MetaSalvar ? T.Green : T.Tan,
                       "LIBRO SALVADO", $"{n}/{MetaSalvar}");
            PistaTutorial.Dibujar(vw, Time.time < _avisoHasta && _aviso != null ? _aviso : Texto());
        }

        private string Texto()
        {
            if (Completa) return "¡Práctica completa!";
            var libro = RitualBookDirector.Instance;
            if (libro != null && libro.Fase == RitualBookPhase.Consuming)
                return libro.Alumbrando > 0
                    ? "Seguí alumbrándolo hasta que vuelva a brillar…"
                    : $"¡El libro se apaga! Alumbralo con la luz intensa ({PistaTutorial.ComoLuzIntensa()}).";
            return "Quedate cerca del libro ritual, sobre tu imagen de referencia. Cuando se empiece a oscurecer, alumbralo.";
        }
    }
}
