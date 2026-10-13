using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Clients
{
    /// <summary>Shared base class for MCP configurators.</summary>
    public abstract class McpClientConfiguratorBase : IMcpClientConfigurator
    {
        protected readonly McpClient client;

        protected McpClientConfiguratorBase(McpClient client)
        {
            this.client = client;
        }

        internal McpClient Client => client;

        public string Id => client.name.Replace(" ", "").ToLowerInvariant();
        public virtual string DisplayName => client.name;
        public McpStatus Status => client.status;
        public virtual bool SupportsAutoConfigure => true;
        public virtual string GetConfigureActionLabel() => "配置";

        public abstract string GetConfigPath();
        public abstract McpStatus CheckStatus(bool attemptAutoRewrite = true);
        public abstract void Configure();
        public abstract string GetManualSnippet();
        public abstract IList<string> GetInstallationSteps();

        protected string GetUvxPathOrError()
        {
            string uvx = MCPServiceLocator.Paths.GetUvxPath();
            if (string.IsNullOrEmpty(uvx))
            {
                throw new InvalidOperationException("未找到 uv/uvx。请先安装 uv，或在高级设置中手动指定 uvx 路径。");
            }
            return uvx;
        }

        protected string CurrentOsPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return client.windowsConfigPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return client.macConfigPath;
            return client.linuxConfigPath;
        }

        protected bool UrlsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            if (Uri.TryCreate(a.Trim(), UriKind.Absolute, out var uriA) &&
                Uri.TryCreate(b.Trim(), UriKind.Absolute, out var uriB))
            {
                return Uri.Compare(
                           uriA,
                           uriB,
                           UriComponents.HttpRequestUrl,
                           UriFormat.SafeUnescaped,
                           StringComparison.OrdinalIgnoreCase) == 0;
            }

            string Normalize(string value) => value.Trim().TrimEnd('/');
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>JSON-file based configurator (Cursor, Windsurf, VS Code, etc.).</summary>
    public abstract class JsonFileMcpConfigurator : McpClientConfiguratorBase
    {
        public JsonFileMcpConfigurator(McpClient client) : base(client) { }

        public override string GetConfigPath() => CurrentOsPath();

        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    return client.status;
                }

                string configJson = File.ReadAllText(path);
                string[] args = null;
                string configuredUrl = null;
                bool configExists = false;

                if (client.IsVsCodeLayout)
                {
                    var vsConfig = JsonConvert.DeserializeObject<JToken>(configJson) as JObject;
                    if (vsConfig != null)
                    {
                        var unityToken =
                            vsConfig["servers"]?["unityMCP"]
                            ?? vsConfig["mcp"]?["servers"]?["unityMCP"];

                        if (unityToken is JObject unityObj)
                        {
                            configExists = true;

                            var argsToken = unityObj["args"];
                            if (argsToken is JArray)
                            {
                                args = argsToken.ToObject<string[]>();
                            }

                            var urlToken = unityObj["url"] ?? unityObj["serverUrl"];
                            if (urlToken != null && urlToken.Type != JTokenType.Null)
                            {
                                configuredUrl = urlToken.ToString();
                            }
                        }
                    }
                }
                else
                {
                    McpConfig standardConfig = JsonConvert.DeserializeObject<McpConfig>(configJson);
                    if (standardConfig?.mcpServers?.unityMCP != null)
                    {
                        args = standardConfig.mcpServers.unityMCP.args;
                        configExists = true;
                    }
                }

                if (!configExists)
                {
                    client.SetStatus(McpStatus.MissingConfig);
                    return client.status;
                }

                bool matches = false;
                if (args != null && args.Length > 0)
                {
                    string expectedUvxUrl = AssetPathUtility.GetMcpServerPackageSource();
                    string configuredUvxUrl = McpConfigurationHelper.ExtractUvxUrl(args);
                    matches = !string.IsNullOrEmpty(configuredUvxUrl) &&
                              McpConfigurationHelper.PathsEqual(configuredUvxUrl, expectedUvxUrl);
                }
                else if (!string.IsNullOrEmpty(configuredUrl))
                {
                    string expectedUrl = HttpEndpointUtility.GetMcpRpcUrl();
                    matches = UrlsEqual(configuredUrl, expectedUrl);
                }

                if (matches)
                {
                    client.SetStatus(McpStatus.Configured);
                    return client.status;
                }

                if (attemptAutoRewrite)
                {
                    var result = McpConfigurationHelper.WriteMcpConfiguration(path, client);
                    if (result == "Configured successfully")
                    {
                        client.SetStatus(McpStatus.Configured);
                    }
                    else
                    {
                        client.SetStatus(McpStatus.IncorrectPath);
                    }
                }
                else
                {
                    client.SetStatus(McpStatus.IncorrectPath);
                }
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
            }

            return client.status;
        }

        public override void Configure()
        {
            string path = GetConfigPath();
            McpConfigurationHelper.EnsureConfigDirectoryExists(path);
            string result = McpConfigurationHelper.WriteMcpConfiguration(path, client);
            if (result == "Configured successfully")
            {
                client.SetStatus(McpStatus.Configured);
            }
            else
            {
                throw new InvalidOperationException(result);
            }
        }

        public override string GetManualSnippet()
        {
            try
            {
                string uvx = GetUvxPathOrError();
                return ConfigJsonBuilder.BuildManualConfigJson(uvx, client);
            }
            catch (Exception ex)
            {
                var errorObj = new { error = ex.Message };
                return JsonConvert.SerializeObject(errorObj);
            }
        }

        public override IList<string> GetInstallationSteps() => new List<string> { "当前客户端没有可用的配置步骤。" };
    }

    /// <summary>Codex (TOML) configurator.</summary>
    public abstract class CodexMcpConfigurator : McpClientConfiguratorBase
    {
        public CodexMcpConfigurator(McpClient client) : base(client) { }

        public override string GetConfigPath() => CurrentOsPath();

        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    return client.status;
                }

                string toml = File.ReadAllText(path);
                if (CodexConfigHelper.TryParseCodexServer(toml, out _, out var args, out var url))
                {
                    bool matches = false;
                    if (!string.IsNullOrEmpty(url))
                    {
                        matches = UrlsEqual(url, HttpEndpointUtility.GetMcpRpcUrl());
                    }
                    else if (args != null && args.Length > 0)
                    {
                        string expected = AssetPathUtility.GetMcpServerPackageSource();
                        string configured = McpConfigurationHelper.ExtractUvxUrl(args);
                        matches = !string.IsNullOrEmpty(configured) &&
                                  McpConfigurationHelper.PathsEqual(configured, expected);
                    }

                    if (matches)
                    {
                        client.SetStatus(McpStatus.Configured);
                        return client.status;
                    }
                }

                if (attemptAutoRewrite)
                {
                    string result = McpConfigurationHelper.ConfigureCodexClient(path, client);
                    if (result == "Configured successfully")
                    {
                        client.SetStatus(McpStatus.Configured);
                    }
                    else
                    {
                        client.SetStatus(McpStatus.IncorrectPath);
                    }
                }
                else
                {
                    client.SetStatus(McpStatus.IncorrectPath);
                }
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
            }

            return client.status;
        }

        public override void Configure()
        {
            string path = GetConfigPath();
            McpConfigurationHelper.EnsureConfigDirectoryExists(path);
            string result = McpConfigurationHelper.ConfigureCodexClient(path, client);
            if (result == "Configured successfully")
            {
                client.SetStatus(McpStatus.Configured);
            }
            else
            {
                throw new InvalidOperationException(result);
            }
        }

        public override string GetManualSnippet()
        {
            try
            {
                string uvx = GetUvxPathOrError();
                return CodexConfigHelper.BuildCodexServerBlock(uvx);
            }
            catch (Exception ex)
            {
                return $"# error: {ex.Message}";
            }
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "运行 'codex config edit'，或打开上方配置文件路径。",
            "粘贴 TOML 配置内容。",
            "保存后重启 Codex。"
        };
    }

    /// <summary>CLI-based configurator (Claude Code).</summary>
    public abstract class ClaudeCliMcpConfigurator : McpClientConfiguratorBase
    {
        public ClaudeCliMcpConfigurator(McpClient client) : base(client) { }

        public override bool SupportsAutoConfigure => true;
        public override string GetConfigureActionLabel() => client.status == McpStatus.Configured ? "取消注册" : "注册";

        public override string GetConfigPath() => "Managed via Claude CLI";

        /// <summary>
        /// Checks the Claude CLI registration status.
        /// MUST be called from the main Unity thread due to EditorPrefs and Application.dataPath access.
        /// </summary>
        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            // Capture main-thread-only values before delegating to thread-safe method
            string projectDir = Path.GetDirectoryName(Application.dataPath);
            bool useHttpTransport = McpProjectSettings.GetUseHttpTransport();
            // Resolve claudePath on the main thread (EditorPrefs access)
            string claudePath = MCPServiceLocator.Paths.GetClaudeCliPath();
            return CheckStatusWithProjectDir(projectDir, useHttpTransport, claudePath, attemptAutoRewrite);
        }

        /// <summary>
        /// Internal thread-safe version of CheckStatus.
        /// Can be called from background threads because all main-thread-only values are passed as parameters.
        /// projectDir, useHttpTransport, and claudePath are REQUIRED (non-nullable) to enforce thread safety at compile time.
        /// </summary>
        internal McpStatus CheckStatusWithProjectDir(string projectDir, bool useHttpTransport, string claudePath, bool attemptAutoRewrite = true)
        {
            try
            {
                if (string.IsNullOrEmpty(claudePath))
                {
                    client.SetStatus(McpStatus.NotConfigured, "Claude CLI not found");
                    return client.status;
                }

                // projectDir is required - no fallback to Application.dataPath
                if (string.IsNullOrEmpty(projectDir))
                {
                    throw new ArgumentNullException(nameof(projectDir), "Project directory must be provided for thread-safe execution");
                }

                string pathPrepend = null;
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    pathPrepend = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
                }
                else if (Application.platform == RuntimePlatform.LinuxEditor)
                {
                    pathPrepend = "/usr/local/bin:/usr/bin:/bin";
                }

                try
                {
                    string claudeDir = Path.GetDirectoryName(claudePath);
                    if (!string.IsNullOrEmpty(claudeDir))
                    {
                        pathPrepend = string.IsNullOrEmpty(pathPrepend)
                            ? claudeDir
                            : $"{claudeDir}:{pathPrepend}";
                    }
                }
                catch { }

                // Check if UnityMCP exists
                if (ExecPath.TryRun(claudePath, "mcp list", projectDir, out var listStdout, out var listStderr, 10000, pathPrepend))
                {
                    if (!string.IsNullOrEmpty(listStdout) && listStdout.IndexOf("UnityMCP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // UnityMCP is registered - now verify transport mode matches
                        // useHttpTransport parameter is required (non-nullable) to ensure thread safety
                        bool currentUseHttp = useHttpTransport;

                        // Get detailed info about the registration to check transport type
                        if (ExecPath.TryRun(claudePath, "mcp get UnityMCP", projectDir, out var getStdout, out var getStderr, 7000, pathPrepend))
                        {
                            // Parse the output to determine registered transport mode
                            // The CLI output format contains "Type: http" or "Type: stdio"
                            bool registeredWithHttp = getStdout.Contains("Type: http", StringComparison.OrdinalIgnoreCase);
                            bool registeredWithStdio = getStdout.Contains("Type: stdio", StringComparison.OrdinalIgnoreCase);

                            // Check for transport mismatch
                            if ((currentUseHttp && registeredWithStdio) || (!currentUseHttp && registeredWithHttp))
                            {
                                string registeredTransport = registeredWithHttp ? "HTTP" : "stdio";
                                string currentTransport = currentUseHttp ? "HTTP" : "stdio";
                                string errorMsg = $"连接方式不一致：Claude Code 已注册为 {registeredTransport}，当前设置为 {currentTransport}。请点击“配置”重新注册。";
                                client.SetStatus(McpStatus.Error, errorMsg);
                                McpLog.Warn(errorMsg);
                                return client.status;
                            }
                        }

                        client.SetStatus(McpStatus.Configured);
                        return client.status;
                    }
                }

                client.SetStatus(McpStatus.NotConfigured);
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
            }

            return client.status;
        }

        public override void Configure()
        {
            if (client.status == McpStatus.Configured)
            {
                Unregister();
            }
            else
            {
                Register();
            }
        }

        private void Register()
        {
            var pathService = MCPServiceLocator.Paths;
            string claudePath = pathService.GetClaudeCliPath();
            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("未找到 Claude CLI。请先安装 Claude Code，或在界面中手动指定路径。");
            }

            bool useHttpTransport = McpProjectSettings.GetUseHttpTransport();

            string args;
            if (useHttpTransport)
            {
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                args = $"mcp add --transport http UnityMCP {httpUrl}";
            }
            else
            {
                var (uvxPath, gitUrl, packageName) = AssetPathUtility.GetUvxCommandParts();
                if (!AssetPathUtility.TryGetPrivateServerSource(out gitUrl, out var sourceError))
                {
                    throw new InvalidOperationException(sourceError);
                }
                args = $"mcp add --transport stdio UnityMCP -- \"{uvxPath}\" --from \"{gitUrl}\" {packageName}";
            }

            string projectDir = Path.GetDirectoryName(Application.dataPath);

            string pathPrepend = null;
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                pathPrepend = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
            }
            else if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                pathPrepend = "/usr/local/bin:/usr/bin:/bin";
            }

            try
            {
                string claudeDir = Path.GetDirectoryName(claudePath);
                if (!string.IsNullOrEmpty(claudeDir))
                {
                    pathPrepend = string.IsNullOrEmpty(pathPrepend)
                        ? claudeDir
                        : $"{claudeDir}:{pathPrepend}";
                }
            }
            catch { }

            // Check if UnityMCP already exists and remove it first to ensure clean registration
            // This ensures we always use the current transport mode setting
            bool serverExists = ExecPath.TryRun(claudePath, "mcp get UnityMCP", projectDir, out _, out _, 7000, pathPrepend);
            if (serverExists)
            {
                McpLog.Info("检测到已有 UnityMCP 注册，正在移除旧配置以更新连接方式。");
                if (!ExecPath.TryRun(claudePath, "mcp remove UnityMCP", projectDir, out var removeStdout, out var removeStderr, 10000, pathPrepend))
                {
                    McpLog.Warn($"移除旧 UnityMCP 注册失败: {removeStderr}。将继续尝试重新注册。");
                }
            }

            // Now add the registration with the current transport mode
            if (!ExecPath.TryRun(claudePath, args, projectDir, out var stdout, out var stderr, 15000, pathPrepend))
            {
                throw new InvalidOperationException($"注册到 Claude Code 失败:\n{stderr}\n{stdout}");
            }

            McpLog.Info($"已使用 {(useHttpTransport ? "HTTP" : "stdio")} 连接方式注册到 Claude Code。");

            // Set status to Configured immediately after successful registration
            // The UI will trigger an async verification check separately to avoid blocking
            client.SetStatus(McpStatus.Configured);
        }

        private void Unregister()
        {
            var pathService = MCPServiceLocator.Paths;
            string claudePath = pathService.GetClaudeCliPath();

            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("未找到 Claude CLI。请先安装 Claude Code，或在界面中手动指定路径。");
            }

            string projectDir = Path.GetDirectoryName(Application.dataPath);
            string pathPrepend = null;
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                pathPrepend = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
            }
            else if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                pathPrepend = "/usr/local/bin:/usr/bin:/bin";
            }

            bool serverExists = ExecPath.TryRun(claudePath, "mcp get UnityMCP", projectDir, out _, out _, 7000, pathPrepend);

            if (!serverExists)
            {
                client.SetStatus(McpStatus.NotConfigured);
                McpLog.Info("未找到 MCP for Unity 注册项，当前已是未注册状态。");
                return;
            }

            if (ExecPath.TryRun(claudePath, "mcp remove UnityMCP", projectDir, out var stdout, out var stderr, 10000, pathPrepend))
            {
                McpLog.Info("已从 Claude Code 取消注册 MCP Server。");
            }
            else
            {
                throw new InvalidOperationException($"取消注册失败: {stderr}");
            }

            client.SetStatus(McpStatus.NotConfigured);
            // Status is already set - no need for blocking CheckStatus() call
        }

        public override string GetManualSnippet()
        {
            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            bool useHttpTransport = McpProjectSettings.GetUseHttpTransport();

            if (useHttpTransport)
            {
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                return "# 注册 UnityMCP 到 Claude Code:\n" +
                       $"claude mcp add --transport http UnityMCP {httpUrl}\n\n" +
                       "# 取消注册 UnityMCP:\n" +
                       "claude mcp remove UnityMCP\n\n" +
                       "# 查看已注册 Server:\n" +
                       "claude mcp list # 需要在项目目录中运行";
            }

            if (string.IsNullOrEmpty(uvxPath))
            {
                return "# 错误: 当前无法生成配置，请检查高级设置中的路径。";
            }

            string packageSource = AssetPathUtility.GetMcpServerPackageSource();
            if (!AssetPathUtility.TryGetPrivateServerSource(out packageSource, out var sourceError))
            {
                return $"# 错误: {sourceError}";
            }
            return "# 注册 UnityMCP 到 Claude Code:\n" +
                   $"claude mcp add --transport stdio UnityMCP -- \"{uvxPath}\" --from \"{packageSource}\" mcp-for-unity\n\n" +
                   "# 移除 UnityMCP:\n" +
                   "claude mcp remove UnityMCP\n\n" +
                   "# 查看已注册 Server:\n" +
                   "claude mcp list # 需要在项目目录中运行";
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "确认 Claude CLI 已安装。",
            "使用“配置”按钮注册 UnityMCP，或复制上方命令手动执行。",
            "重启 Claude Code 或重新打开当前项目。"
        };
    }
}
