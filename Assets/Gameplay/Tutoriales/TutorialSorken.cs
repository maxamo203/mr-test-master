using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica SORKEN: el Sorken entra por las puertas o ventanas que el jugador marcó en
    // el armado rápido (ArmadoRapido, sin escaneo). Hay que echarlo MetaEchar veces con la
    // luz intensa, de las dos formas: el primero se puede echar en la entrada (alumbrando la
    // puerta mientras aparece); después de echar uno ahí, la entrada ya no lo frena
    // (GameDirector.SinRepelerEnEntrada): hay que esperar a que entre, verlo y echarlo
    // adentro (alumbrándolo a él). Si te atrapa, te morís como en una noche: pantalla de muerte con
    // REINTENTAR PRÁCTICA. Al completar, la noche se gana (ServerNightSurvived) y
    // FinDeNoche2 la muestra como PRÁCTICA COMPLETA. Se crea sola en SampleScene sólo en
    // esta práctica.
    public class TutorialSorken : MonoBehaviour
    {
        private const float DemoraFin = 2f;
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
        // ¿Ya echó uno en la entrada? Desde ahí, los que siguen hay que echarlos adentro.
        private bool _echadoEnEntrada;
        private static bool DejarEntrar => Instance != null && Instance._echadoEnEntrada;
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
            GameDirector.SinRepelerEnEntrada = false;
            Instance = null;
        }

        private void AlResolverIntento(GameDirector.ResultadoSorken r)
        {
            if (!_enPartida || Completa) return;
            _echados++;
            bool enEntrada = r == GameDirector.ResultadoSorken.EchadoEnLaEntrada;
            if (enEntrada) _echadoEnEntrada = true;
            if (Completa) Avisar(enEntrada ? "¡Bien! Lo echaste en la entrada." : "¡Bien! Lo echaste adentro.");
            else if (enEntrada) Avisar("¡Bien! Lo echaste en la entrada. Al próximo *dejalo entrar*.");
            else Avisar("¡Bien! Lo echaste adentro. Va a volver.");
        }

        private void Avisar(string texto)
        {
            _aviso = texto;
            _avisoHasta = Time.time + SegundosAviso;
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
                _echadoEnEntrada = false;
                _finEn = -1f;
                _aviso = null;
            }
            _enPartida = enPartida;
            GameDirector.SinRepelerEnEntrada = enPartida && _echadoEnEntrada && !Completa;
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
            // Mientras entra, la flecha va al punto de la entrada (es lo que hay que alumbrar);
            // una vez adentro, a él.
            if (Entrando(out var entrada)) GuiaTutorial.Dibujar(entrada, "ENTRADA");
            else
            {
                var s = SorkenActivo();
                if (s != null) GuiaTutorial.Dibujar(s.Position + Vector3.up, "SORKEN");
            }
        }

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
            return "Va a aparecer en una puerta o ventana.";
        }
    }
}
