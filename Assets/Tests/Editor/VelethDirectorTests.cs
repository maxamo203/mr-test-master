using NUnit.Framework;

public class VelethDirectorTests
{
    [Test]
    public void NoCapturaAunqueEsteCercaSiHayUnObstaculo()
    {
        Assert.That(VelethDirector.CanCaptureAtDistance(0.8f, 1.2f, false), Is.False);
        Assert.That(VelethDirector.CanCaptureAtDistance(0.8f, 1.2f, true), Is.True);
    }
}
