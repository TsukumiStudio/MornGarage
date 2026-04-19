#if UNITY_EDITOR
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornSpreadSheetMaster の SerializeField 名変更:
    /// _getSheetNameApiUrl → _apiUrl
    /// </summary>
    internal sealed class SpreadSheetMasterApiUrlMigrationStep : MornMigrationStep
    {
        private const string OldField = "_getSheetNameApiUrl:";
        private const string NewField = "_apiUrl:";
        public override string Title => $"MornSpreadSheetMaster _getSheetNameApiUrl→_apiUrl ({Results.Count}件)";
        public override Color HeaderColor => new(0.6f, 1f, 0.8f);

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs)
            {
                return;
            }

            if (!file.Content.Contains($"guid: {MornMigrationUtil.MornSpreadSheetMasterGuid}"))
            {
                return;
            }

            if (!ContainsOldField(file.Content))
            {
                return;
            }

            Results.Add(new MornMigrationResult
            {
                AssetPath = file.AssetPath,
                Details = $"{OldField} → {NewField}",
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

            Debug.Log($"[Morn Migration] {result.AssetPath}: {OldField} → {NewField}");
            return true;
        }

        private static bool ContainsOldField(string content)
        {
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains($"guid: {MornMigrationUtil.MornSpreadSheetMasterGuid}"))
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

                    if (trimmed.StartsWith(OldField))
                    {
                        return true;
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
                if (!lines[i].Contains($"guid: {MornMigrationUtil.MornSpreadSheetMasterGuid}"))
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

                    if (trimmed.StartsWith(OldField))
                    {
                        var indent = lines[j].Substring(0, lines[j].Length - trimmed.Length);
                        var rest = trimmed.Substring(OldField.Length);
                        lines[j] = $"{indent}{NewField}{rest}";
                        modified = true;
                        break;
                    }
                }
            }

            return modified ? string.Join('\n', lines) : content;
        }
    }
}
#endif
