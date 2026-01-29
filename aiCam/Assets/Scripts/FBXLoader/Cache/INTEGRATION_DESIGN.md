# Issue #457: アバタースロット統合設計ドキュメント

## 概要

Issue #416で実装したバイナリキャッシュシステム（Phase 1-8）と、既存のスロットシステム（AvatarSlotManager, AvatarMemoryCache）を統合し、「アプリ再起動後も爆速ロード」を実現する。

## 現状のアーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│ 現在のフロー (遅い)                                               │
├─────────────────────────────────────────────────────────────────┤
│  [スロットタップ]                                                 │
│       ↓                                                         │
│  AvatarSlotManager.OnSlotClicked                                │
│       ↓                                                         │
│  AvatarMemoryCache.SwitchToSlotAsync                            │
│       ↓                                                         │
│  ┌──── メモリキャッシュHIT? ────┐                                │
│  │ YES                      NO │                                │
│  ↓                          ↓  │                                │
│  [即座に表示]    RuntimeFBXLoaderBridge.LoadAsync ← ★ボトルネック │
│                          ↓                                      │
│               [VRMファイルから毎回フルロード] (3-10秒)             │
└─────────────────────────────────────────────────────────────────┘
```

## 目標のアーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│ 新しいフロー (高速)                                               │
├─────────────────────────────────────────────────────────────────┤
│  [スロットタップ]                                                 │
│       ↓                                                         │
│  AvatarSlotManager.OnSlotClicked                                │
│       ↓                                                         │
│  AvatarMemoryCache.SwitchToSlotAsync                            │
│       ↓                                                         │
│  ┌──── メモリキャッシュHIT? ────┐                                │
│  │ YES                      NO │                                │
│  ↓                          ↓  │                                │
│  [即座に表示]        ┌─── バイナリキャッシュHIT? ───┐            │
│                     │ YES                       NO │            │
│                     ↓                           ↓  │            │
│     AvatarCacheManager.LoadFromCacheAsync   [VRMからフルロード]   │
│                ↓                                ↓               │
│     [バイナリから高速ロード] (0.5-1秒)    [バイナリキャッシュ作成]  │
│                ↓                                ↓               │
│            [表示]                            [表示]              │
└─────────────────────────────────────────────────────────────────┘
```

---

## Phase 1: AvatarSlotData拡張

### 目的
`AvatarSlotData`にバイナリキャッシュIDを保存するフィールドを追加する。

### 実装内容
```csharp
// AvatarSlotData.cs に追加
public string binaryCacheId;
public bool HasBinaryCache => !string.IsNullOrEmpty(binaryCacheId);

public void SetBinaryCacheId(string cacheId)
{
    binaryCacheId = cacheId;
}

public void ClearBinaryCache()
{
    binaryCacheId = null;
}
```

### テストケース
- `AvatarSlotData_binaryCacheIdを設定できること`
- `AvatarSlotData_HasBinaryCacheがtrueを返すこと`
- `AvatarSlotData_ClearBinaryCacheでクリアできること`
- `AvatarSlotData_JSON永続化でbinaryCacheIdが保存されること`

---

## Phase 2: AvatarCacheIntegrator作成

### 目的
バイナリキャッシュシステム（AvatarCacheManager）と既存システム（AvatarMemoryCache）を橋渡しする統合レイヤーを作成する。

### 実装内容
```csharp
namespace AICam.AvatarCache
{
    public class AvatarCacheIntegrator
    {
        private readonly string _cacheRootPath;

        public AvatarCacheIntegrator(string cacheRootPath = null);

        // バイナリキャッシュからロード（存在しない場合はnull）
        public async UniTask<GameObject> LoadFromBinaryCacheAsync(
            string cacheId,
            Action<float> onProgress = null);

        // バイナリキャッシュを作成
        public async UniTask<string> CreateBinaryCacheAsync(
            GameObject avatar,
            string sourceFilePath);

        // キャッシュの存在確認
        public bool HasBinaryCache(string cacheId);

        // キャッシュを削除
        public void DeleteBinaryCache(string cacheId);
    }
}
```

### テストケース
- `AvatarCacheIntegrator_HasBinaryCacheでキャッシュ存在確認ができること`
- `AvatarCacheIntegrator_CreateBinaryCacheAsyncでキャッシュ作成ができること`
- `AvatarCacheIntegrator_LoadFromBinaryCacheAsyncでロードができること`
- `AvatarCacheIntegrator_DeleteBinaryCacheで削除ができること`
- `AvatarCacheIntegrator_存在しないキャッシュでnullを返すこと`

---

## Phase 3: AvatarMemoryCache統合

### 目的
`AvatarMemoryCache.SwitchToSlotAsync`を変更し、バイナリキャッシュを優先的に使用するようにする。

### 実装内容
```csharp
// AvatarMemoryCache.SwitchToSlotAsync の変更
public async UniTask<SlotSwitchResult> SwitchToSlotAsync(...)
{
    // 1. メモリキャッシュチェック（既存）
    if (HasCachedAvatar(targetSlotIndex))
    {
        return ActivateFromMemoryCache(targetSlotIndex);
    }

    // 2. バイナリキャッシュチェック（新規）
    if (slotData.HasBinaryCache && _cacheIntegrator.HasBinaryCache(slotData.binaryCacheId))
    {
        var avatar = await _cacheIntegrator.LoadFromBinaryCacheAsync(
            slotData.binaryCacheId, onProgress);
        if (avatar != null)
        {
            CacheAvatar(targetSlotIndex, slotData.modelFilePath, avatar, keepActive: true);
            return SlotSwitchResult.Succeeded(targetSlotIndex, avatar, wasCacheHit: true);
        }
    }

    // 3. VRMからフルロード（フォールバック）
    var loadResult = await avatarLoader.LoadAsync(slotData.modelFilePath, ...);
    // ...
}
```

### テストケース
- `SwitchToSlotAsync_バイナリキャッシュがある場合に高速ロードされること`
- `SwitchToSlotAsync_バイナリキャッシュがない場合にVRMからロードされること`
- `SwitchToSlotAsync_バイナリキャッシュ破損時にVRMフォールバックすること`
- `SwitchToSlotAsync_メモリキャッシュが優先されること`

---

## Phase 4: 自動キャッシュ作成

### 目的
VRMロード後に自動的にバイナリキャッシュを作成し、次回以降の高速ロードを可能にする。

### 実装内容
```csharp
// AvatarMemoryCache.SwitchToSlotAsync の変更（VRMロード後）
// 3. VRMからフルロード（フォールバック）
var loadResult = await avatarLoader.LoadAsync(slotData.modelFilePath, ...);
if (loadResult.Success)
{
    // バイナリキャッシュを非同期で作成
    _ = CreateBinaryCacheInBackgroundAsync(loadResult.Avatar, slotData);
}

private async UniTaskVoid CreateBinaryCacheInBackgroundAsync(GameObject avatar, AvatarSlotData slotData)
{
    try
    {
        var cacheId = await _cacheIntegrator.CreateBinaryCacheAsync(avatar, slotData.modelFilePath);
        slotData.SetBinaryCacheId(cacheId);
        // 永続化
        AvatarSlotCache.SaveSlots(...);
        Debug.Log($"[AvatarMemoryCache] Binary cache created: {cacheId}");
    }
    catch (Exception e)
    {
        Debug.LogWarning($"[AvatarMemoryCache] Failed to create binary cache: {e.Message}");
    }
}
```

### テストケース
- `VRMロード後_バイナリキャッシュが自動作成されること`
- `VRMロード後_slotDataにcacheIdが設定されること`
- `VRMロード後_AvatarSlotCacheに永続化されること`
- `キャッシュ作成失敗時_アプリが継続動作すること`

---

## Phase 5: エンドツーエンド統合テスト

### 目的
アプリ起動からスロットタップ、再起動後の高速ロードまでの全体フローをテストする。

### テストケース
- `E2E_初回VRMロード後にバイナリキャッシュが作成されること`
- `E2E_2回目のスロットタップでバイナリキャッシュからロードされること`
- `E2E_アプリ再起動シミュレーション後に高速ロードされること`
- `E2E_複数スロット間の切り替えが正常に動作すること`
- `E2E_キャッシュ削除後にVRMから再ロードされること`

---

## 期待される効果

| メトリクス | 現状 | 統合後 |
|----------|------|-------|
| 初回ロード | 3-10秒 | 3-10秒 (変わらず) |
| 2回目以降ロード (メモリ外) | 3-10秒 | **0.5-1秒** |
| アプリ再起動後 | 3-10秒 | **0.5-1秒** |

---

## ファイル変更一覧

| Phase | ファイル | 変更内容 |
|-------|---------|---------|
| 1 | `AvatarSlotData.cs` | `binaryCacheId`フィールド追加 |
| 2 | `AvatarCacheIntegrator.cs` | 新規作成 |
| 3 | `AvatarMemoryCache.cs` | `SwitchToSlotAsync`にバイナリキャッシュ統合 |
| 4 | `AvatarMemoryCache.cs` | 自動キャッシュ作成処理追加 |
| 5 | - | 統合テストのみ |

---

## 実装順序

1. Phase 1 テスト作成 → 実装 → テスト通過
2. Phase 2 テスト作成 → 実装 → テスト通過
3. Phase 3 テスト作成 → 実装 → テスト通過
4. Phase 4 テスト作成 → 実装 → テスト通過
5. Phase 5 統合テスト作成 → 実装 → テスト通過
6. 各Phaseごとにコミット
