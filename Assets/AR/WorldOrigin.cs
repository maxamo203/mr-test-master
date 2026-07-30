using UnityEngine;

// Representa el origen del mundo compartido entre todos los jugadores.
// Todos los objetos de red usan este transform para convertir entre
// "posicion relativa al anchor" (viaja por red) y "posicion en el espacio AR local".
public class WorldOrigin : MonoBehaviour
{
    public static WorldOrigin Instance { get; private set; }

    public bool IsReady { get; private set; }

    // Anchor del que colgamos ahora mismo (el de la imagen o uno de los anchor
    // points extra). Null si algo nos desparentó (ver ARImageAnchor.RestartTracking).
    public Transform CurrentAnchor => transform.parent;

    private Transform _anchorTransform;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // keepVisualPosition controla qué pasa con lo ya escaneado al recalibrar:
    //   false (default) → WorldOrigin se pega al anchor; los hijos conservan su
    //                     localPosition, así que la escena se MUEVE junto con el
    //                     anchor (se mantienen las coordenadas relativas).
    //   true            → WorldOrigin conserva su pose en el mundo; lo escaneado
    //                     NO se mueve visualmente y solo cambia su posición
    //                     relativa al nuevo anchor.
    public void SetOrigin(Transform anchorTransform, bool keepVisualPosition = false)
    {
        if (anchorTransform == null)
        {
            Debug.LogWarning("[WorldOrigin] SetOrigin recibió un anchor null; se ignora la llamada.");
            return;
        }

        // La primera calibración siempre fija el origen sobre el anchor: todavía
        // no hay nada escaneado, así que "conservar posición" no aplica.
        bool keep = keepVisualPosition && IsReady;

        if (!IsReady)
            Debug.Log($"[WorldOrigin] Calibrado en {anchorTransform.position}");

        if (keep)
        {
            // Recalibrar SOLO el anchor: WorldOrigin (y todo lo que cuelga de él)
            // conserva su pose en el mundo. Igual queda parentado al nuevo anchor
            // para seguir las correcciones de pose del SLAM, pero sin saltar.
            transform.SetParent(anchorTransform, worldPositionStays: true);
        }
        else
        {
            // Parentamos al anchor poniéndonos encima de él: cuando ARCore/ARKit
            // corrija la pose del ARAnchor (loop closure del SLAM), WorldOrigin lo
            // sigue solo. Los hijos conservan su localPosition => se mueven con él.
            transform.SetParent(anchorTransform, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        IsReady = true;
    }

    // Reparenta a un anchor con una pose local EXPLÍCITA: la que quedó guardada
    // cuando se colocó ese anchor point (ver AnchorPointManager). SetOrigin no
    // sirve para esto: su rama normal fuerza localPosition/localRotation a cero y
    // la de keepVisualPosition deriva la local de la pose actual del mundo.
    //
    // Igual que SetOrigin, quedamos parentados: las correcciones de pose que la
    // plataforma le haga al ARAnchor siguen propagando solas a toda la escena.
    public void SetOriginLocal(Transform anchorTransform, Vector3 localPos, Quaternion localRot)
    {
        if (anchorTransform == null)
        {
            Debug.LogWarning("[WorldOrigin] SetOriginLocal recibió un anchor null; se ignora la llamada.");
            return;
        }

        transform.SetParent(anchorTransform, worldPositionStays: false);
        transform.localPosition = localPos;
        transform.localRotation = localRot;

        IsReady = true;
    }

    // Convierte posicion mundo → offset en espacio del anchor
    public Vector3 ToRelative(Vector3 worldPos)
    {
        if (!IsReady) return worldPos;
        return Quaternion.Inverse(transform.rotation) * (worldPos - transform.position);
    }

    // Convierte offset anchor-relativo → posicion mundo
    public Vector3 ToWorld(Vector3 relativePos)
    {
        if (!IsReady) return relativePos;
        return transform.position + transform.rotation * relativePos;
    }

    public Quaternion ToRelativeRot(Quaternion worldRot)  => Quaternion.Inverse(transform.rotation) * worldRot;
    public Quaternion ToWorldRot(Quaternion relativeRot)  => transform.rotation * relativeRot;

    // Direcciones (vectores) anchor-relativas: como ToRelative/ToWorld pero sin la
    // traslacion. Para mandar el "forward" de la camara por red (aim de la linterna).
    public Vector3 ToRelativeDir(Vector3 worldDir) => IsReady ? Quaternion.Inverse(transform.rotation) * worldDir : worldDir;
    public Vector3 ToWorldDir(Vector3 relativeDir) => IsReady ? transform.rotation * relativeDir : relativeDir;
}
