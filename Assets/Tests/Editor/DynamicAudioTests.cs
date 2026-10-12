using NUnit.Framework;
using UnityEngine;

public class DynamicAudioTests
{
    [Test]
    public void FootstepClockFiresOnCrossedContact()
    {
        var clock = new SorkenFootstepClock();
        clock.Reset((int)SorkenState.Chasing, 0.1f);

        Assert.That(clock.Advance((int)SorkenState.Chasing, 0.3f, true,
            new[] { 0.2f, 0.6f }, out int contact), Is.True);
        Assert.That(contact, Is.EqualTo(0));
    }

    [Test]
    public void FootstepClockHandlesLoopWrapWithoutDuplicate()
    {
        var clock = new SorkenFootstepClock();
        var contacts = new[] { 0.05f, 0.5f, 0.95f };
        clock.Reset((int)SorkenState.Chasing, 0.9f);

        Assert.That(clock.Advance((int)SorkenState.Chasing, 0.08f, true,
            contacts, out int contact), Is.True);
        Assert.That(contact, Is.EqualTo(0));
        Assert.That(clock.Advance((int)SorkenState.Chasing, 0.1f, true,
            contacts, out _), Is.False);
    }

    [Test]
    public void FootstepClockDiscardsOldContactsAfterLargeJump()
    {
        var clock = new SorkenFootstepClock();
        clock.Reset((int)SorkenState.CoverWalking, 0f);

        Assert.That(clock.Advance((int)SorkenState.CoverWalking, 0.9f, true,
            new[] { 0.1f, 0.4f, 0.8f }, out int contact), Is.True);
        Assert.That(contact, Is.EqualTo(2));
    }

    [Test]
    public void StateChangeDoesNotReplayStaleContact()
    {
        var clock = new SorkenFootstepClock();
        clock.Reset((int)SorkenState.Chasing, 0.8f);

        Assert.That(clock.Advance((int)SorkenState.Retreating, 0.7f, false,
            new[] { 0.2f, 0.6f }, out _), Is.False);
    }

    [Test]
    public void SilenceWindowRequiresContinuousUnblockedTime()
    {
        var window = new AmbientSilenceWindow();
        window.Arm(6f);
        Assert.That(window.Tick(4f, false), Is.False);
        Assert.That(window.Tick(0.1f, true), Is.False);
        Assert.That(window.Armed, Is.False);

        window.Arm(6f);
        Assert.That(window.Tick(5.9f, false), Is.False);
        Assert.That(window.Tick(0.1f, false), Is.True);
        Assert.That(window.Tick(10f, false), Is.False);
    }

    [Test]
    public void AmbientAudioMessageRoundTripsExactDecision()
    {
        var original = new AmbientAudioMsg
        {
            Sequence = 42,
            Kind = (byte)AmbientAudioKind.DoorKnock,
            Variant = 1,
            RelativePosition = new Vector3(1.5f, 2f, -3f),
        };
        var copy = AmbientAudioMsg.Deserialize(original.Serialize());

        Assert.That(copy.Sequence, Is.EqualTo(original.Sequence));
        Assert.That(copy.Kind, Is.EqualTo(original.Kind));
        Assert.That(copy.Variant, Is.EqualTo(original.Variant));
        Assert.That(copy.RelativePosition, Is.EqualTo(original.RelativePosition));
    }
}
