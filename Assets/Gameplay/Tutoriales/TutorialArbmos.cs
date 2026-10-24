using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica ARBMOS: la lección de la Noche 4 — "apagá la luz para ocultarte; iluminarlo
    // lo provoca". El Arbmos aparece quieto cerca tuyo; si apagás la linterna unos segundos
    // se va, y si le alumbrás la cabeza te ataca y te saca cordura. Hay que hacerlo irse a
    // oscuras MetaOcultarse veces, habiéndolo visto con la luz prendida (si nunca prendiste
    // la luz, se fue sin que te enteres: eso no enseña nada y no cuenta).
    //
    // La cordura sí importa acá, así que su barra queda en la fila 1 y el objetivo va en la
    // 2. Si con la cordura en cero te atrapa la versión letal, te morís como en una noche:
    // pantalla de muerte con REINTENTAR PRÁCTICA. Al completar, la noche se gana
    // (ServerNightSurvived) y FinDeNoche2 la muestra como PRÁCTICA COMPLETA.
    public class TutorialArbmos : MonoBehaviour
    {
        public const int MetaOcultarse = 2;
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
                sesion.Tutorial != TutorialTipo.Arbmos || Instance != null) return;
            new GameObject("TutorialArbmos").AddComponent<TutorialArbmos>();
        }

        public static TutorialArbmos Instance { get; private set; }

        private int _ocultas;
        private bool _enPartida;
        // ¿Lo tuvo a la vista con la luz prendida durante esta aparición?
        private bool _loVioConLuz;
        private float _finEn = -1f;
        private string _aviso;
        private float _avisoHasta;
        private Flashlight _linterna;

        private bool Completa => _ocultas >= MetaOcultarse;
        private Flashlight Linterna => _linterna != null ? _linterna : _linterna = FindFirstObjectByType<Flashlight>();
        private bool LuzPrendida => Linterna != null && Linterna.Mode != FlashlightMode.Off;
        private static bool ArbmosPresente => ArbmosEntity.Active != null && !ArbmosEntity.Active.Lethal;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            ArbmosDirector.IntentoResuelto += AlResolver;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            ArbmosDirector.IntentoResuelto -= AlResolver;
            Instance = null;
        }

        private void AlResolver(ArbmosDirector.ResultadoArbmos r)
        {
            if (!_enPartida || Completa) return;
            switch (r)
            {
                case ArbmosDirector.ResultadoArbmos.SeFueAOscuras:
                    if (_loVioConLuz)
                    {
                        _ocultas++;
                        Avisar("¡Bien! A oscuras, se fue.");
                    }
                    else
                    {
                        Avisar("Se fue sin que lo vieras. *Encontralo con luz* y recién ahí *apagala*.");
                    }
                    break;
                case ArbmosDirector.ResultadoArbmos.Provocado:
                    Avisar("Le alumbraste la cabeza: ¡viene! Ya no lo frena la oscuridad.");
                    break;
                case ArbmosDirector.ResultadoArbmos.Golpe:
                    Avisar("Te sacó cordura. Si llega a cero, el Arbmos mata.");
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
            // El TUTORIAL pasó al paso siguiente (o a la Noche 1) sin recargar la escena.
            if (!Tutoriales.Vigente(TutorialTipo.Arbmos)) { Destroy(gameObject); return; }

            var net = NetworkManager.Instance;
            bool enPartida = net != null && net.GameStarted;
            // Flanco de subida: arranque en frío y cada REPETIR (no recarga la escena).
            if (enPartida && !_enPartida)
            {
                _ocultas = 0;
                _loVioConLuz = false;
                _finEn = -1f;
                _aviso = null;
            }
            _enPartida = enPartida;
            if (!enPartida) return;

            // El evento de la retirada llega antes del despawn, así que la marca todavía
            // vale cuando se cuenta; recién sin Arbmos se borra para la próxima aparición.
            if (ArbmosPresente) { if (LuzPrendida) _loVioConLuz = true; }
            else _loVioConLuz = false;

            if (!net.IsServer) return;
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
            // Fila 1 es la cordura (SanityHUD); el objetivo va en la 2 (la del amanecer,
            // que en una práctica no existe).
            int n = Mathf.Min(_ocultas, MetaOcultarse);
            T.HudBarra(vw, vh, 2, (float)n / MetaOcultarse, n >= MetaOcultarse ? T.Green : T.Tan,
                       "OCULTARTE", $"{n}/{MetaOcultarse}");
            PistaTutorial.Dibujar(vw, Time.time < _avisoHasta && _aviso != null ? _aviso : Texto());
            var arbmos = ArbmosEntity.Active;
            if (arbmos != null) GuiaTutorial.Dibujar(arbmos.transform.position + Vector3.up * 1.2f, "ARBMOS");
        }

        private string Texto()
        {
            if (Completa) return "¡Práctica completa!";
            if (ArbmosEntity.Active != null && ArbmosEntity.Active.Lethal)
                return "Cordura en cero: viene a matarte. *¡Alejate!*";
            if (ArbmosPresente)
                return LuzPrendida
                    ? $"No le alumbres la cabeza: *apagá la linterna* ({PistaTutorial.ComoApagar()})."
                    : "*Quedate a oscuras* hasta que se vaya…";
            return LuzPrendida
                ? "Algo te observa. Ya va a aparecer cerca tuyo."
                : $"*Prendé la linterna* ({PistaTutorial.ComoPrender()}).";
        }
    }
}
