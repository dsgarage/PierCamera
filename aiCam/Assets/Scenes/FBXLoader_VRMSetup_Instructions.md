# FBXLoader Scene - VRM Loader Setup Instructions

## 概要
このシーンでVRMモデルをロードして表示するためのセットアップ手順です。

## 必要な設定

### 1. ModelSpawnPoint の作成

1. **Hierarchyで右クリック → Create Empty**
2. 新しいGameObjectの名前を **`ModelSpawnPoint`** に変更
3. Transformを以下のように設定：
   - **Position**: X=0, Y=0.5, Z=1.5 （カメラの前1.5m、地面から0.5m上）
   - **Rotation**: X=0, Y=0, Z=0
   - **Scale**: X=1, Y=1, Z=1

### 2. RuntimeFBXLoaderBridge の設定

1. **Hierarchyで `RuntimeManager` を選択**
2. **Inspectorで `RuntimeFBXLoaderBridge` コンポーネントを確認**
3. 以下のフィールドを設定：

#### Model Parent
- **Model Parent**: `ModelSpawnPoint` のTransformをドラッグ&ドロップ

#### Settings
- **Model Position**: X=0, Y=0, Z=0 （ModelSpawnPointからの相対位置）
- **Model Rotation**: X=0, Y=180, Z=0 （カメラの方を向く）
- **Model Scale**: X=1, Y=1, Z=1

#### Animation (オプション)
- **Animator Controller**: アニメーションを適用する場合は設定
- **Initial State Name**: 初期アニメーション状態名（例: "Idle"）

### 3. 動作確認

1. **Playモードで実行**
2. **「ファイルを選択」ボタンをクリック**
3. **VRMファイルを選択**
4. **「ロード開始」ボタンをクリック**
5. ModelSpawnPoint の位置にVRMモデルが表示されることを確認

## トラブルシューティング

### モデルが表示されない場合
- Main Cameraの位置を確認（デフォルト: Z=-4）
- ModelSpawnPointがカメラのView Frustum内にあるか確認
- Consoleでエラーを確認

### モデルが小さすぎる/大きすぎる場合
- RuntimeFBXLoaderBridgeの `Model Scale` を調整
- 例: 小さい場合 → X=2, Y=2, Z=2

### モデルの向きが逆の場合
- `Model Rotation` のY値を調整
- 例: Y=180（カメラ向き）, Y=0（カメラの反対向き）

## 既存コンポーネント

以下のコンポーネントは既に設定済みです：
- ✅ **FileBrowserController** (RuntimeManager)
- ✅ **RuntimeFBXLoaderBridge** (RuntimeManager)
- ✅ **FileBrowserUIController** (UI_Document)
- ✅ **UIDocument** (UI_Document)

## ファイル構成

```
Assets/
├── Scenes/
│   └── FBXLoader.unity          # このシーン
├── Scripts/
│   └── FBXLoader/
│       ├── FileBrowserController.cs
│       ├── FileBrowserUIController.cs
│       └── RuntimeFBXLoaderBridge.cs
└── UI/
    └── RuntimeFBXLoaderWithFileBrowser/
        ├── RuntimeFBXLoaderWithFileBrowser.uxml
        └── RuntimeFBXLoaderWithFileBrowser.uss
```
