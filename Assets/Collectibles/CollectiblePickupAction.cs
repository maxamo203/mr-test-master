using Bateries;
using UnityEngine;

namespace Collectibles
{
    // Acción contextual: recoger la reliquia que el jugador está apuntando con la
    // cámara. Igual mecanismo que BatteryPickupAction (apuntado + botón, autoritativo
    // del server), variante para el coleccionable de noche.
    public class CollectiblePickupAction : MonoBehaviour, IContextAction
    {
        [Header("Prioridad")]
        [SerializeField] private int priority = 100;
        [SerializeField] private bool showActionButton = true;

        [Header("Apuntado")]
        [Tooltip("Distancia máxima (m) a la que se puede apuntar/recoger una reliquia.")]
        [SerializeField] private float aimMaxDistance = 2.5f;
        [Tooltip("Semiángulo (grados) del cono de apuntado desde el centro de la cámara.")]
        [SerializeField] private float aimAngle = 12f;
        [Tooltip("Cada cuánto (s) recalcular la reliquia apuntada.")]
        [SerializeField] private float aimCheckInterval = 0.1f;
        [Tooltip("No permitir apuntar/recoger a través de paredes/muebles (layer Placed).")]
        [SerializeField] private bool blockThroughWalls = true;

        public int  Priority         => priority;
        public bool ShowActionButton => showActionButton;

        private Camera            _cam;
        private CollectibleEntity _aimed;
        private float             _timer;
        private int                _occludeMask = -1;
        private static int         _lastSyncFrame = -1;

        public bool TryResolve(out string label)
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = aimCheckInterval;
                _aimed = FindAimedCollectible();
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
                CollectibleSpawnManager.Instance?.ServerHandlePickup(0, _aimed.NetworkId);
            else
                net.ClientSendCollectiblePickup(_aimed.NetworkId);

            // No forzamos _aimed = null: si el pickup tuvo éxito, la reliquia se
            // destruye y el chequeo Unity-null de FindAimedCollectible la descarta sola.
        }

        private CollectibleEntity FindAimedCollectible()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return null;

            var list = CollectibleEntity.Active;
            if (list.Count == 0) return null;

            if (_occludeMask < 0)
            {
                int placed = LayerMask.NameToLayer("Placed");
                _occludeMask = placed >= 0 ? (1 << placed) : 0;
            }
            if (blockThroughWalls && _occludeMask != 0 && _lastSyncFrame != Time.frameCount)
            {
                Physics.SyncTransforms();
                _lastSyncFrame = Time.frameCount;
            }

            CollectibleEntity best = null;
            float bestAngle = aimAngle;
            float maxD2     = aimMaxDistance * aimMaxDistance;
            Vector3 camPos  = _cam.transform.position;
            Vector3 camFwd  = _cam.transform.forward;

            for (int i = 0; i < list.Count; i++)
            {
                var ce = list[i];
                if (ce == null) continue;

                Vector3 to = ce.transform.position - camPos;
                if (to.sqrMagnitude > maxD2) continue;

                float ang = Vector3.Angle(camFwd, to);
                if (ang >= bestAngle) continue;
                if (!HasLineOfSight(camPos, ce.transform.position)) continue;

                bestAngle = ang;
                best = ce;
            }
            return best;
        }

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
