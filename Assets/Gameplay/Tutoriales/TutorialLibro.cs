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
    //     la luz ni se puede escapar de ella (en una noche persigue hasta atrapar a todos): esta
    //     parte es una demostración. Si te atrapa, te morís como en una noche (retrato y todo)
    //     pero el paso cuenta como superado (CompletadaPorVeleth) y FinDeNoche2 lo muestra así.
    //     Si aguantás SegundosEscapar sin que te atrape, también (y Veleth se retira): así
    //     nadie queda trabado si Veleth no llega.
    // Al completar, Veleth se retira, la noche se gana (ServerNightSurvived) y FinDeNoche2 la
    // muestra como PRÁCTICA COMPLETA.
    public class TutorialLibro : MonoBehaviour
    {
        public const int MetaSalvar = 1;
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
        private readonly PistaTutorial.Aviso _aviso = new();
        // ¿Terminó porque Veleth atrapó al jugador? (la demostración se cumplió igual).
        public bool CompletadaPorVeleth { get; private set; }

        // ¿Perder el libro invoca a Veleth? Sólo en la segunda parte; antes, el director lo
        // restaura para volver a intentarlo.
        public bool VelethHabilitada => _fase >= Fase.DejarApagar;

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
            if (!_enPartida) return;
            bool salvado = r == RitualBookDirector.ResultadoLibro.Salvado;
            switch (_fase)
            {
                case Fase.Salvar when salvado:
                    _salvados++;
                    if (_salvados >= MetaSalvar)
                    {
                        _fase = Fase.DejarApagar;
                        Avisar("¡Lo salvaste! Ahora *dejá que la oscuridad lo consuma*.");
                    }
                    else
                    {
                        Avisar("¡Bien! Lo salvaste.");
                    }
                    break;
                case Fase.Salvar:
                    Avisar("La oscuridad lo consumió. En una noche, sale Veleth. *Otra vez*.");
                    break;
                case Fase.DejarApagar when salvado:
                    Avisar("Esta vez *no lo alumbres*: dejá que la oscuridad lo consuma.");
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
            _aviso.Mostrar(texto, SegundosAviso);
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
                _aviso.Limpiar();
                CompletadaPorVeleth = false;
            }
            _enPartida = enPartida;
            if (!enPartida || !net.IsServer) return;
            // Te atrapó Veleth: era la demostración, el paso queda superado igual. Muerto, el
            // reloj de escape ya no corre (es de un jugador: cualquier muerte es la suya).
            if (ServerDeaths.Count > 0)
            {
                if ((_fase == Fase.Emergiendo || _fase == Fase.Cazando) && !CompletadaPorVeleth)
                {
                    CompletadaPorVeleth = true;
                    _fase = Fase.Completa;   // REPETIR arranca de cero
                    TutorialCamino.MarcarSuperado(TutorialTipo.Libro);
                }
                return;
            }

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
                T.HudObjetivos(vw, vh, 1, Mathf.Min(_salvados, MetaSalvar), MetaSalvar, "LIBRO");
            }
            else
            {
                T.HudObjetivos(vw, vh, 1, _fase == Fase.Completa ? 1 : 0, 1, "VELETH");
            }
            PistaTutorial.Dibujar(vw, Texto(), _aviso);
            var veleth = VelethEntity.Active;
            var libro = RitualBookView.Active;
            bool seApaga = RitualBookDirector.Instance != null &&
                           RitualBookDirector.Instance.Fase == RitualBookPhase.Consuming;
            if ((_fase == Fase.Emergiendo || _fase == Fase.Cazando) && veleth != null)
                GuiaTutorial.Dibujar(veleth.Position + Vector3.up, "VELETH");
            // Veleth todavía no salió: la flecha va al libro, que es de donde va a salir.
            else if (_fase == Fase.Emergiendo && libro != null)
                GuiaTutorial.Dibujar(libro.PuntoDeLuz, "VELETH");
            else if ((_fase == Fase.Salvar || _fase == Fase.DejarApagar) && seApaga && libro != null)
                GuiaTutorial.Dibujar(libro.PuntoDeLuz, "LIBRO");
        }

        private string Texto()
        {
            switch (_fase)
            {
                case Fase.Completa:   return "¡Práctica completa!";
                case Fase.Cazando:    return "¡Te persigue! La luz no la frena: *alejate*.";
                case Fase.Emergiendo: return "Veleth está saliendo del libro…";
                case Fase.DejarApagar:return "*No lo alumbres*: dejá que la oscuridad lo consuma.";
            }
            var libro = RitualBookDirector.Instance;
            if (libro != null && libro.Fase == RitualBookPhase.Consuming)
                return libro.Alumbrando > 0
                    ? "*Seguí alumbrándolo*…"
                    : $"¡La oscuridad lo consume! *Luz intensa al libro* ({PistaTutorial.ComoLuzIntensa()}).";
            return "Cuando el libro se oscurezca, *alumbralo*.";
        }
    }
}
