namespace Gameplay
{
    // Resultado de la noche en ESTE dispositivo + reloj de la noche.
    //
    // Complementa a LocalDeath (que sólo sabe de la muerte): acá vive el otro final,
    // sobrevivir hasta el amanecer. DeathScreenUI muestra una u otra según cuál esté
    // marcada. Todo se limpia al reiniciar (ver NightTransition.ResetLocal).
    //
    // El reloj lo manda el server 1 vez por segundo (los clientes no conocen la
    // NightConfig, así que no pueden contarlo solos).
    public static class NightResult
    {
        public static bool Sobrevivio { get; private set; }

        // Segundos que faltan para el amanecer y duración total de la noche.
        // Total <= 0 significa "esta noche no tiene límite de tiempo".
        public static int SegundosRestantes { get; private set; }
        public static int SegundosTotales   { get; private set; }

        public static bool HayReloj => SegundosTotales > 0;

        // 0 al empezar la noche → 1 al amanecer.
        public static float Progreso01 =>
            SegundosTotales > 0
                ? 1f - UnityEngine.Mathf.Clamp01((float)SegundosRestantes / SegundosTotales)
                : 0f;

        public static string RestanteMmSs
        {
            get
            {
                int s = UnityEngine.Mathf.Max(0, SegundosRestantes);
                return $"{s / 60:00}:{s % 60:00}";
            }
        }

        // Noche que esta victoria acaba de desbloquear, en base 1. 0 = ninguna (era
        // una rejugada de una noche ya superada, o era la última).
        public static int NocheDesbloqueada { get; private set; }

        public static void MarcarSobrevivida() => Sobrevivio = true;

        public static void MarcarDesbloqueo(int numeroBase1) => NocheDesbloqueada = numeroBase1;

        public static void SetReloj(int restantes, int totales)
        {
            SegundosRestantes = restantes;
            SegundosTotales   = totales;
        }

        // Sólo el desenlace. Al arrancar una noche el reloj NO se toca: lo publica el
        // GameDirector en el mismo evento OnGameStarted y el orden entre suscriptores
        // no está garantizado, así que limpiarlo acá podría pisar el valor recién puesto.
        public static void LimpiarResultado()
        {
            Sobrevivio        = false;
            NocheDesbloqueada = 0;
        }

        public static void Limpiar()
        {
            LimpiarResultado();
            SegundosRestantes = SegundosTotales = 0;
        }
    }
}
