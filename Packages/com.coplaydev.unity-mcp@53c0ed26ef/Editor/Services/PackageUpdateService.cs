using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Private-build update service. Remote update checks are disabled for this customized package.
    /// </summary>
    public class PackageUpdateService : IPackageUpdateService
    {
        private const string LastCheckDateKey = EditorPrefKeys.LastUpdateCheck;
        private const string CachedVersionKey = EditorPrefKeys.LatestKnownVersion;

        /// <inheritdoc/>
        public UpdateCheckResult CheckForUpdate(string currentVersion)
        {
            return new UpdateCheckResult
            {
                CheckSucceeded = true,
                LatestVersion = currentVersion,
                UpdateAvailable = false,
                Message = "私有定制版已关闭远端版本检查。"
            };
        }

        /// <inheritdoc/>
        public bool IsNewerVersion(string version1, string version2)
        {
            try
            {
                // Remove any "v" prefix
                version1 = version1.TrimStart('v', 'V');
                version2 = version2.TrimStart('v', 'V');

                var version1Parts = version1.Split('.');
                var version2Parts = version2.Split('.');

                for (int i = 0; i < Math.Min(version1Parts.Length, version2Parts.Length); i++)
                {
                    if (int.TryParse(version1Parts[i], out int v1Num) &&
                        int.TryParse(version2Parts[i], out int v2Num))
                    {
                        if (v1Num > v2Num) return true;
                        if (v1Num < v2Num) return false;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool IsGitInstallation()
        {
            string packageRoot = AssetPathUtility.GetMcpPackageRootPath();
            return !string.IsNullOrEmpty(packageRoot) &&
                   packageRoot.StartsWith("Packages/", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public void ClearCache()
        {
            EditorPrefs.DeleteKey(LastCheckDateKey);
            EditorPrefs.DeleteKey(CachedVersionKey);
        }
    }
}
