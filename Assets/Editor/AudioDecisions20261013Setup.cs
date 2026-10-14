using System;
using UnityEditor;
using UnityEngine;

// Integración reproducible de la selección cerrada el 13/10/2026. Sólo referencia
// derivados finales que ya viven en Assets; no importa fuentes, demos ni alternativas.
public static class AudioDecisions20261013Setup
{
    private const string CatalogPath = "Assets/Resources/AudioCatalog.asset";

    [MenuItem("Mortuorium/Audio/Aplicar decisiones 13-10-2026")]
    public static void Apply()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureImporters();

        var cat = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
        if (cat == null) throw new InvalidOperationException($"No existe {CatalogPath}");

        cat.ambienteNoche = Track(0.46f, 0f, 1f, 15f,
            "Assets/Audio/Clips/Musica/dark_choir_singing_loop.wav");
        cat.gameOverStinger = Track(0.85f, 0f, 1f, 15f,
            "Assets/Audio/Clips/Musica/gameover_coro_breve.wav");

        cat.sorkenCubrirse = Track(0.85f, 1f, 0.8f, 20f,
            "Assets/Audio/Clips/Sorken/sorken_cubrirse_dolor_enfado_01.wav",
            "Assets/Audio/Clips/Sorken/sorken_cubrirse_dolor_enfado_02.wav");
        cat.sorkenMarchaCubierta = Track(0.72f, 1f, 0.8f, 20f,
            "Assets/Audio/Clips/Sorken/sorken_marcha_cubierta_vocal_01.wav",
            "Assets/Audio/Clips/Sorken/sorken_marcha_cubierta_vocal_02.wav",
            "Assets/Audio/Clips/Sorken/sorken_marcha_cubierta_vocal_03.wav",
            "Assets/Audio/Clips/Sorken/sorken_marcha_cubierta_vocal_04.wav",
            "Assets/Audio/Clips/Sorken/sorken_marcha_cubierta_vocal_05.wav");
        cat.sorkenCapturaFatal = Track(1f, 0f, 1f, 15f,
            "Assets/Audio/Clips/Sorken/sorken_captura_fatal_scream.wav");

        cat.arbmosExhalacion = Track(0.72f, 1f, 0.7f, 12f,
            "Assets/Audio/Clips/Arbmos/arbmos_exhalacion.wav");
        cat.arbmosResonanciaEntrada = Track(0.55f, 1f, 0.7f, 12f,
            "Assets/Audio/Clips/Arbmos/arbmos_resonancia_entrada.wav");
        cat.arbmosResonanciaSostenida = Track(0.46f, 1f, 0.7f, 12f,
            "Assets/Audio/Clips/Arbmos/arbmos_resonancia_sostenida_loop.wav");
        cat.arbmosResonanciaSalida = Track(0.5f, 1f, 0.7f, 12f,
            "Assets/Audio/Clips/Arbmos/arbmos_resonancia_salida_natural.wav");
        cat.arbmosEnfado = Track(0.85f, 1f, 0.7f, 12f,
            "Assets/Audio/Clips/Arbmos/arbmos_enfado_01.wav",
            "Assets/Audio/Clips/Arbmos/arbmos_enfado_02.wav",
            "Assets/Audio/Clips/Arbmos/arbmos_enfado_03.wav");
        cat.arbmosPasos = Track(0.9f, 1f, 0.7f, 12f,
            "Assets/Audio/Clips/Arbmos/arbmos_paso_01.wav",
            "Assets/Audio/Clips/Arbmos/arbmos_paso_02.wav",
            "Assets/Audio/Clips/Arbmos/arbmos_paso_03.wav",
            "Assets/Audio/Clips/Arbmos/arbmos_paso_04.wav");
        cat.arbmosPasos.variacionTono = 0.035f;
        cat.arbmosPasos.variacionIntensidad = 0.18f;
        cat.arbmosAgarreNoLetal = Track(0.8f, 0f, 1f, 15f,
            "Assets/Audio/Clips/Arbmos/arbmos_agarre_no_letal_gasp.wav");
        cat.arbmosAgarreLetal = Track(1f, 0f, 1f, 15f,
            "Assets/Audio/Clips/Arbmos/arbmos_agarre_letal_primera_onda.wav");

        cat.velethSalidaLibro = Track(0.9f, 1f, 0.7f, 20f,
            "Assets/Audio/Clips/Veleth/veleth_salida_libro_organica_8_3s.wav");
        cat.velethApoyos = Track(0.88f, 1f, 0.7f, 18f,
            "Assets/Audio/Clips/Veleth/veleth_apoyo_01.wav",
            "Assets/Audio/Clips/Veleth/veleth_apoyo_02.wav",
            "Assets/Audio/Clips/Veleth/veleth_apoyo_03.wav",
            "Assets/Audio/Clips/Veleth/veleth_apoyo_04.wav",
            "Assets/Audio/Clips/Veleth/veleth_apoyo_05.wav");
        cat.velethApoyos.variacionTono = 0.035f;
        cat.velethApoyos.variacionIntensidad = 0.15f;
        cat.velethRoces = Track(0.72f, 1f, 0.7f, 18f,
            "Assets/Audio/Clips/Veleth/veleth_roce_01.wav",
            "Assets/Audio/Clips/Veleth/veleth_roce_02.wav",
            "Assets/Audio/Clips/Veleth/veleth_roce_03.wav",
            "Assets/Audio/Clips/Veleth/veleth_roce_04.wav",
            "Assets/Audio/Clips/Veleth/veleth_roce_05.wav");
        cat.velethRoces.variacionTono = 0.025f;
        cat.velethRoces.variacionIntensidad = 0.12f;
        cat.velethPersecucionEspectral = Track(0.62f, 1f, 0.7f, 20f,
            "Assets/Audio/Clips/Veleth/veleth_persecucion_espectral_loop.wav");
        cat.velethCapturaFatal = Track(1f, 0f, 1f, 15f,
            "Assets/Audio/Clips/Veleth/veleth_captura_fatal_scream.wav");

        cat.libroSusurrosConsumo = Track(0.75f, 1f, 0.6f, 15f,
            "Assets/Audio/Clips/Libro/susurros_superpuestos_consumo_loop.wav");

        EditorUtility.SetDirty(cat);
        AssetDatabase.SaveAssets();
        Debug.Log("[Audio] Decisiones del 13/10/2026 integradas y catálogo actualizado.");
    }

    private static AudioCatalog.Pista Track(float volume, float spatial, float min, float max,
                                             params string[] paths)
    {
        var clips = new AudioClip[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
            if (clips[i] == null) throw new InvalidOperationException($"Falta importar {paths[i]}");
        }
        return new AudioCatalog.Pista
        {
            clips = clips,
            volumen = volume,
            espacial = spatial,
            distanciaMin = min,
            distanciaMax = max,
        };
    }

    private static void ConfigureImporters()
    {
        string[] all = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/Clips" });
        foreach (string guid in all)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsNewSelection(path)) continue;
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer) continue;

            bool flat = path.Contains("/Musica/") || path.Contains("agarre_") ||
                        path.Contains("captura_fatal");
            bool streaming = path.Contains("_loop") &&
                             (path.Contains("dark_choir") || path.Contains("Libro/") ||
                              path.Contains("veleth_persecucion"));
            importer.forceToMono = !flat;
            importer.loadInBackground = streaming;
            var settings = importer.defaultSampleSettings;
            settings.loadType = streaming ? AudioClipLoadType.Streaming
                                          : AudioClipLoadType.CompressedInMemory;
            settings.preloadAudioData = !streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = streaming ? 0.72f : 0.82f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }

    private static bool IsNewSelection(string path) =>
        path.Contains("dark_choir_singing") || path.Contains("gameover_coro_breve") ||
        path.Contains("susurros_superpuestos_consumo") ||
        path.Contains("/Arbmos/arbmos_") || path.Contains("/Veleth/veleth_") ||
        path.Contains("sorken_captura_fatal") ||
        path.Contains("sorken_cubrirse") || path.Contains("sorken_marcha_cubierta");
}
