# Motion Extractor

[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com/)
[![Sentis 2.4.1+](https://img.shields.io/badge/Sentis-2.4.1%2B-blue)](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.4/manual/index.html)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)

**動画ファイルから骨格アニメーションとオブジェクト軌跡を抽出し、Unity の Humanoid AnimationClip を直接生成するエディタ拡張パッケージ**

Extract skeletal animation and object trajectories from video files and generate Humanoid AnimationClips directly in the Unity Editor.

---

## Features

- **BlazePose 姿勢推定** - Sentis (ONNX) による 33 キーポイントのリアルタイム骨格検出
- **RF-DETR オブジェクト検出** - COCO 80 クラスの物体検出
- **ByteTrack トラッキング** - ハンガリアン法 + カルマンフィルタによる ID 追跡
- **Humanoid AnimationClip 生成** - ボーン回転・ルートモーション付き `.anim` ファイル出力
- **オブジェクト軌跡 AnimationClip** - バウンディングボックスから Transform アニメーション生成
- **One Euro Filter** - 適応型ローパスフィルタによるジッタ除去
- **足ロック処理** - 接地検出による足滑り軽減
- **キーフレーム最適化** - Ramer-Douglas-Peucker 法によるカーブ簡略化
- **多言語 UI** - 英語・日本語対応の EditorWindow (UI Toolkit)

## Architecture

```
Video File (.mp4/.mov/.avi)
    │
    ▼
┌─────────────────┐     ┌─────────────────────┐
│ VideoFrameReader │     │ ffmpeg frame extract │
└────────┬────────┘     └─────────────────────┘
         │ Texture2D
    ┌────┴────┐
    ▼         ▼
┌────────┐ ┌──────────────┐
│ Pose   │ │ Object       │
│Estimator│ │ Detector     │
│(BlazePose)│ │(RF-DETR)     │
└───┬────┘ └──────┬───────┘
    │              │
    ▼              ▼
┌──────────┐ ┌──────────────┐
│Coordinate│ │ ByteTrack    │
│Converter │ │ Engine       │
└───┬──────┘ └──────┬───────┘
    │              │
    ▼              │
┌──────────┐       │
│Smoothing │       │
│Filter    │       │
└───┬──────┘       │
    │              │
    ▼              ▼
┌──────────────┐ ┌──────────────────┐
│HumanoidClip  │ │TrajectoryClip    │
│Generator     │ │Generator         │
└──────┬───────┘ └────────┬─────────┘
       │                  │
       ▼                  ▼
  Humanoid .anim    Transform .anim
```

> 詳細なアーキテクチャ図は [Wiki](https://github.com/dsgarage/MotionExtractor/wiki) を参照してください。

## Requirements

| 依存関係 | バージョン |
|----------|-----------|
| Unity | 2022.3 LTS 以降 |
| Sentis (`com.unity.ai.inference`) | 2.4.1+ |
| Mathematics (`com.unity.mathematics`) | 1.3.1+ |
| Collections (`com.unity.collections`) | 2.4.0+ |
| Burst (`com.unity.burst`) | 1.8.12+ |
| ffmpeg | PATH に設定済みであること |

### ONNX モデル

以下のモデルファイルを別途ダウンロードし、Unity プロジェクトにインポートしてください：

- `blazepose_detector.onnx` - 姿勢検出モデル
- `blazepose_landmark.onnx` - ランドマーク推定モデル
- `anchors.csv` - 検出アンカー座標 (2254 個)
- RF-DETR モデル (オブジェクト検出使用時)

> BlazePose モデルは [HuggingFace](https://huggingface.co/unity/sentis-blaze-pose) から取得可能です。

## Installation

### Git URL (UPM)

1. **Window > Package Manager** を開く
2. **+** > **Add package from git URL...** をクリック
3. 以下を入力:
   ```
   https://github.com/dsgarage/MotionExtractor.git
   ```

### ローカルパス

1. このリポジトリをクローン
2. **Window > Package Manager** を開く
3. **+** > **Add package from disk...** をクリック
4. クローンしたディレクトリの `package.json` を選択

## Quick Start

1. パッケージをインストール
2. BlazePose ONNX モデルをダウンロードしてプロジェクトにインポート
3. **Window > Motion Extractor > Motion Extractor** を開く
4. Model Assets セクションでモデルファイルを割り当て：
   - Detector → `blazepose_detector.onnx`
   - Landmark → `blazepose_landmark.onnx`
   - Anchors → `anchors.csv`
   - Compute Shader → `ImageTransform.compute`
5. 動画ファイルを選択（またはドラッグ & ドロップ）
6. **Extract Motion** をクリック

## API Reference

### PoseEstimator

```csharp
var estimator = new PoseEstimator(
    detectorAsset, landmarkAsset, anchorsCSV,
    imageTransformShader, BackendType.GPUCompute);

PoseResult result = estimator.Estimate(texture2D);
// result.Keypoints[0..32] - 33 keypoints with x, y, z, visibility, presence
```

### VideoPoseExtractor

```csharp
var extractor = new VideoPoseExtractor(
    detectorAsset, landmarkAsset, anchorsCSV,
    imageTransformShader, settings);

var result = extractor.Extract(videoPath, frameSkip: 2);
// result.Frames - List<PoseFrame>
```

### HumanoidClipGenerator

```csharp
var settings = new HumanoidClipGenerator.Settings { Fps = 30 };
var clip = HumanoidClipGenerator.Generate(frames, "MyClip", settings);
HumanoidClipGenerator.SaveClip(clip, "Assets/Animations/MyClip.anim");
```

### ByteTrackEngine

```csharp
var tracker = new ByteTrackEngine(ByteTrackEngine.Config.Default);
var activeTracks = tracker.Update(detections);
// activeTracks - List<Track> with persistent IDs
```

> 詳細な API リファレンスは [Wiki - API Reference](https://github.com/dsgarage/MotionExtractor/wiki/API-Reference) を参照してください。

## Project Structure

```
├── Editor/
│   ├── DSGarage.MotionExtractor.Editor.asmdef
│   ├── MotionExtractorWindow.cs        # メイン EditorWindow (UI Toolkit)
│   ├── MotionExtractorSettings.cs      # Localization + InputValidator
│   ├── PoseEstimatorTestWindow.cs      # PoC テストウィンドウ
│   ├── Export/
│   │   ├── HumanoidClipGenerator.cs    # Humanoid AnimationClip 生成
│   │   ├── FootLockProcessor.cs        # 足ロック処理
│   │   └── TrajectoryClipGenerator.cs  # オブジェクト軌跡 AnimationClip
│   ├── Localization/
│   │   ├── en.json                     # 英語
│   │   └── ja.json                     # 日本語
│   ├── Pipeline/
│   │   ├── BlazeUtils.cs               # BlazePose ユーティリティ
│   │   ├── CoordinateConverter.cs      # 座標変換
│   │   ├── ImageTransform.compute      # GPU 前処理シェーダー
│   │   ├── KeyframeOptimizer.cs        # キーフレーム最適化
│   │   ├── ObjectDetector.cs           # RF-DETR オブジェクト検出
│   │   ├── PoseData.cs                 # データ構造体
│   │   ├── PoseEstimator.cs            # BlazePose 推論エンジン
│   │   ├── SmoothingFilter.cs          # One Euro Filter
│   │   ├── VideoFrameReader.cs         # ffmpeg フレーム抽出
│   │   └── VideoPoseExtractor.cs       # Video→Pose オーケストレータ
│   ├── Tracking/
│   │   ├── ByteTrackEngine.cs          # ByteTrack トラッカー
│   │   ├── HungarianAlgorithm.cs       # ハンガリアン法
│   │   ├── KalmanFilter.cs             # カルマンフィルタ
│   │   └── TrackState.cs               # トラック状態管理
│   └── UI/
│       ├── MotionExtractor.uxml        # UI レイアウト
│       └── MotionExtractor.uss         # スタイルシート
├── Runtime/
│   └── SkeletonDefinitions.cs          # BlazePose 定数・マッピング
├── Documentation~/
│   └── index.md
├── CHANGELOG.md
├── LICENSE.md
├── package.json
└── README.md
```

## Development

### Git Flow

このプロジェクトは Git Flow ワークフローを採用しています。

- `main` - リリースブランチ
- `develop` - 開発統合ブランチ
- `feature/*` - 機能開発ブランチ

`main` および `develop` ブランチへの直接コミットは禁止です。

### ビルド

Unity で直接開くか、UPM パッケージとしてインポートしてください。追加のビルドステップは不要です。

## References

- [BlazePose (MediaPipe)](https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker) - 姿勢推定モデル
- [ByteTrack (ECCV 2022)](https://arxiv.org/abs/2110.06864) - マルチオブジェクトトラッキング
- [One Euro Filter (CHI 2012)](https://cristal.univ-lille.fr/~casiez/1euro/) - 適応型スムージング
- [Ramer-Douglas-Peucker](https://en.wikipedia.org/wiki/Ramer%E2%80%93Douglas%E2%80%93Peucker_algorithm) - カーブ簡略化
- [Unity Sentis](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.4/manual/index.html) - ニューラルネットワーク推論

## License

MIT License - see [LICENSE.md](LICENSE.md)
