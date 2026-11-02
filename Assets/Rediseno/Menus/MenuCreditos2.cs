using Gameplay;
using Rediseno.UI;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // CRÉDITOS (OPCIONES → CRÉDITOS). Existe por las licencias del audio de terceros: CC BY 3.0
    // y CC BY 4.0 OBLIGAN a acreditar autor, licencia y que el material se modificó mientras
    // esos clips estén en el build. Sólo van los créditos obligatorios; el detalle por clip vive
    // en Assets/Audio/ATTRIBUTIONS.md: al sumar o quitar audio de terceros, actualizar los dos.
    public class MenuCreditos2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.Creditos;
        protected override string Titulo => "CRÉDITOS";

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            lista.Add(PiezasUI.Seccion("EFECTOS DE SONIDO"));
            PiezasUI.Info(lista,
                "Little Robot Sound Factory · Horror Sound Effects Library\n" +
                "www.littlerobotsoundfactory.com · CC BY 3.0");
            PiezasUI.Info(lista,
                "Mike Koenig · \"Dying Light Bulb\" (SoundBible) · CC BY 3.0");
            PiezasUI.Info(lista,
                "Desde Freesound, bajo CC BY 4.0:\n" +
                "MeijstroAudio · \"Monster Growls\"\n" +
                "nicklas3799 · \"Male Gasp\"\n" +
                "jorickhoofd · \"Male Inhale scream 5\"\n" +
                "tosha73 · \"Alien is crawling-003\"\n" +
                "umnachtung · \"Zombie/Monster roars, growls, grunts, devouring sounds\"");
            PiezasUI.Info(lista,
                "Estos sonidos fueron modificados (recortes, fades, mezcla y nivel). Licencias: " +
                "creativecommons.org/licenses/by/3.0 y /by/4.0");
        }
    }
}
