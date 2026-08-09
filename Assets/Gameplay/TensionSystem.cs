using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Contador de tension del jugador LOCAL (0..1), US-11.2. Resume "que tan critico esta
    // el momento" combinando varias señales; por ahora solo lo consume
    // TensionDistortionOverlay (camara), pero cualquier sistema futuro (audio, HUD...)
    // puede leer Value01 o aportar la suya con SetSource sin tocar este archivo.
    //
    // Fuentes automaticas (recalculadas cada frame):
    //  - cordura: 1 - LocalSanity.Value01. Es la base, gradual (cuanto menos cordura, mas
    //    tension), y la unica que no baja a 0 con el tiempo salvo que suba la cordura.
    //  - arbmos:  ArbmosEntity.Active.Distort01, la intensidad de distorsion que YA calcula
    //    el server para la alucinacion individual de este jugador.
    //  - sorken:  proximidad al Sorken mientras emerge/persigue/atrapa (estado replicado,
    //    visible para todos los peers via EntityRegistry).
    // Las fuentes se combinan por MAXIMO (manda el evento mas fuerte del momento, no la
    // suma de todos) y el resultado se suaviza para que no salte de golpe.
    public class TensionSystem : MonoBehaviour
    {
        public static TensionSystem Instance { get; private set; }

        public float Value01 { get; private set; }

        private static readonly int ID_DISTORT = Shader.PropertyToID("_TensionDistort");

        [Header("Suavizado (unidades/seg)")]
        [SerializeField] private float _riseSpeed = 2.5f;
        [SerializeField] private float _fallSpeed = 0.8f;

        [Header("Proximidad del Sorken (persiguiendo/atrapando/emergiendo)")]
        [SerializeField] private float _sorkenNear = 2f;   // distancia (m): tension maxima
        [SerializeField] private float _sorkenFar  = 8f;   // distancia (m): tension nula

        // Fuentes externas (futuras: jumpscares, ritual, etc.). Clave -> valor 0..1.
        private readonly Dictionary<string, float> _externalSources = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            float target = ComputeCorduraSource();
            target = Mathf.Max(target, ComputeArbmosSource());
            target = Mathf.Max(target, ComputeSorkenSource());
            foreach (var v in _externalSources.Values) target = Mathf.Max(target, v);

            float speed = target > Value01 ? _riseSpeed : _fallSpeed;
            Value01 = Mathf.MoveTowards(Value01, target, speed * Time.deltaTime);

            Shader.SetGlobalFloat(ID_DISTORT, Value01);
        }

        private static float ComputeCorduraSource()
        {
            var ls = LocalSanity.Instance;
            return ls == null ? 0f : 1f - ls.Value01;
        }

        private static float ComputeArbmosSource()
        {
            var a = ArbmosEntity.Active;
            return a == null ? 0f : Mathf.Clamp01(a.Distort01);
        }

        private float ComputeSorkenSource()
        {
            if (EntityRegistry.Instance == null) return 0f;
            var cam = Camera.main;
            if (cam == null) return 0f;

            float best = 0f;
            foreach (var e in EntityRegistry.Instance.All)
            {
                if (e.EntityTypeId != EntityTypeIds.Sorken) continue;
                var s = e.GetComponent<SorkenEntity>();
                if (s == null) continue;
                if (s.State != SorkenState.Emerging && s.State != SorkenState.Chasing &&
                    s.State != SorkenState.Grabbing) continue;

                float d = Vector3.Distance(cam.transform.position, s.Position);
                float t = Mathf.InverseLerp(_sorkenFar, _sorkenNear, d);
                if (t > best) best = t;
            }
            return best;
        }

        // Aporte de una fuente externa (0..1); persiste hasta que se pise con otro valor o
        // se limpie con ClearSource. Pensado para eventos puntuales (jumpscares, ritual...).
        public void SetSource(string key, float value01) => _externalSources[key] = Mathf.Clamp01(value01);
        public void ClearSource(string key) => _externalSources.Remove(key);

        private void OnDisable() => Shader.SetGlobalFloat(ID_DISTORT, 0f);
        private void OnDestroy()
        {
            Shader.SetGlobalFloat(ID_DISTORT, 0f);
            if (Instance == this) Instance = null;
        }

        public static TensionSystem Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("TensionSystem");
                Instance = go.AddComponent<TensionSystem>();
            }
            return Instance;
        }
    }
}
