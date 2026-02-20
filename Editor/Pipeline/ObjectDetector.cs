using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;
using DSGarage.MotionExtractor.Editor.Tracking;

namespace DSGarage.MotionExtractor.Editor.Pipeline
{
    /// <summary>
    /// Object detector using RF-DETR (or compatible DETR-style model) via Unity Sentis.
    /// Detects objects in a single frame and returns bounding boxes with class labels.
    /// </summary>
    public class ObjectDetector : IDisposable
    {
        /// <summary>Configuration for the object detector.</summary>
        public struct Config
        {
            /// <summary>Input image size for the model (square).</summary>
            public int InputSize;

            /// <summary>Minimum confidence score for detections.</summary>
            public float ScoreThreshold;

            /// <summary>IoU threshold for Non-Maximum Suppression.</summary>
            public float NmsThreshold;

            /// <summary>Class IDs to include (null = all classes).</summary>
            public HashSet<int> IncludeClassIds;

            /// <summary>Class IDs to exclude (applied after include filter).</summary>
            public HashSet<int> ExcludeClassIds;

            public static Config Default => new Config
            {
                InputSize = 560,
                ScoreThreshold = 0.5f,
                NmsThreshold = 0.45f,
                IncludeClassIds = null,
                ExcludeClassIds = null
            };
        }

        readonly Model _model;
        Worker _worker;
        Tensor<float> _inputTensor;
        readonly ComputeShader _imageTransformShader;
        readonly int _imageTransformKernel;
        readonly Config _config;
        bool _disposed;

        /// <summary>
        /// Initialize the object detector with a DETR-style ONNX model.
        /// </summary>
        /// <param name="modelAsset">RF-DETR or compatible ONNX model.</param>
        /// <param name="imageTransformShader">Compute shader for image preprocessing.</param>
        /// <param name="config">Detection configuration.</param>
        /// <param name="backendType">Sentis backend.</param>
        public ObjectDetector(
            ModelAsset modelAsset,
            ComputeShader imageTransformShader,
            Config config = default,
            BackendType backendType = BackendType.GPUCompute)
        {
            _config = config.InputSize > 0 ? config : Config.Default;
            _imageTransformShader = imageTransformShader;
            _imageTransformKernel = _imageTransformShader.FindKernel("ImageTransform");

            _model = ModelLoader.Load(modelAsset);
            _worker = new Worker(_model, backendType);

            // Preallocate input tensor (NHWC)
            _inputTensor = new Tensor<float>(
                new TensorShape(1, _config.InputSize, _config.InputSize, 3));
        }

        /// <summary>
        /// Detect objects in a single image.
        /// </summary>
        /// <param name="sourceTexture">Input image.</param>
        /// <returns>Array of detections with bounding boxes, scores, and class labels.</returns>
        public Detection[] Detect(Texture2D sourceTexture)
        {
            // Preprocess: transform image to model input tensor
            var uvMatrix = ComputeTextureSamplingMatrix(sourceTexture.width, sourceTexture.height);
            TransformImageToTensor(sourceTexture, _inputTensor, _config.InputSize, _config.InputSize, uvMatrix);

            // Run inference
            _worker.Schedule(_inputTensor);

            // Parse DETR output format
            // RF-DETR outputs: boxes (1, N, 4) in [cx, cy, w, h] normalized format
            //                  scores (1, N, num_classes) class probabilities
            var detections = ParseDetrOutput(sourceTexture.width, sourceTexture.height);

            // Apply NMS
            detections = ApplyNMS(detections, _config.NmsThreshold);

            return detections;
        }

        Detection[] ParseDetrOutput(int imageWidth, int imageHeight)
        {
            var results = new List<Detection>();

            // Try to get outputs - model format may vary
            Tensor<float> boxesTensor = null;
            Tensor<float> scoresTensor = null;

            try
            {
                // Common DETR output format
                boxesTensor = (_worker.PeekOutput(0) as Tensor<float>)?.ReadbackAndClone();
                scoresTensor = (_worker.PeekOutput(1) as Tensor<float>)?.ReadbackAndClone();
            }
            catch
            {
                // Try alternative output names
                try
                {
                    boxesTensor = (_worker.PeekOutput("boxes") as Tensor<float>)?.ReadbackAndClone();
                    scoresTensor = (_worker.PeekOutput("scores") as Tensor<float>)?.ReadbackAndClone();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ObjectDetector] Failed to read model outputs: {e.Message}");
                    return results.ToArray();
                }
            }

            if (boxesTensor == null || scoresTensor == null)
            {
                boxesTensor?.Dispose();
                scoresTensor?.Dispose();
                return results.ToArray();
            }

            using (boxesTensor)
            using (scoresTensor)
            {
                // Determine shape
                int numDetections = boxesTensor.shape[1];
                int numClasses = scoresTensor.shape.length > 2 ? scoresTensor.shape[2] : 1;

                for (int i = 0; i < numDetections; i++)
                {
                    // Find best class
                    int bestClass = 0;
                    float bestScore = 0;

                    if (numClasses > 1)
                    {
                        for (int c = 0; c < numClasses; c++)
                        {
                            float score = scoresTensor[0, i, c];
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestClass = c;
                            }
                        }
                    }
                    else
                    {
                        bestScore = scoresTensor[0, i, 0];
                    }

                    if (bestScore < _config.ScoreThreshold)
                        continue;

                    // Apply class filters
                    if (_config.IncludeClassIds != null && !_config.IncludeClassIds.Contains(bestClass))
                        continue;
                    if (_config.ExcludeClassIds != null && _config.ExcludeClassIds.Contains(bestClass))
                        continue;

                    // DETR box format: [cx, cy, w, h] normalized to [0,1]
                    float cx = boxesTensor[0, i, 0] * imageWidth;
                    float cy = boxesTensor[0, i, 1] * imageHeight;
                    float w = boxesTensor[0, i, 2] * imageWidth;
                    float h = boxesTensor[0, i, 3] * imageHeight;

                    var bbox = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);

                    results.Add(new Detection
                    {
                        BoundingBox = bbox,
                        Score = bestScore,
                        ClassId = bestClass,
                        ClassLabel = CocoLabels.GetLabel(bestClass)
                    });
                }
            }

            return results.ToArray();
        }

        static Detection[] ApplyNMS(Detection[] detections, float iouThreshold)
        {
            if (detections.Length <= 1) return detections;

            // Sort by score descending
            var sorted = new List<Detection>(detections);
            sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

            var kept = new List<Detection>();
            var suppressed = new bool[sorted.Count];

            for (int i = 0; i < sorted.Count; i++)
            {
                if (suppressed[i]) continue;
                kept.Add(sorted[i]);

                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (suppressed[j]) continue;
                    if (sorted[i].ClassId == sorted[j].ClassId &&
                        HungarianAlgorithm.IoU(sorted[i].BoundingBox, sorted[j].BoundingBox) > iouThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }

            return kept.ToArray();
        }

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

        Matrix4x4 ComputeTextureSamplingMatrix(float imageWidth, float imageHeight)
        {
            // Scale from tensor pixel coords to UV [0,1]
            float scale;
            float offsetX = 0, offsetY = 0;

            if (imageWidth > imageHeight)
            {
                scale = imageWidth / _config.InputSize;
                offsetY = (imageWidth - imageHeight) * 0.5f;
            }
            else
            {
                scale = imageHeight / _config.InputSize;
                offsetX = (imageHeight - imageWidth) * 0.5f;
            }

            var tensorToImage = Matrix4x4.identity;
            tensorToImage.m00 = scale;
            tensorToImage.m03 = -offsetX;
            tensorToImage.m11 = scale;
            tensorToImage.m13 = -offsetY;

            var pixelToUV = Matrix4x4.Scale(new Vector3(1f / imageWidth, 1f / imageHeight, 1f));
            return pixelToUV * tensorToImage;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _inputTensor?.Dispose();
            _worker?.Dispose();
        }
    }

    /// <summary>
    /// COCO 80-class label mapping.
    /// </summary>
    public static class CocoLabels
    {
        static readonly string[] Labels = new string[]
        {
            "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck",
            "boat", "traffic light", "fire hydrant", "stop sign", "parking meter", "bench",
            "bird", "cat", "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra",
            "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
            "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove",
            "skateboard", "surfboard", "tennis racket", "bottle", "wine glass", "cup",
            "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich", "orange",
            "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
            "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse",
            "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink",
            "refrigerator", "book", "clock", "vase", "scissors", "teddy bear",
            "hair drier", "toothbrush"
        };

        public static string GetLabel(int classId)
        {
            return classId >= 0 && classId < Labels.Length ? Labels[classId] : $"class_{classId}";
        }

        public static int ClassCount => Labels.Length;
    }
}
