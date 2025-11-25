using System;
using System.Collections.Generic;
using UnityEngine;

namespace AICam.FBXLoader
{
    /// <summary>
    /// Assimp Sceneから抽出したキャッシュデータ（メッシュ/ウェイトを除く）
    /// マテリアル情報、ノード階層、マッピング情報を保持
    /// </summary>
    [Serializable]
    public class AssimpSceneCache
    {
        /// <summary>
        /// キャッシュバージョン（フォーマット変更時にインクリメント）
        /// </summary>
        public string version = "1.0.0";

        /// <summary>
        /// FBXファイル名
        /// </summary>
        public string fbxFileName;

        /// <summary>
        /// FBXファイルの最終更新日時（キャッシュ有効性チェック用）
        /// </summary>
        public string fbxLastModified;

        /// <summary>
        /// 生成日時
        /// </summary>
        public string generatedDate;

        /// <summary>
        /// マテリアル情報リスト
        /// </summary>
        public List<MaterialInfo> materials = new List<MaterialInfo>();

        /// <summary>
        /// ノード情報リスト（階層構造）
        /// </summary>
        public List<NodeInfo> nodes = new List<NodeInfo>();

        /// <summary>
        /// MeshNode名→MaterialIndex[]のマッピング
        /// </summary>
        public Dictionary<string, int[]> meshNodeToMaterialIndices = new Dictionary<string, int[]>();

        /// <summary>
        /// マテリアル情報
        /// </summary>
        [Serializable]
        public class MaterialInfo
        {
            public string name;
            public int materialIndex;

            // シェーダー情報
            public string shaderName;
            public string shaderGuid;

            // カラープロパティ
            public SerializableColor diffuseColor;
            public SerializableColor specularColor;
            public SerializableColor ambientColor;
            public SerializableColor emissiveColor;

            // スカラープロパティ
            public float shininess;
            public float opacity;
            public float reflectivity;

            // テクスチャ情報
            public List<TextureInfo> textures = new List<TextureInfo>();
        }

        /// <summary>
        /// テクスチャ情報
        /// </summary>
        [Serializable]
        public class TextureInfo
        {
            public string textureType;  // Diffuse, Normal, Specular, etc.
            public string filePath;     // 相対パスまたは絶対パス
            public string fileName;     // ファイル名のみ
            public bool isEmbedded;     // 埋め込みテクスチャか
            public int embeddedIndex;   // 埋め込みテクスチャのインデックス
        }

        /// <summary>
        /// ノード情報（階層構造）
        /// </summary>
        [Serializable]
        public class NodeInfo
        {
            public string name;
            public string parentName;
            public int[] meshIndices;       // このノードが持つメッシュのインデックス
            public int[] materialIndices;   // このノードが使用するマテリアルのインデックス
            public bool hasMesh;
        }

        /// <summary>
        /// Colorのシリアライズ可能版
        /// </summary>
        [Serializable]
        public class SerializableColor
        {
            public float r;
            public float g;
            public float b;
            public float a;

            public SerializableColor() { }

            public SerializableColor(float r, float g, float b, float a = 1.0f)
            {
                this.r = r;
                this.g = g;
                this.b = b;
                this.a = a;
            }

            public Color ToColor()
            {
                return new Color(r, g, b, a);
            }

            public static SerializableColor FromColor(Color color)
            {
                return new SerializableColor(color.r, color.g, color.b, color.a);
            }
        }
    }
}
