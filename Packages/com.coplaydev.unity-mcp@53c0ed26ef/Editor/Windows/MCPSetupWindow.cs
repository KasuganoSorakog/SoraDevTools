using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Windows.Components.Setup;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows
{
    /// <summary>
    /// Legacy standalone shell for the UnityMCP dependency check panel.
    /// </summary>
    public class MCPSetupWindow : EditorWindow
    {
        private DependencyCheckResult dependencyResult;

        public static void ShowWindow(DependencyCheckResult dependencyResult = null)
        {
            var window = GetWindow<MCPSetupWindow>("UnityMCP 环境检查");
            window.minSize = new Vector2(480, 320);
            window.dependencyResult = dependencyResult ?? DependencyManager.CheckAllDependencies();
            window.Show();
        }

        public void CreateGUI()
        {
            string basePath = AssetPathUtility.GetMcpPackageRootPath();
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/MCPSetupWindow.uxml"
            );

            if (visualTree == null)
            {
                McpLog.Error($"加载 MCPSetupWindow.uxml 失败: {basePath}/Editor/Windows/MCPSetupWindow.uxml");
                return;
            }

            visualTree.CloneTree(rootVisualElement);
            _ = new McpSetupSection(rootVisualElement, dependencyResult);

            var doneButton = rootVisualElement.Q<Button>("done-button");
            if (doneButton != null)
            {
                doneButton.clicked += Close;
            }
        }

        private void OnEnable()
        {
            dependencyResult ??= DependencyManager.CheckAllDependencies();
        }
    }
}
