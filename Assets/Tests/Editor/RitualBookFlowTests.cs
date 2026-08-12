using Gameplay;
using NUnit.Framework;

public class RitualBookFlowTests
{
    [Test]
    public void EsperaElIntervaloAntesDeEmpezarAOscurecer()
    {
        var flow = Crear(delay: 30f);

        flow.Tick(29f, false, consumeSeconds: 6f, defenseSeconds: 4f);
        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Waiting));
        Assert.That(flow.Darkness01, Is.Zero);

        var result = flow.Tick(1f, false, 6f, 4f);
        Assert.That(result.HasFlag(RitualBookTickResult.ConsumptionStarted), Is.True);
        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Consuming));
        Assert.That(flow.Darkness01, Is.Zero);
    }

    [Test]
    public void OscuridadCreceGradualmenteDuranteSeisSegundos()
    {
        var flow = Crear(delay: 0f);

        flow.Tick(3f, false, consumeSeconds: 6f, defenseSeconds: 4f);

        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Consuming));
        Assert.That(flow.Darkness01, Is.EqualTo(0.5f).Within(0.0001f));
    }

    [TestCase(0, 4, 0.5f)]
    [TestCase(1, 4, 0.375f)]
    [TestCase(2, 4, 0.25f)]
    [TestCase(3, 4, 0.125f)]
    public void JugadoresQueApuntanDemoranElAtaqueHastaQueEstenTodos(
        int apuntando, int jugadores, float oscuridadEsperada)
    {
        var flow = Crear(delay: 0f);

        flow.Tick(3f, apuntando, jugadores, consumeSeconds: 6f, defenseSeconds: 4f);

        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Consuming));
        Assert.That(flow.Darkness01, Is.EqualTo(oscuridadEsperada).Within(0.0001f));
        Assert.That(flow.Defense01, Is.Zero);
    }

    [TestCase(1, 2, 0.25f)]
    [TestCase(1, 3, 1f / 3f)]
    [TestCase(2, 3, 1f / 6f)]
    public void LaFormulaSeAjustaAlTamanoRealDeLaSesion(
        int apuntando, int jugadores, float oscuridadEsperada)
    {
        var flow = Crear(delay: 0f);

        flow.Tick(3f, apuntando, jugadores, consumeSeconds: 6f, defenseSeconds: 4f);

        Assert.That(flow.Darkness01, Is.EqualTo(oscuridadEsperada).Within(0.0001f));
        Assert.That(flow.Defense01, Is.Zero);
    }

    [Test]
    public void UnJugadorConservaElComportamientoIndividualSinDemoraParcial()
    {
        var flow = Crear(delay: 0f);
        flow.Tick(3f, 0, 1, 6f, 4f);

        flow.Tick(1f, 1, 1, 6f, 4f);

        Assert.That(flow.Darkness01, Is.EqualTo(0.375f).Within(0.0001f));
        Assert.That(flow.Defense01, Is.EqualTo(0.25f).Within(0.0001f));
    }

    [Test]
    public void TodosLosJugadoresHacenRetrocederLaOscuridadYProtegenElLibro()
    {
        var flow = Crear(delay: 0f);
        flow.Tick(3f, 0, 4, 6f, 4f);

        var result = flow.Tick(4f, 4, 4, 6f, 4f);

        Assert.That(result.HasFlag(RitualBookTickResult.Saved), Is.True);
        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Waiting));
        Assert.That(flow.Darkness01, Is.Zero);
    }

    [Test]
    public void PerderLaProteccionTotalRetomaDesdeElPorcentajeReducido()
    {
        var flow = Crear(delay: 0f);
        flow.Tick(3f, 0, 4, 6f, 4f); // 50%
        flow.Tick(2f, 4, 4, 6f, 4f); // vuelve a 25%

        flow.Tick(3f, 2, 4, 6f, 4f); // media velocidad: 1.5 s de la nueva ventana

        Assert.That(flow.Darkness01, Is.EqualTo(0.4375f).Within(0.0001f));
        Assert.That(flow.Defense01, Is.Zero);
    }

    [Test]
    public void CuatroSegundosContinuosDeLinternaSalvanElLibro()
    {
        var flow = Crear(delay: 0f);

        var result = flow.Tick(4f, true, consumeSeconds: 6f, defenseSeconds: 4f);

        Assert.That(result.HasFlag(RitualBookTickResult.Saved), Is.True);
        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Waiting));
        Assert.That(flow.Darkness01, Is.Zero);
        Assert.That(flow.Defense01, Is.Zero);
    }

    [Test]
    public void DejarDeAlumbrarReiniciaLaDefensaContinua()
    {
        var flow = Crear(delay: 0f);

        flow.Tick(1f, true, 6f, 4f);
        flow.Tick(0.1f, false, 6f, 4f);
        flow.Tick(3.9f, true, 6f, 4f);

        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Consuming));
        Assert.That(flow.Defense01, Is.EqualTo(0.975f).Within(0.0001f));

        var result = flow.Tick(0.1f, true, 6f, 4f);
        Assert.That(result.HasFlag(RitualBookTickResult.Saved), Is.True);
    }

    [Test]
    public void ASeisSegundosElLibroQuedaConsumido()
    {
        var flow = Crear(delay: 0f);

        var result = flow.Tick(6f, false, consumeSeconds: 6f, defenseSeconds: 4f);

        Assert.That(result.HasFlag(RitualBookTickResult.Consumed), Is.True);
        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Consumed));
        Assert.That(flow.Darkness01, Is.EqualTo(1f));
    }

    [Test]
    public void SalvarProgramaUnNuevoIntervalo()
    {
        float[] delays = { 30f, 50f };
        int index = 0;
        var flow = new RitualBookFlow(() => delays[index++]);
        flow.Restart();

        flow.Tick(30f, false, 6f, 4f);
        flow.Tick(4f, true, 6f, 4f);

        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Waiting));
        Assert.That(flow.WaitRemaining, Is.EqualTo(50f).Within(0.0001f));
    }

    [Test]
    public void ElPlazoDeConsumoEsConfigurableATambienDiezSegundos()
    {
        var flow = Crear(delay: 0f);

        flow.Tick(9.99f, false, consumeSeconds: 10f, defenseSeconds: 4f);
        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Consuming));

        var result = flow.Tick(0.01f, false, consumeSeconds: 10f, defenseSeconds: 4f);
        Assert.That(result.HasFlag(RitualBookTickResult.Consumed), Is.True);
    }

    [Test]
    public void SiDefensaYConsumoCoincidenLaDefensaGana()
    {
        var flow = Crear(delay: 0f);

        var result = flow.Tick(4f, true, consumeSeconds: 4f, defenseSeconds: 4f);

        Assert.That(result.HasFlag(RitualBookTickResult.Saved), Is.True);
        Assert.That(result.HasFlag(RitualBookTickResult.Consumed), Is.False);
        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Waiting));
    }

    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, false)]
    public void LibroSoloApareceConAnclaValidaYPartidaActiva(
        bool hayAncla, bool enSesion, bool esperado)
    {
        Assert.That(RitualBookView.PuedeAparecer(hayAncla, enSesion), Is.EqualTo(esperado));
    }

    [Test]
    public void IntervaloAleatorioSiempreQuedaEntreTreintaYCincuentaSegundos()
    {
        var config = UnityEngine.ScriptableObject.CreateInstance<NightConfig>();
        config.bookEventDelayMin = 30f;
        config.bookEventDelayMax = 50f;

        for (int i = 0; i < 100; i++)
            Assert.That(config.RandomBookEventDelay(), Is.InRange(30f, 50f));

        UnityEngine.Object.DestroyImmediate(config);
    }

    [Test]
    public void AlumbrarReduceLaOscuridadMientrasAcumulaDefensa()
    {
        var flow = Crear(delay: 0f);
        flow.Tick(2f, false, 6f, 4f);
        float oscuridadAntes = flow.Darkness01;

        flow.Tick(2f, true, 6f, 4f);

        Assert.That(flow.Darkness01, Is.EqualTo(oscuridadAntes * 0.5f).Within(0.0001f));
        Assert.That(flow.Defense01, Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void AlDejarDeAlumbrarAvanzaDesdeElPorcentajeReducido()
    {
        var flow = Crear(delay: 0f);
        flow.Tick(2f, false, 6f, 4f);
        flow.Tick(1f, true, 6f, 4f);

        flow.Tick(1f, false, 6f, 4f);

        // 2 s de ataque = 1/3; 1 s de defensa quita 1/4 => queda 1/4.
        // Al soltar, avanza 1/6 del tramo 1/4..1 => 3/8.
        Assert.That(flow.Darkness01, Is.EqualTo(0.375f).Within(0.0001f));
        Assert.That(flow.Defense01, Is.Zero);
    }

    [Test]
    public void ElPorcentajeReducidoRecibeUnaVentanaNuevaCompletaHastaCien()
    {
        var flow = Crear(delay: 0f);
        flow.Tick(2f, false, 6f, 4f);
        flow.Tick(2f, true, 6f, 4f);

        var antes = flow.Tick(5.99f, false, 6f, 4f);
        Assert.That(antes.HasFlag(RitualBookTickResult.Consumed), Is.False);
        Assert.That(flow.Phase, Is.EqualTo(RitualBookPhase.Consuming));

        var limite = flow.Tick(0.01f, false, 6f, 4f);
        Assert.That(limite.HasFlag(RitualBookTickResult.Consumed), Is.True);
        Assert.That(flow.Darkness01, Is.EqualTo(1f));
    }

    [Test]
    public void CincuentaPorCientoLogicoUsaEscalaDeAreaCorrecta()
    {
        float escala = RitualBookView.EscalaParaCobertura(0.5f);
        Assert.That(escala * escala, Is.EqualTo(0.5f).Within(0.0001f));
    }

    private static RitualBookFlow Crear(float delay)
    {
        var flow = new RitualBookFlow(() => delay);
        flow.Restart();
        return flow;
    }
}
