# UI テストシーン ガイド

## 概要

UIテストシーンは、UXML/USSの変更が正しく設定されているかを検証するための専用シーンです。
再現性のある環境で、デバッグログによる自動チェックを実行できます。

## ファイル構成

```
Assets/UI/UITK_Pier/
├── Editor/
│   └── UITestSceneCreator.cs    # シーン作成ユーティリティ
├── Scripts/
│   └── Debug/
│       ├── UIDebugChecker.cs    # UI要素検証
│       └── UITestSceneSetup.cs  # 状態シミュレーション
└── Scenes/
    └── UITestScene.unity        # テストシーン（自動生成）
```

## 使用方法

### スクリプトからの呼び出し

```csharp
using UITK_Pier.Editor;

// テストシーンを作成
UITestSceneCreator.CreateUITestScene();

// テストシーンを開く
UITestSceneCreator.OpenUITestScene();

// バリデーション実行
var result = UITestSceneCreator.RunUIValidation();
if (!result.IsValid)
{
    Debug.LogError($"Validation failed: {result.MissingElements} missing");
}
```

### Inspectorからの操作

テストシーンを開いた後、`UIDocument_Test` オブジェクトを選択:

**UIDebugChecker コンポーネント:**
- Context Menu → `Run All Checks` でバリデーション実行

**UITestSceneSetup コンポーネント:**
- Context Menu → `Reset to Default` - 初期状態に戻す
- Context Menu → `Show All Panels` - 全パネル表示
- Context Menu → `Simulate Recording` - 録画状態シミュレート
- Context Menu → `Cycle Aspect Ratio` - アスペクト比切替

## コンポーネント

### UIDebugChecker

UI要素の存在確認と型チェックを行うコンポーネント。

**機能:**
- 必須要素IDの存在確認（69要素）
- 要素型の検証（Button, Slider, Toggle等）
- PanelSettings設定の確認
- CSSクラスの動作確認

**Inspector設定:**

| 項目 | 説明 |
|------|------|
| Check On Start | 起動時に自動チェック |
| Log To Console | Consoleにログ出力 |
| Show Overlay Panel | デバッグパネル表示 |

### UITestSceneSetup

UI状態のシミュレーションを行うコンポーネント。

**状態シミュレーション:**

| 状態 | 説明 |
|------|------|
| Default | 初期状態 |
| Recording | 録画中 |
| Capturing | 撮影フラッシュ |
| PreviewingPhoto | 写真プレビュー |
| PreviewingIcon | アイコンプレビュー |
| LightingAdjustment | ライティングパネル表示 |
| ShadowAdjustment | シャドウパネル表示 |
| Alert | アラートバー表示 |

**Inspector設定:**

| 項目 | 説明 |
|------|------|
| Show Lighting Panel | ライティングパネル表示 |
| Show Shadow Panel | シャドウパネル表示 |
| Show Viewer Overlay | ビューワーオーバーレイ表示 |
| Show Icon Preview | アイコンプレビュー表示 |
| Show Alert Bar | アラートバー表示 |
| Is Recording | 録画中フラグ |
| Recording Progress | 録画進捗（0-1） |
| Aspect Ratio | Full / 16:9 / 3:2 / 1:1 |

## チェック項目

### 必須要素ID（抜粋）

```
Capture Elements:
  - captureButton
  - innerCircle
  - progressRing
  - galleryThumbnail

Panels:
  - topPanel
  - sidePanel
  - bottomPanel

Buttons:
  - topButton1-5
  - sideButton1-3
  - sideButtonBugReport

Overlays:
  - lightingPanelOverlay
  - shadowPanelOverlay
  - viewerOverlay
  - iconPreviewPanel
```

### 要素型チェック

| 要素 | 期待される型 |
|------|-------------|
| captureButton | VisualElement |
| topButton1 | Button |
| colorTempSlider | Slider |
| arSyncToggle | Toggle |
| alertMessage | Label |
| viewerImage | Image |

### PanelSettings チェック

- 参照解像度: 1920x1080（推奨）または 1200x800（レガシー）
- スケールモード: ScaleWithScreenSize
- スクリーンマッチモード: MatchWidthOrHeight

## トラブルシューティング

### "Root VisualElement is null"

- UIDocumentにVisualTreeAssetが設定されているか確認
- PanelSettingsが設定されているか確認

### 要素が見つからない

- UXMLファイルに `name="要素ID"` が正しく設定されているか確認
- スペルミスがないか確認

### 型が一致しない

- UXML内の要素タグが正しいか確認
  - `<Button>` → Button
  - `<Slider>` → Slider
  - `<Toggle>` → Toggle
  - `<Label>` → Label
