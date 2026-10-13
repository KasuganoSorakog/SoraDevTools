using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Services.Transport.Transports;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Records stdio bridge reload state without automatically reconnecting after reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class StdioBridgeReloadHandler
    {
        static StdioBridgeReloadHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            try
            {
                // Only persist resume intent when stdio is the active transport and the bridge is running.
                bool useHttp = McpProjectSettings.GetUseHttpTransport();
                // Check both TransportManager AND StdioBridgeHost directly, because CI starts via StdioBridgeHost
                // bypassing TransportManager state.
                bool tmRunning = MCPServiceLocator.TransportManager.IsRunning(TransportMode.Stdio);
                bool hostRunning = StdioBridgeHost.IsRunning;
                bool isRunning = tmRunning || hostRunning;
                bool shouldResume = !useHttp && isRunning;

                if (shouldResume)
                {
                    EditorPrefs.SetBool(EditorPrefKeys.ResumeStdioAfterReload, true);
                    EditorPrefs.SetBool(EditorPrefKeys.ManualReconnectRequired, true);

                    // Stop only the stdio bridge; leave HTTP untouched if it is running concurrently.
                    _ = MCPServiceLocator.TransportManager.StopAsync(TransportMode.Stdio);
                    
                    // Write reloading status so clients don't think we vanished
                    StdioBridgeHost.WriteHeartbeat(true, "reloading");
                }
                else
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to persist stdio reload flag: {ex.Message}");
            }
        }

        private static void OnAfterAssemblyReload()
        {
            bool resume = false;
            try
            {
                bool resumeFlag = EditorPrefs.GetBool(EditorPrefKeys.ResumeStdioAfterReload, false);
                bool useHttp = McpProjectSettings.GetUseHttpTransport();
                resume = resumeFlag && !useHttp;

                // If we're not going to resume, clear the flag immediately to avoid stuck "Resuming..." state
                if (!resume)
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to read stdio reload flag: {ex.Message}");
            }

            if (!resume)
            {
                return;
            }

            try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload); } catch { }
            McpLog.Warn("Stdio MCP bridge stopped for domain reload; 自动恢复已关闭，请在 MCP 面板手动连接。");
        }
    }
}
