using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica SORKEN: el Sorken entra por las puertas o ventanas que el jugador marcó en
    // el armado rápido (ArmadoRapido, sin escaneo). Hay que echarlo MetaEchar veces con la
    // luz intensa; da lo mismo si fue en la entrada (alumbrando la puerta) o ya adentro
    // (alumbrándolo a él). Si te atrapa, te morís como en una noche: pantalla de muerte con
    // REINTENTAR PRÁCTICA. Al completar, la noche se gana (ServerNightSurvived) y
    // FinDeNoche2 la muestra como PRÁCTICA COMPLETA. Se crea sola en SampleScene sólo en
    // esta práctica.
    public class TutorialSorken : MonoBehaviour
    {
        private const float DemoraFin = 2f;
        // Cuánto queda a la vista el aviso de un resultado (lo echaste / te atrapó).
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
                sesion.Tutorial != TutorialTipo.Sorken || Instance != null) return;
            new GameObject("TutorialSorken").AddComponent<TutorialSorken>();
        }

        public static TutorialSorken Instance { get; private set; }

        public const int MetaEchar = 2;

        private int _echados;
        private bool _enPartida;
        private float _finEn = -1f;
        private string _aviso;
        private float _avisoHasta;

        private bool Completa => _echados >= MetaEchar;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            GameDirector.IntentoResuelto += AlResolverIntento;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            GameDirector.IntentoResuelto -= AlResolverIntento;
            Instance = null;
        }

        private void AlResolverIntento(GameDirector.ResultadoSorken r)
        {
            if (!_enPartida || Completa) return;
            _echados++;
            string donde = r == GameDirector.ResultadoSorken.EchadoEnLaEntrada ? "antes de que entre" : "cuando ya había entrado";
            Avisar(Completa ? $"¡Bien! Lo echaste {donde}." : $"¡Bien! Lo echaste {donde}. Va a volver: echalo otra vez.");
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
                _echados = 0;
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
            if (LocalDeath.Instance != null && LocalDeath.Instance.IsDead) return;

            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            // Fila 1 (la de la cordura, que acá no se usa).
            int n = Mathf.Min(_echados, MetaEchar);
            T.HudBarra(vw, vh, 1, (float)n / MetaEchar, n >= MetaEchar ? T.Green : T.Tan, "ECHARLO", $"{n}/{MetaEchar}");
            PistaTutorial.Dibujar(vw, Time.time < _avisoHasta && _aviso != null ? _aviso : Texto());
        }

        private string Texto()
        {
            if (Completa) return "¡Práctica completa!";
            string luz = PistaTutorial.ComoLuzIntensa();
            return $"El Sorken va a intentar entrar por la puerta o ventana que marcaste. Echalo con la luz intensa ({luz}): " +
                   "alumbrá la entrada mientras intenta pasar, o a él si ya entró.";
        }
    }
}
