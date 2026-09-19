using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// On-device IMGUI panel for the wall/surface detection thresholds: dial them in against
    /// a live scan and re-run reconstruction without rebuilding the APK. The serialized
    /// fields on <see cref="DepthOccupancyMapper"/> stay the source of truth.
    /// </summary>
    public sealed class DetectionTuningMenu : MonoBehaviour
    {
        [SerializeField] DepthOccupancyMapper mapper;
        [SerializeField] RoomScanningManager manager;

        const string PrefsKey = "Mortuorium.DetectionTuning";

        DepthOccupancyMapper.Tuning _knobs;
        bool _loaded;
        bool _open;
        bool _help;
        Vector2 _scroll;
        string _note;
        GUIStyle _label, _head, _hint;

        void Awake()
        {
            if (mapper == null) mapper = GetComponent<DepthOccupancyMapper>();
            if (manager == null) manager = GetComponent<RoomScanningManager>();
        }

        public bool Open
        {
            get => _open;
            set
            {
                _open = value;
                if (value) LoadFromMapper();
            }
        }

        void LoadFromMapper()
        {
            if (mapper == null) return;
            _knobs = mapper.ReadTuning();
            _loaded = true;
            _note = null;
        }

        void OnGUI()
        {
            if (!_open || mapper == null || manager == null || !manager.IsMapping) return;
            if (!_loaded) LoadFromMapper();
            EnsureStyles();

            GUILayout.BeginArea(SR(0.50f, 0.04f, 0.48f, 0.92f), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("DETECTION TUNING", _head);
            if (GUILayout.Button(_help ? "HELP: ON" : "HELP: OFF", GUILayout.Width(Screen.width * 0.09f)))
                _help = !_help;
            GUILayout.EndHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("Wall band  (needs RESET GRID)", _head);
            FloatRow("normal max", ref _knobs.wallNormalMax, 0.05f, 0.6f,
                "Max tilt a point may have and still count as wall. Up: leaning/noisy surfaces pass. Down: only dead-vertical.");
            FloatRow("band bottom", ref _knobs.bandBottom, 0f, 1f,
                "Points below this height (m) are ignored. Up: skips skirting and low furniture. Down: keeps low detail, lets floor noise in.");
            FloatRow("band top margin", ref _knobs.bandTopMargin, 0f, 0.6f,
                "Points within this (m) of the ceiling are ignored. Up: trims ceiling bleed. Down: full-height walls but ceiling may read as wall.");
            IntRow("min cell frames", ref _knobs.minCellHits, 1, 12,
                "Distinct frames a wall cell must be seen in. Up: slower, cleaner. Down: faster, noisier.");

            GUILayout.Label("Wall column", _head);
            FloatRow("min vert span frac", ref _knobs.minVerticalSpanFraction, 0.1f, 0.9f,
                "Fraction of room height a cell's points must span vertically. Up: rejects furniture. Down: accepts short runs.");
            FloatRow("top reach frac", ref _knobs.wallTopReachFraction, 0.2f, 0.95f,
                "How near the ceiling the column must reach. Up: kills wardrobes/shelves. Down: eye-level walls survive, furniture leaks.");
            FloatRow("column fill frac", ref _knobs.wallColumnFillFraction, 0.1f, 0.9f,
                "Fraction of the column that must be filled floor-to-top. Up: demands a solid wall. Down: tolerates gappy/sparse walls.");
            IntRow("column min frames", ref _knobs.wallColumnMinFrames, 1, 8,
                "Frames a voxel needs before it counts toward a column. Up: noise beside a wall can't borrow its height. Down: walls appear faster.");

            GUILayout.Label("Wall fit", _head);
            IntRow("min segment cells", ref _knobs.minSegmentCells, 3, 20,
                "Min cells for a wall line, and its min length. Up: only long walls, kills stubs. Down: short walls survive, more phantoms.");
            FloatRow("min density frac", ref _knobs.minDensityFraction, 0.1f, 0.9f,
                "Fraction of a fitted line that must be backed by cells. Up: rejects lines across gaps/noise. Down: tolerates sparse fits.");
            FloatRow("max run gap", ref _knobs.maxRunGap, 0.1f, 1.5f,
                "Gap (m) along a line before it splits in two. Up: bridges wider gaps into one wall. Down: splits at every doorway.");
            FloatRow("bridge gap", ref _knobs.maxBridgeGap, 0.5f, 4f,
                "Widest opening (m) the room outline may close over. Up: outline closes more (green), openings sealed. Down: stays open at wide gaps.");
            BoolRow("drop loose walls", ref _knobs.dropUnattachedWalls,
                "Drop walls diagonal to the room frame and merge duplicate overlapping ones. On for square rooms; off if walls are truly angled.");
            BoolRow("claim built footprint", ref _knobs.claimBuiltWallFootprint,
                "Ignore new detection beside a wall built last pass. On: stops phantoms off a real wall's ridge. Off: every pass re-fits freely.");
            FloatRow("ridge claim margin", ref _knobs.wallRidgeClaimMargin, 0.1f, 0.6f,
                "Half-width (m) of that ignored band. Up: kills phantoms further out, may eat a close parallel wall. Down: only the immediate ridge.");
            FloatRow("ridge core keep", ref _knobs.wallRidgeCoreKeep, 0.02f, 0.2f,
                "Half-width (m) kept on the wall line so it still re-fits. Up: safer re-fit. Down: claims more but a wall may thin or drift.");
            FloatRow("collinear merge angle", ref _knobs.collinearMergeAngle, 2f, 30f,
                "How straight two end-to-end fragments must be to fuse into one wall. Up: fuses more aggressively, a slightly bent wall may merge. Down: only near-perfectly straight fragments fuse.");
            FloatRow("floor edge min length", ref _knobs.floorEdgeMinLength, 0.05f, 1f,
                "Shortest floor-boundary edge that can become a wall candidate, for the FLOOR / FLOOR+DEPTH wall sources. Up: drops more floor-mesh stairstepping. Down: keeps short real wall segments too, but more stairstep noise.");
            IntRow("floor confirm min cells", ref _knobs.floorSeedMinCells, 1, 10,
                "Occupancy cells needed to confirm a floor-boundary edge, for FLOOR+DEPTH only (separate from the vertical-plane bar, so this never affects HYBRID). Down: trusts the floor more, confirms edges with weaker depth backing. Up: needs stronger evidence, closer to HYBRID's rigor.");
            FloatRow("floor confirm density", ref _knobs.floorSeedMinDensityFraction, 0.02f, 0.6f,
                "Fraction of a floor-boundary edge's length that must be backed by occupancy, for FLOOR+DEPTH only. Down: confirms an edge from partial/patchy depth coverage — more aggressive trust in the floor. Up: needs the edge backed almost end-to-end.");

            GUILayout.Label("Anchoring", _head);
            IntRow("anchor hits", ref _knobs.autoAnchorHits, 1, 6,
                "Consecutive rebuilds a wall must match before it's frozen (pinned, cells removed, never re-fit). Up: safer, slower to freeze. Down: freezes faster but may lock in a bad fit.");
            FloatRow("anchor tolerance", ref _knobs.autoAnchorTolerance, 0.05f, 0.5f,
                "How close (m) two rebuilds must land to count as the same wall for anchoring. Up: forgives more drift between passes. Down: only a rock-steady fit anchors.");
            IntRow("anchor miss grace", ref _knobs.autoAnchorMissGrace, 0, 4,
                "Consecutive rebuilds a wall may fail to refit without losing its anchor progress. Up: survives more occluded/noisy passes. Down (0): one bad pass resets it to zero.");

            GUILayout.Label("Surfaces", _head);
            IntRow("min voxel frames", ref _knobs.minVoxelHits, 1, 10,
                "Frames a voxel needs before it counts for a surface. Up: less noise, slower. Down: faster, noisier.");
            IntRow("min surface frames", ref _knobs.minSurfaceHits, 1, 8,
                "Frames a horizontal voxel needs to seed a surface. Up: needs several passes. Down: appears on first glance, more false tops.");
            FloatRow("surface fill frac", ref _knobs.surfaceFillFraction, 0.1f, 0.9f,
                "Fraction of a patch's bounding box that must be filled. Up: rejects scattered patches. Down: accepts loose ones.");
            IntRow("min surface voxels", ref _knobs.minSurfaceVoxels, 3, 40,
                "Min voxels in a patch to become a surface. Up: only big flat tops. Down: small ledges too, more noise.");
            FloatRow("surface min height", ref _knobs.surfaceMinHeight, 0.02f, 1f,
                "Min height (m) above the floor for a surface. Up: ignores low platforms. Down: catches low seats but risks the floor.");
            FloatRow("surface max h frac", ref _knobs.surfaceMaxHeightFraction, 0.4f, 0.98f,
                "Max height (fraction of ceiling) for a surface. Up: allows near-ceiling shelves. Down: ignores high surfaces.");

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("APPLY + REBUILD", GUILayout.Height(Screen.height * 0.05f)))
            {
                mapper.WriteTuning(_knobs);
                manager.RebuildRoom();
                _note = "rebuilt with current values";
            }
            if (GUILayout.Button("APPLY + RESET GRID", GUILayout.Height(Screen.height * 0.05f)))
            {
                mapper.WriteTuning(_knobs);
                manager.ResetScan();
                _note = "grid cleared — walk the room again";
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("DEFAULTS", GUILayout.Height(Screen.height * 0.05f)))
            {
                _knobs = DepthOccupancyMapper.DefaultTuning();
                _note = "defaults loaded — not applied yet";
            }
            if (GUILayout.Button("SAVE", GUILayout.Height(Screen.height * 0.05f)))
            {
                PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(_knobs));
                PlayerPrefs.Save();
                _note = "saved to PlayerPrefs";
            }
            if (GUILayout.Button("LOAD", GUILayout.Height(Screen.height * 0.05f)))
            {
                var json = PlayerPrefs.GetString(PrefsKey, "");
                if (string.IsNullOrEmpty(json)) _note = "nothing saved";
                else { _knobs = JsonUtility.FromJson<DepthOccupancyMapper.Tuning>(json); _note = "loaded from PlayerPrefs"; }
            }
            if (GUILayout.Button("CLOSE", GUILayout.Height(Screen.height * 0.05f)))
                _open = false;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_note)) GUILayout.Label(_note, _label);
            GUILayout.EndArea();
        }

        void FloatRow(string label, ref float value, float min, float max, string help = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.00}", _label, GUILayout.Width(Screen.width * 0.20f));
            float step = (max - min) / 40f;
            if (GUILayout.Button("-", GUILayout.Width(Screen.width * 0.05f))) value = Mathf.Max(min, value - step);
            if (GUILayout.Button("+", GUILayout.Width(Screen.width * 0.05f))) value = Mathf.Min(max, value + step);
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            HelpLine(help);
        }

        void IntRow(string label, ref int value, int min, int max, string help = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value}", _label, GUILayout.Width(Screen.width * 0.20f));
            if (GUILayout.Button("-", GUILayout.Width(Screen.width * 0.05f))) value = Mathf.Max(min, value - 1);
            if (GUILayout.Button("+", GUILayout.Width(Screen.width * 0.05f))) value = Mathf.Min(max, value + 1);
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max));
            GUILayout.EndHorizontal();
            HelpLine(help);
        }

        void BoolRow(string label, ref bool value, string help = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {(value ? "on" : "off")}", _label, GUILayout.Width(Screen.width * 0.20f));
            if (GUILayout.Button(value ? "turn off" : "turn on", GUILayout.Width(Screen.width * 0.15f)))
                value = !value;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            HelpLine(help);
        }

        void HelpLine(string help)
        {
            if (_help && !string.IsNullOrEmpty(help)) GUILayout.Label(help, _hint);
        }

        void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.018f) };
            _head  = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
            _hint  = new GUIStyle(_label) { fontStyle = FontStyle.Italic, wordWrap = true };
            _hint.normal.textColor = new Color(0.75f, 0.8f, 0.9f);
            _hint.fontSize = Mathf.RoundToInt(Screen.height * 0.015f);
        }

        static Rect SR(float x, float y, float w, float h) =>
            new Rect(Screen.width * x, Screen.height * y, Screen.width * w, Screen.height * h);
    }
}
