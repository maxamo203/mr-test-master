using UnityEngine;

namespace Scanner
{
    // Flujo alternativo de mapeo del entorno con LiDAR (modo LidarMap del FSM).
    // Mientras el modo esta activo, cada _captureInterval segundos pide al
    // plugin nativo (NativeLidar) una muestra del depth map, convierte los
    // puntos world -> anchor-local y los mete en LidarPointCloud con el filtro
    // de distancia minima (slider en ReticleController).
    //
    // Requiere WorldOrigin calibrado (los puntos se guardan anchor-relativos,
    // igual que todo lo demas, para poder reusarlos entre sesiones).
    //
    // En Editor (sin LiDAR) genera puntos falsos sobre un "cuarto" virtual
    // frente a la camara, para poder ejercitar el flujo completo (slider,
    // guardado, carga) sin dispositivo — mismo espiritu que ARImageAnchor.EditorStub.
    public class LidarMapController : MonoBehaviour
    {
        [Tooltip("Segundos entre capturas del depth map mientras se mapea.")]
        [SerializeField] private float _captureInterval = 0.25f;
        [Tooltip("Muestreo del depth map en pixeles (mas alto = menos puntos por captura).")]
        [SerializeField] private int _sampleStep = 4;
        [Tooltip("Profundidad maxima aceptada (m).")]
        [SerializeField] private float _maxDepth = 5f;
        [Tooltip("Velocidad angular maxima de la camara (grados/s) para capturar. Mas rapido = pose y depth desincronizados = puntos corridos.")]
        [SerializeField] private float _maxAngularSpeed = 60f;
        [Tooltip("Velocidad lineal maxima de la camara (m/s) para capturar.")]
        [SerializeField] private float _maxLinearSpeed = 0.5f;

        // Buffer reusable para la captura nativa (triples xyz).
        private const int MaxPointsPerCapture = 8192;
        private readonly float[] _buffer = new float[MaxPointsPerCapture * 3];

        private ScanStateMachine _fsm;
        private float _nextCapture;

        // Seguimiento de velocidad de la camara para el gate de movimiento.
        private Camera _camera;
        private Vector3 _lastCamPos;
        private Quaternion _lastCamRot;
        private bool _hasLastPose;

        // true mientras la captura esta pausada porque el telefono se mueve
        // demasiado rapido (lo muestra la UI del mapeo).
        public bool MotionGated { get; private set; }

        // Hay hardware capaz de mapear (LiDAR real, o el fake del editor).
        public static bool IsSupported
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                return NativeLidar.IsAvailable;
#endif
            }
        }

        private void Awake() => _fsm = ScanStateMachine.Instance;

        private void Update()
        {
            if (_fsm == null || _fsm.Current != ScannerMode.LidarMap)
            {
                _hasLastPose = false;
                MotionGated  = false;
                return;
            }
            var wo = WorldOrigin.Instance;
            var cloud = LidarPointCloud.Instance;
            if (wo == null || !wo.IsReady || cloud == null) return;

            // Gate de movimiento: con la camara girando/trasladandose rapido, la
            // pose y el depth quedan levemente desincronizados y los puntos caen
            // corridos del mundo fisico. Pausamos la captura hasta que afloje.
            if (!UpdateMotionGate()) return;

            if (Time.unscaledTime < _nextCapture) return;
            _nextCapture = Time.unscaledTime + _captureInterval;

#if UNITY_EDITOR
            int n = FakeCapture();
#else
            // Confianza media o alta: solo-alta deja demasiados frames sin puntos
            // en superficies oscuras/reflectivas.
            int n = NativeLidar.CapturePoints(_buffer, _sampleStep, minConfidence: 1, maxDepth: _maxDepth);
#endif
            for (int i = 0; i < n; i++)
            {
                var world = new Vector3(_buffer[i * 3], _buffer[i * 3 + 1], _buffer[i * 3 + 2]);
                cloud.AddFiltered(wo.ToRelative(world));
            }
        }

        // Devuelve true si la camara esta lo suficientemente quieta para capturar.
        private bool UpdateMotionGate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return false;

            var pos = _camera.transform.position;
            var rot = _camera.transform.rotation;
            if (!_hasLastPose)
            {
                _lastCamPos = pos;
                _lastCamRot = rot;
                _hasLastPose = true;
                MotionGated = true; // primer frame: sin velocidad conocida
                return false;
            }

            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            float linSpeed = (pos - _lastCamPos).magnitude / dt;
            float angSpeed = Quaternion.Angle(_lastCamRot, rot) / dt;
            _lastCamPos = pos;
            _lastCamRot = rot;

            MotionGated = linSpeed > _maxLinearSpeed || angSpeed > _maxAngularSpeed;
            return !MotionGated;
        }

#if UNITY_EDITOR
        // Puntos falsos: una "pared" a 2 m frente a la camara y un "piso" 1.2 m
        // por debajo, con ruido. Suficiente para probar slider y persistencia.
        private int FakeCapture()
        {
            var cam = Camera.main;
            if (cam == null) return 0;
            var t = cam.transform;
            int n = 0;
            for (int i = 0; i < 200 && n < MaxPointsPerCapture; i++)
            {
                Vector3 p;
                if (i % 2 == 0)
                {
                    // pared: plano perpendicular al forward horizontal de la camara
                    var fwd = t.forward; fwd.y = 0f;
                    if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                    fwd.Normalize();
                    var right = Vector3.Cross(Vector3.up, fwd);
                    p = t.position + fwd * 2f
                      + right * Random.Range(-1.5f, 1.5f)
                      + Vector3.up * Random.Range(-1f, 1.5f)
                      + fwd * Random.Range(-0.02f, 0.02f);
                }
                else
                {
                    // piso
                    p = t.position
                      + t.forward * Random.Range(0.5f, 3f)
                      + t.right * Random.Range(-1.5f, 1.5f);
                    p.y = t.position.y - 1.2f + Random.Range(-0.02f, 0.02f);
                }
                _buffer[n * 3]     = p.x;
                _buffer[n * 3 + 1] = p.y;
                _buffer[n * 3 + 2] = p.z;
                n++;
            }
            return n;
        }
#endif
    }
}
