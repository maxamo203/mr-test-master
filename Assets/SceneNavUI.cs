using UnityEngine;

// OBSOLETO: el botón "Ir a Multijugador / Ir a Escanear" quedó reemplazado por la
// navegación del menú principal Mortuorium (NightMenuUI) y las flechas de volver
// de cada pantalla, que usan SceneFlow.GoTo (misma limpieza de singletons que
// hacía este componente). Queda como cáscara vacía para no romper el prefab
// SceneNavUI referenciado en SampleScene/ScannerScene; se puede borrar de las
// escenas cuando se editen en el editor.
public class SceneNavUI : MonoBehaviour
{
}
