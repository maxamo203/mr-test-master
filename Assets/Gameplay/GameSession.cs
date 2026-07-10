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

        public NightConfig SelectedNight { get; set; }
        public string      SelectedMap   { get; set; }

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
