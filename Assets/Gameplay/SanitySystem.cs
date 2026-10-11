using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Cordura SERVER-AUTHORITATIVE, por jugador. El jugador es la camara AR (no una
    // entidad), asi que el estado vive en dicts por clientId (0 = host). La oscuridad
    // no drena cordura: el daño entra de forma explicita desde las amenazas. El server
    // envia a cada cliente su valor (PlayerSanity); al host se lo setea local.
    //
    // Wiring: poner en un GameObject de SampleScene (junto al BatterySpawnManager).
    // Solo actua en el host. Lee la dificultad de GameSession.SelectedNight.
    public class SanitySystem : MonoBehaviour
    {
        public static SanitySystem Instance { get; private set; }

        [Tooltip("Cada cuanto (s) el server recalcula y envia la cordura. El drenaje se " +
                 "acumula con dt real; esto solo limita la frecuencia de red.")]
        [SerializeField] private float _sendInterval = 0.2f;

        private readonly Dictionary<uint, float> _sanity = new();
        private float _sendTimer;
        private bool  _running;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnGameStarted += HandleGameStarted;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnGameStarted -= HandleGameStarted;
            if (Instance == this) Instance = null;
        }

        private NightConfig Night =>
            GameSession.Instance != null ? GameSession.Instance.SelectedNight : null;

        private void HandleGameStarted()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            _sanity.Clear();
            _running = true;
            LocalSanity.Ensure();
        }

        // Corta la noche sin cerrar la sesión (ver Gameplay.NightTransition).
        public void StopRun()
        {
            _running = false;
            _sanity.Clear();
        }

        private void Update()
        {
            if (!_running) return;
            var net = NetworkManager.Instance;
            if (net == null || !net.IsServer) return;

            _sendTimer -= Time.deltaTime;
            bool send = _sendTimer <= 0f;
            if (send) _sendTimer = _sendInterval;

            var  night = Night;
            float max  = night != null ? night.sanityMax               : 100f;
            Tick(0, max, send);
            foreach (var cid in net.ConnectedClients) Tick(cid, max, send);
        }

        private void Tick(uint clientId, float max, bool send)
        {
            EnsurePlayer(clientId, max);
            if (!send) return;
            Publish(clientId, max);
        }

        // ── API server para otros sistemas (Arbmos) ───────────────────────────

        // True si la cordura de ese jugador ya llego a cero (gatillo de la fase letal
        // del Arbmos). Si aun no se registro, se asume que no (arranca en el maximo).
        public bool IsAtZero(uint clientId) =>
            _sanity.TryGetValue(clientId, out var s) && s <= 0f;

        // Aplica un golpe autoritativo y publica el resultado inmediatamente. Devuelve
        // true unicamente cuando este golpe produjo el cruce de cordura positiva a cero.
        public bool ServerApplyDamage(uint clientId, float amount)
        {
            var net = NetworkManager.Instance;
            if (net == null || !net.IsServer) return false;
            float max = Night != null ? Night.sanityMax : 100f;
            EnsurePlayer(clientId, max);
            float before = _sanity[clientId];
            _sanity[clientId] = Mathf.Max(0f, before - Mathf.Max(0f, amount));
            Publish(clientId, max);
            return before > 0f && _sanity[clientId] <= 0f;
        }

        private void EnsurePlayer(uint clientId, float max)
        {
            if (!_sanity.ContainsKey(clientId)) _sanity[clientId] = max;
        }

        private void Publish(uint clientId, float max)
        {
            float value = _sanity[clientId];
            if (clientId == 0) LocalSanity.Ensure().Set(value, max);
            else NetworkManager.Instance.ServerSendSanity(clientId, value, max);
        }
    }
}
