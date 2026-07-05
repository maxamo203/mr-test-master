using System.Collections.Generic;
using Scanner;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bateries
{
    // Una pila en el mundo. Es una NetworkEntity estatica: no se mueve ni tiene IA,
    // asi que no aporta estado de simulacion. La spawnea el servidor (BatterySpawnManager)
    // via NetworkManager.ServerSpawn; el typeId encodea la rareza.
    //
    // rarityIndex se setea en el PREFAB (uno por rareza). En Awake se traduce al typeId
    // de red correspondiente, que debe coincidir con el TypeId registrado en el
    // PrefabRegistry para ese prefab (BatteryBase + rarityIndex).
    public class BatteryEntity : NetworkEntity
    {
        // Registro estatico de pilas vivas: evita que el controlador de pickup tenga que
        // escanear todas las entidades con GetComponent cada frame.
        public static readonly List<BatteryEntity> Active = new();

        [Tooltip("Debe coincidir con la rareza del RaritySet y con el TypeId del PrefabRegistry.")]
        public byte rarityIndex = 0;

        [Header("Glow (para ubicarla en la oscuridad)")]
        [Tooltip("Halo billboard aditivo que emite su propia luz: se ve aunque no haya nada " +
                 "cerca. El color sale del tint de la rareza (BatteryRaritySet).")]
        public bool  addGlow = true;
        [Tooltip("Tamaño del halo (m).")]
        public float glowSize = 0.35f;
        [Tooltip("Brillo del halo.")]
        public float glowIntensity = 1.6f;
        [Tooltip("Latido suave del tamaño (efecto de pulso). Se anima la escala, no la " +
                 "intensidad (el aditivo satura y no se notaría).")]
        public bool  pulse = true;
        [SerializeField] private float pulseSpeed  = 3f;
        [Tooltip("Amplitud del pulso como fracción del tamaño (0.2 = ±20%).")]
        [SerializeField] private float pulseAmount = 0.2f;
        [Tooltip("Ocultar el glow cuando una pared/mueble (layer Placed) tapa la pila.")]
        public bool  occludeByWalls = true;
        [Tooltip("Margen (m) alrededor de la pila donde el glow se funde de a poco al asomar " +
                 "por el borde de una pared. Es una distancia fija, independiente del tamaño " +
                 "de la pila, para que no aparezca de golpe.")]
        [SerializeField] private float occlusionMargin = 0.25f;
        [Tooltip("Velocidad del fundido del glow (unidades de opacidad por segundo).")]
        [SerializeField] private float glowFadeSpeed = 8f;

        public byte RarityIndex => rarityIndex;

        // Posicion anchor-relativa: la guardamos al spawnear y reconvertimos a world
        // cada frame, para seguir las recalibraciones del anchor (igual que SorkerNetwork).
        private Vector3 _relPos;
        private bool    _hasRel;

        // Glow billboard
        private Transform    _glowTf;
        private MeshRenderer _glowMr;
        private Material     _glowMat;
        private Camera       _cam;
        private int          _occludeMask;
        private float        _visibility; // 0..1, fundido de oclusion (arranca en 0 => fade-in)

        private static Mesh _quad;
        private static int  _lastSyncFrame = -1; // 1 Physics.SyncTransforms por frame
        private static readonly int ID_Color     = Shader.PropertyToID("_Color");
        private static readonly int ID_Intensity = Shader.PropertyToID("_Intensity");
        private static readonly int ID_Alpha     = Shader.PropertyToID("_Alpha");
        private static readonly int ID_FloorY    = Shader.PropertyToID("_FloorY");

        private void Awake()
        {
            EntityTypeId = (byte)(EntityTypeIds.BatteryBase + rarityIndex);
        }

        private void OnEnable()  { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() { Active.Remove(this); }

        // Estatica: sin estado que sincronizar por tick.
        public override byte[] SerializeState(uint tick) => System.Array.Empty<byte>();
        public override void    ApplyState(uint tick, byte[] data) { }

        public override void OnNetworkSpawn()
        {
            if (WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady)
            {
                _relPos = WorldOrigin.Instance.ToRelative(transform.position);
                _hasRel = true;
            }
            CreateGlow();
        }

        // Crea el halo: un quad con material unlit aditivo (Custom/BatteryGlow) que mira
        // a la camara. Emite su propio color => visible en oscuridad total, sin depender
        // de una superficie. Barato: 1 quad, sin iluminacion ni sombras.
        private void CreateGlow()
        {
            if (!addGlow || _glowTf != null) return;

            var shader = Resources.Load<Shader>("BatteryGlow")
                       ?? Shader.Find("Custom/BatteryGlow")
                       ?? Shader.Find("Unlit/Color");
            if (shader == null) return;

            var go = new GameObject("BatteryGlow");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localScale = Vector3.one * glowSize;
            _glowTf = go.transform;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = QuadMesh();

            _glowMr = go.AddComponent<MeshRenderer>();
            _glowMr.shadowCastingMode    = ShadowCastingMode.Off;
            _glowMr.receiveShadows       = false;
            _glowMr.lightProbeUsage      = LightProbeUsage.Off;
            _glowMr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _glowMat = new Material(shader) { name = "BatteryGlow (runtime)" };
            Color c = BatteryRaritySet.Current != null
                ? BatteryRaritySet.Current.TintFor(rarityIndex)
                : Color.white;
            c.a = 1f;
            if (_glowMat.HasProperty(ID_Color))     _glowMat.SetColor(ID_Color, c);
            if (_glowMat.HasProperty(ID_Intensity)) _glowMat.SetFloat(ID_Intensity, glowIntensity);
            _glowMr.sharedMaterial = _glowMat;

            // Mascara de oclusion: solo la layer "Placed" (paredes/muebles). Las pilas no
            // estan en esa layer, asi que el linecast no se choca con la propia pila.
            int placed = LayerMask.NameToLayer("Placed");
            _occludeMask = placed >= 0 ? (1 << placed) : 0;
        }

        private void LateUpdate()
        {
            if (_glowTf == null) return;
            if (_cam == null) _cam = Camera.main;

            // Billboard hacia la camara.
            if (_cam != null) _glowTf.rotation = _cam.transform.rotation;

            // Pulso: animamos el TAMAÑO (la intensidad no se nota, el aditivo satura).
            float s = glowSize;
            if (pulse) s *= 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            _glowTf.localScale = Vector3.one * s;

            // Corte de piso: pasamos al shader la Y (world) del piso de referencia; abajo
            // de eso el halo se descarta. Sin FloorPoint => Y muy baja (no corta nada).
            if (_glowMat != null && _glowMat.HasProperty(ID_FloorY))
            {
                float floorY = -100000f;
                if (FloorPoint.Instance != null && WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady)
                    floorY = WorldOrigin.Instance.ToWorld(new Vector3(0f, FloorPoint.Instance.LocalY, 0f)).y;
                _glowMat.SetFloat(ID_FloorY, floorY);
            }

            // Oclusion por paredes/muebles con FUNDIDO: en vez de prender/apagar de golpe,
            // calculamos que fraccion del halo esta visible (muestreando varios puntos en
            // un margen alrededor de la pila) y fundimos la opacidad hacia ese valor. Asi,
            // al asomar por el borde de una pared, aparece de a poco.
            if (_glowMr == null) return;

            float target = 1f;
            if (occludeByWalls && _occludeMask != 0 && _cam != null)
            {
                // El proyecto ship-ea con autoSyncTransforms OFF: sincronizamos los
                // colliders una sola vez por frame (no por pila) antes de los linecasts.
                if (_lastSyncFrame != Time.frameCount)
                {
                    Physics.SyncTransforms();
                    _lastSyncFrame = Time.frameCount;
                }
                target = VisibleFraction();
            }

            // Fundido temporal: suaviza aun mas y elimina cualquier escalon del muestreo.
            _visibility = Mathf.MoveTowards(_visibility, target, glowFadeSpeed * Time.deltaTime);

            if (_glowMat.HasProperty(ID_Alpha)) _glowMat.SetFloat(ID_Alpha, _visibility);
            // Apagar el renderer solo cuando esta totalmente invisible (ahorra el draw).
            bool visible = _visibility > 0.001f;
            if (_glowMr.enabled != visible) _glowMr.enabled = visible;
        }

        // Fraccion visible (0..1): muestrea el centro + puntos a +-occlusionMargin en los
        // ejes derecha/arriba de la camara. Cada rayo termina 10 cm antes del punto para no
        // chocar con la propia pila / la superficie donde apoya. El margen es una distancia
        // fija, NO el tamaño de la pila: da la zona sobre la que el glow se funde.
        private float VisibleFraction()
        {
            Vector3 camPos = _cam.transform.position;
            Vector3 right  = _cam.transform.right * occlusionMargin;
            Vector3 up     = _cam.transform.up    * occlusionMargin;
            Vector3 center = transform.position;

            int clear = 0;
            clear += IsPointVisible(camPos, center)         ? 1 : 0;
            clear += IsPointVisible(camPos, center + right) ? 1 : 0;
            clear += IsPointVisible(camPos, center - right) ? 1 : 0;
            clear += IsPointVisible(camPos, center + up)    ? 1 : 0;
            clear += IsPointVisible(camPos, center - up)    ? 1 : 0;
            return clear / 5f;
        }

        private bool IsPointVisible(Vector3 camPos, Vector3 point)
        {
            Vector3 to = point - camPos;
            float   d  = to.magnitude;
            if (d <= 0.2f) return true; // demasiado cerca: contamos como visible
            return !Physics.Linecast(camPos, camPos + to * ((d - 0.1f) / d),
                                     _occludeMask, QueryTriggerInteraction.Ignore);
        }

        private void Update()
        {
            // Re-anclar a WorldOrigin: si el AR corrige la pose del anchor, la pila
            // sigue pegada a su posicion relativa. Solo tocamos el transform si de verdad
            // cambió (evita ensuciar el transform de un objeto estatico cada frame).
            if (!_hasRel || WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;

            Vector3 target = WorldOrigin.Instance.ToWorld(_relPos);
            if ((transform.position - target).sqrMagnitude > 1e-8f)
                transform.position = target;
        }

        private void OnDestroy()
        {
            if (_glowMat != null) Destroy(_glowMat); // el runtime material no se libera solo
        }

        // Quad 1x1 centrado en el origen, compartido por todas las pilas.
        private static Mesh QuadMesh()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "BatteryGlowQuad" };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            };
            _quad.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateBounds();
            return _quad;
        }
    }
}
