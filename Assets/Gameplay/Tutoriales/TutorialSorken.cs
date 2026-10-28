using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica SORKEN: el Sorken entra por las puertas o ventanas que el jugador marcó en
    // el armado rápido (ArmadoRapido, sin escaneo). Hay que echarlo MetaEchar veces con la
    // luz intensa, de las dos formas: el primero se puede echar en la entrada (alumbrando la
    // puerta mientras aparece); después de echar el primero, la entrada ya no lo frena
    // (GameDirector.SinRepelerEnEntrada): hay que esperar a que entre, verlo y echarlo
    // adentro (alumbrándolo a él). Si te atrapa, te morís como en una noche: pantalla de muerte con
    // REINTENTAR PRÁCTICA. Al completar, la noche se gana (ServerNightSurvived) y
    // FinDeNoche2 la muestra como PRÁCTICA COMPLETA. Se crea sola en SampleScene sólo en
    // esta práctica.
    public class TutorialSorken : MonoBehaviour
    {
        private const float DemoraFin = 2f;
        public const float IntervaloSegundoIngreso = 2.5f;
        // Cuánto queda a la vista el aviso de un resultado (lo echaste / te atrapó).
        private const float SegundosAviso = 2.5f;

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
        // El segundo ingreso empieza con el clip de entrada y se resuelve adentro.
        private bool _segundoIngresoPendiente;
        private static bool DejarEntrar => Instance != null && Instance._echados >= 1;
        public bool SegundoIngresoPendiente => _enPartida && _segundoIngresoPendiente;
        public bool TomarSegundoIngresoInmediato()
        {
            if (!SegundoIngresoPendiente) return false;
            _segundoIngresoPendiente = false;
            return true;
        }
        private bool _enPartida;
        private float _finEn = -1f;
        private readonly PistaTutorial.Aviso _aviso = new();

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
            GameDirector.SinRepelerEnEntrada = false;
            Instance = null;
        }

        private void AlResolverIntento(GameDirector.ResultadoSorken r)
        {
            if (!_enPartida || Completa) return;
            _echados++;
            if (_echados == 1) _segundoIngresoPendiente = true;
            bool enEntrada = r == GameDirector.ResultadoSorken.EchadoEnLaEntrada;
            if (Completa) Avisar(enEntrada ? "¡Bien! Lo echaste en la entrada." : "¡Bien! Lo echaste adentro.");
            else if (enEntrada) Avisar("¡Bien! Lo echaste en la entrada. Al próximo *dejalo entrar*.");
            else Avisar("¡Bien! Lo echaste adentro. Al próximo *dejalo entrar*.");
        }

        private void Avisar(string texto)
        {
            _aviso.Mostrar(texto, SegundosAviso);
        }

        private void Update()
        {
            // El TUTORIAL pasó al paso siguiente (o a la Noche 1) sin recargar la escena.
            if (!Tutoriales.Vigente(TutorialTipo.Sorken)) { Destroy(gameObject); return; }

            var net = NetworkManager.Instance;
            bool enPartida = net != null && net.GameStarted;
            // Flanco de subida: arranque en frío y cada REPETIR (no recarga la escena).
            if (enPartida && !_enPartida)
            {
                _echados = 0;
                _segundoIngresoPendiente = false;
                _finEn = -1f;
                _aviso.Limpiar();
            }
            _enPartida = enPartida;
            GameDirector.SinRepelerEnEntrada = enPartida && _echados >= 1 && !Completa;
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
            T.HudObjetivos(vw, vh, 1, n, MetaEchar, "SORKEN");
            PistaTutorial.Dibujar(vw, Texto(), _aviso);
            // Mientras entra, la flecha va al punto de la entrada (es lo que hay que alumbrar);
            // una vez adentro, a él; antes de que aparezca, a la entrada marcada más cercana.
            if (Entrando(out var entrada)) GuiaTutorial.Dibujar(entrada, "ENTRADA");
            else
            {
                var s = SorkenActivo();
                if (s != null) GuiaTutorial.Dibujar(s.Position + Vector3.up, "SORKEN");
                else if (EntradaMasCercana(out var cercana)) GuiaTutorial.Dibujar(cercana, "ENTRADA");
            }
        }

        // La puerta o ventana marcada más cercana al jugador: por donde va a entrar (con una
        // sola marcada, es ésa). Se recalcula cada medio segundo, no por frame.
        private bool EntradaMasCercana(out Vector3 pos)
        {
            if (Time.unscaledTime >= _buscarEntradaEn)
            {
                _buscarEntradaEn = Time.unscaledTime + 0.5f;
                _hayEntrada = false;
                var reg = Scanner.SceneRegistry.Instance;
                var cam = Camera.main;
                if (reg != null && cam != null)
                {
                    float mejor = float.MaxValue;
                    foreach (var m in reg.Markers)
                    {
                        if (m == null) continue;
                        float d = (m.transform.position - cam.transform.position).sqrMagnitude;
                        if (d >= mejor) continue;
                        mejor = d;
                        _entrada = m.transform;
                        _hayEntrada = true;
                    }
                }
            }
            pos = _hayEntrada && _entrada != null ? _entrada.position : default;
            return _hayEntrada && _entrada != null;
        }

        private Transform _entrada;
        private bool _hayEntrada;
        private float _buscarEntradaEn;

        // El Sorken del intento en curso (null si no hay): existe desde que empieza a salir el
        // humo en la entrada (GameDirector lo crea quieto ahí) hasta que se va. El registro de
        // entidades se recorre cada medio segundo, no por frame.
        private SorkenEntity SorkenActivo()
        {
            if (Time.unscaledTime >= _buscarSorkenEn)
            {
                _buscarSorkenEn = Time.unscaledTime + 0.5f;
                _sorken = null;
                var reg = EntityRegistry.Instance;
                if (reg != null)
                    foreach (var e in reg.All)
                    {
                        if (e == null || e.EntityTypeId != EntityTypeIds.Sorken) continue;
                        var s = e.GetComponent<SorkenEntity>();
                        if (s != null) { _sorken = s; break; }
                    }
            }
            return _sorken != null ? _sorken : null;   // != de Unity: ya despawneado = null
        }

        private SorkenEntity _sorken;
        private float _buscarSorkenEn;

        private static bool Entrando(out Vector3 entrada)
        {
            entrada = default;
            return GameDirector.Instance != null && GameDirector.Instance.TryPuntoDeEntrada(out entrada);
        }

        private string Texto()
        {
            if (Completa) return "¡Práctica completa!";
            string luz = PistaTutorial.ComoLuzIntensa();
            var s = SorkenActivo();
            if (Entrando(out _))
            {
                bool humo = s != null && s.State == SorkenState.Idle;
                if (DejarEntrar)
                    return humo ? "Está apareciendo… *esperá a que entre*." : "Está entrando… *esperá*.";
                return humo ? $"Está apareciendo. *Luz intensa a la entrada* ({luz})."
                            : $"¡Está entrando! *Luz intensa a la entrada* ({luz}).";
            }
            if (s != null) return $"¡Entró! *Alumbralo a él* ({luz}).";
            return "Va a entrar por la entrada que marca la flecha.";
        }
    }
}
