using NUnit.Framework;
using UnityEngine;

// Tests de la logica PURA del sistema de audio: la formula de mezcla y la resolucion del
// sonido por tipo de punto de entrada (US-4.1). Nada de reproducir sonido: eso necesita
// un AudioListener y solo se puede juzgar de oido.
public class AudioCatalogTests
{
    private AudioCatalog _cat;
    private AudioClip _puertaA, _puertaB, _ventana, _generico;

    [SetUp]
    public void Crear()
    {
        _cat      = ScriptableObject.CreateInstance<AudioCatalog>();
        _puertaA  = Clip("puertaA");
        _puertaB  = Clip("puertaB");
        _ventana  = Clip("ventana");
        _generico = Clip("generico");

        _cat.entradasPorTipo = new[]
        {
            Entrada("Door",   _puertaA, _puertaB),
            Entrada("Window", _ventana),
        };
        _cat.entradaGenerica = new AudioCatalog.Pista { clips = new[] { _generico } };
    }

    [TearDown]
    public void Destruir()
    {
        Object.DestroyImmediate(_cat);
        foreach (var c in new[] { _puertaA, _puertaB, _ventana, _generico })
            if (c != null) Object.DestroyImmediate(c);
    }

    private static AudioClip Clip(string nombre) => AudioClip.Create(nombre, 1, 1, 44100, false);

    private static AudioCatalog.PistaPorTipo Entrada(string id, params AudioClip[] clips) =>
        new AudioCatalog.PistaPorTipo
        {
            markerTypeId = id,
            pista = new AudioCatalog.Pista { clips = clips },
        };

    // ── Mezcla ────────────────────────────────────────────────────────────

    [TestCase(1f,   1f,   1f)]
    [TestCase(0.5f, 1f,   0.5f)]
    [TestCase(1f,   0.5f, 0.5f)]
    [TestCase(0.5f, 0.4f, 0.2f)]
    [TestCase(0f,   1f,   0f)]
    [TestCase(1f,   0f,   0f)]
    public void ElVolumenEsElProductoDeLaPistaPorElBus(float pista, float bus, float esperado)
    {
        Assert.That(AudioManager.MezclarVolumen(pista, bus), Is.EqualTo(esperado).Within(1e-5f));
    }

    [Test]
    public void ElVolumenNuncaSeVaDeRango()
    {
        Assert.That(AudioManager.MezclarVolumen(5f, 5f), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(AudioManager.MezclarVolumen(-3f, 1f), Is.Zero);
    }

    // ── US-4.1: resolucion por tipo de punto de entrada ───────────────────

    [Test]
    public void CadaTipoDeEntradaResuelveASuPropioIndice()
    {
        Assert.That(_cat.IndiceEntrada("Door"),   Is.EqualTo(0));
        Assert.That(_cat.IndiceEntrada("Window"), Is.EqualTo(1));
    }

    [Test]
    public void ElIdDelTipoNoDistingueMayusculas()
    {
        // Los ids los tipea una persona en dos assets distintos (MarkerType y el catalogo
        // de audio); que un "door" contra un "Door" deje el sonido mudo seria una trampa.
        Assert.That(_cat.IndiceEntrada("door"), Is.EqualTo(0));
        Assert.That(_cat.IndiceEntrada("WINDOW"), Is.EqualTo(1));
    }

    [TestCase("Ducto")]
    [TestCase("")]
    [TestCase(null)]
    public void UnTipoQueNoEstaEnElCatalogoQuedaComoDesconocido(string id)
    {
        Assert.That(_cat.IndiceEntrada(id), Is.EqualTo(AudioCatalog.IndiceDesconocido));
    }

    [Test]
    public void UnaPuertaSoloPuedeSonarConClipsDePuerta()
    {
        // El requisito del usuario: dentro del tipo la eleccion es al azar, pero NUNCA
        // puede salir un clip de otro tipo. Se repite para cubrir las dos ramas del azar.
        var puerta = _cat.EntradaPorIndice(_cat.IndiceEntrada("Door"));
        for (int i = 0; i < 50; i++)
        {
            var elegido = puerta.Elegir();
            Assert.That(elegido, Is.EqualTo(_puertaA).Or.EqualTo(_puertaB));
        }
    }

    [Test]
    public void ConUnSoloClipLaEleccionEsDeterminista()
    {
        var ventana = _cat.EntradaPorIndice(_cat.IndiceEntrada("Window"));
        for (int i = 0; i < 10; i++)
            Assert.That(ventana.Elegir(), Is.EqualTo(_ventana));
    }

    [Test]
    public void UnIndiceDesconocidoCaeEnLaPistaGenerica()
    {
        var p = _cat.EntradaPorIndice(AudioCatalog.IndiceDesconocido);
        Assert.That(p.Elegir(), Is.EqualTo(_generico));
    }

    [Test]
    public void UnIndiceFueraDeRangoCaeEnLaGenericaEnVezDeRomper()
    {
        // Pasa de verdad: el host manda un indice y despues alguien borra una entrada del
        // catalogo. Tiene que degradar a la generica, no tirar IndexOutOfRange.
        Assert.That(_cat.EntradaPorIndice(200).Elegir(), Is.EqualTo(_generico));
    }

    [Test]
    public void UnTipoConSlotVacioCaeEnLaGenerica()
    {
        _cat.entradasPorTipo = new[] { Entrada("Door") };   // sin clips
        Assert.That(_cat.EntradaPorIndice(0).Elegir(), Is.EqualTo(_generico));
    }

    // ── Slots vacios (el estado inicial del proyecto: no hay ni un audio) ──

    [Test]
    public void UnaPistaSinClipsEstaVacia()
    {
        Assert.That(new AudioCatalog.Pista().Vacia, Is.True);
        Assert.That(new AudioCatalog.Pista { clips = new AudioClip[0] }.Vacia, Is.True);
        Assert.That(new AudioCatalog.Pista { clips = new AudioClip[] { null, null } }.Vacia, Is.True);
    }

    [Test]
    public void UnaPistaConHuecosSigueEligiendoSoloClipsReales()
    {
        // Agrandar el array en el Inspector deja elementos None: no pueden salir elegidos.
        var p = new AudioCatalog.Pista { clips = new[] { null, _ventana, null } };
        Assert.That(p.Vacia, Is.False);
        for (int i = 0; i < 30; i++)
            Assert.That(p.Elegir(), Is.EqualTo(_ventana));
    }

    [Test]
    public void UnCatalogoRecienCreadoNoTieneNingunClip()
    {
        var vacio = ScriptableObject.CreateInstance<AudioCatalog>();
        try { Assert.That(vacio.TieneAlgunClip(), Is.False); }
        finally { Object.DestroyImmediate(vacio); }
    }

    [Test]
    public void UnCatalogoConUnClipYaCuentaComoActivo()
    {
        var conAlgo = ScriptableObject.CreateInstance<AudioCatalog>();
        try
        {
            conAlgo.linternaOn = new AudioCatalog.Pista { clips = new[] { _generico } };
            Assert.That(conAlgo.TieneAlgunClip(), Is.True);
        }
        finally { Object.DestroyImmediate(conAlgo); }
    }
}
