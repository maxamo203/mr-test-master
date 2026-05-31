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

        plist.WriteToFile(plistPath);
        UnityEngine.Debug.Log(
            $"[IOSBuildPostProcessor] Info.plist actualizado: NSLocalNetworkUsageDescription + NSBonjourServices " +
            $"(discovery udp:{DiscoveryPort}).");
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
