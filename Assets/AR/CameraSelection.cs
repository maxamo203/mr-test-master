using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Con QUÉ cámara (y en qué modo de captura) corre el AR.
//
// Por qué existe: en Cardboard el passthrough es lo ÚNICO que el jugador ve del cuarto
// real, así que el ángulo de visión de la cámara ES el campo de visión del juego. Los
// celulares publican varios modos de captura y el que ARCore/ARKit elige por defecto no
// siempre es el más ancho: en muchos equipos el modo 4:3 ve bastante más cuarto que el
// 16:9 por defecto, que es un recorte del mismo sensor.
//
// LÍMITE REAL, importante para no prometer de más: ni ARCore ni ARKit dejan elegir el
// lente ULTRA gran angular para el tracking — el mundo se trackea siempre con el lente
// principal trasero, y AR Foundation no expone los otros sensores. Lo que sí se puede
// elegir son las XRCameraConfiguration que el equipo publica, que difieren en resolución,
// fps y —lo que nos interesa— recorte del sensor. Así que "más gran angular" acá significa
// "el modo más ancho que ESTE equipo expone", no un lente distinto.
//
// La cámara FRONTAL queda afuera a propósito (el juego mira al cuarto, no al jugador):
// CameraSelectionRunner fuerza requestedFacingDirection = World, así que las
// configuraciones que se enumeran son siempre las de la cámara trasera.
//
// El ángulo de cada modo NO viene en la configuración: hay que APLICARLA y leer las
// intrínsecas de la cámara. Por eso los ángulos se van aprendiendo — el modo en uso se
// mide solo, y el resto sale de "MEDIR TODOS", que aplica cada modo un instante. El
// resultado se cachea en PlayerPrefs por dispositivo, así el menú principal (donde no hay
// sesión AR viva) también puede ofrecer la lista.
public static class CameraSelection
{
    // Valores especiales de la selección (el resto de las claves son modos concretos).
    public const string Predeterminada = "";      // la que elige el sistema: lo de siempre
    public const string MasAncha       = "auto";  // la más ancha de las YA medidas

    // Un modo de captura tal como se le muestra al jugador. `clave` es la firma estable
    // ("1600x1200@30"): los IntPtr de XRCameraConfiguration no sobreviven a la sesión,
    // así que es lo único que se puede persistir.
    [Serializable]
    public struct Modo
    {
        public string clave;
        public int    ancho, alto, fps;
        public float  fovH, fovV;   // grados; 0 = todavía no medido en este dispositivo

        public bool   Medido   => fovH > 0f && fovV > 0f;
        public float  Amplitud => Medido ? Diagonal(fovH, fovV) : 0f;

        public string Titulo => fps > 0 ? $"{ancho}x{alto} · {fps} FPS" : $"{ancho}x{alto}";
        public string Angulo => Medido ? $"{Mathf.RoundToInt(fovH)}° x {Mathf.RoundToInt(fovV)}°"
                                       : "sin medir";
    }

    // ── Estado ────────────────────────────────────────────────────────────

    private static readonly List<Modo> _modos = new();
    private static ARCameraManager _mgr;

    // Modos enumerados de la cámara viva, del más ancho al más angosto (los no medidos,
    // al final). Vacío mientras no haya sesión AR — para los menús, ver ModosConocidos.
    public static IReadOnlyList<Modo> Modos => _modos;

    // ¿Hay una cámara AR viva en esta escena? Sin ella se puede elegir (queda guardado)
    // pero no aplicar ni medir.
    public static bool CamaraViva => _mgr != null && _mgr.subsystem != null && _mgr.subsystem.running;

    // Clave del modo en uso ahora mismo ("" si el subsistema no expone configuraciones).
    public static string ClaveActiva { get; private set; } = Predeterminada;

    // Ángulo medido de lo que se está viendo ahora (0 si todavía no se pudo leer).
    public static float FovActualH { get; private set; }
    public static float FovActualV { get; private set; }

    // ¿Está corriendo el barrido de medición? La UI lo usa para bloquear la pantalla.
    public static bool Midiendo { get; internal set; }

    // Progreso del barrido, para el cartel ("3 de 5").
    public static int MedidoN     { get; internal set; }
    public static int MedidoTotal { get; internal set; }

    // Cambió la lista, la selección o una medición. Sólo para quien no redibuja solo
    // (la UI es IMGUI y se redibuja sola en cada frame).
    public static event Action OnCambio;

    // ── Selección (persistente) ───────────────────────────────────────────

    // Qué modo quiere el jugador: Predeterminada, MasAncha, o la clave de uno concreto.
    // Se aplica en caliente si hay cámara viva; si no, queda para la próxima sesión AR.
    public static string Seleccion
    {
        get => GameOptions.CamaraModo;
        set
        {
            string v = value ?? Predeterminada;
            if (v == GameOptions.CamaraModo) return;
            GameOptions.CamaraModo = v;
            AplicarSeleccion();
            OnCambio?.Invoke();
        }
    }

    // La clave que hay que aplicar de verdad, resolviendo los valores especiales.
    // Devuelve null cuando no hay que tocar nada (predeterminada, o "más ancha" sin
    // ninguna medición todavía: sin datos, cambiar a ciegas sería peor que no hacer nada).
    public static string ClaveElegida()
    {
        string sel = Seleccion;
        if (sel == Predeterminada) return null;
        if (sel == MasAncha)       return NullSiVacio(ClaveMasAncha());
        return sel;
    }

    // Nombre para mostrar de la selección actual (menús).
    public static string NombreSeleccion()
    {
        string sel = Seleccion;
        if (sel == Predeterminada) return "PREDETERMINADA";
        if (sel == MasAncha)       return "MÁS AMPLIA (AUTO)";
        var conocidos = ModosConocidos;
        for (int i = 0; i < conocidos.Count; i++)
            if (conocidos[i].clave == sel) return conocidos[i].Titulo;
        return sel;   // elegida en otra sesión y todavía no enumerada acá
    }

    // La clave del modo más ancho MEDIDO (para marcarlo en la lista). "" si no hay ninguno.
    public static string ClaveMasAncha()
    {
        string mejor = ""; float amp = 0f;
        var conocidos = ModosConocidos;
        for (int i = 0; i < conocidos.Count; i++)
            if (conocidos[i].Medido && conocidos[i].Amplitud > amp)
            { amp = conocidos[i].Amplitud; mejor = conocidos[i].clave; }
        return mejor;
    }

    // ── Enumeración y aplicación ──────────────────────────────────────────

    // Llamado por el runner cuando la cámara AR empieza a entregar frames.
    internal static void CamaraLista(ARCameraManager mgr)
    {
        _mgr = mgr;
        Refrescar();
        AplicarSeleccion();
    }

    internal static void CamaraMuerta(ARCameraManager mgr)
    {
        if (_mgr != mgr) return;
        _mgr = null;
        _modos.Clear();
        FovActualH = FovActualV = 0f;
    }

    // Enumera las configuraciones vivas y las fusiona con el caché del dispositivo:
    // las mediciones sobreviven, y los modos que el equipo ya no publica se descartan.
    internal static void Refrescar()
    {
        if (!CamaraViva) return;

        var cache = new Dictionary<string, Modo>();
        foreach (var m in _modos)     cache[m.clave] = m;
        foreach (var m in LeerCache()) if (!cache.ContainsKey(m.clave)) cache[m.clave] = m;

        _modos.Clear();
        var vistos = new HashSet<string>();
        using (var cfgs = _mgr.GetConfigurations(Allocator.Temp))
        {
            for (int i = 0; i < cfgs.Length; i++)
            {
                var c = cfgs[i];
                string clave = ClaveDe(c);
                // Varias configuraciones pueden coincidir en resolución y fps y diferir sólo
                // en cosas que al jugador no le dicen nada (soporte de sensor de profundidad):
                // se queda la primera, si no la lista sería una pared de duplicados.
                if (!vistos.Add(clave)) continue;

                var modo = new Modo { clave = clave, ancho = c.width, alto = c.height,
                                      fps = c.framerate ?? 0 };
                if (cache.TryGetValue(clave, out var prev)) { modo.fovH = prev.fovH; modo.fovV = prev.fovV; }
                _modos.Add(modo);
            }
        }

        ClaveActiva = ClaveDe(_mgr.currentConfiguration);
        Ordenar();
        GuardarCache();
    }

    // Aplica la selección del jugador a la cámara viva. No hace nada si ya está puesta,
    // si el modo no existe en este equipo, o si el subsistema no soporta configuraciones.
    internal static void AplicarSeleccion()
    {
        if (!CamaraViva) return;

        string clave = ClaveElegida();
        if (string.IsNullOrEmpty(clave)) return;
        if (clave == ClaveDe(_mgr.currentConfiguration)) return;

        using (var cfgs = _mgr.GetConfigurations(Allocator.Temp))
        {
            for (int i = 0; i < cfgs.Length; i++)
            {
                if (ClaveDe(cfgs[i]) != clave) continue;
                Poner(cfgs[i]);
                return;
            }
        }
    }

    // Cambia la configuración de la cámara. Devuelve false si el equipo no deja.
    internal static bool Poner(XRCameraConfiguration cfg)
    {
        if (!CamaraViva) return false;
        try
        {
            _mgr.currentConfiguration = cfg;
            ClaveActiva = ClaveDe(cfg);
            FovActualH  = FovActualV = 0f;      // hay que volver a medir lo que se ve
            CameraSelectionRunner.PedirMedicion();
            return true;
        }
        catch (Exception e)
        {
            // NotSupported / InvalidOperation: el equipo no deja cambiarla (o no en caliente).
            Debug.LogWarning($"[CameraSelection] no se pudo aplicar {ClaveDe(cfg)}: {e.Message}");
            return false;
        }
    }

    // ── Mediciones ────────────────────────────────────────────────────────

    // Ángulo de visión de lo que la cámara entrega AHORA, a partir de las intrínsecas:
    // fov = 2*atan(res / (2*focal)), por eje.
    internal static bool TryFov(ARCameraManager mgr, out float fovH, out float fovV, out Vector2Int res)
    {
        fovH = fovV = 0f; res = default;
        if (mgr == null || !mgr.TryGetIntrinsics(out var k)) return false;
        if (k.focalLength.x <= 0f || k.focalLength.y <= 0f) return false;
        if (k.resolution.x  <= 0  || k.resolution.y  <= 0)  return false;
        res  = k.resolution;
        fovH = 2f * Mathf.Atan(k.resolution.x / (2f * k.focalLength.x)) * Mathf.Rad2Deg;
        fovV = 2f * Mathf.Atan(k.resolution.y / (2f * k.focalLength.y)) * Mathf.Rad2Deg;
        return true;
    }

    // Guarda la medición de un modo (y la del "modo actual" si es el que está puesto).
    internal static void RegistrarMedicion(string clave, float fovH, float fovV)
    {
        if (fovH <= 0f || fovV <= 0f) return;

        for (int i = 0; i < _modos.Count; i++)
        {
            if (_modos[i].clave != clave) continue;
            var m = _modos[i]; m.fovH = fovH; m.fovV = fovV; _modos[i] = m;
            break;
        }
        if (clave == ClaveActiva) { FovActualH = fovH; FovActualV = fovV; }
        Ordenar();
        GuardarCache();
        OnCambio?.Invoke();
    }

    // ¿Se puede correr el barrido? Aplicar cada modo reinicia la captura, así que en
    // medio de una noche no se ofrece: cortaría el tracking con el Sorken encima.
    public static bool PuedeMedir =>
        CamaraViva && _modos.Count > 1 && !Midiendo &&
        (NetworkManager.Instance == null || !NetworkManager.Instance.GameStarted);

    // Mide todos los modos, uno por uno. Lo ejecuta el runner (necesita corrutina).
    public static void MedirTodos() => CameraSelectionRunner.IniciarBarrido();

    internal static void Notificar() => OnCambio?.Invoke();

    // ── Utilidades ────────────────────────────────────────────────────────

    // Ángulo diagonal a partir de los dos ejes: es el número que ordena "cuál ve más".
    // tan(d/2)^2 = tan(h/2)^2 + tan(v/2)^2.
    public static float Diagonal(float fovH, float fovV)
    {
        float th = Mathf.Tan(fovH * 0.5f * Mathf.Deg2Rad);
        float tv = Mathf.Tan(fovV * 0.5f * Mathf.Deg2Rad);
        return 2f * Mathf.Atan(Mathf.Sqrt(th * th + tv * tv)) * Mathf.Rad2Deg;
    }

    internal static string ClaveDe(XRCameraConfiguration? cfg) =>
        cfg.HasValue ? ClaveDe(cfg.Value) : Predeterminada;

    internal static string ClaveDe(XRCameraConfiguration cfg) =>
        $"{cfg.width}x{cfg.height}@{cfg.framerate ?? 0}";

    private static string NullSiVacio(string s) => string.IsNullOrEmpty(s) ? null : s;

    // Del más ancho al más angosto; los no medidos van al final, por resolución.
    private static void Ordenar()
    {
        _modos.Sort((a, b) =>
        {
            if (a.Medido != b.Medido) return a.Medido ? -1 : 1;
            if (a.Medido) return b.Amplitud.CompareTo(a.Amplitud);
            return ((long)b.ancho * b.alto).CompareTo((long)a.ancho * a.alto);
        });
    }

    // ── Caché en disco (PlayerPrefs, por dispositivo) ─────────────────────
    // Sirve para que el MENÚ PRINCIPAL —donde no hay sesión AR— pueda mostrar la lista y
    // los ángulos ya medidos. Nada de esto viaja por red ni se guarda con el escaneo: es
    // una preferencia del aparato, igual que PuntosAncla o CalidadAR.

    [Serializable] private class Catalogo { public List<Modo> modos = new(); }

    private static List<Modo> _cacheLeida;

    // Modos conocidos aunque no haya cámara viva (menú principal).
    public static IReadOnlyList<Modo> ModosConocidos
    {
        get
        {
            if (_modos.Count > 0) return _modos;
            _cacheLeida ??= LeerCache();
            return _cacheLeida;
        }
    }

    private static List<Modo> LeerCache()
    {
        string json = GameOptions.CamaraCatalogo;
        if (string.IsNullOrEmpty(json)) return new List<Modo>();
        try { return JsonUtility.FromJson<Catalogo>(json)?.modos ?? new List<Modo>(); }
        catch { return new List<Modo>(); }
    }

    private static void GuardarCache()
    {
        var c = new Catalogo();
        c.modos.AddRange(_modos);
        GameOptions.CamaraCatalogo = JsonUtility.ToJson(c);
        _cacheLeida = c.modos;
    }
}
