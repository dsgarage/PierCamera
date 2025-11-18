# SkinnedMeshRenderer リファクタリング ドキュメント

## 📁 ドキュメント一覧

### 1. [ARCHITECTURE.md](./ARCHITECTURE.md)
**メイン設計書** - 全体のアーキテクチャと実装詳細

- 概要と目標
- 設計原則
- クラス構造
- データフロー
- 処理順序
- 実装詳細（コードスケルトン含む）
- 制約事項
- 期待される結果

### 2. [diagrams.md](./diagrams.md)
**図解集** - Mermaid形式の各種図

- クラス構造図
- データフロー図
- 処理順序図
- データ構造詳細
- 座標変換適用箇所
- 問題と解決策のマッピング
- 実装フェーズ（ガントチャート）

---

## 🎯 リファクタリングの目的

Assimp でロードした FBX を Unity Runtime で正しくスキニング再構築し、以下の問題を完全に解決する：

- ❌ 足の逆関節
- ❌ 膝のカクつき
- ❌ 衣装（Loungewear_saneko, Outer, Underwear）の破綻
- ❌ マルチメッシュでのボーン欠落
- ❌ 座標変換の二重適用

---

## 🏗️ 新しいアーキテクチャ

### クラス分割による責任の明確化

```
RuntimeAssimpFBXLoader (メインコントローラー)
├── TransformBuilder (階層構築・座標変換)
├── MeshDataCollector (メッシュデータ収集)
├── BoneDataCollector (ボーンデータ収集)
└── SkinnedMeshBuilder (SkinnedMeshRenderer構築)
```

### 処理フロー

```
STEP 1: TransformBuilder → 階層構築・座標変換・辞書作成
         ↓
STEP 2: MeshDataCollector → メッシュ結合・BlendShape登録
         ↓
STEP 3: BoneDataCollector → BoneWeight収集・offsetMatrix収集
         ↓
STEP 4: SkinnedMeshBuilder → bone配列構築・bindpose計算・SMR作成
```

---

## 📊 データ構造

### MeshData
```csharp
public struct MeshData
{
    public List<Vector3> vertices;      // 結合された頂点（座標変換済み）
    public List<Vector2> uvs;           // 結合されたUV
    public List<Vector3> normals;       // 結合された法線（座標変換済み）
    public List<int> triangles;         // 結合された三角形
    public UnityEngine.Mesh unityMesh;  // 作成されたMesh（BlendShape含む）
}
```

### BoneData
```csharp
public struct BoneData
{
    public Dictionary<string, Assimp.Matrix4x4> boneNameToOffsetMatrix;  // ボーン名→OffsetMatrix（生データ）
    public Dictionary<string, int> boneNameToIndex;                      // ボーン名→グローバルインデックス
    public BoneWeight[] boneWeights;                                     // 全頂点のBoneWeight（正規化済み）
}
```

---

## 🎨 図の見方

### GitHub での Mermaid 表示

GitHubでは `.md` ファイル内の Mermaid コードブロックが自動的にレンダリングされます。

ブラウザで以下を開いてください：
```
https://github.com/[your-repo]/blob/main/Docs/SkinnedMeshRenderer_Refactoring/diagrams.md
```

### ローカルでの Mermaid 表示

以下のツールを使用してください：

1. **VS Code**
   - 拡張機能: [Markdown Preview Mermaid Support](https://marketplace.visualstudio.com/items?itemName=bierner.markdown-mermaid)

2. **Typora**
   - Mermaid をネイティブサポート

3. **オンラインエディタ**
   - [Mermaid Live Editor](https://mermaid.live/)

---

## 🚀 実装フェーズ

### Phase 1: データ構造定義 ✅
- `MeshData` 構造体
- `BoneData` 構造体

### Phase 2: クラス実装
- [ ] `TransformBuilder.cs`
- [ ] `MeshDataCollector.cs`
- [ ] `BoneDataCollector.cs`
- [ ] `SkinnedMeshBuilder.cs`

### Phase 3: リファクタリング
- [ ] `RuntimeAssimpFBXLoader.cs` の簡素化
- [ ] テストとデバッグ

---

## ⚠️ 重要な制約事項

### coordinateConversionMatrix の適用箇所

✅ **適用する箇所:**
- TransformBuilder: localPosition, localRotation
- MeshDataCollector: vertices, normals
- SkinnedMeshBuilder: bindpose

❌ **適用しない箇所:**
- BoneDataCollector: offsetMatrix は生データのまま

### 禁止事項

- ❌ 座標変換の二重適用
- ❌ offsetMatrix に直接座標変換を適用
- ❌ BlendShape を sharedMesh 設定後に追加

### 必須事項

- ✅ BoneWeight は float で保持（tiny weight を丸めない）
- ✅ SkinnedMeshRenderer の設定順序を厳守
- ✅ bones.Length == bindposes.Length を検証

---

## 📖 使用方法

### ドキュメントの読み方

1. **まず ARCHITECTURE.md を読む**
   - 全体像と設計原則を理解

2. **diagrams.md で図を確認**
   - データフローと処理順序を視覚化

3. **実装時は ARCHITECTURE.md の実装詳細を参照**
   - 各クラスのコードスケルトンを確認

### 図の活用

- **クラス構造図**: クラス間の依存関係を理解
- **データフロー図**: データの受け渡しを追跡
- **処理順序図**: STEP 1-4 の実行順序を確認
- **問題と解決策のマッピング**: 各問題がどう解決されるかを理解

---

## 🔍 トラブルシューティング

### 問題が発生したら

1. **STEP 1 の Transform をまず確認**
   - 「スキニング問題の80%は Transform の破綻が原因」
   - Hips の位置・回転が正しいか確認

2. **座標変換の適用箇所を確認**
   - coordinateConversionMatrix が正しく適用されているか
   - 二重適用されていないか

3. **ログを確認**
   - 各 STEP のログ出力を確認
   - bones.Length == bindposes.Length か
   - boneWeights.Length == mesh.vertexCount か

---

## 📚 参考資料

### Unity 公式ドキュメント

- [SkinnedMeshRenderer](https://docs.unity3d.com/ScriptReference/SkinnedMeshRenderer.html)
- [Mesh.bindposes](https://docs.unity3d.com/ScriptReference/Mesh-bindposes.html)
- [Mesh.boneWeights](https://docs.unity3d.com/ScriptReference/Mesh-boneWeights.html)
- [Mesh.AddBlendShapeFrame](https://docs.unity3d.com/ScriptReference/Mesh.AddBlendShapeFrame.html)

### Assimp ドキュメント

- [Assimp Documentation](http://assimp.sourceforge.net/lib_html/index.html)

---

## 📝 更新履歴

| 日付 | バージョン | 内容 |
|------|-----------|------|
| 2025-01-18 | 1.0.0 | 初版作成 |

---

**プロジェクト:** arCam/aiCam - Runtime FBX Loader
**作成者:** Claude Code
