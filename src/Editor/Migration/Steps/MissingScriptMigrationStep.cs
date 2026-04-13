#if UNITY_EDITOR
using System;
using UnityEngine;

namespace MornLib
{
    /// <summary>プロジェクト内に存在しない script GUID (Missing Script) を検出。</summary>
    internal sealed class MissingScriptMigrationStep : MornMigrationStep
    {
        public override string Title => $"Missing Script ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.4f, 0.4f);
        public override bool ReadOnly => true;

        public override void ScanFile(MornMigrationFile file, MornMigrationContext ctx)
        {
            if (file.IsCs || ctx.KnownScriptGuids.Count == 0)
            {
                return;
            }

            var content = file.Content;
            const string needle = "m_Script: {fileID: 11500000, guid: ";
            var idx = 0;
            while ((idx = content.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                var gStart = idx + needle.Length;
                var gEnd = content.IndexOf(',', gStart);
                if (gEnd > gStart)
                {
                    var scriptGuid = content.Substring(gStart, gEnd - gStart);
                    if (!ctx.KnownScriptGuids.Contains(scriptGuid))
                    {
                        Results.Add(new MornMigrationResult
                        {
                            AssetPath = file.AssetPath,
                            Details = $"Missing script: {scriptGuid}",
                            Payload = scriptGuid,
                        });
                    }
                }

                idx = gEnd > 0 ? gEnd : idx + 1;
            }
        }
    }
}
#endif
