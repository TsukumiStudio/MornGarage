#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornBeat 系のフィールド名移行。
    /// - BeatPlayState._beatMemo → _music
    /// - MornNovelStartCommand._beatMemo → _beatMusic
    /// MonoBehaviour ブロックは m_Script の Script GUID で分岐し、 PrefabInstance ブロックは
    /// modification の target anchor から SourcePrefab を経由で Script GUID を逆引きして個別に rename する。
    /// </summary>
    internal sealed class MornBeatMigrationStep : MornMigrationStep
    {
        public override string Title => $"MornBeat フィールド名移行 ({Results.Count}件)";
        public override Color HeaderColor => new(0.6f, 1f, 0.6f);

        private const string BeatPlayStateGuid = "9617cf301b04ecd47bb46e35d2acc61e";
        private const string MornNovelStartCommandGuid = "e5a59e1bceb94fe4ca21f8ffca1e0678";

        /// <summary>各 Script ごとのフィールド名リネーム (旧 → 新)</summary>
        private static readonly Dictionary<string, (string Old, string New)[]> FieldRenames = new()
        {
            { BeatPlayStateGuid, new[] { ("_beatMemo", "_music") } },
            { MornNovelStartCommandGuid, new[] { ("_beatMemo", "_beatMusic") } },
        };

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            var hits = new List<string>();

            // (1) MonoBehaviour ブロックで対象 Script GUID + 旧フィールド名が残っていれば対象
            var blocks = SplitBlocks(file.Content);
            foreach (var kvp in FieldRenames)
            {
                foreach (var block in blocks)
                {
                    if (!ContainsScriptGuid(block, kvp.Key))
                    {
                        continue;
                    }

                    foreach (var (oldName, newName) in kvp.Value)
                    {
                        if (BlockContainsField(block, oldName))
                        {
                            hits.Add($"{oldName} → {newName}");
                            break;
                        }
                    }
                }
            }

            // (2) PrefabInstance ブロックで target=対象 Script の anchor で旧フィールド名が残っていれば対象
            foreach (var block in blocks)
            {
                if (!IsPrefabInstanceBlock(block))
                {
                    continue;
                }

                var sourcePrefabGuid = ExtractSourcePrefabGuid(block);
                if (sourcePrefabGuid == null)
                {
                    continue;
                }

                var anchorToScriptGuid = ResolveAnchorToScriptGuid(sourcePrefabGuid);
                foreach (var kvp in FieldRenames)
                {
                    foreach (var (anchor, scriptGuid) in anchorToScriptGuid)
                    {
                        if (scriptGuid != kvp.Key)
                        {
                            continue;
                        }

                        foreach (var (oldName, newName) in kvp.Value)
                        {
                            if (BlockHasOverride(block, anchor, oldName))
                            {
                                hits.Add($"{oldName} → {newName}");
                                break;
                            }
                        }
                    }
                }
            }

            if (hits.Count == 0)
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = string.Join(", ", new HashSet<string>(hits)),
            });
        }

        public override bool FixOne(MornMigrationResult result)
        {
            var content = MornMigrationUtil.SafeRead(result.AssetPath);
            if (content == null)
            {
                return false;
            }

            var modified = false;
            var blocks = SplitBlocks(content);
            for (var i = 0; i < blocks.Length; i++)
            {
                var rewritten = blocks[i];

                // (1) MonoBehaviour ブロックで Script GUID 一致の場合のみフィールド名置換
                foreach (var kvp in FieldRenames)
                {
                    if (!ContainsScriptGuid(rewritten, kvp.Key))
                    {
                        continue;
                    }

                    foreach (var (oldName, newName) in kvp.Value)
                    {
                        rewritten = ReplaceFieldName(rewritten, oldName, newName);
                    }
                }

                // (2) PrefabInstance ブロックで target anchor → Script GUID を解決して個別置換
                if (IsPrefabInstanceBlock(rewritten))
                {
                    var sourcePrefabGuid = ExtractSourcePrefabGuid(rewritten);
                    if (sourcePrefabGuid != null)
                    {
                        var anchorToScriptGuid = ResolveAnchorToScriptGuid(sourcePrefabGuid);
                        foreach (var (anchor, scriptGuid) in anchorToScriptGuid)
                        {
                            if (!FieldRenames.TryGetValue(scriptGuid, out var renames))
                            {
                                continue;
                            }

                            foreach (var (oldName, newName) in renames)
                            {
                                rewritten = ReplacePropertyPathForAnchor(rewritten, anchor, oldName, newName);
                            }
                        }
                    }
                }

                if (rewritten != blocks[i])
                {
                    blocks[i] = rewritten;
                    modified = true;
                }
            }

            if (modified)
            {
                content = string.Join("\n", blocks);
                if (!MornMigrationUtil.SafeWrite(result.AssetPath, content))
                {
                    return false;
                }

                Debug.Log($"[Morn Migration] {result.AssetPath}: MornBeat フィールド名移行完了");
            }

            return true;
        }

        /// <summary>指定 prefab guid の YAML を読み、 anchor → m_Script の guid マップを構築。</summary>
        private static Dictionary<string, string> ResolveAnchorToScriptGuid(string prefabGuid)
        {
            var result = new Dictionary<string, string>();
            var path = AssetDatabase.GUIDToAssetPath(prefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                return result;
            }

            var content = MornMigrationUtil.SafeRead(path);
            if (content == null)
            {
                return result;
            }

            string currentAnchor = null;
            foreach (var line in content.Split('\n'))
            {
                if (line.StartsWith("--- "))
                {
                    var anchorMatch = Regex.Match(line, @"&(\d+)");
                    currentAnchor = anchorMatch.Success ? anchorMatch.Groups[1].Value : null;
                    continue;
                }

                if (currentAnchor == null)
                {
                    continue;
                }

                var guidMatch = Regex.Match(line, @"m_Script: \{fileID: \d+, guid: ([0-9a-f]+),");
                if (guidMatch.Success)
                {
                    result[currentAnchor] = guidMatch.Groups[1].Value;
                    currentAnchor = null;
                }
            }

            return result;
        }

        private static string ExtractSourcePrefabGuid(string block)
        {
            var match = Regex.Match(block, @"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]+),");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static bool BlockHasOverride(string block, string anchor, string propertyName)
        {
            var pattern = @"target: \{fileID: " + Regex.Escape(anchor)
                                                + @"[^}]+\}\r?\n      propertyPath: "
                                                + Regex.Escape(propertyName) + @"(\.|\r?\n)";
            return Regex.IsMatch(block, pattern);
        }

        private static string ReplacePropertyPathForAnchor(string block, string anchor, string oldName, string newName)
        {
            var pattern = @"(    - target: \{fileID: " + Regex.Escape(anchor)
                                                       + @", guid: [^}]+\}\r?\n      propertyPath: )"
                                                       + Regex.Escape(oldName)
                                                       + @"((?:\.[^\r\n]+)?\r?\n)";
            return Regex.Replace(block, pattern, m => m.Groups[1].Value + newName + m.Groups[2].Value);
        }

        private static bool ContainsScriptGuid(string block, string guid)
        {
            return block.Contains($"guid: {guid}");
        }

        private static bool BlockContainsField(string block, string fieldName)
        {
            return block.Contains($"\n  {fieldName}:");
        }

        private static bool IsPrefabInstanceBlock(string block)
        {
            return block.Contains("PrefabInstance:");
        }

        private static string ReplaceFieldName(string block, string oldName, string newName)
        {
            return block.Replace($"\n  {oldName}:", $"\n  {newName}:");
        }

        private static string[] SplitBlocks(string content)
        {
            var lines = content.Split('\n');
            var blocks = new List<string>();
            var current = new List<string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("--- ") && current.Count > 0)
                {
                    blocks.Add(string.Join("\n", current));
                    current.Clear();
                }

                current.Add(line);
            }

            if (current.Count > 0)
            {
                blocks.Add(string.Join("\n", current));
            }

            return blocks.ToArray();
        }
    }
}
#endif
