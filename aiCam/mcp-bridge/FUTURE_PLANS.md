# MCP Bridge - Future Development Plans

## Current Status (v2.0)

mcp-bridgeは現在、Unity MCP Serverパッケージ内のテンプレートとして配布されています。

**配置場所**: `UnityMCP/mcp-bridge-template/`

**機能**:
- Claude Code ↔ Unity Editor間の汎用プロキシ
- 接続監視（10秒ごとのヘルスチェック）
- 自動再接続
- ユーザーフレンドリーなエラーメッセージ

## Repository Separation Policy

### 分離するタイミング

以下のいずれかの条件を満たす場合、mcp-bridgeを別リポジトリに分離する：

1. **Node.js側で新機能を追加する必要がある場合**
   - 例: リクエストキャッシュ機能
   - 例: 複数Unity Editorへの同時接続サポート
   - 例: リクエストロギング・デバッグ機能
   - 例: パフォーマンスモニタリング

2. **mcp-bridge独自のバージョン管理が必要になった場合**
   - Unity MCP Serverとは独立したリリースサイクル
   - mcp-bridge側のバグフィックスのみのリリース

3. **設定ファイルやカスタマイズが必要になった場合**
   - `config.json`でタイムアウト値を設定
   - ログレベルの調整
   - カスタムミドルウェアの追加

4. **npm packageとして公開する場合**
   - `npm install -g @dsgarage/unity-mcp-bridge`
   - より簡単なインストール方法の提供

### 分離しないケース

以下の場合は現在の構成（テンプレートとして同梱）を維持：

- ✅ Unity側のAPI追加・変更のみ（mcp-bridgeは汎用プロキシなので変更不要）
- ✅ ドキュメントの更新のみ
- ✅ 軽微なバグフィックス（メッセージ文言の修正など）

## Proposed Repository Structure (分離後)

### Option 1: 独立リポジトリ

```
dsgarage/unity-mcp-bridge
├── src/
│   └── index.js
├── package.json
├── README.md
├── CHANGELOG.md
└── .github/
    └── workflows/
        └── npm-publish.yml
```

**メリット**:
- 完全に独立したバージョン管理
- npm packageとして公開可能
- CI/CDパイプラインの設定が容易

### Option 2: Monorepo（同じリポジトリ内で分離）

```
dsgarage/CC2UniMCP
├── packages/
│   ├── unity-mcp-server/      # Unityパッケージ
│   │   ├── Runtime/
│   │   ├── Editor/
│   │   └── package.json
│   └── mcp-bridge/             # Node.jsサーバー
│       ├── src/
│       ├── package.json
│       └── README.md
└── README.md
```

**メリット**:
- 関連コードが一箇所に集約
- 両方を同時に更新する場合に便利
- Issue管理が一元化

## Migration Plan (分離時の手順)

1. **新リポジトリの作成** (Option 1の場合)
   ```bash
   gh repo create dsgarage/unity-mcp-bridge --public
   ```

2. **mcp-bridge-templateの移動**
   ```bash
   # 履歴を保持して移動
   git subtree split --prefix=mcp-bridge-template -b mcp-bridge-branch
   ```

3. **Unity MCP ServerのREADME更新**
   - mcp-bridgeの新しいインストール方法を記載
   - 旧テンプレートは非推奨として残す（後方互換性のため）

4. **npm package公開** (Option 1の場合)
   ```bash
   npm publish --access public
   ```

5. **ユーザーへの移行ガイド作成**
   - 既存ユーザー向けのアップグレード手順
   - 新規ユーザー向けのインストール手順

## Current Recommendation

**現状は分離しない方針で継続**

理由:
- mcp-bridgeは汎用プロキシとして設計されており、Unity側のAPI変更で更新不要
- 現在の機能（接続監視、自動再接続）で十分
- テンプレートとして同梱する方がセットアップが簡単

**次のアクションが必要になったら分離を検討**:
- Node.js側で新機能の実装要求
- 複数プロジェクトでの共有ニーズ
- より高度なカスタマイズ要求

## Decision Log

| Date | Decision | Reason |
|------|----------|--------|
| 2025-11-08 | テンプレートとして同梱 | セットアップの簡便性、汎用プロキシ設計 |
| 2025-11-08 | 接続監視機能をNode.js側に実装 | 責任の分離、柔軟性 |
| TBD | 分離を検討 | 新機能実装の必要性が生じた場合 |

## Contact

この方針について質問や提案がある場合:
- GitHub Issues: https://github.com/dsgarage/CC2UniMCP/issues
- 担当者: @dsgarage

---

**最終更新**: 2025-11-08
**次回レビュー**: Node.js側で新機能実装が必要になった時点
