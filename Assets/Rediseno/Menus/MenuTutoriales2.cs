using Gameplay;
using Rediseno.UI;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // TUTORIALES: una práctica por cada amenaza, más pilas y reliquias. Sólo los accesos,
    // sin texto introductorio. Todas se juegan en un escenario escaneado elegido en ¿DÓNDE
    // JUGÁS? (NightMenuUI.IniciarTutorial), o caen en FALTA UN ESCENARIO. Sólo en development
    // build SORKEN y VELETH ofrecen además ARMAR EN EL MOMENTO (Gameplay.ArmadoRapido).
    public class MenuTutoriales2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.Tutoriales;
        protected override string Titulo => "TUTORIALES";

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            lista.Add(PiezasUI.Opcion(nav, IconosMenu.Libro, "PILAS Y RELIQUIAS",
                "Recargá la linterna y juntá reliquias",
                () => NightMenuUI.Instance?.IniciarTutorial(TutorialTipo.PilasReliquias)));
            Practica(lista, nav, TutorialTipo.Sorken,
                "Entra por puertas y ventanas. Alumbralo con la luz intensa para echarlo");
            Practica(lista, nav, TutorialTipo.Arbmos,
                "Te observa a oscuras. Apagá la luz para que se vaya; iluminarlo lo provoca");
            Practica(lista, nav, TutorialTipo.Libro,
                "Nace cuando el ritual se apaga y te caza. Aguantá su persecución hasta que se retire");
        }

        private static void Practica(VisualElement lista, MenuNavegable nav, TutorialTipo tipo, string desc) =>
            lista.Add(PiezasUI.Opcion(nav, Tutoriales.Arte(tipo), Tutoriales.Titulo(tipo), desc,
                () => NightMenuUI.Instance?.IniciarTutorial(tipo), retrato: true));
    }
}
