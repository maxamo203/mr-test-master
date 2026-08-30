using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

namespace Scanner
{
    // Detección automática de paredes (BETA) — ver GameOptions.EscaneoAutoBeta.
    //
    // Usa ARPlaneManager (planos verticales de ARCore) para sugerir paredes: cada
    // plano detectado se dibuja como un quad traslúcido tappeable ("candidato").
    // Un tap lo selecciona (AutoWall_Confirm); CONFIRMAR lo convierte en una
    // WallObject recta con el mismo WallObject.Create que usa el modo manual — así
    // queda editable con los mismos handles/gizmos sin código nuevo de edición.
    // DESCARTAR lo deja como candidato (puede seguir refinándose y re-tocarse).
    //
    // El ARPlaneManager arranca y queda SIEMPRE en PlaneDetectionMode.None/enabled
    // false salvo mientras el modo BETA está activo (StartAutoScan/EndAutoScan):
    // no debe costar ciclos fuera de ese uso puntual (ver CLAUDE.md, regla de
    // performance) y no debe interferir con el resto del escáner manual.
    public class AutoWallBuilder : MonoBehaviour
    {
        [Header("Materiales (opcional; se generan translúcidos si están vacíos)")]
        [SerializeField] private Material _candidateMaterial;
        [SerializeField] private Material _selectedMaterial;

        [Header("Defaults (igual que WallBuilder)")]
        [SerializeField] private float _defaultWidth = 0.15f;
        [Tooltip("Tamaño mínimo (m, ancho o alto) de plano vertical para mostrarlo como candidato.")]
        [SerializeField] private float _minPlaneSize = 0.4f;

        private ScanStateMachine _fsm;
        private ARPlaneManager _planeManager;
        private ARRaycastManager _arRaycast;

        private class Candidate
        {
            public ARPlane plane;
            public GameObject quad;
            public MeshRenderer renderer;
        }

        private readonly Dictionary<TrackableId, Candidate> _candidates = new();
        private TrackableId? _selected;

        private static readonly List<ARRaycastHit> _arHits = new();

        public bool HasSelection => _selected.HasValue;

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            _arRaycast = FindFirstObjectByType<ARRaycastManager>();
            EnsureMaterials();
            EnsurePlaneManager();
        }

        private void OnEnable()
        {
            if (_planeManager != null) _planeManager.trackablesChanged.AddListener(OnPlanesChanged);
        }

        private void OnDisable()
        {
            if (_planeManager != null) _planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
        }

        // ARPlaneManager no viene pre-armado en ScannerScene (a diferencia de
        // SampleScene, que lo usa para oclusión vía ARPlaneOccluder). Lo creamos acá
        // en runtime, igual que LiDARScanner crea su ARMeshManager cuando falta.
        private void EnsurePlaneManager()
        {
            _planeManager = GetComponent<ARPlaneManager>();
            if (_planeManager == null) _planeManager = FindFirstObjectByType<ARPlaneManager>();
            if (_planeManager == null)
            {
                var xrOrigin = FindFirstObjectByType<XROrigin>();
                if (xrOrigin == null)
                {
                    Debug.LogWarning("[AutoWallBuilder] No hay XROrigin en la escena; el modo BETA no puede activarse.");
                    return;
                }
                _planeManager = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
                _planeManager.planePrefab = null;
            }

            // Arranca apagado: solo StartAutoScan() lo prende. requestedDetectionMode
            // en None es lo que de verdad evita que detecte algo (más fuerte que solo
            // enabled=false — ver nota de ARImageAnchor, que también toca .enabled de
            // cualquier ARPlaneManager que encuentre en la escena).
            _planeManager.requestedDetectionMode = PlaneDetectionMode.None;
            _planeManager.enabled = false;
        }

        private void EnsureMaterials()
        {
            var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (sh == null) return;

            if (_candidateMaterial == null)
            {
                _candidateMaterial = new Material(sh) { name = "AutoWallCandidateMat (runtime)" };
                var col = new Color(0.7f, 1f, 1f, 0.35f);
                if (_candidateMaterial.HasProperty("_Color")) _candidateMaterial.color = col;
                _candidateMaterial.renderQueue = 4000;
            }
            if (_selectedMaterial == null)
            {
                _selectedMaterial = new Material(sh) { name = "AutoWallSelectedMat (runtime)" };
                var col = new Color(1f, 0.6f, 0.2f, 0.55f);
                if (_selectedMaterial.HasProperty("_Color")) _selectedMaterial.color = col;
                _selectedMaterial.renderQueue = 4000;
            }
        }

        // ── Activación / salida del modo ────────────────────────────────────────

        public void StartAutoScan()
        {
            if (_planeManager == null) EnsurePlaneManager();
            if (_planeManager == null) return;

            ClearAllCandidates();
            _selected = null;
            _planeManager.requestedDetectionMode = PlaneDetectionMode.Vertical;
            _planeManager.enabled = true;
            _fsm.SetMode(ScannerMode.AutoWall_Scanning);
        }

        public void EndAutoScan()
        {
            if (_planeManager != null)
            {
                _planeManager.requestedDetectionMode = PlaneDetectionMode.None;
                foreach (var p in _planeManager.trackables)
                    if (p != null) p.gameObject.SetActive(false);
                _planeManager.enabled = false;
            }
            ClearAllCandidates();
            _selected = null;
            if (_fsm.Current == ScannerMode.AutoWall_Scanning || _fsm.Current == ScannerMode.AutoWall_Confirm)
                _fsm.SetMode(ScannerMode.Idle);
        }

        // ── Selección / confirmación ────────────────────────────────────────────

        // Llamado por SelectionController cuando hay un tap en AutoWall_Scanning.
        public void TryPickCandidate(Vector2 screenPoint)
        {
            if (_arRaycast == null) return;
            _arHits.Clear();
            if (!_arRaycast.Raycast(screenPoint, _arHits, TrackableType.PlaneWithinPolygon) || _arHits.Count == 0)
                return;

            var id = _arHits[0].trackableId;
            if (!_candidates.TryGetValue(id, out var c)) return;

            _selected = id;
            if (c.renderer != null) c.renderer.sharedMaterial = _selectedMaterial;
            _fsm.SetMode(ScannerMode.AutoWall_Confirm);
        }

        public void ConfirmSelected()
        {
            if (!_selected.HasValue || !_candidates.TryGetValue(_selected.Value, out var c) || c.plane == null)
            {
                DiscardSelected();
                return;
            }

            CreateWallFromPlane(c.plane);
            RemoveCandidate(_selected.Value);
            _selected = null;
            _fsm.SetMode(ScannerMode.AutoWall_Scanning);
        }

        public void DiscardSelected()
        {
            if (_selected.HasValue && _candidates.TryGetValue(_selected.Value, out var c) && c.renderer != null)
                c.renderer.sharedMaterial = _candidateMaterial;
            _selected = null;
            if (_fsm.Current == ScannerMode.AutoWall_Confirm)
                _fsm.SetMode(ScannerMode.AutoWall_Scanning);
        }

        // ── Conversión plano → WallObject ───────────────────────────────────────
        // Geometría compartida con LiveWallDetector (Assets/Gameplay/) — ver PlaneWallMath.

        private void CreateWallFromPlane(ARPlane plane)
        {
            if (!PlaneWallMath.TryComputeWallFromPlane(plane, out var aLocal, out var bLocal, out var height, out var side))
                return;
            WallObject.Create(aLocal, bLocal, height, _defaultWidth, side);
        }

        // ── Candidatos: visualización ───────────────────────────────────────────

        private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            foreach (var p in args.added) RegisterOrUpdate(p);
            foreach (var p in args.updated) RegisterOrUpdate(p);
            foreach (var kvp in args.removed) RemoveCandidate(kvp.Key);
        }

        private void RegisterOrUpdate(ARPlane plane)
        {
            if (plane.alignment != PlaneAlignment.Vertical) return;
            // Confirmada: ya no es candidato, no la re-mostramos aunque ARCore
            // la siga actualizando.
            if (!_candidates.ContainsKey(plane.trackableId) && !PassesMinSize(plane)) return;

            if (!_candidates.TryGetValue(plane.trackableId, out var c) || c.quad == null)
            {
                c = new Candidate { plane = plane };
                c.quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                c.quad.name = $"AutoWallCandidate_{plane.trackableId}";
                c.quad.transform.SetParent(transform, worldPositionStays: false);
                c.renderer = c.quad.GetComponent<MeshRenderer>();
                c.renderer.sharedMaterial = _candidateMaterial;
                c.renderer.shadowCastingMode = ShadowCastingMode.Off;
                c.renderer.receiveShadows = false;
                _candidates[plane.trackableId] = c;
            }

            c.plane = plane;
            c.quad.transform.SetPositionAndRotation(plane.transform.position,
                plane.transform.rotation * Quaternion.Euler(90f, 0f, 0f));
            c.quad.transform.localScale = new Vector3(plane.size.x, plane.size.y, 1f);
        }

        private bool PassesMinSize(ARPlane plane) =>
            plane.size.x >= _minPlaneSize || plane.size.y >= _minPlaneSize;

        private void RemoveCandidate(TrackableId id)
        {
            if (_candidates.TryGetValue(id, out var c))
            {
                if (c.quad != null) Destroy(c.quad);
                _candidates.Remove(id);
            }
            if (_selected == id) _selected = null;
        }

        private void ClearAllCandidates()
        {
            foreach (var c in _candidates.Values)
                if (c.quad != null) Destroy(c.quad);
            _candidates.Clear();
        }

        private void OnDestroy()
        {
            ClearAllCandidates();
            if (_candidateMaterial != null && _candidateMaterial.name.Contains("(runtime)")) Destroy(_candidateMaterial);
            if (_selectedMaterial != null && _selectedMaterial.name.Contains("(runtime)")) Destroy(_selectedMaterial);
        }
    }
}
