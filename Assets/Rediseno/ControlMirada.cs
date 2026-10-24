using System.Collections.Generic;
using Bateries;
using Gamepad;
using Gameplay;
using UnityEngine;

namespace Rediseno
{
    // Control por MIRADA para jugar con el visor puesto y sin joystick (Rediseño 2.0).
    // Con el celular en la cabeza no hay pantalla que tocar, así que todo sale de hacia
    // dónde mirás:
    //   - Recoger: mirar una pila o reliquia ~1,2 s (se llena un anillo verde con el ícono
    //     de recoger). Acercarse la recoge más rápido.
    //   - Prender/apagar: FRANJA DE LA LINTERNA. Una franja que rodea al jugador a ~32° por
    //     encima del horizonte: levantar la vista y cruzarla prende o apaga al instante (sin
    //     anillo: se pensó para un momento de pánico). Después hay que bajar la vista por
    //     debajo de ella y esperar un instante para volver a usarla. Casi nadie mira para
    //     arriba (el libro y las pilas están abajo, las entidades a la altura de la cara).
    //     Ajustes en FranjaLinternaAjustes (dev).
    //   - Prender/apagar mirando el piso: DESACTIVADO (mirarPisoApaga) — se superponía con
    //     alumbrar el libro.
    //   - Mirar arriba ~1,5 s para abrir la pausa: DESACTIVADO por ahora (mirarArribaPausa),
    //     para que no choque con la franja (para llegar arriba había que cruzarla).
    //   - Luz intensa: sola, mientras el libro que se está oscureciendo (o un Sorken) quede
    //     centrado en el haz. El libro y el Sorken sólo cuentan la luz en modo intenso.
    //
    // Sólo actúa en partida, en Cardboard y SIN mando
    // conectado (con joystick manda el joystick). Lo agrega ContextActionController a su
    // propio GameObject y corre después de él (que resuelve la acción apuntada del frame).
    [DefaultExecutionOrder(10)]
    public class ControlMirada : MonoBehaviour
    {
        public static ControlMirada Instance { get; private set; }

        // ¿Está mandando la mirada este frame? Lo consulta ContextActionController para no
        // dibujar su botón en pantalla (con el visor puesto no se puede tocar).
        public static bool Activo => Instance != null && Instance._activo;

        [Header("Recoger")]
        [SerializeField, Min(0.1f)] private float segundosRecoger      = 1.2f;
        [SerializeField, Min(0.1f)] private float segundosRecogerCerca = 0.45f;
        [Tooltip("Más cerca que esto (m), el anillo se llena con segundosRecogerCerca.")]
        [SerializeField, Min(0f)]   private float distanciaCerca       = 0.6f;

        [Header("Mirar el piso (prender/apagar)")]
        [Tooltip("DESACTIVADO por ahora: se superpone con alumbrar el libro (que suele estar " +
                 "abajo, sobre la imagen) y apagaba la linterna justo cuando había que salvarlo.")]
        [SerializeField] private bool mirarPisoApaga = false;
        [SerializeField, Min(0.1f)] private float segundosMirarPiso = 1.0f;
        [Tooltip("Grados por debajo del horizonte para empezar a llenar el anillo.")]
        [SerializeField, Range(30f, 89f)] private float pisoGrados = 62f;
        [Tooltip("Hay que subir la vista por encima de esto para poder repetir el gesto.")]
        [SerializeField, Range(10f, 80f)] private float pisoRearmeGrados = 45f;

        [Header("Mirar arriba (abrir la pausa)")]
        [Tooltip("DESACTIVADO por ahora: para mirar tan arriba había que cruzar la franja de la " +
                 "linterna y la prendía/apagaba.")]
        [SerializeField] private bool mirarArribaPausa = false;
        [SerializeField, Min(0.1f)] private float segundosMirarArriba = 1.5f;
        [Tooltip("Grados por encima del horizonte para empezar a llenar el anillo de la pausa.")]
        [SerializeField, Range(30f, 89f)] private float arribaGrados = 62f;

        [Header("Luz intensa automática")]
        [Tooltip("Desvío máximo (grados) del centro de la vista para que se concentre.")]
        [SerializeField, Range(1f, 30f)] private float intensaEntrarGrados = 10f;
        [Tooltip("Desvío para soltarla (más ancho que el de entrada, para que no titile en el borde).")]
        [SerializeField, Range(1f, 40f)] private float intensaSalirGrados = 14f;

        [Tooltip("Pausa (s) después de cada acción, para que un mismo gesto no dispare dos.")]
        [SerializeField, Min(0f)] private float enfriamiento = 0.6f;

        private enum Gesto { Ninguno, Recoger, Linterna, Pausa }

        private bool       _activo;
        private Gesto      _gesto;
        private float      _progreso;          // 0..1
        private Transform  _objetivo;
        private bool       _pisoArmado = true;
        private float      _frio;
        private bool       _intensaPorMirada;
        private Flashlight _fl;
        private Camera     _cam;

        private readonly List<SorkenEntity> _sorkens = new();
        private float _sorkenTimer;

#if UNITY_EDITOR
        // Mortuorium > Modo mirada en Play: prueba el control sin Cardboard ni dispositivo
        // (con los controles WASD + arrastre del mouse para mirar). EditorPrefs, por máquina.
        private const string KeyForzado = "Mortuorium.ModoMiradaEnPlay";
        public static bool ForzadoEnEditor
        {
            get => UnityEditor.EditorPrefs.GetBool(KeyForzado, false);
            set => UnityEditor.EditorPrefs.SetBool(KeyForzado, value);
        }
#else
        private const bool ForzadoEnEditor = false;
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDisable() => Desactivar();

        private static bool DebeActivarse()
        {
            var net = NetworkManager.Instance;
            if (net == null || !net.GameStarted) return false;
            if (PauseMenuController.IsOpen) return false;
            var muerte = LocalDeath.Instance;
            if ((muerte != null && muerte.IsDead) || NightResult.Sobrevivio) return false;
            // Con un mando (también el VR BOX) manda el mando: la ruedita → recoge / prende y
            // apaga, ↑ intensa, ↓ apagada, ← pausa. La mirada queda para jugar sin joystick.
            var gm = GamepadManager.Instance;
            if (gm != null && gm.IsConnected) return false;
            return MRCardboardController.Activo || ForzadoEnEditor;
        }

        private void Update()
        {
            if (!DebeActivarse())
            {
                if (_activo) Desactivar();
                return;
            }
            _activo = true;

            if (_cam == null) _cam = Camera.main;
            if (_fl == null) _fl = FindFirstObjectByType<Flashlight>();
            if (_cam == null) return;

            float dt = Time.deltaTime;
            _frio -= dt;
            Transform cam = _cam.transform;

            // 1) ¿Hay algo recogible apuntado? Gana siempre (aunque estés mirando el piso).
            var accion = ContextActionController.Instance != null
                ? ContextActionController.Instance.AccionActual : null;
            Transform obj = (accion as IObjetivoMirada)?.ObjetivoApuntado;

            if (obj != null)
            {
                if (_gesto != Gesto.Recoger || obj != _objetivo)
                {
                    _gesto = Gesto.Recoger;
                    _objetivo = obj;
                    _progreso = 0f;
                }
                if (_frio <= 0f)
                {
                    float d   = Vector3.Distance(cam.position, obj.position);
                    float dur = d <= distanciaCerca ? segundosRecogerCerca : segundosRecoger;
                    _progreso += dt / dur;
                    if (_progreso >= 1f)
                    {
                        accion.Execute();
                        Terminar();
                    }
                }
            }
            else
            {
                // 2) Mirar el piso: grados por debajo del horizonte.
                float abajo = Mathf.Asin(Mathf.Clamp(-cam.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (Mathf.Abs(abajo) < pisoRearmeGrados) _pisoArmado = true;

                bool puede = mirarPisoApaga && _pisoArmado && abajo >= pisoGrados && _frio <= 0f &&
                             _fl != null && _fl.Operational;
                // Mirar arriba: abrir la pausa (con el visor no hay botón que tocar).
                bool arriba = mirarArribaPausa && _pisoArmado && -abajo >= arribaGrados && _frio <= 0f &&
                              PauseMenuController.Instance != null;
                if (arriba)
                {
                    if (_gesto != Gesto.Pausa) { _gesto = Gesto.Pausa; _progreso = 0f; }
                    _progreso += dt / segundosMirarArriba;
                    if (_progreso >= 1f)
                    {
                        PauseMenuController.Instance.AbrirPausa();
                        Terminar();
                    }
                }
                else if (puede)
                {
                    if (_gesto != Gesto.Linterna) { _gesto = Gesto.Linterna; _progreso = 0f; }
                    _progreso += dt / segundosMirarPiso;
                    if (_progreso >= 1f)
                    {
                        _fl.Toggle();
                        Terminar();
                    }
                }
                else
                {
                    _gesto = Gesto.Ninguno;
                    _progreso = 0f;
                    _objetivo = null;
                }
            }

            // 3) Intensa automática sobre el libro / el Sorken.
            ActualizarIntensa(cam, dt);

            // 4) Franja de la linterna: prender / apagar.
            ActualizarFranja(cam, dt);
        }

        // ── Franja de la linterna ─────────────────────────────────────────────
        // Cruzarla hacia arriba prende/apaga al instante. Para volver a usarla hay que bajar la
        // vista por debajo de la franja (con un margen, para no titilar justo en el borde) y que
        // pase la espera.
        private const float MargenRearme = 8f;
        private bool  _franjaArmada = true;
        private float _franjaFrio;
        private float _avisoFranjaHasta;
        private bool  _avisoPrendida;

        private void ActualizarFranja(Transform cam, float dt)
        {
            _franjaFrio -= dt;
            float altura = Mathf.Asin(Mathf.Clamp(cam.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float franja = FranjaLinternaAjustes.Altura;
            FranjaLinternaAjustes.AlturaVista = altura;

            if (altura < franja - MargenRearme) { _franjaArmada = true; return; }
            if (altura < franja || !_franjaArmada) return;

            _franjaArmada = false;
            if (_franjaFrio > 0f) { FranjaLinternaAjustes.UltimoCambio = "muy seguido"; return; }
            if (_fl == null || !_fl.Operational) { FranjaLinternaAjustes.UltimoCambio = "sin linterna"; return; }
            _fl.Toggle();
            _franjaFrio = FranjaLinternaAjustes.Espera;
            _avisoFranjaHasta = Time.unscaledTime + 0.45f;
            _avisoPrendida = _fl.isOn;
            FranjaLinternaAjustes.UltimoCambio = _fl.isOn ? "prendida" : "apagada";
        }

        // Cierra un gesto ejecutado: enfriamiento y hay que levantar la vista para volver a
        // apagar (si no, recoger una pila del piso y seguir mirando abajo la apagaba).
        private void Terminar()
        {
            _gesto = Gesto.Ninguno;
            _progreso = 0f;
            _objetivo = null;
            _frio = enfriamiento;
            _pisoArmado = false;
        }

        private void Desactivar()
        {
            _activo = false;
            _gesto = Gesto.Ninguno;
            _progreso = 0f;
            _objetivo = null;
            if (_intensaPorMirada && _fl != null) _fl.SetBrightHeld(false);
            _intensaPorMirada = false;
        }

        private void ActualizarIntensa(Transform cam, float dt)
        {
            bool quiere = false;
            if (_fl != null && _fl.isOn && !_fl.IsEmpty)
            {
                float lim = _intensaPorMirada ? intensaSalirGrados : intensaEntrarGrados;

                var libro = RitualBookView.Active;
                if (libro != null && libro.Disponible && libro.Oscuridad01 > 0.02f &&
                    Centrado(cam, libro.PuntoDeLuz, libro.RadioAproximado, lim))
                    quiere = true;

                if (!quiere)
                {
                    RefrescarSorkens(dt);
                    for (int i = 0; i < _sorkens.Count; i++)
                    {
                        var s = _sorkens[i];
                        if (s == null || s.State == SorkenState.Idle) continue;
                        // Centro del torso + radio del cuerpo, igual que GameDirector.
                        if (Centrado(cam, s.Position + Vector3.up, 0.4f, lim)) { quiere = true; break; }
                    }
                }
            }

            if (quiere)
            {
                _fl.SetBrightHeld(true);
                _intensaPorMirada = true;
            }
            else if (_intensaPorMirada)
            {
                if (_fl != null) _fl.SetBrightHeld(false);
                _intensaPorMirada = false;
            }
        }

        // ¿El objetivo (con su radio) cae dentro de 'grados' del centro de la vista y al
        // alcance de la linterna?
        private bool Centrado(Transform cam, Vector3 objetivo, float radio, float grados)
        {
            Vector3 to = objetivo - cam.position;
            float d = to.magnitude;
            if (d < 0.01f) return true;
            if (_fl != null && d > _fl.range + radio) return false;
            float margen = Mathf.Atan2(radio, d) * Mathf.Rad2Deg;
            return Vector3.Angle(cam.forward, to) <= grados + margen;
        }

        // Los Sorken se buscan en el registro de entidades (host y clientes los tienen)
        // cada medio segundo: recorrerlo cada frame alocaría el enumerador.
        private void RefrescarSorkens(float dt)
        {
            _sorkenTimer -= dt;
            if (_sorkenTimer > 0f) return;
            _sorkenTimer = 0.5f;
            _sorkens.Clear();
            var reg = EntityRegistry.Instance;
            if (reg == null) return;
            foreach (var e in reg.All)
            {
                if (e == null || e.EntityTypeId != EntityTypeIds.Sorken) continue;
                var s = e.GetComponent<SorkenEntity>();
                if (s != null) _sorkens.Add(s);
            }
        }

        // ── HUD: anillo de la acción ─────────────────────────────────────────
        // No hay mira fija: sólo aparece el anillo cuando hay una acción en curso.
        // Píxeles de pantalla (sin la matriz de UIScale). En Cardboard, una copia en el
        // centro de cada ojo (MRCardboardController los ubica en W*0.25 / W*0.75), con
        // disparidad 0 para que se fusionen en una sola.
        private const int PuntosAnillo = 32;

        private static readonly Color ColRecoger = new(0.56f, 0.68f, 0.48f, 1f);  // verde
        private static readonly Color ColApagar  = new(0.77f, 0.18f, 0.23f, 1f);  // rojo
        private static readonly Color ColPrender = new(0.79f, 0.54f, 0.24f, 1f);  // dorado

        private void OnGUI()
        {
            if (!_activo || Event.current.type != EventType.Repaint) return;
            var prevM = GUI.matrix;
            var prevC = GUI.color;
            GUI.matrix = Matrix4x4.identity;

            float s = Screen.height / 1080f;
            if (MRCardboardController.Activo)
            {
                // Donde cae "el frente" en cada ojo según la calibración (ver OjosVisor).
                UI.OjosVisor.Centros(out var izq, out var der);
                float ojoW = Screen.width * 0.5f;
                DibujarFranja(izq, s, 0f, ojoW);
                DibujarFranja(der, s, ojoW, ojoW);
                DibujarMira(izq, s);
                DibujarMira(der, s);
            }
            else
            {
                var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                DibujarFranja(c, s, 0f, Screen.width);
                DibujarMira(c, s);
            }

            GUI.matrix = prevM;
            GUI.color = prevC;
        }

        // La franja: una línea fina a la altura de la franja (se mueve con el cuarto, con la misma
        // escala que la imagen de cámara) con el ícono de la linterna al medio. Aparece al
        // acercar la vista y se marca más al estar cerca. Más el debug (dev) debajo de la mira.
        private MRCardboardController _mr;

        private void DibujarFranja(Vector2 c, float s, float x0, float ancho)
        {
            float altura = FranjaLinternaAjustes.AlturaVista;
            float franja = FranjaLinternaAjustes.Altura;
            if (FranjaLinternaAjustes.MostrarDebug)
            {
                var st = MortuoriumTheme.Estilo(MortuoriumTheme.FMono, Mathf.RoundToInt(18 * s),
                                                MortuoriumTheme.Cream, TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                GUI.Label(new Rect(c.x - 200f * s, c.y + 48f * s, 400f * s, 28f * s),
                          $"vista {altura:0}° / franja {franja:0}° · {FranjaLinternaAjustes.UltimoCambio}", st);
            }

            float cerca = Mathf.InverseLerp(franja - 25f, franja - 3f, altura);
            if (cerca <= 0f) return;
            if (_mr == null) _mr = FindAnyObjectByType<MRCardboardController>();
            float ppd = _mr != null ? _mr.PixelesPorGrado() : 0f;
            if (ppd <= 0f) ppd = Screen.width * 0.5f / (UI.Cabeza.MitadVista * 2f);
            float y = c.y - (franja - altura) * ppd;
            if (y < -20f * s) return;

            float alfa = 0.15f + 0.55f * cerca;
            var col = new Color(MortuoriumTheme.Tan.r, MortuoriumTheme.Tan.g, MortuoriumTheme.Tan.b, alfa);
            GUI.color = col;
            GUI.DrawTexture(new Rect(x0 + ancho * 0.08f, y - 1.5f * s, ancho * 0.84f, 3f * s), Texture2D.whiteTexture);

            // Ícono + qué va a pasar al cruzarla, centrados sobre la línea.
            string texto = _fl != null && _fl.isOn ? "APAGAR LINTERNA" : "PRENDER LINTERNA";
            var stTexto = MortuoriumTheme.Estilo(MortuoriumTheme.FBebas, Mathf.RoundToInt(26 * s),
                                                 MortuoriumTheme.Tan, TextAnchor.MiddleLeft);
            float ri = 13f * s, sep = 8f * s;
            float tw = stTexto.CalcSize(new GUIContent(texto)).x;
            float x = c.x - (ri * 2f + sep + tw) * 0.5f, yc = y - 26f * s;
            GUI.DrawTexture(new Rect(x, yc - ri, ri * 2f, ri * 2f), IconosMirada.Linterna);
            GUI.Label(new Rect(x + ri * 2f + sep, yc - 18f * s, tw + 4f, 36f * s), texto, stTexto);
        }

        private void DibujarMira(Vector2 c, float s)
        {
            // Recién prendida/apagada con la franja: el ícono un instante, anillo lleno.
            bool avisoFranja = Time.unscaledTime < _avisoFranjaHasta;
            if (!avisoFranja && (_gesto == Gesto.Ninguno || _progreso <= 0f)) return;
            var punto = IconosMirada.Punto;
            float progreso = _progreso;

            Color col; Texture2D icono;
            if (avisoFranja && _gesto == Gesto.Ninguno)
            {
                col = _avisoPrendida ? ColPrender : ColApagar;
                icono = IconosMirada.Linterna;
                progreso = 1f;
            }
            else if (_gesto == Gesto.Recoger) { col = ColRecoger; icono = IconosMirada.Recoger; }
            else if (_gesto == Gesto.Pausa) { col = new Color(0.91f, 0.89f, 0.84f, 1f); icono = IconosMirada.Pausa; }
            else
            {
                bool prendida = _fl != null && _fl.isOn;
                col = prendida ? ColApagar : ColPrender;
                icono = IconosMirada.Linterna;
            }

            // Anillo de puntos que se encienden en sentido horario desde arriba.
            float radio = 30f * s, rd = 3.2f * s;
            int encendidos = Mathf.CeilToInt(Mathf.Clamp01(progreso) * PuntosAnillo);
            for (int i = 0; i < PuntosAnillo; i++)
            {
                float a = i / (float)PuntosAnillo * Mathf.PI * 2f;
                var p = new Vector2(c.x + Mathf.Sin(a) * radio, c.y - Mathf.Cos(a) * radio);
                GUI.color = i < encendidos ? col : new Color(1f, 1f, 1f, 0.18f);
                GUI.DrawTexture(new Rect(p.x - rd, p.y - rd, rd * 2f, rd * 2f), punto);
            }

            // Ícono de la acción en el centro del anillo (en vez de texto).
            float ri = 13f * s;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - ri, c.y - ri, ri * 2f, ri * 2f), icono);
        }
    }
}
