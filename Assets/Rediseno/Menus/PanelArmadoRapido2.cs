using Gameplay;
using Gamepad;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // ARMÁ LA PRÁCTICA: armado rápido de SORKEN y VELETH (Gameplay.ArmadoRapido),
    // sin escaneo ni imagen. El jugador apunta la mira al piso y ubica el libro (el 0,0); en
    // la del Sorken, además marca la puerta y/o la ventana real por donde entra (una de cada
    // tipo: marcar otra del mismo tipo reemplaza la anterior). LISTO sigue el mismo
    // camino que tras calibrar (ARLobbyManager.ArmadoRapidoListo).
    //
    // A diferencia de las otras pantallas del Rediseño NO depende de Flag.Activo: TUTORIALES
    // sólo existe en el Rediseño 2.0, y la UI vieja del lobby no tiene pantalla para este
    // estado — sin este panel el armado quedaría sin salida.
    public class PanelArmadoRapido2 : PantallaUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static PanelArmadoRapido2 _instancia;
        public static bool Mostrando => _instancia != null && _instancia.EsVisible();

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaJuego || _instancia != null) return;
            new GameObject("PanelArmadoRapido2").AddComponent<PanelArmadoRapido2>();
        }

        private ARLobbyManager _lobby;
        private VisualElement _tarjeta, _botones;
        private Label _paso, _estado, _error;
        private string _textoError;

        // Lo que decide qué botones hay y cuáles están habilitados: se rearman sólo si cambia.
        private (bool libro, int entradas, bool completo) _armado = (false, -1, false);

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
            if (_lobby == null) _lobby = ARLobbyManager.Instance;
            if (_lobby == null || PauseMenuController.IsOpen) return false;
            return _lobby.State == ARLobbyManager.LobbyState.ArmadoRapido;
        }

        protected override bool EnVisor => MRCardboardController.Activo;
        protected override bool CabezaPorCamara => true;
        protected override Color VeloVisor => Color.clear;   // hay que ver el cuarto
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

            _tarjeta.Add(TemaUI.Texto("ARMÁ LA PRÁCTICA", TemaUI.Bebas, 64, TemaUI.Cream, 6f));
            _paso = TemaUI.Texto("", TemaUI.Mono, 24, TemaUI.Muted);
            _paso.style.marginTop = 8; _paso.style.marginBottom = 8;
            _tarjeta.Add(_paso);
            _estado = TemaUI.Texto("", TemaUI.Mono, 24, TemaUI.Tan);
            _tarjeta.Add(_estado);

            _error = TemaUI.Texto("", TemaUI.Mono, 24, TemaUI.Red);
            _error.style.display = DisplayStyle.None;
            _tarjeta.Add(_error);

            // En vertical, como en todos los menús.
            _botones = PiezasUI.Ignorar(new VisualElement());
            _tarjeta.Add(_botones);   // los llena ArmarBotones en el primer Animar
        }

        private void ArmarBotones()
        {
            _botones.Clear();
            Nav.Limpiar();
            var tipo = GameSession.Instance != null ? GameSession.Instance.Tutorial : TutorialTipo.Ninguno;
            _armado = (ArmadoRapido.LibroUbicado, ArmadoRapido.Entradas, ArmadoRapido.Completo(tipo));

            // LISTO primero (arriba y primero en el orden de la ruedita).
            var listo = PiezasUI.Boton(Nav, "LISTO", () => { _textoError = null; _lobby?.ArmadoRapidoListo(); },
                                       habilitado: _armado.completo);
            listo.style.backgroundImage = TemaUI.DegradeRojo;
            listo.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            _botones.Add(listo);

            _botones.Add(PiezasUI.Boton(Nav, _armado.libro ? "MOVER LIBRO" : "UBICAR LIBRO",
                () => _textoError = ArmadoRapido.UbicarLibro()));

            if (ArmadoRapido.PideEntradas(tipo))
            {
                _botones.Add(PiezasUI.Boton(Nav, "MARCAR PUERTA",
                    () => _textoError = ArmadoRapido.MarcarEntrada(false), habilitado: _armado.libro));
                _botones.Add(PiezasUI.Boton(Nav, "MARCAR VENTANA",
                    () => _textoError = ArmadoRapido.MarcarEntrada(true), habilitado: _armado.libro));
            }
        }

        protected override void Animar(float dt)
        {
            var tipo = GameSession.Instance != null ? GameSession.Instance.Tutorial : TutorialTipo.Ninguno;
            bool libro = ArmadoRapido.LibroUbicado;
            int entradas = ArmadoRapido.Entradas;
            if ((libro, entradas, ArmadoRapido.Completo(tipo)) != _armado) ArmarBotones();

            bool pide = ArmadoRapido.PideEntradas(tipo);
            _paso.text = !libro
                ? "Jugá dentro de una sola habitación. Apuntá la mira al PISO, en el medio del cuarto, " +
                  "donde querés el libro ritual y tocá UBICAR LIBRO."
                : pide
                    ? "Parate a 1-2 m frente a una puerta o ventana de ESTA habitación, con pared a los dos " +
                      "costados, apuntá la mira a su centro y marcala. El Sorken va a entrar por ahí. " +
                      "Si marcás otra puerta (o ventana), reemplaza a la anterior del mismo tipo."
                    : "Listo. Si querés el libro en otro lado, apuntá ahí y tocá MOVER LIBRO.";
            _estado.text = pide
                ? $"Puerta: {(ArmadoRapido.HayEntrada(false) ? "marcada" : "—")}  ·  " +
                  $"Ventana: {(ArmadoRapido.HayEntrada(true) ? "marcada" : "—")}"
                : "";
            _estado.style.display = pide ? DisplayStyle.Flex : DisplayStyle.None;

            bool hay = !string.IsNullOrEmpty(_textoError);
            _error.text = hay ? _textoError : "";
            _error.style.display = hay ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Mira (el punto que se toma para el libro o la entrada): en el visor, en cada ojo.
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
