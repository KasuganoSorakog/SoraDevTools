using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Setup;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Setup
{
    /// <summary>
    /// Reusable dependency-check panel for the main MCP window and legacy setup window.
    /// </summary>
    public sealed class McpSetupSection
    {
        private readonly DependencyCheckResult initialResult;
        private VisualElement pythonIndicator;
        private Label pythonVersion;
        private Label pythonDetails;
        private VisualElement uvIndicator;
        private Label uvVersion;
        private Label uvDetails;
        private Label statusMessage;
        private VisualElement installationSection;
        private Label installationInstructions;
        private Button openPythonLinkButton;
        private Button openUvLinkButton;
        private Button refreshButton;
        private Button doneButton;
        private DependencyCheckResult dependencyResult;

        public McpSetupSection(VisualElement root, DependencyCheckResult dependencyResult = null)
        {
            Root = root;
            initialResult = dependencyResult;
            CacheUIElements();
            RegisterCallbacks();
            Refresh(initialResult);
        }

        public VisualElement Root { get; }

        public void Refresh(DependencyCheckResult result = null)
        {
            dependencyResult = result ?? DependencyManager.CheckAllDependencies();
            UpdateUI();
        }

        private void CacheUIElements()
        {
            pythonIndicator = Root.Q<VisualElement>("python-indicator");
            pythonVersion = Root.Q<Label>("python-version");
            pythonDetails = Root.Q<Label>("python-details");
            uvIndicator = Root.Q<VisualElement>("uv-indicator");
            uvVersion = Root.Q<Label>("uv-version");
            uvDetails = Root.Q<Label>("uv-details");
            statusMessage = Root.Q<Label>("status-message");
            installationSection = Root.Q<VisualElement>("installation-section");
            installationInstructions = Root.Q<Label>("installation-instructions");
            openPythonLinkButton = Root.Q<Button>("open-python-link-button");
            openUvLinkButton = Root.Q<Button>("open-uv-link-button");
            refreshButton = Root.Q<Button>("refresh-button");
            doneButton = Root.Q<Button>("done-button");
        }

        private void RegisterCallbacks()
        {
            if (refreshButton != null)
            {
                refreshButton.clicked += () => Refresh();
            }

            if (doneButton != null)
            {
                doneButton.clicked += SetupWindowService.MarkSetupCompleted;
            }

            if (openPythonLinkButton != null)
            {
                openPythonLinkButton.clicked += () =>
                {
                    var (pythonUrl, _) = DependencyManager.GetInstallationUrls();
                    Application.OpenURL(pythonUrl);
                };
            }

            if (openUvLinkButton != null)
            {
                openUvLinkButton.clicked += () =>
                {
                    var (_, uvUrl) = DependencyManager.GetInstallationUrls();
                    Application.OpenURL(uvUrl);
                };
            }
        }

        private void UpdateUI()
        {
            if (dependencyResult == null)
            {
                return;
            }

            var pythonDep = dependencyResult.Dependencies.Find(d => d.Name == "Python");
            if (pythonDep != null)
            {
                UpdateDependencyStatus(pythonIndicator, pythonVersion, pythonDetails, pythonDep);
            }

            var uvDep = dependencyResult.Dependencies.Find(d => d.Name == "uv Package Manager");
            if (uvDep != null)
            {
                UpdateDependencyStatus(uvIndicator, uvVersion, uvDetails, uvDep);
            }

            if (dependencyResult.IsSystemReady)
            {
                statusMessage.text = "依赖已就绪，UnityMCP 可以使用。";
                statusMessage.style.color = new StyleColor(Color.green);
                installationSection.style.display = DisplayStyle.None;
            }
            else
            {
                statusMessage.text = "缺少必要依赖。请安装下方列出的依赖后重新检查。";
                statusMessage.style.color = new StyleColor(new Color(1f, 0.6f, 0f));
                installationSection.style.display = DisplayStyle.Flex;
                installationInstructions.text = DependencyManager.GetInstallationRecommendations();
            }
        }

        private static void UpdateDependencyStatus(VisualElement indicator, Label versionLabel, Label detailsLabel, DependencyStatus dep)
        {
            if (dep.IsAvailable)
            {
                indicator.RemoveFromClassList("invalid");
                indicator.AddToClassList("valid");
                versionLabel.text = $"v{dep.Version}";
                detailsLabel.text = dep.Details ?? "可用";
                detailsLabel.style.color = new StyleColor(Color.gray);
            }
            else
            {
                indicator.RemoveFromClassList("valid");
                indicator.AddToClassList("invalid");
                versionLabel.text = "未找到";
                detailsLabel.text = dep.ErrorMessage ?? "不可用";
                detailsLabel.style.color = new StyleColor(Color.red);
            }
        }
    }
}
