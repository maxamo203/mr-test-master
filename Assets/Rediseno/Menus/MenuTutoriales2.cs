using Gameplay;
using Rediseno.UI;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // TUTORIALES: una práctica por cada amenaza, más pilas y reliquias y el libro ritual.
    // Cada una se juega en un escenario escaneado: al elegirla se pasa a ¿DÓNDE JUGÁS?
    // (NightMenuUI.IniciarTutorial), o a FALTA UN ESCENARIO si no hay ninguno que le sirva.
    public class MenuTutoriales2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.Tutoriales;
        protected override string Titulo => "TUTORIALES";

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            PiezasUI.Info(lista, "Practicá cada amenaza sin riesgo en uno de tus escenarios: " +
                                 "no cuenta como noche y la repetís las veces que quieras.");
            lista.Add(PiezasUI.Opcion(nav, IconosMenu.Libro, "PILAS Y RELIQUIAS",
                "Recargá la linterna y juntá reliquias",
                () => NightMenuUI.Instance?.IniciarTutorial(TutorialTipo.PilasReliquias)));
            Practica(lista, nav, TutorialTipo.Sorken,
                "Entra por puertas y ventanas. Alumbralo con la luz intensa para echarlo");
            Practica(lista, nav, TutorialTipo.Arbmos,
                "Te observa a oscuras. Apagá la luz para que se vaya; iluminarlo lo provoca");
            Practica(lista, nav, TutorialTipo.Libro,
                "Se apaga cada tanto. Alumbralo con la luz intensa para salvarlo");
            Practica(lista, nav, TutorialTipo.Veleth,
                "Sale del libro si se apaga. La luz no la frena: escapale");
        }

        private static void Practica(VisualElement lista, MenuNavegable nav, TutorialTipo tipo, string desc) =>
            lista.Add(PiezasUI.Opcion(nav, Tutoriales.Arte(tipo), Tutoriales.Titulo(tipo), desc,
                () => NightMenuUI.Instance?.IniciarTutorial(tipo), retrato: true));
    }
}
