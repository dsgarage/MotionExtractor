# Motion Extractor for Unity — 実装プラン

> Unity エディタ拡張として動画から骨格アニメーション＆オブジェクト軌跡を抽出し、
> Humanoid AnimationClip として直接生成するツール

**作成日**: 2026-02-20
**ステータス**: 計画段階
**販売先**: Unity Asset Store

---

## 1. 製品概要

### 1.1 コンセプト

動画ファイル（ダンス動画等）を Unity Editor 上にドラッグ&ドロップするだけで、
以下を自動生成するエディタ拡張ツール:

1. **人物のダンスモーション** → Humanoid AnimationClip
2. **オブジェクトの移動軌跡** → Transform AnimationClip

Python 不要。Unity Editor 内で全て完結。

### 1.2 ターゲットユーザー

| セグメント | ニーズ |
|---|---|
| インディーゲーム開発者 | 安価なモーションキャプチャ代替 |
| VTuber / アバター制作者 | ダンスモーションの量産 |
| 教育機関 | アニメーション授業教材 |
| プロトタイプ制作 | 素早いモーション調達 |

### 1.3 競合優位性

| 比較軸 | Motion Extractor | Plask Motion | RADiCAL |
|---|---|---|---|
| Unity 統合 | **エディタ内完結** | FBXエクスポート→手動インポート | 同左 |
| オブジェクト追跡 | **対応** | 非対応 | 非対応 |
| オフライン動作 | **完全ローカル** | クラウド必須 | クラウド必須 |
| 価格モデル | 買い切り | サブスク | サブスク |
| Python 依存 | **なし** | — | — |

---

## 2. 技術アーキテクチャ

### 2.1 全体構成

```
Unity Editor
│
├── MotionExtractorWindow.cs (EditorWindow)
│   ├── 動画入力（ドラッグ&ドロップ / ファイル選択）
│   ├── 設定パネル（追跡モード、品質、出力先）
│   ├── プログレス表示
│   └── プレビュー再生
│
├── Processing Pipeline (Editor/)
│   ├── VideoFrameReader      動画 → フレーム配列
│   ├── PoseEstimator          Sentis + BlazePose → 2D/3D 骨格
│   ├── ObjectDetector         Sentis + RF-DETR → BBox
│   ├── ByteTrackEngine        フレーム間 ID 追跡
│   ├── MotionBERTLifter       2D → 3D リフティング
│   ├── HumanoidClipGenerator  骨格 → Humanoid AnimationClip
│   └── TrajectoryClipGenerator 軌跡 → Transform AnimationClip
│
├── ONNX Models (StreamingAssets/ or Models/)
│   ├── blazepose_detector.onnx        (~5MB)
│   ├── blazepose_landmark_full.onnx   (~15MB)
│   ├── rfdetr_nano.onnx               (~30MB)
│   └── motionbert_lite.onnx           (~50MB)
│
└── Runtime/ (オプション: ランタイム利用可能な共有コード)
    └── SkeletonDefinitions.cs
```

### 2.2 処理パイプライン

```
動画ファイル (.mp4 / .mov / .avi)
    │
    ▼
[VideoFrameReader] ─── ffmpeg or Unity VideoPlayer API
    │                   フレーム配列 (Texture2D[])
    ▼
[PoseEstimator] ─── Sentis + BlazePose ONNX
    │                (224x224 検出 → 256x256 ランドマーク)
    │                33関節 × (x, y, z, visibility)
    ▼
[ObjectDetector] ─── Sentis + RF-DETR ONNX
    │                 BBox (x1, y1, x2, y2, class, conf)
    ▼
[ByteTrackEngine] ─── C# 実装
    │                   人物ID追跡 + 物体ID追跡
    │                   IoU ベースのハンガリアンマッチング
    ▼
[MotionBERTLifter] ─── Sentis + MotionBERT ONNX (オプション)
    │                    2Dキーポイント列 → 3Dキーポイント列
    │                    ※ BlazePose 3D出力で十分な場合はスキップ
    ▼
[HumanoidClipGenerator]
    │   BlazePose 33関節 → Unity Humanoid ボーンマッピング
    │   関節角度計算（逆運動学 or 直接回転）
    │   AnimationClip 生成 (HumanPose API)
    ▼
[TrajectoryClipGenerator]
    │   BBox中心座標 → ワールド空間座標変換
    │   AnimationCurve → AnimationClip 生成
    ▼
出力: .anim ファイル (Assets/ 配下)
```

### 2.3 BlazePose 33関節 → Humanoid マッピング

```
BlazePose Index    関節名               Unity Humanoid Bone
─────────────────────────────────────────────────────────
0                  Nose                 Head (参考)
11                 Left Shoulder        LeftUpperArm
12                 Right Shoulder       RightUpperArm
13                 Left Elbow           LeftLowerArm
14                 Right Elbow          RightLowerArm
15                 Left Wrist           LeftHand
16                 Right Wrist          RightHand
23                 Left Hip             LeftUpperLeg
24                 Right Hip            RightUpperLeg
25                 Left Knee            LeftLowerLeg
26                 Right Knee           RightLowerLeg
27                 Left Ankle           LeftFoot
28                 Right Ankle          RightFoot
```

※ Spine, Chest, Neck は隣接関節の中点から推定

---

## 3. ライセンス監査

### 3.1 使用ライブラリと商用利用可否

| ライブラリ | ライセンス | 商用 | 備考 |
|---|---|---|---|
| Unity Sentis (Inference Engine) | Unity EULA | ✅ | Unity パッケージとして利用 |
| BlazePose ONNX | Apache 2.0 | ✅ | Google MediaPipe 由来 |
| RF-DETR | Apache 2.0 | ✅ | Roboflow 製 |
| MotionBERT | MIT | ✅ | ICCV 2023 |
| ByteTrack アルゴリズム | MIT | ✅ | 論文実装の C# 移植 |
| trackers (参考実装) | Apache 2.0 | ✅ | C# 移植時の参考 |

### 3.2 回避済みリスク

| リスク | 対策 |
|---|---|
| YOLOv8 AGPL 汚染 | **使用しない**（RF-DETR + MediaPipe に置換） |
| yt-dlp / 動画DL法的リスク | **使用しない**（ローカルファイル入力のみ） |
| OpenPose 非商用ライセンス | **使用しない**（BlazePose に置換） |

---

## 4. 開発フェーズ

### Phase 1: PoC — 単一フレーム骨格検出

**目標**: Sentis + BlazePose で1枚の画像から33関節を検出

#### タスク

- [ ] 1-1. Unity プロジェクト作成 (Unity 6 / 2022 LTS)
- [ ] 1-2. Sentis パッケージ (com.unity.ai.inference) インストール
- [ ] 1-3. BlazePose ONNX モデルをダウンロード・配置
  - ソース: https://huggingface.co/unity/sentis-blaze-pose
  - `blazepose_detector.onnx`
  - `blazepose_landmark_full.onnx`
- [ ] 1-4. `PoseEstimator.cs` 実装
  - 画像前処理（リサイズ、正規化）
  - Sentis Worker でモデル推論
  - 出力テンソルから33関節座標をパース
- [ ] 1-5. EditorWindow で画像読み込み → 関節オーバーレイ表示
- [ ] 1-6. 動作検証: サンプル画像で関節位置の正確性確認

#### 成果物
- `Editor/PoseEstimator.cs`
- `Editor/PoseEstimatorTest.cs` (EditorWindow)

#### 技術的な注意点
- Sentis は Editor でもランタイムでも動作可能
- BlazePose は 2段階: 検出モデル(224x224) → ランドマークモデル(256x256)
- GPU バックエンド推奨 (`BackendType.GPUCompute`)

---

### Phase 2: 動画フレーム処理 + 連続骨格検出

**目標**: 動画ファイルから全フレームの骨格を時系列で取得

#### タスク

- [ ] 2-1. `VideoFrameReader.cs` 実装
  - Unity の VideoPlayer API でフレーム抽出
  - 代替: ffmpeg プロセス呼び出し（より確実）
  - Texture2D 配列 or コールバック方式
- [ ] 2-2. フレーム間の骨格データを時系列配列に蓄積
  - データ構造: `List<PoseFrame>` (timestamp, keypoints[33])
- [ ] 2-3. EditorWindow にプログレスバー追加
  - `EditorUtility.DisplayProgressBar()` 使用
- [ ] 2-4. 非同期処理対応
  - `EditorCoroutine` または `Task` ベースで UI ブロック回避
- [ ] 2-5. フレームスキップオプション実装
  - 全フレーム / 2フレーム毎 / 5フレーム毎（速度vs精度トレードオフ）

#### 成果物
- `Editor/VideoFrameReader.cs`
- 動画→時系列骨格データの完全なパイプライン

#### 技術的な注意点
- Unity VideoPlayer は Editor モードでの連続フレーム取得にクセがある
- ffmpeg を StreamingAssets に同梱する方式の方が確実
  - ライセンス: LGPL（動的リンクなら商用OK）または GPL（同梱注意）
  - 代替: FFmpegOut のような MIT ライセンスラッパー
- メモリ管理: 全フレームを Texture2D で保持しない（逐次処理 + 解放）

---

### Phase 3: ByteTrack C# 実装

**目標**: フレーム間で人物・物体の ID を一貫して追跡

#### タスク

- [ ] 3-1. ByteTrack アルゴリズムの理解
  - 論文: "ByteTrack: Multi-Object Tracking by Associating Every Detection Box"
  - 参考実装: roboflow/trackers (Python, Apache 2.0)
- [ ] 3-2. ハンガリアンアルゴリズム C# 実装
  - IoU (Intersection over Union) 計算
  - コスト行列 → 最適マッチング
- [ ] 3-3. カルマンフィルタ C# 実装
  - 位置・速度の予測・更新
  - 遮蔽時のID保持
- [ ] 3-4. `ByteTrackEngine.cs` 実装
  - High-confidence / Low-confidence の2段階マッチング
  - Track の生成・更新・消滅管理
- [ ] 3-5. テスト
  - 既知の検出結果シーケンスで ID 追跡の正確性検証
  - 遮蔽→再出現時の ID 維持テスト

#### 成果物
- `Editor/Tracking/ByteTrackEngine.cs`
- `Editor/Tracking/KalmanFilter.cs`
- `Editor/Tracking/HungarianAlgorithm.cs`
- `Editor/Tracking/TrackState.cs`

#### 技術的な注意点
- ByteTrack の核心は「低スコア検出も2段目で使う」こと
- Python の `trackers` ライブラリのロジックを C# に忠実移植
- Span<T> や NativeArray を活用してGC圧力を軽減

---

### Phase 4: オブジェクト検出（RF-DETR）

**目標**: 動画内の物体（ボール、帽子、小道具等）を検出・追跡

#### タスク

- [ ] 4-1. RF-DETR ONNX モデル取得
  - RF-DETR Nano (軽量、エディタ用途に最適)
  - ONNX 変換が必要な場合は Python で事前変換
- [ ] 4-2. `ObjectDetector.cs` 実装
  - 前処理: リサイズ + 正規化
  - Sentis 推論
  - 後処理: NMS (Non-Maximum Suppression), クラス分類
- [ ] 4-3. Phase 3 の ByteTrackEngine と統合
  - 人物トラッカーとは別インスタンスで物体を追跡
  - クラスラベル付きの軌跡データ蓄積
- [ ] 4-4. COCO 80クラスのうち有用なクラスをフィルタ
  - デフォルト: person 以外の全クラス
  - ユーザー設定: 追跡対象クラスの選択UI

#### 成果物
- `Editor/ObjectDetector.cs`
- 物体の時系列軌跡データ

#### 技術的な注意点
- RF-DETR の ONNX エクスポートは公式対応済み
- Sentis 対応 opset バージョン (7-15) に注意
- 未対応オペレータがあれば代替モデル (SSD MobileNet 等) にフォールバック

---

### Phase 5: 2D → 3D リフティング

**目標**: 2D骨格データを3D空間の骨格データに変換

#### タスク

- [ ] 5-1. BlazePose 3D 出力の精度評価
  - BlazePose 自体が z 座標を出力する（33関節 × x,y,z）
  - 単眼正面動画での z 精度を実測
- [ ] 5-2a. BlazePose 3D で十分な場合 → そのまま使用
- [ ] 5-2b. 精度不足の場合 → MotionBERT ONNX 導入
  - MotionBERT モデルの ONNX 変換
  - Sentis での推論パイプライン構築
  - 入力: 2Dキーポイント時系列 (T, 17, 2)
  - 出力: 3Dキーポイント時系列 (T, 17, 3)
- [ ] 5-3. 座標系変換
  - BlazePose/MotionBERT 座標系 → Unity 座標系 (左手系, Y-up)
- [ ] 5-4. スムージングフィルタ
  - OneEuro フィルタ or ローパスフィルタで 3D 座標のジッター除去

#### 成果物
- `Editor/MotionBERTLifter.cs` (必要な場合)
- `Editor/CoordinateConverter.cs`
- `Editor/SmoothingFilter.cs`

#### 技術的な注意点
- BlazePose の z は「ヒップからの相対深度」で絶対値ではない
- ダンス動画（正面カメラ）なら BlazePose 3D で十分な可能性が高い
- MotionBERT は 17関節 (Human3.6M) なので BlazePose 33→17 のダウンサンプリングが必要

---

### Phase 6: Humanoid AnimationClip 生成

**目標**: 3D骨格データを Unity Humanoid 互換の AnimationClip に変換

#### タスク

- [ ] 6-1. BlazePose 33関節 → Humanoid ボーンマッピング定義
  - `SkeletonDefinitions.cs` に定数テーブルとして定義
  - 15〜20ボーンをマッピング（指は非対応）
- [ ] 6-2. 関節角度計算
  - 方式A: **HumanPose API** 使用（推奨）
    - `HumanPoseHandler` で muscles 配列に直接書き込み
    - Avatar からボーン構造を取得
  - 方式B: 直接クォータニオン計算
    - 親ボーンからの相対回転を計算
    - `Quaternion.LookRotation()` ベース
- [ ] 6-3. AnimationClip 生成
  - `AnimationClip` を new で作成
  - `AnimationCurve` にフレームごとの muscle 値をキーフレーム追加
  - `clip.SetCurve()` で全ボーンのカーブを設定
  - `clip.humanMotion = true` 設定
- [ ] 6-4. ルートモーション対応
  - ヒップ位置から RootT (位置) / RootQ (回転) を計算
  - オプション: ルートモーション有効/無効の切り替え
- [ ] 6-5. フットロック（足滑り防止）
  - 接地判定: 足首の y 速度が閾値以下 → 位置固定
  - IK ターゲットの自動設定

#### 成果物
- `Editor/HumanoidClipGenerator.cs`
- `Runtime/SkeletonDefinitions.cs`
- 出力: `.anim` ファイル (Humanoid AnimationClip)

#### 技術的な注意点
- HumanPose API の muscles 配列は 95 要素（各関節の DoF）
- **Avatar が必須**: ユーザーが対象の Humanoid Avatar を指定する必要あり
- AnimationClip の `humanMotion` フラグを true にしないと Humanoid として認識されない
- フレームレート: 元動画の FPS に合わせる（30fps 推奨）

---

### Phase 7: オブジェクト軌跡 AnimationClip 生成

**目標**: 追跡された物体の移動軌跡を Transform ベースの AnimationClip として生成

#### タスク

- [ ] 7-1. 2D 画面座標 → 3D ワールド座標変換
  - 方式A: 固定深度平面への投影（簡易）
  - 方式B: 人物との相対位置から推定
  - ユーザー設定: ワールド空間のスケール調整
- [ ] 7-2. `TrajectoryClipGenerator.cs` 実装
  - `AnimationCurve` に localPosition.x/y/z キーフレーム追加
  - 各追跡物体ごとに別 `.anim` ファイル生成
- [ ] 7-3. 回転推定（オプション）
  - BBox のアスペクト比変化から簡易回転推定
  - 精度は限定的（2Dのみなので）
- [ ] 7-4. クラスラベルの AnimationClip メタデータ埋め込み
  - `AnimationClip` の `AnimationEvent` にクラス名を記録

#### 成果物
- `Editor/TrajectoryClipGenerator.cs`
- 出力: `Object_{ID}_{ClassName}.anim` ファイル

---

### Phase 8: EditorWindow UI

**目標**: 直感的なUIでワンクリック操作を実現

#### タスク

- [ ] 8-1. メイン EditorWindow レイアウト設計
  ```
  ┌─ Motion Extractor ──────────────────────┐
  │                                          │
  │  ── 入力 ──                              │
  │  動画: [                    ] [選択]      │
  │  Avatar: [                  ] [選択]      │
  │                                          │
  │  ── 設定 ──                              │
  │  モード:  ○人物  ○物体  ●両方            │
  │  品質:    [━━━━●━━━] Balanced            │
  │  出力先:  Assets/Animations/             │
  │  FPS:     [30]                           │
  │                                          │
  │  [▶ Extract Motion]                      │
  │                                          │
  │  ── プログレス ──                         │
  │  [████████░░░░░░░] 53% - 骨格検出中...   │
  │                                          │
  │  ── 結果 ──                              │
  │  ✅ Dance_Person1.anim (Humanoid)        │
  │  ✅ Object_1_ball.anim (Transform)       │
  │  [プレビュー再生] [フォルダを開く]        │
  └──────────────────────────────────────────┘
  ```
- [ ] 8-2. UI Toolkit (UXML + USS) でモダンUI実装
  - レスポンシブレイアウト
  - ダーク/ライトテーマ対応
- [ ] 8-3. ドラッグ&ドロップ対応
  - Project ウィンドウから動画ファイルをドロップ
  - `DragAndDrop` API 使用
- [ ] 8-4. プレビュー機能
  - 処理済みフレームの骨格オーバーレイ表示
  - Scene ビューでの AnimationClip 再生プレビュー
- [ ] 8-5. 設定の永続化
  - `EditorPrefs` で前回の設定を保存・復元

#### 成果物
- `Editor/MotionExtractorWindow.cs`
- `Editor/UI/MotionExtractor.uxml`
- `Editor/UI/MotionExtractor.uss`

---

### Phase 9: 最適化・品質向上

**目標**: 製品品質まで仕上げる

#### タスク

- [ ] 9-1. パフォーマンス最適化
  - Sentis GPU バックエンド活用
  - バッチ推論（複数フレームを一括処理）
  - メモリプーリング（Texture2D の再利用）
- [ ] 9-2. アニメーション品質向上
  - キーフレーム削減（誤差ベースの間引き）
  - ベジェ補間でスムーズなカーブ生成
  - OneEuro フィルタ調整パラメータ公開
- [ ] 9-3. エラーハンドリング
  - 未対応動画フォーマットの検出・通知
  - 人物未検出時のフォールバック
  - Sentis モデル読み込みエラー時のガイダンス
- [ ] 9-4. undo 対応
  - `Undo.RegisterCreatedObjectUndo()` で生成物の取り消し対応
- [ ] 9-5. 多言語対応
  - 日本語 / 英語 UIテキスト
  - `Editor/Localization/` にリソースファイル

#### 成果物
- パフォーマンス改善されたパイプライン
- エラーハンドリング完備

---

### Phase 10: テスト・ドキュメント・リリース

**目標**: Asset Store 提出・販売開始

#### タスク

- [ ] 10-1. テスト
  - 単体テスト: 各モジュールの入出力検証
  - 統合テスト: 動画→AnimationClip の E2E テスト
  - 対応環境テスト: Windows / macOS / Unity 2022 LTS / Unity 6
  - サンプル動画での品質評価
- [ ] 10-2. ドキュメント
  - クイックスタートガイド
  - API リファレンス
  - トラブルシューティング
  - チュートリアル動画（Asset Store 用）
- [ ] 10-3. サンプルプロジェクト
  - サンプル動画 + 生成済み AnimationClip
  - デモシーン（Humanoid キャラ + アニメーション適用済み）
- [ ] 10-4. Asset Store 提出
  - パブリッシャーアカウント設定
  - アセットパッケージ作成 (.unitypackage)
  - スクリーンショット、説明文、キーアート準備
  - 審査提出
- [ ] 10-5. 販売ページ最適化
  - 比較動画 GIF（Before/After）
  - 機能一覧表
  - FAQ

#### 成果物
- `.unitypackage` (配布パッケージ)
- Asset Store 販売ページ

---

## 5. パッケージ構成（最終形）

```
com.dsgarage.motion-extractor/
│
├── package.json                          # UPM パッケージ定義
├── LICENSE.md                            # ライセンス
├── CHANGELOG.md                          # 変更履歴
├── README.md                             # 概要
│
├── Editor/
│   ├── MotionExtractorWindow.cs          # メイン EditorWindow
│   ├── MotionExtractorSettings.cs        # 設定データ管理
│   │
│   ├── Pipeline/
│   │   ├── VideoFrameReader.cs           # 動画→フレーム分解
│   │   ├── PoseEstimator.cs              # BlazePose 推論
│   │   ├── ObjectDetector.cs             # RF-DETR 推論
│   │   ├── MotionBERTLifter.cs           # 2D→3D（オプション）
│   │   ├── CoordinateConverter.cs        # 座標系変換
│   │   └── SmoothingFilter.cs            # OneEuro フィルタ
│   │
│   ├── Tracking/
│   │   ├── ByteTrackEngine.cs            # ByteTrack メイン
│   │   ├── KalmanFilter.cs               # カルマンフィルタ
│   │   ├── HungarianAlgorithm.cs         # ハンガリアン法
│   │   └── TrackState.cs                 # トラック状態管理
│   │
│   ├── Export/
│   │   ├── HumanoidClipGenerator.cs      # Humanoid AnimationClip
│   │   ├── TrajectoryClipGenerator.cs    # Transform AnimationClip
│   │   └── FootLockProcessor.cs          # フットロック処理
│   │
│   ├── UI/
│   │   ├── MotionExtractor.uxml          # UI レイアウト
│   │   ├── MotionExtractor.uss           # UI スタイル
│   │   └── Icons/                        # アイコンアセット
│   │
│   ├── Localization/
│   │   ├── en.json                       # 英語
│   │   └── ja.json                       # 日本語
│   │
│   └── Tests/
│       ├── PoseEstimatorTests.cs
│       ├── ByteTrackTests.cs
│       ├── ClipGeneratorTests.cs
│       └── TestData/
│
├── Runtime/
│   └── SkeletonDefinitions.cs            # 骨格定義（共有）
│
├── Models/
│   ├── blazepose_detector.onnx
│   ├── blazepose_landmark_full.onnx
│   ├── rfdetr_nano.onnx
│   └── motionbert_lite.onnx
│
├── Samples~/
│   ├── QuickStart/
│   │   ├── SampleVideo.mp4
│   │   ├── SampleScene.unity
│   │   └── README.md
│   └── AdvancedUsage/
│       └── ...
│
└── Documentation~/
    ├── index.md
    ├── quick-start.md
    ├── api-reference.md
    └── troubleshooting.md
```

---

## 6. 技術的な課題と対策

### 6.1 動画フレーム読み込み

| 課題 | 対策 |
|---|---|
| Unity VideoPlayer は Editor での連続フレーム取得が不安定 | ffmpeg を外部プロセスとして呼び出し、フレームを PNG/JPG で一時出力 |
| ffmpeg のライセンス (GPL/LGPL) | LGPL ビルドを使用（動的リンク相当）、または NativePlugin として分離 |
| 大容量動画のメモリ問題 | ストリーミング処理（全フレーム保持しない） |

### 6.2 Sentis モデル互換性

| 課題 | 対策 |
|---|---|
| ONNX opset バージョン不一致 | opset 7-15 に変換して提供 |
| 未対応オペレータ | 代替オペレータに置換 or カスタムレイヤー実装 |
| GPU バックエンド非対応環境 | CPU フォールバック自動切替 |

### 6.3 AnimationClip 品質

| 課題 | 対策 |
|---|---|
| 骨格のジッター | OneEuro フィルタ + ローパスフィルタ |
| 足の滑り | フットロック（接地検出 + 位置固定） |
| 不自然な関節角度 | 関節角度制限（人体の可動域クランプ） |
| フレーム間の不連続 | ベジェ補間 + スプライン平滑化 |

### 6.4 ByteTrack C# 移植

| 課題 | 対策 |
|---|---|
| NumPy/SciPy 依存の行列演算 | System.Numerics.Vectors or Unity.Mathematics 使用 |
| ハンガリアンアルゴリズム実装 | Munkres アルゴリズムの C# 実装（参考実装多数あり） |
| パフォーマンス | NativeArray + Burst Compiler で高速化 |

---

## 7. 販売プラン

### 7.1 価格設定

| エディション | 価格 | 内容 |
|---|---|---|
| **Basic** | $49.99 | 人物骨格→Humanoid AnimationClip |
| **Pro** | $99.99 | + オブジェクト追跡 + 複数人対応 + フットロック |
| **Enterprise** | 要問合せ | + ソースコードライセンス + サポート |

### 7.2 Unity Asset Store 情報

- **収益分配**: 70% (パブリッシャー) / 30% (Unity)
- **最低価格**: $4.99
- **手数料なし**: 出品料・月額料なし
- **審査**: 初回は数日〜数週間

### 7.3 段階リリース

| バージョン | 機能 |
|---|---|
| **v1.0** | 単一人物ダンス → Humanoid AnimationClip |
| **v1.5** | オブジェクト追跡 → Transform AnimationClip |
| **v2.0** | 複数人対応、フットロック、品質オプション |
| **v2.5** | リアルタイムカメラ入力 |
| **v3.0** | 表情（フェイシャル）検出、指トラッキング |

---

## 8. 依存関係

### 8.1 Unity パッケージ

```json
{
  "dependencies": {
    "com.unity.ai.inference": "2.4.1",
    "com.unity.mathematics": "1.3.1",
    "com.unity.collections": "2.4.0",
    "com.unity.burst": "1.8.12"
  }
}
```

### 8.2 対応環境

| 項目 | 要件 |
|---|---|
| Unity バージョン | 2022.3 LTS / Unity 6 以降 |
| OS | Windows 10+, macOS 12+ |
| GPU | Sentis GPU バックエンド対応 (推奨) |
| メモリ | 8GB+ (推奨 16GB) |

---

## 9. リスクと緩和策

| リスク | 影響 | 緩和策 |
|---|---|---|
| Sentis で BlazePose が動作しない | 致命的 | HuggingFace に Unity 公式変換済みモデルあり。PoC で早期検証 |
| RF-DETR の ONNX が Sentis 非互換 | Phase 4 遅延 | 代替: SSD MobileNet (TFLite→ONNX) |
| AnimationClip 品質が低い | 販売に影響 | フィルタリング強化 + ユーザー調整パラメータ公開 |
| Asset Store 審査不合格 | リリース遅延 | ドキュメント・サンプル充実、ガイドライン事前確認 |
| 競合製品の出現 | 売上減 | 先行者優位 + Unity 統合という差別化維持 |

---

## 10. 参考リソース

### モデル・論文

- [BlazePose (MediaPipe)](https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker) — Apache 2.0
- [Sentis BlazePose (HuggingFace)](https://huggingface.co/unity/sentis-blaze-pose) — Unity 公式変換済み
- [RF-DETR](https://blog.roboflow.com/rf-detr-object-detection-model/) — Apache 2.0
- [MotionBERT (ICCV 2023)](https://github.com/Walter0807/MotionBERT) — MIT
- [ByteTrack 論文](https://arxiv.org/abs/2110.06864) — アルゴリズム参考
- [roboflow/trackers](https://github.com/roboflow/trackers) — ByteTrack 参考実装 (Apache 2.0)

### Unity ドキュメント

- [Sentis (Inference Engine)](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.4/manual/index.html)
- [HumanPose API](https://docs.unity3d.com/ScriptReference/HumanPose.html)
- [AnimationClip](https://docs.unity3d.com/ScriptReference/AnimationClip.html)
- [EditorWindow](https://docs.unity3d.com/ScriptReference/EditorWindow.html)
- [Asset Store パブリッシャーガイド](https://assetstore.unity.com/publishing/publish-and-sell-assets)
