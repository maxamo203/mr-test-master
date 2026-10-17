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

        // Retrato del asesino: copia SÓLO visual (mallas en la pose del momento de la
        // muerte, sin scripts ni colliders) colgada de la cámara, para que el jugador vea
        // en primera persona qué lo mató aunque mueva la cabeza. El asesino real sigue
        // su simulación (el Sorken va por otro jugador, Veleth se va): sin la copia se
        // escapaba del cuadro y a veces no se llegaba a ver qué había pasado.
        // Por ahora queda quieto; cuando haya animaciones de muerte, van acá.
        private GameObject _retrato;
        private readonly List<Mesh> _mallasRetrato = new();
        private Vector3 _retratoDesde, _retratoHasta;
        private Quaternion _retratoRotDesde, _retratoRotHasta;

        private const float RevealSeconds = 1.45f;
        private const float ShakeDegrees = 0.7f;
        private const float FaceDistance = 0.38f;
        // Con retrato: distancia de la cara a la cámara (cabeza y hombros en cuadro) y
        // cuánto se lo deja ver, ya quieto, antes de la pantalla de muerte.
        private const float RetratoDistancia = 0.55f;
        private const float RetratoSostenSeconds = 2.5f;

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

            // Red de seguridad: muerto y sin pantalla de muerte bastante después de lo que
            // tarda la presentación (algo de la secuencia falló en el dispositivo). Sin esto
            // la noche quedaba "congelada": el jugador muerto para el juego (reloj parado,
            // sin entidades) pero sin MORISTE ni forma de seguir.
            if (IsDead && !PresentationReady &&
                Time.unscaledTime - _muerteEn > RevealSeconds + RetratoSostenSeconds + 3f)
            {
                Debug.LogWarning("[LocalDeath] La pantalla de muerte no apareció a tiempo: se fuerza.");
                if (_sequence != null) { StopCoroutine(_sequence); _sequence = null; }
                try { HideKiller(); } catch (System.Exception e) { Debug.LogException(e); }
                PresentationReady = true;
            }
        }

        private float _muerteEn;

        private void LateUpdate()
        {
            if (_retrato != null)
            {
                // Coords locales a la cámara: el tracking AR la sigue moviendo y el
                // retrato la acompaña. Se desliza desde donde estaba el asesino hasta
                // quedar delante de la cara, con el mismo temblor que el zoom de siempre.
                float p = Mathf.Clamp01((Time.unscaledTime - _sequenceStartedAt) / RevealSeconds);
                p = p * p * (3f - 2f * p);
                float tt = Time.unscaledTime;
                var temblor = Quaternion.Euler(
                    (Mathf.PerlinNoise(tt * 7.1f, 0.17f) - .5f) * 2f * ShakeDegrees,
                    (Mathf.PerlinNoise(0.63f, tt * 6.4f) - .5f) * 2f * ShakeDegrees, 0f);
                _retrato.transform.localPosition = Vector3.Lerp(_retratoDesde, _retratoHasta, p);
                _retrato.transform.localRotation =
                    temblor * Quaternion.Slerp(_retratoRotDesde, _retratoRotHasta, p);
                return;
            }

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
            if (IsDead) AudioManager.CancelarPresentacionMuerte();
            DestruirRetrato();
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
            _muerteEn = Time.unscaledTime;
            _killerNetworkId = killerNetworkId;
            // La presentación (sonido, copia del asesino) no puede cortar Die: si algo de eso
            // tira una excepción en el dispositivo, igual se termina la noche y sale MORISTE.
            try { AudioManager.PresentarMuerte(ResolverTipoAsesino(killerNetworkId)); }
            catch (System.Exception e) { Debug.LogException(e); }
            // Sin asesino que copiar (id desconocido, entidad ya destruida) queda el
            // zoom de siempre con la cámara congelada.
            bool retrato = false;
            try { retrato = CrearRetrato(killerFacePosition); }
            catch (System.Exception e) { Debug.LogException(e); DestruirRetrato(); }
            if (!retrato) ZoomTowardsFace(killerFacePosition);

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
            DestruirRetrato();
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
            // Con retrato se lo deja ver un rato ya quieto delante de la cara; el zoom de
            // siempre pasa a la pantalla apenas termina.
            float espera = RevealSeconds + (_retrato != null ? RetratoSostenSeconds : 0f);
            yield return new WaitForSecondsRealtime(espera);
            try { HideKiller(); } catch (System.Exception e) { Debug.LogException(e); }
            PresentationReady = true;
            _sequence = null;
        }

        private void HideKiller()
        {
            if (_hiddenKillerRenderers.Count > 0) return;   // ya oculto (retrato)
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

        // Arma el retrato del asesino colgado de la cámara y oculta al real (que sigue
        // moviéndose y si no aparecería dos veces). false = no había nada que copiar.
        private bool CrearRetrato(Vector3 facePosition)
        {
            var cam = Camera.main;
            if (cam == null || _killerNetworkId == 0 || EntityRegistry.Instance == null ||
                !EntityRegistry.Instance.TryGet(_killerNetworkId, out var killer) || killer == null)
                return false;

            var origen = killer.transform;
            var raiz = new GameObject("RetratoAsesino");
            raiz.transform.SetPositionAndRotation(origen.position, origen.rotation);
            // Registrado desde ya: si algo de abajo tira una excepción, DestruirRetrato (en el
            // catch de Die) también se lleva lo que quedó a medio armar.
            _retrato = raiz;

            int copiados = 0;
            foreach (var r in killer.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;

                Mesh malla;
                bool horneada = false;
                if (r is SkinnedMeshRenderer smr)
                {
                    if (smr.sharedMesh == null) continue;
                    // Pose del frame de la muerte, con la escala ya aplicada.
                    malla = new Mesh { name = smr.sharedMesh.name + " (retrato)" };
                    smr.BakeMesh(malla, true);
                    _mallasRetrato.Add(malla);
                    horneada = true;
                }
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    malla = mf.sharedMesh;
                }
                else continue;   // partículas, líneas, etc.: no se congelan

                var go = new GameObject(r.name);
                go.layer = r.gameObject.layer;
                go.transform.SetParent(raiz.transform, false);
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                go.transform.localScale = horneada ? Vector3.one : r.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = malla;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                copiados++;
            }

            if (copiados == 0) { Destroy(raiz); _retrato = null; return false; }

            // Pose final, en coords de la cámara: mirándola de frente, con la cara a
            // RetratoDistancia sobre el eje de la vista.
            Vector3 caraLocal = raiz.transform.InverseTransformPoint(facePosition);
            _retratoRotHasta = Quaternion.LookRotation(Vector3.back, Vector3.up);
            _retratoHasta = Vector3.forward * RetratoDistancia - _retratoRotHasta * caraLocal;

            raiz.transform.SetParent(cam.transform, true);
            _retratoDesde = raiz.transform.localPosition;
            _retratoRotDesde = raiz.transform.localRotation;
            _retrato = raiz;
            _sequenceStartedAt = Time.unscaledTime;

            HideKiller();
            return true;
        }

        private void DestruirRetrato()
        {
            if (_retrato != null) Destroy(_retrato);
            _retrato = null;
            foreach (var m in _mallasRetrato) if (m != null) Destroy(m);
            _mallasRetrato.Clear();
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
            DestruirRetrato();
            MostrarAsesino();
            IsDead = false;
            PresentationReady = false;
            Espectando = false;
            _nocheTerminada = false;
            _lockCamera = false;
            _camera = null;
            _killerNetworkId = 0;
            AudioManager.CancelarPresentacionMuerte();
            Time.timeScale = 1f;
        }

        private static byte ResolverTipoAsesino(uint networkId)
        {
            if (networkId == 0 || EntityRegistry.Instance == null ||
                !EntityRegistry.Instance.TryGet(networkId, out var killer) || killer == null)
                return 0;
            return killer.EntityTypeId;
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
