using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Windows.Components.Preferences;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows
{
    /// <summary>
    /// Legacy standalone shell for the UnityMCP EditorPrefs panel.
    /// </summary>
    public class EditorPrefsWindow : EditorWindow
    {
        public static void ShowWindow()
        {
            MCPForUnityEditorWindow.ShowEditorPrefsPage();
        }

        public void CreateGUI()
        {
            string basePath = AssetPathUtility.GetMcpPackageRootPath();
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/EditorPrefs/EditorPrefsWindow.uxml"
            );

            if (visualTree == null)
            {
                McpLog.Error("加载 EditorPrefsWindow.uxml 模板失败");
                return;
            }

            var itemTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/EditorPrefs/EditorPrefItem.uxml"
            );

            if (itemTemplate == null)
            {
                McpLog.Error("加载 EditorPrefItem.uxml 模板失败");
                return;
            }

            visualTree.CloneTree(rootVisualElement);
            _ = new McpEditorPrefsSection(rootVisualElement, itemTemplate);
        }
    }
}
