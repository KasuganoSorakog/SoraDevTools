using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Windows.Components.ClientConfig;
using MCPForUnity.Editor.Windows.Components.Connection;
using MCPForUnity.Editor.Windows.Components.Diagnostics;
using MCPForUnity.Editor.Windows.Components.Preferences;
using MCPForUnity.Editor.Windows.Components.Settings;
using MCPForUnity.Editor.Windows.Components.Setup;
using MCPForUnity.Editor.Windows.Components.Tools;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows
{
    public class MCPForUnityEditorWindow : EditorWindow
    {
        // Section controllers
        private McpSettingsSection settingsSection;
        private McpConnectionSection connectionSection;
        private McpClientConfigSection clientConfigSection;
        private McpToolsSection toolsSection;
        private McpDiagnosticsSection diagnosticsSection;
        private McpSetupSection setupSection;
        private McpEditorPrefsSection editorPrefsSection;

        private ToolbarToggle settingsTabToggle;
        private ToolbarToggle toolsTabToggle;
        private ToolbarToggle diagnosticsTabToggle;
        private ToolbarToggle localSetupTabToggle;
        private ToolbarToggle editorPrefsTabToggle;
        private VisualElement settingsPanel;
        private VisualElement toolsPanel;
        private VisualElement diagnosticsPanel;
        private VisualElement localSetupPanel;
        private VisualElement editorPrefsPanel;
        private Label headerProjectValue;
        private Label headerServerValue;
        private Label headerSessionValue;
        private Label headerEndpointValue;

        private static readonly HashSet<MCPForUnityEditorWindow> OpenWindows = new();
        private bool guiCreated = false;
        private bool toolsLoaded = false;
        private double lastRefreshTime = 0;
        private double lastHeaderRefreshTime = 0;
        private const double RefreshDebounceSeconds = 0.5;
        private const double HeaderRefreshDebounceSeconds = 0.75;

        private enum ActivePanel
        {
            Settings,
            Tools,
            Diagnostics,
            LocalSetup,
            EditorPrefs
        }

        internal static void CloseAllWindows()
        {
            var windows = OpenWindows.Where(window => window != null).ToArray();
            foreach (var window in windows)
            {
                window.Close();
            }
        }

        public static void ShowWindow()
        {
            var window = GetWindow<MCPForUnityEditorWindow>("MCP For Unity");
            window.minSize = new Vector2(500, 600);
        }

        public static void ShowLocalSetupPage(DependencyCheckResult dependencyResult = null)
        {
            var window = GetWindow<MCPForUnityEditorWindow>("MCP For Unity");
            window.minSize = new Vector2(500, 600);
            window.Show();
            window.SchedulePanelSwitch(ActivePanel.LocalSetup, dependencyResult);
        }

        public static void ShowEditorPrefsPage()
        {
            var window = GetWindow<MCPForUnityEditorWindow>("MCP For Unity");
            window.minSize = new Vector2(500, 600);
            window.Show();
            window.SchedulePanelSwitch(ActivePanel.EditorPrefs);
        }

        // Helper to check and manage open windows from other classes
        public static bool HasAnyOpenWindow()
        {
            return OpenWindows.Count > 0;
        }

        public static void CloseAllOpenWindows()
        {
            if (OpenWindows.Count == 0)
                return;

            // Copy to array to avoid modifying the collection while iterating
            var arr = new MCPForUnityEditorWindow[OpenWindows.Count];
            OpenWindows.CopyTo(arr);
            foreach (var window in arr)
            {
                try
                {
                    window?.Close();
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Error closing MCP window: {ex.Message}");
                }
            }
        }

        public void CreateGUI()
        {
            // Guard against repeated CreateGUI calls (e.g., domain reloads)
            if (guiCreated)
                return;

            string basePath = AssetPathUtility.GetMcpPackageRootPath();

            // Load main window UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/MCPForUnityEditorWindow.uxml"
            );

            if (visualTree == null)
            {
                McpLog.Error(
                    $"Failed to load UXML at: {basePath}/Editor/Windows/MCPForUnityEditorWindow.uxml"
                );
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            // Load common USS
            var commonStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"{basePath}/Editor/Windows/Components/Common.uss"
            );
            if (commonStyleSheet != null)
            {
                rootVisualElement.styleSheets.Add(commonStyleSheet);
            }

            // Load the main window sheet last so shell layout styles win over generic embedded page selectors.
            var mainStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"{basePath}/Editor/Windows/MCPForUnityEditorWindow.uss"
            );
            if (mainStyleSheet != null)
            {
                rootVisualElement.styleSheets.Add(mainStyleSheet);
            }

            settingsPanel = rootVisualElement.Q<VisualElement>("settings-panel");
            toolsPanel = rootVisualElement.Q<VisualElement>("tools-panel");
            diagnosticsPanel = rootVisualElement.Q<VisualElement>("diagnostics-panel");
            localSetupPanel = rootVisualElement.Q<VisualElement>("local-setup-panel");
            editorPrefsPanel = rootVisualElement.Q<VisualElement>("editor-prefs-panel");
            headerProjectValue = rootVisualElement.Q<Label>("header-project-value");
            headerServerValue = rootVisualElement.Q<Label>("header-server-value");
            headerSessionValue = rootVisualElement.Q<Label>("header-session-value");
            headerEndpointValue = rootVisualElement.Q<Label>("header-endpoint-value");
            var settingsContainer = rootVisualElement.Q<VisualElement>("settings-container");
            var toolsContainer = rootVisualElement.Q<VisualElement>("tools-container");
            var diagnosticsContainer = rootVisualElement.Q<VisualElement>("diagnostics-container");
            var localSetupContainer = rootVisualElement.Q<VisualElement>("local-setup-container");
            var editorPrefsContainer = rootVisualElement.Q<VisualElement>("editor-prefs-container");

            if (settingsPanel == null || toolsPanel == null || diagnosticsPanel == null || localSetupPanel == null || editorPrefsPanel == null)
            {
                McpLog.Error("Failed to find tab panels in UXML");
                return;
            }

            if (settingsContainer == null)
            {
                McpLog.Error("Failed to find settings-container in UXML");
                return;
            }

            if (toolsContainer == null)
            {
                McpLog.Error("Failed to find tools-container in UXML");
                return;
            }

            if (diagnosticsContainer == null)
            {
                McpLog.Error("Failed to find diagnostics-container in UXML");
                return;
            }

            if (localSetupContainer == null)
            {
                McpLog.Error("Failed to find local-setup-container in UXML");
                return;
            }

            if (editorPrefsContainer == null)
            {
                McpLog.Error("Failed to find editor-prefs-container in UXML");
                return;
            }

            SetupTabs();

            // Load and initialize Settings section
            var settingsTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Settings/McpSettingsSection.uxml"
            );
            if (settingsTree != null)
            {
                var settingsRoot = settingsTree.Instantiate();
                settingsContainer.Add(settingsRoot);
                settingsSection = new McpSettingsSection(settingsRoot);
                settingsSection.OnGitUrlChanged += () =>
                    clientConfigSection?.UpdateManualConfiguration();
                settingsSection.OnHttpServerCommandUpdateRequested += () =>
                    connectionSection?.UpdateHttpServerCommandDisplay();
            }

            // Load and initialize Connection section
            var connectionTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Connection/McpConnectionSection.uxml"
            );
            if (connectionTree != null)
            {
                var connectionRoot = connectionTree.Instantiate();
                settingsContainer.Add(connectionRoot);
                connectionSection = new McpConnectionSection(connectionRoot);
                connectionSection.OnManualConfigUpdateRequested += () =>
                    clientConfigSection?.UpdateManualConfiguration();
                connectionSection.OnTransportChanged += () =>
                    clientConfigSection?.RefreshSelectedClient(forceImmediate: true);
            }

            // Load and initialize Client Configuration section
            var clientConfigTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/ClientConfig/McpClientConfigSection.uxml"
            );
            if (clientConfigTree != null)
            {
                var clientConfigRoot = clientConfigTree.Instantiate();
                settingsContainer.Add(clientConfigRoot);
                clientConfigSection = new McpClientConfigSection(clientConfigRoot);
            }

            // Load and initialize Tools section
            var toolsTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Tools/McpToolsSection.uxml"
            );
            if (toolsTree != null)
            {
                var toolsRoot = toolsTree.Instantiate();
                toolsContainer.Add(toolsRoot);
                toolsSection = new McpToolsSection(toolsRoot);

                if (toolsTabToggle != null && toolsTabToggle.value)
                {
                    EnsureToolsLoaded();
                }
            }
            else
            {
                McpLog.Warn("Failed to load tools section UXML. Tool configuration will be unavailable.");
            }

            var diagnosticsTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Diagnostics/McpDiagnosticsSection.uxml"
            );
            if (diagnosticsTree != null)
            {
                var diagnosticsRoot = diagnosticsTree.Instantiate();
                diagnosticsContainer.Add(diagnosticsRoot);
                diagnosticsSection = new McpDiagnosticsSection(diagnosticsRoot);
            }
            else
            {
                McpLog.Warn("Failed to load diagnostics section UXML. Diagnostics panel will be unavailable.");
            }

            var setupTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/MCPSetupWindow.uxml"
            );
            if (setupTree != null)
            {
                var setupRoot = setupTree.Instantiate();
                localSetupContainer.Add(setupRoot);
                setupSection = new McpSetupSection(setupRoot);
            }
            else
            {
                McpLog.Warn("Failed to load setup section UXML. Local setup panel will be unavailable.");
            }

            var editorPrefsTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/EditorPrefs/EditorPrefsWindow.uxml"
            );
            var editorPrefsItemTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/EditorPrefs/EditorPrefItem.uxml"
            );
            if (editorPrefsTree != null && editorPrefsItemTree != null)
            {
                var editorPrefsRoot = editorPrefsTree.Instantiate();
                editorPrefsContainer.Add(editorPrefsRoot);
                editorPrefsSection = new McpEditorPrefsSection(editorPrefsRoot, editorPrefsItemTree);
            }
            else
            {
                McpLog.Warn("Failed to load EditorPrefs section UXML. EditorPrefs panel will be unavailable.");
            }

            guiCreated = true;

            // Initial updates
            RefreshAllData();
            UpdateHeaderStatus(force: true);
        }

        private void EnsureToolsLoaded()
        {
            if (toolsLoaded)
            {
                return;
            }

            if (toolsSection == null)
            {
                return;
            }

            toolsLoaded = true;
            toolsSection.Refresh();
        }

        private void SchedulePanelSwitch(ActivePanel panel, DependencyCheckResult dependencyResult = null)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                {
                    return;
                }

                if (dependencyResult != null && setupSection != null)
                {
                    setupSection.Refresh(dependencyResult);
                }

                SwitchPanel(panel);
            };
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            OpenWindows.Add(this);
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            OpenWindows.Remove(this);
            guiCreated = false;
            toolsLoaded = false;
        }

        private void OnFocus()
        {
            // Only refresh data if UI is built
            if (rootVisualElement == null || rootVisualElement.childCount == 0)
                return;

            RefreshAllData();
        }

        private void OnEditorUpdate()
        {
            if (rootVisualElement == null || rootVisualElement.childCount == 0)
                return;

            connectionSection?.UpdateConnectionStatus();
            UpdateHeaderStatus();
        }

        private void RefreshAllData()
        {
            // Debounce rapid successive calls (e.g., from OnFocus being called multiple times)
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - lastRefreshTime < RefreshDebounceSeconds)
            {
                return;
            }
            lastRefreshTime = currentTime;

            connectionSection?.UpdateConnectionStatus();
            UpdateHeaderStatus(force: true);

            if (MCPServiceLocator.Bridge.IsRunning)
            {
                _ = connectionSection?.VerifyBridgeConnectionAsync();
            }

            settingsSection?.UpdatePathOverrides();
            clientConfigSection?.RefreshSelectedClient();
        }

        private void UpdateHeaderStatus(bool force = false)
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (!force && currentTime - lastHeaderRefreshTime < HeaderRefreshDebounceSeconds)
            {
                return;
            }

            lastHeaderRefreshTime = currentTime;

            SetHeaderText(headerProjectValue, ProjectIdentityUtility.GetProjectName(), "status-ok");
            SetHeaderText(headerEndpointValue, HttpEndpointUtility.GetBaseUrl(), "status-neutral");

            bool serverRunning = false;
            try
            {
                serverRunning = MCPServiceLocator.Server.IsLocalHttpServerRunning();
            }
            catch (Exception ex)
            {
                McpLog.Debug($"Header server status check failed: {ex.Message}");
            }

            SetHeaderText(
                headerServerValue,
                serverRunning ? "Running" : "Stopped",
                serverRunning ? "status-ok" : "status-warning");

            bool sessionRunning = MCPServiceLocator.Bridge.IsRunning;
            SetHeaderText(
                headerSessionValue,
                sessionRunning ? "Connected" : "Idle",
                sessionRunning ? "status-ok" : "status-warning");
        }

        private static void SetHeaderText(Label label, string text, string statusClass)
        {
            if (label == null)
            {
                return;
            }

            label.text = string.IsNullOrWhiteSpace(text) ? "-" : text;
            label.RemoveFromClassList("status-ok");
            label.RemoveFromClassList("status-warning");
            label.RemoveFromClassList("status-bad");
            label.RemoveFromClassList("status-neutral");
            label.AddToClassList(statusClass);
        }

        private void SetupTabs()
        {
            settingsTabToggle = rootVisualElement.Q<ToolbarToggle>("settings-tab");
            toolsTabToggle = rootVisualElement.Q<ToolbarToggle>("tools-tab");
            diagnosticsTabToggle = rootVisualElement.Q<ToolbarToggle>("diagnostics-tab");
            localSetupTabToggle = rootVisualElement.Q<ToolbarToggle>("local-setup-tab");
            editorPrefsTabToggle = rootVisualElement.Q<ToolbarToggle>("editor-prefs-tab");

            settingsPanel?.RemoveFromClassList("hidden");
            toolsPanel?.RemoveFromClassList("hidden");
            diagnosticsPanel?.RemoveFromClassList("hidden");
            localSetupPanel?.RemoveFromClassList("hidden");
            editorPrefsPanel?.RemoveFromClassList("hidden");

            if (settingsTabToggle != null)
            {
                settingsTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue)
                    {
                        if (toolsTabToggle != null && !toolsTabToggle.value)
                        {
                            settingsTabToggle.SetValueWithoutNotify(true);
                        }
                        return;
                    }

                    SwitchPanel(ActivePanel.Settings);
                });
            }

            if (toolsTabToggle != null)
            {
                toolsTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue)
                    {
                        if (settingsTabToggle != null && !settingsTabToggle.value)
                        {
                            toolsTabToggle.SetValueWithoutNotify(true);
                        }
                        return;
                    }

                    SwitchPanel(ActivePanel.Tools);
                });
            }

            if (diagnosticsTabToggle != null)
            {
                diagnosticsTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue)
                    {
                        EnsureOneTabSelected(diagnosticsTabToggle);
                        return;
                    }

                    SwitchPanel(ActivePanel.Diagnostics);
                });
            }

            if (localSetupTabToggle != null)
            {
                localSetupTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue)
                    {
                        EnsureOneTabSelected(localSetupTabToggle);
                        return;
                    }

                    SwitchPanel(ActivePanel.LocalSetup);
                });
            }

            if (editorPrefsTabToggle != null)
            {
                editorPrefsTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue)
                    {
                        EnsureOneTabSelected(editorPrefsTabToggle);
                        return;
                    }

                    SwitchPanel(ActivePanel.EditorPrefs);
                });
            }

            var savedPanel = EditorPrefs.GetString(EditorPrefKeys.EditorWindowActivePanel, ActivePanel.Settings.ToString());
            if (!Enum.TryParse(savedPanel, out ActivePanel initialPanel))
            {
                initialPanel = ActivePanel.Settings;
            }

            SwitchPanel(initialPanel);
        }

        private void SwitchPanel(ActivePanel panel)
        {
            bool showSettings = panel == ActivePanel.Settings;
            bool showTools = panel == ActivePanel.Tools;
            bool showDiagnostics = panel == ActivePanel.Diagnostics;
            bool showLocalSetup = panel == ActivePanel.LocalSetup;
            bool showEditorPrefs = panel == ActivePanel.EditorPrefs;

            if (settingsPanel != null)
            {
                settingsPanel.style.display = showSettings ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (toolsPanel != null)
            {
                toolsPanel.style.display = showTools ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (diagnosticsPanel != null)
            {
                diagnosticsPanel.style.display = showDiagnostics ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (localSetupPanel != null)
            {
                localSetupPanel.style.display = showLocalSetup ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (editorPrefsPanel != null)
            {
                editorPrefsPanel.style.display = showEditorPrefs ? DisplayStyle.Flex : DisplayStyle.None;
            }

            settingsTabToggle?.SetValueWithoutNotify(showSettings);
            toolsTabToggle?.SetValueWithoutNotify(showTools);
            diagnosticsTabToggle?.SetValueWithoutNotify(showDiagnostics);
            localSetupTabToggle?.SetValueWithoutNotify(showLocalSetup);
            editorPrefsTabToggle?.SetValueWithoutNotify(showEditorPrefs);

            if (showTools)
            {
                EnsureToolsLoaded();
            }

            if (showDiagnostics)
            {
                diagnosticsSection?.Refresh();
            }

            if (showLocalSetup)
            {
                setupSection?.Refresh();
            }

            if (showEditorPrefs)
            {
                editorPrefsSection?.Refresh();
            }

            EditorPrefs.SetString(EditorPrefKeys.EditorWindowActivePanel, panel.ToString());
        }

        private void EnsureOneTabSelected(ToolbarToggle fallback)
        {
            if ((settingsTabToggle?.value ?? false) ||
                (toolsTabToggle?.value ?? false) ||
                (diagnosticsTabToggle?.value ?? false) ||
                (localSetupTabToggle?.value ?? false) ||
                (editorPrefsTabToggle?.value ?? false))
            {
                return;
            }

            fallback.SetValueWithoutNotify(true);
        }

        internal static void RequestHealthVerification()
        {
            foreach (var window in OpenWindows)
            {
                window?.ScheduleHealthCheck();
            }
        }

        private void ScheduleHealthCheck()
        {
            EditorApplication.delayCall += async () =>
            {
                // Ensure window and components are still valid before execution
                if (this == null || connectionSection == null)
                {
                    return;
                }

                try
                {
                    await connectionSection.VerifyBridgeConnectionAsync();
                }
                catch (Exception ex)
                {
                    // Log but don't crash if verification fails during cleanup
                    McpLog.Warn($"Health check verification failed: {ex.Message}");
                }
            };
        }
    }
}
