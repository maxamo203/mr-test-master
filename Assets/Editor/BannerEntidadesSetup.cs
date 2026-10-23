using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

// Menú Mortuorium > Banner: "key art" de Sorken y Veleth juntos para el banner del stand
// (90 × 190 cm). Es una foto, no gameplay: sólo importa que la imagen se vea bien.
//   - Crear escena Sorken + Veleth: arma Assets/Promo/BannerSorkenVeleth.unity desde cero
//     (fondo con degradado, piso brillante, haces de luz, humo, brasas, círculo ritual con
//     el libro y velas) y pone a las dos criaturas en pose con clips reales del juego.
//     Volver a correrlo PISA la escena.
//   - Renderizar PNG: saca la cámara del banner a un PNG vertical 9:19 en <proyecto>/Renders/
//     (fuera de Assets, no se importa).
//
// Todo se ajusta en edit mode, sin Play, desde tres Inspectors:
//   - "Sorken (pose)" / "Veleth (pose)" → BannerPose: clip + slider de tiempo.
//   - "Iluminación" → BannerIluminacion: color / intensidad / sombra de cada luz, niebla, ambiente.
//   - "Cámara banner" → BannerCamaraFX: bloom, exposición, color, viñeta, aberración, grano;
//     y BannerMarco: marco dorado con rombos + logo MORTUORIUM arriba (en la escena ya
//     armada se suma con Agregar marco dorado y logo, sin rehacerla).
// Para ver el encuadre real en el Game view, agregar una resolución de aspecto 9:19.
// Fondos alternativos (ventanal, sigilo, habitación, ojos): BannerFondosSetup.cs.
public static partial class BannerEntidadesSetup
{
    private const string Menu = "Mortuorium/Banner/";
    private const string Carpeta = "Assets/Promo";
    private const string CarpetaMateriales = Carpeta + "/Materiales";
    private const string CarpetaTexturas = Carpeta + "/Texturas";
    private const string ScenePath = Carpeta + "/BannerSorkenVeleth.unity";
    private const string NombreCamara = "Cámara banner";
    private const string WordmarkPath = CarpetaTexturas + "/Wordmark.png";

    private const string SorkenFbx = "Assets/Entities/Sorken/AnimationReviewLocal/Sorken_SurgicalFix_AnimationReview.fbx";
    private const string SorkenMat = "Assets/Entities/Models/Materials/Sorken_PBR.mat";
    private const string SorkenClip = "Armature|Sorken_Grab_Attack";
    private const string VelethFbx = "Assets/Entities/Veleth/Animations/Veleth_EmergeFromBook_HeadFixed.fbx";
    private const string VelethCrawl = "Assets/Entities/Veleth/Animations/Veleth_CrawlRemapped.anim";
    private const string VelethTex = "Assets/Entities/Veleth/Animations/Veleth_";
    private const string LibroPrefab = "Assets/Resources/LibroRitual.prefab";
    private const string VelaPrefab = "Assets/Prefabs/Reliquia_Meshy_AI_Melting_Candle_0830225710_texture.prefab";

    // Composición en profundidad (el cuadro es angosto, 9:19): Veleth en primer plano, baja
    // y grande; Sorken detrás, casi en la misma vertical, asomando por encima. Así entre las
    // dos llenan el alto del cuadro sin salirse de los costados. El círculo queda entre ambas.
    private static readonly Vector3 Ritual = new Vector3(0f, 0f, 0.2f);
    private static readonly Vector3 PosSorken = new Vector3(0.18f, 0f, 1.0f);
    private static readonly Vector3 PosVeleth = new Vector3(-0.15f, 0f, -0.6f);
    private const float YawSorken = 195f;
    private const float YawVeleth = 170f;

    private static readonly Color ColorNiebla = new Color(0.07f, 0.018f, 0.025f);

    [MenuItem(Menu + "Crear escena Sorken + Veleth")]
    public static void CrearEscena()
    {
        var sorkenModelo = AssetDatabase.LoadAssetAtPath<GameObject>(SorkenFbx);
        var velethModelo = AssetDatabase.LoadAssetAtPath<GameObject>(VelethFbx);
        if (sorkenModelo == null || velethModelo == null)
        {
            Debug.LogError("[Banner] Faltan los FBX de Sorken o Veleth (¿assets de LFS sin bajar?).");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Carpetas();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ConfigurarAmbiente();

        Camera cam = ArmarCamara();
        ArmarFondo(new GameObject("Fondo").transform);
        ArmarCirculoRitual(new GameObject("Círculo ritual").transform);
        ArmarAtmosfera(new GameObject("Atmósfera").transform, cam.transform);

        var sorken = CrearCriatura("Sorken (pose)", sorkenModelo, scene, PosSorken, YawSorken,
            AssetDatabase.LoadAssetAtPath<Material>(SorkenMat), Clip(SorkenFbx, SorkenClip), 0.45f);
        var veleth = CrearCriatura("Veleth (pose)", velethModelo, scene, PosVeleth, YawVeleth,
            MaterialVeleth(),
            AssetDatabase.LoadAssetAtPath<AnimationClip>(VelethCrawl) ?? Clip(VelethFbx, null), 0.3f);

        ArmarLuces();

        foreach (var pose in new[] { sorken, veleth })
        {
            pose.CentrarPivote();
            pose.ApoyarEnPiso();
        }
        LogearCriatura(sorken, SorkenFbx);
        LogearCriatura(veleth, VelethFbx);

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = cam.gameObject;
        Debug.Log($"[Banner] Escena creada en {ScenePath}. Poné el Game view en 9:19 para ver el encuadre; " +
                  "Mortuorium > Banner > Renderizar PNG para sacar la imagen.");
    }

    [MenuItem(Menu + "Renderizar PNG (vista previa 1350×2850)")]
    private static void RenderPreview() => Renderizar(1350, 2850, 8);

    // 3× el lienzo del visor de banners (900 × 1900): ~76 dpi a 90 × 190 cm.
    [MenuItem(Menu + "Renderizar PNG (imprenta 2700×5700)")]
    private static void RenderImprenta() => Renderizar(2700, 5700, 4);

    // Más resolución para imprenta: ×1,5 (~114 dpi) y ×2 (~152 dpi a 90 × 190 cm). El MSAA
    // baja a 2 porque a ese tamaño el escalón ya no se ve y las RT HDR intermedias pesan
    // cientos de MB cada una. Bloom, aberración, grano y marco se miden relativos a la
    // imagen, así sale igual que el 2700 × 5700, con más detalle.
    [MenuItem(Menu + "Renderizar PNG (alta 4050×8550)")]
    private static void RenderAlta() => Renderizar(4050, 8550, 2);

    [MenuItem(Menu + "Renderizar PNG (máxima 5400×11400)")]
    private static void RenderMaxima() => Renderizar(5400, 11400, 2);

    // ---------------------------------------------------------------- escena

    private static void Carpetas()
    {
        if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets", "Promo");
        if (!AssetDatabase.IsValidFolder(CarpetaMateriales)) AssetDatabase.CreateFolder(Carpeta, "Materiales");
        if (!AssetDatabase.IsValidFolder(CarpetaTexturas)) AssetDatabase.CreateFolder(Carpeta, "Texturas");
    }

    private static void ConfigurarAmbiente()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        // Sin esto el Standard refleja el cielo por defecto (gris azulado) y lava todo.
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0.3f;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
    }

    // Cerca y en contrapicado (cámara a la altura de la cintura mirando un poco hacia
    // arriba): Veleth llena la parte baja del cuadro, Sorken se agranda encima, y arriba de
    // su cabeza queda ~un cuarto del alto con el resplandor del fondo para el logo.
    private static Camera ArmarCamara()
    {
        var go = new GameObject(NombreCamara) { tag = "MainCamera" };
        go.transform.position = new Vector3(0f, 0.7f, -2.7f);
        go.transform.LookAt(new Vector3(0f, 1.05f, 0.8f));
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 42f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 40f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ColorNiebla;
        // HDR: el bloom y el tonemapping de BannerCamaraFX necesitan valores por encima de 1.
        cam.allowHDR = true;
        cam.allowMSAA = true;
        go.AddComponent<BannerCamaraFX>();
        go.AddComponent<BannerMarco>().logo = Wordmark(); // después del FX: el marco no lleva bloom ni grano
        return cam;
    }

    // Para la escena ya armada (y tuneada): sólo suma el marco y el logo a la cámara, sin
    // tocar nada más. Si ya estaba, reasigna el logo.
    [MenuItem(Menu + "Agregar marco dorado y logo")]
    private static void AgregarMarco()
    {
        var cam = GameObject.Find(NombreCamara);
        if (cam == null)
        {
            Debug.LogError($"[Banner] No está abierta la escena del banner ({ScenePath}).");
            return;
        }
        var marco = cam.GetComponent<BannerMarco>();
        if (marco == null) marco = Undo.AddComponent<BannerMarco>(cam);
        Undo.RecordObject(marco, "Logo del banner");
        marco.logo = Wordmark();
        EditorSceneManager.MarkSceneDirty(cam.scene);
        Selection.activeGameObject = cam;
        Debug.Log("[Banner] Marco y logo agregados a la cámara (Inspector → Banner Marco para ajustarlos). " +
                  "Guardá la escena.");
    }

    // El wordmark de los banners del stand (copiado de su visor). Sin compresión y con mips:
    // en la vista previa se dibuja chico y en el render de imprenta casi a tamaño natural.
    private static Texture2D Wordmark()
    {
        var imp = AssetImporter.GetAtPath(WordmarkPath) as TextureImporter;
        if (imp == null)
        {
            Debug.LogWarning($"[Banner] No encontré {WordmarkPath}; el banner sale sin logo.");
            return null;
        }
        if (imp.textureCompression != TextureImporterCompression.Uncompressed || !imp.mipmapEnabled ||
            !imp.alphaIsTransparency || imp.npotScale != TextureImporterNPOTScale.None)
        {
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = true;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Trilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(WordmarkPath);
    }

    // Sin paredes: un piso oscuro y brillante que se pierde en la niebla, y detrás un telón
    // con un resplandor vino a la altura de las criaturas.
    private static void ArmarFondo(Transform raiz)
    {
        var piso = Primitiva(PrimitiveType.Plane, "Piso", raiz, Mat("Piso", new Color(0.045f, 0.03f, 0.032f), 0.62f));
        piso.transform.position = new Vector3(0f, 0f, 4f);
        piso.transform.localScale = new Vector3(4f, 1f, 4f);

        Texture2D fondo = Textura("Fondo", 256, 512, (u, v) =>
        {
            float d = new Vector2((u - 0.5f) * 1.5f, v - 0.4f).magnitude;
            Color brillo = new Color(0.42f, 0.07f, 0.09f);
            Color c = Color.Lerp(brillo, ColorNiebla, Mathf.SmoothStep(0f, 1f, d / 0.55f));
            c = Color.Lerp(c, new Color(0.008f, 0.004f, 0.008f), Mathf.SmoothStep(0.5f, 1f, v)); // arriba, negro
            return c;
        });
        var telon = Primitiva(PrimitiveType.Quad, "Telón", raiz, MatTextura("Telón", "Unlit/Texture", fondo));
        telon.transform.position = new Vector3(0f, 3.2f, 9f);
        telon.transform.localScale = new Vector3(14f, 9f, 1f);
        telon.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // El libro en el piso, abierto, con velas en círculo y un sigilo que brilla.
    private static void ArmarCirculoRitual(Transform raiz)
    {
        raiz.position = Ritual;

        var sigilo = Emisivo("Sigilo", new Color(1.6f, 0.1f, 0.12f));
        Linea("Círculo", raiz, sigilo, 0.022f,
            Enumerable.Range(0, 64).Select(i => Polar(0.66f, i / 64f * 360f)).ToArray());
        Linea("Círculo interior", raiz, sigilo, 0.01f,
            Enumerable.Range(0, 64).Select(i => Polar(0.58f, i / 64f * 360f)).ToArray());
        // Pentagrama: los 5 vértices salteando de a dos.
        Linea("Estrella", raiz, sigilo, 0.016f,
            Enumerable.Range(0, 5).Select(i => Polar(0.58f, 90f + i * 144f)).ToArray());

        // Resplandor rojo pegado al piso bajo el círculo.
        Billboard("Resplandor", raiz, Particula("Resplandor sigilo", Brillo(), new Color(0.6f, 0.06f, 0.05f)),
                  Ritual + Vector3.up * 0.01f, 2.4f, Quaternion.Euler(90f, 0f, 0f));

        var libro = InstanciarProp(LibroPrefab, raiz);
        if (libro != null)
        {
            Ajustar(libro, ancho: 0.42f);
            Apoyar(libro, 0f);
        }

        var velaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VelaPrefab);
        Material llama = Emisivo("Llama", new Color(1f, 0.72f, 0.32f) * 6f);
        Material halo = Particula("Halo vela", Brillo(), new Color(1f, 0.55f, 0.2f) * 0.7f);
        for (int i = 0; i < 5; i++)
        {
            Vector3 p = Ritual + Polar3(0.66f, 90f + i * 72f);
            float tope = 0.2f;
            var vela = velaPrefab != null ? InstanciarProp(VelaPrefab, raiz) : null;
            if (vela != null)
            {
                vela.transform.position = p;
                Ajustar(vela, alto: 0.2f);
                Apoyar(vela, 0f);
                tope = Bounds(vela).max.y;
            }
            Vector3 punta = new Vector3(p.x, tope + 0.025f, p.z);
            var fuego = Esfera("Llama", raiz, punta, 1f, llama);
            fuego.transform.localScale = new Vector3(0.018f, 0.045f, 0.018f);
            fuego.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Billboard("Halo", raiz, halo, punta, 0.35f, null);
        }
    }

    // Lo que da el "aire" de la foto: haces de luz desde arriba, humo a ras del piso
    // (que también tapa la línea donde el piso se junta con el telón) y brasas flotando.
    // Todo son quads mirando a la cámara con materiales de partícula: es una imagen fija.
    private static void ArmarAtmosfera(Transform raiz, Transform cam)
    {
        var rnd = new Random(1234);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        Quaternion Frente(float giro) => cam.rotation * Quaternion.Euler(0f, 0f, giro);

        Texture2D haz = TexturaHaz(false);
        Material hazFrio = Particula("Haz frío", haz, new Color(0.3f, 0.36f, 0.5f) * 0.55f, suave: true);
        Material hazRojo = Particula("Haz rojo", haz, new Color(0.55f, 0.07f, 0.05f) * 0.6f, suave: true);
        void Haz(Material m, Vector3 pie, float ancho, float alto, float inclinacion)
        {
            var q = Billboard("Haz", raiz, m, pie + Vector3.up * alto * 0.5f, 1f, Frente(inclinacion));
            q.transform.localScale = new Vector3(ancho, alto, 1f);
        }
        Haz(hazFrio, new Vector3(-0.2f, 0f, 1.2f), 1.6f, 5.5f, -8f);
        Haz(hazFrio, new Vector3(-1.1f, 0f, 2.4f), 0.9f, 5f, -14f);
        Haz(hazRojo, new Vector3(1.1f, 0f, 2.2f), 1.1f, 5f, 10f);

        Texture2D humo = TexturaHumo(false);
        Material humoNeutro = Particula("Humo", humo, new Color(0.32f, 0.24f, 0.27f, 0.22f), aditivo: false, suave: true);
        Material humoRojo = Particula("Humo rojo", humo, new Color(0.45f, 0.1f, 0.1f, 0.18f), aditivo: false, suave: true);
        for (int i = 0; i < 22; i++)
        {
            bool lejos = i >= 12;
            // Nada de humo pegado a la cámara: taparía a Veleth en primer plano.
            var pos = new Vector3(R(-2.5f, 2.5f), R(0.05f, lejos ? 0.9f : 0.4f), lejos ? R(4f, 8f) : R(-0.6f, 3.5f));
            float tam = lejos ? R(3.5f, 6f) : R(1.4f, 3f);
            var q = Billboard("Humo", raiz, i % 3 == 0 ? humoRojo : humoNeutro, pos, tam, Frente(R(0f, 360f)));
            q.transform.localScale = new Vector3(tam * 1.6f, tam * 0.7f, 1f); // aplastado: niebla baja
        }

        Material brasa = Particula("Brasa", Brillo(), new Color(1f, 0.42f, 0.12f) * 1.6f);
        for (int i = 0; i < 70; i++)
        {
            var pos = new Vector3(R(-1.2f, 1.2f), R(0.15f, 2.8f), R(-1f, 3f));
            Billboard("Brasa", raiz, brasa, pos, R(0.012f, 0.05f), Frente(0f));
        }
    }

    private static BannerPose CrearCriatura(string nombre, GameObject fbx, Scene scene, Vector3 pos,
                                            float yaw, Material mat, AnimationClip clip, float tiempo)
    {
        var pivote = new GameObject(nombre);
        pivote.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

        var modelo = (GameObject)PrefabUtility.InstantiatePrefab(fbx, scene);
        modelo.name = "modelo";
        modelo.transform.SetParent(pivote.transform, false);

        foreach (var smr in modelo.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // En edit mode los bounds no siguen a la pose muestreada: sin esto el modelo
            // puede desaparecer al encuadrar de cerca.
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.On;
            if (mat != null)
                smr.sharedMaterials = Enumerable.Repeat(mat, smr.sharedMaterials.Length).ToArray();
        }

        var pose = pivote.AddComponent<BannerPose>();
        pose.modelo = modelo.transform;
        pose.clip = clip;
        pose.tiempo = tiempo;
        pose.alturaPiso = 0f;
        if (clip == null) Debug.LogWarning($"[Banner] {nombre}: no encontré el clip; queda en la pose del FBX.");
        return pose;
    }

    // En juego Veleth usa VelethReveal, que es unlit (sólo albedo): en una foto con luces
    // queda plana. Para el banner se arma un PBR con sus propias texturas.
    private static Material MaterialVeleth()
    {
        string ruta = CarpetaMateriales + "/Veleth_Banner.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (mat == null)
        {
            Shader shader = Shader.Find("Mortuorium/Meshy Entity PBR") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, ruta);
        }
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(VelethTex + "Albedo.png"));
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(VelethTex + "Normal.png"));
        mat.SetTexture("_MetallicMap", AssetDatabase.LoadAssetAtPath<Texture2D>(VelethTex + "Metallic.png"));
        mat.SetTexture("_RoughnessMap", AssetDatabase.LoadAssetAtPath<Texture2D>(VelethTex + "Roughness.png"));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Luz de terror: la principal es cálida y viene de ABAJO (velas); dos contraluces fuertes
    // (rojo y frío) recortan las siluetas contra el fondo; una cenital fría da forma a
    // hombros y cabezas; y un relleno bajo evita que las caras sean una mancha negra.
    // Todo queda enganchado al panel "Iluminación" (BannerIluminacion).
    private static void ArmarLuces()
    {
        var raiz = new GameObject("Iluminación");
        Transform t = raiz.transform;
        Vector3 pechoSorken = PosSorken + new Vector3(0f, 1.4f, 0f);
        Vector3 entreAmbos = new Vector3(0f, 0.9f, 0.4f);

        var velas = Luz("Velas", t, LightType.Point, Ritual + new Vector3(0f, 0.35f, 0f), 5f);
        var contraRojo = Apuntar(Luz("Contraluz rojo", t, LightType.Spot, new Vector3(1.6f, 2.8f, 3.2f), 10f, 45f), pechoSorken);
        var contraFrio = Apuntar(Luz("Contraluz frío", t, LightType.Spot, new Vector3(-1.8f, 2.4f, 2.4f), 10f, 50f), entreAmbos);
        var cenital = Apuntar(Luz("Cenital", t, LightType.Spot, new Vector3(0.1f, 4.5f, 0.6f), 8f, 55f), Ritual);
        var relleno = Apuntar(Luz("Relleno", t, LightType.Spot, new Vector3(-1.2f, 1.5f, -3.2f), 10f, 60f), entreAmbos);
        var luna = Luz("Luna", t, LightType.Directional, Vector3.zero);
        luna.transform.rotation = Quaternion.Euler(35f, 200f, 0f);

        var panel = raiz.AddComponent<BannerIluminacion>();
        panel.luces = new[]
        {
            Canal("Velas (principal, desde abajo)", velas, new Color(1f, 0.6f, 0.28f), 4f, 0.85f),
            Canal("Contraluz rojo (Sorken)", contraRojo, new Color(1f, 0.16f, 0.1f), 9f, 1f),
            Canal("Contraluz frío (Veleth)", contraFrio, new Color(0.55f, 0.68f, 1f), 7f, 1f),
            Canal("Cenital", cenital, new Color(0.8f, 0.85f, 1f), 3.5f, 0.9f),
            Canal("Relleno frontal", relleno, new Color(0.6f, 0.62f, 0.85f), 0.7f, 0.3f),
            Canal("Luna (brillo del piso)", luna, new Color(0.45f, 0.55f, 0.9f), 0.15f, 0.6f),
        };
        panel.luzAmbiente = new Color(0.05f, 0.035f, 0.05f);
        panel.niebla = true;
        panel.colorNiebla = ColorNiebla;
        panel.densidadNiebla = 0.05f;
        panel.Aplicar();
    }

    private static BannerIluminacion.Canal Canal(string nombre, Light luz, Color color, float intensidad,
                                                 float sombra) =>
        new BannerIluminacion.Canal
        {
            nombre = nombre, luz = luz, color = color, intensidad = intensidad, sombra = sombra
        };

    // ---------------------------------------------------------------- render

    // Devuelve la ruta del PNG (null si no se pudo). El sufijo va en el nombre del archivo.
    private static string Renderizar(int ancho, int alto, int msaa, string sufijo = null, bool revelar = true)
    {
        var cam = GameObject.Find(NombreCamara)?.GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogError("[Banner] No está abierta la escena del banner. Corré Mortuorium > Banner > Crear escena " +
                           $"o abrí {ScenePath}.");
            return null;
        }
        if (Mathf.Max(ancho, alto) > SystemInfo.maxTextureSize)
        {
            Debug.LogError($"[Banner] La GPU admite texturas de hasta {SystemInfo.maxTextureSize} px; " +
                           $"no se puede renderizar {ancho}×{alto}.");
            return null;
        }
        // Estimación gruesa de VRAM: destino con MSAA + intermedia HDR con MSAA + profundidad +
        // las RT de los efectos (bloom, marco), ~8 bytes por píxel cada una.
        long estimadoMb = (long)ancho * alto * (8L * msaa + 4L * msaa + 32L) / (1024 * 1024);
        if (SystemInfo.graphicsMemorySize > 0 && estimadoMb > SystemInfo.graphicsMemorySize * 0.8f &&
            !EditorUtility.DisplayDialog("Banner",
                $"El render de {ancho}×{alto} necesita ~{estimadoMb / 1024f:0.0} GB de memoria de video y " +
                $"la GPU tiene {SystemInfo.graphicsMemorySize / 1024f:0.0} GB. Puede fallar o colgar Unity un rato.",
                "Renderizar igual", "Cancelar"))
            return null;

        foreach (var pose in Object.FindObjectsByType<BannerPose>()) pose.Aplicar();

        // La calidad (sombras, luces por píxel) la sube BannerCamaraFX en cada render de
        // esta cámara, así que acá alcanza con el RenderTexture.
        var rt = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            antiAliasing = msaa
        };
        var tex = new Texture2D(ancho, alto, TextureFormat.RGB24, false);
        string carpeta = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Renders");
        string nombre = sufijo == null ? "banner-sorken-veleth" : $"banner-sorken-veleth-{sufijo}";
        string ruta = Path.Combine(carpeta, $"{nombre}-{ancho}x{alto}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        try
        {
            EditorUtility.DisplayProgressBar("Banner", $"Renderizando {ancho}×{alto}…", 0.2f);
            try
            {
                cam.targetTexture = rt;
                cam.aspect = ancho / (float)alto;
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0);
                tex.Apply();
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                cam.ResetAspect();
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            EditorUtility.DisplayProgressBar("Banner", "Guardando el PNG…", 0.7f);
            Directory.CreateDirectory(carpeta);
            File.WriteAllBytes(ruta, tex.EncodeToPNG());
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Object.DestroyImmediate(tex);
        }

        Debug.Log($"[Banner] Render guardado en {ruta}");
        if (revelar) EditorUtility.RevealInFinder(ruta);
        return ruta;
    }

    // ---------------------------------------------------------------- helpers

    private static AnimationClip Clip(string ruta, string nombre) =>
        AssetDatabase.LoadAllAssetsAtPath(ruta).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__") && (nombre == null || c.name == nombre));

    private static void LogearCriatura(BannerPose pose, string fbx)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).Select(c => c.name);
        Bounds b = Bounds(pose.modelo.gameObject);
        Debug.Log($"[Banner] {pose.name}: alto ≈ {b.size.y:0.00} m, clip '{pose.clip?.name}'. " +
                  $"Clips del FBX: {string.Join(", ", clips)}");
    }

    private static GameObject InstanciarProp(string ruta, Transform padre)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
        if (prefab == null) { Debug.LogWarning($"[Banner] No encontré {ruta}."); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, padre);
        go.transform.SetParent(padre, false);
        // Sólo la parte visual: sin scripts de gameplay (libro, coleccionables), colliders
        // ni audio.
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        for (int pasada = 0; pasada < 3; pasada++)
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (c is MonoBehaviour || c is Collider || c is Rigidbody || c is AudioSource)
                    Object.DestroyImmediate(c);
        return go;
    }

    private static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static void Ajustar(GameObject go, float ancho = 0f, float alto = 0f)
    {
        Bounds b = Bounds(go);
        float actual = ancho > 0f ? Mathf.Max(b.size.x, b.size.z) : b.size.y;
        float objetivo = ancho > 0f ? ancho : alto;
        if (actual > 1e-4f) go.transform.localScale *= objetivo / actual;
    }

    private static void Apoyar(GameObject go, float y) =>
        go.transform.position += Vector3.up * (y - Bounds(go).min.y);

    private static GameObject Esfera(string nombre, Transform padre, Vector3 pos, float diametro, Material mat)
    {
        var go = Primitiva(PrimitiveType.Sphere, nombre, padre, mat);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * diametro;
        return go;
    }

    // Quad de efecto (haz, humo, brasa, halo). Sin rotación dada, mira a la cámara.
    private static GameObject Billboard(string nombre, Transform padre, Material mat, Vector3 pos, float tam,
                                        Quaternion? rot)
    {
        var go = Primitiva(PrimitiveType.Quad, nombre, padre, mat);
        go.transform.position = pos;
        var cam = GameObject.Find(NombreCamara);
        go.transform.rotation = rot ?? (cam != null ? cam.transform.rotation : Quaternion.identity);
        go.transform.localScale = Vector3.one * tam;
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    // CreatePrimitive es seguro acá (editor); igual se le saca el collider, que no aporta.
    private static GameObject Primitiva(PrimitiveType tipo, string nombre, Transform padre, Material mat)
    {
        var go = GameObject.CreatePrimitive(tipo);
        go.name = nombre;
        go.transform.SetParent(padre, true);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static void Linea(string nombre, Transform padre, Material mat, float ancho, Vector3[] puntos)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.localPosition = Vector3.up * 0.004f;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // acostada sobre el piso
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.alignment = LineAlignment.TransformZ;
        lr.positionCount = puntos.Length;
        lr.SetPositions(puntos);
        lr.widthMultiplier = ancho;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = ShadowCastingMode.Off;
    }

    // Punto en el plano XY local de una línea acostada (se rota 90° en X).
    private static Vector3 Polar(float r, float grados)
    {
        float a = grados * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
    }

    private static Vector3 Polar3(float r, float grados)
    {
        float a = grados * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
    }

    // Color e intensidad los pone el panel BannerIluminacion.
    private static Light Luz(string nombre, Transform padre, LightType tipo, Vector3 pos,
                             float alcance = 0f, float angulo = 0f)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = tipo;
        if (alcance > 0f) l.range = alcance;
        if (angulo > 0f) l.spotAngle = angulo;
        l.shadows = LightShadows.Soft;
        // La calidad Android del editor tiene 1 luz por píxel; el resto pasaría a per-vertex.
        l.renderMode = LightRenderMode.ForcePixel;
        return l;
    }

    private static Light Apuntar(Light l, Vector3 objetivo)
    {
        l.transform.LookAt(objetivo);
        return l;
    }

    // ---------------------------------------------------------------- materiales / texturas

    private static Material Mat(string nombre, Color color, float suavidad)
    {
        var mat = MaterialAsset(nombre, "Standard");
        mat.color = color;
        mat.SetFloat("_Glossiness", suavidad);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Unlit: no depende de las luces (llamas, sigilo). Colores > 1 a propósito: con la
    // cámara en HDR es lo que hace que el bloom los haga brillar.
    private static Material Emisivo(string nombre, Color color)
    {
        var mat = MaterialAsset(nombre, "Unlit/Color");
        mat.color = color;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material MatTextura(string nombre, string shader, Texture2D tex)
    {
        var mat = MaterialAsset(nombre, shader);
        mat.mainTexture = tex;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Materiales de partícula: el color sale de _TintColor × textura (× 2). Los planos
    // grandes (humo, haces) usan BannerNiebla, que se desvanece contra lo que tiene detrás
    // y cerca de la cámara; los chicos (brasas, halos, el resplandor acostado en el piso,
    // que con soft particles desaparecería) siguen con los legacy.
    private static Material Particula(string nombre, Texture2D tex, Color tinte, bool aditivo = true,
                                      bool suave = false)
    {
        string shader = suave ? ShaderNiebla
            : aditivo ? "Legacy Shaders/Particles/Additive" : "Legacy Shaders/Particles/Alpha Blended";
        var mat = MaterialAsset(nombre, shader);
        mat.mainTexture = tex;
        mat.SetColor("_TintColor", tinte);
        if (suave) ConfigurarNiebla(mat, aditivo);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private const string ShaderNiebla = "Mortuorium/Banner Niebla";

    private static void ConfigurarNiebla(Material mat, bool aditivo)
    {
        mat.SetFloat("_Aditivo", aditivo ? 1f : 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)(aditivo ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
    }

    // Aplica el difuminado a una escena ya armada SIN tocarla: rehace las texturas del haz
    // y del humo (mismo archivo → misma referencia) y pasa sus 4 materiales a BannerNiebla
    // conservando tinte y textura. Encuadre, poses y luces quedan como estaban.
    [MenuItem(Menu + "Suavizar bordes de la atmósfera")]
    private static void SuavizarAtmosfera()
    {
        Carpetas();
        TexturaHaz(true);
        TexturaHumo(true);
        if (Shader.Find(ShaderNiebla) == null)
        {
            Debug.LogError($"[Banner] No encontré el shader '{ShaderNiebla}' (Assets/Promo/BannerNiebla.shader).");
            return;
        }
        foreach (var (nombre, aditivo) in new[] { ("Haz frío", true), ("Haz rojo", true), ("Humo", false), ("Humo rojo", false) })
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{CarpetaMateriales}/Banner_{nombre}.mat");
            if (mat == null) { Debug.LogWarning($"[Banner] No está el material Banner_{nombre}."); continue; }
            mat.shader = Shader.Find(ShaderNiebla); // _MainTex y _TintColor se conservan (mismo nombre)
            ConfigurarNiebla(mat, aditivo);
            EditorUtility.SetDirty(mat);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Banner] Atmósfera suavizada. Si hace falta más, en cada material Banner_Humo / Banner_Haz " +
                  "subí 'Suavidad contra objetos' (y 'Cámara: aparece en' para lo que pasa cerca de la cámara).");
    }

    // Haz de luz: gaussiana de lado a lado, y se desvanece abajo Y arriba (antes terminaba
    // de golpe en el borde superior del plano).
    private static Texture2D TexturaHaz(bool rehacer) => Textura("Haz", 128, 512, (u, v) =>
    {
        float ancho = Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.17f, 2f));
        float alto = Mathf.SmoothStep(0f, 1f, v / 0.55f) * (1f - Mathf.SmoothStep(0.6f, 1f, v));
        float vetas = 0.8f + 0.2f * Mathf.PerlinNoise(u * 14f, 3.7f);
        return new Color(1f, 1f, 1f, ancho * alto * vetas);
    }, rehacer);

    // Humo: ruido fractal con un umbral suave (sin cortes) y una caída radial larga que
    // llega a cero bien antes del borde del plano.
    private static Texture2D TexturaHumo(bool rehacer) => Textura("Humo", 512, 512, (u, v) =>
    {
        float n = 0f, amp = 0.5f, f = 2.5f;
        for (int o = 0; o < 5; o++, amp *= 0.5f, f *= 2f) n += amp * Mathf.PerlinNoise(u * f + 11.3f, v * f + 5.1f);
        float r = new Vector2(u - 0.5f, v - 0.5f).magnitude / 0.48f;
        float borde = Mathf.Pow(Mathf.Clamp01(1f - r), 1.8f);
        return new Color(1f, 1f, 1f, Mathf.SmoothStep(0.25f, 0.8f, n) * borde);
    }, rehacer);

    private static Material MaterialAsset(string nombre, string shader)
    {
        string ruta = $"{CarpetaMateriales}/Banner_{nombre}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        Shader s = Shader.Find(shader) ?? Shader.Find("Sprites/Default");
        if (mat == null)
        {
            mat = new Material(s);
            AssetDatabase.CreateAsset(mat, ruta);
        }
        else mat.shader = s;
        return mat;
    }

    private static Texture2D Brillo() => Textura("Brillo", 128, 128, (u, v) =>
    {
        float r = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
        return new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
    });

    // Texturas procedurales guardadas como PNG en Assets/Promo/Texturas (así la escena
    // las referencia como cualquier asset). Se generan una vez; con rehacer se pisa el PNG
    // (misma ruta → mismo GUID, así la escena sigue apuntando al mismo asset).
    private static Texture2D Textura(string nombre, int w, int h, Func<float, float, Color> pixel,
                                     bool rehacer = false)
    {
        var existente = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CarpetaTexturas}/{nombre}.png");
        if (existente != null && !rehacer) return existente;

        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            px[y * w + x] = pixel((x + 0.5f) / w, (y + 0.5f) / h);
        return GuardarTextura(nombre, w, h, px);
    }

    // Escribe (o pisa) el PNG y lo importa. Con recorte, los mips conservan la cobertura del
    // alpha: si no, las líneas finas de una textura con alpha test desaparecen al alejarse.
    private static Texture2D GuardarTextura(string nombre, int w, int h, Color[] px, bool recorte = false,
                                            bool repetir = false)
    {
        string ruta = $"{CarpetaTexturas}/{nombre}.png";
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.SetPixels(px);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, ruta), t.EncodeToPNG());
        Object.DestroyImmediate(t);

        AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(ruta);
        imp.alphaIsTransparency = true;
        imp.wrapMode = repetir ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipMapsPreserveCoverage = recorte;
        imp.alphaTestReferenceValue = 0.5f;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
    }
}
