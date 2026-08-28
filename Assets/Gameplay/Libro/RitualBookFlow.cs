using System;
using UnityEngine;

namespace Gameplay
{
    public enum RitualBookPhase : byte
    {
        Waiting   = 0,
        Consuming = 1,
        Consumed  = 2,
    }

    [Flags]
    public enum RitualBookTickResult : byte
    {
        None              = 0,
        ConsumptionStarted = 1 << 0,
        Saved             = 1 << 1,
        Consumed          = 1 << 2,
    }

    // Logica pura de la epica "Veleth y libro del ritual". No conoce GameObjects,
    // red ni linternas: recibe si el libro esta iluminado y expone valores 0..1 para
    // que el director autoritativo los replique y la vista los dibuje.
    public sealed class RitualBookFlow
    {
        private readonly Func<float> _nextDelay;

        private float _waitRemaining;
        private float _consumeElapsed;
        private float _consumeStartDarkness;
        private float _defenseElapsed;
        private float _defenseStartDarkness;
        private bool _wasIlluminated;

        public RitualBookPhase Phase { get; private set; } = RitualBookPhase.Waiting;
        public float Darkness01 { get; private set; }
        public float Defense01 { get; private set; }
        public float WaitRemaining => Mathf.Max(0f, _waitRemaining);

        public RitualBookFlow(Func<float> nextDelay)
        {
            _nextDelay = nextDelay ?? throw new ArgumentNullException(nameof(nextDelay));
        }

        public void Restart()
        {
            Phase = RitualBookPhase.Waiting;
            Darkness01 = 0f;
            Defense01 = 0f;
            _consumeElapsed = 0f;
            _consumeStartDarkness = 0f;
            _defenseElapsed = 0f;
            _defenseStartDarkness = 0f;
            _wasIlluminated = false;
            _waitRemaining = Mathf.Max(0f, _nextDelay());
        }

        // Compatibilidad para escenarios de un solo jugador.
        public RitualBookTickResult Tick(float deltaTime, bool illuminated,
                                         float consumeSeconds, float defenseSeconds) =>
            Tick(deltaTime, illuminated ? 1 : 0, 1, consumeSeconds, defenseSeconds);

        // Con varios jugadores, cada jugador que apunta reduce una fracción del ataque.
        // Mientras no sean todos: velocidad = 1 - (apuntando / jugadores vivos).
        // La oscuridad recién retrocede cuando apuntan TODOS.
        public RitualBookTickResult Tick(float deltaTime, int illuminatingPlayers,
                                         int alivePlayers, float consumeSeconds,
                                         float defenseSeconds)
        {
            if (Phase == RitualBookPhase.Consumed || deltaTime <= 0f)
                return RitualBookTickResult.None;

            float remaining = deltaTime;
            var result = RitualBookTickResult.None;

            if (Phase == RitualBookPhase.Waiting)
            {
                if (remaining < _waitRemaining)
                {
                    _waitRemaining -= remaining;
                    return result;
                }

                remaining -= _waitRemaining;
                BeginConsumption();
                result |= RitualBookTickResult.ConsumptionStarted;
            }

            consumeSeconds = Mathf.Max(0.001f, consumeSeconds);
            defenseSeconds = Mathf.Max(0.001f, defenseSeconds);

            int alive = Mathf.Max(1, alivePlayers);
            int illuminating = Mathf.Clamp(illuminatingPlayers, 0, alive);
            bool fullyProtected = illuminating >= alive;

            if (fullyProtected)
            {
                if (!_wasIlluminated)
                {
                    _defenseStartDarkness = Darkness01;
                    _defenseElapsed = 0f;
                }

                float advanced = Mathf.Min(remaining, defenseSeconds - _defenseElapsed);
                _defenseElapsed += advanced;
                Defense01 = Mathf.Clamp01(_defenseElapsed / defenseSeconds);
                Darkness01 = Mathf.Lerp(_defenseStartDarkness, 0f, Defense01);
                _wasIlluminated = true;

                if (_defenseElapsed >= defenseSeconds - 1e-5f)
                {
                    Restart();
                    return result | RitualBookTickResult.Saved;
                }
                return result;
            }

            // Al soltar la linterna, el porcentaje alcanzado pasa a ser el inicio de un
            // tramo NUEVO: desde ahi tarda consumeSeconds completos en llegar al 100%.
            if (_wasIlluminated)
            {
                _consumeStartDarkness = Darkness01;
                _consumeElapsed = 0f;
                _defenseElapsed = 0f;
                Defense01 = 0f;
                _wasIlluminated = false;
            }

            // Nadie apuntando: 100%. Cada jugador reduce 1 / total de la sesión:
            // con cuatro, 1 apunta=75%, 2=50%, 3=25%. Defensa continúa en cero.
            float attackSpeed = 1f - illuminating / (float)alive;
            float scaledRemaining = remaining * attackSpeed;
            float consumeAdvanced = Mathf.Min(scaledRemaining, consumeSeconds - _consumeElapsed);
            _consumeElapsed += consumeAdvanced;
            Darkness01 = Mathf.Lerp(_consumeStartDarkness, 1f,
                Mathf.Clamp01(_consumeElapsed / consumeSeconds));
            Defense01 = 0f;

            if (_consumeElapsed >= consumeSeconds - 1e-5f)
            {
                Phase = RitualBookPhase.Consumed;
                Darkness01 = 1f;
                Defense01 = 0f;
                return result | RitualBookTickResult.Consumed;
            }

            return result;
        }

        private void BeginConsumption()
        {
            Phase = RitualBookPhase.Consuming;
            _waitRemaining = 0f;
            _consumeElapsed = 0f;
            _consumeStartDarkness = 0f;
            _defenseElapsed = 0f;
            _defenseStartDarkness = 0f;
            _wasIlluminated = false;
            Darkness01 = 0f;
            Defense01 = 0f;
        }
    }
}
