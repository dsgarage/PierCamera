# ARCamera UI 依存関係図

## コンポーネント依存関係

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           ARCamera_origin.unity                              │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         CameraCaptureController                              │
│  ─────────────────────────────────────────────────────────────────────────  │
│  [Requires] UIDocument, UIToolkitInputBlocker                               │
│  ─────────────────────────────────────────────────────────────────────────  │
│  [SerializeField]                                                            │
│    • ARPhotoController photoController                                       │
│    • RuntimeAvatarLoader avatarLoader                                        │
│    • RuntimeFBXLoaderBridge fbxLoaderBridge                                  │
│    • AnimatorOverrideController[] poseOverrideControllers                    │
│    • PoseSlotController poseSlotController                                   │
│    • VrmExpressionSetup expressionSetup                                      │
└─────────────────────────────────────────────────────────────────────────────┘
         │              │              │              │              │
         ▼              ▼              ▼              ▼              ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│ARPhotoController│ │RuntimeAvatar │ │RuntimeFBX   │ │PoseSlot     │ │VrmExpression │
│              │ │    Loader    │ │LoaderBridge │ │ Controller  │ │   Setup      │
│  写真/動画撮影│ │  VRMロード   │ │  FBXロード   │ │ ポーズ制御  │ │  表情制御    │
└──────────────┘ └──────────────┘ └──────────────┘ └──────────────┘ └──────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        LightingPanelController                               │
│  ─────────────────────────────────────────────────────────────────────────  │
│  [FindFirst]                                                                 │
│    • ARLightEstimationController (AR光推定)                                  │
│    • Light (MainLight)                                                       │
│    • ARPlaneShadowReceiver (影レシーバー)                                    │
└─────────────────────────────────────────────────────────────────────────────┘
         │              │              │
         ▼              ▼              ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│ARLight       │ │    Light     │ │ARPlaneShadow │
│Estimation    │ │  (MainLight) │ │  Receiver    │
│ Controller   │ │              │ │              │
│  AR光推定    │ │  照明制御    │ │  影表示      │
└──────────────┘ └──────────────┘ └──────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                      ARPlaneVisibilityController                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│  topButton5 から制御                                                         │
│  AR平面の表示/非表示を切り替え                                               │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## UIファイル依存関係

```
CameraCaptureUI.uxml
    │
    ├── [Style] CameraCaptureUI.uss
    │
    └── [Style] LightingPanel.uss
            │
            └── ライティング/シャドウパネルのスタイル定義


LightingPanel.uxml (単体参照用、メインUXMLに埋め込み済み)
    │
    └── [Style] LightingPanel.uss
```

---

## イベントフロー

### 撮影フロー

```
[User] タップ
    │
    ▼
CameraCaptureController.OnCaptureButtonDown()
    │
    ├─── isPressed = true
    └─── pressTime = 0

[Update] pressTime < longPressThreshold (0.5s)
    │
    ▼
[User] 指を離す
    │
    ▼
CameraCaptureController.OnCaptureButtonUp()
    │
    ├─── pressTime < longPressThreshold
    │       │
    │       ▼
    │    TakePhoto()
    │       │
    │       ├── ARPhotoController.CapturePhoto()
    │       ├── FlashOverlay アニメーション
    │       ├── サムネイル更新 (galleryThumbnail)
    │       └── IconPreviewPanel 表示
    │
    └─── pressTime >= longPressThreshold
            │
            ▼
         StopRecording()
            │
            ├── ARPhotoController.StopRecording()
            └── サムネイル更新
```

### アバタースロットフロー

```
[User] +ボタン タップ
    │
    ▼
NativeFilePicker.PickFile()
    │
    ▼
OnFileSelected(path)
    │
    ├── VRM? → RuntimeAvatarLoader.LoadVRM()
    └── FBX? → RuntimeFBXLoaderBridge.LoadFBX()
            │
            ▼
        OnAvatarLoaded(avatar)
            │
            ├── スロットにサムネイル設定
            ├── SlotData 更新
            └── スロット選択状態に
```

### ライティングパネルフロー

```
[User] topButton1 タップ
    │
    ▼
ShowLightingPanel()
    │
    ├── lightingPanelOverlay.AddToClassList("visible")
    └── LightingPanelController.Initialize()
            │
            ├── ARLightEstimationController 取得
            ├── MainLight 取得
            └── 各コントロールにイベント登録

[User] AR Sync Toggle OFF
    │
    ▼
OnARSyncToggleChanged(false)
    │
    ├── ARLightEstimationController.enabled = false
    └── マニュアル設定を有効化

[User] プリセット選択
    │
    ▼
ApplyPreset(presetName)
    │
    ├── 色温度設定
    ├── 明るさ設定
    └── スライダー値更新
```

---

## スロットデータ構造

```
SlotData
├── filePath: string          // VRM/FBXファイルパス
├── fileType: SlotFileType    // None | VRM | FBX
├── thumbnail: Texture2D      // サムネイル画像
├── loadedAvatar: GameObject  // ロード済みアバター参照
└── IsConfigured: bool        // filePath != null

slotDataMap: Dictionary<Button, SlotData>
    │
    ├── bottomButton1 → SlotData
    ├── bottomButton2 → SlotData
    └── ...

currentSelectedSlot: Button   // 現在選択中のスロット
```

---

## スタイルクラス一覧

### メインUI (CameraCaptureUI.uss)

| クラス名 | 用途 |
|---------|------|
| `.root` | ルートコンテナ |
| `.capture-button` | 撮影ボタン |
| `.inner-circle` | 撮影ボタン内側円 |
| `.inner-circle.recording` | 録画中 (赤) |
| `.outer-ring` | 撮影ボタン外枠 |
| `.progress-ring` | プログレスリング |
| `.progress-ring.active` | 録画中表示 |
| `.top-panel` | 上部パネル |
| `.top-panel-button` | 上部ボタン |
| `.side-panel` | サイドパネル |
| `.side-panel-button` | サイドボタン |
| `.bottom-panel` | 下部パネル |
| `.bottom-panel-button` | アバタースロット |
| `.bottom-panel-button.selected` | 選択中 |
| `.bottom-panel-button.has-icon` | アイコン設定済み |
| `.bottom-panel-button-add` | +ボタン |
| `.alert-bar` | アラートバー |
| `.alert-bar.visible` | 表示中 |
| `.alert-bar.warning` | 警告 (黄) |
| `.alert-bar.error` | エラー (赤) |
| `.alert-bar.info` | 情報 (青) |
| `.icon-preview-panel` | アイコンプレビュー |
| `.icon-preview-panel.visible` | 表示中 |
| `.flash-overlay` | フラッシュ演出 |
| `.viewer-overlay` | 全画面プレビュー |
| `.aspect-mask` | アスペクト比マスク |
| `.gallery-thumbnail` | ギャラリーサムネイル |

### ライティングパネル (LightingPanel.uss)

| クラス名 | 用途 |
|---------|------|
| `.lighting-panel-overlay` | オーバーレイコンテナ |
| `.lighting-panel-overlay.visible` | 表示中 |
| `.lighting-panel` | メインパネル |
| `.lighting-panel-header` | ヘッダー |
| `.lighting-section` | セクション |
| `.preset-container` | プリセットボタン群 |
| `.preset-button` | プリセットボタン |
| `.preset-button.preset-selected` | 選択中 |
| `.lighting-slider` | スライダー |
| `.color-temp-gradient` | 色温度グラデーション |
| `.light-direction-control` | 方向コントロール |
| `.light-direction-background` | コンパス背景 |
| `.light-direction-knob` | 方向ノブ |
| `.elevation-slider` | 仰角スライダー |
| `.toggle-row` | トグル行 |
| `.ar-sync-toggle` | AR同期トグル |
| `.shadow-toggle` | シャドウトグル |
| `.softness-button` | ソフトネスボタン |
| `.softness-button.softness-selected` | 選択中 |
| `.shadow-panel-overlay` | シャドウパネルオーバーレイ |
| `.shadow-panel-overlay.visible` | 表示中 |
| `.shadow-panel` | シャドウパネル |

---

## 公開インターフェース

### CameraCaptureController

```csharp
// アラート表示
public void ShowAlert(string message, AlertType type);
public void HideAlert();

// スロット操作
public void SelectSlot(int index);
public void AddNewSlot();
public void RemoveSlot(int index);

// パネル表示
public void ShowLightingPanel();
public void HideLightingPanel();
public void ShowShadowPanel();
public void HideShadowPanel();

// アスペクト比
public void SetAspectRatio(AspectRatioType type);
```

### LightingPanelController

```csharp
// 初期化
public void Initialize(VisualElement panelRoot);

// AR同期
public void SetARSyncEnabled(bool enabled);

// プリセット
public void ApplyPreset(string presetName);

// 手動設定
public void SetColorTemperature(float kelvin);
public void SetBrightness(float value);
public void SetLightDirection(float azimuth, float elevation);

// シャドウ
public void SetShadowEnabled(bool enabled);
public void SetShadowIntensity(float value);
public void SetShadowSoftness(ShadowSoftness softness);
```
