using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Configura Assets/Logo/icon-logo.png como icono de la aplicación (Player Settings).
// Se setea el icono "Default" (fallback para todas las plataformas) y además Android
// e iOS explícitamente, que son las dos plataformas de build reales del proyecto.
//
// Uso: menú  Mortuorium > Configurar icono de app  (una sola vez, o cada vez que
// cambie Assets/Logo/icon-logo.png). Queda guardado en Player Settings
// (ProjectSettings.asset).
public static class MortuoriumIconSetup
{
    private const string IconPath = "Assets/Logo/icon-logo.png";

    [MenuItem("Mortuorium/Configurar icono de app")]
    public static void Setup()
    {
        // 1) Asegurar que se importe como textura normal (no Sprite) para poder
        //    asignarla como Texture2D en Player Settings.
        var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[Icono] No se encontró la imagen en {IconPath}.");
            return;
        }

        importer.textureType         = TextureImporterType.Default;
        importer.mipmapEnabled       = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (texture == null)
        {
            Debug.LogError("[Icono] No se pudo cargar la textura (revisá el import de la imagen).");
            return;
        }

        // 2) Icono por defecto (fallback para toda plataforma sin override propio).
        SetIcons(NamedBuildTarget.Unknown, texture);

        // 3) Plataformas de build reales del proyecto (ver CLAUDE.md: Android + iOS).
        SetIcons(NamedBuildTarget.Android, texture);
        SetIcons(NamedBuildTarget.iOS, texture);

        AssetDatabase.SaveAssets();
        Debug.Log("[Icono] Configurado: Assets/Logo/icon-logo.png es el icono de la app.");
    }

    private static void SetIcons(NamedBuildTarget target, Texture2D texture)
    {
        var count = PlayerSettings.GetIconSizes(target, IconKind.Application).Length;
        if (count == 0) count = 1;

        var icons = new Texture2D[count];
        for (var i = 0; i < count; i++) icons[i] = texture;

        PlayerSettings.SetIcons(target, icons, IconKind.Application);
    }
}
