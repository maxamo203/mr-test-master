using Gamepad;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bateries
{
    // Controlador de recoleccion (lado cliente, corre en TODOS los jugadores incluido
    // el host). Detecta la pila que el jugador esta apuntando con la camara y, al
    // apretar el boton (en pantalla, gamepad o tecla), pide recogerla.
    //
    // El pickup es autoritativo del server: el host lo resuelve local; el cliente manda
    // BatteryPickup y el server valida cercania. La carga se acredita a la linterna local
    // (host: directo desde el manager; cliente: via el evento OnBatteryCollected).
    //
    // Wiring: poner este componente en un GameObject de la escena multijugador. La
    // linterna (Flashlight) ya existe en la escena.
    public class BatteryPickupController : MonoBehaviour
    {
        [Header("Apuntado")]
        [Tooltip("Distancia maxima (m) a la que se puede apuntar/recoger una pila.")]
        [SerializeField] private float aimMaxDistance = 2.5f;
        [Tooltip("Semiangulo (grados) del cono de apuntado desde el centro de la camara.")]
        [SerializeField] private float aimAngle = 12f;
        [Tooltip("Cada cuanto (s) recalcular la pila apuntada. No hace falta cada frame.")]
        [SerializeField] private float aimCheckInterval = 0.1f;

        [Header("HUD")]
        [SerializeField] private bool showChargeBar = true;

        private BatteryEntity _target;
        private Flashlight    _flashlight;
        private Camera        _cam;
        private bool          _prevSouth;
        private float         _aimTimer;

        // Estilos IMGUI cacheados: crearlos en cada OnGUI genera basura (GC) cada frame.
        private GUIStyle _btnStyle, _barLabelStyle;
        private bool     _stylesReady;

        // Suscribir en Start para garantizar que NetworkManager.Instance ya exista.
        private void Start()
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnBatteryCollected += HandleCollected;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnBatteryCollected -= HandleCollected;
        }

        private void Update()
        {
            // Recalcular el apuntado a intervalos, no cada frame.
            _aimTimer -= Time.deltaTime;
            if (_aimTimer <= 0f)
            {
                _aimTimer = aimCheckInterval;
                _target = FindAimedBattery();
            }
            HandleButtonInput();
        }

        // ── Deteccion de la pila apuntada ─────────────────────────────────────

        private BatteryEntity FindAimedBattery()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return null;

            var list = BatteryEntity.Active;
            if (list.Count == 0) return null;

            BatteryEntity best = null;
            float bestAngle = aimAngle;
            float maxD2     = aimMaxDistance * aimMaxDistance;
            Vector3 camPos  = _cam.transform.position;
            Vector3 camFwd  = _cam.transform.forward;

            for (int i = 0; i < list.Count; i++)
            {
                var be = list[i];
                if (be == null) continue;

                Vector3 to = be.transform.position - camPos;
                if (to.sqrMagnitude > maxD2) continue;

                float ang = Vector3.Angle(camFwd, to);
                if (ang < bestAngle) { bestAngle = ang; best = be; }
            }
            return best;
        }

        // ── Input (gamepad + tecla) ───────────────────────────────────────────

        private void HandleButtonInput()
        {
            bool south = GamepadManager.Instance != null && GamepadManager.Instance.ReadState().south;
            bool pressed = south && !_prevSouth; // edge-detect del boton sur (A / Cross)
            _prevSouth = south;

#if UNITY_EDITOR
            // Editor: tecla E via el nuevo Input System (el proyecto usa solo ese).
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) pressed = true;
#endif
            if (pressed) TryPickup();
        }

        private void TryPickup()
        {
            if (_target == null) return;
            var net = NetworkManager.Instance;
            if (net == null || !net.GameStarted) return;

            if (net.IsServer)
                BatterySpawnManager.Instance?.ServerHandlePickup(0, _target.NetworkId);
            else
                net.ClientSendBatteryPickup(_target.NetworkId);

            _target = null; // evitar reenvios hasta el proximo chequeo de apuntado
        }

        // ── Cliente: llegó la carga acreditada por el server ──────────────────

        private void HandleCollected(byte rarityIndex, float charge)
        {
            EnsureFlashlight();
            _flashlight?.AddCharge(charge);
        }

        private void EnsureFlashlight()
        {
            if (_flashlight == null) _flashlight = FindFirstObjectByType<Flashlight>();
        }

        // ── HUD (boton de recoger + barra de carga) ───────────────────────────

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 20 };
            _barLabelStyle = new GUIStyle(GUI.skin.label);
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (showChargeBar)
            {
                EnsureFlashlight();
                if (_flashlight != null)
                {
                    float pct = _flashlight.Charge01;
                    var barRect = new Rect(20, Screen.height - 40, 220, 22);
                    GUI.Box(barRect, GUIContent.none);
                    var fill = new Rect(barRect.x + 2, barRect.y + 2,
                                        (barRect.width - 4) * pct, barRect.height - 4);
                    var prev = GUI.color;
                    GUI.color = _flashlight.IsEmpty ? Color.red
                              : pct < 0.25f ? new Color(1f, 0.5f, 0f) : Color.green;
                    GUI.DrawTexture(fill, Texture2D.whiteTexture);
                    GUI.color = prev;
                    GUI.Label(new Rect(barRect.x + 6, barRect.y, barRect.width, barRect.height),
                              $"Linterna {Mathf.RoundToInt(pct * 100f)}%", _barLabelStyle);
                }
            }

            if (_target != null)
            {
                string hint = GamepadManager.Instance != null && GamepadManager.Instance.IsConnected
                    ? "Recoger (A)" : "Recoger";
                var btn = new Rect(Screen.width * 0.5f - 90, Screen.height * 0.62f, 180, 54);
                if (GUI.Button(btn, hint, _btnStyle)) TryPickup();
            }
        }
    }
}
