using UnityEngine;

namespace Gameplay
{
    // Test SERVER-AUTHORITATIVE de "¿quién está alumbrando esto?". Lo comparten el
    // GameDirector (repeler al Sorken) y el RitualBookDirector (mantener el libro abierto),
    // así las dos mecánicas usan exactamente las mismas reglas: sólo cuentan los jugadores
    // VIVOS, con la linterna encendida y con el objetivo dentro del haz.
    //
    // El jugador es su cámara AR, no una entidad: el host se lee de Camera.main y cada
    // cliente de la última pose/linterna que reportó (PlayerPose).
    public static class PlayerLights
    {
        // Linterna local, cacheada (nunca un FindFirstObjectByType por frame en release).
        private static Flashlight _linterna;

        // Cono REAL de la linterna: semi-ángulo y alcance del haz que el jugador VE.
        //
        // Es la única referencia sana para "estoy alumbrando algo". Cuando el cono de
        // gameplay se lleva como un número aparte, los dos se separan sin que nadie se dé
        // cuenta: el prefab quedó en 7.2° / 4.2 m y la NightConfig en 30° / 8 m, o sea que
        // contaba como iluminado algo que estaba MUY afuera del haz visible.
        //
        // Todos los dispositivos instancian el mismo prefab de linterna, así que la local
        // sirve de referencia también para los clientes remotos — que sólo reportan
        // posición y forward, no sus parámetros de cono. (En development build los sliders
        // del menú de pausa pueden mover los del host; en release no existen.)
        public static bool TryConoReal(out float angleDeg, out float range)
        {
            if (_linterna == null) _linterna = Object.FindFirstObjectByType<Flashlight>();
            if (_linterna == null) { angleDeg = 0f; range = 0f; return false; }

            angleDeg = _linterna.outerAngleDeg;
            range    = _linterna.range;
            return true;
        }

        // Cuántos jugadores están alumbrando el objetivo AHORA. El libro ritual lo usa para
        // acumular: varias linternas encima lo abren más rápido.
        //
        // radioObjetivo = radio aproximado de lo que se alumbra. Con 0 el test es sobre un
        // punto (así se comporta el repel del Sorken de siempre); con el radio real de un
        // objeto, alumbrarle el BORDE también cuenta, que es lo que espera el jugador.
        public static int CountIlluminating(Vector3 target, float angleDeg, float range,
                                            float radioObjetivo = 0f)
        {
            var net = NetworkManager.Instance;
            if (net == null) return 0;

            int n = 0;

            if (Camera.main != null && ServerDeaths.IsAlive(0) && net.LocalFlashlightOn() &&
                Alcanza(Camera.main.transform.position, Camera.main.transform.forward,
                        target, angleDeg, range, radioObjetivo))
                n++;

            foreach (var cid in net.ConnectedClients)
            {
                if (ServerDeaths.IsDead(cid)) continue;
                if (!net.TryGetClientFlashlightOn(cid, out var on) || !on) continue;
                if (!net.TryGetClientWorldPosition(cid, out var pos)) continue;
                if (!net.TryGetClientForward(cid, out var fwd)) continue;
                if (Alcanza(pos, fwd, target, angleDeg, range, radioObjetivo)) n++;
            }
            return n;
        }

        // ¿Hay al menos uno? (el repel del Sorken no necesita contarlos).
        public static bool AnyIlluminating(Vector3 target, float angleDeg, float range,
                                           float radioObjetivo = 0f) =>
            CountIlluminating(target, angleDeg, range, radioObjetivo) > 0;

        // ¿El haz que sale de (pos, forward) toca el objetivo?
        public static bool Alcanza(Vector3 pos, Vector3 forward, Vector3 target,
                                   float angleDeg, float range, float radioObjetivo)
        {
            var   to   = target - pos;
            float dist = to.magnitude;
            if (dist > range + radioObjetivo) return false;
            if (dist < 1e-3f) return true;

            float along = Vector3.Dot(to, forward.normalized);
            if (along <= 0f) return false;                  // está detrás del jugador

            // Distancia del objetivo al EJE del haz, contra el radio del cono a esa
            // distancia más el radio del objeto. Con radioObjetivo = 0 es exactamente el
            // test angular de siempre (perp/along <= tan(ángulo)).
            float perp = Mathf.Sqrt(Mathf.Max(0f, dist * dist - along * along));
            return perp <= along * Mathf.Tan(angleDeg * Mathf.Deg2Rad) + radioObjetivo;
        }
    }
}
