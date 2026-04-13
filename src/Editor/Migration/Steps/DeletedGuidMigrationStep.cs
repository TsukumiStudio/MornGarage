#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace MornLib
{
    /// <summary>対応先がなく Missing 状態になる旧 GUID の検出のみ (修正不可)。</summary>
    internal sealed class DeletedGuidMigrationStep : MornMigrationStep
    {
        public override string Title => $"Missing — 対応先なし ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.5f, 0.5f);
        public override bool ReadOnly => true;

        public static readonly Dictionary<string, string> DeletedGuids = new()
        {
            { "e830bade52d747819e91153be1b80223", "MornUGUIButtonModuleBase (削除)" },
            { "e31b2f6049aa48499baee31c0cea8064", "MornUGUISliderModuleBase (削除)" },
        };

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            foreach (var kvp in DeletedGuids)
            {
                if (file.Content.Contains(kvp.Key))
                {
                    Results.Add(new MornMigrationResult
                    {
                        AssetPath = file.AssetPath,
                        Details = $"{kvp.Value} (削除済み)",
                    });
                }
            }
        }
    }
}
#endif
