# Unity Build Error 修正手順

## エラー内容
`Script updater for Library/Bee/artifacts/200b0aE.dag/lilToon.Editor.dll failed with exitcode 134`

このエラーは lilToon v1.5.0 と Unity 6000.1.11f1 の間で発生する既知の互換性問題です。

## 解決方法

### 方法1: キャッシュクリア（実施済み）
以下のディレクトリを削除しました：
- `Library/Bee/`
- `Library/ScriptAssemblies/`
- `Temp/`

### 方法2: Unity Editor での追加対策

1. **Unity Editor を開く**
2. **Edit > Preferences > External Tools** で Script Editor を確認
3. **Edit > Project Settings > Editor** で以下を設定：
   - Enter Play Mode Settings: Domain Reload を有効化
   - Asset Serialization: Force Text

4. **Window > General > Console** でエラーをクリア
5. **Assets > Reimport All** を実行

### 方法3: lilToon の更新または再インストール

1. **Package Manager** を開く（Window > Package Manager）
2. lilToon が表示されない場合は、以下を確認：
   - `Assets/lilToon` フォルダを一旦削除
   - 最新版の lilToon (v1.7.3以降推奨) を以下からダウンロード：
     https://github.com/lilxyzw/lilToon/releases
   - ダウンロードした .unitypackage をインポート

### 方法4: Assembly Definition の修正

もし問題が継続する場合、以下のファイルを作成：

`Assets/lilToon/Editor/lilToon.Editor.asmdef`
```json
{
    "name": "lilToon.Editor",
    "references": [],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

### 方法5: Script Updater の無効化（最終手段）

1. **Edit > Project Settings > Player**
2. **Configuration > Api Compatibility Level** を `.NET Standard 2.1` に設定
3. **Edit > Preferences > General** で Auto Refresh を無効化
4. Unity を再起動

## 推奨される恒久的な解決策

1. lilToon を最新版（v1.7.3以降）に更新
2. Unity 6000.1.11f1 の最新パッチを適用
3. プロジェクトの Library フォルダを完全に削除して再生成

## 確認方法

エラーが解決したかは以下で確認：
1. Unity Editor を起動
2. Console にエラーが表示されないことを確認
3. Play Mode に入れることを確認

## 注意事項

- lilToon は人気のトゥーンシェーダーで、本プロジェクトのアバター表示に使用されています
- 削除や変更の際は、マテリアルの再設定が必要になる場合があります
- バックアップを取ってから作業することを推奨します