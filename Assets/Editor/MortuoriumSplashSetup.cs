using UnityEditor;
using UnityEngine;

// Configura el Splash Screen del proyecto para mostrar el wordmark MORTUORIUM
// (con el mismo estilo del menú principal) ARRIBA del logo "Made with Unity".
//
// Con licencia Personal el logo de Unity NO se puede quitar, pero SÍ se puede
// agregar el logo propio encima: con drawMode = UnityLogoBelow, los logos que
// agregamos se muestran arriba y el de Unity queda abajo.
//
// Uso: menú  Mortuorium > Configurar splash  (una sola vez). Queda guardado en
// Player Settings (ProjectSettings.asset). La imagen vive en
// Assets/Splash/mortuorium_splash.png (fondo transparente para componer sobre el
// color de fondo oscuro del tema).
public static class MortuoriumSplashSetup
{
    private const string LogoPath = "Assets/Splash/mortuorium_splash.png";

    [MenuItem("Mortuorium/Configurar splash")]
    public static void Setup()
    {
        // 1) Asegurar que la imagen se importe como Sprite (para poder asignarla).
        //    Hay que setear textureType Y spriteImportMode = Single: solo cambiar el
        //    textureType no siempre genera el sub-asset Sprite. Lo aplicamos siempre
        //    (idempotente) por si quedó a medias de un intento previo.
        var importer = AssetImporter.GetAtPath(LogoPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[Splash] No se encontró la imagen en {LogoPath}. " +
                           "¿Se importó el asset? Reabrí el proyecto y volvé a intentar.");
            return;
        }

        importer.textureType         = TextureImporterType.Sprite;
        importer.spriteImportMode    = SpriteImportMode.Single;
        importer.mipmapEnabled       = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        AssetDatabase.ImportAsset(LogoPath, ImportAssetOptions.ForceSynchronousImport);

        // Cargar el Sprite (a veces LoadAssetAtPath<Sprite> tarda un frame en verlo;
        // por eso también buscamos entre todos los sub-assets del archivo).
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        if (sprite == null)
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(LogoPath))
                if (o is Sprite s) { sprite = s; break; }

        if (sprite == null)
        {
            Debug.LogError("[Splash] No se pudo cargar el Sprite (revisá el import de la imagen).");
            return;
        }

        // 2) Configurar el splash: fondo del tema + logo Mortuorium (Unity debajo).
        PlayerSettings.SplashScreen.show          = true;
        PlayerSettings.SplashScreen.backgroundColor = new Color(0.024f, 0.020f, 0.016f, 1f); // #060504 (Bg del tema)
        PlayerSettings.SplashScreen.unityLogoStyle  = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
        PlayerSettings.SplashScreen.drawMode        = PlayerSettings.SplashScreen.DrawMode.UnityLogoBelow;
        PlayerSettings.SplashScreen.animationMode   = PlayerSettings.SplashScreen.AnimationMode.Static;
        PlayerSettings.SplashScreen.logos = new[]
        {
            PlayerSettings.SplashScreenLogo.Create(2.5f, sprite),
        };

        AssetDatabase.SaveAssets();
        Debug.Log("[Splash] Configurado: MORTUORIUM se muestra arriba del logo de Unity.");
    }
}
