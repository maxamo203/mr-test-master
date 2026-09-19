using System.Collections.Generic;
using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Re-aligns a saved <see cref="RoomModel"/> to the room as scanned in the current
    /// session, entirely offline.
    ///
    /// ARCore has no local persistent anchor — ARFoundation's anchor save/load is backed
    /// by Cloud Anchors, which needs a Google Cloud key, network at both ends and carries
    /// a TTL. So instead of persisting a spatial anchor, the room's own geometry is the
    /// signature: a wall reduces to a line in normal form (direction angle + perpendicular
    /// offset from the origin), and the multiset of those lines identifies the room.
    ///
    /// The search is small because both scans use a wall-aligned origin
    /// (<see cref="RoomScanningManager"/>) and right-angle snapping
    /// (<see cref="DepthOccupancyMapper"/>): the two frames can only differ by a multiple
    /// of 90°, so rotation is four candidates rather than a continuous search, and within
    /// a candidate the translation separates into an independent 1D offset match per axis.
    ///
    /// Known limit: a square or symmetric room is genuinely ambiguous — several alignments
    /// score equally. <see cref="TryAlign"/> reports the margin between the best and
    /// runner-up so the caller can refuse a coin-flip rather than silently place the room
    /// wrong. Furniture positions are used as a tie-breaker, since they are rarely
    /// symmetric even when walls are.
    /// </summary>
    public class RoomRelocalizer : MonoBehaviour
    {
        [Tooltip("Two wall lines match when their perpendicular offsets agree within this (m).")]
        [SerializeField] float offsetTolerance = 0.30f;
        [Tooltip("Candidate translations are searched on this step (m).")]
        [SerializeField] float searchStep = 0.05f;
        [Tooltip("Maximum translation considered along each axis (m).")]
        [SerializeField] float searchRange = 6f;
        [Tooltip("Fraction of the saved wall length that must be matched to accept an alignment.")]
        [SerializeField] float minMatchFraction = 0.45f;
        [Tooltip("The best alignment must beat the runner-up by this fraction, or the room " +
                 "is treated as too symmetric to place confidently.")]
        [SerializeField] float minConfidenceMargin = 0.12f;
        [Tooltip("Weight given to furniture agreement, which breaks symmetric-room ties.")]
        [SerializeField] float furnitureWeight = 1.5f;

        /// <summary>A wall reduced to its infinite line, plus how much wall sits on it.</summary>
        struct Line
        {
            public bool alongX;   // true ⇒ runs along local X, so its offset is a Z coordinate
            public float offset;  // perpendicular position of the line
            public float length;  // total wall length on this line, used as match weight
        }

        public float LastScore { get; private set; }
        public float LastMargin { get; private set; }

        /// <summary>
        /// Finds the rigid correction that carries <paramref name="saved"/> onto the room
        /// currently reconstructed as <paramref name="fresh"/>. Both are origin-local.
        ///
        /// Apply the result to the map origin: composing the origin with this correction
        /// makes the saved model's coordinates directly valid in the live session — the
        /// same thing ADJUST ORIGIN does by hand.
        /// </summary>
        public bool TryAlign(RoomModel saved, RoomModel fresh, out Pose correction)
        {
            correction = new Pose(Vector3.zero, Quaternion.identity);
            LastScore = 0f;
            LastMargin = 0f;

            if (saved?.walls == null || fresh?.walls == null) return false;
            if (saved.walls.Count == 0 || fresh.walls.Count == 0) return false;

            var freshLines = ToLines(fresh.walls);
            if (freshLines.Count == 0) return false;

            float savedTotal = TotalLength(saved.walls);
            if (savedTotal < 1e-3f) return false;

            float bestScore = -1f, runnerUp = -1f;
            Pose bestPose = correction;

            // Four right-angle rotations: the only ones two wall-aligned frames can differ by.
            for (int q = 0; q < 4; q++)
            {
                float yaw = q * 90f;
                var rot = Quaternion.Euler(0f, yaw, 0f);

                var savedLines = ToLines(RotateWalls(saved.walls, rot));
                if (savedLines.Count == 0) continue;

                // Offsets of X-running lines are Z coordinates and vice versa, so the two
                // axes are independent: solve each as its own 1D correlation.
                float dz = BestShift(savedLines, freshLines, alongX: true,  out float scoreZ);
                float dx = BestShift(savedLines, freshLines, alongX: false, out float scoreX);

                var translation = new Vector3(dx, 0f, dz);
                float score = (scoreX + scoreZ) / savedTotal;

                score += furnitureWeight * FurnitureAgreement(saved, fresh, rot, translation);

                if (score > bestScore)
                {
                    runnerUp = bestScore;
                    bestScore = score;
                    bestPose = new Pose(translation, rot);
                }
                else if (score > runnerUp) runnerUp = score;
            }

            if (bestScore < minMatchFraction)
            {
                Debug.LogWarning($"[Relocalizer] No alignment found (best score {bestScore:F2} < {minMatchFraction:F2}). " +
                                 "Scan more of the room and retry.");
                return false;
            }

            // Reject a coin-flip: in a symmetric room several placements tie, and guessing
            // puts the whole map 90° or half a room out.
            float margin = runnerUp <= 0f ? 1f : (bestScore - runnerUp) / bestScore;
            if (margin < minConfidenceMargin)
            {
                Debug.LogWarning($"[Relocalizer] Ambiguous: best {bestScore:F2} vs runner-up {runnerUp:F2} " +
                                 $"(margin {margin:P0}). Room is too symmetric to place confidently.");
                LastScore = bestScore;
                LastMargin = margin;
                return false;
            }

            LastScore = bestScore;
            LastMargin = margin;
            correction = bestPose;
            Debug.Log($"[Relocalizer] ✔ Aligned: yaw {bestPose.rotation.eulerAngles.y:F0}°, " +
                      $"offset {bestPose.position:F2}, score {bestScore:F2}, margin {margin:P0}.");
            return true;
        }

        // ── Signature ─────────────────────────────────────────────────────────

        /// <summary>Reduces walls to axis-classified lines, fusing those sharing a line.</summary>
        static List<Line> ToLines(List<WallSegment> walls)
        {
            var lines = new List<Line>();

            foreach (var w in walls)
            {
                var d = w.end - w.start;
                float len = new Vector2(d.x, d.z).magnitude;
                if (len < 1e-3f) continue;

                // After right-angle snapping a wall runs along X or along Z; classify by
                // whichever component dominates.
                bool alongX = Mathf.Abs(d.x) >= Mathf.Abs(d.z);
                float offset = alongX ? (w.start.z + w.end.z) * 0.5f
                                      : (w.start.x + w.end.x) * 0.5f;

                int hit = -1;
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].alongX == alongX && Mathf.Abs(lines[i].offset - offset) < 0.15f)
                    {
                        hit = i;
                        break;
                    }

                if (hit < 0)
                {
                    lines.Add(new Line { alongX = alongX, offset = offset, length = len });
                    continue;
                }

                var l = lines[hit];
                l.offset = (l.offset * l.length + offset * len) / (l.length + len);
                l.length += len;
                lines[hit] = l;
            }

            return lines;
        }

        static List<WallSegment> RotateWalls(List<WallSegment> walls, Quaternion rot)
        {
            var result = new List<WallSegment>(walls.Count);
            foreach (var w in walls)
                result.Add(new WallSegment { start = rot * w.start, end = rot * w.end, height = w.height });
            return result;
        }

        static float TotalLength(List<WallSegment> walls)
        {
            float total = 0f;
            foreach (var w in walls)
            {
                var d = w.end - w.start;
                total += new Vector2(d.x, d.z).magnitude;
            }
            return total;
        }

        /// <summary>
        /// Best 1D shift for one axis family: slides the saved lines against the fresh ones
        /// and returns the shift maximising matched wall length. Candidate shifts come from
        /// the actual offset differences rather than a blind sweep, so the search is over
        /// alignments that line up at least one wall.
        /// </summary>
        float BestShift(List<Line> saved, List<Line> fresh, bool alongX, out float score)
        {
            score = 0f;
            float bestShift = 0f;

            var candidates = new List<float>();
            foreach (var s in saved)
            {
                if (s.alongX != alongX) continue;
                foreach (var f in fresh)
                {
                    if (f.alongX != alongX) continue;
                    float shift = f.offset - s.offset;
                    if (Mathf.Abs(shift) <= searchRange)
                        candidates.Add(Mathf.Round(shift / searchStep) * searchStep);
                }
            }
            if (candidates.Count == 0) return 0f;

            foreach (var shift in candidates)
            {
                float matched = 0f;
                foreach (var s in saved)
                {
                    if (s.alongX != alongX) continue;
                    foreach (var f in fresh)
                    {
                        if (f.alongX != alongX) continue;
                        if (Mathf.Abs(s.offset + shift - f.offset) > offsetTolerance) continue;
                        matched += Mathf.Min(s.length, f.length);
                        break; // each saved line matches at most one fresh line
                    }
                }

                if (matched > score) { score = matched; bestShift = shift; }
            }

            return bestShift;
        }

        /// <summary>
        /// Fraction of saved furniture that lands near fresh furniture under a candidate
        /// transform. Walls in a rectangular room are symmetric; the sofa is not, so this
        /// is what separates otherwise tied alignments.
        /// </summary>
        static float FurnitureAgreement(RoomModel saved, RoomModel fresh, Quaternion rot, Vector3 translation)
        {
            if (saved.furniture == null || fresh.furniture == null) return 0f;
            if (saved.furniture.Count == 0 || fresh.furniture.Count == 0) return 0f;

            int hits = 0;
            foreach (var s in saved.furniture)
            {
                var p = rot * s.center + translation;
                foreach (var f in fresh.furniture)
                {
                    var d = f.center - p;
                    if (new Vector2(d.x, d.z).magnitude < 0.5f) { hits++; break; }
                }
            }

            return (float)hits / saved.furniture.Count;
        }
    }
}
