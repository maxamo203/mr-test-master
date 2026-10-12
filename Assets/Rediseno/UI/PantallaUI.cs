using Gamepad;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UIElements;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Rediseno.UI
{
    // Base de las pantallas del rediseño (UI Toolkit). Arma el documento por código y lo
    // hace usable de las dos formas sin que la pantalla tenga que saberlo:
    //
    //   Normal: el panel se dibuja en la pantalla del celular; se toca con el dedo o se
    //   navega con un mando (MenuNavegable).
    //
    //   Visor (ModoVisor.Activo): el panel se dibuja en una textura y se compone una vez por
    //   ojo, como una pantalla flotando delante tuyo, quieta en el espacio: al girar la
    //   cabeza (giroscopio) la pantalla se queda en su lugar y el punto de mira del centro
    //   recorre las opciones. Mirar una opción ~1 s la elige (anillo). La ruedita del
    //   VR BOX sigue funcionando.
    //
    // Las subclases construyen el árbol en Construir() y deciden en EsVisible() cuándo se
    // muestran. Fondo animado: si la subclase lo dibuja dentro del árbol, en el visor lo
    // tiene que ocultar (FondoVisor lo dibuja con profundidad, detrás de la pantalla).
    public abstract class PantallaUI : MonoBehaviour
    {
        // Resolución virtual de diseño (vertical, como un celular). En el visor el panel
        // usa el mismo ancho, así las medidas no cambian.
        protected const int AnchoRef = 1080, AltoRef = 1920;
        private const int AltoVisor = 1500;

        protected UIDocument   Doc;
        protected VisualElement Raiz;
        protected readonly MenuNavegable Nav = new();

        private PanelSettings _ps;
        private RenderTexture _rt;
        private bool _visible;
        private bool _visorAplicado;

        // Hacia dónde mira la cabeza respecto de "al frente" (grados): yaw + derecha, pitch
        // + arriba. Con la cámara AR el "frente" se toma al aparecer la pantalla.
        private float _yaw, _pitch, _refYaw, _refPitch;
        private bool  _tieneRefCamara;

        // ¿Hay alguna pantalla nueva a la vista? Las pantallas viejas (IMGUI) no se dibujan
        // mientras tanto, y si se llega a una vieja estando en el visor se sale del visor
        // (todavía no saben dibujarse en estéreo). Ver NightMenuUI.OnGUI.
        public static bool AlgunaVisible => _visibles > 0;
        private static int _visibles;

        // Contenido que se desplaza (si la pantalla es más alta que el lugar). Se desplaza
        // solo hacia la opción enfocada con el mando, y en el visor al mirar el borde.
        protected ScrollView Desplazable;
        private VisualElement _ultimoFoco;

        // ── Para las subclases ────────────────────────────────────────────────
        protected abstract void Construir(VisualElement raiz, MenuNavegable nav);
        protected abstract bool EsVisible();
        // Fondo que se dibuja detrás de la pantalla en el visor (null = negro).
        protected virtual Texture FondoVisor => null;
        // Se llama al entrar/salir del visor (para ocultar el fondo del árbol, etc.).
        protected virtual void AlCambiarVisor(bool visor) { }
        // ¿Se dibuja en estéreo ahora? Por defecto, el visor de los menús (ModoVisor). La
        // pausa en partida lo pisa para seguir al Cardboard de MRCardboardController.
        protected virtual bool EnVisor => ModoVisor.Activo;
        // De dónde sale hacia dónde mira la cabeza: false = sensor del celular (menús, sin
        // AR); true = la cámara AR (en partida ya hay tracking, más preciso y sin signos).
        protected virtual bool CabezaPorCamara => false;
        // Lo que tapa el fondo en el visor: opaco en los menús; en partida conviene
        // semitransparente para seguir viendo el cuarto detrás.
        protected virtual Color VeloVisor => Color.black;
        // Tamaño (ancho angular, grados) y posición (grados respecto del frente: + derecha,
        // + arriba) de la pantalla flotante en el visor. Un menú va grande y al frente; un
        // panel sobre la cámara (sincronización) va más chico y abajo para no tapar.
        // 44°: la pantalla entera (~61° de alto con el lienzo vertical) entra en lo que ve
        // cada ojo sin rozar el borde de la lente.
        protected virtual float AnguloPanel => 44f;
        protected virtual Vector2 PosicionVisor => Vector2.zero;
        // Para dibujar algo fijo a la vista en cada ojo (IMGUI, píxeles relativos al ojo),
        // antes de la pantalla flotante. c = centro del ojo, ppd = píxeles por grado.
        protected virtual void DibujarEnOjo(Vector2 c, float ppd) { }
        // Cada frame mientras se ve (animaciones).
        protected virtual void Animar(float dt) { }

        protected virtual void Awake()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();

            _ps = ScriptableObject.CreateInstance<PanelSettings>();
            _ps.name = GetType().Name;
            // Sin tema: todo el estilo es inline (TemaUI). El tema vacío evita el aviso.
            _ps.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            _ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _ps.referenceResolution = new Vector2Int(AnchoRef, AltoRef);
            _ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _ps.match = 0f;   // manda el ancho
            // Limpiar sólo en el visor (cada pantalla dibuja en SU textura). En la pantalla
            // del celular NO: varios paneles comparten la misma pantalla, y uno oculto que
            // limpiara después borraba a negro la pantalla visible (pasó con OPCIONES).
            _ps.clearColor = false;
            _ps.colorClearValue = Color.clear;

            Doc = gameObject.AddComponent<UIDocument>();
            Doc.panelSettings = _ps;
        }

        protected virtual void OnEnable()  => ModoVisor.Cambio += AplicarVisor;
        protected virtual void OnDisable() => ModoVisor.Cambio -= AplicarVisor;

        protected virtual void Start()
        {
            Raiz = Doc.rootVisualElement;
            Raiz.style.flexGrow = 1;
            Construir(Raiz, Nav);
            AplicarVisor();
            ActualizarVisible(force: true);
        }

        protected virtual void OnDestroy()
        {
            if (_visible) { _visible = false; _visibles--; }
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_ps != null) Destroy(_ps);
        }

        private void ActualizarVisible(bool force = false)
        {
            bool v = EsVisible();
            if (v == _visible && !force) return;
            if (v != _visible) _visibles += v ? 1 : -1;
            _visible = v;
            if (v)
            {
                _ultimoFoco = null; Nav.MostrarFoco = false; _tieneRefCamara = false;
                // Al aparecer, ignorar un instante la palanca y los toques: el mismo empuje o
                // toque que cerró la pantalla anterior caía sobre una opción de ésta (así se
                // armaba el loop SINCRONIZACIÓN → UBICAR EL ENTORNO → SINCRONIZACIÓN).
                BloqueoNav.TrasAccion();
                _toquesDesde = Time.unscaledTime + 0.35f;
                _dwell = 0f; _dwellOpcion = null;
            }
            if (Raiz != null) Raiz.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Visor ─────────────────────────────────────────────────────────────
        private void AplicarVisor()
        {
            bool visor = EnVisor;
            _visorAplicado = visor;
            if (_ps == null) return;

            if (visor)
            {
                if (_rt == null)
                {
                    // Con depth/stencil (24 = D24S8): UI Toolkit recorta con stencil el contenido de
                    // los elementos con overflow:hidden y esquinas redondeadas (tarjetas de noche).
                    // Sin stencil ese recorte fallaba en el visor y las tarjetas salían en blanco.
                    _rt = new RenderTexture(AnchoRef, AltoVisor, 24, RenderTextureFormat.ARGB32)
                    { name = GetType().Name + "_Visor", useMipMap = false, antiAliasing = 1 };
                    _rt.Create();
                }
                _ps.clearColor = true;
                _ps.targetTexture = _rt;
                _tieneRefCamara = false;
                _modoCuartoAplicado = !EnElCuarto;   // fuerza a preparar la cabeza en TickMirada
            }
            else
            {
                _ps.clearColor = false;
                _ps.targetTexture = null;
                Cabeza.Habilitar(false);
            }
            _dwell = 0f; _dwellOpcion = null;
            if (Raiz != null) AlCambiarVisor(visor);
        }

        // ── Loop ──────────────────────────────────────────────────────────────
        protected virtual void Update()
        {
            ActualizarVisible();
            if (!_visible || Raiz == null) return;
            if (EnVisor != _visorAplicado) AplicarVisor();

            float dt = Time.unscaledDeltaTime;
            Animar(dt);

            var gm = GamepadManager.Instance;
            bool hayMando = gm != null && gm.IsConnected;
            Nav.MostrarFoco = hayMando || EnVisor;

            // Atrás de Android / Escape: en el visor, sale del visor. Con el VR BOX el escape
            // es la B (Android reenvía su BUTTON_BACK como "atrás"): eso lo maneja Nav como
            // volver, sin salir del visor ni volver dos veces.
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !VRBoxInput.CancelDown)
            {
                if (EnVisor) ModoVisor.Salir();
                else Nav.PedirVolver();
            }

            Nav.TickMandos();

            if (EnVisor) { TickMirada(dt, hayMando); TickToquesVisor(); }
            else TickToques();

            // Llevar a la vista la opción enfocada (sólo cuando el foco cambia). Hay que esperar
            // a que la opción tenga medidas: desplazar antes del primer layout (recién
            // armada la lista) la dejaba corrida y sólo se veía el último ítem.
            var foco = Nav.Enfocada?.Elemento;
            if (foco != _ultimoFoco)
            {
                bool enLista = Desplazable != null && foco != null && Desplazable.contentContainer.Contains(foco);
                if (!enLista || !Nav.MostrarFoco) _ultimoFoco = foco;
                else if (foco.layout.height > 0f && !float.IsNaN(foco.layout.height))
                {
                    Desplazable.ScrollTo(foco);
                    _ultimoFoco = foco;
                }
            }

            Nav.PintarFoco();
        }

        // Toque soltado sobre una opción = elegirla. Se resuelve a mano (panel.Pick) para
        // que funcione igual en todas las pantallas y no depender del sistema de eventos.
        private float _toquesDesde;

        private void TickToques()
        {
            if (!TryTapSoltado(out Vector2 px)) return;
            if (Time.unscaledTime < _toquesDesde) return;
            var panel = Raiz.panel;
            if (panel == null) return;
            // Coordenadas de pantalla con el origen arriba (lo que espera UI Toolkit).
            Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(px.x, Screen.height - px.y));
            var op = Nav.OpcionDe(panel.Pick(p));
            if (op != null) Nav.Activar(op);
        }

        // Toque con el visor: tocar el botón en CUALQUIERA de los dos ojos. El punto se pasa al
        // panel con el rect donde la pantalla flotante quedó dibujada en ese ojo (OnGUI).
        private readonly Rect[] _rectOjo = new Rect[2];
        private bool _hayRectOjo;

        private void TickToquesVisor()
        {
            if (!_hayRectOjo || !TryTapSoltado(out Vector2 px)) return;
            if (Time.unscaledTime < _toquesDesde) return;
            var panel = Raiz.panel;
            if (panel == null) return;
            var p = new Vector2(px.x, Screen.height - px.y);   // a origen arriba, como OnGUI
            for (int i = 0; i < 2; i++)
            {
                var r = _rectOjo[i];
                if (!r.Contains(p)) continue;
                float w = Raiz.layout.width, h = Raiz.layout.height;
                var enPanel = new Vector2((p.x - r.x) / r.width * w, (p.y - r.y) / r.height * h);
                var op = Nav.OpcionDe(panel.Pick(enPanel));
                if (op != null) Nav.Activar(op);
                return;
            }
        }

        private static bool TryTapSoltado(out Vector2 pos)
        {
            pos = default;
            foreach (var t in ETouch.activeTouches)
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    // Un arrastre largo no es un toque.
                    if ((t.screenPosition - t.startScreenPosition).sqrMagnitude > 60f * 60f) continue;
                    pos = t.screenPosition;
                    return true;
                }
#if UNITY_EDITOR
            var m = Mouse.current;
            if (m != null && m.leftButton.wasReleasedThisFrame) { pos = m.position.ReadValue(); return true; }
#endif
            return false;
        }

        // ── Mirada (visor) ────────────────────────────────────────────────────
        // Ancho angular de la pantalla flotante y campo visual de cada ojo. Con 50° sobre
        // ~90° la pantalla entra entera mirando al frente y se recorre girando poco.
        private const float FovOjo      = 90f;
        private const float SegundosElegir = 1.1f;
        private float _dwell, _frio;
        private MenuNavegable.Opcion _dwellOpcion;

        // Opción (GameOptions.MenusEnElCuarto): ¿la pantalla queda quieta en el cuarto y se
        // elige mirando, o fija delante de cada ojo (sigue a la cabeza) y se usa con el
        // joystick? Fija es el default por ahora (visor + joystick).
        private static bool EnElCuarto => GameOptions.MenusEnElCuarto;
        private bool _modoCuartoAplicado;

        private bool EnCuentaRegresiva => EnElCuarto && !CabezaPorCamara && Cabeza.EnCuentaRegresiva;

        private void LeerCabeza(float dt)
        {
            if (!CabezaPorCamara)
            {
                Cabeza.Tick(dt, AnguloPanel);
                _yaw = Cabeza.Yaw; _pitch = Cabeza.Pitch;
                return;
            }
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 f = cam.transform.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (!_tieneRefCamara) { _refYaw = yaw; _refPitch = pitch; _tieneRefCamara = true; }
            _yaw = Mathf.DeltaAngle(_refYaw, yaw);
            _pitch = pitch - _refPitch;
            // Si te diste vuelta, la pantalla vuelve a ponerse delante.
            if (Mathf.Abs(_yaw) > AnguloPanel * 0.5f + 45f) { _refYaw = yaw; _yaw = 0f; }
        }

        private void TickMirada(float dt, bool hayMando)
        {
            // Cambio de modo (al entrar al visor o si se tocó la opción): preparar o soltar
            // el sensor de orientación de los menús.
            if (EnElCuarto != _modoCuartoAplicado)
            {
                _modoCuartoAplicado = EnElCuarto;
                _tieneRefCamara = false;
                if (!CabezaPorCamara)
                {
                    if (EnElCuarto) { Cabeza.Recentrar(conCuentaRegresiva: true); Cabeza.Habilitar(true); }
                    else Cabeza.Habilitar(false);
                }
            }

            // Fija delante de cada ojo: sin cabeza ni anillo; se usa con el joystick.
            if (!EnElCuarto)
            {
                _yaw = _pitch = 0f;
                _dwell = 0f; _dwellOpcion = null;
                return;
            }

            LeerCabeza(dt);
            _frio -= dt;
            // Con un mando conectado elige el mando: la mirada no llena el anillo (si no, al
            // apuntar con la cabeza mientras navegás con la palanca se elegían cosas solas).
            if (EnCuentaRegresiva || hayMando) { _dwell = 0f; _dwellOpcion = null; return; }

            var panel = Raiz.panel;
            if (panel == null) return;

            // Punto del panel bajo el centro de la vista.
            float w = Raiz.layout.width, h = Raiz.layout.height;
            if (w <= 0f || h <= 0f) return;
            float ppd = w / AnguloPanel;  // unidades de panel por grado
            Vector2 off = PosicionVisor;
            Vector2 p = new(w * 0.5f + (_yaw - off.x) * ppd, h * 0.5f - (_pitch - off.y) * ppd);

            // Mirar el borde de arriba/abajo de una lista larga la desplaza.
            if (Desplazable != null)
            {
                Rect r = Desplazable.worldBound;
                if (p.x >= r.xMin && p.x <= r.xMax)
                {
                    float banda = Mathf.Min(140f, r.height * 0.15f);
                    float v = 0f;
                    if (p.y >= r.yMin && p.y < r.yMin + banda) v = -1f;
                    else if (p.y <= r.yMax && p.y > r.yMax - banda) v = 1f;
                    if (v != 0f)
                    {
                        var scroll = Desplazable.scrollOffset;
                        scroll.y += v * 700f * dt;
                        Desplazable.scrollOffset = scroll;
                    }
                }
            }

            MenuNavegable.Opcion op = null;
            if (p.x >= 0f && p.y >= 0f && p.x <= w && p.y <= h) op = Nav.OpcionDe(panel.Pick(p));

            if (op != _dwellOpcion) { _dwellOpcion = op; _dwell = 0f; }
            if (op == null || !op.Habilitada || _frio > 0f) { if (op != null) Nav.Enfocar(op); return; }

            Nav.Enfocar(op);
            _dwell += dt / SegundosElegir;
            if (_dwell >= 1f)
            {
                _dwell = 0f;
                _frio = 0.8f;
                Nav.Activar(op);
            }
        }

        // Composición estéreo: fondo con paralaje, la pantalla flotante y la mira, una vez
        // por ojo (centros en W*0.25 / W*0.75, igual que MRCardboardController).
        protected virtual void OnGUI()
        {
            if (!_visible || !EnVisor || _rt == null) return;
            if (Event.current.type != EventType.Repaint) return;

            var prevM = GUI.matrix; var prevC = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            GUI.color = VeloVisor;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            float ojoW = Screen.width * 0.5f, ojoH = Screen.height;
            float ppd = ojoW / FovOjo;   // píxeles de pantalla por grado
            // Centro de cada ojo según la calibración del visor (ver OjosVisor): con el
            // centro de cada mitad las dos imágenes no se fusionaban.
            OjosVisor.Centros(out var centroIzq, out var centroDer);
            for (int ojo = 0; ojo < 2; ojo++)
            {
                GUI.BeginGroup(new Rect(ojo * ojoW, 0, ojoW, ojoH));
                var c = (ojo == 0 ? centroIzq : centroDer) - new Vector2(ojo * ojoW, 0f);

                // Fondo "lejos": se mueve menos que la pantalla al girar (paralaje).
                var fondo = FondoVisor;
                if (fondo != null)
                {
                    float fh = ojoH * 1.5f, fw = fh * fondo.width / Mathf.Max(1f, fondo.height);
                    var fc = c + new Vector2(-_yaw, _pitch) * ppd * 0.35f;
                    GUI.color = new Color(0.45f, 0.42f, 0.4f, 1f);
                    GUI.DrawTexture(new Rect(fc.x - fw * 0.5f, fc.y - fh * 0.5f, fw, fh), fondo, ScaleMode.StretchToFill);
                }

                DibujarEnOjo(c, ppd);

                // Pantalla flotante. La convergencia ya va en los centros de OjosVisor.
                float pw = AnguloPanel * ppd, ph = pw * _rt.height / _rt.width;
                const float disp = 0f;
                Vector2 off = PosicionVisor;
                var pc = c + new Vector2(-(_yaw - off.x) * ppd + disp, (_pitch - off.y) * ppd);
                GUI.color = Color.white;
                var rp = new Rect(pc.x - pw * 0.5f, pc.y - ph * 0.5f, pw, ph);
                GUI.DrawTexture(rp, _rt, ScaleMode.StretchToFill, true);
                // Rect en píxeles de pantalla (el grupo de este ojo arranca en ojo*ojoW).
                _rectOjo[ojo] = new Rect(rp.x + ojo * ojoW, rp.y, rp.width, rp.height);
                _hayRectOjo = true;

                DibujarMira(c, Screen.height / 1080f);
                GUI.EndGroup();
            }

            GUI.matrix = prevM; GUI.color = prevC;
        }

        private void DibujarMira(Vector2 c, float s)
        {
            if (!EnElCuarto) return;   // fija delante del ojo: no hay a dónde apuntar
            var punto = IconosMirada.Punto;
            if (EnCuentaRegresiva)
            {
                // Mientras se acomoda el visor: aviso centrado, sin interacción.
                var st = MortuoriumTheme.Estilo(MortuoriumTheme.FBebas, Mathf.RoundToInt(34 * s),
                                                TemaUI.Cream, TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                GUI.Label(new Rect(c.x - 300 * s, c.y - 60 * s, 600 * s, 120 * s),
                          $"PONÉ EL CELULAR EN EL VISOR\n{Mathf.CeilToInt(Cabeza.CuentaRegresiva)}", st);
                return;
            }

            float rp = 4.5f * s;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(c.x - rp, c.y - rp, rp * 2f, rp * 2f), punto);
            if (_dwell <= 0f) return;

            const int N = 32;
            float radio = 26f * s, rd = 3f * s;
            int on = Mathf.CeilToInt(Mathf.Clamp01(_dwell) * N);
            for (int i = 0; i < N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                var p = new Vector2(c.x + Mathf.Sin(a) * radio, c.y - Mathf.Cos(a) * radio);
                GUI.color = i < on ? TemaUI.Tan : new Color(1f, 1f, 1f, 0.18f);
                GUI.DrawTexture(new Rect(p.x - rd, p.y - rd, rd * 2f, rd * 2f), punto);
            }
        }
    }
}
