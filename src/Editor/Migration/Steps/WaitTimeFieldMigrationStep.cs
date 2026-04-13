#if UNITY_EDITOR
using UnityEngine;

namespace MornLib
{
    /// <summary>WaitTimeState の _waitDuration が FlexibleField&lt;float&gt; 形式なら float にフラット化。</summary>
    internal sealed class WaitTimeFieldMigrationStep : MornMigrationStep
    {
        public override string Title => $"WaitTimeState 不整合 ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.6f, 0.8f);

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            if (!file.Content.Contains($"guid: {MornMigrationUtil.WaitTimeStateGuid}"))
            {
                return;
            }

            if (!HasFlexibleField(file.Content))
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = "_waitDuration FlexibleField<float> → float",
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

            Debug.Log($"[Morn Migration] {result.AssetPath}: WaitTimeState 修正完了");
            return true;
        }

        private static bool HasFlexibleField(string content)
        {
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains($"guid: {MornMigrationUtil.WaitTimeStateGuid}"))
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

        private static string ApplyFix(string content)
        {
            var lines = content.Split('\n');
            var modified = false;
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains($"guid: {MornMigrationUtil.WaitTimeStateGuid}"))
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

                    if (!trimmed.StartsWith("_waitDuration:"))
                    {
                        continue;
                    }

                    // _waitDuration: の次の数行から _Value または _Constant を抽出
                    var indent = lines[j].Substring(0, lines[j].Length - trimmed.Length);
                    var floatValue = "0";
                    var removeEnd = j + 1;
                    for (var k = j + 1; k < lines.Length; k++)
                    {
                        var subTrim = lines[k].TrimStart();
                        if (subTrim.StartsWith("--- !u!"))
                        {
                            break;
                        }

                        // 次のフィールド (例: _next:) が来たら終わり
                        var subIndent = MornMigrationUtil.IndentOf(lines[k]);
                        if (subIndent <= MornMigrationUtil.IndentOf(lines[j]) && subTrim.Length > 0 && !subTrim.StartsWith("#"))
                        {
                            removeEnd = k;
                            break;
                        }

                        if (subTrim.StartsWith("_Value:") || subTrim.StartsWith("_Constant:"))
                        {
                            var colon = subTrim.IndexOf(':');
                            if (colon >= 0)
                            {
                                floatValue = subTrim.Substring(colon + 1).Trim();
                            }
                        }

                        removeEnd = k + 1;
                    }

                    lines[j] = $"{indent}_waitDuration: {floatValue}";
                    // j+1 から removeEnd 未満を削除
                    var linesList = new System.Collections.Generic.List<string>(lines);
                    linesList.RemoveRange(j + 1, removeEnd - (j + 1));
                    lines = linesList.ToArray();
                    modified = true;
                    Debug.Log($"[Morn Migration] WaitTimeState _waitDuration = {floatValue}");
                    break;
                }
            }

            return modified ? string.Join('\n', lines) : content;
        }
    }
}
#endif
