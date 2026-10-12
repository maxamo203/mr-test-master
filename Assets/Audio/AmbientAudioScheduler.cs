using System;
using System.Collections.Generic;
using Gameplay;
using Scanner;
using UnityEngine;

public enum AmbientAudioKind : byte
{
    DoorKnock = 1,
    DoorHandle = 2,
}

// Ventana de silencio testeable. Una espera se sortea una vez y no vuelve a sortearse
// por frame; una amenaza la invalida y obliga a cumplir una ventana completa nueva.
public sealed class AmbientSilenceWindow
{
    public float Elapsed { get; private set; }
    public float Target { get; private set; }
    public bool Armed { get; private set; }

    public void Arm(float seconds)
    {
        Elapsed = 0f;
        Target = Mathf.Max(0f, seconds);
        Armed = true;
    }

    public void Interrupt()
    {
        Elapsed = 0f;
        Armed = false;
    }

    public bool Tick(float deltaTime, bool blocked)
    {
        if (blocked)
        {
            Interrupt();
            return false;
        }
        if (!Armed || deltaTime <= 0f) return false;
        Elapsed += deltaTime;
        if (Elapsed < Target) return false;
        Armed = false;
        return true;
    }
}

// Director host-authoritative de falsas alarmas. No crea amenazas ni modifica cordura:
// solo elige un clip y una puerta real, y replica esa decision a todos los peers.
[DefaultExecutionOrder(-43)]
public sealed class AmbientAudioScheduler : MonoBehaviour
{
    private const float MinQuietSeconds = 5f;
    private const float MaxQuietSeconds = 10f;

    [SerializeField, Range(0, 2)] private int _maxHandleEventsPerNight = 1;
    [SerializeField, Range(0f, 1f)] private float _handleChancePerWindow = 0.3f;

    private readonly AmbientSilenceWindow _silence = new();
    private readonly List<MarkerObject> _doors = new();
    private NetworkManager _network;
    private bool _nightStarted;
    private int _knocksRemaining;
    private int _handlesRemaining;
    private int _lastKnockVariant = -1;
    private uint _lastReceivedSequence;

    private void Update()
    {
        BindNetwork(NetworkManager.Instance);
        bool started = _network != null && _network.GameStarted;
        if (started && !_nightStarted) BeginNight();
        else if (!started && _nightStarted) EndNight();
        if (!started) return;

        bool blocked = IsBlocked();
        if (blocked)
        {
            AudioManager.PararEventoAmbiental();
            _silence.Interrupt();
            return;
        }

        if (!_silence.Armed && (_knocksRemaining > 0 || _handlesRemaining > 0))
            ArmNextWindow();

        if (_network == null || !_network.IsServer) return;
        if (_silence.Tick(Time.unscaledDeltaTime, blocked))
        {
            TryEmit();
            if (_knocksRemaining > 0 || _handlesRemaining > 0) ArmNextWindow();
        }
    }

    private void BindNetwork(NetworkManager network)
    {
        if (_network == network) return;
        if (_network != null)
        {
            _network.OnGameStarted -= BeginNight;
            _network.OnNightReset -= EndNight;
            _network.OnNightSurvived -= EndNight;
            _network.OnAmbientAudio -= PlayReplicated;
        }
        _network = network;
        if (_network != null)
        {
            _network.OnGameStarted += BeginNight;
            _network.OnNightReset += EndNight;
            _network.OnNightSurvived += EndNight;
            _network.OnAmbientAudio += PlayReplicated;
        }
    }

    private void BeginNight()
    {
        _nightStarted = true;
        _knocksRemaining = UnityEngine.Random.Range(1, 3);
        _handlesRemaining = Mathf.Max(0, _maxHandleEventsPerNight);
        _lastKnockVariant = -1;
        _lastReceivedSequence = 0;
        AudioManager.PararEventoAmbiental(0.05f);
        ArmNextWindow();
    }

    private void EndNight()
    {
        _nightStarted = false;
        _knocksRemaining = 0;
        _handlesRemaining = 0;
        _silence.Interrupt();
        AudioManager.PararEventoAmbiental();
    }

    private void ArmNextWindow() =>
        _silence.Arm(UnityEngine.Random.Range(MinQuietSeconds, MaxQuietSeconds));

    private bool IsBlocked()
    {
        if (Time.timeScale <= 0f || Gamepad.PauseMenuController.IsOpen) return true;
        if (!ThreatCoordinator.HasAnyCapablePlayer) return true;
        if (RitualBookDirector.Instance != null &&
            RitualBookDirector.Instance.Fase == RitualBookPhase.Consuming) return true;

        var registry = EntityRegistry.Instance;
        if (registry == null) return false;
        foreach (var entity in registry.All)
        {
            if (entity == null) continue;
            if (entity.EntityTypeId == EntityTypeIds.Sorken ||
                entity.EntityTypeId == EntityTypeIds.Veleth) return true;
            if (entity.EntityTypeId == EntityTypeIds.Arbmos)
            {
                var arbmos = entity.GetComponent<ArbmosEntity>();
                if (arbmos != null && arbmos.Rendered) return true;
            }
        }
        return false;
    }

    private void TryEmit()
    {
        var door = PickDoor();
        var catalog = AudioManager.Catalogo;
        if (door == null || catalog == null) return;

        bool chooseHandle = _handlesRemaining > 0 &&
                            (_knocksRemaining <= 0 ||
                             UnityEngine.Random.value < _handleChancePerWindow);
        AmbientAudioKind kind;
        int variant;

        if (chooseHandle)
        {
            kind = AmbientAudioKind.DoorHandle;
            variant = catalog.ambienteManijaPuerta.ElegirIndiceSinRepetir(-1);
            if (variant < 0) return;
            _handlesRemaining--;
        }
        else
        {
            if (_knocksRemaining <= 0) return;
            kind = AmbientAudioKind.DoorKnock;
            variant = catalog.ambienteGolpesPuerta.ElegirIndiceSinRepetir(_lastKnockVariant);
            if (variant < 0) return;
            _lastKnockVariant = variant;
            _knocksRemaining--;
        }

        _network.ServerSendAmbientAudio((byte)kind, (byte)variant, door.transform.position);
    }

    private MarkerObject PickDoor()
    {
        _doors.Clear();
        var scene = SceneRegistry.Instance;
        if (scene == null) return null;
        foreach (var marker in scene.Markers)
        {
            if (marker != null && string.Equals(marker.KindId, "Door",
                    StringComparison.OrdinalIgnoreCase))
                _doors.Add(marker);
        }
        return _doors.Count > 0 ? _doors[UnityEngine.Random.Range(0, _doors.Count)] : null;
    }

    private void PlayReplicated(AmbientAudioMsg message)
    {
        if (message == null || message.Sequence <= _lastReceivedSequence) return;
        _lastReceivedSequence = message.Sequence;
        if (WorldOrigin.Instance == null) return;

        Vector3 position = WorldOrigin.Instance.ToWorld(message.RelativePosition);
        switch ((AmbientAudioKind)message.Kind)
        {
            case AmbientAudioKind.DoorKnock:
                AudioManager.EventoAmbiental(c => c.ambienteGolpesPuerta,
                                             message.Variant, position);
                break;
            case AmbientAudioKind.DoorHandle:
                AudioManager.EventoAmbiental(c => c.ambienteManijaPuerta,
                                             message.Variant, position);
                break;
        }
    }

    private void OnDestroy() => BindNetwork(null);
}
