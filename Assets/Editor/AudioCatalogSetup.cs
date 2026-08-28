using System.IO;
using UnityEditor;
using UnityEngine;

// Crea el AudioCatalog en la ruta y con el nombre EXACTOS que espera
// AudioManager (Resources.Load<AudioCatalog>(AudioCatalog.ResourceName)).
//
// Existe como item de menu en vez de dejarlo a un "Create > Audio > Catalogo" a mano
// porque si el asset queda fuera de Resources/ o con otro nombre, el juego arranca mudo
// y el unico sintoma es un warning en consola. Asi no hay forma de equivocarse.
public static class AudioCatalogSetup
{
    private const string Carpeta = "Assets/Resources";
    private static string Ruta => $"{Carpeta}/{AudioCatalog.ResourceName}.asset";

    [MenuItem("Mortuorium/Crear catalogo de audio")]
    public static void Crear()
    {
        var existente = AssetDatabase.LoadAssetAtPath<AudioCatalog>(Ruta);
        if (existente != null)
        {
            // No se pisa: se perderian todos los clips ya arrastrados.
            Debug.Log($"[Audio] El catalogo ya existe en {Ruta}. Lo selecciono.");
            Selection.activeObject = existente;
            EditorGUIUtility.PingObject(existente);
            return;
        }

        if (!Directory.Exists(Carpeta)) Directory.CreateDirectory(Carpeta);

        var cat = ScriptableObject.CreateInstance<AudioCatalog>();
        AssetDatabase.CreateAsset(cat, Ruta);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = cat;
        EditorGUIUtility.PingObject(cat);
        Debug.Log($"[Audio] Catalogo creado en {Ruta}. Arrastrale los clips desde el Inspector.");
    }
}
