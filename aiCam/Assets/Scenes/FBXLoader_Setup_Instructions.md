# FBXLoader Scene Setup Instructions

## 📋 概要
FBXLoaderシーンにUIToolkitベースのFBXランタイムローダーをセットアップする手順です。

## 🎬 シーン構成

```
FBXLoader Scene
├── Main Camera (既存)
├── Global Volume (既存)
├── Directional Light (既存)
├── UI_Document (新規作成)
└── RuntimeManager (新規作成)
```

---

## ⚙️ セットアップ手順

### 1. UI_Document GameObject作成

1. **Hierarchy** で右クリック → **UI Toolkit** → **UI Document**
2. 作成された **UI Document** を選択
3. Inspector で以下を設定:

#### UIDocument コンポーネント設定
- **Source Asset**:
  - `Assets/UI/RuntimeFBXLoaderWithFileBrowser/RuntimeFBXLoaderWithFileBrowser.uxml` をドラッグ&ドロップ
- **Panel Settings**:
  - 既存の `PanelSettings.asset` があればそれを使用
  - なければ新規作成 (下記参照)

#### FileBrowserUIController コンポーネント追加
1. **Add Component** → `FileBrowserUIController` を検索して追加
2. **UI Document** フィールドに、同じGameObjectの **UIDocument** コンポーネントをドラッグ&ドロップ

---

### 2. RuntimeManager GameObject作成

1. **Hierarchy** で右クリック → **Create Empty**
2. 名前を `RuntimeManager` に変更
3. Inspector で以下のコンポーネントを追加:

#### 2-1. FileBrowserController
- **Add Component** → `FileBrowserController` を追加
- 設定不要（スクリプトが自動で動作）

#### 2-2. RuntimeFBXLoaderBridge
- **Add Component** → `RuntimeFBXLoaderBridge` を追加
- **Browser** フィールドに、同じGameObjectの `FileBrowserController` をドラッグ
- **Model Parent**: 空でOK（RuntimeManager自身の下に配置される）
- **Model Position**: `(0, 0, 0)`
- **Model Rotation**: `(0, 180, 0)`
- **Model Scale**: `(1, 1, 1)`

---

### 3. Main Camera調整（オプション）

見やすい角度に調整:
- **Position**: `(0, 1.5, -3)`
- **Rotation**: `(10, 0, 0)` (やや上から見下ろす)

---

### 4. Directional Light確認（オプション）

既存のDirectional Lightを確認:
- **Rotation**: `(50, -30, 0)` 程度
- **Intensity**: `1.2`

---

## 📄 PanelSettings.asset 新規作成手順

1. **Project** ウィンドウで `Assets/UI/` フォルダに移動
2. 右クリック → **Create** → **UI Toolkit** → **Panel Settings Asset**
3. 名前を `PanelSettings` にする
4. Inspector で以下を設定:
   - **Scale Mode**: `Scale With Screen Size`
   - **Reference Resolution**: `1920 x 1080`
   - **Screen Match Mode**: `Match Width Or Height`
   - **Match**: `0.5`

---

## ✅ セットアップ完了確認

1. **Play** ボタンを押す
2. 以下が表示されればOK:
   - 上部に「Runtime FBX Loader with File Browser」タイトル
   - 「ファイルを選択」「ロード開始」ボタン
   - プログレスバー（初期は非表示）
   - ログフィールド（下部）

---

## 🚀 動作テスト

1. **「ファイルを選択」** をクリック
   - Unity Editor: ファイル選択ダイアログが開く
   - モバイル: NativeFilePickerが起動
2. FBXまたはZIPファイルを選択
3. **「ロード開始」** をクリック
4. プログレスバーが表示され、ログにメッセージが表示される
5. 完了すると Scene にプレースホルダーキューブが表示される

---

## 📝 Notes

- 現在 `RuntimeFBXLoaderBridge` はプレースホルダーキューブを生成します
- 実際のFBXローダー実装は後から `RuntimeFBXLoaderBridge.cs` に追加します
- ZIP展開は `Application.persistentDataPath/ExtractedFBX` に行われます（iOS対応）

---

## 🔧 トラブルシューティング

### UIが表示されない
- UIDocumentの **Source Asset** が正しく設定されているか確認
- PanelSettingsが設定されているか確認

### ボタンが動作しない
- FileBrowserUIControllerの **UI Document** 参照が設定されているか確認
- RuntimeManager に必要なコンポーネントがすべて付いているか確認

### ファイル選択できない
- NativeFilePickerプラグインがインポートされているか確認
- iOS/Android: アプリのファイルアクセス権限を確認
