#if UNITY_EDITOR
using UnityEngine;

// Pose fija de una criatura para los renders promocionales (banner del stand).
// Muestrea un clip real del juego en un instante dado sobre el modelo hijo, en EDIT MODE:
// se elige la pose moviendo un slider en el Inspector y se ve al instante, sin Play.
//
// Estructura que arma Assets/Editor/BannerEntidadesSetup.cs:
//   "Sorken (pose)"  ← este componente; mover / rotar ESTE objeto para encuadrar
//     └─ modelo      ← instancia del FBX animado (raíz de las curvas del clip)
//
// Aplicar() SÓLO muestrea el clip: nunca mueve nada. Apoyar en el piso y centrar el
// pivote son acciones explícitas (botones del Inspector, BannerPoseEditor). Antes el
// apoyo corría en cada Aplicar (slider, render) y medía el punto más bajo con BakeMesh,
// que en edit mode no seguía bien al modelo: cada pasada lo volvía a bajar y se acumulaba
// (Sorken llegó a quedar 20 m por debajo de su pivote).
//
// TIER: editor-only. El archivo entero está dentro de #if UNITY_EDITOR (mismo criterio
// que EditorPanorama360): no se compila en ningún build.
[ExecuteAlways]
public class BannerPose : MonoBehaviour
{
    [Tooltip("Raíz del modelo animado (el objeto sobre el que se muestrea el clip).")]
    public Transform modelo;

    [Tooltip("Clip del juego del que se toma la pose.")]
    public AnimationClip clip;

    [Tooltip("Instante del clip, normalizado (0 = principio, 1 = final).")]
    [Range(0f, 1f)] public float tiempo = 0.5f;

    [Tooltip("Altura (mundo) del piso para el botón Apoyar en el piso.")]
    public float alturaPiso;

    private bool _pendiente;

    private void OnEnable() => Programar();
    private void OnValidate() => Programar();

    // SampleAnimation dentro de OnValidate dispara warnings ("SendMessage cannot be
    // called during Awake, CheckConsistency, or OnValidate"); se difiere un tick.
    private void Programar()
    {
        if (_pendiente) return;
        _pendiente = true;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            _pendiente = false;
            if (this != null) Aplicar();
        };
    }

    [ContextMenu("Aplicar pose")]
    public void Aplicar()
    {
        if (modelo == null || clip == null) return;
        clip.SampleAnimation(modelo.gameObject, tiempo * clip.length);
        UnityEditor.SceneView.RepaintAll();
    }

    // Sube / baja el pivote (este objeto) para que el punto más bajo de la malla en la
    // pose actual toque alturaPiso. Mide en vivo, así que repetirlo no mueve nada.
    public void ApoyarEnPiso()
    {
        Aplicar();
        if (!TryLimites(out Bounds b)) return;
        UnityEditor.Undo.RecordObject(transform, "Apoyar en el piso");
        transform.position += Vector3.up * (alturaPiso - b.min.y);
    }

    // Lleva el pivote al cuerpo (centro en planta, a la altura de los pies) sin mover el
    // modelo en el mundo: la imagen no cambia, pero mover / rotar vuelve a girar sobre la
    // criatura y no sobre un punto lejano.
    public void CentrarPivote()
    {
        Aplicar();
        if (modelo == null || !TryLimites(out Bounds b)) return;
        UnityEditor.Undo.RecordObject(transform, "Centrar pivote");
        UnityEditor.Undo.RecordObject(modelo, "Centrar pivote");
        Vector3 posModelo = modelo.position;
        Quaternion rotModelo = modelo.rotation;
        transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
        modelo.SetPositionAndRotation(posModelo, rotModelo);
    }

    // Límites de la malla deformada calculados a mano (skinning por CPU con las matrices
    // de los huesos tal como están ahora), sin depender de BakeMesh ni de los bounds del
    // renderer, que en edit mode pueden venir de un frame anterior.
    public bool TryLimites(out Bounds b)
    {
        b = default;
        bool hay = false;
        if (modelo == null) return false;
        foreach (var smr in modelo.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null) continue;
            Vector3[] v = mesh.vertices;
            BoneWeight[] w = mesh.boneWeights;
            Matrix4x4[] bind = mesh.bindposes;
            Transform[] huesos = smr.bones;

            var m = new Matrix4x4[huesos.Length];
            for (int i = 0; i < huesos.Length; i++)
                m[i] = (huesos[i] != null ? huesos[i].localToWorldMatrix : smr.transform.localToWorldMatrix) *
                       (i < bind.Length ? bind[i] : Matrix4x4.identity);
            bool skin = m.Length > 0 && w.Length == v.Length;
            Matrix4x4 rigido = smr.transform.localToWorldMatrix;

            for (int k = 0; k < v.Length; k++)
            {
                Vector3 p;
                if (skin)
                {
                    BoneWeight bw = w[k];
                    p = m[bw.boneIndex0].MultiplyPoint3x4(v[k]) * bw.weight0 +
                        m[bw.boneIndex1].MultiplyPoint3x4(v[k]) * bw.weight1 +
                        m[bw.boneIndex2].MultiplyPoint3x4(v[k]) * bw.weight2 +
                        m[bw.boneIndex3].MultiplyPoint3x4(v[k]) * bw.weight3;
                }
                else p = rigido.MultiplyPoint3x4(v[k]);

                if (!hay) { b = new Bounds(p, Vector3.zero); hay = true; }
                else b.Encapsulate(p);
            }
        }
        return hay;
    }
}
#endif
