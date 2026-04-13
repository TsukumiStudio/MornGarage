#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// C# ソースの namespace/型名一括置換。
    /// using MornLib; の重複は除去する。
    /// </summary>
    internal sealed class CsReplaceMigrationStep : MornMigrationStep
    {
        public override string Title => $"C# namespace/型名変更 ({Results.Count}件)";
        public override Color HeaderColor => new(0.6f, 0.8f, 1f);
        public override bool ScansAssets => false;
        public override bool ScansCs => true;

        public static readonly (string oldText, string newText)[] Replacements =
        {
            ("using MornUGUI;", "using MornLib;"),
            ("MornUGUICtrl", "MornUGUIService"),
        };

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (!file.IsCs)
            {
                return;
            }

            var details = new List<string>();
            foreach (var (oldText, newText) in Replacements)
            {
                if (file.Content.Contains(oldText))
                {
                    details.Add($"{oldText} → {newText}");
                }
            }

            if (details.Count == 0)
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = string.Join("\n", details),
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
            foreach (var (oldText, newText) in Replacements)
            {
                if (content.Contains(oldText))
                {
                    content = content.Replace(oldText, newText);
                    modified = true;
                }
            }

            if (!modified)
            {
                return true;
            }

            // using MornLib; の重複除去
            var lines = content.Split('\n').ToList();
            var seenMornLib = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim() != "using MornLib;")
                {
                    continue;
                }

                if (seenMornLib)
                {
                    lines.RemoveAt(i);
                    i--;
                }
                else
                {
                    seenMornLib = true;
                }
            }

            content = string.Join('\n', lines);
            if (!MornMigrationUtil.SafeWrite(result.AssetPath, content))
            {
                return false;
            }

            Debug.Log($"[Morn Migration] {result.AssetPath}: C# 置換完了");
            return true;
        }
    }
}
#endif
