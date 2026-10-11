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
    [Tooltip("Emergencia por puerta.")]
    public AnimationClip emergeClip;
    [Tooltip("Emergencia por ventana.")]
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

    [Header("Desplazamiento sincronizado")]
    [Tooltip("Apoyos contenidos en el loop de marcha herida.")]
    [Min(1)] [SerializeField] private int _chaseStepsPerLoop = 12;
    [Tooltip("Apoyos contenidos en el loop de marcha cubierta.")]
    [Min(1)] [SerializeField] private int _coverStepsPerLoop = 5;
    [Tooltip("Velocidad relativa conservada justo al plantar un pie.")]
    [Range(0f, 1f)] [SerializeField] private float _plantedSpeedRatio = 0.2f;

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

    public float WindowEntryDuration => emergeWindowClip != null && emergeWindowClip.length > 0.01f
        ? emergeWindowClip.length
        : Mathf.Max(0.2f, _windowEntryFallbackDuration);

    public float WindowLandingDuration => _activeWindowLandingDuration > 0f
        ? _activeWindowLandingDuration
        : Mathf.Max(0.2f, _windowLandingDuration);

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

    public void SynchronizeState(SorkenState state)
    {
        int target = (int)state;
        if (target < 0 || target >= _inputCount) target = 0;
        _entryToChaseBlendActive = state == SorkenState.Chasing &&
            (_lastState == (int)SorkenState.WindowLanding ||
             _lastState == (int)SorkenState.EmergingDoor);
        _lastState = target;

        if (_clipPlayables == null || target >= _clipPlayables.Length ||
            !_clipPlayables[target].IsValid())
            return;

        _clipPlayables[target].SetTime(0d);
        _clipPlayables[target].SetDone(false);
        _clipPlayables[target].SetPlayState(PlayState.Playing);
        if (state == SorkenState.WindowLanding)
        {
            _windowLandingElapsed = 0f;
            AnimationClip clip = windowLandingClip != null ? windowLandingClip : idleClip;
            if (clip != null && clip.length > 0.001f)
                _clipPlayables[target].SetSpeed(clip.length / WindowLandingDuration);
        }
        else
        {
            _clipPlayables[target].SetSpeed(1d);
        }
    }

    private void Awake()
    {
        _sorken = GetComponent<SorkenEntity>();
        var animator = GetComponent<Animator>();
        _hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        _hips ??= FindDescendant(transform, "Hips");
        _activeWindowLandingDuration = Mathf.Max(0.2f, _windowLandingDuration);
        animator.applyRootMotion = false; // el codigo controla pos/rot, no la animacion

        // Un input del mixer por estado (mismo orden que el enum SorkenState).
        var clips = new[]
        {
            idleClip,                                                   // Idle
            emergeClip != null ? emergeClip : idleClip,                 // EmergingDoor
            chaseClip  != null ? chaseClip  : idleClip,                 // Chasing
            grabClip   != null ? grabClip   : idleClip,                 // Grabbing
            retreatClip != null ? retreatClip : (chaseClip ?? idleClip),// Retreating
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
            SynchronizeState((SorkenState)target);

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

        if (_sorken.State == SorkenState.WindowLanding)
            _windowLandingElapsed += Time.deltaTime;
    }

    // La traslacion completa desde la ventana la controla GameDirector. Esta capa agrega
    // la reaccion corporal que hace legible el aterrizaje: impacto, flexion y recuperacion.
    // Se aplica despues del Animator, por lo que funciona tanto con un clip dedicado como
    // con el fallback a Idle y no modifica el root que replica la red.
    private void LateUpdate()
    {
        if (_hips == null || _sorken == null || _sorken.State != SorkenState.WindowLanding)
            return;

        float t = Mathf.Clamp01(_windowLandingElapsed / WindowLandingDuration);
        float impact = Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(0.56f, 0.86f, t)) * Mathf.PI);
        _hips.localPosition += Vector3.down * (_landingCompression * impact);
        _hips.localRotation *= Quaternion.Euler(_landingPitch * impact, 0f, -2.5f * impact);
    }

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
