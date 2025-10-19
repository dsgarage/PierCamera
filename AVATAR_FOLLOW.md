# アバター追従機能

## 概要

`PlaceAvatarOnPlaneOnly.cs` に統合されたシンプルな追従機能です。

## 使い方

### 1. アバター配置

- 床や机など平面を検出
- **画面をタップ** してアバターを配置

### 2. 追従モード切替

- **アバターの近くを素早く2回タップ**（ダブルタップ）
- モードが切り替わります：
  - **Off** → **PlaneLocked** → **CameraLocked** → **Off** → ...

### 3. 各モードの挙動

#### Off（追従なし）
- アバターはその場に固定
- カメラを動かしてもアバターは動かない

#### PlaneLocked（平面追従）
- カメラとの**水平距離**を維持
- アバターが平面上を滑るように移動
- 常にカメラの方を向く

#### CameraLocked（カメラ追従）
- カメラとの**相対位置**を完全に固定
- カメラの動きに完全に追従
- 常にカメラの方を向く

## Inspector設定

`PlaceAvatarOnPlaneOnly` の Inspector で以下を調整できます：

### Avatar Follow (追従機能)

- **Enable Follow Mode**: 追従機能を有効化（デフォルト: ON）
- **Double Tap Interval**: ダブルタップの最大間隔（デフォルト: 0.3秒）
- **Follow Distance**: 維持する距離（デフォルト: 1.5m）
- **Follow Smoothness**: 追従の滑らかさ（デフォルト: 0.15）

## 実装の特徴

- **シンプル**: 既存の `PlaceAvatarOnPlaneOnly.cs` に統合
- **Input.touchCount 使用**: 複雑な Input System 不要
- **依存なし**: 追加のコンポーネントやスクリプト不要
- **軽量**: 約100行の追加コード

## デバッグ

コンソールログで現在のモードを確認できます：

```
[PlaceAvatarOnPlaneOnly] Follow Mode: Off
[PlaceAvatarOnPlaneOnly] Follow Mode: PlaneLocked (平面追従)
[PlaceAvatarOnPlaneOnly] Follow Mode: CameraLocked (カメラ追従)
```
