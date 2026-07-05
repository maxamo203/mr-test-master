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

        [Header("HUD")]
        [SerializeField] private bool showChargeBar = true;

        private BatteryEntity _target;
        private Flashlight    _flashlight;
        private bool          _prevSouth;

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
            _target = FindAimedBattery();
            HandleButtonInput();
        }

        // ── Deteccion de la pila apuntada ─────────────────────────────────────

        private BatteryEntity FindAimedBattery()
        {
            var cam = Camera.main;
            if (cam == null || EntityRegistry.Instance == null) return null;

            BatteryEntity best = null;
            float bestAngle = aimAngle;
            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;

            foreach (var e in EntityRegistry.Instance.All)
            {
                var be = e.GetComponent<BatteryEntity>();
                if (be == null) continue;

                Vector3 to = be.transform.position - camPos;
                if (to.sqrMagnitude > aimMaxDistance * aimMaxDistance) continue;

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

            _target = null; // evitar reenvios hasta el proximo frame de deteccion
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

        private void OnGUI()
        {
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
                              $"Linterna {Mathf.RoundToInt(pct * 100f)}%");
                }
            }

            if (_target != null)
            {
                string hint = GamepadManager.Instance != null && GamepadManager.Instance.IsConnected
                    ? "Recoger (A)" : "Recoger";
                var btn = new Rect(Screen.width * 0.5f - 90, Screen.height * 0.62f, 180, 54);
                var style = new GUIStyle(GUI.skin.button) { fontSize = 20 };
                if (GUI.Button(btn, hint, style)) TryPickup();
            }
        }
    }
}
