# Project Rules

## Git Flow Branching Strategy

このプロジェクトはGit Flowワークフローを採用しています。

### ブランチ構成
- `main` - リリースブランチ（本番環境）
- `develop` - 開発統合ブランチ
- `feature/*` - 新機能開発ブランチ（developから分岐）
- `release/*` - リリース準備ブランチ（developから分岐）
- `hotfix/*` - 緊急修正ブランチ（mainから分岐）

### 禁止事項
- **`main`ブランチへの直接コミットは禁止**
- **`develop`ブランチへの直接コミットは禁止**
- 必ずfeature/release/hotfixブランチを作成し、マージで統合すること

### ワークフロー
1. 新機能: `develop` → `feature/xxx` → (作業) → `develop`にマージ
2. リリース: `develop` → `release/x.x.x` → (準備) → `main`と`develop`にマージ
3. 緊急修正: `main` → `hotfix/xxx` → (修正) → `main`と`develop`にマージ
