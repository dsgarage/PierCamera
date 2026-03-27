---
paths:
  - "Assets/**/*.cs"
  - "Assets/**/*.uss"
  - "Assets/**/*.uxml"
---

# Unity / C# コーディング規約

## 命名規則
- クラス名: UpperCamelCase（例: `AvatarLoader`, `ARCameraController`）
- メソッド名: UpperCamelCase（例: `LoadModel()`, `OnButtonClicked()`）
- フィールド: lowerCamelCase、private は `_` プレフィックス（例: `_isLoading`）
- SerializeField: `[SerializeField] private float _moveSpeed;`
- 名前は英語、コメントは日本語可

## Editor スクリプト禁止（絶対厳守）
- `[InitializeOnLoad]` を持つスクリプトを作成しない
- `[MenuItem]` を使った設定メニューを提案しない
- `SerializedObject` / `SerializedProperty` を使うスクリプトを書かない
- Inspector 設定が必要な場合は MCP 経由で行うか、手動設定を案内する

## UIToolkit 注意事項
- USS で `cursor: link;` をランタイムで使用しない（警告ログの原因）
- `picking-mode="Ignore"` は非表示オーバーレイにのみ設定
- 表示時にクリックが必要な要素は C# で `pickingMode = PickingMode.Position` に動的切替
- オーバーレイのデフォルトは `display: none`、`.visible` クラスで `display: flex` に切替

## ファイル配置
- ランタイムスクリプト: `Assets/Scripts/` 配下
- USS/UXML: 対応する UI コンポーネントと同じディレクトリに配置
