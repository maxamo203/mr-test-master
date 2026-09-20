using UnityEngine;

namespace Gameplay
{
    // Estado de muerte del jugador LOCAL (este dispositivo). Lo dispara el server:
    //  - host  : GameDirector llama Die() directo.
    //  - cliente: llega por red (NetworkManager.OnPlayerDied).
    // Lo lee DeathScreenUI.
    public class LocalDeath : MonoBehaviour
    {
        public static LocalDeath Instance { get; private set; }
        public bool IsDead { get; private set; }

        private bool _subscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            if (!_subscribed && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnPlayerDied += Die;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && NetworkManager.Instance != null)
                NetworkManager.Instance.OnPlayerDied -= Die;
            if (Instance == this) Instance = null;
        }

        // Punto unico de muerte del jugador de ESTE dispositivo (lo llaman el host directo
        // y el cliente por red), asi que el sonido va aca y suena una sola vez para todos.
        public void Die()
        {
            if (IsDead) return;      // el server puede reenviar; no repetir el sonido
            IsDead = true;
            // Momento en el que se "cierra" el resultado de ESTE dispositivo (igual
            // que NocheSuperada en el otro desenlace): si los compañeros siguen
            // juntando reliquias después, no se refleja acá — mismo criterio
            // personal/local que ya tiene Sobrevivio/DeathScreenUI.
            NightResult.MarcarObjetosRecolectados(NightLoot.Total);
            CollectibleProgress.RegistrarIntento(GameSession.Instance != null ? GameSession.Instance.NightIndex : -1,
                                                  NightLoot.Total);
            AudioManager.Musica(c => c.derrotaMuerte, fade: 0.4f);
        }

        // Vuelta a la vida al reiniciar la noche sin cerrar la sesión (ver
        // NightTransition): saca la pantalla de muerte.
        public void Revive() => IsDead = false;

        public static LocalDeath Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("LocalDeath");
                go.AddComponent<LocalDeath>();
            }
            return Instance;
        }
    }
}
