using Gameplay;
using NUnit.Framework;

public class TrackingReliabilityTests
{
    [TestCase(10f, 9.3f, 0.75f, true)]
    [TestCase(10f, 9.2f, 0.75f, false)]
    [TestCase(10f, 0f, 0.75f, false)]
    public void PoseRemotaTieneCaducidadExplicita(
        float now, float receivedAt, float maxAge, bool expected)
    {
        Assert.That(TrackingReliability.PoseIsFresh(now, receivedAt, maxAge),
                    Is.EqualTo(expected));
    }
}
