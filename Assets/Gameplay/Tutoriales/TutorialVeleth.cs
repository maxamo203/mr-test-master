using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica VELETH: esta vez hay que DEJAR que el libro se apague, para conocer lo que
    // sale de él. Veleth no se echa con la luz; lo único que la frena es que no te alcance
    // con línea directa (paredes y muebles en el medio). Hay que aguantar SegundosEscapar
    // mientras persigue. Que te atrape no mata en una práctica: Veleth se retira y el libro
    // vuelve para intentarlo otra vez (VelethDirector.Catch → RitualBookDirector.
    // ReanudarTrasVeleth). Al completar, Veleth se retira, la noche se gana
    // (ServerNightSurvived) y FinDeNoche2 la muestra como PRÁCTICA COMPLETA.
    public class TutorialVeleth : MonoBehaviour
    {
        public const float SegundosEscapar = 15f;
        private const float DemoraFin = 1f;
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
                sesion.Tutorial != TutorialTipo.Veleth || Instance != null) return;
            new GameObject("TutorialVeleth").AddComponent<TutorialVeleth>();
        }

        public static TutorialVeleth Instance { get; private set; }

        private enum Fase { Libro, Emergiendo, Cazando, Completa }

        private Fase _fase;
        private float _escapado;
        private bool _enPartida;
        private float _finEn = -1f;
        private string _aviso;
        private float _avisoHasta;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            RitualBookDirector.IntentoResuelto += AlResolverLibro;
            VelethDirector.IntentoResuelto += AlResolverVeleth;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            RitualBookDirector.IntentoResuelto -= AlResolverLibro;
            VelethDirector.IntentoResuelto -= AlResolverVeleth;
            Instance = null;
        }

        private void AlResolverLibro(RitualBookDirector.ResultadoLibro r)
        {
            if (!_enPartida || _fase == Fase.Completa) return;
            if (r == RitualBookDirector.ResultadoLibro.Salvado)
                Avisar("Lo salvaste: en una noche, perfecto. Acá dejalo apagarse para conocer a Veleth.");
            else
                _fase = Fase.Emergiendo;
        }

        private void AlResolverVeleth(VelethDirector.ResultadoVeleth r)
        {
            if (!_enPartida || _fase == Fase.Completa) return;
            if (r == VelethDirector.ResultadoVeleth.EmpiezaCaza)
            {
                _fase = Fase.Cazando;
                _escapado = 0f;
            }
            else
            {
                _fase = Fase.Libro;
                _escapado = 0f;
                Avisar("Te atrapó. En una noche de verdad ahí perdías. El libro vuelve: probá otra vez.");
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
                _fase = Fase.Libro;
                _escapado = 0f;
                _finEn = -1f;
                _aviso = null;
            }
            _enPartida = enPartida;
            if (!enPartida || !net.IsServer) return;

            if (_fase == Fase.Cazando)
            {
                _escapado += Time.deltaTime;
                if (_escapado >= SegundosEscapar)
                {
                    _fase = Fase.Completa;
                    VelethDirector.Instance?.Retirar();
                    _finEn = Time.time + DemoraFin;
                }
            }
            else if (_finEn >= 0f && Time.time >= _finEn)
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
            float t = Mathf.Min(_escapado, SegundosEscapar);
            bool listo = _fase == Fase.Completa;
            T.HudBarra(vw, vh, 1, listo ? 1f : t / SegundosEscapar, listo ? T.Green : T.Tan,
                       "ESCAPAR", listo ? "LISTO" : $"{Mathf.FloorToInt(t)}/{SegundosEscapar:0} s");
            PistaTutorial.Dibujar(vw, Time.time < _avisoHasta && _aviso != null ? _aviso : Texto());
        }

        private string Texto() => _fase switch
        {
            Fase.Completa   => "¡Práctica completa!",
            Fase.Cazando    => "La luz no la frena. Alejate y poné paredes o muebles entre ustedes.",
            Fase.Emergiendo => "Veleth sale del libro. Cuando termine de salir, te va a perseguir: alejate ya.",
            _               => "Esta vez dejá que el libro se apague: no lo alumbres y mirá qué pasa.",
        };
    }
}
