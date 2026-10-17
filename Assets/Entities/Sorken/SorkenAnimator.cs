using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Reproduce las animaciones del Sorken con la Playables API: arrastras los AnimationClip
// (sin Animator Controller ni transiciones cableadas) y el codigo hace el cross-fade
// segun SorkenEntity.State. Corre en TODOS los peers (el estado viene replicado), asi
// todos ven la misma animacion.
//
// Requiere un Animator en el mismo GameObject (con el Avatar del rig del Sorken). Los
// clips pueden ser Generic o Humanoid mientras coincidan con ese rig.
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(SorkenEntity))]
public class SorkenAnimator : MonoBehaviour
{
    [Header("Clips por estado (arrastrar). Si falta uno, cae a Idle.")]
    public AnimationClip idleClip;
    [Tooltip("Entrada por puerta. Debe apuntar al asset semantico DoorEntry.")]
    public AnimationClip emergeClip;
    [Tooltip("Entrada por ventana. Debe apuntar al asset semantico WindowEntry.")]
    public AnimationClip emergeWindowClip;
    [Tooltip("Pose base del aterrizaje. Si falta, usa Idle y agrega la compresion procedural.")]
    public AnimationClip windowLandingClip;
    public AnimationClip chaseClip;
    public AnimationClip grabClip;
    [Tooltip("Animacion al ser repelido (retroceder / desaparecer). Si falta, usa chase.")]
    public AnimationClip retreatClip;
    public AnimationClip coverStartClip;
    public AnimationClip coverWalkClip;

    [Tooltip("Velocidad del cross-fade entre clips (unidades de peso por segundo).")]
    [SerializeField] private float _blendSpeed = 6f;
    [Tooltip("Duracion del fundido entre la entrada y la primera zancada.")]
    [Min(0.1f)] [SerializeField] private float _entryToChaseBlendSeconds = 0.75f;
    [Tooltip("Velocidad de reproduccion de la marcha de persecucion. La traslacion " +
             "sigue determinada por NightConfig.")]
    [Range(0.1f, 1f)] [SerializeField] private float _chasePlaybackSpeed = 0.75f;

    [Header("Retirada dolorosa")]
    [Tooltip("Duracion aproximada del gesto de dolor antes de ocultarse.")]
    [Range(2.3f, 3f)] [SerializeField] private float _retreatPainDuration = 2.6f;
    [Tooltip("Tiempo de entrada suave a la pose dolorosa.")]
    [Range(0.05f, 0.4f)] [SerializeField] private float _retreatPainBlendIn = 0.18f;

    [Header("Desplazamiento sincronizado")]
    [Tooltip("Apoyos contenidos en el loop de marcha herida.")]
    [Min(1)] [SerializeField] private int _chaseStepsPerLoop = 12;
    [Tooltip("Apoyos contenidos en el loop de marcha cubierta.")]
    [Min(1)] [SerializeField] private int _coverStepsPerLoop = 5;
    [Tooltip("Velocidad relativa conservada justo al plantar un pie.")]
    [Range(0f, 1f)] [SerializeField] private float _plantedSpeedRatio = 0.2f;

    [Header("Contactos de pisada (fase normalizada)")]
    [Tooltip("Apoyos visibles del loop de persecucion, calibrados sobre el clip actual.")]
    [SerializeField] private float[] _chaseFootContacts =
    {
        0.035f, 0.116f, 0.201f, 0.286f, 0.369f, 0.451f,
        0.535f, 0.619f, 0.704f, 0.789f, 0.873f, 0.956f
    };
    [Tooltip("Apoyos visibles del loop lento bajo cobertura.")]
    [SerializeField] private float[] _coverFootContacts =
    {
        0.08f, 0.27f, 0.48f, 0.69f, 0.91f
    };
    [Tooltip("Apoyos visibles mientras termina de atravesar una puerta.")]
    [SerializeField] private float[] _doorEntryFootContacts =
    {
        0.59f, 0.73f, 0.86f, 0.97f
    };
    [Header("Transicion ventana -> persecucion")]
    [Tooltip("Duracion usada si el clip nuevo de entrada todavia no esta asignado.")]
    [Min(0.2f)] [SerializeField] private float _windowEntryFallbackDuration = 5.93f;
    [Tooltip("Escala del ajuste de root que mantiene los pies apoyados en el marco.")]
    [Range(0f, 2f)] [SerializeField] private float _windowEntryRootMotionScale = 1f;
    [Tooltip("Duracion total: caida, impacto y recuperacion antes de perseguir.")]
    [Min(0.2f)] [SerializeField] private float _windowLandingDuration = 2.4f;
    [Tooltip("Ajuste adicional por cada metro de desnivel respecto de una ventana de 1 m.")]
    [Min(0f)] [SerializeField] private float _windowLandingSecondsPerMeter = 0.75f;
    [Tooltip("Cuanto baja la cadera al absorber el impacto.")]
    [Range(0f, 0.3f)] [SerializeField] private float _landingCompression = 0.14f;
    [Tooltip("Inclinacion hacia adelante durante el apoyo.")]
    [Range(0f, 25f)] [SerializeField] private float _landingPitch = 10f;

    private SorkenEntity _sorken;
    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private float[] _weights;   // peso actual de cada input (index = (int)SorkenState)
    private int _inputCount;
    private AnimationClipPlayable[] _clipPlayables;
    private int _lastState = -1;
    private Transform _hips;
    private float _windowLandingElapsed;
    private float _activeWindowLandingDuration;
    private bool _entryToChaseBlendActive;
    private bool _landingHipsReferenceCaptured;
    private Vector3 _neutralHipsLocalPosition;
    private Vector3 _landingHipsReferencePosition;
    private bool _postWindowDepthOffsetPending;
    private bool _postWindowDepthOffsetActive;
    private float _postWindowVisibleHipsDepth;
    private float _postWindowHipsDepthOffset;
    private readonly SorkenFootstepClock _footstepClock = new();
    private Transform _leftFoot;
    private Transform _rightFoot;
    private bool _pendingFootstep;
    private Vector3 _pendingFootstepPosition;
    private float _pendingFootstepIntensity;
    private float _retreatElapsed;
    private Transform _spine;
    private Transform _spine01;
    private Transform _spine02;
    private Transform _neck;
    private Transform _head;
    private Transform _leftShoulder;
    private Transform _rightShoulder;
    private Transform _leftArm;
    private Transform _rightArm;
    private Transform _leftForeArm;
    private Transform _rightForeArm;
    private Quaternion _spineBase;
    private Quaternion _spine01Base;
    private Quaternion _spine02Base;
    private Quaternion _neckBase;
    private Quaternion _headBase;
    private Quaternion _leftShoulderBase;
    private Quaternion _rightShoulderBase;
    private Quaternion _leftArmBase;
    private Quaternion _rightArmBase;
    private Quaternion _leftForeArmBase;
    private Quaternion _rightForeArmBase;

    public float WindowEntryDuration => emergeWindowClip != null && emergeWindowClip.length > 0.01f
        ? emergeWindowClip.length
        : Mathf.Max(0.2f, _windowEntryFallbackDuration);

    public float WindowLandingDuration => _activeWindowLandingDuration > 0f
        ? _activeWindowLandingDuration
        : Mathf.Max(0.2f, _windowLandingDuration);

    public float RetreatPainDuration => Mathf.Clamp(_retreatPainDuration, 2.3f, 3f);

    // La accion de Blender usa dos apoyos: primero el pie derecho queda clavado al marco,
    // luego el peso pasa al izquierdo. Estos offsets son la compensacion del root medida
    // en esa accion (frente = +Z de Unity). Evitan que los pies patinen mientras el cuerpo
    // cruza la ventana; GameDirector los aplica sobre la normal real del marcador.
    private static readonly float[] WindowEntryTimes =
    {
        0f, 0.0955f, 0.1966f, 0.2978f, 0.3876f, 0.4438f,
        0.5f, 0.5506f, 0.6124f, 0.6966f, 0.7978f, 0.8989f, 1f
    };

    private static readonly float[] WindowEntryOffsets =
    {
        0f, 0.0044f, 0.0234f, 0.0444f, 0.0642f, -0.0073f,
        -0.1374f, -0.1988f, -0.1854f, -0.1673f, -0.1792f, -0.1871f, -0.1871f
    };

    public float WindowEntryRootOffset(float normalizedTime) =>
        EvaluateWindowEntryRootOffset(normalizedTime) * _windowEntryRootMotionScale;

    public static float EvaluateWindowEntryRootOffset(float normalizedTime)
    {
        float t = Mathf.Clamp01(normalizedTime);
        for (int i = 1; i < WindowEntryTimes.Length; i++)
        {
            if (t > WindowEntryTimes[i]) continue;
            float segment = Mathf.InverseLerp(WindowEntryTimes[i - 1], WindowEntryTimes[i], t);
            segment = Mathf.SmoothStep(0f, 1f, segment);
            return Mathf.Lerp(WindowEntryOffsets[i - 1], WindowEntryOffsets[i], segment);
        }
        return WindowEntryOffsets[WindowEntryOffsets.Length - 1];
    }

    public void ConfigureWindowLanding(float dropHeight)
    {
        _activeWindowLandingDuration = EvaluateWindowLandingDuration(
            _windowLandingDuration, _windowLandingSecondsPerMeter, dropHeight);
    }

    public static float EvaluateWindowLandingDuration(
        float baseDuration, float secondsPerMeter, float dropHeight)
    {
        float duration = Mathf.Max(0.2f, baseDuration) +
                         (Mathf.Max(0f, dropHeight) - 1f) * Mathf.Max(0f, secondsPerMeter);
        return Mathf.Clamp(duration, 2f, 4f);
    }

    public float MovementMultiplier
    {
        get
        {
            int state = _sorken != null ? (int)_sorken.State : -1;
            AnimationClip clip;
            int steps;
            if (state == (int)SorkenState.Chasing)
            {
                clip = chaseClip;
                steps = _chaseStepsPerLoop;
            }
            else if (state == (int)SorkenState.CoverWalking)
            {
                clip = coverWalkClip != null ? coverWalkClip : chaseClip;
                steps = _coverStepsPerLoop;
            }
            else return 1f;

            if (clip == null || clip.length <= 0.001f ||
                _clipPlayables == null || state < 0 || state >= _clipPlayables.Length ||
                !_clipPlayables[state].IsValid())
                return 1f;

            return AnimationMotionSync.Evaluate(
                _clipPlayables[state].GetTime(), clip.length, steps, _plantedSpeedRatio);
        }
    }

    // El watcher consume el contacto una sola vez. Si hubo un salto grande de fase, el
    // reloj conserva solo el apoyo mas reciente para evitar rafagas atrasadas.
    public bool TryConsumeFootstep(out Vector3 position, out float intensity)
    {
        position = _pendingFootstepPosition;
        intensity = _pendingFootstepIntensity;
        bool pending = _pendingFootstep;
        _pendingFootstep = false;
        return pending;
    }

    public void SynchronizeState(SorkenState state)
    {
        int target = (int)state;
        if (target < 0 || target >= _inputCount) target = 0;
        bool chasingFromWindow = state == SorkenState.Chasing &&
                                  _lastState == (int)SorkenState.WindowLanding;
        _entryToChaseBlendActive = state == SorkenState.Chasing &&
            (chasingFromWindow || _lastState == (int)SorkenState.EmergingDoor);
        bool continueGroundedOffset = _postWindowDepthOffsetActive &&
                                      KeepsPostWindowDepthOffset(state);
        if ((chasingFromWindow || continueGroundedOffset) && _hips != null)
        {
            // Esta es la profundidad realmente visible tras la correccion del descenso.
            // El siguiente LateUpdate calculara el offset contra el primer frame ya
            // evaluado de la caminata, sin introducir un fotograma intermedio.
            _postWindowVisibleHipsDepth = _hips.localPosition.y;
            _postWindowDepthOffsetPending = true;
            _postWindowDepthOffsetActive = false;
        }
        else if (state == SorkenState.Idle ||
                 state == SorkenState.EmergingDoor ||
                 state == SorkenState.EmergingWindow)
        {
            _postWindowDepthOffsetPending = false;
            _postWindowDepthOffsetActive = false;
        }
        _lastState = target;
        _footstepClock.Reset(target, 0f);
        _pendingFootstep = false;

        if (_clipPlayables == null || target >= _clipPlayables.Length ||
            !_clipPlayables[target].IsValid())
            return;

        _clipPlayables[target].SetTime(0d);
        _clipPlayables[target].SetDone(false);
        _clipPlayables[target].SetPlayState(PlayState.Playing);
        if (state == SorkenState.WindowLanding)
        {
            _windowLandingElapsed = 0f;
            _landingHipsReferencePosition = _hips != null
                ? ResolveLandingHipsReference(_hips.localPosition, _neutralHipsLocalPosition)
                : _neutralHipsLocalPosition;
            _landingHipsReferenceCaptured = _hips != null;
            AnimationClip clip = windowLandingClip != null ? windowLandingClip : idleClip;
            if (clip != null && clip.length > 0.001f)
                _clipPlayables[target].SetSpeed(clip.length / WindowLandingDuration);
        }
        else if (state == SorkenState.Chasing)
        {
            _clipPlayables[target].SetSpeed(Mathf.Clamp(_chasePlaybackSpeed, 0.1f, 1f));
        }
        else
        {
            _clipPlayables[target].SetSpeed(1d);
        }

        if (state == SorkenState.Retreating)
        {
            _retreatElapsed = 0f;
            _clipPlayables[target].SetTime(0d);
            _clipPlayables[target].SetSpeed(1d);
            CaptureRetreatPose();
        }
    }

    private void Awake()
    {
        _sorken = GetComponent<SorkenEntity>();
        var animator = GetComponent<Animator>();
        _hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        _leftFoot = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
        _rightFoot = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightFoot) : null;
        _hips ??= FindDescendant(transform, "Hips");
        _leftFoot ??= FindDescendant(transform, "LeftFoot");
        _rightFoot ??= FindDescendant(transform, "RightFoot");
        _spine = FindDescendant(transform, "Spine");
        _spine01 = FindDescendant(transform, "Spine01");
        _spine02 = FindDescendant(transform, "Spine02");
        _neck = FindDescendant(transform, "neck");
        _head = FindDescendant(transform, "Head");
        _leftShoulder = FindDescendant(transform, "LeftShoulder");
        _rightShoulder = FindDescendant(transform, "RightShoulder");
        _leftArm = FindDescendant(transform, "LeftArm");
        _rightArm = FindDescendant(transform, "RightArm");
        _leftForeArm = FindDescendant(transform, "LeftForeArm");
        _rightForeArm = FindDescendant(transform, "RightForeArm");
        if (_hips != null) _neutralHipsLocalPosition = _hips.localPosition;
        _activeWindowLandingDuration = Mathf.Max(0.2f, _windowLandingDuration);
        animator.applyRootMotion = false; // el codigo controla pos/rot, no la animacion

        // Un input del mixer por estado (mismo orden que el enum SorkenState).
        var clips = new[]
        {
            idleClip,                                                   // Idle
            emergeClip != null ? emergeClip : idleClip,                 // EmergingDoor
            chaseClip  != null ? chaseClip  : idleClip,                 // Chasing
            grabClip   != null ? grabClip   : idleClip,                 // Grabbing
            idleClip,                                                   // Retreating: pose base + dolor procedural
            coverStartClip != null ? coverStartClip : idleClip,         // CoverStarting
            coverWalkClip  != null ? coverWalkClip  : (chaseClip ?? idleClip), // CoverWalking
            emergeWindowClip != null ? emergeWindowClip :
                (emergeClip != null ? emergeClip : idleClip),           // EmergingWindow
            windowLandingClip != null ? windowLandingClip : idleClip,   // WindowLanding
        };
        _inputCount = clips.Length;
        _weights    = new float[_inputCount];
        _clipPlayables = new AnimationClipPlayable[_inputCount];

        _graph = PlayableGraph.Create("SorkenAnim");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(_graph, "out", animator);
        _mixer = AnimationMixerPlayable.Create(_graph, _inputCount);
        output.SetSourcePlayable(_mixer);

        for (int i = 0; i < _inputCount; i++)
        {
            if (clips[i] != null)
            {
                var cp = AnimationClipPlayable.Create(_graph, clips[i]);
                _graph.Connect(cp, 0, _mixer, i);
                _clipPlayables[i] = cp;
            }
            _mixer.SetInputWeight(i, i == 0 ? 1f : 0f);
        }
        _weights[0] = 1f;
        _graph.Play();
        SynchronizeState(_sorken.State);
    }

    private void Update()
    {
        if (!_graph.IsValid()) return;

        int target = (int)_sorken.State;
        if (target < 0 || target >= _inputCount) target = 0;

        // Los Playables avanzan aunque su peso sea cero. Al entrar a un estado
        // puntual reiniciamos su clip, para no mostrar un fotograma intermedio o final.
        if (target != _lastState)
        {
            if (_lastState == (int)SorkenState.Retreating)
                RestoreRetreatPose();
            SynchronizeState((SorkenState)target);
        }

        AnimationClip locomotionClip = null;
        if (target == (int)SorkenState.Chasing) locomotionClip = chaseClip;
        else if (target == (int)SorkenState.CoverWalking)
            locomotionClip = coverWalkClip != null ? coverWalkClip : chaseClip;
        if (locomotionClip != null && locomotionClip.length > 0.001f &&
            _clipPlayables[target].IsValid())
        {
            double time = _clipPlayables[target].GetTime();
            if (time >= locomotionClip.length)
                _clipPlayables[target].SetTime(time % locomotionClip.length);
        }

        if (target == (int)SorkenState.Retreating)
        {
            UpdateRetreatAnimation(Time.deltaTime);
            return;
        }

        // Rampa de pesos hacia el estado objetivo y normalizacion.
        float blendSpeed = _entryToChaseBlendActive
            ? 1f / Mathf.Max(0.1f, _entryToChaseBlendSeconds)
            : _blendSpeed;
        float step = blendSpeed * Time.deltaTime;
        float sum  = 0f;
        for (int i = 0; i < _inputCount; i++)
        {
            _weights[i] = Mathf.MoveTowards(_weights[i], i == target ? 1f : 0f, step);
            sum += _weights[i];
        }
        if (sum <= 1e-4f) { _weights[target] = 1f; sum = 1f; }
        for (int i = 0; i < _inputCount; i++)
            _mixer.SetInputWeight(i, _weights[i] / sum);

        if (_entryToChaseBlendActive && _weights[target] >= 0.999f)
            _entryToChaseBlendActive = false;

        ActualizarContactos(target, locomotionClip);

        if (_sorken.State == SorkenState.WindowLanding)
            _windowLandingElapsed += Time.deltaTime;
    }

    private void UpdateRetreatAnimation(float deltaTime)
    {
        _retreatElapsed += Mathf.Max(0f, deltaTime);
        float step = _blendSpeed * Mathf.Max(0f, deltaTime);
        float sum = 0f;
        for (int i = 0; i < _inputCount; i++)
        {
            float desired = i == (int)SorkenState.Retreating ? 1f : 0f;
            _weights[i] = Mathf.MoveTowards(_weights[i], desired, step);
            sum += _weights[i];
        }
        if (sum <= 1e-4f)
        {
            _weights[(int)SorkenState.Retreating] = 1f;
            sum = 1f;
        }
        for (int i = 0; i < _inputCount; i++)
            _mixer.SetInputWeight(i, _weights[i] / sum);
    }

    private void CaptureRetreatPose()
    {
        _spineBase = _spine != null ? _spine.localRotation : Quaternion.identity;
        _spine01Base = _spine01 != null ? _spine01.localRotation : Quaternion.identity;
        _spine02Base = _spine02 != null ? _spine02.localRotation : Quaternion.identity;
        _neckBase = _neck != null ? _neck.localRotation : Quaternion.identity;
        _headBase = _head != null ? _head.localRotation : Quaternion.identity;
        _leftShoulderBase = _leftShoulder != null ? _leftShoulder.localRotation : Quaternion.identity;
        _rightShoulderBase = _rightShoulder != null ? _rightShoulder.localRotation : Quaternion.identity;
        _leftArmBase = _leftArm != null ? _leftArm.localRotation : Quaternion.identity;
        _rightArmBase = _rightArm != null ? _rightArm.localRotation : Quaternion.identity;
        _leftForeArmBase = _leftForeArm != null ? _leftForeArm.localRotation : Quaternion.identity;
        _rightForeArmBase = _rightForeArm != null ? _rightForeArm.localRotation : Quaternion.identity;
    }

    private void RestoreRetreatPose()
    {
        if (_spine != null) _spine.localRotation = _spineBase;
        if (_spine01 != null) _spine01.localRotation = _spine01Base;
        if (_spine02 != null) _spine02.localRotation = _spine02Base;
        if (_neck != null) _neck.localRotation = _neckBase;
        if (_head != null) _head.localRotation = _headBase;
        if (_leftShoulder != null) _leftShoulder.localRotation = _leftShoulderBase;
        if (_rightShoulder != null) _rightShoulder.localRotation = _rightShoulderBase;
        if (_leftArm != null) _leftArm.localRotation = _leftArmBase;
        if (_rightArm != null) _rightArm.localRotation = _rightArmBase;
        if (_leftForeArm != null) _leftForeArm.localRotation = _leftForeArmBase;
        if (_rightForeArm != null) _rightForeArm.localRotation = _rightForeArmBase;
    }

    private void ApplyRetreatPainPose()
    {
        float t = _retreatElapsed;
        float blend = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(0f, Mathf.Max(0.05f, _retreatPainBlendIn), t));
        float irregular = SpasmPulse(t, 0.62f, 0.1f) * 0.9f +
                          SpasmPulse(t, 1.34f, 0.13f) * -0.75f +
                          SpasmPulse(t, 2.12f, 0.09f) * 0.82f;
        float shoulderTwitch = irregular * blend;
        float hunch = (14f + 4f * Mathf.Sin(t * 3.4f + 0.3f)) * blend;
        float recoil = (9f + 3f * Mathf.Sin(t * 4.7f + 1.2f)) * blend;
        // Gesto defensivo: las manos se cierran hacia el pecho/cara de forma
        // progresiva, con una asimetria leve para evitar una pose estatica o de T.
        float cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.24f, 0.82f, t));
        float coverPulse = 0.5f + 0.5f * Mathf.Sin(t * 3.1f + 0.7f);
        float leftCover = cover * (0.86f + 0.1f * coverPulse);
        float rightCover = cover * (0.94f - 0.08f * coverPulse);
        if (_spine != null) _spine.localRotation =
            _spineBase * Quaternion.Euler(hunch * 0.35f, 0f, shoulderTwitch * 1.2f);
        if (_spine01 != null) _spine01.localRotation =
            _spine01Base * Quaternion.Euler(hunch * 0.45f, 0f, shoulderTwitch * 1.8f);
        if (_spine02 != null) _spine02.localRotation =
            _spine02Base * Quaternion.Euler(hunch * 0.2f, 0f, -shoulderTwitch * 1.2f);
        if (_neck != null) _neck.localRotation =
            _neckBase * Quaternion.Euler(-recoil * 0.45f, shoulderTwitch * 0.8f, 0f);
        if (_head != null) _head.localRotation =
            _headBase * Quaternion.Euler(-recoil * 0.55f, -shoulderTwitch * 1.1f, 0f);
        if (_leftShoulder != null) _leftShoulder.localRotation =
            _leftShoulderBase * Quaternion.Euler(-shoulderTwitch * 4f, 0f, 8f + shoulderTwitch * 7f);
        if (_rightShoulder != null) _rightShoulder.localRotation =
            _rightShoulderBase * Quaternion.Euler(shoulderTwitch * 4f, 0f, -8f - shoulderTwitch * 7f);
        if (_leftArm != null) _leftArm.localRotation =
            _leftArmBase * Quaternion.Euler(-7f * leftCover, -18f * leftCover,
                3f + shoulderTwitch * 3f + 5f * leftCover);
        if (_rightArm != null) _rightArm.localRotation =
            _rightArmBase * Quaternion.Euler(-8f * rightCover, 20f * rightCover,
                -3f - shoulderTwitch * 3f - 5f * rightCover);
        if (_leftForeArm != null) _leftForeArm.localRotation =
            _leftForeArmBase * Quaternion.Euler(-16f * leftCover, -38f * leftCover,
                5f + irregular * 4f + 8f * leftCover);
        if (_rightForeArm != null) _rightForeArm.localRotation =
            _rightForeArmBase * Quaternion.Euler(-18f * rightCover, 42f * rightCover,
                -5f - irregular * 4f - 8f * rightCover);
    }

    private static float SpasmPulse(float time, float center, float width)
    {
        float normalized = (time - center) / Mathf.Max(0.01f, width);
        return Mathf.Exp(-(normalized * normalized));
    }

    private void ActualizarContactos(int state, AnimationClip locomotionClip)
    {
        if (_clipPlayables == null || state < 0 || state >= _clipPlayables.Length ||
            !_clipPlayables[state].IsValid())
        {
            _footstepClock.Clear();
            return;
        }

        float[] contacts = null;
        AnimationClip clip = locomotionClip;
        bool loops = false;
        float intensity = 1f;

        switch ((SorkenState)state)
        {
            case SorkenState.Chasing:
                contacts = _chaseFootContacts;
                loops = true;
                break;
            case SorkenState.CoverWalking:
                contacts = _coverFootContacts;
                loops = true;
                intensity = 0.72f;
                break;
            case SorkenState.EmergingDoor:
                contacts = _doorEntryFootContacts;
                clip = emergeClip;
                intensity = 0.9f;
                break;
            default:
                _footstepClock.Clear();
                return;
        }

        if (clip == null || clip.length <= 0.001f) return;
        float phase = (float)(_clipPlayables[state].GetTime() / clip.length);
        if (!_footstepClock.Advance(state, phase, loops, contacts, out int index)) return;

        bool left = (index & 1) == 0;
        Transform foot = left ? _leftFoot : _rightFoot;
        _pendingFootstepPosition = foot != null ? foot.position : transform.position;
        _pendingFootstepIntensity = intensity;
        _pendingFootstep = true;
    }

    // La traslacion completa desde la ventana la controla GameDirector. El rig importado
    // esta rotado: su Y local es profundidad mundial, no altura. El clip de aterrizaje
    // comienza desde otra profundidad, por eso anclamos ese eje al ultimo fotograma que
    // el jugador vio en la ventana. X/Z quedan animados para conservar el descenso.
    private void LateUpdate()
    {
        // El PlayableGraph se evalua despues de Update. Aplicar la pose de dolor aca
        // garantiza que torso, cabeza y brazos no sean sobrescritos por el clip base.
        if (_sorken != null && _sorken.State == SorkenState.Retreating)
        {
            ApplyRetreatPainPose();
            return;
        }

        if (_hips == null || _sorken == null || _weights == null)
            return;

        int landingIndex = (int)SorkenState.WindowLanding;
        if (landingIndex >= _weights.Length) return;

        float totalWeight = 0f;
        for (int i = 0; i < _weights.Length; i++) totalWeight += _weights[i];
        float landingWeight = totalWeight > 1e-4f
            ? Mathf.Clamp01(_weights[landingIndex] / totalWeight)
            : 0f;

        bool landingCorrectionActive = _landingHipsReferenceCaptured &&
                                       landingWeight > 0.0001f &&
                                       _sorken.State == SorkenState.WindowLanding;
        if (landingCorrectionActive)
        {
            float t = Mathf.Clamp01(_windowLandingElapsed / WindowLandingDuration);
            float impact = Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(0.56f, 0.86f, t)) * Mathf.PI);
            float parentScaleY = _hips.parent != null ? Mathf.Abs(_hips.parent.lossyScale.y) : 1f;
            float localCompression = _landingCompression / Mathf.Max(0.0001f, parentScaleY);
            _hips.localPosition = CorrectLandingHipsPosition(
                _hips.localPosition, _landingHipsReferencePosition,
                localCompression, impact, landingWeight);
            _hips.localRotation *= Quaternion.Slerp(
                Quaternion.identity,
                Quaternion.Euler(_landingPitch * impact, 0f, -2.5f * impact),
                landingWeight);
        }
        else if (_sorken.State != SorkenState.WindowLanding)
            _landingHipsReferenceCaptured = false;

        if (_postWindowDepthOffsetPending && KeepsPostWindowDepthOffset(_sorken.State))
        {
            _postWindowHipsDepthOffset = ResolvePostWindowDepthOffset(
                _postWindowVisibleHipsDepth, _hips.localPosition.y);
            _postWindowDepthOffsetPending = false;
            _postWindowDepthOffsetActive = true;
        }

        if (_postWindowDepthOffsetActive && KeepsPostWindowDepthOffset(_sorken.State))
            _hips.localPosition = ApplyPostWindowDepthOffset(
                _hips.localPosition, _postWindowHipsDepthOffset);
    }

    public static Vector3 CorrectLandingHipsPosition(
        Vector3 animatedPosition, Vector3 referencePosition,
        float localCompression, float impact, float landingWeight)
    {
        float targetY = referencePosition.y -
                        (Mathf.Max(0f, localCompression) * Mathf.Clamp01(impact));
        Vector3 corrected = animatedPosition;
        corrected.y = Mathf.Lerp(animatedPosition.y, targetY, Mathf.Clamp01(landingWeight));
        return corrected;
    }

    public static Vector3 ResolveLandingHipsReference(
        Vector3 currentAnimatedPosition, Vector3 neutralPosition)
    {
        // Y local corresponde a la profundidad mundial por la rotacion del Armature.
        // Mantener el valor actualmente visible evita el salto hacia atras al cambiar
        // de clip; los otros ejes conservan la referencia estable del rig.
        neutralPosition.y = currentAnimatedPosition.y;
        return neutralPosition;
    }

    public static float ResolvePostWindowDepthOffset(
        float visibleDepth, float firstChaseAnimatedDepth) =>
        visibleDepth - firstChaseAnimatedDepth;

    public static Vector3 ApplyPostWindowDepthOffset(Vector3 animatedPosition, float depthOffset)
    {
        animatedPosition.y += depthOffset;
        return animatedPosition;
    }

    private static bool KeepsPostWindowDepthOffset(SorkenState state) =>
        state == SorkenState.Chasing ||
        state == SorkenState.CoverStarting ||
        state == SorkenState.CoverWalking ||
        state == SorkenState.Grabbing;

    private static Transform FindDescendant(Transform root, string wantedName)
    {
        foreach (Transform child in root)
        {
            if (child.name == wantedName) return child;
            Transform found = FindDescendant(child, wantedName);
            if (found != null) return found;
        }
        return null;
    }

    private void OnDestroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }
}
