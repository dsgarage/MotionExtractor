using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Pipeline
{
    /// <summary>
    /// Utility methods for BlazePose model pipeline.
    /// Handles anchor loading, functional graph post-processing, and affine transforms.
    /// </summary>
    public static class BlazeUtils
    {
        public const int DetectorInputSize = 224;
        public const int LandmarkInputSize = 256;
        public const int AnchorCount = 2254;
        public const int LandmarkCount = 33;
        public const int ValuesPerLandmark = 5; // x, y, z, visibility, presence

        /// <summary>
        /// Load detector anchors from a CSV text asset.
        /// Each line: center_x, center_y, w, h (only center_x and center_y are used).
        /// </summary>
        public static float[,] LoadAnchors(string csvText, int expectedCount)
        {
            var anchors = new float[expectedCount, 4];
            var lines = csvText.Split('\n');
            int count = 0;

            for (int i = 0; i < lines.Length && count < expectedCount; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var values = line.Split(',');
                if (values.Length < 4)
                    continue;

                for (int j = 0; j < 4; j++)
                {
                    if (float.TryParse(values[j].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float val))
                    {
                        anchors[count, j] = val;
                    }
                }
                count++;
            }

            if (count != expectedCount)
                Debug.LogWarning($"[BlazeUtils] Expected {expectedCount} anchors, loaded {count}");

            return anchors;
        }

        /// <summary>
        /// Build a post-processed detector model using FunctionalGraph.
        /// Bakes ArgMax filtering into the model graph to select the best detection.
        /// Returns a model with outputs: (bestIndex:int, bestScore:float, bestBox:float).
        /// </summary>
        public static Model BuildPostProcessedDetector(Model detectorModel)
        {
            var graph = new FunctionalGraph();
            var input = graph.AddInput(detectorModel, 0);
            var outputs = Functional.Forward(detectorModel, input);

            // outputs[0] = boxes (1, 2254, 12)
            // outputs[1] = scores (1, 2254, 1)
            var boxes = outputs[0];
            var scores = outputs[1];

            // Apply sigmoid to scores with clamping for numerical stability
            var clampedScores = Functional.Clamp(scores, -80f, 80f);
            var sigmoidScores = Functional.Sigmoid(clampedScores);

            // ArgMax to find best detection
            var reshapedScores = Functional.Reshape(sigmoidScores, new[] { 1, AnchorCount });
            var bestIdx = Functional.ArgMax(reshapedScores, 1); // (1, 1) int

            // Extract best score and box using IndexSelect
            var bestScore = Functional.IndexSelect(sigmoidScores, 1, bestIdx); // (1, 1, 1)
            var bestBox = Functional.IndexSelect(boxes, 1, bestIdx);           // (1, 1, 12)

            return graph.Compile(bestIdx, bestScore, bestBox);
        }

        /// <summary>
        /// Decode detector output: apply anchor offsets and compute absolute coordinates.
        /// </summary>
        public static void DecodeDetectorOutput(float[] boxData, float[,] anchors, int bestIndex,
            out Vector2 center, out Vector2 size, out Vector2[] bodyKeypoints)
        {
            float anchorX = anchors[bestIndex, 0] * DetectorInputSize;
            float anchorY = anchors[bestIndex, 1] * DetectorInputSize;

            // Box: [x, y, w, h, kp0_x, kp0_y, kp1_x, kp1_y, kp2_x, kp2_y, kp3_x, kp3_y]
            center = new Vector2(boxData[0] + anchorX, boxData[1] + anchorY);
            size = new Vector2(boxData[2], boxData[3]);

            // 4 body keypoints (mid-hip, mid-shoulder, etc.)
            bodyKeypoints = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                bodyKeypoints[i] = new Vector2(
                    boxData[4 + i * 2] + anchorX,
                    boxData[5 + i * 2] + anchorY
                );
            }
        }

        /// <summary>
        /// Compute the affine transform matrix for cropping the detected body region
        /// from the original image into the landmark model input (256x256).
        /// </summary>
        public static Matrix4x4 ComputeLandmarkCropMatrix(
            Vector2 midHip, Vector2 midShoulder, float imageWidth, float imageHeight)
        {
            // Rotation angle based on hip-to-shoulder direction
            var delta = midShoulder - midHip;
            float theta = Mathf.Atan2(delta.y, delta.x) - Mathf.PI * 0.5f;

            // Scale: body length with margin
            float bodyLength = delta.magnitude;
            float radius = bodyLength * 1.25f;
            float scale = radius * 2f / LandmarkInputSize;

            // Body center (midpoint between hip and shoulder centers)
            var bodyCenter = (midHip + midShoulder) * 0.5f;

            float cosT = Mathf.Cos(theta);
            float sinT = Mathf.Sin(theta);

            // Affine matrix: maps from landmark tensor coords (0..256) to source image coords
            // M * [u, v, 1]^T = [srcX, srcY, 1]^T
            var M = Matrix4x4.identity;
            M.m00 = scale * cosT;
            M.m01 = -scale * sinT;
            M.m02 = 0;
            M.m03 = bodyCenter.x - LandmarkInputSize * 0.5f * (scale * cosT - scale * sinT);

            M.m10 = scale * sinT;
            M.m11 = scale * cosT;
            M.m12 = 0;
            M.m13 = bodyCenter.y - LandmarkInputSize * 0.5f * (scale * sinT + scale * cosT);

            return M;
        }

        /// <summary>
        /// Compute the affine transform matrix for fitting the source image into the detector input (224x224).
        /// Preserves aspect ratio with letterboxing.
        /// </summary>
        public static Matrix4x4 ComputeDetectorInputMatrix(float imageWidth, float imageHeight)
        {
            float scale;
            float offsetX = 0, offsetY = 0;

            if (imageWidth > imageHeight)
            {
                scale = imageWidth / DetectorInputSize;
                offsetY = (imageWidth - imageHeight) * 0.5f;
            }
            else
            {
                scale = imageHeight / DetectorInputSize;
                offsetX = (imageHeight - imageWidth) * 0.5f;
            }

            // Maps from tensor coords (0..224) to source image coords
            var M = Matrix4x4.identity;
            M.m00 = scale;
            M.m03 = -offsetX;
            M.m11 = scale;
            M.m13 = -offsetY;

            return M;
        }

        /// <summary>
        /// Transform landmark coordinates from the 256x256 tensor space back to image coordinates.
        /// </summary>
        public static Vector3 TransformLandmarkToImage(
            float lmX, float lmY, float lmZ, Matrix4x4 cropMatrix)
        {
            var tensorPos = new Vector4(lmX, lmY, 0, 1);
            var imagePos = cropMatrix * tensorPos;
            return new Vector3(imagePos.x, imagePos.y, lmZ);
        }
    }
}
