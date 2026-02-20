using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DSGarage.MotionExtractor.Editor.Pipeline
{
    /// <summary>
    /// Extracts frames from a video file using ffmpeg as an external process.
    /// Outputs frames as PNG images to a temp directory, then loads them as Texture2D.
    /// </summary>
    public class VideoFrameReader : IDisposable
    {
        /// <summary>Video metadata extracted by ffprobe.</summary>
        public struct VideoInfo
        {
            public float Duration;
            public float Fps;
            public int Width;
            public int Height;
            public int TotalFrames;
        }

        readonly string _videoPath;
        readonly string _tempDir;
        readonly int _frameSkip;

        bool _disposed;

        /// <summary>Detected video metadata.</summary>
        public VideoInfo Info { get; private set; }

        /// <summary>
        /// Create a VideoFrameReader for the given video file.
        /// </summary>
        /// <param name="videoPath">Absolute path to the video file (.mp4, .mov, .avi).</param>
        /// <param name="frameSkip">Process every Nth frame. 1 = all frames, 2 = every other, etc.</param>
        public VideoFrameReader(string videoPath, int frameSkip = 1)
        {
            if (!File.Exists(videoPath))
                throw new FileNotFoundException($"Video file not found: {videoPath}");

            _videoPath = videoPath;
            _frameSkip = Mathf.Max(1, frameSkip);
            _tempDir = Path.Combine(Path.GetTempPath(), $"MotionExtractor_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);

            Info = ProbeVideo();
        }

        /// <summary>
        /// Extract all frames from the video and yield them one by one.
        /// Caller is responsible for destroying each Texture2D after use.
        /// </summary>
        /// <param name="onProgress">Progress callback (0..1).</param>
        /// <returns>Enumerable of (frameIndex, timestamp, texture) tuples.</returns>
        public IEnumerable<(int frameIndex, float timestamp, Texture2D texture)> ExtractFrames(
            Action<float, string> onProgress = null)
        {
            string ffmpegPath = FindFfmpeg();
            if (ffmpegPath == null)
                throw new InvalidOperationException(
                    "ffmpeg not found. Install ffmpeg and ensure it is in PATH, " +
                    "or set the path via EditorPrefs key 'MotionExtractor.FfmpegPath'.");

            // Build ffmpeg command to extract frames as PNG
            string outputPattern = Path.Combine(_tempDir, "frame_%06d.png");

            // Build filter: select every Nth frame, then set presentation timestamp
            string vfFilter = _frameSkip > 1
                ? $"select=not(mod(n\\,{_frameSkip})),setpts=N/FRAME_RATE/TB"
                : "";

            var args = new List<string>
            {
                "-i", $"\"{_videoPath}\"",
                "-vsync", "0"
            };

            if (!string.IsNullOrEmpty(vfFilter))
            {
                args.Add("-vf");
                args.Add($"\"{vfFilter}\"");
            }

            args.Add($"\"{outputPattern}\"");

            string argString = string.Join(" ", args);

            onProgress?.Invoke(0f, "Extracting video frames with ffmpeg...");

            // Run ffmpeg
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = argString,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                // ffmpeg outputs progress to stderr
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Debug.LogError($"[VideoFrameReader] ffmpeg failed (exit code {process.ExitCode}):\n{stderr}");
                    throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}");
                }
            }

            // Load extracted frames
            var frameFiles = Directory.GetFiles(_tempDir, "frame_*.png");
            Array.Sort(frameFiles);

            int totalFrames = frameFiles.Length;
            float fps = Info.Fps > 0 ? Info.Fps : 30f;

            for (int i = 0; i < totalFrames; i++)
            {
                float progress = (float)(i + 1) / totalFrames;
                int originalFrameIndex = i * _frameSkip;
                float timestamp = originalFrameIndex / fps;

                onProgress?.Invoke(progress, $"Loading frame {i + 1}/{totalFrames}...");

                // Load PNG as Texture2D
                byte[] pngData = File.ReadAllBytes(frameFiles[i]);
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                texture.LoadImage(pngData);

                yield return (originalFrameIndex, timestamp, texture);

                // Delete the file after loading to save disk space
                try { File.Delete(frameFiles[i]); }
                catch { /* ignore cleanup errors */ }
            }
        }

        /// <summary>
        /// Probe the video file for metadata using ffprobe.
        /// </summary>
        VideoInfo ProbeVideo()
        {
            var info = new VideoInfo();
            string ffprobePath = FindFfprobe();

            if (ffprobePath == null)
            {
                Debug.LogWarning("[VideoFrameReader] ffprobe not found. Video metadata will be estimated.");
                return info;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v quiet -print_format json -show_format -show_streams \"{_videoPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                    info = ParseFfprobeOutput(output);
            }

            return info;
        }

        static VideoInfo ParseFfprobeOutput(string json)
        {
            var info = new VideoInfo();

            // Simple JSON parsing without external dependency
            // Look for video stream properties
            if (TryExtractJsonValue(json, "\"width\"", out string widthStr))
                int.TryParse(widthStr, out info.Width);

            if (TryExtractJsonValue(json, "\"height\"", out string heightStr))
                int.TryParse(heightStr, out info.Height);

            if (TryExtractJsonValue(json, "\"duration\"", out string durationStr))
                float.TryParse(durationStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out info.Duration);

            // Parse r_frame_rate (e.g., "30/1" or "30000/1001")
            if (TryExtractJsonValue(json, "\"r_frame_rate\"", out string fpsStr))
            {
                fpsStr = fpsStr.Trim('"');
                var parts = fpsStr.Split('/');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float num) &&
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float den) &&
                    den > 0)
                {
                    info.Fps = num / den;
                }
            }

            if (TryExtractJsonValue(json, "\"nb_frames\"", out string frameCountStr))
            {
                frameCountStr = frameCountStr.Trim('"');
                int.TryParse(frameCountStr, out info.TotalFrames);
            }

            // Estimate total frames if not available
            if (info.TotalFrames <= 0 && info.Duration > 0 && info.Fps > 0)
                info.TotalFrames = Mathf.RoundToInt(info.Duration * info.Fps);

            return info;
        }

        static bool TryExtractJsonValue(string json, string key, out string value)
        {
            value = null;
            int keyIdx = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIdx < 0) return false;

            int colonIdx = json.IndexOf(':', keyIdx + key.Length);
            if (colonIdx < 0) return false;

            int start = colonIdx + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t'))
                start++;

            if (start >= json.Length) return false;

            // Handle quoted values
            if (json[start] == '"')
            {
                int end = json.IndexOf('"', start + 1);
                if (end < 0) return false;
                value = json.Substring(start + 1, end - start - 1);
                return true;
            }

            // Handle numeric values
            int numEnd = start;
            while (numEnd < json.Length && json[numEnd] != ',' && json[numEnd] != '}' &&
                   json[numEnd] != '\n' && json[numEnd] != '\r')
                numEnd++;

            value = json.Substring(start, numEnd - start).Trim();
            return !string.IsNullOrEmpty(value);
        }

        /// <summary>Find ffmpeg executable path.</summary>
        public static string FindFfmpeg()
        {
            // Check EditorPrefs first
            string prefsPath = EditorPrefs.GetString("MotionExtractor.FfmpegPath", "");
            if (!string.IsNullOrEmpty(prefsPath) && File.Exists(prefsPath))
                return prefsPath;

            // Try common locations
            return FindExecutable("ffmpeg");
        }

        /// <summary>Find ffprobe executable path.</summary>
        static string FindFfprobe()
        {
            string prefsPath = EditorPrefs.GetString("MotionExtractor.FfprobePath", "");
            if (!string.IsNullOrEmpty(prefsPath) && File.Exists(prefsPath))
                return prefsPath;

            return FindExecutable("ffprobe");
        }

        /// <summary>Find an executable in PATH.</summary>
        static string FindExecutable(string name)
        {
            try
            {
                string shell = Application.platform == RuntimePlatform.WindowsEditor ? "cmd" : "/bin/sh";
                string args = Application.platform == RuntimePlatform.WindowsEditor
                    ? $"/c where {name}"
                    : $"-c \"which {name}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    string result = process.StandardOutput.ReadLine();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(result) && File.Exists(result.Trim()))
                        return result.Trim();
                }
            }
            catch
            {
                // Silently fail
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Clean up temp directory
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VideoFrameReader] Failed to clean temp dir: {e.Message}");
            }
        }
    }
}
