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
        private GameObject _retrato, _luzRetrato;
        private readonly List<Mesh> _mallasRetrato = new();
        private static MaterialPropertyBlock _bloque;
        private Vector3 _retratoDesde, _retratoHasta;
        private Quaternion _retratoRotDesde, _retratoRotHasta;

        private const float RevealSeconds = 1.45f;
        private const float ShakeDegrees = 0.7f;
        private const float FaceDistance = 0.38f;
        // Los tiempos, el encuadre y el look del retrato están en RetratoAsesinoConfig (Cfg).

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
                Time.unscaledTime - _muerteEn > Mathf.Max(RevealSeconds, Cfg.revelar) + Cfg.sostener + 3f)
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
#if UNITY_EDITOR
                // Tuning en vivo (sólo editor): el encuadre se recalcula cada frame con la
                // config, así se ve al instante lo que se mueve en el Inspector.
                ReencuadrarEnVivo();
#endif
                var cfg = Cfg;
                float p = Mathf.Clamp01((Time.unscaledTime - _sequenceStartedAt) / Mathf.Max(0.01f, cfg.revelar));
                p = p * p * (3f - 2f * p);
                float tt = Time.unscaledTime;
                var temblor = Quaternion.Euler(
                    (Mathf.PerlinNoise(tt * 7.1f, 0.17f) - .5f) * 2f * cfg.temblorGrados,
                    (Mathf.PerlinNoise(0.63f, tt * 6.4f) - .5f) * 2f * cfg.temblorGrados, 0f);
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
            if (Debug.isDebugBuild)
                Debug.Log($"[LocalDeath] Die: asesino id {killerNetworkId}, cara {killerFacePosition}, " +
                          $"todos muertos {allPlayersDead}, Cardboard {MRCardboardController.Activo}.");
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
            float espera = _retrato != null ? Cfg.revelar + Cfg.sostener : RevealSeconds;
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
            {
                if (Debug.isDebugBuild)
                    Debug.LogWarning($"[LocalDeath] Sin retrato: no se encontró al asesino " +
                                     $"(id {_killerNetworkId}, cámara {(cam != null)}). Queda el zoom.");
                return false;
            }

            if (!ArmarRetrato(killer.transform, cam)) return false;
            HideKiller();
            return true;
        }

        // Arma la copia visual de 'origen' colgada de 'cam' (la usan la muerte y la prueba
        // del editor). false = no había mallas visibles que copiar.
        private bool ArmarRetrato(Transform origen, Camera cam)
        {
            DestruirRetrato();   // uno solo a la vez (p. ej. una prueba del editor abierta)
#if UNITY_EDITOR
            EnPrueba = false;
#endif
            var raiz = new GameObject("RetratoAsesino");
            raiz.transform.SetPositionAndRotation(origen.position, origen.rotation);
            // Registrado desde ya: si algo de abajo tira una excepción, DestruirRetrato (en el
            // catch de Die) también se lleva lo que quedó a medio armar.
            _retrato = raiz;

            int copiados = 0;
            foreach (var r in origen.GetComponentsInChildren<Renderer>())
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
                    malla.RecalculateBounds();   // el encuadre mide sobre estos bounds
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
                mr.sharedMaterials = MaterialesRetrato(r.sharedMaterials);
                // Valores por renderer (el revelado de Veleth va por acá): sin copiarlos
                // el material queda en su default, que puede ser invisible. El recorte
                // por altura de la emergencia se apaga: es en coords de mundo y la copia
                // ya no está donde emergía.
                if (r.HasPropertyBlock())
                {
                    _bloque ??= new MaterialPropertyBlock();
                    r.GetPropertyBlock(_bloque);
                    _bloque.SetFloat("_RevealActive", 0f);
                    mr.SetPropertyBlock(_bloque);
                }
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                // La malla horneada puede salir sin la escala de la jerarquía del modelo
                // (el Sorken salía ~90 veces más grande: FBX en cm con 0.01 en un padre).
                // Se corrige midiendo: la copia tiene que ocupar lo mismo que el original
                // en el mundo (sus bounds, que Unity calcula con el skinning aplicado).
                if (horneada)
                {
                    float original = r.bounds.size.magnitude, copia = mr.bounds.size.magnitude;
                    if (original > 1e-4f && copia > 1e-4f)
                    {
                        float k = original / copia;
                        if (k < 0.8f || k > 1.25f)
                        {
                            // Escalar y después recentrar sobre el original (escalar desde
                            // el pivote corre la malla).
                            go.transform.localScale *= k;
                            go.transform.position += r.bounds.center - mr.bounds.center;
                        }
                    }
                }
                copiados++;
            }

            if (copiados == 0)
            {
                if (Debug.isDebugBuild)
                    Debug.LogWarning($"[LocalDeath] Sin retrato: '{origen.name}' no tenía mallas visibles. Queda el zoom.");
                Destroy(raiz);
                _retrato = null;
                return false;
            }

            // Encuadre medido sobre las mallas COPIADAS (no con la cara que manda el server,
            // que incluye el humo): de la cabeza a las rodillas, a la distancia en que eso
            // entra en la vista de la cámara. Con una distancia fija a la cara quedaba
            // demasiado encima (o con la cara mal estimada, fuera de cuadro).
            Encuadrar(raiz.transform, cam, out var centroLocal, out float distancia);

            // Luz propia: sin ella la copia queda a oscuras con la linterna apagada.
            var luz = new GameObject("LuzRetrato").AddComponent<Light>();
            luz.type = LightType.Point;
            luz.range = distancia + 3f;
            luz.intensity = Cfg.luzIntensidad;
            luz.color = new Color(1f, 0.92f, 0.85f);
            luz.shadows = LightShadows.None;
            // La cámara puede venir escalada (el XR Origin está a escala 10): sus coords
            // locales no son metros. Todo se calcula en metros y se divide por su escala
            // al pasarlo a local; sin esto la copia quedaba 10 veces más lejos y fuera de la
            // vista. (Escala uniforme, como la del origin.)
            float escalaCam = Mathf.Max(1e-4f, cam.transform.lossyScale.x);

            luz.transform.SetParent(cam.transform, false);
            luz.transform.localPosition = new Vector3(0f, 0.2f, 0.1f) / escalaCam;
            _luzRetrato = luz.gameObject;

            // Pose final, en coords de la cámara: mirándola de frente, con el centro del
            // encuadre sobre el eje de la vista. centroLocal está en metros (la raíz no
            // tiene escala en el mundo), igual que la distancia.
            _retratoRotHasta = Quaternion.LookRotation(Vector3.back, Vector3.up);
            _retratoHasta = PoseFinal(cam, centroLocal, distancia, escalaCam);

            // worldPositionStays: la raíz queda con escala local 1/escalaCam, o sea 1 en el mundo.
            raiz.transform.SetParent(cam.transform, true);
            _retratoDesde = raiz.transform.localPosition;
            _retratoRotDesde = raiz.transform.localRotation;
            _retrato = raiz;
            _sequenceStartedAt = Time.unscaledTime;

            if (Debug.isDebugBuild)
            {
                Debug.Log($"[LocalDeath] Retrato de '{origen.name}': {copiados} mallas, " +
                          $"a {distancia:0.00} m, centro local {centroLocal}, " +
                          $"shader encima de todo: {(_shaderRetrato != null && _shaderRetrato.isSupported)}.");
                Debug.Log($"[LocalDeath] Cámara '{cam.name}' activa={cam.isActiveAndEnabled} " +
                          $"rt={(cam.targetTexture != null ? cam.targetTexture.name : "pantalla")} " +
                          $"capa {LayerMask.LayerToName(raiz.transform.GetChild(0).gameObject.layer)} " +
                          $"en máscara={(cam.cullingMask & (1 << raiz.transform.GetChild(0).gameObject.layer)) != 0} " +
                          $"near={cam.nearClipPlane:0.00} cámaras={Camera.allCamerasCount} " +
                          $"escala={cam.transform.lossyScale}.");
                StartCoroutine(DiagnosticarRetrato());
            }
            return true;
        }

        // Posición final de la raíz en coords locales de la cámara: el centro del encuadre
        // sobre el rayo que pasa por el CENTRO DE LA PANTALLA (más el corrimiento horizontal
        // de la config), a 'distancia' m. No sobre el eje de la cámara: en el dispositivo
        // AR Foundation usa las intrínsecas reales, cuyo centro óptico no cae en el medio de
        // la pantalla, y el retrato quedaba corrido (centrado en el editor, a la izquierda
        // en el iPhone). centroLocal en metros (la raíz no tiene escala en el mundo); se
        // divide por la escala de la cámara al pasar a su local.
        private Vector3 PoseFinal(Camera cam, Vector3 centroLocal, float distancia, float escalaCam)
        {
            var rayo = cam.ViewportPointToRay(new Vector3(0.5f + Cfg.corrimientoHorizontal, 0.5f, 0f));
            Vector3 dir = cam.transform.InverseTransformDirection(rayo.direction);
            if (dir.z < 1e-3f) dir = Vector3.forward;
            // Escalado para que su profundidad (z de la cámara) sea 'distancia'.
            dir /= dir.z;
            return (dir * distancia - _retratoRotHasta * centroLocal) / escalaCam;
        }

        private static RetratoAsesinoConfig Cfg => RetratoAsesinoConfig.Actual;

#if UNITY_EDITOR
        // ── Prueba en el editor (menú Mortuorium > Retrato del asesino) ─────────────
        // Arma el retrato de un prefab sin morir y lo deja delante de la cámara hasta
        // CerrarPrueba, para tunear RetratoAsesinoConfig con el Play corriendo.
        public bool EnPrueba { get; private set; }

        public bool ProbarRetrato(GameObject prefab)
        {
            var cam = Camera.main;
            if (prefab == null || cam == null || IsDead) return false;
            CerrarPrueba();

            // Instancia descartable de donde copiar las mallas: unos metros delante, de
            // frente, para que el deslizamiento se vea como en la muerte de verdad.
            var adelante = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (adelante.sqrMagnitude < 1e-4f) adelante = Vector3.forward;
            adelante.Normalize();
            var inst = Instantiate(prefab, cam.transform.position + adelante * 3f - Vector3.up * 1.5f,
                                   Quaternion.LookRotation(-adelante, Vector3.up));
            bool ok = ArmarRetrato(inst.transform, cam);
            Destroy(inst);
            EnPrueba = ok;
            return ok;
        }

        public void CerrarPrueba()
        {
            if (!EnPrueba) return;
            EnPrueba = false;
            DestruirRetrato();
        }

        private void ReencuadrarEnVivo()
        {
            var cam = _retrato.transform.parent != null ? _retrato.transform.parent.GetComponent<Camera>() : null;
            if (cam == null) return;
            Encuadrar(_retrato.transform, cam, out var centro, out float distancia);
            _retratoHasta = PoseFinal(cam, centro, distancia, Mathf.Max(1e-4f, cam.transform.lossyScale.x));
            if (_luzRetrato != null && _luzRetrato.TryGetComponent<Light>(out var luz))
            {
                luz.intensity = Cfg.luzIntensidad;
                luz.range = distancia + 3f;
            }
        }
#endif

        // PROVISORIO (dev build): ¿alguna cámara llegó a dibujar el retrato? isVisible es
        // true si algún render del frame lo incluyó (frustum + culling), así distingue
        // "no se dibuja" de "se dibuja pero algo lo tapa / queda fuera de la vista".
        private IEnumerator DiagnosticarRetrato()
        {
            yield return new WaitForSecondsRealtime(1.0f);
            for (int vez = 0; vez < 2; vez++)
            {
                if (_retrato == null) { Debug.Log("[LocalDeath] Diagnóstico: el retrato ya no existe."); yield break; }
                var cam = Camera.main;
                int visibles = 0, total = 0;
                foreach (var mr in _retrato.GetComponentsInChildren<MeshRenderer>(true))
                {
                    total++;
                    if (mr.isVisible) visibles++;
                }
                var b = _retrato.GetComponentInChildren<MeshRenderer>(true);
                Vector3 enCam = cam != null && b != null ? cam.transform.InverseTransformPoint(b.bounds.center) : Vector3.zero;
                Vector3 vp = cam != null && b != null ? cam.WorldToViewportPoint(b.bounds.center) : Vector3.zero;
                Debug.Log($"[LocalDeath] Diagnóstico t+{1f + vez * 1.5f:0.0}s: visibles {visibles}/{total}, " +
                          $"activo={_retrato.activeInHierarchy}, padre={(_retrato.transform.parent != null ? _retrato.transform.parent.name : "ninguno")}, " +
                          $"centro en cámara {enCam}, viewport {vp}, tamaño {(b != null ? b.bounds.size : Vector3.zero)}, " +
                          $"camMain='{(cam != null ? cam.name : "null")}', timeScale={Time.timeScale}.");
                yield return new WaitForSecondsRealtime(1.5f);
            }
        }

        // Encuadre del retrato. Caja de las mallas copiadas en coords de la raíz (= del
        // asesino, así "alto" es su alto aunque la cámara esté inclinada); se encuadra sólo
        // la parte de arriba (Cfg.encuadreAlto del alto; 0.2 ≈ la cabeza), con margen, contra
        // el campo visual REAL — el de la matriz de proyección, que AR Foundation pisa con
        // las intrínsecas de la cámara. Sólo cuenta el alto: el ancho del cuerpo (alas,
        // brazos) no es de la cabeza y la alejaba. Valores en RetratoAsesinoConfig.
        private static void Encuadrar(Transform raiz, Camera cam, out Vector3 centro, out float distancia)
        {
            bool hay = false;
            Bounds b = default;
            foreach (var mf in raiz.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                var mb = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var esquina = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var p = raiz.InverseTransformPoint(mf.transform.TransformPoint(esquina));
                    if (!hay) { b = new Bounds(p, Vector3.zero); hay = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!hay) b = new Bounds(Vector3.up, new Vector3(0.6f, 2f, 0.6f));

            var cfg = Cfg;
            float alto = b.size.y * cfg.encuadreAlto;
            centro = new Vector3(b.center.x, b.max.y - alto * (0.5f - cfg.corrimientoVertical), b.center.z);

            var proj = cam.projectionMatrix;
            float tanV = proj.m11 > 1e-4f ? 1f / proj.m11 : Mathf.Tan(30f * Mathf.Deg2Rad);

            float dCabeza = alto * 0.5f * cfg.margen / tanV;

            // Nada del cuerpo puede quedar más cerca que el plano de recorte cercano: lo que
            // sobresale hacia la cámara (alas, brazos; el Sorken tiene más de 1 m de fondo)
            // se cortaba en polígonos con la cabeza a 30 cm. Se toma el mayor fondo desde el
            // centro hacia cualquiera de los dos lados (no depende de hacia dónde mire el modelo).
            float fondo = Mathf.Max(b.max.z - centro.z, centro.z - b.min.z);
            float dSinCorte = fondo + cam.nearClipPlane + cfg.margenCorte;

            distancia = Mathf.Clamp(Mathf.Max(dCabeza, dSinCorte), cfg.distanciaMin,
                                    Mathf.Max(cfg.distanciaMin, cfg.distanciaMax));
        }

        // Materiales del retrato: los originales pasados al shader que dibuja por encima de
        // todo (Resources/RetratoAsesino.shader) — sin eso la profundidad real de ARKit, las
        // paredes escaneadas o un plano AR más cerca que la copia la tapaban. Si el shader
        // no está, quedan los originales.
        private static Shader _shaderRetrato;
        private readonly List<Material> _materialesRetrato = new();

        private Material[] MaterialesRetrato(Material[] originales)
        {
            if (_shaderRetrato == null) _shaderRetrato = Resources.Load<Shader>("RetratoAsesino");
            if (_shaderRetrato == null || !_shaderRetrato.isSupported) return originales;

            var copia = new Material[originales.Length];
            for (int i = 0; i < originales.Length; i++)
            {
                var m = new Material(_shaderRetrato);
                if (originales[i] != null) m.CopyPropertiesFromMaterial(originales[i]);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
                _materialesRetrato.Add(m);
                copia[i] = m;
            }
            return copia;
        }

        // Retrato a la vista: DarknessOverlay no oscurece (si no, del asesino sólo se veía
        // lo que caía en el cono de la linterna, un circulito en el centro).
        public static bool MostrandoRetrato => Instance != null && Instance._retrato != null;

        private void DestruirRetrato()
        {
            if (_retrato != null) Destroy(_retrato);
            _retrato = null;
            if (_luzRetrato != null) Destroy(_luzRetrato);
            _luzRetrato = null;
            foreach (var m in _mallasRetrato) if (m != null) Destroy(m);
            _mallasRetrato.Clear();
            foreach (var m in _materialesRetrato) if (m != null) Destroy(m);
            _materialesRetrato.Clear();
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
#if UNITY_EDITOR
            EnPrueba = false;
#endif
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
