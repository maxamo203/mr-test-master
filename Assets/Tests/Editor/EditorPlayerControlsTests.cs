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

    [Test]
    public void ClickCapturadoSeleccionaDesdeElCentroSoloEnScanner()
    {
        Assert.That(Scanner.SelectionController.ShouldUseEditorCenterClick(
            "ScannerScene", true), Is.True);
        Assert.That(Scanner.SelectionController.ShouldUseEditorCenterClick(
            "ScannerScene", false), Is.False);
        Assert.That(Scanner.SelectionController.ShouldUseEditorCenterClick(
            "SampleScene", true), Is.False);
    }

    [Test]
    public void DeseleccionarRecapturaElMouseSoloEnScanner()
    {
        Assert.That(EditorPlayerControls.ShouldRecaptureAfterDeselect(
            "ScannerScene", true, false), Is.True);
        Assert.That(EditorPlayerControls.ShouldRecaptureAfterDeselect(
            "ScannerScene", false, false), Is.False);
        Assert.That(EditorPlayerControls.ShouldRecaptureAfterDeselect(
            "ScannerScene", true, true), Is.False);
        Assert.That(EditorPlayerControls.ShouldRecaptureAfterDeselect(
            "SampleScene", true, false), Is.False);
    }
}
#endif
