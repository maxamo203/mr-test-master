using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Práctica PILAS Y RELIQUIAS: aviso de la PRIMERA pila. Aparece encima de la barra de
    // la linterna antes de que la barra suba, con el drenaje en pausa; CONTINUAR suelta la
    // carga y recién ahí la barra se llena (ver Gameplay.TutorialPilasReliquias). Es una
    // pantalla del rediseño para que funcione igual con el dedo, el mando y la mirada.
    // Mientras se ve, el botón primario del juego no recoge ni toca la linterna
    // (Bateries.ContextActionController.AvisoAbierto).
    public class AvisoPrimeraPila2 : PantallaUI
    {
        private Label _energia;

        protected override bool EsVisible()
        {
            var t = TutorialPilasReliquias.Instance;
            return t != null && t.AvisoAbierto && !Gamepad.PauseMenuController.IsOpen;
        }

        protected override bool EnVisor => MRCardboardController.Activo;
        protected override bool CabezaPorCamara => true;
        protected override Color VeloVisor => Color.clear;     // se tiene que ver el cuarto
        // Chico y un poco abajo: donde quedan las barras de la noche bajo cada ojo.
        protected override float AnguloPanel => 26f;
        protected override Vector2 PosicionVisor => new(0f, -10f);

        protected override void AlCambiarVisor(bool visor)
        {
            if (Raiz == null) return;
            // En pantalla, abajo a la izquierda, apoyado sobre la barra de la linterna; en el
            // visor, la tarjeta sola centrada en su lienzo.
            Raiz.style.justifyContent = visor ? Justify.Center : Justify.FlexEnd;
            Raiz.style.alignItems = visor ? Align.Center : Align.FlexStart;
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            raiz.style.justifyContent = Justify.FlexEnd;
            raiz.style.alignItems = Align.FlexStart;

            var tarjeta = PiezasUI.Ignorar(new VisualElement());
            tarjeta.style.backgroundColor = new Color(0.047f, 0.039f, 0.035f, 0.94f);
            TemaUI.Radio(tarjeta, 36);
            TemaUI.Borde(tarjeta, 2, TemaUI.Green);
            TemaUI.Relleno(tarjeta, 34, 40);
            tarjeta.style.width = 760;
            tarjeta.style.marginLeft = 40;
            // Por encima de la barra de la linterna (fila de abajo del HUD).
            tarjeta.style.marginBottom = 170;
            raiz.Add(tarjeta);

            _energia = TemaUI.Texto("", TemaUI.Bebas, 84, TemaUI.Green, 6f);
            tarjeta.Add(_energia);
            var texto = TemaUI.Texto("Recogiste tu primera pila: las pilas recargan la linterna. " +
                                     "Mientras leés esto, la linterna no se gasta.",
                                     TemaUI.Mono, 28, TemaUI.Cream);
            texto.style.marginTop = 6; texto.style.marginBottom = 26;
            tarjeta.Add(texto);

            var continuar = PiezasUI.Boton(nav, "CONTINUAR", () => TutorialPilasReliquias.Instance?.Continuar());
            continuar.style.backgroundImage = TemaUI.DegradeRojo;
            continuar.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            tarjeta.Add(continuar);
            nav.Enfocar(nav.OpcionDe(continuar));
            // Sin "volver": el aviso se cierra sólo con CONTINUAR.
        }

        protected override void Animar(float dt)
        {
            var t = TutorialPilasReliquias.Instance;
            if (t != null) _energia.text = $"+{t.PorcentajeRetenido}% DE ENERGÍA";
        }
    }
}
