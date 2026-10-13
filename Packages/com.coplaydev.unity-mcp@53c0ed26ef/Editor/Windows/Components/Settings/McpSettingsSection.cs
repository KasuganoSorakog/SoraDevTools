using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Settings
{
    /// <summary>
    /// Controller for the Settings section of the MCP For Unity editor window.
    /// Handles version display, debug logs, validation level, and advanced path overrides.
    /// </summary>
    public class McpSettingsSection
    {
        // UI Elements
        private Label versionLabel;
        private Toggle debugLogsToggle;
        private DropdownField validationLevelField;
        private Label validationDescription;
        private Foldout advancedSettingsFoldout;
        private TextField uvxPathOverride;
        private Button browseUvxButton;
        private Button clearUvxButton;
        private VisualElement uvxPathStatus;
        private TextField gitUrlOverride;
        private Button browseGitUrlButton;
        private Button clearGitUrlButton;
        private Toggle devModeForceRefreshToggle;
        private TextField deploySourcePath;
        private Button browseDeploySourceButton;
        private Button clearDeploySourceButton;
        private Button deployButton;
        private Button deployRestoreButton;
        private Label deployTargetLabel;
        private Label deployBackupLabel;
        private Label deployStatusLabel;

        // Data
        private ValidationLevel currentValidationLevel = ValidationLevel.Standard;

        // Events
        public event Action OnGitUrlChanged;
        public event Action OnHttpServerCommandUpdateRequested;

        // Validation levels
        private enum ValidationLevel
        {
            Basic,
            Standard,
            Comprehensive,
            Strict
        }

        private static readonly List<string> ValidationLevelLabels = new()
        {
            "基础",
            "标准",
            "完整",
            "严格"
        };

        public VisualElement Root { get; private set; }

        public McpSettingsSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            versionLabel = Root.Q<Label>("version-label");
            debugLogsToggle = Root.Q<Toggle>("debug-logs-toggle");
            validationLevelField = Root.Q<DropdownField>("validation-level");
            validationDescription = Root.Q<Label>("validation-description");
            advancedSettingsFoldout = Root.Q<Foldout>("advanced-settings-foldout");
            uvxPathOverride = Root.Q<TextField>("uv-path-override");
            browseUvxButton = Root.Q<Button>("browse-uv-button");
            clearUvxButton = Root.Q<Button>("clear-uv-button");
            uvxPathStatus = Root.Q<VisualElement>("uv-path-status");
            gitUrlOverride = Root.Q<TextField>("git-url-override");
            browseGitUrlButton = Root.Q<Button>("browse-git-url-button");
            clearGitUrlButton = Root.Q<Button>("clear-git-url-button");
            devModeForceRefreshToggle = Root.Q<Toggle>("dev-mode-force-refresh-toggle");
            deploySourcePath = Root.Q<TextField>("deploy-source-path");
            browseDeploySourceButton = Root.Q<Button>("browse-deploy-source-button");
            clearDeploySourceButton = Root.Q<Button>("clear-deploy-source-button");
            deployButton = Root.Q<Button>("deploy-button");
            deployRestoreButton = Root.Q<Button>("deploy-restore-button");
            deployTargetLabel = Root.Q<Label>("deploy-target-label");
            deployBackupLabel = Root.Q<Label>("deploy-backup-label");
            deployStatusLabel = Root.Q<Label>("deploy-status-label");
        }

        private void InitializeUI()
        {
            UpdateVersionLabel();

            bool debugEnabled = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            debugLogsToggle.value = debugEnabled;
            McpLog.SetDebugLoggingEnabled(debugEnabled);

            validationLevelField.choices = ValidationLevelLabels;
            int savedLevel = EditorPrefs.GetInt(EditorPrefKeys.ValidationLevel, 1);
            currentValidationLevel = (ValidationLevel)Mathf.Clamp(savedLevel, 0, 3);
            validationLevelField.index = (int)currentValidationLevel;
            UpdateValidationDescription();

            advancedSettingsFoldout.value = false;
            gitUrlOverride.value = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            EditorPrefs.DeleteKey(EditorPrefKeys.DevModeForceServerRefresh);
            devModeForceRefreshToggle.value = false;
            devModeForceRefreshToggle.style.display = DisplayStyle.None;

            UpdateDeploymentSection();
        }

        private void RegisterCallbacks()
        {
            debugLogsToggle.RegisterValueChangedCallback(evt =>
            {
                McpLog.SetDebugLoggingEnabled(evt.newValue);
            });

            validationLevelField.RegisterValueChangedCallback(evt =>
            {
                int selectedIndex = Mathf.Clamp(validationLevelField.index, 0, ValidationLevelLabels.Count - 1);
                currentValidationLevel = (ValidationLevel)selectedIndex;
                EditorPrefs.SetInt(EditorPrefKeys.ValidationLevel, (int)currentValidationLevel);
                UpdateValidationDescription();
            });

            browseUvxButton.clicked += OnBrowseUvxClicked;
            clearUvxButton.clicked += OnClearUvxClicked;

            browseGitUrlButton.clicked += OnBrowseGitUrlClicked;

            gitUrlOverride.RegisterValueChangedCallback(evt =>
            {
                string url = evt.newValue?.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
                }
                else
                {
                    EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, url);
                }
                OnGitUrlChanged?.Invoke();
                OnHttpServerCommandUpdateRequested?.Invoke();
            });

            clearGitUrlButton.clicked += () =>
            {
                gitUrlOverride.value = string.Empty;
                EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
                OnGitUrlChanged?.Invoke();
                OnHttpServerCommandUpdateRequested?.Invoke();
            };

            devModeForceRefreshToggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.DevModeForceServerRefresh);
                devModeForceRefreshToggle.SetValueWithoutNotify(false);
                OnHttpServerCommandUpdateRequested?.Invoke();
            });

            deploySourcePath.RegisterValueChangedCallback(evt =>
            {
                string path = evt.newValue?.Trim();
                if (string.IsNullOrEmpty(path) || path == "未设置")
                {
                    return;
                }
                
                try
                {
                    MCPServiceLocator.Deployment.SetStoredSourcePath(path);
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("源目录无效", ex.Message, "确定");
                    UpdateDeploymentSection();
                }
            });

            browseDeploySourceButton.clicked += OnBrowseDeploySourceClicked;
            clearDeploySourceButton.clicked += OnClearDeploySourceClicked;
            deployButton.clicked += OnDeployClicked;
            deployRestoreButton.clicked += OnRestoreBackupClicked;
        }

        public void UpdatePathOverrides()
        {
            var pathService = MCPServiceLocator.Paths;

            bool hasOverride = pathService.HasUvxPathOverride;
            string uvxPath = hasOverride ? pathService.GetUvxPath() : null;
            uvxPathOverride.value = hasOverride
                ? (uvxPath ?? "已设置覆盖路径，但当前无效")
                : "uvx（使用系统 PATH 自动查找）";

            uvxPathStatus.RemoveFromClassList("valid");
            uvxPathStatus.RemoveFromClassList("invalid");
            if (hasOverride)
            {
                if (!string.IsNullOrEmpty(uvxPath) && File.Exists(uvxPath))
                {
                    uvxPathStatus.AddToClassList("valid");
                }
                else
                {
                    uvxPathStatus.AddToClassList("invalid");
                }
            }
            else
            {
                uvxPathStatus.AddToClassList("valid");
            }

            gitUrlOverride.value = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            EditorPrefs.DeleteKey(EditorPrefKeys.DevModeForceServerRefresh);
            devModeForceRefreshToggle.value = false;
            UpdateDeploymentSection();
        }

        private void UpdateVersionLabel()
        {
            string currentVersion = AssetPathUtility.GetPackageVersion();
            versionLabel.text = $"v{currentVersion}（私有定制版）";
            versionLabel.style.color = StyleKeyword.Null;
            versionLabel.tooltip = "当前为项目内私有定制版本，已关闭远端版本检查和自动升级提示。";
        }

        private void UpdateValidationDescription()
        {
            validationDescription.text = GetValidationLevelDescription((int)currentValidationLevel);
        }

        private string GetValidationLevelDescription(int index)
        {
            return index switch
            {
                0 => "仅做基础语法检查（括号、引号、注释等）。",
                1 => "语法检查 + Unity 常见实践和警告。",
                2 => "完整检查 + 语义分析和性能风险提示。",
                3 => "最严格校验，包含命名空间和类型解析（需要 Roslyn 支持）。",
                _ => "标准校验。"
            };
        }

        private void OnBrowseUvxClicked()
        {
            string suggested = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "/opt/homebrew/bin"
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string picked = EditorUtility.OpenFilePanel("选择 uvx 可执行文件", suggested, "");
            if (!string.IsNullOrEmpty(picked))
            {
                try
                {
                    MCPServiceLocator.Paths.SetUvxPathOverride(picked);
                    UpdatePathOverrides();
                    McpLog.Info($"uvx 路径已设置为: {picked}");
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("路径无效", ex.Message, "确定");
                }
            }
        }

        private void OnClearUvxClicked()
        {
            MCPServiceLocator.Paths.ClearUvxPathOverride();
            UpdatePathOverrides();
            McpLog.Info("uvx 路径覆盖已清除");
        }

        private void OnBrowseGitUrlClicked()
        {
            string picked = EditorUtility.OpenFolderPanel("选择本地 MCP Server 源码目录", string.Empty, string.Empty);
            if (!string.IsNullOrEmpty(picked))
            {
                gitUrlOverride.value = picked;
                EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, picked);
                OnGitUrlChanged?.Invoke();
                OnHttpServerCommandUpdateRequested?.Invoke();
                McpLog.Info($"本地 Server 源码目录已设置为: {picked}");
            }
        }

        private void UpdateDeploymentSection()
        {
            var deployService = MCPServiceLocator.Deployment;

            string sourcePath = deployService.GetStoredSourcePath();
            deploySourcePath.value = sourcePath ?? string.Empty;

            deployTargetLabel.text = $"目标位置: {deployService.GetTargetDisplayPath()}";

            string backupPath = deployService.GetLastBackupPath();
            if (deployService.HasBackup())
            {
                // Use forward slashes to avoid backslash escape sequence issues in UI text
                deployBackupLabel.text = $"最近备份: {backupPath?.Replace('\\', '/')}";
            }
            else
            {
                deployBackupLabel.text = "最近备份: 无";
            }

            deployRestoreButton?.SetEnabled(deployService.HasBackup());
        }

        private void OnBrowseDeploySourceClicked()
        {
            string picked = EditorUtility.OpenFolderPanel("选择 MCPForUnity 包目录", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            try
            {
                MCPServiceLocator.Deployment.SetStoredSourcePath(picked);
                SetDeployStatus($"源目录已设置: {picked}");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("源目录无效", ex.Message, "确定");
                SetDeployStatus("源目录选择失败");
            }

            UpdateDeploymentSection();
        }

        private void OnClearDeploySourceClicked()
        {
            MCPServiceLocator.Deployment.ClearStoredSourcePath();
            UpdateDeploymentSection();
            SetDeployStatus("源目录已清除");
        }

        private void OnDeployClicked()
        {
            var result = MCPServiceLocator.Deployment.DeployFromStoredSource();
            SetDeployStatus(result.Message, !result.Success);

            if (!result.Success)
            {
                EditorUtility.DisplayDialog("部署失败", result.Message, "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("部署完成", result.Message + (string.IsNullOrEmpty(result.BackupPath) ? string.Empty : $"\n备份: {result.BackupPath}"), "确定");
            }

            UpdateDeploymentSection();
        }

        private void OnRestoreBackupClicked()
        {
            var result = MCPServiceLocator.Deployment.RestoreLastBackup();
            SetDeployStatus(result.Message, !result.Success);

            if (!result.Success)
            {
                EditorUtility.DisplayDialog("恢复失败", result.Message, "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("恢复完成", result.Message, "确定");
            }

            UpdateDeploymentSection();
        }

        private void SetDeployStatus(string message, bool isError = false)
        {
            if (deployStatusLabel == null)
            {
                return;
            }

            deployStatusLabel.text = message;
            deployStatusLabel.style.color = isError
                ? new StyleColor(new Color(0.85f, 0.2f, 0.2f))
                : StyleKeyword.Null;
        }
    }
}
