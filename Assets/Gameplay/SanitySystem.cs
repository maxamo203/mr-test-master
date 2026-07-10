using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Cordura SERVER-AUTHORITATIVE, por jugador. El jugador es la camara AR (no una
    // entidad), asi que el estado vive en dicts por clientId (0 = host). Cada jugador
    // drena cordura cuando su linterna esta apagada mas de flashlightOffThreshold; no
    // se recupera. El server envia a cada cliente su valor (PlayerSanity); al host se
    // lo setea local en LocalSanity.
    //
    // Wiring: poner en un GameObject de SampleScene (junto al BatterySpawnManager).
    // Solo actua en el host. Lee la dificultad de GameSession.SelectedNight.
    public class SanitySystem : MonoBehaviour
    {
        public static SanitySystem Instance { get; private set; }

        [Tooltip("Cada cuanto (s) el server recalcula y envia la cordura. El drenaje se " +
                 "acumula con dt real; esto solo limita la frecuencia de red.")]
        [SerializeField] private float _sendInterval = 0.2f;

        private readonly Dictionary<uint, float> _sanity   = new();
        private readonly Dictionary<uint, float> _offTimer = new();
        private Flashlight _hostFlashlight;
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
        }

        private NightConfig Night =>
            GameSession.Instance != null ? GameSession.Instance.SelectedNight : null;

        private void HandleGameStarted()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            _sanity.Clear();
            _offTimer.Clear();
            _running = true;
            LocalSanity.Ensure();
        }

        private void Update()
        {
            if (!_running) return;
            var net = NetworkManager.Instance;
            if (net == null || !net.IsServer) return;

            float dt = Time.deltaTime;
            _sendTimer -= dt;
            bool send = _sendTimer <= 0f;
            if (send) _sendTimer = _sendInterval;

            var  night = Night;
            float max  = night != null ? night.sanityMax               : 100f;
            float thr  = night != null ? night.flashlightOffThreshold  : 5f;
            float rate = night != null ? night.sanityDrainPerSecond     : 4f;

            // Host (clientId 0): linterna local.
            Tick(0, HostFlashlightOn(), dt, max, thr, rate, send);

            // Clientes remotos: linterna reportada (desconocida => encendida, no drena).
            foreach (var cid in net.ConnectedClients)
            {
                bool on = !net.TryGetClientFlashlightOn(cid, out var v) || v;
                Tick(cid, on, dt, max, thr, rate, send);
            }
        }

        private void Tick(uint clientId, bool flashOn, float dt,
                          float max, float thr, float rate, bool send)
        {
            if (!_sanity.ContainsKey(clientId)) { _sanity[clientId] = max; _offTimer[clientId] = 0f; }

            if (flashOn)
            {
                _offTimer[clientId] = 0f;            // apagon interrumpido: reinicia la ventana
            }
            else
            {
                _offTimer[clientId] += dt;
                if (_offTimer[clientId] >= thr)      // apagada mas que el umbral => drena
                    _sanity[clientId] = Mathf.Max(0f, _sanity[clientId] - rate * dt);
            }

            if (!send) return;
            float s = _sanity[clientId];
            if (clientId == 0) LocalSanity.Ensure().Set(s, max);        // host: local
            else               NetworkManager.Instance.ServerSendSanity(clientId, s, max);
        }

        private bool HostFlashlightOn()
        {
            if (_hostFlashlight == null) _hostFlashlight = FindFirstObjectByType<Flashlight>();
            return _hostFlashlight != null && _hostFlashlight.isOn;
        }
    }
}
