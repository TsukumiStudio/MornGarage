#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// 指定 Script GUID を持つ MonoBehaviour ブロック内の YAML フィールド名を置換する migration。
    /// 例: Old (public Text) → 新 (private _text) のリネームに伴って prefab/scene の値を引き継ぐ。
    /// </summary>
    internal sealed class FieldRenameMigrationStep : MornMigrationStep
    {
        public override string Title => $"フィールド名リネーム ({Results.Count}件)";
        public override Color HeaderColor => new(0.6f, 1f, 0.6f);

        /// <summary>Script GUID → リネーム規則。</summary>
        public static readonly Dictionary<string, FieldRenameEntry> RenameTable = new()
        {
            // MornUGUITextSetter (Old は public 大文字 → 新は private アンダースコア)
            {
                "3e5221d3bb1849d38d28458571bb2548",
                new FieldRenameEntry(
                    "MornUGUITextSetter",
                    new[]
                    {
                        ("Text", "_text"),
                        ("SizeSettings", "_sizeSettings"),
                        ("FontSettings", "_fontSettings"),
                        ("MaterialType", "_materialType"),
                    })
            },
        };

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            foreach (var kvp in RenameTable)
            {
                var entry = kvp.Value;
                if (!FindHits(file.Content, kvp.Key, entry, out var hitNames))
                {
                    continue;
                }

                Results.Add(new MornMigrationResult
                {
                    AssetPath = file.AssetPath,
                    Details = $"{entry.ClassName}: {string.Join(", ", hitNames)}",
                    Payload = kvp.Key,
                });
            }
        }

        public override bool FixOne(MornMigrationResult result)
        {
            var content = MornMigrationUtil.SafeRead(result.AssetPath);
            if (content == null)
            {
                return false;
            }

            var modified = false;
            foreach (var kvp in RenameTable)
            {
                if (Replace(ref content, kvp.Key, kvp.Value))
                {
                    modified = true;
                }
            }

            if (!modified)
            {
                return true;
            }

            if (!MornMigrationUtil.SafeWrite(result.AssetPath, content))
            {
                return false;
            }

            Debug.Log($"[Morn Migration] {result.AssetPath}: フィールド名リネーム完了");
            return true;
        }

        /// <summary>YAML を MonoBehaviour ブロック単位で走査し、該当 GUID のブロックに古フィールドが残っていれば名前を返す。</summary>
        private static bool FindHits(string content, string scriptGuid, FieldRenameEntry entry, out List<string> hitNames)
        {
            hitNames = new List<string>();
            var blocks = SplitBlocks(content);
            foreach (var block in blocks)
            {
                if (!ContainsScriptGuid(block, scriptGuid))
                {
                    continue;
                }

                foreach (var (oldName, _) in entry.Renames)
                {
                    if (BlockContainsField(block, oldName) && !hitNames.Contains(oldName))
                    {
                        hitNames.Add(oldName);
                    }
                }
            }

            return hitNames.Count > 0;
        }

        private static bool Replace(ref string content, string scriptGuid, FieldRenameEntry entry)
        {
            var blocks = SplitBlocks(content);
            var modified = false;
            for (var i = 0; i < blocks.Length; i++)
            {
                if (!ContainsScriptGuid(blocks[i], scriptGuid))
                {
                    continue;
                }

                var rewritten = blocks[i];
                foreach (var (oldName, newName) in entry.Renames)
                {
                    rewritten = ReplaceFieldName(rewritten, oldName, newName);
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
            }

            return modified;
        }

        /// <summary>YAML を `---` 区切りでブロック分割。</summary>
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

        private static bool ContainsScriptGuid(string block, string guid)
        {
            return block.Contains($"guid: {guid}");
        }

        private static bool BlockContainsField(string block, string fieldName)
        {
            return block.Contains($"\n  {fieldName}:");
        }

        /// <summary>YAML の `  oldName:` を `  newName:` に置換 (インデント 2 のキー行のみ対象)。</summary>
        private static string ReplaceFieldName(string block, string oldName, string newName)
        {
            return block.Replace($"\n  {oldName}:", $"\n  {newName}:");
        }

        internal sealed class FieldRenameEntry
        {
            public string ClassName { get; }
            public (string Old, string New)[] Renames { get; }

            public FieldRenameEntry(string className, (string, string)[] renames)
            {
                ClassName = className;
                Renames = renames;
            }
        }
    }
}
#endif
