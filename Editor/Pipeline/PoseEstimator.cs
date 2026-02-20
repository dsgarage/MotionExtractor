using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Pipeline
{
    /// <summary>
    /// BlazePose-based pose estimator using Unity Sentis (Inference Engine).
    /// Two-stage pipeline: Pose Detector (224x224) → Pose Landmarker (256x256).
    /// Detects 33 keypoints per person from a single image.
    /// </summary>
    public class PoseEstimator : IDisposable
    {
        // ─── Model Assets ───

        readonly Model _detectorModel;
        readonly Model _landmarkModel;

        // ─── Workers ───

        Worker _detectorWorker;
        Worker _landmarkWorker;

        // ─── Anchors ───

        readonly float[,] _anchors;

        // ─── Preallocated Tensors ───

        Tensor<float> _detectorInput;
        Tensor<float> _landmarkInput;

        // ─── Compute Shader ───

        readonly ComputeShader _imageTransformShader;
        readonly int _imageTransformKernel;

        // ─── Configuration ───

        readonly BackendType _backendType;
        readonly float _scoreThreshold;

        bool _disposed;

        /// <summary>
        /// Initialize the PoseEstimator with model assets and configuration.
        /// </summary>
        /// <param name="detectorAsset">BlazePose detector ONNX model (224x224 input).</param>
        /// <param name="landmarkAsset">BlazePose landmark ONNX model (256x256 input).</param>
        /// <param name="anchorsCSV">Detector anchors CSV text asset (2254 anchors).</param>
        /// <param name="imageTransformShader">Compute shader for affine image-to-tensor transform.</param>
        /// <param name="backendType">Sentis backend (GPU recommended).</param>
        /// <param name="scoreThreshold">Minimum detection score to consider valid.</param>
        public PoseEstimator(
            ModelAsset detectorAsset,
            ModelAsset landmarkAsset,
            TextAsset anchorsCSV,
            ComputeShader imageTransformShader,
            BackendType backendType = BackendType.GPUCompute,
            float scoreThreshold = 0.5f)
        {
            _backendType = backendType;
            _scoreThreshold = scoreThreshold;

            // Load anchors
            _anchors = BlazeUtils.LoadAnchors(anchorsCSV.text, BlazeUtils.AnchorCount);

            // Load and post-process detector model (bake ArgMax into graph)
            var rawDetector = ModelLoader.Load(detectorAsset);
            _detectorModel = BlazeUtils.BuildPostProcessedDetector(rawDetector);

            // Load landmark model
            _landmarkModel = ModelLoader.Load(landmarkAsset);

            // Create workers
            _detectorWorker = new Worker(_detectorModel, _backendType);
            _landmarkWorker = new Worker(_landmarkModel, _backendType);

            // Preallocate input tensors (NHWC format)
            _detectorInput = new Tensor<float>(
                new TensorShape(1, BlazeUtils.DetectorInputSize, BlazeUtils.DetectorInputSize, 3));
            _landmarkInput = new Tensor<float>(
                new TensorShape(1, BlazeUtils.LandmarkInputSize, BlazeUtils.LandmarkInputSize, 3));

            // Compute shader
            _imageTransformShader = imageTransformShader;
            _imageTransformKernel = _imageTransformShader.FindKernel("ImageTransform");
        }

        /// <summary>
        /// Estimate pose from a single image (synchronous, blocking).
        /// Suitable for Editor batch processing.
        /// </summary>
        /// <param name="sourceTexture">Input image as Texture2D.</param>
        /// <returns>Pose result with 33 keypoints, or an invalid result if no person detected.</returns>
        public PoseResult Estimate(Texture2D sourceTexture)
        {
            var result = new PoseResult();

            // ─── Stage 1: Pose Detection ───

            // Compute affine transform: source image → 224x224 detector input
            var detectorMatrix = BlazeUtils.ComputeDetectorInputMatrix(
                sourceTexture.width, sourceTexture.height);
            var detectorMatrixInv = detectorMatrix.inverse;

            // Transform image to detector input tensor via compute shader
            TransformImageToTensor(sourceTexture, _detectorInput,
                BlazeUtils.DetectorInputSize, BlazeUtils.DetectorInputSize,
                ComputeTextureSamplingMatrix(detectorMatrixInv, sourceTexture.width, sourceTexture.height));

            // Run detector
            _detectorWorker.Schedule(_detectorInput);

            // Read detector outputs (blocking)
            using var outputIdx = (_detectorWorker.PeekOutput(0) as Tensor<int>).ReadbackAndClone();
            using var outputScore = (_detectorWorker.PeekOutput(1) as Tensor<float>).ReadbackAndClone();
            using var outputBox = (_detectorWorker.PeekOutput(2) as Tensor<float>).ReadbackAndClone();

            float score = outputScore[0, 0, 0];
            if (score < _scoreThreshold)
            {
                result.Score = score;
                result.IsValid = false;
                return result;
            }

            int bestIndex = outputIdx[0, 0];

            // Decode detector box
            float[] boxData = new float[12];
            for (int i = 0; i < 12; i++)
                boxData[i] = outputBox[0, 0, i];

            BlazeUtils.DecodeDetectorOutput(boxData, _anchors, bestIndex,
                out var center, out var size, out var bodyKeypoints);

            // bodyKeypoints[0] = mid-hip, bodyKeypoints[1] = mid-shoulder (in detector 224x224 space)
            // Transform to source image space
            var midHipDetector = bodyKeypoints[0];
            var midShoulderDetector = bodyKeypoints[1];

            var midHipImage = (Vector2)(detectorMatrix * new Vector4(midHipDetector.x, midHipDetector.y, 0, 1));
            var midShoulderImage = (Vector2)(detectorMatrix * new Vector4(midShoulderDetector.x, midShoulderDetector.y, 0, 1));

            // ─── Stage 2: Pose Landmark ───

            // Compute affine transform: source image → 256x256 landmark input (crop + rotate)
            var cropMatrix = BlazeUtils.ComputeLandmarkCropMatrix(
                midHipImage, midShoulderImage, sourceTexture.width, sourceTexture.height);
            var cropMatrixInv = cropMatrix.inverse;

            // Transform image to landmark input tensor
            TransformImageToTensor(sourceTexture, _landmarkInput,
                BlazeUtils.LandmarkInputSize, BlazeUtils.LandmarkInputSize,
                ComputeTextureSamplingMatrix(cropMatrixInv, sourceTexture.width, sourceTexture.height));

            // Run landmark model
            _landmarkWorker.Schedule(_landmarkInput);

            // Read landmark output: (1, 195) = 33 keypoints * 5 values
            using var landmarks = (_landmarkWorker.PeekOutput("Identity") as Tensor<float>).ReadbackAndClone();

            // Parse 33 keypoints
            for (int i = 0; i < BlazeUtils.LandmarkCount; i++)
            {
                int offset = i * BlazeUtils.ValuesPerLandmark;
                float lmX = landmarks[0, offset + 0]; // x in 256x256 space
                float lmY = landmarks[0, offset + 1]; // y in 256x256 space
                float lmZ = landmarks[0, offset + 2]; // relative depth
                float vis = Sigmoid(landmarks[0, offset + 3]);
                float pres = Sigmoid(landmarks[0, offset + 4]);

                // Transform from landmark tensor space back to image coordinates
                var imagePos = BlazeUtils.TransformLandmarkToImage(lmX, lmY, lmZ, cropMatrix);

                result.Keypoints[i] = new Keypoint
                {
                    Position = imagePos,
                    Visibility = vis,
                    Presence = pres
                };
            }

            result.Score = score;
            result.IsValid = true;
            return result;
        }

        /// <summary>
        /// Use the compute shader to transform the source texture into the target tensor
        /// with the given affine matrix (maps tensor coords to UV coordinates).
        /// </summary>
        void TransformImageToTensor(
            Texture sourceTexture, Tensor<float> targetTensor,
            int targetWidth, int targetHeight, Matrix4x4 uvMatrix)
        {
            var tensorData = ComputeTensorData.Pin(targetTensor, clearOnInit: false);

            _imageTransformShader.SetTexture(_imageTransformKernel, "X_tex2D", sourceTexture);
            _imageTransformShader.SetBuffer(_imageTransformKernel, "Optr", tensorData.buffer);
            _imageTransformShader.SetInt("O_height", targetHeight);
            _imageTransformShader.SetInt("O_width", targetWidth);
            _imageTransformShader.SetInt("O_channels", 3);
            _imageTransformShader.SetMatrix("affineMatrix", uvMatrix);

            int threadGroupsX = Mathf.CeilToInt(targetWidth / 8f);
            int threadGroupsY = Mathf.CeilToInt(targetHeight / 8f);
            _imageTransformShader.Dispatch(_imageTransformKernel, threadGroupsX, threadGroupsY, 1);
        }

        /// <summary>
        /// Compute a matrix that maps from tensor pixel coordinates to texture UV [0,1] coordinates,
        /// using the inverse of the image-to-tensor affine matrix.
        /// </summary>
        static Matrix4x4 ComputeTextureSamplingMatrix(
            Matrix4x4 tensorToImageMatrix, float imageWidth, float imageHeight)
        {
            // Scale from image pixel coords to UV [0,1]
            var pixelToUV = Matrix4x4.Scale(new Vector3(1f / imageWidth, 1f / imageHeight, 1f));
            return pixelToUV * tensorToImageMatrix;
        }

        static float Sigmoid(float x)
        {
            x = Mathf.Clamp(x, -80f, 80f);
            return 1f / (1f + Mathf.Exp(-x));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _detectorInput?.Dispose();
            _landmarkInput?.Dispose();
            _detectorWorker?.Dispose();
            _landmarkWorker?.Dispose();
        }
    }
}
