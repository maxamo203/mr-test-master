using UnityEngine;

// Esferas de marcador para anchors (el de la imagen y los anchor points extra).
// Vive aparte porque lo usan ARImageAnchor y AnchorPointManager.
public static class AnchorVisuals
{
    private static Mesh _sphereMesh;

    // Construimos la esfera con la malla built-in + MeshRenderer en vez de
    // GameObject.CreatePrimitive: este último intenta agregar un SphereCollider y,
    // si el módulo Physics está stripeado en el build (IL2CPP), tira
    // "class SphereCollider doesn't exist" y rompía la confirmación del anchor.
    // El visual no necesita colliders.
    public static GameObject MakeSphere(Transform parent, Vector3 localPos, float scale, Color color)
    {
        if (_sphereMesh == null) _sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        if (_sphereMesh == null)
        {
            Debug.LogWarning("[AnchorVisuals] No se pudo obtener la malla built-in 'Sphere.fbx'.");
            return null;
        }

        var go = new GameObject("VisualSphere");
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = Vector3.one * scale;

        go.AddComponent<MeshFilter>().sharedMesh = _sphereMesh;
        var mr = go.AddComponent<MeshRenderer>();

        // Custom/LitMarker (lit, committeado y en Always Included Shaders): la esfera
        // queda iluminada (no plana) y NO sale magenta en el celular. Antes usaba
        // URP/Lit→Standard: el proyecto es Built-in y 'Standard' no esta incluido en
        // el build => se stripeaba => magenta. LitMarker es de una sola variante, por
        // eso es seguro en device. Ver [[builtin-pipeline-shader-stripping]].
        var shader = Shader.Find("Custom/LitMarker") ?? Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        if (mat.HasProperty("_Color"))     mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mr.material = mat;
        return go;
    }
}
