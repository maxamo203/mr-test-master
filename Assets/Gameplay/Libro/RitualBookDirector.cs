using UnityEngine;

namespace Gameplay
{
    // El LIBRO RITUAL, la mecánica que corre toda la noche de fondo: el libro descansa
    // sobre la imagen de referencia, arranca ABIERTO y se va cerrando solo. Alumbrarlo con
    // la linterna lo vuelve a abrir, y varios jugadores alumbrando ACUMULAN (se abre más
    // rápido). Si llega a cerrarse del todo: game over para todos.
    //
    // SERVER-AUTHORITATIVE: sólo el host simula la apertura — es el único que conoce las
    // poses y las linternas de todos. El valor viaja a los clientes por RitualBook a 5 Hz;
    // la apertura cambia del orden de 1/bookCloseSeconds por segundo (minutos de recorrido),
    // así que a ese ritmo el salto entre paquetes es imperceptible y no hace falta interpolar.
    //
    // La apertura vive acá y no en el visual porque el visual cuelga del anchor de la
    // imagen y se destruye/recrea en cada recalibración (ver ARImageAnchor.RestartTracking).
    //
    // Se auto-crea (Ensure) desde GameDirector (host) y desde RitualBookView.TrySpawn
    // (cliente) — no hay que ponerlo en la escena.
    public class RitualBookDirector : MonoBehaviour
    {
        public static RitualBookDirector Instance { get; private set; }

        // Cada cuánto (s) el server manda la apertura a los clientes.
        private const float SendInterval = 0.2f;

        // 0 = cerrado (game over), 1 = abierto del todo.
        public float Apertura01 { get; private set; } = 1f;

        // Jugadores que lo alumbraron en el último frame (sólo host; diagnóstico).
        public int Alumbrando { get; private set; }

        private NightConfig _night;
        private bool  _running;
        private float _sendTimer;

        // NetworkManager al que estamos suscritos. No alcanza con un bool: sobrevivimos a
        // los cambios de escena (DontDestroyOnLoad) y SceneFlow destruye el NetworkManager,
        // así que la próxima sesión trae uno nuevo al que hay que re-suscribirse.
        private NetworkManager _suscritoA;

        public static RitualBookDirector Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("RitualBookDirector");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<RitualBookDirector>();
            }
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_suscritoA != null) _suscritoA.OnRitualBook -= SetApertura;
            _suscritoA = null;
            if (Instance == this) Instance = null;
        }

        // Lo llama el GameDirector (server) al arrancar la partida.
        public void StartRun()
        {
            var net = NetworkManager.Instance;
            if (net == null || !net.IsServer) return;

            _night     = GameSession.Instance != null ? GameSession.Instance.SelectedNight : null;
            _sendTimer = 0f;
            Alumbrando = 0;
            SetApertura(1f);                  // el libro arranca ABIERTO
            net.ServerSendRitualBook(1f);
            _running = true;
        }

        // Corta la corrida sin cerrar la sesión (ver Gameplay.NightTransition).
        public void StopRun()
        {
            _running   = false;
            Alumbrando = 0;
        }

        // Parte local del reinicio de la noche: el libro vuelve a verse abierto en ESTE
        // dispositivo (el host además reenvía el valor al arrancar la próxima noche).
        public void Reiniciar()
        {
            Alumbrando = 0;
            SetApertura(1f);
        }

        private void Update()
        {
            // El NetworkManager puede no existir todavía al crearnos, y cambia de instancia
            // entre sesiones (ver _suscritoA).
            var net = NetworkManager.Instance;
            if (net != _suscritoA)
            {
                if (_suscritoA != null) _suscritoA.OnRitualBook -= SetApertura;
                if (net        != null) net.OnRitualBook        += SetApertura;
                _suscritoA = net;
            }

            // Cliente: sólo recibe la apertura, no simula nada.
            if (!_running) return;
            if (net == null || !net.IsServer) return;
            if (_night == null || !_night.bookActive) return;

            // Sin visual no sabemos dónde está el libro (anchor perdido / todavía sin
            // calibrar). Congelamos la apertura en vez de cerrarlo a ciegas: el jugador
            // tampoco lo podría alumbrar, así que sería una muerte que no puede evitar.
            var view = RitualBookView.Active;
            if (view == null) return;

            float dt = Time.deltaTime;

            // El cono sale de la linterna REAL, no del número de la NightConfig: ése es el
            // del repel del Sorken y quedó mucho más abierto que el haz (30° / 8 m contra
            // 7.2° / 4.2 m del prefab), así que el libro se abría con la linterna
            // claramente afuera. Los valores de la noche quedan de respaldo.
            float ang   = _night.flashlightConeAngleDeg;
            float range = _night.flashlightRange;
            if (PlayerLights.TryConoReal(out var angReal, out var rangeReal))
            {
                ang   = angReal;
                range = rangeReal;
            }

            Alumbrando = PlayerLights.CountIlluminating(
                view.PuntoDeLuz, ang, range, view.RadioAproximado);

            // Se cierra solo a ritmo constante; cada linterna encima suma su propio ritmo
            // de apertura (de ahí que varios jugadores lo abran más rápido).
            float delta = (_night.BookOpenRatePerPlayer * Alumbrando - _night.BookCloseRate) * dt;
            SetApertura(Apertura01 + delta);

            _sendTimer -= dt;
            if (_sendTimer <= 0f)
            {
                _sendTimer = SendInterval;
                net.ServerSendRitualBook(Apertura01);
            }

            if (Apertura01 <= 0f) CerrarYMatar();
        }

        // El libro se cerró: game over compartido. Por ahora no hay otra consecuencia —
        // mueren todos y cada uno ve su pantalla de muerte.
        private void CerrarYMatar()
        {
            _running = false;
            NetworkManager.Instance?.ServerSendRitualBook(0f);   // que todos lo vean cerrado
            int muertos = ServerDeaths.KillAll();
            Debug.Log($"[LibroRitual] El libro se cerró: game over ({muertos} jugadores).");
        }

        private void SetApertura(float valor)
        {
            Apertura01 = Mathf.Clamp01(valor);
            if (RitualBookView.Active != null) RitualBookView.Active.Aplicar(Apertura01);
        }

        // Línea para el HUD de diagnóstico (development builds). Null si no corre acá.
        public string DebugLine()
        {
            if (!_running) return null;
            return $"libro={Apertura01:P0}  alumbrando={Alumbrando}";
        }
    }
}
