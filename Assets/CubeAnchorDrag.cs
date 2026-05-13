using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

[RequireComponent(typeof(Collider))]
public class CubeAnchorDrag : MonoBehaviour
{
    [Header("Referencias")]
    public Camera arCamera;
    public ARRaycastManager raycastManager;
    public ARAnchorManager anchorManager;
    public ARPlaneManager planeManager;

    [Header("Configuración")]
    [Tooltip("Distancia por defecto desde la cámara si no se detecta superficie")]
    public float fallbackDistance = 1.5f;
    [Tooltip("Altura del plano virtual horizontal para fallback (en metros, relativo al origen)")]
    public float fallbackPlaneHeight = -0.5f;

    private bool _isDragging = false;
    private int _activeTouchId = -1;
    private ARAnchor _currentAnchor;
    private readonly List<ARRaycastHit> _arHits = new List<ARRaycastHit>();

    void Awake()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (raycastManager == null) raycastManager = FindFirstObjectByType<ARRaycastManager>();
        if (anchorManager == null) anchorManager = FindFirstObjectByType<ARAnchorManager>();
        if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
    }

    void OnEnable()
    {
        ETouch.EnhancedTouchSupport.Enable();
    }

    void OnDisable()
    {
        ETouch.EnhancedTouchSupport.Disable();
    }

    void Update()
    {
        var touches = ETouch.Touch.activeTouches;

        if (!_isDragging)
        {
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                if (t.phase == TouchPhase.Began && TryBeginDrag(t.screenPosition))
                {
                    _activeTouchId = t.touchId;
                    break;
                }
            }
        }
        else
        {
            bool stillActive = false;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                if (t.touchId != _activeTouchId) continue;
                stillActive = true;

                if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                {
                    UpdateDragPosition(t.screenPosition);
                }
                else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    EndDrag();
                }
                break;
            }
            if (!stillActive) EndDrag();
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 mp = mouse.position.ReadValue();
            if (!_isDragging && mouse.leftButton.wasPressedThisFrame)
            {
                if (TryBeginDrag(mp)) _activeTouchId = -999;
            }
            else if (_isDragging && _activeTouchId == -999)
            {
                if (mouse.leftButton.isPressed) UpdateDragPosition(mp);
                else EndDrag();
            }
        }
#endif
    }

    bool TryBeginDrag(Vector2 screenPos)
    {
        if (arCamera == null) return false;
        Ray ray = arCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f) && hit.transform == transform)
        {
            _isDragging = true;
            DetachAnchor();
            return true;
        }
        return false;
    }

    void UpdateDragPosition(Vector2 screenPos)
    {
        if (TryGetARPlanePoint(screenPos, out Vector3 arPoint, out Quaternion arRot))
        {
            transform.SetPositionAndRotation(arPoint, arRot);
            return;
        }

        if (TryGetFallbackPoint(screenPos, out Vector3 fallbackPoint))
        {
            transform.position = fallbackPoint;
        }
    }

    bool TryGetARPlanePoint(Vector2 screenPos, out Vector3 point, out Quaternion rotation)
    {
        point = Vector3.zero;
        rotation = Quaternion.identity;
        if (raycastManager == null) return false;

        if (raycastManager.Raycast(screenPos, _arHits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
        {
            var pose = _arHits[0].pose;
            point = pose.position + Vector3.up * (transform.localScale.y * 0.5f);
            rotation = pose.rotation;
            return true;
        }
        return false;
    }

    bool TryGetFallbackPoint(Vector2 screenPos, out Vector3 point)
    {
        point = Vector3.zero;
        if (arCamera == null) return false;
        Ray ray = arCamera.ScreenPointToRay(screenPos);

        Plane horizontalPlane = new Plane(Vector3.up, new Vector3(0f, fallbackPlaneHeight, 0f));
        if (horizontalPlane.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter) + Vector3.up * (transform.localScale.y * 0.5f);
            return true;
        }

        point = ray.GetPoint(fallbackDistance);
        return true;
    }

    void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        _activeTouchId = -1;
        TryCreateAnchor();
    }

    void TryCreateAnchor()
    {
        if (anchorManager == null && raycastManager == null) return;

        if (anchorManager != null)
        {
            var anchor = transform.gameObject.GetComponent<ARAnchor>();
            if (anchor == null) anchor = transform.gameObject.AddComponent<ARAnchor>();
            _currentAnchor = anchor;
        }
    }

    void DetachAnchor()
    {
        if (_currentAnchor != null)
        {
            Destroy(_currentAnchor);
            _currentAnchor = null;
        }
        else
        {
            var existing = GetComponent<ARAnchor>();
            if (existing != null) Destroy(existing);
        }
    }
}
