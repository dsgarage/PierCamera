# FBXインポートシステム ドキュメント

このディレクトリには、Assimpを使用したFBXインポートシステムの包括的なドキュメントが含まれています。

## 📚 ドキュメント一覧

### [ARCHITECTURE.md](ARCHITECTURE.md)
FBXインポートの完全な設計書。以下の内容を含みます：

- **座標系変換の数学的基礎**: 線形代数による厳密な変換式
- **GlobalSettings検出**: FBXプロファイル自動判定アルゴリズム
- **4ステップパイプライン**: Transform階層、メッシュ、ボーン、SMR構築
- **スキニング数学**: 線形ブレンドスキニング（LBS）の完全な数式
- **トラブルシューティング**: よくある問題と解決方法

## 📊 図解（diagrams/）

全ての技術図はPNG形式で提供されています：

### 1. アーキテクチャ図
- **architecture_overview.png**: システム全体のフロー
- **4step_pipeline.png**: 4ステップパイプライン詳細

### 2. 座標系変換
- **coordinate_systems.png**: 各座標系の比較表
- **coordinate_conversion.png**: 変換プロセスの図解

### 3. プロファイル検出
- **fbx_profile_detection.png**: 自動判定フローチャート

### 4. スキニング数学
- **skinning_math.png**: LBS計算フロー
- **bindpose_calculation.png**: BindPose行列計算
- **boneweight_normalization.png**: BoneWeight正規化

## 🔧 図の再生成方法

DOTファイルからPNG画像を生成：

```bash
cd diagrams
dot -Tpng -Gdpi=150 <filename>.dot -o <filename>.png
```

全ての図を一括生成：

```bash
cd diagrams
for file in *.dot; do dot -Tpng -Gdpi=150 "$file" -o "${file%.dot}.png"; done
```

## 📖 読み方ガイド

### 初心者向け
1. ARCHITECTURE.md の「1. 概要と設計原則」から読む
2. 図解を見ながら全体像を把握
3. 「7. トラブルシューティングガイド」で実践的な知識を得る

### 実装者向け
1. 「2. 座標系変換の数学的基礎」で理論を理解
2. 「4. 4ステップインポートパイプライン」で実装詳細を確認
3. 各図解で視覚的に検証

### デバッグ時
1. 「7. トラブルシューティングガイド」で問題を特定
2. 該当セクションの数式とコード例を参照
3. 図解で正しい処理フローを確認

## ⚙️ 必要なツール

- **Graphviz**: DOT形式の図をレンダリング
  ```bash
  brew install graphviz
  ```

- **Markdownビューアー**: Mermaid対応のもの推奨
  - VSCode + Markdown Preview Enhanced
  - Obsidian
  - GitHub/GitLabのWebビューアー

## 📝 バージョン情報

- **Document Version**: 1.0.0
- **Last Updated**: 2025-11-18
- **Author**: AICam FBXLoader Team

## 🤝 貢献

ドキュメントの改善提案は歓迎します：

1. 数式の間違いや不明瞭な説明の指摘
2. 新しい図解の追加提案
3. トラブルシューティング事例の追加

---

**Next Steps**: ARCHITECTURE.mdを読んで、FBXインポートの設計思想を理解しましょう！
