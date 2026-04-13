#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornLib 系の移行作業を束ねる EditorWindow。
    /// 新しい移行を追加したい場合は <see cref="MornMigrationStep"/> を継承して
    /// <see cref="_steps"/> に追加するだけ。
    /// </summary>
    public sealed class MornMigrationWindow : EditorWindow
    {
        private readonly List<MornMigrationStep> _steps = new()
        {
            new GuidRemapMigrationStep(),
            new DeletedGuidMigrationStep(),
            new ControlStateFieldMigrationStep(),
            new SubStateFieldMigrationStep(),
            new WaitTimeFieldMigrationStep(),
            new ButtonMergeMigrationStep(),
            new AutoFocusTargetMigrationStep(),
            new CancelTargetMigrationStep(),
            new CsReplaceMigrationStep(),
            new MissingScriptMigrationStep(),
        };

        private Vector2 _scrollPos;
        private bool _scanPrefabs = true;
        private bool _scanScenes = true;
        private bool _scanCs = true;

        [MenuItem("Tools/Morn Migration Tool")]
        private static void Open()
        {
            GetWindow<MornMigrationWindow>("Morn Migration");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Morn Migration Tool", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

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

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            foreach (var step in _steps)
            {
                DrawStep(step);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            if (GUILayout.Button("レポート出力 (Console)"))
            {
                PrintReport();
            }
        }

        private void Scan()
        {
            foreach (var s in _steps)
            {
                s.ClearResults();
            }

            var ctx = new MornMigrationContext
            {
                KnownScriptGuids = CollectKnownScriptGuids(),
            };

            // prefab/scene スキャン
            if (_scanPrefabs || _scanScenes)
            {
                var anyNeedsAssets = _steps.Any(s => s.ScansAssets);
                if (anyNeedsAssets)
                {
                    var assetPaths = new List<string>();
                    if (_scanPrefabs)
                    {
                        assetPaths.AddRange(AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath));
                    }

                    if (_scanScenes)
                    {
                        assetPaths.AddRange(AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath));
                    }

                    for (var i = 0; i < assetPaths.Count; i++)
                    {
                        var path = assetPaths[i];
                        EditorUtility.DisplayProgressBar("Morn Migration スキャン", path, (float)i / assetPaths.Count);

                        try
                        {
                            var content = File.ReadAllText(path);
                            var file = new MornMigrationFile
                            {
                                AssetPath = path,
                                Content = content,
                                IsCs = false,
                            };
                            foreach (var step in _steps)
                            {
                                if (step.ScansAssets)
                                {
                                    step.ScanFile(file, ctx);
                                }
                            }
                        }
                        catch
                        {
                            // バイナリ等はスキップ
                        }
                    }

                    EditorUtility.ClearProgressBar();
                }
            }

            // C# スキャン
            if (_scanCs && _steps.Any(s => s.ScansCs))
            {
                var csFiles = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);
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
                        var file = new MornMigrationFile
                        {
                            AssetPath = relativePath,
                            Content = content,
                            IsCs = true,
                        };
                        foreach (var step in _steps)
                        {
                            if (step.ScansCs)
                            {
                                step.ScanFile(file, ctx);
                            }
                        }
                    }
                    catch
                    {
                        // スキップ
                    }
                }
            }

            var summary = string.Join(", ", _steps.Select(s => $"{s.GetType().Name.Replace("MigrationStep", "")}={s.Results.Count}"));
            Debug.Log($"[Morn Migration] スキャン完了: {summary}");
        }

        private static HashSet<string> CollectKnownScriptGuids()
        {
            var known = new HashSet<string>();
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var searchDirs = new[]
            {
                Application.dataPath,
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
                        if (guidIdx < 0)
                        {
                            continue;
                        }

                        var start = guidIdx + 6;
                        var end = metaContent.IndexOfAny(new[] { '\n', '\r', ' ', '\t' }, start);
                        if (end < 0)
                        {
                            end = metaContent.Length;
                        }

                        if (end > start)
                        {
                            known.Add(metaContent.Substring(start, end - start).Trim());
                        }
                    }
                    catch
                    {
                        // skip
                    }
                }
            }

            return known;
        }

        private static void DrawStep(MornMigrationStep step)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = step.HeaderColor;
            step.Foldout = EditorGUILayout.Foldout(step.Foldout, step.Title, true, EditorStyles.foldoutHeader);
            GUI.backgroundColor = originalColor;

            if (!step.Foldout)
            {
                return;
            }

            if (step.Results.Count == 0)
            {
                EditorGUILayout.LabelField("  (なし)", EditorStyles.miniLabel);
                return;
            }

            if (!step.ReadOnly && step.Results.Count > 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"全て修正 ({step.Results.Count}件)", GUILayout.Height(22)))
                    {
                        if (EditorUtility.DisplayDialog(
                                step.Title,
                                $"{step.Results.Count} 件を一括で処理します。\n事前に git commit を推奨。",
                                "実行",
                                "キャンセル"))
                        {
                            step.FixAll();
                        }
                    }
                }
            }

            EditorGUI.indentLevel++;
            for (var i = step.Results.Count - 1; i >= 0; i--)
            {
                var result = step.Results[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            Path.GetFileName(result.AssetPath),
                            EditorStyles.linkLabel,
                            GUILayout.ExpandWidth(false)))
                    {
                        MornMigrationUtil.Ping(result.AssetPath);
                    }

                    EditorGUILayout.LabelField(result.Details, EditorStyles.miniLabel);

                    if (!step.ReadOnly)
                    {
                        if (GUILayout.Button("修正", GUILayout.Width(40)))
                        {
                            if (step.FixOne(result))
                            {
                                step.Results.RemoveAt(i);
                            }
                        }
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        private void PrintReport()
        {
            var report = "=== Morn Migration Report ===\n\n";
            foreach (var step in _steps)
            {
                report += $"■ {step.Title}\n";
                foreach (var r in step.Results)
                {
                    report += $"  {r.AssetPath}\n    {r.Details}\n";
                }

                report += "\n";
            }

            Debug.Log(report);
        }
    }
}
#endif
