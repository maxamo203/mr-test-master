#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

// Post-build de iOS: inyecta en el Info.plist las claves necesarias para que
// el broadcast/discovery UDP de la LAN funcione en iOS 14+.
//
// Se ejecuta automaticamente cada vez que Unity exporta el proyecto Xcode.
// No hay que abrir Xcode ni tocar nada a mano.
public static class IOSBuildPostProcessor
{
    // Mismo puerto que LanDiscovery.DiscoveryPort. Mantener sincronizado.
    private const int DiscoveryPort = 47777;

    // Texto que ve el usuario en el popup "X quiere acceder a dispositivos en tu red local".
    private const string LocalNetworkUsage =
        "Esta app usa la red local para encontrar otros jugadores en la misma red Wi-Fi.";

    [PostProcessBuild(45)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        var plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        var plist     = new PlistDocument();
        plist.ReadFromFile(plistPath);

        var root = plist.root;

        // 1) Mensaje del popup de permiso (iOS 14+).
        root.SetString("NSLocalNetworkUsageDescription", LocalNetworkUsage);

        // 2) NSBonjourServices: registra los servicios que la app va a usar en la LAN.
        //    Aunque usemos broadcast UDP plano (no Bonjour), declararlos hace que iOS
        //    no bloquee el trafico en algunas redes/versiones.
        PlistElementArray bonjour;
        var existing = root["NSBonjourServices"];
        if (existing != null && existing is PlistElementArray arr) bonjour = arr;
        else                                                       bonjour = root.CreateArray("NSBonjourServices");

        AddIfMissing(bonjour, $"_mrtgame._udp.");
        AddIfMissing(bonjour, $"_mrtgame._tcp.");

        // 3) Tipo de documento .mscn: que la app aparezca en "Abrir con" y reciba
        //    el archivo (lo maneja MscnNative.mm via application:openURL:).
        RegisterMscnDocumentType(root);

        plist.WriteToFile(plistPath);
        UnityEngine.Debug.Log(
            $"[IOSBuildPostProcessor] Info.plist actualizado: red local + document type .mscn.");
    }

    // Declara el UTI exportado com.<bundleId>.mscn y el CFBundleDocumentType que lo
    // maneja. Idempotente (no duplica en builds incrementales).
    private static void RegisterMscnDocumentType(PlistElementDict root)
    {
        string bundleId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS);
        if (string.IsNullOrEmpty(bundleId)) bundleId = "com.company.app";
        string uti = bundleId + ".mscn";

        // UTExportedTypeDeclarations
        var exported = GetOrCreateArray(root, "UTExportedTypeDeclarations");
        if (!ArrayHasDictWithString(exported, "UTTypeIdentifier", uti))
        {
            var decl = exported.AddDict();
            decl.SetString("UTTypeIdentifier", uti);
            decl.SetString("UTTypeDescription", "Escaneo MR (.mscn)");
            var conforms = decl.CreateArray("UTTypeConformsTo");
            conforms.AddString("public.data");
            var tagSpec = decl.CreateDict("UTTypeTagSpecification");
            var exts = tagSpec.CreateArray("public.filename-extension");
            exts.AddString("mscn");
        }

        // CFBundleDocumentTypes
        var docTypes = GetOrCreateArray(root, "CFBundleDocumentTypes");
        if (!ArrayHasDictWithString(docTypes, "CFBundleTypeName", "Escaneo MR"))
        {
            var doc = docTypes.AddDict();
            doc.SetString("CFBundleTypeName", "Escaneo MR");
            doc.SetString("LSHandlerRank", "Owner");
            doc.SetString("CFBundleTypeRole", "Editor");
            var content = doc.CreateArray("LSItemContentTypes");
            content.AddString(uti);
        }
    }

    private static PlistElementArray GetOrCreateArray(PlistElementDict dict, string key)
    {
        var existing = dict[key];
        if (existing is PlistElementArray arr) return arr;
        return dict.CreateArray(key);
    }

    // True si algun dict del array tiene [key] == value (string).
    private static bool ArrayHasDictWithString(PlistElementArray arr, string key, string value)
    {
        foreach (var el in arr.values)
        {
            if (el is PlistElementDict d && d[key] is PlistElementString s && s.value == value)
                return true;
        }
        return false;
    }

    private static void AddIfMissing(PlistElementArray arr, string value)
    {
        foreach (var v in arr.values)
        {
            if (v is PlistElementString s && s.value == value) return;
        }
        arr.AddString(value);
    }
}
#endif
