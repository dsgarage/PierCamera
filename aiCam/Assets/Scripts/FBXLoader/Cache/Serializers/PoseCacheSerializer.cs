using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// ポーズキャッシュのシリアライザー
    /// </summary>
    public static class PoseCacheSerializer
    {
        private const int ANIMATION_VERSION = 1;

        /// <summary>
        /// ポーズマニフェストをJSONにシリアライズ
        /// </summary>
        public static string SerializeManifestToJson(PoseManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            return JsonUtility.ToJson(manifest, true);
        }

        /// <summary>
        /// JSONからポーズマニフェストをデシリアライズ
        /// </summary>
        public static PoseManifest DeserializeManifestFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            return JsonUtility.FromJson<PoseManifest>(json);
        }

        /// <summary>
        /// アニメーションクリップをバイナリにシリアライズ
        /// </summary>
        public static void SerializeAnimationToBinary(AnimationClip clip, string filePath)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));

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
            writer.Write(Encoding.ASCII.GetBytes(AnimationCacheHeader.MAGIC));
            writer.Write(ANIMATION_VERSION);

            // クリップ情報
            writer.Write(clip.name ?? "");
            writer.Write(clip.frameRate);
            writer.Write(clip.length);
            writer.Write((int)clip.wrapMode);

#if UNITY_EDITOR
            // エディタでのみカーブデータを抽出して保存
            var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
            writer.Write(bindings.Length);

            foreach (var binding in bindings)
            {
                // バインディング情報
                writer.Write(binding.path ?? "");
                writer.Write(binding.propertyName ?? "");
                writer.Write(binding.type?.AssemblyQualifiedName ?? "");

                // カーブデータ
                var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null)
                {
                    var keys = curve.keys;
                    writer.Write(keys.Length);

                    foreach (var key in keys)
                    {
                        writer.Write(key.time);
                        writer.Write(key.value);
                        writer.Write(key.inTangent);
                        writer.Write(key.outTangent);
                        writer.Write(key.inWeight);
                        writer.Write(key.outWeight);
                        writer.Write((int)key.weightedMode);
                    }
                }
                else
                {
                    writer.Write(0); // キーなし
                }
            }
#else
            // ランタイムではカーブデータなし
            writer.Write(0);
#endif

            Debug.Log($"[PoseCacheSerializer] Animation serialized: {filePath}");
        }

        /// <summary>
        /// バイナリからアニメーションクリップをデシリアライズ
        /// </summary>
        public static AnimationClip DeserializeAnimationFromBinary(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Animation cache file not found: {filePath}");

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            // ヘッダー読み込み
            var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic != AnimationCacheHeader.MAGIC)
                throw new InvalidDataException($"Invalid magic number: {magic}");

            var version = reader.ReadInt32();
            if (version != ANIMATION_VERSION)
                throw new InvalidDataException($"Unsupported version: {version}");

            // クリップ情報
            var clipName = reader.ReadString();
            var frameRate = reader.ReadSingle();
            var length = reader.ReadSingle();
            var wrapMode = (WrapMode)reader.ReadInt32();

            var clip = new AnimationClip
            {
                name = clipName,
                frameRate = frameRate,
                wrapMode = wrapMode
            };

            // カーブデータを読み込み
            var bindingCount = reader.ReadInt32();

            for (int i = 0; i < bindingCount; i++)
            {
                // バインディング情報
                var path = reader.ReadString();
                var propertyName = reader.ReadString();
                var typeName = reader.ReadString();

                // カーブデータ
                var keyCount = reader.ReadInt32();
                var keys = new Keyframe[keyCount];

                for (int j = 0; j < keyCount; j++)
                {
                    var key = new Keyframe
                    {
                        time = reader.ReadSingle(),
                        value = reader.ReadSingle(),
                        inTangent = reader.ReadSingle(),
                        outTangent = reader.ReadSingle(),
                        inWeight = reader.ReadSingle(),
                        outWeight = reader.ReadSingle(),
                        weightedMode = (WeightedMode)reader.ReadInt32()
                    };
                    keys[j] = key;
                }

                if (keys.Length > 0)
                {
                    var curve = new AnimationCurve(keys);

                    // タイプを解決
                    var type = !string.IsNullOrEmpty(typeName) ? Type.GetType(typeName) : typeof(Transform);
                    if (type != null)
                    {
                        clip.SetCurve(path, type, propertyName, curve);
                    }
                }
            }

            Debug.Log($"[PoseCacheSerializer] Animation deserialized: {filePath}");
            return clip;
        }

        /// <summary>
        /// アニメーションバイナリのマジックナンバーを検証
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
                return magic == AnimationCacheHeader.MAGIC;
            }
            catch
            {
                return false;
            }
        }
    }
}
