using Gameplay;
using Rediseno.UI;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // TUTORIALES: una práctica por cada amenaza, más pilas y reliquias y el libro ritual.
    // SORKEN y LIBRO RITUAL Y VELETH van directo a SampleScene y se arman en el momento
    // (Gameplay.ArmadoRapido); PILAS y ARBMOS se juegan en un escenario escaneado: pasan a
    // ¿DÓNDE JUGÁS? (NightMenuUI.IniciarTutorial), o a FALTA UN ESCENARIO si no hay ninguno.
    public class MenuTutoriales2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.Tutoriales;
        protected override string Titulo => "TUTORIALES";

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            PiezasUI.Info(lista, "Practicá cada amenaza sin riesgo: no cuenta como noche y la repetís " +
                                 "las veces que quieras. Sorken y el libro se arman en el momento; " +
                                 "pilas y Arbmos se juegan en uno de tus escenarios.");
            lista.Add(PiezasUI.Opcion(nav, IconosMenu.Libro, "PILAS Y RELIQUIAS",
                "Recargá la linterna y juntá reliquias",
                () => NightMenuUI.Instance?.IniciarTutorial(TutorialTipo.PilasReliquias)));
            Practica(lista, nav, TutorialTipo.Sorken,
                "Entra por puertas y ventanas. Alumbralo con la luz intensa para echarlo");
            Practica(lista, nav, TutorialTipo.Arbmos,
                "Te observa a oscuras. Apagá la luz para que se vaya; iluminarlo lo provoca");
            Practica(lista, nav, TutorialTipo.Libro,
                "Alumbralo con la luz intensa para salvarlo. Después dejalo apagarse y escapale a Veleth");
        }

        private static void Practica(VisualElement lista, MenuNavegable nav, TutorialTipo tipo, string desc) =>
            lista.Add(PiezasUI.Opcion(nav, Tutoriales.Arte(tipo), Tutoriales.Titulo(tipo), desc,
                () => NightMenuUI.Instance?.IniciarTutorial(tipo), retrato: true));
    }
}
