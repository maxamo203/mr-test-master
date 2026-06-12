using UnityEngine;

namespace Scanner
{
    // Fantasma en vivo de lo que se va a colocar. Cada frame, segun el modo de la
    // FSM y la posicion de la reticula (centro de pantalla), posiciona/dibuja un
    // preview semitransparente ANTES de tocar COLOCAR:
    //   - Wall_V1 / Cube_V1 / Floor_Place / Door_V1 : marcador (esfera) donde caeria.
    //   - Wall_Height : pilar vertical desde el 1er punto hasta la altura apuntada.
    //   - Wall_Vn     : el segmento de pared (caja 3D) formandose desde el ult. vertice.
    //   - Cube_V2     : el cubo axis-aligned formandose desde la 1ra esquina.
    //   - Cube_V3     : el cubo ya con su rotacion (yaw) tomada del 3er punto.
    //   - Door_V2     : el hueco de la puerta sobre la cara de la pared.
    //   - EditMoveTarget : el objeto seleccionado donde quedaria al moverlo.
    //
    // Todos los ghosts son hijos de WorldOrigin (coordenadas anchor-locales, igual
    // que los objetos reales) y no tienen collider (no interfieren con taps/raycasts).
    // ReticleController se encarga de instanciar este componente (AddComponent).
    public class PlacementPreview : MonoBehaviour
    {
        private static readonly Color FloorColor   = new Color(0.2f, 0.6f, 1f, 1f);
        private static readonly Color CubeColor    = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly Color DoorColor    = new Color(0.3f, 1f, 0.4f, 1f);
        private static readonly Color CeilingColor = new Color(1f, 0.6f, 0.2f, 1f);

        private WallBuilder _wall;
        private DoorBuilder _door;
        private CubeBuilder _cube;

        private GameObject _marker;   // esfera para previews de un solo punto
        private GameObject _box;      // primitiva cubo (cubo / pilar de altura / mover-cubo)
        private GameObject _meshGo;   // MeshFilter+Renderer para pared/puerta (mesh por frame)
        private Material   _ghostBoxMat;   // translucido para la caja (aristas de cubo)
        private Material   _ghostMeshMat;  // translucido para mallas de pared (grid sigue la pared)
        private Material   _markerMat;     // lit, para los marcadores de punto (esferas)

        private void Awake()
        {
            _wall = FindFirstObjectByType<WallBuilder>();
            _door = FindFirstObjectByType<DoorBuilder>();
            _cube = FindFirstObjectByType<CubeBuilder>();
        }

        private void Update()
        {
            HideAll();

            var fsm = ScanStateMachine.Instance;
            var wo  = WorldOrigin.Instance;
            var rr  = RaycastResolver.Instance;
            if (fsm == null || wo == null || !wo.IsReady || rr == null) return;
            if (!IsPreviewMode(fsm.Current)) return;

            var hit = rr.ResolveFromScreenCenter();
            if (!hit.Hit) return;
            Vector3 local = wo.ToRelative(hit.Position);

            switch (fsm.Current)
            {
                case ScannerMode.Wall_V1:
                    ShowMarker(local, 0.06f, FloorColor);
                    break;

                case ScannerMode.Wall_Height:
                    ShowHeightPillar(local);
                    ShowMarker(local, 0.06f, CeilingColor);
                    break;

                case ScannerMode.Wall_Vn:
                    if (_wall != null &&
                        _wall.TryGetWallPreview(local, out var a, out var b, out var h, out var w, out var n))
                        ShowMesh(WallMeshBuilder.Build(a, b, h, w, n, null));
                    break;

                case ScannerMode.Cube_V1:
                    ShowMarker(local, 0.06f, CubeColor);
                    break;

                case ScannerMode.Cube_V2:
                    if (_cube != null && _cube.FirstCorner.HasValue)
                        ShowAxisBox(_cube.FirstCorner.Value, local);
                    break;

                case ScannerMode.Cube_V3:
                    if (_cube != null && _cube.FirstCorner.HasValue && _cube.SecondCorner.HasValue)
                    {
                        CubeObject.ComputeYawBox(_cube.FirstCorner.Value, _cube.SecondCorner.Value, local,
                                                 out var c, out var rot, out var sc, out _);
                        ShowBox(c, rot, sc);
                    }
                    ShowMarker(local, 0.05f, CubeColor);
                    break;

                case ScannerMode.Floor_Place:
                    ShowMarker(local, 0.12f, FloorColor);
                    break;

                case ScannerMode.Door_V1:
                    ShowDoorMarker(hit.Position, local);
                    break;

                case ScannerMode.Door_V2:
                    ShowDoorGhost(hit.Position);
                    break;

                case ScannerMode.EditMoveTarget:
                    ShowMoveGhost(fsm.CurrentSelection, local);
                    break;
            }
        }

        private static bool IsPreviewMode(ScannerMode m) =>
            m == ScannerMode.Wall_V1 || m == ScannerMode.Wall_Height || m == ScannerMode.Wall_Vn ||
            m == ScannerMode.Cube_V1 || m == ScannerMode.Cube_V2 || m == ScannerMode.Cube_V3 ||
            m == ScannerMode.Door_V1 || m == ScannerMode.Door_V2 ||
            m == ScannerMode.Floor_Place || m == ScannerMode.EditMoveTarget;

        // ── Helpers de cada preview ────────────────────────────────────────────

        private void ShowHeightPillar(Vector3 reticleLocal)
        {
            var first = _wall != null ? _wall.FirstFloor : null;
            if (!first.HasValue) return;
            var baseP = first.Value;
            float topY = reticleLocal.y;
            float h = Mathf.Max(0.02f, Mathf.Abs(topY - baseP.y));
            var center = new Vector3(baseP.x, (baseP.y + topY) * 0.5f, baseP.z);
            ShowBox(center, Quaternion.identity, new Vector3(0.04f, h, 0.04f));
        }

        private void ShowAxisBox(Vector3 p1, Vector3 p2)
        {
            var center = (p1 + p2) * 0.5f;
            var diff   = p2 - p1;
            var scale = new Vector3(
                Mathf.Max(0.02f, Mathf.Abs(diff.x)),
                Mathf.Max(0.02f, Mathf.Abs(diff.y)),
                Mathf.Max(0.02f, Mathf.Abs(diff.z)));
            ShowBox(center, Quaternion.identity, scale);
        }

        private void ShowDoorMarker(Vector3 worldPoint, Vector3 fallbackLocal)
        {
            var wall = _door != null ? _door.Target : null;
            if (wall != null && wall.WorldPointToWallUV(worldPoint, out var u, out var v))
            {
                u = Mathf.Clamp(u, 0f, wall.Length);
                v = Mathf.Max(0f, v);
                var lp = wall.ALocal + u * wall.BaseHat + v * Vector3.up;
                ShowMarker(lp, 0.06f, DoorColor);
            }
            else ShowMarker(fallbackLocal, 0.06f, DoorColor);
        }

        private void ShowDoorGhost(Vector3 worldPoint)
        {
            var wall = _door != null ? _door.Target : null;
            var uv1  = _door != null ? _door.Uv1 : null;
            if (wall == null || !uv1.HasValue) return;
            if (!wall.WorldPointToWallUV(worldPoint, out var u, out var v)) return;

            float uMin = Mathf.Clamp(Mathf.Min(uv1.Value.x, u), 0f, wall.Length);
            float uMax = Mathf.Clamp(Mathf.Max(uv1.Value.x, u), 0f, wall.Length);
            float vMax = Mathf.Clamp(Mathf.Max(uv1.Value.y, v), 0f, wall.Height);
            float vMin = 0f; // las puertas arrancan del piso (igual que DoorBuilder)
            if (uMax - uMin < 0.02f || vMax - vMin < 0.02f) return;

            ShowMesh(BuildWallQuad(wall, uMin, uMax, vMin, vMax));
        }

        private void ShowMoveGhost(ISelectable sel, Vector3 local)
        {
            switch (sel)
            {
                case CubeObject cube:
                    ShowBox(local, cube.transform.localRotation, cube.transform.localScale);
                    break;
                case WallObject wall:
                {
                    var delta = local - wall.ALocal;
                    ShowMesh(WallMeshBuilder.Build(wall.ALocal + delta, wall.BLocal + delta,
                                                   wall.Height, wall.Width, wall.Normal, wall.Doors));
                    break;
                }
                case FloorPoint _:
                    ShowMarker(local, 0.12f, FloorColor);
                    break;
            }
        }

        // Quad (cara cercana) sobre la pared, en coords anchor-locales.
        private static Mesh BuildWallQuad(WallObject wall, float uMin, float uMax, float vMin, float vMax)
        {
            Vector3 LP(float uu, float vv) => wall.ALocal + uu * wall.BaseHat + vv * Vector3.up;
            var p00 = LP(uMin, vMin);
            var p10 = LP(uMax, vMin);
            var p11 = LP(uMax, vMax);
            var p01 = LP(uMin, vMax);
            var mesh = new Mesh { name = "GhostDoorQuad" };
            // Doble cara para que se vea desde ambos lados.
            mesh.SetVertices(new[] { p00, p10, p11, p01 });
            // Coords (u,v,w) + normal de frame (cara de grosor) para el grid del shader.
            mesh.SetUVs(1, new[]
            {
                new Vector3(uMin, vMin, 0f), new Vector3(uMax, vMin, 0f),
                new Vector3(uMax, vMax, 0f), new Vector3(uMin, vMax, 0f),
            });
            var gn = new Vector3(0f, 0f, 1f);
            mesh.SetUVs(2, new[] { gn, gn, gn, gn });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── Primitivas de ghost (lazy, hijas de WorldOrigin, sin collider) ──────

        private void ShowMarker(Vector3 localPos, float diameter, Color c)
        {
            var go = MarkerGO();
            go.SetActive(true);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one * diameter;
            SetColor(_markerMat, c);
        }

        private void ShowBox(Vector3 centerLocal, Quaternion rotLocal, Vector3 scaleLocal)
        {
            var go = BoxGO();
            go.SetActive(true);
            go.transform.localPosition = centerLocal;
            go.transform.localRotation = rotLocal;
            go.transform.localScale    = scaleLocal;
        }

        private void ShowMesh(Mesh m)
        {
            var go = MeshGO();
            var mf = go.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
            if (m == null) { mf.sharedMesh = null; go.SetActive(false); return; }
            go.SetActive(true);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;
            mf.sharedMesh = m;
        }

        private void HideAll()
        {
            if (_marker != null) _marker.SetActive(false);
            if (_box    != null) _box.SetActive(false);
            if (_meshGo != null) _meshGo.SetActive(false);
        }

        private GameObject MarkerGO()
        {
            if (_marker == null)
            {
                _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _marker.name = "GhostMarker";
                StripCollider(_marker);
                _marker.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
                _markerMat = new Material(Shader.Find("Custom/LitMarker") ?? Shader.Find("Unlit/Color"))
                    { name = "GhostMarkerMat (runtime)" };
                _marker.GetComponent<MeshRenderer>().sharedMaterial = _markerMat;
            }
            return _marker;
        }

        private GameObject BoxGO()
        {
            if (_box == null)
            {
                _box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _box.name = "GhostBox";
                StripCollider(_box);
                _box.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
                _box.GetComponent<MeshRenderer>().sharedMaterial = GhostBoxMat();
            }
            return _box;
        }

        private GameObject MeshGO()
        {
            if (_meshGo == null)
            {
                _meshGo = new GameObject("GhostMesh");
                _meshGo.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
                _meshGo.AddComponent<MeshFilter>();
                _meshGo.AddComponent<MeshRenderer>().sharedMaterial = GhostMeshMat();
            }
            return _meshGo;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // Material translucido del fantasma. Custom/EdgeGrid para el look "fantasma";
        // si no esta, Unlit/Color (opaco, igual sirve de guia).
        // Caja (cubo): aristas de caja geometricas.
        private Material GhostBoxMat()
        {
            if (_ghostBoxMat != null) return _ghostBoxMat;
            _ghostBoxMat = NewGhostMat("PlacementGhostBoxMat (runtime)");
            if (_ghostBoxMat.HasProperty("_BoxEdges")) _ghostBoxMat.SetFloat("_BoxEdges", 1f);
            return _ghostBoxMat;
        }

        // Malla (pared): grid desde las coords (u,v,w) horneadas, asi el preview en
        // vivo sigue la inclinacion de la pared en vez de quedar world-horizontal.
        private Material GhostMeshMat()
        {
            if (_ghostMeshMat != null) return _ghostMeshMat;
            _ghostMeshMat = NewGhostMat("PlacementGhostMeshMat (runtime)");
            if (_ghostMeshMat.HasProperty("_GridFromUV")) _ghostMeshMat.SetFloat("_GridFromUV", 1f);
            return _ghostMeshMat;
        }

        private static Material NewGhostMat(string name)
        {
            var sh = Shader.Find("Custom/EdgeGrid") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = name };
            SetColor(m, new Color(0.3f, 0.9f, 1f, 0.35f));
            m.renderQueue = 3000;
            return m;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_Color"))     m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        private void OnDestroy()
        {
            if (_meshGo != null)
            {
                var mf = _meshGo.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            }
            if (_marker != null) Destroy(_marker);
            if (_box    != null) Destroy(_box);
            if (_meshGo != null) Destroy(_meshGo);
            if (_ghostBoxMat  != null) Destroy(_ghostBoxMat);
            if (_ghostMeshMat != null) Destroy(_ghostMeshMat);
            if (_markerMat    != null) Destroy(_markerMat);
        }
    }
}
