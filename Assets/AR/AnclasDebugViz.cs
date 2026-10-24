using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

// Vista de debug (sólo development build): dibuja en el mundo la nube de puntos que ve
// ARKit/ARCore y deja visibles las esferas de los anchor points también durante la
// noche (ámbar = raíz, verde = barrido / a mano, azul = repuesta en partida). Se prende
// desde la pausa ("Anclas y nube de puntos (dev)"), al lado de "Paredes visibles".
//
// Cuesta sólo con el toggle prendido: el ARPointCloudManager queda encendido y la malla
// de puntos se rearma a 4 Hz. En release ni se crea y Activo es siempre false.
public class AnclasDebugViz : MonoBehaviour
{
    private const string Key            = "dbg_anclas_viz";
    private const float  IntervaloMalla = 0.25f;
    private const int    MaxPuntos      = 4000;     // tope de puntos dibujados (6 vértices c/u)
    private const float  TamPunto       = 0.012f;   // m, semieje del octaedro de cada punto

    private static readonly Color ColorPunto = new(0.3f, 0.95f, 0.95f);

    private static bool _activo;
    public static bool Activo => _activo;

    // Puntos de la última malla (lo muestra el panel ANCLAS del Debug HUD).
    public static int PuntosVisibles { get; private set; }

    private static AnclasDebugViz _instancia;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Debug.isDebugBuild || _instancia != null) return;
        _activo = PlayerPrefs.GetInt(Key, 0) == 1;
        var go = new GameObject("AnclasDebugViz");
        DontDestroyOnLoad(go);
        _instancia = go.AddComponent<AnclasDebugViz>();
    }

    public static void SetActivo(bool on)
    {
        if (!Debug.isDebugBuild) return;
        _activo = on;
        PlayerPrefs.SetInt(Key, on ? 1 : 0);
        PlayerPrefs.Save();
        AnchorPointManager.Instance?.SincronizarVisualesDebug();
        if (!on && _instancia != null) _instancia.SoltarNube();
    }

    private ARPointCloudManager _pcm;
    private bool     _buscado;   // ya se buscó en esta escena (sin XROrigin no se reintenta)
    private Mesh     _malla;
    private float    _timer;
    private readonly List<Vector3> _verts   = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<int>     _tris    = new();

    private void OnEnable()  => SceneManager.sceneLoaded += AlCargar;
    private void OnDisable() => SceneManager.sceneLoaded -= AlCargar;

    private void OnDestroy()
    {
        if (_malla != null) Destroy(_malla);
        if (_instancia == this) _instancia = null;
    }

    private void AlCargar(Scene s, LoadSceneMode m)
    {
        _buscado = false;
        _pcm     = null;
        if (_malla != null) _malla.Clear();
        PuntosVisibles = 0;
    }

    private void Update()
    {
        if (!_activo) return;

        if (_pcm == null && !_buscado)
        {
            _buscado = true;
            if (FindAnyObjectByType<XROrigin>() != null)
                _pcm = AnchorQuality.AsegurarPointCloudManager();
        }
        if (_pcm == null) return;
        if (!_pcm.enabled) _pcm.enabled = true;

        _timer += Time.deltaTime;
        if (_timer >= IntervaloMalla)
        {
            _timer = 0f;
            ArmarMalla();
        }

        if (_malla != null && _malla.vertexCount > 0)
            Graphics.DrawMesh(_malla, Matrix4x4.identity, AnchorVisuals.MaterialPara(ColorPunto), 0,
                              null, 0, null, ShadowCastingMode.Off, false);
    }

    // Apagado: la nube vuelve a ser sólo de AnchorQuality (prendida mientras se coloca).
    private void SoltarNube()
    {
        bool calidadLaUsa = AnchorQuality.Instance != null && AnchorQuality.Instance.Activo;
        if (_pcm != null && !calidadLaUsa) _pcm.enabled = false;
        if (_malla != null) _malla.Clear();
        PuntosVisibles = 0;
    }

    // Un octaedro chiquito por feature point, todo en una malla en espacio mundo.
    private void ArmarMalla()
    {
        _verts.Clear();
        _normals.Clear();
        _tris.Clear();

        int puntos = 0;
        foreach (var nube in _pcm.trackables)
        {
            var pos = nube.positions;
            if (pos == null) continue;
            var lista = pos.Value;
            var tf    = nube.transform;
            for (int i = 0; i < lista.Length && puntos < MaxPuntos; i++)
            {
                // ARCore entrega puntos con NaN/infinito (y el transform de la nube puede
                // tenerlos un frame): uno solo deja la malla con bounds NaN y no se dibuja.
                var mundo = tf.TransformPoint(lista[i]);
                if (!Finito(mundo)) continue;
                AgregarOctaedro(mundo);
                puntos++;
            }
        }
        PuntosVisibles = puntos;

        if (_malla == null)
        {
            // 4000 puntos × 6 vértices = 24000 < 65535: alcanza UInt16 (soportado en todo GPU).
            _malla = new Mesh { name = "NubeDePuntosDebug", indexFormat = IndexFormat.UInt16 };
            _malla.MarkDynamic();
        }
        _malla.Clear();
        _malla.SetVertices(_verts);
        _malla.SetNormals(_normals);
        _malla.SetTriangles(_tris, 0);
        _malla.RecalculateBounds();
    }

    private static bool Finito(Vector3 v) =>
        float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    // Vértices: +x, -x, +y, -y, +z, -z. Caras con la normal hacia afuera (convención de
    // Unity: frente = cross(b-a, c-a)).
    private static readonly Vector3[] Ejes =
        { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
    private static readonly int[] Caras =
    {
        2, 4, 0,  2, 0, 5,  2, 5, 1,  2, 1, 4,
        3, 0, 4,  3, 5, 0,  3, 1, 5,  3, 4, 1,
    };

    private void AgregarOctaedro(Vector3 centro)
    {
        int b = _verts.Count;
        for (int i = 0; i < Ejes.Length; i++)
        {
            _verts.Add(centro + Ejes[i] * TamPunto);
            _normals.Add(Ejes[i]);
        }
        for (int i = 0; i < Caras.Length; i++) _tris.Add(b + Caras[i]);
    }
}
