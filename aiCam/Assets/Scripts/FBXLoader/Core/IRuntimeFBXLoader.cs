using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

namespace AICam.FBXLoader
{
    /// <summary>
    /// Runtime FBXローダーのインターフェース
    /// </summary>
    public interface IRuntimeFBXLoader
    {
        /// <summary>
        /// FBXファイルをロードしてGameObjectを返す
        /// </summary>
        /// <param name="fbxPath">FBXファイルのパス</param>
        /// <returns>ロードされたGameObject</returns>
        UniTask<GameObject> LoadFBX(string fbxPath);

        /// <summary>
        /// MeshNode名とMaterial名のマッピング情報を取得
        /// </summary>
        /// <returns>MeshNode名をキーとしたMaterial名リストの辞書</returns>
        Dictionary<string, List<string>> GetMeshNodeToMaterialNames();
    }
}
