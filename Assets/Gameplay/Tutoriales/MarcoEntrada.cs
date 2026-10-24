using UnityEngine;

namespace Gameplay
{
    // Contorno de una entrada marcada en el armado rápido del Sorken (ArmadoRapido): un
    // rectángulo del tamaño de la puerta/ventana sobre la pared real, para que el jugador
    // vea qué marcó. Cuelga del MarkerObject (sigue al 0,0) y se oculta durante la partida:
    // ahí la entrada es sólo el punto por donde sale el Sorken.
    // Además prende en partida la pared de referencia de la entrada (WallObject.SoloReferencia)
    // cuando SceneOccluderMode ya le puso el material oclusor invisible: así la pared real
    // alrededor del marco tapa al Sorken aunque el equipo no mida profundidad.
    [RequireComponent(typeof(LineRenderer))]
    public class MarcoEntrada : MonoBehaviour
    {
        private static Material _mat;
        private LineRenderer _lr;
        private MeshRenderer _pared;

        // Rectángulo en el plano local XY del marcador (X a lo largo de la pared, Y arriba,
        // Z = normal hacia el jugador), de abajo a arriba relativo al marcador.
        public static MarcoEntrada Crear(Transform marcador, float ancho, float abajo, float arriba,
                                         Scanner.WallObject pared)
        {
            var go = new GameObject("MarcoEntrada");
            go.transform.SetParent(marcador, false);
            // Apenas delante de la pared real, para que la oclusión por profundidad no lo coma.
            go.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            var lr = go.AddComponent<LineRenderer>();
            var m = go.AddComponent<MarcoEntrada>();
            m._lr = lr;
            m._pared = pared != null ? pared.GetComponent<MeshRenderer>() : null;

            if (_mat == null)
            {
                var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                _mat = new Material(sh) { name = "MarcoEntrada (runtime)" };
                _mat.color = new Color(0.85f, 0.16f, 0.14f, 1f);
            }
            lr.sharedMaterial = _mat;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.widthMultiplier = 0.025f;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            float x = ancho * 0.5f;
            lr.positionCount = 4;
            lr.SetPosition(0, new Vector3(-x, abajo, 0f));
            lr.SetPosition(1, new Vector3(-x, arriba, 0f));
            lr.SetPosition(2, new Vector3( x, arriba, 0f));
            lr.SetPosition(3, new Vector3( x, abajo, 0f));
            m.Update();
            return m;
        }

        private void Update()
        {
            var nm = NetworkManager.Instance;
            bool ver = nm == null || !nm.GameStarted;
            if (_lr.enabled != ver) _lr.enabled = ver;
            // Sólo con el oclusor invisible puesto: con el material del escáner (o el sólido
            // de debug) la pared se vería como un bloque alrededor de la abertura.
            var occ = SceneOccluderMode.Instance;
            bool pared = !ver && occ != null && occ.Mode == SceneOccluderMode.DisplayMode.InvisibleOccluder;
            if (_pared != null && _pared.enabled != pared) _pared.enabled = pared;
        }
    }
}
