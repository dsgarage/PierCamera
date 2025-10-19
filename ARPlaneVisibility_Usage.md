# ARPlaneVisibilityController 使用ガイド

## 概要

AR平面検知プレートの表示/非表示を制御するクラスです。
UIのToggleやCheckboxと簡単に連携できる設計になっています。

## セットアップ方法

### 1. スクリプトのアタッチ

1. Hierarchyで `XR Origin` (または AR Session Origin) を選択
2. Inspector で `Add Component` をクリック
3. `ARPlaneVisibilityController` を検索して追加

### 2. 参照の設定

Inspector で以下を設定:

- **Plane Manager**: `AR Plane Manager` への参照（空欄の場合は自動検索）
- **Show Planes On Start**: 起動時に平面を表示するか（デフォルト: true）
- **Disable Detection When Hidden**: 非表示時に検知自体を停止するか（デフォルト: false）

### 3. UIとの連携

#### 方法1: Toggle と連携（推奨）

```csharp
// UI Toggle の OnValueChanged イベントに設定
using UnityEngine;
using UnityEngine.UI;

public class ARSettingsUI : MonoBehaviour
{
    [SerializeField] private Toggle planeVisibilityToggle;
    [SerializeField] private ARPlaneVisibilityController planeController;

    void Start()
    {
        // Toggleの初期状態を設定
        planeVisibilityToggle.isOn = planeController.IsPlanesVisible;

        // イベント登録
        planeVisibilityToggle.onValueChanged.AddListener(OnPlaneVisibilityChanged);
    }

    void OnPlaneVisibilityChanged(bool isOn)
    {
        planeController.SetPlanesVisible(isOn);
    }

    void OnDestroy()
    {
        planeVisibilityToggle.onValueChanged.RemoveListener(OnPlaneVisibilityChanged);
    }
}
```

#### 方法2: Inspector から直接設定

1. UI Canvas に Toggle を配置
2. Toggle の Inspector で `On Value Changed (Boolean)` を展開
3. `+` ボタンをクリック
4. XR Origin オブジェクトをドラッグ＆ドロップ
5. ドロップダウンから `ARPlaneVisibilityController > SetPlanesVisible` を選択

#### 方法3: ボタンで切り替え

```csharp
// ボタンクリックで表示/非表示を切り替え
[SerializeField] private Button toggleButton;
[SerializeField] private ARPlaneVisibilityController planeController;

void Start()
{
    toggleButton.onClick.AddListener(() =>
    {
        planeController.TogglePlanesVisibility();
    });
}
```

## 主な機能

### Public API

#### SetPlanesVisible(bool visible)
平面プレートの表示/非表示を設定します。

```csharp
// 表示
planeController.SetPlanesVisible(true);

// 非表示
planeController.SetPlanesVisible(false);
```

#### TogglePlanesVisibility()
現在の表示状態を反転します。

```csharp
planeController.TogglePlanesVisibility();
```

#### IsPlanesVisible (プロパティ)
現在の表示状態を取得します。

```csharp
bool isVisible = planeController.IsPlanesVisible;
```

## 設定オプション

### Show Planes On Start
- **true**: アプリ起動時に平面プレートを表示
- **false**: アプリ起動時は非表示（ユーザーが手動でON）

### Disable Detection When Hidden
- **false** (推奨): 非表示でも平面検知は継続（タップ配置は可能）
- **true**: 非表示時は平面検知自体を停止（パフォーマンス優先）

## 使用例

### 例1: 設定メニューでON/OFF

```csharp
public class SettingsMenu : MonoBehaviour
{
    [SerializeField] private Toggle planeToggle;
    [SerializeField] private ARPlaneVisibilityController planeController;

    void Start()
    {
        // 現在の状態を反映
        planeToggle.isOn = planeController.IsPlanesVisible;
        planeToggle.onValueChanged.AddListener(planeController.SetPlanesVisible);
    }
}
```

### 例2: 撮影モード時は非表示

```csharp
public class CameraMode : MonoBehaviour
{
    [SerializeField] private ARPlaneVisibilityController planeController;

    public void EnterPhotoMode()
    {
        // 撮影モードに入ったら平面を非表示
        planeController.SetPlanesVisible(false);
    }

    public void ExitPhotoMode()
    {
        // 撮影モードを抜けたら平面を表示
        planeController.SetPlanesVisible(true);
    }
}
```

### 例3: 初回配置後は自動で非表示

```csharp
public class FirstPlacement : MonoBehaviour
{
    [SerializeField] private ARPlaneVisibilityController planeController;
    [SerializeField] private PlaceAvatarOnPlaneOnly avatarPlacer;

    private bool hasPlacedAvatar = false;

    void Update()
    {
        // アバターが配置されたら平面を非表示
        if (!hasPlacedAvatar && avatarPlacer.HasAvatar)
        {
            hasPlacedAvatar = true;
            planeController.SetPlanesVisible(false);
        }
    }
}
```

## UIデザイン例

### シンプルなToggle

```
┌─────────────────────────┐
│ ☑ 平面を表示            │
└─────────────────────────┘
```

### 設定パネル

```
┌─────────────────────────┐
│  AR設定                 │
├─────────────────────────┤
│ ☑ 平面検知プレート      │
│ ☐ オクルージョン        │
│ ☑ ライティング推定      │
└─────────────────────────┘
```

## トラブルシューティング

### 平面が表示されない

1. **ARPlaneManager が有効か確認**
   - XR Origin の Inspector で AR Plane Manager が enabled になっているか

2. **平面検知が有効か確認**
   - `Disable Detection When Hidden` が true の場合、SetPlanesVisible(true) を呼んでいるか

3. **マテリアルが設定されているか**
   - AR Plane Manager の Plane Prefab にマテリアルが割り当てられているか

### 非表示にしてもタップ配置ができない

- `Disable Detection When Hidden` を **false** に設定してください
- false の場合、検知は継続し表示のみOFFになります

### パフォーマンスが気になる

- `Disable Detection When Hidden` を **true** に設定
- 平面検知自体を停止するため、CPU負荷を削減できます
- ただし、非表示時はタップ配置もできなくなります

## 参照関係の図

```
UI (Toggle/Button)
    │
    ↓ onValueChanged / onClick
ARPlaneVisibilityController
    │
    ↓ planeManager reference
ARPlaneManager
    │
    ↓ trackables (ARPlanes)
各 ARPlane
    └─ MeshRenderer (表示制御対象)
    └─ LineRenderer (輪郭線制御対象)
```

## 注意事項

- UnityEditor上では平面検知は動作しません（実機でテスト）
- MeshColliderは無効化されません（Raycastを継続するため）
- 表示/非表示の切り替えは即座に反映されます
