using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Avisos modales del menú principal (Rediseño 2.0): la partida multijugador se cortó,
    // el celular no tiene ARCore/ARKit (no bloquea; con INSTALAR si sólo faltan los
    // Servicios de Google Play para RA) y "mejor en iOS" (el primero que ve cualquiera que
    // instala en Android). Qué aviso toca y cerrarlo sigue en NightMenuUI; acá sólo se
    // dibuja, usable con el visor.
    public class AvisoMenu2 : PantallaUI
    {
        private static AvisoMenu2 _instancia;

        private VisualElement _fondo, _cuerpo;
        private NightMenuUI.AvisoMenu _armado = NightMenuUI.AvisoMenu.Ninguno;

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
            var menu = NightMenuUI.Instance;
            return menu != null && menu.AvisoActual != NightMenuUI.AvisoMenu.Ninguno;
        }

        protected override Texture FondoVisor => TarjetasNoche.Arte(0);

        protected override void AlCambiarVisor(bool visor)
        {
            if (_fondo != null) _fondo.style.display = visor ? DisplayStyle.None : DisplayStyle.Flex;
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            _fondo = PiezasUI.Fondo(TarjetasNoche.Arte(0));
            raiz.Add(_fondo);
            var col = PiezasUI.Columna();
            col.style.justifyContent = Justify.Center;
            raiz.Add(col);
            _cuerpo = PiezasUI.Ignorar(new VisualElement());
            col.Add(_cuerpo);
        }

        protected override void Animar(float dt)
        {
            var menu = NightMenuUI.Instance;
            var aviso = menu != null ? menu.AvisoActual : NightMenuUI.AvisoMenu.Ninguno;
            if (aviso == _armado) return;
            _armado = aviso;
            _cuerpo.Clear();
            Nav.Limpiar();
            if (aviso != NightMenuUI.AvisoMenu.Ninguno) Armar(menu, aviso);
        }

        private void Armar(NightMenuUI menu, NightMenuUI.AvisoMenu aviso)
        {
            string titulo, texto;
            bool alerta = aviso == NightMenuUI.AvisoMenu.SinAR;
            bool escanear = aviso == NightMenuUI.AvisoMenu.EscanearSinVisor;
            bool instalar = alerta && menu.ARInstalable;
            switch (aviso)
            {
                case NightMenuUI.AvisoMenu.EscanearSinVisor:
                    titulo = "EL ESCANEO SE HACE SIN EL VISOR";
                    texto = "Sacá el celular del visor: se escanea con el celular en la mano, tocando la " +
                            "pantalla. Cuando guardes el escaneo volvés al menú y te ponés el visor de nuevo.";
                    break;
                case NightMenuUI.AvisoMenu.SinAR when instalar:
                    titulo = "FALTA ARCORE";
                    texto = "Para escanear tu cuarto y jugar las noches hace falta \"Servicios de Google " +
                            "Play para RA\" (ARCore). Instalalo desde Play Store; sin eso el juego abre, " +
                            "pero el escaneo y las noches no van a funcionar.";
                    break;
                case NightMenuUI.AvisoMenu.SinAR:
                    titulo = "TU CELULAR NO TIENE ARCORE";
                    texto = "Este dispositivo no es compatible con la realidad aumentada (ARCore / ARKit). " +
                            "Podés recorrer el menú, pero el escaneo y las noches no van a funcionar.";
                    break;
                case NightMenuUI.AvisoMenu.Salida:
                    titulo = (menu.TextoAvisoSalida ?? "LA PARTIDA TERMINÓ").ToUpperInvariant();
                    texto = "La partida terminó porque el host salió o se cortó la red. " +
                            "Podés volver a unirte cuando abra una sala nueva.";
                    break;
                default:
                    titulo = "MEJOR EXPERIENCIA EN iOS";
                    texto = "En iPhone la realidad aumentada es más estable y el escenario se corre " +
                            "mucho menos. Podés jugar en este celular sin problema, pero si tenés un " +
                            "iPhone a mano, vas a tener la mejor experiencia.";
                    break;
            }

            var t = PiezasUI.Ignorar(PiezasUI.Tarjeta());
            TemaUI.Borde(t, 2, alerta ? TemaUI.Red : TemaUI.Linea);
            TemaUI.Relleno(t, 44, 44);
            t.style.backgroundColor = new Color(0.047f, 0.039f, 0.035f, 0.94f);
            var kicker = TemaUI.Texto("AVISO", TemaUI.Mono, 26, alerta ? TemaUI.Red : TemaUI.Tan, 6f);
            kicker.style.marginBottom = 10;
            t.Add(kicker);
            t.Add(TemaUI.Texto(titulo, TemaUI.Bebas, 72, TemaUI.Cream, 5f));
            var cuerpo = TemaUI.Texto(texto, TemaUI.Elite, 34, TemaUI.CreamDim);
            cuerpo.style.marginTop = 16;
            t.Add(cuerpo);
            _cuerpo.Add(t);

            VisualElement boton;
            if (escanear)
            {
                boton = PiezasUI.Boton(Nav, "ESCANEAR SIN EL VISOR", () => NightMenuUI.Instance?.EscanearSinVisor());
                boton.style.backgroundImage = TemaUI.DegradeRojo;
                boton.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                boton.style.marginTop = 8;
                _cuerpo.Add(boton);
                boton = PiezasUI.Boton(Nav, "VOLVER", () => NightMenuUI.Instance?.CerrarAviso());
                Nav.Volver = () => NightMenuUI.Instance?.CerrarAviso();
            }
            else if (instalar)
            {
                boton = PiezasUI.Boton(Nav, "INSTALAR ARCORE", () => NightMenuUI.Instance?.InstalarAR());
                boton.style.backgroundImage = TemaUI.DegradeRojo;
                boton.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                boton.style.marginTop = 8;
                _cuerpo.Add(boton);
                boton = PiezasUI.Boton(Nav, "SEGUIR SIN ARCORE", () => NightMenuUI.Instance?.CerrarAviso());
                Nav.Volver = () => NightMenuUI.Instance?.CerrarAviso();
            }
            else
            {
                boton = PiezasUI.Boton(Nav, "ENTENDIDO", () => NightMenuUI.Instance?.CerrarAviso());
                boton.style.backgroundImage = TemaUI.DegradeRojo;
                boton.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                Nav.Volver = () => NightMenuUI.Instance?.CerrarAviso();
            }
            boton.style.marginTop = 8;
            _cuerpo.Add(boton);
        }
    }
}
