using System;
using System.IO;
using MCPForUnity.Editor.Constants;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Helper methods for managing HTTP endpoint URLs used by the MCP bridge.
    /// Ensures the stored value is always the base URL (without trailing path),
    /// and provides convenience accessors for specific endpoints.
    /// </summary>
    public static class HttpEndpointUtility
    {
        private const string PrefKey = EditorPrefKeys.HttpBaseUrl;
        private const string DefaultBaseUrl = "http://localhost:8080";

        /// <summary>
        /// Returns the normalized base URL currently stored for this project.
        /// </summary>
        public static string GetBaseUrl()
        {
            string stored = McpProjectSettings.GetHttpBaseUrl();
            return NormalizeBaseUrl(stored);
        }

        /// <summary>
        /// Saves a user-provided URL after normalizing it to a base form.
        /// </summary>
        public static void SaveBaseUrl(string userValue)
        {
            string normalized = NormalizeBaseUrl(userValue);
            McpProjectSettings.SetHttpBaseUrl(normalized);
            EditorPrefs.SetString(PrefKey, normalized);
        }

        /// <summary>
        /// Builds the JSON-RPC endpoint used by FastMCP clients (base + /mcp).
        /// </summary>
        public static string GetMcpRpcUrl()
        {
            return AppendPathSegment(GetBaseUrl(), "mcp");
        }

        /// <summary>
        /// Builds the endpoint used when POSTing custom-tool registration payloads.
        /// </summary>
        public static string GetRegisterToolsUrl()
        {
            return AppendPathSegment(GetBaseUrl(), "register-tools");
        }

        /// <summary>
        /// Normalizes a URL so that we consistently store just the base (no trailing slash/path).
        /// </summary>
        private static string NormalizeBaseUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultBaseUrl;
            }

            string trimmed = value.Trim();

            // Ensure scheme exists; default to http:// if user omitted it.
            if (!trimmed.Contains("://"))
            {
                trimmed = $"http://{trimmed}";
            }

            // Remove trailing slash segments.
            trimmed = trimmed.TrimEnd('/');

            // Strip trailing "/mcp" (case-insensitive) if provided.
            if (trimmed.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^4];
            }

            return trimmed;
        }

        private static string AppendPathSegment(string baseUrl, string segment)
        {
            return $"{baseUrl.TrimEnd('/')}/{segment}";
        }
    }

    /// <summary>
    /// Project-scoped MCP settings. EditorPrefs remain as migration/default fallback.
    /// Kept in this compiled helper file so Unity-generated project files pick it up immediately.
    /// </summary>
    internal sealed class McpProjectSettings
    {
        private const string SettingsPath = "ProjectSettings/MCPForUnitySettings.json";
        private const string DefaultBaseUrl = "http://localhost:8080";

        public bool UseHttpTransport = true;
        public string HttpTransportScope = "local";
        public string HttpBaseUrl = DefaultBaseUrl;
        public bool CentralServerEnabled = true;
        public bool CentralServerAutoStart = true;
        public bool CentralServerHideWindow = true;
        public string ServerSourceMode = "project-local";
        public string CustomServerSource = "";
        public bool AutoConnect = true;
        public string Language = "zh-CN";
        public string MigrationStatus = "";

        private static McpProjectSettings cached;

        private static string AbsoluteSettingsPath =>
            Path.Combine(ProjectRootPath, SettingsPath.Replace('/', Path.DirectorySeparatorChar));

        private static string ProjectRootPath
        {
            get
            {
                try
                {
                    return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                }
                catch
                {
                    return Directory.GetCurrentDirectory();
                }
            }
        }

        public static McpProjectSettings Load()
        {
            if (cached != null)
            {
                return cached;
            }

            try
            {
                string path = AbsoluteSettingsPath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    cached = JsonConvert.DeserializeObject<McpProjectSettings>(json) ?? new McpProjectSettings();
                    string previousMigrationStatus = cached.MigrationStatus;
                    cached.Normalize();
                    if (!string.Equals(previousMigrationStatus, cached.MigrationStatus, StringComparison.Ordinal))
                    {
                        cached.Save();
                    }
                    return cached;
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to read project MCP settings: {ex.Message}");
            }

            cached = CreateMigratedDefaults();
            cached.Save();
            return cached;
        }

        public void Save()
        {
            Normalize();
            cached = this;
            try
            {
                string path = AbsoluteSettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to write project MCP settings: {ex.Message}");
            }
        }

        public static bool GetUseHttpTransport()
        {
            return Load().UseHttpTransport;
        }

        public static void SetUseHttpTransport(bool value)
        {
            var settings = Load();
            settings.UseHttpTransport = value;
            settings.Save();
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, value);
        }

        public static string GetHttpTransportScope()
        {
            return Load().HttpTransportScope;
        }

        public static void SetHttpTransportScope(string value)
        {
            var settings = Load();
            settings.HttpTransportScope = NormalizeScope(value);
            settings.Save();
            EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, settings.HttpTransportScope);
        }

        public static string GetHttpBaseUrl()
        {
            return NormalizeBaseUrl(Load().HttpBaseUrl);
        }

        public static void SetHttpBaseUrl(string value)
        {
            var settings = Load();
            settings.HttpBaseUrl = NormalizeBaseUrl(value);
            settings.Save();
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, settings.HttpBaseUrl);
        }

        public static bool ShouldHideServerWindow()
        {
            return Load().CentralServerHideWindow;
        }

        public static string GetServerPackageSource(Func<string> fallbackFactory)
        {
            var settings = Load();
            string mode = (settings.ServerSourceMode ?? "").Trim().ToLowerInvariant();

            if (mode == "custom" && !string.IsNullOrWhiteSpace(settings.CustomServerSource))
            {
                return settings.CustomServerSource.Trim();
            }

            if (mode == "project-local")
            {
                string local = GetProjectLocalServerPath();
                if (!string.IsNullOrEmpty(local) && File.Exists(Path.Combine(local, "pyproject.toml")))
                {
                    return local;
                }
            }

            return fallbackFactory?.Invoke();
        }

        public static string GetProjectLocalServerPath()
        {
            string path = Path.Combine(ProjectRootPath, "Tools", "MCPForUnityServer");
            return Directory.Exists(path) ? path : null;
        }

        private static McpProjectSettings CreateMigratedDefaults()
        {
            var settings = new McpProjectSettings();
            try
            {
                settings.UseHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
                settings.HttpTransportScope = NormalizeScope(EditorPrefs.GetString(EditorPrefKeys.HttpTransportScope, "local"));
                settings.HttpBaseUrl = NormalizeBaseUrl(EditorPrefs.GetString(EditorPrefKeys.HttpBaseUrl, DefaultBaseUrl));

                string overrideSource = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
                if (!string.IsNullOrWhiteSpace(overrideSource))
                {
                    settings.ServerSourceMode = "custom";
                    settings.CustomServerSource = overrideSource.Trim();
                }
                else if (!string.IsNullOrEmpty(GetProjectLocalServerPath()))
                {
                    settings.ServerSourceMode = "project-local";
                }
                else
                {
                    settings.ServerSourceMode = "project-local";
                }

                settings.MigrationStatus = "Created with private project-local defaults.";
            }
            catch (Exception ex)
            {
                settings.MigrationStatus = $"Created with defaults after migration read failed: {ex.Message}";
            }

            settings.Normalize();
            return settings;
        }

        private void Normalize()
        {
            HttpTransportScope = NormalizeScope(HttpTransportScope);
            HttpBaseUrl = NormalizeBaseUrl(HttpBaseUrl);
            ServerSourceMode = string.IsNullOrWhiteSpace(ServerSourceMode) ? "project-local" : ServerSourceMode.Trim();
            if (string.Equals(ServerSourceMode, "pypi-pinned", StringComparison.OrdinalIgnoreCase))
            {
                ServerSourceMode = "project-local";
            }
            CustomServerSource = CustomServerSource?.Trim() ?? "";
            Language = string.IsNullOrWhiteSpace(Language) ? "zh-CN" : Language.Trim();
            ApplyPrivateDefaultMigration();
        }

        private void ApplyPrivateDefaultMigration()
        {
            bool legacyGeneratedSettings =
                string.IsNullOrWhiteSpace(MigrationStatus) ||
                string.Equals(MigrationStatus, "Created from EditorPrefs/defaults.", StringComparison.Ordinal);

            bool projectLocalPrivateServer =
                string.Equals(ServerSourceMode, "project-local", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(GetProjectLocalServerPath());

            if (!legacyGeneratedSettings || !projectLocalPrivateServer)
            {
                return;
            }

            CentralServerEnabled = true;
            CentralServerAutoStart = true;
            CentralServerHideWindow = true;
            AutoConnect = true;
            if (string.IsNullOrWhiteSpace(ServerSourceMode))
            {
                ServerSourceMode = "project-local";
            }
            MigrationStatus = "Upgraded to private project-local defaults.";
        }

        private static string NormalizeScope(string value)
        {
            return string.Equals(value, "remote", StringComparison.OrdinalIgnoreCase) ? "remote" : "local";
        }

        private static string NormalizeBaseUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultBaseUrl;
            }

            string trimmed = value.Trim();
            if (!trimmed.Contains("://"))
            {
                trimmed = $"http://{trimmed}";
            }

            trimmed = trimmed.TrimEnd('/');
            if (trimmed.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^4];
            }

            return trimmed;
        }
    }
}
