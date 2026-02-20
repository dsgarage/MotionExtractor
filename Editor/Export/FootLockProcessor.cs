using System.Collections.Generic;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Export
{
    /// <summary>
    /// Post-processor that detects foot ground contact and locks foot position
    /// to prevent sliding artifacts in generated animations.
    /// </summary>
    public static class FootLockProcessor
    {
        /// <summary>Configuration for foot lock processing.</summary>
        public struct Settings
        {
            /// <summary>Y-velocity threshold below which the foot is considered grounded (m/s).</summary>
            public float GroundVelocityThreshold;

            /// <summary>Y-position threshold below which the foot is considered near ground.</summary>
            public float GroundHeightThreshold;

            /// <summary>Blending frames when transitioning between locked and unlocked states.</summary>
            public int BlendFrames;

            public static Settings Default => new Settings
            {
                GroundVelocityThreshold = 0.05f,
                GroundHeightThreshold = 0.1f,
                BlendFrames = 3
            };
        }

        /// <summary>
        /// Process a sequence of skeleton frames to apply foot locking.
        /// Modifies the ankle positions in-place.
        /// </summary>
        /// <param name="frames">Sequence of (timestamp, positions[33]) tuples.</param>
        /// <param name="settings">Foot lock configuration.</param>
        public static void Process(
            List<(float time, Vector3[] positions)> frames,
            Settings settings = default)
        {
            if (settings.GroundVelocityThreshold <= 0) settings = Settings.Default;
            if (frames.Count < 3) return;

            ProcessFoot(frames, SkeletonDefinitions.LeftAnkle, settings);
            ProcessFoot(frames, SkeletonDefinitions.RightAnkle, settings);
            ProcessFoot(frames, SkeletonDefinitions.LeftFootIndex, settings);
            ProcessFoot(frames, SkeletonDefinitions.RightFootIndex, settings);
        }

        static void ProcessFoot(
            List<(float time, Vector3[] positions)> frames,
            int jointIndex,
            Settings settings)
        {
            // Detect grounded frames
            var grounded = new bool[frames.Count];

            // Find the minimum Y (ground level)
            float minY = float.MaxValue;
            for (int i = 0; i < frames.Count; i++)
            {
                float y = frames[i].positions[jointIndex].y;
                if (y < minY) minY = y;
            }

            // Mark grounded frames based on height and velocity
            for (int i = 0; i < frames.Count; i++)
            {
                float y = frames[i].positions[jointIndex].y;
                bool nearGround = (y - minY) < settings.GroundHeightThreshold;

                if (nearGround && i > 0 && i < frames.Count - 1)
                {
                    float dt = frames[i].time - frames[i - 1].time;
                    if (dt > 0)
                    {
                        float vy = Mathf.Abs(
                            (frames[i].positions[jointIndex].y - frames[i - 1].positions[jointIndex].y) / dt);
                        float vx = Mathf.Abs(
                            (frames[i].positions[jointIndex].x - frames[i - 1].positions[jointIndex].x) / dt);
                        float vz = Mathf.Abs(
                            (frames[i].positions[jointIndex].z - frames[i - 1].positions[jointIndex].z) / dt);

                        grounded[i] = vy < settings.GroundVelocityThreshold
                            && vx < settings.GroundVelocityThreshold * 3f
                            && vz < settings.GroundVelocityThreshold * 3f;
                    }
                }
            }

            // Find grounded segments and lock foot position
            int segStart = -1;
            Vector3 lockPos = Vector3.zero;

            for (int i = 0; i <= frames.Count; i++)
            {
                bool isGrounded = (i < frames.Count) && grounded[i];

                if (isGrounded && segStart < 0)
                {
                    // Start of grounded segment
                    segStart = i;
                    lockPos = frames[i].positions[jointIndex];
                    lockPos.y = minY; // Snap to ground
                }
                else if (!isGrounded && segStart >= 0)
                {
                    // End of grounded segment — apply lock
                    for (int j = segStart; j < i; j++)
                    {
                        frames[j].positions[jointIndex] = lockPos;
                    }

                    // Blend transition at segment boundaries
                    BlendTransition(frames, jointIndex, segStart, settings.BlendFrames, lockPos, true);
                    BlendTransition(frames, jointIndex, i - 1, settings.BlendFrames, lockPos, false);

                    segStart = -1;
                }
            }
        }

        static void BlendTransition(
            List<(float time, Vector3[] positions)> frames,
            int jointIndex, int boundaryFrame, int blendFrames,
            Vector3 lockPos, bool blendIn)
        {
            for (int k = 1; k <= blendFrames; k++)
            {
                int idx = blendIn ? boundaryFrame - k : boundaryFrame + k;
                if (idx < 0 || idx >= frames.Count) continue;

                float t = (float)k / (blendFrames + 1);
                var original = frames[idx].positions[jointIndex];
                frames[idx].positions[jointIndex] = Vector3.Lerp(lockPos, original, t);
            }
        }
    }
}
