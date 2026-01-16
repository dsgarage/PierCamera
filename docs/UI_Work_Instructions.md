# UI刷新 作業指示書

**対象ファイル**: `Assets/UITK_Pier/UI/ARCameraScreen/CaptureControls.uxml`
**関連USS**: `Assets/UITK_Pier/UI/ARCameraScreen/CaptureControls.uss`

---

## 作業概要

UITK_Pierの新デザインを維持しつつ、既存コントローラーとの互換性を確保するため、要素ID・型・CSSクラスを修正します。

**重要**: 見た目のデザインは自由に変更可能ですが、以下の互換性要件は必須です。

---

## 1. 撮影ボタン (captureButton)

### 現状 (UITK_Pier)
```xml
<ui:VisualElement name="shootingBtn" class="shooting-btn">
    <ui:VisualElement name="ellipse" class="shooting-btn-ellipse" />
</ui:VisualElement>
```

### 変更後 (必須)
```xml
<ui:VisualElement name="captureButton" class="capture-button">
    <ui:VisualElement name="outerRing" class="outer-ring" />
    <ui:VisualElement name="innerCircle" class="inner-circle" />
    <ui:VisualElement name="progressRing" class="progress-ring">
        <ui:VisualElement name="progressRingBg" class="progress-ring-bg" />
        <ui:VisualElement name="progressArc" class="progress-arc" />
    </ui:VisualElement>
</ui:VisualElement>
```

### UXML チェックリスト
- [ ] name="shootingBtn" → name="captureButton" に変更
- [ ] name="ellipse" → name="innerCircle" に変更
- [ ] name="outerRing" を追加（外枠画像用）
- [ ] name="progressRing" を追加（プログレス親）
- [ ] name="progressRingBg" を追加（プログレス背景）
- [ ] name="progressArc" を追加（プログレス円弧）

### USS チェックリスト
- [ ] .capture-button: サイズ120x120px
- [ ] .inner-circle: サイズ90x90px、border-radius 50%
- [ ] .inner-circle.recording: 録画時の赤色スタイル
- [ ] .outer-ring: position absolute、背景画像
- [ ] .progress-ring: position absolute、デフォルトopacity 0
- [ ] .progress-ring.active: opacity 1
- [ ] .progress-arc: 円弧描画用スタイル

---

## 2. 上部パネル (topPanel)

### 現状 (UITK_Pier)
```xml
<ui:VisualElement name="topNavBar" class="controls-top-nav">
    <ui:VisualElement name="closeBtn" />
    <ui:VisualElement name="rightTopContainer">
        <ui:VisualElement name="flashBtn" />
        <ui:VisualElement name="flatVisualBtn" />
    </ui:VisualElement>
</ui:VisualElement>
```

### 変更後 (必須)
```xml
<ui:VisualElement name="topPanel" class="top-panel">
    <ui:Button name="topButton1" class="top-panel-button" />  <!-- Light -->
    <ui:Button name="topButton2" class="top-panel-button" />  <!-- Shadow -->
    <ui:Button name="topButton3" class="top-panel-button" />  <!-- Expression -->
    <ui:Button name="topButton4" class="top-panel-button" />  <!-- Pose -->
    <ui:Button name="topButton5" class="top-panel-button" />  <!-- Plane -->
</ui:VisualElement>
```

### UXML チェックリスト
- [ ] name="topNavBar" → name="topPanel" に変更
- [ ] 型を VisualElement → Button に変更（topButton1〜5）
- [ ] name="topButton1" (ライティング)
- [ ] name="topButton2" (シャドウ)
- [ ] name="topButton3" (表情)
- [ ] name="topButton4" (ポーズ)
- [ ] name="topButton5" (平面表示)

### USS チェックリスト
- [ ] .top-panel: position absolute, top 60px
- [ ] .top-panel-button: 64x64px（タッチ領域）
- [ ] #topButton5.plane-visible: 表示状態
- [ ] #topButton5.plane-hidden: 非表示状態（opacity 0.4）

---

## 3. サイドパネル (sidePanel)

### 現状 (UITK_Pier)
```xml
<ui:VisualElement name="sideMenu" class="controls-side-menu">
    <ui:VisualElement name="aspectBtn" />
    <ui:VisualElement name="bugRepoBtn" />
</ui:VisualElement>
```

### 変更後 (必須)
```xml
<ui:VisualElement name="sidePanel" class="side-panel">
    <ui:Button name="sideButton1" class="side-panel-button" />  <!-- 設定 -->
    <ui:Button name="sideButton2" class="side-panel-button" />  <!-- アスペクト比 -->
    <ui:Button name="sideButton3" class="side-panel-button" />  <!-- フラッシュ -->
    <ui:Button name="sideButtonBugReport" class="side-panel-button" />
</ui:VisualElement>
```

### UXML チェックリスト
- [ ] name="sideMenu" → name="sidePanel" に変更
- [ ] 型を VisualElement → Button に変更
- [ ] name="sideButton1" (設定)
- [ ] name="sideButton2" (アスペクト比) ※aspectBtnから変更
- [ ] name="sideButton3" (フラッシュ) ※flashBtnをここに移動
- [ ] name="sideButtonBugReport" ※bugRepoBtnから変更

### USS チェックリスト
- [ ] .side-panel: position absolute, left 16px, top 50%
- [ ] .side-panel-button: 58x58px

---

## 4. アバタースロット (bottomPanel)

### 現状 (UITK_Pier)
```xml
<ui:ScrollView name="avatarSlotBar">
    <ui:VisualElement name="avatarSlotRow">
        <ui:VisualElement name="avatarSlotBtn" />
        <ui:VisualElement name="slotAddBtn" />
    </ui:VisualElement>
</ui:ScrollView>
```

### 変更後 (必須)
```xml
<ui:VisualElement name="bottomPanel" class="bottom-panel">
    <ui:ScrollView name="bottomScrollView" mode="Horizontal">
        <ui:VisualElement name="bottomButtonContainer" class="bottom-button-container">
            <ui:Button name="bottomButton1" class="bottom-panel-button" />
            <ui:Button name="bottomButtonAdd" text="+" class="bottom-panel-button" />
        </ui:VisualElement>
    </ui:ScrollView>
</ui:VisualElement>
```

### UXML チェックリスト
- [ ] 親要素 name="bottomPanel" を追加（AvaterSlotでも可）
- [ ] name="avatarSlotBar" → name="bottomScrollView" に変更
- [ ] name="avatarSlotRow" → name="bottomButtonContainer" に変更
- [ ] 型を VisualElement → Button に変更
- [ ] name="bottomButton1" (最初のスロット)
- [ ] name="slotAddBtn" → name="bottomButtonAdd" に変更

### USS チェックリスト
- [ ] .bottom-panel: position absolute, bottom 220px
- [ ] .bottom-panel-button: 52x52px、border-radius 50%
- [ ] .bottom-panel-button.selected: 選択状態（青ボーダー）
- [ ] .bottom-panel-button.has-icon: サムネイル設定済み

---

## 5. アラートバー (alertBar) ※新規追加

### 追加が必要
```xml
<ui:VisualElement name="alertBar" class="alert-bar">
    <ui:Label name="alertMessage" class="alert-bar-message" />
    <ui:Button name="alertClose" text="x" class="alert-bar-close" />
</ui:VisualElement>
```

### UXML チェックリスト
- [ ] name="alertBar" を追加
- [ ] name="alertMessage" (Label型)
- [ ] name="alertClose" (Button型)

### USS チェックリスト
- [ ] .alert-bar: position absolute, display none（デフォルト非表示）
- [ ] .alert-bar.visible: display flex
- [ ] .alert-bar.warning: 黄色背景
- [ ] .alert-bar.error: 赤色背景
- [ ] .alert-bar.info: 青色背景

---

## 6. アスペクト比マスク ※新規追加

### 追加が必要
```xml
<ui:VisualElement name="topMask" class="aspect-mask" />
<ui:VisualElement name="bottomMask" class="aspect-mask" />
<ui:VisualElement name="leftMask" class="aspect-mask" />
<ui:VisualElement name="rightMask" class="aspect-mask" />
```

### UXML チェックリスト
- [ ] name="topMask" を追加
- [ ] name="bottomMask" を追加
- [ ] name="leftMask" を追加
- [ ] name="rightMask" を追加

### USS チェックリスト
- [ ] .aspect-mask: position absolute、C#で動的にサイズ設定

---

## 7. プレビュー系オーバーレイ ※新規追加

### 追加が必要
```xml
<!-- フラッシュ演出 -->
<ui:VisualElement name="flashOverlay" class="flash-overlay" />

<!-- ギャラリーサムネイル -->
<ui:VisualElement name="galleryThumbnail" class="gallery-thumbnail" />

<!-- 全画面プレビュー -->
<ui:VisualElement name="viewerOverlay" class="viewer-overlay">
    <ui:Image name="viewerImage" class="viewer-image" />
</ui:VisualElement>

<!-- アイコン確認パネル -->
<ui:VisualElement name="iconPreviewPanel" class="icon-preview-panel">
    <ui:VisualElement name="iconPreviewImage" class="icon-preview-image" />
    <ui:VisualElement class="icon-preview-button-container">
        <ui:Button name="iconPreviewRetake" text="Retake" class="icon-preview-button" />
        <ui:Button name="iconPreviewConfirm" text="Confirm" class="icon-preview-button" />
    </ui:VisualElement>
</ui:VisualElement>
```

### UXML チェックリスト
- [ ] name="flashOverlay" を追加
- [ ] name="galleryThumbnail" を追加
- [ ] name="viewerOverlay" を追加
- [ ] name="viewerImage" を追加 (Image型)
- [ ] name="iconPreviewPanel" を追加
- [ ] name="iconPreviewImage" を追加
- [ ] name="iconPreviewRetake" を追加 (Button型)
- [ ] name="iconPreviewConfirm" を追加 (Button型)

### USS チェックリスト
- [ ] .flash-overlay: 全画面、白背景、opacity 0
- [ ] .gallery-thumbnail: position absolute, bottom 90px, left 24px
- [ ] .viewer-overlay: 全画面、display none
- [ ] .icon-preview-panel: 全画面、display none
- [ ] .icon-preview-panel.visible: display flex

---

## 8. ライティングパネル (lightingPanelOverlay)

### 現状 (UITK_Pier)
```xml
<ui:VisualElement name="lightingPanel" class="lighting-panel">
    <ui:VisualElement name="lightingCloseBtn">...</ui:VisualElement>
    <ui:VisualElement name="autoSyncBtn">
        <ui:VisualElement name="autoSyncToggle" />
    </ui:VisualElement>
    ...
</ui:VisualElement>
```

### 変更後 (必須)
```xml
<ui:VisualElement name="lightingPanelOverlay" class="lighting-panel-overlay">
    <ui:VisualElement name="lightingPanel" class="lighting-panel">
        <ui:Button name="lightingPanelClose" text="x" />
        <ui:Toggle name="arSyncToggle" />
        <ui:Button name="presetAuto" text="Auto" />
        <ui:Button name="presetSunny" text="Sunny" />
        <ui:Button name="presetCloudy" text="Cloudy" />
        <ui:Button name="presetIndoor" text="Indoor" />
        <ui:Button name="presetWarm" text="Warm" />
        <ui:Button name="presetSunset" text="Sunset" />
        <ui:Slider name="colorTempSlider" low-value="2000" high-value="10000" />
        <ui:Label name="colorTempValue" />
        <ui:Slider name="brightnessSlider" low-value="0.1" high-value="2.0" />
        <ui:Label name="brightnessValue" />
        <ui:VisualElement name="lightDirectionBackground">
            <ui:VisualElement name="lightDirectionKnob" />
        </ui:VisualElement>
        <ui:Slider name="elevationSlider" direction="Vertical" />
        <ui:Label name="elevationValue" />
    </ui:VisualElement>
</ui:VisualElement>
```

### UXML チェックリスト
- [ ] 親要素 name="lightingPanelOverlay" を追加
- [ ] name="lightingCloseBtn" → name="lightingPanelClose" (Button型)
- [ ] name="autoSyncToggle" → name="arSyncToggle" (Toggle型に変更)
- [ ] プリセットボタンを Button型 に変更:
  - [ ] name="presetBtnAuto" → name="presetAuto"
  - [ ] name="presetBtnSunny" → name="presetSunny"
  - [ ] name="presetBtnCloudy" → name="presetCloudy"
  - [ ] name="presetBtnIndoor" → name="presetIndoor"
  - [ ] name="presetBtnWarm" → name="presetWarm"
  - [ ] name="presetBtnSunset" → name="presetSunset"
- [ ] スライダーを Slider型 に変更:
  - [ ] name="colorTemperatureSlider" → name="colorTempSlider" (Slider型)
  - [ ] name="colorTemperatureValue" → name="colorTempValue"
  - [ ] name="brightnessSlider" (Slider型)
  - [ ] name="brightnessValue"
- [ ] 方向コントロール:
  - [ ] name="directionPad" → name="lightDirectionBackground"
  - [ ] name="dirKnob" → name="lightDirectionKnob"
  - [ ] name="elevSlider" → name="elevationSlider" (Slider型)
  - [ ] name="elevationValue"

### USS チェックリスト
- [ ] .lighting-panel-overlay: display none
- [ ] .lighting-panel-overlay.visible: display flex
- [ ] .preset-button.preset-selected: 選択状態

---

## 9. シャドウパネル (shadowPanelOverlay)

### 現状 (UITK_Pier)
```xml
<ui:VisualElement name="shadowPanel" class="lighting-panel">
    <ui:VisualElement name="shadowCloseBtn">...</ui:VisualElement>
    <ui:VisualElement name="enableShadowToggle" />
    ...
</ui:VisualElement>
```

### 変更後 (必須)
```xml
<ui:VisualElement name="shadowPanelOverlay" class="shadow-panel-overlay">
    <ui:VisualElement name="shadowPanel" class="shadow-panel">
        <ui:Button name="shadowPanelClose" text="x" />
        <ui:Toggle name="shadowToggle" />
        <ui:Slider name="shadowIntensitySlider" />
        <ui:Label name="shadowIntensityValue" />
        <ui:Button name="softHard" text="Hard" />
        <ui:Button name="softMedium" text="Medium" />
        <ui:Button name="softSoft" text="Soft" />
    </ui:VisualElement>
</ui:VisualElement>
```

### UXML チェックリスト
- [ ] 親要素 name="shadowPanelOverlay" を追加
- [ ] name="shadowCloseBtn" → name="shadowPanelClose" (Button型)
- [ ] name="enableShadowToggle" → name="shadowToggle" (Toggle型)
- [ ] スライダー:
  - [ ] name="shadowIntensitySlider" (Slider型)
  - [ ] name="shadowIntensityValue"
- [ ] ソフトネスボタンを Button型 に変更:
  - [ ] name="softnessHard" → name="softHard"
  - [ ] name="softnessMedium" → name="softMedium"
  - [ ] name="softnessSoft" → name="softSoft"

### USS チェックリスト
- [ ] .shadow-panel-overlay: display none
- [ ] .shadow-panel-overlay.visible: display flex
- [ ] .softness-button.softness-selected: 選択状態

---

## 10. 設定パネル背景 ※新規追加

### 追加が必要
```xml
<ui:VisualElement name="settingsPanelBackdrop" class="lighting-panel-backdrop" />
```

### UXML チェックリスト
- [ ] name="settingsPanelBackdrop" を追加

### USS チェックリスト
- [ ] .lighting-panel-backdrop: 全画面、display none
- [ ] .lighting-panel-backdrop.visible: display flex、半透明背景

---

## 完了確認チェックリスト

### 必須要素ID (全69個)
- [ ] 撮影ボタン関連: 5個
- [ ] ギャラリー/プレビュー: 7個
- [ ] アラート: 3個
- [ ] 上部パネル: 6個
- [ ] サイドパネル: 5個
- [ ] アバタースロット: 4個
- [ ] アスペクト比マスク: 4個
- [ ] 設定パネル: 3個
- [ ] ライティングパネル: 18個
- [ ] シャドウパネル: 8個

### 必須状態CSSクラス (12個)
- [ ] visible
- [ ] recording
- [ ] active
- [ ] selected
- [ ] has-icon
- [ ] plane-visible / plane-hidden
- [ ] warning / error / info
- [ ] preset-selected
- [ ] softness-selected

### 型の確認
- [ ] Button型: topButton1-5, sideButton1-3, sideButtonBugReport, bottomButtonAdd, alertClose, iconPreviewRetake, iconPreviewConfirm, presetAuto-Sunset, softHard/Medium/Soft, lightingPanelClose, shadowPanelClose
- [ ] Toggle型: arSyncToggle, shadowToggle
- [ ] Slider型: colorTempSlider, brightnessSlider, elevationSlider, shadowIntensitySlider
- [ ] Label型: alertMessage, colorTempValue, brightnessValue, elevationValue, shadowIntensityValue
- [ ] Image型: viewerImage
- [ ] ScrollView型: bottomScrollView

---

## 変更履歴

| 日付 | バージョン | 変更内容 |
|------|-----------|----------|
| 2026-01-16 | 1.0 | 初版作成 |
