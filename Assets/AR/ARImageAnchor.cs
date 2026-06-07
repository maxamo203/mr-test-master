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
    public Transform CurrentAnchor => _anchor != null ? _anchor.transform : null;

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

        foreach (var img in _imageManager.trackables)
        {
            if (img.trackingState != TrackingState.Tracking) continue;

            PlaceAnchorAndSpawn(img.transform);
            IsFound = true;

            if (!_foundEverFired)
            {
                _foundEverFired = true;
                OnImageFound?.Invoke();
            }
            OnImageReacquired?.Invoke();

            _imageManager.enabled = false;
            return;
        }
    }

    private void PlaceAnchorAndSpawn(Transform imageTransform)
    {
        var anchorGO = new GameObject("ImageAnchor");
        // El eje Y del anchor SIEMPRE apunta hacia arriba en el mundo, sin
        // importar la rotación física de la imagen (vertical, horizontal o dada
        // vuelta). Solo conservamos el rumbo horizontal — ver UprightFromImage.
        anchorGO.transform.SetPositionAndRotation(imageTransform.position, UprightFromImage(imageTransform));
        _anchor = anchorGO.AddComponent<ARAnchor>();

        if (_planeManager != null) _planeManager.enabled = true;

        WorldOrigin.Instance.SetOrigin(_anchor.transform, _pendingKeepVisual);
        // El visual es solo cosmético: si falla por cualquier motivo, no debe
        // impedir que el anchor quede confirmado (IsFound / eventos).
        try { SpawnVisual(_anchor.transform); }
        catch (Exception e) { Debug.LogWarning($"[ARImageAnchor] SpawnVisual falló: {e.Message}"); }
    }

    // Devuelve una rotación cuyo eje Y es SIEMPRE el up del mundo, con el rumbo
    // (yaw) derivado de la pose REAL de la imagen + la gravedad — sin usar la
    // cámara ni la posición desde donde se escaneó.
    //
    // Convención de ARFoundation en este proyecto: el PLANO de la imagen son los
    // ejes +X (ancho) y +Y (alto); la NORMAL es +Z (forward). Lo confirma el caso
    // "imagen apoyada en el piso": +X/+Y quedan horizontales (estan sobre la foto)
    // y +Z apunta vertical.
    //
    // Para el rumbo usamos un eje del PLANO (NO la normal). Por qué evitamos la
    // normal: el tracking la estima con más ruido (es la dirección fuera del plano)
    // y, en una imagen VERTICAL, queda horizontal y "empata" con el ancho — el
    // código viejo elegía "el más horizontal" y ese empate hacía saltar el yaw
    // ~90° entre calibraciones (la calibración vertical inconsistente reportada).
    // Tomando solo ejes del plano el empate desaparece y el rumbo es determinista.
    private static Quaternion UprightFromImage(Transform img)
    {
        // Proyecciones al plano horizontal de los dos ejes del plano de la imagen.
        Vector3 altoH  = new Vector3(img.up.x,    0f, img.up.z);    // +Y (alto)
        Vector3 anchoH = new Vector3(img.right.x, 0f, img.right.z); // +X (ancho)

        // Preferimos el alto (+Y) — mantiene el rumbo que ya daba el caso horizontal,
        // así los mapas con imagen horizontal no cambian de convención. Cambiamos al
        // ancho (+X) solo si es claramente más horizontal (histéresis para no
        // alternar en casos ambiguos ~45°). En una imagen vertical el alto queda
        // vertical (≈0) y gana el ancho => rumbo estable.
        Vector3 best = (anchoH.sqrMagnitude > altoH.sqrMagnitude + 0.05f) ? anchoH : altoH;

        if (best.sqrMagnitude < 1e-6f) best = Vector3.forward;
        return Quaternion.LookRotation(best.normalized, Vector3.up);
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
        _anchorVisual = new GameObject("AnchorVisual");
        _anchorVisual.transform.SetParent(anchorTransform, worldPositionStays: false);
        _anchorVisual.transform.localPosition = Vector3.up * 0.05f;
        _anchorVisual.transform.localRotation = Quaternion.identity;

        // Esfera principal (blanca) + satélite (rojo). Las construimos con la malla
        // built-in y MeshRenderer en vez de GameObject.CreatePrimitive: este último
        // intenta agregar un SphereCollider y, si el módulo Physics está stripeado
        // en el build (IL2CPP), tira "class SphereCollider doesn't exist" y rompía
        // la confirmación del anchor. El visual no necesita colliders.
        var main = MakeSphere(_anchorVisual.transform, Vector3.zero, 0.1f, Color.white);
        if (main != null) MakeSphere(main.transform, new Vector3(0.7f, 0f, 0f), 0.35f, Color.red);
    }

    private static Mesh _sphereMesh;
    private static GameObject MakeSphere(Transform parent, Vector3 localPos, float scale, Color color)
    {
        if (_sphereMesh == null) _sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        if (_sphereMesh == null)
        {
            Debug.LogWarning("[ARImageAnchor] No se pudo obtener la malla built-in 'Sphere.fbx'.");
            return null;
        }

        var go = new GameObject("VisualSphere");
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = Vector3.one * scale;

        go.AddComponent<MeshFilter>().sharedMesh = _sphereMesh;
        var mr = go.AddComponent<MeshRenderer>();

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.color = color;
        mat.SetColor("_BaseColor", color); // URP
        mr.material = mat;
        return go;
    }

#if UNITY_EDITOR
    private IEnumerator EditorStub()
    {
        yield return new WaitForSeconds(1f);

        var go = new GameObject("EditorAnchor");
        go.transform.position = new Vector3(0f, 0f, 1f);
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
