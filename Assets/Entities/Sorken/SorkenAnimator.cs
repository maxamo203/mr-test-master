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
    public AnimationClip emergeClip;
    public AnimationClip chaseClip;
    public AnimationClip grabClip;
    [Tooltip("Animacion al ser repelido (retroceder / desaparecer). Si falta, usa chase.")]
    public AnimationClip retreatClip;

    [Tooltip("Velocidad del cross-fade entre clips (unidades de peso por segundo).")]
    [SerializeField] private float _blendSpeed = 6f;

    private SorkenEntity _sorken;
    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private float[] _weights;   // peso actual de cada input (index = (int)SorkenState)
    private int _inputCount;

    private void Awake()
    {
        _sorken = GetComponent<SorkenEntity>();
        var animator = GetComponent<Animator>();
        animator.applyRootMotion = false; // el codigo controla pos/rot, no la animacion

        // Un input del mixer por estado (mismo orden que el enum SorkenState).
        var clips = new[]
        {
            idleClip,                                  // Idle
            emergeClip != null ? emergeClip : idleClip, // Emerging
            chaseClip  != null ? chaseClip  : idleClip, // Chasing
            grabClip   != null ? grabClip   : idleClip, // Grabbing
            retreatClip != null ? retreatClip                        // Retreating
                                : (chaseClip != null ? chaseClip : idleClip),
        };
        _inputCount = clips.Length;
        _weights    = new float[_inputCount];

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
            }
            _mixer.SetInputWeight(i, i == 0 ? 1f : 0f);
        }
        _weights[0] = 1f;
        _graph.Play();
    }

    private void Update()
    {
        if (!_graph.IsValid()) return;

        int target = (int)_sorken.State;
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
