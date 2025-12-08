# Changelog

All notable changes to PierCamera will be documented in this file.

## [v0.6.1] - 2025-12-08

### Bug Fixes

- **#397**: Lightingパネルで初回Preset選択時にエラー[W120]が表示される問題を修正
  - グローバルシェーダープロパティとUnityライトで十分に機能するため、不要な警告を削除
  - `LightingPanelController.cs`から`warningShown`フラグと関連ロジックを削除

- **#74**: Light Estimation機能のUI改善
  - Light Direction UIの中心ズレを修正
  - EWSN方向ラベルの配置とサイズを調整

- **#346**: バックグラウンド復帰時のアバター再ロード問題を修正
  - アプリをバックグラウンドから復帰した際にスロットのアバターが再ロードできなくなる問題を解決

### Changed Files

- `aiCam/Assets/Scripts/UI/Lighting/LightingPanelController.cs`
- `aiCam/Assets/UI/CameraCapture/LightingPanel.uss`

---

## [v0.6.0] - 2025-12-05

### Features

- Light Estimation機能の実装 (#74)
- Lightingパネル/Shadowパネル UI実装 (#120)
- AR平面シャドウレシーバー (#75)

### Bug Fixes

- FBX/VRMローダーの各種修正
- マテリアル重複検出と削除
- lilToonシェーダー対応改善
