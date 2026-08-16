using System;
using System.Collections.Generic;
using UnityEngine;

// Motor de audio del juego. Es el UNICO que crea AudioSources y el unico que reproduce:
// todo el gameplay lo llama desde aca, y todos los clips salen del AudioCatalog.
//
// Se auto-crea al arrancar la app (RuntimeInitializeOnLoadMethod + DontDestroyOnLoad), asi
// que NO hay que ponerlo en ninguna escena y sobrevive a SceneFlow.GoTo (que solo destruye
// NetworkManager, EntityRegistry y WorldOrigin). Mismo patron que GamepadManager.
//
// NO crea AudioListener: ya hay uno por escena, sobre la Main Camera del rig XR (que ademas
// esta head-trackeada, que es lo que hace que el paneo se actualice al girar la cabeza).
// NO toca AudioListener.volume: ese es el volumen MAESTRO y lo maneja GameOptions.Volumen.
// NO toca AudioSettings en runtime: eso recrearia el motor de audio e invalidaria los clips
// de streaming del chat de voz (Voice/VoiceStream), dejandolo mudo sin forma de recuperarse.
//
// Reparto de voces (m_RealVoiceCount = 48 en ProjectSettings/AudioManager.asset):
//   2 musica (crossfade) + 1 capa de tension + 8 loops + 24 one-shots + 3 del chat de voz.
// Musica y loops NUNCA se roban; si los 24 one-shots estan ocupados se reusa el mas viejo.
// Asi ningun sonido corta a otro en la practica.
//
// Espacializacion: todas las fuentes 3D nacen con spatialize = true aunque hoy no haya
// plugin instalado (sin plugin la bandera es inocua). Si mas adelante se instala Steam
// Audio, alcanza con setear el Spatializer Plugin en Project Settings: cero cambios de codigo.
[DefaultExecutionOrder(-45)]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private const int CantOneShots = 24;
    private const int CantBucles   = 8;

    private AudioCatalog _cat;

    // Pool de one-shots + el instante en que arranco cada uno (para reusar el mas viejo).
    private AudioSource[] _pool;
    private float[]       _poolDesde;

    // Loops con clave ("sorken_pasos", "veleth_persecucion", ...). Idempotentes: volver a
    // pedirlos solo actualiza posicion y volumen, no reinicia el sonido.
    private readonly Dictionary<string, AudioSource> _bucles = new();
    private AudioSource[] _buclesLibres;

    // Musica: dos fuentes para poder hacer crossfade entre pistas.
    private AudioSource _musA, _musB;
    private bool        _usandoA;
    private AudioClip   _musActual;
    private float       _musObjetivo, _musFade;

    // Capa de tension (US-11.3): loop propio cuyo volumen sigue a TensionSystem.
    private AudioSource _tension;
    private float       _tension01;

    // True solo si hay catalogo con al menos un clip cargado. Mientras no haya audio en el
    // proyecto esto es false y el Update se va en la primera linea: costo cero.
    public static bool Activo => Instance != null && Instance._cat != null &&
                                 Instance._cat.TieneAlgunClip();

    // Catalogo cargado (null si falta el asset). Publico para inspeccion/tests.
    public static AudioCatalog Catalogo => Instance != null ? Instance._cat : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("AudioManager");
        go.AddComponent<AudioManager>();
        // El watcher traduce el estado de las entidades a sonidos. Va en el mismo objeto
        // para que viva y muera con el motor.
        go.AddComponent<AudioEventWatcher>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _cat = Resources.Load<AudioCatalog>(AudioCatalog.ResourceName);
        if (_cat == null)
        {
            // Un solo aviso, no por frame. Sin catalogo el juego corre mudo pero entero.
            Debug.LogWarning($"[Audio] Falta Assets/Resources/{AudioCatalog.ResourceName}.asset " +
                             "(crealo con el menu Mortuorium > Crear catalogo de audio). El juego corre sin sonido.");
            return;
        }

        ConstruirFuentes();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Crea todas las fuentes de una vez, al arrancar. Nunca se crean AudioSources en
    // caliente: instanciarlas durante un susto seria justo el peor momento para un hitch.
    private void ConstruirFuentes()
    {
        _musA = NuevaFuente("Musica A", loop: true);
        _musB = NuevaFuente("Musica B", loop: true);
        _tension = NuevaFuente("Capa Tension", loop: true);

        _pool      = new AudioSource[CantOneShots];
        _poolDesde = new float[CantOneShots];
        for (int i = 0; i < CantOneShots; i++)
            _pool[i] = NuevaFuente("OneShot " + i, loop: false);

        _buclesLibres = new AudioSource[CantBucles];
        for (int i = 0; i < CantBucles; i++)
            _buclesLibres[i] = NuevaFuente("Bucle " + i, loop: true);
    }

    private AudioSource NuevaFuente(string nombre, bool loop)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(transform, false);

        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop        = loop;
        src.volume      = 0f;

        // Espacializacion: ver el comentario de cabecera. El doppler queda en 0 porque con
        // el tracking AR (que corrige la pose a saltos) produce artefactos de tono audibles.
        src.spatialize           = true;
        src.spatializePostEffects = true;
        src.dopplerLevel         = 0f;
        src.rolloffMode          = AudioRolloffMode.Logarithmic;
        return src;
    }

    // ───────────────────────────────────────────────────────── API publica

    // One-shot plano (UI, avisos que no tienen lugar en el cuarto).
    public static void Sonar(Func<AudioCatalog, AudioCatalog.Pista> sel) =>
        Reproducir(sel, Vector3.zero, false, 1f);

    // One-shot posicionado: el jugador puede ubicar de donde viene.
    public static void Sonar(Func<AudioCatalog, AudioCatalog.Pista> sel, Vector3 pos) =>
        Reproducir(sel, pos, true, 1f);

    // One-shot posicionado con un factor extra de volumen (0..1), para modular por
    // distancia/intensidad sin tocar la calibracion del catalogo.
    public static void Sonar(Func<AudioCatalog, AudioCatalog.Pista> sel, Vector3 pos, float escala) =>
        Reproducir(sel, pos, true, escala);

    private static void Reproducir(Func<AudioCatalog, AudioCatalog.Pista> sel,
                                   Vector3 pos, bool posicionado, float escala)
    {
        var m = Instance;
        if (m == null || m._cat == null || m._pool == null || sel == null) return;

        var pista = sel(m._cat);
        if (pista == null || pista.Vacia) return;      // slot vacio: silencio, sin log

        var clip = pista.Elegir();
        if (clip == null) return;

        var src = m.TomarDelPool();
        if (src == null) return;

        m.Configurar(src, pista, posicionado ? pos : Vector3.zero,
                     posicionado, GameOptions.VolumenEfectos * Mathf.Clamp01(escala));
        src.clip = clip;
        src.Play();
    }

    // Loop identificado por clave. Llamalo cada frame mientras el sonido deba sonar:
    // la primera vez arranca, las siguientes solo actualizan posicion y volumen.
    public static void Bucle(string clave, Func<AudioCatalog, AudioCatalog.Pista> sel,
                             Vector3 pos, float escala = 1f)
    {
        var m = Instance;
        if (m == null || m._cat == null || string.IsNullOrEmpty(clave) || sel == null) return;

        var pista = sel(m._cat);
        if (pista == null || pista.Vacia) return;

        if (!m._bucles.TryGetValue(clave, out var src) || src == null)
        {
            src = m.TomarBucleLibre();
            if (src == null) return;                   // sin fuentes de loop: se ignora
            m._bucles[clave] = src;

            var clip = pista.Elegir();
            if (clip == null) return;
            src.clip = clip;
            m.Configurar(src, pista, pos, pista.espacial > 0f,
                         GameOptions.VolumenEfectos * Mathf.Clamp01(escala));
            src.Play();
            return;
        }

        // Ya suena: solo refrescar (sin reiniciar el clip ni el pitch aleatorio).
        src.transform.position = pos;
        src.volume = pista.volumen * GameOptions.VolumenEfectos * Mathf.Clamp01(escala);
    }

    public static void PararBucle(string clave)
    {
        var m = Instance;
        if (m == null || string.IsNullOrEmpty(clave)) return;
        if (!m._bucles.TryGetValue(clave, out var src)) return;

        if (src != null) { src.Stop(); src.clip = null; }
        m._bucles.Remove(clave);
    }

    // Cambia la musica de fondo con crossfade. Si ya suena esa misma pista, no hace nada
    // (asi se puede llamar sin miedo desde un cambio de escena o al reanudar).
    public static void Musica(Func<AudioCatalog, AudioCatalog.Pista> sel, float fade = 1.5f)
    {
        var m = Instance;
        if (m == null || m._cat == null || m._musA == null || sel == null) return;

        var pista = sel(m._cat);
        if (pista == null || pista.Vacia) { PararMusica(fade); return; }

        var clip = pista.Elegir();
        if (clip == null || clip == m._musActual) return;

        var entra = m._usandoA ? m._musB : m._musA;
        entra.clip   = clip;
        entra.volume = 0f;
        m.Configurar(entra, pista, Vector3.zero, false, 0f);
        entra.Play();

        m._musActual   = clip;
        m._usandoA     = !m._usandoA;
        m._musObjetivo = pista.volumen;
        m._musFade     = Mathf.Max(0.01f, fade);
    }

    public static void PararMusica(float fade = 1.5f)
    {
        var m = Instance;
        if (m == null) return;
        m._musActual   = null;
        m._musObjetivo = 0f;
        m._musFade     = Mathf.Max(0.01f, fade);
    }

    // US-11.3: capa que sube con el nivel de peligro. La llama el watcher con
    // TensionSystem.Value01. Comparte el bus de MUSICA, no el de efectos.
    public static void CapaTension(float t01)
    {
        var m = Instance;
        if (m == null) return;
        m._tension01 = Mathf.Clamp01(t01);
    }

    // ─────────────────────────────────────────────────────── Interno

    // Aplica al AudioSource la configuracion de la pista. `bus` ya viene multiplicado por
    // el volumen del canal (musica o efectos); el maestro lo aplica AudioListener.volume.
    private void Configurar(AudioSource src, AudioCatalog.Pista p, Vector3 pos,
                            bool posicionado, float bus)
    {
        src.transform.position = pos;
        src.volume       = MezclarVolumen(p.volumen, bus);
        src.pitch        = p.variacionTono > 0f
                         ? 1f + UnityEngine.Random.Range(-p.variacionTono, p.variacionTono)
                         : 1f;
        src.spatialBlend = posicionado ? p.espacial : 0f;
        src.minDistance  = Mathf.Max(0.1f, p.distanciaMin);
        src.maxDistance  = Mathf.Max(src.minDistance + 0.1f, p.distanciaMax);
        src.spread       = p.apertura;
    }

    // Formula de mezcla, aislada para poder testearla sin Unity.
    // El maestro NO entra aca: lo aplica el motor via AudioListener.volume.
    public static float MezclarVolumen(float volumenPista, float bus) =>
        Mathf.Clamp01(volumenPista) * Mathf.Clamp01(bus);

    // Toma una fuente libre del pool. Si estan las 24 ocupadas reusa la que arranco hace
    // mas tiempo — nunca un loop ni la musica, que tienen sus propias fuentes.
    private AudioSource TomarDelPool()
    {
        for (int i = 0; i < _pool.Length; i++)
        {
            if (_pool[i] != null && !_pool[i].isPlaying)
            {
                _poolDesde[i] = Time.unscaledTime;
                return _pool[i];
            }
        }

        int viejo = 0;
        for (int i = 1; i < _pool.Length; i++)
            if (_poolDesde[i] < _poolDesde[viejo]) viejo = i;

        _poolDesde[viejo] = Time.unscaledTime;
        return _pool[viejo];
    }

    private AudioSource TomarBucleLibre()
    {
        for (int i = 0; i < _buclesLibres.Length; i++)
        {
            var s = _buclesLibres[i];
            if (s != null && !s.isPlaying && !_bucles.ContainsValue(s)) return s;
        }
        return null;
    }

    private void Update()
    {
        if (_cat == null) return;

        // Fades de musica y capa de tension. Es lo unico que corre por frame y solo mueve
        // dos floats; el watcher de entidades vive en AudioEventWatcher.
        float dt = Time.unscaledDeltaTime;
        float busMusica = GameOptions.VolumenMusica;

        var entra = _usandoA ? _musA : _musB;
        var sale  = _usandoA ? _musB : _musA;

        if (entra != null)
        {
            float objetivo = MezclarVolumen(_musObjetivo, busMusica);
            entra.volume = Mathf.MoveTowards(entra.volume, objetivo, dt / _musFade);
        }
        if (sale != null && sale.volume > 0f)
        {
            sale.volume = Mathf.MoveTowards(sale.volume, 0f, dt / Mathf.Max(0.01f, _musFade));
            if (sale.volume <= 0f && sale.isPlaying) { sale.Stop(); sale.clip = null; }
        }

        ActualizarCapaTension(busMusica);
    }

    // La capa de tension arranca y para sola segun haya o no tension: no hace falta que
    // nadie la administre desde afuera.
    private void ActualizarCapaTension(float busMusica)
    {
        if (_tension == null) return;

        var p = _cat.capaTension;
        if (p == null || p.Vacia)
        {
            if (_tension.isPlaying) _tension.Stop();
            return;
        }

        float objetivo = MezclarVolumen(p.volumen, busMusica) * _tension01;

        if (objetivo <= 0.001f)
        {
            if (_tension.isPlaying && _tension.volume <= 0.001f) _tension.Stop();
            else _tension.volume = Mathf.MoveTowards(_tension.volume, 0f, Time.unscaledDeltaTime);
            return;
        }

        if (!_tension.isPlaying)
        {
            if (_tension.clip == null) _tension.clip = p.Elegir();
            if (_tension.clip == null) return;
            _tension.spatialBlend = 0f;
            _tension.volume = 0f;
            _tension.Play();
        }
        _tension.volume = Mathf.MoveTowards(_tension.volume, objetivo, Time.unscaledDeltaTime);
    }

    // NOTA: no hace falta un "RefrescarVolumenes" como el del chat de voz. Mover un slider
    // se oye en el acto solo: la musica y la capa de tension recalculan su objetivo contra
    // GameOptions en cada Update, los loops los reescribe el watcher cada frame, y los
    // one-shots duran menos que el gesto de mover el slider.
}
