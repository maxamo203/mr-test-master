#if UNITY_EDITOR
using NUnit.Framework;

public class EditorPlayerControlsTests
{
    [Test]
    public void SoloScannerSceneHabilitaElControlDeEscaneo()
    {
        Assert.That(EditorPlayerControls.IsScannerScene("ScannerScene"), Is.True);
        Assert.That(EditorPlayerControls.IsScannerScene("SampleScene"), Is.False);
        Assert.That(EditorPlayerControls.IsScannerScene("MenuNoche"), Is.False);
    }
}
#endif
