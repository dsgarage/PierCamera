---
paths:
  - "Assets/**/*.cs"
  - "Assets/Editor/**"
---

# 禁止パターン

## Editor スクリプトの作成は完全に禁止

以下のパターンに該当するコードを絶対に生成しないこと:

- `using UnityEditor;` を含むランタイム外スクリプト
- `[InitializeOnLoad]` 属性
- `[MenuItem(...)]` 属性
- `EditorWindow` を継承するクラス
- `Editor` を継承するカスタムインスペクタ

## MCP 不可時の対応

MCP ツールが利用できない場合:
「現在 Unity MCP ツールへのアクセスがありません。Inspector での手動設定をお願いします。」
と回答し、それ以上の回避策を提案しない。
