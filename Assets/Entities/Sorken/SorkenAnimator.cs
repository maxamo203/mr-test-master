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
    public AnimationClip chaseClip;
    public AnimationClip grabClip;
    [Tooltip("Animacion al ser repelido (retroceder / desaparecer). Si falta, usa chase.")]
    public AnimationClip retreatClip;
    public AnimationClip coverStartClip;
    public AnimationClip coverWalkClip;

    [Tooltip("Velocidad del cross-fade entre clips (unidades de peso por segundo).")]
    [SerializeField] private float _blendSpeed = 6f;

    private SorkenEntity _sorken;
    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private float[] _weights;   // peso actual de cada input (index = (int)SorkenState)
    private int _inputCount;
    private AnimationClipPlayable[] _clipPlayables;
    private int _lastState = -1;

    private void Awake()
    {
        _sorken = GetComponent<SorkenEntity>();
        var animator = GetComponent<Animator>();
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
            if (_clipPlayables[target].IsValid())
            {
                _clipPlayables[target].SetTime(0d);
                _clipPlayables[target].SetPlayState(PlayState.Playing);
            }
            _lastState = target;
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
