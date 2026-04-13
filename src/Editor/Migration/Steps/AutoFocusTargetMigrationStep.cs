#if UNITY_EDITOR
using UnityEngine;

namespace MornLib
{
    /// <summary>MornUGUIControlState の _autoFocusModule._target に残った Button stripped 孤立参照を MornUGUIButton に修正。</summary>
    internal sealed class AutoFocusTargetMigrationStep : MissingTargetMigrationStepBase
    {
        public override string Title => $"AutoFocusModule._target missing ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.6f, 0.8f);
        protected override string ModuleName => "_autoFocusModule";
        protected override string[] LegacyTargetFieldNames => new[] { "_autoFocusTarget" };
    }
}
#endif
