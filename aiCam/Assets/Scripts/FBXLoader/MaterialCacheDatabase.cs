using UnityEngine;
using System.Collections.Generic;

namespace AICam.FBXLoader
{
    [CreateAssetMenu(fileName = "MaterialCacheDatabase", menuName = "AICam/Material Cache Database")]
    public class MaterialCacheDatabase : ScriptableObject
    {
        public List<MaterialMapping> mappings = new List<MaterialMapping>();

        [System.Serializable]
        public class MaterialMapping
        {
            public string fbxName; // FBXの名前
            public List<MaterialEntry> materialEntries = new List<MaterialEntry>();

            [System.Serializable]
            public class MaterialEntry
            {
                public string meshNodeName;     // Mesh Nodeの名前
                public string materialName;    // マテリアルの名前
                public string shaderName;      // 使用されているシェーダー名
                public Color mainColor;        // マテリアルのメインカラー
                public string texturePath;     // テクスチャのパス
            }
        }
    }
}
