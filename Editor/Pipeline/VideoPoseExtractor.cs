using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Pipeline
{
    /// <summary>
    /// Orchestrates the full video → pose pipeline.
    /// Extracts frames from video via VideoFrameReader and runs PoseEstimator on each,
    /// accumulating time-series pose data.
    /// </summary>
    public class VideoPoseExtractor : IDisposable
    {
        /// <summary>Configuration for the extraction pipeline.</summary>
        public struct Settings
        {
            /// <summary>Process every Nth frame. 1 = all, 2 = every other, 5 = every 5th.</summary>
            public int FrameSkip;

            /// <summary>Minimum pose detection score to consider valid.</summary>
            public float ScoreThreshold;

            /// <summary>Sentis backend type.</summary>
            public BackendType BackendType;

            public static Settings Default => new Settings
            {
                FrameSkip = 1,
                ScoreThreshold = 0.5f,
                BackendType = BackendType.GPUCompute
            };
        }

        /// <summary>Result of the full extraction pipeline.</summary>
        public class ExtractionResult
        {
            /// <summary>All extracted pose frames in chronological order.</summary>
            public List<PoseFrame> Frames = new List<PoseFrame>();

            /// <summary>Video metadata.</summary>
            public VideoFrameReader.VideoInfo VideoInfo;

            /// <summary>Number of frames where pose was successfully detected.</summary>
            public int ValidFrameCount;

            /// <summary>Total number of frames processed.</summary>
            public int TotalFrameCount;

            /// <summary>Processing time in seconds.</summary>
            public float ProcessingTime;
        }

        readonly PoseEstimator _estimator;
        bool _disposed;
        bool _cancelRequested;

        /// <summary>
        /// Create a VideoPoseExtractor with the given model assets.
        /// </summary>
        public VideoPoseExtractor(
            ModelAsset detectorAsset,
            ModelAsset landmarkAsset,
            TextAsset anchorsCSV,
            ComputeShader imageTransformShader,
            Settings settings)
        {
            _estimator = new PoseEstimator(
                detectorAsset,
                landmarkAsset,
                anchorsCSV,
                imageTransformShader,
                settings.BackendType,
                settings.ScoreThreshold);
        }

        /// <summary>
        /// Extract poses from all frames of a video file.
        /// This is a blocking call suitable for Editor batch processing.
        /// Shows progress via EditorUtility.DisplayProgressBar.
        /// </summary>
        /// <param name="videoPath">Absolute path to the video file.</param>
        /// <param name="frameSkip">Process every Nth frame.</param>
        /// <returns>Extraction result with all pose frames.</returns>
        public ExtractionResult Extract(string videoPath, int frameSkip = 1)
        {
            _cancelRequested = false;
            var result = new ExtractionResult();
            float startTime = Time.realtimeSinceStartup;

            using var reader = new VideoFrameReader(videoPath, frameSkip);
            result.VideoInfo = reader.Info;

            try
            {
                foreach (var (frameIndex, timestamp, texture) in
                    reader.ExtractFrames(OnFrameExtractionProgress))
                {
                    if (_cancelRequested)
                    {
                        Debug.Log("[VideoPoseExtractor] Extraction cancelled by user.");
                        break;
                    }

                    // Update progress for pose estimation phase
                    float estimatedTotal = result.VideoInfo.TotalFrames > 0
                        ? result.VideoInfo.TotalFrames / (float)Mathf.Max(1, frameSkip)
                        : 0;
                    float progress = estimatedTotal > 0
                        ? result.TotalFrameCount / estimatedTotal
                        : 0;

                    bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                        "Motion Extractor",
                        $"Detecting pose: frame {result.TotalFrameCount + 1}" +
                        (estimatedTotal > 0 ? $" / {estimatedTotal:F0}" : "") +
                        $" (t={timestamp:F2}s)",
                        0.5f + progress * 0.5f); // 50-100% range for pose estimation

                    if (cancelled)
                    {
                        _cancelRequested = true;
                        UnityEngine.Object.DestroyImmediate(texture);
                        break;
                    }

                    // Run pose estimation
                    var poseResult = _estimator.Estimate(texture);

                    // Store frame data
                    var frame = new PoseFrame
                    {
                        FrameIndex = frameIndex,
                        Timestamp = timestamp,
                        Poses = new[] { poseResult },
                        ImageSize = new Vector2Int(texture.width, texture.height)
                    };
                    result.Frames.Add(frame);
                    result.TotalFrameCount++;

                    if (poseResult.IsValid)
                        result.ValidFrameCount++;

                    // Release texture memory
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            result.ProcessingTime = Time.realtimeSinceStartup - startTime;

            Debug.Log($"[VideoPoseExtractor] Extraction complete: " +
                $"{result.ValidFrameCount}/{result.TotalFrameCount} frames with valid pose, " +
                $"processed in {result.ProcessingTime:F1}s");

            return result;
        }

        /// <summary>Request cancellation of the current extraction.</summary>
        public void Cancel()
        {
            _cancelRequested = true;
        }

        void OnFrameExtractionProgress(float progress, string message)
        {
            // Frame extraction is 0-50% of total progress
            EditorUtility.DisplayProgressBar("Motion Extractor", message, progress * 0.5f);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _estimator?.Dispose();
        }
    }
}
