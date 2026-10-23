using UnityEditor;
using UnityEngine;

// Botones del Inspector de BannerPose (escena del banner, ver BannerEntidadesSetup).
[CustomEditor(typeof(BannerPose))]
public class BannerPoseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        if (GUILayout.Button("Centrar pivote en el cuerpo (no mueve el modelo)"))
            foreach (Object t in targets) ((BannerPose)t).CentrarPivote();

        if (GUILayout.Button("Apoyar en el piso"))
            foreach (Object t in targets) ((BannerPose)t).ApoyarEnPiso();

        EditorGUILayout.HelpBox(
            "El slider de tiempo y el render sólo cambian la pose; nunca mueven la criatura. " +
            "Para ubicarla, mové / rotá este objeto.",
            UnityEditor.MessageType.Info); // calificado: el MessageType de red tapa al de UnityEditor
    }
}
