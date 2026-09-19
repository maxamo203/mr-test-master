using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARPlane))]
[RequireComponent(typeof(MeshRenderer))]
public class PlaneClassificationVisualizer : MonoBehaviour
{
    [Tooltip("Planes smaller than this area (m²) are hidden. Filters ARCore phantom fragments.")]
    [SerializeField] float minPlaneArea = 0.25f;

    ARPlane _plane;
    MeshRenderer _meshRenderer;
    LineRenderer _lineRenderer;
    Material _gridMaterial;
    PlaneClassifications _lastClassifications;
    bool _initialized;

    static readonly Color FloorColor   = new Color(0.15f, 0.55f, 1.00f, 0.90f); // blue
    static readonly Color CeilingColor = new Color(1.00f, 0.40f, 0.15f, 0.90f); // orange
    static readonly Color WallColor    = new Color(0.15f, 0.90f, 0.35f, 0.90f); // green
    static readonly Color TableColor   = new Color(1.00f, 0.85f, 0.10f, 0.90f); // yellow
    static readonly Color SeatColor    = new Color(1.00f, 0.40f, 0.80f, 0.90f); // pink
    static readonly Color DoorColor    = new Color(0.70f, 0.40f, 0.10f, 0.90f); // brown
    static readonly Color WindowColor  = new Color(0.50f, 0.90f, 1.00f, 0.90f); // cyan
    static readonly Color NoneColor    = new Color(0.70f, 0.70f, 0.70f, 0.70f); // grey

    static readonly int GridColorId = Shader.PropertyToID("_GridColor");
    static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
    static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");

    void Awake()
    {
        _plane        = GetComponent<ARPlane>();
        _meshRenderer = GetComponent<MeshRenderer>();
        _lineRenderer = GetComponent<LineRenderer>();

        // sharedMaterial can be null if the plane prefab has no material assigned —
        // fall back to the grid shader so Awake never throws.
        var baseMat = _meshRenderer.sharedMaterial;
        _gridMaterial = baseMat != null
            ? new Material(baseMat)
            : new Material(Shader.Find("Mortuorium/ARPlaneGrid"));
        _meshRenderer.material = _gridMaterial;
    }

    void Update()
    {
        float area = _plane.size.x * _plane.size.y;
        bool visible = area >= minPlaneArea && IsInitialKeeper(_plane);

        if (_meshRenderer.enabled != visible)
        {
            _meshRenderer.enabled = visible;
            if (_lineRenderer != null) _lineRenderer.enabled = visible;
            if (!visible)
                Debug.Log($"[PlaneViz] Hiding plane {_plane.trackableId} (area={area:F3}m², kept={IsInitialKeeper(_plane)})");
        }

        if (!visible) return;

        var current = EffectiveClassification();
        if (_initialized && current == _lastClassifications) return;
        _initialized = true;
        _lastClassifications = current;
        Refresh(current);
    }

    void Refresh(PlaneClassifications c)
    {
        var arcore = _plane.classifications;
        string source = (arcore != PlaneClassifications.None && arcore != PlaneClassifications.Other) ? "ARCore" : "geometry-fallback";
        Debug.Log($"[PlaneViz] Plane {_plane.trackableId} → {LabelFor(c)} [{source}]  ARCore={arcore}  normal.y={Vector3.Dot(_plane.normal, Vector3.up):F3}  pos={_plane.transform.position:F2}");

        bool classified = c != PlaneClassifications.None && c != PlaneClassifications.Other;

        var color = ColorFor(c);
        _gridMaterial.SetColor(GridColorId, color);
        _gridMaterial.SetFloat(FillAlphaId, classified ? 0.05f : 0.02f);
        _gridMaterial.SetFloat(LineWidthId,  classified ? 0.030f : 0.015f);

        if (_lineRenderer == null) return;
        var borderColor = new Color(color.r, color.g, color.b, 1f);
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(borderColor, 0f), new GradientColorKey(borderColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        _lineRenderer.colorGradient = grad;
        _lineRenderer.startWidth = classified ? 0.010f : 0.005f;
        _lineRenderer.endWidth   = classified ? 0.010f : 0.005f;
    }

    /// <summary>
    /// Returns ARCore classification if available, otherwise derives one from plane geometry.
    /// </summary>
    PlaneClassifications EffectiveClassification()
    {
        var arcore = _plane.classifications;
        if (arcore != PlaneClassifications.None && arcore != PlaneClassifications.Other)
            return arcore;

        return ClassifyByGeometry(_plane);
    }

    /// <summary>
    /// Geometry-based fallback: uses plane orientation and height relative to the camera.
    /// Wall   = nearly vertical normal (|normal.y| < 0.3)
    /// Floor  = horizontal, well below camera (~1.2m+)
    /// Ceiling= horizontal, above camera
    /// Table  = horizontal, 0.4–1.1m below camera
    /// </summary>
    static PlaneClassifications ClassifyByGeometry(ARPlane plane)
    {
        float normalY = Mathf.Abs(Vector3.Dot(plane.normal, Vector3.up));

        // Vertical surface → wall
        if (normalY < 0.30f)
            return PlaneClassifications.WallFace;

        // Horizontal surface — classify by height relative to camera
        var cam = Camera.main;
        if (cam == null) return PlaneClassifications.None;

        float planeY  = plane.transform.position.y;
        float cameraY = cam.transform.position.y;
        float relY    = planeY - cameraY; // negative = below camera

        if (relY > 0.3f)          return PlaneClassifications.Ceiling;  // above eye level
        if (relY < -1.2f)         return PlaneClassifications.Floor;    // well below → floor
        if (relY < -0.35f)        return PlaneClassifications.Table;    // table/desk height
        return PlaneClassifications.Floor;                               // close below → floor
    }

    // Called to tint baked planes consistently
    public static Color ColorForClassification(PlaneClassifications c) => ColorFor(c);

    public static PlaneClassifications GeometryClassification(ARPlane plane)
        => ClassifyByGeometry(plane);

    // ── Initial-scan plane gate ───────────────────────────────────────────────
    // During the initial scan we only want WALLS and the one real FLOOR. Tables,
    // chairs, ceilings and ARCore's spurious mid-air horizontal planes (which the
    // geometry fallback paints "floor blue") are rejected. The real floor is the
    // lowest substantial horizontal plane seen so far; floating "floor" phantoms
    // sit well above it and are dropped.

    const float HorizontalNormalDot = 0.85f; // |normal·up| above this ⇒ horizontal
    const float FloorBandTolerance  = 0.35f; // m a floor plane may sit above the reference
    const float MinFloorRefArea     = 0.50f; // m² needed to define the floor level

    // Walls are reconstructed from depth occupancy (DepthOccupancyMapper), not from
    // ARCore vertical planes — which are unreliable on blank walls and produce
    // "phantom wall" noise. So by default we do NOT render/bake vertical planes.
    // Flip to true only to debug raw AR plane detection.
    public static bool ShowWallPlanes = false;

    static float s_floorReferenceY = float.PositiveInfinity;

    /// <summary>The lowest substantial floor height observed, or +∞ if none yet.</summary>
    public static float FloorReferenceY => s_floorReferenceY;

    /// <summary>Resets the floor reference (call when a new scan/origin begins).</summary>
    public static void ResetFloorReference() => s_floorReferenceY = float.PositiveInfinity;

    /// <summary>
    /// True if the plane should be shown/baked during the initial scan: any vertical
    /// surface (wall), or a horizontal plane that is the real floor. Updates the shared
    /// floor reference as a side effect.
    /// </summary>
    public static bool IsInitialKeeper(ARPlane plane)
    {
        float ny = Mathf.Abs(Vector3.Dot(plane.normal, Vector3.up));
        if (ny < HorizontalNormalDot) return ShowWallPlanes; // vertical ⇒ wall (hidden by default)

        // Horizontal: keep only the floor.
        var c = plane.classifications;
        bool isFloor = c.HasFlag(PlaneClassifications.Floor)
            || ((c == PlaneClassifications.None || c == PlaneClassifications.Other)
                && ClassifyByGeometry(plane) == PlaneClassifications.Floor);
        if (!isFloor) return false; // table / chair / counter / ceiling

        float y = plane.transform.position.y;
        float area = plane.size.x * plane.size.y;
        if (area >= MinFloorRefArea && y < s_floorReferenceY)
            s_floorReferenceY = y; // lowest substantial floor establishes the level

        // Drop floating "floor" phantoms sitting above the established floor.
        if (!float.IsInfinity(s_floorReferenceY) && y > s_floorReferenceY + FloorBandTolerance)
            return false;
        return true;
    }

    static Color ColorFor(PlaneClassifications c)
    {
        if (c == PlaneClassifications.None)                    return NoneColor;
        if (c.HasFlag(PlaneClassifications.Floor))             return FloorColor;
        if (c.HasFlag(PlaneClassifications.Ceiling))           return CeilingColor;
        if (c.HasFlag(PlaneClassifications.WallFace))          return WallColor;
        if (c.HasFlag(PlaneClassifications.InnerWallFace))     return WallColor;
        if (c.HasFlag(PlaneClassifications.InvisibleWallFace)) return WallColor;
        if (c.HasFlag(PlaneClassifications.Table))             return TableColor;
        if (c.HasFlag(PlaneClassifications.Seat))              return SeatColor;
        if (c.HasFlag(PlaneClassifications.SeatOfAnyType))     return SeatColor;
        if (c.HasFlag(PlaneClassifications.Couch))             return SeatColor;
        if (c.HasFlag(PlaneClassifications.DoorFrame))         return DoorColor;
        if (c.HasFlag(PlaneClassifications.WindowFrame))       return WindowColor;
        if (c.HasFlag(PlaneClassifications.WallArt))           return WallColor;
        return NoneColor;
    }

    public static string LabelFor(PlaneClassifications c)
    {
        if (c == PlaneClassifications.None)                    return "unclassified";
        if (c.HasFlag(PlaneClassifications.Floor))             return "FLOOR";
        if (c.HasFlag(PlaneClassifications.Ceiling))           return "CEILING";
        if (c.HasFlag(PlaneClassifications.WallFace))          return "WALL";
        if (c.HasFlag(PlaneClassifications.InnerWallFace))     return "INNER WALL";
        if (c.HasFlag(PlaneClassifications.InvisibleWallFace)) return "WALL (invis)";
        if (c.HasFlag(PlaneClassifications.Table))             return "TABLE";
        if (c.HasFlag(PlaneClassifications.Couch))             return "COUCH";
        if (c.HasFlag(PlaneClassifications.Seat))              return "SEAT";
        if (c.HasFlag(PlaneClassifications.SeatOfAnyType))     return "SEAT";
        if (c.HasFlag(PlaneClassifications.DoorFrame))         return "DOOR";
        if (c.HasFlag(PlaneClassifications.WindowFrame))       return "WINDOW";
        if (c.HasFlag(PlaneClassifications.WallArt))           return "WALL ART";
        return c.ToString();
    }
}
