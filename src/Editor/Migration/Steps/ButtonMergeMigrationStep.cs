#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// 同一 GameObject 上で Button と MornUGUIButton が共存する prefab/scene を検出し、
    /// Button 側を削除、参照は MornUGUIButton に引き継がせる統合移行。
    /// </summary>
    internal sealed class ButtonMergeMigrationStep : MornMigrationStep
    {
        public override string Title => $"Button + MornUGUIButton 共存 ({Results.Count}件)";
        public override Color HeaderColor => new(0.8f, 1f, 0.6f);

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            if (!HasNonStrippedButton(file.Content) || !file.Content.Contains(MornMigrationUtil.MornUGUIButtonGuid))
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = "Button + MornUGUIButton 共存 → 統合可能",
            });
        }

        public override bool FixOne(MornMigrationResult result)
        {
            return MergeSinglePrefab(result.AssetPath);
        }

        private static bool HasNonStrippedButton(string content)
        {
            var idx = 0;
            while (true)
            {
                idx = content.IndexOf(MornMigrationUtil.UnityButtonGuid, idx, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return false;
                }

                var blockStart = content.LastIndexOf("--- !u!", idx, StringComparison.Ordinal);
                if (blockStart >= 0)
                {
                    var headerEnd = content.IndexOf('\n', blockStart);
                    if (headerEnd > blockStart)
                    {
                        var header = content.Substring(blockStart, headerEnd - blockStart);
                        if (!header.Contains("stripped"))
                        {
                            return true;
                        }
                    }
                }

                idx++;
            }
        }

        private struct ComponentInfo
        {
            public string FileId;
            public string ScriptGuid;
            public string GameObjectFileId;
        }

        private static List<ComponentInfo> ParseComponents(List<string> lines)
        {
            var result = new List<ComponentInfo>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (!lines[i].StartsWith("--- !u!114 &"))
                {
                    continue;
                }

                var fileId = lines[i].Substring("--- !u!114 &".Length).Trim();
                var scriptGuid = "";
                var goFileId = "";
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (lines[j].StartsWith("--- "))
                    {
                        break;
                    }

                    var trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("m_Script:") && trimmed.Contains("guid: "))
                    {
                        var s = trimmed.IndexOf("guid: ", StringComparison.Ordinal) + 6;
                        var e = trimmed.IndexOf(',', s);
                        if (e > s)
                        {
                            scriptGuid = trimmed.Substring(s, e - s);
                        }
                    }

                    if (trimmed.StartsWith("m_GameObject:") && trimmed.Contains("fileID: "))
                    {
                        var s = trimmed.IndexOf("fileID: ", StringComparison.Ordinal) + 8;
                        var e = trimmed.IndexOf('}', s);
                        if (e > s)
                        {
                            goFileId = trimmed.Substring(s, e - s);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(scriptGuid))
                {
                    result.Add(new ComponentInfo
                    {
                        FileId = fileId,
                        ScriptGuid = scriptGuid,
                        GameObjectFileId = goFileId,
                    });
                }
            }

            return result;
        }

        private static void RemoveComponentBlock(List<string> lines, string fileId)
        {
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (!lines[i].StartsWith($"--- !u!114 &{fileId}"))
                {
                    continue;
                }

                var endLine = lines.Count - 1;
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (lines[j].StartsWith("--- "))
                    {
                        endLine = j - 1;
                        break;
                    }
                }

                lines.RemoveRange(i, endLine - i + 1);
                break;
            }
        }

        private static bool MergeSinglePrefab(string assetPath)
        {
            var content = MornMigrationUtil.SafeRead(assetPath);
            if (content == null)
            {
                return false;
            }

            try
            {
                var lines = content.Split('\n').ToList();
                var components = ParseComponents(lines);

                var goComponents = new Dictionary<string, List<ComponentInfo>>();
                foreach (var comp in components)
                {
                    if (!goComponents.ContainsKey(comp.GameObjectFileId))
                    {
                        goComponents[comp.GameObjectFileId] = new List<ComponentInfo>();
                    }

                    goComponents[comp.GameObjectFileId].Add(comp);
                }

                var buttonFileIdsToRemove = new List<string>();
                var fileIdReplacements = new Dictionary<string, string>();
                foreach (var kvp in goComponents)
                {
                    var btn = kvp.Value.FirstOrDefault(c => c.ScriptGuid == MornMigrationUtil.UnityButtonGuid);
                    var mrn = kvp.Value.FirstOrDefault(c => c.ScriptGuid == MornMigrationUtil.MornUGUIButtonGuid);
                    if (string.IsNullOrEmpty(btn.FileId) || string.IsNullOrEmpty(mrn.FileId))
                    {
                        continue;
                    }

                    fileIdReplacements[btn.FileId] = mrn.FileId;
                    buttonFileIdsToRemove.Add(btn.FileId);
                    Debug.Log($"[Morn Migration] {assetPath}: Button({btn.FileId}) → MornUGUIButton({mrn.FileId}) 統合");
                }

                if (buttonFileIdsToRemove.Count == 0)
                {
                    return true;
                }

                // m_Component エントリを削除
                foreach (var fileId in buttonFileIdsToRemove)
                {
                    for (var i = lines.Count - 1; i >= 0; i--)
                    {
                        if (lines[i].TrimStart().StartsWith("- component:") && lines[i].Contains($"fileID: {fileId}"))
                        {
                            lines.RemoveAt(i);
                            break;
                        }
                    }
                }

                content = string.Join('\n', lines);
                foreach (var kvp in fileIdReplacements)
                {
                    content = content.Replace($"fileID: {kvp.Key}", $"fileID: {kvp.Value}");
                }

                lines = content.Split('\n').ToList();
                foreach (var fileId in buttonFileIdsToRemove)
                {
                    RemoveComponentBlock(lines, fileId);
                }

                content = string.Join('\n', lines);
                if (!MornMigrationUtil.SafeWrite(assetPath, content))
                {
                    return false;
                }

                Debug.Log($"[Morn Migration] {assetPath}: Button 統合完了");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {assetPath} の統合に失敗: {e.Message}");
                return false;
            }
        }
    }
}
#endif
