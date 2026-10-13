using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Migrations
{
    /// <summary>
    /// Clears legacy embedded-server preferences without rewriting private client configs.
    /// </summary>
    [InitializeOnLoad]
    internal static class LegacyServerSrcMigration
    {
        private const string ServerSrcKey = EditorPrefKeys.ServerSrc;
        private const string UseEmbeddedKey = EditorPrefKeys.UseEmbeddedServer;

        static LegacyServerSrcMigration()
        {
            if (Application.isBatchMode)
                return;

            EditorApplication.delayCall += RunMigrationIfNeeded;
        }

        private static void RunMigrationIfNeeded()
        {
            EditorApplication.delayCall -= RunMigrationIfNeeded;

            bool hasServerSrc = EditorPrefs.HasKey(ServerSrcKey);
            bool hasUseEmbedded = EditorPrefs.HasKey(UseEmbeddedKey);

            if (!hasServerSrc && !hasUseEmbedded)
            {
                return;
            }

            if (hasServerSrc)
            {
                EditorPrefs.DeleteKey(ServerSrcKey);
                McpLog.Info("Removed legacy key: MCPForUnity.ServerSrc");
            }

            if (hasUseEmbedded)
            {
                EditorPrefs.DeleteKey(UseEmbeddedKey);
                McpLog.Info("Removed legacy key: MCPForUnity.UseEmbeddedServer");
            }
        }
    }
}
