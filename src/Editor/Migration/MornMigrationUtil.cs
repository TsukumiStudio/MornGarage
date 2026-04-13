#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MornLib
{
    /// <summary>Migration 全体で共有する Unity Script GUID や YAML ユーティリティ。</summary>
    internal static class MornMigrationUtil
    {
        public const string UnityButtonGuid = "4e29b1a8efbd4b44bb3f3716e73f07ff";
        public const string MornUGUIButtonGuid = "6a3b7849bb0c4f15b15dee46b0711a7c";
        public const string SubStateGuid = "aac67bf328824705a55354ac6d26608c";
        public const string LinkModuleGuid = "8be286109e114b849574ebd8390b6191";
        public const string WaitTimeStateGuid = "178f13c9c785bb341b8537958dc16138";

        /// <summary>assetPath からフルパスを取得。</summary>
        public static string ToFullPath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetPath);
        }

        /// <summary>assetPath に対応する YAML (.prefab/.unity/.asset) を読み込む。失敗時 null。</summary>
        public static string SafeRead(string assetPath)
        {
            try
            {
                return File.ReadAllText(ToFullPath(assetPath));
            }
            catch
            {
                return null;
            }
        }

        public static bool SafeWrite(string assetPath, string content)
        {
            try
            {
                File.WriteAllText(ToFullPath(assetPath), content);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {assetPath} 書き込み失敗: {e.Message}");
                return false;
            }
        }

        /// <summary>assetPath を Unity 上で ping する。</summary>
        public static void Ping(string assetPath)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }

        /// <summary>
        /// YAML ブロックヘッダー "--- !u!114 &12345 stripped" などから fileID を取り出す。
        /// </summary>
        public static bool TryParseBlockFileId(string headerLine, out string fileId, out bool isStripped)
        {
            fileId = null;
            isStripped = false;
            const string prefix = "--- !u!114 &";
            if (!headerLine.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var start = prefix.Length;
            var end = headerLine.Length;
            for (var i = start; i < headerLine.Length; i++)
            {
                if (!char.IsDigit(headerLine[i]))
                {
                    end = i;
                    break;
                }
            }

            if (end == start)
            {
                return false;
            }

            fileId = headerLine.Substring(start, end - start);
            isStripped = headerLine.IndexOf("stripped", end, StringComparison.Ordinal) >= 0;
            return true;
        }

        /// <summary>YAML 行から "guid: xxxx" を抽出。</summary>
        public static bool TryParseGuid(string line, out string guid)
        {
            guid = null;
            var idx = line.IndexOf("guid: ", StringComparison.Ordinal);
            if (idx < 0)
            {
                return false;
            }

            var start = idx + 6;
            var end = line.IndexOfAny(new[] { ',', ' ', '}', '\r', '\n' }, start);
            if (end < 0)
            {
                end = line.Length;
            }

            if (end <= start)
            {
                return false;
            }

            guid = line.Substring(start, end - start);
            return true;
        }

        /// <summary>YAML 行から "fileID: 12345" を抽出 (文字列)。</summary>
        public static bool TryParseFileId(string line, out string fileId)
        {
            fileId = null;
            var idx = line.IndexOf("fileID: ", StringComparison.Ordinal);
            if (idx < 0)
            {
                return false;
            }

            var start = idx + 8;
            var end = line.IndexOfAny(new[] { ',', '}', ' ', '\r', '\n' }, start);
            if (end < 0)
            {
                end = line.Length;
            }

            if (end <= start)
            {
                return false;
            }

            fileId = line.Substring(start, end - start);
            return true;
        }

        /// <summary>行のインデント幅 (左空白数) を返す。</summary>
        public static int IndentOf(string line)
        {
            var i = 0;
            while (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            return i;
        }
    }
}
#endif
