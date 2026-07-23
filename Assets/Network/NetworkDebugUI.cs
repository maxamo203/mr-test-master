using UnityEngine;

// OBSOLETO: el panel de debug de red se movió al GameObject "DebugHud"
// (Assets/DebugHud/DebugRedUI.cs), que centraliza todos los datos de debug y
// solo existe en development builds. Este componente queda como cáscara vacía
// para no romper la referencia serializada en SampleScene; se puede quitar de
// la escena cuando se edite en el editor.
public class NetworkDebugUI : MonoBehaviour
{
}
