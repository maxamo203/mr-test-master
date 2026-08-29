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
        private readonly List<MarkerObject> _markers = new();

        public IReadOnlyList<WallObject> Walls => _walls;
        public IReadOnlyList<CubeObject> Cubes => _cubes;
        public IReadOnlyList<MarkerObject> Markers => _markers;

        // Sube en cada ClearAll: identifica el "contenido" cargado ahora mismo. Los
        // consumidores que cachean geometría derivada (SorkerNav) la incluyen en su firma
        // para detectar un cambio de mapa — contar paredes y cubos no alcanza, porque dos
        // escaneos distintos pueden coincidir en cantidad y dejar el caché viejo en pie.
        public int Generacion { get; private set; }

        // Busca una pared por Id (usado al reconstruir marcadores, que son relativos
        // a una pared).
        public WallObject FindWall(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var w in _walls) if (w != null && w.Id == id) return w;
            return null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public void Register(WallObject w)   { if (!_walls.Contains(w)) _walls.Add(w); }
        public void Register(CubeObject c)   { if (!_cubes.Contains(c)) _cubes.Add(c); }
        public void Register(MarkerObject m) { if (!_markers.Contains(m)) _markers.Add(m); }

        public void Unregister(WallObject w)   => _walls.Remove(w);
        public void Unregister(CubeObject c)   => _cubes.Remove(c);
        public void Unregister(MarkerObject m) => _markers.Remove(m);

        public void ClearAll()
        {
            Generacion++;

            // Los marcadores primero: son hijos-logicos de las paredes.
            foreach (var m in _markers) if (m != null) Destroy(m.gameObject);
            foreach (var w in _walls) if (w != null) Destroy(w.gameObject);
            foreach (var c in _cubes) if (c != null) Destroy(c.gameObject);
            _markers.Clear();
            _walls.Clear();
            _cubes.Clear();
            if (FloorPoint.Instance != null) FloorPoint.Instance.Delete();
        }

        public ScanData Capture(string name)
        {
            var data = new ScanData { name = name };
            foreach (var w in _walls) if (w != null) data.walls.Add(w.ToData());
            foreach (var c in _cubes) if (c != null) data.cubes.Add(c.ToData());
            foreach (var m in _markers) if (m != null) data.markers.Add(m.ToData());
            if (FloorPoint.Instance != null)
            {
                data.hasFloor   = true;
                data.floorLocal = new Vec3(FloorPoint.Instance.LocalPosition);
            }
            return data;
        }
    }
}
