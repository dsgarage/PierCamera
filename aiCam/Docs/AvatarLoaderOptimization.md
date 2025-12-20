# アバターローダー最適化計画

## 1. 現在のワークフロー

### 1.1 全体フロー図

![Avatar Load Workflow](diagrams/01_workflow.png)

**処理フロー:**
1. `AvatarLoadHandler.LoadAsync()` がエントリポイント
2. ファイル形式（VRM 1.0 / VRM 0.x / FBX）を判定
3. 対応するローダーで読み込み
4. 後処理（アニメーター設定、アイコン生成、キャッシュ）

### 1.2 FBXローダー詳細フロー

![FBX Loader Detail](diagrams/02_fbx_detail.png)

**ボトルネック箇所:**
- `importer.ImportFile()`: 1-5秒（ファイルサイズ依存）
- `mesh.SetVertices()` 等: GPU転送待機
- テクスチャ読み込み: 逐次処理で200-400ms

---

## 2. 問題点の可視化

### 2.1 メインスレッドブロッキング

| 処理 | 時間 | 影響度 |
|------|------|--------|
| SetVertices/Triangles | 100-500ms | 高 |
| LoadImage(texture) × 4 | 200-400ms | 高 |
| ReadPixels() | 20-50ms | 中 |
| EncodeToPNG() | 30-50ms | 中 |
| **合計** | **500-1000ms** | フレームレート低下 |

### 2.2 メモリ使用量の問題

**100K頂点アバターの場合:**

| データ | サイズ |
|--------|--------|
| List\<Vector3\> verts | 1.2 MB |
| List\<int\> tris | 1.2 MB |
| List\<Vector3\> norms | 1.2 MB |
| Unity Mesh内部 | 3.6 MB |
| Assimp Scene | 2-5 MB |
| **ピークメモリ** | **10-12 MB** |
| **理想値** | **4-5 MB** |

**問題:** 一時データがGCされるまでメモリに残存

---

## 3. 最適化案

### 3.1 テクスチャ読み込みの並列化

![Texture Optimization](diagrams/03_texture_optimization.png)

**実装方法:**
```csharp
// Before: 逐次処理 (400ms)
var diffuse = await LoadTextureFromFile(diffusePath);
var normal = await LoadTextureFromFile(normalPath);
var roughness = await LoadTextureFromFile(roughnessPath);
var metallic = await LoadTextureFromFile(metallicPath);

// After: 並列処理 (100ms)
var (diffuse, normal, roughness, metallic) = await UniTask.WhenAll(
    LoadTextureFromFile(diffusePath),
    LoadTextureFromFile(normalPath),
    LoadTextureFromFile(roughnessPath),
    LoadTextureFromFile(metallicPath)
);
```

**期待効果:** 150-200ms削減

### 3.2 アイコン生成の非同期化

**Before:**
```csharp
// 同期処理（メインスレッドブロック）
texture.ReadPixels(rect, 0, 0);  // GPU待機 20-50ms
byte[] png = texture.EncodeToPNG();  // CPU処理 30-50ms
File.WriteAllBytes(path, png);
```

**After:**
```csharp
// 非同期処理
var request = AsyncGPUReadback.Request(renderTexture);
await request;  // 他の処理が可能

// Worker Threadでエンコード
byte[] png = await UniTask.RunOnThreadPool(() =>
    ImageConversion.EncodeArrayToPNG(request.GetData<byte>().ToArray(), ...));

await File.WriteAllBytesAsync(path, png);
```

**期待効果:** 100ms削減、メインスレッドブロック解消

### 3.3 メモリ最適化

**Before:**
```csharp
var verts = new List<Vector3>();
// ... 頂点データ追加 ...
mesh.SetVertices(verts);
// verts はまだメモリに残存
```

**After:**
```csharp
var verts = new List<Vector3>();
// ... 頂点データ追加 ...
mesh.SetVertices(verts);
verts.Clear();
verts.TrimExcess();  // メモリ即時解放
```

**期待効果:** ピークメモリ30-40%削減

---

## 4. 実装優先度

![Priority Matrix](diagrams/04_priority.png)

### 優先度 S（即座に実装）
| 項目 | 効果 | 難易度 |
|------|------|--------|
| テクスチャ並列読み込み | 150-200ms削減 | 低 |
| アイコン生成非同期化 | 100ms削減 | 中 |

### 優先度 A（次フェーズ）
| 項目 | 効果 | 難易度 |
|------|------|--------|
| メモリ即時解放 | 30-40%削減 | 低 |
| Assimp Scene破棄 | 2-5MB解放 | 低 |

### 優先度 B（将来）
| 項目 | 効果 | 難易度 |
|------|------|--------|
| LRUキャッシュ実装 | メモリ予測可能化 | 中 |
| メッシュデータ構造最適化 | 20-30%削減 | 高 |

---

## 5. 最適化後の目標フロー

![Optimized Workflow](diagrams/05_optimized_workflow.png)

**設計方針:**
- メインスレッドは進捗更新とコールバックのみ
- 重い処理はバックグラウンドスレッドへ
- GPU処理は非同期リクエスト

---

## 6. 期待される改善効果

| 項目 | 現在 | 最適化後 | 削減率 |
|------|------|---------|--------|
| テクスチャ読み込み | 200-400ms | 50-100ms | **75%** |
| アイコン生成 | 50-150ms | 10-30ms | **80%** |
| ピークメモリ | 10-12MB | 5-6MB | **50%** |
| メインスレッドブロック | 500-1000ms | 100-200ms | **80%** |
| 総ロード時間 | 3-8秒 | 2-4秒 | **50%** |

---

## 7. 実装計画

### Phase 1: 即効性のある最適化
- [ ] テクスチャ並列読み込み (`UniTask.WhenAll`)
- [ ] 一時リストの即時解放 (`Clear` + `TrimExcess`)

### Phase 2: 非同期化
- [ ] `AsyncGPUReadback` によるアイコン生成
- [ ] PNG エンコードの Worker Thread 化

### Phase 3: アーキテクチャ改善
- [ ] Assimp Scene のライフサイクル管理
- [ ] LRU マテリアルキャッシュ
- [ ] メッシュデータ構造の最適化

---

## 8. 関連ファイル

| ファイル | 役割 |
|----------|------|
| `AvatarLoadHandler.cs` | 統一ロードエントリポイント |
| `RuntimeFBXLoaderBridge.cs` | VRM/FBX判定・ロード制御 |
| `RuntimeAssimpFBXLoader.cs` | FBXパース・メッシュ構築 |
| `RuntimeFBXModelBuilder.cs` | メッシュ作成・アタッチ |
| `RuntimeMaterialManager.cs` | マテリアル・テクスチャ管理 |
| `AvatarMemoryCache.cs` | メモリキャッシュ管理 |
| `AvatarIconCapture.cs` | アイコン生成 |
