using System.Text;
using Gamepad;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // UNIRSE A SALA del Rediseño 2.0 (UI Toolkit), usable con el visor: lista las salas que
    // se anuncian en la red y conecta con un toque, el joystick o la mirada. La IP a mano
    // necesita teclado: sólo se ofrece sin el visor. Reemplaza la pantalla Unirse de
    // GameBootstrapper con el flag; la lógica (descubrimiento, conectar) sigue allá.
    public class PanelUnirse2 : PantallaUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static PanelUnirse2 _instancia;
        public static bool Mostrando => _instancia != null && _instancia.EsVisible();

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaJuego || _instancia != null) return;
            new GameObject("PanelUnirse2").AddComponent<PanelUnirse2>();
        }

        private VisualElement _fondo, _lista, _volver;
        private Label _estado;
        private string _firma;
        private float _spin;
        private readonly StringBuilder _sb = new();

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
            var gb = GameBootstrapper.Instance;
            return Flag.Activo && gb != null && gb.EnUnirse && !PauseMenuController.IsOpen;
        }

        protected override bool EnVisor => MRCardboardController.Activo;
        protected override bool CabezaPorCamara => true;
        protected override Color VeloVisor => new(0.02f, 0.016f, 0.012f, 0.92f);
        protected override Texture FondoVisor => null;

        protected override void AlCambiarVisor(bool visor)
        {
            if (_fondo != null) _fondo.style.display = visor ? DisplayStyle.None : DisplayStyle.Flex;
            _firma = null;   // con/sin visor cambia si se ofrece la IP a mano
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            _fondo = PiezasUI.Fondo(TarjetasNoche.Arte(3));
            raiz.Add(_fondo);
            var col = PiezasUI.Columna();
            raiz.Add(col);
            col.Add(PiezasUI.Encabezado("UNIRSE A SALA", out _volver));
            _estado = TemaUI.Texto("", TemaUI.Elite, 34, TemaUI.Muted);
            _estado.style.marginBottom = 20;
            col.Add(_estado);
            var sv = PiezasUI.Lista();
            Desplazable = sv;
            col.Add(sv);
            _lista = sv;
            nav.Volver = () => GameBootstrapper.Instance?.SalirUnirse();
        }

        protected override void Animar(float dt)
        {
            var gb = GameBootstrapper.Instance;
            if (gb == null) return;
            var salas = gb.Salas;
            int n = salas != null ? salas.Count : 0;

            _spin += dt;
            string giro = new[] { "—", "\\", "|", "/" }[Mathf.FloorToInt(_spin * 6f) % 4];
            _estado.text = n > 0 ? "Salas en tu red:" : $"Buscando salas en tu red… {giro}";

            // Rearmar sólo cuando cambia el conjunto de salas (o el visor).
            _sb.Clear().Append(EnVisor ? 'v' : 'p');
            if (salas != null) foreach (var k in salas.Keys) _sb.Append('|').Append(k);
            string firma = _sb.ToString();
            if (firma == _firma) return;
            _firma = firma;
            Armar(gb);
        }

        private void Armar(GameBootstrapper gb)
        {
            _lista.Clear();
            Nav.Limpiar();
            var salas = gb.Salas;
            if (salas != null)
                foreach (var h in salas.Values)
                {
                    var host = h;
                    var b = PiezasUI.Tarjeta();
                    b.Add(TemaUI.Texto(string.IsNullOrEmpty(host.Name) ? "SALA" : host.Name.ToUpperInvariant(),
                                       TemaUI.Bebas, 52, TemaUI.Cream, 4f));
                    var d = TemaUI.Texto($"{host.Address} : {host.GamePort}", TemaUI.Mono, 26, TemaUI.Muted);
                    d.style.marginTop = 4;
                    b.Add(d);
                    _lista.Add(b);
                    Nav.Agregar(b, () => GameBootstrapper.Instance?.ConectarA(host.Address, host.GamePort));
                }

            if (EnVisor)
            {
                PiezasUI.Info(_lista, "Para ingresar la IP del host a mano, salí del visor " +
                                      "(pausa → opciones → Modo Cardboard).");
            }
            else
            {
                _lista.Add(PiezasUI.Seccion("O INGRESÁ LA IP DEL HOST"));
                var campo = new TextField { value = gb.HostManual ?? "" };
                campo.style.unityFontDefinition = TemaUI.Mono;
                campo.style.fontSize = 34;
                campo.style.color = TemaUI.Cream;
                campo.style.marginBottom = 16;
                var input = campo.Q(className: TextField.inputUssClassName);
                if (input != null)
                {
                    input.style.backgroundColor = new Color(0.075f, 0.063f, 0.063f, 1f);
                    TemaUI.Radio(input, 24);
                    TemaUI.Borde(input, 2, TemaUI.Linea);
                    TemaUI.Relleno(input, 22, 26);
                    input.style.color = TemaUI.Cream;
                }
                campo.RegisterValueChangedCallback(e => { if (GameBootstrapper.Instance != null) GameBootstrapper.Instance.HostManual = e.newValue; });
                _lista.Add(campo);
                var nota = TemaUI.Texto($"Ej: 192.168.1.42 — si no ponés puerto se usa el {NetworkConfig.DefaultPort} (o ip:puerto)",
                                        TemaUI.Mono, 22, TemaUI.Dim);
                nota.style.marginBottom = 18;
                _lista.Add(nota);
                _lista.Add(PiezasUI.Boton(Nav, "CONECTAR", () => GameBootstrapper.Instance?.ConectarManual()));
            }

            Nav.Agregar(_volver, () => GameBootstrapper.Instance?.SalirUnirse());
        }
    }
}
