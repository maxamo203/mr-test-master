using System.Collections.Generic;
using Scanner;
using UnityEngine;
using UnityEngine.Rendering;

namespace Collectibles
{
    // Una reliquia del ritual en el mundo. Es una NetworkEntity estatica: no se mueve
    // ni tiene IA, asi que no aporta estado de simulacion. La spawnea el servidor
    // (CollectibleSpawnManager) via NetworkManager.ServerSpawn; el typeId encodea la
    // variante VISUAL (ver EntityTypeIds.CollectibleReliquia) — a diferencia de las
    // pilas, ninguna variante vale mas que otra, todas suman igual a NightLoot.Total.
    //
    // El modelo (FBX) va como hijo del prefab (ver Assets/Editor/CollectiblePrefabSetup.cs);
    // esta clase solo agrega el halo — mismo mecanismo que BatteryEntity, reutilizando
    // el mismo shader (Resources/BatteryGlow.shader es generico: aditivo con
    // color/intensidad/alpha/corte de piso, no especifico de las pilas).
    public class CollectibleEntity : NetworkEntity
    {
        // Registro estatico de reliquias vivas: evita que el pickup tenga que escanear
        // todas las entidades con GetComponent cada frame.
        public static readonly List<CollectibleEntity> Active = new();

        [Tooltip("Variante visual (que modelo trae este prefab). Se suma a " +
                 "EntityTypeIds.CollectibleReliquia para formar el typeId de red — debe " +
                 "coincidir con el indice registrado en el PrefabRegistry.")]
        public byte variantIndex = 0;

        [Header("Glow (desactivado a propósito: la reliquia tiene que costar encontrarla)")]
        [Tooltip("Halo billboard aditivo que emite su propia luz, encima del modelo. Apagado " +
                 "por defecto — con el halo puesto se veía desde lejos en la oscuridad, y el " +
                 "punto de la reliquia es que cueste encontrarla.")]
        public bool  addGlow = false;
        [Tooltip("Color del halo — energia residual del ritual.")]
        public Color glowColor = new Color(0.86f, 0.55f, 0.16f, 1f); // ambar apagado
        [Tooltip("Tamaño del halo (m).")]
        public float glowSize = 0.30f;
        [Tooltip("Brillo del halo.")]
        public float glowIntensity = 1.2f;
        [Tooltip("Latido suave del tamaño (mas lento que las pilas: se siente 'viejo', " +
                 "no una fuente de energia activa).")]
        public bool  pulse = true;
        [SerializeField] private float pulseSpeed  = 1.4f;
        [Tooltip("Amplitud del pulso como fracción del tamaño (0.15 = ±15%).")]
        [SerializeField] private float pulseAmount = 0.15f;
        [Tooltip("Ocultar el glow cuando una pared/mueble (layer Placed) tapa la reliquia.")]
        public bool  occludeByWalls = true;
        [SerializeField] private float occlusionMargin = 0.25f;
        [SerializeField] private float glowFadeSpeed = 8f;

        // Posicion anchor-relativa: la guardamos al spawnear y reconvertimos a world
        // cada frame, para seguir las recalibraciones del anchor (igual que BatteryEntity).
        private Vector3 _relPos;
        private bool    _hasRel;

        // Glow billboard
        private Transform    _glowTf;
        private MeshRenderer _glowMr;
        private Material     _glowMat;
        private Camera       _cam;
        private int          _occludeMask;
        private float        _visibility;

        private static Mesh _quad;
        private static int  _lastSyncFrame = -1;
        private static readonly int ID_Color     = Shader.PropertyToID("_Color");
        private static readonly int ID_Intensity = Shader.PropertyToID("_Intensity");
        private static readonly int ID_Alpha     = Shader.PropertyToID("_Alpha");
        private static readonly int ID_FloorY    = Shader.PropertyToID("_FloorY");

        private void Awake()
        {
            EntityTypeId = (byte)(EntityTypeIds.CollectibleReliquia + variantIndex);
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

        private void CreateGlow()
        {
            if (!addGlow || _glowTf != null) return;

            var shader = Resources.Load<Shader>("BatteryGlow")
                       ?? Shader.Find("Custom/BatteryGlow")
                       ?? Shader.Find("Unlit/Color");
            if (shader == null) return;

            var go = new GameObject("ReliquiaGlow");
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

            _glowMat = new Material(shader) { name = "ReliquiaGlow (runtime)" };
            var c = glowColor; c.a = 1f;
            if (_glowMat.HasProperty(ID_Color))     _glowMat.SetColor(ID_Color, c);
            if (_glowMat.HasProperty(ID_Intensity)) _glowMat.SetFloat(ID_Intensity, glowIntensity);
            _glowMr.sharedMaterial = _glowMat;

            int placed = LayerMask.NameToLayer("Placed");
            _occludeMask = placed >= 0 ? (1 << placed) : 0;
        }

        private void LateUpdate()
        {
            if (_glowTf == null) return;
            if (_cam == null) _cam = Camera.main;

            if (_cam != null) _glowTf.rotation = _cam.transform.rotation;

            float s = glowSize;
            if (pulse) s *= 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            _glowTf.localScale = Vector3.one * s;

            if (_glowMat != null && _glowMat.HasProperty(ID_FloorY))
            {
                float floorY = -100000f;
                if (FloorPoint.Instance != null && WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady)
                    floorY = WorldOrigin.Instance.ToWorld(new Vector3(0f, FloorPoint.Instance.LocalY, 0f)).y;
                _glowMat.SetFloat(ID_FloorY, floorY);
            }

            if (_glowMr == null) return;

            float target = 1f;
            if (occludeByWalls && _occludeMask != 0 && _cam != null)
            {
                if (_lastSyncFrame != Time.frameCount)
                {
                    Physics.SyncTransforms();
                    _lastSyncFrame = Time.frameCount;
                }
                target = VisibleFraction();
            }

            _visibility = Mathf.MoveTowards(_visibility, target, glowFadeSpeed * Time.deltaTime);

            if (_glowMat.HasProperty(ID_Alpha)) _glowMat.SetFloat(ID_Alpha, _visibility);
            bool visible = _visibility > 0.001f;
            if (_glowMr.enabled != visible) _glowMr.enabled = visible;
        }

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
            if (d <= 0.2f) return true;
            return !Physics.Linecast(camPos, camPos + to * ((d - 0.1f) / d),
                                     _occludeMask, QueryTriggerInteraction.Ignore);
        }

        private void Update()
        {
            if (!_hasRel || WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;

            Vector3 target = WorldOrigin.Instance.ToWorld(_relPos);
            if ((transform.position - target).sqrMagnitude > 1e-8f)
                transform.position = target;
        }

        private void OnDestroy()
        {
            if (_glowMat != null) Destroy(_glowMat);
        }

        private static Mesh QuadMesh()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "ReliquiaGlowQuad" };
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
