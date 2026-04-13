#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornUGUIControlState の特定サブモジュール (_autoFocusModule 等) 内の `_target` フィールドに残った
    /// UnityEngine.UI.Button の stripped 孤立参照を、PrefabUtility ベースで MornUGUIButton に付け替える
    /// 移行処理の共通基底クラス。
    /// </summary>
    internal abstract class MissingTargetMigrationStepBase : MornMigrationStep
    {
        /// <summary>監視対象のサブモジュール名 (例: "_autoFocusModule")。</summary>
        protected abstract string ModuleName { get; }

        /// <summary>
        /// サブモジュール内で target を参照する旧フィールド名 (例: "_autoFocusTarget", "_cancelTarget")。
        /// これらの旧名があれば修正時に `_target:` にリネームしてから missing 修正を実行する。
        /// </summary>
        protected virtual string[] LegacyTargetFieldNames => System.Array.Empty<string>();

        private string PropertySuffixUnderscore => $"{ModuleName}._target";
        private string PropertySuffixPlain => $"{ModuleName.TrimStart('_')}._target";

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

            // このモジュール内で orphan を参照しているものだけをカウント (旧フィールド名も含む)
            var perMb = ParseModuleTargetReferences(file.Content, new HashSet<ulong>(orphans.Keys), ModuleName, AllTargetFieldNames());
            var total = 0;
            foreach (var list in perMb.Values)
            {
                total += list.Count;
            }

            if (total == 0)
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = $"{ModuleName}._target missing {total}件",
            });
        }

        public override bool FixOne(MornMigrationResult result)
        {
            return FixSingle(result.AssetPath);
        }

        // ========== YAML parsing ==========

        /// <summary>UnityEngine.UI.Button の stripped エントリ fileID → m_PrefabInstance fileID。</summary>
        private static Dictionary<ulong, ulong> ExtractOrphans(string content)
        {
            var result = new Dictionary<ulong, ulong>();
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
                    result[strippedFid] = prefInst;
                }
            }

            return result;
        }

        /// <summary>
        /// MonoBehaviour の fileID → 対象サブモジュール内にある `_target:` で orphan を指す fileID のリスト (YAML順)。
        /// </summary>
        private string[] AllTargetFieldNames()
        {
            var legacy = LegacyTargetFieldNames;
            var result = new string[legacy.Length + 1];
            result[0] = "_target";
            for (var i = 0; i < legacy.Length; i++)
            {
                result[i + 1] = legacy[i];
            }

            return result;
        }

        private static Dictionary<ulong, List<ulong>> ParseModuleTargetReferences(
            string content,
            HashSet<ulong> orphanSet,
            string moduleName,
            string[] targetFieldNames)
        {
            var result = new Dictionary<ulong, List<ulong>>();
            var lines = content.Split('\n');
            ulong currentMbFileId = 0;
            var currentModuleIndent = -1; // -1 = サブモジュール外

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

                    currentModuleIndent = -1;
                    continue;
                }

                if (line.StartsWith("--- "))
                {
                    currentMbFileId = 0;
                    currentModuleIndent = -1;
                    continue;
                }

                if (currentMbFileId == 0)
                {
                    continue;
                }

                var indent = MornMigrationUtil.IndentOf(line);
                var trimmed = line.TrimStart();

                // サブモジュールヘッダー検出
                if (trimmed == $"{moduleName}:" || trimmed.StartsWith($"{moduleName}:"))
                {
                    currentModuleIndent = indent;
                    continue;
                }

                // サブモジュールから出たかチェック
                if (currentModuleIndent >= 0 && !string.IsNullOrWhiteSpace(line) && indent <= currentModuleIndent)
                {
                    currentModuleIndent = -1;
                }

                if (currentModuleIndent < 0)
                {
                    continue;
                }

                // サブモジュール内の targetFieldNames のいずれかで始まる行のみ対象
                var matchField = false;
                foreach (var fn in targetFieldNames)
                {
                    if (trimmed.StartsWith(fn + ":"))
                    {
                        matchField = true;
                        break;
                    }
                }

                if (!matchField)
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

        // ========== Fix ==========

        private bool FixSingle(string assetPath)
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

            // 旧フィールド名 → _target の事前リネーム (サブモジュール内スコープ)
            var renamed = RenameLegacyFieldsInModule(ref yamlContent, ModuleName, LegacyTargetFieldNames);
            if (renamed > 0)
            {
                if (!MornMigrationUtil.SafeWrite(assetPath, yamlContent))
                {
                    return false;
                }

                Debug.Log($"[Morn Migration] {assetPath}: {ModuleName} 旧フィールド名 → _target を {renamed}件リネーム");
                AssetDatabase.ImportAsset(assetPath);
            }

            var orphans = ExtractOrphans(yamlContent);
            if (orphans.Count == 0)
            {
                return true;
            }

            var perMb = ParseModuleTargetReferences(yamlContent, new HashSet<ulong>(orphans.Keys), ModuleName, AllTargetFieldNames());
            if (perMb.Count == 0)
            {
                return true;
            }

            var root = PrefabUtility.LoadPrefabContents(assetPath);
            if (root == null)
            {
                Debug.LogError($"[Morn Migration] {assetPath}: ロード失敗");
                return false;
            }

            try
            {
                // runtime の MornUGUIButton を targetPrefabId で索引
                var prefabInstanceToButton = BuildButtonMap(root);

                var fixedCount = 0;
                var skippedCount = 0;
                var pathSuffixU = PropertySuffixUnderscore;
                var pathSuffixP = PropertySuffixPlain;

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

                        if (!iter.propertyPath.EndsWith(pathSuffixU) && !iter.propertyPath.EndsWith(pathSuffixP))
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
                            if (orphans.TryGetValue(orphanFid, out var prefInstId) &&
                                prefabInstanceToButton.TryGetValue(prefInstId, out var matched))
                            {
                                target = matched;
                                diagnostic = $"orphan={orphanFid} prefInst={prefInstId}";
                            }
                            else
                            {
                                diagnostic = $"orphan={orphanFid} prefInst={prefInstId} 対応Buttonなし";
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
                    Debug.Log($"[Morn Migration] {assetPath}: {ModuleName} {fixedCount}件修正、{skippedCount}件スキップ");
                }
                else if (skippedCount > 0)
                {
                    Debug.LogWarning($"[Morn Migration] {assetPath}: {ModuleName} {skippedCount}件の missing を自動修正できず");
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

        /// <summary>
        /// 指定サブモジュール内の旧フィールド名を `_target:` にリネームする。
        /// PrefabInstance の m_Modifications 内の propertyPath も書き換える。
        /// </summary>
        private static int RenameLegacyFieldsInModule(ref string content, string moduleName, string[] legacyFieldNames)
        {
            if (legacyFieldNames.Length == 0)
            {
                return 0;
            }

            var renamed = 0;
            var lines = content.Split('\n');
            var currentModuleIndent = -1;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("--- "))
                {
                    currentModuleIndent = -1;
                    continue;
                }

                var indent = MornMigrationUtil.IndentOf(line);
                var trimmed = line.TrimStart();

                if (trimmed == $"{moduleName}:" || trimmed.StartsWith($"{moduleName}:"))
                {
                    currentModuleIndent = indent;
                    continue;
                }

                if (currentModuleIndent >= 0 && !string.IsNullOrWhiteSpace(line) && indent <= currentModuleIndent)
                {
                    currentModuleIndent = -1;
                }

                if (currentModuleIndent < 0)
                {
                    continue;
                }

                foreach (var legacy in legacyFieldNames)
                {
                    if (trimmed.StartsWith($"{legacy}:"))
                    {
                        lines[i] = line.Replace($"{legacy}:", "_target:");
                        renamed++;
                        break;
                    }
                }
            }

            // PrefabInstance modifications 内の propertyPath も書き換え (例: "_cancelModule._cancelTarget" → "_cancelModule._target")
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith("propertyPath:"))
                {
                    continue;
                }

                foreach (var legacy in legacyFieldNames)
                {
                    var oldPath = $"{moduleName}.{legacy}";
                    var newPath = $"{moduleName}._target";
                    if (lines[i].Contains(oldPath))
                    {
                        lines[i] = lines[i].Replace(oldPath, newPath);
                        renamed++;
                    }
                }
            }

            if (renamed > 0)
            {
                content = string.Join('\n', lines);
            }

            return renamed;
        }

        private static Dictionary<ulong, MonoBehaviour> BuildButtonMap(GameObject root)
        {
            var prefabInstanceToButton = new Dictionary<ulong, MonoBehaviour>();
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

                if (AssetDatabase.AssetPathToGUID(scriptPath) != MornMigrationUtil.MornUGUIButtonGuid)
                {
                    continue;
                }

                var outer = mb.gameObject;
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
                    prefabInstanceToButton[gid.targetPrefabId] = mb;
                }
            }

            return prefabInstanceToButton;
        }
    }
}
#endif
