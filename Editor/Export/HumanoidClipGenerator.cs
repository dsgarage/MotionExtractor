using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DSGarage.MotionExtractor.Editor.Pipeline;

namespace DSGarage.MotionExtractor.Editor.Export
{
    /// <summary>
    /// Generates Humanoid AnimationClips from time-series 3D skeleton data.
    /// Converts BlazePose keypoints to Unity HumanBodyBones muscle values
    /// and builds AnimationCurves for each muscle channel.
    /// </summary>
    public static class HumanoidClipGenerator
    {
        /// <summary>Configuration for clip generation.</summary>
        public struct Settings
        {
            /// <summary>Target frames per second.</summary>
            public float Fps;

            /// <summary>Whether to include root motion (hip position/rotation).</summary>
            public bool IncludeRootMotion;

            /// <summary>World scale factor (pixels to meters).</summary>
            public float WorldScale;

            /// <summary>Whether to center the skeleton on hips each frame.</summary>
            public bool CenterOnHips;

            /// <summary>Apply smoothing filter before generating curves.</summary>
            public bool ApplySmoothing;

            /// <summary>One Euro Filter: minimum cutoff Hz.</summary>
            public float SmoothMinCutoff;

            /// <summary>One Euro Filter: speed coefficient.</summary>
            public float SmoothBeta;

            public static Settings Default => new Settings
            {
                Fps = 30f,
                IncludeRootMotion = true,
                WorldScale = 0f, // 0 = auto-estimate
                CenterOnHips = true,
                ApplySmoothing = true,
                SmoothMinCutoff = 1.0f,
                SmoothBeta = 0.5f
            };
        }

        /// <summary>
        /// Generate a Humanoid AnimationClip from extracted pose frames.
        /// </summary>
        /// <param name="frames">Time-series pose data (must have at least 2 frames).</param>
        /// <param name="clipName">Name for the generated clip.</param>
        /// <param name="settings">Generation settings.</param>
        /// <returns>The generated AnimationClip, or null on failure.</returns>
        public static AnimationClip Generate(
            List<PoseFrame> frames, string clipName, Settings settings = default)
        {
            if (settings.Fps <= 0) settings = Settings.Default;
            if (frames == null || frames.Count < 2)
            {
                Debug.LogWarning("[HumanoidClipGenerator] Need at least 2 valid frames.");
                return null;
            }

            // Filter to only valid frames
            var validFrames = frames.FindAll(f =>
                f.Poses != null && f.Poses.Length > 0 && f.Poses[0].IsValid);

            if (validFrames.Count < 2)
            {
                Debug.LogWarning("[HumanoidClipGenerator] Not enough valid pose frames.");
                return null;
            }

            // Determine world scale
            float worldScale = settings.WorldScale;
            if (worldScale <= 0)
            {
                worldScale = CoordinateConverter.EstimateWorldScale(
                    validFrames[0].Poses[0].GetPositions());
            }

            // Convert all frames to Unity world coordinates
            var worldFrames = new List<(float time, Vector3[] positions)>();
            foreach (var frame in validFrames)
            {
                var positions = CoordinateConverter.ConvertSkeleton(
                    frame.Poses[0].GetPositions(),
                    frame.ImageSize.x, frame.ImageSize.y,
                    worldScale,
                    settings.CenterOnHips && !settings.IncludeRootMotion);
                worldFrames.Add((frame.Timestamp, positions));
            }

            // Apply smoothing
            if (settings.ApplySmoothing)
            {
                var smoother = new SkeletonSmoother(
                    settings.SmoothMinCutoff, settings.SmoothBeta);
                for (int i = 0; i < worldFrames.Count; i++)
                {
                    var (time, positions) = worldFrames[i];
                    var smoothed = smoother.Smooth(positions, time);
                    worldFrames[i] = (time, smoothed);
                }
            }

            // Create AnimationClip
            var clip = new AnimationClip
            {
                name = clipName,
                frameRate = settings.Fps
            };

            // Generate bone rotation curves
            GenerateBoneRotationCurves(clip, worldFrames);

            // Generate root motion curves if enabled
            if (settings.IncludeRootMotion)
            {
                GenerateRootMotionCurves(clip, worldFrames);
            }

            // Mark as humanoid
            clip.EnsureQuaternionContinuity();

            return clip;
        }

        /// <summary>
        /// Save the generated clip as an asset file.
        /// </summary>
        public static string SaveClip(AnimationClip clip, string outputPath)
        {
            AssetDatabase.CreateAsset(clip, outputPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HumanoidClipGenerator] Saved: {outputPath}");
            return outputPath;
        }

        static void GenerateBoneRotationCurves(
            AnimationClip clip, List<(float time, Vector3[] positions)> frames)
        {
            // For each bone pair (parent → child), compute rotation from reference pose
            // and write as quaternion AnimationCurves

            var bonePairs = new (int parent, int child, string propertyPath)[]
            {
                // Spine chain
                (SkeletonDefinitions.LeftHip, SkeletonDefinitions.LeftShoulder, "Hips/Spine"),
                (SkeletonDefinitions.LeftShoulder, SkeletonDefinitions.Nose, "Hips/Spine/Chest/Neck"),

                // Left arm
                (SkeletonDefinitions.LeftShoulder, SkeletonDefinitions.LeftElbow, "Hips/Spine/Chest/LeftShoulder/LeftUpperArm"),
                (SkeletonDefinitions.LeftElbow, SkeletonDefinitions.LeftWrist, "Hips/Spine/Chest/LeftShoulder/LeftUpperArm/LeftLowerArm"),

                // Right arm
                (SkeletonDefinitions.RightShoulder, SkeletonDefinitions.RightElbow, "Hips/Spine/Chest/RightShoulder/RightUpperArm"),
                (SkeletonDefinitions.RightElbow, SkeletonDefinitions.RightWrist, "Hips/Spine/Chest/RightShoulder/RightUpperArm/RightLowerArm"),

                // Left leg
                (SkeletonDefinitions.LeftHip, SkeletonDefinitions.LeftKnee, "Hips/LeftUpperLeg"),
                (SkeletonDefinitions.LeftKnee, SkeletonDefinitions.LeftAnkle, "Hips/LeftUpperLeg/LeftLowerLeg"),

                // Right leg
                (SkeletonDefinitions.RightHip, SkeletonDefinitions.RightKnee, "Hips/RightUpperLeg"),
                (SkeletonDefinitions.RightKnee, SkeletonDefinitions.RightAnkle, "Hips/RightUpperLeg/RightLowerLeg"),
            };

            foreach (var (parent, child, path) in bonePairs)
            {
                var curveX = new AnimationCurve();
                var curveY = new AnimationCurve();
                var curveZ = new AnimationCurve();
                var curveW = new AnimationCurve();

                for (int i = 0; i < frames.Count; i++)
                {
                    var (time, positions) = frames[i];
                    var direction = (positions[child] - positions[parent]).normalized;

                    Quaternion rotation;
                    if (direction.sqrMagnitude < 0.001f)
                    {
                        rotation = Quaternion.identity;
                    }
                    else
                    {
                        // Compute rotation that aligns the bone with the detected direction
                        rotation = Quaternion.LookRotation(
                            direction,
                            Vector3.up);
                    }

                    curveX.AddKey(time, rotation.x);
                    curveY.AddKey(time, rotation.y);
                    curveZ.AddKey(time, rotation.z);
                    curveW.AddKey(time, rotation.w);
                }

                clip.SetCurve(path, typeof(Transform), "localRotation.x", curveX);
                clip.SetCurve(path, typeof(Transform), "localRotation.y", curveY);
                clip.SetCurve(path, typeof(Transform), "localRotation.z", curveZ);
                clip.SetCurve(path, typeof(Transform), "localRotation.w", curveW);
            }
        }

        static void GenerateRootMotionCurves(
            AnimationClip clip, List<(float time, Vector3[] positions)> frames)
        {
            var curveX = new AnimationCurve();
            var curveY = new AnimationCurve();
            var curveZ = new AnimationCurve();

            var rotX = new AnimationCurve();
            var rotY = new AnimationCurve();
            var rotZ = new AnimationCurve();
            var rotW = new AnimationCurve();

            for (int i = 0; i < frames.Count; i++)
            {
                var (time, positions) = frames[i];

                // Root position from hips center
                var hipsCenter = SkeletonDefinitions.EstimateHips(positions);
                curveX.AddKey(time, hipsCenter.x);
                curveY.AddKey(time, hipsCenter.y);
                curveZ.AddKey(time, hipsCenter.z);

                // Root rotation from hip orientation
                var leftHip = positions[SkeletonDefinitions.LeftHip];
                var rightHip = positions[SkeletonDefinitions.RightHip];
                var hipForward = Vector3.Cross(rightHip - leftHip, Vector3.up).normalized;

                Quaternion hipRotation;
                if (hipForward.sqrMagnitude < 0.001f)
                    hipRotation = Quaternion.identity;
                else
                    hipRotation = Quaternion.LookRotation(hipForward, Vector3.up);

                rotX.AddKey(time, hipRotation.x);
                rotY.AddKey(time, hipRotation.y);
                rotZ.AddKey(time, hipRotation.z);
                rotW.AddKey(time, hipRotation.w);
            }

            // Root position
            clip.SetCurve("Hips", typeof(Transform), "localPosition.x", curveX);
            clip.SetCurve("Hips", typeof(Transform), "localPosition.y", curveY);
            clip.SetCurve("Hips", typeof(Transform), "localPosition.z", curveZ);

            // Root rotation
            clip.SetCurve("Hips", typeof(Transform), "localRotation.x", rotX);
            clip.SetCurve("Hips", typeof(Transform), "localRotation.y", rotY);
            clip.SetCurve("Hips", typeof(Transform), "localRotation.z", rotZ);
            clip.SetCurve("Hips", typeof(Transform), "localRotation.w", rotW);
        }
    }
}
