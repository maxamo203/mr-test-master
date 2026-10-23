using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

// Fondos alternativos del banner, para llenar el hueco oscuro entre el logo y las
// criaturas. Mortuorium > Banner > Agregar fondos alternativos los suma a la escena ya
// tuneada (sin tocar nada más) bajo "Fondos alternativos", y su BannerFondo elige cuál se ve:
//   - Ventanal: un ventanal gótico con luna, vitral rojo en el óculo y haces de luz que
//     bajan hasta el círculo ritual; tapa el telón con un muro de piedra.
//   - Sigilo: un sigilo enorme, como quemado en el aire, que hace de halo a las criaturas.
//   - Habitación: el cuarto como lo ve el escáner (líneas y grilla), con la puerta
//     entreabierta al cuarto de al lado, que tiene la luz prendida.
//   - Ojos: pares de ojos en la oscuridad (se pueden sumar encima de los otros).
//   - Foto: Texturas/Fondo living.jpg plana, de frente a la cámara y llenando el cuadro.
//   - Living: la misma foto armada en 3D (paredes con molduras, puerta entreabierta con
//     una luz cálida real del otro lado, sillón, alfombra y piso de madera brillante), así
//     las luces, la niebla y las sombras de la escena la atraviesan.
// Foto y Living ponen la niebla en azul oscuro (BannerFondo.nieblaAzul).
// Todo se ubica con rayos desde la cámara al aspecto del render (9:19), así que se acomoda
// al encuadre que tenga la escena. Volver a correr el menú los rehace y pisa sus texturas.
// "Renderizar cada fondo" saca una vista previa por variante para compararlas.
public static partial class BannerEntidadesSetup
{
    private const string NombreFondos = "Fondos alternativos";
    private const float AspectoBanner = 9f / 19f;

    [MenuItem(Menu + "Agregar fondos alternativos")]
    private static void AgregarFondos()
    {
        var cam = GameObject.Find(NombreCamara)?.GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogError($"[Banner] No está abierta la escena del banner ({ScenePath}).");
            return;
        }
        Carpetas();

        var variante = BannerFondo.Variante.Ventanal;
        bool ojosEncima = false;
        var viejo = GameObject.Find(NombreFondos);
        if (viejo != null)
        {
            var anterior = viejo.GetComponent<BannerFondo>();
            if (anterior != null) (variante, ojosEncima) = (anterior.variante, anterior.ojosEncima);
            Undo.DestroyObjectImmediate(viejo);
        }

        var raiz = new GameObject(NombreFondos);
        var fondo = raiz.AddComponent<BannerFondo>();
        Vector3 ritual = GameObject.Find("Círculo ritual")?.transform.position ?? Ritual;

        cam.aspect = AspectoBanner;
        try
        {
            EditorUtility.DisplayProgressBar("Banner", "Ventanal…", 0.1f);
            fondo.ventanal = Grupo("Ventanal", raiz.transform);
            ArmarVentanal(fondo.ventanal.transform, cam, ritual);
            EditorUtility.DisplayProgressBar("Banner", "Sigilo…", 0.45f);
            fondo.sigilo = Grupo("Sigilo", raiz.transform);
            ArmarSigilo(fondo.sigilo.transform, cam);
            EditorUtility.DisplayProgressBar("Banner", "Habitación…", 0.7f);
            fondo.habitacion = Grupo("Habitación", raiz.transform);
            ArmarHabitacion(fondo.habitacion.transform, cam);
            EditorUtility.DisplayProgressBar("Banner", "Ojos…", 0.8f);
            fondo.ojos = Grupo("Ojos", raiz.transform);
            ArmarOjos(fondo.ojos.transform, cam);
            EditorUtility.DisplayProgressBar("Banner", "Foto y living…", 0.9f);
            fondo.foto = Grupo("Foto", raiz.transform);
            ArmarFoto(fondo.foto.transform, cam);
            fondo.living = Grupo("Living", raiz.transform);
            ArmarLiving(fondo.living.transform, cam);
        }
        finally
        {
            cam.ResetAspect();
            EditorUtility.ClearProgressBar();
        }

        fondo.pisoOriginal = GameObject.Find("Piso")?.GetComponent<Renderer>();
        fondo.iluminacion = GameObject.Find("Iluminación")?.GetComponent<BannerIluminacion>();
        fondo.variante = variante;
        fondo.ojosEncima = ojosEncima;
        fondo.Aplicar();
        Undo.RegisterCreatedObjectUndo(raiz, "Fondos alternativos");
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(raiz.scene);
        Selection.activeGameObject = raiz;
        Debug.Log("[Banner] Fondos alternativos agregados. En el Inspector de \"Fondos alternativos\" se elige " +
                  "cuál se ve; Mortuorium > Banner > Renderizar cada fondo saca una vista previa de cada uno. " +
                  "Guardá la escena.");
    }

    [MenuItem(Menu + "Renderizar cada fondo (vista previa)")]
    private static void RenderFondos()
    {
        var fondo = GameObject.Find(NombreFondos)?.GetComponent<BannerFondo>();
        if (fondo == null)
        {
            Debug.LogError("[Banner] No están los fondos: Mortuorium > Banner > Agregar fondos alternativos.");
            return;
        }
        var (variante, ojosEncima) = (fondo.variante, fondo.ojosEncima);
        string ultimo = null;
        try
        {
            foreach (BannerFondo.Variante v in Enum.GetValues(typeof(BannerFondo.Variante)))
            {
                fondo.variante = v;
                fondo.ojosEncima = false;
                fondo.Aplicar();
                ultimo = Renderizar(1350, 2850, 8, "fondo-" + v.ToString().ToLowerInvariant(), revelar: false) ?? ultimo;
            }
        }
        finally
        {
            fondo.variante = variante;
            fondo.ojosEncima = ojosEncima;
            fondo.Aplicar();
        }
        if (ultimo != null) EditorUtility.RevealInFinder(ultimo);
    }

    private static GameObject Grupo(string nombre, Transform padre)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        return go;
    }

    // ---------------------------------------------------------------- ventanal

    // El ventanal en "unidades de ventana": 1 = ancho de su textura, el alto va de 0 a 2.
    // Arco ojival equilátero: cada lado es un arco con centro en el arranque del lado opuesto.
    private const float ZVentanal = 6f;
    private const float VenIzq = 0.06f, VenDer = 0.94f, VenArranque = 1.15f, VenBase = 0.04f;
    private const float VenMarco = 0.075f, VenArquivolta = 0.035f, VenParteluz = 0.045f, VenRombo = 0.055f;
    private const float VenOculoR = 0.115f, VenLunaR = 0.1f;
    private static readonly Vector2 VenOculo = new Vector2(0.5f, 1.6f);
    private static readonly Vector2 VenLuna = new Vector2(0.3f, 1.25f);

    // Altura de la punta del arco, moldura incluida.
    private static float VenApice
    {
        get
        {
            float w = VenDer - VenIzq, r = w + VenArquivolta;
            return VenArranque + Mathf.Sqrt(r * r - w * w * 0.25f);
        }
    }

    private static void ArmarVentanal(Transform g, Camera cam, Vector3 ritual)
    {
        // La punta del arco al pie del logo, en el hueco entre Sorken y Veleth (un poco a
        // la izquierda del centro); el alféizar casi en el piso, tapado por las criaturas.
        const float alfeizar = 0.25f;
        Vector3 punta = EnPlano(cam, 0.42f, 0.83f, ZVentanal);
        float ancho = Mathf.Max(0.8f, (punta.y - alfeizar) / (VenApice - VenBase));
        var origen = new Vector3(punta.x - ancho * 0.5f, alfeizar - VenBase * ancho, ZVentanal);
        Vector3 AMundo(float u, float y) => origen + new Vector3(u * ancho, y * ancho, 0f);
        var tam = new Vector2(ancho, 2f * ancho);

        // Muro: tapa todo el cuadro (y el telón) salvo el hueco del vidrio. El borde del hueco
        // cae debajo del marco del ventanal, así que alcanza con baja resolución.
        float mx0 = EnPlano(cam, -0.1f, 0.5f, ZVentanal).x, mx1 = EnPlano(cam, 1.1f, 0.5f, ZVentanal).x;
        float my0 = -0.05f, my1 = Mathf.Max(EnPlano(cam, 0.5f, 1.1f, ZVentanal).y, punta.y + 1f);
        const int th = 1024;
        int tw = Mathf.Clamp(Mathf.RoundToInt(th * (mx1 - mx0) / (my1 - my0)), 64, 2048);
        var muro = new Color[tw * th];
        for (int j = 0; j < th; j++)
        for (int i = 0; i < tw; i++)
        {
            float wx = mx0 + (i + 0.5f) / tw * (mx1 - mx0), wy = my0 + (j + 0.5f) / th * (my1 - my0);
            float h = HonduraVentanal((wx - origen.x) / ancho, (wy - origen.y) / ancho);
            muro[j * tw + i] = h >= VenMarco * 0.5f ? Color.clear : PiedraMuro(wx, wy, Mathf.Max(0f, -h) * ancho);
        }
        Plano("Muro", g, Recorte("Muro ventanal", GuardarTextura("Ventanal muro", tw, th, muro, recorte: true)),
              new Vector3((mx0 + mx1) * 0.5f, (my0 + my1) * 0.5f, ZVentanal + 0.02f), new Vector2(mx1 - mx0, my1 - my0));

        // Tracería (piedra + plomo, recortada) delante; el vidrio, aditivo, detrás del muro.
        Plano("Tracería", g, Recorte("Tracería ventanal", GuardarTextura("Ventanal tracería", 1024, 2048,
              Pixeles(1024, 2048, PixelVentana), recorte: true)), AMundo(0.5f, 1f) + Vector3.back * 0.01f, tam);
        Plano("Vidrio", g, Particula("Vidrio ventanal", GuardarTextura("Ventanal vidrio", 512, 1024,
              Pixeles(512, 1024, (x, y, _) => PixelVidrio(x, y))), new Color(1.1f, 1.1f, 1.1f, 0.5f)),
              AMundo(0.5f, 1f) + Vector3.forward * 0.06f, tam);

        Billboard("Resplandor", g, Particula("Halo ventanal", Brillo(), new Color(0.3f, 0.36f, 0.55f) * 0.5f),
                  AMundo(0.5f, 1.2f) + Vector3.back * 0.4f, ancho * 2.6f, null);

        // Haces: dos fríos desde las lancetas y uno rojo desde el óculo, hasta el círculo.
        Material hazLuna = Particula("Haz ventanal", TexturaHaz(false), new Color(0.32f, 0.4f, 0.6f) * 0.5f, suave: true);
        Material hazOculo = Particula("Haz óculo", TexturaHaz(false), new Color(0.6f, 0.07f, 0.05f) * 0.45f, suave: true);
        var haces = new[]
        {
            (AMundo(0.31f, 0.9f), ritual + new Vector3(-0.6f, 0f, -0.5f), ancho * 0.45f, hazLuna),
            (AMundo(0.69f, 0.9f), ritual + new Vector3(0.5f, 0f, -0.3f), ancho * 0.45f, hazLuna),
            (AMundo(0.5f, 1.6f), ritual, ancho * 0.3f, hazOculo),
        };
        Material polvo = Particula("Polvo ventanal", Brillo(), new Color(0.7f, 0.76f, 0.95f) * 0.55f);
        var rnd = new Random(4321);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        foreach (var (desde, hasta, grosor, mat) in haces)
        {
            HazEntre("Haz", g, mat, desde, hasta, grosor);
            for (int i = 0; i < 30; i++)
            {
                Vector3 p = Vector3.Lerp(desde, hasta, R(0.15f, 0.95f)) +
                            new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)) * grosor * 0.4f;
                Billboard("Polvo", g, polvo, p, R(0.006f, 0.018f), null);
            }
        }

        // La luna también ilumina: contraluz frío desde el ventanal sobre el círculo y las
        // espaldas de las criaturas. No está en el panel "Iluminación" (se apaga con el grupo).
        var luna = Luz("Luna del ventanal", g, LightType.Spot, AMundo(0.5f, 1.1f) + Vector3.back * 0.3f, 16f, 38f);
        Apuntar(luna, ritual + Vector3.up * 0.9f);
        luna.color = new Color(0.55f, 0.66f, 1f);
        luna.intensity = 2.2f;
    }

    // > 0 adentro del arco ojival del ventanal (con 0 en su borde exterior), < 0 afuera.
    private static float HonduraVentanal(float x, float y) =>
        Hondura(x, y, VenIzq, VenDer, VenArranque, VenBase);

    // Las dos lancetas, separadas por el parteluz central.
    private static float HonduraLancetas(float x, float y)
    {
        float baseY = VenBase + VenMarco;
        return Mathf.Max(Hondura(x, y, VenIzq + VenMarco, 0.5f - VenParteluz * 0.5f, VenArranque, baseY),
                         Hondura(x, y, 0.5f + VenParteluz * 0.5f, VenDer - VenMarco, VenArranque, baseY));
    }

    // Cuánto adentro de un arco ojival está el punto, en las mismas unidades: Hondura >= d es
    // el mismo arco achicado d por todos lados (mismos centros, radio menos d).
    private static float Hondura(float x, float y, float izq, float der, float arranque, float baseY)
    {
        float w = der - izq, d = y - baseY;
        if (y <= arranque) return Mathf.Min(d, Mathf.Min(x - izq, der - x));
        var p = new Vector2(x, y);
        return Mathf.Min(d, Mathf.Min(w - Vector2.Distance(p, new Vector2(der, arranque)),
                                      w - Vector2.Distance(p, new Vector2(izq, arranque))));
    }

    private static Color PixelVentana(float x, float y, float px)
    {
        float hv = HonduraVentanal(x, y);
        if (hv < -VenArquivolta) return Color.clear;
        Color piedra = new Color(0.035f, 0.03f, 0.037f) * (0.75f + 0.5f * Fbm(x * 14f, y * 14f, 4));
        if (hv < 0f) // arquivolta: moldura con una media caña al medio
            return Opaco(piedra * (Mathf.Abs(hv + VenArquivolta * 0.5f) < 0.004f ? 0.5f : 1.15f));
        if (hv < VenMarco) // derrame del muro: la luna lo aclara hacia el vidrio
        {
            float t = hv / VenMarco;
            return Opaco(Color.Lerp(piedra, new Color(0.13f, 0.15f, 0.21f) * (0.8f + 0.4f * Fbm(x * 9f, y * 9f, 3)), t * t));
        }
        float hl = HonduraLancetas(x, y);
        float oc = VenOculoR - Vector2.Distance(new Vector2(x, y), VenOculo);
        if (hl < 0f && oc < 0f) // tracería de placa; el canto que da al vidrio agarra luz
            return Opaco(Color.Lerp(piedra * 0.8f, new Color(0.1f, 0.12f, 0.17f), Suave(-0.012f, 0f, Mathf.Max(hl, oc))));
        float barras = hl >= 0f ? BarrasLanceta(x, y, px) : BarrasOculo(x, y, px);
        return new Color(0.012f, 0.01f, 0.014f, barras);
    }

    // Plomo en rombos y barras de hierro horizontales.
    private static float BarrasLanceta(float x, float y, float px)
    {
        float p1 = 0.866f * x + 0.5f * y, p2 = -0.866f * x + 0.5f * y;
        float c = Mathf.Max(Cobertura(DistRed(p1, VenRombo), 0.0035f, px), Cobertura(DistRed(p2, VenRombo), 0.0035f, px));
        for (float b = 0.35f; b < 1.2f; b += 0.25f) c = Mathf.Max(c, Cobertura(Mathf.Abs(y - b), 0.008f, px));
        return c;
    }

    // Rosetón: anillo central y ocho rayos.
    private static float BarrasOculo(float x, float y, float px)
    {
        Vector2 d = new Vector2(x, y) - VenOculo;
        float r = d.magnitude;
        float c = Cobertura(Mathf.Abs(r - 0.035f), 0.008f, px);
        if (r > 0.035f)
        {
            float ang = Mathf.Atan2(d.y, d.x), sector = Mathf.PI / 4f;
            c = Mathf.Max(c, Cobertura(Mathf.Abs(Mathf.Sin(ang - Mathf.Round(ang / sector) * sector)) * r, 0.008f, px));
        }
        return c;
    }

    // El vidrio: cielo nublado frío con la luna asomando en la lanceta izquierda; cada rombo
    // con su tono (alguno roto, más claro); el óculo, vitral rojo y ámbar.
    private static Color PixelVidrio(float x, float y)
    {
        var negro = new Color(0f, 0f, 0f, 1f);
        if (HonduraVentanal(x, y) < VenMarco - 0.01f) return negro;
        var p = new Vector2(x, y);
        float hl = HonduraLancetas(x, y);
        float oc = VenOculoR - Vector2.Distance(p, VenOculo);
        // Un margen por debajo de la piedra: sin él queda una costura negra en el recorte.
        if (hl < -0.01f && oc < -0.01f) return negro;

        Color c;
        if (oc > -0.01f && y > VenOculo.y - VenOculoR - 0.02f)
        {
            float ang = Mathf.Atan2(p.y - VenOculo.y, p.x - VenOculo.x);
            int sector = Mathf.FloorToInt((ang + Mathf.PI) / (Mathf.PI / 4f));
            Color vitral = (sector & 1) == 0 ? new Color(0.85f, 0.1f, 0.07f) : new Color(0.95f, 0.4f, 0.07f);
            float rr = Mathf.Clamp01(Vector2.Distance(p, VenOculo) / VenOculoR);
            c = vitral * (0.55f + 0.6f * (1f - rr)) * (0.8f + 0.4f * Fbm(x * 20f, y * 20f, 3));
        }
        else
        {
            float nube = Fbm(x * 4f + 3f, y * 2.5f, 5);
            c = Color.Lerp(new Color(0.09f, 0.11f, 0.18f), new Color(0.3f, 0.36f, 0.5f), Suave(0.1f, 1.7f, y));
            c *= 0.5f + 0.9f * nube;
            float dl = Vector2.Distance(p, VenLuna);
            float disco = Suave(VenLunaR, VenLunaR - 0.006f, dl) * (1f - 0.5f * Suave(0.55f, 0.8f, nube));
            c = Color.Lerp(c, new Color(1f, 0.96f, 0.88f) * (0.8f + 0.25f * Fbm(x * 35f, y * 35f, 3)), disco);
            c += new Color(0.4f, 0.46f, 0.6f) * (0.7f * Mathf.Exp(-Mathf.Max(0f, dl - VenLunaR) / 0.07f));
            int ca = Mathf.FloorToInt((0.866f * x + 0.5f * y) / VenRombo);
            int cb = Mathf.FloorToInt((-0.866f * x + 0.5f * y) / VenRombo);
            c *= Hash(ca, cb, 7) < 0.05f ? 1.45f : 0.82f + 0.3f * Hash(ca, cb);
        }
        return new Color(Mathf.Min(1f, c.r), Mathf.Min(1f, c.g), Mathf.Min(1f, c.b), 1f);
    }

    // Sillería del muro, en metros. dFuera = distancia al ventanal (la luna derrama sobre la
    // piedra de alrededor); abajo la tiñen las velas y arriba se pierde en negro.
    private static Color PiedraMuro(float x, float y, float dFuera)
    {
        const float hilada = 0.32f, largo = 0.62f, junta = 0.012f;
        int fila = Mathf.FloorToInt(y / hilada);
        float bx = (x + Hash(fila, 3) * largo) / largo;
        int bloque = Mathf.FloorToInt(bx);
        float fx = (bx - bloque) * largo, fy = y - fila * hilada;
        float tono = (0.75f + 0.5f * Hash(fila, bloque)) * (0.7f + 0.6f * Fbm(x * 3f, y * 3f, 4));
        if (Mathf.Min(fx, largo - fx) < junta || Mathf.Min(fy, hilada - fy) < junta) tono *= 0.45f;
        Color c = new Color(0.045f, 0.036f, 0.044f) * tono;
        c += new Color(0.07f, 0.09f, 0.14f) * Mathf.Exp(-dFuera / 0.45f);
        c += new Color(0.06f, 0.012f, 0.01f) * Mathf.Exp(-Mathf.Max(0f, y) / 0.7f);
        c *= 1f - 0.75f * Suave(2.5f, 5f, y);
        return Opaco(c);
    }

    // Haz de luz de un punto a otro: un quad con el eje largo de desde a hasta, girado sobre
    // ese eje para mirar a la cámara. A diferencia de los haces de ArmarAtmosfera (planos
    // frente a la cámara) atraviesa la escena en profundidad: pasa por delante o por detrás
    // de cada criatura según dónde esté, y BannerNiebla lo desvanece contra el piso.
    private static void HazEntre(string nombre, Transform padre, Material mat, Vector3 desde, Vector3 hasta, float ancho)
    {
        var cam = GameObject.Find(NombreCamara).transform;
        Vector3 eje = hasta - desde, medio = (desde + hasta) * 0.5f;
        Vector3 haciaCamara = Vector3.ProjectOnPlane(cam.position - medio, eje.normalized);
        var q = Billboard(nombre, padre, mat, medio, 1f, Quaternion.LookRotation(-haciaCamara, eje));
        q.transform.localScale = new Vector3(ancho, eje.magnitude * 1.2f, 1f); // la textura se apaga en las puntas
    }

    // ---------------------------------------------------------------- sigilo

    private const float ZSigilo = 6.5f;
    private static readonly Vector2 SigCentro = new Vector2(0.5f, 1.12f); // en la textura: x 0..1, y 0..2

    private static void ArmarSigilo(Transform g, Camera cam)
    {
        // El círculo en el hueco, del 85 % del ancho del cuadro; el eje vertical sigue
        // hacia arriba (bajo el logo) y hacia abajo (detrás de las criaturas).
        Vector3 centro = EnPlano(cam, 0.45f, 0.6f, ZSigilo);
        float ancho = 0.85f * (EnPlano(cam, 1f, 0.6f, ZSigilo).x - EnPlano(cam, 0f, 0.6f, ZSigilo).x);
        Vector3 medio = centro + new Vector3((0.5f - SigCentro.x) * ancho, (1f - SigCentro.y) * ancho, 0f);
        Texture2D tex = GuardarTextura("Sigilo fondo", 1024, 2048, PixelesSigilo(1024, 2048));
        // Una mancha oscura detrás, para que el sigilo no se lave contra el resplandor del telón.
        Billboard("Quemadura", g, Particula("Quemadura sigilo", Brillo(), new Color(0f, 0f, 0f, 0.3f), aditivo: false),
                  centro + Vector3.forward * 0.1f, ancho * 1.3f, Quaternion.identity);
        Plano("Sigilo", g, Particula("Sigilo fondo", tex, new Color(0.8f, 0.8f, 0.8f, 0.5f)), medio,
              new Vector2(ancho, 2f * ancho));
    }

    private static Color[] PixelesSigilo(int w, int h)
    {
        var rnd = new Random(77);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        Vector2 c = SigCentro;
        Vector2 Pol(float r, float grados) =>
            c + new Vector2(Mathf.Cos(grados * Mathf.Deg2Rad), Mathf.Sin(grados * Mathf.Deg2Rad)) * r;

        var lz = new Lienzo(w, h);
        lz.Circulo(c, 0.45f, 0.007f);
        lz.Circulo(c, 0.425f, 0.003f);
        lz.Circulo(c, 0.345f, 0.005f);

        // Heptagrama {7/3}, con un círculo chico en cada punta.
        for (int i = 0; i < 7; i++)
        {
            lz.Segmento(Pol(0.335f, 90f + i * 360f / 7f), Pol(0.335f, 90f + (i + 3) * 360f / 7f), 0.005f);
            lz.Circulo(Pol(0.335f, 90f + i * 360f / 7f), 0.02f, 0.003f);
        }

        // Anillo de runas: un tallo (o dos) y de una a tres ramas, paradas hacia afuera.
        const int runas = 28;
        for (int k = 0; k < runas; k++)
        {
            float a = (90f + (k + 0.5f) * 360f / runas) * Mathf.Deg2Rad;
            var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var t = new Vector2(-n.y, n.x);
            Vector2 L(float lx, float ly) => c + n * (0.385f + ly * 0.046f) + t * (lx * 0.024f);
            if (rnd.NextDouble() < 0.8) lz.Segmento(L(0f, -0.5f), L(0f, 0.5f), 0.0035f);
            else
            {
                lz.Segmento(L(-0.3f, -0.5f), L(-0.3f, 0.5f), 0.0035f);
                lz.Segmento(L(0.3f, -0.5f), L(0.3f, 0.5f), 0.0035f);
            }
            int ramas = rnd.Next(1, 4);
            for (int r = 0; r < ramas; r++)
            {
                float s = R(-0.4f, 0.4f);
                lz.Segmento(L(0f, s), L(rnd.NextDouble() < 0.5 ? -0.5f : 0.5f, Mathf.Clamp(s + R(-0.35f, 0.35f), -0.5f, 0.5f)), 0.0035f);
            }
        }

        // Un ojo al centro.
        lz.Circulo(c, 0.13f, 0.004f);
        var arriba = new List<Vector2>();
        var abajo = new List<Vector2>();
        for (int i = 0; i <= 24; i++)
        {
            float x = -0.1f + i * 0.2f / 24f, alto = 0.05f * (1f - x / 0.1f * (x / 0.1f));
            arriba.Add(c + new Vector2(x, alto));
            abajo.Add(c + new Vector2(x, -alto));
        }
        lz.Polilinea(arriba, 0.004f);
        lz.Polilinea(abajo, 0.004f);
        lz.Circulo(c, 0.034f, 0.004f);
        lz.Rellenar(c - new Vector2(0.01f, 0.03f), c + new Vector2(0.01f, 0.03f), p =>
        {
            Vector2 d = p - c;
            float f = d.y / 0.03f;
            return Mathf.Abs(d.x) < 0.007f * (1f - f * f);
        });

        // Eje vertical: hacia arriba remata en un círculo con travesaño; hacia abajo, en punta.
        lz.Segmento(c + Vector2.up * 0.45f, new Vector2(0.5f, 1.86f), 0.005f);
        lz.Circulo(new Vector2(0.5f, 1.9f), 0.04f, 0.005f);
        lz.Segmento(new Vector2(0.42f, 1.72f), new Vector2(0.58f, 1.72f), 0.004f);
        lz.Segmento(c + Vector2.down * 0.45f, new Vector2(0.5f, 0.34f), 0.005f);
        lz.Polilinea(new[] { new Vector2(0.45f, 0.34f), new Vector2(0.55f, 0.34f), new Vector2(0.5f, 0.2f) }, 0.004f, cerrada: true);
        lz.Segmento(new Vector2(0.44f, 0.52f), new Vector2(0.56f, 0.52f), 0.004f);

        // Medialunas a los costados y grietas que salen del anillo.
        lz.Arco(c, 0.49f, 160f, 200f, 0.004f);
        lz.Arco(c, 0.49f, -20f, 20f, 0.004f);
        for (int i = 0; i < 12; i++)
        {
            float rumbo = R(0f, 360f);
            Grieta(lz, rnd, Pol(0.455f, rumbo), rumbo, 0.7f, 0);
        }

        // Color: núcleo anaranjado donde está el trazo y un halo rojo alrededor (el bloom de
        // la cámara hace el resto). Se apaga contra los bordes del plano.
        var halo = lz.Copia();
        halo.Desenfocar(Mathf.Max(2, w / 80), 3);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
            float borde = Suave(0f, 0.04f, u) * Suave(1f, 0.96f, u) * Suave(0f, 0.02f, v) * Suave(1f, 0.98f, v);
            float m = lz.A[i];
            Color col = new Color(1f, 0.12f, 0.06f) * (Mathf.Min(1f, halo.A[i] * 2.2f) * 0.55f)
                        + new Color(1f, 0.42f, 0.18f) * m + new Color(1f, 0.85f, 0.6f) * (m * m * 0.3f);
            col *= borde;
            px[i] = new Color(Mathf.Min(1f, col.r), Mathf.Min(1f, col.g), Mathf.Min(1f, col.b), 1f);
        }
        return px;
    }

    // Grieta quebrada que se aleja del sigilo y se va apagando; a veces se bifurca.
    private static void Grieta(Lienzo lz, Random rnd, Vector2 desde, float rumbo, float valor, int nivel)
    {
        float rumboBase = rumbo;
        int pasos = rnd.Next(7, 14);
        Vector2 p = desde;
        for (int s = 0; s < pasos; s++)
        {
            rumbo = Mathf.Lerp(rumbo, rumboBase, 0.3f) + ((float)rnd.NextDouble() - 0.5f) * 70f;
            float largo = 0.015f + (float)rnd.NextDouble() * 0.025f;
            Vector2 q = p + new Vector2(Mathf.Cos(rumbo * Mathf.Deg2Rad), Mathf.Sin(rumbo * Mathf.Deg2Rad)) * largo;
            float f = 1f - s / (float)pasos;
            lz.Segmento(p, q, 0.0015f + 0.002f * f, valor * (0.25f + 0.75f * f));
            if (nivel == 0 && rnd.NextDouble() < 0.2)
                Grieta(lz, rnd, q, rumbo + (rnd.NextDouble() < 0.5 ? -40f : 40f), valor * 0.6f * f, 1);
            p = q;
        }
    }

    // ---------------------------------------------------------------- habitación

    private const float ZHabitacion = 5f, AltoHabitacion = 2.6f;

    // El cuarto tal como lo deja el escáner: superficies oscuras con sus aristas y una grilla
    // de 50 cm, un mueble como cubo y dos identificadores. La puerta del fondo está
    // entreabierta y del otro lado hay luz prendida.
    private static void ArmarHabitacion(Transform g, Camera cam)
    {
        float zB = ZHabitacion, zF = cam.transform.position.z + 0.3f, alto = AltoHabitacion;
        float X(float vx) => EnPlano(cam, vx, 0.5f, zB).x;
        float xL = X(0.03f), xR = X(0.97f);
        const float e = 0.006f; // las líneas se despegan de las superficies hacia adentro

        Material linea = Emisivo("Escáner línea", new Color(0.3f, 1.5f, 1.35f));
        Material grilla = Emisivo("Escáner grilla", new Color(0.05f, 0.32f, 0.3f));
        Material oscuro = Emisivo("Habitación pared", new Color(0.018f, 0.014f, 0.02f));

        var puerta = new Rect(X(0.4f), 0f, X(0.62f) - X(0.4f), 2.05f);
        var ventana = new Rect(X(0.07f), 1f, X(0.27f) - X(0.07f), 1f);
        MuroConHuecos("Pared fondo", g, oscuro, xL, xR, alto, zB, puerta, ventana);
        float largo = zB - zF, zMedio = (zF + zB) * 0.5f;
        Plano("Pared izquierda", g, oscuro, new Vector3(xL, alto * 0.5f, zMedio), new Vector2(largo, alto), Quaternion.Euler(0f, -90f, 0f));
        Plano("Pared derecha", g, oscuro, new Vector3(xR, alto * 0.5f, zMedio), new Vector2(largo, alto), Quaternion.Euler(0f, 90f, 0f));
        Plano("Techo", g, oscuro, new Vector3((xL + xR) * 0.5f, alto, zMedio), new Vector2(xR - xL, largo), Quaternion.Euler(-90f, 0f, 0f));

        // Aristas.
        float l = xL + e, r = xR - e, f = zB - e, t = alto - e;
        Trazo("Aristas", g, linea, 0.012f, new Vector3(l, e, f), new Vector3(r, e, f), new Vector3(r, t, f),
              new Vector3(l, t, f), new Vector3(l, e, f));
        Trazo("Aristas", g, linea, 0.012f, new Vector3(l, e, zF), new Vector3(l, e, f));
        Trazo("Aristas", g, linea, 0.012f, new Vector3(r, e, zF), new Vector3(r, e, f));
        Trazo("Aristas", g, linea, 0.012f, new Vector3(l, t, zF), new Vector3(l, t, f));
        Trazo("Aristas", g, linea, 0.012f, new Vector3(r, t, zF), new Vector3(r, t, f));

        // Grilla: pared del fondo (cortada por los huecos), paredes laterales y techo.
        const float paso = 0.5f, fina = 0.004f;
        for (float x = Mathf.Ceil(xL / paso) * paso; x < xR - 0.05f; x += paso)
        {
            if (x - xL < 0.05f) continue;
            foreach (var (y0, y1) in Tramos(0f, alto, x, true, puerta, ventana))
                Trazo("Grilla", g, grilla, fina, new Vector3(x, y0, f), new Vector3(x, y1, f));
            Trazo("Grilla", g, grilla, fina, new Vector3(x, t, zF), new Vector3(x, t, f));
        }
        for (float y = paso; y < alto - 0.05f; y += paso)
        {
            foreach (var (x0, x1) in Tramos(xL, xR, y, false, puerta, ventana))
                Trazo("Grilla", g, grilla, fina, new Vector3(x0, y, f), new Vector3(x1, y, f));
            Trazo("Grilla", g, grilla, fina, new Vector3(l, y, zF), new Vector3(l, y, f));
            Trazo("Grilla", g, grilla, fina, new Vector3(r, y, zF), new Vector3(r, y, f));
        }
        for (float z = Mathf.Ceil(zF / paso) * paso; z < zB - 0.05f; z += paso)
        {
            Trazo("Grilla", g, grilla, fina, new Vector3(l, e, z), new Vector3(l, t, z));
            Trazo("Grilla", g, grilla, fina, new Vector3(r, e, z), new Vector3(r, t, z));
            Trazo("Grilla", g, grilla, fina, new Vector3(l, t, z), new Vector3(r, t, z));
        }

        // Puerta entreabierta hacia el otro cuarto: la hoja gira sobre la bisagra derecha.
        float dx0 = puerta.xMin, dx1 = puerta.xMax, dh = puerta.yMax, dw = puerta.width;
        Trazo("Marco puerta", g, linea, 0.012f, new Vector3(dx0, e, f), new Vector3(dx0, dh, f),
              new Vector3(dx1, dh, f), new Vector3(dx1, e, f));
        float apertura = 38f * Mathf.Deg2Rad;
        var bisagra = new Vector3(dx1, 0f, zB);
        Vector3 hoja = new Vector3(-Mathf.Cos(apertura), 0f, Mathf.Sin(apertura)) * dw;
        Plano("Hoja", g, oscuro, bisagra + hoja * 0.5f + Vector3.up * (dh * 0.5f), new Vector2(dw, dh),
              Quaternion.LookRotation(new Vector3(Mathf.Sin(apertura), 0f, Mathf.Cos(apertura))));
        Trazo("Hoja", g, linea, 0.008f, bisagra + Vector3.up * e, bisagra + hoja + Vector3.up * e,
              bisagra + hoja + Vector3.up * dh, bisagra + Vector3.up * dh);
        Plano("Luz del otro cuarto", g, Emisivo("Luz puerta", new Color(1.9f, 1.05f, 0.55f)),
              new Vector3((dx0 + dx1) * 0.5f, dh * 0.55f, zB + 0.9f), new Vector2(dw * 1.6f, dh * 1.3f));
        Texture2D cuna = Textura("Cuña de luz", 128, 256, (u, v) =>
        {
            float ancho = 0.1f + 0.32f * (1f - v);
            return new Color(1f, 1f, 1f, Mathf.Exp(-Mathf.Pow((u - 0.5f) / ancho, 2f) * 2f) * Mathf.Pow(v, 1.6f));
        }, rehacer: true);
        // Acostada en el piso: el borde v = 1 (el más brillante) queda contra la puerta.
        Plano("Luz en el piso", g, Particula("Luz puerta piso", cuna, new Color(1f, 0.6f, 0.3f) * 0.5f),
              new Vector3((dx0 + dx1 + hoja.x) * 0.5f, 0.004f, zB - 1.1f), new Vector2(1.3f, 2.2f), Quaternion.Euler(90f, 0f, 0f));

        // Ventana con la noche y la luna detrás.
        float vx0 = ventana.xMin, vx1 = ventana.xMax, vy0 = ventana.yMin, vy1 = ventana.yMax, vxm = ventana.center.x, vym = ventana.center.y;
        Trazo("Marco ventana", g, linea, 0.012f, new Vector3(vx0, vy0, f), new Vector3(vx1, vy0, f),
              new Vector3(vx1, vy1, f), new Vector3(vx0, vy1, f), new Vector3(vx0, vy0, f));
        Trazo("Cruz ventana", g, linea, 0.008f, new Vector3(vxm, vy0, f), new Vector3(vxm, vy1, f));
        Trazo("Cruz ventana", g, linea, 0.008f, new Vector3(vx0, vym, f), new Vector3(vx1, vym, f));
        Plano("Noche", g, Emisivo("Noche ventana", new Color(0.12f, 0.16f, 0.28f)),
              new Vector3(vxm, vym, zB + 0.4f), new Vector2(ventana.width * 1.5f, ventana.height * 1.5f));
        Billboard("Luna", g, Particula("Luna ventana", Brillo(), new Color(1f, 0.95f, 0.85f)),
                  new Vector3(vx1 - ventana.width * 0.3f, vy1 - 0.25f, zB + 0.35f), 0.3f, Quaternion.identity);

        // Lo que agrega el jugador al escanear: un mueble como cubo e identificadores.
        Caja(g, linea, new Vector3(xL + 0.08f, e, zB - 0.6f), new Vector3(xL + 0.95f, 0.85f, zB - 0.06f));
        Marcador(g, Emisivo("Marcador rojo", new Color(1.8f, 0.18f, 0.12f)), new Vector3(dx0 - 0.18f, 1.25f, zB - 0.04f));
        Marcador(g, Emisivo("Marcador ámbar", new Color(1.8f, 1f, 0.2f)), new Vector3(vx1 + 0.15f, 1.5f, zB - 0.04f));
    }

    // Pared rectangular (de 0 a alto) partida en franjas para dejar libres los huecos.
    private static void MuroConHuecos(string nombre, Transform g, Material mat, float x0, float x1, float alto,
                                      float z, params Rect[] huecos)
    {
        var xs = new List<float> { x0, x1 };
        foreach (var h in huecos) xs.AddRange(new[] { h.xMin, h.xMax });
        xs.Sort();
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            float a = xs[i], b = xs[i + 1];
            if (b - a < 1e-3f) continue;
            float medio = (a + b) * 0.5f;
            foreach (var (y0, y1) in Tramos(0f, alto, medio, true, huecos))
                Plano(nombre, g, mat, new Vector3(medio, (y0 + y1) * 0.5f, z), new Vector2(b - a, y1 - y0));
        }
    }

    // Las partes del tramo [a, b] (vertical en x = fijo, u horizontal en y = fijo) que no
    // caen dentro de ningún hueco.
    private static IEnumerable<(float, float)> Tramos(float a, float b, float fijo, bool vertical, params Rect[] huecos)
    {
        var cortes = new List<(float, float)>();
        foreach (var h in huecos)
            if (vertical ? fijo > h.xMin && fijo < h.xMax : fijo > h.yMin && fijo < h.yMax)
                cortes.Add(vertical ? (h.yMin, h.yMax) : (h.xMin, h.xMax));
        cortes.Sort();
        float desde = a;
        foreach (var (c0, c1) in cortes)
        {
            if (c0 > desde + 0.01f) yield return (desde, Mathf.Min(c0, b));
            desde = Mathf.Max(desde, c1);
        }
        if (b > desde + 0.01f) yield return (desde, b);
    }

    private static void Caja(Transform g, Material mat, Vector3 a, Vector3 b)
    {
        Trazo("Cubo", g, mat, 0.01f, new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, a.y, b.z),
              new Vector3(a.x, a.y, b.z), new Vector3(a.x, a.y, a.z));
        Trazo("Cubo", g, mat, 0.01f, new Vector3(a.x, b.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(b.x, b.y, b.z),
              new Vector3(a.x, b.y, b.z), new Vector3(a.x, b.y, a.z));
        foreach (var (x, z) in new[] { (a.x, a.z), (b.x, a.z), (b.x, b.z), (a.x, b.z) })
            Trazo("Cubo", g, mat, 0.01f, new Vector3(x, a.y, z), new Vector3(x, b.y, z));
    }

    // Como los identificadores del escáner: esfera de color y una flecha con la normal.
    private static void Marcador(Transform g, Material mat, Vector3 p)
    {
        Esfera("Marcador", g, p, 0.07f, mat).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        Trazo("Normal", g, mat, 0.008f, p, p + Vector3.back * 0.25f);
    }

    // ---------------------------------------------------------------- ojos

    // Pares de ojos a distintas profundidades en la zona oscura (debajo del logo). Con la
    // niebla, los más lejanos quedan apenas como un brillo.
    private static void ArmarOjos(Transform g, Camera cam)
    {
        var rnd = new Random(666);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        Texture2D tex = Textura("Ojo", 128, 64, PixelOjo, rehacer: true);
        Material[] mats =
        {
            Particula("Ojo ámbar", tex, new Color(1f, 0.85f, 0.6f) * 0.8f),
            Particula("Ojo rojo", tex, new Color(1f, 0.35f, 0.3f) * 0.8f),
            Particula("Ojo blanco", tex, new Color(0.9f, 0.9f, 1f) * 0.6f),
        };
        var usados = new List<Vector2>();
        for (int intento = 0; intento < 400 && usados.Count < 16; intento++)
        {
            var v = new Vector2(R(0.06f, 0.94f), R(0.32f, 0.8f));
            if (usados.Exists(o => Vector2.Distance(o, v) < 0.09f)) continue;
            usados.Add(v);

            Vector3 p = EnPlano(cam, v.x, v.y, R(2.5f, 8.5f));
            float ojo = R(0.05f, 0.09f), entorna = R(0.45f, 1f);
            Quaternion rot = cam.transform.rotation * Quaternion.Euler(0f, 0f, R(-14f, 14f));
            Vector3 lado = rot * Vector3.right * (ojo * 1.25f);
            Material mat = mats[rnd.Next(mats.Length)];
            var par = Grupo("Par de ojos", g).transform;
            foreach (float s in new[] { -1f, 1f })
            {
                var o = Billboard("Ojo", par, mat, p + lado * s, 1f, rot * Quaternion.Euler(0f, 0f, s * R(4f, 12f)));
                o.transform.localScale = new Vector3(ojo, ojo * 0.5f * entorna, 1f);
            }
        }
    }

    // Almendrado con el iris encendido y una pupila rasgada; un halo rojo apenas por fuera.
    private static Color PixelOjo(float u, float v)
    {
        float x = (u - 0.5f) * 2f, y = (v - 0.5f) * 2f;
        float borde = Mathf.Pow(Mathf.Max(0f, 1f - x * x), 0.9f);
        float dentro = Mathf.Clamp01((borde - Mathf.Abs(y)) * 8f);
        float brillo = Mathf.Exp(-(x * x + y * y) * 2.2f);
        Color c = Color.Lerp(new Color(0.9f, 0.15f, 0.04f), new Color(1f, 0.9f, 0.55f), brillo) * dentro;
        float pupila = Mathf.Clamp01((0.09f * (1f - y * y) - Mathf.Abs(x)) * 30f);
        c *= 1f - 0.85f * pupila;
        c += new Color(0.5f, 0.08f, 0.03f) * (0.3f * Mathf.Exp(-(x * x + y * y * 4f) * 1.2f) * (1f - dentro)
                                              * (1f - x * x) * (1f - y * y));
        return Opaco(c);
    }

    // ---------------------------------------------------------------- foto

    private const string FotoLivingPath = CarpetaTexturas + "/Fondo living.jpg";

    // La foto ya es 9:19: de frente a la cámara y del tamaño justo del cuadro, toda delante
    // del telón. El piso de la escena le tapa la parte de abajo (salvo con "fotoSinPiso").
    // Sprites/Default no lleva niebla: la foto se ve con sus colores tal cual.
    private static void ArmarFoto(Transform g, Camera cam)
    {
        if (AssetImporter.GetAtPath(FotoLivingPath) == null) AssetDatabase.ImportAsset(FotoLivingPath);
        if (!(AssetImporter.GetAtPath(FotoLivingPath) is TextureImporter imp))
        {
            Debug.LogWarning($"[Banner] No encontré {FotoLivingPath}; la variante Foto queda vacía.");
            return;
        }
        if (imp.npotScale != TextureImporterNPOTScale.None || !imp.mipmapEnabled ||
            imp.textureCompression != TextureImporterCompression.Uncompressed)
        {
            imp.npotScale = TextureImporterNPOTScale.None; // sin esto la estira a 1024 × 2048
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Trilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(FotoLivingPath);

        // De frente a la cámara el plano queda inclinado como ella: con la cámara mirando
        // hacia abajo, el borde de arriba se aleja. Se busca la distancia a la que ESE borde
        // (el punto más lejano) queda delante del telón (z = 9); con el centro en 8,9 la
        // mitad de arriba de la foto quedaba detrás del telón y se cortaba en la puerta.
        Transform t = cam.transform;
        float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float d = (8.8f - t.position.z) / (t.forward.z + Mathf.Abs(t.up.z) * tan);
        float alto = 2f * d * tan;
        Plano("Foto", g, MatTextura("Foto living", "Sprites/Default", tex), t.position + t.forward * d,
              new Vector2(alto * AspectoBanner, alto), t.rotation);
    }

    // ---------------------------------------------------------------- living 3D

    private const float ZLiving = 5f, AltoLiving = 2.6f;

    // El living de la foto en cajas: pared del fondo azul pizarra con la puerta (a la altura
    // del hueco entre Sorken y Veleth), una esquina a la derecha donde la pared avanza hacia
    // la cámara, sillón contra la pared a la izquierda, alfombra y piso de madera que llega
    // hasta las criaturas. Es geometría con Standard, así que las velas, los contraluces y la
    // niebla de la escena la iluminan igual que a todo lo demás. Del otro lado de la puerta
    // hay una luz puntual con sombras: la cuña de luz en el piso sale sola por la rendija.
    private static void ArmarLiving(Transform g, Camera cam)
    {
        const float grosor = 0.12f, retiro = 0.7f, anchoPuerta = 0.86f, altoPuerta = 2.05f, apertura = 28f;
        float zB = ZLiving, alto = AltoLiving, zF = cam.transform.position.z - 0.5f, zAtras = zB + 2.5f;
        float X(float vx) => EnPlano(cam, vx, 0.5f, zB).x;
        float xL = X(-0.3f), xR = X(1.3f), xEsq = X(0.72f);
        float dx0 = X(0.42f) - anchoPuerta * 0.5f, dx1 = dx0 + anchoPuerta;
        float zDer = zB - retiro; // pared a la derecha de la esquina, más cerca

        Material pared = MatConTextura("Living pared", Textura("Living pared", 256, 256,
            (u, v) => Gris(0.85f + 0.2f * Fbm(u * 4f, v * 4f, 4)), rehacer: true), new Color(0.11f, 0.14f, 0.19f), 0.12f, Vector2.one);
        Material moldura = Mat("Living moldura", new Color(0.5f, 0.52f, 0.55f), 0.3f);
        Material madera = MatConTextura("Living piso", GuardarTextura("Living piso", 1024, 1024,
            Pixeles(1024, 1024, (x, y, _) => PixelMadera(x, y)), repetir: true), Color.white, 0.72f,
            new Vector2((xR - xL) / 2f, (zAtras - zF) / 2f));
        Material puerta = Mat("Living puerta", new Color(0.55f, 0.55f, 0.53f), 0.35f);
        Material tela = Mat("Living sillón", new Color(0.07f, 0.09f, 0.13f), 0.05f);
        Material almohadon = Mat("Living almohadón", new Color(0.22f, 0.2f, 0.19f), 0.05f);
        Material herraje = Mat("Living herraje", new Color(0.03f, 0.03f, 0.03f), 0.6f);
        Material alfombra = MatConTextura("Living alfombra", Textura("Living alfombra", 512, 512,
            (u, v) => Gris(0.65f + 0.35f * Fbm(u * 90f, v * 90f, 3) * (0.8f + 0.4f * Hash((int)(u * 512f), (int)(v * 512f)))),
            rehacer: true), new Color(0.33f, 0.34f, 0.36f), 0f, Vector2.one);

        // Piso de madera (reemplaza al de la escena: BannerFondo lo apaga) y techo.
        var piso = Plano("Piso madera", g, madera, new Vector3((xL + xR) * 0.5f, 0f, (zF + zAtras) * 0.5f),
                         new Vector2(xR - xL, zAtras - zF), Quaternion.Euler(90f, 0f, 0f));
        piso.GetComponent<Renderer>().receiveShadows = true;
        Bloque("Techo", g, pared, new Vector3(xL, alto, zF), new Vector3(xR, alto + 0.05f, zAtras));

        // Paredes: el fondo con el hueco de la puerta, el retorno de la esquina y el tramo de la derecha.
        Bloque("Pared fondo", g, pared, new Vector3(xL, 0f, zB), new Vector3(dx0, alto, zB + grosor));
        Bloque("Pared fondo", g, pared, new Vector3(dx1, 0f, zB), new Vector3(xEsq + grosor, alto, zB + grosor));
        Bloque("Dintel", g, pared, new Vector3(dx0, altoPuerta, zB), new Vector3(dx1, alto, zB + grosor));
        Bloque("Pared esquina", g, pared, new Vector3(xEsq, 0f, zDer), new Vector3(xEsq + grosor, alto, zB));
        Bloque("Pared derecha", g, pared, new Vector3(xEsq, 0f, zDer - grosor), new Vector3(xR, alto, zDer));

        // Molduras de techo y zócalos, siguiendo las tres paredes (el zócalo se corta en la puerta).
        foreach (var (y0, y1, salida) in new[] { (alto - 0.12f, alto, 0.07f), (0f, 0.11f, 0.02f) })
        {
            bool zocalo = y0 == 0f;
            Bloque("Moldura", g, moldura, new Vector3(xL, y0, zB - salida), new Vector3(zocalo ? dx0 - 0.08f : xEsq, y1, zB));
            if (zocalo) Bloque("Moldura", g, moldura, new Vector3(dx1 + 0.08f, y0, zB - salida), new Vector3(xEsq, y1, zB));
            Bloque("Moldura", g, moldura, new Vector3(xEsq - salida, y0, zDer - salida), new Vector3(xEsq, y1, zB));
            Bloque("Moldura", g, moldura, new Vector3(xEsq - salida, y0, zDer - salida), new Vector3(xR, y1, zDer));
        }

        // Marco de la puerta: contramarcos del lado del living y el forro del vano.
        const float contramarco = 0.08f;
        Bloque("Contramarco", g, moldura, new Vector3(dx0 - contramarco, 0f, zB - 0.02f), new Vector3(dx0, altoPuerta + contramarco, zB));
        Bloque("Contramarco", g, moldura, new Vector3(dx1, 0f, zB - 0.02f), new Vector3(dx1 + contramarco, altoPuerta + contramarco, zB));
        Bloque("Contramarco", g, moldura, new Vector3(dx0 - contramarco, altoPuerta, zB - 0.02f), new Vector3(dx1 + contramarco, altoPuerta + contramarco, zB));
        Bloque("Forro", g, moldura, new Vector3(dx0, 0f, zB), new Vector3(dx0 + 0.02f, altoPuerta, zB + grosor));
        Bloque("Forro", g, moldura, new Vector3(dx1 - 0.02f, 0f, zB), new Vector3(dx1, altoPuerta, zB + grosor));
        Bloque("Forro", g, moldura, new Vector3(dx0, altoPuerta - 0.02f, zB), new Vector3(dx1, altoPuerta, zB + grosor));

        // Hoja entreabierta hacia el otro cuarto, con la bisagra a la izquierda (como en la
        // foto): la luz queda en la rendija de la derecha. Dos tableros y el picaporte.
        var hoja = new GameObject("Puerta").transform;
        hoja.SetParent(g, false);
        hoja.SetPositionAndRotation(new Vector3(dx0 + 0.02f, 0f, zB + 0.03f), Quaternion.Euler(0f, -apertura, 0f));
        float hw = anchoPuerta - 0.05f, hh = altoPuerta - 0.02f;
        Pieza("Hoja", hoja, puerta, new Vector3(hw * 0.5f, hh * 0.5f, 0f), new Vector3(hw, hh, 0.04f));
        Pieza("Tablero", hoja, puerta, new Vector3(hw * 0.5f, hh * 0.72f, -0.026f), new Vector3(hw * 0.62f, hh * 0.34f, 0.012f));
        Pieza("Tablero", hoja, puerta, new Vector3(hw * 0.5f, hh * 0.28f, -0.026f), new Vector3(hw * 0.62f, hh * 0.38f, 0.012f));
        Pieza("Picaporte", hoja, herraje, new Vector3(hw - 0.07f, 0.95f, -0.05f), Vector3.one * 0.06f, PrimitiveType.Sphere);

        // El otro cuarto: una pared que brilla (el bloom la hace sangrar por la rendija) y una
        // luz cálida con sombras que pinta el forro, el canto de la hoja y la cuña en el piso.
        Plano("Luz del otro cuarto", g, Emisivo("Living luz cuarto", new Color(1.6f, 1f, 0.6f)),
              new Vector3(dx1, alto * 0.5f, zB + 1.8f), new Vector2(3.2f, alto));
        var calida = Luz("Luz del otro cuarto", g, LightType.Point, new Vector3(dx1 - 0.15f, 1.9f, zB + 0.9f), 7f);
        calida.color = new Color(1f, 0.66f, 0.38f);
        calida.intensity = 3.5f;

        // Luz fría de ambiente (la "noche" de la foto): de arriba a la izquierda contra el fondo.
        var noche = Luz("Noche living", g, LightType.Spot,
                        new Vector3(cam.transform.position.x - 1.5f, 2.4f, zB - 5f), 14f, 75f);
        Apuntar(noche, new Vector3(dx0, 1f, zB));
        noche.color = new Color(0.45f, 0.56f, 0.85f);
        noche.intensity = 1.2f;

        // Sillón contra la pared, a la izquierda de la puerta, mirando a la cámara.
        float s1 = dx0 - 0.3f, s0 = s1 - 2.1f, sAtras = zB - 0.05f, sFrente = sAtras - 0.9f;
        foreach (float px in new[] { s0 + 0.08f, s1 - 0.08f })
        foreach (float pz in new[] { sFrente + 0.08f, sAtras - 0.08f })
            Bloque("Pata", g, herraje, new Vector3(px - 0.025f, 0f, pz - 0.025f), new Vector3(px + 0.025f, 0.08f, pz + 0.025f));
        Bloque("Base", g, tela, new Vector3(s0, 0.08f, sFrente), new Vector3(s1, 0.4f, sAtras));
        Bloque("Respaldo", g, tela, new Vector3(s0, 0.4f, sAtras - 0.22f), new Vector3(s1, 0.85f, sAtras));
        Bloque("Brazo", g, tela, new Vector3(s0, 0.4f, sFrente), new Vector3(s0 + 0.2f, 0.62f, sAtras));
        Bloque("Brazo", g, tela, new Vector3(s1 - 0.2f, 0.4f, sFrente), new Vector3(s1, 0.62f, sAtras));
        float medio = (s0 + s1) * 0.5f;
        Bloque("Almohadón asiento", g, tela, new Vector3(s0 + 0.2f, 0.4f, sFrente + 0.02f), new Vector3(medio - 0.005f, 0.52f, sAtras - 0.22f));
        Bloque("Almohadón asiento", g, tela, new Vector3(medio + 0.005f, 0.4f, sFrente + 0.02f), new Vector3(s1 - 0.2f, 0.52f, sAtras - 0.22f));
        var almohada = Esfera("Almohadón", g, new Vector3(s1 - 0.45f, 0.68f, sAtras - 0.33f), 1f, almohadon);
        almohada.transform.localScale = new Vector3(0.45f, 0.4f, 0.16f);
        almohada.transform.rotation = Quaternion.Euler(-12f, 20f, 8f);

        // Alfombra delante del sillón, un poco girada.
        var tapete = Bloque("Alfombra", g, alfombra, new Vector3(-1.3f, 0f, -0.85f), new Vector3(1.3f, 0.02f, 0.85f));
        tapete.transform.SetPositionAndRotation(new Vector3(X(0.3f), 0.01f, zB - 2.3f), Quaternion.Euler(0f, -8f, 0f));
    }

    // Tablas de 14 cm a lo largo de X y de 1 m de largo, trabadas: 2 m × 2 m que repiten sin
    // costura (la veta va por tabla, sembrada con su id, así no corta en el borde del tile).
    private static Color PixelMadera(float x, float y)
    {
        const float tabla = 2f / 14f, largo = 1f;
        float mx = x * 2f, my = y * 2f;
        int fila = Mathf.FloorToInt(my / tabla);
        float corrida = (mx + Hash(fila % 14, 1) * largo) % 2f;
        int pieza = Mathf.FloorToInt(corrida / largo);
        float fx = corrida - pieza * largo, fy = my - fila * tabla;
        int id = (fila % 14) * 2 + pieza;
        float veta = Mathf.PerlinNoise(fx * 3f + id * 17.3f, fy * 60f + id * 5.1f);
        veta = 0.8f * veta + 0.2f * Mathf.PerlinNoise(fx * 25f + id * 3.7f, fy * 200f);
        Color c = new Color(0.12f, 0.085f, 0.065f) * ((0.75f + 0.5f * Hash(id, 9)) * (0.8f + 0.4f * veta));
        float junta = Mathf.Min(Mathf.Min(fx, largo - fx), Mathf.Min(fy, tabla - fy) * 2f);
        if (junta < 0.004f) c *= 0.4f;
        return Opaco(c);
    }

    private static Color Gris(float v) => new Color(v, v, v, 1f);

    private static Material MatConTextura(string nombre, Texture2D tex, Color color, float suavidad, Vector2 repeticion)
    {
        var mat = Mat(nombre, color, suavidad);
        mat.mainTexture = tex;
        mat.mainTextureScale = repeticion;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Caja sólida (con sombras) entre dos esquinas del mundo.
    private static GameObject Bloque(string nombre, Transform padre, Material mat, Vector3 min, Vector3 max)
    {
        var go = Primitiva(PrimitiveType.Cube, nombre, padre, mat);
        go.transform.position = (min + max) * 0.5f;
        Vector3 tam = max - min;
        go.transform.localScale = new Vector3(Mathf.Abs(tam.x), Mathf.Abs(tam.y), Mathf.Abs(tam.z));
        ConSombras(go);
        return go;
    }

    // Pieza en coordenadas locales de un pivote (la hoja de la puerta gira con su bisagra).
    private static void Pieza(string nombre, Transform padre, Material mat, Vector3 centro, Vector3 tam,
                              PrimitiveType tipo = PrimitiveType.Cube)
    {
        var go = Primitiva(tipo, nombre, padre, mat);
        go.transform.localPosition = centro;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = tam;
        ConSombras(go);
    }

    // Las cajas tiran sombra de los dos lados: la luz del otro cuarto sólo entra por la rendija.
    private static void ConSombras(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = ShadowCastingMode.TwoSided;
        r.receiveShadows = true;
    }

    // ---------------------------------------------------------------- helpers

    // Punto del plano z = z que se ve en (vx, vy) del cuadro. La cámara del banner mira hacia +Z.
    private static Vector3 EnPlano(Camera cam, float vx, float vy, float z)
    {
        Ray r = cam.ViewportPointToRay(new Vector3(vx, vy, 0f));
        return r.GetPoint((z - r.origin.z) / r.direction.z);
    }

    // Quad de fondo (sin sombras); sin rotación mira hacia -Z, o sea a la cámara.
    private static GameObject Plano(string nombre, Transform padre, Material mat, Vector3 centro, Vector2 tam,
                                    Quaternion? rot = null)
    {
        var go = Primitiva(PrimitiveType.Quad, nombre, padre, mat);
        go.transform.SetPositionAndRotation(centro, rot ?? Quaternion.identity);
        go.transform.localScale = new Vector3(tam.x, tam.y, 1f);
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    // Línea siempre de frente a la cámara, en el espacio local del padre (los grupos están en
    // el origen, así que es el del mundo; y si se mueve el grupo, la línea lo sigue).
    private static void Trazo(string nombre, Transform padre, Material mat, float ancho, params Vector3[] puntos)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.alignment = LineAlignment.View;
        lr.positionCount = puntos.Length;
        lr.SetPositions(puntos);
        lr.widthMultiplier = ancho;
        lr.numCapVertices = 2;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    // Unlit con recorte (alpha >= 0,5): escribe profundidad, así el humo y los haces se
    // desvanecen contra él igual que contra el piso.
    private static Material Recorte(string nombre, Texture2D tex)
    {
        var mat = MatTextura(nombre, "Unlit/Transparent Cutout", tex);
        mat.SetFloat("_Cutoff", 0.5f);
        return mat;
    }

    private static Color[] Pixeles(int w, int h, Func<float, float, float, Color> pixel)
    {
        // Coordenadas con la misma escala en los dos ejes: x de 0 a 1, y de 0 a h / w.
        var px = new Color[w * h];
        float tamPixel = 1f / w;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            px[y * w + x] = pixel((x + 0.5f) / w, (y + 0.5f) / w, tamPixel);
        return px;
    }

    private static Color Opaco(Color c)
    {
        c.a = 1f;
        return c;
    }

    // smoothstep de GLSL (Mathf.SmoothStep interpola entre sus dos primeros argumentos, no
    // es lo mismo). Admite e0 > e1 para una caída.
    private static float Suave(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    // Cuánto tapa una línea de este grosor a distancia d del centro del píxel (antialias).
    private static float Cobertura(float d, float grosor, float tamPixel) =>
        Mathf.Clamp01((grosor * 0.5f - d) / tamPixel + 0.5f);

    // Distancia a la línea más cercana de una red de líneas paralelas separadas por paso.
    private static float DistRed(float p, float paso)
    {
        float f = p / paso;
        return Mathf.Abs(f - Mathf.Round(f)) * paso;
    }

    private static float Hash(int a, int b, int c = 0)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    private static float Fbm(float x, float y, int octavas)
    {
        float n = 0f, amp = 0.5f, total = 0f, f = 1f;
        for (int o = 0; o < octavas; o++, amp *= 0.5f, f *= 2f)
        {
            n += amp * Mathf.PerlinNoise(x * f + 13.1f, y * f + 7.7f);
            total += amp;
        }
        return n / total;
    }

    // Máscara en escala de grises para dibujar trazos con antialias. Coordenadas: x de 0 a 1
    // a lo ancho, y de 0 a H / W (misma escala en los dos ejes). Los trazos se acumulan con
    // máximo, así los cruces no se queman.
    private sealed class Lienzo
    {
        public readonly int W, H;
        public readonly float[] A;

        public Lienzo(int w, int h)
        {
            W = w;
            H = h;
            A = new float[w * h];
        }

        public Lienzo Copia()
        {
            var l = new Lienzo(W, H);
            Array.Copy(A, l.A, A.Length);
            return l;
        }

        public void Segmento(Vector2 a, Vector2 b, float grosor, float valor = 1f)
        {
            Vector2 pa = a * W, pb = b * W, ab = pb - pa;
            float r = Mathf.Max(grosor * W * 0.5f, 0.35f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, pb.x) - r - 2f));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(pa.x, pb.x) + r + 2f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, pb.y) - r - 2f));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(pa.y, pb.y) + r + 2f));
            float largo2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - pa, ab) / largo2);
                float v = Mathf.Clamp01(r + 0.5f - Vector2.Distance(p, pa + ab * t)) * valor;
                int i = y * W + x;
                if (v > A[i]) A[i] = v;
            }
        }

        public void Polilinea(IList<Vector2> puntos, float grosor, float valor = 1f, bool cerrada = false)
        {
            for (int i = 0; i + 1 < puntos.Count; i++) Segmento(puntos[i], puntos[i + 1], grosor, valor);
            if (cerrada && puntos.Count > 2) Segmento(puntos[puntos.Count - 1], puntos[0], grosor, valor);
        }

        public void Arco(Vector2 c, float r, float desdeGrados, float hastaGrados, float grosor, float valor = 1f)
        {
            int n = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(hastaGrados - desdeGrados) / 360f * r * W * 0.6f));
            var puntos = new List<Vector2>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(desdeGrados, hastaGrados, i / (float)n) * Mathf.Deg2Rad;
                puntos.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            Polilinea(puntos, grosor, valor);
        }

        public void Circulo(Vector2 c, float r, float grosor, float valor = 1f) => Arco(c, r, 0f, 360f, grosor, valor);

        public void Rellenar(Vector2 min, Vector2 max, Func<Vector2, bool> dentro, float valor = 1f)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(min.x * W)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(max.x * W));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(min.y * W)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(max.y * W));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (dentro(new Vector2((x + 0.5f) / W, (y + 0.5f) / W)))
                    A[y * W + x] = Mathf.Max(A[y * W + x], valor);
        }

        // Tres pasadas de caja separable ≈ gaussiana.
        public void Desenfocar(int radio, int pasadas)
        {
            var tmp = new float[A.Length];
            for (int p = 0; p < pasadas; p++)
            {
                Caja(A, tmp, radio, true);
                Caja(tmp, A, radio, false);
            }
        }

        private void Caja(float[] src, float[] dst, int r, bool horizontal)
        {
            int n = horizontal ? W : H, lineas = horizontal ? H : W;
            float inv = 1f / (2 * r + 1);
            for (int l = 0; l < lineas; l++)
            {
                int Idx(int i) => horizontal ? l * W + i : i * W + l;
                float suma = 0f;
                for (int i = -r; i <= r; i++) suma += src[Idx(Mathf.Clamp(i, 0, n - 1))];
                for (int i = 0; i < n; i++)
                {
                    dst[Idx(i)] = suma * inv;
                    suma += src[Idx(Mathf.Min(i + r + 1, n - 1))] - src[Idx(Mathf.Max(i - r, 0))];
                }
            }
        }
    }
}
