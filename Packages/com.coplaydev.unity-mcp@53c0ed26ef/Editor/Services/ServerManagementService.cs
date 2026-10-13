using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for managing MCP server lifecycle
    /// </summary>
    public class ServerManagementService : IServerManagementService
    {
        private static readonly HashSet<int> LoggedStopDiagnosticsPids = new HashSet<int>();
        private static readonly List<System.Diagnostics.Process> HiddenServerProcesses = new List<System.Diagnostics.Process>();
        private static readonly object HiddenServerLogLock = new object();
        private static readonly string[] RequiredServerPythonModules =
        {
            "fastmcp",
            "mcp",
            "pydantic",
            "fastapi",
            "uvicorn",
            "httpx"
        };

        private static string GetProjectRootPath()
        {
            try
            {
                // Application.dataPath is ".../<Project>/Assets"
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
            catch
            {
                return Application.dataPath;
            }
        }

        private static string QuoteIfNeeded(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.IndexOf(' ') >= 0 ? $"\"{s}\"" : s;
        }

        private static string QuoteArgument(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            return $"\"{s.Replace("\"", "\\\"")}\"";
        }

        private static string NormalizeForMatch(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c)) continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static void ClearLocalServerPidTracking()
        {
            try { EditorPrefs.DeleteKey(EditorPrefKeys.LastLocalHttpServerPid); } catch { }
            try { EditorPrefs.DeleteKey(EditorPrefKeys.LastLocalHttpServerPort); } catch { }
            try { EditorPrefs.DeleteKey(EditorPrefKeys.LastLocalHttpServerStartedUtc); } catch { }
            try { EditorPrefs.DeleteKey(EditorPrefKeys.LastLocalHttpServerPidArgsHash); } catch { }
            try { EditorPrefs.DeleteKey(EditorPrefKeys.LastLocalHttpServerPidFilePath); } catch { }
            try { EditorPrefs.DeleteKey(EditorPrefKeys.LastLocalHttpServerInstanceToken); } catch { }
        }

        private static void StoreLocalHttpServerHandshake(string pidFilePath, string instanceToken)
        {
            try
            {
                if (!string.IsNullOrEmpty(pidFilePath))
                {
                    EditorPrefs.SetString(EditorPrefKeys.LastLocalHttpServerPidFilePath, pidFilePath);
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(instanceToken))
                {
                    EditorPrefs.SetString(EditorPrefKeys.LastLocalHttpServerInstanceToken, instanceToken);
                }
            }
            catch { }
        }

        private static bool TryGetLocalHttpServerHandshake(out string pidFilePath, out string instanceToken)
        {
            pidFilePath = null;
            instanceToken = null;
            try
            {
                pidFilePath = EditorPrefs.GetString(EditorPrefKeys.LastLocalHttpServerPidFilePath, string.Empty);
                instanceToken = EditorPrefs.GetString(EditorPrefKeys.LastLocalHttpServerInstanceToken, string.Empty);
                if (string.IsNullOrEmpty(pidFilePath) || string.IsNullOrEmpty(instanceToken))
                {
                    pidFilePath = null;
                    instanceToken = null;
                    return false;
                }
                return true;
            }
            catch
            {
                pidFilePath = null;
                instanceToken = null;
                return false;
            }
        }

        private static string GetLocalHttpServerPidDirectory()
        {
            // Keep it project-scoped and out of version control.
            return Path.Combine(GetProjectRootPath(), "Library", "MCPForUnity", "RunState");
        }

        private static string GetLocalHttpServerPidFilePath(int port)
        {
            string dir = GetLocalHttpServerPidDirectory();
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"mcp_http_{port}.pid");
        }

        private static bool TryReadPidFromPidFile(string pidFilePath, out int pid)
        {
            pid = 0;
            try
            {
                if (string.IsNullOrEmpty(pidFilePath) || !File.Exists(pidFilePath))
                {
                    return false;
                }

                string text = File.ReadAllText(pidFilePath).Trim();
                if (int.TryParse(text, out pid))
                {
                    return pid > 0;
                }

                // Best-effort: tolerate accidental extra whitespace/newlines.
                var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (int.TryParse(firstLine, out pid))
                {
                    return pid > 0;
                }

                pid = 0;
                return false;
            }
            catch
            {
                pid = 0;
                return false;
            }
        }

        private bool TryProcessCommandLineContainsInstanceToken(int pid, string instanceToken, out bool containsToken)
        {
            containsToken = false;
            if (pid <= 0 || string.IsNullOrEmpty(instanceToken))
            {
                return false;
            }

            try
            {
                string tokenNeedle = instanceToken.ToLowerInvariant();

                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Query full command line so we can validate token (reduces PID reuse risk).
                    // Use CIM via PowerShell (wmic is deprecated).
                    string ps = $"(Get-CimInstance Win32_Process -Filter \\\"ProcessId={pid}\\\").CommandLine";
                    bool ok = ExecPath.TryRun("powershell", $"-NoProfile -Command \"{ps}\"", Application.dataPath, out var stdout, out var stderr, 5000);
                    string combined = ((stdout ?? string.Empty) + "\n" + (stderr ?? string.Empty)).ToLowerInvariant();
                    containsToken = combined.Contains(tokenNeedle);
                    return ok;
                }

                if (TryGetUnixProcessArgs(pid, out var argsLowerNow))
                {
                    containsToken = argsLowerNow.Contains(NormalizeForMatch(tokenNeedle));
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static void StoreLocalServerPidTracking(int pid, int port, string argsHash = null)
        {
            try { EditorPrefs.SetInt(EditorPrefKeys.LastLocalHttpServerPid, pid); } catch { }
            try { EditorPrefs.SetInt(EditorPrefKeys.LastLocalHttpServerPort, port); } catch { }
            try { EditorPrefs.SetString(EditorPrefKeys.LastLocalHttpServerStartedUtc, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)); } catch { }
            try
            {
                if (!string.IsNullOrEmpty(argsHash))
                {
                    EditorPrefs.SetString(EditorPrefKeys.LastLocalHttpServerPidArgsHash, argsHash);
                }
                else
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.LastLocalHttpServerPidArgsHash);
                }
            }
            catch { }
        }

        private static string ComputeShortHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            try
            {
                using var sha = SHA256.Create();
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = sha.ComputeHash(bytes);
                // 8 bytes => 16 hex chars is plenty as a stable fingerprint for our purposes.
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8 && i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryGetStoredLocalServerPid(int expectedPort, out int pid)
        {
            pid = 0;
            try
            {
                int storedPid = EditorPrefs.GetInt(EditorPrefKeys.LastLocalHttpServerPid, 0);
                int storedPort = EditorPrefs.GetInt(EditorPrefKeys.LastLocalHttpServerPort, 0);
                string storedUtc = EditorPrefs.GetString(EditorPrefKeys.LastLocalHttpServerStartedUtc, string.Empty);

                if (storedPid <= 0 || storedPort != expectedPort)
                {
                    return false;
                }

                // Only trust the stored PID for a short window to avoid PID reuse issues.
                // (We still verify the PID is listening on the expected port before killing.)
                if (!string.IsNullOrEmpty(storedUtc)
                    && DateTime.TryParse(storedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var startedAt))
                {
                    if ((DateTime.UtcNow - startedAt) > TimeSpan.FromHours(6))
                    {
                        return false;
                    }
                }

                pid = storedPid;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clear the local uvx cache for the MCP server package
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool ClearUvxCache()
        {
            try
            {
                string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
                string uvCommand = BuildUvPathFromUvx(uvxPath);

                // Get the package name
                string packageName = "mcp-for-unity";

                // Run uvx cache clean command
                string args = $"cache clean {packageName}";

                bool success;
                string stdout;
                string stderr;

                success = ExecuteUvCommand(uvCommand, args, out stdout, out stderr);

                if (success)
                {
                    McpLog.Debug($"uv cache cleared successfully: {stdout}");
                    return true;
                }
                string combinedOutput = string.Join(
                    Environment.NewLine,
                    new[] { stderr, stdout }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

                string lockHint = (!string.IsNullOrEmpty(combinedOutput) &&
                                   combinedOutput.IndexOf("currently in-use", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? "Another uv process may be holding the cache lock; wait a moment and try again or clear with '--force' from a terminal."
                    : string.Empty;

                if (string.IsNullOrEmpty(combinedOutput))
                {
                    combinedOutput = "Command failed with no output. Ensure uv is installed, on PATH, or set an override in Advanced Settings.";
                }

                McpLog.Error(
                    $"Failed to clear uv cache using '{uvCommand} {args}'. " +
                    $"Details: {combinedOutput}{(string.IsNullOrEmpty(lockHint) ? string.Empty : " Hint: " + lockHint)}");
                return false;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error clearing uv cache: {ex.Message}");
                return false;
            }
        }

        private bool ExecuteUvCommand(string uvCommand, string args, out string stdout, out string stderr)
        {
            stdout = null;
            stderr = null;

            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            string uvPath = BuildUvPathFromUvx(uvxPath);

            if (!string.Equals(uvCommand, uvPath, StringComparison.OrdinalIgnoreCase))
            {
                return ExecPath.TryRun(uvCommand, args, Application.dataPath, out stdout, out stderr, 30000);
            }

            string command = $"{uvPath} {args}";
            string extraPathPrepend = GetPlatformSpecificPathPrepend();

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return ExecPath.TryRun("cmd.exe", $"/c {command}", Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
            }

            string shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";

            if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            {
                string escaped = command.Replace("\"", "\\\"");
                return ExecPath.TryRun(shell, $"-lc \"{escaped}\"", Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
            }

            return ExecPath.TryRun(uvPath, args, Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
        }

        private static string BuildUvPathFromUvx(string uvxPath)
        {
            if (string.IsNullOrWhiteSpace(uvxPath))
            {
                return uvxPath;
            }

            string directory = Path.GetDirectoryName(uvxPath);
            string extension = Path.GetExtension(uvxPath);
            string uvFileName = "uv" + extension;

            return string.IsNullOrEmpty(directory)
                ? uvFileName
                : Path.Combine(directory, uvFileName);
        }

        private string GetPlatformSpecificPathPrepend()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    "/opt/homebrew/bin",
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin"
                });
            }

            if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin"
                });
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    !string.IsNullOrEmpty(localAppData) ? Path.Combine(localAppData, "Programs", "uv") : null,
                    !string.IsNullOrEmpty(programFiles) ? Path.Combine(programFiles, "uv") : null
                }.Where(p => !string.IsNullOrEmpty(p)).ToArray());
            }

            return null;
        }

        public bool StartLocalHttpServer()
        {
            return StartLocalHttpServerInternal(quiet: false);
        }

        public bool StartLocalHttpServerQuiet()
        {
            return StartLocalHttpServerInternal(quiet: true);
        }

        /// <summary>
        /// Start the local HTTP server in a separate terminal window or hidden process.
        /// </summary>
        private bool StartLocalHttpServerInternal(bool quiet)
        {
            // Central-server mode: if a compatible local MCP server is already running,
            // reuse it before touching the shared runtime. This prevents a second Unity
            // project from overwriting the runtime while the first server process is active.
            if (IsLocalHttpServerRunning())
            {
                McpLog.Info("中心 MCP HTTP Server 已运行，复用现有 Server。");
                return true;
            }

            /// Clean stale Python build artifacts when using a local dev server path
            AssetPathUtility.CleanLocalServerBuildArtifacts();

            if (!TryGetLocalHttpServerCommandParts(
                    out var fileName,
                    out var arguments,
                    out var displayCommand,
                    out var error))
            {
                if (!quiet)
                {
                    EditorUtility.DisplayDialog(
                        "无法启动中心 MCP Server",
                        error ?? "当前配置无法生成 Server 启动命令。",
                        "确定");
                }
                else
                {
                    McpLog.Warn(error ?? "当前配置无法生成 Server 启动命令。");
                }
                return false;
            }

            if (!TryEnsureSharedRuntimeDependencies(
                    fileName,
                    GetSharedServerRuntimeDirectory(),
                    allowOnlineInitialize: !quiet,
                    out error))
            {
                if (!quiet)
                {
                    EditorUtility.DisplayDialog("Server Runtime 未就绪", error, "确定");
                }
                else
                {
                    McpLog.Warn(error);
                }
                return false;
            }

            // If the port is occupied by a non-MCP process, don't start and explain why.
            try
            {
                string httpUrl = HttpEndpointUtility.GetBaseUrl();
                if (Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
                {
                    var remaining = GetListeningProcessIdsForPort(uri.Port);
                    if (remaining.Count > 0)
                    {
                        string message =
                            $"无法启动中心 MCP Server，因为端口 {uri.Port} 已被以下 PID 占用：" +
                            $"{string.Join(", ", remaining)}\n\n" +
                            "MCP For Unity 不会终止无关进程。请先停止占用端口的进程，或修改 HTTP URL。";
                        if (!quiet)
                        {
                            EditorUtility.DisplayDialog("端口已被占用", message, "确定");
                        }
                        else
                        {
                            McpLog.Warn(message);
                        }
                        return false;
                    }
                }
            }
            catch { }

            // Create a per-launch token + pidfile path so Stop can be deterministic without relying on port/PID heuristics.
            string baseUrlForPid = HttpEndpointUtility.GetBaseUrl();
            Uri.TryCreate(baseUrlForPid, UriKind.Absolute, out var uriForPid);
            int portForPid = uriForPid?.Port ?? 0;
            string instanceToken = Guid.NewGuid().ToString("N");
            string pidFilePath = portForPid > 0 ? GetLocalHttpServerPidFilePath(portForPid) : null;

            string launchCommand = displayCommand;
            if (!string.IsNullOrEmpty(pidFilePath))
            {
                launchCommand = $"{displayCommand} --pidfile {QuoteIfNeeded(pidFilePath)} --unity-instance-token {instanceToken}";
            }

            bool hideServerWindow = quiet || McpProjectSettings.ShouldHideServerWindow();
            bool shouldStart = hideServerWindow || EditorUtility.DisplayDialog(
                "启动本地 HTTP Server",
                $"即将以 HTTP 模式启动中心 MCP Server：\n\n{launchCommand}\n\n" +
                "是否继续？",
                "启动 Server",
                "取消");

            if (shouldStart)
            {
                try
                {
                    // Clear any stale handshake state from prior launches.
                    ClearLocalServerPidTracking();

                    // Best-effort: delete stale pidfile if it exists.
                    try
                    {
                        if (!string.IsNullOrEmpty(pidFilePath) && File.Exists(pidFilePath))
                        {
                            File.Delete(pidFilePath);
                        }
                    }
                    catch { }

                    string launchArguments = arguments;
                    if (!string.IsNullOrEmpty(pidFilePath))
                    {
                        launchArguments = $"{arguments} --pidfile {QuoteIfNeeded(pidFilePath)} --unity-instance-token {instanceToken}";
                    }

                    var startInfo = hideServerWindow
                        ? CreateHiddenProcessStartInfo(fileName, launchArguments, launchCommand)
                        : CreateTerminalProcessStartInfo(launchCommand);
                    var process = System.Diagnostics.Process.Start(startInfo);
                    if (hideServerWindow)
                    {
                        AttachHiddenServerLoggers(process);
                    }
                    if (!string.IsNullOrEmpty(pidFilePath))
                    {
                        StoreLocalHttpServerHandshake(pidFilePath, instanceToken);
                    }
                    if (process != null && portForPid > 0)
                    {
                        StoreLocalServerPidTracking(process.Id, portForPid, ComputeShortHash(launchCommand));
                    }
                    McpLog.Info($"已启动本地 HTTP Server: {launchCommand}");
                    return true;
                }
                catch (Exception ex)
                {
                    McpLog.Error($"启动中心 MCP Server 失败: {ex.Message}");
                    if (!quiet)
                    {
                        EditorUtility.DisplayDialog(
                            "启动失败",
                            $"中心 MCP Server 启动失败：{ex.Message}",
                            "确定");
                    }
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Stop the local HTTP server by finding the process listening on the configured port
        /// </summary>
        public bool StopLocalHttpServer()
        {
            return StopLocalHttpServerInternal(quiet: false);
        }

        public bool StopManagedLocalHttpServer()
        {
            if (!TryGetLocalHttpServerHandshake(out var pidFilePath, out _))
            {
                return false;
            }

            int port = 0;
            if (!TryGetPortFromPidFilePath(pidFilePath, out port) || port <= 0)
            {
                string baseUrl = HttpEndpointUtility.GetBaseUrl();
                if (IsLocalUrl(baseUrl)
                    && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                    && uri.Port > 0)
                {
                    port = uri.Port;
                }
            }

            if (port <= 0)
            {
                return false;
            }

            return StopLocalHttpServerInternal(quiet: true, portOverride: port, allowNonLocalUrl: true);
        }

        public bool IsLocalHttpServerRunning()
        {
            try
            {
                string httpUrl = HttpEndpointUtility.GetBaseUrl();
                if (!IsLocalUrl(httpUrl))
                {
                    return false;
                }

                if (!Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri) || uri.Port <= 0)
                {
                    return false;
                }

                int port = uri.Port;

                // Handshake path: if we have a pidfile+token and the PID is still the listener, treat as running.
                if (TryGetLocalHttpServerHandshake(out var pidFilePath, out var instanceToken)
                    && TryReadPidFromPidFile(pidFilePath, out var pidFromFile)
                    && pidFromFile > 0)
                {
                    var pidsNow = GetListeningProcessIdsForPort(port);
                    if (pidsNow.Contains(pidFromFile))
                    {
                        return true;
                    }
                }

                var pids = GetListeningProcessIdsForPort(port);
                if (pids.Count == 0)
                {
                    return false;
                }

                // Strong signal: stored PID is still the listener.
                if (TryGetStoredLocalServerPid(port, out int storedPid) && storedPid > 0)
                {
                    if (pids.Contains(storedPid))
                    {
                        return true;
                    }
                }

                // Best-effort: if anything listening looks like our server, treat as running.
                foreach (var pid in pids)
                {
                    if (pid <= 0) continue;
                    if (LooksLikeMcpServerProcess(pid))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool StopLocalHttpServerInternal(bool quiet, int? portOverride = null, bool allowNonLocalUrl = false)
        {
            string httpUrl = HttpEndpointUtility.GetBaseUrl();
            if (!allowNonLocalUrl && !IsLocalUrl(httpUrl))
            {
                if (!quiet)
                {
                    McpLog.Warn("Cannot stop server: URL is not local.");
                }
                return false;
            }

            try
            {
                int port = 0;
                if (portOverride.HasValue)
                {
                    port = portOverride.Value;
                }
                else
                {
                    var uri = new Uri(httpUrl);
                    port = uri.Port;
                }

                if (port <= 0)
                {
                    if (!quiet)
                    {
                        McpLog.Warn("Cannot stop server: Invalid port.");
                    }
                    return false;
                }

                // Guardrails:
                // - Never terminate the Unity Editor process.
                // - Only terminate processes that look like the MCP server (uv/uvx/python running mcp-for-unity).
                // This prevents accidental termination of unrelated services (including Unity itself).
                int unityPid = GetCurrentProcessIdSafe();
                bool stoppedAny = false;

                // Preferred deterministic stop path: if we have a pidfile+token from a Unity-managed launch,
                // validate and terminate exactly that PID.
                if (TryGetLocalHttpServerHandshake(out var pidFilePath, out var instanceToken))
                {
                    // Prefer deterministic stop when Unity started the server (pidfile+token).
                    // If the pidfile isn't available yet (fast quit after start), we can optionally fall back
                    // to port-based heuristics when a port override was supplied (managed-stop path).
                    if (!TryReadPidFromPidFile(pidFilePath, out var pidFromFile) || pidFromFile <= 0)
                    {
                        if (!portOverride.HasValue)
                        {
                            if (!quiet)
                            {
                                McpLog.Warn(
                                    $"Cannot stop local HTTP server on port {port}: pidfile not available yet at '{pidFilePath}'. " +
                                    "If you just started the server, wait a moment and try again.");
                            }
                            return false;
                        }

                        // Managed-stop fallback: proceed with port-based heuristics below.
                        // We intentionally do NOT clear handshake state here; it will be cleared if we successfully
                        // stop a server process and/or the port is freed.
                    }
                    else
                    {
                        // Never kill Unity/Hub.
                        if (unityPid > 0 && pidFromFile == unityPid)
                        {
                            if (!quiet)
                            {
                                McpLog.Warn($"Refusing to stop port {port}: pidfile PID {pidFromFile} is the Unity Editor process.");
                            }
                        }
                        else
                        {
                            var listeners = GetListeningProcessIdsForPort(port);
                            if (listeners.Count == 0)
                            {
                                // Nothing is listening anymore; clear stale handshake state.
                                try { File.Delete(pidFilePath); } catch { }
                                ClearLocalServerPidTracking();
                                if (!quiet)
                                {
                                    McpLog.Info($"No process found listening on port {port}");
                                }
                                return false;
                            }
                            bool pidIsListener = listeners.Contains(pidFromFile);
                            bool tokenQueryOk = TryProcessCommandLineContainsInstanceToken(pidFromFile, instanceToken, out bool tokenMatches);
                            bool allowKill;
                            if (tokenQueryOk)
                            {
                                allowKill = tokenMatches;
                            }
                            else
                            {
                                // If token validation is unavailable (e.g. Windows CIM permission issues),
                                // fall back to a stricter heuristic: only allow stop if the PID still looks like our server.
                                allowKill = LooksLikeMcpServerProcess(pidFromFile);
                            }

                            if (pidIsListener && allowKill)
                            {
                                if (TerminateProcess(pidFromFile))
                                {
                                    stoppedAny = true;
                                    try { File.Delete(pidFilePath); } catch { }
                                    ClearLocalServerPidTracking();
                                    if (!quiet)
                                    {
                                        McpLog.Info($"Stopped local HTTP server on port {port} (PID: {pidFromFile})");
                                    }
                                    return true;
                                }
                                if (!quiet)
                                {
                                    McpLog.Warn($"Failed to terminate local HTTP server on port {port} (PID: {pidFromFile}).");
                                }
                                return false;
                            }
                            if (!quiet)
                            {
                                McpLog.Warn(
                                    $"Refusing to stop port {port}: pidfile PID {pidFromFile} failed validation " +
                                    $"(listener={pidIsListener}, tokenMatch={tokenMatches}, tokenQueryOk={tokenQueryOk}).");
                            }
                            return false;
                        }
                    }
                }

                var pids = GetListeningProcessIdsForPort(port);
                if (pids.Count == 0)
                {
                    if (stoppedAny)
                    {
                        // We stopped what Unity started; the port is now free.
                        if (!quiet)
                        {
                            McpLog.Info($"Stopped local HTTP server on port {port}");
                        }
                        ClearLocalServerPidTracking();
                        return true;
                    }

                    if (!quiet)
                    {
                        McpLog.Info($"No process found listening on port {port}");
                    }
                    ClearLocalServerPidTracking();
                    return false;
                }

                // Prefer killing the PID that we previously observed binding this port (if still valid).
                if (TryGetStoredLocalServerPid(port, out int storedPid))
                {
                    if (pids.Contains(storedPid))
                    {
                        string expectedHash = string.Empty;
                        try { expectedHash = EditorPrefs.GetString(EditorPrefKeys.LastLocalHttpServerPidArgsHash, string.Empty); } catch { }

                        // Prefer a fingerprint match (reduces PID reuse risk). If missing (older installs),
                        // fall back to a looser check to avoid leaving orphaned servers after domain reload.
                        if (TryGetUnixProcessArgs(storedPid, out var storedArgsLowerNow))
                        {
                        // Never kill Unity/Hub.
                        // Note: "mcp-for-unity" includes "unity", so detect MCP indicators first.
                        bool storedMentionsMcp = storedArgsLowerNow.Contains("mcp-for-unity")
                                                 || storedArgsLowerNow.Contains("mcp_for_unity")
                                                 || storedArgsLowerNow.Contains("mcpforunity");
                        if (storedArgsLowerNow.Contains("unityhub")
                            || storedArgsLowerNow.Contains("unity hub")
                            || (storedArgsLowerNow.Contains("unity") && !storedMentionsMcp))
                            {
                                if (!quiet)
                                {
                                    McpLog.Warn($"Refusing to stop port {port}: stored PID {storedPid} appears to be a Unity process.");
                                }
                            }
                            else
                            {
                                bool allowKill = false;
                                if (!string.IsNullOrEmpty(expectedHash))
                                {
                                    allowKill = string.Equals(expectedHash, ComputeShortHash(storedArgsLowerNow), StringComparison.OrdinalIgnoreCase);
                                }
                                else
                                {
                                    // Older versions didn't store a fingerprint; accept common server indicators.
                                    allowKill = storedArgsLowerNow.Contains("uvicorn")
                                                || storedArgsLowerNow.Contains("fastmcp")
                                                || storedArgsLowerNow.Contains("mcpforunity")
                                                || storedArgsLowerNow.Contains("mcp-for-unity")
                                                || storedArgsLowerNow.Contains("mcp_for_unity")
                                                || storedArgsLowerNow.Contains("uvx")
                                                || storedArgsLowerNow.Contains("python");
                                }

                                if (allowKill && TerminateProcess(storedPid))
                                {
                                    if (!quiet)
                                    {
                                        McpLog.Info($"Stopped local HTTP server on port {port} (PID: {storedPid})");
                                    }
                                    stoppedAny = true;
                                    ClearLocalServerPidTracking();
                                    // Refresh the PID list to avoid double-work.
                                    pids = GetListeningProcessIdsForPort(port);
                                }
                                else if (!allowKill && !quiet)
                                {
                                    McpLog.Warn($"Refusing to stop port {port}: stored PID {storedPid} did not match expected server fingerprint.");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Stale PID (no longer listening). Clear.
                        ClearLocalServerPidTracking();
                    }
                }

                foreach (var pid in pids)
                {
                    if (pid <= 0) continue;
                    if (unityPid > 0 && pid == unityPid)
                    {
                        if (!quiet)
                        {
                            McpLog.Warn($"Refusing to stop port {port}: owning PID appears to be the Unity Editor process (PID {pid}).");
                        }
                        continue;
                    }

                    if (!LooksLikeMcpServerProcess(pid))
                    {
                        if (!quiet)
                        {
                            McpLog.Warn($"Refusing to stop port {port}: owning PID {pid} does not look like mcp-for-unity.");
                        }
                        continue;
                    }

                    if (TerminateProcess(pid))
                    {
                        McpLog.Info($"Stopped local HTTP server on port {port} (PID: {pid})");
                        stoppedAny = true;
                    }
                    else
                    {
                        if (!quiet)
                        {
                            McpLog.Warn($"Failed to stop process PID {pid} on port {port}");
                        }
                    }
                }

                if (stoppedAny)
                {
                    ClearLocalServerPidTracking();
                }
                return stoppedAny;
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    McpLog.Error($"Failed to stop server: {ex.Message}");
                }
                return false;
            }
        }

        private static bool TryGetUnixProcessArgs(int pid, out string argsLower)
        {
            argsLower = string.Empty;
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    return false;
                }

                string psPath = "/bin/ps";
                if (!File.Exists(psPath)) psPath = "ps";

                bool ok = ExecPath.TryRun(psPath, $"-p {pid} -ww -o args=", Application.dataPath, out var stdout, out var stderr, 5000);
                if (!ok && string.IsNullOrWhiteSpace(stdout))
                {
                    return false;
                }
                string combined = ((stdout ?? string.Empty) + "\n" + (stderr ?? string.Empty)).Trim();
                if (string.IsNullOrEmpty(combined)) return false;
                // Normalize for matching to tolerate ps wrapping/newlines.
                argsLower = NormalizeForMatch(combined);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetPortFromPidFilePath(string pidFilePath, out int port)
        {
            port = 0;
            if (string.IsNullOrEmpty(pidFilePath))
            {
                return false;
            }

            try
            {
                string fileName = Path.GetFileNameWithoutExtension(pidFilePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    return false;
                }

                const string prefix = "mcp_http_";
                if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string portText = fileName.Substring(prefix.Length);
                return int.TryParse(portText, out port) && port > 0;
            }
            catch
            {
                port = 0;
                return false;
            }
        }

        private List<int> GetListeningProcessIdsForPort(int port)
        {
            var results = new List<int>();
            try
            {
                string stdout, stderr;
                bool success;

                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Run netstat -ano directly (without findstr) and filter in C#.
                    // Using findstr in a pipe causes the entire command to return exit code 1 when no matches are found,
                    // which ExecPath.TryRun interprets as failure. Running netstat alone gives us exit code 0 on success.
                    success = ExecPath.TryRun("netstat.exe", "-ano", Application.dataPath, out stdout, out stderr);

                    // Process stdout regardless of success flag - netstat might still produce valid output
                    if (!string.IsNullOrEmpty(stdout))
                    {
                        string portSuffix = $":{port}";
                        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            // Windows netstat format: Proto  Local Address          Foreign Address        State           PID
                            // Example: TCP    0.0.0.0:8080           0.0.0.0:0              LISTENING       12345
                            if (line.Contains("LISTENING") && line.Contains(portSuffix))
                            {
                                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                // Verify the local address column actually ends with :{port}
                                // parts[0] = Proto (TCP), parts[1] = Local Address, parts[2] = Foreign Address, parts[3] = State, parts[4] = PID
                                if (parts.Length >= 5)
                                {
                                    string localAddr = parts[1];
                                    if (localAddr.EndsWith(portSuffix) && int.TryParse(parts[parts.Length - 1], out int pid))
                                    {
                                        results.Add(pid);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // lsof: only return LISTENers (avoids capturing random clients)
                    // Use /usr/sbin/lsof directly as it might not be in PATH for Unity
                    string lsofPath = "/usr/sbin/lsof";
                    if (!System.IO.File.Exists(lsofPath)) lsofPath = "lsof"; // Fallback

                    // -nP: avoid DNS/service name lookups; faster and less error-prone
                    success = ExecPath.TryRun(lsofPath, $"-nP -iTCP:{port} -sTCP:LISTEN -t", Application.dataPath, out stdout, out stderr);
                    if (success && !string.IsNullOrWhiteSpace(stdout))
                    {
                        var pidStrings = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var pidString in pidStrings)
                        {
                            if (int.TryParse(pidString.Trim(), out int pid))
                            {
                                results.Add(pid);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Error checking port {port}: {ex.Message}");
            }
            return results.Distinct().ToList();
        }

        private static int GetCurrentProcessIdSafe()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
            catch { return -1; }
        }

        private bool LooksLikeMcpServerProcess(int pid)
        {
            try
            {
                // Windows best-effort: First check process name with tasklist, then try to get command line with wmic
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Step 1: Check if process name matches known server executables
                    ExecPath.TryRun("cmd.exe", $"/c tasklist /FI \"PID eq {pid}\"", Application.dataPath, out var tasklistOut, out var tasklistErr, 5000);
                    string tasklistCombined = ((tasklistOut ?? string.Empty) + "\n" + (tasklistErr ?? string.Empty)).ToLowerInvariant();

                    // Check for common process names
                    bool isPythonOrUv = tasklistCombined.Contains("python") || tasklistCombined.Contains("uvx") || tasklistCombined.Contains("uv.exe");
                    if (!isPythonOrUv)
                    {
                        return false;
                    }

                    // Step 2: Try to get command line with wmic for better validation
                    ExecPath.TryRun("cmd.exe", $"/c wmic process where \"ProcessId={pid}\" get CommandLine /value", Application.dataPath, out var wmicOut, out var wmicErr, 5000);
                    string wmicCombined = ((wmicOut ?? string.Empty) + "\n" + (wmicErr ?? string.Empty)).ToLowerInvariant();
                    string wmicCompact = NormalizeForMatch(wmicOut ?? string.Empty);

                    // If we can see the command line, validate it's our server
                    if (!string.IsNullOrEmpty(wmicCombined) && wmicCombined.Contains("commandline="))
                    {
                        bool mentionsMcp = wmicCompact.Contains("mcp-for-unity")
                                           || wmicCompact.Contains("mcp_for_unity")
                                           || wmicCompact.Contains("mcpforunity")
                                           || wmicCompact.Contains("mcpforunityserver");
                        bool mentionsTransport = wmicCompact.Contains("--transporthttp") || (wmicCompact.Contains("--transport") && wmicCompact.Contains("http"));
                        bool mentionsUvicorn = wmicCombined.Contains("uvicorn");

                        if (mentionsMcp || mentionsTransport || mentionsUvicorn)
                        {
                            return true;
                        }
                    }

                    // Fall back to just checking for python/uv processes if wmic didn't give us details
                    // This is less precise but necessary for cases where wmic access is restricted
                    return isPythonOrUv;
                }

                // macOS/Linux: ps -p pid -ww -o comm= -o args=
                // Use -ww to avoid truncating long command lines (important for reliably spotting 'mcp-for-unity').
                // Use an absolute ps path to avoid relying on PATH inside the Unity Editor process.
                string psPath = "/bin/ps";
                if (!File.Exists(psPath)) psPath = "ps";
                // Important: ExecPath.TryRun returns false when exit code != 0, but ps output can still be useful.
                // Always parse stdout/stderr regardless of exit code to avoid false negatives.
                ExecPath.TryRun(psPath, $"-p {pid} -ww -o comm= -o args=", Application.dataPath, out var psOut, out var psErr, 5000);
                string raw = ((psOut ?? string.Empty) + "\n" + (psErr ?? string.Empty)).Trim();
                string s = raw.ToLowerInvariant();
                string sCompact = NormalizeForMatch(raw);
                if (!string.IsNullOrEmpty(s))
                {
                    bool mentionsMcp = sCompact.Contains("mcp-for-unity")
                                       || sCompact.Contains("mcp_for_unity")
                                       || sCompact.Contains("mcpforunity");

                    // If it explicitly mentions the server package/entrypoint, that is sufficient.
                    // Note: Check before Unity exclusion since "mcp-for-unity" contains "unity".
                    if (mentionsMcp)
                    {
                        return true;
                    }

                    // Explicitly never kill Unity / Unity Hub processes
                    // Note: explicit !mentionsMcp is defensive; we already return early for mentionsMcp above.
                    if (s.Contains("unityhub") || s.Contains("unity hub") || (s.Contains("unity") && !mentionsMcp))
                    {
                        return false;
                    }

                    // Positive indicators
                    bool mentionsUvx = s.Contains("uvx") || s.Contains(" uvx ");
                    bool mentionsUv = s.Contains("uv ") || s.Contains("/uv");
                    bool mentionsPython = s.Contains("python");
                    bool mentionsUvicorn = s.Contains("uvicorn");
                    bool mentionsTransport = sCompact.Contains("--transporthttp") || (sCompact.Contains("--transport") && sCompact.Contains("http"));

                    // Accept if it looks like uv/uvx/python launching our server package/entrypoint
                    if ((mentionsUvx || mentionsUv || mentionsPython || mentionsUvicorn) && mentionsTransport)
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static void LogStopDiagnosticsOnce(int pid, string details)
        {
            try
            {
                if (LoggedStopDiagnosticsPids.Contains(pid))
                {
                    return;
                }
                LoggedStopDiagnosticsPids.Add(pid);
                McpLog.Debug($"[StopLocalHttpServer] PID {pid} did not match server heuristics. {details}");
            }
            catch { }
        }

        private static string TrimForLog(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            const int max = 500;
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "...(truncated)";
        }

        private bool TerminateProcess(int pid)
        {
            try
            {
                string stdout, stderr;
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // taskkill without /F first; fall back to /F if needed.
                    bool ok = ExecPath.TryRun("taskkill", $"/PID {pid} /T", Application.dataPath, out stdout, out stderr);
                    if (!ok)
                    {
                        ok = ExecPath.TryRun("taskkill", $"/F /PID {pid} /T", Application.dataPath, out stdout, out stderr);
                    }
                    return ok;
                }
                else
                {
                    // Try a graceful termination first, then escalate if the process is still alive.
                    // Note: `kill -15` can succeed (exit 0) even if the process takes time to exit,
                    // so we verify and only escalate when needed.
                    string killPath = "/bin/kill";
                    if (!File.Exists(killPath)) killPath = "kill";
                    ExecPath.TryRun(killPath, $"-15 {pid}", Application.dataPath, out stdout, out stderr);

                    // Wait briefly for graceful shutdown.
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (!ProcessExistsUnix(pid))
                        {
                            return true;
                        }
                        System.Threading.Thread.Sleep(100);
                    }

                    // Escalate.
                    ExecPath.TryRun(killPath, $"-9 {pid}", Application.dataPath, out stdout, out stderr);
                    return !ProcessExistsUnix(pid);
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error killing process {pid}: {ex.Message}");
                return false;
            }
        }

        private static bool ProcessExistsUnix(int pid)
        {
            try
            {
                // ps exits non-zero when PID is not found.
                string psPath = "/bin/ps";
                if (!File.Exists(psPath)) psPath = "ps";
                ExecPath.TryRun(psPath, $"-p {pid} -o pid=", Application.dataPath, out var stdout, out var stderr, 2000);
                string combined = ((stdout ?? string.Empty) + "\n" + (stderr ?? string.Empty)).Trim();
                return !string.IsNullOrEmpty(combined) && combined.Any(char.IsDigit);
            }
            catch
            {
                return true; // Assume it exists if we cannot verify.
            }
        }

        /// <summary>
        /// Attempts to build the command used for starting the local HTTP server
        /// </summary>
        public bool TryGetLocalHttpServerCommand(out string command, out string error)
        {
            command = null;
            error = null;
            if (!TryGetLocalHttpServerCommandParts(
                    out var fileName,
                    out var args,
                    out var displayCommand,
                    out error))
            {
                return false;
            }

            // Maintain existing behavior: return a single command string suitable for display/copy.
            command = displayCommand;
            return true;
        }

        private bool TryGetLocalHttpServerCommandParts(out string fileName, out string arguments, out string displayCommand, out string error)
        {
            fileName = null;
            arguments = null;
            displayCommand = null;
            error = null;

            bool useHttpTransport = McpProjectSettings.GetUseHttpTransport();
            if (!useHttpTransport)
            {
                error = "HTTP 连接方式未启用。请先在 UnityMCP 面板中启用 HTTP 本机模式。";
                return false;
            }

            string httpUrl = HttpEndpointUtility.GetBaseUrl();
            if (!IsLocalUrl())
            {
                error = $"当前 HTTP 地址 ({httpUrl}) 不是本机地址。本地 Server 启动仅支持 localhost/127.0.0.1/0.0.0.0/::1。";
                return false;
            }

            var (uvxPath, fromUrl, packageName) = AssetPathUtility.GetUvxCommandParts();
            if (string.IsNullOrEmpty(uvxPath))
            {
                error = "没有找到 uv/uvx。请安装 uv，或在高级设置中指定 uvx 路径。";
                return false;
            }

            string uvPath = BuildUvPathFromUvx(uvxPath);

            if (!AssetPathUtility.TryGetPrivateServerSource(out fromUrl, out error))
            {
                return false;
            }

            if (!TryPrepareSharedServerRuntime(fromUrl, out var runtimeDir, out error))
            {
                return false;
            }

            string args = $"run --project {QuoteArgument(runtimeDir)} --frozen {packageName} --transport http --http-url {httpUrl}";

            fileName = uvPath;
            arguments = args;
            displayCommand = $"{QuoteIfNeeded(uvPath)} {args}";
            return true;
        }

        private static string GetLocalHttpServerLogDirectory()
        {
            string dir = Path.Combine(GetProjectRootPath(), "Library", "MCPForUnity", "Logs");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetSharedServerRuntimeDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".mcp-for-unity");
            }

            return Path.Combine(localAppData, "MCPForUnity", "ServerRuntime");
        }

        private static string GetRuntimeManifestPath(string runtimeDir)
        {
            return Path.Combine(runtimeDir, ".unitymcp-runtime-source.txt");
        }

        private static bool TryPrepareSharedServerRuntime(string sourceDir, out string runtimeDir, out string error)
        {
            runtimeDir = null;
            error = null;

            if (string.IsNullOrWhiteSpace(sourceDir) ||
                !Directory.Exists(sourceDir) ||
                !File.Exists(Path.Combine(sourceDir, "pyproject.toml")))
            {
                error = $"本地 MCP Server 源码目录无效或缺少 pyproject.toml: {sourceDir}";
                return false;
            }

            runtimeDir = GetSharedServerRuntimeDirectory();
            try
            {
                Directory.CreateDirectory(runtimeDir);
                MirrorDirectory(sourceDir, runtimeDir);
                File.WriteAllText(GetRuntimeManifestPath(runtimeDir), sourceDir, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = $"同步共享 Server Runtime 失败: {ex.Message}";
                return false;
            }
        }

        private static void MirrorDirectory(string sourceDir, string targetDir)
        {
            var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, directory);
                if (ShouldSkipRuntimePath(relative))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.Combine(targetDir, relative));
            }

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                if (ShouldSkipRuntimePath(relative))
                {
                    continue;
                }

                string target = Path.Combine(targetDir, relative);
                expectedFiles.Add(Path.GetFullPath(target));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }

            DeleteStaleRuntimeFiles(targetDir, expectedFiles);
        }

        private static void DeleteStaleRuntimeFiles(string targetDir, HashSet<string> expectedFiles)
        {
            if (!Directory.Exists(targetDir))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(targetDir, file);
                if (ShouldSkipRuntimePath(relative))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(file);
                if (!expectedFiles.Contains(fullPath) && !string.Equals(fullPath, Path.GetFullPath(GetRuntimeManifestPath(targetDir)), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }

        private static bool ShouldSkipRuntimePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            string normalized = relativePath.Replace('\\', '/');
            string[] segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Any(segment =>
                string.Equals(segment, ".venv", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "build", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "__pycache__", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, ".pytest_cache", StringComparison.OrdinalIgnoreCase));
        }

        private bool TryEnsureSharedRuntimeDependencies(string uvPath, string runtimeDir, bool allowOnlineInitialize, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(uvPath))
            {
                error = "没有找到 uv。请安装 uv，或在高级设置中指定 uvx 路径。";
                return false;
            }

            if (TryVerifySharedRuntimeDependenciesOffline(uvPath, runtimeDir, out error))
            {
                return true;
            }

            if (!allowOnlineInitialize)
            {
                return false;
            }

            McpLog.Info("共享 Server Runtime 依赖未就绪，开始联网初始化一次。");
            string initArgs = $"run --project {QuoteArgument(runtimeDir)} --frozen python -c {QuoteArgument("import fastmcp")}";
            if (!ExecuteUvCommand(uvPath, initArgs, out var initStdout, out var initStderr))
            {
                string combinedInitOutput = string.Join(
                    Environment.NewLine,
                    new[] { initStderr, initStdout }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
                if (string.IsNullOrWhiteSpace(combinedInitOutput))
                {
                    combinedInitOutput = "uv 初始化共享 Runtime 失败，但没有返回详细输出。";
                }

                error =
                    "共享 Server Runtime 初始化失败。请检查网络、uv、Python 版本或依赖源配置。\n\n" +
                    $"Runtime: {runtimeDir}\n\n" +
                    combinedInitOutput;
                return false;
            }

            return TryVerifySharedRuntimeDependenciesOffline(uvPath, runtimeDir, out error);
        }

        private bool TryVerifySharedRuntimeDependenciesOffline(string uvPath, string runtimeDir, out string error)
        {
            error = null;
            string importStatement = string.Join("; ", RequiredServerPythonModules.Select(module => $"import {module}"));
            string args = $"run --project {QuoteArgument(runtimeDir)} --frozen --offline python -c {QuoteArgument(importStatement)}";

            if (ExecuteUvCommand(uvPath, args, out var stdout, out var stderr))
            {
                return true;
            }

            string combinedOutput = string.Join(
                Environment.NewLine,
                new[] { stderr, stdout }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            if (string.IsNullOrWhiteSpace(combinedOutput))
            {
                combinedOutput = "uv 离线依赖检查失败，但没有返回详细输出。";
            }

            error =
                "共享 Server Runtime 的本地依赖不完整，已阻止无网启动以避免 MCP 调用时随机失败。\n\n" +
                $"Runtime: {runtimeDir}\n\n" +
                "请先联网执行一次 Server 初始化，或后续使用本地 wheel 离线包初始化。\n\n" +
                combinedOutput;
            return false;
        }

        private static void AppendHiddenServerLog(string path, string line)
        {
            if (line == null)
            {
                return;
            }

            try
            {
                lock (HiddenServerLogLock)
                {
                    File.AppendAllText(
                        path,
                        $"[{DateTime.Now:O}] {line}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never break server lifecycle.
            }
        }

        private static void AttachHiddenServerLoggers(System.Diagnostics.Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                string logDir = GetLocalHttpServerLogDirectory();
                string stdoutPath = Path.Combine(logDir, "central-server.out.log");
                string stderrPath = Path.Combine(logDir, "central-server.err.log");

                if (process.StartInfo.RedirectStandardOutput)
                {
                    process.OutputDataReceived += (_, e) => AppendHiddenServerLog(stdoutPath, e.Data);
                    process.BeginOutputReadLine();
                }

                if (process.StartInfo.RedirectStandardError)
                {
                    process.ErrorDataReceived += (_, e) => AppendHiddenServerLog(stderrPath, e.Data);
                    process.BeginErrorReadLine();
                }

                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    lock (HiddenServerProcesses)
                    {
                        HiddenServerProcesses.Remove(process);
                    }
                };

                lock (HiddenServerProcesses)
                {
                    HiddenServerProcesses.Add(process);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to attach hidden server log capture: {ex.Message}");
            }
        }

        private System.Diagnostics.ProcessStartInfo CreateHiddenProcessStartInfo(string fileName, string arguments, string displayCommand)
        {
            if (string.IsNullOrWhiteSpace(displayCommand))
                throw new ArgumentException("Command cannot be empty", nameof(displayCommand));

#if UNITY_EDITOR_WIN
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = GetProjectRootPath()
            };
#else
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = GetProjectRootPath()
            };
#endif
        }

        /// <summary>
        /// Check if the configured HTTP URL is a local address
        /// </summary>
        public bool IsLocalUrl()
        {
            string httpUrl = HttpEndpointUtility.GetBaseUrl();
            return IsLocalUrl(httpUrl);
        }

        /// <summary>
        /// Check if a URL is local (localhost, 127.0.0.1, 0.0.0.0)
        /// </summary>
        private static bool IsLocalUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            try
            {
                var uri = new Uri(url);
                string host = uri.Host.ToLower();
                return host == "localhost" || host == "127.0.0.1" || host == "0.0.0.0" || host == "::1";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if the local HTTP server can be started
        /// </summary>
        public bool CanStartLocalServer()
        {
            bool useHttpTransport = McpProjectSettings.GetUseHttpTransport();
            return useHttpTransport && IsLocalUrl();
        }

        /// <summary>
        /// Creates a ProcessStartInfo for opening a terminal window with the given command
        /// Works cross-platform: macOS, Windows, and Linux
        /// </summary>
        private System.Diagnostics.ProcessStartInfo CreateTerminalProcessStartInfo(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("Command cannot be empty", nameof(command));

            command = command.Replace("\r", "").Replace("\n", "");

#if UNITY_EDITOR_OSX
            // macOS: Avoid AppleScript (automation permission prompts). Use a .command script and open it.
            string scriptsDir = Path.Combine(GetProjectRootPath(), "Library", "MCPForUnity", "TerminalScripts");
            Directory.CreateDirectory(scriptsDir);
            string scriptPath = Path.Combine(scriptsDir, "mcp-terminal.command");
            File.WriteAllText(
                scriptPath,
                "#!/bin/bash\n" +
                "set -e\n" +
                "clear\n" +
                $"{command}\n");
            ExecPath.TryRun("/bin/chmod", $"+x \"{scriptPath}\"", Application.dataPath, out _, out _, 3000);
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                Arguments = $"-a Terminal \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
#elif UNITY_EDITOR_WIN
            // Windows: Avoid brittle nested-quote escaping by writing a .cmd script and starting it in a new window.
            string scriptsDir = Path.Combine(GetProjectRootPath(), "Library", "MCPForUnity", "TerminalScripts");
            Directory.CreateDirectory(scriptsDir);
            string scriptPath = Path.Combine(scriptsDir, "mcp-terminal.cmd");
            File.WriteAllText(
                scriptPath,
                "@echo off\r\n" +
                "cls\r\n" +
                command + "\r\n");
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"MCP Server\" cmd.exe /k \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
#else
            // Linux: Try common terminal emulators
            // We use bash -c to execute the command, so we must properly quote/escape for bash
            // Escape single quotes for the inner bash string
            string escapedCommandLinux = command.Replace("'", "'\\''");
            // Wrap the command in single quotes for bash -c
            string script = $"'{escapedCommandLinux}; exec bash'";
            // Escape double quotes for the outer Process argument string
            string escapedScriptForArg = script.Replace("\"", "\\\"");
            string bashCmdArgs = $"bash -c \"{escapedScriptForArg}\"";
            
            string[] terminals = { "gnome-terminal", "xterm", "konsole", "xfce4-terminal" };
            string terminalCmd = null;
            
            foreach (var term in terminals)
            {
                try
                {
                    var which = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = term,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    which.WaitForExit(5000); // Wait for up to 5 seconds, the command is typically instantaneous
                    if (which.ExitCode == 0)
                    {
                        terminalCmd = term;
                        break;
                    }
                }
                catch { }
            }
            
            if (terminalCmd == null)
            {
                terminalCmd = "xterm"; // Fallback
            }
            
            // Different terminals have different argument formats
            string args;
            if (terminalCmd == "gnome-terminal")
            {
                args = $"-- {bashCmdArgs}";
            }
            else if (terminalCmd == "konsole")
            {
                args = $"-e {bashCmdArgs}";
            }
            else if (terminalCmd == "xfce4-terminal")
            {
                // xfce4-terminal expects -e "command string" or -e command arg
                args = $"--hold -e \"{bashCmdArgs.Replace("\"", "\\\"")}\"";
            }
            else // xterm and others
            {
                args = $"-hold -e {bashCmdArgs}";
            }
            
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = terminalCmd,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };
#endif
        }
    }
}
