using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Connection
{
    /// <summary>
    /// Controller for the Connection section of the MCP For Unity editor window.
    /// Handles transport protocol, HTTP/stdio configuration, connection status, and health checks.
    /// </summary>
    public class McpConnectionSection
    {
        // Transport protocol enum
        private enum TransportProtocol
        {
            HTTPLocal,
            HTTPRemote,
            Stdio
        }

        private static readonly List<string> TransportLabels = new()
        {
            "HTTP 本机",
            "HTTP 远程",
            "Stdio 本地"
        };

        // UI Elements
        private DropdownField transportDropdown;
        private VisualElement httpUrlRow;
        private VisualElement httpServerCommandSection;
        private TextField httpServerCommandField;
        private Button copyHttpServerCommandButton;
        private Label httpServerCommandHint;
        private TextField httpUrlField;
        private Button startHttpServerButton;
        private Button stopHttpServerButton;
        private VisualElement unitySocketPortRow;
        private TextField unityPortField;
        private VisualElement statusIndicator;
        private Label connectionStatusLabel;
        private Button connectionToggleButton;
        private VisualElement healthIndicator;
        private Label healthStatusLabel;
        private VisualElement healthRow;
        private Button testConnectionButton;

        private bool connectionToggleInProgress;
        private bool httpServerToggleInProgress;
        private Task verificationTask;
        private string lastHealthStatus;
        private double lastLocalServerRunningPollTime;
        private bool lastLocalServerRunning;

        // Health status constants
        private const string HealthStatusUnknown = "未知";
        private const string HealthStatusHealthy = "健康";
        private const string HealthStatusPingFailed = "Ping 失败";
        private const string HealthStatusUnhealthy = "异常";

        // Events
        public event Action OnManualConfigUpdateRequested;
        public event Action OnTransportChanged;

        public VisualElement Root { get; private set; }

        public McpConnectionSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            transportDropdown = Root.Q<DropdownField>("transport-dropdown");
            httpUrlRow = Root.Q<VisualElement>("http-url-row");
            httpServerCommandSection = Root.Q<VisualElement>("http-server-command-section");
            httpServerCommandField = Root.Q<TextField>("http-server-command");
            copyHttpServerCommandButton = Root.Q<Button>("copy-http-server-command-button");
            httpServerCommandHint = Root.Q<Label>("http-server-command-hint");
            httpUrlField = Root.Q<TextField>("http-url");
            startHttpServerButton = Root.Q<Button>("start-http-server-button");
            stopHttpServerButton = Root.Q<Button>("stop-http-server-button");
            unitySocketPortRow = Root.Q<VisualElement>("unity-socket-port-row");
            unityPortField = Root.Q<TextField>("unity-port");
            statusIndicator = Root.Q<VisualElement>("status-indicator");
            connectionStatusLabel = Root.Q<Label>("connection-status");
            connectionToggleButton = Root.Q<Button>("connection-toggle");
            healthIndicator = Root.Q<VisualElement>("health-indicator");
            healthStatusLabel = Root.Q<Label>("health-status");
            healthRow = Root.Q<VisualElement>("health-row");
            testConnectionButton = Root.Q<Button>("test-connection-button");
        }

        private void InitializeUI()
        {
            transportDropdown.choices = TransportLabels;
            bool useHttpTransport = McpProjectSettings.GetUseHttpTransport();
            if (!useHttpTransport)
            {
                SetSelectedTransport(TransportProtocol.Stdio);
            }
            else
            {
                // Back-compat: if scope pref isn't set yet, infer from current URL.
                string scope = McpProjectSettings.GetHttpTransportScope();
                if (string.IsNullOrEmpty(scope))
                {
                    scope = MCPServiceLocator.Server.IsLocalUrl() ? "local" : "remote";
                    try
                    {
                        McpProjectSettings.SetHttpTransportScope(scope);
                    }
                    catch
                    {
                        McpLog.Debug("保存 HTTP 连接范围失败。");
                    }
                }

                SetSelectedTransport(scope == "remote" ? TransportProtocol.HTTPRemote : TransportProtocol.HTTPLocal);
            }

            httpUrlField.value = HttpEndpointUtility.GetBaseUrl();

            int unityPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
            if (unityPort == 0)
            {
                unityPort = MCPServiceLocator.Bridge.CurrentPort;
            }
            unityPortField.value = unityPort.ToString();

            UpdateHttpFieldVisibility();
            RefreshHttpUi();
            UpdateConnectionStatus();

            if (httpServerCommandSection is Foldout commandFoldout)
            {
                commandFoldout.value = false;
            }

            // Explain what "Health" means (it is a separate verify/ping check and can differ from session state).
            if (healthStatusLabel != null)
            {
                healthStatusLabel.tooltip = "健康状态是对当前传输的轻量 verify/ping 检查；Session 可用时健康检查也可能短暂降级。";
            }
            if (healthIndicator != null)
            {
                healthIndicator.tooltip = healthStatusLabel?.tooltip;
            }
        }

        private void SetSelectedTransport(TransportProtocol protocol)
        {
            transportDropdown.index = (int)protocol;
        }

        private TransportProtocol GetSelectedTransport()
        {
            if (transportDropdown == null)
            {
                return TransportProtocol.HTTPLocal;
            }

            int index = Mathf.Clamp(transportDropdown.index, 0, TransportLabels.Count - 1);
            return (TransportProtocol)index;
        }

        private TransportProtocol GetTransportFromLabel(string label)
        {
            int index = TransportLabels.IndexOf(label);
            return index >= 0 ? (TransportProtocol)index : GetSelectedTransport();
        }

        private void RegisterCallbacks()
        {
            transportDropdown.RegisterValueChangedCallback(evt =>
            {
                var previous = GetTransportFromLabel(evt.previousValue);
                var selected = GetSelectedTransport();
                bool useHttp = selected != TransportProtocol.Stdio;
                McpProjectSettings.SetUseHttpTransport(useHttp);
                
                // Clear any stale resume flags when user manually changes transport
                try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload); } catch { }
                try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload); } catch { }

                if (useHttp)
                {
                    string scope = selected == TransportProtocol.HTTPRemote ? "remote" : "local";
                    McpProjectSettings.SetHttpTransportScope(scope);
                }

                UpdateHttpFieldVisibility();
                RefreshHttpUi();
                UpdateConnectionStatus();
                OnManualConfigUpdateRequested?.Invoke();
                OnTransportChanged?.Invoke();
                McpLog.Info($"连接方式已切换为: {evt.newValue}");

                // Best-effort: stop the deselected transport to avoid leaving duplicated sessions running.
                // (Switching between HttpLocal/HttpRemote does not require stopping.)
                bool prevWasHttp = previous != TransportProtocol.Stdio;
                bool nextIsHttp = selected != TransportProtocol.Stdio;
                if (prevWasHttp != nextIsHttp)
                {
                    var stopMode = nextIsHttp ? TransportMode.Stdio : TransportMode.Http;
                    try
                    {
                        var stopTask = MCPServiceLocator.TransportManager.StopAsync(stopMode);
                        stopTask.ContinueWith(t =>
                        {
                            try
                            {
                                if (t.IsFaulted)
                                {
                                    var msg = t.Exception?.GetBaseException()?.Message ?? "未知错误";
                                    McpLog.Warn($"停止 {stopMode} 连接失败: {msg}");
                                }
                            }
                            catch { }
                        }, TaskScheduler.Default);
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"切换连接方式后停止旧连接失败（{stopMode}）: {ex.Message}");
                    }
                }
            });

            // Don't normalize/overwrite the URL on every keystroke (it fights the user and can duplicate schemes).
            // Instead, persist + normalize on focus-out / Enter, then update UI once.
            httpUrlField.RegisterCallback<FocusOutEvent>(_ => PersistHttpUrlFromField());
            httpUrlField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    PersistHttpUrlFromField();
                    evt.StopPropagation();
                }
            });

            if (startHttpServerButton != null)
            {
                startHttpServerButton.clicked += OnHttpServerToggleClicked;
            }

            if (stopHttpServerButton != null)
            {
                // Stop button removed from UXML as part of consolidated Start/Stop UX.
                // Kept null-check for backward compatibility if older UXML is loaded.
                stopHttpServerButton.clicked += () =>
                {
                    // In older UXML layouts, route the stop button to the consolidated toggle behavior.
                    // If a session is active, this will end it and attempt to stop the local server.
                    OnHttpServerToggleClicked();
                };
            }

            if (copyHttpServerCommandButton != null)
            {
                copyHttpServerCommandButton.clicked += () =>
                {
                    if (!string.IsNullOrEmpty(httpServerCommandField?.value) && copyHttpServerCommandButton.enabledSelf)
                    {
                        EditorGUIUtility.systemCopyBuffer = httpServerCommandField.value;
                        McpLog.Info("本地 HTTP Server 启动命令已复制到剪贴板。");
                    }
                };
            }

            unityPortField.RegisterCallback<FocusOutEvent>(_ => PersistUnityPortFromField());
            unityPortField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    PersistUnityPortFromField();
                    evt.StopPropagation();
                }
            });

            connectionToggleButton.clicked += OnConnectionToggleClicked;
            testConnectionButton.clicked += OnTestConnectionClicked;
        }

        private void PersistHttpUrlFromField()
        {
            if (httpUrlField == null)
            {
                return;
            }

            HttpEndpointUtility.SaveBaseUrl(httpUrlField.text);
            // Update displayed value to normalized form without re-triggering callbacks/caret jumps.
            httpUrlField.SetValueWithoutNotify(HttpEndpointUtility.GetBaseUrl());
            OnManualConfigUpdateRequested?.Invoke();
            RefreshHttpUi();
        }

        public void UpdateConnectionStatus()
        {
            var bridgeService = MCPServiceLocator.Bridge;
            bool isRunning = bridgeService.IsRunning;
            bool showLocalServerControls = IsHttpLocalSelected();
            bool debugMode = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            // Use EditorPrefs as source of truth for stdio selection - more reliable after domain reload
            // than checking the dropdown which may not be initialized yet
            bool stdioSelected = !McpProjectSettings.GetUseHttpTransport();

            // Keep the Start/Stop Server button label in sync even when the session is not running
            // (e.g., orphaned server after a domain reload).
            // NOTE: This also updates lastLocalServerRunning which is used below for session toggle visibility.
            UpdateStartHttpButtonState();

            // Detect orphaned session: if HTTP Local session thinks it's running but the server is gone,
            // automatically end the session to keep UI in sync with reality.
            if (showLocalServerControls && isRunning && !lastLocalServerRunning && !connectionToggleInProgress)
            {
                McpLog.Info("Server no longer running; ending orphaned session.");
                _ = EndOrphanedSessionAsync();
                isRunning = false; // Update local state for the rest of this method
            }

            // For HTTP Local: show session toggle button only when server is running (so user can manually start/end session).
            // For Stdio/HTTP Remote: always show the session toggle button.
            // This separates server lifecycle from session lifecycle for multi-instance scenarios.
            // We use lastLocalServerRunning which was just refreshed by UpdateStartHttpButtonState() above.
            if (connectionToggleButton != null)
            {
                bool showSessionToggle = !showLocalServerControls || lastLocalServerRunning;
                connectionToggleButton.style.display = showSessionToggle ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Hide "Test" buttons unless Debug Mode is enabled.
            if (testConnectionButton != null)
            {
                testConnectionButton.style.display = debugMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Health is useful mainly for diagnostics: hide it once we're "Healthy" unless Debug Mode is enabled.
            // If health is degraded, keep it visible even outside Debug Mode so it can act as a signal.
            if (healthRow != null)
            {
                bool showHealth = debugMode || (isRunning && lastHealthStatus != HealthStatusHealthy);
                healthRow.style.display = showHealth ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (isRunning)
            {
                // Show instance name (project folder name) for better identification in multi-instance scenarios.
                // Defensive: handle edge cases where path parsing might return null/empty.
                string projectDir = System.IO.Path.GetDirectoryName(Application.dataPath);
                string instanceName = !string.IsNullOrEmpty(projectDir) 
                    ? System.IO.Path.GetFileName(projectDir) 
                    : "Unity";
                if (string.IsNullOrEmpty(instanceName)) instanceName = "Unity";
                connectionStatusLabel.text = $"Session 已连接（{instanceName}）";
                statusIndicator.RemoveFromClassList("disconnected");
                statusIndicator.AddToClassList("connected");
                connectionToggleButton.text = "断开 Session";
                connectionToggleButton.SetEnabled(true); // Re-enable in case it was disabled during resumption
                
                // Force the UI to reflect the actual port being used
                unityPortField.value = bridgeService.CurrentPort.ToString();
                unityPortField.SetEnabled(false);
            }
            else
            {
                // Check if we're resuming the stdio bridge after a domain reload.
            // During this brief window, show a restoring state to avoid UI flicker.
                bool isStdioResuming = stdioSelected 
                    && EditorPrefs.GetBool(EditorPrefKeys.ResumeStdioAfterReload, false);

                if (isStdioResuming)
                {
                    connectionStatusLabel.text = "正在恢复...";
                    // Keep the indicator in a neutral/transitional state
                    statusIndicator.RemoveFromClassList("connected");
                    statusIndicator.RemoveFromClassList("disconnected");
                    connectionToggleButton.text = "启动 Session";
                    connectionToggleButton.SetEnabled(false);
                }
                else
                {
                    connectionStatusLabel.text = "未连接 Session";
                    statusIndicator.RemoveFromClassList("connected");
                    statusIndicator.AddToClassList("disconnected");
                    connectionToggleButton.text = "启动 Session";
                    connectionToggleButton.SetEnabled(true);
                }
                
                unityPortField.SetEnabled(!isStdioResuming);

                healthStatusLabel.text = HealthStatusUnknown;
                healthIndicator.RemoveFromClassList("healthy");
                healthIndicator.RemoveFromClassList("warning");
                healthIndicator.AddToClassList("unknown");
                
                int savedPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
                unityPortField.value = (savedPort == 0 
                    ? bridgeService.CurrentPort 
                    : savedPort).ToString();
            }

            // For stdio session toggling, make End Session visually "danger" (red).
            // (HTTP Local uses the consolidated Start/Stop Server button instead.)
            connectionToggleButton?.EnableInClassList("server-running", isRunning && stdioSelected);
        }

        public void UpdateHttpServerCommandDisplay()
        {
            if (httpServerCommandSection == null || httpServerCommandField == null)
            {
                return;
            }

            bool useHttp = transportDropdown != null && GetSelectedTransport() != TransportProtocol.Stdio;
            bool httpLocalSelected = IsHttpLocalSelected();
            bool isLocalHttpUrl = MCPServiceLocator.Server.IsLocalUrl();

            // Only show the local-server helper UI when HTTP Local is selected.
            if (!useHttp || !httpLocalSelected)
            {
                httpServerCommandSection.style.display = DisplayStyle.None;
                httpServerCommandField.value = string.Empty;
                httpServerCommandField.tooltip = string.Empty;
                if (httpServerCommandHint != null)
                {
                    httpServerCommandHint.text = string.Empty;
                }
                if (copyHttpServerCommandButton != null)
                {
                    copyHttpServerCommandButton.SetEnabled(false);
                }
                return;
            }

            httpServerCommandSection.style.display = DisplayStyle.Flex;

            if (!isLocalHttpUrl)
            {
                httpServerCommandField.value = string.Empty;
                httpServerCommandField.tooltip = string.Empty;
                if (httpServerCommandHint != null)
                {
                    httpServerCommandHint.text = "HTTP 本地模式需要 localhost 地址（localhost/127.0.0.1/0.0.0.0/::1）。";
                }
                copyHttpServerCommandButton?.SetEnabled(false);
                return;
            }

            if (MCPServiceLocator.Server.TryGetLocalHttpServerCommand(out var command, out var error))
            {
                httpServerCommandField.value = command;
                httpServerCommandField.tooltip = command;
                if (httpServerCommandHint != null)
                {
                    httpServerCommandHint.text = "这是当前项目的私有本地 Server 启动命令；需要手动启动时可复制到终端执行。";
                }
                if (copyHttpServerCommandButton != null)
                {
                    copyHttpServerCommandButton.SetEnabled(true);
                }
            }
            else
            {
                httpServerCommandField.value = string.Empty;
                httpServerCommandField.tooltip = string.Empty;
                if (httpServerCommandHint != null)
                {
                    httpServerCommandHint.text = error ?? "当前配置无法生成本地 Server 启动命令。";
                }
                if (copyHttpServerCommandButton != null)
                {
                    copyHttpServerCommandButton.SetEnabled(false);
                }
            }
        }

        private void UpdateHttpFieldVisibility()
        {
            bool useHttp = GetSelectedTransport() != TransportProtocol.Stdio;

            httpUrlRow.style.display = useHttp ? DisplayStyle.Flex : DisplayStyle.None;
            unitySocketPortRow.style.display = useHttp ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private bool IsHttpLocalSelected()
        {
            return transportDropdown != null && GetSelectedTransport() == TransportProtocol.HTTPLocal;
        }

        private void UpdateStartHttpButtonState()
        {
            if (startHttpServerButton == null)
                return;

            bool useHttp = transportDropdown != null && GetSelectedTransport() != TransportProtocol.Stdio;
            bool httpLocalSelected = IsHttpLocalSelected();
            startHttpServerButton.style.display = httpLocalSelected ? DisplayStyle.Flex : DisplayStyle.None;

            if (!useHttp)
            {
                startHttpServerButton.SetEnabled(false);
                startHttpServerButton.tooltip = string.Empty;
                return;
            }

            bool canStartLocalServer = httpLocalSelected && MCPServiceLocator.Server.IsLocalUrl();
            bool localServerRunning = false;

            // Avoid running expensive port/PID checks every UI tick.
            if (httpLocalSelected)
            {
                double now = EditorApplication.timeSinceStartup;
                if ((now - lastLocalServerRunningPollTime) > 0.75f || httpServerToggleInProgress)
                {
                    lastLocalServerRunningPollTime = now;
                    lastLocalServerRunning = MCPServiceLocator.Server.IsLocalHttpServerRunning();
                }
                localServerRunning = lastLocalServerRunning;
            }

            // Server button only controls server lifecycle (Start/Stop Server).
            // Session lifecycle is handled by the separate connectionToggleButton.
            bool shouldShowStop = localServerRunning;
            startHttpServerButton.text = shouldShowStop ? "停止 Server" : "启动 Server";
            // Note: Server logs may contain transient HTTP 400s on /mcp during startup probing and
            // CancelledError stack traces on shutdown when streaming requests are cancelled; this is expected.
            startHttpServerButton.EnableInClassList("server-running", localServerRunning);
            startHttpServerButton.SetEnabled(
                !httpServerToggleInProgress && (shouldShowStop || canStartLocalServer));
            startHttpServerButton.tooltip = httpLocalSelected
                ? (canStartLocalServer ? string.Empty : "HTTP Local 需要 localhost URL（localhost/127.0.0.1/0.0.0.0/::1）。")
                : string.Empty;

            // Stop button is no longer used; it may be null depending on UXML version.
            stopHttpServerButton?.SetEnabled(false);
        }

        private void RefreshHttpUi()
        {
            UpdateStartHttpButtonState();
            UpdateHttpServerCommandDisplay();
        }

        private async void OnHttpServerToggleClicked()
        {
            if (httpServerToggleInProgress)
            {
                return;
            }

            var bridgeService = MCPServiceLocator.Bridge;
            httpServerToggleInProgress = true;
            startHttpServerButton?.SetEnabled(false);

            try
            {
                // Check if a local server is running.
                bool serverRunning = IsHttpLocalSelected() && MCPServiceLocator.Server.IsLocalHttpServerRunning();

                if (serverRunning)
                {
                    // Stop Server: end session first (if active), then stop the server.
                    if (bridgeService.IsRunning)
                    {
                        await bridgeService.StopAsync();
                    }
                    bool stopped = MCPServiceLocator.Server.StopLocalHttpServer();
                    if (!stopped)
                    {
                    McpLog.Warn("停止 HTTP Server 失败，或当前没有正在运行的 Server。");
                    }
                }
                else
                {
                    // Start Server: launch the local HTTP server.
                    // When WE start the server, auto-start our session (we clearly want to use it).
                    // This differs from detecting an already-running server, where we require manual session start.
                    bool serverStarted = MCPServiceLocator.Server.StartLocalHttpServer();
                    if (serverStarted)
                    {
                        await TryAutoStartSessionAsync();
                    }
                    else
                    {
                        McpLog.Warn("启动本地 HTTP Server 失败。");
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"HTTP server toggle failed: {ex.Message}");
                EditorUtility.DisplayDialog("Server 操作失败", $"切换本地 HTTP Server 失败：\n\n{ex.Message}", "确定");
            }
            finally
            {
                httpServerToggleInProgress = false;
                RefreshHttpUi();
                UpdateConnectionStatus();
            }
        }

        private async Task TryAutoStartSessionAsync()
        {
            // Wait briefly for the HTTP server to become ready, then start the session.
            // This is called when THIS instance starts the server (not when detecting an external server).
            var bridgeService = MCPServiceLocator.Bridge;
            // Windows/dev mode may take much longer due to uv package resolution, fresh downloads, antivirus scans, etc.
            const int maxAttempts = 1;
            // Use shorter delays initially, then longer delays to allow server startup
            var shortDelay = TimeSpan.FromMilliseconds(500);
            var longDelay = TimeSpan.FromSeconds(3);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var delay = attempt < 6 ? shortDelay : longDelay;

                // Check if server is actually accepting connections
                bool serverDetected = MCPServiceLocator.Server.IsLocalHttpServerRunning();

                if (serverDetected)
                {
                    // Server detected - try to connect
                    bool started = await bridgeService.StartAsync();
                    if (started)
                    {
                        await VerifyBridgeConnectionAsync();
                        UpdateConnectionStatus();
                        return;
                    }
                }
                else if (attempt >= 20)
                {
                    // After many attempts without detection, try connecting anyway as a last resort.
                    // This handles cases where process detection fails but the server is actually running.
                    // Only try once every 3 attempts to avoid spamming connection errors (at attempts 20, 23, 26, 29).
                    if ((attempt - 20) % 3 != 0) continue;
                    
                    bool started = await bridgeService.StartAsync();
                    if (started)
                    {
                        await VerifyBridgeConnectionAsync();
                        UpdateConnectionStatus();
                        return;
                    }
                }

                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(delay);
                }
            }

            McpLog.Warn("HTTP Server 启动后未能自动连接 Session。");
        }

        private void PersistUnityPortFromField()
        {
            if (unityPortField == null)
            {
                return;
            }

            string input = unityPortField.text?.Trim();
            if (!int.TryParse(input, out int requestedPort) || requestedPort <= 0)
            {
                unityPortField.value = MCPServiceLocator.Bridge.CurrentPort.ToString();
                return;
            }

            try
            {
                int storedPort = PortManager.SetPreferredPort(requestedPort);
                EditorPrefs.SetInt(EditorPrefKeys.UnitySocketPort, storedPort);
                unityPortField.value = storedPort.ToString();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"保存 Unity Socket 端口失败: {ex.Message}");
                EditorUtility.DisplayDialog(
                    "端口不可用",
                    $"请求的端口无法使用：\n\n{ex.Message}\n\n已恢复为当前 Unity 端口。",
                    "确定");
                unityPortField.value = MCPServiceLocator.Bridge.CurrentPort.ToString();
            }
        }

        private async void OnConnectionToggleClicked()
        {
            if (connectionToggleInProgress)
            {
                return;
            }

            var bridgeService = MCPServiceLocator.Bridge;
            connectionToggleInProgress = true;
            connectionToggleButton?.SetEnabled(false);

            try
            {
                if (bridgeService.IsRunning)
                {
                    await bridgeService.StopAsync();
                }
                else
                {
                    bool started = await bridgeService.StartAsync();
                    if (started)
                    {
                        await VerifyBridgeConnectionAsync();
                    }
                    else
                    {
                        McpLog.Warn("启动 MCP 连接失败。");
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"切换 MCP 连接失败: {ex.Message}");
                EditorUtility.DisplayDialog("连接失败",
                    $"切换 MCP 连接失败：\n\n{ex.Message}",
                    "确定");
            }
            finally
            {
                connectionToggleInProgress = false;
                connectionToggleButton?.SetEnabled(true);
                UpdateConnectionStatus();
            }
        }

        private async void OnTestConnectionClicked()
        {
            await VerifyBridgeConnectionAsync();
        }

        private async Task EndOrphanedSessionAsync()
        {
            // Fire-and-forget cleanup of orphaned session when server is no longer running.
            // This prevents the UI from showing "Session Active" when the underlying server is gone.
            try
            {
                connectionToggleInProgress = true;
                connectionToggleButton?.SetEnabled(false);
                await MCPServiceLocator.Bridge.StopAsync();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"清理失效 Session 失败: {ex.Message}");
            }
            finally
            {
                connectionToggleInProgress = false;
                connectionToggleButton?.SetEnabled(true);
                UpdateConnectionStatus();
            }
        }

        public async Task VerifyBridgeConnectionAsync()
        {
            // Prevent concurrent verification calls
            if (verificationTask != null && !verificationTask.IsCompleted)
            {
                return;
            }

            verificationTask = VerifyBridgeConnectionInternalAsync();
            await verificationTask;
        }

        private async Task VerifyBridgeConnectionInternalAsync()
        {
            var bridgeService = MCPServiceLocator.Bridge;
            if (!bridgeService.IsRunning)
            {
                healthStatusLabel.text = HealthStatusUnknown;
                healthIndicator.RemoveFromClassList("healthy");
                healthIndicator.RemoveFromClassList("warning");
                healthIndicator.AddToClassList("unknown");
                
                // Only log if state changed
                if (lastHealthStatus != HealthStatusUnknown)
                {
                    McpLog.Warn("无法验证连接：Bridge 未运行。");
                    lastHealthStatus = HealthStatusUnknown;
                }
                return;
            }

            var result = await bridgeService.VerifyAsync();

            healthIndicator.RemoveFromClassList("healthy");
            healthIndicator.RemoveFromClassList("warning");
            healthIndicator.RemoveFromClassList("unknown");

            string newStatus;
            if (result.Success && result.PingSucceeded)
            {
                newStatus = HealthStatusHealthy;
                healthStatusLabel.text = newStatus;
                healthIndicator.AddToClassList("healthy");
                
                // Only log if state changed
                if (lastHealthStatus != newStatus)
                {
                    McpLog.Debug($"连接验证成功: {result.Message}");
                    lastHealthStatus = newStatus;
                }
            }
            else if (result.HandshakeValid)
            {
                newStatus = HealthStatusPingFailed;
                healthStatusLabel.text = newStatus;
                healthIndicator.AddToClassList("warning");
                
                // Log once per distinct warning state
                if (lastHealthStatus != newStatus)
                {
                    McpLog.Warn($"连接验证警告: {result.Message}");
                    lastHealthStatus = newStatus;
                }
            }
            else
            {
                newStatus = HealthStatusUnhealthy;
                healthStatusLabel.text = newStatus;
                healthIndicator.AddToClassList("warning");
                
                // Log once per distinct error state
                if (lastHealthStatus != newStatus)
                {
                    McpLog.Error($"连接验证失败: {result.Message}");
                    lastHealthStatus = newStatus;
                }
            }
        }
    }
}
