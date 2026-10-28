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
    //  2. VELETH: la linterna se agota, una pila guiada permite volver al libro y la luz
    //     falla al intentar defenderlo. Sale Veleth, que no se echa con
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

        private enum Fase { Salvar, BuscarPila, BuscarLibro, Fallando, Emergiendo, Cazando, Completa }

        private Fase _fase;
        private int _salvados;
        private float _escapado;
        private bool _enPartida;
        private float _finEn = -1f;
        private Flashlight _linterna;
        private uint _pila;
        private bool _hayPila;
        private float _reintentoPila;
        private float _sinLugarDesde = -1f;
        private float _proximoFallo;
        private readonly PistaTutorial.Aviso _aviso = new();
        private Flashlight Linterna => _linterna != null ? _linterna : _linterna = FindFirstObjectByType<Flashlight>();
        public bool BuscandoPila => _enPartida && _fase == Fase.BuscarPila;
        public bool FalloActivo => _enPartida && _fase == Fase.Fallando;
        public bool EsperandoPila => _enPartida && _fase == Fase.BuscarPila;
        public float ConsumoFinalSegundos => 6f;

        public bool DebeSonarFallo(float ahora)
        {
            if (!FalloActivo || ahora < _proximoFallo) return false;
            _proximoFallo = ahora + Random.Range(0.65f, 1.15f);
            return true;
        }

        public void IntentarDefenderLibro()
        {
            if (_fase != Fase.BuscarLibro) return;
            _fase = Fase.Fallando;
            _proximoFallo = 0f;
            Avisar("La linterna falla. *Intentá mantener la luz sobre el libro*.");
        }
        // ¿Terminó porque Veleth atrapó al jugador? (la demostración se cumplió igual).
        public bool CompletadaPorVeleth { get; private set; }

        // ¿Perder el libro invoca a Veleth? Sólo en la segunda parte; antes, el director lo
        // restaura para volver a intentarlo.
        public bool VelethHabilitada => _fase >= Fase.BuscarPila;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            RitualBookDirector.IntentoResuelto += AlResolverLibro;
            VelethDirector.IntentoResuelto += AlResolverVeleth;
            Flashlight.PilaRecogida += AlRecogerPila;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            RitualBookDirector.IntentoResuelto -= AlResolverLibro;
            VelethDirector.IntentoResuelto -= AlResolverVeleth;
            Flashlight.PilaRecogida -= AlRecogerPila;
            Bateries.BatterySpawnManager.Retenidas = false;
            Instance = null;
        }

        private void AlRecogerPila()
        {
            if (!_enPartida || _fase != Fase.BuscarPila) return;
            _fase = Fase.BuscarLibro;
            _hayPila = false;
            Avisar("Tenés luz otra vez. *Volvé al libro y alúmbralo*.");
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
                        _fase = Fase.BuscarPila;
                        _hayPila = false;
                        _reintentoPila = 0f;
                        _sinLugarDesde = -1f;
                        Bateries.BatterySpawnManager.Retenidas = true;
                        Bateries.BatterySpawnManager.Instance?.ServerPrepararPilaTutorialLibro();
                        Linterna?.AgotarParaTutorialLibro();
                        Avisar("¡Lo salvaste! La linterna se agotó. *Buscá una pila*.");
                    }
                    else
                    {
                        Avisar("¡Bien! Lo salvaste.");
                    }
                    break;
                case Fase.Salvar:
                    Avisar("La oscuridad lo consumió. En una noche, sale Veleth. *Otra vez*.");
                    break;
                case Fase.Fallando:
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
                    Bateries.BatterySpawnManager.Retenidas = false;
                }
                else
                {
                    _fase = Fase.BuscarPila;
                    Bateries.BatterySpawnManager.Retenidas = true;
                    _hayPila = false;
                    _reintentoPila = 0f;
                    _sinLugarDesde = -1f;
                    Linterna?.AgotarParaTutorialLibro();
                }
                _escapado = 0f;
                _finEn = -1f;
                _aviso.Limpiar();
                CompletadaPorVeleth = false;
            }
            _enPartida = enPartida;
            if (!enPartida || !net.IsServer) return;
            if (_fase == Fase.BuscarPila && Linterna != null && !Linterna.IsEmpty)
                Linterna.AgotarParaTutorialLibro();
            if (_fase == Fase.BuscarPila && !_hayPila && Time.time >= _reintentoPila)
            {
                if (_sinLugarDesde < 0f) _sinLugarDesde = Time.time;
                var cam = Camera.main;
                var manager = Bateries.BatterySpawnManager.Instance;
                if (cam != null && manager != null)
                    _hayPila = manager.ServerSpawnUnaAhora(cam.transform.position, out _pila);
                _reintentoPila = Time.time + 0.5f;
            }
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
            else if (_fase == Fase.BuscarPila && _hayPila && EntityRegistry.Instance != null &&
                     EntityRegistry.Instance.TryGet(_pila, out var pila) && pila != null)
                GuiaTutorial.Dibujar(pila.transform.position, "PILA");
            else if ((_fase == Fase.Salvar && seApaga || _fase == Fase.BuscarLibro || _fase == Fase.Fallando) && libro != null)
                GuiaTutorial.Dibujar(libro.PuntoDeLuz, "LIBRO");
        }

        private string Texto()
        {
            switch (_fase)
            {
                case Fase.Completa:   return "¡Práctica completa!";
                case Fase.Cazando:    return "¡Te persigue! La luz no la frena: *alejate*.";
                case Fase.Emergiendo: return "Veleth está saliendo del libro…";
                case Fase.BuscarPila: return !_hayPila && _sinLugarDesde >= 0f && Time.time - _sinLugarDesde > 4f
                    ? "No hay dónde dejar la pila: faltan piso o paredes que cierren el cuarto."
                    : "Sin luz. *Seguí la flecha y recogé la pila*.";
                case Fase.BuscarLibro: return $"*Volvé al libro y alumbralo* ({PistaTutorial.ComoLuzIntensa()}).";
                case Fase.Fallando: return "La linterna titila. *Intentá mantener la luz sobre el libro*.";
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
