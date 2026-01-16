# UI テストシーン ガイド

## 概要

UIテストシーンは、UXML/USSの変更が正しく設定されているかを検証するための専用シーンです。
再現性のある環境で、デバッグログによる自動チェックを実行できます。

## セットアップ

### テストシーンの作成

Unity Editorのメニューから:

```
ARCamera > UI Test > Create UI Test Scene
```

これにより `Assets/Scenes/UITestScene.unity` が作成されます。

### テストシーンを開く

```
ARCamera > UI Test > Open UI Test Scene
```

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

**Context Menu:**
- `Run All Checks` - 全チェックを実行

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

**パネル表示切替:**
- Show Lighting Panel
- Show Shadow Panel
- Show Viewer Overlay
- Show Icon Preview
- Show Alert Bar

**録画状態:**
- Is Recording - 録画中フラグ
- Recording Progress - 進捗（0-1）

**アスペクト比:**
- Full
- 16:9
- 3:2
- 1:1

**Context Menu:**
- `Reset to Default` - 初期状態に戻す
- `Show All Panels` - 全パネル表示
- `Simulate Recording` - 録画シミュレート
- `Cycle Aspect Ratio` - アスペクト比切替
- `Run Debug Check` - デバッグチェック実行

## テストウィンドウ

```
ARCamera > UI Test > Open Test Window
```

GUIベースのテストツールウィンドウを開きます。

**機能:**
- シーン作成/オープン
- バリデーション実行
- 結果表示
- 状態シミュレーション

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

## 使用例

### 新UIの検証ワークフロー

1. UITK_Pier内でUXML/USSを編集
2. テストシーンを開く
3. `Run All Checks`を実行
4. 欠落要素/型エラーを確認
5. 各状態をシミュレートして表示確認

### CI/CD連携

`UIDebugChecker.GetValidationResult()` を使用してプログラムから検証結果を取得可能。

```csharp
var checker = FindFirstObjectByType<UIDebugChecker>();
checker.RunAllChecks();
var result = checker.GetValidationResult();

if (!result.IsValid)
{
    Debug.LogError($"UI Validation Failed: {result.MissingElements} missing elements");
}
```

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
