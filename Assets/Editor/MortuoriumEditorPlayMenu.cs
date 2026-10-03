using UnityEditor;

// Interruptores de las ayudas de PLAY MODE (ambas viven en Assets/AR/, dentro de
// #if UNITY_EDITOR, así que no existen en ningún build). El estado va a EditorPrefs: es
// de esta máquina y no se commitea.
public static class MortuoriumEditorPlayMenu
{
    private const string RutaFondo      = "Mortuorium/Fondo 360 en Play";
    private const string RutaControles  = "Mortuorium/Controles WASD en Play";
    private const string RutaPerfilDev  = "Mortuorium/Datos en Play/DEV";
    private const string RutaPerfilProd = "Mortuorium/Datos en Play/PROD";

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

    // Selector exclusivo del Editor. Las dos entradas funcionan como opciones de
    // radio: sólo una queda marcada. Las builds release ignoran esta preferencia.
    [MenuItem(RutaPerfilDev)]
    private static void UsarDatosDev()
    {
        Gameplay.PlayDataProfile.UseProduction = false;
        RefrescarPerfilSeleccionado();
    }

    [MenuItem(RutaPerfilDev, true)]
    private static bool UsarDatosDevValidate()
    {
        RefrescarPerfilSeleccionado();
        return true;
    }

    [MenuItem(RutaPerfilProd)]
    private static void UsarDatosProd()
    {
        Gameplay.PlayDataProfile.UseProduction = true;
        RefrescarPerfilSeleccionado();
    }

    [MenuItem(RutaPerfilProd, true)]
    private static bool UsarDatosProdValidate()
    {
        RefrescarPerfilSeleccionado();
        return true;
    }

    private static void RefrescarPerfilSeleccionado()
    {
        bool prod = Gameplay.PlayDataProfile.UseProduction;
        Menu.SetChecked(RutaPerfilDev, !prod);
        Menu.SetChecked(RutaPerfilProd, prod);
    }
}