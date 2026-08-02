using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARTrackedImageManager))]
public class ARImageAnchor : MonoBehaviour
{
    public event Action OnImageFound;       // solo la PRIMERA vez (compat con consumidores actuales)
    public event Action OnImageReacquired;  // cada vez que se (re)detecta la imagen, incluida la primera

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

    // Librería mutable: arranca como copia de la serializada y le agregamos en
    // runtime las imágenes capturadas con la cámara (ver AddReferenceImage).
    private MutableRuntimeReferenceImageLibrary _runtimeLib;

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

    // Al detectar la imagen, ARKit/ARCore sigue refinando su pose unos frames: anclar con
    // UN solo frame da un rumbo (yaw) ligeramente distinto cada vez y la escena queda
    // rotada respecto al escaneo original. Promediamos varias muestras de la pose antes de
    // anclar. NO hace falta que los frames Tracking sean seguidos: si la imagen se ve a
    // ratos, igual vamos acumulando; así funciona con fragmentos que trackean intermitente.
    [Tooltip("Muestras (frames en Tracking, NO necesariamente seguidos) a promediar antes de anclar.")]
    [SerializeField] private int   _minSamples = 5;
    [Tooltip("Fallback: si la imagen se ve solo a ratos, anclar igual pasado este tiempo " +
             "(s) desde la 1ª muestra, con lo que se haya juntado.")]
    [SerializeField] private float _maxSampleWait = 2.5f;
    private int     _sampleCount;
    private float   _sampleStart;
    private Vector3 _accumYaw, _accumPos;

    private void ResetSampling() { _sampleCount = 0; _accumYaw = Vector3.zero; _accumPos = Vector3.zero; }

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
        ResetSampling();
#if UNITY_EDITOR
        StartCoroutine(EditorStub());
#else
        _imageManager.enabled = true;
#endif
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

        if (_anchorVisual != null) Destroy(_anchorVisual);
        _anchorVisual = null;

        if (_anchor != null) Destroy(_anchor.gameObject);
        _anchor = null;

        _searchSince = Time.time;
        ResetSampling();
#if UNITY_EDITOR
        StartCoroutine(EditorStub());
#else
        _imageManager.enabled = true;
#endif
        Debug.Log("[ARImageAnchor] RestartTracking — buscando imagen otra vez.");
    }

    private void Update()
    {
#if UNITY_EDITOR
        return;
#endif
        if (!_imageManager.enabled || IsFound) return;

        // Esperamos la ventana de re-adquisición: así el modo se queda en
        // Calibrating y ARKit/ARCore actualiza la pose de la imagen antes de anclar.
        if (Time.time - _searchSince < _reacquireDelay) return;

        // Buscar la imagen en estado Tracking (si hay una este frame).
        Transform tracked = null;
        foreach (var img in _imageManager.trackables)
            if (img.trackingState == TrackingState.Tracking) { tracked = img.transform; break; }

        // Si la imagen se ve este frame, acumular una muestra. NO reseteamos cuando NO se
        // ve: seguimos juntando entre frames aunque el tracking sea intermitente.
        if (tracked != null)
        {
            if (_sampleCount == 0) _sampleStart = Time.time; // primera muestra

            Vector3 yawAxis = HorizontalYawAxis(tracked);
            // Evitar que un flip de 180° del eje entre frames cancele el promedio.
            if (_sampleCount > 0 && Vector3.Dot(yawAxis, _accumYaw) < 0f) yawAxis = -yawAxis;
            _accumYaw += yawAxis;
            _accumPos += tracked.position;
            _sampleCount++;
        }

        if (_sampleCount == 0) return; // todavía no se vio la imagen ni una vez

        // Anclar cuando junte suficientes muestras (aunque no hayan sido seguidas), o como
        // fallback pasado _maxSampleWait desde la primera (para imágenes que trackean poco).
        bool enough   = _sampleCount >= _minSamples;
        bool timedOut = (Time.time - _sampleStart) >= _maxSampleWait;
        if (!enough && !timedOut) return;

        // Anclar con la pose promediada (más estable/consistente que un solo frame).
        PlaceAnchorAndSpawn(_accumPos / _sampleCount, UprightFromYaw(_accumYaw));
        IsFound = true;

        if (!_foundEverFired)
        {
            _foundEverFired = true;
            OnImageFound?.Invoke();
        }
        OnImageReacquired?.Invoke();

        _imageManager.enabled = false;
    }

    private void PlaceAnchorAndSpawn(Vector3 position, Quaternion rotation)
    {
        var anchorGO = new GameObject("ImageAnchor");
        // El eje Y del anchor SIEMPRE apunta hacia arriba en el mundo; solo conservamos
        // el rumbo horizontal (ya viene promediado desde Update — ver UprightFromYaw).
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

    // Devuelve una rotación cuyo eje Y es SIEMPRE el up del mundo, con el rumbo
    // (yaw) derivado de la pose REAL de la imagen + la gravedad — sin usar la cámara
    // ni la posición desde donde se escaneó.
    //
    // ASUNCIÓN: la imagen de referencia SIEMPRE se escanea HORIZONTAL (acostada en
    // piso/mesa, su normal apuntando hacia arriba). Con eso identificamos la normal
    // de forma puramente GEOMÉTRICA — es el eje más vertical (mayor |y|) — sin
    // depender de la convención de ejes de ARFoundation (cuál de +X/+Y/+Z es la
    // normal), que era el origen de la inconsistencia: cuando intentábamos adivinar
    // la convención, a veces usábamos la normal (ruidosa) para el rumbo y el yaw
    // saltaba ~90° entre calibraciones.
    //
    // El rumbo sale de un eje del PLANO de la imagen (los otros dos), tomado en un
    // orden fijo => determinista y estable entre calibraciones de la misma imagen.
    // Eje de RUMBO horizontal (yaw) de la imagen, en un orden fijo => determinista y
    // estable entre calibraciones de la misma imagen. Devuelve un vector horizontal
    // unitario (o forward si degenerado). Update lo acumula por varios frames y promedia.
    private static Vector3 HorizontalYawAxis(Transform img)
    {
        Vector3[] axes = { img.right, img.up, img.forward };

        // 1) La NORMAL es el eje más vertical (en una imagen horizontal apunta arriba).
        //    Geométrico, no depende de la convención de ejes de ARFoundation.
        int normalIdx = 0;
        float maxAbsY = Mathf.Abs(axes[0].y);
        for (int i = 1; i < axes.Length; i++)
        {
            float ay = Mathf.Abs(axes[i].y);
            if (ay > maxAbsY) { maxAbsY = ay; normalIdx = i; }
        }

        // 2) El rumbo: el primero de los OTROS dos ejes (en el plano de la imagen),
        //    proyectado al horizontal. Orden fijo => mismo eje siempre.
        for (int i = 0; i < axes.Length; i++)
        {
            if (i == normalIdx) continue;
            var h = new Vector3(axes[i].x, 0f, axes[i].z);
            if (h.sqrMagnitude > 1e-6f) return h.normalized;
        }
        return Vector3.forward;
    }

    // Rotación upright (Y = up del mundo) desde un eje de rumbo horizontal (posiblemente
    // acumulado/promediado: se aplana a horizontal y se normaliza).
    private static Quaternion UprightFromYaw(Vector3 horizYawAxis)
    {
        horizYawAxis.y = 0f;
        if (horizYawAxis.sqrMagnitude < 1e-6f) horizYawAxis = Vector3.forward;
        return Quaternion.LookRotation(horizYawAxis.normalized, Vector3.up);
    }

    // ── Imagen de referencia en runtime ───────────────────────────────────────
    // Agrega una imagen (un fragmento capturado con la cámara, o una cargada de
    // disco) a la librería mutable y reinicia la detección para que ARKit/ARCore
    // la busque en el entorno físico. Asíncrono: el job de validación corre en
    // background; cuando termina, reiniciamos el tracking.
    public void AddReferenceImage(Texture2D tex, string imageName, float widthMeters, bool keepVisualPosition = false)
    {
        if (tex == null) { Debug.LogWarning("[ARImageAnchor] AddReferenceImage con textura null."); return; }
#if UNITY_EDITOR
        // En editor no hay subsistema real: simulamos el anchor con el stub.
        HasReferenceImage = true;
        RestartTracking(keepVisualPosition);
#else
        StartCoroutine(AddReferenceImageRoutine(tex, imageName, widthMeters, keepVisualPosition));
#endif
    }

    private IEnumerator AddReferenceImageRoutine(Texture2D tex, string imageName, float widthMeters, bool keepVisualPosition)
    {
        // Necesitamos el subsistema corriendo para crear/usar la librería mutable.
        _imageManager.enabled = true;
        while (_imageManager.subsystem == null) yield return null;

        if (!EnsureRuntimeLibrary())
        {
            Debug.LogError("[ARImageAnchor] No se pudo crear una librería mutable; la imagen no se agrega.");
            yield break;
        }

        if (tex.format != TextureFormat.RGBA32) tex = ToRGBA32(tex);
        if (widthMeters <= 0f) widthMeters = 0.15f;

        // Construimos el XRReferenceImage con el tamaño físico (ancho, alto) en
        // metros y se lo pasamos a la sobrecarga de instancia del job.
        float aspect = tex.width > 0 ? tex.height / (float)tex.width : 1f;
        var refImage = new XRReferenceImage(
            new SerializableGuid(0, 0),
            new SerializableGuid(0, 0),
            new Vector2(widthMeters, widthMeters * aspect),
            imageName,
            tex);

        var jobState = _runtimeLib.ScheduleAddImageWithValidationJob(
            tex.GetRawTextureData<byte>(),
            new Vector2Int(tex.width, tex.height),
            tex.format,
            refImage);

        while (jobState.status == AddReferenceImageJobStatus.Pending) yield return null;

        if (jobState.status != AddReferenceImageJobStatus.Success)
            Debug.LogWarning($"[ARImageAnchor] El job de imagen terminó en {jobState.status} " +
                             "(el fragmento puede tener pocos detalles para trackear).");
        else
            Debug.Log($"[ARImageAnchor] Imagen '{imageName}' agregada a la librería ({_runtimeLib.count} total).");

        HasReferenceImage = true;

        // Reiniciamos la detección para que busque la imagen recién agregada.
        RestartTracking(keepVisualPosition);
    }

    private bool EnsureRuntimeLibrary()
    {
        if (_runtimeLib != null) return true;
        try
        {
            // CreateRuntimeLibrary toma el asset serializado (XRReferenceImageLibrary).
            // Si el manager ya tiene una librería serializada, la usamos como base
            // (conserva las imágenes pre-cargadas); si no, creamos una vacía.
            var serialized = _imageManager.referenceLibrary as XRReferenceImageLibrary;
            RuntimeReferenceImageLibrary lib = serialized != null
                ? _imageManager.CreateRuntimeLibrary(serialized)
                : _imageManager.CreateRuntimeLibrary();
            _runtimeLib = lib as MutableRuntimeReferenceImageLibrary;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ARImageAnchor] CreateRuntimeLibrary falló: {e.Message}");
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

    private static Texture2D ToRGBA32(Texture2D src)
    {
        var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, mipChain: false);
        dst.SetPixels(src.GetPixels());
        dst.Apply(updateMipmaps: false);
        return dst;
    }

    private void SpawnVisual(Transform anchorTransform)
    {
        // EN PARTIDA el visual del anchor es el LIBRO RITUAL: no es un marcador sino una
        // mecánica (se cierra solo y hay que alumbrarlo). Fuera de partida —escáner,
        // calibración— siguen las esferas de siempre, que es cuando sirven de referencia.
        _anchorVisual = Gameplay.RitualBookView.TrySpawn(anchorTransform);
        if (_anchorVisual != null) return;

        _anchorVisual = new GameObject("AnchorVisual");
        _anchorVisual.transform.SetParent(anchorTransform, worldPositionStays: false);
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
        if (!_foundEverFired)
        {
            _foundEverFired = true;
            OnImageFound?.Invoke();
        }
        OnImageReacquired?.Invoke();
    }
#endif
}
