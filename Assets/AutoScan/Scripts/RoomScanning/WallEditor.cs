using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Hand correction of automatically detected walls, in AR on the device.
    ///
    /// Detection stays automatic; this exists because the fitted line is often a few
    /// centimetres off the real wall — worst on blank walls, where ARCore's depth is
    /// largely an ML prior. The user taps a wall, drags it onto the real one, and the
    /// wall becomes <see cref="WallSegment.pinned"/>: <see cref="RoomBuilder.ClearWalls"/>
    /// then spares it and <see cref="DepthOccupancyMapper"/> stops emitting an automatic
    /// wall on top of it, so the rest of the room can keep scanning around the fix.
    ///
    /// Hit testing is analytic — screen-space distance to the wall's projected mid-line,
    /// plus a camera-ray/floor-plane intersection for the drag, mirroring
    /// <c>RoomScanningManager.TryBeginGrab</c>/<c>RayToHorizontalPlane</c>. It deliberately
    /// does not use <c>Physics.Raycast</c> against the wall MeshColliders: the player is
    /// built with engine-code stripping and the Physics module is stripped out (see the
    /// comments in <c>RoomScanningManager.UnitCube</c> and <c>OcclusionCubePlacer</c>).
    ///
    /// Driven by <see cref="RoomScanningManager"/>, which owns the mode toggle; this
    /// component draws its own sub-HUD and consumes its own touches while active.
    /// </summary>
    public class WallEditor : MonoBehaviour
    {
        /// <summary>
        /// What a drag does to the selected wall.
        ///
        /// <see cref="Move"/> and <see cref="Ends"/> constrain the edit to what the fit most
        /// often gets wrong (an offset, or a run that stops short). <see cref="Corners"/> is
        /// the unconstrained one: it treats the wall as the box it actually is and lets each
        /// corner be placed by hand, which is what fitting a room whose walls are not
        /// clean, full-height rectangles requires.
        /// </summary>
        public enum EditMode { Move, Ends, Corners, Rotate, Combine, Add }

        [SerializeField] RoomBuilder roomBuilder;

        [Tooltip("Renders the corner grab handles. Added automatically if not assigned.")]
        [SerializeField] WallCornerHandles cornerHandles;

        [Tooltip("Tap tolerance as a fraction of screen width, matching the origin-marker grab.")]
        [SerializeField] float grabRadiusFraction = 0.12f;

        [Tooltip("Tap tolerance for a corner handle, as a fraction of screen width. Smaller " +
                 "than the wall radius so the four handles of one wall stay distinguishable.")]
        [SerializeField] float cornerGrabRadiusFraction = 0.08f;

        [Tooltip("A dragged base corner within this distance (m) of another wall's base " +
                 "corner welds onto it exactly. 0 disables snapping outright.")]
        [SerializeField] float cornerSnapRadius = 0.25f;

        [Tooltip("A dragged top corner within this distance (m) of the measured room height " +
                 "snaps to it, so walls that do reach the ceiling end up exactly level.")]
        [SerializeField] float heightSnapTolerance = 0.08f;

        [Tooltip("Metres of vertical movement per screen-height of two-finger drag, " +
                 "matching RoomScanningManager's origin grab.")]
        [SerializeField] float verticalDragMetres = 2.0f;

        [Tooltip("Thickness added or removed (m) per press of the W- / W+ buttons.")]
        [SerializeField] float widthStep = 0.02f;
        [SerializeField] float minWallWidth = 0.02f;
        [SerializeField] float maxWallWidth = 0.60f;

        [Tooltip("Shortest wall (m) that can be drawn by hand — guards against stray double taps.")]
        [SerializeField] float minManualWallLength = 0.30f;

        [Tooltip("Shortest wall box (m) a corner drag may leave behind, vertically.")]
        [SerializeField] float minWallHeight = 0.10f;

        /// <summary>Raised after any committed edit, so the manager can autosave.</summary>
        public System.Action Edited;

        /// <summary>True while the user is in wall-editing mode.</summary>
        public bool Active { get; private set; }

        public EditMode Mode { get; private set; } = EditMode.Move;

        Transform _mapOrigin;
        float _roomHeight = 2.5f;
        Camera _cam;

        int _selectedId = -1;

        // Drag state, captured on the touch that began the drag.
        bool _dragging;
        bool _movedDuringDrag;      // a tap that never moved must not pin or autosave
        bool _dragEndIsB;           // Ends mode: which endpoint was grabbed
        Vector3 _grabLocal;         // floor-plane hit where the drag started, origin-local
        Vector3 _origStart, _origEnd;
        Vector3 _pivotLocal;        // Rotate mode: the point the wall turns about

        // Corners mode: which handle is under the finger, and the box it started from.
        // The whole original box is captured because a corner drag is expressed as an
        // offset from where the wall was when the finger landed — accumulating frame to
        // frame would let rounding walk the wall away under a stationary thumb.
        WallCornerId? _dragCorner;
        float _origBaseY, _origHeight;

        // Vertical grabs (a top corner, or the two-finger base lift) record where the drag
        // started so the corner does not jump to the finger on the first frame.
        float _grabTopY;
        float _twoFingerStartY;     // screen y of the two-finger centroid
        bool _liftingBase;

        /// <summary>Weld a dragged base corner onto a nearby one. On by default.</summary>
        bool _snapCorners = true;

        /// <summary>Rotate mode: snap the wall's heading to the nearest 15° on release.</summary>
        bool _snapAngle;

        /// <summary>Combine mode: the first wall tapped, waiting for the second. -1 = none.</summary>
        int _combineFirstId = -1;

        // Why the last tap did or did not select anything. This runs on a phone with no
        // console attached, so "nothing selected" has to be able to explain itself: how
        // many walls were even in front of the camera, and how close the tap came to the
        // nearest one in pixels.
        int _lastPickCandidates = -1;
        float _lastPickNearest = -1f;
        int _lastTouchCount;

        // Add mode: first tap, waiting for the second.
        bool _hasFirstPoint;
        Vector3 _firstPoint;

        string _status = "";

        void Awake()
        {
            if (roomBuilder == null) roomBuilder = GetComponent<RoomBuilder>();
            // Self-provisioned like the manager provisions this editor: the scene asset is
            // hand-wired and adding a component to it by hand is one more step to forget.
            if (cornerHandles == null) cornerHandles = GetComponent<WallCornerHandles>();
            if (cornerHandles == null) cornerHandles = gameObject.AddComponent<WallCornerHandles>();
        }

        void OnEnable() => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        // ── Mode control (called by RoomScanningManager) ──────────────────────

        public void Activate(Transform mapOrigin, float roomHeight)
        {
            _mapOrigin = mapOrigin;
            _roomHeight = roomHeight;
            Active = true;
            Mode = EditMode.Move;
            ResetTransient();
            cornerHandles?.Attach(mapOrigin);
            _status = "";
        }

        public void Deactivate()
        {
            Select(-1);
            ResetTransient();
            // Handles are hidden explicitly rather than left to the next Update: Update
            // returns immediately while inactive, so nothing else would take them down.
            cornerHandles?.Hide();
            Active = false;
        }

        /// <summary>Keeps the height used for hand-drawn walls in step with the live estimate.</summary>
        public void SetRoomHeight(float roomHeight)
        {
            if (roomHeight > 0.1f) _roomHeight = roomHeight;
        }

        void ResetTransient()
        {
            _dragging = false;
            _movedDuringDrag = false;
            _hasFirstPoint = false;
            _dragCorner = null;
            _liftingBase = false;
            if (_combineFirstId >= 0 && _combineFirstId != _selectedId)
                roomBuilder?.SetWallHighlight(_combineFirstId, false);
            _combineFirstId = -1;
        }

        // ── Per-frame interaction ─────────────────────────────────────────────

        void Update()
        {
            if (!Active || _mapOrigin == null || roomBuilder == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // The handles are the whole point of Corners mode, so they are refreshed every
            // frame from the live model — the wall moves under the finger, and a handle
            // still sitting where the corner used to be is worse than none at all.
            RefreshHandles();

            var touches = Touch.activeTouches;
            _lastTouchCount = touches.Count;
            if (touches.Count == 0)
            {
                // Only a drag that actually moved the wall is worth saving; a bare
                // tap is just a selection.
                if (_dragging && _movedDuringDrag) Commit();
                _dragging = false;
                _movedDuringDrag = false;
                _dragCorner = null;
                _liftingBase = false;
                return;
            }

            // Two fingers in Corners mode lift the wall's bottom edge off the floor,
            // reusing the "1 finger = floor, 2 fingers = up/down" idiom the origin marker
            // already established, rather than inventing a third gesture vocabulary.
            if (Mode == EditMode.Corners && touches.Count >= 2)
            {
                HandleBaseLift(touches[0], touches[1]);
                return;
            }
            _liftingBase = false;

            var touch = touches[0];
            bool began = touch.phase == UnityEngine.InputSystem.TouchPhase.Began;

            // A touch that starts on the sub-HUD belongs to the button, not the room.
            // Without this, pressing DELETE WALL would also grab the wall behind it.
            if (began && IsOverEditUI(touch.screenPosition)) return;

            if (Mode == EditMode.Add)
            {
                if (began) HandleAddTap(touch.screenPosition);
                return;
            }

            if (Mode == EditMode.Combine)
            {
                if (began) HandleCombineTap(touch.screenPosition);
                return;
            }

            if (Mode == EditMode.Corners)
            {
                if (began) BeginCornerDrag(touch.screenPosition);
                else if (_dragging && _dragCorner.HasValue) ContinueCornerDrag(touch.screenPosition);
                return;
            }

            if (began) BeginDrag(touch.screenPosition);
            else if (_dragging) ContinueDrag(touch.screenPosition);
        }

        /// <summary>
        /// Shows the corner handles on the selected wall while in Corners mode, and hides
        /// them everywhere else. Reading the wall back from the builder each frame (rather
        /// than caching it) is what keeps the handles glued to the geometry during a drag.
        /// </summary>
        void RefreshHandles()
        {
            if (cornerHandles == null) return;

            if (Mode != EditMode.Corners || _selectedId < 0 ||
                !roomBuilder.TryGetWall(_selectedId, out var wall))
            {
                cornerHandles.Hide();
                return;
            }
            cornerHandles.Show(wall, _dragCorner);
        }

        void BeginDrag(Vector2 screenPos)
        {
            int hit = PickWall(screenPos);
            if (hit < 0) { Select(-1); return; }

            Select(hit);
            if (!roomBuilder.TryGetWall(hit, out var wall)) return;
            if (!FloorPointUnder(screenPos, out var local)) return;

            _grabLocal = local;
            _origStart = wall.start;
            _origEnd = wall.end;
            _dragEndIsB = (local - wall.end).sqrMagnitude < (local - wall.start).sqrMagnitude;
            _pivotLocal = (wall.start + wall.end) * 0.5f;
            _pivotLocal.y = 0f;
            _dragging = true;
        }

        void ContinueDrag(Vector2 screenPos)
        {
            if (_selectedId < 0 || !FloorPointUnder(screenPos, out var local)) return;

            var delta = local - _grabLocal;
            delta.y = 0f;

            // Below this the finger has not really moved: pinning a wall (and rewriting
            // its mesh) because someone rested a thumb on it would be a surprise.
            if (delta.magnitude < 0.005f) return;
            _movedDuringDrag = true;

            if (Mode == EditMode.Move)
            {
                // Constrain to the wall's own normal. The correction being made is an
                // offset error, so letting the wall slide along itself or change length
                // would only add noise to a measurement the fit already got right.
                var along = _origEnd - _origStart;
                along.y = 0f;
                if (along.sqrMagnitude < 1e-6f) return;
                var normal = new Vector3(-along.z, 0f, along.x).normalized;
                var slide = normal * Vector3.Dot(delta, normal);
                roomBuilder.TryUpdateWall(_selectedId, _origStart + slide, _origEnd + slide);
            }
            else if (Mode == EditMode.Rotate)
            {
                // Angle the finger has swept around the pivot since the grab; apply it
                // rigidly to both original endpoints so length is preserved.
                float a0 = Mathf.Atan2(_grabLocal.z - _pivotLocal.z, _grabLocal.x - _pivotLocal.x);
                float a1 = Mathf.Atan2(local.z - _pivotLocal.z, local.x - _pivotLocal.x);
                float turn = a1 - a0;

                var s = RotateAboutY(_origStart, _pivotLocal, turn);
                var e = RotateAboutY(_origEnd, _pivotLocal, turn);
                if (_snapAngle)
                {
                    float heading = Mathf.Atan2(e.z - s.z, e.x - s.x);
                    float snapped = Mathf.Round(heading / (Mathf.PI / 12f)) * (Mathf.PI / 12f);
                    e = RotateAboutY(e, _pivotLocal, snapped - heading);
                    s = RotateAboutY(s, _pivotLocal, snapped - heading);
                }
                roomBuilder.TryUpdateWall(_selectedId, s, e);
                roomBuilder.SetWallHighlight(_selectedId, true);
                SetStatus($"wall {_selectedId} turned {turn * Mathf.Rad2Deg:F0}°");
                return;
            }
            else // Ends: move the grabbed endpoint freely across the floor.
            {
                if (_dragEndIsB) roomBuilder.TryUpdateWall(_selectedId, _origStart, _origEnd + delta);
                else roomBuilder.TryUpdateWall(_selectedId, _origStart + delta, _origEnd);
            }

            // A wall that had to be respawned (a corner quad becoming a box) comes back
            // with its resting tint, so restate the selection highlight.
            roomBuilder.SetWallHighlight(_selectedId, true);
            SetStatus($"wall {_selectedId} moved {delta.magnitude * 100f:F0} cm");
        }

        // ── Corner editing ────────────────────────────────────────────────────
        //
        // A wall is a box, and this is where it is edited as one. The two base corners
        // place the footprint on the floor; the two top corners set the ceiling line; a
        // two-finger drag lifts the bottom edge. Together those cover the cases the
        // constrained modes cannot express — a wall that stops at waist height, a stub
        // between two doorways, a wall whose fitted run overshot into the next room.
        //
        // The box stays a box: its top edge is one height, not a per-end height, because
        // AutoWallMeshBuilder (and the game's scan format behind it) parameterises a wall as
        // base line + height + thickness. Dragging either top corner therefore moves the
        // whole top edge. Skewing a wall into a trapezoid would need a different geometry
        // type on both sides of the export, which is a much bigger change than it looks.

        void BeginCornerDrag(Vector2 screenPos)
        {
            // A corner already on screen wins over the wall behind it: the handles are
            // drawn on top of the geometry, so grabbing what you can see is the least
            // surprising rule.
            if (_selectedId >= 0 && roomBuilder.TryGetWall(_selectedId, out var selected))
            {
                var corner = PickCorner(selected, screenPos);
                if (corner.HasValue)
                {
                    _dragCorner = corner;
                    _origStart = selected.start;
                    _origEnd = selected.end;
                    _origBaseY = selected.baseY;
                    _origHeight = Mathf.Max(minWallHeight, selected.height);
                    _dragging = true;
                    _movedDuringDrag = false;

                    if (WallCorners.IsBase(corner.Value))
                    {
                        // Grab point on the wall's own base plane, so dragging a lifted
                        // wall's footprint does not silently project it onto the floor.
                        if (!PlanePointUnder(screenPos, _origBaseY, out _grabLocal))
                        { _dragging = false; _dragCorner = null; return; }
                    }
                    else if (!TopUnder(screenPos, selected, corner.Value, out _grabTopY))
                    {
                        _dragging = false; _dragCorner = null; return;
                    }

                    SetStatus($"corner {corner.Value} grabbed");
                    return;
                }
            }

            // No handle under the finger: treat the tap as a selection, exactly as the
            // other modes do, so switching walls does not need a mode change.
            int hit = PickWall(screenPos);
            Select(hit);
            _dragging = false;
            _dragCorner = null;
        }

        void ContinueCornerDrag(Vector2 screenPos)
        {
            if (_selectedId < 0 || !_dragCorner.HasValue) return;
            if (!roomBuilder.TryGetWall(_selectedId, out var wall)) return;

            var corner = _dragCorner.Value;
            var start = _origStart;
            var end = _origEnd;
            float baseY = _origBaseY;
            float height = _origHeight;

            if (WallCorners.IsBase(corner))
            {
                if (!PlanePointUnder(screenPos, _origBaseY, out var local)) return;

                var delta = local - _grabLocal;
                delta.y = 0f;
                // Below this the finger has not really moved: pinning a wall because
                // someone rested a thumb on a handle would be a surprise.
                if (delta.magnitude < 0.005f) return;

                if (WallCorners.IsEnd(corner)) end = SnapBaseCorner(_origEnd + delta, start);
                else start = SnapBaseCorner(_origStart + delta, end);

                SetStatus($"corner moved {delta.magnitude * 100f:F0} cm");
            }
            else
            {
                if (!TopUnder(screenPos, wall, corner, out float topY)) return;

                // Carry the grab offset so the top edge does not jump to the finger.
                topY += _origBaseY + _origHeight - _grabTopY;
                topY = SnapTop(topY);

                float newHeight = topY - _origBaseY;
                if (Mathf.Abs(newHeight - _origHeight) < 0.005f) return;

                height = Mathf.Max(minWallHeight, newHeight);
                SetStatus($"height {height:F2} m");
            }

            _movedDuringDrag = true;
            roomBuilder.TryUpdateWallGeometry(_selectedId, start, end, baseY, height, wall.width);
            // A wall that had to be respawned (a corner quad becoming a box) comes back
            // with its resting tint, so restate the selection highlight.
            roomBuilder.SetWallHighlight(_selectedId, true);
        }

        /// <summary>
        /// Two-finger vertical drag: raises or lowers the wall's bottom edge while holding
        /// the top edge still, which is how a full-height wall becomes a half wall.
        /// </summary>
        void HandleBaseLift(Touch a, Touch b)
        {
            if (_selectedId < 0 || !roomBuilder.TryGetWall(_selectedId, out var wall)) return;

            float centroidY = (a.screenPosition.y + b.screenPosition.y) * 0.5f;

            // The gesture starts on the frame the second finger lands, whichever finger
            // that is, so a one-finger drag that gains a second finger does not jump.
            if (!_liftingBase)
            {
                _liftingBase = true;
                _twoFingerStartY = centroidY;
                _origBaseY = wall.baseY;
                _origHeight = Mathf.Max(minWallHeight, wall.height);
                _dragging = true;
                return;
            }

            float dy = (centroidY - _twoFingerStartY) / Mathf.Max(1f, Screen.height) * verticalDragMetres;
            float top = _origBaseY + _origHeight;

            // The base can rise until it is minWallHeight short of the top, and cannot go
            // below the floor: a wall buried under the floor plane is never what was meant.
            float baseY = Mathf.Clamp(_origBaseY + dy, 0f, Mathf.Max(0f, top - minWallHeight));
            float height = Mathf.Max(minWallHeight, top - baseY);
            if (Mathf.Abs(baseY - wall.baseY) < 0.002f) return;

            _movedDuringDrag = true;
            roomBuilder.TryUpdateWallGeometry(_selectedId, wall.start, wall.end, baseY, height, wall.width);
            roomBuilder.SetWallHighlight(_selectedId, true);
            SetStatus($"base {baseY:F2} m | height {height:F2} m");
        }

        /// <summary>
        /// Welds a dragged base corner onto the nearest base corner of another wall, when
        /// one is within <see cref="cornerSnapRadius"/>.
        ///
        /// This is the difference between a room that merely looks closed and one that is:
        /// two walls that share a corner exactly leave no sliver for the game's occlusion
        /// or pathing to leak through, and no amount of careful thumb work gets two
        /// independently dragged corners to the same millimetre.
        /// </summary>
        /// <param name="other">The wall's other endpoint — never snapped to, or a short
        /// wall would collapse onto itself.</param>
        Vector3 SnapBaseCorner(Vector3 candidate, Vector3 other)
        {
            if (!_snapCorners || cornerSnapRadius <= 0f) return candidate;

            float best = cornerSnapRadius;
            var result = candidate;

            foreach (var w in roomBuilder.Walls)
            {
                if (w.id == _selectedId) continue;
                TryCandidate(w.start);
                TryCandidate(w.end);
            }
            return result;

            void TryCandidate(Vector3 p)
            {
                // Compared in XZ only: two walls sharing a vertical edge is the case worth
                // welding, and they may legitimately start at different heights.
                var flatP = new Vector3(p.x, candidate.y, p.z);
                float d = Vector3.Distance(flatP, candidate);
                if (d >= best) return;
                if (Vector3.Distance(new Vector3(flatP.x, other.y, flatP.z), other)
                    < AutoWallMeshBuilder.MinDimension) return;
                best = d;
                result = flatP;
            }
        }

        /// <summary>Snaps a top edge to the measured room height when it lands close to it.</summary>
        float SnapTop(float topY)
        {
            if (!_snapCorners || _roomHeight <= 0.1f) return topY;
            return Mathf.Abs(topY - _roomHeight) <= heightSnapTolerance ? _roomHeight : topY;
        }

        /// <summary>
        /// The corner handle nearest <paramref name="screenPos"/> within the grab radius,
        /// or null. Positions come from <see cref="WallCorners"/>, the same source the
        /// renderer uses, so the handle you see is the handle you get.
        /// </summary>
        WallCornerId? PickCorner(in WallSegment wall, Vector2 screenPos)
        {
            float bestDist = Screen.width * cornerGrabRadiusFraction;
            WallCornerId? best = null;

            for (int i = 0; i < WallCorners.Count; i++)
            {
                var id = (WallCornerId)i;
                var world = _mapOrigin.TransformPoint(WallCorners.Local(wall, id));
                var s = _cam.WorldToScreenPoint(world);
                if (s.z <= 0f) continue;   // behind the camera

                float d = Vector2.Distance(screenPos, new Vector2(s.x, s.y));
                if (d < bestDist) { bestDist = d; best = id; }
            }
            return best;
        }

        /// <summary>
        /// Height (origin-local) that the camera ray through <paramref name="screenPos"/>
        /// indicates for a top corner, by taking the point on the corner's own vertical
        /// line closest to the ray.
        ///
        /// A plane intersection will not do here: the natural drag plane for a vertical
        /// move is one containing the ray, and the closest-point-between-two-lines
        /// formulation degrades gracefully as the user looks along the wall instead of
        /// producing an intersection at infinity.
        /// </summary>
        bool TopUnder(Vector2 screenPos, in WallSegment wall, WallCornerId corner, out float topY)
        {
            topY = 0f;

            var footLocal = WallCorners.Local(wall, corner);
            footLocal.y = wall.baseY;
            var linePoint = _mapOrigin.TransformPoint(footLocal);
            var lineDir = _mapOrigin.up;   // the origin carries yaw, so 'up' is its own

            var ray = _cam.ScreenPointToRay(screenPos);
            var w0 = ray.origin - linePoint;
            float b = Vector3.Dot(ray.direction, lineDir);
            float d = Vector3.Dot(ray.direction, w0);
            float e = Vector3.Dot(lineDir, w0);
            float denom = 1f - b * b;      // both directions are unit length

            // Looking straight down the wall's vertical line: no usable answer this frame.
            if (Mathf.Abs(denom) < 1e-4f) return false;

            float t = (e - b * d) / denom;
            var pointOnLine = linePoint + lineDir * t;
            topY = _mapOrigin.InverseTransformPoint(pointOnLine).y;
            return true;
        }

        void HandleAddTap(Vector2 screenPos)
        {
            if (!FloorPointUnder(screenPos, out var local)) return;

            if (!_hasFirstPoint)
            {
                _firstPoint = local;
                _hasFirstPoint = true;
                SetStatus("start set — tap the far end of the wall");
                return;
            }

            _hasFirstPoint = false;
            if ((local - _firstPoint).magnitude < minManualWallLength)
            {
                SetStatus("too short — tap two points further apart");
                return;
            }

            int id = roomBuilder.AddManualWall(_firstPoint, local, _roomHeight);
            if (id < 0) { SetStatus("could not add that wall"); return; }

            Select(id);
            Mode = EditMode.Move;
            SetStatus($"wall {id} added");
            Commit();
        }

        /// <summary>Two-tap fuse: first tap picks a wall, second tap picks the wall to
        /// merge it with. Falls back to selecting nothing if a tap misses.</summary>
        void HandleCombineTap(Vector2 screenPos)
        {
            int hit = PickWall(screenPos);
            if (hit < 0)
            {
                if (_combineFirstId >= 0 && _combineFirstId != _selectedId)
                    roomBuilder.SetWallHighlight(_combineFirstId, false);
                _combineFirstId = -1;
                SetStatus("tap the first wall to combine");
                return;
            }

            if (_combineFirstId < 0)
            {
                _combineFirstId = hit;
                roomBuilder.SetWallHighlight(hit, true);
                SetStatus($"first wall {hit} — tap the one to merge it with");
                return;
            }

            if (hit == _combineFirstId) { SetStatus("tap a different wall"); return; }

            int first = _combineFirstId;
            int merged = roomBuilder.CombineWalls(first, hit);
            _combineFirstId = -1;
            if (merged < 0)
            {
                if (first != _selectedId) roomBuilder.SetWallHighlight(first, false);
                SetStatus("could not combine those walls");
                return;
            }

            Select(merged);
            SetMode(EditMode.Move);
            SetStatus($"walls {first} + {hit} combined into {merged}");
            Commit();
        }

        // ── Operations exposed to the HUD ─────────────────────────────────────

        public void DeleteSelected()
        {
            if (_selectedId < 0) { SetStatus("select a wall first"); return; }
            int id = _selectedId;
            Select(-1);
            if (roomBuilder.RemoveWall(id)) { SetStatus($"wall {id} deleted"); Commit(); }
        }

        public void SetMode(EditMode mode)
        {
            Mode = mode;
            ResetTransient();
            if (mode == EditMode.Add) SetStatus("tap the start of the new wall");
            if (mode == EditMode.Corners) SetStatus("drag a corner cube to reshape the wall");
            if (mode == EditMode.Rotate) SetStatus("drag around a wall to pivot it about its centre");
            if (mode == EditMode.Combine) SetStatus("tap the first wall to combine");
            // Leaving Corners takes its handles with it; staying puts them back next frame.
            if (mode != EditMode.Corners) cornerHandles?.Hide();
        }

        /// <summary>
        /// Steps the reshape modes in a fixed order. ADD, COMBINE and ROTATE are not in
        /// the cycle — they have their own buttons; ADD/COMBINE would swallow the next tap
        /// if cycled into by accident, and ROTATE is easier to find as a labelled button.
        /// </summary>
        public void CycleMode()
        {
            SetMode(Mode switch
            {
                EditMode.Move => EditMode.Ends,
                EditMode.Ends => EditMode.Corners,
                _ => EditMode.Move,
            });
        }

        /// <summary>Turns base-corner welding and top-edge height snapping on or off.</summary>
        public void ToggleSnap()
        {
            _snapCorners = !_snapCorners;
            SetStatus(_snapCorners ? "snapping on" : "snapping off");
        }

        /// <summary>Rotate mode: turns 15° heading snapping on or off.</summary>
        public void ToggleAngleSnap()
        {
            _snapAngle = !_snapAngle;
            SetStatus(_snapAngle ? "angle snap 15°" : "angle free");
        }

        /// <summary>
        /// Thickens or thins the selected wall by one step, and pins it.
        ///
        /// Thickness gets buttons rather than a handle on purpose: the near and far faces
        /// are ~12 cm apart, so their corners land within a few pixels of each other on a
        /// phone and there is no reliable way to say which one a thumb meant.
        /// </summary>
        public void NudgeWidth(int steps)
        {
            if (_selectedId < 0) { SetStatus("select a wall first"); return; }
            if (!roomBuilder.TryGetWall(_selectedId, out var wall)) return;

            float current = wall.width > 0f ? wall.width : minWallWidth;
            float width = Mathf.Clamp(current + steps * widthStep, minWallWidth, maxWallWidth);
            if (Mathf.Approximately(width, current)) return;

            roomBuilder.TryUpdateWallGeometry(_selectedId, wall.start, wall.end,
                                              wall.baseY, wall.height, width);
            roomBuilder.SetWallHighlight(_selectedId, true);
            SetStatus($"thickness {width * 100f:F0} cm");
            Commit();
        }

        void Commit() => Edited?.Invoke();

        void Select(int wallId)
        {
            if (_selectedId == wallId) return;
            if (_selectedId >= 0) roomBuilder.SetWallHighlight(_selectedId, false);
            _selectedId = wallId;
            if (_selectedId >= 0) roomBuilder.SetWallHighlight(_selectedId, true);
        }

        void SetStatus(string s) => _status = s;

        /// <summary>Rotate <paramref name="p"/> about <paramref name="pivot"/> in the XZ
        /// plane by <paramref name="radians"/>; y is carried through unchanged.</summary>
        static Vector3 RotateAboutY(Vector3 p, Vector3 pivot, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            float dx = p.x - pivot.x, dz = p.z - pivot.z;
            return new Vector3(pivot.x + dx * c - dz * s, p.y, pivot.z + dx * s + dz * c);
        }

        // ── Hit testing (no physics) ──────────────────────────────────────────

        /// <summary>
        /// The wall under <paramref name="screenPos"/>, or -1.
        ///
        /// Hit testing is against the wall's projected <b>face</b> — the quad through its
        /// four near-face corners — not against a line. The first version measured distance
        /// to the wall's mid-line, which fails exactly where this is used: in AR you stand
        /// one or two metres from a wall that fills the screen, and a tap on its upper half
        /// is then hundreds of pixels from that mid-line, further than any sane grab
        /// radius. Tapping anywhere on the wall you can see now selects it, and the radius
        /// only widens that to a near miss.
        ///
        /// When a tap lands inside two walls — looking into a corner, or through one wall
        /// at another — the nearer wall wins, because that is the one you can see.
        /// </summary>
        int PickWall(Vector2 screenPos)
        {
            float slack = Screen.width * grabRadiusFraction;

            int best = -1;
            float bestDist = float.MaxValue;
            float bestDepth = float.MaxValue;

            // Diagnostics for the HUD: this runs on a phone with no console attached, so
            // "nothing selected" has to be able to say why.
            _lastPickCandidates = 0;
            _lastPickNearest = -1f;

            foreach (var w in roomBuilder.Walls)
            {
                if (!ProjectWallFace(w, out var quad)) continue;
                _lastPickCandidates++;

                float d = DistanceToQuad(screenPos, quad);
                if (_lastPickNearest < 0f || d < _lastPickNearest) _lastPickNearest = d;
                if (d > slack) continue;

                // Depth to the face's centre, which is enough to order two walls the tap
                // is inside of; exact per-pixel depth would need the ray intersection this
                // class deliberately does without.
                float depth = WallDepth(w);

                // A hit inside the face (d == 0) beats any near miss; between two of the
                // same kind, the nearer wall wins.
                bool better = d < bestDist - 0.5f
                           || (Mathf.Abs(d - bestDist) <= 0.5f && depth < bestDepth);
                if (!better) continue;

                bestDist = d;
                bestDepth = depth;
                best = w.id;
            }
            return best;
        }

        /// <summary>Distance from the camera to the centre of a wall's near face.</summary>
        float WallDepth(in WallSegment w)
        {
            var mid = (w.start + w.end) * 0.5f;
            mid.y = w.baseY + Mathf.Max(w.height, 0.1f) * 0.5f;
            return Vector3.Distance(_cam.transform.position, _mapOrigin.TransformPoint(mid));
        }

        /// <summary>
        /// Projects a wall's four near-face corners to screen space, in quad order
        /// (base start, base end, top end, top start).
        ///
        /// The horizontal edges are clipped to the camera's near plane rather than the wall
        /// being dropped when a corner falls behind it. Dropping is what the first version
        /// did, and it discards precisely the walls you stand closest to: a long wall
        /// running past your shoulder has one end behind the camera almost always, while
        /// most of its surface still fills the screen in front of you.
        /// </summary>
        bool ProjectWallFace(in WallSegment w, out Vector2[] quad)
        {
            quad = null;

            float top = w.baseY + Mathf.Max(w.height, 0.1f);
            var aBase = _mapOrigin.TransformPoint(new Vector3(w.start.x, w.baseY, w.start.z));
            var bBase = _mapOrigin.TransformPoint(new Vector3(w.end.x, w.baseY, w.end.z));
            var aTop = _mapOrigin.TransformPoint(new Vector3(w.start.x, top, w.start.z));
            var bTop = _mapOrigin.TransformPoint(new Vector3(w.end.x, top, w.end.z));

            // Clipped along the wall at both heights, so the quad keeps its shape.
            if (!ClipToNearPlane(ref aBase, ref bBase)) return false;
            if (!ClipToNearPlane(ref aTop, ref bTop)) return false;

            quad = new[]
            {
                (Vector2)_cam.WorldToScreenPoint(aBase),
                (Vector2)_cam.WorldToScreenPoint(bBase),
                (Vector2)_cam.WorldToScreenPoint(bTop),
                (Vector2)_cam.WorldToScreenPoint(aTop),
            };
            return true;
        }

        /// <summary>
        /// Trims a world-space segment so both ends sit in front of the near plane.
        /// Returns false when the whole segment is behind the camera.
        /// </summary>
        bool ClipToNearPlane(ref Vector3 a, ref Vector3 b)
        {
            var camPos = _cam.transform.position;
            var fwd = _cam.transform.forward;
            float near = _cam.nearClipPlane + 0.01f;

            float da = Vector3.Dot(a - camPos, fwd);
            float db = Vector3.Dot(b - camPos, fwd);

            if (da < near && db < near) return false;
            if (da >= near && db >= near) return true;

            // Exactly one end is behind: pull it up to the near plane along the segment.
            float t = (near - da) / (db - da);
            var onPlane = Vector3.Lerp(a, b, t);
            if (da < near) a = onPlane; else b = onPlane;
            return true;
        }

        /// <summary>
        /// 0 when <paramref name="p"/> is inside the quad, otherwise the distance to its
        /// nearest edge. The quad is convex — a rectangle in space stays convex under
        /// projection once it is clipped to the near plane — so the inside test is a
        /// consistent sign across the edge cross-products. It accepts either winding, which
        /// flips with the side of the wall you are standing on.
        /// </summary>
        static float DistanceToQuad(Vector2 p, Vector2[] q)
        {
            bool anyPositive = false, anyNegative = false;
            for (int i = 0; i < q.Length; i++)
            {
                var a = q[i];
                var b = q[(i + 1) % q.Length];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (cross > 0f) anyPositive = true;
                else if (cross < 0f) anyNegative = true;
            }
            if (!(anyPositive && anyNegative)) return 0f;   // same side of every edge

            float best = float.MaxValue;
            for (int i = 0; i < q.Length; i++)
                best = Mathf.Min(best, PointToSegment(p, q[i], q[(i + 1) % q.Length]));
            return best;
        }

        static float PointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// Camera ray through <paramref name="screenPos"/> intersected with the floor,
        /// returned in map-origin-local space. Uses InverseTransformPoint rather than a
        /// subtraction because the origin carries an arbitrary yaw and can be moved by
        /// ADJUST ORIGIN or the relocalizer at any time.
        /// </summary>
        bool FloorPointUnder(Vector2 screenPos, out Vector3 local)
            => PlanePointUnder(screenPos, 0f, out local);

        /// <summary>
        /// As <see cref="FloorPointUnder"/>, but on a horizontal plane
        /// <paramref name="localY"/> metres above the origin floor.
        ///
        /// A lifted wall's footprint has to be dragged on its own base plane: projecting
        /// the finger onto the floor instead would shift the corner by the parallax
        /// between the two planes, which at arm's length on a half-height wall is tens of
        /// centimetres — larger than the error being corrected.
        /// </summary>
        bool PlanePointUnder(Vector2 screenPos, float localY, out Vector3 local)
        {
            local = default;
            // The origin is yaw-only by construction (the bootstrap levels it), so a
            // local height is a world height offset.
            float planeY = _mapOrigin.position.y + localY;

            var ray = _cam.ScreenPointToRay(screenPos);
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;

            local = _mapOrigin.InverseTransformPoint(ray.origin + ray.direction * t);
            local.y = localY;
            return true;
        }

        // ── Sub-HUD ───────────────────────────────────────────────────────────

        // Kept inside one screen region so IsOverEditUI can exclude it from the room
        // touch handling. GUI y runs downward, hence the flip when testing.
        const float UiX = 0.05f, UiY = 0.30f, UiW = 0.40f, UiH = 0.07f;
        const float UiRow = UiH + 0.01f;

        /// <summary>Rows currently drawn: a fourth exists in every mode except Add
        /// (Corners' width/snap split, Rotate's angle toggle, COMBINE elsewhere).</summary>
        int UiRows => Mode == EditMode.Add ? 3 : 4;

        bool IsOverEditUI(Vector2 screenPos)
        {
            float gx = screenPos.x / Screen.width;
            float gy = 1f - screenPos.y / Screen.height;

            if (gx < UiX || gx > UiX + UiW) return false;

            // Each drawn row is tested on its own rather than as one block spanning the
            // whole column. The block form swallowed the gaps between the buttons and a
            // margin below the last one — a band across the middle of the screen where a
            // tap on a wall silently did nothing, which is most of where the walls are.
            for (int row = 0; row < UiRows; row++)
            {
                float top = UiY + UiRow * row;
                if (gy >= top && gy <= top + UiH) return true;
            }
            return false;
        }

        void OnGUI()
        {
            if (!Active) return;

            string modeLabel = Mode switch
            {
                EditMode.Ends => "MODE: ENDS",
                EditMode.Corners => "MODE: CORNERS",
                EditMode.Rotate => "MODE: ROTATE",
                EditMode.Combine => "MODE: COMBINE",
                _ => "MODE: MOVE",
            };
            // ADD borrows the mode row's label while it is running, so the button that got
            // you there is also the one that gets you out.
            if (Button(UiX, UiY, UiW, UiH, Mode == EditMode.Add ? "MODE: ADD" : modeLabel))
                CycleMode();

            if (Button(UiX, UiY + UiRow, UiW, UiH,
                       Mode == EditMode.Add ? "CANCEL ADD" : "ADD WALL"))
                SetMode(Mode == EditMode.Add ? EditMode.Move : EditMode.Add);

            if (Button(UiX, UiY + UiRow * 2f, UiW, UiH, "DELETE WALL"))
                DeleteSelected();

            // Row 3 is mode-specific.
            float row3 = UiY + UiRow * 3f;
            float half = (UiW - 0.01f) / 2f;
            if (Mode == EditMode.Rotate)
            {
                if (Button(UiX, row3, half, UiH, _snapAngle ? "ANGLE: 15°" : "ANGLE: FREE"))
                    ToggleAngleSnap();
                if (Button(UiX + half + 0.01f, row3, half, UiH, "DONE"))
                    SetMode(EditMode.Move);
                return;
            }
            if (Mode == EditMode.Combine)
            {
                if (Button(UiX, row3, UiW, UiH, "CANCEL COMBINE"))
                    SetMode(EditMode.Move);
                return;
            }
            if (Mode == EditMode.Move || Mode == EditMode.Ends)
            {
                if (Button(UiX, row3, half, UiH, "ROTATE"))
                    SetMode(EditMode.Rotate);
                if (Button(UiX + half + 0.01f, row3, half, UiH, "COMBINE"))
                    SetMode(EditMode.Combine);
                return;
            }

            if (Mode != EditMode.Corners) return;

            // Split three ways: snapping, and the two thickness nudges that stand in for
            // the near/far corner handles Corners mode deliberately omits.
            float third = (UiW - 0.02f) / 3f;
            float y = row3;
            if (Button(UiX, y, third, UiH, _snapCorners ? "SNAP: ON" : "SNAP: OFF"))
                ToggleSnap();
            if (Button(UiX + third + 0.01f, y, third, UiH, "W -"))
                NudgeWidth(-1);
            if (Button(UiX + (third + 0.01f) * 2f, y, third, UiH, "W +"))
                NudgeWidth(+1);
        }

        /// <summary>Prompt text for the manager to render in the shared HUD label.</summary>
        public string Prompt()
        {
            string head = Mode switch
            {
                EditMode.Add => "TAP TWO FLOOR POINTS TO DRAW A WALL",
                EditMode.Ends => "DRAG A WALL END TO STRETCH IT",
                EditMode.Corners => "DRAG A CORNER CUBE — BLUE = FLOOR, ORANGE = TOP\n" +
                                    "2 fingers = lift the wall's bottom edge",
                EditMode.Rotate => "DRAG AROUND A WALL TO PIVOT IT\n" +
                                   "turns about its centre, length kept",
                EditMode.Combine => "TAP TWO WALLS TO FUSE THEM INTO ONE",
                _ => "TAP A WALL, DRAG IT ONTO THE REAL ONE",
            };

            // In Corners mode the box dimensions are what is being edited, so they belong
            // on screen: reading "1.95 m" back is how the user knows the drag landed.
            string sel = " | nothing selected";
            if (_selectedId >= 0)
            {
                sel = $" | selected {_selectedId}";
                if (Mode == EditMode.Corners && roomBuilder != null &&
                    roomBuilder.TryGetWall(_selectedId, out var s))
                {
                    float width = s.width > 0f ? s.width : 0f;
                    sel += $" | {(s.end - s.start).magnitude:F2} × {s.height:F2} m" +
                           $" | base {s.baseY:F2} | {width * 100f:F0} cm thick";
                }
            }
            int pinned = 0;
            if (roomBuilder != null)
                foreach (var w in roomBuilder.Walls) if (w.pinned) pinned++;
            string tail = string.IsNullOrEmpty(_status) ? "" : $"\n{_status}";

            // Diagnostics. Selection has no console to log to and no way to be inspected
            // once the app is on a phone, so the numbers that decide it are on screen:
            // fingers seen this frame, walls in the model, walls actually in front of the
            // camera on the last tap, and how far that tap fell from the nearest one
            // against the radius it had to beat. "0 in view" or a distance far above the
            // radius each point at a different bug.
            int walls = roomBuilder != null ? roomBuilder.Walls.Count : 0;
            string diag = $"\ntouch {_lastTouchCount} | walls {walls}";
            if (_lastPickCandidates >= 0)
            {
                diag += $" | {_lastPickCandidates} in view";
                if (_lastPickNearest >= 0f)
                    diag += $" | tap {_lastPickNearest:F0}px / {Screen.width * grabRadiusFraction:F0}px";
            }

            return $"{head}\n{pinned} pinned{sel}{tail}{diag}";
        }

        static bool Button(float x, float y, float w, float h, string label) =>
            GUI.Button(new Rect(Screen.width * x, Screen.height * y,
                                Screen.width * w, Screen.height * h), label);
    }
}

