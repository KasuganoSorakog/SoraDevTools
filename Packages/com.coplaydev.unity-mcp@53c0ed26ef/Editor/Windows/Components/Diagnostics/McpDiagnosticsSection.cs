using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Diagnostics
{
    internal sealed class McpDiagnosticsSection
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private readonly VisualElement root;
        private readonly Button refreshButton;
        private readonly VisualElement summaryContainer;
        private readonly Label versionLockStatus;
        private readonly VisualElement warningsContainer;
        private readonly VisualElement sessionsContainer;
        private readonly Label toolHealthStatus;
        private readonly VisualElement toolsContainer;

        private bool refreshInProgress;

        public McpDiagnosticsSection(VisualElement root)
        {
            this.root = root;
            refreshButton = root.Q<Button>("diagnostics-refresh-button");
            summaryContainer = root.Q<VisualElement>("diagnostics-summary");
            versionLockStatus = root.Q<Label>("version-lock-status");
            warningsContainer = root.Q<VisualElement>("diagnostics-warnings");
            sessionsContainer = root.Q<VisualElement>("diagnostics-sessions");
            toolHealthStatus = root.Q<Label>("tool-health-status");
            toolsContainer = root.Q<VisualElement>("diagnostics-tools");

            if (refreshButton != null)
            {
                refreshButton.tooltip = "重新读取中心 Server 诊断信息，并刷新本地工具/连接状态。";
                refreshButton.clicked += () => _ = RefreshAsync();
            }
        }

        public void Refresh()
        {
            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (refreshInProgress)
            {
                return;
            }

            refreshInProgress = true;
            refreshButton?.SetEnabled(false);

            try
            {
                RenderLocalSummary(null, "正在读取中心 Server 诊断...");
                JObject diagnostics = await FetchDiagnosticsAsync();
                RenderLocalSummary(diagnostics, null);
                RenderVersionLock(diagnostics);
                RenderSessions(diagnostics);
                RenderToolHealth(diagnostics);
            }
            catch (Exception ex)
            {
                RenderLocalSummary(null, $"诊断读取失败: {ex.Message}");
                SetLabel(versionLockStatus, "无法读取中心 Server 诊断，先确认 Server 是否 Running。", "status-warning");
                ClearAndAdd(warningsContainer, "中心 Server 诊断接口不可用。");
                ClearAndAdd(sessionsContainer, "暂无可显示的远端 Session。");
                RenderToolHealth(null);
            }
            finally
            {
                refreshButton?.SetEnabled(true);
                refreshInProgress = false;
            }
        }

        private static async Task<JObject> FetchDiagnosticsAsync()
        {
            string baseUrl = HttpEndpointUtility.GetBaseUrl().TrimEnd('/');
            using var response = await HttpClient.GetAsync($"{baseUrl}/plugin/diagnostics");
            string body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return await FetchCompatibilityDiagnosticsAsync(baseUrl);
            }

            response.EnsureSuccessStatusCode();
            return JObject.Parse(body);
        }

        private static async Task<JObject> FetchCompatibilityDiagnosticsAsync(string baseUrl)
        {
            using var response = await HttpClient.GetAsync($"{baseUrl}/plugin/sessions");
            string body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            var payload = JObject.Parse(body);
            var sourceSessions = payload["sessions"] as JObject ?? new JObject();
            var sessions = new JObject();

            foreach (var property in sourceSessions.Properties())
            {
                if (property.Value is not JObject sourceSession)
                {
                    continue;
                }

                var session = (JObject)sourceSession.DeepClone();
                string project = session["project"]?.ToString() ?? "Unknown";
                string hash = session["hash"]?.ToString() ?? "unknown";
                if (string.IsNullOrWhiteSpace(session["instance"]?.ToString()))
                {
                    session["instance"] = $"{project}@{hash}";
                }

                session["tool_count"] = JValue.CreateNull();
                sessions[property.Name] = session;
            }

            return new JObject
            {
                ["status"] = "warning",
                ["server"] = new JObject
                {
                    ["transport"] = "http",
                    ["version"] = "unknown",
                    ["diagnostics_available"] = false,
                    ["session_count"] = sessions.Properties().Count()
                },
                ["sessions"] = sessions,
                ["warnings"] = new JArray
                {
                    new JObject
                    {
                        ["code"] = "diagnostics_route_missing",
                        ["severity"] = "warning",
                        ["message"] = "当前中心 Server 正在运行旧版本，未提供 /plugin/diagnostics。Session 已连接，但完整 Health 检查需要重启中心 Server 后才会生效。"
                    }
                }
            };
        }

        private void RenderLocalSummary(JObject diagnostics, string transientMessage)
        {
            summaryContainer?.Clear();

            bool serverRunning = false;
            try
            {
                serverRunning = MCPServiceLocator.Server.IsLocalHttpServerRunning();
            }
            catch { }

            var httpState = MCPServiceLocator.TransportManager.GetState(TransportMode.Http);
            var settings = McpProjectSettings.Load();
            string serverStatus = serverRunning ? "Running" : "Stopped";
            string sessionStatus = httpState.IsConnected ? $"Connected ({httpState.SessionId ?? "pending"})" : "Idle";
            string autoConnect = settings.AutoConnect ? "Enabled" : "Disabled";
            bool diagnosticsAvailable = diagnostics?["server"]?["diagnostics_available"]?.Value<bool?>() ?? true;
            string diagnosticStatus = transientMessage
                ?? (diagnosticsAvailable ? diagnostics?["status"]?.ToString() : "兼容模式：需重启 Server")
                ?? "unknown";

            AddSummaryCard("Server", serverStatus, serverRunning ? "status-ok" : "status-warning");
            AddSummaryCard("Session", sessionStatus, httpState.IsConnected ? "status-ok" : "status-warning");
            AddSummaryCard("AutoConnect", autoConnect, settings.AutoConnect ? "status-ok" : "status-neutral");
            AddSummaryCard("Diagnostics", diagnosticStatus, diagnostics?["status"]?.ToString() == "warning" ? "status-warning" : "status-ok");
        }

        private void RenderVersionLock(JObject diagnostics)
        {
            warningsContainer?.Clear();

            string localPackage = AssetPathUtility.GetPackageVersion();
            string serverVersion = diagnostics?["server"]?["version"]?.ToString() ?? "unknown";
            bool diagnosticsAvailable = diagnostics?["server"]?["diagnostics_available"]?.Value<bool?>() ?? true;
            var warnings = diagnostics?["warnings"] as JArray;
            bool hasWarnings = warnings != null && warnings.Count > 0;

            if (!diagnosticsAvailable)
            {
                SetLabel(
                    versionLockStatus,
                    $"中心 Server 正在运行旧版本。本项目包版本 {localPackage}，完整版本锁检查需要重启中心 Server 后启用。",
                    "status-warning");
            }
            else
            {
                SetLabel(
                    versionLockStatus,
                    hasWarnings
                        ? $"发现版本锁告警。本项目包版本 {localPackage}，中心 Server {serverVersion}。"
                        : $"版本锁正常。本项目包版本 {localPackage}，中心 Server {serverVersion}。",
                    hasWarnings ? "status-warning" : "status-ok");
            }

            if (!hasWarnings)
            {
                ClearAndAdd(warningsContainer, "没有发现已连接项目之间的 MCP 版本或能力协议冲突。");
                return;
            }

            foreach (var warning in warnings)
            {
                AddInfoRow(warningsContainer, warning["code"]?.ToString() ?? "warning", warning["message"]?.ToString() ?? warning.ToString());
            }
        }

        private void RenderSessions(JObject diagnostics)
        {
            sessionsContainer?.Clear();
            var sessions = diagnostics?["sessions"] as JObject;
            if (sessions == null || !sessions.Properties().Any())
            {
                ClearAndAdd(sessionsContainer, "当前中心 Server 没有已注册的 Unity 项目。");
                return;
            }

            foreach (var property in sessions.Properties())
            {
                var session = property.Value as JObject;
                if (session == null)
                {
                    continue;
                }

                string instance = session["instance"]?.ToString() ?? $"{session["project"]}@{session["hash"]}";
                string details =
                    $"set_active_instance: {instance} | Scene: {ValueOrDash(session["current_scene"])} | Tools: {ValueOrDash(session["tool_count"])} | " +
                    $"Unity: {ValueOrDash(session["unity_version"])} | MCP: {ValueOrDash(session["package_version"])}";
                AddInfoRow(sessionsContainer, instance, details);
            }
        }

        private void RenderToolHealth(JObject diagnostics)
        {
            toolsContainer?.Clear();

            var allTools = MCPServiceLocator.ToolDiscovery.DiscoverAllTools();
            var enabledTools = MCPServiceLocator.ToolDiscovery.GetEnabledTools();
            JObject currentSession = FindCurrentProjectSession(diagnostics);
            bool hasRemoteToolCount = TryGetRemoteToolCount(currentSession, out int remoteToolCount);
            string remoteText = currentSession == null
                ? "中心 Server 未找到当前项目 Session"
                : hasRemoteToolCount
                    ? $"当前项目中心 Server 已注册 {remoteToolCount} 个"
                    : "旧版 Server 未返回当前项目工具注册数";

            string text = $"本地发现 {allTools.Count} 个工具，启用 {enabledTools.Count} 个，{remoteText}。";
            bool mismatch = currentSession != null && hasRemoteToolCount && remoteToolCount != enabledTools.Count;
            bool warning = diagnostics != null && (currentSession == null || !hasRemoteToolCount || mismatch);
            SetLabel(toolHealthStatus, mismatch ? $"{text} 注册数量不一致，建议重启 Session 或刷新工具。" : text, warning ? "status-warning" : "status-ok");

            AddInfoRow(toolsContainer, "启用工具", string.Join(", ", enabledTools.Select(tool => tool.Name).Take(12)) + (enabledTools.Count > 12 ? " ..." : ""));
            AddInfoRow(
                toolsContainer,
                "健康建议",
                hasRemoteToolCount
                    ? "如果工具调用一直失败，先确认 Session Connected，再检查版本锁告警和 Unity Console。"
                    : "当前连接可用，但中心 Server 需要重启后才能显示完整工具注册自检。");
        }

        private static JObject FindCurrentProjectSession(JObject diagnostics)
        {
            var sessions = diagnostics?["sessions"] as JObject;
            if (sessions == null)
            {
                return null;
            }

            string localHash = ProjectIdentityUtility.GetProjectHash();
            return sessions
                .Properties()
                .Select(property => property.Value as JObject)
                .FirstOrDefault(session => string.Equals(
                    session?["hash"]?.ToString(),
                    localHash,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetRemoteToolCount(JObject session, out int toolCount)
        {
            toolCount = 0;
            JToken token = session?["tool_count"];
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            return int.TryParse(token.ToString(), out toolCount);
        }

        private void AddSummaryCard(string title, string value, string statusClass)
        {
            if (summaryContainer == null)
            {
                return;
            }

            var card = new VisualElement();
            card.AddToClassList("header-status-card");

            var label = new Label(title);
            label.AddToClassList("header-status-label");
            card.Add(label);

            var valueLabel = new Label(value);
            valueLabel.AddToClassList("header-status-value");
            valueLabel.AddToClassList(statusClass);
            card.Add(valueLabel);

            summaryContainer.Add(card);
        }

        private static void AddInfoRow(VisualElement container, string title, string value)
        {
            if (container == null)
            {
                return;
            }

            var row = new VisualElement();
            row.AddToClassList("diagnostics-row");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("diagnostics-row-title");
            row.Add(titleLabel);

            var valueLabel = new Label(string.IsNullOrWhiteSpace(value) ? "-" : value);
            valueLabel.AddToClassList("help-text");
            row.Add(valueLabel);

            container.Add(row);
        }

        private static void ClearAndAdd(VisualElement container, string text)
        {
            container?.Clear();
            AddInfoRow(container, "状态", text);
        }

        private static void SetLabel(Label label, string text, string statusClass)
        {
            if (label == null)
            {
                return;
            }

            label.text = text;
            label.RemoveFromClassList("status-ok");
            label.RemoveFromClassList("status-warning");
            label.RemoveFromClassList("status-bad");
            label.RemoveFromClassList("status-neutral");
            label.AddToClassList(statusClass);
        }

        private static string ValueOrDash(JToken token)
        {
            string value = token?.ToString();
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }
}
