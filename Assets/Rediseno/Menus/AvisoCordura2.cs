using Gameplay;
using Rediseno.UI;
using UnityEngine;

namespace Rediseno.Menus
{
    // Paso PILAS Y CORDURA del TUTORIAL: presenta la barra de cordura, que en ese momento
    // aparece por primera vez encima de la de la linterna (ver Gameplay.TutorialPilasReliquias).
    // CONTINUAR sigue con las reliquias.
    public class AvisoCordura2 : AvisoSobreLinterna2
    {
        private static TutorialPilasReliquias T => TutorialPilasReliquias.Instance;

        protected override bool Abierto => T != null && T.AvisoCorduraAbierto;
        protected override Color Acento => TemaUI.Tan;
        protected override string Titulo => "ESTA ES TU CORDURA";
        protected override string Cuerpo =>
            "La oscuridad no la baja: la bajan las criaturas. " +
            "Si llega a cero, estás en peligro de verdad.";
        protected override void Continuar() => T?.ContinuarCordura();
    }
}
