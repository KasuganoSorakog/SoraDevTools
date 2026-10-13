using System;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Windows;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Records HTTP transport reload state without automatically reconnecting after reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class HttpBridgeReloadHandler
    {
        private static readonly SynchronizationContext EditorSynchronizationContext;

        static HttpBridgeReloadHandler()
        {
            EditorSynchronizationContext = SynchronizationContext.Current;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            try
            {
                var transport = MCPServiceLocator.TransportManager;
                bool shouldResume = transport.IsRunning(TransportMode.Http);

                if (shouldResume)
                {
                    EditorPrefs.SetBool(EditorPrefKeys.ResumeHttpAfterReload, true);
                    EditorPrefs.SetBool(EditorPrefKeys.ManualReconnectRequired, true);
                }
                else
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload);
                }

                if (shouldResume)
                {
                    var stopTask = transport.StopAsync(TransportMode.Http);
                    stopTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            McpLog.Warn($"Error stopping MCP bridge before reload: {t.Exception.GetBaseException().Message}");
                        }
                    }, TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to evaluate HTTP bridge reload state: {ex.Message}");
            }
        }

        private static void OnAfterAssemblyReload()
        {
            bool resume = false;
            try
            {
                // Only resume HTTP if it is still the selected transport.
                bool useHttp = McpProjectSettings.GetUseHttpTransport();
                resume = useHttp && EditorPrefs.GetBool(EditorPrefKeys.ResumeHttpAfterReload, false);
                if (resume)
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to read HTTP bridge reload flag: {ex.Message}");
                resume = false;
            }

            if (!resume)
            {
                return;
            }

            McpLog.Warn("HTTP MCP bridge stopped for domain reload; 自动恢复已关闭，请在 MCP 面板手动连接。");
        }

        private static void RunOnEditorThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (EditorSynchronizationContext != null)
            {
                EditorSynchronizationContext.Post(_ => action(), null);
                return;
            }

            action();
        }
    }

    [InitializeOnLoad]
    internal static class CentralServerAutoConnect
    {
        private const int MaxAttempts = 1;
        private const int InitialDelayMs = 1000;
        private const int RetryDelayMs = 2000;

        private static bool running;

        static CentralServerAutoConnect()
        {
            EditorApplication.delayCall += () => _ = TryAutoConnectAsync();
        }

        private static async Task TryAutoConnectAsync()
        {
            if (running)
            {
                return;
            }
            running = true;

            try
            {
                await Task.Delay(InitialDelayMs);

                var settings = McpProjectSettings.Load();
                if (!settings.CentralServerEnabled || !settings.CentralServerAutoStart || !settings.AutoConnect)
                {
                    return;
                }

                if (EditorPrefs.GetBool(EditorPrefKeys.ManualReconnectRequired, false))
                {
                    McpLog.Warn("UnityMCP 自动连接已跳过：上次连接因 reload/断线停止，请在 MCP 面板手动连接。");
                    return;
                }

                if (!settings.UseHttpTransport || !string.Equals(settings.HttpTransportScope, "local", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    if (!IsEditorReadyForAutoConnect())
                    {
                        continue;
                    }

                    if (MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http))
                    {
                        McpLog.Info("UnityMCP Session 已连接，跳过自动连接。");
                        return;
                    }

                    bool serverReady = MCPServiceLocator.Server.IsLocalHttpServerRunning();
                    if (!serverReady)
                    {
                        bool launchRequested = MCPServiceLocator.Server.StartLocalHttpServerQuiet();
                        if (!launchRequested)
                        {
                            continue;
                        }

                        serverReady = await WaitForServerReadyAsync();
                    }

                    if (!serverReady)
                    {
                        continue;
                    }

                    bool connected = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
                    if (connected)
                    {
                        McpLog.Info("UnityMCP 已在项目加载完成后自动连接 Session。");
                        MCPForUnityEditorWindow.RequestHealthVerification();
                        return;
                    }

                }

                McpLog.Warn("UnityMCP 自动连接超时：中心 Server 未就绪或 Session 建立失败。");
            }
            catch (Exception ex)
            {
                McpLog.Warn($"UnityMCP 自动连接失败：{ex.Message}");
            }
            finally
            {
                running = false;
            }
        }

        private static bool IsEditorReadyForAutoConnect()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return false;
            }

            try
            {
                var pipeline = Type.GetType("UnityEditor.Compilation.CompilationPipeline, UnityEditor");
                var prop = pipeline?.GetProperty("isCompiling", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null && (bool)prop.GetValue(null))
                {
                    return false;
                }
            }
            catch { }

            return true;
        }

        private static async Task<bool> WaitForServerReadyAsync()
        {
            await Task.Yield();
            return MCPServiceLocator.Server.IsLocalHttpServerRunning();
        }
    }
}
