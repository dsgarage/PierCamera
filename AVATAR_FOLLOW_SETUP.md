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

### 2. アバターの設定（自動セットアップ）

アバターは **自動的にセットアップ** されます。Prefabでの事前設定は不要です。

#### 方法1: FBXから動的にロード（推奨）

FBXからAssimpなどで動的にロードする場合：

```csharp
// アバターをロード（Assimp等）
GameObject avatar = LoadAvatarFromFBX(fbxPath);

// 自動セットアップは不要！
// PlanePlacementController または PlaceAvatarOnPlaneOnly が
// Instantiate時に自動的に AvatarAutoSetup.Setup(avatar) を呼び出します
```

**内部動作:**
- `PlanePlacementController.PlaceAvatar()` または `PlaceAvatarOnPlaneOnly` でInstantiate時
- `AvatarAutoSetup` コンポーネントがアタッチされていない場合
- 自動的に `AvatarAutoSetup.Setup(avatar)` が呼ばれる
- 以下が自動実行される：
  - `AvatarFollowController` の追加と参照の自動設定
  - `BoxCollider` の追加（デフォルトサイズ: 0.5x1.8x0.5）
  - ARRaycastManager、ARPlaneManager、MainCamera の自動検索と設定

#### 方法2: Prefabに事前にコンポーネントをアタッチ

Prefabを使う場合は、事前に `AvatarAutoSetup` をアタッチできます：

1. **AvatarAutoSetup コンポーネントを追加**
   - アバターPrefabのルートに `AvatarAutoSetup` (Assets/Scripts/AR/AvatarAutoSetup.cs) を追加

2. **AvatarAutoSetup の Inspector 設定**

   **Follow Settings:**
   - `Desired Distance`: 1.5（維持する距離）
   - `Pos Lerp`: 0.15（位置の補間速度）
   - `Rot Lerp`: 0.15（回転の補間速度）

   **Collider Settings:**
   - `Auto Add Collider`: チェック（自動でColliderを追加）
   - `Collider Size`: アバターのサイズに合わせて調整（例: 0.5, 1.8, 0.5）
   - `Collider Center`: Colliderの中心位置（例: 0, 0.9, 0）

   **Layer Settings:**
   - `Avatar Layer Name`: レイヤー名を入力（例: "ARAvatar"）
     - 空欄の場合はレイヤー変更なし
     - レイヤーが存在しない場合は警告が出ます

3. **自動セットアップ内容**

   インスタンス化時（Awake）に以下が自動実行されます：
   - `AvatarFollowController` の追加と参照の自動設定
   - `BoxCollider` の追加（サイズは設定値に基づく）
   - レイヤーの設定（指定した場合）
   - ARRaycastManager、ARPlaneManager、MainCamera の自動検索と設定

#### 方法3: カスタム設定で動的セットアップ

コードから直接セットアップする場合：

```csharp
// アバターをロード
GameObject avatar = LoadAvatarFromFBX(fbxPath);

// カスタム設定でセットアップ
var config = new AR.AvatarAutoSetup.SetupConfig
{
    desiredDistance = 2.0f,  // 2mの距離を維持
    colliderSize = new Vector3(0.6f, 2.0f, 0.6f),  // カスタムサイズ
    avatarLayerName = "ARAvatar"  // レイヤー指定
};

AR.AvatarAutoSetup.Setup(avatar, config);
```

#### （オプション）ARAvatar レイヤーの作成

タップ検出を最適化したい場合：
- Project Settings > Tags and Layers で `ARAvatar` レイヤーを作成
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

- アバター自動セットアップ: `[AvatarAutoSetup] Setting up avatar: ...`
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
- `AvatarAutoSetup` が正しくアタッチされているか確認
- コンソールに `[AvatarAutoSetup] Setting up avatar` ログが出ているか確認
- 手動セットアップの場合: `AvatarFollowController` の参照が全て設定されているか確認
- コンソールにエラーログが出ていないか確認

### 自動セットアップが失敗する
- ARRaycastManager、ARPlaneManager、MainCamera がシーンに存在するか確認
- コンソールに警告ログが出ていないか確認
- 必要に応じて手動セットアップに切り替え

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
