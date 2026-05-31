using UnityEngine;

// Representa el origen del mundo compartido entre todos los jugadores.
// Todos los objetos de red usan este transform para convertir entre
// "posicion relativa al anchor" (viaja por red) y "posicion en el espacio AR local".
public class WorldOrigin : MonoBehaviour
{
    public static WorldOrigin Instance { get; private set; }

    public bool IsReady { get; private set; }

    private Transform _anchorTransform;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetOrigin(Transform anchorTransform)
    {
        if (!IsReady)
            Debug.Log($"[WorldOrigin] Calibrado en {anchorTransform.position}");

        // Parentamos al anchor para que, cuando ARCore/ARKit corrija la pose
        // del ARAnchor (por loop closure del SLAM), WorldOrigin lo siga
        // automáticamente sin que tengamos que actualizar nada por frame.
        transform.SetParent(anchorTransform, worldPositionStays: false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
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
}
