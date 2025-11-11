using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AICam.FBXLoader
{
    public static class RuntimeHumanoidAvatarBuilderDebugHelper
    {
        // ----------------------------------------------------------------------
        //  共通：ログフォルダ（Assets と同階層に "Avatar DebugLog"）
        // ----------------------------------------------------------------------
        private static string GetLogDirectory()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string logDir      = Path.Combine(projectRoot, "Avatar DebugLog");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            return logDir;
        }

        // ----------------------------------------------------------------------
        //  1. Armature 階層をテキストで保存
        // ----------------------------------------------------------------------
        public static void DumpArmatureStructure(Transform root, string title = "Armature Dump")
        {
            if (root == null)
            {
                Debug.LogWarning("[DebugHelper] DumpArmatureStructure: root is null");
                return;
            }

            string ts   = DateTime.Now.ToString("yyMMddHHmmss");
            string path = Path.Combine(GetLogDirectory(),
                                       $"{ts}_RuntimeHumanoidAvatarBuilderLog.txt");

            var sb = new StringBuilder();
            sb.AppendLine($"=== {title} ===");
            sb.AppendLine($"Root : {root.name}");
            sb.AppendLine($"Time : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            AppendHierarchy(root, sb, "");

            try
            {
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Debug.Log($"[DebugHelper] Armature構造を出力しました -> {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DebugHelper] ファイル書き込み失敗: {ex.Message}");
            }
        }

        /// <summary>後方互換ラッパー</summary>
        public static void DumpHierarchy(GameObject rootGO, string reason = "")
        {
            if (rootGO == null)
            {
                Debug.LogWarning("[DebugHelper] DumpHierarchy: GameObject is null");
                return;
            }
            DumpArmatureStructure(rootGO.transform,
                $"{rootGO.name} - {reason}");
        }

        // ----------------------------------------------------------------------
        //  再帰出力ユーティリティ
        // ----------------------------------------------------------------------
        private static void AppendHierarchy(Transform t, StringBuilder sb, string indent)
        {
            sb.AppendLine($"{indent}- {t.name}");
            foreach (Transform child in t) AppendHierarchy(child, sb, indent + "  ");
        }

        public static string LogDirPath => GetLogDirectory();
    }
}
