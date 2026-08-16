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

        // Unico punto por el que entra la cordura en este dispositivo (host y cliente), asi
        // que es donde se detectan los cruces de umbral para avisarlos por audio. La cordura
        // solo baja, por eso alcanza con comparar contra el valor anterior.
        public void Set(float value, float max)
        {
            float antes01 = Value01;
            Value = value;
            Max   = max;
            float ahora01 = Value01;

            if (antes01 > 0f && ahora01 <= 0f)
                AudioManager.Sonar(c => c.corduraCero);          // a partir de aca el Arbmos mata
            else if (antes01 > UmbralCorduraBaja && ahora01 <= UmbralCorduraBaja)
                AudioManager.Sonar(c => c.corduraBaja);
        }

        // Fraccion de cordura a la que se avisa "estas en rojo".
        private const float UmbralCorduraBaja = 0.3f;

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
