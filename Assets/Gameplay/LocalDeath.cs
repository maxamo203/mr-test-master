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

        // Modo espectador: el jugador muerto ocultó la pantalla de muerte para mirar
        // cómo siguen los demás. Sólo se puede mientras la noche sigue en juego; al
        // terminar (todos muertos o amanecer) se cierra solo y vuelve el overlay.
        public bool Espectando { get; private set; }
        public bool PuedeEspectar => IsDead && PresentationReady && !_nocheTerminada;

        private bool _nocheTerminada;
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
                // Ya estaba muerto (quizás espectando) y acaba de caer el último.
                if (allPlayersDead) { TerminarNoche(); StopGameplayForAll(); }
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

            if (allPlayersDead) { TerminarNoche(); StopGameplayForAll(); }
            _sequence = StartCoroutine(ShowScreenAfterReveal());
        }

        // Ocultar la pantalla de muerte y mirar la partida. Suelta la cámara (vuelve a
        // manejarla el tracking AR) y devuelve la copia local del asesino, que se había
        // ocultado sólo para no tenerlo pegado a la cara detrás del overlay.
        public void Espectar()
        {
            if (!PuedeEspectar) return;
            Espectando = true;
            _lockCamera = false;
            MostrarAsesino();
        }

        public void DejarDeEspectar() => Espectando = false;

        // La noche terminó para todos (cayó el último o amaneció): no hay más que
        // mirar, así que se vuelve al overlay. Lo llaman Die y NightTransition.NocheSuperada.
        public void TerminarNoche()
        {
            _nocheTerminada = true;
            Espectando = false;
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

        private void MostrarAsesino()
        {
            foreach (var renderer in _hiddenKillerRenderers)
                if (renderer != null) renderer.enabled = true;
            _hiddenKillerRenderers.Clear();
        }

        public void Revive()
        {
            if (_sequence != null) StopCoroutine(_sequence);
            _sequence = null;
            MostrarAsesino();
            IsDead = false;
            PresentationReady = false;
            Espectando = false;
            _nocheTerminada = false;
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
