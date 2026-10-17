using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // MIS ESCENARIOS del Rediseño 2.0: los escaneos guardados, cada uno con EDITAR y BORRAR
    // uno al lado del otro (←/→ pasan de uno al otro, ↑↓ cambian de escenario), y arriba
    // NUEVO ESCANEO. El borrado se confirma dentro de la misma pantalla (CANCELAR primero,
    // así un → de más no borra nada).
    //
    // Escanear se hace con el celular en la mano: al ir al escáner se sale del visor.
    public class MenuEscenarios2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.Escaneos;
        protected override string Titulo => "MIS ESCENARIOS";
        protected override Texture2D ArteFondo => TarjetasNoche.Arte(1);

        private string _borrando;   // escenario a confirmar, o null

        protected override void AlVolver()
        {
            if (_borrando != null) { _borrando = null; Rearmar(); return; }
            base.AlVolver();
        }

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            var menu = NightMenuUI.Instance;
            if (menu == null) return;
            if (_borrando != null && !Contiene(menu, _borrando)) _borrando = null;
            if (_borrando != null) { ArmarConfirmacion(lista, nav); return; }

            var nuevo = PiezasUI.Boton(nav, "+ NUEVO ESCANEO", () => IrAlEscaner(null));
            nuevo.style.backgroundImage = TemaUI.DegradeRojo;
            nuevo.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            lista.Add(nuevo);
            if (EnVisor)
            {
                var nota = TemaUI.Texto("Escanear se hace con el celular en la mano, sin el visor.",
                                        TemaUI.Mono, 24, TemaUI.Dim);
                nota.style.marginBottom = 8;
                lista.Add(nota);
            }

            lista.Add(PiezasUI.Seccion("GUARDADOS"));
            if (menu.Escenarios.Count == 0)
                PiezasUI.Info(lista, "Todavía no escaneaste ningún escenario. El ritual necesita conocer " +
                                     "tu espacio real: empezá con NUEVO ESCANEO.");
            foreach (var e in menu.Escenarios)
            {
                string nombre = e;
                bool jugable = menu.EsJugable(nombre);

                var fila = PiezasUI.Ignorar(PiezasUI.Tarjeta());
                fila.style.flexDirection = FlexDirection.Row;
                fila.style.alignItems = Align.Center;

                var textos = PiezasUI.Ignorar(new VisualElement());
                textos.style.flexGrow = 1; textos.style.flexShrink = 1;
                textos.Add(TemaUI.Texto(nombre, TemaUI.Bebas, 46, TemaUI.Cream, 3f));
                var det = TemaUI.Texto(jugable ? $"{menu.ElementosDe(nombre)} elementos"
                                               : $"{menu.ElementosDe(nombre)} elementos · sin imagen de referencia",
                                       TemaUI.Mono, 24, jugable ? TemaUI.Muted : TemaUI.Tan);
                det.style.marginTop = 2;
                textos.Add(det);
                fila.Add(textos);

                var editar = Pastilla("EDITAR", TemaUI.Tan);
                var borrar = Pastilla("BORRAR", TemaUI.Red);
                fila.Add(editar);
                fila.Add(borrar);
                lista.Add(fila);

                nav.FilaHorizontal(
                    nav.Agregar(editar, () => IrAlEscaner(nombre)),
                    nav.Agregar(borrar, () =>
                    {
                        AudioManager.Sonar(c => c.uiAlerta);
                        _borrando = nombre;
                        Rearmar();
                    }));
            }
        }

        private void ArmarConfirmacion(VisualElement lista, MenuNavegable nav)
        {
            var t = PiezasUI.Ignorar(PiezasUI.Tarjeta());
            TemaUI.Borde(t, 2, TemaUI.Red);
            TemaUI.Relleno(t, 40, 40);
            t.Add(TemaUI.Texto($"¿BORRAR \"{_borrando}\"?", TemaUI.Bebas, 64, TemaUI.Cream, 5f));
            var sub = TemaUI.Texto("Se borra el escaneo y su imagen de referencia. No se puede deshacer.",
                                   TemaUI.Mono, 28, TemaUI.Muted);
            sub.style.marginTop = 10;
            t.Add(sub);
            lista.Add(t);

            lista.Add(PiezasUI.Boton(nav, "CANCELAR", () => { _borrando = null; Rearmar(); }));
            lista.Add(PiezasUI.Boton(nav, "BORRAR", () =>
            {
                string n = _borrando;
                _borrando = null;
                NightMenuUI.Instance?.BorrarEscenario(n);
                Rearmar();
            }, peligro: true));
        }

        private static void IrAlEscaner(string nombre)
        {
            var menu = NightMenuUI.Instance;
            if (menu == null) return;
            // Con el visor puesto, NightMenuUI avisa primero que el escaneo se hace sin él.
            if (nombre == null) menu.EscanearNuevo(); else menu.EditarEscenario(nombre);
        }

        private static bool Contiene(NightMenuUI menu, string nombre)
        {
            foreach (var e in menu.Escenarios) if (e == nombre) return true;
            return false;
        }

        private static VisualElement Pastilla(string texto, Color c)
        {
            var b = new VisualElement();
            TemaUI.Radio(b, 30);
            TemaUI.Relleno(b, 20, 28);
            TemaUI.Borde(b, 2, new Color(c.r, c.g, c.b, 0.6f));
            b.style.backgroundColor = new Color(c.r, c.g, c.b, 0.10f);
            b.style.marginLeft = 16;
            b.style.flexShrink = 0;
            b.Add(TemaUI.Texto(texto, TemaUI.Bebas, 38, c, 3f, TextAnchor.MiddleCenter));
            return b;
        }
    }
}
