#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// UnityEngine.UI.Button の stripped 孤立参照 (入れ子 prefab のButton→MornUGUIButton統合で取り残されたもの)を、
    /// PrefabUtility ベースで MornUGUIButton に付け替え。
    /// YAML を事前解析して orphan fileID → m_PrefabInstance マップを構築し、
    /// runtime の GlobalObjectId.targetPrefabId と対応付けて正しい Button を選択する。
    /// </summary>
    internal sealed class StrippedOrphanMigrationStep : MornMigrationStep
    {
        public override string Title => $"Button stripped 孤立参照 ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.6f, 0.8f);

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            var orphans = ExtractOrphans(file.Content);
            if (orphans.Count == 0)
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = $"{orphans.Count}件の孤立参照",
            });
        }

        public override bool FixOne(MornMigrationResult result)
        {
            return FixSingle(result.AssetPath);
        }

        // ========== YAML parsing ==========

        private static List<(ulong fileId, ulong prefInst)> ExtractOrphans(string content)
        {
            var result = new List<(ulong, ulong)>();
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!MornMigrationUtil.TryParseBlockFileId(lines[i], out var fidStr, out var stripped) || !stripped)
                {
                    continue;
                }

                if (!ulong.TryParse(fidStr, out var strippedFid))
                {
                    continue;
                }

                var scriptGuid = "";
                ulong prefInst = 0;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].StartsWith("--- "))
                    {
                        break;
                    }

                    var trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("m_PrefabInstance:") && MornMigrationUtil.TryParseFileId(trimmed, out var pStr))
                    {
                        ulong.TryParse(pStr, out prefInst);
                    }

                    if (trimmed.StartsWith("m_Script:") && MornMigrationUtil.TryParseGuid(trimmed, out var g))
                    {
                        scriptGuid = g;
                    }
                }

                if (scriptGuid == MornMigrationUtil.UnityButtonGuid && prefInst != 0)
                {
                    result.Add((strippedFid, prefInst));
                }
            }

            return result;
        }

        /// <summary>MonoBehaviour の fileID → `_target: {fileID: X}` で X が orphan のリスト (YAML 順)。</summary>
        private static Dictionary<ulong, List<ulong>> ParseTargetReferences(string content, HashSet<ulong> orphanSet)
        {
            var result = new Dictionary<ulong, List<ulong>>();
            var lines = content.Split('\n');
            ulong currentMbFileId = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("--- !u!114 &"))
                {
                    if (line.Contains("stripped"))
                    {
                        currentMbFileId = 0;
                    }
                    else if (MornMigrationUtil.TryParseBlockFileId(line, out var fid, out _))
                    {
                        ulong.TryParse(fid, out currentMbFileId);
                    }

                    continue;
                }

                if (line.StartsWith("--- "))
                {
                    currentMbFileId = 0;
                    continue;
                }

                if (currentMbFileId == 0)
                {
                    continue;
                }

                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("_target:"))
                {
                    continue;
                }

                if (!MornMigrationUtil.TryParseFileId(trimmed, out var targetStr))
                {
                    continue;
                }

                if (!ulong.TryParse(targetStr, out var targetFid) || !orphanSet.Contains(targetFid))
                {
                    continue;
                }

                if (!result.TryGetValue(currentMbFileId, out var list))
                {
                    list = new List<ulong>();
                    result[currentMbFileId] = list;
                }

                list.Add(targetFid);
            }

            return result;
        }

        // ========== Fix implementation ==========

        private static bool FixSingle(string assetPath)
        {
            if (!assetPath.EndsWith(".prefab"))
            {
                Debug.LogWarning($"[Morn Migration] {assetPath}: scene は未対応");
                return false;
            }

            var yamlContent = MornMigrationUtil.SafeRead(assetPath);
            if (yamlContent == null)
            {
                return false;
            }

            var orphans = ExtractOrphans(yamlContent);
            if (orphans.Count == 0)
            {
                return true;
            }

            var orphanToPrefab = new Dictionary<ulong, ulong>();
            foreach (var o in orphans)
            {
                orphanToPrefab[o.fileId] = o.prefInst;
            }

            var perMb = ParseTargetReferences(yamlContent, new HashSet<ulong>(orphanToPrefab.Keys));

            var root = PrefabUtility.LoadPrefabContents(assetPath);
            if (root == null)
            {
                Debug.LogError($"[Morn Migration] {assetPath}: ロード失敗");
                return false;
            }

            try
            {
                // MornUGUIButton を全収集
                var allButtons = new List<MonoBehaviour>();
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null)
                    {
                        continue;
                    }

                    var script = MonoScript.FromMonoBehaviour(mb);
                    if (script == null)
                    {
                        continue;
                    }

                    var scriptPath = AssetDatabase.GetAssetPath(script);
                    if (string.IsNullOrEmpty(scriptPath))
                    {
                        continue;
                    }

                    if (AssetDatabase.AssetPathToGUID(scriptPath) == MornMigrationUtil.MornUGUIButtonGuid)
                    {
                        allButtons.Add(mb);
                    }
                }

                // outermost PrefabInstance root の targetPrefabId → MornUGUIButton
                var prefabInstanceToButton = new Dictionary<ulong, MonoBehaviour>();
                foreach (var btn in allButtons)
                {
                    var outer = btn.gameObject;
                    while (outer != null && !PrefabUtility.IsOutermostPrefabInstanceRoot(outer))
                    {
                        var parent = outer.transform.parent;
                        outer = parent != null ? parent.gameObject : null;
                    }

                    if (outer == null)
                    {
                        continue;
                    }

                    var gid = GlobalObjectId.GetGlobalObjectIdSlow(outer);
                    if (gid.targetPrefabId != 0 && !prefabInstanceToButton.ContainsKey(gid.targetPrefabId))
                    {
                        prefabInstanceToButton[gid.targetPrefabId] = btn;
                    }
                }

                var fixedCount = 0;
                var skippedCount = 0;

                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null)
                    {
                        continue;
                    }

                    var mbGid = GlobalObjectId.GetGlobalObjectIdSlow(mb);
                    if (!perMb.TryGetValue(mbGid.targetObjectId, out var yamlOrphans))
                    {
                        continue;
                    }

                    var so = new SerializedObject(mb);
                    var iter = so.GetIterator();
                    var missingIdx = 0;
                    var changed = false;
                    while (iter.NextVisible(true))
                    {
                        if (iter.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }

                        if (!iter.propertyPath.EndsWith("_autoFocusModule._target") &&
                            !iter.propertyPath.EndsWith("autoFocusModule._target") &&
                            !iter.propertyPath.EndsWith("_cancelModule._target") &&
                            !iter.propertyPath.EndsWith("cancelModule._target"))
                        {
                            continue;
                        }

                        if (iter.objectReferenceValue != null)
                        {
                            continue;
                        }

                        if (iter.objectReferenceInstanceIDValue == 0)
                        {
                            continue;
                        }

                        MonoBehaviour target = null;
                        var diagnostic = "";
                        if (missingIdx < yamlOrphans.Count)
                        {
                            var orphanFid = yamlOrphans[missingIdx];
                            if (orphanToPrefab.TryGetValue(orphanFid, out var prefInstId) &&
                                prefabInstanceToButton.TryGetValue(prefInstId, out var matched))
                            {
                                target = matched;
                                diagnostic = $"orphan={orphanFid} prefInst={prefInstId}";
                            }
                            else
                            {
                                diagnostic = $"orphan={orphanFid} prefInst={prefInstId} → 対応Buttonなし";
                            }
                        }
                        else
                        {
                            diagnostic = $"YAMLリスト({yamlOrphans.Count})とズレ idx={missingIdx}";
                        }

                        if (target != null)
                        {
                            iter.objectReferenceValue = target;
                            changed = true;
                            fixedCount++;
                        }
                        else
                        {
                            skippedCount++;
                            Debug.LogWarning($"[Morn Migration] {assetPath}: MB={mbGid.targetObjectId} {iter.propertyPath}: {diagnostic}");
                        }

                        missingIdx++;
                    }

                    if (changed)
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                if (fixedCount > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                    Debug.Log($"[Morn Migration] {assetPath}: {fixedCount}件修正、{skippedCount}件スキップ");
                }
                else if (skippedCount > 0)
                {
                    Debug.LogWarning($"[Morn Migration] {assetPath}: {skippedCount}件の missing を自動修正できず");
                }

                return skippedCount == 0;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {assetPath} 修正失敗: {e.Message}");
                return false;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
