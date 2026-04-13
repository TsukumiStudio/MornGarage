#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornUGUIControlState 系のフィールドリネーム + 廃止フラグ削除。
    /// _buttonModule → _linkModule, _buttonStateLinkSets → _stateLinkSets,
    /// _focusModule → _autoFocusModule, _autoFocusTarget → _target,
    /// _cancelTarget → _target, LinkSet 内 Button → Target,
    /// _autoFocusModule/_cancelModule の _isActive 削除。
    /// </summary>
    internal sealed class ControlStateFieldMigrationStep : MornMigrationStep
    {
        public override string Title => $"ControlState フィールド不整合 ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.7f, 0.3f);

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            var c = file.Content;
            if (c.Contains("_buttonStateLinkSets") ||
                c.Contains("_buttonModule:") ||
                c.Contains("_focusModule:") ||
                c.Contains("_autoFocusTarget:") ||
                c.Contains("_cancelTarget:") ||
                HasIsActiveInAutoFocusOrCancelModule(c))
            {
                Results.Add(new MornMigrationResult
                {
                    AssetPath = file.AssetPath,
                    Details = "フィールドリネーム / _isActive 削除",
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
            content = ApplyFieldRenames(content, ref modified);
            var removedCount = RemoveIsActiveFromModules(ref content);
            if (removedCount > 0)
            {
                modified = true;
                Debug.Log($"[Morn Migration] {result.AssetPath}: _isActive {removedCount}件削除");
            }

            if (!modified)
            {
                return true;
            }

            if (!MornMigrationUtil.SafeWrite(result.AssetPath, content))
            {
                return false;
            }

            Debug.Log($"[Morn Migration] {result.AssetPath}: フィールド修正完了");
            return true;
        }

        private static string ApplyFieldRenames(string content, ref bool modified)
        {
            var renames = new (string from, string to)[]
            {
                ("_buttonModule:", "_linkModule:"),
                ("_buttonStateLinkSets", "_stateLinkSets"),
                ("_focusModule:", "_autoFocusModule:"),
                ("_autoFocusTarget:", "_target:"),
                ("_cancelTarget:", "_target:"),
            };

            foreach (var (from, to) in renames)
            {
                if (content.Contains(from))
                {
                    content = content.Replace(from, to);
                    modified = true;
                }
            }

            // LinkSet 内 "Button: {fileID:...}" → "Target: {fileID:...}"
            var lines = content.Split('\n');
            var linesModified = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if ((trimmed.StartsWith("Button:") || trimmed.StartsWith("- Button:")) && trimmed.Contains("{fileID:"))
                {
                    lines[i] = lines[i].Replace("Button:", "Target:");
                    linesModified = true;
                }
            }

            if (linesModified)
            {
                modified = true;
                content = string.Join('\n', lines);
            }

            return content;
        }

        public static bool HasIsActiveInAutoFocusOrCancelModule(string content)
        {
            if (content.Contains("_autoFocusModule._isActive") || content.Contains("_cancelModule._isActive"))
            {
                return true;
            }

            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed != "_autoFocusModule:" && trimmed != "_cancelModule:")
                {
                    continue;
                }

                var moduleIndent = MornMigrationUtil.IndentOf(lines[i]);
                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (string.IsNullOrWhiteSpace(lines[j]))
                    {
                        continue;
                    }

                    var childIndent = MornMigrationUtil.IndentOf(lines[j]);
                    if (childIndent <= moduleIndent)
                    {
                        break;
                    }

                    if (lines[j].TrimStart().StartsWith("_isActive:"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int RemoveIsActiveFromModules(ref string content)
        {
            var lines = content.Split('\n').ToList();
            var removed = 0;
            var targetModules = new[] { "_autoFocusModule:", "_cancelModule:" };

            // Module ブロック内の _isActive 行を削除
            for (var i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].TrimStart();
                var isModuleStart = targetModules.Any(m => trimmed == m || trimmed.StartsWith(m));
                if (!isModuleStart)
                {
                    continue;
                }

                var moduleIndent = MornMigrationUtil.IndentOf(lines[i]);
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (string.IsNullOrWhiteSpace(lines[j]))
                    {
                        continue;
                    }

                    var childIndent = MornMigrationUtil.IndentOf(lines[j]);
                    if (childIndent <= moduleIndent)
                    {
                        break;
                    }

                    if (lines[j].TrimStart().StartsWith("_isActive:"))
                    {
                        lines.RemoveAt(j);
                        removed++;
                        j--;
                    }
                }
            }

            // PrefabInstance modifications から _autoFocusModule._isActive / _cancelModule._isActive 削除
            for (var i = 0; i < lines.Count; i++)
            {
                if (!lines[i].TrimStart().StartsWith("- target:"))
                {
                    continue;
                }

                if (i + 1 >= lines.Count)
                {
                    continue;
                }

                var pathLine = lines[i + 1].TrimStart();
                if (!pathLine.StartsWith("propertyPath:"))
                {
                    continue;
                }

                var isMatch = pathLine.Contains("_autoFocusModule._isActive") ||
                              pathLine.Contains(".autoFocusModule._isActive") ||
                              pathLine.Contains("_cancelModule._isActive") ||
                              pathLine.Contains(".cancelModule._isActive");
                if (!isMatch)
                {
                    continue;
                }

                var endLine = i + 1;
                for (var j = i + 1; j < lines.Count; j++)
                {
                    var t = lines[j].TrimStart();
                    if (j > i && (t.StartsWith("- target:") ||
                                  t.StartsWith("m_RemovedComponents:") ||
                                  t.StartsWith("m_RemovedGameObjects:") ||
                                  t.StartsWith("m_AddedGameObjects:") ||
                                  t.StartsWith("m_AddedComponents:")))
                    {
                        endLine = j - 1;
                        break;
                    }

                    endLine = j;
                }

                lines.RemoveRange(i, endLine - i + 1);
                removed++;
                i--;
            }

            if (removed > 0)
            {
                content = string.Join('\n', lines);
            }

            return removed;
        }
    }
}
#endif
