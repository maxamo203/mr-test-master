using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gameplay
{
    public static class TrackingReliability
    {
        public const float RemotePoseMaxAgeSeconds = 0.75f;
        public const float RecoverySeconds = 2f;
        private static float _reliableAfter;

        public static bool LocalIsReliable()
        {
#if UNITY_EDITOR
            return true;
#elif UNITY_ANDROID || UNITY_IOS
            bool raw = ARSession.state == ARSessionState.SessionTracking &&
                       ARSession.notTrackingReason == NotTrackingReason.None;
            if (!raw)
            {
                _reliableAfter = Time.unscaledTime + RecoverySeconds;
                return false;
            }
            return Time.unscaledTime >= _reliableAfter;
#else
            return true;
#endif
        }

        public static bool PoseIsFresh(float now, float receivedAt, float maxAge) =>
            receivedAt > 0f && now - receivedAt <= Mathf.Max(0f, maxAge);

        public static bool PlayerIsReliable(uint clientId)
        {
            if (clientId == 0) return LocalIsReliable();
            return NetworkManager.Instance != null &&
                   NetworkManager.Instance.IsClientPoseReliable(
                       clientId, RemotePoseMaxAgeSeconds);
        }
    }
}
