using System;
using Gameplay;
using UnityEditor;
using UnityEngine;

// Previsualizador del retrato del asesino (Mortuorium > Retrato del asesino > Previsualizador).
// Una pestaña por entidad (y una General, para asesinos sin ajuste propio): cada una edita SU
// ajuste de RetratoAsesinoConfig — encuadre, distancia, luz blanca y luz roja — y, con el Play
// corriendo, arma el retrato de esa entidad delante de la cámara. LocalDeath reencuadra cada
// frame en el editor, así que lo que se mueve acá se ve al instante en el Game. El modelo sale
// del PrefabRegistry (el mismo prefab que spawnea la red) y se puede cambiar por otro.
public class RetratoAsesinoWindow : EditorWindow
{
    private const string RutaAsset = "Assets/Resources/" + RetratoAsesinoConfig.RutaResources + ".asset";
    private const string RutaRegistro = "Assets/PrefabRegistry.asset";
    private const string PrefabGeneral = "Assets/Entities/Prefabs/SorkenGameplay.prefab";

    private static readonly RetratoAsesinoConfig.Entidad[] Entidades =
        (RetratoAsesinoConfig.Entidad[])Enum.GetValues(typeof(RetratoAsesinoConfig.Entidad));

    private int _pestana;                 // 0 = General, i + 1 = Entidades[i]
    private GameObject[] _modelo;         // modelo elegido a mano por pestaña (null = el del registro)
    private SerializedObject _so;
    private Vector2 _scroll;

    [MenuItem("Mortuorium/Retrato del asesino/Previsualizador")]
    public static void Abrir() => GetWindow<RetratoAsesinoWindow>("Retrato del asesino");

    private bool EsGeneral => _pestana == 0;
    private byte TipoActual => EsGeneral ? (byte)0 : (byte)Entidades[_pestana - 1];

    private void OnInspectorUpdate() => Repaint();   // estado de Play / prueba

    private void OnGUI()
    {
        _modelo ??= new GameObject[Entidades.Length + 1];

        var cfg = AssetDatabase.LoadAssetAtPath<RetratoAsesinoConfig>(RutaAsset);
        if (cfg == null)
        {
            EditorGUILayout.HelpBox($"No existe {RutaAsset}. Sin el asset el juego usa los valores por defecto.",
                                    UnityEditor.MessageType.Info);
            if (GUILayout.Button("Crear config")) RetratoAsesinoMenu.SeleccionarConfig();
            return;
        }
        if (_so == null || _so.targetObject != cfg) _so = new SerializedObject(cfg);
        _so.Update();

        var nombres = new string[Entidades.Length + 1];
        nombres[0] = "General";
        for (int i = 0; i < Entidades.Length; i++) nombres[i + 1] = Entidades[i].ToString();
        int antes = _pestana;
        _pestana = GUILayout.Toolbar(Mathf.Clamp(_pestana, 0, nombres.Length - 1), nombres, GUILayout.Height(26));
        // Cambiar de pestaña con una prueba abierta muestra directamente la otra entidad.
        if (_pestana != antes && PruebaAbierta) Previsualizar();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DibujarPrevisualizacion();
        EditorGUILayout.Space(8);
        DibujarAjuste(cfg);
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Tiempos (comunes a todas las entidades)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_so.FindProperty("revelar"));
        EditorGUILayout.PropertyField(_so.FindProperty("sostener"));
        EditorGUILayout.EndScrollView();

        // La pose se hornea al armar el retrato (no se reencuadra en vivo como el resto): si
        // cambió el clip o el momento, se rehace en el lugar, sin la entrada.
        var aj = cfg.Para(TipoActual);
        var clipAntes = aj.poseClip;
        float momentoAntes = aj.poseMomento;
        _so.ApplyModifiedProperties();
        aj = cfg.Para(TipoActual);
        if (PruebaAbierta && (aj.poseClip != clipAntes || !Mathf.Approximately(aj.poseMomento, momentoAntes)))
            Previsualizar(sinEntrada: true);

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Seleccionar asset")) { Selection.activeObject = cfg; EditorGUIUtility.PingObject(cfg); }
            using (new EditorGUI.DisabledScope(!EditorUtility.IsDirty(cfg)))
                if (GUILayout.Button("Guardar")) AssetDatabase.SaveAssetIfDirty(cfg);
        }
    }

    private static bool PruebaAbierta =>
        Application.isPlaying && LocalDeath.Instance != null && LocalDeath.Instance.EnPrueba;

    private void DibujarPrevisualizacion()
    {
        using var caja = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Previsualizar", EditorStyles.boldLabel);

        var porDefecto = ModeloPorDefecto();
        var elegido = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Modelo", "Vacío = el prefab que spawnea la red para esta entidad (PrefabRegistry)."),
            _modelo[_pestana], typeof(GameObject), false);
        if (elegido != _modelo[_pestana])
        {
            _modelo[_pestana] = elegido;
            if (PruebaAbierta) Previsualizar();
        }
        if (_modelo[_pestana] == null)
            EditorGUILayout.LabelField(" ", porDefecto != null ? $"Usando {porDefecto.name}" : "Sin prefab registrado",
                                       EditorStyles.miniLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Entrá en Play para verlo delante de la cámara. Los valores se pueden editar igual.",
                                    UnityEditor.MessageType.None);
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(PruebaAbierta ? "Repetir" : "Mostrar", GUILayout.Height(24))) Previsualizar();
            using (new EditorGUI.DisabledScope(!PruebaAbierta))
                if (GUILayout.Button("Cerrar", GUILayout.Height(24))) LocalDeath.Instance.CerrarPrueba();
        }
        if (LocalDeath.Instance != null && LocalDeath.Instance.IsDead)
            EditorGUILayout.HelpBox("Estás muerto en la partida: la prueba no se puede abrir.", UnityEditor.MessageType.Warning);
    }

    private void DibujarAjuste(RetratoAsesinoConfig cfg)
    {
        SerializedProperty ajuste;
        if (EsGeneral)
        {
            EditorGUILayout.LabelField("Ajuste General", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Para asesinos sin ajuste propio.", EditorStyles.miniLabel);
            ajuste = _so.FindProperty("general");
        }
        else
        {
            var entidad = Entidades[_pestana - 1];
            int idx = cfg.porEntidad.FindIndex(e => e != null && e.entidad == entidad);
            EditorGUILayout.LabelField($"Ajuste de {entidad}", EditorStyles.boldLabel);
            if (idx < 0)
            {
                EditorGUILayout.HelpBox($"{entidad} no tiene ajuste propio: usa el General.", UnityEditor.MessageType.Info);
                if (GUILayout.Button("Crear ajuste propio (copia del General)"))
                {
                    Undo.RecordObject(cfg, "Ajuste de retrato");
                    cfg.porEntidad.Add(new RetratoAsesinoConfig.AjusteEntidad
                    {
                        entidad = entidad,
                        ajuste = cfg.general.Clonar(),
                    });
                    EditorUtility.SetDirty(cfg);
                    _so.Update();
                    GUIUtility.ExitGUI();
                }
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copiar del General"))
                {
                    Undo.RecordObject(cfg, "Copiar ajuste de retrato");
                    cfg.porEntidad[idx].ajuste = cfg.general.Clonar();
                    EditorUtility.SetDirty(cfg);
                    _so.Update();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Usar el General (borrar propio)"))
                {
                    Undo.RecordObject(cfg, "Borrar ajuste de retrato");
                    cfg.porEntidad.RemoveAt(idx);
                    EditorUtility.SetDirty(cfg);
                    _so.Update();
                    GUIUtility.ExitGUI();
                }
            }
            ajuste = _so.FindProperty("porEntidad").GetArrayElementAtIndex(idx).FindPropertyRelative("ajuste");
        }

        // Los campos del ajuste, con sus [Header]/[Range]/[Tooltip].
        var it = ajuste.Copy();
        var fin = ajuste.GetEndProperty();
        bool entrar = true;
        while (it.NextVisible(entrar) && !SerializedProperty.EqualContents(it, fin))
        {
            EditorGUILayout.PropertyField(it, true);
            entrar = false;
        }
    }

    private GameObject ModeloPorDefecto()
    {
        if (EsGeneral) return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabGeneral);
        var registro = AssetDatabase.LoadAssetAtPath<NetworkedPrefabRegistry>(RutaRegistro);
        if (registro == null) return null;
        try { return registro.Get(TipoActual); }
        catch (Exception) { return null; }   // tipo sin prefab registrado
    }

    private void Previsualizar(bool sinEntrada = false)
    {
        var prefab = _modelo[_pestana] != null ? _modelo[_pestana] : ModeloPorDefecto();
        if (prefab == null) { Debug.LogWarning("[Retrato] No hay modelo para esta pestaña."); return; }
        if (!LocalDeath.Ensure().ProbarRetrato(prefab, TipoActual, sinEntrada))
            Debug.LogWarning("[Retrato] No se pudo armar el retrato (¿hay cámara? ¿ya estás muerto? " +
                             $"¿'{prefab.name}' tiene mallas visibles?).");
    }
}
