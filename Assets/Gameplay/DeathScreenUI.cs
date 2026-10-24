using UnityEngine;

namespace Gameplay
{
    // Fin de la noche del jugador local (MORISTE / SOBREVIVISTE). La pantalla la dibuja
    // Rediseno.Menus.FinDeNoche2 (y la barra de espectador, Espectando2); este componente
    // de SampleScene sólo asegura LocalDeath y aporta las reglas de qué ofrecer al terminar.
    public class DeathScreenUI : MonoBehaviour
    {
        private void Awake() => LocalDeath.Ensure();

        // ¿La noche recién superada era la ÚLTIMA que habilita la demo, y hay más en el
        // catálogo? Lo segundo importa: sin noches de más no hay nada que ofrecer y el
        // cartel sería puro ruido. En la build completa es const false y se compila afuera.
        internal static bool FinDeLaDemo()
        {
            if (!BuildVariant.EsDemo) return false;

            var s = GameSession.Instance;
            if (s == null || s.NightIndex < 0) return false;

            int sig = s.NightIndex + 1;
            return s.Nights != null && sig < s.Nights.Length && s.Nights[sig] != null &&
                   !BuildVariant.NocheHabilitada(sig);
        }

        // ¿La noche recién superada dejó desbloqueada una siguiente que existe?
        internal static bool SiguienteDesbloqueada(out int index)
        {
            index = -1;
            var s = GameSession.Instance;
            if (s == null || s.NightIndex < 0) return false;
            int sig = s.NightIndex + 1;
            if (!s.NocheDisponible(sig) || !NightProgress.Desbloqueada(sig)) return false;
            index = sig;
            return true;
        }
    }
}
