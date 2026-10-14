using NUnit.Framework;
using UnityEditor;

public class AudioDecisions20261013Tests
{
    private AudioCatalog _catalog;

    [SetUp]
    public void LoadCatalog()
    {
        _catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(
            "Assets/Resources/AudioCatalog.asset");
        Assert.That(_catalog, Is.Not.Null);
    }

    [Test]
    public void FinalBackgroundAndDeathStingerAreAssigned()
    {
        AssertTrack(_catalog.ambienteNoche, 1, "dark_choir_singing_loop");
        AssertTrack(_catalog.gameOverStinger, 1, "gameover_coro_breve");
    }

    [Test]
    public void ArbmosFinalBanksAreComplete()
    {
        AssertTrack(_catalog.arbmosExhalacion, 1, "arbmos_exhalacion");
        AssertTrack(_catalog.arbmosResonanciaEntrada, 1, "arbmos_resonancia_entrada");
        AssertTrack(_catalog.arbmosResonanciaSostenida, 1, "arbmos_resonancia_sostenida_loop");
        AssertTrack(_catalog.arbmosResonanciaSalida, 1, "arbmos_resonancia_salida_natural");
        AssertTrack(_catalog.arbmosEnfado, 3, "arbmos_enfado_01");
        AssertTrack(_catalog.arbmosPasos, 4, "arbmos_paso_01");
        AssertTrack(_catalog.arbmosAgarreNoLetal, 1, "arbmos_agarre_no_letal_gasp");
        AssertTrack(_catalog.arbmosAgarreLetal, 1, "arbmos_agarre_letal_primera_onda");
    }

    [Test]
    public void VelethFinalBanksAreComplete()
    {
        AssertTrack(_catalog.velethSalidaLibro, 1, "veleth_salida_libro_organica_8_3s");
        Assert.That(_catalog.velethSalidaLibro.clips[0].length, Is.EqualTo(8.3f).Within(0.03f));
        AssertTrack(_catalog.velethApoyos, 5, "veleth_apoyo_01");
        AssertTrack(_catalog.velethRoces, 5, "veleth_roce_01");
        AssertTrack(_catalog.velethPersecucionEspectral, 1, "veleth_persecucion_espectral_loop");
        AssertTrack(_catalog.velethCapturaFatal, 1, "veleth_captura_fatal_scream");
    }

    [Test]
    public void SorkenFinalVocalBanksAreComplete()
    {
        AssertTrack(_catalog.sorkenCubrirse, 2, "sorken_cubrirse_dolor_enfado_01");
        AssertTrack(_catalog.sorkenMarchaCubierta, 5, "sorken_marcha_cubierta_vocal_01");
        AssertTrack(_catalog.sorkenCapturaFatal, 1, "sorken_captura_fatal_scream");
    }

    [Test]
    public void BookUsesTheFinalLayeredWhisperLoop()
    {
        AssertTrack(_catalog.libroSusurrosConsumo, 1, "susurros_superpuestos_consumo_loop");
        Assert.That(_catalog.libroSusurrosConsumo.clips[0].length, Is.EqualTo(32f).Within(0.03f));
    }

    private static void AssertTrack(AudioCatalog.Pista track, int expectedCount,
                                    string expectedFirstName)
    {
        Assert.That(track, Is.Not.Null);
        Assert.That(track.CantidadClipsValidos, Is.EqualTo(expectedCount));
        Assert.That(track.ClipValido(0), Is.Not.Null);
        Assert.That(track.ClipValido(0).name, Is.EqualTo(expectedFirstName));
    }
}
