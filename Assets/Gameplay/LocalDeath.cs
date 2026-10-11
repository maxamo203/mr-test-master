using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Presentación local: inmoviliza la cámara, se acerca a la cara del atacante y
    // oculta esa copia local recién al abrir la pantalla final.
    [DefaultExecutionOrder(10000)]
    public class LocalDeath : MonoBehaviour
    {
        public static LocalDeath Instance { get; private set; }
        public bool IsDead { get; private set; }
        public bool PresentationReady { get; private set; }

        private bool _subscribed;
        private Camera _camera;
        private Vector3 _cameraStart;
        private Vector3 _cameraEnd;
        private Vector3 _facePosition;
        private float _sequenceStartedAt;
        private bool _lockCamera;
        private Coroutine _sequence;
        private readonly List<Renderer> _hiddenKillerRenderers = new();
        private uint _killerNetworkId;

        private const float RevealSeconds = 1.45f;
        private const float ShakeDegrees = 0.7f;
        private const float FaceDistance = 0.38f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            if (!_subscribed && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnPlayerDied += DieFromNetwork;
                _subscribed = true;
            }
        }

        private void LateUpdate()
        {
            if (!_lockCamera || _camera == null) return;

            float elapsed = Time.unscaledTime - _sequenceStartedAt;
            float progress = Mathf.Clamp01(elapsed / RevealSeconds);
            progress = progress * progress * (3f - 2f * progress); // smoothstep
            Vector3 position = Vector3.Lerp(_cameraStart, _cameraEnd, progress);
            Vector3 direction = _facePosition - position;
            Quaternion look = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : _camera.transform.rotation;

            float t = Time.unscaledTime;
            float pitch = (Mathf.PerlinNoise(t * 7.1f, 0.17f) - .5f) * 2f * ShakeDegrees;
            float yaw = (Mathf.PerlinNoise(0.63f, t * 6.4f) - .5f) * 2f * ShakeDegrees;
            _camera.transform.SetPositionAndRotation(position, look * Quaternion.Euler(pitch, yaw, 0f));
        }

        private void OnDestroy()
        {
            if (_subscribed && NetworkManager.Instance != null)
                NetworkManager.Instance.OnPlayerDied -= DieFromNetwork;
            if (Instance == this) Instance = null;
        }

        private void DieFromNetwork(PlayerDeathMsg death) =>
            Die(death.KillerFacePosition, death.KillerNetworkId, death.AllPlayersDead);

        public void Die(Vector3 killerFacePosition, uint killerNetworkId, bool allPlayersDead)
        {
            if (IsDead)
            {
                if (allPlayersDead) StopGameplayForAll();
                return;
            }

            IsDead = true;
            // Momento en el que se "cierra" el resultado de ESTE dispositivo (igual
            // que NocheSuperada en el otro desenlace): si los compañeros siguen
            // juntando reliquias después, no se refleja acá — mismo criterio
            // personal/local que ya tiene Sobrevivio/DeathScreenUI.
            NightResult.MarcarObjetosRecolectados(NightLoot.Total);
            CollectibleProgress.RegistrarIntento(GameSession.Instance != null ? GameSession.Instance.NightIndex : -1,
                                                  NightLoot.Total);
            PresentationReady = false;
            _killerNetworkId = killerNetworkId;
            AudioManager.Musica(c => c.derrotaMuerte, fade: 0.4f);
            ZoomTowardsFace(killerFacePosition);

            if (allPlayersDead) StopGameplayForAll();
            _sequence = StartCoroutine(ShowScreenAfterReveal());
        }

        private static void StopGameplayForAll()
        {
            NightTransition.DetenerSistemas();
            Time.timeScale = 0f;
        }

        private void ZoomTowardsFace(Vector3 facePosition)
        {
            _camera = Camera.main;
            if (_camera == null) return;

            _cameraStart = _camera.transform.position;
            _facePosition = facePosition;
            Vector3 toFace = facePosition - _cameraStart;
            if (toFace.sqrMagnitude < 0.0001f) toFace = _camera.transform.forward;
            _cameraEnd = facePosition - toFace.normalized * FaceDistance;
            _sequenceStartedAt = Time.unscaledTime;
            _lockCamera = true;
        }

        private IEnumerator ShowScreenAfterReveal()
        {
            yield return new WaitForSecondsRealtime(RevealSeconds);
            HideKiller();
            PresentationReady = true;
            _sequence = null;
        }

        private void HideKiller()
        {
            if (_killerNetworkId == 0 || EntityRegistry.Instance == null ||
                !EntityRegistry.Instance.TryGet(_killerNetworkId, out var killer) || killer == null)
                return;

            foreach (var renderer in killer.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                renderer.enabled = false;
                _hiddenKillerRenderers.Add(renderer);
            }
        }

        public void Revive()
        {
            if (_sequence != null) StopCoroutine(_sequence);
            _sequence = null;
            foreach (var renderer in _hiddenKillerRenderers)
                if (renderer != null) renderer.enabled = true;
            _hiddenKillerRenderers.Clear();
            IsDead = false;
            PresentationReady = false;
            _lockCamera = false;
            _camera = null;
            _killerNetworkId = 0;
            Time.timeScale = 1f;
        }

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
