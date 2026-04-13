#if UNITY_EDITOR
using UnityEngine;

namespace MornLib
{
    /// <summary>MornUGUIControlState の _cancelModule._target に残った Button stripped 孤立参照を MornUGUIButton に修正。</summary>
    internal sealed class CancelTargetMigrationStep : MissingTargetMigrationStepBase
    {
        public override string Title => $"CancelModule._target missing ({Results.Count}件)";
        public override Color HeaderColor => new(1f, 0.5f, 0.9f);
        protected override string ModuleName => "_cancelModule";
        protected override string[] LegacyTargetFieldNames => new[] { "_cancelTarget" };
    }
}
#endif
