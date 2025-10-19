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
- カメラの**完全な3D回転**（pitch/yaw/roll）に対応
- カメラ相対座標系で位置を維持
- 平面を無視してカメラに追従
- 常にカメラの方を向く
- ARFoundation の BackgroundRenderer と同じ挙動

**実装**:
```csharp
void UpdateCameraLocked()
{
    if (!arCamera || !avatar) return;

    Vector3 camPos = arCamera.transform.position;
    // カメラの完全な回転を使用（pitch/yaw/roll全て対応）
    Quaternion camRot = arCamera.transform.rotation;

    // カメラ相対位置を維持（スワイプ距離調整を反映）
    Vector3 offset = cameraLocalOffset.normalized * followDistance;
    Vector3 targetPos = camPos + (camRot * offset);

    // CameraLockedモードではより強くカメラに追従
    float cameraLockSmoothness = Mathf.Min(followSmoothness * 3f, 0.8f);
    avatar.transform.position = Vector3.Lerp(avatar.transform.position, targetPos, cameraLockSmoothness);

    // カメラを向く（手動回転を考慮）
    Vector3 lookDir = camPos - avatar.transform.position;
    lookDir.y = 0;
    if (lookDir.sqrMagnitude > 0.01f)
    {
        Quaternion baseLookRot = Quaternion.LookRotation(lookDir);
        if (Mathf.Abs(avatarRotationY) > 0.1f)
        {
            Quaternion manualRot = Quaternion.Euler(0, avatarRotationY, 0);
            avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot * manualRot, cameraLockSmoothness);
        }
        else
        {
            avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot, cameraLockSmoothness);
        }
    }
}
```

**カメラ相対オフセットの計算**:
```csharp
// モード切替時にオフセットを計算（カメラの完全な回転を使用）
Vector3 camPos = arCamera.transform.position;
Vector3 avatarPos = avatar.transform.position;
Quaternion invCamRot = Quaternion.Inverse(arCamera.transform.rotation);
cameraLocalOffset = invCamRot * (avatarPos - camPos);
```

**smoothness の調整**:
CameraLockedモードでは、より強い追従のために `followSmoothness` を3倍に増幅（最大0.8）:
```csharp
float cameraLockSmoothness = Mathf.Min(followSmoothness * 3f, 0.8f);
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

スワイプインタラクションは、PlaneLocked と CameraLocked モードでのみ有効です。Off モードではスワイプは無効化されています。

### 上下スワイプ: 距離調整

**操作**:
- **上にスワイプ**: アバターが**奥（遠く）**に移動
- **下にスワイプ**: アバターが**手前（近く）**に移動

**動作条件**:
- 追従モードが Off 以外（PlaneLocked または CameraLocked）
- 縦方向の移動量が横方向より大きい
- `enableSwipeDistance` が true

**実装**:
```csharp
void HandleSwipeInteraction()
{
    if (Input.touchCount == 0) return;
    Touch touch = Input.GetTouch(0);

    // UI上のタッチは無視
    if (IsTouchOverUI(touch)) return;
    if (EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return;

    switch (touch.phase)
    {
        case TouchPhase.Began:
            isSwipeActive = true;
            swipeStartPosition = touch.position;
            break;

        case TouchPhase.Moved:
            if (!isSwipeActive) return;

            Vector2 delta = touch.position - swipeStartPosition;

            // 上下スワイプ: 距離調整
            if (enableSwipeDistance && Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
            {
                // 上にスワイプ(+Y) = 遠くに、下にスワイプ(-Y) = 近くに
                float distanceDelta = delta.y / swipeDistanceSensitivity;
                followDistance = Mathf.Clamp(followDistance + distanceDelta, minDistance, maxDistance);

                Debug.Log($"[PlaceAvatarOnPlaneOnly] Swipe distance adjust: {followDistance:F2}m (delta: {distanceDelta:F2}m)");
            }

            swipeStartPosition = touch.position;
            break;

        case TouchPhase.Ended:
        case TouchPhase.Canceled:
            isSwipeActive = false;
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
距離変化 = スワイプY / 感度
新しい距離 = 現在の距離 + 距離変化
最終距離 = Clamp(新しい距離, 最小値, 最大値)
```

**例**:
- 200ピクセル上スワイプ → +1.0m 遠くに
- 100ピクセル下スワイプ → -0.5m 近くに

### 左右スワイプ: 回転調整

**操作**:
- **右にスワイプ**: アバターが**右回転**（時計回り）
- **左にスワイプ**: アバターが**左回転**（反時計回り）

**動作条件**:
- 追従モードが Off 以外（PlaneLocked または CameraLocked）
- 横方向の移動量が縦方向より大きい
- `enableSwipeRotation` が true

**実装**:
```csharp
// 左右スワイプ: 回転
else if (enableSwipeRotation && Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
{
    float rotationDelta = -delta.x * swipeRotationSensitivity;
    avatarRotationY += rotationDelta;

    // -180〜180度に正規化
    while (avatarRotationY > 180f) avatarRotationY -= 360f;
    while (avatarRotationY < -180f) avatarRotationY += 360f;

    Debug.Log($"[PlaceAvatarOnPlaneOnly] Swipe rotation adjust: {avatarRotationY:F1}° (delta: {rotationDelta:F1}°)");
}
```

**パラメータ**:
- `swipeRotationSensitivity`: 0.3°/px（Inspector で調整可能）

**計算式**:
```
回転変化 = -スワイプX * 感度
新しい回転 = 現在の回転 + 回転変化
正規化回転 = 回転を-180°〜180°の範囲に正規化
```

**例**:
- 100ピクセル右スワイプ → -30° 回転（右回り）
- 100ピクセル左スワイプ → +30° 回転（左回り）

**回転の適用**:
```csharp
// カメラを向く方向を基準に手動回転を加算
Quaternion baseLookRot = Quaternion.LookRotation(lookDir);
if (Mathf.Abs(avatarRotationY) > 0.1f)
{
    Quaternion manualRot = Quaternion.Euler(0, avatarRotationY, 0);
    avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot * manualRot, followSmoothness);
}
```

### スワイプの判定ロジック

**方向判定**:
```csharp
Vector2 delta = touch.position - swipeStartPosition;

if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
{
    // 縦方向が強い → 距離調整
}
else if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
{
    // 横方向が強い → 回転調整
}
```

**UI ブロック**:
UI要素の上でのスワイプは無視されます：
```csharp
if (IsTouchOverUI(touch)) return;
if (EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return;
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
   - 画面を**上にスワイプ**してアバターを遠ざける
   - 画面を**下にスワイプ**してアバターを近づける
   - 範囲: 0.5m 〜 5.0m（Inspector で調整可能）

4. **回転を調整**
   - 画面を**右にスワイプ**してアバターを右回転
   - 画面を**左にスワイプ**してアバターを左回転
   - 回転はカメラ向き方向を基準に適用される

5. **モードを切り替え**
   - ダブルタップでモードを順次切り替え
   - Off → PlaneLocked → CameraLocked → Off → ...

### 各モードでの操作

#### Off モード
- **スワイプ**: 無効
- **ダブルタップ**: PlaneLocked へ切り替え
- **特徴**: アバターは固定位置に留まる

#### PlaneLocked モード
- **スワイプ**: 有効（距離・回転調整可能）
- **ダブルタップ**: CameraLocked へ切り替え
- **特徴**: カメラの水平方向にアバターが追従、平面上を滑るように移動

#### CameraLocked モード
- **スワイプ**: 有効（距離・回転調整可能）
- **ダブルタップ**: Off へ切り替え
- **特徴**: カメラの完全な3D回転に追従、カメラの傾きにも対応

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
[PlaceAvatarOnPlaneOnly] Follow Mode: CameraLocked (カメラ追従) - Offset: (0.0, 0.0, 1.5)
[PlaceAvatarOnPlaneOnly] Follow Mode: Off (固定)
```

### スワイプ操作ログ

```
[PlaceAvatarOnPlaneOnly] Swipe distance adjust: 2.15m (delta: 0.15m)
[PlaceAvatarOnPlaneOnly] Swipe rotation adjust: 45.3° (delta: -5.2°)
```

**注意**:
- 距離の delta が正の場合、アバターが遠ざかる
- 回転の delta が負の場合、右回転（時計回り）

### 追従中のログ（30フレームごと）

```
[PlaceAvatarOnPlaneOnly] PlaneLocked: Distance=1.48m (target=1.50m), Horizontal=1.50m
[PlaceAvatarOnPlaneOnly] CameraLocked: Distance=1.52m (target=1.50m), CamRot=(15.0°, 90.0°, 0.0°), Smoothness=0.45
```

**CameraLocked ログの説明**:
- `CamRot=(pitch, yaw, roll)`: カメラの3軸回転（Euler角）
- `Smoothness`: 実際の追従smoothness値（基本値の3倍）

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

**解決**:
- Inspector で `Enable Swipe Distance` を有効化
- PlaneLocked または CameraLocked モードに切り替え

#### スワイプの方向が逆

**原因**: コードの符号設定

**確認**:
```csharp
// 正しい実装
float distanceDelta = delta.y / swipeDistanceSensitivity;  // 上=+, 下=-
float rotationDelta = -delta.x * swipeRotationSensitivity;  // 右=-, 左=+
```

#### スワイプの感度が合わない

**原因**: デバイスや画面サイズによる感度の違い

**解決**:
```csharp
// Inspector で調整
swipeDistanceSensitivity = 150f;  // より敏感に
swipeRotationSensitivity = 0.5f;  // より敏感に
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
