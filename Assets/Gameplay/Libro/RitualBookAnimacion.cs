using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Animación de "libro encantado" del LIBRO RITUAL, al estilo del libro de la mesa de
    // encantamiento de Minecraft: flota sobre la imagen, sube y baja, se mece apenas y
    // respira abriendo y cerrando las tapas; cuando el jugador se acerca, se abre del todo.
    // No gira hacia el jugador: conserva el rumbo con el que quedó sobre la imagen.
    // Mientras la oscuridad lo consume, tiembla. Sólo con la partida en curso: en la
    // sincronización (antes de INICIAR NOCHE) queda apoyado y quieto sobre la imagen, y al
    // arrancar la noche se eleva suave (Peso).
    //
    // Anima la RAÍZ del libro y no un hijo a propósito: la oscuridad radial y el punto que hay
    // que alumbrar (RitualBookView.PuntoDeLuz) usan la posición y los ejes de la raíz, así
    // siguen pegados a la malla. La pose base es la que dejó RitualBookView.TrySpawn. Corre
    // antes que RitualBookView (DefaultExecutionOrder) para que su LateUpdate ya vea la pose
    // de este frame.
    //
    // Las tapas: el prefab no guarda cuáles son, así que se identifican como en
    // RitualBookPrefabSetup (el par de piezas con pivotes más cercanos; el lomo es la otra) y
    // giran alrededor del lomo (+Z de la raíz). Sus capas de oscuridad son hijas suyas y las
    // siguen. Si el modelo no tiene tres piezas, el libro igual flota y gira, sin tapas.
    [DefaultExecutionOrder(-10)]
    public class RitualBookAnimacion : MonoBehaviour
    {
        [Header("Flotar")]
        [Tooltip("Cuánto se eleva sobre la imagen (m).")]
        [SerializeField] private float _altura = 0.04f;
        [Tooltip("Cuánto sube y baja (m).")]
        [SerializeField] private float _amplitud = 0.012f;
        [Tooltip("Segundos que tarda en despertar al empezar la noche (y en volver a apoyarse).")]
        [SerializeField] private float _despertar = 1.5f;
        [Tooltip("Segundos de una subida y bajada completa.")]
        [SerializeField] private float _periodo = 3.2f;

        [Header("Mecerse")]
        [Tooltip("Vaivén suave (grados), para que nunca quede quieto del todo.")]
        [SerializeField] private float _vaiven = 4f;

        [Header("Tapas")]
        [Tooltip("Cuánto se cierran las tapas con el jugador lejos (grados).")]
        [SerializeField] private float _cierreLejos = 38f;
        [Tooltip("Respiración de las tapas (grados), siempre presente.")]
        [SerializeField] private float _respiracion = 5f;
        [SerializeField] private float _distanciaCerca = 1.5f;
        [SerializeField] private float _distanciaLejos = 4f;

        [Header("Temblor al consumirse")]
        [Tooltip("Temblor (grados) con la oscuridad al 100%.")]
        [SerializeField] private float _temblor = 3f;

        private struct Tapa
        {
            public Transform t;
            public Vector3 pos0;
            public Quaternion rot0;
            public Vector3 pivote;   // en el espacio del padre de la tapa
            public Vector3 eje;      // ídem
            public float signo;      // hacia dónde gira para cerrarse (el borde libre sube)
        }

        private readonly List<Tapa> _tapas = new();
        private Vector3 _basePos;
        private Quaternion _baseRot;
        private float _cierre, _velCierre;
        private float _fase;
        private float _peso;   // 0 = apoyado y quieto, 1 = libro encantado
        private RitualBookView _vista;

        private void Start()
        {
            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
            _vista = GetComponent<RitualBookView>();
            _fase = Random.Range(0f, 10f);   // que dos libros no respiren en sincronía
            BuscarTapas();
        }

        private void LateUpdate()
        {
            float t = Time.time + _fase;
            float dt = Time.deltaTime;
            var cam = Camera.main;

            var net = NetworkManager.Instance;
            bool enPartida = net != null && net.GameStarted;
            _peso = Mathf.MoveTowards(_peso, enPartida ? 1f : 0f, dt / Mathf.Max(0.05f, _despertar));
            float w = Mathf.SmoothStep(0f, 1f, _peso);

            // Distancia al jugador: de cerca las tapas se abren del todo.
            float distancia = float.PositiveInfinity;
            if (cam != null)
            {
                Vector3 haciaJugador = cam.transform.position - transform.position;
                haciaJugador.y = 0f;
                distancia = haciaJugador.magnitude;
            }

            float oscuridad = _vista != null ? _vista.Oscuridad01 : 0f;
            float temblor = _temblor * oscuridad;
            float roll = Mathf.Sin(t * 0.9f) * _vaiven + (Mathf.PerlinNoise(t * 9f, 0.3f) - 0.5f) * 2f * temblor;
            float pitch = Mathf.Sin(t * 0.7f) * _vaiven * 0.5f +
                          (Mathf.PerlinNoise(0.7f, t * 9f) - 0.5f) * 2f * temblor;
            var animada = _baseRot * Quaternion.Euler(pitch, 0f, roll);
            transform.localRotation = Quaternion.Slerp(_baseRot, animada, w);

            // ── Flotar ──
            float bob = Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.1f, _periodo)) * _amplitud;
            transform.localPosition = _basePos + Vector3.up * ((_altura + bob) * w);

            // ── Tapas: abiertas de cerca, entornadas de lejos, siempre respirando ──
            if (_tapas.Count == 0) return;
            float lejos = Mathf.InverseLerp(_distanciaCerca, _distanciaLejos, distancia);
            float objetivoCierre = (_cierreLejos * lejos + (Mathf.Sin(t * 2.2f) * 0.5f + 0.5f) * _respiracion) * w;
            _cierre = Mathf.SmoothDamp(_cierre, objetivoCierre, ref _velCierre, 0.35f, Mathf.Infinity, dt);
            foreach (var tapa in _tapas)
            {
                if (tapa.t == null) continue;
                var r = Quaternion.AngleAxis(tapa.signo * _cierre, tapa.eje);
                tapa.t.localPosition = tapa.pivote + r * (tapa.pos0 - tapa.pivote);
                tapa.t.localRotation = r * tapa.rot0;
            }
        }

        private void BuscarTapas()
        {
            var piezas = new List<Transform>();
            foreach (var filtro in GetComponentsInChildren<MeshFilter>(true))
            {
                // Las capas de oscuridad (RitualBookView) son copias hijas de cada pieza.
                if (filtro.sharedMesh == null || filtro.gameObject.name.StartsWith("__")) continue;
                piezas.Add(filtro.transform);
            }
            if (piezas.Count != 3) return;

            // Tapas = el par de pivotes más cercanos (comparten la bisagra); lomo = el resto.
            int a = 0, b = 1, lomo = 2;
            float d01 = Vector3.Distance(piezas[0].position, piezas[1].position);
            float d02 = Vector3.Distance(piezas[0].position, piezas[2].position);
            float d12 = Vector3.Distance(piezas[1].position, piezas[2].position);
            if (d02 < d01 && d02 <= d12) { a = 0; b = 2; lomo = 1; }
            else if (d12 < d01 && d12 < d02) { a = 1; b = 2; lomo = 0; }

            Vector3 centroLomo = Bounds(piezas[lomo]).center;
            Vector3 ejeMundo = transform.forward;   // el lomo va a lo largo de +Z del prefab
            foreach (int i in new[] { a, b })
            {
                var tapa = piezas[i];
                var p = tapa.parent;
                if (p == null) continue;
                Vector3 r = Bounds(tapa).center - centroLomo;
                r -= Vector3.Project(r, ejeMundo);
                float signo = Vector3.Cross(ejeMundo, r).y >= 0f ? 1f : -1f;
                _tapas.Add(new Tapa
                {
                    t = tapa,
                    pos0 = tapa.localPosition,
                    rot0 = tapa.localRotation,
                    pivote = p.InverseTransformPoint(centroLomo),
                    eje = p.InverseTransformDirection(ejeMundo).normalized,
                    signo = signo,
                });
            }
        }

        private static Bounds Bounds(Transform t)
        {
            var r = t.GetComponent<Renderer>();
            return r != null ? r.bounds : new Bounds(t.position, Vector3.zero);
        }
    }
}
