using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport.Transports;
using UnityEditor;

namespace MCPForUnity.Editor
{
    public static class McpCiBoot
    {
        public static void StartStdioForCi()
        {
            try 
            { 
                McpProjectSettings.SetUseHttpTransport(false);
                EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false); 
            }
            catch { /* ignore */ }

            StdioBridgeHost.StartAutoConnect();
        }
    }
}
