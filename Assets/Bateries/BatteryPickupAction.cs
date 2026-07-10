using UnityEngine;

namespace Bateries
{
    // Acción contextual: recoger la pila que el jugador está apuntando con la cámara.
    // Prioridad alta (gana a la linterna). Autónoma: hace el apuntado y el pickup.
    //
    // El pickup es autoritativo del server: el host lo resuelve local; el cliente manda
    // BatteryPickup y el server valida cercanía.
    public class BatteryPickupAction : MonoBehaviour, IContextAction
    {
        [Header("Prioridad")]
        [SerializeField] private int priority = 100;
        [Tooltip("Mostrar el botón en pantalla cuando podés recoger una pila.")]
        [SerializeField] private bool showActionButton = true;

        [Header("Apuntado")]
        [Tooltip("Distancia máxima (m) a la que se puede apuntar/recoger una pila.")]
        [SerializeField] private float aimMaxDistance = 2.5f;
        [Tooltip("Semiángulo (grados) del cono de apuntado desde el centro de la cámara.")]
        [SerializeField] private float aimAngle = 12f;
        [Tooltip("Cada cuánto (s) recalcular la pila apuntada. No hace falta cada frame.")]
        [SerializeField] private float aimCheckInterval = 0.1f;
        [Tooltip("No permitir apuntar/recoger pilas a través de paredes/muebles (layer Placed).")]
        [SerializeField] private bool blockThroughWalls = true;

        public int  Priority         => priority;
        public bool ShowActionButton => showActionButton;

        private Camera        _cam;
        private BatteryEntity _aimed;
        private float         _timer;
        private int           _occludeMask = -1; // -1 = sin inicializar
        private static int    _lastSyncFrame = -1;

        public bool TryResolve(out string label)
        {
            // Throttle: recalcular el apuntado a intervalos, no cada frame.
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = aimCheckInterval;
                _aimed = FindAimedBattery();
            }

            if (_aimed != null) { label = "Recoger"; return true; }
            label = null;
            return false;
        }

        public void Execute()
        {
            if (_aimed == null) return;
            var net = NetworkManager.Instance;
            if (net == null || !net.GameStarted) return;

            if (net.IsServer)
                BatterySpawnManager.Instance?.ServerHandlePickup(0, _aimed.NetworkId);
            else
                net.ClientSendBatteryPickup(_aimed.NetworkId);

            // No forzamos _aimed = null: si el pickup tuvo éxito, la pila se destruye y el
            // chequeo Unity-null de abajo la descarta sola (sin parpadeo al botón de
            // linterna); si fue rechazada, sigue mostrando "Recoger" para reintentar.
        }

        // Pila más centrada respecto a la cámara dentro del cono y la distancia. Itera el
        // registro estático (sin GetComponent por frame).
        private BatteryEntity FindAimedBattery()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return null;

            var list = BatteryEntity.Active;
            if (list.Count == 0) return null;

            if (_occludeMask < 0)
            {
                int placed = LayerMask.NameToLayer("Placed");
                _occludeMask = placed >= 0 ? (1 << placed) : 0;
            }
            // autoSyncTransforms viene OFF en el proyecto: sincronizar los colliders una
            // vez por frame antes de los linecasts de línea de visión.
            if (blockThroughWalls && _occludeMask != 0 && _lastSyncFrame != Time.frameCount)
            {
                Physics.SyncTransforms();
                _lastSyncFrame = Time.frameCount;
            }

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
                if (ang >= bestAngle) continue;
                if (!HasLineOfSight(camPos, be.transform.position)) continue; // tapada por pared

                bestAngle = ang;
                best = be;
            }
            return best;
        }

        // ¿Línea de visión libre a la pila? Linecast contra la layer Placed (paredes/muebles),
        // terminando 10 cm antes para no chocar con la propia pila/superficie.
        private bool HasLineOfSight(Vector3 camPos, Vector3 target)
        {
            if (!blockThroughWalls || _occludeMask == 0) return true;
            Vector3 to = target - camPos;
            float   d  = to.magnitude;
            if (d <= 0.2f) return true;
            return !Physics.Linecast(camPos, camPos + to * ((d - 0.1f) / d),
                                     _occludeMask, QueryTriggerInteraction.Ignore);
        }
    }
}
