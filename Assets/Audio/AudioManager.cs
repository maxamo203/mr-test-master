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
//   2 musica (crossfade) + 1 capa de tension + 8 loops + 23 one-shots +
//   1 evento ambiental dedicado + 3 del chat de voz.
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

    private const int CantOneShots = 23;
    private const int CantBucles   = 8;
    private const int CantTransitorios = 4;

    private AudioCatalog _cat;

    // Pool de one-shots + el instante en que arranco cada uno (para reusar el mas viejo).
    private AudioSource[] _pool;
    private float[]       _poolDesde;

    // Loops con clave ("sorken_pasos", "veleth_persecucion", ...). Idempotentes: volver a
    // pedirlos solo actualiza posicion y volumen, no reinicia el sonido.
    private readonly Dictionary<string, AudioSource> _bucles = new();
    private AudioSource[] _buclesLibres;

    // Voces con identidad y cancelacion suave (reacciones/fases). A diferencia de un
    // one-shot del pool, pueden apagarse cuando la animacion que las justifica termina.
    private AudioSource[] _transitoriosLibres;
    private readonly Dictionary<string, AudioSource> _transitorios = new();
    private readonly Dictionary<string, float> _transitorioFade = new();

    // Musica: dos fuentes para poder hacer crossfade entre pistas.
    private AudioSource _musA, _musB;
    private bool        _usandoA;
    private AudioClip   _musActual;
    private float       _musObjetivo, _musFade;
    private float       _musDuck = 1f;

    // Captura local y stinger usan voces propias: no pueden ser robados por un paso y se
    // cancelan limpiamente al reintentar/salir. El fondo permanece en sus fuentes.
    private AudioSource _deathVoice, _deathStinger;
    private float _deathStingerStartsAt = -1f;

    // Resonancia de Arbmos: tres voces dedicadas permiten entrada -> loop -> salida con
    // cruce real, incluso si la luz se apaga a mitad del sostenido.
    private AudioSource _resonanceIntro, _resonanceLoop, _resonanceExit;
    private bool _resonanceRequested;
    private bool _resonanceActive;
    private float _resonanceTarget;
    private float _resonanceLoopStartAt;
    private Vector3 _resonancePosition;
    private const float ResonanceCrossfade = 0.15f;

    // Capa de tension (US-11.3): loop propio cuyo volumen sigue a TensionSystem.
    private AudioSource _tension;
    private float       _tension01;

    // Fuente exclusiva para golpes/manija. Permite impedir solapamientos y cortar con
    // fade una falsa alarma si empieza una amenaza real.
    private AudioSource _ambienteEvento;
    private float _ambienteEventoObjetivo;
    private float _ambienteEventoFade = 0.12f;

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
        go.AddComponent<AmbientAudioScheduler>();
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
        _ambienteEvento = NuevaFuente("Evento ambiental", loop: false);
        _deathVoice = NuevaFuente("Captura local", loop: false);
        _deathStinger = NuevaFuente("Stinger derrota", loop: false);
        _resonanceIntro = NuevaFuente("Arbmos resonancia entrada", loop: false);
        _resonanceLoop = NuevaFuente("Arbmos resonancia sostenida", loop: true);
        _resonanceExit = NuevaFuente("Arbmos resonancia salida", loop: false);

        _pool      = new AudioSource[CantOneShots];
        _poolDesde = new float[CantOneShots];
        for (int i = 0; i < CantOneShots; i++)
            _pool[i] = NuevaFuente("OneShot " + i, loop: false);

        _buclesLibres = new AudioSource[CantBucles];
        for (int i = 0; i < CantBucles; i++)
            _buclesLibres[i] = NuevaFuente("Bucle " + i, loop: true);

        _transitoriosLibres = new AudioSource[CantTransitorios];
        for (int i = 0; i < CantTransitorios; i++)
            _transitoriosLibres[i] = NuevaFuente("Fase " + i, loop: false);
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

    // One-shot con eleccion sin repeticion inmediata. Devuelve el indice compacto usado
    // para que el llamador lo recuerde por entidad.
    public static int SonarSinRepetir(Func<AudioCatalog, AudioCatalog.Pista> sel,
                                      Vector3 pos, int anterior, float escala = 1f,
                                      float ganancia = 1f)
    {
        var m = Instance;
        if (m == null || m._cat == null || m._pool == null || sel == null) return anterior;
        var pista = sel(m._cat);
        if (pista == null || pista.Vacia) return anterior;

        int indice = pista.ElegirIndiceSinRepetir(anterior);
        var clip = pista.ClipValido(indice);
        if (clip == null) return anterior;
        m.ReproducirClip(pista, clip, pos, true, escala, ganancia);
        return indice;
    }

    private static void Reproducir(Func<AudioCatalog, AudioCatalog.Pista> sel,
                                   Vector3 pos, bool posicionado, float escala)
    {
        var m = Instance;
        if (m == null || m._cat == null || m._pool == null || sel == null) return;

        var pista = sel(m._cat);
        if (pista == null || pista.Vacia) return;      // slot vacio: silencio, sin log

        var clip = pista.Elegir();
        if (clip == null) return;

        m.ReproducirClip(pista, clip, pos, posicionado, escala);
    }

    private void ReproducirClip(AudioCatalog.Pista pista, AudioClip clip, Vector3 pos,
                                bool posicionado, float escala, float ganancia = 1f)
    {
        var src = TomarDelPool();
        if (src == null || clip == null) return;
        Configurar(src, pista, posicionado ? pos : Vector3.zero,
                   posicionado, GameOptions.VolumenEfectos * Mathf.Clamp01(escala));
        ganancia = Mathf.Max(0f, ganancia);
        if (Mathf.Approximately(ganancia, 1f))
        {
            src.clip = clip;
            src.Play();
        }
        else
        {
            // PlayOneShot admite una ganancia adicional mayor a 1. Se usa cuando una
            // pista ya esta al techo normal de AudioSource.volume pero necesita refuerzo.
            src.clip = null;
            src.PlayOneShot(clip, ganancia);
        }
    }

    // Evento ambiental compartido. `indice` lo elige el host y todos los peers reciben
    // la misma variante. Una llamada nueva reemplaza la anterior, nunca se superponen.
    public static bool EventoAmbiental(Func<AudioCatalog, AudioCatalog.Pista> sel,
                                       int indice, Vector3 pos)
    {
        var m = Instance;
        if (m == null || m._cat == null || m._ambienteEvento == null || sel == null) return false;
        var pista = sel(m._cat);
        var clip = pista?.ClipValido(indice);
        if (clip == null) return false;

        var src = m._ambienteEvento;
        src.Stop();
        m.Configurar(src, pista, pos, true, GameOptions.VolumenEfectos);
        src.clip = clip;
        m._ambienteEventoObjetivo = src.volume;
        src.Play();
        return true;
    }

    public static void PararEventoAmbiental(float fade = 0.12f)
    {
        var m = Instance;
        if (m == null || m._ambienteEvento == null) return;
        m._ambienteEventoObjetivo = 0f;
        m._ambienteEventoFade = Mathf.Max(0.01f, fade);
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

    // One-shot de fase cancelable. Devuelve la duracion elegida para programar la
    // siguiente emision desde el final real del clip, no desde un timer de gameplay.
    public static float TransitorioSinRepetir(string clave,
        Func<AudioCatalog, AudioCatalog.Pista> sel, Vector3 pos, int anterior,
        out int elegido, float escala = 1f)
    {
        elegido = anterior;
        var m = Instance;
        if (m == null || m._cat == null || string.IsNullOrEmpty(clave) || sel == null)
            return 0f;
        var pista = sel(m._cat);
        if (pista == null || pista.Vacia) return 0f;

        if (m._transitorios.TryGetValue(clave, out var existente) && existente != null &&
            existente.isPlaying)
        {
            existente.transform.position = pos;
            return 0f;
        }

        elegido = pista.ElegirIndiceSinRepetir(anterior);
        var clip = pista.ClipValido(elegido);
        if (clip == null) return 0f;
        var src = existente != null ? existente : m.TomarTransitorioLibre();
        if (src == null) return 0f;
        m._transitorios[clave] = src;
        m._transitorioFade.Remove(clave);
        src.Stop();
        src.clip = clip;
        m.Configurar(src, pista, pos, pista.espacial > 0f,
                     GameOptions.VolumenEfectos * Mathf.Clamp01(escala));
        src.Play();
        return clip.length / Mathf.Max(0.01f, Mathf.Abs(src.pitch));
    }

    public static void PararTransitorio(string clave, float fade = 0.12f)
    {
        var m = Instance;
        if (m == null || string.IsNullOrEmpty(clave) ||
            !m._transitorios.TryGetValue(clave, out var src) || src == null) return;
        m._transitorioFade[clave] = Mathf.Max(0.01f, fade);
    }

    public static bool TransitorioActivo(string clave)
    {
        var m = Instance;
        return m != null && m._transitorios.TryGetValue(clave, out var src) &&
               src != null && src.isPlaying && !m._transitorioFade.ContainsKey(clave);
    }

    public static void PosicionarTransitorio(string clave, Vector3 pos)
    {
        var m = Instance;
        if (m != null && m._transitorios.TryGetValue(clave, out var src) && src != null)
            src.transform.position = pos;
    }

    // Capa individual de Arbmos. Se llama cada frame mientras la aparicion visible exista;
    // `encendida` representa la linterna local, no una regla de daño.
    public static void ResonanciaArbmos(Vector3 pos, bool encendida)
    {
        var m = Instance;
        if (m == null || m._cat == null) return;
        m._resonancePosition = pos;
        if (encendida && !m._resonanceRequested) m.IniciarResonancia();
        else if (!encendida && m._resonanceRequested) m.FinalizarResonancia(true);
        m._resonanceRequested = encendida;
    }

    public static void DetenerResonanciaArbmos(bool salidaNatural)
    {
        var m = Instance;
        if (m == null) return;
        if (m._resonanceRequested || m._resonanceActive)
            m.FinalizarResonancia(salidaNatural);
        m._resonanceRequested = false;
    }

    // Presentacion local de muerte. El tipo del asesino decide la vocalizacion; el coro
    // breve se programa una vez y nunca reemplaza la cama musical.
    public static void PresentarMuerte(byte killerType)
    {
        var m = Instance;
        if (m == null || m._cat == null || m._deathVoice == null) return;
        m.CancelarPresentacionMuerteInterna();

        AudioCatalog.Pista causa = killerType switch
        {
            EntityTypeIds.Sorken => m._cat.sorkenCapturaFatal,
            EntityTypeIds.Veleth => m._cat.velethCapturaFatal,
            EntityTypeIds.Arbmos => m._cat.arbmosAgarreLetal,
            _ => null,
        };
        var causeClip = causa?.Elegir();
        if (causeClip != null)
        {
            m._deathVoice.clip = causeClip;
            m.Configurar(m._deathVoice, causa, Vector3.zero, false,
                         GameOptions.VolumenEfectos);
            m._deathVoice.Play();
        }

        var stinger = m._cat.gameOverStinger;
        var stingerClip = stinger?.Elegir();
        if (stingerClip == null) return;
        const float delay = 0.55f; // conserva el ataque de la captura sin retrasar la UI
        m._deathStinger.clip = stingerClip;
        m.Configurar(m._deathStinger, stinger, Vector3.zero, false,
                     GameOptions.VolumenEfectos);
        m._deathStinger.PlayDelayed(delay);
        m._deathStingerStartsAt = Time.unscaledTime + delay;
    }

    public static void CancelarPresentacionMuerte()
    {
        Instance?.CancelarPresentacionMuerteInterna();
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
        src.volume       = VariarIntensidad(MezclarVolumen(p.volumen, bus),
                                            p.variacionIntensidad,
                                            UnityEngine.Random.value);
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

    // El volumen de la pista representa su techo. La variacion solo puede suavizar el
    // disparo, nunca superar la mezcla calibrada ni producir clipping.
    public static float VariarIntensidad(float volumenBase, float variacion, float azar01)
    {
        float techo = Mathf.Clamp01(volumenBase);
        float rango = Mathf.Clamp(variacion, 0f, 0.5f);
        return techo * Mathf.Lerp(1f - rango, 1f, Mathf.Clamp01(azar01));
    }

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
            float objetivo = MezclarVolumen(_musObjetivo, busMusica) * _musDuck;
            entra.volume = Mathf.MoveTowards(entra.volume, objetivo, dt / _musFade);
        }
        if (sale != null && sale.volume > 0f)
        {
            sale.volume = Mathf.MoveTowards(sale.volume, 0f, dt / Mathf.Max(0.01f, _musFade));
            if (sale.volume <= 0f && sale.isPlaying) { sale.Stop(); sale.clip = null; }
        }

        ActualizarCapaTension(busMusica);
        ActualizarEventoAmbiental();
        ActualizarTransitorios();
        ActualizarResonancia();
        ActualizarMuerte();
    }

    private AudioSource TomarTransitorioLibre()
    {
        for (int i = 0; i < _transitoriosLibres.Length; i++)
        {
            var src = _transitoriosLibres[i];
            if (src != null && !src.isPlaying && !_transitorios.ContainsValue(src)) return src;
        }
        return null;
    }

    private void ActualizarTransitorios()
    {
        if (_transitorios.Count == 0) return;
        var borrar = new List<string>();
        foreach (var pair in _transitorios)
        {
            var src = pair.Value;
            if (src == null) { borrar.Add(pair.Key); continue; }
            if (_transitorioFade.TryGetValue(pair.Key, out float fade))
            {
                src.volume = Mathf.MoveTowards(src.volume, 0f,
                    Time.unscaledDeltaTime / Mathf.Max(0.01f, fade));
                if (src.volume <= 0.001f) { src.Stop(); src.clip = null; borrar.Add(pair.Key); }
            }
            else if (!src.isPlaying) { src.clip = null; borrar.Add(pair.Key); }
        }
        foreach (string key in borrar)
        {
            _transitorios.Remove(key);
            _transitorioFade.Remove(key);
        }
    }

    private void IniciarResonancia()
    {
        var entrada = _cat.arbmosResonanciaEntrada;
        var sostenido = _cat.arbmosResonanciaSostenida;
        var introClip = entrada?.Elegir();
        var loopClip = sostenido?.Elegir();
        if (introClip == null || loopClip == null) return;

        _resonanceExit.Stop();
        _resonanceIntro.Stop();
        _resonanceLoop.Stop();
        _resonanceIntro.clip = introClip;
        _resonanceLoop.clip = loopClip;
        Configurar(_resonanceIntro, entrada, _resonancePosition, true,
                   GameOptions.VolumenEfectos);
        Configurar(_resonanceLoop, sostenido, _resonancePosition, true, 0f);
        _resonanceTarget = MezclarVolumen(sostenido.volumen, GameOptions.VolumenEfectos);
        _resonanceIntro.Play();
        _resonanceLoopStartAt = Time.unscaledTime + Mathf.Max(0f, introClip.length - ResonanceCrossfade);
        _resonanceActive = true;
    }

    private void FinalizarResonancia(bool salidaNatural)
    {
        if (!_resonanceActive) return;
        _resonanceActive = false;
        if (salidaNatural)
        {
            var salida = _cat.arbmosResonanciaSalida;
            var clip = salida?.Elegir();
            if (clip != null)
            {
                _resonanceExit.Stop();
                _resonanceExit.clip = clip;
                Configurar(_resonanceExit, salida, _resonancePosition, true,
                           GameOptions.VolumenEfectos);
                _resonanceExit.Play();
            }
        }
    }

    private void ActualizarResonancia()
    {
        if (_resonanceIntro == null) return;
        _resonanceIntro.transform.position = _resonancePosition;
        _resonanceLoop.transform.position = _resonancePosition;
        _resonanceExit.transform.position = _resonancePosition;

        if (_resonanceActive && !_resonanceLoop.isPlaying &&
            Time.unscaledTime >= _resonanceLoopStartAt)
            _resonanceLoop.Play();

        float step = Time.unscaledDeltaTime / ResonanceCrossfade;
        float target = _resonanceActive && _resonanceLoop.isPlaying ? _resonanceTarget : 0f;
        _resonanceLoop.volume = Mathf.MoveTowards(_resonanceLoop.volume, target, step);
        if (_resonanceActive && _resonanceLoop.isPlaying && _resonanceIntro.isPlaying)
        {
            _resonanceIntro.volume = Mathf.MoveTowards(_resonanceIntro.volume, 0f, step);
            if (_resonanceIntro.volume <= 0.001f) _resonanceIntro.Stop();
        }
        if (!_resonanceActive)
        {
            _resonanceIntro.volume = Mathf.MoveTowards(_resonanceIntro.volume, 0f, step);
            if (_resonanceIntro.volume <= 0.001f && _resonanceIntro.isPlaying) _resonanceIntro.Stop();
            if (_resonanceLoop.volume <= 0.001f && _resonanceLoop.isPlaying) _resonanceLoop.Stop();
        }
    }

    private void ActualizarMuerte()
    {
        bool stingerActivo = _deathStinger != null && _deathStinger.isPlaying &&
                             Time.unscaledTime >= _deathStingerStartsAt;
        float target = stingerActivo ? 0.42f : 1f;
        float seconds = stingerActivo ? 0.25f : 1.5f;
        _musDuck = Mathf.MoveTowards(_musDuck, target,
            Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds));
        if (!stingerActivo && _deathStingerStartsAt >= 0f &&
            Time.unscaledTime > _deathStingerStartsAt)
            _deathStingerStartsAt = -1f;
    }

    private void CancelarPresentacionMuerteInterna()
    {
        if (_deathVoice != null) { _deathVoice.Stop(); _deathVoice.clip = null; }
        if (_deathStinger != null) { _deathStinger.Stop(); _deathStinger.clip = null; }
        _deathStingerStartsAt = -1f;
        _musDuck = 1f;
    }

    private void ActualizarEventoAmbiental()
    {
        if (_ambienteEvento == null || !_ambienteEvento.isPlaying) return;
        if (_ambienteEventoObjetivo > 0f)
        {
            _ambienteEvento.volume = _ambienteEventoObjetivo;
            return;
        }

        _ambienteEvento.volume = Mathf.MoveTowards(
            _ambienteEvento.volume, 0f,
            Time.unscaledDeltaTime / Mathf.Max(0.01f, _ambienteEventoFade));
        if (_ambienteEvento.volume <= 0.001f)
        {
            _ambienteEvento.Stop();
            _ambienteEvento.clip = null;
        }
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
