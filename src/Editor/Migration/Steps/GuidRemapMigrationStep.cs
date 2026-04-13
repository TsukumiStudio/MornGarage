#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MornLib
{
    /// <summary>旧 GUID → 新 GUID の置換を行う migration。</summary>
    internal sealed class GuidRemapMigrationStep : MornMigrationStep
    {
        public override string Title => $"GUID リマップ必要 ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.9f, 0.4f);

        /// <summary>旧 GUID → (新 GUID, 旧名, 新名)。</summary>
        public static readonly Dictionary<string, (string newGuid, string oldName, string newName)> RemapTable = new()
        {
            // MornUGUI Module 統合
            { "f7757884ec8e470cb416ef32dbceecdb", ("3b724e5ba7194030a2a965eb502aa345", "MornUGUIButtonActiveModule", "MornUGUIActiveModule") },
            { "82b0ac599184c42bfa443c85ba2fd7d0", ("242140c1ceb74211adff12464434ed55", "MornUGUIButtonColorModule", "MornUGUIColorModule") },
            { "ddd29d8d393f4344889c51b493e844b6", ("28bc5e2a967d4eb4b43706ca161fd00b", "MornUGUIButtonSoundModule", "MornUGUISoundModule") },
            { "38955c969fb645429f8d790f2f1b9ad5", ("3b724e5ba7194030a2a965eb502aa345", "MornUGUISliderActiveModule", "MornUGUIActiveModule") },
            { "00aa1d2f6f3e44a6b1572bb190a4c622", ("242140c1ceb74211adff12464434ed55", "MornUGUISliderColorModule", "MornUGUIColorModule") },
            { "901d32d7664d4ebab804e9f182575c53", ("26859705ac4d4171afc20025d269f521", "MornUGUISliderConvertPointerToSelectModule", "MornUGUIPointerModule") },
            // MornUGUI Toggle
            { "aa97cf6498d344038ef47fc0da2b161c", ("710b6c07a89ff4b14adde443d111fa7b", "MornUGUIButtonToggleModule", "MornUGUIToggleModule") },
            // MornArbor Obsolete → 新型
            { "ac57e10a4fe04d3499fe699b2eeee5ea", ("aac67bf328824705a55354ac6d26608c", "ObsoleteSubState", "SubState") },
            { "394ac4c5f3df4673862c62fc0563a7fb", ("25ce3e5de7204deabd088884851df9a5", "ObsoletePlayAnimationProcess", "PlayAnimationProcess") },
        };

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            foreach (var kvp in RemapTable)
            {
                if (file.Content.Contains(kvp.Key))
                {
                    Results.Add(new MornMigrationResult
                    {
                        AssetPath = file.AssetPath,
                        Details = $"{kvp.Value.oldName} → {kvp.Value.newName}",
                        Payload = kvp.Key,
                    });
                }
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
            foreach (var kvp in RemapTable)
            {
                if (content.Contains(kvp.Key))
                {
                    content = content.Replace(kvp.Key, kvp.Value.newGuid);
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

            Debug.Log($"[Morn Migration] {result.AssetPath}: GUID リマップ完了");
            return true;
        }
    }
}
#endif
