using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

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
    // COORDENADAS: el plugin devuelve session space (z ya invertida a mano
    // izquierda). Unity cuelga los trackables bajo XROrigin.TrackablesParent,
    // que en modo Device lleva el CameraYOffset (1.1176 m en este proyecto) y
    // sigue al XROrigin — asi que TODO lo nativo se transforma por ese parent
    // antes de usarse. Sin esto, los hits quedan ~1.12 m corridos en Y.
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
        [DllImport("__Internal")] private static extern void _LidarGetStatus(int[] outStatus, int n);
#endif

        private static bool _sessionSet;
        private static Transform _trackablesParent;
        private static readonly float[] _raycastOut = new float[6];
        private static readonly int[] _status = new int[8];
        // Backoff de EnsureConfig: si ARFoundation nos pisa la config una y otra
        // vez, dejamos de forzarla cada segundo para no degradar el tracking.
        private static int _consecutiveReapplies;

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

        // Transform bajo el que ARFoundation cuelga los trackables (incluye el
        // CameraYOffset del XROrigin). Lo setea NativeLidarDriver.
        public static void SetTrackablesParent(Transform trackablesParent) =>
            _trackablesParent = trackablesParent;

        private static Vector3 SessionToWorldPoint(Vector3 p) =>
            _trackablesParent != null ? _trackablesParent.TransformPoint(p) : p;

        private static Vector3 SessionToWorldDir(Vector3 d) =>
            _trackablesParent != null ? _trackablesParent.TransformDirection(d) : d;

        // Inyecta sceneDepth en la config nativa si falta. Idempotente: si el
        // AROcclusionManager ya lo pidio (lo normal en esta escena), es no-op.
        public static void EnsureConfig()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_sessionSet) return;
            // Algo (ARFoundation) nos viene pisando la config: aflojar para no
            // re-correr la sesion cada segundo (degrada tracking y depth).
            if (_consecutiveReapplies >= 5) return;

            int r = _LidarEnsureConfig();
            if (r == 1)
            {
                _consecutiveReapplies++;
                Debug.Log($"[NativeLidar] sceneDepth re-aplicado a la ARSession ({_consecutiveReapplies}).");
                if (_consecutiveReapplies >= 5)
                    Debug.LogWarning("[NativeLidar] La config se pisa repetidamente; se deja de forzar sceneDepth. " +
                                     "Revisar que AROcclusionManager tenga Environment Depth activo.");
            }
            else if (r == 0)
            {
                _consecutiveReapplies = 0;
            }
#endif
        }

        // Raycast fisico via depth map (fallback: raycast de ARKit) desde un
        // punto de pantalla (coords Unity, origen abajo-izquierda).
        // pos/normal ya en world-space de Unity.
        public static bool TryRaycast(Vector2 screenPoint, out Vector3 position, out Vector3 normal)
        {
            position = Vector3.zero;
            normal   = Vector3.up;
#if UNITY_IOS && !UNITY_EDITOR
            if (!_sessionSet) return false;
            // Unity: origen abajo-izquierda -> UIKit: origen arriba-izquierda.
            float vx = screenPoint.x / Screen.width;
            float vy = 1f - screenPoint.y / Screen.height;
            if (_LidarRaycast(vx, vy, Screen.width, Screen.height, _raycastOut) == 0) return false;
            position = SessionToWorldPoint(new Vector3(_raycastOut[0], _raycastOut[1], _raycastOut[2]));
            normal   = SessionToWorldDir(new Vector3(_raycastOut[3], _raycastOut[4], _raycastOut[5]));
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up; else normal.Normalize();
            return true;
#else
            return false;
#endif
        }

        // Muestrea el depth map actual y llena buffer con triples (x,y,z) YA en
        // world-space de Unity. Devuelve la cantidad de puntos escritos.
        //   step: muestreo en pixeles del depth map (mas alto = menos puntos).
        //   minConfidence: 0..2 (ARConfidenceLevel; 1 = media o alta).
        public static int CapturePoints(float[] buffer, int step = 4, int minConfidence = 1, float maxDepth = 5f)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_sessionSet || buffer == null) return 0;
            int n = _LidarCapturePoints(buffer, buffer.Length / 3, step, minConfidence, maxDepth);
            if (_trackablesParent != null)
            {
                var m = _trackablesParent.localToWorldMatrix;
                for (int i = 0; i < n; i++)
                {
                    var p = m.MultiplyPoint3x4(new Vector3(buffer[i * 3], buffer[i * 3 + 1], buffer[i * 3 + 2]));
                    buffer[i * 3] = p.x; buffer[i * 3 + 1] = p.y; buffer[i * 3 + 2] = p.z;
                }
            }
            return n;
#else
            return 0;
#endif
        }

        // Resumen de diagnostico para mostrar en la UI del mapeo.
        public static string StatusSummary()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_sessionSet) return "LiDAR: sesion nativa NO registrada";
            _LidarGetStatus(_status, _status.Length);
            if (_status[0] == 0) return "LiDAR: sesion nativa perdida";
            if (_status[1] == 0) return "LiDAR: sin frame de ARKit todavia";
            if (_status[2] == 0) return "LiDAR: config no es WorldTracking";
            if (_status[3] == 0) return "LiDAR: sceneDepth NO activo en la config";
            if (_status[5] == 0 && _status[6] == 0) return "LiDAR: config OK pero el frame no trae depth";
            return $"LiDAR OK — depth {_status[7]}px" +
                   $" (smooth={(_status[6] == 1 ? "si" : "no")})";
#else
            return "LiDAR: editor (puntos simulados)";
#endif
        }
    }

    // Driver de escena: registra la ARSession en el plugin en cuanto el
    // subsystem existe, engancha el TrackablesParent del XROrigin y re-asegura
    // la config nativa a 1 Hz. Lo crea ScannerSceneBootstrap.
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
                {
                    var origin = FindFirstObjectByType<XROrigin>();
                    if (origin != null) NativeLidar.SetTrackablesParent(origin.TrackablesParent);
                    else Debug.LogWarning("[NativeLidarDriver] No hay XROrigin: los hits LiDAR quedarian en session space.");
                    Debug.Log($"[NativeLidarDriver] ARSession nativa registrada. LiDAR={(NativeLidar.IsAvailable ? "OK" : "no disponible")}");
                }
            }

            if (_registered) NativeLidar.EnsureConfig();
#endif
        }
    }
}
