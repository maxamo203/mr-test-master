using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Wireframe de DEBUG para LiveWallDetector (ver ese archivo): sin esto, los puntos que
    // se van detectando y la altura de piso estimada son una caja negra en el celular —
    // exactamente el tipo de cosa que hace falta ver para diagnosticar un piso mal
    // estimado (ej. una mesa cerca de la cámara confundida con el piso real).
    //
    // A propósito NO tiene toggle de menú (a diferencia de ArbmosDebug/ArbmosQuietudViz):
    // es para diagnosticar esta feature mientras se termina, no una perilla de tester.
    // Se dibuja siempre que Debug.isDebugBuild sea true — en release el archivo entero es
    // código muerto que nunca aloca nada (ver CLAUDE.md, tier development build).
    //
    // Mismo mecanismo que ArbmosQuietudViz: Graphics.DrawMesh con Hidden/GizmoOverlay
    // (ZTest Always, se ve por encima de las paredes/piso real) — mallas y material se
    // crean perezosos la primera vez que se dibuja algo.
    public static class LiveWallDetectorViz
    {
        private const int Segmentos = 32;

        private static readonly int ID_COLOR = Shader.PropertyToID("_Color");
        private static readonly Color ColorPunto = new Color(1f, 0.55f, 0.15f, 0.95f); // naranja, igual que la mira del escáner
        private static readonly Color ColorPiso  = new Color(0.2f, 0.6f, 1f, 0.95f);   // azul, igual que FloorPoint

        private static Mesh _cruz;
        private static Mesh _anillo;
        private static Material _mat;
        private static MaterialPropertyBlock _mpb;

        // Un puntito (cruz chica) en cada posición guardada en LiveWallDetector._puntos.
        public static void DibujarPunto(Vector3 pos)
        {
            if (!Debug.isDebugBuild) return;
            if (!Asegurar()) return;

            _mpb.SetColor(ID_COLOR, ColorPunto);
            Graphics.DrawMesh(_cruz, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 0.15f),
                              _mat, 0, null, 0, _mpb, false, false);
        }

        // Anillo horizontal a la altura de piso ESTIMADA, centrado bajo centroXZ (la
        // cámara) — para comparar a ojo contra el piso real.
        public static void DibujarPiso(Vector3 centroXZ, float y)
        {
            if (!Debug.isDebugBuild) return;
            if (!Asegurar()) return;

            _mpb.SetColor(ID_COLOR, ColorPiso);
            var pos = new Vector3(centroXZ.x, y, centroXZ.z);
            Graphics.DrawMesh(_anillo, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 0.5f),
                              _mat, 0, null, 0, _mpb, false, false);
        }

        private static bool Asegurar()
        {
            if (_mat != null) return true;

            var sh = Resources.Load<Shader>("GizmoOverlay") ?? Shader.Find("Hidden/GizmoOverlay")
                     ?? Shader.Find("Unlit/Color");
            if (sh == null) return false;
            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _mpb = new MaterialPropertyBlock();

            _cruz   = MallaCruz();
            _anillo = MallaAnillo();
            return true;
        }

        private static Mesh MallaCruz()
        {
            var v = new List<Vector3>
            {
                -Vector3.right, Vector3.right, -Vector3.up, Vector3.up, -Vector3.forward, Vector3.forward,
            };
            return Construir(v, new List<int> { 0, 1, 2, 3, 4, 5 });
        }

        private static Mesh MallaAnillo()
        {
            var v = new List<Vector3>();
            var idx = new List<int>();
            for (int k = 0; k < Segmentos; k++)
            {
                float t = k / (float)Segmentos * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)));
                idx.Add(k);
                idx.Add((k + 1) % Segmentos);
            }
            return Construir(v, idx);
        }

        private static Mesh Construir(List<Vector3> v, List<int> idx)
        {
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            m.SetVertices(v);
            m.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            // Bounds generosos: evita que el culling lo haga desaparecer con el jugador cerca.
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }
    }
}
