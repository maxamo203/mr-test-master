using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay
{
    // Efecto de pantalla completa en momentos de tension (US-11.2): distorsion de lente +
    // aberracion cromatica que escala con TensionSystem.Value01. Distinto de la distorsion
    // LOCALIZADA del Arbmos (ArbmosDistortionHUD, acotada a su segmento de pantalla) y de
    // la distorsion binaria de SanityHUD en cordura 0 (queda aparte, sin tocar).
    //
    // Sin post-process/URP en el proyecto (pipeline built-in): el warp es REAL (no un tinte
    // IMGUI) via GrabPass en Assets/Resources/TensionDistortion.shader. Misma tecnica que
    // DarknessOverlay: un quad sobredimensionado parentado a la camara, asi entra en el
    // pase por-ojo de MRCardboardController (Render() manual del ojo izquierdo + el
    // automatico del derecho) sin que haya que tocar ese pipeline.
    //
    // Costo: GrabPass copia el framebuffer ya renderizado, dos veces por frame en estereo.
    // Por eso el renderer se apaga por completo cuando no hay tension (_minVisible) — el
    // costo solo se paga en los momentos que le dan sentido al efecto.
    //
    // Se auto-crea (Ensure) cuando arranca la partida; no necesita wiring en el prefab.
    public class TensionDistortionOverlay : MonoBehaviour
    {
        public static TensionDistortionOverlay Instance { get; private set; }

        [Tooltip("Por debajo de este umbral no se dibuja el quad: evita el costo del GrabPass sin tension.")]
        [SerializeField] private float _minVisible = 0.02f;

        private Camera _cam;
        private GameObject _quad;
        private Material _mat;
        private Renderer _renderer;

        public static TensionDistortionOverlay Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("TensionDistortionOverlay");
                Instance = go.AddComponent<TensionDistortionOverlay>();
            }
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _cam = Camera.main;
            if (_cam == null)
            {
                Debug.LogError("TensionDistortionOverlay: no encontré la cámara AR.");
                return;
            }

            var shader = Resources.Load<Shader>("TensionDistortion");
            if (shader == null) shader = Shader.Find("AR/TensionDistortion");
            if (shader == null)
            {
                Debug.LogError("TensionDistortionOverlay: shader 'AR/TensionDistortion' no encontrado en Assets/Resources/.");
                return;
            }
            _mat = new Material(shader) { name = "TensionDistortionMat" };
            CreateQuad();
        }

        private void CreateQuad()
        {
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "TensionDistortionQuad";
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

            float t = TensionSystem.Instance != null ? TensionSystem.Instance.Value01 : 0f;
            _renderer.enabled = t >= _minVisible;
        }

        private void OnDestroy()
        {
            if (_quad != null) Destroy(_quad);
            if (_mat  != null) Destroy(_mat);
            if (Instance == this) Instance = null;
        }
    }
}
