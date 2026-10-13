using MCPForUnity.Editor.Windows;
using UnityEditor;

namespace MCPForUnity.Editor.MenuItems
{
    public static class MCPForUnityMenu
    {
        [MenuItem("Window/Unity MCP Window %#m", priority = 1)]
        public static void ToggleMCPWindow()
        {
            if (MCPForUnityEditorWindow.HasAnyOpenWindow())
            {
                MCPForUnityEditorWindow.CloseAllOpenWindows();
            }
            else
            {
                MCPForUnityEditorWindow.ShowWindow();
            }
        }
    }
}
