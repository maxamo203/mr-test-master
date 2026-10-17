using Gamepad;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // PUNTOS DE ANCLAJE (colocar anclas extra contra la deriva) del Rediseño 2.0, usable con
    // el visor: la mira va en el centro de cada ojo (ahí apunta el raycast de la cámara) y los
    // botones se eligen con el mando o tocándolos. Misma lógica que la pantalla vieja de
    // ARLobbyUI (AnchorPointManager / ARLobbyManager).
    //
    // Visor SIN joystick (menús quietos en el cuarto, se elige mirando): no hay botón COLOCAR
    // ANCLA — para "apretarlo" había que mirarlo, y así se dejaba de apuntar adonde iba el
    // ancla. En cambio: quedarse quieto apuntando a un punto ~1,5 s llena un anillo
    // ("CONFIRMANDO ANCLA"); al llenarse aparece el ancla semitransparente en ese punto y el
    // cartel "¿CONFIRMAR EL ANCLA?" con SÍ / NO, que se eligen mirando.
    public class PanelAnclas2 : PantallaUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static PanelAnclas2 _instancia;
        public static bool Mostrando => _instancia != null && _instancia.EsVisible();

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaJuego || _instancia != null) return;
            new GameObject("PanelAnclas2").AddComponent<PanelAnclas2>();
        }

        private const float SegundosApuntar = 1.5f;
        private const float ToleranciaQuieto = 0.08f;   // metros que puede moverse el punto apuntado

        private ARLobbyManager _lobby;
        private VisualElement _tarjeta;

        // Visor sin joystick: apuntar quieto → vista previa → confirmar.
        private float _carga;              // 0..1 del anillo
        private Vector3 _puntoInicio;
        private bool _hayInicio;
        private string _motivo;            // por qué no se puede poner acá (muy cerca, etc.)
        private bool _confirmando;
        private Scanner.ResolvedHit _hitFijo;
        private Quaternion _rotFija;
        private float _frioApuntar;
        private GameObject _fantasma;
        private Label _estado, _calidad, _error;
        private string _textoError;
        private string _firma;

        protected override void Awake()
        {
            if (_instancia != null && _instancia != this) { Destroy(gameObject); return; }
            _instancia = this;
            base.Awake();
        }

        protected override bool EsVisible()
        {
            if (!Flag.Activo) return false;
            if (_lobby == null) _lobby = ARLobbyManager.Instance;
            if (_lobby == null || PauseMenuController.IsOpen) return false;
            return _lobby.State == ARLobbyManager.LobbyState.PlacingAnchors && AnchorPointManager.Instance != null;
        }

        protected override bool EnVisor => MRCardboardController.Activo;
        protected override bool CabezaPorCamara => true;
        protected override Color VeloVisor => Color.clear;
        protected override float AnguloPanel => 32f;
        // Sin joystick, mientras se busca dónde poner el ancla, la tarjeta va más abajo para que
        // no tape lo que se apunta; el cartel de confirmar, debajo de la vista previa.
        protected override Vector2 PosicionVisor =>
            !ModoMirada ? new(0f, -12f) : _confirmando ? new(0f, -24f) : new(0f, -30f);

        // Visor sin joystick: menús quietos en el cuarto y nada de mando (se elige mirando).
        private bool ModoMirada =>
            EnVisor && GameOptions.MenusEnElCuarto &&
            !(GamepadManager.Instance != null && GamepadManager.Instance.IsConnected);

        protected override void AlCambiarVisor(bool visor)
        {
            if (_tarjeta == null) return;
            Raiz.style.justifyContent = visor ? Justify.Center : Justify.FlexEnd;
            _tarjeta.style.borderBottomLeftRadius = visor ? 40 : 0;
            _tarjeta.style.borderBottomRightRadius = visor ? 40 : 0;
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            raiz.style.justifyContent = Justify.FlexEnd;
            _tarjeta = PiezasUI.Ignorar(new VisualElement());
            _tarjeta.style.backgroundColor = new Color(0.047f, 0.039f, 0.035f, 0.9f);
            TemaUI.Radio(_tarjeta, 40);
            _tarjeta.style.borderBottomLeftRadius = 0; _tarjeta.style.borderBottomRightRadius = 0;
            TemaUI.Borde(_tarjeta, 2, TemaUI.Linea);
            TemaUI.Relleno(_tarjeta, 36, 44);
            raiz.Add(_tarjeta);
        }

        // Los botones se habilitan según cuántas anclas hay: se rearman sólo si eso cambia.
        protected override void Animar(float dt)
        {
            var mgr = AnchorPointManager.Instance;
            if (mgr == null) return;
            bool mirada = ModoMirada;
            if (!mirada && _confirmando) CancelarConfirmacion();
            string firma = $"{mgr.PuedeColocar}|{mgr.Count > 0}|{mgr.PuedeCerrar}|{mirada}|{_confirmando}";
            if (firma != _firma) { _firma = firma; Armar(mgr); }
            if (mirada) TickApuntar(mgr, dt);
            else _carga = 0f;
            if (_fantasma != null)
            {
                // Late un poco: es una vista previa, todavía no un ancla.
                float k = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
                _fantasma.transform.localScale = Vector3.one * (0.09f * k);
            }
            if (_confirmando) return;   // el cartel no tiene estado ni calidad

            _estado.text = mgr.Count < AnchorPointManager.MinAnclas
                ? $"Anclas: {mgr.Count} / {AnchorPointManager.MaxAnclas} · faltan {AnchorPointManager.MinAnclas - mgr.Count}"
                : $"Anclas: {mgr.Count} / {AnchorPointManager.MaxAnclas}";
            _estado.style.color = mgr.PuedeCerrar ? TemaUI.Green : TemaUI.Tan;
            var q = AnchorQuality.Instance;
            _calidad.text = q != null ? $"Punto apuntado: {q.Etiqueta}" : "";
            if (q != null) _calidad.style.color = q.Color;
            bool hay = !string.IsNullOrEmpty(_textoError);
            _error.text = hay ? _textoError : "";
            _error.style.display = hay ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Armar(AnchorPointManager mgr)
        {
            _tarjeta.Clear();
            Nav.Limpiar();
            if (ModoMirada && _confirmando) { ArmarConfirmacion(); return; }
            bool mirada = ModoMirada;
            _tarjeta.Add(TemaUI.Texto("PUNTOS DE ANCLAJE", TemaUI.Bebas, 64, TemaUI.Cream, 6f));
            var ayuda = TemaUI.Texto(mirada
                ? "Apuntá con la mira a una pared o mueble y quedate quieto un momento para poner un ancla. " +
                  "Cuantas más y más repartidas, menos se corre el mapa."
                : "Recorré tu cuarto y colocá anclas apuntando con la mira a paredes o muebles. " +
                  "Cuantas más y más repartidas, menos se corre el mapa.",
                TemaUI.Mono, 24, TemaUI.Muted);
            ayuda.style.marginTop = 8; ayuda.style.marginBottom = 12;
            _tarjeta.Add(ayuda);
            _estado = TemaUI.Texto("", TemaUI.Mono, 30, TemaUI.Tan);
            _tarjeta.Add(_estado);
            _calidad = TemaUI.Texto("", TemaUI.Mono, 26, TemaUI.Muted);
            _calidad.style.marginTop = 4;
            _tarjeta.Add(_calidad);
            _error = TemaUI.Texto("", TemaUI.Mono, 24, TemaUI.Red);
            _error.style.display = DisplayStyle.None;
            _tarjeta.Add(_error);

            var botones = PiezasUI.Ignorar(new VisualElement());
            botones.style.marginTop = 20;
            if (!mirada)
            {
                var colocar = PiezasUI.Boton(Nav, "COLOCAR ANCLA", () =>
                {
                    var m = AnchorPointManager.Instance;
                    if (m == null) return;
                    _textoError = m.TryColocar(out var err) ? null : err;
                    _lobby?.ReportarAnclas();
                }, habilitado: mgr.PuedeColocar);
                colocar.style.backgroundImage = TemaUI.DegradeRojo;
                colocar.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                botones.Add(colocar);
            }
            botones.Add(PiezasUI.Boton(Nav, "DESHACER", () =>
            {
                AnchorPointManager.Instance?.DeshacerUltima();
                _textoError = null;
                _lobby?.ReportarAnclas();
            }, habilitado: mgr.Count > 0));
            botones.Add(PiezasUI.Boton(Nav, "LISTO", () =>
            {
                AnchorPointManager.Instance?.MarcarListo();
                _textoError = null;
                _lobby?.AnchorPlacementDone();
            }, habilitado: mgr.PuedeCerrar));
            botones.Add(PiezasUI.Boton(Nav, "OMITIR ANCLAS", () =>
            {
                AnchorPointManager.Instance?.Omitir();
                _textoError = null;
                _lobby?.AnchorPlacementDone();
            }));
            _tarjeta.Add(botones);
        }

        // ── Visor sin joystick: apuntar quieto → vista previa → confirmar ─────────
        private void TickApuntar(AnchorPointManager mgr, float dt)
        {
            _frioApuntar -= dt;
            if (_confirmando || !mgr.PuedeColocar || _frioApuntar > 0f || MirandoLaTarjeta())
            {
                _carga = 0f; _hayInicio = false; _motivo = null;
                return;
            }
            var hit = mgr.UltimoHit;
            bool valido = mgr.ValidarPunto(hit, out _motivo);
            if (!hit.Hit) _motivo = null;   // no apunta a nada: sin cartel
            if (!valido || !_hayInicio || (hit.Position - _puntoInicio).sqrMagnitude > ToleranciaQuieto * ToleranciaQuieto)
            {
                // Se movió (o el punto no sirve): el anillo arranca de nuevo desde acá.
                _puntoInicio = hit.Position;
                _hayInicio = valido;
                _carga = 0f;
                return;
            }
            _carga += dt / SegundosApuntar;
            if (_carga < 1f) return;

            _carga = 0f;
            _hayInicio = false;
            _hitFijo = hit;
            _rotFija = mgr.RumboCamara();
            _confirmando = true;
            _textoError = null;
            CrearFantasma(hit.Position);
            RecentrarMirada();   // el cartel aparece delante, debajo de la vista previa
            AudioManager.Sonar(c => c.uiConfirmar);
        }

        // ¿La mira está sobre la tarjeta? (mirando LISTO / DESHACER no se apunta a una pared).
        private bool MirandoLaTarjeta()
        {
            if (!PuntoMirada(out var p) || _tarjeta == null) return false;
            return _tarjeta.worldBound.Contains(p);
        }

        private void ArmarConfirmacion()
        {
            _tarjeta.Add(TemaUI.Texto("¿CONFIRMAR EL ANCLA?", TemaUI.Bebas, 64, TemaUI.Cream, 6f));
            var ayuda = TemaUI.Texto("La esfera marca dónde va a quedar. Mirá SÍ para dejarla o NO para buscar otro punto.",
                                     TemaUI.Mono, 24, TemaUI.Muted);
            ayuda.style.marginTop = 8; ayuda.style.marginBottom = 16;
            _tarjeta.Add(ayuda);
            var si = PiezasUI.Boton(Nav, "SÍ, PONER EL ANCLA", () =>
            {
                var m = AnchorPointManager.Instance;
                if (m != null) _textoError = m.TryColocar(_hitFijo, _rotFija, out var err) ? null : err;
                _lobby?.ReportarAnclas();
                CancelarConfirmacion();
            });
            si.style.backgroundImage = TemaUI.DegradeRojo;
            si.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            _tarjeta.Add(si);
            _tarjeta.Add(PiezasUI.Boton(Nav, "NO", CancelarConfirmacion));
        }

        private void CancelarConfirmacion()
        {
            _confirmando = false;
            _frioApuntar = 0.8f;   // que no arranque a cargar de nuevo apenas se levanta la vista
            if (_fantasma != null) { Destroy(_fantasma); _fantasma = null; }
        }

        private void CrearFantasma(Vector3 pos)
        {
            if (_fantasma != null) Destroy(_fantasma);
            var malla = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var shader = Resources.Load<Shader>("Rediseno/MarcadorFantasma") ?? Shader.Find("Rediseno/MarcadorFantasma");
            if (malla == null || shader == null)
            {
                // Sin el shader transparente: la esfera opaca de siempre (mejor que nada).
                _fantasma = AnchorVisuals.MakeSphere(null, pos, 0.09f, new Color(0.9f, 0.85f, 0.75f));
                return;
            }
            _fantasma = new GameObject("AnclaVistaPrevia");
            _fantasma.transform.position = pos;
            _fantasma.transform.localScale = Vector3.one * 0.09f;
            _fantasma.AddComponent<MeshFilter>().sharedMesh = malla;
            if (_matFantasma == null)
                _matFantasma = new Material(shader) { name = "AnclaVistaPrevia (runtime)", color = new Color(0.91f, 0.86f, 0.78f, 0.45f) };
            _fantasma.AddComponent<MeshRenderer>().sharedMaterial = _matFantasma;
        }

        private static Material _matFantasma;

        protected override void Update()
        {
            base.Update();
            // Si la pantalla se cierra (LISTO, pausa, se pasó a otro estado) no puede quedar la
            // vista previa flotando en el cuarto.
            if (_confirmando && !EsVisible()) CancelarConfirmacion();
        }

        protected override void OnDestroy()
        {
            if (_fantasma != null) Destroy(_fantasma);
            if (_instancia == this) _instancia = null;
            base.OnDestroy();
        }

        // Mira (la cámara tira el raycast por su centro): en cada ojo con el visor.
        protected override void DibujarEnOjo(Vector2 c, float ppd) => Mira(c, Screen.height / 1080f);

        protected override void OnGUI()
        {
            base.OnGUI();
            if (EnVisor && EsVisible() && Event.current.type == EventType.Repaint && ModoMirada && !_confirmando)
                DibujarApuntar();
            if (EnVisor || !EsVisible() || Event.current.type != EventType.Repaint) return;
            var prevM = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Mira(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), Screen.height / 1080f);
            GUI.matrix = prevM;
        }

        // Anillo "CONFIRMANDO ANCLA" alrededor de la mira (en cada ojo) mientras se apunta
        // quieto, o el motivo si ese punto no sirve.
        private void DibujarApuntar()
        {
            string texto = !string.IsNullOrEmpty(_motivo) ? _motivo.ToUpperInvariant()
                         : _carga > 0.02f ? "CONFIRMANDO ANCLA" : null;
            if (texto == null) return;
            if (_carga > 0.02f) Rediseno.AnilloHud.Dibujar(_carga, TemaUI.Tan, null);
            var prevM = GUI.matrix; var prevC = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            float s = Screen.height / 1080f;
            var st = MortuoriumTheme.Estilo(MortuoriumTheme.FBebas, Mathf.RoundToInt(30 * s),
                                            string.IsNullOrEmpty(_motivo) ? TemaUI.Cream : TemaUI.Red, TextAnchor.MiddleCenter);
            OjosVisor.Centros(out var izq, out var der);
            foreach (var c in new[] { izq, der })
                GUI.Label(new Rect(c.x - 220f * s, c.y + 44f * s, 440f * s, 40f * s), texto, st);
            GUI.matrix = prevM; GUI.color = prevC;
        }

        private static void Mira(Vector2 c, float s)
        {
            var q = AnchorQuality.Instance;
            float r = 22f * s, g = 7f * s, w = 2.5f * s;
            var prev = GUI.color;
            GUI.color = q != null ? q.Color : TemaUI.Cream;
            var t = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(c.x - r, c.y - w * 0.5f, r - g, w), t);
            GUI.DrawTexture(new Rect(c.x + g, c.y - w * 0.5f, r - g, w), t);
            GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y - r, w, r - g), t);
            GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y + g, w, r - g), t);
            GUI.color = prev;
        }
    }
}
