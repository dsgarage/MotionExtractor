using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Pipeline
{
    /// <summary>
    /// Converts between BlazePose/MotionBERT coordinate systems and Unity coordinate system.
    /// BlazePose: right-handed, Y-down, Z towards camera (image coordinates)
    /// Unity: left-handed, Y-up, Z-forward
    /// </summary>
    public static class CoordinateConverter
    {
        /// <summary>
        /// Convert BlazePose image coordinates to Unity world coordinates.
        /// BlazePose: origin at top-left, Y-down, Z is relative depth from hip.
        /// Unity: Y-up, Z-forward (left-handed).
        /// </summary>
        /// <param name="blazePosePos">Position in BlazePose image space.</param>
        /// <param name="imageWidth">Source image width in pixels.</param>
        /// <param name="imageHeight">Source image height in pixels.</param>
        /// <param name="worldScale">Scale factor to convert pixels to world units.</param>
        /// <returns>Position in Unity world space.</returns>
        public static Vector3 BlazePoseToUnity(
            Vector3 blazePosePos, float imageWidth, float imageHeight, float worldScale = 0.01f)
        {
            // Center the coordinates (origin at image center)
            float centeredX = blazePosePos.x - imageWidth * 0.5f;
            float centeredY = blazePosePos.y - imageHeight * 0.5f;

            // BlazePose Z is depth relative to hip (positive = closer to camera)
            // Unity: X-right, Y-up, Z-forward
            return new Vector3(
                centeredX * worldScale,       // X: left-right (same direction)
                -centeredY * worldScale,      // Y: up-down (flipped, BlazePose is Y-down)
                -blazePosePos.z * worldScale  // Z: depth (flipped, BlazePose Z+ = toward camera)
            );
        }

        /// <summary>
        /// Convert an entire set of BlazePose keypoints to Unity world coordinates.
        /// Also re-centers the skeleton so that the hips are at the origin.
        /// </summary>
        /// <param name="blazePoseKeypoints">33 keypoints in BlazePose image space.</param>
        /// <param name="imageWidth">Source image width.</param>
        /// <param name="imageHeight">Source image height.</param>
        /// <param name="worldScale">Scale factor (pixels to world units).</param>
        /// <param name="centerOnHips">If true, translate so hips center is at origin.</param>
        /// <returns>33 keypoints in Unity world space.</returns>
        public static Vector3[] ConvertSkeleton(
            Vector3[] blazePoseKeypoints,
            float imageWidth, float imageHeight,
            float worldScale = 0.01f,
            bool centerOnHips = true)
        {
            var unityPositions = new Vector3[blazePoseKeypoints.Length];

            for (int i = 0; i < blazePoseKeypoints.Length; i++)
            {
                unityPositions[i] = BlazePoseToUnity(
                    blazePoseKeypoints[i], imageWidth, imageHeight, worldScale);
            }

            if (centerOnHips)
            {
                var hipsCenter = SkeletonDefinitions.EstimateHips(unityPositions);
                for (int i = 0; i < unityPositions.Length; i++)
                    unityPositions[i] -= hipsCenter;
            }

            return unityPositions;
        }

        /// <summary>
        /// Estimate world scale from the detected skeleton height.
        /// Assumes an average human height of approximately 1.7 meters.
        /// </summary>
        /// <param name="keypoints">33 keypoints in image space.</param>
        /// <param name="targetHeightMeters">Expected real-world height in meters.</param>
        /// <returns>Scale factor to apply when converting coordinates.</returns>
        public static float EstimateWorldScale(Vector3[] keypoints, float targetHeightMeters = 1.7f)
        {
            // Estimate body height from ankle-to-head distance
            var head = SkeletonDefinitions.EstimateHead(keypoints);
            var leftAnkle = keypoints[SkeletonDefinitions.LeftAnkle];
            var rightAnkle = keypoints[SkeletonDefinitions.RightAnkle];
            var ankleCenter = (leftAnkle + rightAnkle) * 0.5f;

            float pixelHeight = Vector3.Distance(head, ankleCenter);
            if (pixelHeight < 1f) return 0.01f; // fallback

            return targetHeightMeters / pixelHeight;
        }
    }
}
