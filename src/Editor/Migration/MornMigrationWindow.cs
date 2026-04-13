#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MornLib
{
    public sealed class MornMigrationWindow : EditorWindow
    {
        // ========== GUID リマップテーブル ==========
        private static readonly Dictionary<string, (string newGuid, string oldName, string newName)> GuidRemapTable = new()
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

        // ========== フィールドリネーム ==========
        private static readonly string SubStateNewGuid = "aac67bf328824705a55354ac6d26608c";
        private static readonly string LinkModuleGuid = "8be286109e114b849574ebd8390b6191";
        private static readonly string WaitTimeStateGuid = "178f13c9c785bb341b8537958dc16138";

        // ========== 削除された GUID (対応先なし) ==========
        private static readonly Dictionary<string, string> DeletedGuids = new()
        {
            { "e830bade52d747819e91153be1b80223", "MornUGUIButtonModuleBase (削除)" },
            { "e31b2f6049aa48499baee31c0cea8064", "MornUGUISliderModuleBase (削除)" },
        };

        // ========== C# 置換テーブル ==========
        private static readonly (string oldText, string newText)[] CsReplacements =
        {
            ("using MornUGUI;", "using MornLib;"),
            ("MornUGUICtrl", "MornUGUIService"),
        };

        // ========== スキャン結果 ==========
        private readonly List<ScanResult> _remapResults = new();
        private readonly List<ScanResult> _deletedResults = new();
        private readonly List<ScanResult> _csResults = new();
        private readonly List<ScanResult> _fieldFixResults = new();
        private readonly List<ScanResult> _waitTimeFixResults = new();
        private readonly List<ScanResult> _mergeResults = new();
        private readonly List<ScanResult> _missingScriptResults = new();
        private readonly List<ScanResult> _strippedOrphanResults = new();
        private Vector2 _scrollPos;
        private bool _scanPrefabs = true;
        private bool _scanScenes = true;
        private bool _scanCs = true;
        private bool _foldRemap = true;
        private bool _foldDeleted = true;
        private bool _foldCs = true;
        private bool _foldFieldFix = true;
        private bool _foldWaitTimeFix = true;
        private bool _foldMerge = true;
        private bool _foldMissingScript = true;
        private bool _foldStrippedOrphan = true;

        private struct ScanResult
        {
            public string AssetPath;
            public string Details;
            public string OldGuid;
        }

        [MenuItem("Tools/Morn Migration Tool")]
        private static void Open()
        {
            GetWindow<MornMigrationWindow>("Morn Migration");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Morn Migration Tool", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 検索対象
            EditorGUILayout.LabelField("検索対象", EditorStyles.miniBoldLabel);
            _scanPrefabs = EditorGUILayout.Toggle("Prefabs", _scanPrefabs);
            _scanScenes = EditorGUILayout.Toggle("Scenes", _scanScenes);
            _scanCs = EditorGUILayout.Toggle("C# files", _scanCs);
            EditorGUILayout.Space(5);

            if (GUILayout.Button("スキャン", GUILayout.Height(30)))
            {
                Scan();
            }

            EditorGUILayout.Space(10);

            // 結果表示
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // GUID リマップ
            DrawRemapSection(
                ref _foldRemap,
                $"GUID リマップ必要 ({_remapResults.Count}件)",
                _remapResults);

            // Missing
            DrawSection(
                ref _foldDeleted,
                $"Missing — 対応先なし ({_deletedResults.Count}件)",
                _deletedResults,
                new Color(1f, 0.5f, 0.5f));

            // フィールド不整合
            DrawFieldFixSection(
                ref _foldFieldFix,
                $"SubState フィールド不整合 ({_fieldFixResults.Count}件)",
                _fieldFixResults);

            // WaitTimeState 不整合
            DrawWaitTimeFixSection(
                ref _foldWaitTimeFix,
                $"WaitTimeState 不整合 ({_waitTimeFixResults.Count}件)",
                _waitTimeFixResults);

            // コンポーネント統合
            DrawMergeSection(
                ref _foldMerge,
                $"Button + MornUGUIButton 共存 ({_mergeResults.Count}件)",
                _mergeResults);

            // Button stripped 孤立参照
            DrawStrippedOrphanSection(
                ref _foldStrippedOrphan,
                $"Button stripped 孤立参照 ({_strippedOrphanResults.Count}件)",
                _strippedOrphanResults);

            // C# 変更
            DrawSection(
                ref _foldCs,
                $"C# namespace/型名変更 ({_csResults.Count}件)",
                _csResults,
                new Color(0.6f, 0.8f, 1f));

            // Missing Script
            DrawSection(
                ref _foldMissingScript,
                $"Missing Script ({_missingScriptResults.Count}件)",
                _missingScriptResults,
                new Color(1f, 0.4f, 0.4f));

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            // 実行ボタン
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _remapResults.Count > 0;
                if (GUILayout.Button("GUID リマップ実行", GUILayout.Height(25)))
                {
                    ExecuteGuidRemap();
                }

                GUI.enabled = _csResults.Count > 0;
                if (GUILayout.Button("C# 一括置換実行", GUILayout.Height(25)))
                {
                    ExecuteCsReplace();
                }

                GUI.enabled = true;
            }

            if (GUILayout.Button("レポート出力 (Console)"))
            {
                PrintReport();
            }
        }

        private void Scan()
        {
            _remapResults.Clear();
            _deletedResults.Clear();
            _csResults.Clear();
            _fieldFixResults.Clear();
            _waitTimeFixResults.Clear();
            _mergeResults.Clear();
            _missingScriptResults.Clear();
            _strippedOrphanResults.Clear();
            _resolveCache.Clear();

            // プロジェクト全体の script GUID を収集 (Assets + Packages + Library/PackageCache)
            var knownScriptGuids = new HashSet<string>();
            if (_scanPrefabs || _scanScenes)
            {
                var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                var searchDirs = new[]
                {
                    Application.dataPath, // Assets/
                    Path.Combine(projectRoot, "Packages"),
                    Path.Combine(projectRoot, "Library", "PackageCache"),
                };
                foreach (var dir in searchDirs)
                {
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }

                    foreach (var metaFile in Directory.GetFiles(dir, "*.cs.meta", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var metaContent = File.ReadAllText(metaFile);
                            var guidIdx = metaContent.IndexOf("guid: ", StringComparison.Ordinal);
                            if (guidIdx >= 0)
                            {
                                var start = guidIdx + 6;
                                var end = metaContent.IndexOfAny(new[] { '\n', '\r', ' ', '\t' }, start);
                                if (end < 0)
                                {
                                    end = metaContent.Length;
                                }

                                if (end > start)
                                {
                                    knownScriptGuids.Add(metaContent.Substring(start, end - start).Trim());
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // skip
                        }
                    }
                }

                Debug.Log($"[Morn Migration] 既知 script GUID: {knownScriptGuids.Count} 件を収集");
            }

            var allGuids = GuidRemapTable.Keys
                .Concat(DeletedGuids.Keys)
                .ToHashSet();

            // Prefab/Scene スキャン
            if (_scanPrefabs || _scanScenes)
            {
                var assetPaths = new List<string>();
                if (_scanPrefabs)
                {
                    assetPaths.AddRange(
                        AssetDatabase.FindAssets("t:Prefab")
                            .Select(AssetDatabase.GUIDToAssetPath));
                }

                if (_scanScenes)
                {
                    assetPaths.AddRange(
                        AssetDatabase.FindAssets("t:Scene")
                            .Select(AssetDatabase.GUIDToAssetPath));
                }

                for (var i = 0; i < assetPaths.Count; i++)
                {
                    var path = assetPaths[i];
                    EditorUtility.DisplayProgressBar(
                        "MornUGUI スキャン",
                        path,
                        (float)i / assetPaths.Count);

                    try
                    {
                        var content = File.ReadAllText(path);
                        foreach (var kvp in GuidRemapTable)
                        {
                            if (content.Contains(kvp.Key))
                            {
                                _remapResults.Add(new ScanResult
                                {
                                    AssetPath = path,
                                    Details = $"{kvp.Value.oldName} → {kvp.Value.newName}",
                                    OldGuid = kvp.Key,
                                });
                            }
                        }

                        foreach (var kvp in DeletedGuids)
                        {
                            if (content.Contains(kvp.Key))
                            {
                                _deletedResults.Add(new ScanResult
                                {
                                    AssetPath = path,
                                    Details = $"{kvp.Value} (削除済み)",
                                });
                            }
                        }

                        // SubState フィールド不整合検出
                        if (content.Contains(SubStateNewGuid) && HasSubStateFieldMismatch(content))
                        {
                            _fieldFixResults.Add(new ScanResult
                            {
                                AssetPath = path,
                                Details = "SubState: _prefab → _instance リネーム必要",
                            });
                        }

                        // MornUGUIControlState フィールド不整合検出
                        if (content.Contains("_buttonStateLinkSets") ||
                            content.Contains("_buttonModule:") ||
                            content.Contains("_autoFocusTarget:") ||
                            content.Contains("_cancelTarget:") ||
                            HasIsActiveInAutoFocusOrCancelModule(content))
                        {
                            _fieldFixResults.Add(new ScanResult
                            {
                                AssetPath = path,
                                Details = "ControlState: フィールドリネーム / _isActive 削除",
                            });
                        }

                        // WaitTimeState FlexibleField<float> → float 検出
                        if (content.Contains($"guid: {WaitTimeStateGuid}") && HasWaitTimeFlexibleField(content))
                        {
                            _waitTimeFixResults.Add(new ScanResult
                            {
                                AssetPath = path,
                                Details = "WaitTimeState: _waitDuration FlexibleField<float> → float",
                            });
                        }

                        // Button + MornUGUIButton 共存検出 (stripped は除外)
                        if (HasNonStrippedButton(content) && content.Contains(MornUGUIButtonGuid))
                        {
                            _mergeResults.Add(new ScanResult
                            {
                                AssetPath = path,
                                Details = "Button + MornUGUIButton 共存 → 統合可能",
                            });
                        }

                        // Button stripped 孤立参照検出
                        var orphanCount = CountStrippedButtonOrphans(content, out var resolvableCount);
                        if (orphanCount > 0)
                        {
                            _strippedOrphanResults.Add(new ScanResult
                            {
                                AssetPath = path,
                                Details = $"{orphanCount}件の孤立参照 ({resolvableCount}件解決可能)",
                            });
                        }

                        // Missing script 検出 (全 m_Script guid を検査)
                        if (knownScriptGuids.Count > 0)
                        {
                            var idx = 0;
                            while ((idx = content.IndexOf("m_Script: {fileID: 11500000, guid: ", idx, StringComparison.Ordinal)) >= 0)
                            {
                                var guidStart = idx + "m_Script: {fileID: 11500000, guid: ".Length;
                                var guidEnd = content.IndexOf(',', guidStart);
                                if (guidEnd > guidStart)
                                {
                                    var scriptGuid = content.Substring(guidStart, guidEnd - guidStart);
                                    if (!knownScriptGuids.Contains(scriptGuid))
                                    {
                                        _missingScriptResults.Add(new ScanResult
                                        {
                                            AssetPath = path,
                                            Details = $"Missing script: {scriptGuid}",
                                            OldGuid = scriptGuid,
                                        });
                                    }
                                }

                                idx = guidEnd > 0 ? guidEnd : idx + 1;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // バイナリファイル等はスキップ
                    }
                }

                EditorUtility.ClearProgressBar();
            }

            // C# スキャン
            if (_scanCs)
            {
                var csFiles = Directory.GetFiles(
                    Application.dataPath,
                    "*.cs",
                    SearchOption.AllDirectories);

                foreach (var fullPath in csFiles)
                {
                    // MornUGUI サブモジュール自体はスキップ
                    if (fullPath.Contains("_Morn/MornUGUI"))
                    {
                        continue;
                    }

                    try
                    {
                        var content = File.ReadAllText(fullPath);
                        var relativePath = "Assets" + fullPath.Replace(Application.dataPath, "");
                        var details = new List<string>();

                        foreach (var (oldText, newText) in CsReplacements)
                        {
                            if (content.Contains(oldText))
                            {
                                details.Add($"{oldText} → {newText}");
                            }
                        }

                        if (details.Count > 0)
                        {
                            _csResults.Add(new ScanResult
                            {
                                AssetPath = relativePath,
                                Details = string.Join("\n", details),
                            });
                        }
                    }
                    catch (Exception)
                    {
                        // スキップ
                    }
                }
            }

            Debug.Log($"[MornUGUI Migration] スキャン完了: リマップ={_remapResults.Count}, Missing={_deletedResults.Count}, C#={_csResults.Count}");
        }

        private void ExecuteGuidRemap()
        {
            if (!EditorUtility.DisplayDialog(
                    "GUID リマップ実行",
                    $"{_remapResults.Count} 件の prefab/scene ファイル内の旧 GUID を新 GUID に置換します。\n\n実行前に git commit しておくことを推奨します。",
                    "実行",
                    "キャンセル"))
            {
                return;
            }

            var modifiedFiles = new HashSet<string>();
            foreach (var result in _remapResults)
            {
                var fullPath = Path.Combine(
                    Directory.GetParent(Application.dataPath)!.FullName,
                    result.AssetPath);

                try
                {
                    var content = File.ReadAllText(fullPath);
                    var modified = false;
                    foreach (var kvp in GuidRemapTable)
                    {
                        if (content.Contains(kvp.Key))
                        {
                            content = content.Replace(kvp.Key, kvp.Value.newGuid);
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        // フィールドリネーム
                        if (content.Contains(SubStateNewGuid))
                        {
                            content = FixSubStateFields(content);
                        }

                        if (content.Contains(LinkModuleGuid))
                        {
                            content = FixControlStateFields(content);
                        }

                        File.WriteAllText(fullPath, content);
                        modifiedFiles.Add(result.AssetPath);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Morn Migration] {result.AssetPath} の置換に失敗: {e.Message}");
                }
            }

            Debug.Log($"[Morn Migration] GUID リマップ完了: {modifiedFiles.Count} ファイルを更新 (Ctrl+R でリフレッシュ)");
            Scan();
        }

        private void ExecuteCsReplace()
        {
            if (!EditorUtility.DisplayDialog(
                    "C# 一括置換実行",
                    $"{_csResults.Count} 件の C# ファイルの namespace/型名を置換します。\n\n実行前に git commit しておくことを推奨します。",
                    "実行",
                    "キャンセル"))
            {
                return;
            }

            var modifiedCount = 0;
            foreach (var result in _csResults)
            {
                var fullPath = Path.Combine(
                    Directory.GetParent(Application.dataPath)!.FullName,
                    result.AssetPath);

                try
                {
                    var content = File.ReadAllText(fullPath);
                    var modified = false;
                    foreach (var (oldText, newText) in CsReplacements)
                    {
                        if (content.Contains(oldText))
                        {
                            content = content.Replace(oldText, newText);
                            modified = true;
                        }
                    }

                    // using MornLib; の重複除去
                    if (modified)
                    {
                        var lines = content.Split('\n').ToList();
                        var mornLibCount = 0;
                        for (var i = lines.Count - 1; i >= 0; i--)
                        {
                            if (lines[i].Trim() == "using MornLib;")
                            {
                                mornLibCount++;
                                if (mornLibCount > 1)
                                {
                                    lines.RemoveAt(i);
                                }
                            }
                        }

                        content = string.Join('\n', lines);
                        File.WriteAllText(fullPath, content);
                        modifiedCount++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MornUGUI Migration] {result.AssetPath} の置換に失敗: {e.Message}");
                }
            }

            Debug.Log($"[Morn Migration] C# 置換完了: {modifiedCount} ファイルを更新 (Ctrl+R でリフレッシュ)");
            Scan();
        }

        private void PrintReport()
        {
            var report = "=== MornUGUI Migration Report ===\n\n";

            report += $"■ GUID リマップ必要 ({_remapResults.Count}件)\n";
            foreach (var r in _remapResults)
            {
                report += $"  {r.AssetPath}\n    {r.Details}\n";
            }

            report += $"\n■ Missing — 対応先なし ({_deletedResults.Count}件)\n";
            foreach (var r in _deletedResults)
            {
                report += $"  {r.AssetPath}\n    {r.Details}\n";
            }

            report += $"\n■ C# namespace/型名変更 ({_csResults.Count}件)\n";
            foreach (var r in _csResults)
            {
                report += $"  {r.AssetPath}\n    {r.Details}\n";
            }

            Debug.Log(report);
        }

        private static void DrawSection(
            ref bool foldout,
            string title,
            List<ScanResult> results,
            Color headerColor)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = headerColor;
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            GUI.backgroundColor = originalColor;

            if (!foldout)
            {
                return;
            }

            if (results.Count == 0)
            {
                EditorGUILayout.LabelField("  (なし)", EditorStyles.miniLabel);
                return;
            }

            EditorGUI.indentLevel++;
            foreach (var result in results)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            Path.GetFileName(result.AssetPath),
                            EditorStyles.linkLabel,
                            GUILayout.ExpandWidth(false)))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.AssetPath);
                        if (obj != null)
                        {
                            EditorGUIUtility.PingObject(obj);
                            Selection.activeObject = obj;
                        }
                    }

                    EditorGUILayout.LabelField(result.Details, EditorStyles.miniLabel);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawRemapSection(
            ref bool foldout,
            string title,
            List<ScanResult> results)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.9f, 0.4f);
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            GUI.backgroundColor = originalColor;

            if (!foldout)
            {
                return;
            }

            if (results.Count == 0)
            {
                EditorGUILayout.LabelField("  (なし)", EditorStyles.miniLabel);
                return;
            }

            EditorGUI.indentLevel++;
            for (var i = results.Count - 1; i >= 0; i--)
            {
                var result = results[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            Path.GetFileName(result.AssetPath),
                            EditorStyles.linkLabel,
                            GUILayout.ExpandWidth(false)))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.AssetPath);
                        if (obj != null)
                        {
                            EditorGUIUtility.PingObject(obj);
                            Selection.activeObject = obj;
                        }
                    }

                    EditorGUILayout.LabelField(result.Details, EditorStyles.miniLabel);

                    if (GUILayout.Button("変換", GUILayout.Width(40)))
                    {
                        RemapSingleFile(result);
                        results.RemoveAt(i);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        private void RemapSingleFile(ScanResult result)
        {
            var fullPath = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName,
                result.AssetPath);

            try
            {
                var content = File.ReadAllText(fullPath);
                if (!string.IsNullOrEmpty(result.OldGuid) && GuidRemapTable.TryGetValue(result.OldGuid, out var remap))
                {
                    content = content.Replace(result.OldGuid, remap.newGuid);

                    // ObsoleteSubState → SubState: _instantiate==0 のとき _prefab を _instance にリネーム
                    if (remap.newGuid == SubStateNewGuid)
                    {
                        content = FixSubStateFields(content);
                    }

                    File.WriteAllText(fullPath, content);
                    // AssetDatabase.Refresh(); — 手動 Ctrl+R で
                    Debug.Log($"[Morn Migration] {result.AssetPath}: {remap.oldName} → {remap.newName}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {result.AssetPath} の変換に失敗: {e.Message}");
            }
        }

        /// <summary>
        /// ObsoleteSubState → SubState のフィールド互換性修正。
        /// 旧: _prefab が直接参照 + インスタンス化 兼用
        /// 新: _instance が直接参照用、_prefab がインスタンス化用
        /// _instantiate: 0 のとき _prefab の値を _instance に移す。
        /// </summary>
        private static string FixSubStateFields(string content)
        {
            var lines = content.Split('\n');
            var modified = false;
            for (var i = 0; i < lines.Length; i++)
            {
                // SubState コンポーネントブロック内で _instantiate: 0 かつ _prefab に参照あり を検出
                if (!lines[i].Contains($"guid: {SubStateNewGuid}"))
                {
                    continue;
                }

                // このコンポーネントブロック内を走査
                var instantiateValue = -1;
                var prefabLineIdx = -1;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var trimmed = lines[j].TrimStart();
                    // 次のコンポーネント境界
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

                // _instantiate: 0 かつ _prefab に参照あり → _instance にリネーム
                if (instantiateValue == 0 && prefabLineIdx >= 0)
                {
                    lines[prefabLineIdx] = lines[prefabLineIdx].Replace("_prefab:", "_instance:");
                    modified = true;
                    Debug.Log($"[Morn Migration] SubState フィールドリネーム: _prefab → _instance (line {prefabLineIdx + 1})");
                }
            }

            return modified ? string.Join('\n', lines) : content;
        }

        /// <summary>新 SubState GUID を持つが _instantiate:0 + _prefab に値あり のパターンを検出</summary>
        private static bool HasSubStateFieldMismatch(string content)
        {
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains($"guid: {SubStateNewGuid}"))
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

        private void DrawFieldFixSection(
            ref bool foldout,
            string title,
            List<ScanResult> results)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            GUI.backgroundColor = originalColor;

            if (!foldout)
            {
                return;
            }

            if (results.Count == 0)
            {
                EditorGUILayout.LabelField("  (なし)", EditorStyles.miniLabel);
                return;
            }

            EditorGUI.indentLevel++;
            for (var i = results.Count - 1; i >= 0; i--)
            {
                var result = results[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            Path.GetFileName(result.AssetPath),
                            EditorStyles.linkLabel,
                            GUILayout.ExpandWidth(false)))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.AssetPath);
                        if (obj != null)
                        {
                            EditorGUIUtility.PingObject(obj);
                            Selection.activeObject = obj;
                        }
                    }

                    EditorGUILayout.LabelField(result.Details, EditorStyles.miniLabel);

                    if (GUILayout.Button("修正", GUILayout.Width(40)))
                    {
                        FixSubStateFieldsInFile(result.AssetPath);
                        results.RemoveAt(i);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawWaitTimeFixSection(
            ref bool foldout,
            string title,
            List<ScanResult> results)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.6f, 0.8f);
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            GUI.backgroundColor = originalColor;

            if (!foldout)
            {
                return;
            }

            if (results.Count == 0)
            {
                EditorGUILayout.LabelField("  (なし)", EditorStyles.miniLabel);
                return;
            }

            EditorGUI.indentLevel++;
            for (var i = results.Count - 1; i >= 0; i--)
            {
                var result = results[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            Path.GetFileName(result.AssetPath),
                            EditorStyles.linkLabel,
                            GUILayout.ExpandWidth(false)))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.AssetPath);
                        if (obj != null)
                        {
                            EditorGUIUtility.PingObject(obj);
                            Selection.activeObject = obj;
                        }
                    }

                    EditorGUILayout.LabelField(result.Details, EditorStyles.miniLabel);

                    if (GUILayout.Button("修正", GUILayout.Width(40)))
                    {
                        FixWaitTimeFieldInFile(result.AssetPath);
                        results.RemoveAt(i);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        private static void FixWaitTimeFieldInFile(string assetPath)
        {
            var fullPath = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName,
                assetPath);

            try
            {
                var content = File.ReadAllText(fullPath);
                content = FixWaitTimeField(content);
                File.WriteAllText(fullPath, content);
                Debug.Log($"[Morn Migration] {assetPath}: WaitTimeState 修正完了");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {assetPath} の WaitTimeState 修正に失敗: {e.Message}");
            }
        }

        private static void FixSubStateFieldsInFile(string assetPath)
        {
            var fullPath = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName,
                assetPath);

            try
            {
                var content = File.ReadAllText(fullPath);
                content = FixSubStateFields(content);
                content = FixControlStateFields(content);
                File.WriteAllText(fullPath, content);
                // AssetDatabase.Refresh(); — 手動 Ctrl+R で
                Debug.Log($"[Morn Migration] {assetPath}: フィールド修正完了");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {assetPath} の修正に失敗: {e.Message}");
            }
        }

        /// <summary>WaitTimeState の _waitDuration が FlexibleField<float> 形式なら検出</summary>
        private static bool HasWaitTimeFlexibleField(string content)
        {
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains($"guid: {WaitTimeStateGuid}"))
                {
                    continue;
                }

                for (var j = i + 1; j < lines.Length; j++)
                {
                    var trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("--- !u!"))
                    {
                        break;
                    }

                    if (trimmed.StartsWith("_waitDuration:"))
                    {
                        // FlexibleField なら値が同一行に無く、次行が "_Type:" で始まる
                        var rest = trimmed.Substring("_waitDuration:".Length).Trim();
                        if (rest.Length == 0 && j + 1 < lines.Length && lines[j + 1].TrimStart().StartsWith("_Type:"))
                        {
                            return true;
                        }

                        break;
                    }
                }
            }

            return false;
        }

        /// <summary>WaitTimeState の _waitDuration を FlexibleField 構造から float 値に変換</summary>
        private static string FixWaitTimeField(string content)
        {
            var lines = content.Split('\n').ToList();
            var modified = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (!lines[i].Contains($"guid: {WaitTimeStateGuid}"))
                {
                    continue;
                }

                for (var j = i + 1; j < lines.Count; j++)
                {
                    var rawLine = lines[j];
                    var trimmed = rawLine.TrimStart();
                    if (trimmed.StartsWith("--- !u!"))
                    {
                        break;
                    }

                    if (!trimmed.StartsWith("_waitDuration:"))
                    {
                        continue;
                    }

                    var rest = trimmed.Substring("_waitDuration:".Length).Trim();
                    if (rest.Length != 0 || j + 1 >= lines.Count || !lines[j + 1].TrimStart().StartsWith("_Type:"))
                    {
                        break;
                    }

                    var indentLength = rawLine.Length - trimmed.Length;
                    var indent = rawLine.Substring(0, indentLength);

                    var floatValue = "0";
                    var removeEnd = j + 1;
                    for (var k = j + 1; k < lines.Count; k++)
                    {
                        var kLine = lines[k];
                        var kTrimmedLocal = kLine.TrimStart();
                        if (kLine.Length == 0 || kTrimmedLocal.StartsWith("--- !u!"))
                        {
                            removeEnd = k;
                            break;
                        }

                        var kIndentLen = kLine.Length - kTrimmedLocal.Length;
                        if (kIndentLen <= indentLength)
                        {
                            removeEnd = k;
                            break;
                        }

                        if (kIndentLen == indentLength + 2 && kTrimmedLocal.StartsWith("_Value:"))
                        {
                            floatValue = kTrimmedLocal.Substring("_Value:".Length).Trim();
                        }

                        removeEnd = k + 1;
                    }

                    lines[j] = $"{indent}_waitDuration: {floatValue}";
                    lines.RemoveRange(j + 1, removeEnd - (j + 1));
                    modified = true;
                    Debug.Log($"[Morn Migration] WaitTimeState _waitDuration: FlexibleField<float> → float (value={floatValue})");
                    break;
                }
            }

            return modified ? string.Join('\n', lines) : content;
        }

        /// <summary>
        /// MornUGUIControlState 全体のフィールドリネーム。
        /// _buttonModule → _linkModule
        /// _buttonStateLinkSets → _stateLinkSets
        /// Button: → Target: (LinkSet 内)
        /// _focusModule → _autoFocusModule
        /// モジュール内 _ignore/_active → _isActive
        /// _leftFrame 削除
        /// </summary>
        private static string FixControlStateFields(string content)
        {
            var modified = false;

            // _buttonModule → _linkModule
            if (content.Contains("_buttonModule:"))
            {
                content = content.Replace("_buttonModule:", "_linkModule:");
                modified = true;
                Debug.Log("[Morn Migration] ControlState: _buttonModule → _linkModule");
            }

            // _buttonStateLinkSets → _stateLinkSets
            if (content.Contains("_buttonStateLinkSets"))
            {
                content = content.Replace("_buttonStateLinkSets", "_stateLinkSets");
                modified = true;
                Debug.Log("[Morn Migration] ControlState: _buttonStateLinkSets → _stateLinkSets");
            }

            // _focusModule → _autoFocusModule
            if (content.Contains("_focusModule:"))
            {
                content = content.Replace("_focusModule:", "_autoFocusModule:");
                modified = true;
                Debug.Log("[Morn Migration] ControlState: _focusModule → _autoFocusModule");
            }

            // _autoFocusTarget → _target (AutoFocusModule 内)
            if (content.Contains("_autoFocusTarget:"))
            {
                content = content.Replace("_autoFocusTarget:", "_target:");
                modified = true;
                Debug.Log("[Morn Migration] ControlState: _autoFocusTarget → _target");
            }

            // _cancelTarget → _target (CancelModule 内)
            if (content.Contains("_cancelTarget:"))
            {
                content = content.Replace("_cancelTarget:", "_target:");
                modified = true;
                Debug.Log("[Morn Migration] ControlState: _cancelTarget → _target");
            }

            // AutoFocus/Cancel Module の _isActive フラグを削除 (null 判定ベースに変更)
            var removed = RemoveIsActiveFromModules(ref content);
            if (removed > 0)
            {
                modified = true;
                Debug.Log($"[Morn Migration] ControlState: _autoFocusModule/_cancelModule の _isActive を {removed} 件削除");
            }

            // Button: → Target: (LinkSet 内)
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if ((trimmed.StartsWith("Button:") || trimmed.StartsWith("- Button:")) && trimmed.Contains("{fileID:"))
                {
                    lines[i] = lines[i].Replace("Button:", "Target:");
                    modified = true;
                }
            }

            if (modified)
            {
                content = string.Join('\n', lines);
            }

            return content;
        }

        /// <summary>
        /// 全 prefab/scene から Button + MornUGUIButton が共存する GameObject を検出し、
        /// Button の Navigation/Interactable を MornUGUIButton に移してから Button を削除する。
        /// </summary>
        private const string UnityButtonGuid = "4e29b1a8efbd4b44bb3f3716e73f07ff";
        private const string MornUGUIButtonGuid = "6a3b7849bb0c4f15b15dee46b0711a7c";

        private void DrawMergeSection(
            ref bool foldout,
            string title,
            List<ScanResult> results)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.8f, 1f, 0.6f);
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            GUI.backgroundColor = originalColor;

            if (!foldout)
            {
                return;
            }

            if (results.Count == 0)
            {
                EditorGUILayout.LabelField("  (なし)", EditorStyles.miniLabel);
                return;
            }

            EditorGUI.indentLevel++;
            for (var i = results.Count - 1; i >= 0; i--)
            {
                var result = results[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            Path.GetFileName(result.AssetPath),
                            EditorStyles.linkLabel,
                            GUILayout.ExpandWidth(false)))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.AssetPath);
                        if (obj != null)
                        {
                            EditorGUIUtility.PingObject(obj);
                            Selection.activeObject = obj;
                        }
                    }

                    EditorGUILayout.LabelField(result.Details, EditorStyles.miniLabel);

                    if (GUILayout.Button("統合", GUILayout.Width(40)))
                    {
                        MergeSinglePrefab(result.AssetPath);
                        results.RemoveAt(i);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// YAML テキスト操作で Button → MornUGUIButton 統合。
        /// 1. Button コンポーネントの Selectable フィールドを MornUGUIButton にコピー
        /// 2. Button の fileID を MornUGUIButton の fileID に全置換 (参照引き継ぎ)
        /// 3. Button コンポーネントブロックを削除
        /// 4. GameObject の m_Component リストから Button の参照を削除
        /// </summary>
        private static void MergeSinglePrefab(string assetPath)
        {
            var fullPath = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName, assetPath);

            try
            {
                var content = File.ReadAllText(fullPath);
                var lines = content.Split('\n').ToList();
                var modified = false;

                // 同一 GameObject 上の Button と MornUGUIButton のペアを探す
                // まず全コンポーネントブロックを解析
                var components = ParseComponents(lines);

                // GameObject ごとにグルーピング
                var goComponents = new Dictionary<string, List<ComponentInfo>>();
                foreach (var comp in components)
                {
                    if (!goComponents.ContainsKey(comp.GameObjectFileId))
                    {
                        goComponents[comp.GameObjectFileId] = new List<ComponentInfo>();
                    }

                    goComponents[comp.GameObjectFileId].Add(comp);
                }

                // Button + MornUGUIButton 共存を検出して統合
                var buttonFileIdsToRemove = new List<string>();
                var fileIdReplacements = new Dictionary<string, string>();

                foreach (var kvp in goComponents)
                {
                    var buttonComp = kvp.Value.FindIndex(c => c.ScriptGuid == UnityButtonGuid);
                    var mornComp = kvp.Value.FindIndex(c => c.ScriptGuid == MornUGUIButtonGuid);
                    if (buttonComp < 0 || mornComp < 0)
                    {
                        continue;
                    }

                    var btn = kvp.Value[buttonComp];
                    var mrn = kvp.Value[mornComp];

                    // fileID 置換テーブルに追加
                    // (Selectable フィールドコピーは不要 — MornUGUIButton 自身が Selectable を継承済み)
                    fileIdReplacements[btn.FileId] = mrn.FileId;
                    buttonFileIdsToRemove.Add(btn.FileId);
                    modified = true;

                    Debug.Log($"[Morn Migration] {assetPath}: Button({btn.FileId}) → MornUGUIButton({mrn.FileId}) 統合");
                }

                if (!modified)
                {
                    return;
                }

                // Button の m_Component エントリを先に削除 (fileID 置換前)
                foreach (var fileId in buttonFileIdsToRemove)
                {
                    for (var i = lines.Count - 1; i >= 0; i--)
                    {
                        if (lines[i].TrimStart().StartsWith("- component:") && lines[i].Contains($"fileID: {fileId}"))
                        {
                            lines.RemoveAt(i);
                            break;
                        }
                    }
                }

                // 全テキストを結合してから fileID 置換 + ブロック削除
                content = string.Join('\n', lines);

                // fileID 参照を全置換
                foreach (var kvp in fileIdReplacements)
                {
                    content = content.Replace($"fileID: {kvp.Key}", $"fileID: {kvp.Value}");
                }

                // Button コンポーネントブロックを削除
                lines = content.Split('\n').ToList();
                foreach (var fileId in buttonFileIdsToRemove)
                {
                    RemoveComponentBlock(lines, fileId, UnityButtonGuid);
                }

                content = string.Join('\n', lines);
                File.WriteAllText(fullPath, content);
                Debug.Log($"[Morn Migration] {assetPath}: Button 統合 + 参照引き継ぎ完了 (Ctrl+R でリフレッシュしてください)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {assetPath} の統合に失敗: {e.Message}");
            }
        }

        private struct ComponentInfo
        {
            public string FileId;
            public string ScriptGuid;
            public string GameObjectFileId;
            public int StartLine;
            public int EndLine;
        }

        private static List<ComponentInfo> ParseComponents(List<string> lines)
        {
            var result = new List<ComponentInfo>();
            for (var i = 0; i < lines.Count; i++)
            {
                // --- !u!114 &FILEID
                if (!lines[i].StartsWith("--- !u!114 &"))
                {
                    continue;
                }

                var fileId = lines[i].Substring("--- !u!114 &".Length).Trim();
                var scriptGuid = "";
                var goFileId = "";
                var endLine = lines.Count - 1;

                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (lines[j].StartsWith("--- "))
                    {
                        endLine = j - 1;
                        break;
                    }

                    var trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("m_Script:") && trimmed.Contains("guid: "))
                    {
                        var guidStart = trimmed.IndexOf("guid: ", StringComparison.Ordinal) + 6;
                        var guidEnd = trimmed.IndexOf(',', guidStart);
                        if (guidEnd > guidStart)
                        {
                            scriptGuid = trimmed.Substring(guidStart, guidEnd - guidStart);
                        }
                    }

                    if (trimmed.StartsWith("m_GameObject:") && trimmed.Contains("fileID: "))
                    {
                        var fidStart = trimmed.IndexOf("fileID: ", StringComparison.Ordinal) + 8;
                        var fidEnd = trimmed.IndexOf('}', fidStart);
                        if (fidEnd > fidStart)
                        {
                            goFileId = trimmed.Substring(fidStart, fidEnd - fidStart);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(scriptGuid))
                {
                    result.Add(new ComponentInfo
                    {
                        FileId = fileId,
                        ScriptGuid = scriptGuid,
                        GameObjectFileId = goFileId,
                        StartLine = i,
                        EndLine = endLine,
                    });
                }
            }

            return result;
        }

        /// <summary>stripped でない Button コンポーネントが存在するか</summary>
        private static bool HasNonStrippedButton(string content)
        {
            var idx = 0;
            while (true)
            {
                idx = content.IndexOf(UnityButtonGuid, idx, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return false;
                }

                // この GUID を含むブロックヘッダーを探す (--- !u!114 &xxx か --- !u!114 &xxx stripped か)
                var blockStart = content.LastIndexOf("--- !u!", idx, StringComparison.Ordinal);
                if (blockStart >= 0)
                {
                    var headerEnd = content.IndexOf('\n', blockStart);
                    if (headerEnd > blockStart)
                    {
                        var header = content.Substring(blockStart, headerEnd - blockStart);
                        if (!header.Contains("stripped"))
                        {
                            return true;
                        }
                    }
                }

                idx++;
            }
        }

        private static readonly string[] SelectableFields =
        {
            "m_Navigation:", "m_Transition:", "m_Colors:", "m_SpriteState:",
            "m_AnimationTriggers:", "m_Interactable:", "m_TargetGraphic:",
        };

        private static void CopySelectableFields(List<string> lines, ComponentInfo source, ComponentInfo target)
        {
            // source (Button) から Selectable フィールドを抽出
            var fieldLines = new List<string>();
            for (var i = source.StartLine + 1; i <= source.EndLine; i++)
            {
                var trimmed = lines[i].TrimStart();
                var isSelectableField = false;
                foreach (var sf in SelectableFields)
                {
                    if (trimmed.StartsWith(sf))
                    {
                        isSelectableField = true;
                        break;
                    }
                }

                if (!isSelectableField)
                {
                    continue;
                }

                // このフィールドのブロック全体 (インデント深い行も含む) を収集
                fieldLines.Add(lines[i]);
                for (var j = i + 1; j <= source.EndLine; j++)
                {
                    var nextTrimmed = lines[j].TrimStart();
                    // 次のトップレベルフィールドが来たら終了
                    if (nextTrimmed.Length > 0 && !nextTrimmed.StartsWith(" ") &&
                        !nextTrimmed.StartsWith("-") && !char.IsWhiteSpace(nextTrimmed[0]))
                    {
                        break;
                    }

                    // インデントが浅い or 新しいフィールドならbreak
                    if (lines[j].Length > 0 && lines[j][0] != ' ' && !lines[j].TrimStart().StartsWith("-")
                        && !lines[j].TrimStart().StartsWith("m_"))
                    {
                        break;
                    }

                    fieldLines.Add(lines[j]);
                    i = j; // skip
                }
            }

            if (fieldLines.Count == 0)
            {
                return;
            }

            // target (MornUGUIButton) の既存 Selectable フィールドを削除して source のものを挿入
            // まず target 内の m_EditorClassIdentifier: の後にフィールドを挿入
            for (var i = target.StartLine + 1; i <= target.EndLine; i++)
            {
                if (lines[i].TrimStart().StartsWith("m_EditorClassIdentifier:"))
                {
                    // この行の後に挿入
                    lines.InsertRange(i + 1, fieldLines);
                    return;
                }
            }
        }

        // ========== Button stripped 孤立参照の修正 ==========

        // (srcFileId, srcGuid) → (newFileId, newGuid) のキャッシュ
        private static readonly Dictionary<(string, string), (string fileId, string guid)?> _resolveCache = new();

        /// <summary>
        /// 自身の YAML 内 stripped エントリで m_Script が UnityEngine.UI.Button を指しているものをカウント。
        /// 解決可能(差し替え先 MornUGUIButton が推測できる)な件数も返す。
        /// </summary>
        private static int CountStrippedButtonOrphans(string content, out int resolvableCount)
        {
            resolvableCount = 0;
            var orphans = ExtractStrippedButtonOrphans(content);
            foreach (var o in orphans)
            {
                if (ResolveMornUGUIButtonTarget(o.SrcFileId, o.SrcGuid) != null)
                {
                    resolvableCount++;
                }
            }

            return orphans.Count;
        }

        private struct StrippedOrphan
        {
            public string StrippedFileId;
            public string SrcFileId;
            public string SrcGuid;
            public int BlockStart;
            public int BlockEnd;
        }

        /// <summary>YAML 内の Button stripped 孤立エントリを抽出</summary>
        private static List<StrippedOrphan> ExtractStrippedButtonOrphans(string content)
        {
            var result = new List<StrippedOrphan>();
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("--- !u!114 &") || !lines[i].Contains("stripped"))
                {
                    continue;
                }

                var fileIdStart = "--- !u!114 &".Length;
                var fileIdEnd = lines[i].IndexOf(' ', fileIdStart);
                if (fileIdEnd < 0)
                {
                    continue;
                }

                var strippedFileId = lines[i].Substring(fileIdStart, fileIdEnd - fileIdStart);

                var scriptGuid = "";
                var srcFileId = "";
                var srcGuid = "";
                var endLine = lines.Length - 1;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].StartsWith("--- "))
                    {
                        endLine = j - 1;
                        break;
                    }

                    var trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("m_CorrespondingSourceObject:") && trimmed.Contains("fileID: "))
                    {
                        var fidStart = trimmed.IndexOf("fileID: ", StringComparison.Ordinal) + 8;
                        var fidEnd = trimmed.IndexOf(',', fidStart);
                        if (fidEnd > fidStart)
                        {
                            srcFileId = trimmed.Substring(fidStart, fidEnd - fidStart);
                        }

                        var gidStart = trimmed.IndexOf("guid: ", StringComparison.Ordinal);
                        if (gidStart > 0)
                        {
                            gidStart += 6;
                            var gidEnd = trimmed.IndexOf(',', gidStart);
                            if (gidEnd > gidStart)
                            {
                                srcGuid = trimmed.Substring(gidStart, gidEnd - gidStart);
                            }
                        }
                    }

                    if (trimmed.StartsWith("m_Script:") && trimmed.Contains("guid: "))
                    {
                        var gidStart = trimmed.IndexOf("guid: ", StringComparison.Ordinal) + 6;
                        var gidEnd = trimmed.IndexOf(',', gidStart);
                        if (gidEnd > gidStart)
                        {
                            scriptGuid = trimmed.Substring(gidStart, gidEnd - gidStart);
                        }
                    }
                }

                if (scriptGuid == UnityButtonGuid && !string.IsNullOrEmpty(srcFileId) && !string.IsNullOrEmpty(srcGuid))
                {
                    result.Add(new StrippedOrphan
                    {
                        StrippedFileId = strippedFileId,
                        SrcFileId = srcFileId,
                        SrcGuid = srcGuid,
                        BlockStart = i,
                        BlockEnd = endLine,
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 指定 prefab (guid) 内で MornUGUIButton を見つける。見つからない場合は Root PrefabInstance を辿って再帰。
        /// 複数候補がある場合は Root GameObject 上のものを優先。
        /// </summary>
        private static (string fileId, string guid)? ResolveMornUGUIButtonTarget(string srcFileId, string srcGuid)
        {
            var key = (srcFileId, srcGuid);
            if (_resolveCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var result = ResolveMornUGUIButtonTargetInternal(srcGuid, new HashSet<string>());
            _resolveCache[key] = result;
            return result;
        }

        private static (string fileId, string guid)? ResolveMornUGUIButtonTargetInternal(string guid, HashSet<string> visited)
        {
            if (!visited.Add(guid))
            {
                return null;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var fullPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetPath);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            string content;
            try
            {
                content = File.ReadAllText(fullPath);
            }
            catch
            {
                return null;
            }

            var lines = content.Split('\n');
            // 非 stripped な MornUGUIButton コンポーネントを探す
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("--- !u!114 &") || lines[i].Contains("stripped"))
                {
                    continue;
                }

                var fileIdStart = "--- !u!114 &".Length;
                var fileIdEnd = lines[i].Length;
                for (var k = fileIdStart; k < lines[i].Length; k++)
                {
                    if (!char.IsDigit(lines[i][k]))
                    {
                        fileIdEnd = k;
                        break;
                    }
                }

                var fileId = lines[i].Substring(fileIdStart, fileIdEnd - fileIdStart);

                // このブロック内で m_Script guid を確認
                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].StartsWith("--- "))
                    {
                        break;
                    }

                    var trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("m_Script:") && trimmed.Contains($"guid: {MornUGUIButtonGuid}"))
                    {
                        return (fileId, guid);
                    }
                }
            }

            // 直接ない場合、Root PrefabInstance を探して再帰
            // Root とは m_TransformParent が {fileID: 0} の PrefabInstance
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("--- !u!1001 &"))
                {
                    continue;
                }

                string sourceGuid = null;
                var parentFileId = "?";
                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].StartsWith("--- "))
                    {
                        break;
                    }

                    var trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("m_TransformParent:") && trimmed.Contains("fileID: "))
                    {
                        var fidStart = trimmed.IndexOf("fileID: ", StringComparison.Ordinal) + 8;
                        var fidEnd = trimmed.IndexOf('}', fidStart);
                        if (fidEnd > fidStart)
                        {
                            parentFileId = trimmed.Substring(fidStart, fidEnd - fidStart);
                        }
                    }

                    if (trimmed.StartsWith("m_SourcePrefab:") && trimmed.Contains("guid: "))
                    {
                        var gidStart = trimmed.IndexOf("guid: ", StringComparison.Ordinal) + 6;
                        var gidEnd = trimmed.IndexOf(',', gidStart);
                        if (gidEnd > gidStart)
                        {
                            sourceGuid = trimmed.Substring(gidStart, gidEnd - gidStart);
                        }
                    }
                }

                if (parentFileId == "0" && !string.IsNullOrEmpty(sourceGuid))
                {
                    var nested = ResolveMornUGUIButtonTargetInternal(sourceGuid, visited);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private void DrawStrippedOrphanSection(
            ref bool foldout,
            string title,
            List<ScanResult> results)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.6f, 0.8f);
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            GUI.backgroundColor = originalColor;

            if (!foldout)
            {
                return;
            }

            if (results.Count == 0)
            {
                EditorGUILayout.LabelField("  (なし)", EditorStyles.miniLabel);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"全て修正 ({results.Count}件)", GUILayout.Height(22)))
                {
                    if (EditorUtility.DisplayDialog(
                            "全ての Button stripped 孤立参照を修正",
                            $"{results.Count} ファイル内の孤立参照を MornUGUIButton に差し替えます。\n\n実行前に git commit しておくことを推奨します。",
                            "実行",
                            "キャンセル"))
                    {
                        for (var i = results.Count - 1; i >= 0; i--)
                        {
                            if (FixStrippedOrphansSingle(results[i].AssetPath))
                            {
                                results.RemoveAt(i);
                            }
                        }
                    }
                }
            }

            EditorGUI.indentLevel++;
            for (var i = results.Count - 1; i >= 0; i--)
            {
                var result = results[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            Path.GetFileName(result.AssetPath),
                            EditorStyles.linkLabel,
                            GUILayout.ExpandWidth(false)))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.AssetPath);
                        if (obj != null)
                        {
                            EditorGUIUtility.PingObject(obj);
                            Selection.activeObject = obj;
                        }
                    }

                    EditorGUILayout.LabelField(result.Details, EditorStyles.miniLabel);

                    if (GUILayout.Button("修正", GUILayout.Width(40)))
                    {
                        if (FixStrippedOrphansSingle(result.AssetPath))
                        {
                            results.RemoveAt(i);
                        }
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// PrefabUtility 経由で prefab をロードし、_autoFocusModule._target の missing 参照に
        /// MornUGUIButton を代入して保存する。
        /// 候補が一意に定まらない場合(hierarchy 内に MornUGUIButton が複数ある場合)はスキップする。
        /// Unity が stripped エントリの fileID を自動生成するので安全。
        /// scene ファイルは現在未対応。
        /// </summary>
        private static bool FixStrippedOrphansSingle(string assetPath)
        {
            if (!assetPath.EndsWith(".prefab"))
            {
                Debug.LogWarning($"[Morn Migration] {assetPath}: scene は未対応。手動で修正してください");
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(assetPath);
            if (root == null)
            {
                Debug.LogError($"[Morn Migration] {assetPath}: ロード失敗");
                return false;
            }

            try
            {
                // 1. YAML を事前解析: orphan stripped エントリ → m_PrefabInstance, 各 MonoBehaviour の _target 参照順序
                var fullPath = Path.Combine(
                    Directory.GetParent(Application.dataPath)!.FullName, assetPath);
                var yamlContent = File.ReadAllText(fullPath);
                var yamlLines = yamlContent.Split('\n');
                var orphanToPrefabInstance = new Dictionary<ulong, ulong>();
                ParseOrphanStrippedEntries(yamlLines, orphanToPrefabInstance);

                // MonoBehaviour fileID → 順番に並んだ orphan fileID リスト
                var perMonoBehaviour = new Dictionary<ulong, List<ulong>>();
                ParseTargetReferences(yamlLines, orphanToPrefabInstance, perMonoBehaviour);

                // 2. hierarchy 内の全 MornUGUIButton を収集
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

                    if (AssetDatabase.AssetPathToGUID(scriptPath) == MornUGUIButtonGuid)
                    {
                        allButtons.Add(mb);
                    }
                }

                // 3. outermost PrefabInstance root の targetPrefabId → MornUGUIButton をマップ化
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

                // 4. 各 MonoBehaviour ごとに _target missing 参照を順序で YAML と突き合わせて代入
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null)
                    {
                        continue;
                    }

                    var mbGid = GlobalObjectId.GetGlobalObjectIdSlow(mb);
                    var mbFileId = mbGid.targetObjectId;
                    if (!perMonoBehaviour.TryGetValue(mbFileId, out var yamlOrphans))
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
                            !iter.propertyPath.EndsWith("autoFocusModule._target"))
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

                        // YAML orphan を index で引いて m_PrefabInstance 経由で Button を特定
                        MonoBehaviour target = null;
                        var diagnostic = "";
                        if (missingIdx < yamlOrphans.Count)
                        {
                            var orphanFid = yamlOrphans[missingIdx];
                            if (orphanToPrefabInstance.TryGetValue(orphanFid, out var prefInstId) &&
                                prefabInstanceToButton.TryGetValue(prefInstId, out var matched))
                            {
                                target = matched;
                                diagnostic = $"orphan={orphanFid} prefInst={prefInstId}";
                            }
                            else
                            {
                                diagnostic = $"orphan={orphanFid} 未マッチ (prefInst not in map)";
                            }
                        }
                        else
                        {
                            diagnostic = $"YAML の orphan リスト({yamlOrphans.Count})とズレ (idx={missingIdx})";
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
                            Debug.LogWarning($"[Morn Migration] {assetPath}: MB fileId={mbFileId} {iter.propertyPath}: {diagnostic}");
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
                    Debug.LogWarning($"[Morn Migration] {assetPath}: {skippedCount}件の missing を自動修正できず。手動で Inspector から設定してください");
                }

                return skippedCount == 0;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {assetPath} の修正に失敗: {e.Message}");
                return false;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// YAML 内で `_autoFocusModule:` または `_cancelModule:` の子に `_isActive:` があるか、
        /// もしくは PrefabInstance modifications に該当パスがあるかを検出。
        /// </summary>
        private static bool HasIsActiveInAutoFocusOrCancelModule(string content)
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

                var moduleIndent = lines[i].Length - trimmed.Length;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (string.IsNullOrWhiteSpace(lines[j]))
                    {
                        continue;
                    }

                    var childTrimmed = lines[j].TrimStart();
                    var childIndent = lines[j].Length - childTrimmed.Length;
                    if (childIndent <= moduleIndent)
                    {
                        break;
                    }

                    if (childTrimmed.StartsWith("_isActive:"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// `_autoFocusModule:` / `_cancelModule:` ブロックと PrefabInstance の modifications から _isActive を削除。
        /// 返り値は削除件数。
        /// </summary>
        private static int RemoveIsActiveFromModules(ref string content)
        {
            var lines = content.Split('\n').ToList();
            var removed = 0;
            var targetModules = new[] { "_autoFocusModule:", "_cancelModule:" };

            // 1. Module ブロック内の _isActive 行を削除
            for (var i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].TrimStart();
                var isModuleStart = false;
                foreach (var m in targetModules)
                {
                    if (trimmed == m || trimmed.StartsWith(m))
                    {
                        isModuleStart = true;
                        break;
                    }
                }

                if (!isModuleStart)
                {
                    continue;
                }

                var moduleIndent = lines[i].Length - lines[i].TrimStart().Length;
                // 子フィールドは moduleIndent より深いインデント
                for (var j = i + 1; j < lines.Count; j++)
                {
                    var childTrimmed = lines[j].TrimStart();
                    if (string.IsNullOrWhiteSpace(lines[j]))
                    {
                        continue;
                    }

                    var childIndent = lines[j].Length - childTrimmed.Length;
                    if (childIndent <= moduleIndent)
                    {
                        break;
                    }

                    if (childTrimmed.StartsWith("_isActive:"))
                    {
                        lines.RemoveAt(j);
                        removed++;
                        j--;
                    }
                }
            }

            // 2. PrefabInstance の m_Modifications 内で _autoFocusModule._isActive / _cancelModule._isActive を削除
            //    "- target: {...}\n  propertyPath: ..._isActive\n  value: ...\n  objectReference: ..." を 4 行で削除
            for (var i = 0; i < lines.Count; i++)
            {
                if (!lines[i].TrimStart().StartsWith("- target:"))
                {
                    continue;
                }

                // 次の "propertyPath:" 行を探す (通常は i+1)
                if (i + 1 >= lines.Count)
                {
                    continue;
                }

                var pathLine = lines[i + 1].TrimStart();
                if (!pathLine.StartsWith("propertyPath:"))
                {
                    continue;
                }

                var isAutoFocus = pathLine.Contains("_autoFocusModule._isActive") ||
                                  pathLine.Contains(".autoFocusModule._isActive");
                var isCancel = pathLine.Contains("_cancelModule._isActive") ||
                               pathLine.Contains(".cancelModule._isActive");
                if (!isAutoFocus && !isCancel)
                {
                    continue;
                }

                // 対応する modification の行範囲を特定 (次の "- target:" または "m_RemovedComponents:" などまで)
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

        /// <summary>
        /// YAML から UnityEngine.UI.Button の stripped 孤立エントリを抽出し、fileID → m_PrefabInstance fileID を返す。
        /// </summary>
        private static void ParseOrphanStrippedEntries(string[] lines, Dictionary<ulong, ulong> result)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("--- !u!114 &") || !lines[i].Contains("stripped"))
                {
                    continue;
                }

                var fidStart = "--- !u!114 &".Length;
                var fidEnd = lines[i].IndexOf(' ', fidStart);
                if (fidEnd < 0)
                {
                    continue;
                }

                if (!ulong.TryParse(lines[i].Substring(fidStart, fidEnd - fidStart), out var strippedFid))
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
                    if (trimmed.StartsWith("m_PrefabInstance:") && trimmed.Contains("fileID: "))
                    {
                        var gs = trimmed.IndexOf("fileID: ", StringComparison.Ordinal) + 8;
                        var ge = trimmed.IndexOf('}', gs);
                        if (ge > gs)
                        {
                            ulong.TryParse(trimmed.Substring(gs, ge - gs), out prefInst);
                        }
                    }

                    if (trimmed.StartsWith("m_Script:") && trimmed.Contains("guid: "))
                    {
                        var gs = trimmed.IndexOf("guid: ", StringComparison.Ordinal) + 6;
                        var ge = trimmed.IndexOf(',', gs);
                        if (ge > gs)
                        {
                            scriptGuid = trimmed.Substring(gs, ge - gs);
                        }
                    }
                }

                if (scriptGuid == UnityButtonGuid && prefInst != 0)
                {
                    result[strippedFid] = prefInst;
                }
            }
        }

        /// <summary>
        /// 各 MonoBehaviour ブロック内の `_target: {fileID: X}` 参照を走査し、X が orphan に含まれれば順序付きリストに追加。
        /// </summary>
        private static void ParseTargetReferences(
            string[] lines,
            Dictionary<ulong, ulong> orphanSet,
            Dictionary<ulong, List<ulong>> result)
        {
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
                    else
                    {
                        var fidStart = "--- !u!114 &".Length;
                        var fidEnd = line.Length;
                        for (var k = fidStart; k < line.Length; k++)
                        {
                            if (!char.IsDigit(line[k]))
                            {
                                fidEnd = k;
                                break;
                            }
                        }

                        ulong.TryParse(line.Substring(fidStart, fidEnd - fidStart), out currentMbFileId);
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
                if (!trimmed.StartsWith("_target:") || !trimmed.Contains("fileID: "))
                {
                    continue;
                }

                var gs = trimmed.IndexOf("fileID: ", StringComparison.Ordinal) + 8;
                var ge = trimmed.IndexOf('}', gs);
                if (ge <= gs)
                {
                    continue;
                }

                if (!ulong.TryParse(trimmed.Substring(gs, ge - gs), out var targetFid))
                {
                    continue;
                }

                if (!orphanSet.ContainsKey(targetFid))
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
        }

        private static void RemoveComponentBlock(List<string> lines, string fileId, string scriptGuid)
        {
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (!lines[i].StartsWith($"--- !u!114 &{fileId}"))
                {
                    continue;
                }

                // このブロックの終端を探す
                var endLine = lines.Count - 1;
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (lines[j].StartsWith("--- "))
                    {
                        endLine = j - 1;
                        break;
                    }
                }

                // ブロック削除
                lines.RemoveRange(i, endLine - i + 1);
                break;
            }
        }

    }
}
#endif
