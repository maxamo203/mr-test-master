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
    //   recorre las opciones. Mirar una opción la elige (~2 s en los menús, ~1 s en
    //   partida; anillo). La ruedita del VR BOX sigue funcionando.
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

        // Punto del panel (unidades de panel) bajo el centro de la vista, en el modo "quieto en
        // el cuarto" con mirada. Lo usan las pantallas que además apuntan al mundo (anclas):
        // mirando su tarjeta no se está apuntando a una pared.
        private Vector2 _puntoMirada;
        private bool _hayPuntoMirada;
        protected bool PuntoMirada(out Vector2 p) { p = _puntoMirada; return _hayPuntoMirada; }

        // Vuelve a poner la pantalla delante (rumbo actual) en el modo "quieto en el cuarto".
        protected void RecentrarMirada()
        {
            _tieneRefCamara = false;
            if (!CabezaPorCamara) Cabeza.Recentrar();
        }

        // ¿Hay alguna pantalla UI Toolkit a la vista? Las IMGUI que quedan (CONTROL del mando)
        // no se dibujan mientras tanto, y si se llega a una estando en el visor se sale del
        // visor (todavía no saben dibujarse en estéreo). Ver NightMenuUI.OnGUI.
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
        // Tarjeta de una pantalla sobre la cámara. En el visor fijo a la cabeza se sube lo justo
        // para que su borde inferior quede en la zona nítida, y NO se centra en el foco: traer el
        // botón al centro taparía la mira / la guía de la imagen. null = menú: centra el foco.
        protected virtual VisualElement TarjetaVisor => null;
        // Última opción de un menú que conviene ver hacia arriba (mirar arriba en el visor cansa
        // menos que bajar la vista). En el visor fijo a la cabeza el menú se mantiene alto: esa
        // opción no baja de -ZonaNitida.PisoMenu. null = centrar siempre el foco.
        protected virtual VisualElement PisoVisor => null;
        // Visor fijo a la cabeza: cabecear mira arriba/abajo sobre la pantalla 1:1 (vertical quieto
        // en el cuarto, de costado sigue fija a la cabeza; sin elegir con la mirada, se elige con
        // el joystick). Así lo de abajo se lee inclinando la cabeza, con el ojo en el eje de la lente.
        protected virtual bool CabeceoVisor => false;

        // Deslizamiento vertical aplicado en el visor fijo a la cabeza (grados, + arriba): trae
        // lo de abajo a la zona nítida de la lente (ver ZonaNitida).
        private float _ajusteY;
        private bool  _ajusteSinSuavizar;
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

            _ps.SetScreenToPanelSpaceFunction(PantallaAPanel);

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
            if (_visible && CabeceoVisor && !EnElCuarto && !CabezaPorCamara) Cabeza.Habilitar(false);
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
                // Quieta en el cuarto: cada pantalla nueva aparece delante de donde mirás
                // (sin cuenta regresiva: ésa es sólo al empezar el modo).
                if (EnVisor && EnElCuarto && !CabezaPorCamara && Cabeza.EnUso && !Cabeza.EnCuentaRegresiva)
                    Cabeza.Recentrar();
                // Al aparecer, ignorar un instante la palanca y los toques: el mismo empuje o
                // toque que cerró la pantalla anterior caía sobre una opción de ésta (así se
                // armaba el loop SINCRONIZACIÓN → UBICAR EL ENTORNO → SINCRONIZACIÓN).
                BloqueoNav.TrasAccion();
                _toquesDesde = Time.unscaledTime + 0.35f;
                _dwell = 0f; _dwellOpcion = null;
                _ajusteY = 0f; _ajusteSinSuavizar = true;
            }
            else if (CabeceoVisor && !EnElCuarto && !CabezaPorCamara) Cabeza.Habilitar(false);
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
                    // Formato sRGB EXPLÍCITO en espacio Linear (el del proyecto): con ARGB32 "por
                    // defecto" en iOS/Metal el menú en el visor salía lavado, gris (conversión
                    // de color doble al pegar la textura en cada ojo); en Android no pasaba.
                    var formato = QualitySettings.activeColorSpace == ColorSpace.Linear
                        ? UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB
                        : UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
                    var desc = new RenderTextureDescriptor(AnchoRef, AltoVisor, formato, 24)
                    { useMipMap = false, msaaSamples = 1 };
                    _rt = new RenderTexture(desc) { name = GetType().Name + "_Visor" };
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
            _ajusteY = 0f; _ajusteSinSuavizar = true;
            if (Raiz != null) { MargenesSeguros(visor); AlCambiarVisor(visor); }
        }

        // Márgenes de arriba/abajo de las columnas de la pantalla (PiezasUI.Columna): aire base
        // más lo que tape el celular (notch / barra de estado, barra de gestos), en unidades
        // del panel (ancho de referencia AnchoRef). En el visor no hay nada que esquivar.
        private void MargenesSeguros(bool visor)
        {
            float k = Screen.width > 0 ? AnchoRef / (float)Screen.width : 1f;
            var seguro = Screen.safeArea;
            float arriba = visor ? 0f : Mathf.Max(0f, Screen.height - seguro.yMax) * k;
            float abajo  = visor ? 0f : Mathf.Max(0f, seguro.yMin) * k;
            Raiz.Query(className: PiezasUI.ClaseColumna).ForEach(c =>
            {
                c.style.paddingTop = PiezasUI.Arriba(arriba);
                c.style.paddingBottom = PiezasUI.AireAbajo + abajo;
            });
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

            if (EnVisor) { TickMirada(dt, MenuNavegable.MandoEnUso); TickToquesVisor(); }
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
            ActualizarAjusteVisor(dt);
        }

        // Con el visor fijo a la cabeza lo de abajo no se puede mirar de frente: cae fuera de
        // la zona nítida de la lente. Las tarjetas sobre la cámara suben hasta que su borde
        // inferior entra; los menús se deslizan para que la opción enfocada quede en el centro
        // de la lente. El objetivo se calcula desde la posición base (sin _ajusteY): no se realimenta.
        private void ActualizarAjusteVisor(float dt)
        {
            if (!EnVisor || EnElCuarto) { _ajusteY = 0f; return; }
            float w = Raiz.layout.width, h = Raiz.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h)) return;
            float upg = w / AnguloPanel;   // unidades de panel por grado
            float baseY = PosicionVisor.y;

            float objetivo;
            var tarjeta = TarjetaVisor;
            if (tarjeta != null)
            {
                if (!TieneMedidas(tarjeta)) return;
                float borde = baseY + (h * 0.5f - tarjeta.worldBound.yMax) / upg;
                objetivo = ZonaNitida.SubirTarjeta(borde);
            }
            else
            {
                var foco = Nav.Enfocada?.Elemento;
                if (foco == null || !TieneMedidas(foco)) return;
                float centro = baseY + (h * 0.5f - foco.worldBound.center.y) / upg;
                objetivo = -centro;
                // Menú que prefiere quedar arriba: su última opción nunca cae más abajo de
                // -ZonaNitida.PisoMenu, y el foco sólo lo desliza hacia arriba de ahí (enfocar
                // lo de arriba no baja el menú dejando todo bajo la vista).
                var piso = PisoVisor;
                if (piso != null && TieneMedidas(piso))
                {
                    float centroPiso = baseY + (h * 0.5f - piso.worldBound.center.y) / upg;
                    objetivo = Mathf.Max(objetivo, -ZonaNitida.PisoMenu - centroPiso);
                }
            }

            if (_ajusteSinSuavizar) { _ajusteY = objetivo; _ajusteSinSuavizar = false; }
            else _ajusteY = Mathf.Lerp(_ajusteY, objetivo, 1f - Mathf.Exp(-ZonaNitida.Velocidad * dt));
        }

        private static bool TieneMedidas(VisualElement e) => e.layout.height > 0f && !float.IsNaN(e.layout.height);

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

        // Pantalla → panel para los eventos propios de UI Toolkit (p. ej. arrastrar una lista).
        // Fuera del visor, identidad (el panel es la pantalla, como por defecto). En el visor el
        // panel se dibuja en una textura pegada en cada ojo: sin esto UI Toolkit tomaba el dedo
        // tal cual sobre la textura y el arrastre andaba sólo sobre el ojo izquierdo, de
        // casualidad. Se traduce desde el rect donde quedó dibujado en cualquiera de los dos
        // ojos; fuera de ambos, desde el punto más cercano del rect más cercano (si se
        // descartara, soltar el dedo afuera no le llegaba a la lista y quedaba "arrastrando",
        // corrida). Origen arriba, como OnGUI.
        private Vector2 PantallaAPanel(Vector2 p)
        {
            var rt = _ps != null ? _ps.targetTexture : null;
            if (rt == null) return p;
            if (!_hayRectOjo) return new Vector2(float.NaN, float.NaN);
            int mejor = -1;
            float mejorD = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                var r = _rectOjo[i];
                if (r.width <= 0f || r.height <= 0f) continue;
                var q = new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
                float d = (q - p).sqrMagnitude;
                if (d < mejorD) { mejorD = d; mejor = i; }
            }
            if (mejor < 0) return new Vector2(float.NaN, float.NaN);
            var e = _rectOjo[mejor];
            float x = Mathf.Clamp(p.x, e.xMin, e.xMax), y = Mathf.Clamp(p.y, e.yMin, e.yMax);
            return new Vector2((x - e.x) / e.width * rt.width, (y - e.y) / e.height * rt.height);
        }

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
        private const float FovOjo      = Cabeza.MitadVista * 2f;

        // Dos escalas (píxeles de pantalla por grado):
        //  - Base: la de siempre (90° por ojo). Define el TAMAÑO de la pantalla flotante y su
        //    corrimiento fijo (PosicionVisor), así se ven igual que antes.
        //  - Mundo: cuánto se corre el cuarto en pantalla al girar la cabeza un grado. En la
        //    partida con Cardboard es la de la imagen de cámara (bastante menos que 90° por ojo,
        //    según la cámara y el zoom de calibración): con la base la pantalla se movía la
        //    mitad que el cuarto y parecía que te acompañaba un poco, y eso mareaba. En los
        //    menús no hay cámara: el fondo lo dibujamos nosotros y las dos coinciden.
        private static float PpdBase => Screen.width * 0.5f / FovOjo;
        private MRCardboardController _mr;

        private float PpdMundo()
        {
            if (CabezaPorCamara && MRCardboardController.Activo)
            {
                if (_mr == null) _mr = FindAnyObjectByType<MRCardboardController>();
                float p = _mr != null ? _mr.PixelesPorGrado() : 0f;
                if (p > 0f) return p;
            }
            return PpdBase;
        }
        // Cuánto hay que mirar una opción para elegirla. Menús (sensor del celular) más lento,
        // para no elegir de pasada; en partida (cámara AR) más ágil.
        private const float SegundosElegirMenu    = 2.0f;
        private const float SegundosElegirPartida = 1.1f;
        private float SegundosElegir => CabezaPorCamara ? SegundosElegirPartida : SegundosElegirMenu;
        private float _dwell, _frio;
        private MenuNavegable.Opcion _dwellOpcion;

        // Opción (GameOptions.MenusEnElCuarto): ¿la pantalla queda quieta en el cuarto y se
        // elige mirando, o fija delante de cada ojo (sigue a la cabeza) y se usa con el
        // joystick? Fija es el default por ahora (visor + joystick).
        private static bool EnElCuarto => GameOptions.MenusEnElCuarto;
        private bool _modoCuartoAplicado;

        private bool EnCuentaRegresiva => EnElCuarto && !CabezaPorCamara && Cabeza.EnCuentaRegresiva;

        // Margen extra (grados) antes de que la pantalla empiece a seguirte: el borde de lo que
        // se ve por las lentes no es exacto, y así nunca se la ve "arrastrarse".
        private const float MargenArrastre = 8f;

        // Hasta qué yaw la pantalla todavía se ve, para cada lado. Cada ojo se dibuja recortado
        // a su mitad de pantalla, pero su centro (calibración, OjosVisor) no siempre cae en el
        // medio de esa mitad: hacia un costado se ve más de 45°. Se toma lo más que se vea en
        // cualquiera de los dos ojos, más media pantalla y el margen.
        private void LimitesArrastre(out float limitePos, out float limiteNeg)
        {
            OjosVisor.Centros(out var izq, out var der);
            float ojoW = Screen.width * 0.5f;
            float ppd = PpdMundo();
            float haciaIzq = Mathf.Max(izq.x, der.x - ojoW) / ppd;
            float haciaDer = Mathf.Max(ojoW - izq.x, Screen.width - der.x) / ppd;
            // Media pantalla en grados del mundo (su tamaño en pantalla va en la escala base).
            float medio = AnguloPanel * PpdBase / ppd * 0.5f + MargenArrastre;
            limitePos = haciaIzq + medio;   // yaw > 0: la pantalla queda a la izquierda
            limiteNeg = haciaDer + medio;
        }

        private void LeerCabeza(float dt)
        {
            LimitesArrastre(out float limPos, out float limNeg);
            if (!CabezaPorCamara)
            {
                Cabeza.Tick(dt, limPos, limNeg);
                _yaw = Cabeza.Yaw; _pitch = Cabeza.Pitch;
                return;
            }
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 f = cam.transform.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            // Rumbo de cuando apareció; altura respecto del horizonte (la cámara AR tiene Y
            // vertical): la pantalla queda a la altura de los ojos aunque al aparecer estuvieras
            // mirando el piso.
            if (!_tieneRefCamara) { _refYaw = yaw; _refPitch = 0f; _tieneRefCamara = true; }
            _yaw = Mathf.DeltaAngle(_refYaw, yaw);
            _pitch = pitch - _refPitch;
            // Quieta mientras se ve; entera fuera de la vista, pegada al borde de afuera (antes
            // saltaba de golpe adelante al darse vuelta). Ver Cabeza.ArrastrarAlBorde.
            _yaw = Cabeza.ArrastrarAlBorde(_yaw, limPos, limNeg, ref _refYaw);
        }

        private void TickMirada(float dt, bool mandoEnUso)
        {
            // Cambio de modo (al entrar al visor o si se tocó la opción): preparar o soltar
            // el sensor de orientación de los menús.
            if (EnElCuarto != _modoCuartoAplicado)
            {
                _modoCuartoAplicado = EnElCuarto;
                _tieneRefCamara = false;
                if (!CabezaPorCamara)
                {
                    // La cuenta regresiva va sólo al EMPEZAR el modo (prender el visor o la
                    // opción), no cada vez que aparece una pantalla nueva que todavía no lo
                    // había aplicado (pasaba al ir a OPCIONES).
                    if (EnElCuarto && !Cabeza.EnUso)
                    {
                        Cabeza.Recentrar(conCuentaRegresiva: !Cabeza.YaAcomodado);
                        Cabeza.Habilitar(true);
                    }
                    else if (!EnElCuarto) Cabeza.Habilitar(false);
                }
            }

            _hayPuntoMirada = false;
            // Fija delante de cada ojo: sin cabeza ni anillo; se usa con el joystick.
            if (!EnElCuarto)
            {
                _yaw = 0f;
                _pitch = 0f;
                if (CabeceoVisor && !CabezaPorCamara)
                {
                    if (!Cabeza.EnUso) { Cabeza.Recentrar(); Cabeza.Habilitar(true); }
                    LimitesArrastre(out float limPos, out float limNeg);
                    Cabeza.Tick(dt, limPos, limNeg);
                    _pitch = Cabeza.Pitch;
                }
                _dwell = 0f; _dwellOpcion = null;
                return;
            }

            LeerCabeza(dt);
            _frio -= dt;
            // Mientras se usa el mando elige el mando: la mirada no llena el anillo (si no, al
            // apuntar con la cabeza mientras navegás con la palanca se elegían cosas solas).
            // Sólo si se usó hace poco (MenuNavegable.MandoEnUso), no por estar conectado.
            if (EnCuentaRegresiva || mandoEnUso) { _dwell = 0f; _dwellOpcion = null; return; }

            var panel = Raiz.panel;
            if (panel == null) return;

            // Punto del panel bajo el centro de la vista.
            float w = Raiz.layout.width, h = Raiz.layout.height;
            if (w <= 0f || h <= 0f) return;
            // Centro de la vista → panel, con las mismas escalas que el dibujo (OnGUI): el giro
            // en la escala del mundo y el corrimiento fijo en la base; unidades de panel por
            // píxel de pantalla = w / ancho dibujado.
            float ppdM = PpdMundo(), ppdB = PpdBase;
            float uPorPx = w / (AnguloPanel * ppdB);
            Vector2 off = PosicionVisor + new Vector2(0f, _ajusteY);
            Vector2 p = new(w * 0.5f + (_yaw * ppdM - off.x * ppdB) * uPorPx,
                            h * 0.5f - (_pitch * ppdM - off.y * ppdB) * uPorPx);
            _puntoMirada = p;
            _hayPuntoMirada = true;

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
            float ppd = PpdMundo();      // píxeles de pantalla por grado de giro (ver PpdBase)
            float ppdB = PpdBase;
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
                float pw = AnguloPanel * ppdB, ph = pw * _rt.height / _rt.width;
                const float disp = 0f;
                Vector2 off = PosicionVisor + new Vector2(0f, _ajusteY);
                var pc = c + new Vector2(-_yaw * ppd + off.x * ppdB + disp, _pitch * ppd - off.y * ppdB);
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
                // Mientras se acomoda el visor: cartel centrado (tarjeta oscura, para que el
                // texto no se pierda sobre lo que hay detrás), sin interacción.
                GUI.color = Color.white;
                var r = new Rect(c.x - 280 * s, c.y - 95 * s, 560 * s, 190 * s);
                MortuoriumTheme.Tarjeta2(r, MortuoriumTheme.Hoja2, MortuoriumTheme.Tan, 28f * s, 2f * s);
                var stTit = MortuoriumTheme.Estilo(MortuoriumTheme.FBebas, Mathf.RoundToInt(34 * s),
                                                   TemaUI.Cream, TextAnchor.MiddleCenter);
                var stNum = MortuoriumTheme.Estilo(MortuoriumTheme.FBebas, Mathf.RoundToInt(64 * s),
                                                   TemaUI.Tan, TextAnchor.MiddleCenter);
                GUI.Label(new Rect(r.x, r.y + 18 * s, r.width, 50 * s), "PONÉ EL CELULAR EN EL VISOR", stTit);
                GUI.Label(new Rect(r.x, r.y + 76 * s, r.width, 90 * s),
                          Mathf.CeilToInt(Cabeza.CuentaRegresiva).ToString(), stNum);
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
