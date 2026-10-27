using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica LA LINTERNA, primer paso del TUTORIAL. Sin amenazas, sin pilas y sin gasto
    // de batería (Resources/Tutoriales/Linterna: drenaje 0), en tres partes:
    //  1. APAGAR Y PRENDER: MetaCiclos veces, con el gesto del control que se use.
    //  2. CONCENTRAR: aparece una marca flotando delante del jugador; hay que sostenerle la
    //     luz intensa SegundosSostener. Si se suelta, el avance baja despacio (no de golpe),
    //     para que se entienda que hay que sostener.
    //  3. EL LIBRO: lo mismo sobre el libro ritual (el 0,0 del escenario), que es lo que
    //     después hay que salvar con luz en el paso de Veleth.
    // En el visor sin mando la intensa es automática al mirar de frente: la marca y el libro
    // se le pasan a ControlMirada como objetivo extra.
    // Al completar, la noche se gana (ServerNightSurvived) y FinDeNoche2 ofrece el paso
    // siguiente. Se crea sola en SampleScene sólo en esta práctica.
    public class TutorialLinterna : MonoBehaviour
    {
        public const int MetaCiclos = 2;
        public const float SegundosSostener = 2f;
        // Cuánto avance (en segundos) se pierde por segundo sin luz encima.
        private const float Decaimiento = 0.6f;
        private const float DemoraFin = 1.5f;
        private const float SegundosAviso = 3f;
        // Dónde aparece la marca: delante, a esta distancia (menos si hay una pared antes).
        private const float DistanciaMarca = 1.6f;
        private const float RadioMarca = 0.12f;

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
                sesion.Tutorial != TutorialTipo.Linterna || Instance != null) return;
            new GameObject("TutorialLinterna").AddComponent<TutorialLinterna>();
        }

        public static TutorialLinterna Instance { get; private set; }

        private enum Fase { Apagar, Concentrar, Libro, Completa }

        private Fase _fase;
        private int _ciclos;
        private bool _estuvoApagada;
        private float _avance;
        private bool _alumbrando;
        private bool _enPartida;
        private float _finEn = -1f;
        private readonly PistaTutorial.Aviso _aviso = new();
        private Flashlight _linterna;

        private GameObject _marca, _halo;
        private Material _matMarca, _matHalo;

        private Flashlight Linterna => _linterna != null ? _linterna : _linterna = FindFirstObjectByType<Flashlight>();
        private bool LuzPrendida => Linterna != null && Linterna.isOn && !Linterna.IsEmpty;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // Nada de pilas en este paso: la batería no se gasta y el cartel de RECOGER
            // distraería de lo que se practica.
            Bateries.BatterySpawnManager.Retenidas = true;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Bateries.BatterySpawnManager.Retenidas = false;
            Rediseno.ControlMirada.ObjetivoIntensa = null;
            BorrarMarca();
            Instance = null;
        }

        private void Avisar(string texto)
        {
            _aviso.Mostrar(texto, SegundosAviso);
        }

        private void Update()
        {
            // El TUTORIAL pasó al paso siguiente (o a la Noche 1) sin recargar la escena.
            if (!Tutoriales.Vigente(TutorialTipo.Linterna)) { Destroy(gameObject); return; }

            var net = NetworkManager.Instance;
            bool enPartida = net != null && net.GameStarted;
            // Flanco de subida: arranque en frío y cada REPETIR (no recarga la escena).
            if (enPartida && !_enPartida) Reiniciar();
            _enPartida = enPartida;
            if (!enPartida)
            {
                Rediseno.ControlMirada.ObjetivoIntensa = null;
                if (_marca != null) _marca.SetActive(false);
                return;
            }

            float dt = Time.deltaTime;
            switch (_fase)
            {
                case Fase.Apagar:
                    if (Linterna == null) break;
                    if (!Linterna.isOn) _estuvoApagada = true;
                    else if (_estuvoApagada)
                    {
                        _estuvoApagada = false;
                        _ciclos++;
                        if (_ciclos >= MetaCiclos)
                        {
                            _fase = Fase.Concentrar;
                            _avance = 0f;
                            CrearMarca();
                            Avisar("¡Bien! Ya sabés prenderla y apagarla.");
                        }
                        else Avisar("Eso es. Una vez más: *apagala y prendela*.");
                    }
                    break;

                case Fase.Concentrar:
                    if (_marca == null) CrearMarca();
                    if (_marca == null) break;   // todavía sin cámara
                    _marca.SetActive(true);
                    Sostener(_marca.transform.position, RadioMarca, dt);
                    PintarMarca();
                    if (_avance >= SegundosSostener)
                    {
                        BorrarMarca();
                        _avance = 0f;
                        _fase = RitualBookView.Active != null ? Fase.Libro : Fase.Completa;
                        Avisar("¡Eso es concentrar! Gasta más, pero es la que sirve.");
                    }
                    break;

                case Fase.Libro:
                    var libro = RitualBookView.Active;
                    if (libro == null) { _fase = Fase.Completa; break; }
                    Sostener(libro.PuntoDeLuz, libro.RadioAproximado, dt);
                    if (_avance >= SegundosSostener) _fase = Fase.Completa;
                    break;
            }

            if (_fase == Fase.Completa) Rediseno.ControlMirada.ObjetivoIntensa = null;

            if (!net.IsServer) return;
            if (_finEn < 0f)
            {
                if (_fase == Fase.Completa) _finEn = Time.time + DemoraFin;
            }
            else if (Time.time >= _finEn)
            {
                _finEn = -1f;
                net.ServerNightSurvived();
            }
        }

        // Suma avance mientras la luz INTENSA toca el objetivo (las mismas reglas de cono que
        // el libro y el Sorken: PlayerLights) y lo pierde despacio si no.
        private void Sostener(Vector3 objetivo, float radio, float dt)
        {
            Rediseno.ControlMirada.ObjetivoIntensa = objetivo;
            Rediseno.ControlMirada.RadioObjetivoIntensa = radio;

            _alumbrando = false;
            var cam = Camera.main;
            if (cam != null && Linterna != null && Linterna.Mode == FlashlightMode.Bright &&
                PlayerLights.TryConoReal(out float ang, out float alcance))
                _alumbrando = PlayerLights.Alcanza(cam.transform.position, cam.transform.forward,
                                                   objetivo, ang, alcance, radio);
            _avance = Mathf.Clamp(_avance + (_alumbrando ? dt : -Decaimiento * dt), 0f, SegundosSostener);
        }

        private void Reiniciar()
        {
            _fase = Fase.Apagar;
            _ciclos = 0;
            _estuvoApagada = false;
            _avance = 0f;
            _alumbrando = false;
            _finEn = -1f;
            _aviso.Limpiar();
            BorrarMarca();
            Bateries.BatterySpawnManager.Retenidas = true;
        }

        // ── La marca: una esfera semitransparente con un halo que late ──────────

        private void CrearMarca()
        {
            BorrarMarca();
            var cam = Camera.main;
            if (cam == null) return;

            // Delante, a la altura de la vista y un poco más abajo; si hay una pared escaneada
            // más cerca, antes de ella (detrás quedaría tapada por la oclusión).
            Vector3 adelante = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (adelante.sqrMagnitude < 0.01f) adelante = Vector3.ProjectOnPlane(cam.transform.up, Vector3.up);
            adelante.Normalize();
            Vector3 origen = cam.transform.position;
            float dist = DistanciaMarca;
            if (Physics.Raycast(origen, adelante, out var hit, DistanciaMarca + 0.3f))
                dist = Mathf.Max(0.6f, hit.distance - 0.3f);
            Vector3 pos = origen + adelante * dist + Vector3.down * 0.1f;

            var malla = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var shader = Resources.Load<Shader>("Rediseno/MarcadorFantasma") ?? Shader.Find("Rediseno/MarcadorFantasma");
            if (malla == null || shader == null)
            {
                _marca = AnchorVisuals.MakeSphere(null, pos, RadioMarca * 2f, T.Tan);
                if (_marca != null) Anclar(_marca.transform);
                return;
            }

            _marca = new GameObject("TutorialLinterna.Marca");
            _marca.transform.position = pos;
            _marca.transform.localScale = Vector3.one * (RadioMarca * 2f);
            _marca.AddComponent<MeshFilter>().sharedMesh = malla;
            _matMarca = new Material(shader) { name = "TutorialLinterna.Marca (runtime)" };
            _marca.AddComponent<MeshRenderer>().sharedMaterial = _matMarca;

            _halo = new GameObject("Halo");
            _halo.transform.SetParent(_marca.transform, false);
            _halo.AddComponent<MeshFilter>().sharedMesh = malla;
            _matHalo = new Material(shader) { name = "TutorialLinterna.Halo (runtime)" };
            _halo.AddComponent<MeshRenderer>().sharedMaterial = _matHalo;

            Anclar(_marca.transform);
            PintarMarca();
        }

        // Colgada del origen del mapa, como todo lo escaneado: sigue las correcciones del ancla.
        private static void Anclar(Transform t)
        {
            var wo = WorldOrigin.Instance;
            if (wo != null && wo.IsReady) t.SetParent(wo.transform, true);
        }

        // Dorada en reposo, roja a medida que se llena; el halo late (más rápido con la luz
        // encima) para que se encuentre aunque esté en penumbra.
        private void PintarMarca()
        {
            if (_matMarca == null) return;
            float k = _avance / SegundosSostener;
            var c = Color.Lerp(T.Tan, T.Red, k);
            _matMarca.color = new Color(c.r, c.g, c.b, 0.55f + 0.4f * k);
            if (_halo == null) return;
            float fase = Mathf.Repeat(Time.time * (_alumbrando ? 2.2f : 0.8f), 1f);
            _halo.transform.localScale = Vector3.one * Mathf.Lerp(1.2f, 2.4f, fase);
            _matHalo.color = new Color(c.r, c.g, c.b, 0.35f * (1f - fase));
        }

        private void BorrarMarca()
        {
            if (_marca != null) Destroy(_marca);
            if (_matMarca != null) Destroy(_matMarca);
            if (_matHalo != null) Destroy(_matHalo);
            _marca = _halo = null;
            _matMarca = _matHalo = null;
        }

        // ── HUD ──────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_enPartida || Gamepad.PauseMenuController.IsOpen) return;
            if (LocalDeath.Instance != null && LocalDeath.Instance.IsDead) return;

            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            // Fila 1: la de la cordura, que en esta práctica no se usa (SanityHUD la oculta).
            switch (_fase)
            {
                case Fase.Apagar:
                    T.HudObjetivos(vw, vh, 1, Mathf.Min(_ciclos, MetaCiclos), MetaCiclos, "APAGAR Y PRENDER");
                    break;
                case Fase.Concentrar:
                case Fase.Libro:
                    T.HudBarra(vw, vh, 1, _avance / SegundosSostener, _alumbrando ? T.Red : T.Tan,
                               _fase == Fase.Libro ? "LUZ AL LIBRO" : "CONCENTRAR",
                               $"{_avance:0.0}/{SegundosSostener:0} s");
                    break;
                default:
                    T.HudBarra(vw, vh, 1, 1f, T.Green, "LINTERNA", "LISTO");
                    break;
            }
            PistaTutorial.Dibujar(vw, Texto(), _aviso);
            if (_fase == Fase.Concentrar && _marca != null)
                GuiaTutorial.Dibujar(_marca.transform.position, "MARCA");
            else if (_fase == Fase.Libro && RitualBookView.Active != null)
                GuiaTutorial.Dibujar(RitualBookView.Active.PuntoDeLuz, "LIBRO");
        }

        private string Texto()
        {
            switch (_fase)
            {
                case Fase.Completa:
                    return "¡Listo! Ya manejás la linterna.";
                case Fase.Apagar:
                    return Linterna != null && !Linterna.isOn
                        ? $"Ahora *prendela* ({PistaTutorial.ComoPrender()})."
                        : $"*Apagá la linterna* ({PistaTutorial.ComoApagar()}).";
            }
            if (!LuzPrendida) return $"*Prendé la linterna* ({PistaTutorial.ComoPrender()}).";
            if (_alumbrando) return "¡Ahí! *Sostené la luz encima*…";
            return _fase == Fase.Libro
                ? $"Ahora *concentrá la luz en el libro* ({PistaTutorial.ComoLuzIntensa()})."
                : $"*Concentrá la luz en la marca* ({PistaTutorial.ComoLuzIntensa()}).";
        }
    }
}
