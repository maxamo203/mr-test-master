using System.Linq;
using Gameplay;
using UnityEditor;
using UnityEngine;

// Arma el prefab del LIBRO RITUAL a partir del FBX de 3 piezas (tapa, tapa, lomo), con el
// RitualBookView ya cableado: qué pieza es cada una, el eje de la bisagra y hacia dónde
// cierra cada tapa. También normaliza el tamaño a metros y apoya el libro sobre el origen
// (que es donde está la imagen de referencia física).
//
// Uso: menú  Mortuorium > Crear prefab del Libro Ritual  (re-ejecutable: pisa el prefab).
// Después conviene abrir el prefab y mover el slider "Apertura Preview" del inspector para
// verificar que las tapas cierren para el lado correcto — el modelo puede venir exportado
// con cualquier convención de ejes y ahí se termina de ajustar a ojo.
//
// El prefab va a Assets/Resources porque se instancia en runtime (Resources.Load desde
// RitualBookView.TrySpawn) y así entra seguro al build con sus materiales.
public static class RitualBookPrefabSetup
{
    private const string FbxPath      = "Assets/Libro Ritual/libroritual.fbx";
    private const string PrefabPath   = "Assets/Resources/LibroRitual.prefab";

    // Tamaño real del libro abierto (lado más largo, en metros). El FBX viene en unidades
    // de Blender, que no son metros, así que lo re-escalamos acá una sola vez en vez de
    // pagarlo en runtime.
    private const float TamanoMetros = 0.30f;

    // Grados que rota cada tapa al cerrarse. 90 = las dos suben y se juntan en el medio.
    // Es sólo el punto de partida: se afina con el preview del inspector.
    private const float CierreGrados = 90f;

    [MenuItem("Mortuorium/Crear prefab del Libro Ritual")]
    public static void Crear()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null)
        {
            Debug.LogError($"[LibroRitual] No se encontró el modelo en {FbxPath}.");
            return;
        }

        var modelo = Object.Instantiate(fbx);
        modelo.name = "modelo";

        try
        {
            var partes = modelo.GetComponentsInChildren<MeshFilter>()
                               .Where(mf => mf.sharedMesh != null)
                               .Select(mf => mf.transform)
                               .ToArray();

            if (partes.Length != 3)
            {
                Debug.LogError($"[LibroRitual] El modelo tiene {partes.Length} piezas con malla y se " +
                               "esperaban 3 (tapa delantera, tapa trasera y lomo). Revisá el FBX.");
                return;
            }

            // Las TAPAS son las dos cuyos pivotes están más cerca entre sí: las dos
            // comparten la bisagra. La que sobra es el lomo.
            IdentificarPartes(partes, out var tapaA, out var tapaB, out var lomo);

            // 1) Enderezar: dejamos el lomo alineado con +Z. La dirección del lomo sale de
            //    unir los CENTROS de las dos tapas —que en un libro abierto caen una a cada
            //    lado de la bisagra— así que ese vector es perpendicular al lomo. No sirve
            //    mirar los pivotes (pueden estar separados a lo largo de la bisagra O a lo
            //    ancho del lomo, y las dos cosas se ven igual) ni la AABB del lomo (si el
            //    modelo viene rotado, su caja alineada a ejes no dice nada).
            //    Enderezarlo además hace que la AABB quede ajustada, y de ahí que el tamaño
            //    en metros del paso siguiente sea el real y no el de una caja inflada.
            var eje = Enderezar(modelo.transform, tapaA, tapaB);

            // 2) Tamaño real + apoyado sobre el origen (la imagen de referencia).
            EscalarYApoyar(modelo.transform);

            // 3) Signo de cada tapa: la rotamos para el lado que LEVANTA su borde libre.
            //    Al rotar +θ sobre 'eje', un punto r se mueve en dirección (eje × r); si
            //    esa componente Y es negativa, hay que cerrar con el ángulo opuesto.
            float cierreA = SignoDeCierre(tapaA, eje) * CierreGrados;
            float cierreB = SignoDeCierre(tapaB, eje) * CierreGrados;

            // El eje va en el espacio del PADRE de las tapas: así las dos giran sobre la
            // misma bisagra física aunque el modelo traiga una de ellas espejada.
            var padre    = tapaA.parent;
            var ejeLocal = padre != null ? padre.InverseTransformDirection(eje) : eje;

            // 4) Raíz limpia (identity) para colgarla del anchor sin arrastrar la escala.
            var root = new GameObject("LibroRitual");
            modelo.transform.SetParent(root.transform, worldPositionStays: true);

            var view = root.AddComponent<RitualBookView>();
            view.EditorConfigurar(tapaA, tapaB, lomo, ejeLocal, cierreA, cierreB);

            // Los nombres van al log DESPUÉS de destruir la copia de trabajo, así que hay
            // que quedárselos ahora: los Transform mueren con ella.
            string nA = tapaA.name, nB = tapaB.name, nLomo = lomo.name;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            modelo = null;

            AssetDatabase.SaveAssets();
            Debug.Log($"[LibroRitual] Prefab creado en {PrefabPath}.\n" +
                      $"  tapas = '{nA}' ({cierreA:+0;-0}°) y '{nB}' ({cierreB:+0;-0}°)\n" +
                      $"  lomo  = '{nLomo}'   eje de bisagra = {ejeLocal}\n" +
                      "  Abrí el prefab y mové 'Apertura Preview' en el inspector para verificar " +
                      "que cierre bien; si alguna tapa gira para el lado que no es, cambiale el " +
                      "signo a su ángulo de cierre.");
        }
        finally
        {
            if (modelo != null) Object.DestroyImmediate(modelo);
        }
    }

    // Tapas = el par de pivotes más cercano entre sí (comparten la bisagra); lomo = el resto.
    private static void IdentificarPartes(Transform[] p, out Transform tapaA, out Transform tapaB, out Transform lomo)
    {
        float d01 = Vector3.Distance(p[0].position, p[1].position);
        float d02 = Vector3.Distance(p[0].position, p[2].position);
        float d12 = Vector3.Distance(p[1].position, p[2].position);

        if (d01 <= d02 && d01 <= d12)      { tapaA = p[0]; tapaB = p[1]; lomo = p[2]; }
        else if (d02 <= d01 && d02 <= d12) { tapaA = p[0]; tapaB = p[2]; lomo = p[1]; }
        else                               { tapaA = p[1]; tapaB = p[2]; lomo = p[0]; }

        // Contraste: el lomo suele ser además la pieza más chica. Si no coincide, el
        // modelo no sigue la convención esperada y conviene revisarlo a mano.
        var masChica = p.OrderBy(t => Volumen(BoundsMundo(t))).First();
        if (masChica != lomo)
            Debug.LogWarning($"[LibroRitual] Por los pivotes el lomo sería '{lomo.name}', pero la pieza " +
                             $"más chica es '{masChica.name}'. Verificá la asignación en el inspector " +
                             "del prefab (Tapa A / Tapa B / Lomo).");
    }

    // Gira el modelo sobre Y para dejar el lomo alineado con +Z del prefab, y devuelve el
    // eje de la bisagra en MUNDO (que tras el giro es exactamente Vector3.forward).
    private static Vector3 Enderezar(Transform modelo, Transform tapaA, Transform tapaB)
    {
        // Perpendicular al lomo: de una tapa a la otra (en un libro abierto, una a cada lado).
        var d = BoundsMundo(tapaB).center - BoundsMundo(tapaA).center;
        d.y = 0f;
        if (d.sqrMagnitude < 1e-8f)
        {
            Debug.LogWarning("[LibroRitual] Las dos tapas están una sobre la otra: no se puede " +
                             "deducir la dirección del lomo. Uso +Z y habrá que corregir el eje " +
                             "de bisagra a mano en el prefab.");
            return Vector3.forward;
        }

        var lomoDir = Vector3.Cross(Vector3.up, d.normalized);
        float yaw   = Vector3.SignedAngle(lomoDir, Vector3.forward, Vector3.up);
        modelo.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * modelo.rotation;
        return Vector3.forward;
    }

    // El borde libre de la tapa tiene que SUBIR al cerrarse: eso define el signo del giro.
    private static float SignoDeCierre(Transform tapa, Vector3 eje)
    {
        var r = BoundsMundo(tapa).center - tapa.position;
        r.y = 0f;
        if (r.sqrMagnitude < 1e-8f) return 1f;
        return Vector3.Cross(eje, r.normalized).y >= 0f ? 1f : -1f;
    }

    // Lleva el lado más largo a TamanoMetros y apoya el libro sobre el plano Y=0,
    // centrado en el origen (donde está físicamente la imagen de referencia).
    private static void EscalarYApoyar(Transform modelo)
    {
        var b   = BoundsMundo(modelo);
        float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (m > 1e-6f) modelo.localScale = Vector3.one * (TamanoMetros / m);

        b = BoundsMundo(modelo);
        modelo.position -= new Vector3(b.center.x, b.min.y, b.center.z);
    }

    // Bounds en mundo calculadas desde las mallas (y no desde Renderer.bounds, que puede
    // venir de un frame viejo justo después de mover/escalar el transform).
    private static Bounds BoundsMundo(Transform t)
    {
        var filtros = t.GetComponentsInChildren<MeshFilter>().Where(mf => mf.sharedMesh != null).ToArray();
        if (filtros.Length == 0) return new Bounds(t.position, Vector3.zero);

        Bounds total = default;
        bool   first = true;
        foreach (var mf in filtros)
        {
            var local = mf.sharedMesh.bounds;
            var mtx   = mf.transform.localToWorldMatrix;

            // Las 8 esquinas: la AABB local rotada no se puede transformar por su centro.
            for (int i = 0; i < 8; i++)
            {
                var c = local.center + Vector3.Scale(local.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1,
                    (i & 2) == 0 ? -1 : 1,
                    (i & 4) == 0 ? -1 : 1));
                var w = mtx.MultiplyPoint3x4(c);
                if (first) { total = new Bounds(w, Vector3.zero); first = false; }
                else       total.Encapsulate(w);
            }
        }
        return total;
    }

    private static float Volumen(Bounds b) => b.size.x * b.size.y * b.size.z;
}
