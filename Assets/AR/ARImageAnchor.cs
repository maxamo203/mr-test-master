using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARTrackedImageManager))]
public class ARImageAnchor : MonoBehaviour
{
    public event Action OnImageFound;       // solo la PRIMERA vez (compat con consumidores actuales)
    public event Action OnImageReacquired;  // cada vez que se (re)detecta la imagen, incluida la primera

    // Aviso sobre la imagen de referencia en uso (null si está bien): ARKit/ARCore
    // la rechazó por falta de detalle, etc. Lo muestran las pantallas de "buscando
    // la imagen…" para que el jugador sepa que conviene recapturar / re-escanear.
    public event Action<string> OnAvisoImagen;
    public string AvisoImagen { get; private set; }

    public bool IsFound { get; private set; }
#if UNITY_EDITOR
    // El stub de editor no crea un ARAnchor (no hay subsistema), así que guardamos su
    // transform aparte para que CurrentAnchor sirva igual en play mode.
    private Transform _editorAnchor;
    public Transform CurrentAnchor => _anchor != null ? _anchor.transform : _editorAnchor;
#else
    public Transform CurrentAnchor => _anchor != null ? _anchor.transform : null;
#endif

    [SerializeField] private ARAnchorManager _anchorManager;

    private ARTrackedImageManager _imageManager;
    private ARPlaneManager        _planeManager;
    private ARAnchor              _anchor;
    private GameObject            _anchorVisual;
    private bool                  _foundEverFired;
    private bool                  _pendingKeepVisual;  // modo elegido en la última recalibración

    // Librería mutable con UNA sola imagen: la de referencia en uso. Se crea de cero
    // en cada AddReferenceImage — acumular las imágenes de toda la sesión (la del
    // escáner, la del mapa anterior…) hacía que ARKit buscara todas a la vez y que
    // cualquiera de ellas, si seguía a la vista, anclara el mapa en el lugar
    // equivocado. Con una sola imagen la búsqueda es más rápida y más barata.
    private MutableRuntimeReferenceImageLibrary _runtimeLib;
    private Guid   _imagenActual;      // guid de la imagen en uso (Guid.Empty si no se conoce)
    private string _nombreActual;      // nombre único de esa imagen (fallback del filtro)
    private int    _contadorImagenes;

    // Orientación física de la imagen en uso (guardada con el escaneo; Desconocida en
    // escaneos viejos, que se resuelve con la pose detectada). Define de qué eje de
    // la imagen sale el rumbo — ver ImageAnchorPose.EjeRumbo.
    public ImageAnchorPose.Orientacion Orientacion { get; private set; }

    // Orientación que se CONFIRMÓ con la última detección real (Desconocida hasta
    // anclar). Es lo que se persiste al guardar un escaneo nuevo.
    public ImageAnchorPose.Orientacion OrientacionDetectada { get; private set; }

    // True una vez que hay al menos una imagen lista para buscar (capturada o
    // cargada). El bootstrap la usa para no arrancar el tracking en vacío.
    public bool HasReferenceImage { get; private set; }

    // Ventana mínima de búsqueda tras (re)iniciar el tracking antes de aceptar
    // una detección. Sin esto, al recalibrar el trackable que quedaba de antes se
    // re-detecta en el MISMO frame: el modo nunca se ve en Calibrating y se re-
    // anclaba con una pose vieja. Con el retardo, ARKit/ARCore re-adquiere la
    // imagen con una pose fresca y el modo se queda en Calibrating mientras tanto.
    [SerializeField] private float _reacquireDelay = 1.0f;
    private float _searchSince;

    // ── Muestreo de la pose ───────────────────────────────────────────────────
    // Al detectar la imagen, ARKit/ARCore siguen refinando su pose durante los primeros
    // frames de tracking: las muestras más viejas son las peores. Por eso NO se
    // promedia todo lo visto desde la primera detección, sino sólo una VENTANA de las
    // últimas muestras, y se ancla recién cuando esa ventana dejó de moverse (rumbo y
    // posición estables). Si la imagen se ve mal (de lejos, de canto, intermitente) y
    // nunca converge, pasado _maxSampleWait se ancla igual con la ventana que haya.
    //
    // Para que haya frames en estado Tracking SEGUIDOS el manager se pone en modo
    // "moving images" (ver ActivarBusqueda): en modo detección pura (max = 0) ARKit
    // deja la imagen en Limited después del primer frame y sólo vuelve a Tracking
    // cuando la re-detecta (~1 Hz) — así el muestreo tardaba segundos y solía caer al
    // timeout con una o dos muestras crudas.
    [Tooltip("Muestras de la ventana de convergencia.")]
    [SerializeField] private int   _ventana = 8;
    [Tooltip("Segundos mínimos entre muestras: con 0.05 la ventana de 8 cubre ~0.4 s, " +
             "tiempo suficiente para que el tracking refine la pose.")]
    [SerializeField] private float _intervaloMuestra = 0.05f;
    [Tooltip("Dispersión máxima del rumbo dentro de la ventana (grados) para dar por convergida la pose.")]
    [SerializeField] private float _tolRumboGrados = 2f;
    [Tooltip("Dispersión máxima de la posición dentro de la ventana (m).")]
    [SerializeField] private float _tolPosMetros = 0.02f;
    [Tooltip("Fallback: si la ventana no converge, anclar igual pasado este tiempo (s) desde la 1ª muestra.")]
    [SerializeField] private float _maxSampleWait = 4f;

    private readonly List<ImageAnchorPose.Muestra> _muestras = new();
    private float   _sampleStart, _ultimaMuestraT;
    private Vector3 _rumboPrevio;
    private Vector3 _normalUltima;

    private void ResetSampling()
    {
        _muestras.Clear();
        _rumboPrevio = Vector3.zero;
        _ultimaMuestraT = -1f;
    }

    private void Awake()
    {
        _imageManager         = GetComponent<ARTrackedImageManager>();
        _imageManager.enabled = false;

        if (_anchorManager == null) _anchorManager = FindFirstObjectByType<ARAnchorManager>();

        _planeManager = GetComponent<ARPlaneManager>();
        if (_planeManager == null) _planeManager = FindFirstObjectByType<ARPlaneManager>();
        if (_planeManager != null) _planeManager.enabled = false;
    }

    public void StartTracking()
    {
        _searchSince = Time.time;
        _buscando    = true;
        ResetSampling();
        ActivarBusqueda();
    }

    // Vuelve a entrar en modo "buscando imagen". El anchor viejo se destruye, pero
    // WorldOrigin (y toda la escena escaneada que cuelga de él) sobrevive porque lo
    // soltamos del anchor ANTES de destruirlo.
    //   keepVisualPosition = true  → al re-anclar, la escena se queda donde está.
    //   keepVisualPosition = false → al re-anclar, la escena se mueve con el anchor.
    public void RestartTracking(bool keepVisualPosition = false)
    {
        _pendingKeepVisual = keepVisualPosition;
        IsFound = false;

        // CRÍTICO: WorldOrigin es hijo del anchor actual. Si destruimos el anchor
        // sin soltarlo primero, nos llevamos puesto WorldOrigin y TODO lo escaneado
        // (eso causaba el NullReference al recalibrar). Lo desparentamos al root
        // conservando su pose en el mundo para que sobreviva hasta el nuevo anchor.
        if (WorldOrigin.Instance != null)
            WorldOrigin.Instance.transform.SetParent(null, worldPositionStays: true);

        // Una búsqueda de imagen en curso y un anchor manual sosteniendo el mapa no
        // pueden convivir: se pelearían por WorldOrigin. Este es el único camino hacia
        // una búsqueda nueva, así que centralizamos acá el descarte del manual.
        ManualCalibration.Instance?.Descartar();

        if (_anchorVisual != null) Destroy(_anchorVisual);
        _anchorVisual = null;

        if (_anchor != null) Destroy(_anchor.gameObject);
        _anchor = null;

        _searchSince = Time.time;
        _buscando    = true;
        ResetSampling();
        ActivarBusqueda();
        Debug.Log("[ARImageAnchor] RestartTracking — buscando imagen otra vez.");
    }

    // Prende el tracking de la imagen. Sólo corre mientras se busca: en cuanto hay
    // anchor se apaga (ver Update), así que su costo es acotado al lobby.
    private void ActivarBusqueda()
    {
#if UNITY_EDITOR
        StartCoroutine(EditorStub());
#else
        // Tracking continuo de 1 imagen (no detección pura): es lo que da frames en
        // estado Tracking seguidos, con la pose refinada frame a frame. Ver el bloque
        // de muestreo arriba. El setter guarda el valor y el manager lo aplica al
        // habilitarse, así que va ANTES de enabled = true.
        _imageManager.requestedMaxNumberOfMovingImages = 1;
        _imageManager.enabled = true;
#endif
    }

    // Corta la búsqueda de la imagen sin tocar el anchor actual. La usa la
    // calibración manual (ManualCalibration): el jugador declaró que no tiene la
    // imagen física, así que una detección tardía no debe moverle el mapa después
    // de haberlo acomodado a mano. Cualquier Start/RestartTracking la reanuda.
    public void StopTracking()
    {
        _buscando = false;
#if UNITY_EDITOR
        StopAllCoroutines();
#else
        _imageManager.enabled = false;
#endif
        ResetSampling();
    }

    // Hay una búsqueda de imagen en curso (todavía sin anclar). Lo consulta la UI
    // para distinguir "buscando la imagen…" de "calibrado a mano".
    public bool Buscando => _buscando && !IsFound;
    private bool _buscando;

    // Muestras juntadas en la búsqueda actual (para la UI / debug: "afinando pose…").
    public int MuestrasActuales => _muestras.Count;

    private void Update()
    {
#if UNITY_EDITOR
        return;
#endif
        if (!_imageManager.enabled || IsFound) return;

        // Esperamos la ventana de re-adquisición: así el modo se queda en
        // Calibrating y ARKit/ARCore actualiza la pose de la imagen antes de anclar.
        if (Time.time - _searchSince < _reacquireDelay) return;

        // Sólo NUESTRA imagen, y sólo en estado Tracking (pose real de este frame).
        var tracked = BuscarImagenActual();

        if (tracked != null && Time.time - _ultimaMuestraT >= _intervaloMuestra)
        {
            if (_muestras.Count == 0) _sampleStart = Time.time;   // primera muestra
            _ultimaMuestraT = Time.time;

            var t = tracked.transform;
            _normalUltima = t.up;

            var orient = ImageAnchorPose.Resolver(Orientacion, _normalUltima);
            var rumbo  = ImageAnchorPose.EjeRumbo(t.right, t.up, t.forward, orient);
            // Evitar que un flip de 180° del eje entre muestras cancele el promedio.
            if (_rumboPrevio != Vector3.zero) rumbo = ImageAnchorPose.AlinearSigno(rumbo, _rumboPrevio);
            _rumboPrevio = rumbo;

            _muestras.Add(new ImageAnchorPose.Muestra(rumbo, t.position));
            if (_muestras.Count > _ventana) _muestras.RemoveAt(0);
        }

        if (_muestras.Count == 0) return; // todavía no se vio la imagen ni una vez

        bool convergio = ImageAnchorPose.Convergio(_muestras, _ventana, _tolRumboGrados, _tolPosMetros);
        bool timedOut  = (Time.time - _sampleStart) >= _maxSampleWait;
        if (!convergio && !timedOut) return;

        if (!ImageAnchorPose.Promediar(_muestras, out var rumboFinal, out var posFinal)) return;

        OrientacionDetectada = ImageAnchorPose.DesdePose(_normalUltima);
        var resuelta = ImageAnchorPose.Resolver(Orientacion, _normalUltima);
        if (resuelta != Orientacion)
            Debug.Log($"[ARImageAnchor] Orientación guardada={Orientacion}, detectada={resuelta} " +
                      $"(|normal.y|={Mathf.Abs(_normalUltima.y):0.00}); se usa la detectada.");
        Debug.Log($"[ARImageAnchor] Anclando con {_muestras.Count} muestras " +
                  $"({(convergio ? "convergió" : "timeout")}) orientación={resuelta}.");

        // Anclar con la pose promediada de la ventana (Y-up, sólo rumbo).
        PlaceAnchorAndSpawn(posFinal, ImageAnchorPose.UprightDesdeRumbo(rumboFinal));
        IsFound = true;

        // Confirmar la orientación en el holder de la imagen actual: si este escaneo se
        // guarda (escáner), se persiste la real y no la estimada al capturar.
        Scanner.CapturedReference.ConfirmarOrientacion(OrientacionDetectada);

        if (!_foundEverFired)
        {
            _foundEverFired = true;
            OnImageFound?.Invoke();
        }
        OnImageReacquired?.Invoke();

        // Imagen anclada: el tracking ya no hace falta (y cuesta). RestartTracking lo
        // vuelve a prender cuando se recalibra.
        _imageManager.enabled = false;
    }

    // Trackable en estado Tracking que corresponda a la imagen en uso. Con librería
    // de una sola imagen debería ser el único, pero el filtro es la garantía: un
    // trackable viejo (de una librería anterior) nunca puede anclar este mapa.
    private ARTrackedImage BuscarImagenActual()
    {
        foreach (var img in _imageManager.trackables)
        {
            if (img.trackingState != TrackingState.Tracking) continue;
            if (!EsImagenActual(img)) continue;
            return img;
        }
        return null;
    }

    private bool EsImagenActual(ARTrackedImage img)
    {
        var referencia = img.referenceImage;
        if (_imagenActual != Guid.Empty) return referencia.guid == _imagenActual;
        return !string.IsNullOrEmpty(_nombreActual) && referencia.name == _nombreActual;
    }

    private void PlaceAnchorAndSpawn(Vector3 position, Quaternion rotation)
    {
        var anchorGO = new GameObject("ImageAnchor");
        // El eje Y del anchor SIEMPRE apunta hacia arriba en el mundo; solo conservamos
        // el rumbo horizontal (ya viene promediado desde Update — ver ImageAnchorPose).
        anchorGO.transform.SetPositionAndRotation(position, rotation);
        _anchor = anchorGO.AddComponent<ARAnchor>();
        // ARTrackable.destroyOnRemoval arranca en TRUE y ARTrackableManager hace
        // Destroy(removed.gameObject) cuando la plataforma reporta el trackable como
        // removido. WorldOrigin es HIJO de este GameObject: sin esto, una remoción
        // del lado nativo se lleva puesta toda la escena escaneada. RestartTracking
        // lo destruye explícitamente, así que no queda nada colgado.
        _anchor.destroyOnRemoval = false;

        if (_planeManager != null) _planeManager.enabled = true;

        WorldOrigin.Instance.SetOrigin(_anchor.transform, _pendingKeepVisual);
        // El visual es solo cosmético: si falla por cualquier motivo, no debe
        // impedir que el anchor quede confirmado (IsFound / eventos).
        try { SpawnVisual(_anchor.transform); }
        catch (Exception e) { Debug.LogWarning($"[ARImageAnchor] SpawnVisual falló: {e.Message}"); }
    }

    // ── Imagen de referencia en runtime ───────────────────────────────────────
    // Agrega una imagen (un fragmento capturado con la cámara, o una cargada de
    // disco) a una librería mutable NUEVA y reinicia la detección para que
    // ARKit/ARCore la busque en el entorno físico. Asíncrono: el job de validación
    // corre en background; cuando termina, reiniciamos el tracking.
    //
    // orientacion: cómo está pegada la imagen (piso/mesa o pared). Desconocida para
    // escaneos viejos: se infiere de la pose detectada.
    public void AddReferenceImage(Texture2D tex, string imageName, float widthMeters,
                                  bool keepVisualPosition = false,
                                  ImageAnchorPose.Orientacion orientacion = ImageAnchorPose.Orientacion.Desconocida)
    {
        if (tex == null) { Debug.LogWarning("[ARImageAnchor] AddReferenceImage con textura null."); return; }
        Orientacion          = orientacion;
        OrientacionDetectada = ImageAnchorPose.Orientacion.Desconocida;
        SetAviso(null);
#if UNITY_EDITOR
        // En editor no hay subsistema real: simulamos el anchor con el stub.
        HasReferenceImage = true;
        RestartTracking(keepVisualPosition);
#else
        StopAllCoroutines();   // un AddReferenceImage anterior a medio validar no debe pisar a éste
        StartCoroutine(AddReferenceImageRoutine(tex, imageName, widthMeters, keepVisualPosition));
#endif
    }

    private IEnumerator AddReferenceImageRoutine(Texture2D tex, string imageName, float widthMeters, bool keepVisualPosition)
    {
        // Necesitamos el subsistema instanciado para crear la librería mutable. Al
        // habilitar el manager sin librería se auto-deshabilita, pero deja el
        // subsistema creado, que es lo que buscamos.
        _imageManager.enabled = true;
        while (_imageManager.subsystem == null) yield return null;
        // Mientras se valida la imagen nueva NO se busca la vieja.
        _imageManager.enabled = false;

        if (!CrearLibreria())
        {
            Debug.LogError("[ARImageAnchor] No se pudo crear una librería mutable; la imagen no se agrega.");
            yield break;
        }

        if (tex.format != TextureFormat.RGBA32) tex = ToRGBA32(tex);
        if (widthMeters <= 0f) widthMeters = 0.15f;

        // Nombre único por alta: es el fallback del filtro de trackables (el guid lo
        // asigna la librería al agregar y lo leemos después).
        _contadorImagenes++;
        _nombreActual = $"{imageName}#{_contadorImagenes}";
        _imagenActual = Guid.Empty;

        // Construimos el XRReferenceImage con el tamaño físico (ancho, alto) en
        // metros y se lo pasamos a la sobrecarga de instancia del job.
        float aspect = tex.width > 0 ? tex.height / (float)tex.width : 1f;
        var refImage = new XRReferenceImage(
            new SerializableGuid(0, 0),
            new SerializableGuid(0, 0),
            new Vector2(widthMeters, widthMeters * aspect),
            _nombreActual,
            tex);

        var jobState = _runtimeLib.ScheduleAddImageWithValidationJob(
            tex.GetRawTextureData<byte>(),
            new Vector2Int(tex.width, tex.height),
            tex.format,
            refImage);

        while (jobState.status == AddReferenceImageJobStatus.Pending) yield return null;

        if (jobState.status != AddReferenceImageJobStatus.Success)
        {
            Debug.LogWarning($"[ARImageAnchor] El job de imagen terminó en {jobState.status} " +
                             "(el fragmento puede tener pocos detalles para trackear).");
            SetAviso(jobState.status == AddReferenceImageJobStatus.ErrorInvalidImage
                ? "El sistema AR rechazó la imagen: tiene poco detalle o es repetitiva. Conviene recapturarla."
                : "No se pudo registrar la imagen de referencia en el sistema AR.");
        }
        else
        {
            foreach (var img in _runtimeLib)
                if (img.name == _nombreActual) { _imagenActual = img.guid; break; }
            Debug.Log($"[ARImageAnchor] Imagen '{_nombreActual}' agregada (librería de {_runtimeLib.count}).");
        }

        HasReferenceImage = true;

        // Reiniciamos la detección para que busque la imagen recién agregada.
        RestartTracking(keepVisualPosition);
    }

    // Librería mutable VACÍA nueva (descarta la anterior) y la deja puesta en el manager.
    private bool CrearLibreria()
    {
        try
        {
            _runtimeLib = _imageManager.CreateRuntimeLibrary() as MutableRuntimeReferenceImageLibrary;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ARImageAnchor] CreateRuntimeLibrary falló: {e.Message}");
            _runtimeLib = null;
            return false;
        }

        if (_runtimeLib == null)
        {
            Debug.LogWarning("[ARImageAnchor] El subsistema no soporta librerías mutables.");
            return false;
        }

        _imageManager.referenceLibrary = _runtimeLib;
        return true;
    }

    private void SetAviso(string aviso)
    {
        if (AvisoImagen == aviso) return;
        AvisoImagen = aviso;
        OnAvisoImagen?.Invoke(aviso);
    }

    private static Texture2D ToRGBA32(Texture2D src)
    {
        var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, mipChain: false);
        dst.SetPixels(src.GetPixels());
        dst.Apply(updateMipmaps: false);
        return dst;
    }

    private void SpawnVisual(Transform anchorTransform)
    {
        // El visual cuelga de WORLDORIGIN, no del anchor. Marca el 0,0 DEL MAPA: es lo
        // que define dónde y con qué rotación quedó el entorno escaneado, así que tiene
        // que moverse con él. Colgándolo del anchor se quedaba clavado en la pose de
        // detección y no seguía ni el ajuste manual del origen (ManualCalibration) ni
        // una recalibración "mantener escena fija" (keepVisualPosition: true), con lo
        // que el libro terminaba lejos del mapa que supuestamente ancla.
        // Fallback al anchor sólo si WorldOrigin no está (no debería pasar: SetOrigin
        // corre justo antes que esto).
        var origen = WorldOrigin.Instance != null ? WorldOrigin.Instance.transform : anchorTransform;

        // EN PARTIDA el visual es el LIBRO RITUAL: no es un marcador sino una mecánica
        // (se cierra solo y hay que alumbrarlo). Fuera de partida —escáner,
        // calibración— siguen las esferas de siempre, que es cuando sirven de referencia.
        _anchorVisual = Gameplay.RitualBookView.TrySpawn(origen);
        if (_anchorVisual != null) return;

        _anchorVisual = new GameObject("AnchorVisual");
        _anchorVisual.transform.SetParent(origen, worldPositionStays: false);
        _anchorVisual.transform.localPosition = Vector3.up * 0.05f;
        _anchorVisual.transform.localRotation = Quaternion.identity;

        // Esfera principal (blanca) + satélite (rojo). Ver AnchorVisuals para el
        // porqué de no usar GameObject.CreatePrimitive.
        var main = AnchorVisuals.MakeSphere(_anchorVisual.transform, Vector3.zero, 0.1f, Color.white);
        if (main != null) AnchorVisuals.MakeSphere(main.transform, new Vector3(0.7f, 0f, 0f), 0.35f, Color.red);
    }

#if UNITY_EDITOR
    private IEnumerator EditorStub()
    {
        yield return new WaitForSeconds(1f);

        var go = new GameObject("EditorAnchor");
        go.transform.position = new Vector3(0f, 0f, 1f);
        _editorAnchor = go.transform;
        WorldOrigin.Instance.SetOrigin(go.transform, _pendingKeepVisual);

        try { SpawnVisual(go.transform); }
        catch (Exception e) { Debug.LogWarning($"[ARImageAnchor] SpawnVisual falló: {e.Message}"); }
        IsFound = true;
        OrientacionDetectada = Orientacion == ImageAnchorPose.Orientacion.Desconocida
            ? ImageAnchorPose.Orientacion.Horizontal : Orientacion;
        Scanner.CapturedReference.ConfirmarOrientacion(OrientacionDetectada);
        if (!_foundEverFired)
        {
            _foundEverFired = true;
            OnImageFound?.Invoke();
        }
        OnImageReacquired?.Invoke();
    }
#endif
}
