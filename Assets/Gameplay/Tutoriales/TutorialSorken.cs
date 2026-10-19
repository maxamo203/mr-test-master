using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica SORKEN: el Sorken entra por las puertas o ventanas que el jugador marcó en
    // el armado rápido (ArmadoRapido, sin escaneo). Hay que echarlo de las dos formas que
    // tiene el juego:
    //  · EN LA PUERTA: alumbrar la puerta con la luz intensa mientras intenta entrar.
    //  · ADENTRO: si entró, alumbrarlo a él con la luz intensa hasta que se vaya.
    // Las dos cuentan en cualquier orden (la pista pide primero la de la puerta). Que te
    // atrape no mata en una práctica (GameDirector.KillPlayer): sólo se avisa y se sigue.
    // Al completar las dos, la noche se gana (ServerNightSurvived) y FinDeNoche2 la muestra
    // como PRÁCTICA COMPLETA. Se crea sola en SampleScene sólo en esta práctica.
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

        private bool _enPuerta, _adentro, _enPartida;
        private float _finEn = -1f;
        private string _aviso;
        private float _avisoHasta;

        private bool Completa => _enPuerta && _adentro;

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
            switch (r)
            {
                case GameDirector.ResultadoSorken.EchadoEnLaEntrada:
                    Avisar(_enPuerta && !_adentro ? "Lo echaste en la puerta otra vez. Ahora dejalo entrar."
                                                  : "¡Bien! Lo echaste antes de que entre.");
                    _enPuerta = true;
                    break;
                case GameDirector.ResultadoSorken.EchadoAdentro:
                    Avisar("¡Bien! Lo echaste cuando ya había entrado.");
                    _adentro = true;
                    break;
                case GameDirector.ResultadoSorken.Atrapo:
                    Avisar("Te atrapó. En una noche de verdad ahí perdías: en la práctica, seguí intentando.");
                    break;
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
                _enPuerta = _adentro = false;
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
            // Filas 1 y 2 (las de cordura y amanecer, que acá no se usan).
            Fila(vw, vh, 1, "EN LA PUERTA", _enPuerta);
            Fila(vw, vh, 2, "ADENTRO", _adentro);
            PistaTutorial.Dibujar(vw, Time.time < _avisoHasta && _aviso != null ? _aviso : Texto());
        }

        private static void Fila(float vw, float vh, int fila, string nombre, bool hecho) =>
            T.HudBarra(vw, vh, fila, hecho ? 1f : 0f, hecho ? T.Green : T.Tan, nombre, hecho ? "LISTO" : "0/1");

        private string Texto()
        {
            if (Completa) return "¡Práctica completa!";
            string luz = PistaTutorial.ComoLuzIntensa();
            if (!_enPuerta)
                return $"El Sorken va a intentar entrar por la puerta o ventana que marcaste. Alumbrala con la luz intensa ({luz}) hasta que se vaya.";
            return $"Ahora dejalo entrar. Cuando se acerque, alumbralo a él con la luz intensa ({luz}) hasta que se vaya.";
        }
    }
}
