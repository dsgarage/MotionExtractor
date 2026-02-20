using System;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using DSGarage.MotionExtractor.Editor.Pipeline;

namespace DSGarage.MotionExtractor.Editor
{
    /// <summary>
    /// Phase 1 PoC EditorWindow for testing BlazePose pose estimation.
    /// Load an image, run pose detection, and visualize skeleton overlay.
    /// </summary>
    public class PoseEstimatorTestWindow : EditorWindow
    {
        // ─── Model Assets (assigned in Inspector-like UI) ───

        ModelAsset _detectorModelAsset;
        ModelAsset _landmarkModelAsset;
        TextAsset _anchorsCSV;
        ComputeShader _imageTransformShader;

        // ─── Input ───

        Texture2D _sourceImage;

        // ─── State ───

        PoseEstimator _estimator;
        PoseResult _lastResult;
        bool _isProcessing;
        string _statusMessage = "Ready. Load an image and model assets to begin.";

        // ─── Display ───

        Vector2 _scrollPosition;
        float _keypointRadius = 4f;
        float _lineWidth = 2f;
        Color _keypointColor = Color.green;
        Color _lineColor = new Color(0f, 1f, 0.5f, 0.8f);
        Color _lowConfidenceColor = new Color(1f, 0.5f, 0f, 0.5f);

        [MenuItem("Window/Motion Extractor/Pose Estimator Test")]
        public static void ShowWindow()
        {
            var window = GetWindow<PoseEstimatorTestWindow>();
            window.titleContent = new GUIContent("Pose Estimator Test");
            window.minSize = new Vector2(400, 600);
        }

        void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawModelAssetsSection();
            EditorGUILayout.Space(10);
            DrawInputSection();
            EditorGUILayout.Space(10);
            DrawActionSection();
            EditorGUILayout.Space(10);
            DrawResultSection();
            EditorGUILayout.Space(10);
            DrawPreviewSection();

            EditorGUILayout.EndScrollView();
        }

        void DrawModelAssetsSection()
        {
            EditorGUILayout.LabelField("Model Assets", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _detectorModelAsset = (ModelAsset)EditorGUILayout.ObjectField(
                    "Detector Model", _detectorModelAsset, typeof(ModelAsset), false);

                _landmarkModelAsset = (ModelAsset)EditorGUILayout.ObjectField(
                    "Landmark Model", _landmarkModelAsset, typeof(ModelAsset), false);

                _anchorsCSV = (TextAsset)EditorGUILayout.ObjectField(
                    "Anchors CSV", _anchorsCSV, typeof(TextAsset), false);

                _imageTransformShader = (ComputeShader)EditorGUILayout.ObjectField(
                    "Image Transform Shader", _imageTransformShader, typeof(ComputeShader), false);
            }
        }

        void DrawInputSection()
        {
            EditorGUILayout.LabelField("Input Image", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _sourceImage = (Texture2D)EditorGUILayout.ObjectField(
                    "Image", _sourceImage, typeof(Texture2D), false);

                if (_sourceImage != null)
                {
                    EditorGUILayout.LabelField($"Size: {_sourceImage.width} x {_sourceImage.height}");
                }
            }
        }

        void DrawActionSection()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            bool canRun = _detectorModelAsset != null
                && _landmarkModelAsset != null
                && _anchorsCSV != null
                && _imageTransformShader != null
                && _sourceImage != null
                && !_isProcessing;

            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (GUILayout.Button("Detect Pose", GUILayout.Height(30)))
                {
                    RunPoseEstimation();
                }
            }

            // Status
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
        }

        void DrawResultSection()
        {
            if (_lastResult == null)
                return;

            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField($"Valid: {_lastResult.IsValid}");
                EditorGUILayout.LabelField($"Score: {_lastResult.Score:F3}");

                if (_lastResult.IsValid)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Keypoints:", EditorStyles.miniLabel);
                    for (int i = 0; i < _lastResult.Keypoints.Length; i++)
                    {
                        var kp = _lastResult.Keypoints[i];
                        string name = GetKeypointName(i);
                        EditorGUILayout.LabelField(
                            $"  [{i:D2}] {name}: ({kp.Position.x:F1}, {kp.Position.y:F1}, {kp.Position.z:F2}) " +
                            $"vis={kp.Visibility:F2} pres={kp.Presence:F2}",
                            EditorStyles.miniLabel);
                    }
                }
            }
        }

        void DrawPreviewSection()
        {
            if (_sourceImage == null)
                return;

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            // Display settings
            _keypointRadius = EditorGUILayout.Slider("Keypoint Size", _keypointRadius, 1f, 10f);
            _lineWidth = EditorGUILayout.Slider("Line Width", _lineWidth, 1f, 5f);

            // Calculate preview area maintaining aspect ratio
            float availableWidth = EditorGUIUtility.currentViewWidth - 40;
            float aspectRatio = (float)_sourceImage.height / _sourceImage.width;
            float previewWidth = Mathf.Min(availableWidth, _sourceImage.width);
            float previewHeight = previewWidth * aspectRatio;

            // Draw image
            var previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight);
            GUI.DrawTexture(previewRect, _sourceImage, ScaleMode.ScaleToFit);

            // Draw skeleton overlay
            if (_lastResult != null && _lastResult.IsValid)
            {
                DrawSkeletonOverlay(previewRect, previewWidth, previewHeight);
            }
        }

        void DrawSkeletonOverlay(Rect previewRect, float displayWidth, float displayHeight)
        {
            // Scale factors from image coords to display coords
            float scaleX = displayWidth / _sourceImage.width;
            float scaleY = displayHeight / _sourceImage.height;

            var positions = _lastResult.Keypoints;

            // Draw connections
            foreach (var (from, to) in SkeletonDefinitions.Connections)
            {
                if (!positions[from].IsPresent || !positions[to].IsPresent)
                    continue;

                var fromPos = ImageToDisplay(positions[from].Position, previewRect, scaleX, scaleY);
                var toPos = ImageToDisplay(positions[to].Position, previewRect, scaleX, scaleY);

                var color = (positions[from].IsVisible && positions[to].IsVisible)
                    ? _lineColor
                    : _lowConfidenceColor;

                Handles.color = color;
                Handles.DrawLine(fromPos, toPos, _lineWidth);
            }

            // Draw keypoints
            for (int i = 0; i < positions.Length; i++)
            {
                if (!positions[i].IsPresent)
                    continue;

                var pos = ImageToDisplay(positions[i].Position, previewRect, scaleX, scaleY);
                var color = positions[i].IsVisible ? _keypointColor : _lowConfidenceColor;

                Handles.color = color;
                Handles.DrawSolidDisc(pos, Vector3.forward, _keypointRadius);
            }
        }

        static Vector3 ImageToDisplay(Vector3 imagePos, Rect displayRect, float scaleX, float scaleY)
        {
            return new Vector3(
                displayRect.x + imagePos.x * scaleX,
                displayRect.y + imagePos.y * scaleY,
                0);
        }

        void RunPoseEstimation()
        {
            _isProcessing = true;
            _statusMessage = "Processing...";
            _lastResult = null;

            try
            {
                // Create estimator if needed (lazy init / recreate on asset change)
                _estimator?.Dispose();
                _estimator = new PoseEstimator(
                    _detectorModelAsset,
                    _landmarkModelAsset,
                    _anchorsCSV,
                    _imageTransformShader,
                    BackendType.GPUCompute,
                    0.5f);

                _lastResult = _estimator.Estimate(_sourceImage);

                _statusMessage = _lastResult.IsValid
                    ? $"Detected! Score: {_lastResult.Score:F3}, " +
                      $"Visible keypoints: {CountVisibleKeypoints(_lastResult)}/33"
                    : $"No person detected (score: {_lastResult.Score:F3})";
            }
            catch (Exception e)
            {
                _statusMessage = $"Error: {e.Message}";
                Debug.LogException(e);
            }
            finally
            {
                _isProcessing = false;
                Repaint();
            }
        }

        static int CountVisibleKeypoints(PoseResult result)
        {
            int count = 0;
            foreach (var kp in result.Keypoints)
                if (kp.IsVisible) count++;
            return count;
        }

        static string GetKeypointName(int index)
        {
            return index switch
            {
                SkeletonDefinitions.Nose => "Nose",
                SkeletonDefinitions.LeftEyeInner => "L Eye Inner",
                SkeletonDefinitions.LeftEye => "L Eye",
                SkeletonDefinitions.LeftEyeOuter => "L Eye Outer",
                SkeletonDefinitions.RightEyeInner => "R Eye Inner",
                SkeletonDefinitions.RightEye => "R Eye",
                SkeletonDefinitions.RightEyeOuter => "R Eye Outer",
                SkeletonDefinitions.LeftEar => "L Ear",
                SkeletonDefinitions.RightEar => "R Ear",
                SkeletonDefinitions.MouthLeft => "Mouth L",
                SkeletonDefinitions.MouthRight => "Mouth R",
                SkeletonDefinitions.LeftShoulder => "L Shoulder",
                SkeletonDefinitions.RightShoulder => "R Shoulder",
                SkeletonDefinitions.LeftElbow => "L Elbow",
                SkeletonDefinitions.RightElbow => "R Elbow",
                SkeletonDefinitions.LeftWrist => "L Wrist",
                SkeletonDefinitions.RightWrist => "R Wrist",
                SkeletonDefinitions.LeftPinky => "L Pinky",
                SkeletonDefinitions.RightPinky => "R Pinky",
                SkeletonDefinitions.LeftIndex => "L Index",
                SkeletonDefinitions.RightIndex => "R Index",
                SkeletonDefinitions.LeftThumb => "L Thumb",
                SkeletonDefinitions.RightThumb => "R Thumb",
                SkeletonDefinitions.LeftHip => "L Hip",
                SkeletonDefinitions.RightHip => "R Hip",
                SkeletonDefinitions.LeftKnee => "L Knee",
                SkeletonDefinitions.RightKnee => "R Knee",
                SkeletonDefinitions.LeftAnkle => "L Ankle",
                SkeletonDefinitions.RightAnkle => "R Ankle",
                SkeletonDefinitions.LeftHeel => "L Heel",
                SkeletonDefinitions.RightHeel => "R Heel",
                SkeletonDefinitions.LeftFootIndex => "L Foot Index",
                SkeletonDefinitions.RightFootIndex => "R Foot Index",
                _ => $"Unknown({index})"
            };
        }

        void OnDisable()
        {
            _estimator?.Dispose();
            _estimator = null;
        }
    }
}
