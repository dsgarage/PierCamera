using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// メッシュのバイナリシリアライザー
    /// </summary>
    public static class MeshCacheSerializer
    {
        public const string MAGIC = "MESH";
        private const int VERSION = 1;

        /// <summary>
        /// メッシュをバイナリにシリアライズ
        /// </summary>
        public static void SerializeToBinary(Mesh[] meshes, string filePath)
        {
            if (meshes == null)
                throw new ArgumentNullException(nameof(meshes));

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
            writer.Write(meshes.Length);

            // 各メッシュを書き込み
            foreach (var mesh in meshes)
            {
                WriteMesh(writer, mesh);
            }
        }

        /// <summary>
        /// バイナリからメッシュをデシリアライズ
        /// </summary>
        public static Mesh[] DeserializeFromBinary(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Mesh cache file not found: {filePath}");

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
            var meshes = new Mesh[meshCount];

            // 各メッシュを読み込み
            for (int i = 0; i < meshCount; i++)
            {
                meshes[i] = ReadMesh(reader);
            }

            return meshes;
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

        private static void WriteMesh(BinaryWriter writer, Mesh mesh)
        {
            // メッシュ名
            writer.Write(mesh.name ?? "");

            // 頂点データ
            var vertices = mesh.vertices;
            writer.Write(vertices.Length);
            foreach (var v in vertices)
            {
                writer.Write(v.x);
                writer.Write(v.y);
                writer.Write(v.z);
            }

            // 法線
            var normals = mesh.normals;
            writer.Write(normals.Length);
            foreach (var n in normals)
            {
                writer.Write(n.x);
                writer.Write(n.y);
                writer.Write(n.z);
            }

            // タンジェント
            var tangents = mesh.tangents;
            writer.Write(tangents.Length);
            foreach (var t in tangents)
            {
                writer.Write(t.x);
                writer.Write(t.y);
                writer.Write(t.z);
                writer.Write(t.w);
            }

            // UV
            var uvs = mesh.uv;
            writer.Write(uvs.Length);
            foreach (var uv in uvs)
            {
                writer.Write(uv.x);
                writer.Write(uv.y);
            }

            // UV2
            var uv2s = mesh.uv2;
            writer.Write(uv2s.Length);
            foreach (var uv in uv2s)
            {
                writer.Write(uv.x);
                writer.Write(uv.y);
            }

            // 頂点カラー
            var colors = mesh.colors;
            writer.Write(colors.Length);
            foreach (var c in colors)
            {
                writer.Write(c.r);
                writer.Write(c.g);
                writer.Write(c.b);
                writer.Write(c.a);
            }

            // ボーンウェイト
            var boneWeights = mesh.boneWeights;
            writer.Write(boneWeights.Length);
            foreach (var bw in boneWeights)
            {
                writer.Write(bw.boneIndex0);
                writer.Write(bw.boneIndex1);
                writer.Write(bw.boneIndex2);
                writer.Write(bw.boneIndex3);
                writer.Write(bw.weight0);
                writer.Write(bw.weight1);
                writer.Write(bw.weight2);
                writer.Write(bw.weight3);
            }

            // バインドポーズ
            var bindposes = mesh.bindposes;
            writer.Write(bindposes.Length);
            foreach (var bp in bindposes)
            {
                for (int i = 0; i < 16; i++)
                {
                    writer.Write(bp[i]);
                }
            }

            // サブメッシュ
            writer.Write(mesh.subMeshCount);
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var triangles = mesh.GetTriangles(i);
                writer.Write(triangles.Length);
                foreach (var t in triangles)
                {
                    writer.Write(t);
                }
            }
        }

        private static Mesh ReadMesh(BinaryReader reader)
        {
            var mesh = new Mesh();

            // メッシュ名
            mesh.name = reader.ReadString();

            // 頂点データ
            var vertexCount = reader.ReadInt32();
            var vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            mesh.vertices = vertices;

            // 法線
            var normalCount = reader.ReadInt32();
            if (normalCount > 0)
            {
                var normals = new Vector3[normalCount];
                for (int i = 0; i < normalCount; i++)
                {
                    normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                mesh.normals = normals;
            }

            // タンジェント
            var tangentCount = reader.ReadInt32();
            if (tangentCount > 0)
            {
                var tangents = new Vector4[tangentCount];
                for (int i = 0; i < tangentCount; i++)
                {
                    tangents[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                mesh.tangents = tangents;
            }

            // UV
            var uvCount = reader.ReadInt32();
            if (uvCount > 0)
            {
                var uvs = new Vector2[uvCount];
                for (int i = 0; i < uvCount; i++)
                {
                    uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
                mesh.uv = uvs;
            }

            // UV2
            var uv2Count = reader.ReadInt32();
            if (uv2Count > 0)
            {
                var uv2s = new Vector2[uv2Count];
                for (int i = 0; i < uv2Count; i++)
                {
                    uv2s[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
                mesh.uv2 = uv2s;
            }

            // 頂点カラー
            var colorCount = reader.ReadInt32();
            if (colorCount > 0)
            {
                var colors = new Color[colorCount];
                for (int i = 0; i < colorCount; i++)
                {
                    colors[i] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                mesh.colors = colors;
            }

            // ボーンウェイト
            var boneWeightCount = reader.ReadInt32();
            if (boneWeightCount > 0)
            {
                var boneWeights = new BoneWeight[boneWeightCount];
                for (int i = 0; i < boneWeightCount; i++)
                {
                    boneWeights[i] = new BoneWeight
                    {
                        boneIndex0 = reader.ReadInt32(),
                        boneIndex1 = reader.ReadInt32(),
                        boneIndex2 = reader.ReadInt32(),
                        boneIndex3 = reader.ReadInt32(),
                        weight0 = reader.ReadSingle(),
                        weight1 = reader.ReadSingle(),
                        weight2 = reader.ReadSingle(),
                        weight3 = reader.ReadSingle()
                    };
                }
                mesh.boneWeights = boneWeights;
            }

            // バインドポーズ
            var bindposeCount = reader.ReadInt32();
            if (bindposeCount > 0)
            {
                var bindposes = new Matrix4x4[bindposeCount];
                for (int i = 0; i < bindposeCount; i++)
                {
                    var m = new Matrix4x4();
                    for (int j = 0; j < 16; j++)
                    {
                        m[j] = reader.ReadSingle();
                    }
                    bindposes[i] = m;
                }
                mesh.bindposes = bindposes;
            }

            // サブメッシュ
            var subMeshCount = reader.ReadInt32();
            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                var triangleCount = reader.ReadInt32();
                var triangles = new int[triangleCount];
                for (int j = 0; j < triangleCount; j++)
                {
                    triangles[j] = reader.ReadInt32();
                }
                mesh.SetTriangles(triangles, i);
            }

            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
