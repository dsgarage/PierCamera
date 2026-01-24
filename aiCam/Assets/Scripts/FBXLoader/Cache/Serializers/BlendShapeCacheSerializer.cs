using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// BlendShapeのバイナリシリアライザー
    /// </summary>
    public static class BlendShapeCacheSerializer
    {
        public const string MAGIC = "BLND";
        private const int VERSION = 1;

        /// <summary>
        /// BlendShapeをバイナリにシリアライズ
        /// </summary>
        public static void SerializeToBinary(SkinnedMeshRenderer[] smrs, string filePath)
        {
            if (smrs == null)
                throw new ArgumentNullException(nameof(smrs));

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(filePath, FileMode.Create);
            using var writer = new BinaryWriter(stream, Encoding.UTF8);

            // ヘッダー書き込み
            writer.Write(Encoding.ASCII.GetBytes(MAGIC));
            writer.Write(VERSION);

            // BlendShapeを持つメッシュのみをカウント
            var meshesWithBlendShapes = new List<(SkinnedMeshRenderer smr, Mesh mesh)>();
            foreach (var smr in smrs)
            {
                if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                {
                    meshesWithBlendShapes.Add((smr, smr.sharedMesh));
                }
            }

            writer.Write(meshesWithBlendShapes.Count);

            // 各メッシュのBlendShapeを書き込み
            foreach (var (smr, mesh) in meshesWithBlendShapes)
            {
                WriteBlendShapes(writer, mesh);
            }
        }

        /// <summary>
        /// バイナリからBlendShapeをデシリアライズしてメッシュに適用
        /// </summary>
        public static void DeserializeAndApply(string filePath, Mesh[] meshes)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (meshes == null)
                throw new ArgumentNullException(nameof(meshes));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"BlendShape cache file not found: {filePath}");

            // メッシュ名から検索用の辞書を作成
            var meshByName = new Dictionary<string, Mesh>();
            foreach (var mesh in meshes)
            {
                if (mesh != null && !string.IsNullOrEmpty(mesh.name))
                {
                    meshByName[mesh.name] = mesh;
                }
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            // ヘッダー読み込み
            var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic != MAGIC)
                throw new InvalidDataException($"Invalid magic number: {magic}");

            var version = reader.ReadInt32();
            if (version != VERSION)
                throw new InvalidDataException($"Unsupported version: {version}");

            var meshCount = reader.ReadInt32();

            // 各メッシュのBlendShapeを読み込み・適用
            for (int i = 0; i < meshCount; i++)
            {
                ReadAndApplyBlendShapesWithNameMatching(reader, meshByName);
            }
        }

        /// <summary>
        /// バイナリファイルのマジックナンバーを検証
        /// </summary>
        public static bool ValidateMagic(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                if (stream.Length < 4)
                    return false;

                var magicBytes = new byte[4];
                stream.Read(magicBytes, 0, 4);
                var magic = Encoding.ASCII.GetString(magicBytes);
                return magic == MAGIC;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteBlendShapes(BinaryWriter writer, Mesh mesh)
        {
            // メッシュ名
            writer.Write(mesh.name ?? "");

            // BlendShape数
            var blendShapeCount = mesh.blendShapeCount;
            writer.Write(blendShapeCount);

            // 頂点数
            var vertexCount = mesh.vertexCount;
            writer.Write(vertexCount);

            // 各BlendShape
            for (int i = 0; i < blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                var frameCount = mesh.GetBlendShapeFrameCount(i);

                writer.Write(name ?? "");
                writer.Write(frameCount);

                // 各フレーム
                for (int frame = 0; frame < frameCount; frame++)
                {
                    var weight = mesh.GetBlendShapeFrameWeight(i, frame);
                    writer.Write(weight);

                    // デルタ頂点を取得
                    var deltaVertices = new Vector3[vertexCount];
                    var deltaNormals = new Vector3[vertexCount];
                    var deltaTangents = new Vector3[vertexCount];

                    mesh.GetBlendShapeFrameVertices(i, frame, deltaVertices, deltaNormals, deltaTangents);

                    // デルタ頂点
                    foreach (var v in deltaVertices)
                    {
                        writer.Write(v.x);
                        writer.Write(v.y);
                        writer.Write(v.z);
                    }

                    // デルタ法線
                    foreach (var n in deltaNormals)
                    {
                        writer.Write(n.x);
                        writer.Write(n.y);
                        writer.Write(n.z);
                    }

                    // デルタタンジェント
                    foreach (var t in deltaTangents)
                    {
                        writer.Write(t.x);
                        writer.Write(t.y);
                        writer.Write(t.z);
                    }
                }
            }
        }

        private static void ReadAndApplyBlendShapesWithNameMatching(BinaryReader reader, Dictionary<string, Mesh> meshByName)
        {
            // メッシュ名を読み込み
            var meshName = reader.ReadString();

            // BlendShape数
            var blendShapeCount = reader.ReadInt32();

            // 頂点数
            var vertexCount = reader.ReadInt32();

            // 対応するメッシュを検索
            Mesh targetMesh = null;
            if (!string.IsNullOrEmpty(meshName) && meshByName.TryGetValue(meshName, out var mesh))
            {
                // 頂点数が一致する場合のみ適用
                if (mesh.vertexCount == vertexCount)
                {
                    targetMesh = mesh;
                }
                else
                {
                    Debug.LogWarning($"[BlendShapeCacheSerializer] Vertex count mismatch for mesh '{meshName}': expected {vertexCount}, got {mesh.vertexCount}");
                }
            }

            // 各BlendShape
            for (int i = 0; i < blendShapeCount; i++)
            {
                var name = reader.ReadString();
                var frameCount = reader.ReadInt32();

                // 各フレーム
                for (int frame = 0; frame < frameCount; frame++)
                {
                    var weight = reader.ReadSingle();

                    // デルタ頂点
                    var deltaVertices = new Vector3[vertexCount];
                    for (int v = 0; v < vertexCount; v++)
                    {
                        deltaVertices[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    }

                    // デルタ法線
                    var deltaNormals = new Vector3[vertexCount];
                    for (int v = 0; v < vertexCount; v++)
                    {
                        deltaNormals[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    }

                    // デルタタンジェント
                    var deltaTangents = new Vector3[vertexCount];
                    for (int v = 0; v < vertexCount; v++)
                    {
                        deltaTangents[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    }

                    // 対応するメッシュがある場合のみBlendShapeを追加
                    if (targetMesh != null)
                    {
                        targetMesh.AddBlendShapeFrame(name, weight, deltaVertices, deltaNormals, deltaTangents);
                    }
                }
            }
        }

        private static void SkipBlendShapes(BinaryReader reader)
        {
            // メッシュ名
            reader.ReadString();

            // BlendShape数
            var blendShapeCount = reader.ReadInt32();

            // 頂点数
            var vertexCount = reader.ReadInt32();

            // 各BlendShape
            for (int i = 0; i < blendShapeCount; i++)
            {
                reader.ReadString(); // name
                var frameCount = reader.ReadInt32();

                // 各フレーム
                for (int frame = 0; frame < frameCount; frame++)
                {
                    reader.ReadSingle(); // weight

                    // デルタデータをスキップ（Vector3 * 3 * vertexCount）
                    var bytesToSkip = vertexCount * 3 * 3 * sizeof(float);
                    reader.BaseStream.Seek(bytesToSkip, SeekOrigin.Current);
                }
            }
        }
    }
}
