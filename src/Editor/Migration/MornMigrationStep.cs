#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// Migration 1ステップの基底クラス。新しい移行を追加する時はこれを継承。
    /// </summary>
    internal abstract class MornMigrationStep
    {
        /// <summary>UIに表示するセクション名。</summary>
        public abstract string Title { get; }

        /// <summary>セクションヘッダー色。</summary>
        public virtual Color HeaderColor => new(0.8f, 0.8f, 1f);

        /// <summary>prefab/scene などのアセットをスキャンするか。</summary>
        public virtual bool ScansAssets => true;

        /// <summary>C# ソースをスキャンするか。</summary>
        public virtual bool ScansCs => false;

        /// <summary>検出のみでFixは提供しない場合 true。</summary>
        public virtual bool ReadOnly => false;

        /// <summary>スキャン結果一覧。</summary>
        public List<MornMigrationResult> Results { get; } = new();

        /// <summary>UI開閉状態。</summary>
        public bool Foldout = true;

        public virtual void ClearResults()
        {
            Results.Clear();
        }

        /// <summary>1 ファイルをスキャンし、該当すれば Results に追加する。</summary>
        public abstract void ScanFile(MornMigrationFile file, MornMigrationContext ctx);

        /// <summary>1 件を修正。成功したら true (Results から除去される)。</summary>
        public virtual bool FixOne(MornMigrationResult result)
        {
            return false;
        }

        /// <summary>全件一括修正 (デフォルト実装は FixOne の繰り返し)。</summary>
        public virtual int FixAll()
        {
            var count = 0;
            for (var i = Results.Count - 1; i >= 0; i--)
            {
                if (FixOne(Results[i]))
                {
                    Results.RemoveAt(i);
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>スキャン結果 1 件。</summary>
    internal sealed class MornMigrationResult
    {
        public string AssetPath;
        public string Details;
        /// <summary>Step 固有の任意データ。</summary>
        public object Payload;
    }

    /// <summary>スキャン対象の 1 ファイル。</summary>
    internal sealed class MornMigrationFile
    {
        public string AssetPath;
        public string Content;
        public bool IsCs;
    }

    /// <summary>スキャン実行時の共有情報。</summary>
    internal sealed class MornMigrationContext
    {
        /// <summary>プロジェクト内に存在する全 script GUID (Missing 判定用)。</summary>
        public HashSet<string> KnownScriptGuids = new();
    }
}
#endif
