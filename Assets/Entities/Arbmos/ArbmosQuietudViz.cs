using UnityEngine;
using Gameplay;

// Wireframe de DEBUG de la esfera de quietud del Arbmos (ver ArbmosDirector.UpdateQuietud):
// muestra donde se genero la esfera actual, de que tamaño es y si el jugador sigue adentro.
// Sin esto el gatillo de quietud es una caja negra en el celular — que es justamente donde
// falla (jitter/drift del tracking).
//
// Tier development build: no se dibuja nada si !Debug.isDebugBuild o si el toggle
// (pausa -> Opciones -> ARBMOS (DEV) -> Wireframe) esta apagado. Las mallas y el material
// se crean perezosamente la primera vez que se dibuja, asi que en release el archivo entero
// es codigo muerto que nunca aloca nada.
//
// Se dibuja con Graphics.DrawMesh (mallas de lineas, sin GameObjects ni colliders) y con
// Hidden/GizmoOverlay (Assets/Resources/GizmoOverlay.shader): ZTest Always, asi se ve por
// encima de las paredes escaneadas y de la malla del cuarto — un wireframe de debug tapado
// por la geometria no sirve de nada. Vive en Resources/, asi que no se stripea en el build
// (los materiales runtime con un shader stripeado salen magenta en el celular).
//
// OJO con la geometria: la esfera esta centrada en el PIVOTE del cuerpo, o sea ~40 cm
// DETRAS de la camara, y el jugador esta adentro. Con R medio metro, casi toda queda a la
// espalda o pegada al near clip: en primera persona la esfera en si se ve poco y nada. Por
// eso lo que de verdad se lee es el ANILLO EN EL PISO (mirando hacia abajo, un circulo
// alrededor de los pies) y la COLUMNA vertical en el centro, que se ve al alejarse.
public static class ArbmosQuietudViz
{
    private const int Segmentos = 48;
    private const float AltoColumna = 2.2f;   // metros de la columna vertical del centro

    private static readonly int ID_COLOR = Shader.PropertyToID("_Color");
    private static readonly Color Dentro = new Color(0.35f, 1f, 0.45f, 0.95f);   // adentro
    private static readonly Color Fuera  = new Color(1f, 0.3f, 0.25f, 0.95f);    // afuera

    private static Mesh _esfera;     // 3 circulos ortogonales, radio 1
    private static Mesh _anillo;     // banda horizontal (2 circulos), radio 1 — marca del piso
    private static Mesh _columna;    // linea vertical de 1 m desde el piso
    private static Mesh _cruz;       // cruz chica, radio 1 (posicion actual del jugador)
    private static Material _mat;
    private static MaterialPropertyBlock _mpb;

    // centro/radio: la esfera vigente (radio ya con la tolerancia por giro aplicada).
    // pivote: donde esta el jugador AHORA (para ver cuanto se corrio del centro).
    // sueloY: altura del piso, para el anillo proyectado y la base de la columna.
    public static void Dibujar(Vector3 centro, float radio, Vector3 pivote, float sueloY, bool dentro)
    {
        if (!ArbmosDebug.Wireframe) return;
        if (!Asegurar()) return;

        _mpb.SetColor(ID_COLOR, dentro ? Dentro : Fuera);
        var piso = new Vector3(centro.x, sueloY + 0.01f, centro.z);

        Dibujar(_esfera,  centro, radio);
        Dibujar(_anillo,  piso,   radio);
        Dibujar(_cruz,    pivote, 0.12f);
        Graphics.DrawMesh(_columna, Matrix4x4.TRS(piso, Quaternion.identity,
                                                  new Vector3(1f, AltoColumna, 1f)),
                          _mat, 0, null, 0, _mpb, false, false);
    }

    private static void Dibujar(Mesh m, Vector3 pos, float escala)
    {
        Graphics.DrawMesh(m, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * escala),
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

        _esfera  = MallaEsfera();
        _anillo  = MallaAnillo();
        _columna = MallaColumna();
        _cruz    = MallaCruz();
        return true;
    }

    // Tres circulos ortogonales: el ecuador (horizontal) es el que importa —el gatillo
    // mide distancia HORIZONTAL, agacharse o levantar el telefono no cuenta como caminar—
    // y los dos verticales le dan volumen para ubicarla en la escena.
    private static Mesh MallaEsfera()
    {
        var v = new System.Collections.Generic.List<Vector3>();
        var i = new System.Collections.Generic.List<int>();
        Circulo(v, i, Vector3.right,   Vector3.forward);
        Circulo(v, i, Vector3.right,   Vector3.up);
        Circulo(v, i, Vector3.forward, Vector3.up);
        return Construir(v, i);
    }

    // Marca del piso: dos circulos horizontales a distinta altura, para que se lea como
    // una banda y no como una linea de un pixel cuando la mirás casi de canto.
    private static Mesh MallaAnillo()
    {
        var v = new System.Collections.Generic.List<Vector3>();
        var i = new System.Collections.Generic.List<int>();
        Circulo(v, i, Vector3.right, Vector3.forward);
        int b0 = v.Count;
        Circulo(v, i, Vector3.right, Vector3.forward);
        for (int k = b0; k < v.Count; k++) v[k] += Vector3.up * 0.04f;
        return Construir(v, i);
    }

    // Columna vertical en el centro de la esfera: es lo que te dice DONDE se genero,
    // visible de lejos y cuando la esfera te queda a la espalda. Escala Y = altura.
    private static Mesh MallaColumna()
    {
        var v = new System.Collections.Generic.List<Vector3> { Vector3.zero, Vector3.up };
        return Construir(v, new System.Collections.Generic.List<int> { 0, 1 });
    }

    private static Mesh MallaCruz()
    {
        var v = new System.Collections.Generic.List<Vector3>
        {
            -Vector3.right, Vector3.right, -Vector3.up, Vector3.up, -Vector3.forward, Vector3.forward,
        };
        return Construir(v, new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4, 5 });
    }

    private static void Circulo(System.Collections.Generic.List<Vector3> v,
                                System.Collections.Generic.List<int> idx, Vector3 a, Vector3 b)
    {
        int b0 = v.Count;
        for (int k = 0; k < Segmentos; k++)
        {
            float t = k / (float)Segmentos * Mathf.PI * 2f;
            v.Add(a * Mathf.Cos(t) + b * Mathf.Sin(t));
            idx.Add(b0 + k);
            idx.Add(b0 + (k + 1) % Segmentos);
        }
    }

    private static Mesh Construir(System.Collections.Generic.List<Vector3> v,
                                  System.Collections.Generic.List<int> idx)
    {
        var m = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        m.SetVertices(v);
        m.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
        // El wireframe se dibuja con una escala uniforme distinta por llamada; unos bounds
        // generosos evitan que el culling lo haga desaparecer al estar el jugador adentro.
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
        return m;
    }
}
