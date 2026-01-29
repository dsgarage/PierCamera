# Avatar Cache System 実装ログ

Issue #416: アバタースロット永続化

## Phase 1: 基盤・マニフェスト管理

### 実装ファイル
- `Core/AvatarCacheManager.cs`
- `IO/PersistenceManager.cs`

### 実装メソッド

#### AvatarCacheManager
| メソッド | 説明 |
|---------|------|
| `CalculateFileHash(string)` | SHA256でファイルハッシュを計算 |
| `GetCacheDirectoryPath(string)` | キャッシュディレクトリパスを取得 |
| `CacheExists(string)` | キャッシュの存在確認 |
| `IsCacheValid(string)` | キャッシュのバージョン検証 |
| `CreateCacheAsync(string, GameObject)` | キャッシュ構造とマニフェスト作成 |

#### PersistenceManager
| メソッド | 説明 |
|---------|------|
| `SaveSlots(SlotsData)` | スロットデータをJSON保存 |
| `LoadSlots()` | スロットデータをロード（破損時はデフォルト値） |
| `SaveAtomic(string, string)` | 一時ファイル経由でアトミック保存 |
| `TryRecoverCorruptedFile(string, out string)` | 破損ファイルのバックアップと復旧試行 |

### キャッシュ構造
```
{cacheRoot}/AvatarCache/{hash}/
  ├── manifest.json     # メタ情報
  ├── core/             # ボーン・メッシュ
  ├── textures/         # テクスチャ
  └── icons/            # サムネイル
```

---

## Phase 2: ボーン/Humanoid キャッシュ

### 実装ファイル
- `Serializers/BoneHierarchyCacheSerializer.cs`
- `Serializers/HumanoidCacheSerializer.cs`

### 実装メソッド

#### BoneHierarchyCacheSerializer
| メソッド | 説明 |
|---------|------|
| `ExtractFromAvatar(GameObject)` | 全Transform情報を抽出 |
| `SerializeToJson(BoneHierarchyCache)` | JSON形式でシリアライズ |
| `DeserializeFromJson(string)` | JSONからデシリアライズ |
| `Reconstruct(BoneHierarchyCache)` | キャッシュからGameObject階層を再構築 |

#### HumanoidCacheSerializer
| メソッド | 説明 |
|---------|------|
| `ExtractFromAnimator(Animator)` | HumanBodyBonesマッピングを抽出 |
| `SerializeToJson(HumanoidCache)` | JSON形式でシリアライズ |
| `DeserializeFromJson(string)` | JSONからデシリアライズ |
| `CreateAvatar(HumanoidCache, GameObject)` | AvatarBuilder.BuildHumanAvatarでAvatar再構築 |

### データ構造
```csharp
// BoneInfo
- name, path, parentIndex
- localPosition[3], localRotation[4], localScale[3]

// HumanBoneMapping
- humanBoneName (HumanBodyBones名)
- bonePath (Transform階層パス)
```

---

## Phase 3: Mesh/BlendShape キャッシュ

### 実装ファイル
- `Serializers/MeshCacheSerializer.cs`
- `Serializers/BlendShapeCacheSerializer.cs`

### 実装メソッド

#### MeshCacheSerializer
| メソッド | 説明 |
|---------|------|
| `SerializeToBinary(Mesh[], string)` | メッシュをバイナリ形式で保存 |
| `DeserializeFromBinary(string)` | バイナリからMesh配列を復元 |
| `ValidateMagic(string)` | "MESH"マジックナンバー検証 |

#### BlendShapeCacheSerializer
| メソッド | 説明 |
|---------|------|
| `SerializeToBinary(SkinnedMeshRenderer[], string)` | BlendShapeをバイナリ保存 |
| `DeserializeAndApply(string, Mesh[])` | BlendShapeを復元・メッシュに適用 |
| `ValidateMagic(string)` | "BLND"マジックナンバー検証 |

### バイナリフォーマット

#### meshes.bin (MESH)
```
[Header]
- Magic: "MESH" (4 bytes)
- Version: int32
- MeshCount: int32

[Per Mesh]
- Name: string
- Vertices: count + Vector3[]
- Normals: count + Vector3[]
- Tangents: count + Vector4[]
- UVs: count + Vector2[]
- UV2s: count + Vector2[]
- Colors: count + Color[]
- BoneWeights: count + (indices[4] + weights[4])[]
- BindPoses: count + Matrix4x4[]
- SubMeshes: count + (triangleCount + int[])[]
```

#### blendshapes.bin (BLND)
```
[Header]
- Magic: "BLND" (4 bytes)
- Version: int32
- MeshCount: int32

[Per Mesh]
- MeshName: string
- BlendShapeCount: int32
- VertexCount: int32

[Per BlendShape]
- Name: string
- FrameCount: int32

[Per Frame]
- Weight: float
- DeltaVertices: Vector3[vertexCount]
- DeltaNormals: Vector3[vertexCount]
- DeltaTangents: Vector3[vertexCount]
```

---

## Phase 4: テクスチャ/マテリアル キャッシュ

### 実装ファイル
- `IO/TextureCacheManager.cs`
- `Serializers/MaterialCacheSerializer.cs`

### 実装状況
**未実装** - スタブメソッドのみ

---

## Phase 5: 高速ロードパス

### 実装ファイル
- `Core/AvatarCacheManager.cs`

### 実装状況
**未実装** - 以下のメソッドがスタブ
- `LoadFromCacheAsync(string)`
- `DeleteCache(string)`

---

## Phase 6: 永続化・保存タイミング

### 実装ファイル
- `IO/PersistenceManager.cs`

### 実装状況
**未実装** - 以下のメソッドがスタブ
- `RegisterPauseCallback(Action<bool>)`
- `StartAutoSave(float)`
- `GetRecoveryStats()`

---

## Phase 7: エクスポート/インポート

### 実装ファイル
- `IO/AvatarCacheExporter.cs`
- `IO/AvatarCacheImporter.cs`

### 実装状況
**未実装** - スタブメソッドのみ

---

## Phase 8: ポーズ/表情

### 実装ファイル
- `Serializers/ExpressionCacheSerializer.cs`
- `Serializers/PoseCacheSerializer.cs`

### 実装状況
**未実装** - スタブメソッドのみ
