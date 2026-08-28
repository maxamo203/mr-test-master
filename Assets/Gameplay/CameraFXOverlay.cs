using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay
{
    // Efecto de camara de pantalla completa durante la PARTIDA. Fusiona dos historias en
    // un solo pase (ver Assets/Resources/CameraFX.shader):
    //   US-11.2 — distorsion de lente + aberracion cromatica que escalan con
    //             TensionSystem.Value01.
    //   US-11.1 — filtro VHS / camara antigua constante (scanlines, grano, bandas de
    //             tracking, jitter, viñeta, tinte), reforzado por la misma tension.
    // Distinto de la distorsion LOCALIZADA del Arbmos (ArbmosDistortionHUD, acotada a su
    // segmento de pantalla) y de la distorsion binaria de SanityHUD en cordura 0.
    //
    // Sin post-process/URP en el proyecto (pipeline built-in): el warp es REAL via
    // GrabPass. Misma tecnica que DarknessOverlay: un quad sobredimensionado parentado a
    // la camara, asi entra en el pase por-ojo de MRCardboardController (Render() manual
    // del ojo izquierdo + el automatico del derecho) sin tocar ese pipeline. Cada ojo
    // rendea a su propia RenderTexture, asi que scanlines/viñeta salen bien por ojo.
    //
    // Costo: el GrabPass copia el framebuffer ya renderizado, dos veces por frame en
    // estereo. Por eso hay UN solo GrabPass para los dos efectos, y el renderer se apaga
    // por completo fuera de partida o si no hay nada que dibujar. El VHS sobre los MENUS
    // no pasa por aca (la IMGUI se dibuja despues del render 3D): lo hace VHSOverlayUI.
    //
    // Se auto-crea (Ensure) cuando arranca la partida; no necesita wiring en el prefab.
    public class CameraFXOverlay : MonoBehaviour
    {
        public static CameraFXOverlay Instance { get; private set; }

        [Tooltip("Por debajo de este umbral de tension, y sin VHS activo, no se dibuja el quad: evita el costo del GrabPass.")]
        [SerializeField] private float _minVisible = 0.02f;

        private Camera _cam;
        private GameObject _quad;
        private Material _mat;
        private Renderer _renderer;

        public static CameraFXOverlay Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("CameraFXOverlay");
                Instance = go.AddComponent<CameraFXOverlay>();
            }
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            VHSSettings.Ensure();   // publica los uniforms del VHS una sola vez

            _cam = Camera.main;
            if (_cam == null)
            {
                Debug.LogError("CameraFXOverlay: no encontré la cámara AR.");
                return;
            }

            var shader = Resources.Load<Shader>("CameraFX");
            if (shader == null) shader = Shader.Find("AR/CameraFX");
            if (shader == null)
            {
                Debug.LogError("CameraFXOverlay: shader 'AR/CameraFX' no encontrado en Assets/Resources/.");
                return;
            }
            _mat = new Material(shader) { name = "CameraFXMat" };
            CreateQuad();
        }

        private void CreateQuad()
        {
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "CameraFXQuad";
            var col = _quad.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _quad.transform.SetParent(_cam.transform, false);

            _renderer = _quad.GetComponent<Renderer>();
            _renderer.sharedMaterial = _mat;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.enabled = false;
        }

        private void LateUpdate()
        {
            if (_quad == null || _cam == null) return;

            // Mismo sobredimensionado que DarknessOverlay (ver ese archivo): la FOV real no
            // coincide con arCamera.fieldOfView bajo AR Foundation y en Cardboard hay que
            // cubrir los dos viewports (izq/der) con el mismo quad.
            const float kOversize = 2.5f;
            float dist = _cam.nearClipPlane * 1.5f + 0.01f;
            float h = 2f * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * kOversize;
            float w = h * Mathf.Max(_cam.aspect, 2.2f);
            _quad.transform.localPosition = new Vector3(0f, 0f, dist);
            _quad.transform.localRotation = Quaternion.identity;
            _quad.transform.localScale = new Vector3(w, h, 1f);

            _renderer.enabled = EnPartida && (VHSSettings.Activo || Tension >= _minVisible);
        }

        private static float Tension =>
            TensionSystem.Instance != null ? TensionSystem.Instance.Value01 : 0f;

        // El filtro es de la PARTIDA: en el lobby / sala de sincronización la imagen de la
        // cámara tiene que verse limpia (el jugador está alineando el cuarto real).
        public static bool EnPartida =>
            NetworkManager.Instance != null && NetworkManager.Instance.GameStarted;

        private void OnDestroy()
        {
            if (_quad != null) Destroy(_quad);
            if (_mat  != null) Destroy(_mat);
            if (Instance == this) Instance = null;
        }
    }
}
