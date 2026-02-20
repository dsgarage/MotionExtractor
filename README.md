# MotionExtractor

MotionExtractor - Unity Editor Extension

## Overview

Unity向けのモーション抽出エディタ拡張パッケージです。Unity Package Manager (UPM) 形式で提供されます。

- **パッケージ名**: `jp.dsgarage.MotionExtractor`
- **バージョン**: 0.1.0
- **作者**: DSGarage
- **ライセンス**: MIT License

## Requirements

- Unity 2021.3 or later

## Installation

### Via Git URL (UPM)

1. Open **Window > Package Manager**
2. Click **+** > **Add package from git URL...**
3. Enter:
   ```
   https://github.com/dsgarage/MotionExtractor.git
   ```

### Via Local Path

1. Clone this repository
2. Open **Window > Package Manager**
3. Click **+** > **Add package from disk...**
4. Select the `package.json` in the cloned directory

## Project Structure

```
├── CHANGELOG.md        # 変更履歴（Keep a Changelog形式、Semantic Versioning準拠）
├── CLAUDE.md           # AI開発ルール（Git Flowブランチ戦略）
├── Documentation~/     # ドキュメント（index.md）
├── Editor/             # エディタ拡張スクリプト
├── LICENSE.md          # MITライセンス
├── package.json        # UPMパッケージ定義
├── README.md           # このファイル
├── Runtime/            # ランタイムスクリプト
├── Samples~/           # サンプル
└── Tests/              # テスト
```

## Git Flow Branching

このプロジェクトはGit Flowワークフローを採用しています。

- `main` - リリースブランチ
- `develop` - 開発統合ブランチ
- `feature/*` - 新機能開発
- `release/*` - リリース準備
- `hotfix/*` - 緊急修正

**`main`および`develop`ブランチへの直接コミットは禁止です。**

## Initial Files Summary (v0.1.0)

| ファイル | 内容 |
|---------|------|
| `README.md` | パッケージの概要、動作要件（Unity 2021.3+）、インストール手順（Git URL / ローカルパス） |
| `CHANGELOG.md` | Keep a Changelog形式。v0.1.0（2026-02-20）で初期パッケージ構造を追加 |
| `LICENSE.md` | MIT License（Copyright 2026 DSGarage） |
| `Documentation~/index.md` | Editor Toolsのドキュメントプレースホルダー |
| `package.json` | UPMパッケージ定義。名前: `jp.dsgarage.MotionExtractor`、キーワード: editor, motion, extractor |

## License

MIT License - see [LICENSE.md](LICENSE.md)
