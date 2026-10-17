using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Menú principal del Rediseño 2.0 (UI Toolkit). Reemplaza SOLO la pantalla raíz de
    // NightMenuUI cuando el flag está prendido; las demás pantallas (noches, escenarios,
    // opciones…) siguen siendo las IMGUI de siempre hasta que se migren. Si el jugador
    // está en el visor y elige algo que lleva a una pantalla vieja, se sale del visor
    // (esas pantallas todavía no se dibujan en estéreo).
    //
    // Se crea solo en NightMenuScene (RuntimeInitializeOnLoadMethod + sceneLoaded): no
    // hay que tocar la escena.
    public class MenuPrincipal2 : PantallaUI
    {
        public static MenuPrincipal2 Instance { get; private set; }

        // ¿Lo está dibujando este menú? NightMenuUI no dibuja su pantalla raíz cuando sí.
        public static bool Mostrando => Instance != null && Instance.EsVisible();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaMenu || Instance != null) return;
            new GameObject("MenuPrincipal2").AddComponent<MenuPrincipal2>();
        }

        private Texture2D _fondoA, _fondoB;
        private VisualElement _capaA, _capaB, _puntoRec;
        private Label _rec, _ctaTitulo, _ctaSub;
        private VisualElement _iconoVisor;
        private float _t;
        private int _idxContinuar;

        protected override void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            base.Awake();
            _fondoA = Resources.Load<Texture2D>("Rediseno/fondo_sorken");
            _fondoB = Resources.Load<Texture2D>("Rediseno/fondo_arbmos");
        }

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // Al irse del menú (a una partida o al escáner) sin pasar por la pantalla vieja.
            base.OnDestroy();
        }

        protected override bool EsVisible()
        {
            var menu = NightMenuUI.Instance;
            return Flag.Activo && menu != null && menu.MostrandoPrincipal;
        }

        protected override Texture FondoVisor => _fondoA;

        protected override void AlCambiarVisor(bool visor)
        {
            // En el visor el fondo lo dibuja PantallaUI detrás, con profundidad.
            DisplayStyle d = visor ? DisplayStyle.None : DisplayStyle.Flex;
            if (_capaA != null) _capaA.style.display = d;
            if (_capaB != null) _capaB.style.display = d;
            if (_iconoVisor != null)
                _iconoVisor.style.backgroundImage = visor ? IconosMenu.Celular : IconosMenu.Visor;
        }

        // ── Construcción ──────────────────────────────────────────────────────
        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            raiz.style.backgroundColor = Color.clear;

            // Fondo animado: dos capas del concept art que se funden, con paneo lento.
            _capaA = Capa(_fondoA);
            _capaB = Capa(_fondoB);
            _capaB.style.opacity = 0f;
            raiz.Add(_capaA);
            raiz.Add(_capaB);
            var sombra = new VisualElement { pickingMode = PickingMode.Ignore };
            Llenar(sombra);
            sombra.style.backgroundImage = TemaUI.SombraAbajo;
            sombra.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            raiz.Add(sombra);

            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            Llenar(col);
            col.style.flexDirection = FlexDirection.Column;
            col.style.paddingLeft = 48; col.style.paddingRight = 48;
            col.style.paddingTop = 56; col.style.paddingBottom = 40;
            raiz.Add(col);

            // Barra de arriba: REC + visor + opciones.
            var barra = Fila();
            barra.style.justifyContent = Justify.SpaceBetween;
            var rec = Fila();
            _puntoRec = new VisualElement { pickingMode = PickingMode.Ignore };
            _puntoRec.style.width = 18; _puntoRec.style.height = 18;
            TemaUI.Radio(_puntoRec, 9);
            _puntoRec.style.backgroundColor = TemaUI.Red;
            _puntoRec.style.marginRight = 16;
            rec.Add(_puntoRec);
            _rec = TemaUI.Texto("REC", TemaUI.Mono, 30, TemaUI.Muted, 6f);
            rec.Add(_rec);
            barra.Add(rec);

            var iconos = Fila();
            var bVisor = BotonRedondo(IconosMenu.Visor, out _iconoVisor);
            bVisor.style.marginRight = 28;
            iconos.Add(bVisor);
            var bOpc = BotonRedondo(IconosMenu.Engranaje, out _);
            iconos.Add(bOpc);
            barra.Add(iconos);
            col.Add(barra);

            col.Add(Espacio(1f));

            // Logo + frase.
            var logoTex = MortuoriumTheme.LogoMortuorium;
            var logo = new VisualElement { pickingMode = PickingMode.Ignore };
            logo.style.alignSelf = Align.Center;
            logo.style.width = 900;
            logo.style.height = logoTex != null ? 900f * logoTex.height / Mathf.Max(1, logoTex.width) : 300;
            logo.style.backgroundImage = logoTex;
            logo.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            col.Add(logo);
            var frase = TemaUI.Texto("El ritual no debe parar", TemaUI.Elite, 44, TemaUI.Muted, 0f, TextAnchor.MiddleCenter);
            frase.style.marginTop = 10;
            col.Add(frase);

            col.Add(Espacio(1.3f));

            // Visor y opciones: arriba en pantalla, así que son el primer escalón de la pila
            // (↑ desde CONTINUAR llega acá; ↑ desde acá da la vuelta al último botón de abajo).
            // Están uno al lado del otro: ←/→ pasan de uno al otro y ↑↓ salen de la fila.
            nav.FilaHorizontal(nav.Agregar(bVisor, () => ModoVisor.Set(!ModoVisor.Activo)),
                               nav.Agregar(bOpc, () => Abrir(m => m.AbrirOpciones())));

            // Acciones. El orden de agregado es el orden de navegación (↑↓).
            var cta = new VisualElement();
            TemaUI.Radio(cta, 42);
            TemaUI.Relleno(cta, 34, 46);
            TemaUI.Borde(cta, 2, new Color(1f, 1f, 1f, 0.10f));
            cta.style.backgroundImage = TemaUI.DegradeRojo;
            cta.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            cta.style.marginBottom = 26;
            _ctaTitulo = TemaUI.Texto("CONTINUAR", TemaUI.Bebas, 70, TemaUI.Cream, 7f);
            _ctaSub = TemaUI.Texto("", TemaUI.Mono, 28, new Color(TemaUI.Cream.r, TemaUI.Cream.g, TemaUI.Cream.b, 0.82f));
            _ctaSub.style.marginTop = 6;
            cta.Add(_ctaTitulo);
            cta.Add(_ctaSub);
            col.Add(cta);
            // El foco arranca en CONTINUAR, no en la fila de arriba.
            nav.Enfocar(nav.Agregar(cta, () => Abrir(m => m.AbrirContinuar(_idxContinuar))));

            if (BuildVariant.Multijugador)
            {
                var bOtros = Secundario(IconosMenu.Otros, "JUGAR CON OTROS", "Misma red wifi");
                col.Add(bOtros);
                nav.Agregar(bOtros, () => Abrir(m => m.AbrirMultijugador()));
            }
            var bEsc = Secundario(IconosMenu.Escenarios, "MIS ESCENARIOS", "Escanear o editar");
            col.Add(bEsc);
            nav.Agregar(bEsc, () => Abrir(m => m.AbrirEscenarios()));

            var bTuto = Secundario(IconosMenu.Libro, "TUTORIAL", "Próximamente");
            bTuto.style.opacity = 0.5f;
            col.Add(bTuto);
            nav.Agregar(bTuto, null, habilitada: false);


            var pie = TemaUI.Texto(BuildVariant.EsDemo ? "DEMO · equipo 112" : "Build interna · equipo 112",
                                   TemaUI.Mono, 24, TemaUI.Dim, 2f, TextAnchor.MiddleCenter);
            pie.style.marginTop = 18;
            col.Add(pie);

            ActualizarTextos();
        }

        // Pasa a otra pantalla de NightMenuUI. Si esa pantalla ya está migrada (UI Toolkit)
        // el visor sigue; si todavía es vieja (IMGUI), NightMenuUI sale del visor solo.
        private void Abrir(System.Action<NightMenuUI> accion)
        {
            var menu = NightMenuUI.Instance;
            if (menu != null) accion(menu);
        }

        private void ActualizarTextos()
        {
            var menu = NightMenuUI.Instance;
            int total = menu != null ? menu.TotalNoches : 6;
            int idx = Mathf.Clamp(NightProgress.Desbloqueadas - 1, 0, Mathf.Max(0, total - 1));
            while (idx > 0 && !BuildVariant.NocheHabilitada(idx)) idx--;
            int n = idx + 1;
            _idxContinuar = idx;

            _rec.text = $"REC · NOCHE {n}";
            _ctaTitulo.text = $"CONTINUAR · NOCHE {n}";
            int record = CollectibleProgress.Record(idx);
            _ctaSub.text = record > 0 ? $"Mejor marca: {record} reliquias"
                         : idx == 0 ? "El ritual empieza esta noche"
                         : "Todavía sin reliquias en esta noche";
        }

        // ── Animación ─────────────────────────────────────────────────────────
        protected override void Animar(float dt)
        {
            _t += dt;
            if (_puntoRec != null) _puntoRec.style.opacity = Mathf.Repeat(_t, 1.6f) < 0.8f ? 1f : 0.2f;
            if (ModoVisor.Activo || _capaA == null) return;

            // Paneo lento (26 s ida y vuelta) y fundido entre Sorken y Arbmos.
            float k = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(_t / 26f, 1f));
            _capaA.style.scale = new Scale(Vector3.one * Mathf.Lerp(1.04f, 1.16f, k));
            _capaA.style.translate = new Translate(Length.Percent(-3f * k), Length.Percent(2f * k));
            float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.PingPong(_t / 13f, 1f) - 0.45f) / 0.1f));
            _capaB.style.opacity = f;
            _capaB.style.scale = new Scale(Vector3.one * Mathf.Lerp(1.10f, 1.18f, k));
            _capaB.style.translate = new Translate(Length.Percent(2f - 4f * k), Length.Percent(-2f * k));

            // Si vuelve al menú desde otra pantalla, refrescar (se pudo ganar una noche).
            if (Time.frameCount % 30 == 0) ActualizarTextos();
        }

        // ── Piezas ────────────────────────────────────────────────────────────
        private static void Llenar(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
        }

        private static VisualElement Capa(Texture2D tex)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(-8); e.style.right = Length.Percent(-8);
            e.style.top = Length.Percent(-8); e.style.bottom = Length.Percent(-8);
            e.style.backgroundImage = tex;
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            // Oscuro y desaturado: es clima, no protagonista.
            e.style.unityBackgroundImageTintColor = new Color(0.55f, 0.52f, 0.5f, 1f);
            return e;
        }

        private static VisualElement Fila()
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            return e;
        }

        private static VisualElement Espacio(float peso)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.flexGrow = peso;
            return e;
        }

        private static VisualElement BotonRedondo(Texture2D icono, out VisualElement iconoEl)
        {
            var b = new VisualElement();
            b.style.width = 132; b.style.height = 132;
            TemaUI.Radio(b, 66);
            TemaUI.Borde(b, 2, new Color(1f, 1f, 1f, 0.25f));
            b.style.backgroundColor = new Color(0.024f, 0.02f, 0.016f, 0.45f);
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
            iconoEl = TemaUI.Icono(icono, 66, TemaUI.Cream);
            b.Add(iconoEl);
            return b;
        }

        private static VisualElement Secundario(Texture2D icono, string titulo, string sub)
        {
            var b = new VisualElement();
            b.style.flexDirection = FlexDirection.Row;
            b.style.alignItems = Align.Center;
            TemaUI.Radio(b, 34);
            TemaUI.Relleno(b, 24, 30);
            TemaUI.Borde(b, 2, TemaUI.Linea);
            b.style.backgroundColor = TemaUI.Panel;
            b.style.marginBottom = 20;

            var caja = new VisualElement { pickingMode = PickingMode.Ignore };
            caja.style.width = 78; caja.style.height = 78;
            TemaUI.Radio(caja, 22);
            caja.style.backgroundColor = new Color(TemaUI.Tan.r, TemaUI.Tan.g, TemaUI.Tan.b, 0.14f);
            caja.style.alignItems = Align.Center;
            caja.style.justifyContent = Justify.Center;
            caja.style.marginRight = 28;
            caja.Add(TemaUI.Icono(icono, 44, TemaUI.Tan));
            b.Add(caja);

            var textos = new VisualElement { pickingMode = PickingMode.Ignore };
            textos.style.flexShrink = 1;
            textos.Add(TemaUI.Texto(titulo, TemaUI.Bebas, 48, TemaUI.Cream, 5f));
            var s = TemaUI.Texto(sub, TemaUI.Mono, 26, TemaUI.Muted);
            s.style.marginTop = 2;
            textos.Add(s);
            b.Add(textos);
            return b;
        }
    }
}
