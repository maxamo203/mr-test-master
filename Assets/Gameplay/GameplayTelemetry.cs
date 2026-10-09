using System;
using UnityEngine;

namespace Gameplay
{
    // Eventos estructurados para medir gameplay sin capturar imagen, audio ni identidad.
    public static class GameplayTelemetry
    {
        [Serializable]
        private sealed class Metric
        {
            public string eventName;
            public int session;
            public int threat;
            public string kind;
            public string phase;
            public string result;
            public string profile;
            public string night;
            public uint owner;
            public int players;
            public float time;
            public float battery01;
            public Vector3 position;
            public string detail;
        }

        private static int _session;
        private static int _nextThreat;
        private static string _night;
        private static int _players;

        public static void BeginSession(NightConfig night, int players)
        {
            _session++;
            _nextThreat = 0;
            _night = night != null ? night.displayName : "unknown";
            _players = Mathf.Max(0, players);
            Emit(new Metric
            {
                eventName = "session_start",
                session = _session,
                profile = PlayDataProfile.DisplayName,
                night = _night,
                players = _players,
                detail = $"randomState={UnityEngine.Random.state.GetHashCode()}",
            });
        }

        public static int BeginThreat(string kind, uint owner, Vector3 position,
                                      string detail = null)
        {
            int id = ++_nextThreat;
            Emit(Base("threat_start", id, kind, owner, position, detail));
            return id;
        }

        public static void Phase(int id, string kind, string phase, uint owner,
                                 Vector3 position, string detail = null)
        {
            var metric = Base("threat_phase", id, kind, owner, position, detail);
            metric.phase = phase;
            Emit(metric);
        }

        public static void End(int id, string kind, string result, uint owner,
                               Vector3 position, string detail = null)
        {
            if (id == 0) return;
            var metric = Base("threat_end", id, kind, owner, position, detail);
            metric.result = result;
            Emit(metric);
        }

        public static void Postponed(string kind, string reason, uint owner = 0)
        {
            var metric = Base("threat_postponed", 0, kind, owner, Vector3.zero, reason);
            Emit(metric);
        }

        private static Metric Base(string eventName, int threat, string kind,
                                   uint owner, Vector3 position, string detail)
        {
            return new Metric
            {
                eventName = eventName,
                session = _session,
                threat = threat,
                kind = kind,
                profile = PlayDataProfile.DisplayName,
                night = _night,
                owner = owner,
                players = _players,
                time = Time.time,
                battery01 = Battery01(owner),
                position = position,
                detail = detail,
            };
        }

        private static float Battery01(uint owner)
        {
            var net = NetworkManager.Instance;
            if (net == null) return -1f;
            if (owner == 0) return net.LocalFlashlightCharge01();
            return net.TryGetClientFlashlightCharge01(owner, out float charge)
                ? charge : -1f;
        }

        private static void Emit(Metric metric) =>
            Debug.Log($"[GameplayMetric] {JsonUtility.ToJson(metric)}");
    }
}
