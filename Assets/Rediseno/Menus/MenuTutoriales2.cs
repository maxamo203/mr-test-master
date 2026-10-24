using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // TUTORIAL: el mapa del camino de pasos (TutorialCamino). Arriba el botón para empezar o
    // seguir desde el primer paso sin superar; abajo los pasos en orden: los superados se
    // pueden repetir sueltos, el actual arranca el camino y los que siguen están bloqueados.
    // Todo se juega en un escenario escaneado elegido en ¿DÓNDE JUGÁS?
    // (NightMenuUI.IniciarCamino), o cae en FALTA UN ESCENARIO.
    public class MenuTutoriales2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.Tutoriales;
        protected override string Titulo => "TUTORIAL";

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            var pasos = TutorialCamino.Pasos;
            int progreso = TutorialCamino.Progreso;
            bool completo = TutorialCamino.Completo;

            if (completo)
            {
                PiezasUI.Info(lista, "Completaste el tutorial. Podés repetir cualquier paso cuando quieras.");
            }
            else
            {
                var actual = TutorialCamino.PasoActual;
                var cta = PiezasUI.Boton(nav, progreso == 0 ? "EMPEZAR TUTORIAL" : $"CONTINUAR · PASO {progreso + 1}",
                                         () => NightMenuUI.Instance?.IniciarCamino(actual));
                cta.style.backgroundImage = TemaUI.DegradeRojo;
                cta.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                lista.Add(cta);
                nav.Enfocar(nav.OpcionDe(cta));
            }

            lista.Add(PiezasUI.Seccion($"TU AVANCE · {progreso} DE {pasos.Length}"));
            for (int i = 0; i < pasos.Length; i++)
            {
                var tipo = pasos[i];
                bool superado = i < progreso;
                bool esActual = i == progreso;
                string sub = superado ? "Superado · tocá para repetirlo"
                           : esActual ? Tutoriales.Descripcion(tipo)
                           : "Bloqueado · superá el paso anterior";
                bool retrato = tipo == TutorialTipo.Sorken || tipo == TutorialTipo.Arbmos || tipo == TutorialTipo.Libro;
                var op = PiezasUI.Opcion(nav, retrato ? Tutoriales.Arte(tipo) : IconosMenu.Libro,
                    $"{i + 1} · {Tutoriales.Titulo(tipo)}", sub,
                    () =>
                    {
                        // El actual sigue el camino; uno superado se repite suelto.
                        if (superado) NightMenuUI.Instance?.IniciarTutorial(tipo);
                        else NightMenuUI.Instance?.IniciarCamino(tipo);
                    },
                    habilitada: superado || esActual, retrato: retrato);
                if (superado) TemaUI.Borde(op, 2, new Color(TemaUI.Green.r, TemaUI.Green.g, TemaUI.Green.b, 0.6f));
                else if (esActual) TemaUI.Borde(op, 3, TemaUI.Red);
                lista.Add(op);
            }

            var fin = PiezasUI.Ignorar(PiezasUI.Tarjeta());
            fin.style.alignItems = Align.Center;
            fin.style.opacity = completo ? 1f : 0.45f;
            if (completo) TemaUI.Borde(fin, 2, new Color(TemaUI.Green.r, TemaUI.Green.g, TemaUI.Green.b, 0.6f));
            fin.Add(TemaUI.Texto("LISTO PARA LA NOCHE 1", TemaUI.Bebas, 56, completo ? TemaUI.Green : TemaUI.Cream, 5f,
                                 TextAnchor.MiddleCenter));
            lista.Add(fin);

            if (progreso > 0)
                lista.Add(PiezasUI.Boton(nav, "REINICIAR TUTORIAL", () =>
                {
                    TutorialCamino.ReiniciarProgreso();
                    Rearmar();
                }, peligro: true));
        }
    }
}
