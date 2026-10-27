using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica ARBMOS: la lección de la Noche 4 — "apagá la luz para ocultarte; iluminarlo
    // lo provoca". El Arbmos aparece quieto cerca tuyo; si apagás la linterna unos segundos
    // se va, y si le alumbrás la cabeza te ataca y te saca cordura. Hay que hacerlo irse a
    // oscuras MetaOcultarse veces (la flecha lo marca aunque estés a oscuras, así que no se
    // va sin que te enteres).
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
        // La primera vez que baja la cordura se presenta (es el único paso donde algo la baja):
        // la barra se resalta y la pista lo explica, sin frenar el juego.
        private const float SegundosPresentarCordura = 6f;

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
        private float _finEn = -1f;
        private readonly PistaTutorial.Aviso _aviso = new();
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
                    _ocultas++;
                    Avisar("¡Bien! A oscuras, se fue.");
                    break;
                case ArbmosDirector.ResultadoArbmos.Provocado:
                    Avisar("Le alumbraste la cabeza: ¡viene! Ya no lo frena la oscuridad.");
                    break;
                case ArbmosDirector.ResultadoArbmos.Golpe:
                    // La primera baja la presenta Update (barra resaltada + explicación).
                    if (_corduraPresentada) Avisar("Te sacó cordura. Si llega a cero, el Arbmos mata.");
                    break;
            }
        }

        private void Avisar(string texto, float segundos = SegundosAviso)
        {
            _aviso.Mostrar(texto, segundos);
        }

        private bool _corduraPresentada;
        private float _corduraAnterior = 1f;

        // La primera vez que la cordura baja en la práctica: barra resaltada y explicación.
        private void PresentarCorduraSiBaja()
        {
            var ls = LocalSanity.Instance;
            if (ls == null) return;
            float c = ls.Value01;
            if (!_corduraPresentada && c < _corduraAnterior - 0.001f)
            {
                _corduraPresentada = true;
                SanityHUD.ResaltarHasta = Time.unscaledTime + SegundosPresentarCordura;
                Avisar("Bajó tu *cordura*: con el Arbmos cerca, la luz te la saca. En cero, te mata.",
                       SegundosPresentarCordura);
            }
            _corduraAnterior = c;
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
                _finEn = -1f;
                _aviso.Limpiar();
                _corduraPresentada = false;
                _corduraAnterior = LocalSanity.Instance != null ? LocalSanity.Instance.Value01 : 1f;
            }
            _enPartida = enPartida;
            if (!enPartida) return;
            PresentarCorduraSiBaja();

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
            T.HudObjetivos(vw, vh, 2, n, MetaOcultarse, "ARBMOS");
            PistaTutorial.Dibujar(vw, Texto(), _aviso);
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
                    ? $"No soporta la luz: *apagá la linterna* para que se vaya ({PistaTutorial.ComoApagar()})."
                    : "¡Bien! *Mantené la luz apagada* hasta que se vaya.";
            // Aparece solo al terminar la espera; la flecha lo marca, no hace falta buscarlo.
            return "Está por aparecer cerca tuyo…";
        }
    }
}
