# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-02-20

### Added

- **Phase 1**: BlazePose pose estimation pipeline (Sentis + ONNX)
  - `PoseEstimator`: Two-stage detector (224x224) + landmark (256x256) inference
  - `BlazeUtils`: Anchor loading, FunctionalGraph post-processing, affine transforms
  - `PoseData`: Keypoint, PoseResult, PoseFrame data structures
  - `ImageTransform.compute`: GPU compute shader for NHWC tensor preprocessing
  - `SkeletonDefinitions`: BlazePose 33-keypoint constants and Humanoid bone mapping
  - `PoseEstimatorTestWindow`: PoC validation EditorWindow with skeleton overlay

- **Phase 2**: Video frame processing
  - `VideoFrameReader`: ffmpeg-based frame extraction with metadata probing
  - `VideoPoseExtractor`: Video-to-pose pipeline orchestrator with progress and cancellation

- **Phase 3**: ByteTrack multi-object tracking (C# implementation)
  - `ByteTrackEngine`: Two-stage association (high + low confidence matching)
  - `KalmanFilter`: 8D linear Kalman filter for bounding box prediction
  - `HungarianAlgorithm`: Munkres optimal assignment with IoU cost matrix
  - `TrackState`: Track lifecycle management

- **Phase 4**: RF-DETR object detection
  - `ObjectDetector`: DETR-style inference with NMS and class filtering
  - `CocoLabels`: COCO 80-class label mapping

- **Phase 5**: 2D-to-3D coordinate conversion
  - `CoordinateConverter`: BlazePose to Unity world space transformation
  - `SmoothingFilter`: One Euro Filter (1D, 3D, SkeletonSmoother variants)

- **Phase 6**: Humanoid AnimationClip generation
  - `HumanoidClipGenerator`: Skeleton-to-AnimationClip with bone rotations and root motion
  - `FootLockProcessor`: Ground contact detection and foot position locking

- **Phase 7**: Object trajectory AnimationClip generation
  - `TrajectoryClipGenerator`: Bounding box trajectories to Transform AnimationClips

- **Phase 8**: Main EditorWindow
  - `MotionExtractorWindow`: Full-featured UI Toolkit window (UXML + USS)
  - Drag-and-drop video input, settings persistence, progress display

- **Phase 9**: Optimization and quality improvements
  - `KeyframeOptimizer`: Ramer-Douglas-Peucker curve simplification
  - `Localization`: JSON-based i18n (English + Japanese)
  - `InputValidator`: Video format and dependency validation

### Changed

- Updated `package.json` with Sentis, Mathematics, Collections, Burst dependencies
- Bumped minimum Unity version to 2022.3 LTS
- Updated Editor asmdef with assembly references

## [0.1.0] - 2026-02-20

### Added

- Initial package structure
