# ログ仕様書 - SkinnedMeshRenderer リファクタリング

## 📋 目次

- [概要](#概要)
- [ログフォーマット](#ログフォーマット)
- [Phase 1: TransformBuilder](#phase-1-transformbuilder)
- [Phase 2: MeshDataCollector](#phase-2-meshdatacollector)
- [Phase 3: BoneDataCollector](#phase-3-bonedatacollector)
- [Phase 4: SkinnedMeshBuilder](#phase-4-skinnedmeshbuilder)
- [エラーログ](#エラーログ)
- [パフォーマンスログ](#パフォーマンスログ)

---

## 概要

各フェーズで以下の情報を明確にログ出力します：

1. **対応内容** - 何を処理しているか
2. **処理方法** - どのように処理したか
3. **処理結果** - 結果の数値・状態
4. **特殊ケース** - マルチメッシュ、マテリアル複数、ボーン制限等
5. **除外・スキップ** - 何を除外したか、理由は何か

---

## ログフォーマット

### 基本フォーマット

```
[Phase名] [レベル] [ノード名] メッセージ
```

### レベル定義

- `[INFO]` - 通常の処理情報
- `[WARN]` - 警告（処理は継続）
- `[ERROR]` - エラー（処理失敗）
- `[DEBUG]` - 詳細デバッグ情報
- `[STATS]` - 統計情報

### ノード名の表記

```
Node: "Outer" (ID: 5, Depth: 2)
```

---

## Phase 1: TransformBuilder

### 開始ログ

```
========================================
[TransformBuilder] [INFO] Phase 1 開始: Transform階層構築
[TransformBuilder] [INFO] 入力:
  - Root Node: "RootNode"
  - Scene Nodes: 156
  - Coordinate Conversion: Enabled
========================================
```

### ノード処理ログ（各ノード）

```
[TransformBuilder] [INFO] Node: "Armature" (ID: 2, Depth: 1)
  ├─ 親: "RootNode"
  ├─ 子: 1個 (Hips)
  ├─ メッシュ: なし
  └─ Transform変換:
      Position: (0.00, 0.00, 0.00) → (0.00, 0.00, 0.00)
      Rotation: (0.00, 0.00, 0.00, 1.00) → (0.00, 0.00, 0.00, 1.00)
      Scale: (1.00, 1.00, 1.00)
```

### ボーン登録ログ

```
[TransformBuilder] [INFO] ボーン登録: "Hips"
  ├─ Path: RootNode/Armature/Hips
  ├─ World Position: (0.00, 0.70, 0.06)
  ├─ World Rotation: (0.00, 0.00, 0.00)
  └─ 辞書登録: boneNameToTransform["Hips"]
```

### 座標変換詳細ログ（デバッグモード）

```
[TransformBuilder] [DEBUG] Node: "Hips" - 座標変換詳細
  ├─ Assimp Matrix (分解前):
  │   Position: (0.000, 0.700, -0.060)
  │   Rotation: (0.000, 0.000, 0.000, 1.000)
  │   Scale: (1.000, 1.000, 1.000)
  ├─ ConversionMatrix適用:
  │   Method: ConvertVector() / ConvertQuaternion()
  │   Matrix: [[1,0,0,0],[0,1,0,0],[0,0,-1,0],[0,0,0,1]]
  └─ Unity Transform (適用後):
      localPosition: (0.000, 0.700, 0.060)
      localRotation: (0.000, 0.000, 0.000, 1.000)
      localScale: (1.000, 1.000, 1.000)
```

### Phase 1 完了ログ

```
========================================
[TransformBuilder] [STATS] Phase 1 完了: Transform階層構築
  ├─ 処理ノード数: 156
  ├─ ボーン登録数: 54
  ├─ 座標変換適用: 156回
  ├─ エラー: 0件
  ├─ 警告: 0件
  └─ 処理時間: 45ms
========================================
```

---

## Phase 2: MeshDataCollector

### 開始ログ

```
========================================
[MeshDataCollector] [INFO] Phase 2 開始: メッシュデータ収集
[MeshDataCollector] [INFO] 入力:
  - Node: "Outer"
  - サブメッシュ数: 1
  - Coordinate Conversion: Enabled
========================================
```

### サブメッシュ処理ログ（各サブメッシュ）

```
[MeshDataCollector] [INFO] サブメッシュ[0]: "Plane.015"
  ├─ 頂点数: 6724
  ├─ 三角形数: 3354
  ├─ UV数: 6724
  ├─ 法線: あり
  ├─ 頂点カラー: なし
  └─ マテリアルスロット: 0
```

### マルチメッシュ対応ログ

```
[MeshDataCollector] [INFO] マルチメッシュ結合:
  ├─ サブメッシュ数: 3
  ├─ 結合方法: 頂点オフセット方式
  └─ 結合詳細:
      ├─ Mesh[0] "Plane.022": Vertices 4726 (Offset: 0)
      ├─ Mesh[1] "Plane.023": Vertices 1856 (Offset: 4726)
      └─ Mesh[2] "Plane.024": Vertices 900 (Offset: 6582)
```

### 座標変換適用ログ

```
[MeshDataCollector] [INFO] 座標変換適用:
  ├─ 頂点変換: 6724個
  │   Sample[0]: (-0.123, 1.450, -0.034) → (-0.123, 1.450, 0.034)
  │   Sample[100]: (0.056, 1.234, -0.012) → (0.056, 1.234, 0.012)
  ├─ 法線変換: 6724個
  │   Sample[0]: (0.000, 0.000, -1.000) → (0.000, 0.000, 1.000)
  └─ UV: そのまま保持（座標変換不要）
```

### BlendShape処理ログ

```
[MeshDataCollector] [INFO] BlendShape処理:
  ├─ AnimMesh数: 3
  ├─ BlendShape追加:
  │   ├─ [0] "Plane.015_BlendShape_0": 6724 deltas
  │   ├─ [1] "Plane.015_BlendShape_1": 6724 deltas
  │   └─ [2] "Plane.015_BlendShape_2": 6724 deltas
  └─ 追加タイミング: sharedMesh設定前（必須）
```

### マテリアル情報ログ

```
[MeshDataCollector] [INFO] マテリアル情報:
  ├─ Assimp Materials: 1個
  │   └─ Material[0]: "OuterMaterial"
  │       ├─ Diffuse: (0.8, 0.8, 0.8)
  │       ├─ Texture: "outer_base.png"
  │       └─ Shader: Phong
  └─ Unity Material作成: 後段で実施
```

### マルチマテリアル対応ログ

```
[MeshDataCollector] [WARN] マルチマテリアル検出:
  ├─ マテリアル数: 3
  ├─ 対応方法: SubMesh分割方式
  └─ SubMesh割り当て:
      ├─ SubMesh[0]: Material "Body" (Triangles: 0-1200)
      ├─ SubMesh[1]: Material "Face" (Triangles: 1200-2400)
      └─ SubMesh[2]: Material "Hair" (Triangles: 2400-3354)
```

### Phase 2 完了ログ

```
========================================
[MeshDataCollector] [STATS] Phase 2 完了: メッシュデータ収集
  ├─ 処理サブメッシュ数: 1
  ├─ 総頂点数: 6724
  ├─ 総三角形数: 3354
  ├─ BlendShape数: 3
  ├─ 座標変換適用: 頂点6724個、法線6724個
  ├─ Unity Mesh作成: 成功
  ├─ エラー: 0件
  ├─ 警告: 0件
  └─ 処理時間: 78ms
========================================
```

---

## Phase 3: BoneDataCollector

### 開始ログ

```
========================================
[BoneDataCollector] [INFO] Phase 3 開始: ボーンデータ収集
[BoneDataCollector] [INFO] 入力:
  - Node: "Outer"
  - サブメッシュ数: 1
========================================
```

### ユニークボーン収集ログ

```
[BoneDataCollector] [INFO] ユニークボーン収集:
  ├─ 処理方法: 全サブメッシュから重複排除
  ├─ サブメッシュ別ボーン数:
  │   ├─ Mesh[0]: 23個
  │   ├─ Mesh[1]: 18個
  │   └─ Mesh[2]: 12個
  ├─ 重複排除前: 53個
  └─ ユニークボーン数: 28個
```

### ボーン名→インデックスマッピングログ

```
[BoneDataCollector] [INFO] ボーンマッピング作成:
  ├─ 総ボーン数: 28
  ├─ マッピング方式: ボーン名 → グローバルインデックス
  └─ マッピング詳細:
      ├─ "Hips" → 0
      ├─ "Spine" → 1
      ├─ "Chest" → 2
      ├─ "Upper Arm.L" → 3
      ...（省略）
      └─ "coat8_R" → 27
```

### OffsetMatrix収集ログ

```
[BoneDataCollector] [INFO] OffsetMatrix収集:
  ├─ 収集方法: 各ボーンのoffsetMatrixを生データで保持
  ├─ 座標変換: なし（Phase 4で適用）
  └─ 収集詳細:
      ├─ "Hips": Matrix4x4 (Raw)
      │   [[1.00, 0.00, 0.00, 0.00],
      │    [0.00, 1.00, 0.00, -0.70],
      │    [0.00, 0.00, 1.00, 0.06],
      │    [0.00, 0.00, 0.00, 1.00]]
      └─ （他省略）
```

### BoneWeight収集ログ（ノード全体）

```
[BoneDataCollector] [INFO] BoneWeight収集開始:
  ├─ 総頂点数: 6724
  ├─ 処理方法: 各メッシュのBoneWeightをグローバルインデックスに変換
  └─ サブメッシュ別処理:
      └─ Mesh[0] "Plane.015": 6724頂点
```

### BoneWeight収集ログ（ボーン別）

```
[BoneDataCollector] [INFO] Bone[0] "Hips":
  ├─ ローカルインデックス: 0
  ├─ グローバルインデックス: 0
  ├─ 影響頂点数: 1590
  ├─ Weight範囲: 0.012 ~ 0.987
  └─ 処理結果: 1590個のBoneWeight登録
```

### 4つ以上のボーン処理ログ

```
[BoneDataCollector] [WARN] 頂点[1234] - ボーン制限処理:
  ├─ 元のボーン数: 6個
  ├─ Unity制限: 最大4個
  ├─ 処理方法: Weight降順ソート → 上位4個選択
  ├─ 選択されたボーン:
  │   ├─ Bone[2] "Chest": Weight 0.456
  │   ├─ Bone[1] "Spine": Weight 0.312
  │   ├─ Bone[3] "Upper Arm.L": Weight 0.187
  │   └─ Bone[5] "Lower Arm.L": Weight 0.045
  └─ 除外されたボーン:
      ├─ Bone[7] "coat_L": Weight 0.008 (除外理由: 5番目以降)
      └─ Bone[8] "coat2_L": Weight 0.003 (除外理由: 5番目以降)
```

### Weight正規化ログ

```
[BoneDataCollector] [INFO] BoneWeight正規化:
  ├─ 対象頂点数: 6724
  ├─ 正規化方法: sum(weights) = 1.0 に調整
  ├─ 正規化結果:
  │   ├─ 正規化済み: 6720頂点
  │   ├─ Weight=0（補正）: 4頂点
  │   └─ Sample[1234]:
  │       Before: (0.456, 0.312, 0.187, 0.045) sum=1.000
  │       After: (0.456, 0.312, 0.187, 0.045) sum=1.000
  └─ 統計:
      ├─ 平均Weight/頂点: 2.3個
      ├─ 1個のみ: 342頂点
      ├─ 2個: 1890頂点
      ├─ 3個: 2456頂点
      └─ 4個: 2036頂点
```

### Weight=0の頂点処理ログ

```
[BoneDataCollector] [WARN] Weight=0の頂点処理:
  ├─ 検出頂点数: 4
  ├─ 処理方法: 強制的にweight0=1.0, boneIndex0=0に設定
  └─ 対象頂点:
      ├─ Vertex[3456]: weight0=1.0, boneIndex0=0 (Hips)
      ├─ Vertex[3457]: weight0=1.0, boneIndex0=0 (Hips)
      ├─ Vertex[3458]: weight0=1.0, boneIndex0=0 (Hips)
      └─ Vertex[3459]: weight0=1.0, boneIndex0=0 (Hips)
```

### Phase 3 完了ログ

```
========================================
[BoneDataCollector] [STATS] Phase 3 完了: ボーンデータ収集
  ├─ ユニークボーン数: 28
  ├─ OffsetMatrix収集: 28個
  ├─ BoneWeight処理: 6724頂点
  ├─ 4個以上ボーン制限: 156頂点
  ├─ Weight正規化: 6720頂点
  ├─ Weight=0補正: 4頂点
  ├─ エラー: 0件
  ├─ 警告: 160件
  └─ 処理時間: 124ms
========================================
```

---

## Phase 4: SkinnedMeshBuilder

### 開始ログ

```
========================================
[SkinnedMeshBuilder] [INFO] Phase 4 開始: SkinnedMeshRenderer構築
[SkinnedMeshBuilder] [INFO] 入力:
  - Node: "Outer"
  - MeshData: 6724頂点、3354三角形
  - BoneData: 28ボーン、6724 BoneWeight
  - boneNameToTransform: 54エントリ
========================================
```

### Bone配列構築ログ

```
[SkinnedMeshBuilder] [INFO] Bone配列構築:
  ├─ 総ボーン数: 28
  ├─ 構築方法: boneNameToTransform辞書から取得
  └─ 構築結果:
      ├─ bones[0] = "Hips" (Transform: RootNode/Armature/Hips)
      ├─ bones[1] = "Spine" (Transform: RootNode/Armature/Hips/Spine)
      ...（省略）
      └─ bones[27] = "coat8_R" (Transform: RootNode/Armature/.../coat8_R)
```

### ボーン未発見ログ

```
[SkinnedMeshBuilder] [ERROR] Bone配列構築エラー:
  ├─ ボーン名: "MissingBone"
  ├─ グローバルインデックス: 15
  ├─ エラー理由: boneNameToTransform辞書に存在しない
  └─ 対処: bones[15] = null（後続処理でエラー）
```

### BindPose計算ログ

```
[SkinnedMeshBuilder] [INFO] BindPose計算:
  ├─ 総ボーン数: 28
  ├─ 計算方法: conv * offsetMatrix * conv.inverse
  ├─ 座標変換: coordinateConversionMatrix適用
  └─ 計算詳細:
      ├─ Bone[0] "Hips":
      │   ├─ OffsetMatrix (Raw):
      │   │   [[1.00, 0.00, 0.00, 0.00],
      │   │    [0.00, 1.00, 0.00, -0.70],
      │   │    [0.00, 0.00, 1.00, 0.06],
      │   │    [0.00, 0.00, 0.00, 1.00]]
      │   └─ BindPose (Converted):
      │       Position: (0.00, -0.70, -0.06)
      │       Rotation: (270.02, 0.00, 0.00)
      └─ （他省略）
```

### BindPose検証ログ

```
[SkinnedMeshBuilder] [INFO] BindPose検証:
  ├─ bones.Length: 28
  ├─ bindposes.Length: 28
  ├─ 一致: OK
  └─ NULL Bone: 0個
```

### SkinnedMeshRenderer作成ログ

```
[SkinnedMeshBuilder] [INFO] SkinnedMeshRenderer作成:
  ├─ GameObject: "Outer"
  ├─ 設定順序:
  │   1. smr.bones = bones (28個)
  │   2. smr.sharedMesh = mesh (6724頂点、3354三角形)
  │   3. smr.rootBone = "Hips"
  │   4. smr.sharedMaterial = "Outer_Material"
  │   5. smr.updateWhenOffscreen = true
  └─ 作成結果: 成功
```

### マテリアル作成ログ

```
[SkinnedMeshBuilder] [INFO] マテリアル作成:
  ├─ マテリアル数: 1
  ├─ シェーダー: lilToon（見つからない場合はStandard）
  └─ 作成詳細:
      └─ Material[0] "Outer_Material":
          ├─ Shader: lilToon
          ├─ BaseColor: (1.0, 1.0, 1.0)
          └─ 適用先: smr.sharedMaterial
```

### マルチマテリアル作成ログ

```
[SkinnedMeshBuilder] [INFO] マルチマテリアル作成:
  ├─ マテリアル数: 3
  ├─ 適用方法: smr.sharedMaterials配列
  └─ 作成詳細:
      ├─ Material[0] "Body_Material": lilToon
      ├─ Material[1] "Face_Material": lilToon
      └─ Material[2] "Hair_Material": lilToon
```

### 最終検証ログ

```
[SkinnedMeshBuilder] [INFO] 最終検証:
  ├─ SkinnedMeshRenderer: 存在
  ├─ bones.Length: 28
  ├─ sharedMesh: 存在
  │   ├─ vertexCount: 6724
  │   ├─ triangles: 10062（3354三角形）
  │   ├─ bindposes.Length: 28
  │   ├─ boneWeights.Length: 6724
  │   └─ blendShapeCount: 3
  ├─ rootBone: "Hips"
  ├─ sharedMaterial: "Outer_Material"
  └─ 検証結果: ✅ 全てOK
```

### Phase 4 完了ログ

```
========================================
[SkinnedMeshBuilder] [STATS] Phase 4 完了: SkinnedMeshRenderer構築
  ├─ Bone配列: 28個
  ├─ BindPose: 28個
  ├─ SkinnedMeshRenderer: 作成成功
  ├─ Material: 1個
  ├─ エラー: 0件
  ├─ 警告: 0件
  └─ 処理時間: 56ms
========================================
```

---

## エラーログ

### Transform構築エラー

```
[TransformBuilder] [ERROR] Transform構築失敗:
  ├─ Node: "BrokenNode"
  ├─ エラー理由: Assimpマトリクス分解失敗
  ├─ Stack Trace: ...
  └─ 対処: Identity Transformを設定して継続
```

### ボーン未発見エラー

```
[BoneDataCollector] [ERROR] ボーン未発見:
  ├─ ボーン名: "MissingBone"
  ├─ 参照元: Mesh "Outer"
  ├─ エラー理由: boneNameToTransform辞書に存在しない
  └─ 対処: このボーンの影響を受ける頂点はスキップ
```

### BoneWeight制限エラー

```
[BoneDataCollector] [ERROR] BoneWeight制限エラー:
  ├─ 頂点: 3456
  ├─ エラー理由: 4個を超えるBoneWeightを追加しようとした
  ├─ 現在のWeight: [0.4, 0.3, 0.2, 0.1]
  ├─ 追加しようとしたWeight: 0.05
  └─ 対処: 追加をスキップ（警告カウント+1）
```

---

## パフォーマンスログ

### 全体サマリー

```
========================================
[RuntimeAssimpFBXLoader] [STATS] 全フェーズ完了サマリー
========================================
Phase 1: TransformBuilder
  ├─ 処理時間: 45ms
  ├─ 処理ノード数: 156
  └─ ボーン登録: 54

Phase 2: MeshDataCollector
  ├─ 処理時間: 78ms
  ├─ 総頂点数: 6724
  └─ BlendShape: 3

Phase 3: BoneDataCollector
  ├─ 処理時間: 124ms
  ├─ ユニークボーン: 28
  └─ BoneWeight: 6724

Phase 4: SkinnedMeshBuilder
  ├─ 処理時間: 56ms
  ├─ BindPose: 28
  └─ SMR作成: 成功

========================================
総処理時間: 303ms
総エラー: 0件
総警告: 160件
========================================
```

---

## ログレベル制御

### 設定方法

```csharp
public enum LogLevel
{
    None = 0,      // ログなし
    Error = 1,     // エラーのみ
    Warning = 2,   // 警告以上
    Info = 3,      // 通常情報以上（デフォルト）
    Debug = 4,     // デバッグ情報含む全て
    Stats = 5      // 統計情報のみ
}

// 使用例
TransformBuilder.LogLevel = LogLevel.Debug;
```

### ログフィルタリング

```csharp
// 特定のノードのみログ出力
LogFilter.EnableNode("Outer");
LogFilter.EnableNode("Hair");

// 特定のフェーズのみログ出力
LogFilter.EnablePhase("BoneDataCollector");
```

---

## ログ出力例（実際の使用）

### 正常ケース（Outer メッシュ）

```
[TransformBuilder] [INFO] Node: "Outer" (ID: 45, Depth: 1)
  └─ Transform変換: OK

[MeshDataCollector] [INFO] サブメッシュ[0]: "Plane.015"
  ├─ 頂点数: 6724
  └─ BlendShape: 3個

[BoneDataCollector] [INFO] ユニークボーン収集: 28個
  └─ BoneWeight正規化: 6720頂点

[SkinnedMeshBuilder] [INFO] SkinnedMeshRenderer作成: 成功
  └─ 検証結果: ✅ OK
```

### 警告ケース（マルチメッシュ + 4個以上ボーン）

```
[MeshDataCollector] [INFO] マルチメッシュ結合: 3サブメッシュ

[BoneDataCollector] [WARN] 頂点[1234] - ボーン制限処理:
  ├─ 元: 6個 → 選択: 4個
  └─ 除外: 2個（Weight合計: 0.011）

[SkinnedMeshBuilder] [INFO] SkinnedMeshRenderer作成: 成功
  └─ 検証結果: ✅ OK（警告あり）
```

### エラーケース（ボーン未発見）

```
[BoneDataCollector] [ERROR] ボーン未発見: "MissingBone"
  └─ 対処: スキップ

[SkinnedMeshBuilder] [ERROR] Bone配列構築エラー: bones[15] = null
  └─ 処理: 中断
```

---

## 実装時の注意事項

### ログ出力のタイミング

1. **処理開始時** - 入力データのサマリー
2. **処理中** - 重要な決定ポイント
3. **処理完了時** - 結果の統計
4. **エラー発生時** - 詳細なエラー情報

### パフォーマンス考慮

- Debug/Stats レベルのログは大量になるため、Release ビルドでは無効化
- ログ文字列の生成は遅延評価（ログレベルがOFFなら生成しない）

### ログファイル出力

```csharp
// ログをファイルに保存
Logger.EnableFileOutput("FBXImportLogs/FBX_Import_{timestamp}.txt");
```

---

## 更新履歴

| 日付 | バージョン | 内容 |
|------|-----------|------|
| 2025-01-18 | 1.0.0 | 初版作成 |
