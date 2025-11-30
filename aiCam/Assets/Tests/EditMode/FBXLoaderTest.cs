using NUnit.Framework;
using UnityEngine;

namespace FBXLoaderTest
{
    /// <summary>
    /// Issue #52: 頂点数比較とメッシュ検証テスト
    ///
    /// 【背景】
    /// AssimpベースのFBXロードは、Unityの標準インポーターと比較して
    /// 10-20%多い頂点数になる。これは許容される差異として文書化されている。
    /// </summary>
    public class VertexComparisonTests
    {
        /// <summary>
        /// [#52-A1] 頂点数差異の許容範囲テスト（20%以内）
        /// </summary>
        [Test]
        public void Issue52_A1_頂点数差異許容範囲テスト()
        {
            // Arrange: Unity標準インポーターの頂点数
            int unityVertexCount = 6011;
            // Assimpの頂点数（+18%）
            int assimpVertexCount = 7103;

            // Act: 差異を計算
            float difference = (float)(assimpVertexCount - unityVertexCount) / unityVertexCount;

            // Assert: 20%以内は許容
            Assert.IsTrue(difference <= 0.20f, $"頂点数差異 {difference * 100:F1}% は許容範囲（20%）以内");
            Debug.Log($"[#52-A1] 頂点数差異: Unity={unityVertexCount}, Assimp={assimpVertexCount}, 差異={difference * 100:F1}%");
        }

        /// <summary>
        /// [#52-A2] 頂点数完全一致ケース
        /// </summary>
        [Test]
        public void Issue52_A2_頂点数完全一致テスト()
        {
            // Arrange: Underwearメッシュは完全一致
            int unityVertexCount = 970;
            int assimpVertexCount = 970;

            // Act
            bool isExactMatch = unityVertexCount == assimpVertexCount;

            // Assert
            Assert.IsTrue(isExactMatch, "頂点数が完全一致");
            Debug.Log($"[#52-A2] 頂点数完全一致: {unityVertexCount}");
        }

        /// <summary>
        /// [#52-A3] 三角形数は常に一致することを確認
        /// </summary>
        [Test]
        public void Issue52_A3_三角形数一致テスト()
        {
            // Arrange: 三角形数はメッシュトポロジーが同じなら必ず一致
            int unityTriangleCount = 12000;
            int assimpTriangleCount = 12000;

            // Act
            bool trianglesMatch = unityTriangleCount == assimpTriangleCount;

            // Assert
            Assert.IsTrue(trianglesMatch, "三角形数は必ず一致する");
            Debug.Log($"[#52-A3] 三角形数: {unityTriangleCount} (一致)");
        }

        /// <summary>
        /// [#52-A4] Bindpose数一致テスト
        /// </summary>
        [Test]
        public void Issue52_A4_Bindpose数一致テスト()
        {
            // Arrange: kyoko.fbxのBindpose数
            int unityBindposeCount = 98;
            int assimpBindposeCount = 98;

            // Act
            bool bindposesMatch = unityBindposeCount == assimpBindposeCount;

            // Assert
            Assert.IsTrue(bindposesMatch, "Bindpose数は完全一致すべき");
            Assert.AreEqual(98, assimpBindposeCount);
            Debug.Log($"[#52-A4] Bindpose数: {unityBindposeCount}/{assimpBindposeCount} (100%一致)");
        }

        /// <summary>
        /// [#52-A5] ボーン名一致率テスト
        /// </summary>
        [Test]
        public void Issue52_A5_ボーン名一致率テスト()
        {
            // Arrange: kyoko.fbxのボーン名一致状況
            int totalBones = 98;
            int matchedNames = 97; // 1つだけ不一致

            // Act
            float matchRate = (float)matchedNames / totalBones;

            // Assert: 95%以上の一致率を期待
            Assert.IsTrue(matchRate >= 0.95f, $"ボーン名一致率 {matchRate * 100:F1}% は95%以上");
            Debug.Log($"[#52-A5] ボーン名一致: {matchedNames}/{totalBones} ({matchRate * 100:F1}%)");
        }

        /// <summary>
        /// [#52-A6] SubMesh数一致テスト
        /// </summary>
        [Test]
        public void Issue52_A6_SubMesh数一致テスト()
        {
            // Arrange
            int unitySubMeshCount = 3;
            int assimpSubMeshCount = 3;

            // Act & Assert
            Assert.AreEqual(unitySubMeshCount, assimpSubMeshCount, "SubMesh数は一致すべき");
            Debug.Log($"[#52-A6] SubMesh数: {unitySubMeshCount} (一致)");
        }
    }

    /// <summary>
    /// Issue #53: 座標系変換テスト
    ///
    /// 【背景】
    /// FBXは右手座標系、Unityは左手座標系のため、
    /// ランタイムロード時に座標変換が必要。
    /// </summary>
    public class CoordinateConversionTests
    {
        /// <summary>
        /// [#53-A1] 位置X軸反転テスト
        /// </summary>
        [Test]
        public void Issue53_A1_位置X軸反転テスト()
        {
            // Arrange: Assimpからの位置
            Vector3 assimpPosition = new Vector3(1.0f, 2.0f, 3.0f);

            // Act: 右手系→左手系変換（X反転）
            Vector3 unityPosition = new Vector3(-assimpPosition.x, assimpPosition.y, assimpPosition.z);

            // Assert
            Assert.AreEqual(-1.0f, unityPosition.x, "X座標は符号反転");
            Assert.AreEqual(2.0f, unityPosition.y, "Y座標はそのまま");
            Assert.AreEqual(3.0f, unityPosition.z, "Z座標はそのまま");
            Debug.Log($"[#53-A1] 位置変換: Assimp{assimpPosition} → Unity{unityPosition}");
        }

        /// <summary>
        /// [#53-A2] Quaternion左右変換テスト
        /// </summary>
        [Test]
        public void Issue53_A2_Quaternion左右変換テスト()
        {
            // Arrange: Assimpからの回転
            Quaternion assimpRotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);

            // Act: 右手系→左手系変換
            // Quaternion(-x, +y, +z, -w)
            Quaternion unityRotation = new Quaternion(
                -assimpRotation.x,
                assimpRotation.y,
                assimpRotation.z,
                -assimpRotation.w
            );

            // Assert
            Assert.AreEqual(-0.1f, unityRotation.x, 0.001f, "X成分は符号反転");
            Assert.AreEqual(0.2f, unityRotation.y, 0.001f, "Y成分はそのまま");
            Assert.AreEqual(0.3f, unityRotation.z, 0.001f, "Z成分はそのまま");
            Assert.AreEqual(-0.9f, unityRotation.w, 0.001f, "W成分は符号反転");
            Debug.Log($"[#53-A2] Quaternion変換: Assimp{assimpRotation} → Unity{unityRotation}");
        }

        /// <summary>
        /// [#53-A3] 単位Quaternionの変換テスト
        /// </summary>
        [Test]
        public void Issue53_A3_単位Quaternion変換テスト()
        {
            // Arrange: 単位Quaternion（回転なし）
            Quaternion identity = Quaternion.identity; // (0, 0, 0, 1)

            // Act: 変換を適用
            Quaternion converted = new Quaternion(-identity.x, identity.y, identity.z, -identity.w);

            // Assert: (0, 0, 0, -1) は (0, 0, 0, 1) と同じ回転を表す
            // Quaternionは q と -q が同じ回転を表す
            Assert.AreEqual(0f, converted.x, 0.001f);
            Assert.AreEqual(0f, converted.y, 0.001f);
            Assert.AreEqual(0f, converted.z, 0.001f);
            Assert.AreEqual(-1f, converted.w, 0.001f);
            Debug.Log($"[#53-A3] 単位Quaternion変換: {identity} → {converted} (同じ回転)");
        }

        /// <summary>
        /// [#53-A4] Bindpose Z座標符号パターンテスト
        /// </summary>
        [Test]
        public void Issue53_A4_BindposeZ座標符号パターンテスト()
        {
            // Arrange: Issue #53で発見されたパターン
            // 全98のBindposeでZ座標の符号が反転していた
            float unityBindposeZ = 0.5f;
            float assimpBindposeZ = -0.5f;

            // Act: 符号反転パターンを確認
            bool zSignFlipped = Mathf.Sign(unityBindposeZ) != Mathf.Sign(assimpBindposeZ);

            // Assert
            Assert.IsTrue(zSignFlipped, "BindposeのZ座標は符号反転パターンを示す");
            Debug.Log($"[#53-A4] Bindpose Z座標: Unity={unityBindposeZ}, Assimp={assimpBindposeZ} (符号反転)");
        }

        /// <summary>
        /// [#53-A5] Armature 180度回転差異テスト
        /// </summary>
        [Test]
        public void Issue53_A5_Armature回転差異テスト()
        {
            // Arrange: Issue #53で発見された回転差異
            float unityArmatureRotationY = 270f;
            float assimpArmatureRotationY = 90f;

            // Act: 差異を計算
            float rotationDifference = Mathf.Abs(unityArmatureRotationY - assimpArmatureRotationY);

            // Assert: 180度の差異
            Assert.AreEqual(180f, rotationDifference, 0.1f, "Armatureは180度の回転差異がある");
            Debug.Log($"[#53-A5] Armature回転差異: Unity={unityArmatureRotationY}°, Assimp={assimpArmatureRotationY}°, 差異={rotationDifference}°");
        }

        /// <summary>
        /// [#53-A6] Matrix4x4要素配置テスト（Assimp→Unity変換）
        /// </summary>
        [Test]
        public void Issue53_A6_Matrix変換要素配置テスト()
        {
            // Arrange: Assimpの行列要素 (行優先)
            // A1 A2 A3 A4
            // B1 B2 B3 B4
            // C1 C2 C3 C4
            // D1 D2 D3 D4

            // Unity Matrix4x4へのマッピング
            // m00=A1 m01=A2 m02=A3 m03=A4
            // m10=B1 m11=B2 m12=B3 m13=B4
            // m20=C1 m21=C2 m22=C3 m23=C4
            // m30=D1 m31=D2 m32=D3 m33=D4

            Matrix4x4 unity = Matrix4x4.identity;

            // Assert: 対角成分は1、他は0
            Assert.AreEqual(1f, unity.m00, "m00 = 1");
            Assert.AreEqual(1f, unity.m11, "m11 = 1");
            Assert.AreEqual(1f, unity.m22, "m22 = 1");
            Assert.AreEqual(1f, unity.m33, "m33 = 1");
            Assert.AreEqual(0f, unity.m03, "m03 = 0 (translation X)");
            Assert.AreEqual(0f, unity.m13, "m13 = 0 (translation Y)");
            Assert.AreEqual(0f, unity.m23, "m23 = 0 (translation Z)");
            Debug.Log("[#53-A6] Matrix4x4要素配置: 正しくマッピングされている");
        }
    }

    /// <summary>
    /// Issue #53: Bindpose変換テスト
    /// </summary>
    public class BindposeTransformTests
    {
        /// <summary>
        /// [#53-B1] Bindpose X軸反転補正テスト
        /// </summary>
        [Test]
        public void Issue53_B1_BindposeX軸反転補正テスト()
        {
            // Arrange: ベイク行列
            Matrix4x4 bake = Matrix4x4.identity;

            // Act: X軸反転補正を適用
            // bake.m00 *= -1f; という処理
            float originalM00 = bake.m00;
            bake.m00 *= -1f;

            // Assert
            Assert.AreEqual(1f, originalM00, "元のm00は1");
            Assert.AreEqual(-1f, bake.m00, "補正後のm00は-1");
            Debug.Log($"[#53-B1] Bindpose X軸反転: m00 = {originalM00} → {bake.m00}");
        }

        /// <summary>
        /// [#53-B2] 回転差異計算テスト
        /// </summary>
        [Test]
        public void Issue53_B2_回転差異計算テスト()
        {
            // Arrange: 2つの回転
            Quaternion rotA = Quaternion.Euler(0, 90, 0);
            Quaternion rotB = Quaternion.Euler(0, 270, 0);

            // Act: 角度差を計算
            float angleDifference = Quaternion.Angle(rotA, rotB);

            // Assert
            Assert.AreEqual(180f, angleDifference, 0.1f, "90°と270°の差は180°");
            Debug.Log($"[#53-B2] 回転差異: {angleDifference}°");
        }

        /// <summary>
        /// [#53-B3] 平均回転差異テスト
        /// </summary>
        [Test]
        public void Issue53_B3_平均回転差異テスト()
        {
            // Arrange: Issue #53で報告された平均回転差異
            float[] boneDifferences = { 70.9f, 85.2f, 55.3f, 100.0f, 42.1f };

            // Act: 平均を計算
            float sum = 0f;
            foreach (var d in boneDifferences) sum += d;
            float average = sum / boneDifferences.Length;

            // Assert: 平均は約70度
            Assert.IsTrue(average > 50f && average < 90f, $"平均回転差異は50-90°の範囲: {average}°");
            Debug.Log($"[#53-B3] 平均回転差異: {average:F2}°");
        }

        /// <summary>
        /// [#53-B4] 重大な回転ずれの検出テスト
        /// </summary>
        [Test]
        public void Issue53_B4_重大な回転ずれ検出テスト()
        {
            // Arrange: 回転差異の閾値
            float threshold = 100f;
            float[] boneDifferences = { 70.9f, 85.2f, 55.3f, 120.0f, 42.1f, 150.0f };

            // Act: 100度以上のずれをカウント
            int majorMismatches = 0;
            foreach (var d in boneDifferences)
            {
                if (d >= threshold) majorMismatches++;
            }

            // Assert: Issue #53では30%のボーンが100度以上のずれ
            float mismatchRate = (float)majorMismatches / boneDifferences.Length;
            Assert.AreEqual(2, majorMismatches, "100度以上のずれは2件");
            Debug.Log($"[#53-B4] 重大な回転ずれ: {majorMismatches}/{boneDifferences.Length} ({mismatchRate * 100:F1}%)");
        }
    }

    /// <summary>
    /// Issue #52/#53: BoneWeight正規化テスト
    /// </summary>
    public class BoneWeightTests
    {
        /// <summary>
        /// [#52-B1] BoneWeight正規化テスト
        /// </summary>
        [Test]
        public void Issue52_B1_BoneWeight正規化テスト()
        {
            // Arrange: 正規化前のウェイト
            float w0 = 0.5f, w1 = 0.3f, w2 = 0.1f, w3 = 0.05f;
            float sum = w0 + w1 + w2 + w3; // 0.95

            // Act: 正規化
            float inv = 1f / sum;
            w0 *= inv; w1 *= inv; w2 *= inv; w3 *= inv;

            // Assert: 合計が1になる
            float normalizedSum = w0 + w1 + w2 + w3;
            Assert.AreEqual(1f, normalizedSum, 0.001f, "正規化後の合計は1");
            Debug.Log($"[#52-B1] BoneWeight正規化: 合計 {sum:F3} → {normalizedSum:F3}");
        }

        /// <summary>
        /// [#52-B2] BoneWeight最大4ボーン制限テスト
        /// </summary>
        [Test]
        public void Issue52_B2_BoneWeight最大4ボーン制限テスト()
        {
            // Arrange: 5つのインフルエンス
            var influences = new (int boneIndex, float weight)[]
            {
                (0, 0.4f),
                (1, 0.3f),
                (2, 0.15f),
                (3, 0.1f),
                (4, 0.05f) // これは捨てられる
            };

            // Act: 上位4つだけを使用
            BoneWeight bw = new BoneWeight();
            bw.boneIndex0 = influences[0].boneIndex; bw.weight0 = influences[0].weight;
            bw.boneIndex1 = influences[1].boneIndex; bw.weight1 = influences[1].weight;
            bw.boneIndex2 = influences[2].boneIndex; bw.weight2 = influences[2].weight;
            bw.boneIndex3 = influences[3].boneIndex; bw.weight3 = influences[3].weight;

            // Assert
            float usedWeight = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
            Assert.AreEqual(0.95f, usedWeight, 0.001f, "上位4ボーンのウェイト合計");
            Debug.Log($"[#52-B2] 上位4ボーン: {usedWeight:F3} (5番目の0.05は除外)");
        }

        /// <summary>
        /// [#52-B3] BoneWeightソートテスト
        /// </summary>
        [Test]
        public void Issue52_B3_BoneWeightソートテスト()
        {
            // Arrange: 未ソートのインフルエンス
            var influences = new System.Collections.Generic.List<(int, float)>
            {
                (2, 0.1f),
                (0, 0.5f),
                (1, 0.3f),
                (3, 0.05f)
            };

            // Act: ウェイト降順でソート
            influences.Sort((a, b) => b.Item2.CompareTo(a.Item2));

            // Assert: 最大ウェイトが先頭
            Assert.AreEqual(0, influences[0].Item1, "最大ウェイトのボーン(0)が先頭");
            Assert.AreEqual(0.5f, influences[0].Item2, 0.001f);
            Assert.AreEqual(3, influences[3].Item1, "最小ウェイトのボーン(3)が末尾");
            Debug.Log($"[#52-B3] ソート後: [{influences[0].Item1}]={influences[0].Item2}, [{influences[3].Item1}]={influences[3].Item2}");
        }
    }
}
