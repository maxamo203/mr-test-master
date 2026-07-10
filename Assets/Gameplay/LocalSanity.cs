using UnityEngine;

namespace Gameplay
{
    // Cordura del jugador LOCAL (este dispositivo). El server es autoritativo:
    //  - host  : la setea SanitySystem directo (clientId 0).
    //  - cliente: llega por red (NetworkManager.OnSanityUpdated).
    // La leen el HUD y la distorsion. No es recuperable (el server solo la baja).
    public class LocalSanity : MonoBehaviour
    {
        public static LocalSanity Instance { get; private set; }

        public float Value { get; private set; } = 100f;
        public float Max   { get; private set; } = 100f;
        public float Value01 => Max > 0f ? Mathf.Clamp01(Value / Max) : 0f;

        private bool _subscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            // NetworkManager puede no existir todavia al crearse; suscribir cuando aparezca.
            if (!_subscribed && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnSanityUpdated += Set;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && NetworkManager.Instance != null)
                NetworkManager.Instance.OnSanityUpdated -= Set;
        }

        public void Set(float value, float max) { Value = value; Max = max; }

        public static LocalSanity Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("LocalSanity");
                go.AddComponent<LocalSanity>();
            }
            return Instance;
        }
    }
}
