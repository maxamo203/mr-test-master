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
        }

        public void Die() => IsDead = true;

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
