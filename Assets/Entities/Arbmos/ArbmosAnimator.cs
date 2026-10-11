using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Reproduce las animaciones del Arbmos con la Playables API (igual criterio que el
// SorkenAnimator): arrastras los AnimationClip en el inspector — SIN Animator
// Controller ni transiciones cableadas — y el codigo hace el cross-fade segun
// ArbmosEntity.State. Corre en el UNICO peer que dibuja esta copia (el estado viene
// del server).
//
// Usa tres variantes idle de aparición y un único clip de persecución. Attacking y
// Chasing comparten ese clip; Running conserva la variante idle por compatibilidad.
// Requiere un Animator en el mismo GameObject (con el Avatar del rig del Arbmos).
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(ArbmosEntity))]
public class ArbmosAnimator : MonoBehaviour
{
    [Header("Variantes de aparición")]
    [Tooltip("Se elige una variante válida al azar cada vez que aparece una instancia.")]
    public AnimationClip[] idleVariants = new AnimationClip[3];

    [Header("Persecución letal")]
    public AnimationClip chaseClip;

    [Header("Desplazamiento sincronizado")]
    [Tooltip("Cantidad de pasos completos contenidos en el ciclo de persecución.")]
    [Min(1)] [SerializeField] private int _stepsPerLoop = 6;

    [Tooltip("Distancia real que avanza por cada paso del clip. Define la velocidad " +
             "media sin desacoplarla del ritmo de la animación.")]
    [Min(0.05f)] [SerializeField] private float _metersPerStep = 1.5f;

    [Tooltip("Multiplicador del ritmo de la animación de persecución. Se mantiene en 1 para " +
             "conservar los pasos lentos y pesados; la velocidad se obtiene de su longitud.")]
    [Min(0.1f)] [SerializeField] private float _chasePlaybackSpeed = 1f;

    [Tooltip("Fracción de la velocidad media que conserva al plantar cada pie. " +
             "El resto del avance ocurre durante la transferencia de peso.")]
    [Range(0f, 1f)] [SerializeField] private float _plantedSpeedRatio = 0.15f;

    public int SelectedIdleIndex { get; private set; } = -1;

    [Tooltip("Velocidad del cross-fade entre clips (unidades de peso por segundo).")]
    [SerializeField] private float _blendSpeed = 6f;

    private ArbmosEntity _arbmos;
    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private AnimationClipPlayable[] _clipPlayables;
    private float[] _weights;   // peso actual de cada input (index = (int)ArbmosState)
    private int _inputCount;
    private int _lastTarget = -1;

    // Velocidad media que corresponde físicamente a los seis pasos del clip. Evita que
    // una configuración antigua de varios m/s haga recorrer metros durante un solo paso.
    public float ChaseAverageSpeed => EvaluateAverageChaseSpeed(
        chaseClip != null ? chaseClip.length : 0f, _stepsPerLoop, _metersPerStep,
        _chasePlaybackSpeed);

    // Multiplicador instantáneo del desplazamiento. Su promedio durante el loop es 1;
    // la velocidad media anterior aporta la escala y esta curva aporta los apoyos.
    public float ChaseMovementMultiplier
    {
        get
        {
            int state = _arbmos != null ? (int)_arbmos.State : -1;
            if (state != (int)ArbmosState.Chasing && state != (int)ArbmosState.Attacking)
                return 1f;
            if (chaseClip == null || chaseClip.length <= 0.001f ||
                _clipPlayables == null || state < 0 || state >= _clipPlayables.Length ||
                !_clipPlayables[state].IsValid())
                return 1f;

            return EvaluateStepSpeed(
                _clipPlayables[state].GetTime(), chaseClip.length,
                _stepsPerLoop, _plantedSpeedRatio);
        }
    }

    public static float EvaluateStepSpeed(double playbackSeconds, float clipLength,
                                          int stepsPerLoop, float plantedSpeedRatio)
    {
        return AnimationMotionSync.Evaluate(
            playbackSeconds, clipLength, stepsPerLoop, plantedSpeedRatio);
    }

    public static float EvaluateAverageChaseSpeed(float clipLength, int stepsPerLoop,
                                                   float metersPerStep,
                                                   float playbackSpeed = 1f)
    {
        if (clipLength <= 0.001f || stepsPerLoop <= 0 || metersPerStep <= 0f ||
            playbackSpeed <= 0f)
            return 0f;
        return metersPerStep * stepsPerLoop / clipLength * playbackSpeed;
    }

    // ArbmosEntity lo llama en el mismo instante en que cambia el estado. Evita que el
    // primer frame de movimiento use la fase arbitraria de un playable que estaba a peso 0.
    public void SynchronizeState(ArbmosState state)
    {
        int target = (int)state;
        if (target < 0 || target >= _inputCount) target = 0;
        _lastTarget = target;

        if (_clipPlayables == null || target >= _clipPlayables.Length ||
            !_clipPlayables[target].IsValid())
            return;

        _clipPlayables[target].SetTime(0d);
        _clipPlayables[target].SetDone(false);
        _clipPlayables[target].SetPlayState(PlayState.Playing);
    }

    private void Awake()
    {
        _arbmos = GetComponent<ArbmosEntity>();
        var animator = GetComponent<Animator>();
        animator.applyRootMotion = false;

        var validIdles = new System.Collections.Generic.List<int>();
        if (idleVariants != null)
        {
            for (int i = 0; i < idleVariants.Length; i++)
                if (idleVariants[i] != null) validIdles.Add(i);
        }

        AnimationClip selectedIdle = null;
        if (validIdles.Count > 0)
        {
            SelectedIdleIndex = validIdles[Random.Range(0, validIdles.Count)];
            selectedIdle = idleVariants[SelectedIdleIndex];
        }

        // El ataque normal (Attacking) y el letal (Chasing) usan el ciclo de persecución.
        var clips = new[]
        {
            selectedIdle,
            selectedIdle,
            chaseClip != null ? chaseClip : selectedIdle,
            chaseClip != null ? chaseClip : selectedIdle,
        };
        _inputCount = clips.Length;
        _weights = new float[_inputCount];
        _clipPlayables = new AnimationClipPlayable[_inputCount];

        _graph = PlayableGraph.Create("ArbmosAnim");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(_graph, "out", animator);
        _mixer = AnimationMixerPlayable.Create(_graph, _inputCount);
        output.SetSourcePlayable(_mixer);

        for (int i = 0; i < _inputCount; i++)
        {
            if (clips[i] != null)
            {
                var cp = AnimationClipPlayable.Create(_graph, clips[i]);
                cp.SetApplyFootIK(false);
                if (chaseClip != null && clips[i] == chaseClip)
                    cp.SetSpeed(Mathf.Max(0.1f, _chasePlaybackSpeed));
                _clipPlayables[i] = cp;
                _graph.Connect(cp, 0, _mixer, i);
            }
            _mixer.SetInputWeight(i, i == 0 ? 1f : 0f);
        }
        _weights[0] = 1f;
        _graph.Play();
        SynchronizeState(_arbmos.State);
    }

    private void Update()
    {
        if (!_graph.IsValid()) return;

        int target = (int)_arbmos.State;
        if (target < 0 || target >= _inputCount) target = 0;

        if (target != _lastTarget)
            SynchronizeState((ArbmosState)target);

        if ((target == (int)ArbmosState.Chasing ||
             target == (int)ArbmosState.Attacking) &&
            chaseClip != null && chaseClip.length > 0.001f &&
            _clipPlayables[target].IsValid())
        {
            double time = _clipPlayables[target].GetTime();
            if (time >= chaseClip.length)
                _clipPlayables[target].SetTime(time % chaseClip.length);
        }

        // Rampa de pesos hacia el estado objetivo y normalizacion.
        float step = _blendSpeed * Time.deltaTime;
        float sum  = 0f;
        for (int i = 0; i < _inputCount; i++)
        {
            _weights[i] = Mathf.MoveTowards(_weights[i], i == target ? 1f : 0f, step);
            sum += _weights[i];
        }
        if (sum <= 1e-4f) { _weights[target] = 1f; sum = 1f; }
        for (int i = 0; i < _inputCount; i++)
            _mixer.SetInputWeight(i, _weights[i] / sum);
    }

    private void OnDestroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }
}
