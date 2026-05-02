#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornUGUI / MornLocalize の Old → 新クラス移行を 1 ステップに統合。
    /// - 旧 GUID (MornUGUITextSetterOld / MornLocalizeFontOld / MornLocalizeButtonOld) を新 GUID に置換
    /// - MornUGUITextSetter のフィールド名 (Text/SizeSettings/FontSettings/MaterialType → _text/_sizeSettings/_fontSettings/_materialType) を MonoBehaviour ブロックで置換
    /// - PrefabInstance の m_Modifications の propertyPath は「private 版 + Inherited 版」の 2 modifications に複製し、親 (MornLocalizeButton) からの伝搬と自身の値の両方に同じ override を持たせる
    /// </summary>
    internal sealed class MornUGUILocalizeMigrationStep : MornMigrationStep
    {
        public override string Title => $"MornUGUI/Localize Old → 新 ({Results.Count}件)";
        public override Color HeaderColor => new(0.6f, 1f, 0.6f);

        /// <summary>旧 GUID → (新 GUID, 旧名, 新名)</summary>
        private static readonly Dictionary<string, (string newGuid, string oldName, string newName)> GuidRemap = new()
        {
            { "4e26e1a68b544d0f8fac57dfedc41450", ("3e5221d3bb1849d38d28458571bb2548", "MornUGUITextSetterOld", "MornUGUITextSetter") },
            { "84cd7484039147119679b73d79fce3ef", ("e86d3b40ea8f44df9e0cde946ab6453e", "MornLocalizeFontOld", "MornLocalizeFont") },
            { "1ef380cda7ff49ccbbbf023fb1f22e4a", ("e9bb9d729c14468aa470409b0dbc9ec8", "MornLocalizeButtonOld", "MornLocalizeButton") },
        };

        private const string TextSetterNewGuid = "3e5221d3bb1849d38d28458571bb2548";
        private const string LocalizeButtonNewGuid = "e9bb9d729c14468aa470409b0dbc9ec8";

        /// <summary>MonoBehaviour ブロック内のフィールド名リネーム (旧 → 新)</summary>
        private static readonly (string Old, string New)[] FieldRenames =
        {
            ("Text", "_text"),
            ("SizeSettings", "_sizeSettings"),
            ("FontSettings", "_fontSettings"),
            ("MaterialType", "_materialType"),
        };

        /// <summary>PrefabInstance 内の propertyPath を「private 版 + Inherited 版」で複製。
        /// PropagateToParent=true なら、同じ prefab 内の MornLocalizeButton (親) の同名フィールドにも伝搬。
        /// Text は m_Text 等と紛らわしいため除外。MaterialType は MornLocalizeButton に対応フィールド無しのため伝搬無し。</summary>
        private static readonly (string Old, string Private, string Inherited, bool PropagateToParent)[] PrefabInstanceDuplicates =
        {
            ("SizeSettings", "_sizeSettings", "InheritedSizeSettings", true),
            ("FontSettings", "_fontSettings", "InheritedFontSettings", true),
            ("MaterialType", "_materialType", "InheritedMaterialType", false),
        };

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            var hits = new List<string>();

            foreach (var kvp in GuidRemap)
            {
                if (file.Content.Contains(kvp.Key))
                {
                    hits.Add($"{kvp.Value.oldName} → {kvp.Value.newName}");
                }
            }

            foreach (var (oldName, _) in FieldRenames)
            {
                if (ContentContainsField(file.Content, oldName))
                {
                    hits.Add($"{oldName} → _{char.ToLower(oldName[0])}{oldName.Substring(1)}");
                }
            }

            foreach (var (oldName, _, _, _) in PrefabInstanceDuplicates)
            {
                if (ContentContainsPropertyPath(file.Content, oldName))
                {
                    hits.Add($"propertyPath {oldName} を private + Inherited に複製");
                }
            }

            // 既に新フィールド名 (_sizeSettings/_fontSettings) で書かれた MornUGUITextSetter override が
            // あるが、 親 MornLocalizeButton への伝搬がまだない prefab も対象にする。
            if (NeedsParentPropagation(file.Content))
            {
                hits.Add("MornLocalizeButton への propagate 補填");
            }

            if (hits.Count == 0)
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = string.Join("\n", new HashSet<string>(hits)),
            });
        }

        public override bool FixOne(MornMigrationResult result)
        {
            var content = MornMigrationUtil.SafeRead(result.AssetPath);
            if (content == null)
            {
                return false;
            }

            var modified = false;

            // 1. GUID 置換 (旧 → 新)
            foreach (var kvp in GuidRemap)
            {
                if (content.Contains(kvp.Key))
                {
                    content = content.Replace(kvp.Key, kvp.Value.newGuid);
                    modified = true;
                }
            }

            // 2. ブロック単位でフィールド名リネーム + PrefabInstance 複製
            var blocks = SplitBlocks(content);

            // (前段) ファイル内の MornUGUITextSetter 向け override 値を収集 (PrefabInstance ブロックを走査)。
            // 後段で、 同ファイル内に直接配置された MornLocalizeButton の _sizeSettings/_fontSettings が
            // {fileID: 0} (= None) のときに、 ここで集めた値で埋める。
            var fillCandidates = CollectFillCandidates(blocks);

            for (var i = 0; i < blocks.Length; i++)
            {
                var rewritten = blocks[i];

                if (ContainsScriptGuid(rewritten, TextSetterNewGuid))
                {
                    foreach (var (oldName, newName) in FieldRenames)
                    {
                        rewritten = ReplaceFieldName(rewritten, oldName, newName);
                    }
                }

                // 直接配置された MornLocalizeButton ブロックで _sizeSettings/_fontSettings が None なら埋める
                if (ContainsScriptGuid(rewritten, LocalizeButtonNewGuid))
                {
                    rewritten = FillNoneFields(rewritten, fillCandidates);
                }

                if (IsPrefabInstanceBlock(rewritten))
                {
                    var sourcePrefabGuid = ExtractSourcePrefabGuid(rewritten);
                    var localizeButtonAnchor = sourcePrefabGuid != null
                        ? ResolveLocalizeButtonAnchor(sourcePrefabGuid)
                        : null;
                    var textSetterAnchors = sourcePrefabGuid != null
                        ? ResolveTextSetterAnchors(sourcePrefabGuid)
                        : new List<string>();

                    foreach (var (oldName, privateName, inheritedName, propagate) in PrefabInstanceDuplicates)
                    {
                        // (a) 旧 oldName が残っていれば private + Inherited (+ propagate なら親) に複製
                        rewritten = DuplicatePropertyPath(
                            rewritten, oldName, privateName, inheritedName,
                            propagate ? localizeButtonAnchor : null);

                        // (b) 既に privateName が target=TextSetter に存在するが target=LocalizeButton には無い場合に親へ補填
                        if (propagate && localizeButtonAnchor != null)
                        {
                            rewritten = PropagateToLocalizeButton(
                                rewritten, privateName, localizeButtonAnchor, textSetterAnchors);
                        }
                    }
                }

                if (rewritten != blocks[i])
                {
                    blocks[i] = rewritten;
                    modified = true;
                }
            }

            if (modified)
            {
                content = string.Join("\n", blocks);
                if (!MornMigrationUtil.SafeWrite(result.AssetPath, content))
                {
                    return false;
                }

                Debug.Log($"[Morn Migration] {result.AssetPath}: MornUGUI/Localize 移行完了");
            }

            return true;
        }

        /// <summary>
        /// 既に新フィールド名 (_sizeSettings / _fontSettings) で書かれているが、
        /// 親 MornLocalizeButton (e9bb9d72) への伝搬がまだ無い場合 true。
        /// PropagateToLocalizeButton で補填するため、 ScanFile で対象に拾うフラグ。
        /// </summary>
        private static bool NeedsParentPropagation(string content)
        {
            // (1) ファイル内に MornLocalizeButton が直接配置されていて _sizeSettings/_fontSettings が None
            if (content.Contains($"guid: {LocalizeButtonNewGuid}")
                && (content.Contains("_sizeSettings: {fileID: 0}")
                    || content.Contains("_fontSettings: {fileID: 0}")))
            {
                return true;
            }

            // (2) PrefabInstance ブロック単位に走査し、 TextSetter override があるが
            //     LocalizeButton override が無い (= まだ親伝搬されていない) ものがあれば true
            if (!content.Contains("PrefabInstance:"))
            {
                return false;
            }

            foreach (var block in SplitBlocks(content))
            {
                if (!IsPrefabInstanceBlock(block))
                {
                    continue;
                }

                if (BlockNeedsPropagation(block))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>1 つの PrefabInstance ブロックで TextSetter override 有り & LocalizeButton override 無しなら true。</summary>
        private static bool BlockNeedsPropagation(string block)
        {
            var sourcePrefabGuid = ExtractSourcePrefabGuid(block);
            if (sourcePrefabGuid == null)
            {
                return false;
            }

            var localizeButtonAnchor = ResolveLocalizeButtonAnchor(sourcePrefabGuid);
            if (localizeButtonAnchor == null)
            {
                return false;
            }

            var textSetterAnchors = ResolveTextSetterAnchors(sourcePrefabGuid);
            if (textSetterAnchors.Count == 0)
            {
                return false;
            }

            foreach (var (_, privateName, _, propagate) in PrefabInstanceDuplicates)
            {
                if (!propagate)
                {
                    continue;
                }

                var hasOnTextSetter = false;
                foreach (var setterAnchor in textSetterAnchors)
                {
                    if (BlockHasOverride(block, setterAnchor, privateName))
                    {
                        hasOnTextSetter = true;
                        break;
                    }
                }

                if (!hasOnTextSetter)
                {
                    continue;
                }

                if (!BlockHasOverride(block, localizeButtonAnchor, privateName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool BlockHasOverride(string block, string anchor, string propertyName)
        {
            var pattern = @"target: \{fileID: " + Regex.Escape(anchor)
                                                + @"[^}]+\}\r?\n      propertyPath: "
                                                + Regex.Escape(propertyName) + @"\r?\n";
            return Regex.IsMatch(block, pattern);
        }

        private static bool ContentContainsField(string content, string fieldName)
        {
            return content.Contains($"\n  {fieldName}:");
        }

        private static bool ContentContainsPropertyPath(string content, string oldName)
        {
            return content.Contains($"propertyPath: {oldName}\n") || content.Contains($"propertyPath: {oldName}.");
        }

        private static bool ContainsScriptGuid(string block, string guid)
        {
            return block.Contains($"guid: {guid}");
        }

        private static bool IsPrefabInstanceBlock(string block)
        {
            return block.Contains("PrefabInstance:");
        }

        private static string ReplaceFieldName(string block, string oldName, string newName)
        {
            return block.Replace($"\n  {oldName}:", $"\n  {newName}:");
        }

        /// <summary>
        /// PrefabInstance の m_Modifications の 4 行 modification を 「private 版 + Inherited 版」 の 2 modifications に複製。
        /// 子プロパティパス (.subprop) も保持。
        /// localizeButtonAnchor が指定されかつ subProp が無い場合、 同 prefab の MornLocalizeButton の同名フィールドへも 1 modification を追加。
        /// </summary>
        private static string DuplicatePropertyPath(string block, string oldName, string privateName, string inheritedName,
            string localizeButtonAnchor)
        {
            var pattern = @"(    - target: \{fileID: )(\d+)(, guid: [^}]+\}\r?\n      propertyPath: )"
                          + Regex.Escape(oldName)
                          + @"(\.[^\r\n]+)?(\r?\n      value:[^\r\n]*\r?\n      objectReference: \{[^}]+\}\r?\n)";
            return Regex.Replace(block, pattern, m =>
            {
                var prefix = m.Groups[1].Value;
                var targetAnchor = m.Groups[2].Value;
                var middle = m.Groups[3].Value;
                var subProp = m.Groups[4].Success ? m.Groups[4].Value : string.Empty;
                var suffix = m.Groups[5].Value;

                var sb = new StringBuilder();
                sb.Append(prefix).Append(targetAnchor).Append(middle).Append(privateName).Append(subProp).Append(suffix);
                sb.Append(prefix).Append(targetAnchor).Append(middle).Append(inheritedName).Append(subProp).Append(suffix);
                if (subProp.Length == 0
                    && localizeButtonAnchor != null
                    && targetAnchor != localizeButtonAnchor)
                {
                    sb.Append(prefix).Append(localizeButtonAnchor).Append(middle).Append(privateName).Append(suffix);
                }

                return sb.ToString();
            });
        }

        private static string ExtractSourcePrefabGuid(string block)
        {
            var match = Regex.Match(block, @"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]+),");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// ファイル内の PrefabInstance ブロックを走査して、 propertyPath: <oldName> または <privateName> の
        /// objectReference 値を収集する。 同ファイル内 MornLocalizeButton の埋め直しに使う。
        /// </summary>
        private static Dictionary<string, string> CollectFillCandidates(string[] blocks)
        {
            var result = new Dictionary<string, string>();
            foreach (var block in blocks)
            {
                if (!IsPrefabInstanceBlock(block))
                {
                    continue;
                }

                foreach (var (oldName, privateName, _, propagate) in PrefabInstanceDuplicates)
                {
                    if (!propagate)
                    {
                        continue;
                    }

                    foreach (var name in new[] { oldName, privateName })
                    {
                        var pattern = @"propertyPath: " + Regex.Escape(name)
                                                       + @"\r?\n      value:[^\r\n]*\r?\n      objectReference: (\{[^}]+\})";
                        var match = Regex.Match(block, pattern);
                        if (match.Success && !result.ContainsKey(privateName))
                        {
                            result[privateName] = match.Groups[1].Value;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// MornLocalizeButton ブロックで _sizeSettings/_fontSettings が `{fileID: 0}` (= None) なら
        /// 同ファイル内 PrefabInstance から取得した値で埋める。
        /// </summary>
        private static string FillNoneFields(string block, Dictionary<string, string> fillCandidates)
        {
            foreach (var kvp in fillCandidates)
            {
                var fieldName = kvp.Key;
                var noneLine = $"  {fieldName}: {{fileID: 0}}";
                var filledLine = $"  {fieldName}: {kvp.Value}";
                if (block.Contains(noneLine))
                {
                    block = block.Replace(noneLine, filledLine);
                }
            }

            return block;
        }

        /// <summary>
        /// 既に新フィールド名 (privateName) で書かれている modification を target=TextSetter から読み取り、
        /// 同じ value を target=MornLocalizeButton に対応する modification としてまだ無ければ追加する。
        /// </summary>
        private static string PropagateToLocalizeButton(string block, string privateName, string localizeButtonAnchor,
            List<string> textSetterAnchors)
        {
            var existingPattern = @"    - target: \{fileID: " + Regex.Escape(localizeButtonAnchor)
                                  + @"[^}]+\}\r?\n      propertyPath: " + Regex.Escape(privateName) + @"\r?\n";
            if (Regex.IsMatch(block, existingPattern))
            {
                return block;
            }

            foreach (var setterAnchor in textSetterAnchors)
            {
                var pattern = @"(    - target: \{fileID: )" + Regex.Escape(setterAnchor)
                              + @"(, guid: [^}]+\}\r?\n      propertyPath: )"
                              + Regex.Escape(privateName)
                              + @"(\r?\n      value:[^\r\n]*\r?\n      objectReference: \{[^}]+\}\r?\n)";
                var match = Regex.Match(block, pattern);
                if (!match.Success)
                {
                    continue;
                }

                var prefix = match.Groups[1].Value;
                var middle = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;
                var addition = prefix + localizeButtonAnchor + middle + privateName + suffix;
                return block.Insert(match.Index + match.Length, addition);
            }

            return block;
        }

        private static List<string> ResolveTextSetterAnchors(string prefabGuid)
        {
            return ResolveAnchorsByScriptGuid(prefabGuid, TextSetterNewGuid);
        }

        private static List<string> ResolveAnchorsByScriptGuid(string prefabGuid, string scriptGuid)
        {
            var result = new List<string>();
            var path = AssetDatabase.GUIDToAssetPath(prefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                return result;
            }

            var content = MornMigrationUtil.SafeRead(path);
            if (content == null)
            {
                return result;
            }

            string currentAnchor = null;
            foreach (var line in content.Split('\n'))
            {
                if (line.StartsWith("--- "))
                {
                    var anchorMatch = Regex.Match(line, @"&(\d+)");
                    currentAnchor = anchorMatch.Success ? anchorMatch.Groups[1].Value : null;
                    continue;
                }

                if (currentAnchor != null && line.Contains($"guid: {scriptGuid}"))
                {
                    if (!result.Contains(currentAnchor))
                    {
                        result.Add(currentAnchor);
                    }

                    currentAnchor = null;
                }
            }

            return result;
        }

        /// <summary>指定 prefab guid の YAML を読み、最初に見つかった MornLocalizeButton の anchor を返す。</summary>
        private static string ResolveLocalizeButtonAnchor(string prefabGuid)
        {
            var path = AssetDatabase.GUIDToAssetPath(prefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var content = MornMigrationUtil.SafeRead(path);
            if (content == null)
            {
                return null;
            }

            string currentAnchor = null;
            foreach (var line in content.Split('\n'))
            {
                if (line.StartsWith("--- "))
                {
                    var anchorMatch = Regex.Match(line, @"&(\d+)");
                    currentAnchor = anchorMatch.Success ? anchorMatch.Groups[1].Value : null;
                    continue;
                }

                if (currentAnchor != null && line.Contains($"guid: {LocalizeButtonNewGuid}"))
                {
                    return currentAnchor;
                }
            }

            return null;
        }

        /// <summary>YAML を `---` 区切りでブロック分割。</summary>
        private static string[] SplitBlocks(string content)
        {
            var lines = content.Split('\n');
            var blocks = new List<string>();
            var current = new List<string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("--- ") && current.Count > 0)
                {
                    blocks.Add(string.Join("\n", current));
                    current.Clear();
                }

                current.Add(line);
            }

            if (current.Count > 0)
            {
                blocks.Add(string.Join("\n", current));
            }

            return blocks.ToArray();
        }
    }
}
#endif
