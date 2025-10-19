# アバター追従機能セットアップガイド

## 概要

この機能は、ARアバターとカメラの距離を固定する2通りの追従モードを実装します。

### 追従モード

1. **ModeA: 平面固定追従（PlaneLocked）**
   - 平面に座標を固定したまま、カメラとの距離を維持
   - アバターが平面上を滑るように移動

2. **ModeB: カメラ固定追従（CameraLocked）**
   - 平面を無視して、カメラとの相対位置（距離・方位）を維持
   - カメラの動きに完全に追従

3. **ModeOff: 追従なし**
   - アバターはその場に固定

## セットアップ手順

### 1. Hierarchyの設定

#### XR Origin（またはAR Session Origin）
既存のXR Originに以下のコンポーネントがアタッチされていることを確認してください：

- `ARRaycastManager`
- `ARPlaneManager`

#### AR Session
- `ARSession` コンポーネントがアタッチされていることを確認

#### Input Manager（新規作成）
1. Hierarchy で空のGameObject を作成（名前: `InputManager`）
2. 以下のコンポーネントをアタッチ：
   - `TouchRouter` (Assets/Scripts/AR/Input/TouchRouter.cs)
   - `AvatarTapHandler` (Assets/Scripts/AR/AvatarTapHandler.cs)
   - `PlanePlacementController` (Assets/Scripts/AR/PlanePlacementController.cs)

#### InputManager の Inspector 設定

**TouchRouter:**
- `Double Tap Max Interval Sec`: 0.3
- `Double Tap Max Move Pixels`: 20

**AvatarTapHandler:**
- `AR Camera`: MainCamera（XR Origin配下のカメラ）を参照
- `Placement Controller`: InputManagerの `PlanePlacementController` を参照
- `Avatar Layer Mask`: アバター検出用レイヤー（デフォルトは全レイヤー）
- `Max Raycast Distance`: 100

**PlanePlacementController:**
- `Placed Prefab`: 配置するアバターのPrefabを設定
- `Raycast Manager`: XR Originの `ARRaycastManager` を参照
- `Plane Manager`: XR Originの `ARPlaneManager` を参照
- `AR Camera`: MainCamera を参照

### 2. アバターPrefabの設定

配置するアバターPrefabに以下を設定してください：

1. **AvatarFollowController コンポーネントを追加**
   - `Mode`: Off（初期値）
   - `Desired Distance`: 1.5（維持する距離）
   - `Pos Lerp`: 0.15（位置の補間速度）
   - `Rot Lerp`: 0.15（回転の補間速度）
   - `Raycaster`: XR Originの `ARRaycastManager` を参照
   - `Plane Manager`: XR Originの `ARPlaneManager` を参照
   - `AR Camera`: MainCamera を参照

2. **Collider を追加**（タップ検出用）
   - `BoxCollider` または `SphereCollider` を追加
   - サイズはアバター全体をカバーする程度に設定
   - `Is Trigger`: チェック不要

3. **（オプション）Layer 設定**
   - Project Settings > Tags and Layers で `ARAvatar` レイヤーを作成
   - アバターPrefabのLayerを `ARAvatar` に設定
   - `AvatarTapHandler` の `Avatar Layer Mask` を `ARAvatar` のみに限定

### 3. Input System の設定

このスクリプトは Unity Input System の Enhanced Touch Support を使用しています。

1. **Package Manager から Input System をインストール**
   - Window > Package Manager
   - Input System を検索してインストール

2. **Project Settings の設定**
   - Edit > Project Settings > Player
   - Active Input Handling を `Both` または `Input System Package (New)` に設定

## 使い方

### シーン実行時の操作

1. **アプリ起動**
   - カメラで周囲をスキャンして平面を検出

2. **アバター配置**
   - 画面を**シングルタップ**すると、その位置にアバターが配置されます
   - 再度シングルタップすると、アバターが新しい位置に移動します

3. **追従モード切替**
   - アバターを**ダブルタップ**すると、追従モードが以下の順で切り替わります：
     - Off → PlaneLocked → CameraLocked → Off → ...

### 各モードの挙動

#### Off（追従なし）
- アバターは配置された位置に固定
- カメラを動かしてもアバターは動かない

#### PlaneLocked（平面固定追従）
- カメラとアバターの**水平距離**を維持
- アバターは平面上を滑るように移動
- カメラを前後左右に動かすと、アバターも同じ距離を保って移動
- 常にカメラの方を向く

#### CameraLocked（カメラ固定追従）
- カメラとアバターの**相対位置**を完全に固定
- カメラの動きに完全に追従（前後左右・回転すべて）
- 平面を無視して空中でも追従
- 常にカメラの方を向く

## デバッグ

### Gizmos表示
エディタでシーン実行中、以下のGizmosが表示されます：

- **PlaneLocked モード（緑色）**: アバターと紐付けられた平面中心への線
- **CameraLocked モード（青色）**: アバターとカメラへの線

### ログ出力
以下のイベント時にログが出力されます：

- アバター配置: `[PlanePlacementController] Avatar placed at ...`
- モード切替: `[AvatarFollowController] Mode changed: ...`
- 平面バインド: `[AvatarFollowController] Bound to plane ...`
- カメラバインド: `[AvatarFollowController] Bound to camera ...`

## トラブルシューティング

### アバターが配置できない
- `ARPlaneManager` で平面が検出されているか確認
- `PlanePlacementController` の `Placed Prefab` が設定されているか確認
- `ARRaycastManager` が有効になっているか確認

### ダブルタップが反応しない
- アバターPrefabに `Collider` が付いているか確認
- `AvatarTapHandler` の `Avatar Layer Mask` 設定を確認
- タップ間隔が0.3秒以内か確認（設定変更可能）

### 追従モードが動作しない
- `AvatarFollowController` の参照が全て設定されているか確認
- `AR Camera` が正しく参照されているか確認
- コンソールにエラーログが出ていないか確認

### 平面が消えてモードがOffになる
- ARPlaneが統合（subsume）された場合は自動で親平面に追従します
- 平面が完全に消失した場合は安全のため自動的にOffモードになります

## パフォーマンス最適化

- GCアロケーションを避けるため、リストを再利用しています
- Raycastは必要な時のみ実施（タップ時とModeA追従時）
- 補間（Lerp/Slerp）により滑らかな動きを実現

## 今後の拡張

以下の機能を追加できます：

- UIトグルボタンでのモード切替
- 長押しで距離調整
- アバターの回転追従モード（Look-at Camera）
- 複数アバター対応
- モード切替時のUI Toast表示
