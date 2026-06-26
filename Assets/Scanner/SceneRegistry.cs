using System.Collections.Generic;
using UnityEngine;

namespace Scanner
{
    // Registro vivo de paredes y cubos en escena. Los Builders se registran
    // aca; el ScanSerializer enumera para guardar; al cargar, ClearAll lo vacia.
    [DefaultExecutionOrder(-40)]
    public class SceneRegistry : MonoBehaviour
    {
        public static SceneRegistry Instance { get; private set; }

        private readonly List<WallObject> _walls = new();
        private readonly List<CubeObject> _cubes = new();

        public IReadOnlyList<WallObject> Walls => _walls;
        public IReadOnlyList<CubeObject> Cubes => _cubes;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public void Register(WallObject w) { if (!_walls.Contains(w)) _walls.Add(w); }
        public void Register(CubeObject c) { if (!_cubes.Contains(c)) _cubes.Add(c); }

        public void Unregister(WallObject w) => _walls.Remove(w);
        public void Unregister(CubeObject c) => _cubes.Remove(c);

        public void ClearAll()
        {
            foreach (var w in _walls) if (w != null) Destroy(w.gameObject);
            foreach (var c in _cubes) if (c != null) Destroy(c.gameObject);
            _walls.Clear();
            _cubes.Clear();
            if (FloorPoint.Instance != null) FloorPoint.Instance.Delete();
        }

        public ScanData Capture(string name)
        {
            var data = new ScanData { name = name };
            foreach (var w in _walls) if (w != null) data.walls.Add(w.ToData());
            foreach (var c in _cubes) if (c != null) data.cubes.Add(c.ToData());
            if (FloorPoint.Instance != null)
            {
                data.hasFloor   = true;
                data.floorLocal = new Vec3(FloorPoint.Instance.LocalPosition);
            }
            return data;
        }
    }
}
