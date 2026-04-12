#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

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

        // ========== フィールドリネーム (GUID リマップ後に適用) ==========
        // ObsoleteSubState → SubState: _instantiate==0 のとき _prefab の値を _instance に移す
        private static readonly string SubStateNewGuid = "aac67bf328824705a55354ac6d26608c";

        // ========== 削除された GUID (対応先なし) ==========
        private static readonly Dictionary<string, string> DeletedGuids = new()
        {
            { "e830bade52d747819e91153be1b80223", "MornUGUIButtonModuleBase (削除)" },
            { "e31b2f6049aa48499baee31c0cea8064", "MornUGUISliderModuleBase (削除)" },
            { "8be286109e114b849574ebd8390b6191", "MornUGUIButtonModule Arbor (削除)" },
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
        private Vector2 _scrollPos;
        private bool _scanPrefabs = true;
        private bool _scanScenes = true;
        private bool _scanCs = true;
        private bool _foldRemap = true;
        private bool _foldDeleted = true;
        private bool _foldCs = true;
        private bool _foldFieldFix = true;

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

            // C# 変更
            DrawSection(
                ref _foldCs,
                $"C# namespace/型名変更 ({_csResults.Count}件)",
                _csResults,
                new Color(0.6f, 0.8f, 1f));

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

                        // SubState フィールド不整合検出 (新GUID済みだが _prefab/_instance 不一致)
                        if (content.Contains(SubStateNewGuid) && HasSubStateFieldMismatch(content))
                        {
                            _fieldFixResults.Add(new ScanResult
                            {
                                AssetPath = path,
                                Details = "SubState: _prefab → _instance リネーム必要",
                            });
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
                        // SubState フィールドリネーム
                        if (content.Contains(SubStateNewGuid))
                        {
                            content = FixSubStateFields(content);
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
                    AssetDatabase.ImportAsset(result.AssetPath);
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
                File.WriteAllText(fullPath, content);
                AssetDatabase.ImportAsset(assetPath);
                Debug.Log($"[Morn Migration] {assetPath}: SubState フィールド修正完了");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Morn Migration] {assetPath} の修正に失敗: {e.Message}");
            }
        }
    }
}
#endif
