using UnityEngine;

namespace Gameplay
{
    // Simulacion SERVER-AUTHORITATIVE del libro ritual. Espera un intervalo aleatorio,
    // oscurece el libro desde el centro durante una ventana corta y permite salvarlo
    // manteniendo una linterna apuntada durante el tiempo configurado.
    public class RitualBookDirector : MonoBehaviour
    {
        public static RitualBookDirector Instance { get; private set; }

        private const float SendInterval = 0.2f;

        private bool UsaFlowLocal => NetworkManager.Instance != null &&
                                      NetworkManager.Instance.IsServer && _flow != null;
        public float Oscuridad01 => UsaFlowLocal ? _flow.Darkness01 : _oscuridadRemota;
        public float Defensa01 => UsaFlowLocal ? _flow.Defense01 : 0f;
        public RitualBookPhase Fase => UsaFlowLocal ? _flow.Phase : RitualBookPhase.Waiting;
        public int Alumbrando { get; private set; }
        public bool TodosAlumbrando { get; private set; }
        public event System.Action OnVelethInvoked;

        // Compatibilidad de lectura para codigo/editor antiguo: 1 era abierto y 0 cerrado.
        public float Apertura01 => 1f - Oscuridad01;

        private NightConfig _night;
        private RitualBookFlow _flow;
        private bool _running;
        private float _sendTimer;
        private float _oscuridadRemota;
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
            if (_suscritoA != null) _suscritoA.OnRitualBook -= SetOscuridadRemota;
            _suscritoA = null;
            if (Instance == this) Instance = null;
        }

        public void StartRun()
        {
            var net = NetworkManager.Instance;
            if (net == null || !net.IsServer) return;

            _night = GameSession.Instance != null ? GameSession.Instance.SelectedNight : null;
            _sendTimer = 0f;
            Alumbrando = 0;
            TodosAlumbrando = false;
            _oscuridadRemota = 0f;

            if (_night != null && _night.bookActive)
            {
                _flow = new RitualBookFlow(_night.RandomBookEventDelay);
                _flow.Restart();
            }
            else
            {
                _flow = null;
            }

            // Silencioso: arrancar la noche pone la oscuridad en 0, y si la noche anterior
            // terminó con el libro perdido (oscuridad 1) esa bajada sonaría como "lo salvaste".
            AplicarVista(0f, silencioso: true);
            net.ServerSendRitualBook(0f);
            _running = true;
        }

        // Frena la mecanica del libro. SUELTA el flow a proposito: este director es
        // DontDestroyOnLoad, asi que un flow a medio consumir sobrevivia al cambio de
        // escena y se reanudaba en cuanto habia un server nuevo — antes incluso de que
        // arrancara la partida — invocando a Veleth en la sala de sincronizacion.
        // StartRun siempre construye uno nuevo, asi que no hay nada que conservar.
        public void StopRun()
        {
            _running = false;
            _flow = null;
            _oscuridadRemota = 0f;
            Alumbrando = 0;
            TodosAlumbrando = false;
        }

        public void Reiniciar()
        {
            Alumbrando = 0;
            TodosAlumbrando = false;
            _oscuridadRemota = 0f;
            _flow?.Restart();
            AplicarVista(0f, silencioso: true);   // reinicio de noche, no es que lo salvaron
        }

        private void Update()
        {
            var net = NetworkManager.Instance;
            if (net != _suscritoA)
            {
                if (_suscritoA != null) _suscritoA.OnRitualBook -= SetOscuridadRemota;
                if (net != null) _suscritoA = net;
                else             _suscritoA = null;
                if (_suscritoA != null) _suscritoA.OnRitualBook += SetOscuridadRemota;
            }

            // GameStarted ademas de _running: el libro ya existe en la SALA (lo spawnea
            // ARImageAnchor apenas aparece la imagen, que solo exige InSession), asi que
            // sin este guard la mecanica corre durante la sincronizacion.
            if (!_running || net == null || !net.IsServer || !net.GameStarted || _flow == null) return;

            // Si se perdio el anchor, congelamos la mecanica: el jugador no puede ver ni
            // defender el libro y no debe perder por una recalibracion.
            var view = RitualBookView.Active;
            if (view == null) return;

            // Se mide tambien durante Waiting. El flow no acumula defensa antes del
            // ataque, pero asi el primer fragmento de frame del evento cuenta si el
            // jugador ya estaba apuntando al libro.
            float ang = _night.flashlightConeAngleDeg;
            float range = _night.flashlightRange;
            if (PlayerLights.TryConoReal(out var angReal, out var rangeReal))
            {
                ang = angReal;
                range = rangeReal;
            }
            Alumbrando = PlayerLights.CountIlluminating(
                view.PuntoDeLuz, ang, range, view.RadioAproximado);
            int jugadoresVivos = ContarJugadoresVivos(net);
            TodosAlumbrando = jugadoresVivos > 0 && Alumbrando >= jugadoresVivos;

            var result = _flow.Tick(Time.deltaTime, Alumbrando, jugadoresVivos,
                                    _night.bookConsumeSeconds, _night.bookDefenseSeconds);
            AplicarVista(_flow.Darkness01);

            if ((result & RitualBookTickResult.ConsumptionStarted) != 0)
                Debug.Log("[LibroRitual] La oscuridad empezo a consumir el libro.");

            if ((result & RitualBookTickResult.Saved) != 0)
                Debug.Log("[LibroRitual] El libro fue salvado con la linterna.");

            _sendTimer -= Time.deltaTime;
            if (_sendTimer <= 0f || result != RitualBookTickResult.None)
            {
                _sendTimer = SendInterval;
                net.ServerSendRitualBook(_flow.Darkness01);
            }

            if ((result & RitualBookTickResult.Consumed) != 0)
                InvocarVeleth();
        }

        private void InvocarVeleth()
        {
            _running = false;
            NetworkManager.Instance?.ServerSendRitualBook(1f);
            var view = RitualBookView.Active;
            Vector3 origen = view != null ? view.PuntoDeLuz : Vector3.zero;
            if (view != null) view.SetDisponible(false);

            bool invocada = VelethDirector.Ensure().StartHunt(origen);
            OnVelethInvoked?.Invoke();
            Debug.Log(invocada
                ? "[LibroRitual] El libro fue consumido: Veleth fue invocada."
                : "[LibroRitual] El libro fue consumido, pero no se pudo crear a Veleth.");
        }

        private void SetOscuridadRemota(float oscuridad01)
        {
            // El host usa su flow local; este valor es la fuente de verdad en clientes.
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsServer) return;
            _oscuridadRemota = Mathf.Clamp01(oscuridad01);
            AplicarVista(_oscuridadRemota);
        }

        private static void AplicarVista(float oscuridad01, bool silencioso = false)
        {
            if (RitualBookView.Active != null)
                RitualBookView.Active.AplicarOscuridad(oscuridad01, silencioso);
        }

        private static int ContarJugadoresVivos(NetworkManager net)
        {
            int vivos = ServerDeaths.IsAlive(0) ? 1 : 0;
            foreach (uint clientId in net.ConnectedClients)
                if (ServerDeaths.IsAlive(clientId)) vivos++;
            return vivos;
        }

        public string DebugLine()
        {
            if (!_running || _flow == null) return null;
            int jugadoresVivos = NetworkManager.Instance != null
                ? ContarJugadoresVivos(NetworkManager.Instance)
                : 1;
            float velocidadAtaque = TodosAlumbrando ? 0f
                : Alumbrando == 0 ? 1f
                : 1f - Alumbrando / (float)Mathf.Max(1, jugadoresVivos);
            return $"libro={_flow.Phase} oscuridad={_flow.Darkness01:P0} " +
                   $"defensa={_flow.Defense01:P0} alumbrando={Alumbrando} " +
                   $"todos={TodosAlumbrando} velocidadAtaque=" +
                   $"{velocidadAtaque:P0}";
        }
    }
}
