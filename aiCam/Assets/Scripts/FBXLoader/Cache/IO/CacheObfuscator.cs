using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AICam.AvatarCache.IO
{
    /// <summary>
    /// キャッシュファイルの簡易難読化ユーティリティ
    /// SHA256ハッシュベースのXOR暗号化を使用
    /// </summary>
    public static class CacheObfuscator
    {
        // 難読化のシークレットキー（アプリ固有）
        private const string SECRET_KEY = "AICam_AvatarCache_2024_Obfuscation_Key";

        // 難読化マジックヘッダー（難読化されたファイルの識別用）
        public const string OBFUSCATION_MAGIC = "ACOB"; // Avatar Cache OBfuscated
        private const int MAGIC_LENGTH = 4;

        /// <summary>
        /// データを難読化
        /// </summary>
        /// <param name="data">元データ</param>
        /// <param name="cacheId">キャッシュID（キー生成に使用）</param>
        /// <returns>難読化されたデータ（マジックヘッダー付き）</returns>
        public static byte[] Obfuscate(byte[] data, string cacheId)
        {
            if (data == null || data.Length == 0)
                return data;

            var key = GenerateKey(cacheId);
            var obfuscated = XorTransform(data, key);

            // マジックヘッダーを付加
            var result = new byte[MAGIC_LENGTH + obfuscated.Length];
            Encoding.ASCII.GetBytes(OBFUSCATION_MAGIC).CopyTo(result, 0);
            obfuscated.CopyTo(result, MAGIC_LENGTH);

            return result;
        }

        /// <summary>
        /// データを復号化
        /// </summary>
        /// <param name="data">難読化されたデータ</param>
        /// <param name="cacheId">キャッシュID</param>
        /// <returns>元データ</returns>
        public static byte[] Deobfuscate(byte[] data, string cacheId)
        {
            if (data == null || data.Length <= MAGIC_LENGTH)
                return data;

            // マジックヘッダーを確認
            var magic = Encoding.ASCII.GetString(data, 0, MAGIC_LENGTH);
            if (magic != OBFUSCATION_MAGIC)
            {
                // 難読化されていないデータ（後方互換性）
                Debug.LogWarning("[CacheObfuscator] Data is not obfuscated, returning as-is");
                return data;
            }

            // データ部分を抽出
            var obfuscatedData = new byte[data.Length - MAGIC_LENGTH];
            Array.Copy(data, MAGIC_LENGTH, obfuscatedData, 0, obfuscatedData.Length);

            var key = GenerateKey(cacheId);
            return XorTransform(obfuscatedData, key);
        }

        /// <summary>
        /// ファイルを難読化して保存
        /// </summary>
        public static void ObfuscateFile(string sourcePath, string destPath, string cacheId)
        {
            var data = File.ReadAllBytes(sourcePath);
            var obfuscated = Obfuscate(data, cacheId);

            var directory = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(destPath, obfuscated);
        }

        /// <summary>
        /// 難読化ファイルを復号化して読み込み
        /// </summary>
        public static byte[] DeobfuscateFile(string filePath, string cacheId)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var data = File.ReadAllBytes(filePath);
            return Deobfuscate(data, cacheId);
        }

        /// <summary>
        /// ファイルが難読化されているか確認
        /// </summary>
        public static bool IsObfuscated(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                if (stream.Length < MAGIC_LENGTH)
                    return false;

                var magicBytes = new byte[MAGIC_LENGTH];
                stream.Read(magicBytes, 0, MAGIC_LENGTH);
                var magic = Encoding.ASCII.GetString(magicBytes);
                return magic == OBFUSCATION_MAGIC;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// バイト配列が難読化されているか確認
        /// </summary>
        public static bool IsObfuscated(byte[] data)
        {
            if (data == null || data.Length < MAGIC_LENGTH)
                return false;

            var magic = Encoding.ASCII.GetString(data, 0, MAGIC_LENGTH);
            return magic == OBFUSCATION_MAGIC;
        }

        /// <summary>
        /// SHA256ハッシュベースのキー生成
        /// </summary>
        private static byte[] GenerateKey(string cacheId)
        {
            var combined = SECRET_KEY + cacheId;
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        }

        /// <summary>
        /// XOR変換（暗号化/復号化共通）
        /// </summary>
        private static byte[] XorTransform(byte[] data, byte[] key)
        {
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ key[i % key.Length]);
            }
            return result;
        }
    }
}
