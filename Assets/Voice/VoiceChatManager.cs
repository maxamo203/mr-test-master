using System.Collections.Generic;
using UnityEngine;

namespace Voice
{
    // Chat de voz de la sala (historia "Tener un chat de voz en la sesión" del backlog).
    //
    // Modelo: el mismo que el resto del juego —el servidor manda—. Cada cliente manda
    // sus frames de voz al host y el host los REENVÍA al resto (y los reproduce). Así
    // no hace falta que los clientes se conozcan entre sí ni abrir puertos extra: viaja
    // por la conexión TCP que ya existe (ver NetworkManager.ServerSendVoice).
    //
    // Rendimiento: en un jugador solo NO se enciende nada (ni micrófono, ni AudioSources,
    // ni tráfico) — Update sale en la primera línea. Ver la sección de tiers del CLAUDE.md.
    //
    // Se auto-crea en cualquier escena, igual que GamepadManager, y sobrevive el cambio
    // de escena; se apaga sola cuando la sesión de red se termina.
    [DefaultExecutionOrder(-50)]
    public class VoiceChatManager : MonoBehaviour
    {
        public static VoiceChatManager Instance { get; private set; }

        // Segundos que un jugador se sigue mostrando como "hablando" en la UI tras el
        // último paquete (los frames llegan cada 30 ms; esto evita que titile).
        private const float MarcaHablando = 0.25f;

        private readonly VoiceCapture                  _captura = new();
        private readonly Dictionary<uint, VoiceStream> _streams = new();
        // Volumen por jugador (0..1). NO se persiste: los clientId se reasignan en cada
        // sala, así que guardarlos en disco haría que el ajuste caiga en otra persona.
        private readonly Dictionary<uint, float>       _volumen = new();
        private readonly List<uint>                    _otros   = new();
        private readonly List<uint>                    _quitar  = new();   // scratch

        private NetworkManager _suscritoA;
        private bool           _permisoPedido;
        private bool           _avisoSinMic;

        // ── API para la UI (menú de pausa) ────────────────────────────────────

        // ¿Tiene sentido mostrar el chat de voz? Sólo en una sesión multijugador.
        public static bool SesionConVoz
        {
            get
            {
                var net = NetworkManager.Instance;
                if (net == null || !net.InSession) return false;
                var ses = Gameplay.GameSession.Instance;
                if (ses != null && ses.Mode == Gameplay.GameSession.SessionMode.SinglePlayer)
                    return false;
                return true;
            }
        }

        // Jugadores de la sala menos vos (no tiene sentido regular tu propio volumen).
        public IReadOnlyList<uint> Otros => _otros;

        public static string NombreDe(uint clientId) =>
            clientId == 0 ? "ANFITRIÓN" : $"JUGADOR {clientId + 1}";

        public float VolumenDe(uint clientId) =>
            _volumen.TryGetValue(clientId, out var v) ? v : 1f;

        public void SetVolumenDe(uint clientId, float v)
        {
            v = Mathf.Clamp01(v);
            _volumen[clientId] = v;
            if (_streams.TryGetValue(clientId, out var st))
                st.SetVolumen(v * GameOptions.VozVolumen);
        }

        public bool EstaHablando(uint clientId) =>
            _streams.TryGetValue(clientId, out var st) &&
            Time.unscaledTime - st.UltimoPaquete < MarcaHablando;

        // Nivel del micrófono propio (0..1), para la barra de la pantalla de opciones.
        public float NivelMic => _captura.Nivel;
        public bool  MicAbierto => _captura.Activa;

        // ── Ciclo de vida ─────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("VoiceChat");
            go.AddComponent<VoiceChatManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            if (!SesionConVoz) { Apagar(); return; }

            Sincronizar();

            // Captura: sólo si hay alguien a quien hablarle, el jugador dejó el micrófono
            // activado y el SO nos dio permiso. Lo de "_otros" no es cosmético: tener el
            // micrófono abierto en iOS cambia la categoría de la sesión de audio (baja el
            // resto del sonido), así que estando solo en la sala se suelta.
            if (_otros.Count > 0 && GameOptions.VozMic && PuedeCapturar())
            {
                if (!_captura.Activa) _captura.Iniciar();
                if (_captura.Activa)  _captura.Procesar(UmbralActual(), Enviar);
            }
            else if (_captura.Activa)
            {
                _captura.Detener();
            }
        }

        private void OnDestroy()
        {
            Apagar();
            if (Instance == this) Instance = null;
        }

        private void OnApplicationPause(bool pausada)
        {
            // En background el micrófono queda tomado y en iOS puede cortar la sesión de
            // audio del juego entero. Lo soltamos y se vuelve a abrir solo al volver.
            if (pausada && _captura.Activa) _captura.Detener();
        }

        // ── Red ───────────────────────────────────────────────────────────────

        // NetworkManager se destruye y se vuelve a crear al cambiar de escena
        // (SceneFlow.GoTo), así que re-enganchamos los eventos cuando cambia la instancia.
        private void Sincronizar()
        {
            var net = NetworkManager.Instance;
            if (_suscritoA == net) return;

            if (_suscritoA != null)
            {
                _suscritoA.OnVoiceData     -= AlRecibirVoz;
                _suscritoA.OnRosterChanged -= AlCambiarSala;
            }

            _suscritoA = net;

            if (net != null)
            {
                net.OnVoiceData     += AlRecibirVoz;
                net.OnRosterChanged += AlCambiarSala;
            }

            AlCambiarSala();
        }

        private uint IdLocal()
        {
            var net = NetworkManager.Instance;
            if (net == null) return 0;
            return net.IsServer ? 0u : net.LocalClientId;
        }

        // Reconstruye la lista de compañeros y tira los streams de los que se fueron.
        private void AlCambiarSala()
        {
            _otros.Clear();

            var net = NetworkManager.Instance;
            if (net != null)
            {
                uint yo = IdLocal();
                foreach (var id in net.Roster)
                    if (id != yo) _otros.Add(id);
            }

            // Streams de jugadores que ya no están en la sala.
            _quitar.Clear();
            foreach (var kv in _streams)
                if (!_otros.Contains(kv.Key)) _quitar.Add(kv.Key);

            for (int i = 0; i < _quitar.Count; i++)
            {
                _streams[_quitar[i]].Destruir();
                _streams.Remove(_quitar[i]);
            }
            _quitar.Clear();
        }

        private void AlRecibirVoz(uint emisor, byte[] adpcm)
        {
            if (adpcm == null || adpcm.Length == 0) return;
            if (emisor == IdLocal()) return;   // nunca deberíamos oírnos a nosotros mismos
            ObtenerStream(emisor).Escribir(adpcm, adpcm.Length);
        }

        private VoiceStream ObtenerStream(uint clientId)
        {
            if (_streams.TryGetValue(clientId, out var st)) return st;

            st = new VoiceStream(transform, "Voz_" + clientId);
            st.SetVolumen(VolumenDe(clientId) * GameOptions.VozVolumen);
            _streams[clientId] = st;
            return st;
        }

        private void Enviar(byte[] datos, int largo)
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            if (net.IsServer) net.ServerSendVoice(0, datos, largo);
            else              net.ClientSendVoice(datos, largo);
        }

        // ── Ajustes ───────────────────────────────────────────────────────────

        // La sensibilidad de la UI (0..1) se mapea al umbral de RMS al revés: más
        // sensibilidad = umbral más bajo = se abre el micro con menos voz.
        // Público porque el menú de pausa dibuja la marca del umbral sobre la barra de
        // nivel del micrófono, y las dos cosas tienen que usar el mismo número.
        public static float UmbralActual() =>
            Mathf.Lerp(0.060f, 0.002f, GameOptions.VozSensibilidad);

        // Reaplica el volumen global a todos los streams (la llama el menú de pausa al
        // mover el slider general).
        public void RefrescarVolumenes()
        {
            float global = GameOptions.VozVolumen;
            foreach (var kv in _streams) kv.Value.SetVolumen(VolumenDe(kv.Key) * global);
        }

        private bool PuedeCapturar()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
            {
                if (!_permisoPedido)
                {
                    _permisoPedido = true;
                    UnityEngine.Android.Permission.RequestUserPermission(
                        UnityEngine.Android.Permission.Microphone);
                }
                return false;
            }
#elif UNITY_IOS && !UNITY_EDITOR
            if (!_permisoPedido)
            {
                _permisoPedido = true;
                Application.RequestUserAuthorization(UserAuthorization.Microphone);
            }
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone)) return false;
#endif
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                if (!_avisoSinMic)
                {
                    _avisoSinMic = true;
                    Debug.LogWarning("[Voz] Este dispositivo no reporta micrófonos.");
                }
                return false;
            }
            return true;
        }

        // Suelta micrófono y AudioSources: fuera de una sala esto no debe costar nada.
        private void Apagar()
        {
            if (_captura.Activa) _captura.Detener();

            if (_streams.Count > 0)
            {
                foreach (var kv in _streams) kv.Value.Destruir();
                _streams.Clear();
            }

            if (_otros.Count > 0) _otros.Clear();

            if (_suscritoA != null)
            {
                _suscritoA.OnVoiceData     -= AlRecibirVoz;
                _suscritoA.OnRosterChanged -= AlCambiarSala;
                _suscritoA = null;
            }
        }
    }
}
