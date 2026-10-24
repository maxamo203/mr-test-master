using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica LIBRO RITUAL Y VELETH, en dos partes:
    //  1. SALVAR EL LIBRO: el libro (el 0,0 del escenario) se oscurece cada tanto; alumbrarlo
    //     con la luz intensa unos segundos lo salva. Hay que salvarlo MetaSalvar veces. Si se
    //     apaga del todo, todavía no sale Veleth: RitualBookDirector lo restaura
    //     (VelethHabilitada es false) y se vuelve a intentar hasta lograrlo.
    //  2. VELETH: ahora hay que DEJAR que el libro se apague. Sale Veleth, que no se echa con
    //     la luz; hay que aguantar SegundosEscapar mientras persigue. Si te atrapa, te morís
    //     como en una noche; REINTENTAR PRÁCTICA retoma directo en esta parte.
    // Al completar, Veleth se retira, la noche se gana (ServerNightSurvived) y FinDeNoche2 la
    // muestra como PRÁCTICA COMPLETA.
    // La primera vez que el libro empieza a oscurecerse aparece un aviso sobre la barra de
    // la linterna (Rediseno.Menus.AvisoLuzIntensa2) con la práctica en pausa
    // (Time.timeScale = 0: congela el libro, el humo y las animaciones) hasta CONTINUAR.
    public class TutorialLibro : MonoBehaviour
    {
        public const int MetaSalvar = 2;
        public const float SegundosEscapar = 5f;
        private const float DemoraFin = 1.5f;
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

        private enum Fase { Salvar, DejarApagar, Emergiendo, Cazando, Completa }

        private Fase _fase;
        private int _salvados;
        private float _escapado;
        private bool _enPartida;
        private float _finEn = -1f;
        private string _aviso;
        private float _avisoHasta;
        // Una vez por entrada a la práctica: REPETIR (no recarga la escena) no lo repite.
        private bool _avisoLuzMostrado;

        public bool AvisoAbierto { get; private set; }

        // ¿Perder el libro invoca a Veleth? Sólo en la segunda parte; antes, el director lo
        // restaura para volver a intentarlo.
        public bool VelethHabilitada => _fase >= Fase.DejarApagar;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            RitualBookDirector.IntentoResuelto += AlResolverLibro;
            VelethDirector.IntentoResuelto += AlResolverVeleth;
            new GameObject("AvisoLuzIntensa2").AddComponent<Rediseno.Menus.AvisoLuzIntensa2>();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            RitualBookDirector.IntentoResuelto -= AlResolverLibro;
            VelethDirector.IntentoResuelto -= AlResolverVeleth;
            // Salir con el aviso abierto (pausa → menú) no puede dejar el juego congelado.
            CerrarAviso();
            Instance = null;
        }

        private void AbrirAviso()
        {
            _avisoLuzMostrado = true;
            AvisoAbierto = true;
            Bateries.ContextActionController.AvisoAbierto = true;
            Time.timeScale = 0f;
        }

        // CONTINUAR del aviso.
        public void Continuar() => CerrarAviso();

        // Sólo despausa si la pausa era suya: no pisa el timeScale = 0 de LocalDeath.
        private void CerrarAviso()
        {
            if (!AvisoAbierto) return;
            AvisoAbierto = false;
            Bateries.ContextActionController.AvisoAbierto = false;
            Time.timeScale = 1f;
        }

        private void AlResolverLibro(RitualBookDirector.ResultadoLibro r)
        {
            if (!_enPartida) return;
            bool salvado = r == RitualBookDirector.ResultadoLibro.Salvado;
            switch (_fase)
            {
                case Fase.Salvar when salvado:
                    _salvados++;
                    if (_salvados >= MetaSalvar)
                    {
                        _fase = Fase.DejarApagar;
                        Avisar("¡Lo salvaste! Ahora dejá que se apague: vas a conocer lo que sale de él.");
                    }
                    else
                    {
                        Avisar("¡Bien! Volvió a brillar: lo salvaste.");
                    }
                    break;
                case Fase.Salvar:
                    Avisar("Se apagó del todo. En una noche de verdad, de ahí sale Veleth. Volvé a intentarlo.");
                    break;
                case Fase.DejarApagar when salvado:
                    Avisar("Esta vez no lo alumbres: dejalo apagarse para conocer a Veleth.");
                    break;
                case Fase.DejarApagar:
                    _fase = Fase.Emergiendo;
                    break;
            }
        }

        private void AlResolverVeleth(VelethDirector.ResultadoVeleth r)
        {
            if (!_enPartida || _fase != Fase.Emergiendo) return;
            if (r != VelethDirector.ResultadoVeleth.EmpiezaCaza) return;
            _fase = Fase.Cazando;
            _escapado = 0f;
        }

        private void Avisar(string texto)
        {
            _aviso = texto;
            _avisoHasta = Time.time + SegundosAviso;
        }

        private void Update()
        {
            // El TUTORIAL pasó al paso siguiente (o a la Noche 1) sin recargar la escena.
            if (!Tutoriales.Vigente(TutorialTipo.Libro)) { Destroy(gameObject); return; }

            var net = NetworkManager.Instance;
            bool enPartida = net != null && net.GameStarted;
            // Flanco de subida: arranque en frío y cada REPETIR (no recarga la escena). Tras
            // morir con Veleth se retoma en la segunda parte: el libro ya se aprendió. Tras
            // completarla, REPETIR la arranca de cero.
            if (enPartida && !_enPartida)
            {
                if (_fase == Fase.Completa || _fase == Fase.Salvar)
                {
                    _fase = Fase.Salvar;
                    _salvados = 0;
                }
                else
                {
                    _fase = Fase.DejarApagar;
                }
                _escapado = 0f;
                _finEn = -1f;
                _aviso = null;
            }
            if (!enPartida) CerrarAviso();
            _enPartida = enPartida;
            // Muerto ya no cuenta: el reloj de escape no puede ganar la práctica detrás de la
            // pantalla de muerte (es de un jugador, así que cualquier muerte es la suya).
            if (!enPartida || !net.IsServer || ServerDeaths.Count > 0) return;

            var libro = RitualBookDirector.Instance;
            if (!_avisoLuzMostrado && libro != null && libro.Fase == RitualBookPhase.Consuming)
                AbrirAviso();

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
            if (LocalDeath.Instance != null && LocalDeath.Instance.IsDead) return;

            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            // Fila 1: la de la cordura, que en esta práctica no se usa (SanityHUD la oculta).
            if (_fase == Fase.Salvar)
            {
                int n = Mathf.Min(_salvados, MetaSalvar);
                T.HudBarra(vw, vh, 1, (float)n / MetaSalvar, T.Tan, "LIBRO SALVADO", $"{n}/{MetaSalvar}");
            }
            else
            {
                float t = Mathf.Min(_escapado, SegundosEscapar);
                bool listo = _fase == Fase.Completa;
                T.HudBarra(vw, vh, 1, listo ? 1f : t / SegundosEscapar, listo ? T.Green : T.Tan,
                           "ESCAPAR", listo ? "LISTO" : $"{Mathf.FloorToInt(t)}/{SegundosEscapar:0} s");
            }
            if (!AvisoAbierto)
                PistaTutorial.Dibujar(vw, Time.time < _avisoHasta && _aviso != null ? _aviso : Texto());
        }

        private string Texto()
        {
            switch (_fase)
            {
                case Fase.Completa:   return "¡Práctica completa!";
                case Fase.Cazando:    return "La luz no la frena. Alejate y poné paredes o muebles entre ustedes.";
                case Fase.Emergiendo: return "Veleth sale del libro. Cuando termine de salir, te va a perseguir: alejate ya.";
                case Fase.DejarApagar:return "Esta vez dejá que el libro se apague: no lo alumbres y mirá qué pasa.";
            }
            var libro = RitualBookDirector.Instance;
            if (libro != null && libro.Fase == RitualBookPhase.Consuming)
                return libro.Alumbrando > 0
                    ? "Seguí alumbrándolo hasta que vuelva a brillar…"
                    : $"¡El libro se apaga! Alumbralo con la luz intensa ({PistaTutorial.ComoLuzIntensa()}).";
            return "Quedate cerca del libro ritual. Cuando se empiece a oscurecer, alumbralo.";
        }
    }
}
