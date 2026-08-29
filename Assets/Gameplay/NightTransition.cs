using UnityEngine;

namespace Gameplay
{
    // Pasar a la SIGUIENTE noche o REINTENTAR la actual sin recargar la escena.
    //
    // El punto de no recargar es la SESIÓN AR: los ARAnchor (imagen + anchor points)
    // sólo viven mientras vive la sesión, y recargar SampleScene los mata a todos. Si
    // la sesión sobrevive, los anchor points colocados siguen ahí y al jugador sólo le
    // queda volver a apuntar a la imagen de referencia — que además es lo que dispara
    // ARImageAnchor.OnImageReacquired y hace que AnchorPointManager re-mida toda la
    // cadena contra la imagen fresca.
    //
    // Server-authoritative: sólo el host reinicia. Los clientes reciben ResetNight y
    // hacen su parte local.
    public static class NightTransition
    {
        // ¿Este dispositivo puede decidir el reinicio? (sólo el host).
        public static bool PuedeReiniciar =>
            NetworkManager.Instance != null && NetworkManager.Instance.IsServer;

        // ¿Se puede ofrecer el selector de noches? (hay catálogo cargado).
        public static bool HayCatalogoDeNoches =>
            GameSession.Instance != null && GameSession.Instance.HayCatalogoDeNoches;

        // Reintentar la MISMA noche.
        public static void Reintentar() => Reiniciar();

        // Saltar a CUALQUIER noche del catálogo (no sólo la siguiente).
        public static bool IrANoche(int index)
        {
            if (!PuedeReiniciar) return false;
            var s = GameSession.Instance;
            if (s == null || !s.SeleccionarNoche(index)) return false;
            Reiniciar();
            return true;
        }

        private static void Reiniciar()
        {
            if (!PuedeReiniciar)
            {
                Debug.LogWarning("[NightTransition] Sólo el host puede reiniciar la noche.");
                return;
            }
            var s = GameSession.Instance;

            // 1) Cortar la noche en el server y avisar a los clientes.
            NetworkManager.Instance.ServerResetNight();

            // 2) Bajar los sistemas de gameplay locales (host).
            ResetLocal();

            // 3) Volver a la pantalla de sincronización: re-detectar la imagen. Los
            //    anchor points NO se tocan — siguen vivos en la misma sesión AR.
            ARLobbyManager.Instance?.ReiniciarSincronizacion();

            Debug.Log($"[NightTransition] Reinicio → noche '{NombreNoche(s)}' (misma sesión AR).");
        }

        // Parte local del reinicio. La corre el host directo y cada cliente al recibir
        // ResetNight. Todos estos sistemas se re-inicializan solos en el próximo
        // OnGameStarted, así que acá sólo hay que frenarlos y limpiar.
        public static void ResetLocal()
        {
            ServerDeaths.Reset();
            LocalDeath.Instance?.Revive();
            LocalSanity.Instance?.Reiniciar();
            NightResult.Limpiar();
            RitualBookDirector.Instance?.Reiniciar();   // el libro vuelve a verse limpio
            VelethDirector.Instance?.StopRun();

            // Salir de Cardboard: la pantalla de sincronización es monoscópica y hay que
            // volver a apuntar a la imagen, cosa imposible con la vista estéreo (el
            // centro de la pantalla no es lo que ve ninguno de los dos ojos).
            var cb = Object.FindAnyObjectByType<MRCardboardController>();
            if (cb != null && cb.CardboardActive) cb.SetCardboard(false);

            DetenerSistemas();
        }

        // SALIR de la partida (al menú, al escáner…), que NO es lo mismo que reiniciar la
        // noche: acá se recarga la escena, así que todo lo que vive en ella se va solo.
        //
        // El problema es lo que NO se va: los directores son DontDestroyOnLoad
        // (RitualBookDirector, VelethDirector, ArbmosDirector, SorkerNav) y el estado de
        // muerte / resultado es estático. Sin este teardown quedaban "corriendo" y se
        // reanudaban en la partida siguiente en cuanto había un server nuevo — todavía en
        // la sala de sincronización, antes de que arrancara la noche.
        //
        // Lo llaman SceneFlow.GoTo y DeathScreenUI.ReturnToMenu (los dos únicos caminos
        // de salida). Es idempotente y seguro fuera de partida (menú, escáner): los
        // singletons de escena limpian su Instance en OnDestroy, así que acá el ?. de C#
        // ve null de verdad y no una referencia a un objeto ya destruido — que es lo que
        // vería si sólo confiáramos en el == sobrecargado de UnityEngine.Object.
        public static void TeardownSesion()
        {
            DetenerSistemas();
            ServerDeaths.Reset();
            LocalDeath.Instance?.Revive();
            LocalSanity.Instance?.Reiniciar();
            NightResult.Limpiar();
        }

        // Sólo frena los sistemas de gameplay (sin revivir ni limpiar el resultado):
        // es lo que hace falta cuando la noche TERMINA pero la pantalla de fin todavía
        // tiene que mostrar cómo terminó.
        public static void DetenerSistemas()
        {
            GameDirector.Instance?.StopRun();
            ArbmosDirector.Instance?.StopRun();
            RitualBookDirector.Instance?.StopRun();
            VelethDirector.Instance?.StopRun();
            SanitySystem.Instance?.StopRun();
            Bateries.BatterySpawnManager.Instance?.StopRun();
        }

        // Amaneció. Lo corren el host y cada cliente al recibir NightSurvived.
        // El que ya estaba muerto NO sobrevivió: se queda con su pantalla de muerte.
        public static void NocheSuperada()
        {
            DetenerSistemas();

            if (LocalDeath.Instance != null && LocalDeath.Instance.IsDead) return;

            NightResult.MarcarSobrevivida();
            AudioManager.Musica(c => c.victoriaAmanecer, fade: 0.5f);

            // El desbloqueo es por dispositivo y necesita saber QUÉ noche era. Un
            // cliente que se unió por LAN no pasó por el menú de noches (NightIndex
            // queda en -1), así que ve la victoria pero no mueve su progresión.
            var s = GameSession.Instance;
            if (s != null && s.NightIndex >= 0 &&
                NightProgress.RegistrarNocheSuperada(s.NightIndex))
                NightResult.MarcarDesbloqueo(s.NightIndex + 2);   // la siguiente, en base 1
        }

        private static string NombreNoche(GameSession s)
        {
            var n = s != null ? s.SelectedNight : null;
            if (n == null) return "?";
            return string.IsNullOrEmpty(n.displayName) ? n.name : n.displayName;
        }
    }
}
