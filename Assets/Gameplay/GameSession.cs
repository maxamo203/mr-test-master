using UnityEngine;

namespace Gameplay
{
    // Estado elegido en el menu (noche + mapa) que sobrevive el cambio de escena
    // MenuNoche -> SampleScene. DontDestroyOnLoad. En el host, los sistemas de gameplay
    // leen SelectedNight como la config activa de la partida.
    //
    // Multijugador: cada peer tiene su GameSession, pero la que manda es la del host
    // (server-authoritative). El cliente que se une puede dejar SelectedNight en null;
    // la dificultad la define el host.
    public class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        // Cómo se juega esta sesión. Lo elige el menú principal (NightMenuUI) antes
        // de cargar SampleScene; GameBootstrapper lo lee para hostear o unirse.
        //   SinglePlayer / MultiHost = host+server local (la partida es idéntica; sólo
        //                              cambia si se espera a otros jugadores).
        //   MultiClient              = se une a un host por LAN.
        public enum SessionMode { SinglePlayer, MultiHost, MultiClient }

        public SessionMode Mode          { get; set; } = SessionMode.SinglePlayer;
        public NightConfig SelectedNight { get; set; }
        public string      SelectedMap   { get; set; }
        // Puerto TCP a hostear (elegido en "Avanzado" del menú, o el por defecto).
        public int         HostPort      { get; set; } = NetworkConfig.DefaultPort;

        // True si es un jugador (host sin esperar a nadie). Para adaptar la UI del
        // lobby/sincronización: la partida sigue siendo la misma que un host multi.
        public bool SoloUnJugador => Mode == SessionMode.SinglePlayer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static GameSession Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("GameSession");
                go.AddComponent<GameSession>();
            }
            return Instance;
        }
    }
}
