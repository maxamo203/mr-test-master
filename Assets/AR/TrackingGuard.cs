using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Monitorea el estado del ARSession y congela WorldOrigin cuando el tracking
// se degrada (pared lisa, movimiento excesivo), evitando que la geometría
// escaneada flote o quede estática mientras la cámara se mueve.
//
// Estados: OK → Frozen → Recovering → OK
// Ver docs/tracking-stability.md para la especificación completa.
[DefaultExecutionOrder(-30)]
public class TrackingGuard : MonoBehaviour
{
    public static TrackingGuard Instance { get; private set; }

    [SerializeField, Tooltip("Segundos en estado degradado antes de freezar (filtra micro-blips de SLAM)")]
    private float _degradedThreshold = 0.5f;

    [SerializeField, Tooltip("Velocidad de lerp al re-anclar cuando el tracking se recupera")]
    private float _recoveryLerpSpeed = 3f;

    private ARAnchor _currentAnchor;
    private float    _degradedSince = -1f;
    private bool     _recovering    = false;

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    // ARImageAnchor llama esto cada vez que coloca o reemplaza el anchor activo.
    public void SetAnchor(ARAnchor anchor) => _currentAnchor = anchor;

    private void Update()
    {
#if UNITY_EDITOR
        return;
#endif
        var origin = WorldOrigin.Instance;
        if (origin == null || !origin.IsReady) return;

        bool trackingOk = ARSession.state == ARSessionState.SessionTracking;

        if (trackingOk)
        {
            _degradedSince = -1f;

            if (origin.IsFrozen && !_recovering)
                StartCoroutine(RecoverRoutine(origin));
        }
        else
        {
            if (_degradedSince < 0f)
                _degradedSince = Time.time;

            if (!origin.IsFrozen && !_recovering && Time.time - _degradedSince >= _degradedThreshold)
                origin.Freeze();
        }
    }

    private IEnumerator RecoverRoutine(WorldOrigin origin)
    {
        _recovering = true;

        // Esperamos a tener un anchor válido y vivo al cual volver.
        while (_currentAnchor == null || !_currentAnchor.gameObject.activeInHierarchy)
            yield return null;

        var anchorTransform = _currentAnchor.transform;

        // Lerp de posición y rotación desde la pose congelada hasta el anchor.
        while (true)
        {
            if (_currentAnchor == null) { _recovering = false; yield break; }

            float dist  = Vector3.Distance(origin.transform.position, anchorTransform.position);
            float angle = Quaternion.Angle(origin.transform.rotation, anchorTransform.rotation);

            if (dist < 0.01f && angle < 0.5f) break;

            origin.transform.position = Vector3.Lerp(
                origin.transform.position,
                anchorTransform.position,
                Time.deltaTime * _recoveryLerpSpeed);

            origin.transform.rotation = Quaternion.Slerp(
                origin.transform.rotation,
                anchorTransform.rotation,
                Time.deltaTime * _recoveryLerpSpeed);

            yield return null;
        }

        origin.Unfreeze(anchorTransform);
        _recovering = false;
    }
}
