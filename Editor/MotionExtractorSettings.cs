using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor
{
    /// <summary>
    /// Localization support for Motion Extractor UI.
    /// Loads string resources from JSON files in the Localization directory.
    /// </summary>
    public static class Localization
    {
        static Dictionary<string, string> _strings;
        static string _currentLanguage;

        /// <summary>Current language code (e.g., "en", "ja").</summary>
        public static string CurrentLanguage
        {
            get => _currentLanguage ?? DetectLanguage();
            set
            {
                _currentLanguage = value;
                _strings = null; // Force reload
                EditorPrefs.SetString("MotionExtractor.Language", value);
            }
        }

        /// <summary>
        /// Get a localized string by key.
        /// </summary>
        public static string Get(string key, params object[] args)
        {
            EnsureLoaded();
            if (_strings != null && _strings.TryGetValue(key, out string value))
            {
                return args.Length > 0 ? string.Format(value, args) : value;
            }
            return key; // Fallback to key name
        }

        static void EnsureLoaded()
        {
            if (_strings != null) return;

            string lang = CurrentLanguage;
            _strings = LoadLanguageFile(lang);

            // Fallback to English if the requested language file doesn't exist
            if (_strings == null && lang != "en")
                _strings = LoadLanguageFile("en");

            _strings ??= new Dictionary<string, string>();
        }

        static Dictionary<string, string> LoadLanguageFile(string lang)
        {
            // Find the localization file in the package
            string[] guids = AssetDatabase.FindAssets($"{lang} t:TextAsset",
                new[] { "Packages/jp.dsgarage.MotionExtractor/Editor/Localization" });

            // Also check in Assets (for development)
            if (guids.Length == 0)
                guids = AssetDatabase.FindAssets($"{lang} t:TextAsset");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("Localization") || !path.EndsWith($"{lang}.json"))
                    continue;

                var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (textAsset == null) continue;

                return ParseJson(textAsset.text);
            }

            return null;
        }

        /// <summary>Simple JSON object parser for flat key-value strings.</summary>
        static Dictionary<string, string> ParseJson(string json)
        {
            var dict = new Dictionary<string, string>();
            // Remove braces
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            var entries = json.Split(',');
            foreach (var entry in entries)
            {
                int colonIdx = entry.IndexOf(':');
                if (colonIdx < 0) continue;

                string key = entry.Substring(0, colonIdx).Trim().Trim('"');
                string value = entry.Substring(colonIdx + 1).Trim().Trim('"');

                // Unescape basic JSON escape sequences
                value = value.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");

                if (!string.IsNullOrEmpty(key))
                    dict[key] = value;
            }

            return dict;
        }

        static string DetectLanguage()
        {
            string saved = EditorPrefs.GetString("MotionExtractor.Language", "");
            if (!string.IsNullOrEmpty(saved))
                return saved;

            // Auto-detect from Unity Editor language
            var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return lang == "ja" ? "ja" : "en";
        }
    }

    /// <summary>
    /// Validation utilities for Motion Extractor inputs.
    /// </summary>
    public static class InputValidator
    {
        static readonly HashSet<string> SupportedVideoExtensions = new HashSet<string>
        {
            ".mp4", ".mov", ".avi", ".webm", ".mkv", ".flv", ".wmv", ".m4v"
        };

        /// <summary>Check if a file path points to a supported video format.</summary>
        public static bool IsVideoFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return SupportedVideoExtensions.Contains(ext);
        }

        /// <summary>Validate all inputs before extraction and return error message or null.</summary>
        public static string ValidateInputs(
            Object videoAsset,
            Object detectorModel,
            Object landmarkModel,
            Object anchors,
            Object computeShader)
        {
            if (videoAsset == null)
                return Localization.Get("error_no_video");

            string videoPath = AssetDatabase.GetAssetPath(videoAsset);
            if (!IsVideoFile(videoPath))
                return Localization.Get("error_unsupported_format", Path.GetExtension(videoPath));

            if (detectorModel == null || landmarkModel == null ||
                anchors == null || computeShader == null)
                return Localization.Get("error_no_models");

            // Check ffmpeg availability
            if (Pipeline.VideoFrameReader.FindFfmpeg() == null)
                return Localization.Get("error_no_ffmpeg");

            return null;
        }
    }
}
