#if UNITY_EDITOR
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// ObsoleteSubState → SubState 移行後のフィールド不整合修正。
    /// _instantiate:0 かつ _prefab に値ありの時、_prefab → _instance にリネーム。
    /// </summary>
    internal sealed class SubStateFieldMigrationStep : MornMigrationStep
    {
        public override string Title => $"SubState フィールド不整合 ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.7f, 0.3f);

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            if (!file.Content.Contains(MornMigrationUtil.SubStateGuid))
            {
                return;
            }

            if (!HasMismatch(file.Content))
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = "_prefab → _instance リネーム必要",
            });
        }

        public override bool FixOne(MornMigrationResult result)
        {
            var content = MornMigrationUtil.SafeRead(result.AssetPath);
            if (content == null)
            {
                return false;
            }

            var newContent = ApplyFix(content);
            if (ReferenceEquals(newContent, content))
            {
                return true;
            }

            if (!MornMigrationUtil.SafeWrite(result.AssetPath, newContent))
            {
                return false;
            }

            Debug.Log($"[Morn Migration] {result.AssetPath}: SubState _prefab → _instance 完了");
            return true;
        }

        private static bool HasMismatch(string content)
        {
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains($"guid: {MornMigrationUtil.SubStateGuid}"))
                {
                    continue;
                }

                var instantiateValue = -1;
                var hasPrefabRef = false;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("--- !u!"))
                    {
                        break;
                    }

                    if (trimmed.StartsWith("_instantiate:"))
                    {
                        instantiateValue = trimmed.Contains("1") ? 1 : 0;
                    }

                    if (trimmed.StartsWith("_prefab:") && !trimmed.Contains("{fileID: 0}"))
                    {
                        hasPrefabRef = true;
                    }
                }

                if (instantiateValue == 0 && hasPrefabRef)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>_instantiate:0 + _prefab に値あり → _prefab: を _instance: にリネーム。</summary>
        private static string ApplyFix(string content)
        {
            var lines = content.Split('\n');
            var modified = false;
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains($"guid: {MornMigrationUtil.SubStateGuid}"))
                {
                    continue;
                }

                var instantiateValue = -1;
                var prefabLineIdx = -1;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("--- !u!"))
                    {
                        break;
                    }

                    if (trimmed.StartsWith("_instantiate:"))
                    {
                        instantiateValue = trimmed.Contains("1") ? 1 : 0;
                    }

                    if (trimmed.StartsWith("_prefab:") && !trimmed.Contains("{fileID: 0}"))
                    {
                        prefabLineIdx = j;
                    }
                }

                if (instantiateValue == 0 && prefabLineIdx >= 0)
                {
                    lines[prefabLineIdx] = lines[prefabLineIdx].Replace("_prefab:", "_instance:");
                    modified = true;
                }
            }

            return modified ? string.Join('\n', lines) : content;
        }
    }
}
#endif
