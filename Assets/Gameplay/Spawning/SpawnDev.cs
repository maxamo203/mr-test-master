using System.Collections.Generic;
using Scanner;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gameplay.Spawning
{
    // Interruptores de desarrollo para la aparición de pilas y reliquias (menú principal →
    // OPCIONES → DESARROLLO). En release valen siempre los de fábrica: sin piso automático.
    // (El viejo "exigir cuarto cerrado" ya no existe: sin recintos se usa un radio de
    // respaldo, ver SpawnSampler.)
    public static class SpawnDev
    {
        // ¿Sortear también alrededor del jugador al empezar la noche? Se suma al área de los
        // recintos (o al radio de respaldo). También estima la altura del piso (ver abajo).
        public static bool PisoAutomatico
        {
            get => Debug.isDebugBuild && PlayerPrefs.GetInt("dev_spawn_piso_auto", 0) == 1;
            set { if (Debug.isDebugBuild) PlayerPrefs.SetInt("dev_spawn_piso_auto", value ? 1 : 0); }
        }

        // Altura de ojos típica de pie: respaldo si no hay PISO ni el AR reconoció el piso.
        private const float AlturaOjosTipica = 1.5f;
        // Lo que el rayo hacia abajo puede tomar como piso, medido desde la cámara.
        private const float PisoMin = 0.5f, PisoMax = 2.5f;

        private static readonly List<ARRaycastHit> _hits = new();

        // Centro (anchor-relativo) de la grilla del piso automático: debajo del jugador, a la
        // altura del PISO del escáner si hay; si no, la del piso que reconoce el AR (rayo hacia
        // abajo); si tampoco, la de la cámara menos una altura de ojos típica.
        public static bool TryCentroPisoAutomatico(out Vector3 centroRel, out string fuente)
        {
            centroRel = default;
            fuente = null;
            var wo = WorldOrigin.Instance;
            var cam = Camera.main;
            if (wo == null || !wo.IsReady || cam == null) return false;

            Vector3 p = cam.transform.position;
            float y;
            if (FloorPoint.Instance != null)
            {
                y = wo.ToWorld(FloorPoint.Instance.LocalPosition).y;
                fuente = "PISO del escáner";
            }
            else if (TryPisoPorRayo(p, out y))
                fuente = "piso detectado por AR";
            else
            {
                y = p.y - AlturaOjosTipica;
                fuente = "altura de la cámara";
            }
            centroRel = wo.ToRelative(new Vector3(p.x, y, p.z));
            return true;
        }

        // Rayo hacia abajo contra planos y puntos que reconoce el AR. Se toma el impacto MÁS
        // BAJO dentro del rango, para preferir el piso a una mesa. Sólo al armar los puntos
        // (una vez por noche), no por frame.
        private static bool TryPisoPorRayo(Vector3 desde, out float y)
        {
            y = 0f;
            var rm = Object.FindAnyObjectByType<ARRaycastManager>();
            if (rm == null) return false;
            _hits.Clear();
            if (!rm.Raycast(new Ray(desde, Vector3.down), _hits,
                    TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated | TrackableType.FeaturePoint))
                return false;
            bool hay = false;
            foreach (var h in _hits)
            {
                float abajo = desde.y - h.pose.position.y;
                if (abajo < PisoMin || abajo > PisoMax) continue;
                if (!hay || h.pose.position.y < y) { y = h.pose.position.y; hay = true; }
            }
            return hay;
        }
    }
}
