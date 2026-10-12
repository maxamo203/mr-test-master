using Gamepad;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // UBICAR EL ENTORNO (calibración a mano del 0,0) del Rediseño 2.0, usable con el visor.
    // La pantalla vieja sólo se manejaba arrastrando las flechas del gizmo con el dedo; acá
    // el mapa se mueve con filas ajustables: con la ruedita ↑↓ elegís la fila y ←→ ajustás
    // (con repetición); con la mirada o el dedo, los botones − / + de cada fila. El gizmo
    // sigue enganchado, así que sin visor también se puede arrastrar como antes.
    //
    // Los movimientos son relativos a hacia dónde mirás (izquierda/derecha, acercar/alejar)
    // y se aplican directo sobre WorldOrigin, igual que el gizmo: LISTO (Lobby) los fija.
    public class PanelUbicar2 : PantallaUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static PanelUbicar2 _instancia;
        public static bool Mostrando => _instancia != null && _instancia.EsVisible();

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaJuego || _instancia != null) return;
            new GameObject("PanelUbicar2").AddComponent<PanelUbicar2>();
        }

        private const float PasoMetros = 0.04f;
        private const float PasoGrados = 2f;

        private ARLobbyManager _lobby;
        private VisualElement _tarjeta;
        private Label _error;
        private string _textoError;

        protected override void Awake()
        {
            if (_instancia != null && _instancia != this) { Destroy(gameObject); return; }
            _instancia = this;
            base.Awake();
        }

        protected override void OnDestroy()
        {
            if (_instancia == this) _instancia = null;
            base.OnDestroy();
        }

        protected override bool EsVisible()
        {
            if (!Flag.Activo) return false;
            if (_lobby == null) _lobby = ARLobbyManager.Instance;
            if (_lobby == null || PauseMenuController.IsOpen) return false;
            return _lobby.State == ARLobbyManager.LobbyState.CalibrandoManual;
        }

        protected override bool EnVisor => MRCardboardController.Activo;
        protected override bool CabezaPorCamara => true;
        protected override Color VeloVisor => Color.clear;   // hay que ver el cuarto
        // Un poco por debajo de la vista y centrado en su lienzo: antes iba apoyado abajo
        // del lienzo y bajado 24°, y su borde inferior caía fuera de lo que ve cada ojo.
        protected override float AnguloPanel => 32f;
        protected override Vector2 PosicionVisor => new(0f, -12f);

        protected override void AlCambiarVisor(bool visor)
        {
            if (_tarjeta == null) return;
            // En pantalla: franja apoyada abajo. En el visor: tarjeta centrada en el lienzo.
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

            _tarjeta.Add(TemaUI.Texto("UBICAR EL ENTORNO", TemaUI.Bebas, 64, TemaUI.Cream, 6f));
            var ayuda = TemaUI.Texto("Mové el mapa hasta que las paredes virtuales coincidan con las reales. " +
                                     "Con la ruedita: arriba/abajo elegís qué ajustar, izquierda/derecha lo ajustás.",
                                     TemaUI.Mono, 24, TemaUI.Muted);
            ayuda.style.marginTop = 8; ayuda.style.marginBottom = 18;
            _tarjeta.Add(ayuda);

            Fila(nav, "IZQUIERDA / DERECHA", d => Mover(Lateral() * (d * PasoMetros)));
            Fila(nav, "ACERCAR / ALEJAR",    d => Mover(Frente() * (d * PasoMetros)));
            Fila(nav, "BAJAR / SUBIR",       d => Mover(Vector3.up * (d * PasoMetros)));
            Fila(nav, "GIRAR",               d => Girar(d * PasoGrados));

            _error = TemaUI.Texto("", TemaUI.Mono, 24, TemaUI.Red);
            _error.style.display = DisplayStyle.None;
            _tarjeta.Add(_error);

            // En vertical, como en todos los menús.
            var botones = PiezasUI.Ignorar(new VisualElement());
            botones.style.marginTop = 8;
            // LISTO primero (arriba y primero en el orden de la ruedita).
            var listo = PiezasUI.Boton(nav, "LISTO", () => { _textoError = null; _lobby?.CalibracionManualLista(); });
            var recentrar = PiezasUI.Boton(nav, "RECENTRAR ACÁ", () => _textoError = _lobby?.RecentrarManual());
            listo.style.backgroundImage = TemaUI.DegradeRojo;
            listo.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            botones.Add(listo); botones.Add(recentrar);
            _tarjeta.Add(botones);
        }

        // Fila ajustable: texto a la izquierda, − / + a la derecha. Con mando, ←/→ sobre la
        // fila ajustan (repetición tipo teclado); − / + quedan para la mirada y el dedo.
        private void Fila(MenuNavegable nav, string titulo, System.Action<int> ajustar)
        {
            var f = PiezasUI.Tarjeta();
            f.style.flexDirection = FlexDirection.Row;
            f.style.alignItems = Align.Center;
            f.style.paddingTop = 14; f.style.paddingBottom = 14;
            f.style.marginBottom = 12;
            var l = TemaUI.Texto(titulo, TemaUI.Bebas, 40, TemaUI.Cream, 3f);
            l.style.flexGrow = 1;
            f.Add(l);
            var menos = BotonChico("-");
            var mas = BotonChico("+");
            menos.style.marginRight = 16;
            f.Add(menos); f.Add(mas);
            _tarjeta.Add(f);
            nav.AgregarDeslizador(f, ajustar, menos, mas);
        }

        private static VisualElement BotonChico(string s)
        {
            var b = new VisualElement();
            b.style.width = 84; b.style.height = 84;
            TemaUI.Radio(b, 42);
            TemaUI.Borde(b, 2, TemaUI.Linea);
            b.style.backgroundColor = new Color(1f, 1f, 1f, 0.05f);
            b.style.alignItems = Align.Center; b.style.justifyContent = Justify.Center;
            b.style.flexShrink = 0;
            b.Add(TemaUI.Texto(s, TemaUI.Bebas, 54, TemaUI.Cream, 0f, TextAnchor.MiddleCenter));
            return b;
        }

        // Ejes horizontales de la vista (sin la inclinación de la cabeza).
        private static Vector3 Frente()
        {
            var cam = Camera.main;
            if (cam == null) return Vector3.forward;
            var f = cam.transform.forward; f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        private static Vector3 Lateral() => Vector3.Cross(Vector3.up, Frente());

        private static void Mover(Vector3 delta)
        {
            var wo = WorldOrigin.Instance;
            if (wo == null) return;
            wo.transform.position += delta;
        }

        private static void Girar(float grados)
        {
            var wo = WorldOrigin.Instance;
            if (wo == null) return;
            wo.transform.Rotate(Vector3.up, grados, Space.World);
        }

        protected override void Animar(float dt)
        {
            bool hay = !string.IsNullOrEmpty(_textoError);
            _error.text = hay ? _textoError : "";
            _error.style.display = hay ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Mira de RECENTRAR (el punto que se toma como nuevo 0,0): en el visor, en cada ojo.
        protected override void DibujarEnOjo(Vector2 c, float ppd) => Mira(c, Screen.height / 1080f);

        protected override void OnGUI()
        {
            base.OnGUI();
            if (EnVisor || !EsVisible() || Event.current.type != EventType.Repaint) return;
            var prevM = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Mira(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), Screen.height / 1080f);
            GUI.matrix = prevM;
        }

        private static void Mira(Vector2 c, float s)
        {
            float r = 22f * s, g = 7f * s, w = 2.5f * s;
            var prev = GUI.color;
            GUI.color = new Color(TemaUI.Cream.r, TemaUI.Cream.g, TemaUI.Cream.b, 0.8f);
            var t = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(c.x - r, c.y - w * 0.5f, r - g, w), t);
            GUI.DrawTexture(new Rect(c.x + g, c.y - w * 0.5f, r - g, w), t);
            GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y - r, w, r - g), t);
            GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y + g, w, r - g), t);
            GUI.color = prev;
        }
    }
}
