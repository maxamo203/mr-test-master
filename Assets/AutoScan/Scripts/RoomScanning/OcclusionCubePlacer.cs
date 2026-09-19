using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// A single, on-demand occlusion test cube. Enter PLACE CUBE mode, tap the screen
    /// to position the cube (it follows each tap), then CONFIRM to lock it; DELETE
    /// removes it. Only one cube ever exists. Placement uses the AR raycast cascade
    /// (Depth → Plane → FeaturePoint) with a fixed-distance fallback, so it lands a
    /// point even on featureless walls.
    /// </summary>
    public class OcclusionCubePlacer : MonoBehaviour
    {
        [SerializeField] ARRaycastManager arRaycast;
        [Tooltip("Distance (m) along the camera ray when nothing is hit.")]
        [SerializeField] float fallbackDistance = 1.5f;
        [SerializeField] float cubeSize = 0.3f;
        [SerializeField] Color cubeColor = new Color(1f, 0.1f, 0.8f);

        Camera _cam;
        GameObject _cube;
        bool _placing;
        GUIStyle _style;

        static readonly List<ARRaycastHit> _hits = new();

        void Awake()
        {
            if (arRaycast == null) arRaycast = GetComponent<ARRaycastManager>();
        }

        void OnEnable()  => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Update()
        {
            _cam = Camera.main;
            if (_cam == null || !_placing) return;

            foreach (var t in Touch.activeTouches)
            {
                if (t.phase != UnityEngine.InputSystem.TouchPhase.Began) continue;
                if (IsOverButtons(t.screenPosition)) continue; // don't place when tapping UI
                MoveCubeTo(t.screenPosition);
                break;
            }
        }

        void MoveCubeTo(Vector2 screenPos)
        {
            Vector3 pos;
            var flags = TrackableType.Depth | TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint;
            if (arRaycast != null && arRaycast.Raycast(screenPos, _hits, flags) && _hits.Count > 0)
                pos = _hits[0].pose.position;
            else
            {
                var ray = _cam.ScreenPointToRay(screenPos);
                pos = ray.origin + ray.direction * fallbackDistance;
            }
            EnsureCube();
            _cube.transform.position = pos;
        }

        void EnsureCube()
        {
            if (_cube != null) return;
            // Build the cube mesh manually — GameObject.CreatePrimitive's implicit
            // collider throws under IL2CPP (Physics module stripped).
            _cube = new GameObject("OcclusionTestCube");
            _cube.transform.localScale = Vector3.one * cubeSize;
            _cube.AddComponent<MeshFilter>().sharedMesh = UnitCube();

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", cubeColor);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", cubeColor);
            _cube.AddComponent<MeshRenderer>().material = mat;
        }

        static Mesh _unitCube;
        static Mesh UnitCube()
        {
            if (_unitCube != null) return _unitCube;
            var v = new[]
            {
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3(0.5f,-0.5f,-0.5f),
                new Vector3(0.5f, 0.5f,-0.5f),  new Vector3(-0.5f,0.5f,-0.5f),
                new Vector3(-0.5f,-0.5f, 0.5f), new Vector3(0.5f,-0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),  new Vector3(-0.5f,0.5f, 0.5f),
            };
            var t = new[]
            {
                0,2,1, 0,3,2,  4,5,6, 4,6,7,
                0,1,5, 0,5,4,  3,7,6, 3,6,2,
                0,4,7, 0,7,3,  1,2,6, 1,6,5,
            };
            _unitCube = new Mesh { vertices = v, triangles = t };
            _unitCube.RecalculateNormals();
            _unitCube.RecalculateBounds();
            return _unitCube;
        }

        // ── UI ────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            EnsureStyle();

            if (_placing)
            {
                GUI.color = new Color(1f, 0.6f, 0.9f);
                GUI.Label(SR(0.55f, 0.02f, 0.42f, 0.06f), "TAP TO POSITION THE CUBE", _style);
                GUI.color = Color.white;
                if (Button(0.55f, 0.09f, 0.42f, 0.07f, "CONFIRM CUBE"))
                    _placing = false;
            }
            else if (Button(0.55f, 0.02f, 0.42f, 0.07f, "PLACE CUBE"))
            {
                _placing = true;
                if (_cube == null && Camera.main != null)
                {
                    // Spawn in front of the camera so it's immediately visible.
                    var fwd = Camera.main.transform.forward;
                    MoveCubeTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                    if (_cube != null) _cube.transform.position =
                        Camera.main.transform.position + fwd * fallbackDistance;
                }
            }

            if (_cube != null && Button(0.55f, _placing ? 0.16f : 0.09f, 0.42f, 0.07f, "DELETE CUBE"))
            {
                Destroy(_cube);
                _cube = null;
                _placing = false;
            }
        }

        bool IsOverButtons(Vector2 screenPos)
        {
            // Buttons live in the top-right ~45% width / ~24% height; flip Y (GUI is top-left).
            float gx = screenPos.x / Screen.width;
            float gy = 1f - screenPos.y / Screen.height;
            return gx >= 0.55f && gy <= 0.24f;
        }

        void EnsureStyle()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = Mathf.RoundToInt(Screen.height * 0.022f),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap  = true
            };
        }

        bool Button(float x, float y, float w, float h, string label)
            => GUI.Button(SR(x, y, w, h), label);

        static Rect SR(float x, float y, float w, float h)
            => new Rect(Screen.width * x, Screen.height * y, Screen.width * w, Screen.height * h);
    }
}
