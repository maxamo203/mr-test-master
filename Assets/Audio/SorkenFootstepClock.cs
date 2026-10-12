using UnityEngine;

// Reloj puro de contactos: convierte fase normalizada de una animacion en, como maximo,
// un apoyo nuevo por frame. Ante saltos grandes conserva el contacto mas reciente y no
// reproduce una rafaga de sonidos atrasados.
public sealed class SorkenFootstepClock
{
    private int _state = -1;
    private float _previousPhase;
    private bool _initialized;

    public void Reset(int state, float phase = 0f)
    {
        _state = state;
        _previousPhase = Mathf.Repeat(phase, 1f);
        _initialized = true;
    }

    public void Clear()
    {
        _state = -1;
        _previousPhase = 0f;
        _initialized = false;
    }

    public bool Advance(int state, float phase, bool loops, float[] contacts,
                        out int contactIndex)
    {
        contactIndex = -1;
        phase = loops ? Mathf.Repeat(phase, 1f) : Mathf.Clamp01(phase);

        if (!_initialized || state != _state)
        {
            Reset(state, phase);
            return false;
        }

        if (contacts == null || contacts.Length == 0)
        {
            _previousPhase = phase;
            return false;
        }

        bool wrapped = loops && phase + 0.0001f < _previousPhase;
        if (!loops && phase + 0.0001f < _previousPhase)
        {
            _previousPhase = phase;
            return false;
        }

        if (!wrapped)
        {
            for (int i = 0; i < contacts.Length; i++)
            {
                float marker = Mathf.Clamp01(contacts[i]);
                if (marker > _previousPhase && marker <= phase) contactIndex = i;
            }
        }
        else
        {
            // Orden cronologico: final del ciclo anterior y luego comienzo del actual.
            for (int i = 0; i < contacts.Length; i++)
            {
                float marker = Mathf.Clamp01(contacts[i]);
                if (marker > _previousPhase) contactIndex = i;
            }
            for (int i = 0; i < contacts.Length; i++)
            {
                float marker = Mathf.Clamp01(contacts[i]);
                if (marker <= phase) contactIndex = i;
            }
        }

        _previousPhase = phase;
        return contactIndex >= 0;
    }
}
