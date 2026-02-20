using System.Collections.Generic;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Pipeline
{
    /// <summary>
    /// Reduces keyframe count in AnimationCurves by removing redundant keyframes
    /// that fall within a specified error tolerance.
    /// Uses the Ramer-Douglas-Peucker algorithm for curve simplification.
    /// </summary>
    public static class KeyframeOptimizer
    {
        /// <summary>
        /// Optimize an AnimationCurve by removing redundant keyframes.
        /// </summary>
        /// <param name="curve">The curve to optimize.</param>
        /// <param name="tolerance">Maximum allowed error. Higher = more aggressive reduction.</param>
        /// <returns>A new optimized AnimationCurve.</returns>
        public static AnimationCurve Optimize(AnimationCurve curve, float tolerance = 0.001f)
        {
            if (curve.length <= 2)
                return new AnimationCurve(curve.keys);

            var keys = curve.keys;
            var keepIndices = new List<int>();

            // Ramer-Douglas-Peucker simplification
            RDP(keys, 0, keys.Length - 1, tolerance, keepIndices);

            // Always keep first and last
            keepIndices.Add(0);
            keepIndices.Add(keys.Length - 1);
            keepIndices.Sort();

            // Remove duplicates
            var unique = new List<int>();
            int prev = -1;
            foreach (int idx in keepIndices)
            {
                if (idx != prev)
                {
                    unique.Add(idx);
                    prev = idx;
                }
            }

            // Build new curve with kept keyframes
            var newKeys = new Keyframe[unique.Count];
            for (int i = 0; i < unique.Count; i++)
                newKeys[i] = keys[unique[i]];

            var optimized = new AnimationCurve(newKeys);

            // Set tangent mode to auto for smooth interpolation
            for (int i = 0; i < optimized.length; i++)
                AnimationUtility.SetKeyLeftTangentMode(optimized, i, AnimationUtility.TangentMode.Auto);

            return optimized;
        }

        /// <summary>
        /// Optimize all curves in an AnimationClip.
        /// </summary>
        /// <param name="clip">The clip to optimize.</param>
        /// <param name="tolerance">Maximum allowed error.</param>
        /// <returns>Statistics: (originalKeyCount, optimizedKeyCount).</returns>
        public static (int original, int optimized) OptimizeClip(AnimationClip clip, float tolerance = 0.001f)
        {
            int originalCount = 0;
            int optimizedCount = 0;

            var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;

                originalCount += curve.length;

                var optimizedCurve = Optimize(curve, tolerance);
                optimizedCount += optimizedCurve.length;

                UnityEditor.AnimationUtility.SetEditorCurve(clip, binding, optimizedCurve);
            }

            return (originalCount, optimizedCount);
        }

        static void RDP(Keyframe[] keys, int start, int end, float tolerance, List<int> keepIndices)
        {
            if (end - start < 2) return;

            // Find the point with maximum distance from the line segment
            float maxDist = 0;
            int maxIdx = start;

            float t0 = keys[start].time;
            float v0 = keys[start].value;
            float t1 = keys[end].time;
            float v1 = keys[end].value;
            float dt = t1 - t0;

            for (int i = start + 1; i < end; i++)
            {
                // Perpendicular distance from point to line segment
                float t = (keys[i].time - t0) / dt;
                float expectedValue = v0 + t * (v1 - v0);
                float dist = Mathf.Abs(keys[i].value - expectedValue);

                if (dist > maxDist)
                {
                    maxDist = dist;
                    maxIdx = i;
                }
            }

            if (maxDist > tolerance)
            {
                keepIndices.Add(maxIdx);
                RDP(keys, start, maxIdx, tolerance, keepIndices);
                RDP(keys, maxIdx, end, tolerance, keepIndices);
            }
        }
    }
}
