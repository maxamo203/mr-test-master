using UnityEngine;

// Estados del Sorken. Se replican (SorkenNetwork) y manejan la animacion (SorkenAnimator)
// en todos los peers. El GameDirector (server) los fija.
public enum SorkenState : byte
{
    Idle       = 0,
    Emerging   = 1,  // asomando por el marcador (puerta/ventana)
    Chasing    = 2,  // persiguiendo al jugador
    Grabbing   = 3,  // atrapando
    Retreating = 4,  // repelido: se retira
}

// Logica pura del Sorken (sin red). El GameDirector (server) le fija el estado y lo
// mueve; SorkenNetwork replica pos/rot/estado a los clientes; SorkenAnimator reproduce
// el clip del estado en todos los peers.
//
// IMPORTANTE: el Animator (Playables) con clips Generic pisa el transform del root cada
// frame. Por eso guardamos la pose deseada (pos/rot) que fija el codigo y la RE-APLICAMOS
// en LateUpdate (despues de que corre el Animator), asi el control de pos/rotacion es del
// codigo, no de la animacion.
public class SorkenEntity : MonoBehaviour
{
    public Vector3     Position => transform.position;
    public SorkenState State    { get; private set; } = SorkenState.Idle;

    // US-4.1: por que TIPO de punto de entrada esta asomando (indice en el AudioCatalog,
    // 255 = desconocido). Lo fija el GameDirector al spawnear y se replica junto al estado,
    // porque el cliente no tiene forma de saberlo: los marcadores se cargan en modo
    // DisplayOnly y ahi no hay MarkerCatalog que resuelva el tipo.
    public byte MarkerTypeIndex { get; private set; } = 255;

    public void SetMarkerTypeIndex(byte i) => MarkerTypeIndex = i;

    [Tooltip("Velocidad de giro (grados/seg). Mas chico = giro mas suave.")]
    [SerializeField] public float TurnSpeed = 180f;

    [Tooltip("Correccion de rotacion del modelo (grados X/Y/Z) SOLO al emerger por el " +
             "marcador (asomar horizontal por la puerta/ventana). Al perseguir vuelve a " +
             "0,0,0 (parado). Ej: X=-90 para dejarlo horizontal.")]
    [SerializeField] public Vector3 ModelRotationOffset = Vector3.zero;

    [Tooltip("Cuanto se mete el Sorken DENTRO de la pared al emerger (m), a lo largo de la " +
             "normal del marcador. ~mitad del largo del cuerpo para que se vea medio cuerpo; " +
             "la pared (occluder) tapa el resto. Ajustar a ojo por el modelo.")]
    [SerializeField] public float EmergeDepth = 0.5f;

    // El offset del modelo solo aplica mientras emerge; el resto del tiempo va derecho.
    private Quaternion RotOffset =>
        State == SorkenState.Emerging ? Quaternion.Euler(ModelRotationOffset) : Quaternion.identity;

    // Pose deseada (la que fija el codigo). Se re-aplica en LateUpdate.
    private Vector3    _desiredPos;
    private Quaternion _desiredRot;
    private bool       _hasDesired;

    private void Awake()
    {
        _desiredPos = transform.position;
        _desiredRot = transform.rotation;
    }

    public void SetState(SorkenState s) => State = s;

    // Avanza hacia target (horizontal) girando suave. La velocidad la decide el
    // llamador (GameDirector, desde NightConfig.sorkenChaseSpeed).
    public void MoveTo(Vector3 target, float speed, float deltaTime)
    {
        var dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();

        transform.position += dir * speed * deltaTime;
        var rot = Quaternion.LookRotation(dir, Vector3.up) * RotOffset;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, TurnSpeed * deltaTime);
        Capture();
    }

    // Orienta al Sorken hacia una direccion horizontal (para el emerge: mirar segun la
    // normal del marcador, hacia adentro del ambiente).
    public void FaceDirection(Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up) * RotOffset;
        Capture();
    }

    public void SetPositionDirectly(Vector3 p)    { transform.position = p; Capture(); }
    public void SetRotationDirectly(Quaternion r) { transform.rotation = r; Capture(); }

    // Guarda la pose actual como "deseada" (fijada por el codigo, no por la animacion).
    private void Capture()
    {
        _desiredPos = transform.position;
        _desiredRot = transform.rotation;
        _hasDesired = true;
    }

    // El Animator corre entre Update y LateUpdate; aca restauramos la pose del codigo.
    private void LateUpdate()
    {
        if (!_hasDesired) return;
        transform.position = _desiredPos;
        transform.rotation = _desiredRot;
    }
}
