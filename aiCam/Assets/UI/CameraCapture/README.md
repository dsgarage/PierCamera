# UIToolkit カメラ撮影UI

このディレクトリには、UIToolkitを使用したカメラ撮影ボタンUIが含まれています。

## ファイル構成

- `CameraCaptureUI.uxml` - UIのレイアウト定義
- `CameraCaptureUI.uss` - UIのスタイル定義
- `CameraCaptureController.cs` - UI制御のC#スクリプト

## 機能

### 写真撮影（短押し）
- ボタンを**短くタップ**すると写真を撮影します
- デフォルトの短押し判定時間：0.5秒未満

### 動画撮影（長押し）
- ボタンを**長押し**すると動画撮影を開始します
- 長押し開始でボタンが**赤色**に変化します
- ボタンを離すと録画が停止します
- デフォルトの長押し判定時間：0.5秒以上

## セットアップ方法

### 1. UIDocumentの作成

1. Hierarchy で右クリック → `UI Toolkit` → `UI Document` を選択
2. 作成された `UIDocument` を選択
3. Inspector で以下を設定：
   - **Source Asset**: `CameraCaptureUI.uxml` をドラッグ&ドロップ
   - **Panel Settings**: 新規作成または既存のものを設定
     - Panel Settings がない場合：
       1. Project ウィンドウで右クリック → `Create` → `UI Toolkit` → `Panel Settings Asset`
       2. 作成したアセットを UIDocument の Panel Settings にドラッグ&ドロップ

### 2. コントローラースクリプトの追加

1. UIDocument と同じGameObjectに `CameraCaptureController` スクリプトを追加
2. Inspector で以下を設定：
   - **UI Document**: 自動で設定されます（または手動でドラッグ&ドロップ）
   - **Photo Controller**: `ARPhotoController` コンポーネントをドラッグ&ドロップ
   - **Long Press Threshold**: 長押し判定時間（デフォルト: 0.5秒）
   - **Enable Video Recording**: 動画録画を有効にする場合はチェック（現在は開発中）

### 3. ARPhotoController の確認

既存の `ARPhotoController` コンポーネントがシーンに存在することを確認してください。
通常は AR Camera または専用のGameObjectにアタッチされています。

## カスタマイズ

### スタイルの変更

`CameraCaptureUI.uss` を編集して、ボタンの見た目をカスタマイズできます：

```css
/* ボタンのサイズを変更 */
.capture-button {
    width: 100px;
    height: 100px;
}

/* ボタンの色を変更 */
.capture-button {
    border-color: rgb(0, 255, 0); /* 緑色の枠線 */
}

/* 録画中の色を変更 */
.capture-button--recording {
    background-color: rgba(0, 0, 255, 0.5); /* 青色の背景 */
}
```

### レイアウトの変更

`CameraCaptureUI.uxml` を編集して、ボタンの配置を変更できます：

```xml
<!-- ボタンを上部に配置 -->
<ui:VisualElement name="RootContainer" style="justify-content: flex-start;">
    <!-- ... -->
</ui:VisualElement>

<!-- ボタンを右側に配置 -->
<ui:VisualElement name="RootContainer" style="align-items: flex-end;">
    <!-- ... -->
</ui:VisualElement>
```

### 長押し時間の変更

`CameraCaptureController` の Inspector で `Long Press Threshold` を調整してください。

## トラブルシューティング

### ボタンが表示されない

1. UIDocument の Source Asset が正しく設定されているか確認
2. Panel Settings が設定されているか確認
3. Canvas Scaler の設定を確認（Screen Space - Overlay推奨）

### ボタンが反応しない

1. `CameraCaptureController` スクリプトが追加されているか確認
2. `ARPhotoController` が正しく設定されているか確認
3. Console でエラーメッセージを確認

### 写真が撮影されない

1. `ARPhotoController` が正しく機能しているか確認
2. カメラの権限が許可されているか確認（デバイス設定）
3. AR Session が正しく初期化されているか確認

## 今後の実装予定

- [ ] 実際の動画録画機能の実装
- [ ] 録画時間の表示
- [ ] ズーム機能
- [ ] フラッシュ機能
- [ ] フィルター選択UI

## ライセンス

MIT License
