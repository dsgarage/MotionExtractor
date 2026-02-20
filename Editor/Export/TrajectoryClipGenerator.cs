using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DSGarage.MotionExtractor.Editor.Tracking;

namespace DSGarage.MotionExtractor.Editor.Export
{
    /// <summary>
    /// Generates Transform-based AnimationClips from tracked object trajectories.
    /// Converts 2D bounding box center trajectories to 3D world-space position curves.
    /// </summary>
    public static class TrajectoryClipGenerator
    {
        /// <summary>Configuration for trajectory clip generation.</summary>
        public struct Settings
        {
            /// <summary>Target frames per second.</summary>
            public float Fps;

            /// <summary>Scale factor for converting image pixels to world units.</summary>
            public float WorldScale;

            /// <summary>Fixed depth plane Z position for 2D-to-3D projection.</summary>
            public float DepthPlaneZ;

            /// <summary>Image width in pixels (for coordinate centering).</summary>
            public float ImageWidth;

            /// <summary>Image height in pixels (for coordinate centering).</summary>
            public float ImageHeight;

            /// <summary>Whether to estimate rotation from bounding box changes.</summary>
            public bool EstimateRotation;

            public static Settings Default => new Settings
            {
                Fps = 30f,
                WorldScale = 0.01f,
                DepthPlaneZ = 0f,
                ImageWidth = 1920,
                ImageHeight = 1080,
                EstimateRotation = false
            };
        }

        /// <summary>
        /// A single tracked object's trajectory data for one frame.
        /// </summary>
        public struct TrajectoryPoint
        {
            public float Timestamp;
            public Rect BoundingBox;
            public float Score;
        }

        /// <summary>
        /// A complete trajectory for one tracked object across multiple frames.
        /// </summary>
        public class ObjectTrajectory
        {
            public int TrackId;
            public string ClassLabel;
            public int ClassId;
            public List<TrajectoryPoint> Points = new List<TrajectoryPoint>();
        }

        /// <summary>
        /// Generate an AnimationClip from a single object's trajectory.
        /// </summary>
        /// <param name="trajectory">The object's trajectory data.</param>
        /// <param name="settings">Generation settings.</param>
        /// <returns>The generated AnimationClip.</returns>
        public static AnimationClip Generate(ObjectTrajectory trajectory, Settings settings = default)
        {
            if (settings.Fps <= 0) settings = Settings.Default;
            if (trajectory.Points.Count < 2)
            {
                Debug.LogWarning("[TrajectoryClipGenerator] Need at least 2 trajectory points.");
                return null;
            }

            string clipName = $"Object_{trajectory.TrackId}_{trajectory.ClassLabel}";
            var clip = new AnimationClip
            {
                name = clipName,
                frameRate = settings.Fps
            };

            // Position curves
            var curveX = new AnimationCurve();
            var curveY = new AnimationCurve();
            var curveZ = new AnimationCurve();

            foreach (var point in trajectory.Points)
            {
                var worldPos = ImageToWorldPosition(
                    point.BoundingBox, settings);

                curveX.AddKey(point.Timestamp, worldPos.x);
                curveY.AddKey(point.Timestamp, worldPos.y);
                curveZ.AddKey(point.Timestamp, worldPos.z);
            }

            clip.SetCurve("", typeof(Transform), "localPosition.x", curveX);
            clip.SetCurve("", typeof(Transform), "localPosition.y", curveY);
            clip.SetCurve("", typeof(Transform), "localPosition.z", curveZ);

            // Optional rotation estimation from bounding box aspect ratio changes
            if (settings.EstimateRotation && trajectory.Points.Count >= 3)
            {
                GenerateRotationCurves(clip, trajectory.Points, settings);
            }

            // Add class label as AnimationEvent at the start
            var classEvent = new AnimationEvent
            {
                time = 0f,
                functionName = "OnObjectClass",
                stringParameter = trajectory.ClassLabel,
                intParameter = trajectory.ClassId
            };
            AnimationUtility.SetAnimationEvents(clip, new[] { classEvent });

            return clip;
        }

        /// <summary>
        /// Generate clips for all tracked objects and save them as assets.
        /// </summary>
        /// <param name="trajectories">All object trajectories.</param>
        /// <param name="outputFolder">Asset folder path (e.g., "Assets/Animations").</param>
        /// <param name="settings">Generation settings.</param>
        /// <returns>List of saved asset paths.</returns>
        public static List<string> GenerateAndSaveAll(
            List<ObjectTrajectory> trajectories, string outputFolder, Settings settings = default)
        {
            if (!AssetDatabase.IsValidFolder(outputFolder))
            {
                // Create folder hierarchy
                var parts = outputFolder.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }

            var savedPaths = new List<string>();
            foreach (var trajectory in trajectories)
            {
                var clip = Generate(trajectory, settings);
                if (clip == null) continue;

                string path = $"{outputFolder}/{clip.name}.anim";
                // Ensure unique filename
                path = AssetDatabase.GenerateUniqueAssetPath(path);

                AssetDatabase.CreateAsset(clip, path);
                savedPaths.Add(path);
                Debug.Log($"[TrajectoryClipGenerator] Saved: {path}");
            }

            AssetDatabase.SaveAssets();
            return savedPaths;
        }

        /// <summary>
        /// Convert a 2D bounding box to a 3D world position.
        /// Uses a fixed depth plane projection.
        /// </summary>
        static Vector3 ImageToWorldPosition(Rect bbox, Settings settings)
        {
            // BBox center in image coordinates
            float cx = bbox.x + bbox.width * 0.5f;
            float cy = bbox.y + bbox.height * 0.5f;

            // Center and scale to world coordinates
            float worldX = (cx - settings.ImageWidth * 0.5f) * settings.WorldScale;
            float worldY = -(cy - settings.ImageHeight * 0.5f) * settings.WorldScale; // Y-flip
            float worldZ = settings.DepthPlaneZ;

            return new Vector3(worldX, worldY, worldZ);
        }

        /// <summary>
        /// Estimate rotation from bounding box aspect ratio and position changes.
        /// This is a rough approximation since we only have 2D data.
        /// </summary>
        static void GenerateRotationCurves(
            AnimationClip clip,
            List<TrajectoryPoint> points,
            Settings settings)
        {
            var curveX = new AnimationCurve();
            var curveY = new AnimationCurve();
            var curveZ = new AnimationCurve();
            var curveW = new AnimationCurve();

            for (int i = 0; i < points.Count; i++)
            {
                // Estimate Y-rotation from horizontal movement direction
                float yaw = 0f;
                if (i > 0)
                {
                    var prevPos = ImageToWorldPosition(points[i - 1].BoundingBox, settings);
                    var currPos = ImageToWorldPosition(points[i].BoundingBox, settings);
                    var delta = currPos - prevPos;
                    if (delta.sqrMagnitude > 0.0001f)
                        yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                }

                var rotation = Quaternion.Euler(0, yaw, 0);
                curveX.AddKey(points[i].Timestamp, rotation.x);
                curveY.AddKey(points[i].Timestamp, rotation.y);
                curveZ.AddKey(points[i].Timestamp, rotation.z);
                curveW.AddKey(points[i].Timestamp, rotation.w);
            }

            clip.SetCurve("", typeof(Transform), "localRotation.x", curveX);
            clip.SetCurve("", typeof(Transform), "localRotation.y", curveY);
            clip.SetCurve("", typeof(Transform), "localRotation.z", curveZ);
            clip.SetCurve("", typeof(Transform), "localRotation.w", curveW);
        }
    }
}
