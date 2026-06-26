using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

// Asegura que el subsistema XR (ARKit) esté inicializado y la ARSession arrancada.
// Pegalo en cualquier GameObject de la escena iOS. No necesita configuración.
[DisallowMultipleComponent]
public class IOSARBootstrap : MonoBehaviour
{
    [SerializeField] private ARSession _arSession;

    private IEnumerator Start()
    {
        if (_arSession == null) _arSession = FindFirstObjectByType<ARSession>();

        // Esperamos a que ARKit reporte su estado real.
        if ((ARSession.state == ARSessionState.None) ||
            (ARSession.state == ARSessionState.CheckingAvailability))
        {
            yield return ARSession.CheckAvailability();
        }

        if (ARSession.state == ARSessionState.Unsupported)
        {
            Debug.LogError("[IOSARBootstrap] Dispositivo NO soporta ARKit.");
            yield break;
        }

        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            yield return ARSession.Install();
        }

        // Forzamos el inicio del loader XR si Unity no lo hizo solo
        // (suele pasar al usar carga manual de XR).
        if (XRGeneralSettings.Instance != null &&
            XRGeneralSettings.Instance.Manager != null &&
            !XRGeneralSettings.Instance.Manager.isInitializationComplete)
        {
            yield return XRGeneralSettings.Instance.Manager.InitializeLoader();
            XRGeneralSettings.Instance.Manager.StartSubsystems();
        }

        if (_arSession != null) _arSession.enabled = true;

        Debug.Log($"[IOSARBootstrap] ARSession lista. Estado: {ARSession.state}");
    }
}
