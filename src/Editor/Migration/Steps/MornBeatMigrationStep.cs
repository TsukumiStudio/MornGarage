#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornBeat 系のフィールド名移行。
    /// - BeatPlayState._beatMemo → _music (旧 MornBeatMemo → 新 MornBeatMusic)
    /// MonoBehaviour ブロック内のフィールド名と PrefabInstance の propertyPath の両方を置換する。
    /// </summary>
    internal sealed class MornBeatMigrationStep : MornMigrationStep
    {
        public override string Title => $"MornBeat フィールド名移行 ({Results.Count}件)";
        public override Color HeaderColor => new(0.6f, 1f, 0.6f);

        private const string BeatPlayStateGuid = "9617cf301b04ecd47bb46e35d2acc61e";

        /// <summary>BeatPlayState 等のフィールド名リネーム (旧 → 新)</summary>
        private static readonly Dictionary<string, (string Old, string New)[]> FieldRenames = new()
        {
            { BeatPlayStateGuid, new[] { ("_beatMemo", "_music") } },
        };

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            var hits = new List<string>();
            foreach (var kvp in FieldRenames)
            {
                foreach (var (oldName, newName) in kvp.Value)
                {
                    if (ContentContainsField(file.Content, oldName)
                        || ContentContainsPropertyPath(file.Content, oldName))
                    {
                        hits.Add($"{oldName} → {newName}");
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

                foreach (var kvp in FieldRenames)
                {
                    if (ContainsScriptGuid(rewritten, kvp.Key))
                    {
                        foreach (var (oldName, newName) in kvp.Value)
                        {
                            rewritten = ReplaceFieldName(rewritten, oldName, newName);
                        }
                    }
                }

                if (IsPrefabInstanceBlock(rewritten))
                {
                    foreach (var kvp in FieldRenames)
                    {
                        foreach (var (oldName, newName) in kvp.Value)
                        {
                            rewritten = ReplacePropertyPath(rewritten, oldName, newName);
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

        private static bool ContentContainsField(string content, string fieldName)
        {
            return content.Contains($"\n  {fieldName}:");
        }

        private static bool ContentContainsPropertyPath(string content, string fieldName)
        {
            return content.Contains($"propertyPath: {fieldName}\n") || content.Contains($"propertyPath: {fieldName}.");
        }

        private static bool ContainsScriptGuid(string block, string guid)
        {
            return block.Contains($"guid: {guid}");
        }

        private static bool IsPrefabInstanceBlock(string block)
        {
            return block.Contains("PrefabInstance:");
        }

        private static string ReplaceFieldName(string block, string oldName, string newName)
        {
            return block.Replace($"\n  {oldName}:", $"\n  {newName}:");
        }

        private static string ReplacePropertyPath(string block, string oldName, string newName)
        {
            block = block.Replace($"propertyPath: {oldName}\n", $"propertyPath: {newName}\n");
            block = block.Replace($"propertyPath: {oldName}.", $"propertyPath: {newName}.");
            return block;
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
