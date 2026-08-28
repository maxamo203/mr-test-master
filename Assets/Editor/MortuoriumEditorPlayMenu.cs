using UnityEditor;

// Interruptores de las ayudas de PLAY MODE (ambas viven en Assets/AR/, dentro de
// #if UNITY_EDITOR, así que no existen en ningún build). El estado va a EditorPrefs: es
// de esta máquina y no se commitea.
public static class MortuoriumEditorPlayMenu
{
    private const string RutaFondo     = "Mortuorium/Fondo 360 en Play";
    private const string RutaControles = "Mortuorium/Controles WASD en Play";

    // Fondo panorámico como skybox: sin él no hay imagen detrás y los efectos de pantalla
    // completa (VHS / distorsión) no se pueden evaluar. Ver EditorPanorama360.
    [MenuItem(RutaFondo)]
    private static void ToggleFondo()
    {
        EditorPanorama360.Activo = !EditorPanorama360.Activo;
        Menu.SetChecked(RutaFondo, EditorPanorama360.Activo);
    }

    // Además de validar, refresca el tilde del menú cada vez que se abre.
    [MenuItem(RutaFondo, true)]
    private static bool ToggleFondoValidate()
    {
        Menu.SetChecked(RutaFondo, EditorPanorama360.Activo);
        return true;
    }

    // WASD + arrastre del mouse para recorrer la escena sin tracking AR.
    // Ver EditorPlayerControls.
    [MenuItem(RutaControles)]
    private static void ToggleControles()
    {
        EditorPlayerControls.Activo = !EditorPlayerControls.Activo;
        Menu.SetChecked(RutaControles, EditorPlayerControls.Activo);
    }

    [MenuItem(RutaControles, true)]
    private static bool ToggleControlesValidate()
    {
        Menu.SetChecked(RutaControles, EditorPlayerControls.Activo);
        return true;
    }
}
