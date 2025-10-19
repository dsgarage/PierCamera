# Avatar Follow System 技術仕様

アバターをカメラに追従させ、距離固定やスワイプ操作による距離・回転調整を提供するアバター追従システムの実装仕様です。

## システム概要

アバター追従システムは、ARアプリケーションにおいてアバターとカメラの関係を動的に制御し、ユーザーに直感的な操作体験を提供します。

### 主要機能
1. **3つの追従モード**: Off（固定）、PlaneLocked（平面追従）、CameraLocked（カメラ追従）
2. **スワイプによる距離調整**: 上下スワイプでアバターとの距離を変更
3. **スワイプによる回転調整**: 左右スワイプでアバターを回転
4. **ダブルタップでモード切替**: 簡単な操作でモードを切り替え

## システム構成

### 実装クラス

**PlaceAvatarOnPlaneOnly.cs**
- アバターの配置と追従機能を統合管理
- AR平面検出とRaycast処理
- タッチ入力の検出と処理
- 追従モードの管理と更新

```csharp
[RequireComponent(typeof(ARRaycastManager))]
public sealed class PlaceAvatarOnPlaneOnly : MonoBehaviour
{
    // 追従モード
    enum FollowMode { Off, PlaneLocked, CameraLocked }
    FollowMode currentFollowMode = FollowMode.Off;

    // 距離と回転の制御
    float followDistance = 1.5f;
    float avatarRotationY = 0f;
}
```

## 追従モード詳細

### Off（固定モード）

**説明**: アバターは完全に固定され、カメラを動かしても位置が変わりません。

**用途**:
- アバターを特定の位置に配置したい場合
- スワイプで距離や回転を調整する場合

**挙動**:
```csharp
void UpdateFollowMode()
{
    if (currentFollowMode == FollowMode.Off || !avatar || !arCamera)
        return;  // 何もしない（固定）
}
```

### PlaneLocked（平面追従モード）

**説明**: アバターはカメラとの水平距離を維持しながら、配置された平面上を滑るように移動します。

**特徴**:
- カメラの前方方向にアバターが追従
- 平面上に投影されるため、高さは平面に依存
- 常にカメラの方を向く

**実装**:
```csharp
void UpdatePlaneLocked()
{
    if (!avatarPlane) return;

    Vector3 camPos = arCamera.transform.position;
    Vector3 camForwardFlat = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up).normalized;

    // カメラから followDistance 離れた位置
    Vector3 targetPosFlat = camPos + camForwardFlat * followDistance;

    // 平面上に投影
    Vector3 planeCenter = avatarPlane.center;
    Vector3 planeNormal = avatarPlane.normal;
    float distance = Vector3.Dot(planeNormal, targetPosFlat - planeCenter);
    Vector3 targetPos = targetPosFlat - planeNormal * distance;

    // 滑らかに移動
    avatar.transform.position = Vector3.Lerp(avatar.transform.position, targetPos, followSmoothness);

    // カメラを向く（手動回転を考慮）
    Vector3 lookDir = camPos - avatar.transform.position;
    lookDir.y = 0;
    if (lookDir.sqrMagnitude > 0.01f)
    {
        Quaternion baseLookRot = Quaternion.LookRotation(lookDir);
        if (Mathf.Abs(avatarRotationY) > 0.1f)
        {
            Quaternion manualRot = Quaternion.Euler(0, avatarRotationY, 0);
            avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot * manualRot, followSmoothness);
        }
        else
        {
            avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot, followSmoothness);
        }
    }
}
```

**数学的説明**:
1. カメラの前方ベクトルを水平面に投影: `Vector3.ProjectOnPlane(forward, Vector3.up)`
2. 目標位置を計算: `camPos + camForwardFlat * followDistance`
3. 平面法線との内積で平面までの距離を計算: `Vector3.Dot(planeNormal, targetPosFlat - planeCenter)`
4. 平面上に投影: `targetPosFlat - planeNormal * distance`

### CameraLocked（カメラ追従モード）

**説明**: アバターはカメラとの相対位置を完全に固定し、カメラの動きに完全に追従します。

**特徴**:
- カメラ相対座標系で位置を維持
- 平面を無視してカメラに追従
- 常にカメラの方を向く

**実装**:
```csharp
void UpdateCameraLocked()
{
    if (!arCamera || !avatar) return;

    Vector3 camPos = arCamera.transform.position;
    float camYaw = arCamera.transform.eulerAngles.y;
    Quaternion camRot = Quaternion.Euler(0, camYaw, 0);

    // カメラ相対位置を維持
    Vector3 targetPos = camPos + (camRot * cameraLocalOffset.normalized) * followDistance;

    // 滑らかに移動
    avatar.transform.position = Vector3.Lerp(avatar.transform.position, targetPos, followSmoothness);

    // カメラを向く（手動回転を考慮）
    Vector3 lookDir = camPos - avatar.transform.position;
    lookDir.y = 0;
    if (lookDir.sqrMagnitude > 0.01f)
    {
        Quaternion baseLookRot = Quaternion.LookRotation(lookDir);
        if (Mathf.Abs(avatarRotationY) > 0.1f)
        {
            Quaternion manualRot = Quaternion.Euler(0, avatarRotationY, 0);
            avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot * manualRot, followSmoothness);
        }
        else
        {
            avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot, followSmoothness);
        }
    }
}
```

**カメラ相対オフセットの計算**:
```csharp
// モード切替時にオフセットを計算
Vector3 camPos = arCamera.transform.position;
Vector3 avatarPos = avatar.transform.position;
float camYaw = arCamera.transform.eulerAngles.y;
Quaternion invCamRot = Quaternion.Inverse(Quaternion.Euler(0, camYaw, 0));
cameraLocalOffset = invCamRot * (avatarPos - camPos);
```

## タッチ操作

### ダブルタップ検出

**仕様**: 0.3秒以内に50ピクセル以内の範囲で2回タップ

**実装**:
```csharp
bool CheckDoubleTap(Vector2 position)
{
    float currentTime = Time.time;

    if (currentTime - lastTapTime <= doubleTapInterval &&
        Vector2.Distance(lastTapPosition, position) < 50f)
    {
        lastTapTime = -1f; // リセット
        return true; // ダブルタップ検出
    }

    lastTapTime = currentTime;
    lastTapPosition = position;
    return false;
}
```

**パラメータ**:
- `doubleTapInterval`: 0.3秒（Inspector で調整可能）
- 距離閾値: 50ピクセル（固定）

### モード切替

**操作**: アバターをダブルタップ

**遷移サイクル**:
```
Off → PlaneLocked → CameraLocked → Off → ...
```

**実装**:
```csharp
void ToggleFollowMode()
{
    switch (currentFollowMode)
    {
        case FollowMode.Off:
            currentFollowMode = FollowMode.PlaneLocked;
            avatarRotationY = 0f;  // 回転リセット
            Debug.Log("[PlaceAvatarOnPlaneOnly] Follow Mode: PlaneLocked (平面追従)");
            break;
        case FollowMode.PlaneLocked:
            currentFollowMode = FollowMode.CameraLocked;
            avatarRotationY = 0f;  // 回転リセット
            // カメラ相対オフセットを計算
            break;
        case FollowMode.CameraLocked:
            currentFollowMode = FollowMode.Off;
            Debug.Log("[PlaceAvatarOnPlaneOnly] Follow Mode: Off (固定)");
            break;
    }
}
```

## スワイプインタラクション

### 上下スワイプ: 距離調整

**操作**:
- **上にスワイプ**: アバターが遠ざかる
- **下にスワイプ**: アバターが近づく

**動作条件**:
- 追従モードが Off 以外（PlaneLocked または CameraLocked）
- 縦方向の移動量が横方向より大きい

**実装**:
```csharp
void HandleSwipeInteraction()
{
    if (Input.touchCount == 0) return;
    Touch touch = Input.GetTouch(0);

    switch (touch.phase)
    {
        case TouchPhase.Moved:
            if (!isSwipeActive) return;

            Vector2 delta = touch.position - swipeStartPosition;

            // 上下スワイプ: 距離調整
            if (enableSwipeDistance && Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
            {
                // 上にスワイプ(+Y) = 遠くに、下にスワイプ(-Y) = 近くに
                float distanceDelta = -delta.y / swipeDistanceSensitivity;
                followDistance = Mathf.Clamp(followDistance + distanceDelta, minDistance, maxDistance);

                Debug.Log($"[PlaceAvatarOnPlaneOnly] Swipe distance adjust: {followDistance:F2}m");
            }

            swipeStartPosition = touch.position;
            break;
    }
}
```

**パラメータ**:
- `swipeDistanceSensitivity`: 200px/m（Inspector で調整可能）
- `minDistance`: 0.5m（Inspector で調整可能）
- `maxDistance`: 5.0m（Inspector で調整可能）

**計算式**:
```
新しい距離 = 現在の距離 + (-スワイプY / 感度)
最終距離 = Clamp(新しい距離, 最小値, 最大値)
```

### 左右スワイプ: 回転調整

**操作**:
- **左右にスワイプ**: アバターがY軸で回転

**動作条件**:
- 追従モードが Off 以外（PlaneLocked または CameraLocked）
- 横方向の移動量が縦方向より大きい

**実装**:
```csharp
// 左右スワイプ: 回転
else if (enableSwipeRotation && Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
{
    float rotationDelta = delta.x * swipeRotationSensitivity;
    avatarRotationY += rotationDelta;

    // -180〜180度に正規化
    while (avatarRotationY > 180f) avatarRotationY -= 360f;
    while (avatarRotationY < -180f) avatarRotationY += 360f;

    Debug.Log($"[PlaceAvatarOnPlaneOnly] Swipe rotation adjust: {avatarRotationY:F1}°");
}
```

**パラメータ**:
- `swipeRotationSensitivity`: 0.3°/px（Inspector で調整可能）

**回転の適用**:
```csharp
// カメラを向く方向を基準に手動回転を加算
Quaternion baseLookRot = Quaternion.LookRotation(lookDir);
Quaternion manualRot = Quaternion.Euler(0, avatarRotationY, 0);
avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot * manualRot, followSmoothness);
```

## Inspector 設定

### Avatar Follow (追従機能)

| パラメータ | 型 | デフォルト | 説明 |
|-----------|-----|----------|------|
| Enable Follow Mode | bool | true | 追従機能の有効/無効 |
| Double Tap Interval | float | 0.3 | ダブルタップの最大間隔（秒） |
| Follow Distance | float | 1.5 | 維持する距離（メートル） |
| Follow Smoothness | float | 0.15 | 追従の滑らかさ（0-1） |
| Enable Debug Log | bool | true | デバッグログの表示 |

### Avatar Interaction (アバター操作)

| パラメータ | 型 | デフォルト | 説明 |
|-----------|-----|----------|------|
| Enable Swipe Distance | bool | true | スワイプ距離調整の有効/無効 |
| Enable Swipe Rotation | bool | true | スワイプ回転の有効/無効 |
| Swipe Distance Sensitivity | float | 200 | 距離感度（ピクセル/メートル） |
| Swipe Rotation Sensitivity | float | 0.3 | 回転感度（度/ピクセル） |
| Min Distance | float | 0.5 | 距離の最小値（メートル） |
| Max Distance | float | 5.0 | 距離の最大値（メートル） |

## 使用方法

### 基本的な使い方

1. **アバターを配置**
   - AR平面検出後、平面をタップしてアバターを配置
   - 初期状態は Off（固定）モード

2. **追従モードを有効化**
   - アバターをダブルタップ
   - PlaneLocked（平面追従）モードに切り替わる

3. **距離を調整**
   - 画面を上下にスワイプして距離を変更
   - 上スワイプで遠く、下スワイプで近く

4. **回転を調整**
   - 画面を左右にスワイプしてアバターを回転

5. **モードを切り替え**
   - ダブルタップでモードを順次切り替え
   - PlaneLocked → CameraLocked → Off → ...

### 使用例: プログラムからの制御

```csharp
public class AvatarController : MonoBehaviour
{
    private PlaceAvatarOnPlaneOnly placeScript;

    void Start()
    {
        placeScript = GetComponent<PlaceAvatarOnPlaneOnly>();
    }

    // 距離を設定
    public void SetDistance(float distance)
    {
        // Inspector の followDistance を直接変更
        // または、public プロパティを追加して制御
    }
}
```

## パフォーマンス最適化

### スムージング処理

**Lerp/Slerp の使用**:
```csharp
// 位置の補間
avatar.transform.position = Vector3.Lerp(current, target, followSmoothness);

// 回転の補間
avatar.transform.rotation = Quaternion.Slerp(current, target, followSmoothness);
```

**followSmoothness の調整**:
- 小さい値（0.1）: よりスムーズだが追従が遅い
- 大きい値（0.5）: 追従が速いがカクつく可能性
- 推奨値: 0.15（バランスが良い）

### フレームレート対応

**30フレームごとのログ出力**:
```csharp
if (enableDebugLog && Time.frameCount % 30 == 0)
{
    Debug.Log($"Distance={currentDistance:F2}m (target={followDistance:F2}m)");
}
```

### タッチ処理の最適化

**UI タッチの早期リターン**:
```csharp
// UI上のタッチは無視
if (IsTouchOverUI(touch)) return;
if (EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return;
```

## デバッグログ

### 起動ログ

```
[PlaceAvatarOnPlaneOnly] Awake - Debug logging enabled: true
[PlaceAvatarOnPlaneOnly] Initialized - FollowMode: true, Distance: 1.50m
```

### タッチ操作ログ

```
[PlaceAvatarOnPlaneOnly] Touch detected at (512.0, 768.0), phase: Began
[PlaceAvatarOnPlaneOnly] Single tap - checking for plane at (512.0, 768.0)
[PlaceAvatarOnPlaneOnly] Plane hit detected: XXXX-YYYY, alignment: HorizontalUp
[PlaceAvatarOnPlaneOnly] Avatar placed at (0.0, 0.0, 1.5). Tap twice to toggle follow mode.
```

### モード切替ログ

```
[PlaceAvatarOnPlaneOnly] Double tap detected! Toggling follow mode...
[PlaceAvatarOnPlaneOnly] Follow Mode: PlaneLocked (平面追従) - Current distance: 1.52m
[PlaceAvatarOnPlaneOnly] Follow Mode: CameraLocked (カメラ追従) - Offset: (0.0, 0.0, 1.5), CamYaw: 45.0°
[PlaceAvatarOnPlaneOnly] Follow Mode: Off (固定)
```

### スワイプ操作ログ

```
[PlaceAvatarOnPlaneOnly] Swipe distance adjust: 2.15m (delta: 0.15m)
[PlaceAvatarOnPlaneOnly] Swipe rotation adjust: 45.3° (delta: 5.2°)
```

### 追従中のログ（30フレームごと）

```
[PlaceAvatarOnPlaneOnly] PlaneLocked: Distance=1.48m (target=1.50m), Horizontal=1.50m
[PlaceAvatarOnPlaneOnly] CameraLocked: Distance=1.52m (target=1.50m), CamYaw=90.0°
```

## トラブルシューティング

### よくある問題と解決方法

#### 追従モードが切り替わらない

**原因**: ダブルタップの間隔が長すぎる、または離れすぎている

**解決**:
```csharp
// Inspector で調整
doubleTapInterval = 0.5f;  // より長い間隔を許容
```

#### スワイプが反応しない

**原因**: UI要素がタッチをブロックしている

**確認**:
```csharp
Debug.Log($"IsTouchOverUI: {IsTouchOverUI(touch)}");
Debug.Log($"IsPointerOverGameObject: {EventSystem.current.IsPointerOverGameObject(touch.fingerId)}");
```

**解決**: `touchBlockAreas` に UI の RectTransform を登録

#### アバターが平面から外れる

**原因**: 平面検出が不安定

**解決**:
- 良好な照明環境を確保
- テクスチャのある平面を使用
- `ARAnchorManager` を使用してアンカーで固定

#### 距離調整が効かない

**原因**: `enableSwipeDistance` が無効、またはモードが Off

**確認**:
```csharp
Debug.Log($"Current mode: {currentFollowMode}");
Debug.Log($"Swipe distance enabled: {enableSwipeDistance}");
```

## 技術的制約

### AR Foundation の制約

1. **平面検出の制約**
   - 水平面のみ対応（`PlaneDetectionMode.Horizontal`）
   - 十分な照明が必要
   - テクスチャのある表面が必要

2. **デバイス依存**
   - ARCore（Android）: Floor 分類をサポート
   - ARKit（iOS）: Floor 分類をサポート
   - 古いデバイスは分類非対応

### Unity の制約

1. **タッチ入力**
   - `Input.touchCount` を使用（Input System 不要）
   - シングルタッチのみ対応

2. **座標系**
   - World 座標系で計算
   - Y-up（Unityデフォルト）

## 拡張機能の実装例

### マルチタッチ対応

```csharp
void HandlePinchZoom()
{
    if (Input.touchCount == 2)
    {
        Touch touch0 = Input.GetTouch(0);
        Touch touch1 = Input.GetTouch(1);

        Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
        Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;

        float prevMagnitude = (touch0PrevPos - touch1PrevPos).magnitude;
        float currentMagnitude = (touch0.position - touch1.position).magnitude;

        float difference = currentMagnitude - prevMagnitude;

        followDistance -= difference * 0.01f;
        followDistance = Mathf.Clamp(followDistance, minDistance, maxDistance);
    }
}
```

### アバターの高さ調整

```csharp
void AdjustHeight(float deltaHeight)
{
    if (avatar != null)
    {
        Vector3 pos = avatar.transform.position;
        pos.y += deltaHeight;
        avatar.transform.position = pos;
    }
}
```

### 追従速度の動的調整

```csharp
void AdjustSmoothness(float cameraSpeed)
{
    // カメラの移動速度に応じて滑らかさを調整
    if (cameraSpeed > 2f)
    {
        followSmoothness = 0.3f;  // 速い追従
    }
    else
    {
        followSmoothness = 0.15f;  // 通常の追従
    }
}
```

## 関連ファイル

### 実装
- [`PlaceAvatarOnPlaneOnly.cs`](../aiCam/Assets/Scripts/PlaceAvatarOnPlaneOnly.cs) - 追従システムコア

### ドキュメント
- [`AVATAR_FOLLOW.md`](../AVATAR_FOLLOW.md) - ユーザー向けガイド

## 関連ドキュメント

- [Implemented Requirements](./Implemented-Requirements.md) - 全体要件
- [AR Foundation Documentation](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.0/manual/index.html)
- [Unity Input System](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.0/manual/index.html)
