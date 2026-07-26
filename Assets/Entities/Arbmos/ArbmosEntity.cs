using UnityEngine;

// Estados del Arbmos (la alucinacion de cordura). Se replican al UNICO jugador dueño
// (ArbmosNetwork, spawn dirigido) y manejan la animacion (ArbmosAnimator). El
// ArbmosDirector (server) los fija por jugador.
//
// Solo tres clips, como pidio el diseño: idle / running / chase (ver ArbmosAnimator).
public enum ArbmosState : byte
{
    Idle    = 0,  // presente, mirando fijo al jugador (quieto)
    Running = 1,  // se reposiciona / deriva hacia el jugador mientras drena cordura
    Chasing = 2,  // secuencia letal: embiste al jugador (cordura en cero)
}

// Logica pura del Arbmos (sin red). El ArbmosDirector (server) le fija estado/pose y lo
// mueve; ArbmosNetwork lo replica al jugador dueño; ArbmosAnimator reproduce el clip.
//
// A diferencia del Sorken, el Arbmos es una alucinacion INDIVIDUAL: cada jugador ve su
// propia copia y solo a el le afecta (spawn dirigido, no broadcast). Ademas lleva unos
// flags de presentacion — aura de humo, "letal" y una intensidad de distorsion de
// camara (0..1) — que fija el server y lee el ArbmosDistortionHUD / ArbmosSmokeAura.
//
// IMPORTANTE (igual que el Sorken): el Animator (Playables) pisa el transform del root
// cada frame. Guardamos la pose deseada que fija el codigo y la RE-APLICAMOS en
// LateUpdate, asi el control de pos/rotacion es del codigo, no de la animacion.
public class ArbmosEntity : MonoBehaviour
{
    // La copia que se DIBUJA en este dispositivo (host: la suya propia; cliente: la que
    // le llego dirigida). Null si no hay ninguna visible. Lo lee el ArbmosDistortionHUD.
    public static ArbmosEntity Active { get; private set; }

    public Vector3     Position => transform.position;
    public ArbmosState State    { get; private set; } = ArbmosState.Idle;

    // Presentacion (server-authoritative, replicada al dueño).
    public bool  AuraOn   { get; private set; } = true;   // aura de humo negro
    public bool  Lethal   { get; private set; }           // secuencia de cordura 0
    public float Distort01 { get; private set; }          // intensidad de distorsion de lente

    // false en las copias que el server simula para OTROS jugadores (se ocultan en el
    // host: no las tiene que ver). El aura/luz consultan esto para no emitir.
    public bool Rendered { get; set; } = true;

    [Tooltip("Velocidad de giro (grados/seg). Mas chico = giro mas suave.")]
    [SerializeField] public float TurnSpeed = 180f;

    [Tooltip("Correccion CONSTANTE de rotacion del modelo (grados X/Y/Z) para dejarlo " +
             "parado y mirando al frente. Depende del rig; 0,0,0 si ya viene derecho.")]
    [SerializeField] public Vector3 ModelRotationOffset = Vector3.zero;

    [Header("Distorsion de camara (look — la dibuja ArbmosDistortionHUD)")]
    [Tooltip("Configuracion visual de la distorsion de lente en la zona del Arbmos.")]
    public ArbmosDistortionSettings distortion = new ArbmosDistortionSettings();

    private Quaternion RotOffset => Quaternion.Euler(ModelRotationOffset);

    // Pose deseada (la que fija el codigo). Se re-aplica en LateUpdate.
    private Vector3    _desiredPos;
    private Quaternion _desiredRot;
    private bool       _hasDesired;

    private void Awake()
    {
        _desiredPos = transform.position;
        _desiredRot = transform.rotation;
    }

    // Active se fija SOLO via RefreshActive (tras fijarse Rendered en el spawn), no en
    // OnEnable: en el host conviven varias copias (la propia + las ocultas de otros
    // jugadores) y no queremos que una oculta pise a la que se dibuja.
    private void OnDisable() { if (Active == this) Active = null; }
    private void OnDestroy() { if (Active == this) Active = null; }

    public void SetState(ArbmosState s) => State = s;
    public void SetAura(bool on)         => AuraOn = on;
    public void SetLethal(bool lethal)   => Lethal = lethal;
    public void SetDistort(float d01)    => Distort01 = Mathf.Clamp01(d01);

    // Se llama tras fijar Rendered en el spawn: registra (o no) esta copia como la
    // visible localmente, para el HUD de distorsion.
    public void RefreshActive()
    {
        if (Rendered) Active = this;
        else if (Active == this) Active = null;
    }

    // Avanza hacia target (horizontal) girando suave. La velocidad la decide el llamador.
    public void MoveTo(Vector3 target, float speed, float deltaTime)
    {
        var dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) { Capture(); return; }
        dir.Normalize();

        transform.position += dir * speed * deltaTime;
        var rot = Quaternion.LookRotation(dir, Vector3.up) * RotOffset;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, TurnSpeed * deltaTime);
        Capture();
    }

    // Orienta al Arbmos hacia una direccion horizontal (mirar al jugador).
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

// Parametros del look de la distorsion de camara del Arbmos (se editan por prefab en el
// ArbmosEntity; los lee el ArbmosDistortionHUD). Nada de esto afecta al gameplay: es solo
// el aspecto del efecto de lente localizado + el jolt letal.
[System.Serializable]
public class ArbmosDistortionSettings
{
    [Tooltip("Prende/apaga toda la distorsion de camara del Arbmos.")]
    public bool enabled = true;

    [Header("Intensidad")]
    [Tooltip("Distorsion base cuando el Arbmos esta presente (sin fase letal).")]
    [Range(0f, 1f)] public float baseIntensity = 0.35f;
    [Tooltip("Cuanto suma la intensidad que fija el server (Distort01: escala en la fase letal).")]
    [Range(0f, 1.5f)] public float intensityScale = 0.65f;

    [Header("Look de la zona")]
    [Tooltip("Tamaño de la zona de pantalla afectada, alrededor del Arbmos (1 = ajustada al cuerpo).")]
    [Range(0.5f, 3f)] public float regionScale = 1f;
    [Tooltip("Color del tinte de la zona.")]
    public Color tintColor = Color.black;
    [Tooltip("Opacidad maxima del tinte.")]
    [Range(0f, 1f)] public float tintStrength = 0.30f;
    [Tooltip("Aberracion cromatica maxima (corrimiento rojo/cian en pixeles).")]
    [Range(0f, 40f)] public float chromatic = 14f;
    [Tooltip("Cantidad maxima de bandas/scanlines.")]
    [Range(0, 30)] public int scanlines = 14;

    [Header("Jolt letal (cordura 0)")]
    [Tooltip("Fuerza del sacudon full-screen al atacar (0 = sin jolt).")]
    [Range(0f, 2f)] public float lethalBurst = 1f;
    [Tooltip("Color del sacudon letal.")]
    public Color lethalColor = new Color(0.05f, 0f, 0f, 1f);
}
