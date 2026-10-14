using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Avisos modales del menú principal (Rediseño 2.0): versión no compatible (bloquea, sólo
    // SALIR), la partida multijugador se cortó, no se pudo verificar la versión y "mejor en
    // iOS" (el primero que ve cualquiera que instala en Android). Qué aviso toca y cerrarlo
    // sigue en NightMenuUI; acá sólo se dibuja, usable con el visor.
    public class AvisoMenu2 : PantallaUI
    {
        private static AvisoMenu2 _instancia;
        public static bool Mostrando => _instancia != null && _instancia.EsVisible();

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
            return Flag.Activo && menu != null && menu.AvisoActual != NightMenuUI.AvisoMenu.Ninguno;
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
            bool bloquea = aviso == NightMenuUI.AvisoMenu.VersionIncompatible;
            switch (aviso)
            {
                case NightMenuUI.AvisoMenu.VersionIncompatible:
                    titulo = "VERSIÓN NO COMPATIBLE";
                    texto = "La versión de tu dispositivo no es compatible con el juego. " +
                            "Hace falta Android 14 o iOS 17, o más nuevos.";
                    break;
                case NightMenuUI.AvisoMenu.Salida:
                    titulo = (menu.TextoAvisoSalida ?? "LA PARTIDA TERMINÓ").ToUpperInvariant();
                    texto = "La partida terminó porque el host salió o se cortó la red. " +
                            "Podés volver a unirte cuando abra una sala nueva.";
                    break;
                case NightMenuUI.AvisoMenu.VersionDesconocida:
                    titulo = "NO PUDIMOS VERIFICAR TU VERSIÓN";
                    texto = "Puede haber inestabilidad o problemas de rendimiento. Para la mejor " +
                            "experiencia recomendamos Android 14 o iOS 17, o más nuevos.";
                    break;
                default:
                    titulo = "MEJOR EXPERIENCIA EN iOS";
                    texto = "En iPhone la realidad aumentada es más estable y el escenario se corre " +
                            "mucho menos. Podés jugar en este celular sin problema, pero si tenés un " +
                            "iPhone a mano, vas a tener la mejor experiencia.";
                    break;
            }

            var t = PiezasUI.Ignorar(PiezasUI.Tarjeta());
            TemaUI.Borde(t, 2, bloquea ? TemaUI.Red : TemaUI.Linea);
            TemaUI.Relleno(t, 44, 44);
            t.style.backgroundColor = new Color(0.047f, 0.039f, 0.035f, 0.94f);
            var kicker = TemaUI.Texto("AVISO", TemaUI.Mono, 26, bloquea ? TemaUI.Red : TemaUI.Tan, 6f);
            kicker.style.marginBottom = 10;
            t.Add(kicker);
            t.Add(TemaUI.Texto(titulo, TemaUI.Bebas, 72, TemaUI.Cream, 5f));
            var cuerpo = TemaUI.Texto(texto, TemaUI.Elite, 34, TemaUI.CreamDim);
            cuerpo.style.marginTop = 16;
            t.Add(cuerpo);
            _cuerpo.Add(t);

            VisualElement boton;
            if (bloquea)
            {
                boton = PiezasUI.Boton(Nav, "SALIR", NightMenuUI.Salir, peligro: true);
                Nav.Volver = null;   // no se puede omitir
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
