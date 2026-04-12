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
        private readonly List<ScanResult> _mergeResults = new();
        private readonly List<ScanResult> _missingScriptResults = new();
        private Vector2 _scrollPos;
        private bool _scanPrefabs = true;
        private bool _scanScenes = true;
        private bool _scanCs = true;
        private bool _foldRemap = true;
        private bool _foldDeleted = true;
        private bool _foldCs = true;
        private bool _foldFieldFix = true;
        private bool _foldMerge = true;
        private bool _foldMissingScript = true;

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

            // コンポーネント統合
            DrawMergeSection(
                ref _foldMerge,
                $"Button + MornUGUIButton 共存 ({_mergeResults.Count}件)",
                _mergeResults);

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
            _mergeResults.Clear();
            _missingScriptResults.Clear();

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
                        if (content.Contains("_buttonStateLinkSets") || content.Contains("_buttonModule:"))
                        {
                            _fieldFixResults.Add(new ScanResult
                            {
                                AssetPath = path,
                                Details = "ControlState: _buttonModule→_linkModule, フィールドリネーム",
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

            AssetDatabase.Refresh();
            Debug.Log($"[MornUGUI Migration] GUID リマップ完了: {modifiedFiles.Count} ファイルを更新");
            Scan(); // 再スキャン
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

            AssetDatabase.Refresh();
            Debug.Log($"[MornUGUI Migration] C# 置換完了: {modifiedCount} ファイルを更新");
            Scan(); // 再スキャン
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
                    AssetDatabase.Refresh();
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
                AssetDatabase.Refresh();
                Debug.Log($"[Morn Migration] {assetPath}: フィールド修正完了");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {assetPath} の修正に失敗: {e.Message}");
            }
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
                AssetDatabase.Refresh();
                Debug.Log($"[Morn Migration] {assetPath}: Button 統合 + 参照引き継ぎ完了");
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
