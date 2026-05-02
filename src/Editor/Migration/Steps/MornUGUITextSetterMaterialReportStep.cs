#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornUGUITextSetter (3e5221d3) の _materialType._key が空のインスタンスを検出してレポートする。
    /// 旧 prefab で MaterialType._key が serialize されていなかったケース、 または Migration の引き継ぎ漏れを発見するための診断ステップ。
    /// </summary>
    internal sealed class MornUGUITextSetterMaterialReportStep : MornMigrationStep
    {
        public override string Title => $"MornUGUITextSetter._materialType._key 未設定 ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.6f, 0.6f);
        public override bool ReadOnly => true;

        private const string TextSetterGuid = "3e5221d3bb1849d38d28458571bb2548";

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            var blocks = SplitBlocks(file.Content);
            var emptyAnchors = new List<string>();
            foreach (var block in blocks)
            {
                if (!block.Contains($"guid: {TextSetterGuid}"))
                {
                    continue;
                }

                // 直接配置 (stripped でない) または stripped 関わらず、
                // フィールドとして "  _materialType:\n    _key: <value>" を持つかチェック。
                var match = Regex.Match(block, @"  _materialType:\r?\n    _key:\s*([^\r\n]*)");
                if (!match.Success)
                {
                    continue;
                }

                var keyValue = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(keyValue))
                {
                    continue;
                }

                var anchorMatch = Regex.Match(block, @"--- !u!\d+ &(\d+)");
                emptyAnchors.Add(anchorMatch.Success ? anchorMatch.Groups[1].Value : "?");
            }

            if (emptyAnchors.Count == 0)
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = string.Join(", ", emptyAnchors),
            });
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
