using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DSGarage.MotionExtractor.Editor.Export;
using DSGarage.MotionExtractor.Editor.Pipeline;
using DSGarage.MotionExtractor.Editor.Tracking;

namespace DSGarage.MotionExtractor.Editor
{
    /// <summary>
    /// Main EditorWindow for Motion Extractor.
    /// Provides a UI for extracting skeletal animations and object trajectories from video files.
    /// </summary>
    public class MotionExtractorWindow : EditorWindow
    {
        // ─── EditorPrefs Keys ───

        const string PrefKeyOutputPath = "MotionExtractor.OutputPath";
        const string PrefKeyFps = "MotionExtractor.Fps";
        const string PrefKeyFrameSkip = "MotionExtractor.FrameSkip";
        const string PrefKeyMode = "MotionExtractor.Mode";
        const string PrefKeyRootMotion = "MotionExtractor.RootMotion";
        const string PrefKeyFootLock = "MotionExtractor.FootLock";
        const string PrefKeySmoothing = "MotionExtractor.Smoothing";

        // ─── UI Elements ───

        ObjectField _videoField;
        ObjectField _avatarField;
        ObjectField _detectorModelField;
        ObjectField _landmarkModelField;
        ObjectField _anchorsField;
        ObjectField _objectDetectorField;
        ObjectField _computeShaderField;

        RadioButtonGroup _modeGroup;
        SliderInt _qualitySlider;
        Label _qualityHint;
        IntegerField _fpsField;
        TextField _outputPathField;
        Toggle _rootMotionToggle;
        Toggle _footLockToggle;
        Toggle _smoothingToggle;

        Button _extractButton;
        VisualElement _progressSection;
        ProgressBar _progressBar;
        Label _progressDetail;
        Button _cancelButton;
        VisualElement _resultsSection;
        VisualElement _resultsList;
        Button _openFolderButton;

        // ─── State ───

        bool _isProcessing;
        VideoPoseExtractor _extractor;
        List<string> _generatedFiles = new List<string>();

        [MenuItem("Window/Motion Extractor/Motion Extractor")]
        public static void ShowWindow()
        {
            var window = GetWindow<MotionExtractorWindow>();
            window.titleContent = new GUIContent("Motion Extractor");
            window.minSize = new Vector2(400, 550);
        }

        void CreateGUI()
        {
            // Load UXML
            var uxmlPath = FindAssetPath<VisualTreeAsset>("MotionExtractor");
            if (uxmlPath != null)
            {
                var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
                uxml.CloneTree(rootVisualElement);
            }

            // Load USS
            var ussPath = FindAssetPath<StyleSheet>("MotionExtractor");
            if (ussPath != null)
            {
                var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
                rootVisualElement.styleSheets.Add(uss);
            }

            BindUIElements();
            LoadPreferences();
            SetupCallbacks();
            SetupDragAndDrop();
        }

        void BindUIElements()
        {
            _videoField = rootVisualElement.Q<ObjectField>("video-field");
            _avatarField = rootVisualElement.Q<ObjectField>("avatar-field");
            _detectorModelField = rootVisualElement.Q<ObjectField>("detector-model-field");
            _landmarkModelField = rootVisualElement.Q<ObjectField>("landmark-model-field");
            _anchorsField = rootVisualElement.Q<ObjectField>("anchors-field");
            _objectDetectorField = rootVisualElement.Q<ObjectField>("object-detector-field");
            _computeShaderField = rootVisualElement.Q<ObjectField>("compute-shader-field");

            _modeGroup = rootVisualElement.Q<RadioButtonGroup>("mode-group");
            _qualitySlider = rootVisualElement.Q<SliderInt>("quality-slider");
            _qualityHint = rootVisualElement.Q<Label>("quality-hint");
            _fpsField = rootVisualElement.Q<IntegerField>("fps-field");
            _outputPathField = rootVisualElement.Q<TextField>("output-path-field");
            _rootMotionToggle = rootVisualElement.Q<Toggle>("root-motion-toggle");
            _footLockToggle = rootVisualElement.Q<Toggle>("foot-lock-toggle");
            _smoothingToggle = rootVisualElement.Q<Toggle>("smoothing-toggle");

            _extractButton = rootVisualElement.Q<Button>("extract-button");
            _progressSection = rootVisualElement.Q<VisualElement>("progress-section");
            _progressBar = rootVisualElement.Q<ProgressBar>("progress-bar");
            _progressDetail = rootVisualElement.Q<Label>("progress-detail");
            _cancelButton = rootVisualElement.Q<Button>("cancel-button");
            _resultsSection = rootVisualElement.Q<VisualElement>("results-section");
            _resultsList = rootVisualElement.Q<VisualElement>("results-list");
            _openFolderButton = rootVisualElement.Q<Button>("open-folder-button");

            // Set object field types
            if (_videoField != null) _videoField.objectType = typeof(DefaultAsset);
            if (_avatarField != null) _avatarField.objectType = typeof(Avatar);
            if (_detectorModelField != null) _detectorModelField.objectType = typeof(ModelAsset);
            if (_landmarkModelField != null) _landmarkModelField.objectType = typeof(ModelAsset);
            if (_anchorsField != null) _anchorsField.objectType = typeof(TextAsset);
            if (_objectDetectorField != null) _objectDetectorField.objectType = typeof(ModelAsset);
            if (_computeShaderField != null) _computeShaderField.objectType = typeof(ComputeShader);
        }

        void SetupCallbacks()
        {
            _extractButton?.RegisterCallback<ClickEvent>(_ => OnExtractClicked());
            _cancelButton?.RegisterCallback<ClickEvent>(_ => OnCancelClicked());
            _openFolderButton?.RegisterCallback<ClickEvent>(_ => OnOpenFolderClicked());

            _qualitySlider?.RegisterValueChangedCallback(evt =>
            {
                UpdateQualityHint(evt.newValue);
            });
        }

        void SetupDragAndDrop()
        {
            rootVisualElement.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
                {
                    string path = DragAndDrop.paths[0];
                    if (IsVideoFile(path))
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                }
            });

            rootVisualElement.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
                {
                    string path = DragAndDrop.paths[0];
                    if (IsVideoFile(path) && _videoField != null)
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
                        if (asset != null)
                            _videoField.value = asset;
                    }
                }
            });
        }

        void OnExtractClicked()
        {
            if (_isProcessing) return;

            // Validate inputs
            var videoAsset = _videoField?.value;
            if (videoAsset == null)
            {
                EditorUtility.DisplayDialog("Motion Extractor", "Please select a video file.", "OK");
                return;
            }

            var detectorModel = _detectorModelField?.value as ModelAsset;
            var landmarkModel = _landmarkModelField?.value as ModelAsset;
            var anchors = _anchorsField?.value as TextAsset;
            var computeShader = _computeShaderField?.value as ComputeShader;

            if (detectorModel == null || landmarkModel == null || anchors == null || computeShader == null)
            {
                EditorUtility.DisplayDialog("Motion Extractor",
                    "Please assign all required model assets:\n" +
                    "- Detector Model\n- Landmark Model\n- Anchors CSV\n- Image Transform Shader",
                    "OK");
                return;
            }

            string videoPath = AssetDatabase.GetAssetPath(videoAsset);
            string fullVideoPath = System.IO.Path.GetFullPath(videoPath);

            SavePreferences();
            StartExtraction(fullVideoPath, detectorModel, landmarkModel, anchors, computeShader);
        }

        void StartExtraction(
            string videoPath, ModelAsset detector, ModelAsset landmark,
            TextAsset anchors, ComputeShader computeShader)
        {
            _isProcessing = true;
            _generatedFiles.Clear();
            ShowProgress(true);
            ShowResults(false);

            int frameSkip = _qualitySlider?.value ?? 1;
            var settings = new VideoPoseExtractor.Settings
            {
                FrameSkip = frameSkip,
                ScoreThreshold = 0.5f,
                BackendType = BackendType.GPUCompute
            };

            try
            {
                _extractor = new VideoPoseExtractor(detector, landmark, anchors, computeShader, settings);
                var result = _extractor.Extract(videoPath, frameSkip);

                int mode = _modeGroup?.value ?? 2;

                // Generate Humanoid AnimationClip (if person mode)
                if (mode == 0 || mode == 2)
                {
                    GenerateHumanoidClip(result);
                }

                ShowResults(true);
                UpdateResultsList();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Motion Extractor",
                    $"Extraction failed:\n{e.Message}", "OK");
                Debug.LogException(e);
            }
            finally
            {
                _extractor?.Dispose();
                _extractor = null;
                _isProcessing = false;
                ShowProgress(false);
            }
        }

        void GenerateHumanoidClip(VideoPoseExtractor.ExtractionResult result)
        {
            string outputPath = _outputPathField?.value ?? "Assets/Animations";
            float fps = _fpsField?.value ?? 30;
            bool rootMotion = _rootMotionToggle?.value ?? true;
            bool smoothing = _smoothingToggle?.value ?? true;

            var clipSettings = new HumanoidClipGenerator.Settings
            {
                Fps = fps,
                IncludeRootMotion = rootMotion,
                CenterOnHips = !rootMotion,
                ApplySmoothing = smoothing,
                SmoothMinCutoff = 1.0f,
                SmoothBeta = 0.5f
            };

            string clipName = System.IO.Path.GetFileNameWithoutExtension(
                AssetDatabase.GetAssetPath(_videoField?.value));

            var clip = HumanoidClipGenerator.Generate(result.Frames, clipName, clipSettings);
            if (clip != null)
            {
                // Ensure output folder exists
                if (!AssetDatabase.IsValidFolder(outputPath))
                {
                    var parts = outputPath.Split('/');
                    string current = parts[0];
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string next = current + "/" + parts[i];
                        if (!AssetDatabase.IsValidFolder(next))
                            AssetDatabase.CreateFolder(current, parts[i]);
                        current = next;
                    }
                }

                string assetPath = $"{outputPath}/{clipName}.anim";
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                HumanoidClipGenerator.SaveClip(clip, assetPath);
                _generatedFiles.Add(assetPath);
            }
        }

        void OnCancelClicked()
        {
            _extractor?.Cancel();
        }

        void OnOpenFolderClicked()
        {
            string path = _outputPathField?.value ?? "Assets/Animations";
            EditorUtility.RevealInFinder(path);
        }

        void ShowProgress(bool show)
        {
            if (_progressSection != null)
                _progressSection.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (_extractButton != null)
                _extractButton.SetEnabled(!show);
        }

        void ShowResults(bool show)
        {
            if (_resultsSection != null)
                _resultsSection.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void UpdateResultsList()
        {
            if (_resultsList == null) return;
            _resultsList.Clear();

            foreach (var path in _generatedFiles)
            {
                var item = new VisualElement();
                item.AddToClassList("result-item");

                var icon = new Label("✅");
                icon.AddToClassList("result-icon");
                item.Add(icon);

                var name = new Label(System.IO.Path.GetFileName(path));
                name.AddToClassList("result-name");
                item.Add(name);

                bool isHumanoid = path.Contains("Object_") == false;
                var typeLabel = new Label(isHumanoid ? "Humanoid" : "Transform");
                typeLabel.AddToClassList("result-type");
                item.Add(typeLabel);

                _resultsList.Add(item);
            }
        }

        void UpdateQualityHint(int frameSkip)
        {
            if (_qualityHint == null) return;
            _qualityHint.text = frameSkip switch
            {
                1 => "Process every frame (highest quality)",
                2 => "Process every 2nd frame (balanced)",
                <= 5 => $"Process every {frameSkip}th frame (faster)",
                _ => $"Process every {frameSkip}th frame (fastest)"
            };
        }

        void LoadPreferences()
        {
            if (_outputPathField != null)
                _outputPathField.value = EditorPrefs.GetString(PrefKeyOutputPath, "Assets/Animations");
            if (_fpsField != null)
                _fpsField.value = EditorPrefs.GetInt(PrefKeyFps, 30);
            if (_qualitySlider != null)
                _qualitySlider.value = EditorPrefs.GetInt(PrefKeyFrameSkip, 1);
            if (_modeGroup != null)
                _modeGroup.value = EditorPrefs.GetInt(PrefKeyMode, 2);
            if (_rootMotionToggle != null)
                _rootMotionToggle.value = EditorPrefs.GetBool(PrefKeyRootMotion, true);
            if (_footLockToggle != null)
                _footLockToggle.value = EditorPrefs.GetBool(PrefKeyFootLock, true);
            if (_smoothingToggle != null)
                _smoothingToggle.value = EditorPrefs.GetBool(PrefKeySmoothing, true);
        }

        void SavePreferences()
        {
            if (_outputPathField != null)
                EditorPrefs.SetString(PrefKeyOutputPath, _outputPathField.value);
            if (_fpsField != null)
                EditorPrefs.SetInt(PrefKeyFps, _fpsField.value);
            if (_qualitySlider != null)
                EditorPrefs.SetInt(PrefKeyFrameSkip, _qualitySlider.value);
            if (_modeGroup != null)
                EditorPrefs.SetInt(PrefKeyMode, _modeGroup.value);
            if (_rootMotionToggle != null)
                EditorPrefs.SetBool(PrefKeyRootMotion, _rootMotionToggle.value);
            if (_footLockToggle != null)
                EditorPrefs.SetBool(PrefKeyFootLock, _footLockToggle.value);
            if (_smoothingToggle != null)
                EditorPrefs.SetBool(PrefKeySmoothing, _smoothingToggle.value);
        }

        static string FindAssetPath<T>(string name) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("MotionExtractor"))
                    return path;
            }
            return null;
        }

        static bool IsVideoFile(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".webm" || ext == ".mkv";
        }

        void OnDisable()
        {
            _extractor?.Dispose();
        }
    }
}
