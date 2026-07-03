using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Scanner
{
    // Acceso al LiDAR del iPhone SIN pasar por ARFoundation: habla directo con
    // la ARSession nativa de ARKit via Assets/Plugins/iOS/LidarNative.mm.
    // El puntero a la sesion sale de XRSessionSubsystem.nativePtr (mecanismo
    // documentado de Unity para extender ARKit por debajo de ARFoundation).
    //
    // En Editor / Android todas las llamadas son no-op (IsAvailable = false);
    // el RaycastResolver cae al siguiente paso de su cascada.
    public static class NativeLidar
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void _LidarSetSession(IntPtr nativeSessionStruct);
        [DllImport("__Internal")] private static extern int  _LidarIsSupported();
        [DllImport("__Internal")] private static extern int  _LidarEnsureConfig();
        [DllImport("__Internal")] private static extern int  _LidarRaycast(float vx, float vy, float vpW, float vpH, float[] outPosNormal);
        [DllImport("__Internal")] private static extern int  _LidarCapturePoints(float[] outBuffer, int maxPoints, int step, int minConfidence, float maxDepth);
#endif

        private static bool _sessionSet;
        private static readonly float[] _raycastOut = new float[6];

        // El dispositivo tiene LiDAR y la sesion nativa ya fue registrada.
        public static bool IsAvailable
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return _sessionSet && _LidarIsSupported() == 1;
#else
                return false;
#endif
            }
        }

        // Registra la ARSession nativa a partir del subsystem de ARFoundation.
        // Devuelve true si quedo registrada.
        public static bool TrySetSession(ARSession arSession)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (arSession == null || arSession.subsystem == null) return false;
            var ptr = arSession.subsystem.nativePtr;
            if (ptr == IntPtr.Zero) return false;
            _LidarSetSession(ptr);
            _sessionSet = true;
            return true;
#else
            return false;
#endif
        }

        // Inyecta sceneDepth + sceneReconstruction en la config nativa si faltan.
        // Idempotente y barato: llamar periodicamente (ARFoundation puede pisar
        // la config al re-armar el image tracking en RestartTracking).
        public static void EnsureConfig()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_sessionSet) return;
            int r = _LidarEnsureConfig();
            if (r == 1) Debug.Log("[NativeLidar] sceneDepth + sceneReconstruction re-aplicados a la ARSession.");
#endif
        }

        // Raycast depth-aware de ARKit desde un punto de pantalla (coords Unity,
        // origen abajo-izquierda). pos/normal en world-space de Unity.
        public static bool TryRaycast(Vector2 screenPoint, out Vector3 position, out Vector3 normal)
        {
            position = Vector3.zero;
            normal   = Vector3.up;
#if UNITY_IOS && !UNITY_EDITOR
            if (!_sessionSet) return false;
            // Unity: origen abajo-izquierda -> UIKit: origen arriba-izquierda.
            float vx = screenPoint.x / Screen.width;
            float vy = 1f - screenPoint.y / Screen.height;
            if (_LidarRaycast(vx, vy, Screen.width, Screen.height, _raycastOut) != 1) return false;
            position = new Vector3(_raycastOut[0], _raycastOut[1], _raycastOut[2]);
            normal   = new Vector3(_raycastOut[3], _raycastOut[4], _raycastOut[5]);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up; else normal.Normalize();
            return true;
#else
            return false;
#endif
        }

        // Muestrea el depth map actual y llena buffer con triples (x,y,z) en
        // world-space de Unity. Devuelve la cantidad de puntos escritos.
        //   step: muestreo en pixeles del depth map (mas alto = menos puntos).
        //   minConfidence: 0..2 (ARConfidenceLevel; 2 = solo alta confianza).
        public static int CapturePoints(float[] buffer, int step = 4, int minConfidence = 2, float maxDepth = 5f)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_sessionSet || buffer == null) return 0;
            return _LidarCapturePoints(buffer, buffer.Length / 3, step, minConfidence, maxDepth);
#else
            return 0;
#endif
        }
    }

    // Driver de escena: registra la ARSession en el plugin en cuanto el
    // subsystem existe y re-asegura la config nativa a 1 Hz. Lo crea
    // ScannerSceneBootstrap; es inofensivo fuera de iOS.
    public class NativeLidarDriver : MonoBehaviour
    {
        private ARSession _arSession;
        private bool _registered;
        private float _nextEnsure;

        private void Update()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (Time.unscaledTime < _nextEnsure) return;
            _nextEnsure = Time.unscaledTime + 1f;

            if (!_registered)
            {
                if (_arSession == null) _arSession = FindFirstObjectByType<ARSession>();
                if (ARSession.state < ARSessionState.SessionInitializing) return;
                _registered = NativeLidar.TrySetSession(_arSession);
                if (_registered)
                    Debug.Log($"[NativeLidarDriver] ARSession nativa registrada. LiDAR={(NativeLidar.IsAvailable ? "OK" : "no disponible")}");
            }

            if (_registered) NativeLidar.EnsureConfig();
#endif
        }
    }
}
