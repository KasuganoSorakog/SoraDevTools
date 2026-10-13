using System;
using System.IO;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using UnityEngine;

#if UNITY_STANDALONE_WIN

namespace SoraTools
{
    // ── Native Structs ──

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    // ── COM Callback Interfaces (we implement these) ──

    [ComVisible(true)]
    [Guid("4E8A3389-C9D8-4BD2-B6B5-124FEE6CC14D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
    {
        [PreserveSig]
        int Invoke(int errorCode,
            [MarshalAs(UnmanagedType.Interface)] ICoreWebView2Environment createdEnvironment);
    }

    [ComVisible(true)]
    [Guid("6C4819F3-C9B7-4260-8127-C9F5BDE7F68C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
    {
        [PreserveSig]
        int Invoke(int errorCode,
            [MarshalAs(UnmanagedType.Interface)] ICoreWebView2Controller createdController);
    }

    // ── COM Interfaces (we receive and call these) ──

    /// <summary>
    /// ICoreWebView2Environment — vtable after IUnknown:
    /// 0: CreateCoreWebView2Controller
    /// 1: CreateCoreWebView2WebResourceResponse
    /// 2: get_BrowserVersionString
    /// 3: add_NewBrowserVersionAvailable
    /// 4: remove_NewBrowserVersionAvailable
    /// We only need slot 0.
    /// </summary>
    [ComImport]
    [Guid("B96D755E-0319-4E92-A296-23436F46A1FC")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2Environment
    {
        void CreateCoreWebView2Controller(
            IntPtr parentWindow,
            [MarshalAs(UnmanagedType.Interface)]
            ICoreWebView2CreateCoreWebView2ControllerCompletedHandler handler);
    }

    /// <summary>
    /// ICoreWebView2Controller — vtable after IUnknown (23 methods):
    ///  0: get_IsVisible          13: remove_GotFocus
    ///  1: put_IsVisible          14: add_LostFocus
    ///  2: get_Bounds             15: remove_LostFocus
    ///  3: put_Bounds             16: add_AcceleratorKeyPressed
    ///  4: get_ZoomFactor         17: remove_AcceleratorKeyPressed
    ///  5: put_ZoomFactor         18: get_ParentWindow
    ///  6: add_ZoomFactorChanged  19: put_ParentWindow
    ///  7: remove_ZoomFactorChanged 20: NotifyParentWindowPositionChanged
    ///  8: SetBoundsAndZoomFactor 21: Close
    ///  9: MoveFocus              22: get_CoreWebView2
    /// 10: add_MoveFocusRequested
    /// 11: remove_MoveFocusRequested
    /// 12: add_GotFocus
    /// All must be declared in order. Unused methods are stubs.
    /// </summary>
    [ComImport]
    [Guid("4D00C0D1-9434-4EB6-8078-8697A560334F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2Controller
    {
        // 0
        void get_IsVisible(out int isVisible);
        // 1
        void put_IsVisible(int isVisible);
        // 2
        void get_Bounds(out RECT bounds);
        // 3
        void put_Bounds(RECT bounds);
        // 4
        void get_ZoomFactor(out double zoomFactor);
        // 5
        void put_ZoomFactor(double zoomFactor);
        // 6
        void add_ZoomFactorChanged(IntPtr eventHandler, out long token);
        // 7
        void remove_ZoomFactorChanged(long token);
        // 8
        void SetBoundsAndZoomFactor(RECT bounds, double zoomFactor);
        // 9
        void MoveFocus(int reason);
        // 10
        void add_MoveFocusRequested(IntPtr eventHandler, out long token);
        // 11
        void remove_MoveFocusRequested(long token);
        // 12
        void add_GotFocus(IntPtr eventHandler, out long token);
        // 13
        void remove_GotFocus(long token);
        // 14
        void add_LostFocus(IntPtr eventHandler, out long token);
        // 15
        void remove_LostFocus(long token);
        // 16
        void add_AcceleratorKeyPressed(IntPtr eventHandler, out long token);
        // 17
        void remove_AcceleratorKeyPressed(long token);
        // 18
        void get_ParentWindow(out IntPtr parentWindow);
        // 19
        void put_ParentWindow(IntPtr parentWindow);
        // 20
        void NotifyParentWindowPositionChanged();
        // 21
        void Close();
        // 22
        void get_CoreWebView2(
            [MarshalAs(UnmanagedType.Interface)] out ICoreWebView2 coreWebView2);
    }

    /// <summary>
    /// ICoreWebView2 — vtable after IUnknown (28 methods declared up to CapturePreview).
    /// All must be declared in order. Unused methods are stubs for correct vtable layout.
    /// </summary>
    [ComImport]
    [Guid("76ECEACB-0462-4D94-AC83-423A6793775E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2
    {
        // 0
        void get_Settings(out IntPtr settings);
        // 1
        void get_Source([MarshalAs(UnmanagedType.LPWStr)] out string source);
        // 2
        void Navigate([MarshalAs(UnmanagedType.LPWStr)] string uri);
        // 3
        void NavigateToString([MarshalAs(UnmanagedType.LPWStr)] string htmlContent);
        // 4
        void add_NavigationStarting(IntPtr handler, out long token);
        // 5
        void remove_NavigationStarting(long token);
        // 6
        void add_ContentLoading(IntPtr handler, out long token);
        // 7
        void remove_ContentLoading(long token);
        // 8
        void add_SourceChanged(IntPtr handler, out long token);
        // 9
        void remove_SourceChanged(long token);
        // 10
        void add_HistoryChanged(IntPtr handler, out long token);
        // 11
        void remove_HistoryChanged(long token);
        // 12
        void add_NavigationCompleted(IntPtr handler, out long token);
        // 13
        void remove_NavigationCompleted(long token);
        // 14
        void add_FrameNavigationStarting(IntPtr handler, out long token);
        // 15
        void remove_FrameNavigationStarting(long token);
        // 16
        void add_FrameNavigationCompleted(IntPtr handler, out long token);
        // 17
        void remove_FrameNavigationCompleted(long token);
        // 18
        void add_ScriptDialogOpening(IntPtr handler, out long token);
        // 19
        void remove_ScriptDialogOpening(long token);
        // 20
        void add_PermissionRequested(IntPtr handler, out long token);
        // 21
        void remove_PermissionRequested(long token);
        // 22
        void add_ProcessFailed(IntPtr handler, out long token);
        // 23
        void remove_ProcessFailed(long token);
        // 24
        void AddScriptToExecuteOnDocumentCreated(
            [MarshalAs(UnmanagedType.LPWStr)] string javaScript, IntPtr handler);
        // 25
        void RemoveScriptToExecuteOnDocumentCreated(
            [MarshalAs(UnmanagedType.LPWStr)] string id);
        // 26
        void ExecuteScript(
            [MarshalAs(UnmanagedType.LPWStr)] string javaScript, IntPtr handler);
        // 27
        void CapturePreview(
            int imageFormat,
            [MarshalAs(UnmanagedType.Interface)] ComTypes.IStream imageStream,
            [MarshalAs(UnmanagedType.Interface)] ICoreWebView2CapturePreviewCompletedHandler handler);
    }

    // ── CapturePreview Callback ──

    [ComVisible(true)]
    [Guid("697E05E9-3F8F-4F6F-B944-A9C8D44A1F95")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2CapturePreviewCompletedHandler
    {
        [PreserveSig]
        int Invoke(int errorCode);
    }

    // ── Callback Handler Implementations ──

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal class EnvironmentCompletedHandler : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
    {
        private readonly Action<int, ICoreWebView2Environment> _callback;

        public EnvironmentCompletedHandler(Action<int, ICoreWebView2Environment> callback)
        {
            _callback = callback;
        }

        public int Invoke(int errorCode, ICoreWebView2Environment createdEnvironment)
        {
            try
            {
                _callback?.Invoke(errorCode, createdEnvironment);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebView2] EnvironmentCompleted callback error: {ex.Message}");
            }
            return 0;
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal class ControllerCompletedHandler : ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
    {
        private readonly Action<int, ICoreWebView2Controller> _callback;

        public ControllerCompletedHandler(Action<int, ICoreWebView2Controller> callback)
        {
            _callback = callback;
        }

        public int Invoke(int errorCode, ICoreWebView2Controller createdController)
        {
            try
            {
                _callback?.Invoke(errorCode, createdController);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebView2] ControllerCompleted callback error: {ex.Message}");
            }
            return 0;
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal class CapturePreviewCompletedHandler : ICoreWebView2CapturePreviewCompletedHandler
    {
        private readonly Action<int> _callback;

        public CapturePreviewCompletedHandler(Action<int> callback)
        {
            _callback = callback;
        }

        public int Invoke(int errorCode)
        {
            try
            {
                _callback?.Invoke(errorCode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebView2] CapturePreview callback error: {ex.Message}");
            }
            return 0;
        }
    }

    // ── WebView2Host Wrapper ──

    /// <summary>
    /// High-level wrapper for embedding a WebView2 browser control as a child window.
    /// Handles initialization, navigation, bounds management, and cleanup.
    /// </summary>
    internal class WebView2Host
    {
        private ICoreWebView2Environment _environment;
        private ICoreWebView2Controller _controller;
        private ICoreWebView2 _webview;
        private IntPtr _parentHwnd;
        private bool _initialized;
        private bool _closed;

        // Keep callback handlers alive to prevent GC collection during async COM callbacks
        private EnvironmentCompletedHandler _envHandler;
        private ControllerCompletedHandler _ctrlHandler;
        private CapturePreviewCompletedHandler _captureHandler;

        public event Action OnReady;
        public event Action<string> OnError;

        public bool IsReady => _initialized && !_closed;

        [DllImport("WebView2Loader.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private static extern int CreateCoreWebView2EnvironmentWithOptions(
            [MarshalAs(UnmanagedType.LPWStr)] string browserExecutableFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string userDataFolder,
            IntPtr environmentOptions,
            [MarshalAs(UnmanagedType.Interface)]
            ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler environmentCreatedHandler);

        /// <summary>
        /// Initialize WebView2 asynchronously. OnReady or OnError will be invoked when complete.
        /// Must be called from the main thread (Unity's message pump is needed for COM callbacks).
        /// </summary>
        public void Initialize(IntPtr parentHwnd, string userDataFolder)
        {
            if (_initialized || _closed) return;

            _parentHwnd = parentHwnd;

            if (!Directory.Exists(userDataFolder))
                Directory.CreateDirectory(userDataFolder);

            _envHandler = new EnvironmentCompletedHandler(OnEnvironmentCreated);

            int hr = CreateCoreWebView2EnvironmentWithOptions(null, userDataFolder, IntPtr.Zero, _envHandler);
            if (hr != 0)
            {
                string msg = $"CreateCoreWebView2EnvironmentWithOptions 失败: 0x{hr:X8}";
                Debug.LogError($"[WebView2] {msg}");
                OnError?.Invoke(msg);
            }
        }

        private void OnEnvironmentCreated(int errorCode, ICoreWebView2Environment environment)
        {
            if (errorCode != 0 || environment == null)
            {
                string msg = errorCode != 0
                    ? $"环境创建失败: 0x{errorCode:X8}"
                    : "环境创建失败: 返回 null";
                Debug.LogError($"[WebView2] {msg}");
                OnError?.Invoke(msg);
                return;
            }

            _environment = environment;
            _ctrlHandler = new ControllerCompletedHandler(OnControllerCreated);
            _environment.CreateCoreWebView2Controller(_parentHwnd, _ctrlHandler);
        }

        private void OnControllerCreated(int errorCode, ICoreWebView2Controller controller)
        {
            if (errorCode != 0 || controller == null)
            {
                string msg = errorCode != 0
                    ? $"控制器创建失败: 0x{errorCode:X8}"
                    : "控制器创建失败: 返回 null";
                Debug.LogError($"[WebView2] {msg}");
                OnError?.Invoke(msg);
                return;
            }

            _controller = controller;

            try
            {
                _controller.get_CoreWebView2(out _webview);
                _initialized = true;
                Debug.Log("[WebView2] 初始化成功");
                OnReady?.Invoke();
            }
            catch (Exception ex)
            {
                string msg = $"获取 WebView2 失败: {ex.Message}";
                Debug.LogError($"[WebView2] {msg}");
                OnError?.Invoke(msg);
            }
        }

        public void Navigate(string url)
        {
            if (!IsReady || _webview == null) return;
            try
            {
                _webview.Navigate(url);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebView2] Navigate 失败: {ex.Message}");
            }
        }

        public void SetBounds(RECT rect)
        {
            if (_controller == null || _closed) return;
            try
            {
                _controller.put_Bounds(rect);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebView2] SetBounds 失败: {ex.Message}");
            }
        }

        public void SetVisible(bool visible)
        {
            if (_controller == null || _closed) return;
            try
            {
                _controller.put_IsVisible(visible ? 1 : 0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebView2] SetVisible 失败: {ex.Message}");
            }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateStreamOnFileEx(
            string pszFile, uint grfMode, uint dwAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool fCreate,
            IntPtr pstmTemplate,
            [MarshalAs(UnmanagedType.Interface)] out ComTypes.IStream ppstm);

        /// <summary>
        /// Capture the current WebView2 page as a PNG image saved to filePath.
        /// The onComplete callback receives (success, message).
        /// </summary>
        public void CaptureToFile(string filePath, Action<bool, string> onComplete)
        {
            if (!IsReady || _webview == null)
            {
                onComplete?.Invoke(false, "WebView2 未就绪");
                return;
            }

            try
            {
                // STGM_CREATE | STGM_WRITE = 0x00001001
                const uint STGM_CREATE_WRITE = 0x00001001;
                // FILE_ATTRIBUTE_NORMAL
                const uint FILE_ATTRIBUTE_NORMAL = 0x80;

                int hr = SHCreateStreamOnFileEx(filePath, STGM_CREATE_WRITE,
                    FILE_ATTRIBUTE_NORMAL, true, IntPtr.Zero, out var stream);
                if (hr != 0)
                {
                    onComplete?.Invoke(false, $"创建文件流失败: 0x{hr:X8}");
                    return;
                }

                _captureHandler = new CapturePreviewCompletedHandler(errorCode =>
                {
                    try
                    {
                        // Flush and release the file stream
                        stream.Commit(0);
                        Marshal.ReleaseComObject(stream);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[WebView2] Stream cleanup: {ex.Message}");
                    }

                    if (errorCode == 0)
                        onComplete?.Invoke(true, filePath);
                    else
                        onComplete?.Invoke(false, $"截图失败: 0x{errorCode:X8}");
                });

                // 0 = COREWEBVIEW2_CAPTURE_PREVIEW_IMAGE_FORMAT_PNG
                _webview.CapturePreview(0, stream, _captureHandler);
            }
            catch (Exception ex)
            {
                onComplete?.Invoke(false, $"截图异常: {ex.Message}");
            }
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _initialized = false;

            try
            {
                _controller?.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebView2] Close 警告: {ex.Message}");
            }

            // Clear event handlers to prevent stale listeners
            OnReady = null;
            OnError = null;

            _webview = null;
            _controller = null;
            _environment = null;
            _envHandler = null;
            _ctrlHandler = null;
            _captureHandler = null;
        }
    }
}

#endif
