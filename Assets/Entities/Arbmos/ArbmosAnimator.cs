using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Reproduce las animaciones del Arbmos con la Playables API (igual criterio que el
// SorkenAnimator): arrastras los AnimationClip en el inspector — SIN Animator
// Controller ni transiciones cableadas — y el codigo hace el cross-fade segun
// ArbmosEntity.State. Corre en el UNICO peer que dibuja esta copia (el estado viene
// del server).
//
// Usa tres variantes idle de aparición y un único clip de persecución. El estado Running
// conserva la variante idle: ese desplazamiento no letal lo controla el código.
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

    public int SelectedIdleIndex { get; private set; } = -1;

    [Tooltip("Velocidad del cross-fade entre clips (unidades de peso por segundo).")]
    [SerializeField] private float _blendSpeed = 6f;

    private ArbmosEntity _arbmos;
    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private float[] _weights;   // peso actual de cada input (index = (int)ArbmosState)
    private int _inputCount;

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

        // Running pertenece al desplazamiento no letal: mantiene la pose elegida.
        // Chasing usa exclusivamente el nuevo ciclo de persecución.
        var clips = new[]
        {
            selectedIdle,
            selectedIdle,
            chaseClip != null ? chaseClip : selectedIdle,
        };
        _inputCount = clips.Length;
        _weights = new float[_inputCount];

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
                _graph.Connect(cp, 0, _mixer, i);
            }
            _mixer.SetInputWeight(i, i == 0 ? 1f : 0f);
        }
        _weights[0] = 1f;
        _graph.Play();
    }

    private void Update()
    {
        if (!_graph.IsValid()) return;

        int target = (int)_arbmos.State;
        if (target < 0 || target >= _inputCount) target = 0;

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
