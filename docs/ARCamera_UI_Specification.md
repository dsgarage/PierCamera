# ARCamera UI 仕様書

**対象シーン**: `Assets/Scenes/ARCamera_origin.unity`
**最終更新**: 2026-01-14
**ドキュメント目的**: 外部委託によるUI刷新のための現状仕様まとめ

---

## 目次

1. [概要](#概要)
2. [技術スタック](#技術スタック)
3. [ファイル構成](#ファイル構成)
4. [UI階層構造](#ui階層構造)
5. [各コンポーネント詳細](#各コンポーネント詳細)
6. [アイコンリソース](#アイコンリソース)
7. [C#コントローラー](#cコントローラー)
8. [依存関係](#依存関係)
9. [注意事項](#注意事項)

---

## 概要

ARCamera_originは、ARカメラアプリのメイン撮影画面です。主な機能は以下の通りです：

- **写真撮影**: タップで撮影
- **動画撮影**: 長押しで録画（最大5秒）
- **アバター管理**: VRM/FBXモデルのロードとスロット管理
- **ライティング調整**: AR光推定または手動設定
- **シャドウ調整**: 影の有効/無効、強度、ソフトネス
- **表情/ポーズ切り替え**: アバターのアニメーション制御
- **アスペクト比変更**: Full / 16:9 / 3:2 / 1:1

---

## 技術スタック

| 項目 | 技術 |
|------|------|
| **UIフレームワーク** | Unity UIToolkit (UnityEngine.UIElements) |
| **レイアウト定義** | UXML |
| **スタイル定義** | USS (CSS-like) |
| **ロジック** | C# |
| **非同期処理** | UniTask (Cysharp.Threading.Tasks) |
| **ファイル選択** | NativeFilePicker |

---

## ファイル構成

```
Assets/
├── UI/
│   └── CameraCapture/
│       ├── CameraCaptureUI.uxml          # メインレイアウト
│       ├── CameraCaptureUI.uss           # メインスタイル
│       ├── LightingPanel.uxml            # ライティングパネル（単体参照用）
│       ├── LightingPanel.uss             # ライティング/シャドウパネルスタイル
│       └── CameraCapturePanelSettings.asset  # PanelSettings
│
├── Scripts/UI/
│   ├── CameraCaptureController.cs        # メインコントローラー (3,367行)
│   ├── UIMgr.cs                          # UI状態管理
│   ├── UIToolkitInputBlocker.cs          # 入力ブロッカー
│   ├── Lighting/
│   │   └── LightingPanelController.cs    # ライティングパネル制御
│   ├── Progress/
│   │   └── CircularProgressElement.cs    # カスタム円形プログレス
│   ├── Scaling/
│   │   ├── UIToolkitCanvasScaler.cs      # レスポンシブ対応
│   │   └── SafeAreaVisualizer.cs         # セーフエリア可視化
│   ├── Expression/                       # 表情システム (レガシーuGUI)
│   └── Pose/                             # ポーズシステム
│
└── Resources/Sprite/
    ├── PlaneVisibility.png
    └── PictIcon/
        ├── TopPanel/                     # 上部パネルアイコン
        ├── SideBear/                     # サイドパネルアイコン
        └── AvatarSlot/                   # アバタースロットアイコン
```

---

## UI階層構造

### 全体構造 (CameraCaptureUI.uxml)

```
root (.root)
│
├── topMask, bottomMask, leftMask, rightMask  # アスペクト比マスク
│
├── alertBar (.alert-bar)                      # 警告/エラー通知
│   ├── alertMessage
│   └── alertClose
│
├── topPanel (.top-panel)                      # 上部機能ボタン
│   ├── topButton1 (Light)
│   ├── topButton2 (Shadow)
│   ├── topButton3 (Expression)
│   ├── topButton4 (Pose)
│   └── topButton5 (Plane Visibility)
│
├── sidePanel (.side-panel)                    # 左サイドバー
│   ├── sideButton1 (Settings)
│   ├── sideButton2 (Aspect Ratio)
│   ├── sideButton3 (Flash)
│   └── sideButtonBugReport (Bug Report)
│
├── galleryThumbnail (.gallery-thumbnail)      # 最後の撮影サムネイル
│
├── AvaterSlot (.bottom-panel)                 # アバター選択パネル
│   └── bottomScrollView
│       └── bottomButtonContainer
│           ├── bottomButton1 (スロット1)
│           ├── bottomButton2 (スロット2)
│           ├── ...
│           └── bottomButtonAdd (+ボタン)
│
├── captureButton (.capture-button)            # 撮影ボタン
│   ├── outerRing
│   ├── innerCircle
│   └── progressRing                           # 録画プログレス
│       ├── progressRingBg
│       └── progressArc
│
├── flashOverlay (.flash-overlay)              # 撮影フラッシュ演出
│
├── viewerOverlay (.viewer-overlay)            # 全画面プレビュー
│   └── viewerImage
│
├── iconPreviewPanel (.icon-preview-panel)     # アイコン確認パネル
│   ├── iconPreviewImage
│   └── button-container
│       ├── iconPreviewRetake (撮り直す)
│       └── iconPreviewConfirm (確定)
│
├── lightingPanelOverlay (.lighting-panel-overlay)  # ライティング設定
│   └── lightingPanel (.lighting-panel)
│       ├── header (タイトル + 閉じるボタン)
│       ├── AR同期トグル
│       ├── プリセット選択 (6個)
│       ├── 色温度スライダー (2000-10000K)
│       ├── 明るさスライダー (0.1-2.0)
│       └── ライト方向コントロール (コンパス + 仰角)
│
└── shadowPanelOverlay (.shadow-panel-overlay)      # シャドウ設定
    └── shadowPanel
        ├── header
        ├── Enable Shadow トグル
        ├── Intensity スライダー
        └── Softness ボタン (Hard/Medium/Soft)
```

---

## 各コンポーネント詳細

### 1. 撮影ボタン (captureButton)

| 項目 | 値 |
|------|-----|
| サイズ | 120x120px |
| 内側円 | 90x90px (白、録画中は赤) |
| 外枠 | PNG画像 (CaptureButtonOuter) |
| プログレス | カスタム円弧描画 (CircularProgressElement) |

**動作**:
- タップ: 写真撮影 → フラッシュ演出 → サムネイル更新
- 長押し (0.5秒以上): 動画録画開始 → プログレスリング表示
- 最大録画時間: 5秒

### 2. 上部パネル (topPanel)

| ボタン | 機能 | アイコン |
|--------|------|---------|
| topButton1 | ライティングパネル表示 | 01_Icon_Light.png |
| topButton2 | シャドウパネル表示 | 02_Icon_Shadow.png |
| topButton3 | 表情切り替え (タップ/ダブルタップ) | 03_Icon_Face.png |
| topButton4 | ポーズ切り替え (タップ/ダブルタップ) | 04_Icon_Pose.png |
| topButton5 | AR平面表示ON/OFF | PlaneVisibility.png |

**スタイル**:
- タッチ領域: 64x64px (Apple HIG準拠)
- 背景: 半透明グレー (rgba(128,128,128,0.5))
- 角丸: 8px

### 3. サイドパネル (sidePanel)

| ボタン | 機能 | アイコン |
|--------|------|---------|
| sideButton1 | 設定 | 01_Prefarence.png |
| sideButton2 | アスペクト比トグル | 02_01_Full.png → 02_02_169.png → 02_03_32.png → 02_04_11.png |
| sideButton3 | フラッシュ | 03_Flash.png |
| sideButtonBugReport | バグレポート | 04_BugReport.png |

**アスペクト比サイクル**:
1. Full (カメラ最大画角)
2. 16:9
3. 3:2
4. 1:1 (正方形)

### 4. アバタースロット (AvaterSlot / bottom-panel)

**構造**:
- 水平スクロール可能
- 動的にスロットを追加可能
- 最後に「+」ボタン

**スロット状態**:
| 状態 | スタイル |
|------|---------|
| 空 | グレー背景 + EmptySlotIcon |
| 設定済み | サムネイル表示、opacity: 0.4 |
| 選択中 | 青ボーダー、opacity: 1.0 |

**操作**:
- タップ: スロット選択 → アバター表示切替
- 長押し: 削除ポップアップ表示
- 「+」タップ: ファイル選択ダイアログ

### 5. ライティングパネル (lightingPanelOverlay)

**配置**: 画面下50%〜下端8px

**セクション**:

| セクション | コントロール | 値域 |
|-----------|-------------|------|
| AR Light Sync | Toggle | ON/OFF |
| Preset | 6ボタン | Auto, Sunny, Cloudy, Indoor, Warm, Sunset |
| Color Temperature | Slider | 2000K〜10000K |
| Brightness | Slider | 0.1〜2.0 |
| Light Direction | コンパスUI + Elevation | 方位角 + 仰角10°〜90° |

### 6. シャドウパネル (shadowPanelOverlay)

**配置**: 画面下80px〜

**セクション**:

| セクション | コントロール | 値域 |
|-----------|-------------|------|
| Enable Shadow | Toggle | ON/OFF |
| Intensity | Slider | 0〜1 |
| Softness | 3ボタン | Hard, Medium, Soft |

### 7. アラートバー (alertBar)

**タイプ**:
| クラス | 背景色 | 用途 |
|--------|--------|------|
| .warning | 黄 (rgba(255,200,0,0.95)) | 警告 |
| .error | 赤 (rgba(220,60,60,0.95)) | エラー |
| .info | 青 (rgba(80,180,220,0.95)) | 情報 |

### 8. アイコンプレビューパネル (iconPreviewPanel)

撮影後のプレビュー確認用フルスクリーンパネル。

**ボタン**:
- 撮り直す (retake): グレー背景
- 確定 (confirm): 緑背景

---

## アイコンリソース

### パス: `Assets/Resources/Sprite/`

```
Sprite/
├── PlaneVisibility.png
└── PictIcon/
    ├── TopPanel/
    │   ├── 01_Icon_Light.png
    │   ├── 02_Icon_Shadow.png
    │   ├── 03_Icon_Face.png
    │   └── 04_Icon_Pose.png
    ├── SideBear/
    │   ├── 01_Prefarence.png
    │   ├── 02_01_Full.png
    │   ├── 02_02_169.png
    │   ├── 02_03_32.png
    │   ├── 02_04_11.png
    │   ├── 03_Flash.png
    │   └── 04_BugReport.png
    └── AvatarSlot/
        └── EmptySlotIcon.png
```

**撮影ボタン画像**: `Assets/UI/Buttons/` (推定)
- CaptureButtonOuter.png
- CaptureButtonInner.png

---

## C#コントローラー

### CameraCaptureController.cs (3,367行)

**主要フィールド**:

```csharp
[Header("Capture Settings")]
private ARPhotoController photoController;

[Header("Avatar Loader")]
private RuntimeAvatarLoader avatarLoader;
private RuntimeFBXLoaderBridge fbxLoaderBridge;

[Header("Pose Animation")]
private AnimatorOverrideController[] poseOverrideControllers;
private PoseSlotController poseSlotController;

[Header("Expression System")]
private VrmExpressionSetup expressionSetup;
```

**スロットデータ構造**:

```csharp
private class SlotData
{
    public string filePath;
    public SlotFileType fileType;  // None, VRM, FBX
    public Texture2D thumbnail;
    public GameObject loadedAvatar;
    public bool IsConfigured => !string.IsNullOrEmpty(filePath);
}
```

**主要メソッド**:
- `OnCaptureButtonDown()` / `OnCaptureButtonUp()`: 撮影ボタン制御
- `TakePhoto()`: 写真撮影
- `StartRecording()` / `StopRecording()`: 動画録画
- `LoadAvatarToSlot()`: アバターロード
- `ShowLightingPanel()` / `HideLightingPanel()`: パネル表示
- `UpdateAspectMask()`: アスペクト比マスク更新

### LightingPanelController.cs

**責務**:
- AR光推定とマニュアル設定の切り替え
- プリセット適用
- スライダー値の反映
- ライト方向のコンパスUI制御

---

## 依存関係

### 内部依存

```
CameraCaptureController
├── UIDocument
├── ARPhotoController
├── RuntimeAvatarLoader
├── RuntimeFBXLoaderBridge
├── PoseSlotController
├── VrmExpressionSetup
├── LightingPanelController
│   ├── ARLightEstimationController
│   └── Light (Main Light)
├── ARPlaneVisibilityController
└── ARPlaneShadowReceiver
```

### 外部パッケージ

| パッケージ | 用途 |
|-----------|------|
| AR Foundation | AR機能全般 |
| UniTask | 非同期処理 |
| NativeFilePicker | ファイル選択 |
| UniVRM / VRM-1.0 | VRMモデル読み込み |

---

## 注意事項

### UIToolkit制約 (CLAUDE.mdより)

1. **`cursor: link` はランタイムで使用不可**
   - テクスチャベースのカーソルが必要
   - 警告ログが大量に出るため避けること

2. **`picking-mode="Ignore"` の扱い**
   - オーバーレイ要素は `display: none` で非表示にする
   - 表示時にクリックが必要な場合は動的に `PickingMode.Position` に切り替え

3. **オーバーレイ表示パターン**
   - デフォルト: `display: none`
   - 表示時: `.visible` クラス追加 → `display: flex`

### レスポンシブ対応

- 参照解像度: 1200x800 (PanelSettings)
- セーフエリア: `UIToolkitCanvasScaler` で自動パディング
- ノッチ/パンチホール対応済み

### パフォーマンス

- 円形プログレスはカスタムメッシュ描画 (`generateVisualContent`)
- スロット追加は動的生成（プールなし）
- サムネイルはキャッシュ保存

---

## 変更履歴

| Issue | 内容 |
|-------|------|
| #33/#405 | 表情切り替え機能 |
| #73 | スロット別プログレス |
| #74 | Light Estimation制御 |
| #75 | Shadow制御 |
| #120 | ライティングパネル |
| #145/#411 | 表情システム |
| #345 | 平面表示ON/OFF |
| #407 | ポーズ切り替え |
| #413 | バグレポートボタン |
