# Motion Extractor Documentation

## Quick Start

1. Install the package via UPM (Git URL or local path)
2. Download BlazePose ONNX models from [HuggingFace](https://huggingface.co/unity/sentis-blaze-pose)
3. Open **Window > Motion Extractor > Motion Extractor**
4. Assign model assets (Detector, Landmark, Anchors CSV, Compute Shader)
5. Select a video file and click **Extract Motion**

## Requirements

- Unity 2022.3 LTS or later
- ffmpeg installed and available in PATH
- Sentis (com.unity.ai.inference) 2.4.1+

## Architecture

```
Video File (.mp4/.mov/.avi)
    |
    v
[VideoFrameReader] --- ffmpeg frame extraction
    |
    v
[PoseEstimator] --- Sentis + BlazePose ONNX
    |                 (33 keypoints per person)
    v
[ObjectDetector] --- Sentis + RF-DETR ONNX
    |                 (COCO 80 class objects)
    v
[ByteTrackEngine] --- Multi-object tracking
    |                   (Hungarian + Kalman filter)
    v
[CoordinateConverter] --- BlazePose -> Unity coords
    |
[SmoothingFilter] --- One Euro Filter
    |
    v
[HumanoidClipGenerator] --- Skeleton -> .anim (Humanoid)
[TrajectoryClipGenerator] --- BBox -> .anim (Transform)
```

## API Reference

### PoseEstimator

```csharp
var estimator = new PoseEstimator(
    detectorAsset, landmarkAsset, anchorsCSV,
    imageTransformShader, BackendType.GPUCompute);
PoseResult result = estimator.Estimate(texture2D);
```

### VideoPoseExtractor

```csharp
var extractor = new VideoPoseExtractor(
    detectorAsset, landmarkAsset, anchorsCSV,
    imageTransformShader, settings);
var result = extractor.Extract(videoPath, frameSkip: 2);
```

### HumanoidClipGenerator

```csharp
var clip = HumanoidClipGenerator.Generate(frames, "MyClip", settings);
HumanoidClipGenerator.SaveClip(clip, "Assets/Animations/MyClip.anim");
```

### ByteTrackEngine

```csharp
var tracker = new ByteTrackEngine(ByteTrackEngine.Config.Default);
var activeTracks = tracker.Update(detections);
```

## License

MIT License - see LICENSE.md
