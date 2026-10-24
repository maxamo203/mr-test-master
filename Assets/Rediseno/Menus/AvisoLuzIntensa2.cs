using Gameplay;
using Rediseno.UI;
using UnityEngine;

namespace Rediseno.Menus
{
    // Práctica LIBRO RITUAL: la primera vez que el libro empieza a oscurecerse, un aviso
    // encima de la barra de la linterna explica la luz intensa y lo que cuesta en batería.
    // La práctica queda en pausa (Time.timeScale = 0) hasta CONTINUAR; ver
    // Gameplay.TutorialLibro.
    public class AvisoLuzIntensa2 : AvisoSobreLinterna2
    {
        private static TutorialLibro T => TutorialLibro.Instance;

        protected override bool Abierto => T != null && T.AvisoAbierto;
        protected override Color Acento => TemaUI.Tan;
        protected override string Titulo => "LUZ INTENSA";
        protected override string Cuerpo =>
            $"Alumbrá el libro con la luz intensa ({PistaTutorial.ComoLuzIntensa()}) para salvarlo. " +
            "Ojo: gasta la batería mucho más rápido que la luz normal. Usala sólo cuando haga falta.";
        protected override void Continuar() => T?.Continuar();
    }
}
