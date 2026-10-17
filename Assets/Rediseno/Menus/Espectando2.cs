using Gameplay;
using Gamepad;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Barra de ESPECTANDO del Rediseño 2.0 (muerto en multijugador, mirando la noche de los
    // demás). Chica y sin velo para que se vea todo: en la pantalla, una franja arriba; en el
    // visor, una tarjeta un poco por debajo de la vista en cada ojo. VOLVER (A / → / dedo)
    // regresa a la pantalla de muerte.
    public class Espectando2 : PantallaUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static Espectando2 _instancia;
        public static bool Mostrando => _instancia != null && _instancia.EsVisible();

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaJuego || _instancia != null) return;
            new GameObject("Espectando2").AddComponent<Espectando2>();
        }

        private VisualElement _tarjeta;

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
            if (!Flag.Activo || PauseMenuController.IsOpen) return false;
            var ld = LocalDeath.Instance;
            return ld != null && ld.Espectando && !NightResult.Sobrevivio;
        }

        protected override bool EnVisor => MRCardboardController.Activo;
        protected override bool CabezaPorCamara => true;
        protected override Color VeloVisor => Color.clear;   // se espectea mirando el cuarto
        protected override float AnguloPanel => 30f;
        protected override Vector2 PosicionVisor => new(0f, -18f);

        protected override void AlCambiarVisor(bool visor)
        {
            if (_tarjeta == null) return;
            // En pantalla: franja arriba. En el visor: tarjeta centrada en su lienzo.
            Raiz.style.justifyContent = visor ? Justify.Center : Justify.FlexStart;
            Raiz.style.paddingTop = visor ? 0 : 56;
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            raiz.style.justifyContent = Justify.FlexStart;
            raiz.style.paddingLeft = 32; raiz.style.paddingRight = 32;

            _tarjeta = PiezasUI.Ignorar(new VisualElement());
            _tarjeta.style.flexDirection = FlexDirection.Row;
            _tarjeta.style.alignItems = Align.Center;
            _tarjeta.style.backgroundColor = new Color(0.047f, 0.039f, 0.035f, 0.85f);
            TemaUI.Radio(_tarjeta, 34);
            TemaUI.Borde(_tarjeta, 2, new Color(TemaUI.Red.r, TemaUI.Red.g, TemaUI.Red.b, 0.7f));
            TemaUI.Relleno(_tarjeta, 22, 32);
            raiz.Add(_tarjeta);

            var textos = PiezasUI.Ignorar(new VisualElement());
            textos.style.flexGrow = 1; textos.style.flexShrink = 1;
            textos.Add(TemaUI.Texto("ESPECTANDO", TemaUI.Bebas, 54, TemaUI.Red, 6f));
            var sub = TemaUI.Texto("Moriste. Mirá cómo sigue la noche.", TemaUI.Mono, 24, TemaUI.Muted);
            sub.style.marginTop = 2;
            textos.Add(sub);
            _tarjeta.Add(textos);

            var volver = new VisualElement();
            TemaUI.Radio(volver, 30);
            TemaUI.Relleno(volver, 20, 30);
            TemaUI.Borde(volver, 2, TemaUI.Linea);
            volver.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            volver.style.marginLeft = 20;
            volver.style.flexShrink = 0;
            volver.Add(TemaUI.Texto("VOLVER", TemaUI.Bebas, 40, TemaUI.Cream, 4f, TextAnchor.MiddleCenter));
            _tarjeta.Add(volver);

            nav.Agregar(volver, () => LocalDeath.Instance?.DejarDeEspectar());
            nav.Volver = () => LocalDeath.Instance?.DejarDeEspectar();
        }
    }
}
