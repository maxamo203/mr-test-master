using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Cambia el proyecto entre la build COMPLETA y la build DEMO desde el editor.
//
// Qué toca (siempre para el build target activo, los dos únicos que usamos son
// Android e iOS):
//   1) El define MORTUORIUM_DEMO, que es lo que lee Gameplay.BuildVariant para
//      recortar noches y sacar el multijugador.
//   2) El bundle id (applicationIdentifier) y el nombre de la app. Con un id
//      distinto, el sistema operativo las trata como DOS apps: se pueden tener la
//      demo y la completa instaladas a la vez, cada una con sus PlayerPrefs y sus
//      escaneos (el sandbox va por id, así que no comparten Application.persistentDataPath).
//
// Uso: Mortuorium > Variante de build > (COMPLETA | DEMO). Cambiar de variante
// dispara una recompilación del proyecto; el menú muestra cuál está activa.
public static class MortuoriumBuildVariant
{
    private const string Define = "MORTUORIUM_DEMO";

    private const string RutaCompleta = "Mortuorium/Variante de build/COMPLETA";
    private const string RutaDemo     = "Mortuorium/Variante de build/DEMO";
    private const string RutaEstado   = "Mortuorium/Variante de build/Ver estado";

    // Bundle id y nombre visible por variante. La demo cuelga del id de la completa
    // con el sufijo ".demo": es la convención habitual y evita pisar el id publicado.
    private const string IdCompleta     = "com.equipo112.mortuorium";
    private const string IdDemo         = "com.equipo112.mortuorium.demo";
    private const string NombreCompleta = "Mortuorium";
    private const string NombreDemo     = "Mortuorium Demo";

    public static bool DemoActiva => DefinesActuales().Contains(Define);

    [MenuItem(RutaCompleta)]
    private static void PonerCompleta() => Aplicar(demo: false);

    [MenuItem(RutaDemo)]
    private static void PonerDemo() => Aplicar(demo: true);

    [MenuItem(RutaCompleta, true)]
    private static bool ValidarCompleta()
    {
        Menu.SetChecked(RutaCompleta, !DemoActiva);
        return true;
    }

    [MenuItem(RutaDemo, true)]
    private static bool ValidarDemo()
    {
        Menu.SetChecked(RutaDemo, DemoActiva);
        return true;
    }

    [MenuItem(RutaEstado)]
    private static void VerEstado()
    {
        var t = TargetActivo();
        Debug.Log($"[Variante] {(DemoActiva ? "DEMO" : "COMPLETA")} para {t}\n" +
                  $"  bundle id : {PlayerSettings.GetApplicationIdentifier(t)}\n" +
                  $"  nombre    : {PlayerSettings.productName}\n" +
                  $"  noches    : {(DemoActiva ? Gameplay.BuildVariant.NochesDemo.ToString() : "todas")}\n" +
                  $"  multi     : {(DemoActiva ? "no" : "sí")}\n" +
                  $"  defines   : {string.Join(";", DefinesActuales())}");
    }

    private static void Aplicar(bool demo)
    {
        var target = TargetActivo();

        var defines = DefinesActuales().ToList();
        bool cambio = demo ? !defines.Contains(Define) : defines.Remove(Define);
        if (demo && cambio) defines.Add(Define);

        PlayerSettings.SetScriptingDefineSymbols(target, defines.ToArray());

        // El id se setea SOLO para el target activo: cambiar de plataforma y olvidarse
        // de volver a correr esto dejaría la otra con el id equivocado, y eso se nota
        // recién al publicar. Ver estado lo muestra.
        PlayerSettings.SetApplicationIdentifier(target, demo ? IdDemo : IdCompleta);
        PlayerSettings.productName = demo ? NombreDemo : NombreCompleta;

        AssetDatabase.SaveAssets();

        Menu.SetChecked(RutaDemo, demo);
        Menu.SetChecked(RutaCompleta, !demo);

        Debug.Log($"[Variante] Ahora es {(demo ? "DEMO" : "COMPLETA")} " +
                  $"({PlayerSettings.GetApplicationIdentifier(target)} · \"{PlayerSettings.productName}\") " +
                  $"para {target}. Esperá a que termine de recompilar antes de buildear.");
    }

    private static NamedBuildTarget TargetActivo()
    {
        var grupo = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        return NamedBuildTarget.FromBuildTargetGroup(grupo);
    }

    private static IEnumerable<string> DefinesActuales()
    {
        PlayerSettings.GetScriptingDefineSymbols(TargetActivo(), out string[] defines);
        return defines ?? Array.Empty<string>();
    }
}
