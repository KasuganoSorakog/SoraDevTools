using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Migrations
{
    /// <summary>
    /// Private builds do not auto-upgrade or rewrite stdio client configs on package version changes.
    /// </summary>
    [InitializeOnLoad]
    internal static class StdIoVersionMigration
    {
        private const string LastUpgradeKey = EditorPrefKeys.LastStdIoUpgradeVersion;

        static StdIoVersionMigration()
        {
            if (Application.isBatchMode)
                return;

            EditorApplication.delayCall += RunMigrationIfNeeded;
        }

        private static void RunMigrationIfNeeded()
        {
            EditorApplication.delayCall -= RunMigrationIfNeeded;

            string currentVersion = AssetPathUtility.GetPackageVersion();
            if (!string.IsNullOrEmpty(currentVersion))
            {
                try { EditorPrefs.SetString(LastUpgradeKey, currentVersion); } catch { }
            }
        }
    }
}
