using Rediseno.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Aviso de las prácticas que se apoya justo encima de la barra de la linterna (abajo a
    // la izquierda; en el visor, la tarjeta sola centrada y abajo). Se cierra sólo con
    // CONTINUAR. Es una pantalla del rediseño para que funcione igual con el dedo, el mando
    // y la mirada. Mientras se ve, la práctica que lo abre pone
    // Bateries.ContextActionController.AvisoAbierto, así el botón primario del juego no
    // recoge ni toca la linterna.
    public abstract class AvisoSobreLinterna2 : PantallaUI
    {
        private Label _titulo, _cuerpo;

        protected abstract bool Abierto { get; }
        protected abstract string Titulo { get; }
        protected abstract string Cuerpo { get; }
        protected abstract Color Acento { get; }
        protected abstract void Continuar();

        protected override bool EsVisible() => Abierto && !Gamepad.PauseMenuController.IsOpen;

        protected override bool EnVisor => MRCardboardController.Activo;
        protected override bool CabezaPorCamara => true;
        protected override Color VeloVisor => Color.clear;     // se tiene que ver el cuarto
        // Chico y un poco abajo: donde quedan las barras de la noche bajo cada ojo.
        protected override float AnguloPanel => 26f;
        protected override Vector2 PosicionVisor => new(0f, -10f);

        protected override void AlCambiarVisor(bool visor)
        {
            if (Raiz == null) return;
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
            TemaUI.Borde(tarjeta, 2, Acento);
            TemaUI.Relleno(tarjeta, 34, 40);
            tarjeta.style.width = 760;
            tarjeta.style.marginLeft = 40;
            // Por encima de la barra de la linterna (fila de abajo del HUD).
            tarjeta.style.marginBottom = 170;
            raiz.Add(tarjeta);

            _titulo = TemaUI.Texto(Titulo, TemaUI.Bebas, 84, Acento, 6f);
            tarjeta.Add(_titulo);
            _cuerpo = TemaUI.Texto(Gameplay.PistaTutorial.Resaltar(Cuerpo), TemaUI.Mono, 28, TemaUI.Cream);
            _cuerpo.enableRichText = true;
            _cuerpo.style.marginTop = 6; _cuerpo.style.marginBottom = 26;
            tarjeta.Add(_cuerpo);

            var continuar = PiezasUI.Boton(nav, "CONTINUAR", Continuar);
            continuar.style.backgroundImage = TemaUI.DegradeRojo;
            continuar.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            tarjeta.Add(continuar);
            nav.Enfocar(nav.OpcionDe(continuar));
            // Sin "volver": el aviso se cierra sólo con CONTINUAR.
        }

        // El texto puede depender del estado (carga retenida, control en uso): se refresca
        // mientras se ve, sólo si cambió.
        protected override void Animar(float dt)
        {
            string t = Titulo, c = Gameplay.PistaTutorial.Resaltar(Cuerpo);
            if (_titulo.text != t) _titulo.text = t;
            if (_cuerpo.text != c) _cuerpo.text = c;
        }
    }
}
