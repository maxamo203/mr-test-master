using Gameplay;
using Rediseno.UI;
using UnityEngine;

namespace Rediseno.Menus
{
    // Práctica PILAS Y RELIQUIAS: aviso de la PRIMERA pila. Aparece encima de la barra de
    // la linterna antes de que la barra suba, con el drenaje en pausa; CONTINUAR suelta la
    // carga y recién ahí la barra se llena (ver Gameplay.TutorialPilasReliquias).
    public class AvisoPrimeraPila2 : AvisoSobreLinterna2
    {
        private static TutorialPilasReliquias T => TutorialPilasReliquias.Instance;

        protected override bool Abierto => T != null && T.AvisoAbierto;
        protected override Color Acento => TemaUI.Green;
        protected override string Titulo => T != null ? $"+{T.PorcentajeRetenido}% DE ENERGÍA" : "";
        protected override string Cuerpo =>
            "Recogiste tu primera pila: las pilas recargan la linterna. " +
            "Mientras leés esto, la linterna no se gasta.";
        protected override void Continuar() => T?.Continuar();
    }
}
