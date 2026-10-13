using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SoraTools.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace SoraTools
{
    /// <summary>
    /// Main application for ADB Command Tools.
    /// Builds the entire UI using UI Toolkit at runtime.
    /// Attach this MonoBehaviour + UIDocument to a GameObject in the ADBCommandTools scene.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class AdbToolsApp : MonoBehaviour
    {
        [Header("Window Settings")]
        [SerializeField] private int windowWidth = 1280;
        [SerializeField] private int windowHeight = 800;

        // Services
        private AdbService adb;
        private UIDocument uiDoc;

        // Navigation state
        private string activePanelId = "";
        private readonly Dictionary<string, VisualElement> panels = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();

        // Device panel
        private Label deviceOutput;
        private TextField deviceIpField;
        private TextField pairIpField;
        private TextField pairCodeField;
        private Label scanStatusLabel;
        private VisualElement scanResultContainer;
        private volatile bool scanningNetwork;

        // Install panel
        private Label installOutput;
        private TextField installPathField;
        private TextField obbPathField;
        private TextField abPathField;
        private VisualElement installDeviceListContainer;
        private Button installRefreshDevicesBtn;
        private Button installSelectAllBtn;
        private Label installDeviceStatusLabel;
        private readonly List<DeviceEntry> installDeviceEntries = new List<DeviceEntry>();
        private bool installSelectAllState = true;
        private VisualElement installTransferPanel;
        private VisualElement installTransferRowsContainer;
        private readonly Dictionary<string, TransferProgressRow> installTransferRows =
            new Dictionary<string, TransferProgressRow>();

        // Logcat panel
        private ScrollView logcatScroll;
        private VisualElement logcatLinesContainer;
        private TextField logcatCurrentChunk;
        private int logcatCurrentChunkLen;
        private TextField logcatFilterField;
        private Button logcatStartBtn;
        private Button logcatStopBtn;
        private readonly StringBuilder logcatSaveBuffer = new StringBuilder();
        private readonly List<string> logcatPendingLines = new List<string>();
        private readonly object logcatLock = new object();
        private bool logcatAutoScroll = true;
        private volatile bool logcatDirty = false;
        private float lastLogcatFlush = 0f;
        private const int LogcatMaxSaveChars = 80000;
        private const int LogcatChunkMaxChars = 4000;
        private const int LogcatMaxChunks = 20;
        private const float LogcatFlushInterval = 0.15f; // 150ms throttle

        // Install panel – drag hint
        private VisualElement dropHint;

        // Apps panel
        private Label appsOutput;
        private TextField appsSearchField;
        private TextField appsPackageField;
        private ScrollView appsListScroll;
        private VisualElement appsListContainer;
        private VisualElement _selectedAppRow;
        private readonly List<string> appsPackageNames = new List<string>();

        // File Manager panel
        private Label fileManagerOutput;
        private TextField fileManagerPathField;
        private TextField fileManagerLocalPathField;
        private ScrollView fileManagerListScroll;
        private VisualElement fileManagerListContainer;
        private VisualElement _selectedFileRow;
        private DeviceFileEntry _selectedFileEntry;
        private Label fileManagerStatusLabel;
        private Button fileManagerRefreshBtn;
        private Button fileManagerPushBtn;
        private Button fileManagerPullBtn;
        private Button fileManagerDeleteBtn;
        private Button fileManagerMkdirBtn;
        private Button fileManagerUpBtn;
        private string fileManagerCurrentPath = "/sdcard";
        private readonly List<DeviceFileEntry> fileManagerEntries = new List<DeviceFileEntry>();
        private volatile bool fileManagerLoading;

        // Headset control panel
        private Label headsetStatusLabel;
        private Button headsetRefreshWifiBtn;
        private ScrollView headsetWifiScroll;
        private VisualElement headsetWifiContainer;
        private volatile bool headsetWifiLoading;
        private string activeHeadsetTabId = "wifi";
        private readonly Dictionary<string, Button> headsetTabButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, VisualElement> headsetTabPages = new Dictionary<string, VisualElement>();
        private const int HeadsetKeyeventWakeup = 224;
        private const int HeadsetKeyeventSleep = 223;
        private DropdownField headsetSwitchDeviceDropdown;
        private DropdownField headsetSavedWifiDropdown;
        private TextField headsetWifiPasswordField;
        private Toggle headsetRememberWifiPasswordToggle;
        private Toggle headsetShowWifiPasswordToggle;
        private Toggle headsetForceClearSavedWifiToggle;
        private Button headsetRefreshSavedWifiBtn;
        private Button headsetSwitchWifiBtn;
        private Label headsetSwitchWifiStatusLabel;
        private readonly List<AdbDeviceInfo> headsetSwitchDevices = new List<AdbDeviceInfo>();
        private readonly List<HeadsetSavedWifiNetwork> headsetSavedWifiNetworks = new List<HeadsetSavedWifiNetwork>();
        private HeadsetWifiPasswordStore headsetWifiPasswordStore = new HeadsetWifiPasswordStore();

        // Screencast panel
        private TextField screencastIpField;
        private Label screencastStatusLabel;
        private VisualElement webviewPlaceholder;
        private Button screencastConnectBtn;
        private Button screencastDisconnectBtn;
        private Button screencastOpenBrowserBtn;
        private Button screencastScreenshotBtn;
        private string screencastCurrentUrl;
#if UNITY_STANDALONE_WIN
        private WebView2Host webView2Host;
#endif

        // Performance panel
        private TextField perfPackageField;
        private Button perfStartBtn;
        private Button perfStopBtn;
        private Label perfStatusLabel;

        // System metric rows
        private VisualElement perfCpuBarFill;
        private Label perfCpuValueLabel;
        private VisualElement perfMemBarFill;
        private Label perfMemValueLabel;
        private VisualElement perfBatteryBarFill;
        private Label perfBatteryValueLabel;
        private Label perfBatteryTempLabel;
        private Label perfNetRxLabel;
        private Label perfNetTxLabel;

        // App metric rows
        private VisualElement perfAppSection;
        private Label perfAppSectionTitle;
        private VisualElement perfAppCpuBarFill;
        private Label perfAppCpuValueLabel;
        private VisualElement perfAppMemBarFill;
        private Label perfAppMemValueLabel;
        private VisualElement perfAppFpsBarFill;
        private Label perfAppFpsValueLabel;
        private Label perfAppNetRxLabel;
        private Label perfAppNetTxLabel;

        // Memory detail (top 10 processes)
        private VisualElement perfMemDetailSection;
        private VisualElement perfMemDetailContainer;
        private Button perfMemDetailRefreshBtn;
        private Label perfMemDetailStatusLabel;
        private volatile bool perfMemDetailLoading;

        // Performance monitoring state
        private volatile bool perfMonitoringActive;
        private Thread perfMonitorThread;
        private readonly object perfLock = new object();
        private PerfSnapshot perfPendingSnapshot;
        private PerfSnapshot perfPreviousSnapshot;
        private bool perfHasPreviousSample;
        private volatile bool perfDirty;
        private float lastPerfFlush;
        private volatile string perfCurrentPackage = "";
        private const float PerfFlushInterval = 0.3f;
        private const int PerfPollIntervalMs = 2500;

        private struct PerfSnapshot
        {
            // CPU (/proc/stat raw jiffies for delta)
            public long cpuUser, cpuNice, cpuSystem, cpuIdle, cpuIoWait, cpuIrq, cpuSoftIrq;
            public float cpuUsagePercent;

            // Memory (/proc/meminfo)
            public long memTotalKb, memAvailableKb;
            public float memUsagePercent;

            // Battery (dumpsys battery)
            public int batteryLevel;
            public float batteryTempCelsius;
            public string batteryStatus;

            // Network (/proc/net/dev cumulative bytes)
            public long netRxBytes, netTxBytes;
            public float netRxBytesPerSec, netTxBytesPerSec;

            // Per-app (optional)
            public bool hasAppMetrics;
            public long appUtime, appStime;
            public float appCpuPercent;
            public long appPssKb;
            public int appTotalFrames, appJankyFrames;
            public long appNetRxBytes, appNetTxBytes;
            public float appNetRxPerSec, appNetTxPerSec;

            // Status
            public bool isValid;
            public string errorMessage;
        }

        private class DeviceEntry
        {
            public string serial;
            public string description; // e.g. "device model:Pixel_5"
            public Toggle toggle;
        }

        private class AdbDeviceInfo
        {
            public string Serial;
            public string Status;
            public string Model;
            public string Description;
        }

        private class TransferProgressRow
        {
            public VisualElement Container;
            public Label Header;
            public ProgressBar ProgressBar;
            public Label Details;
        }

        private class HeadsetWifiInfo
        {
            public string Serial;
            public string Model;
            public string DeviceStatus;
            public bool QuerySuccess;
            public string ErrorMessage;
            public string WifiState;
            public string Ssid;
            public string Bssid;
            public string IpAddress;
            public string MacAddress;
            public string Rssi;
            public string Frequency;
            public string LinkSpeed;
            public string NetworkId;
            public string SupplicantState;
        }

        private class HeadsetSavedWifiNetwork
        {
            public string NetworkId;
            public string Ssid;
            public string SecurityTypes;
            public string Password;
        }

        private class HeadsetForgetSavedWifiResult
        {
            public bool Success;
            public int ForgottenCount;
            public string Message;
        }

        [Serializable]
        private class HeadsetWifiPasswordEntry
        {
            public string ssid;
            public string password;
        }

        [Serializable]
        private class HeadsetWifiPasswordStore
        {
            public List<HeadsetWifiPasswordEntry> entries = new List<HeadsetWifiPasswordEntry>();
        }

        private class DeviceFileEntry
        {
            public string Name;
            public bool IsDirectory;
            public bool IsSymlink;
            public string Permissions;
            public string Owner;
            public string Group;
            public long Size;
            public string DateString;
            public string LinkTarget;
            public VisualElement Row;
        }

        // Log save path
        private string logSavePath;

        #region Color Palette

        static readonly Color ColBgDark = new Color(0.10f, 0.10f, 0.18f);
        static readonly Color ColBgHeader = new Color(0.07f, 0.07f, 0.14f);
        static readonly Color ColBgSidebar = new Color(0.08f, 0.10f, 0.19f);
        static readonly Color ColBgOutput = new Color(0.05f, 0.05f, 0.09f);
        static readonly Color ColBgInputField = new Color(0.08f, 0.10f, 0.18f);
        static readonly Color ColAccent = new Color(0f, 0.47f, 0.83f);       // #0078D4
        static readonly Color ColSuccess = new Color(0.24f, 0.60f, 0.26f);   // green
        static readonly Color ColDanger = new Color(0.84f, 0.22f, 0.18f);    // red
        static readonly Color ColWarning = new Color(0.90f, 0.68f, 0.02f);   // yellow
        static readonly Color ColNeutral = new Color(0.30f, 0.32f, 0.40f);   // gray
        static readonly Color ColText = new Color(0.88f, 0.88f, 0.92f);
        static readonly Color ColTextDim = new Color(0.50f, 0.52f, 0.62f);
        static readonly Color ColBorder = new Color(0.18f, 0.20f, 0.30f);
        static readonly Color ColNavActive = new Color(0f, 0.47f, 0.83f, 0.18f);
        static readonly Color ColNavHover = new Color(1f, 1f, 1f, 0.05f);

        #endregion

        #region Lifecycle

        void Awake()
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(windowWidth, windowHeight, false);
        }

        void Start()
        {
            adb = new AdbService();
            SetupLogSavePath();
            LoadHeadsetWifiPasswordStore();

            // Ensure dispatcher exists
            _ = MainThreadDispatcher.Instance;

            // Enable Win32 drag-and-drop for standalone builds
            Win32DragDrop.Enable();

            // UIDocument and PanelSettings should be pre-configured via:
            //   Menu: SoraTools > ADB Tools - Setup Scene
            uiDoc = GetComponent<UIDocument>();
            if (uiDoc.panelSettings == null)
            {
                Debug.LogError("[AdbToolsApp] PanelSettings not assigned! Run menu: SoraTools > ADB Tools - Setup Scene");
                return;
            }

            BuildUI();
            LoadCommandHistory();
            ShowPanel("install"); // Default to Install APK (primary use case)
        }

        void Update()
        {
            // Check for Win32 drag-and-drop files
            var droppedFiles = Win32DragDrop.GetDroppedFiles();
            if (droppedFiles != null)
            {
                foreach (var file in droppedFiles)
                {
                    if (Directory.Exists(file))
                    {
                        abPathField.value = file;
                        ShowPanel("install");
                    }
                    else if (file.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                    {
                        installPathField.value = file;
                        ShowPanel("install");
                    }
                    else if (file.EndsWith(".obb", StringComparison.OrdinalIgnoreCase))
                    {
                        obbPathField.value = file;
                        ShowPanel("install");
                    }
                }
            }

            // Throttled logcat UI updates for performance
            if (logcatDirty && Time.time - lastLogcatFlush >= LogcatFlushInterval)
            {
                logcatDirty = false;
                lastLogcatFlush = Time.time;
                FlushLogcatLines();
            }

            // Throttled performance monitor UI updates
            if (perfDirty && Time.time - lastPerfFlush >= PerfFlushInterval)
            {
                perfDirty = false;
                lastPerfFlush = Time.time;
                perfCurrentPackage = perfPackageField?.value?.Trim() ?? "";
                FlushPerfData();
            }
        }

        void OnDestroy()
        {
            SaveCommandHistory();
            if (perfMonitoringActive)
            {
                perfMonitoringActive = false;
                if (perfMonitorThread != null)
                {
                    if (!perfMonitorThread.Join(3000))
                        Debug.LogWarning("[AdbToolsApp] PerfMonitor thread did not exit within 3s");
                    perfMonitorThread = null;
                }
            }
#if UNITY_STANDALONE_WIN
            webView2Host?.Close();
#endif
            Win32DragDrop.Disable();
            adb?.Dispose();
        }

        void OnApplicationQuit()
        {
            SaveCommandHistory();
            adb?.Dispose();
        }

        private void LoadCommandHistory()
        {
            var data = CommandHistory.Load();
            if (!string.IsNullOrEmpty(data.deviceIp)) deviceIpField.value = data.deviceIp;
            if (!string.IsNullOrEmpty(data.installApkPath)) installPathField.value = data.installApkPath;
            if (!string.IsNullOrEmpty(data.installObbPath)) obbPathField.value = data.installObbPath;
            if (!string.IsNullOrEmpty(data.installAbPath)) abPathField.value = data.installAbPath;
            if (!string.IsNullOrEmpty(data.screencastIp)) screencastIpField.value = data.screencastIp;
            if (!string.IsNullOrEmpty(data.logcatFilter)) logcatFilterField.value = data.logcatFilter;
            if (!string.IsNullOrEmpty(data.perfPackageName)) perfPackageField.value = data.perfPackageName;
            if (!string.IsNullOrEmpty(data.pairIp)) pairIpField.value = data.pairIp;
            if (!string.IsNullOrEmpty(data.fileManagerDevicePath))
            {
                fileManagerPathField.value = data.fileManagerDevicePath;
                fileManagerCurrentPath = data.fileManagerDevicePath;
            }
            if (!string.IsNullOrEmpty(data.fileManagerLocalPath)) fileManagerLocalPathField.value = data.fileManagerLocalPath;
        }

        private void SaveCommandHistory()
        {
            var data = new CommandHistoryData
            {
                deviceIp = deviceIpField?.value ?? "",
                installApkPath = installPathField?.value ?? "",
                installObbPath = obbPathField?.value ?? "",
                installAbPath = abPathField?.value ?? "",
                screencastIp = screencastIpField?.value ?? "",
                logcatFilter = logcatFilterField?.value ?? "",
                perfPackageName = perfPackageField?.value ?? "",
                pairIp = pairIpField?.value ?? "",
                fileManagerDevicePath = fileManagerCurrentPath ?? "",
                fileManagerLocalPath = fileManagerLocalPathField?.value ?? "",
            };
            CommandHistory.Save(data);
        }

        private void SetupLogSavePath()
        {
            string basePath = Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Assets", "Logs")
                : Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
            logSavePath = basePath;
            if (!Directory.Exists(logSavePath))
                Directory.CreateDirectory(logSavePath);
        }

        #endregion

        #region UI Building

        private void BuildUI()
        {
            var root = uiDoc.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.backgroundColor = ColBgDark;

            root.Add(BuildHeader());

            var main = new VisualElement();
            main.style.flexGrow = 1;
            main.style.flexDirection = FlexDirection.Row;
            main.Add(BuildSidebar());
            main.Add(BuildContentArea());
            root.Add(main);
        }

        // ── Header ──

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.style.height = 46;
            header.style.backgroundColor = ColBgHeader;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 20;
            header.style.paddingRight = 20;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = ColBorder;

            var title = new Label("ADB Command Center");
            title.style.fontSize = 17;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1;
            header.Add(title);

            header.Add(Spacer());

            var statusLabel = new Label();
            statusLabel.style.fontSize = 11;
            statusLabel.style.color = ColTextDim;
            statusLabel.text = adb.IsAdbAvailable ? "ADB Ready" : "ADB Not Found!";
            statusLabel.style.color = adb.IsAdbAvailable ? ColSuccess : ColDanger;
            header.Add(statusLabel);

            return header;
        }

        // ── Sidebar ──

        private VisualElement BuildSidebar()
        {
            var sidebar = new VisualElement();
            sidebar.style.width = 160;
            sidebar.style.backgroundColor = ColBgSidebar;
            sidebar.style.paddingTop = 12;
            sidebar.style.paddingBottom = 12;
            sidebar.style.borderRightWidth = 1;
            sidebar.style.borderRightColor = ColBorder;

            var sidebarTitle = new Label("工具");
            sidebarTitle.style.fontSize = 10;
            sidebarTitle.style.color = ColTextDim;
            sidebarTitle.style.marginLeft = 18;
            sidebarTitle.style.marginBottom = 8;
            sidebarTitle.style.letterSpacing = 2;
            sidebarTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            sidebar.Add(sidebarTitle);

            AddNavButton(sidebar, "devices", "设备管理");
            AddNavButton(sidebar, "install", "安装 APK");
            AddNavButton(sidebar, "logcat", "实时日志");
            AddNavButton(sidebar, "apps", "应用管理");
            AddNavButton(sidebar, "screencast", "投屏查看");
            AddNavButton(sidebar, "perf", "性能监控");
            AddNavButton(sidebar, "filemanager", "文件管理");
            AddNavButton(sidebar, "headset", "头显控制");

            sidebar.Add(Spacer());

            // ADB path info at bottom
            var pathInfo = new Label("ADB Path:");
            pathInfo.style.fontSize = 9;
            pathInfo.style.color = ColTextDim;
            pathInfo.style.marginLeft = 12;
            pathInfo.style.marginRight = 8;
            sidebar.Add(pathInfo);

            var pathValue = new Label(adb.AdbPath ?? "未找到");
            pathValue.style.fontSize = 8;
            pathValue.style.color = ColTextDim;
            pathValue.style.marginLeft = 12;
            pathValue.style.marginRight = 8;
            pathValue.style.whiteSpace = WhiteSpace.Normal;
            pathValue.style.overflow = Overflow.Hidden;
            sidebar.Add(pathValue);

            return sidebar;
        }

        private void AddNavButton(VisualElement parent, string id, string label)
        {
            var btn = new Button(() => ShowPanel(id));
            btn.text = label;
            btn.name = $"nav-{id}";
            btn.style.height = 38;
            btn.style.marginLeft = 8;
            btn.style.marginRight = 8;
            btn.style.marginTop = 2;
            btn.style.marginBottom = 2;
            SetBorderRadius(btn, 6);
            btn.style.backgroundColor = Color.clear;
            btn.style.color = ColTextDim;
            btn.style.fontSize = 13;
            SetBorderWidth(btn, 0);
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            btn.style.paddingLeft = 14;

            btn.RegisterCallback<MouseEnterEvent>(e =>
            {
                if (activePanelId != id)
                    btn.style.backgroundColor = ColNavHover;
            });
            btn.RegisterCallback<MouseLeaveEvent>(e =>
            {
                if (activePanelId != id)
                    btn.style.backgroundColor = Color.clear;
            });

            parent.Add(btn);
            navButtons[id] = btn;
        }

        // ── Content Area ──

        private VisualElement BuildContentArea()
        {
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            content.style.overflow = Overflow.Hidden;
            content.style.paddingTop = 16;
            content.style.paddingBottom = 16;
            content.style.paddingLeft = 20;
            content.style.paddingRight = 20;

            panels["devices"] = BuildDevicePanel();
            panels["install"] = BuildInstallPanel();
            panels["logcat"] = BuildLogcatPanel();
            panels["apps"] = BuildAppsPanel();
            panels["screencast"] = BuildScreencastPanel();
            panels["perf"] = BuildPerformancePanel();
            panels["filemanager"] = BuildFileManagerPanel();
            panels["headset"] = BuildHeadsetControlPanel();

            foreach (var panel in panels.Values)
            {
                panel.style.flexGrow = 1;
                panel.style.display = DisplayStyle.None;
                content.Add(panel);
            }

            return content;
        }

        private void ShowPanel(string id)
        {
            if (activePanelId == id) return;

            // Hide all panels
            foreach (var kvp in panels)
                kvp.Value.style.display = DisplayStyle.None;

            // Reset nav buttons
            foreach (var kvp in navButtons)
            {
                kvp.Value.style.backgroundColor = Color.clear;
                kvp.Value.style.color = ColTextDim;
                SetBorderWidth(kvp.Value, 0);
            }

            // Show selected
            if (panels.ContainsKey(id))
            {
                panels[id].style.display = DisplayStyle.Flex;
                activePanelId = id;
            }

            // Highlight active nav button
            if (navButtons.ContainsKey(id))
            {
                navButtons[id].style.backgroundColor = ColNavActive;
                navButtons[id].style.color = ColText;
                navButtons[id].style.borderLeftWidth = 3;
                navButtons[id].style.borderLeftColor = ColAccent;
            }

            // Toggle WebView2 visibility when switching panels
#if UNITY_STANDALONE_WIN
            if (webView2Host != null && webView2Host.IsReady)
                webView2Host.SetVisible(id == "screencast");
#endif

            if (id == "headset")
            {
                RefreshHeadsetSwitchDevicesAndNetworks();
                RefreshHeadsetWifiInfo();
            }
        }

        #endregion

        #region Device Panel

        private VisualElement BuildDevicePanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;

            panel.Add(SectionTitle("设备管理"));
            panel.Add(SectionDesc("查看、连接和断开 ADB 设备"));

            // Actions row
            var row1 = Row();
            var refreshBtn = ActionButton("刷新设备列表", ColAccent);
            refreshBtn.clicked += () => RunAdbAsync("devices -l", deviceOutput, "正在查找设备...");
            row1.Add(refreshBtn);

            var deviceInfoBtn = ActionButton("设备信息", ColAccent);
            deviceInfoBtn.clicked += OnDeviceInfo;
            row1.Add(deviceInfoBtn);

            var screenshotBtn = ActionButton("截取屏幕", ColSuccess);
            screenshotBtn.clicked += OnScreenshot;
            row1.Add(screenshotBtn);

            var disconnectBtn = ActionButton("断开所有设备", ColDanger);
            disconnectBtn.style.marginRight = 0;
            disconnectBtn.clicked += () => RunAdbAsync("disconnect", deviceOutput, "正在断开...");
            row1.Add(disconnectBtn);
            panel.Add(row1);

            // ── Wireless debugging (pair + connect) ──
            var wirelessSection = new VisualElement();
            wirelessSection.style.marginTop = 12;
            wirelessSection.style.paddingTop = 10;
            wirelessSection.style.paddingBottom = 10;
            wirelessSection.style.paddingLeft = 14;
            wirelessSection.style.paddingRight = 14;
            wirelessSection.style.backgroundColor = ColBgOutput;
            SetBorderRadius(wirelessSection, 6);
            wirelessSection.style.borderTopWidth = 1;
            wirelessSection.style.borderBottomWidth = 1;
            wirelessSection.style.borderLeftWidth = 1;
            wirelessSection.style.borderRightWidth = 1;
            wirelessSection.style.borderTopColor = ColBorder;
            wirelessSection.style.borderBottomColor = ColBorder;
            wirelessSection.style.borderLeftColor = ColBorder;
            wirelessSection.style.borderRightColor = ColBorder;

            var wirelessTitle = new Label("无线连接");
            wirelessTitle.style.fontSize = 13;
            wirelessTitle.style.color = ColTextDim;
            wirelessTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            wirelessTitle.style.marginBottom = 6;
            wirelessSection.Add(wirelessTitle);

            // ── Scan row ──
            var scanRow = Row();
            var scanBtn = ActionButton("搜索局域网设备", ColAccent);
            scanBtn.clicked += () => OnScanNetwork(scanBtn);
            scanRow.Add(scanBtn);

            scanStatusLabel = new Label("扫描本地私有网络中响应 ADB 协议的 5555 设备");
            scanStatusLabel.style.fontSize = 11;
            scanStatusLabel.style.color = ColTextDim;
            scanStatusLabel.style.flexGrow = 1;
            scanRow.Add(scanStatusLabel);
            wirelessSection.Add(scanRow);

            scanResultContainer = new VisualElement();

            var scanResultScroll = new ScrollView(ScrollViewMode.Vertical);
            scanResultScroll.style.marginTop = 4;
            scanResultScroll.style.maxHeight = 170; // ~5 rows visible, scroll for more
            scanResultScroll.Add(scanResultContainer);
            wirelessSection.Add(scanResultScroll);

            // ── Connect row ──
            var connectRow = Row();
            connectRow.style.marginTop = 8;
            deviceIpField = StyledTextField("连接 IP:端口", "192.168.1.100:5555");
            deviceIpField.style.flexGrow = 1;
            deviceIpField.style.flexShrink = 1;
            deviceIpField.style.overflow = Overflow.Hidden;
            connectRow.Add(deviceIpField);

            var connectBtn = ActionButton("连接", ColSuccess);
            connectBtn.style.width = 80;
            connectBtn.clicked += () =>
            {
                string ip = deviceIpField.value.Trim();
                if (!string.IsNullOrEmpty(ip))
                    RunAdbAsync($"connect {ip}", deviceOutput, $"正在连接 {ip}...");
            };
            connectRow.Add(connectBtn);
            wirelessSection.Add(connectRow);

            // ── Pair row (Android 11+ optional) ──
            var pairTitle = new Label("配对 (Android 11+ 无线调试，可选)");
            pairTitle.style.fontSize = 11;
            pairTitle.style.color = ColTextDim;
            pairTitle.style.marginTop = 10;
            wirelessSection.Add(pairTitle);

            var pairRow = Row();
            pairRow.style.marginTop = 4;
            pairIpField = StyledTextField("配对 IP:端口", "192.168.1.100:37000");
            pairIpField.style.flexGrow = 1;
            pairIpField.style.flexShrink = 1;
            pairIpField.style.overflow = Overflow.Hidden;
            pairRow.Add(pairIpField);

            pairCodeField = StyledTextField("配对码", "123456");
            pairCodeField.style.width = 120;
            pairCodeField.style.flexShrink = 0;
            pairRow.Add(pairCodeField);

            var pairBtn = ActionButton("配对", ColWarning);
            pairBtn.style.width = 70;
            pairBtn.style.marginRight = 0;
            pairBtn.clicked += OnWirelessPair;
            pairRow.Add(pairBtn);
            wirelessSection.Add(pairRow);

            panel.Add(wirelessSection);

            // Output area
            panel.Add(OutputArea(out deviceOutput));

            return panel;
        }

        private void OnWirelessPair()
        {
            string ip = pairIpField.value.Trim();
            string code = pairCodeField.value.Trim();

            if (string.IsNullOrEmpty(ip))
            {
                SetOutput(deviceOutput, "[错误] 请输入配对地址 (IP:端口)");
                return;
            }
            if (string.IsNullOrEmpty(code))
            {
                SetOutput(deviceOutput, "[错误] 请输入配对码");
                return;
            }

            SetOutput(deviceOutput, $"正在配对 {ip}...");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = adb.Execute($"pair {ip} {code}", 30000);
                MainThreadDispatcher.Enqueue(() =>
                {
                    string text = result.GetDisplayText();
                    // Check for success indicators
                    bool paired = result.IsSuccess ||
                        text.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase) ||
                        text.Contains("paired", StringComparison.OrdinalIgnoreCase);

                    if (paired)
                    {
                        SetOutput(deviceOutput,
                            text + "\n\n配对成功! 现在可以使用下方的连接功能连接到设备。\n" +
                            "提示: 连接端口通常与配对端口不同，请查看设备上显示的连接地址。");
                    }
                    else
                    {
                        SetOutput(deviceOutput, text);
                    }
                });
            });
        }

        // ── Network Scan ──

        private void OnScanNetwork(Button scanBtn)
        {
            if (scanningNetwork) return;
            scanningNetwork = true;
            scanBtn.SetEnabled(false);
            scanResultContainer.Clear();
            scanStatusLabel.text = "正在扫描局域网...";
            scanStatusLabel.style.color = ColWarning;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Collect all local IPv4 subnets from all network interfaces
                    var subnets = GetAllLocalSubnets();
                    if (subnets.Count == 0)
                    {
                        MainThreadDispatcher.Enqueue(() =>
                        {
                            scanningNetwork = false;
                            scanBtn.SetEnabled(true);
                            scanStatusLabel.text = "无法获取本机网络信息";
                            scanStatusLabel.style.color = ColDanger;
                        });
                        return;
                    }

                    var subnetNames = string.Join(", ", subnets.Select(s => $"{s.prefix}.0/24"));
                    Debug.Log($"[ScanNetwork] 本机网卡子网: {subnetNames}");

                    // Phase 1: Quick scan — check ARP table for known devices
                    MainThreadDispatcher.Enqueue(() =>
                        scanStatusLabel.text = "正在检查 ARP 表...");

                    var arpIps = GetArpTableIps(subnets);
                    Debug.Log($"[ScanNetwork] ARP 表中找到 {arpIps.Count} 个同子网 IP");

                    // Phase 2: Build full IP list across all subnets
                    var localIps = new HashSet<string>(subnets.Select(s => s.localIp));
                    var fullTargets = new List<string>();
                    foreach (var sub in subnets)
                    {
                        for (int i = 1; i <= 254; i++)
                        {
                            string target = $"{sub.prefix}.{i}";
                            if (!localIps.Contains(target))
                                fullTargets.Add(target);
                        }
                    }

                    // Prioritize ARP IPs: scan them first, then remaining IPs
                    var arpSet = new HashSet<string>(arpIps);
                    var remainingTargets = fullTargets.Where(ip => !arpSet.Contains(ip)).ToList();
                    var orderedTargets = new List<string>(arpIps);
                    orderedTargets.AddRange(remainingTargets);

                    int totalHosts = orderedTargets.Count;
                    MainThreadDispatcher.Enqueue(() =>
                        scanStatusLabel.text = $"正在扫描 {subnetNames} ({totalHosts} 个地址, ADB 5555)...");

                    // Parallel port scan using dedicated threads (avoids ThreadPool starvation)
                    var foundDevices = new ConcurrentBag<string>();
                    var queue = new ConcurrentQueue<string>(orderedTargets);
                    int scanned = 0;
                    int workerCount = Math.Min(64, totalHosts);
                    var workers = new Thread[workerCount];

                    for (int w = 0; w < workerCount; w++)
                    {
                        workers[w] = new Thread(() =>
                        {
                            while (queue.TryDequeue(out string ip))
                            {
                                try
                                {
                                    if (IsAdbTcpDevice(ip, 5555, 1500))
                                    {
                                        Debug.Log($"[ScanNetwork] 发现ADB设备: {ip}:5555");
                                        foundDevices.Add(ip);
                                    }
                                }
                                catch { }

                                int done = Interlocked.Increment(ref scanned);
                                if (done % 50 == 0 || done == arpIps.Count || done >= totalHosts)
                                {
                                    MainThreadDispatcher.Enqueue(() =>
                                        scanStatusLabel.text = $"扫描中... {done}/{totalHosts}");
                                }
                            }
                        });
                        workers[w].IsBackground = true;
                        workers[w].Start();
                    }

                    // Wait for all workers (max 30 seconds)
                    foreach (var w in workers)
                        w.Join(30000);

                    // Sort by IP
                    var sortedDevices = foundDevices.ToList();
                    sortedDevices.Sort((a, b) =>
                    {
                        var aParts = a.Split('.');
                        var bParts = b.Split('.');
                        for (int i = 0; i < 4; i++)
                        {
                            int cmp = int.Parse(aParts[i]).CompareTo(int.Parse(bParts[i]));
                            if (cmp != 0) return cmp;
                        }
                        return 0;
                    });

                    MainThreadDispatcher.Enqueue(() => ShowScanResults(scanBtn, sortedDevices));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ScanNetwork] 扫描异常: {ex}");
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        scanningNetwork = false;
                        scanBtn.SetEnabled(true);
                        scanStatusLabel.text = $"扫描异常: {ex.Message}";
                        scanStatusLabel.style.color = ColDanger;
                    });
                }
            });
        }

        /// <summary>
        /// Parse the system ARP table to find IPs that are already known on the same subnets.
        /// This is much faster than blind TCP scanning since ARP only lists active devices.
        /// </summary>
        private static bool IsAdbTcpDevice(string ip, int port, int timeoutMs)
        {
            using (var client = new TcpClient())
            {
                var ar = client.BeginConnect(ip, port, null, null);
                bool completed = ar.AsyncWaitHandle.WaitOne(timeoutMs);
                if (!completed) return false;

                try
                {
                    client.EndConnect(ar);
                }
                catch
                {
                    return false;
                }

                if (!client.Connected) return false;

                client.ReceiveTimeout = timeoutMs;
                client.SendTimeout = timeoutMs;

                using (var stream = client.GetStream())
                {
                    byte[] payload = Encoding.ASCII.GetBytes("host::\0");
                    byte[] packet = BuildAdbPacket("CNXN", 0x01000000, 4096, payload);
                    stream.Write(packet, 0, packet.Length);

                    byte[] header = new byte[24];
                    int read = ReadExact(stream, header, 0, header.Length);
                    if (read != header.Length) return false;

                    uint command = ReadUInt32(header, 0);
                    uint magic = ReadUInt32(header, 20);
                    if ((command ^ 0xFFFFFFFFu) != magic) return false;

                    string commandText = Encoding.ASCII.GetString(header, 0, 4);
                    return commandText == "CNXN" || commandText == "AUTH";
                }
            }
        }

        private static byte[] BuildAdbPacket(string commandText, uint arg0, uint arg1, byte[] payload)
        {
            byte[] packet = new byte[24 + payload.Length];
            uint command = MakeAdbCommand(commandText);
            uint checksum = 0;
            foreach (byte b in payload)
                checksum += b;

            WriteUInt32(packet, 0, command);
            WriteUInt32(packet, 4, arg0);
            WriteUInt32(packet, 8, arg1);
            WriteUInt32(packet, 12, (uint)payload.Length);
            WriteUInt32(packet, 16, checksum);
            WriteUInt32(packet, 20, command ^ 0xFFFFFFFFu);
            Buffer.BlockCopy(payload, 0, packet, 24, payload.Length);
            return packet;
        }

        private static uint MakeAdbCommand(string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            return (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        private static List<string> GetArpTableIps(List<SubnetInfo> subnets)
        {
            var result = new List<string>();
            var prefixes = new HashSet<string>(subnets.Select(s => s.prefix));

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);

                    // Parse lines like: "  192.168.50.9          aa-bb-cc-dd-ee-ff     dynamic"
                    var ipRegex = new Regex(@"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
                    foreach (string line in output.Split('\n'))
                    {
                        var m = ipRegex.Match(line);
                        if (!m.Success) continue;
                        string ip = m.Groups[1].Value;

                        // Skip broadcast (x.x.x.255) and network (x.x.x.0)
                        int lastDot = ip.LastIndexOf('.');
                        string prefix = ip.Substring(0, lastDot);
                        string lastOctet = ip.Substring(lastDot + 1);
                        if (lastOctet == "255" || lastOctet == "0") continue;

                        if (prefixes.Contains(prefix))
                        {
                            Debug.Log($"[ScanNetwork] ARP: {ip}");
                            result.Add(ip);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScanNetwork] ARP 查询失败: {ex.Message}");
            }

            return result.Distinct().ToList();
        }

        private void ShowScanResults(Button scanBtn, List<string> foundDevices)
        {
            scanningNetwork = false;
            scanBtn.SetEnabled(true);
            scanResultContainer.Clear();
            foundDevices = foundDevices
                .Where(IsPrivateLanAddressString)
                .Distinct()
                .ToList();

            if (foundDevices.Count == 0)
            {
                scanStatusLabel.text = "未发现局域网内响应 ADB 协议的 5555 设备";
                scanStatusLabel.style.color = ColWarning;
                return;
            }

            scanStatusLabel.text = $"发现 {foundDevices.Count} 个设备";
            scanStatusLabel.style.color = ColSuccess;

            foreach (var ip in foundDevices)
            {
                var row = Row();
                row.style.marginTop = 4;
                row.style.height = 30;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.backgroundColor = new Color(0.12f, 0.14f, 0.22f);
                SetBorderRadius(row, 4);

                var ipLabel = new Label($"{ip}:5555");
                ipLabel.style.flexGrow = 1;
                ipLabel.style.fontSize = 12;
                ipLabel.style.color = ColText;
                ipLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(ipLabel);

                string capturedIp = ip;

                var fillBtn = ActionButton("填入", ColNeutral);
                fillBtn.style.height = 24;
                fillBtn.style.paddingLeft = 10;
                fillBtn.style.paddingRight = 10;
                fillBtn.style.fontSize = 11;
                fillBtn.clicked += () => deviceIpField.value = $"{capturedIp}:5555";
                row.Add(fillBtn);

                var quickConnectBtn = ActionButton("连接", ColSuccess);
                quickConnectBtn.style.height = 24;
                quickConnectBtn.style.paddingLeft = 10;
                quickConnectBtn.style.paddingRight = 10;
                quickConnectBtn.style.fontSize = 11;
                quickConnectBtn.style.marginRight = 0;
                quickConnectBtn.clicked += () =>
                {
                    deviceIpField.value = $"{capturedIp}:5555";
                    RunAdbAsync($"connect {capturedIp}:5555", deviceOutput,
                        $"正在连接 {capturedIp}:5555...");
                };
                row.Add(quickConnectBtn);

                scanResultContainer.Add(row);
            }
        }

        private struct SubnetInfo
        {
            public string localIp;  // e.g. "192.168.50.100"
            public string prefix;   // e.g. "192.168.50"
        }

        private static List<SubnetInfo> GetAllLocalSubnets()
        {
            var subnets = new List<SubnetInfo>();
            var seenPrefixes = new HashSet<string>();

            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    // Skip loopback, down, tunnel interfaces
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        continue;
                    if (nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Ethernet &&
                        nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                        continue;

                    var props = nic.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;

                        string ip = addr.Address.ToString();
                        if (!IsPrivateLanAddress(addr.Address)) continue;

                        int lastDot = ip.LastIndexOf('.');
                        string prefix = ip.Substring(0, lastDot);

                        if (seenPrefixes.Add(prefix))
                        {
                            Debug.Log($"[ScanNetwork] 网卡 {nic.Name}: {ip} -> 子网 {prefix}.0/24");
                            subnets.Add(new SubnetInfo { localIp = ip, prefix = prefix });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScanNetwork] 枚举网卡失败: {ex.Message}");
            }

            return subnets;
        }

        private static bool IsPrivateLanAddress(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length != 4) return false;

            // 10.0.0.0/8
            if (bytes[0] == 10) return true;

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            return false;
        }

        private static bool IsPrivateLanAddressString(string ip)
        {
            return IPAddress.TryParse(ip, out var address) && IsPrivateLanAddress(address);
        }

        // ── Screenshot ──

        private void OnScreenshot()
        {
            SetOutput(deviceOutput, "正在截取屏幕...");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Step 1: Capture screenshot on device
                    var capResult = adb.Execute("shell screencap -p /sdcard/screenshot_temp.png");
                    if (!capResult.IsSuccess)
                    {
                        MainThreadDispatcher.Enqueue(() =>
                            SetOutput(deviceOutput, $"[错误] 截屏失败: {capResult.Error}"));
                        return;
                    }

                    // Step 2: Pull to local Screenshots folder
                    string screenshotsDir = Path.Combine(
                        Path.GetDirectoryName(logSavePath), "Screenshots");
                    if (!Directory.Exists(screenshotsDir))
                        Directory.CreateDirectory(screenshotsDir);

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string localPath = Path.Combine(screenshotsDir, $"screenshot_{timestamp}.png");

                    var pullResult = adb.Execute($"pull /sdcard/screenshot_temp.png \"{localPath}\"");

                    // Step 3: Clean up temp file on device
                    adb.Execute("shell rm /sdcard/screenshot_temp.png");

                    if (pullResult.IsSuccess && File.Exists(localPath))
                    {
                        MainThreadDispatcher.Enqueue(() =>
                        {
                            SetOutput(deviceOutput, $"截屏已保存:\n{localPath}");
                            FileDialogHelper.OpenInExplorer(localPath);
                        });
                    }
                    else
                    {
                        MainThreadDispatcher.Enqueue(() =>
                            SetOutput(deviceOutput, $"[错误] 拉取截图失败: {pullResult.Error}"));
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AdbToolsApp] 截屏异常: {ex.Message}");
                    MainThreadDispatcher.Enqueue(() =>
                        SetOutput(deviceOutput, $"[错误] 截屏异常: {ex.Message}"));
                }
            });
        }

        // ── Device Info ──

        private void OnDeviceInfo()
        {
            SetOutput(deviceOutput, "正在获取设备信息...");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string brand = GetProp("ro.product.brand");
                    string model = GetProp("ro.product.model");
                    string androidVer = GetProp("ro.build.version.release");
                    string sdk = GetProp("ro.build.version.sdk");
                    string serial = GetProp("ro.serialno");

                    string resolution = adb.Execute("shell wm size").Output.Trim();
                    if (resolution.Contains(":"))
                        resolution = resolution.Substring(resolution.LastIndexOf(':') + 1).Trim();

                    string density = adb.Execute("shell wm density").Output.Trim();
                    if (density.Contains(":"))
                        density = density.Substring(density.LastIndexOf(':') + 1).Trim() + "dpi";

                    // Battery
                    string batteryInfo = ParseBattery(adb.Execute("shell dumpsys battery").Output);

                    // Storage
                    string storageInfo = ParseStorage(adb.Execute("shell df /data").Output);

                    var sb = new StringBuilder();
                    sb.AppendLine("═══ 设备信息 ═══");
                    sb.AppendLine($"品牌:        {brand}");
                    sb.AppendLine($"型号:        {model}");
                    sb.AppendLine($"Android:     {androidVer} (SDK {sdk})");
                    sb.AppendLine($"序列号:      {serial}");
                    sb.AppendLine($"分辨率:      {resolution}");
                    sb.AppendLine($"屏幕密度:    {density}");
                    sb.AppendLine($"电量:        {batteryInfo}");
                    sb.AppendLine($"存储:        {storageInfo}");

                    string text = sb.ToString();
                    MainThreadDispatcher.Enqueue(() => SetOutput(deviceOutput, text));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AdbToolsApp] 获取设备信息异常: {ex.Message}");
                    MainThreadDispatcher.Enqueue(() =>
                        SetOutput(deviceOutput, $"[错误] 获取设备信息失败: {ex.Message}"));
                }
            });
        }

        private string GetProp(string prop)
        {
            var result = adb.Execute($"shell getprop {prop}");
            return result.IsSuccess ? result.Output.Trim() : "N/A";
        }

        private static string ParseBattery(string dumpsysOutput)
        {
            if (string.IsNullOrEmpty(dumpsysOutput)) return "N/A";

            string level = "?";
            string status = "";
            var lines = dumpsysOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("level:", StringComparison.OrdinalIgnoreCase))
                    level = trimmed.Substring(6).Trim();
                else if (trimmed.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = trimmed.Substring(7).Trim();
                    // status: 2=charging, 3=discharging, 4=not charging, 5=full
                    switch (val)
                    {
                        case "2": status = "充电中"; break;
                        case "3": status = "放电中"; break;
                        case "4": status = "未充电"; break;
                        case "5": status = "已充满"; break;
                        default: status = val; break;
                    }
                }
            }

            return string.IsNullOrEmpty(status) ? $"{level}%" : $"{level}% ({status})";
        }

        private static string ParseStorage(string dfOutput)
        {
            if (string.IsNullOrEmpty(dfOutput)) return "N/A";

            var lines = dfOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            // Skip header line, parse first data line
            if (lines.Length < 2) return "N/A";

            // df output columns: Filesystem  1K-blocks  Used  Available  Use%  Mounted on
            var parts = lines[1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return "N/A";

            if (long.TryParse(parts[1], out long totalKb) &&
                long.TryParse(parts[2], out long usedKb))
            {
                double totalGb = totalKb / 1048576.0;
                double usedGb = usedKb / 1048576.0;
                int percent = totalKb > 0 ? (int)(usedKb * 100 / totalKb) : 0;
                return $"{usedGb:F1}G / {totalGb:F1}G (已用 {percent}%)";
            }

            return "N/A";
        }

        #endregion

        #region Install APK Panel

        private VisualElement BuildInstallPanel()
        {
            var panel = new ScrollView(ScrollViewMode.Vertical);
            panel.style.flexGrow = 1;
            panel.style.flexShrink = 1;
            panel.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            panel.verticalScrollerVisibility = ScrollerVisibility.Auto;

            panel.Add(SectionTitle("安装 APK"));
            panel.Add(SectionDesc("选择 APK 文件并安装到已连接的设备（支持附带 OBB 扩展文件）"));

            // File path row
            var fileRow = Row();

            installPathField = StyledTextField("APK 文件路径", "点击浏览选择APK文件...");
            installPathField.style.flexGrow = 1;
            installPathField.style.flexShrink = 1;
            installPathField.style.overflow = Overflow.Hidden;
            fileRow.Add(installPathField);

            var browseBtn = ActionButton("浏览...", ColAccent);
            browseBtn.style.width = 90;
            browseBtn.style.marginRight = 0;
            browseBtn.clicked += () =>
            {
                string path = FileDialogHelper.OpenApkFileDialog();
                if (!string.IsNullOrEmpty(path))
                    installPathField.value = path;
            };
            fileRow.Add(browseBtn);
            panel.Add(fileRow);

            // OBB file path row (optional)
            var obbRow = Row();
            obbRow.style.marginTop = 8;

            obbPathField = StyledTextField("OBB 文件路径 (可选)", "拖拽或浏览选择OBB文件...");
            obbPathField.style.flexGrow = 1;
            obbPathField.style.flexShrink = 1;
            obbPathField.style.overflow = Overflow.Hidden;
            obbRow.Add(obbPathField);

            var obbBrowseBtn = ActionButton("浏览...", ColAccent);
            obbBrowseBtn.style.width = 90;
            obbBrowseBtn.style.marginRight = 0;
            obbBrowseBtn.clicked += () =>
            {
                string path = FileDialogHelper.OpenObbFileDialog();
                if (!string.IsNullOrEmpty(path))
                    obbPathField.value = path;
            };
            obbRow.Add(obbBrowseBtn);
            panel.Add(obbRow);

            // AB package folder path row (optional)
            var abRow = Row();
            abRow.style.marginTop = 8;

            abPathField = StyledTextField("AB 包路径 (可选)", "拖拽或浏览选择AB包文件夹...");
            abPathField.style.flexGrow = 1;
            abPathField.style.flexShrink = 1;
            abPathField.style.overflow = Overflow.Hidden;
            abRow.Add(abPathField);

            var abBrowseBtn = ActionButton("浏览...", ColAccent);
            abBrowseBtn.style.width = 90;
            abBrowseBtn.style.marginRight = 0;
            abBrowseBtn.clicked += () =>
            {
                string path = Win32Dialog.OpenFolderDialog("选择AB包文件夹");
                if (!string.IsNullOrEmpty(path))
                    abPathField.value = path;
            };
            abRow.Add(abBrowseBtn);
            panel.Add(abRow);

            // ── Target device selection ──
            var deviceSection = new VisualElement();
            deviceSection.style.marginTop = 12;
            deviceSection.style.paddingTop = 10;
            deviceSection.style.paddingBottom = 10;
            deviceSection.style.paddingLeft = 14;
            deviceSection.style.paddingRight = 14;
            deviceSection.style.backgroundColor = ColBgOutput;
            SetBorderRadius(deviceSection, 6);
            deviceSection.style.borderTopWidth = 1;
            deviceSection.style.borderBottomWidth = 1;
            deviceSection.style.borderLeftWidth = 1;
            deviceSection.style.borderRightWidth = 1;
            deviceSection.style.borderTopColor = ColBorder;
            deviceSection.style.borderBottomColor = ColBorder;
            deviceSection.style.borderLeftColor = ColBorder;
            deviceSection.style.borderRightColor = ColBorder;

            var devHeaderRow = Row();
            var devTitle = new Label("目标设备");
            devTitle.style.fontSize = 13;
            devTitle.style.color = ColTextDim;
            devTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            devTitle.style.flexGrow = 1;
            devHeaderRow.Add(devTitle);

            installSelectAllBtn = ActionButton("全选", ColNeutral);
            installSelectAllBtn.style.height = 26;
            installSelectAllBtn.style.paddingLeft = 10;
            installSelectAllBtn.style.paddingRight = 10;
            installSelectAllBtn.style.fontSize = 11;
            installSelectAllBtn.clicked += OnInstallToggleSelectAll;
            devHeaderRow.Add(installSelectAllBtn);

            installRefreshDevicesBtn = ActionButton("刷新设备", ColAccent);
            installRefreshDevicesBtn.style.height = 26;
            installRefreshDevicesBtn.style.paddingLeft = 10;
            installRefreshDevicesBtn.style.paddingRight = 10;
            installRefreshDevicesBtn.style.fontSize = 11;
            installRefreshDevicesBtn.style.marginRight = 0;
            installRefreshDevicesBtn.clicked += RefreshInstallDeviceList;
            devHeaderRow.Add(installRefreshDevicesBtn);
            deviceSection.Add(devHeaderRow);

            installDeviceStatusLabel = new Label("点击「刷新设备」获取已连接设备列表");
            installDeviceStatusLabel.style.fontSize = 11;
            installDeviceStatusLabel.style.color = ColTextDim;
            installDeviceStatusLabel.style.marginTop = 4;
            deviceSection.Add(installDeviceStatusLabel);

            installDeviceListContainer = new VisualElement();
            installDeviceListContainer.style.marginTop = 4;
            deviceSection.Add(installDeviceListContainer);

            panel.Add(deviceSection);

            // Install options row
            var optRow = Row();
            optRow.style.marginTop = 10;

            var installBtn = ActionButton("安装到选中设备", ColSuccess);
            installBtn.style.height = 40;
            installBtn.style.fontSize = 14;
            installBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            installBtn.style.flexGrow = 1;
            installBtn.style.flexShrink = 1;
            installBtn.clicked += () => InstallToSelectedDevices("install");
            optRow.Add(installBtn);

            var reinstallBtn = ActionButton("覆盖安装 (-r)", ColWarning);
            reinstallBtn.style.height = 40;
            reinstallBtn.style.flexGrow = 1;
            reinstallBtn.style.flexShrink = 1;
            reinstallBtn.style.marginRight = 0;
            reinstallBtn.clicked += () => InstallToSelectedDevices("install -r");
            optRow.Add(reinstallBtn);

            panel.Add(optRow);

            panel.Add(BuildInstallTransferPanel());

            // Drag & drop hint
            dropHint = new VisualElement();
            dropHint.style.marginTop = 12;
            dropHint.style.height = 60;
            SetBorderRadius(dropHint, 8);
            dropHint.style.borderTopWidth = 2;
            dropHint.style.borderBottomWidth = 2;
            dropHint.style.borderLeftWidth = 2;
            dropHint.style.borderRightWidth = 2;
            dropHint.style.borderTopColor = ColBorder;
            dropHint.style.borderBottomColor = ColBorder;
            dropHint.style.borderLeftColor = ColBorder;
            dropHint.style.borderRightColor = ColBorder;
            dropHint.style.justifyContent = Justify.Center;
            dropHint.style.alignItems = Align.Center;
            var dropLabel = new Label("提示: 将 APK、OBB 文件或 AB 包文件夹拖拽到窗口中，路径会自动填入对应输入框");
            dropLabel.style.color = ColTextDim;
            dropLabel.style.fontSize = 12;
            dropHint.Add(dropLabel);
            panel.Add(dropHint);

            // Output area
            panel.Add(OutputArea(out installOutput, 180));

            return panel;
        }

        // ── Device list for install ──

        private void RefreshInstallDeviceList()
        {
            installRefreshDevicesBtn.SetEnabled(false);
            installDeviceStatusLabel.text = "正在获取设备列表...";
            installDeviceStatusLabel.style.color = ColWarning;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = adb.Execute("devices -l");
                MainThreadDispatcher.Enqueue(() => OnInstallDeviceListResult(result));
            });
        }

        private void OnInstallDeviceListResult(AdbResult result)
        {
            installRefreshDevicesBtn.SetEnabled(true);
            installDeviceListContainer.Clear();
            installDeviceEntries.Clear();

            if (!result.IsSuccess || !result.HasOutput)
            {
                installDeviceStatusLabel.text = "获取设备列表失败";
                installDeviceStatusLabel.style.color = ColDanger;
                return;
            }

            var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                // Skip header "List of devices attached"
                if (line.StartsWith("List of")) continue;
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Format: "SERIAL\tSTATUS  key:value key:value"
                // e.g.:   "192.168.1.100:5555\tdevice product:redfin model:Pixel_5 ..."
                //         "emulator-5554\tdevice"
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                string serial = parts[0];
                string status = parts[1];

                // Only include 'device' status (skip offline, unauthorized, etc.)
                if (status != "device") continue;

                // Build description from remaining key:value pairs
                string desc = serial;
                string model = "";
                for (int i = 2; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith("model:"))
                        model = parts[i].Substring(6);
                }
                if (!string.IsNullOrEmpty(model))
                    desc = $"{serial}  ({model})";

                var entry = new DeviceEntry { serial = serial, description = desc };

                // Build checkbox row
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 28;
                row.style.marginTop = 2;

                var toggle = new Toggle();
                toggle.value = true;
                toggle.style.marginRight = 6;
                toggle.style.flexShrink = 0;
                row.Add(toggle);
                entry.toggle = toggle;

                var nameLabel = new Label(desc);
                nameLabel.style.fontSize = 12;
                nameLabel.style.color = ColText;
                nameLabel.style.flexGrow = 1;
                row.Add(nameLabel);

                var statusBadge = new Label("已连接");
                statusBadge.style.fontSize = 10;
                statusBadge.style.color = ColSuccess;
                statusBadge.style.marginLeft = 8;
                row.Add(statusBadge);

                installDeviceListContainer.Add(row);
                installDeviceEntries.Add(entry);
            }

            if (installDeviceEntries.Count == 0)
            {
                installDeviceStatusLabel.text = "未发现已连接的设备";
                installDeviceStatusLabel.style.color = ColWarning;
            }
            else
            {
                installDeviceStatusLabel.text = $"发现 {installDeviceEntries.Count} 台设备";
                installDeviceStatusLabel.style.color = ColSuccess;
            }
        }

        private void OnInstallToggleSelectAll()
        {
            installSelectAllState = !installSelectAllState;
            installSelectAllBtn.text = installSelectAllState ? "取消全选" : "全选";
            foreach (var entry in installDeviceEntries)
            {
                if (entry.toggle != null)
                    entry.toggle.value = installSelectAllState;
            }
        }

        private void InstallToSelectedDevices(string installCmd)
        {
            string apkPath = installPathField.value.Trim();
            if (!ValidateApkPath(apkPath)) return;
            ResetInstallTransferProgress();

            // Gather selected devices
            var selectedSerials = new List<string>();
            foreach (var entry in installDeviceEntries)
            {
                if (entry.toggle != null && entry.toggle.value)
                    selectedSerials.Add(entry.serial);
            }

            if (selectedSerials.Count == 0)
            {
                // Fallback: no device selected (list may be empty) → install without -s (default device)
                if (installDeviceEntries.Count == 0)
                {
                    InstallWithObb(apkPath, installCmd, null);
                    return;
                }
                SetOutput(installOutput, "[错误] 请至少选择一台设备");
                return;
            }

            if (selectedSerials.Count == 1)
            {
                // Single device: simple path
                InstallWithObb(apkPath, installCmd, selectedSerials[0]);
                return;
            }

            // Multiple devices: parallel install
            string obbPath = obbPathField.value.Trim();
            bool hasObb = !string.IsNullOrEmpty(obbPath) && File.Exists(obbPath);
            string abPath = abPathField.value.Trim();
            bool hasAb = !string.IsNullOrEmpty(abPath) && Directory.Exists(abPath);
            bool hasInvalidAb = !string.IsNullOrEmpty(abPath) && !hasAb;
            string apkPackageName = hasAb ? ExtractPackageNameFromApk(apkPath) : null;

            int total = selectedSerials.Count;
            int completed = 0;
            var results = new string[total];

            SetOutput(installOutput, $"正在向 {total} 台设备并行安装...\n");

            for (int i = 0; i < total; i++)
            {
                int idx = i;
                string serial = selectedSerials[i];

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"═══ 设备: {serial} ═══");

                        // Install APK
                        var installResult = adb.Execute($"-s {serial} {installCmd} \"{apkPath}\"");
                        sb.AppendLine(installResult.GetDisplayText());

                        // OBB push (if applicable and install succeeded)
                        if (hasObb && installResult.IsSuccess)
                        {
                            string obbFileName = Path.GetFileName(obbPath);
                            string packageName = ExtractPackageNameFromObb(obbFileName);
                            if (!string.IsNullOrEmpty(packageName))
                            {
                                string remotePath = $"/sdcard/Android/obb/{packageName}/";
                                adb.Execute($"-s {serial} shell mkdir -p {ShellQuote(remotePath)}");
                                long obbSize = new FileInfo(obbPath).Length;
                                ReportInstallTransferPreparing(serial, "OBB", obbFileName, obbSize);
                                var pushResult = adb.ExecuteWithProgress(
                                    $"-s {serial} push \"{obbPath}\" \"{remotePath}{obbFileName}\"",
                                    obbSize,
                                    progress => ReportInstallTransferProgress(serial, "OBB", obbFileName, progress));
                                sb.AppendLine("--- OBB ---");
                                sb.AppendLine(pushResult.GetDisplayText());
                                if (!pushResult.IsSuccess)
                                    ReportInstallTransferFailure(serial, "OBB", obbFileName, pushResult);
                            }
                        }

                        if (hasInvalidAb)
                        {
                            sb.AppendLine("--- AB 包 ---");
                            sb.AppendLine($"[警告] AB包路径不是有效文件夹，已跳过: {abPath}");
                        }

                        // AB 包目录不带包名信息，统一使用 APK Manifest 中的包名作为目标目录。
                        if (hasAb && installResult.IsSuccess)
                        {
                            if (string.IsNullOrEmpty(apkPackageName))
                            {
                                sb.AppendLine("--- AB 包 ---");
                                sb.AppendLine("[警告] 无法从APK中解析包名，跳过AB包推送。");
                            }
                            else
                            {
                                sb.Append(PushAbFolderToDevice(
                                    abPath, apkPackageName, $"-s {serial} ", serial));
                            }
                        }

                        results[idx] = sb.ToString();
                    }
                    catch (Exception ex)
                    {
                        results[idx] = $"═══ 设备: {serial} ═══\n[错误] {ex.Message}";
                    }

                    int done = Interlocked.Increment(ref completed);

                    // Update progress on main thread
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        if (done < total)
                        {
                            SetOutput(installOutput, $"安装进度: {done}/{total} 台设备已完成...");
                        }
                        else
                        {
                            // All done, show combined results
                            var allResults = new StringBuilder();
                            allResults.AppendLine($"全部 {total} 台设备安装完成:\n");
                            for (int r = 0; r < results.Length; r++)
                            {
                                allResults.AppendLine(results[r]);
                            }
                            SetOutput(installOutput, allResults.ToString());
                        }
                    });
                });
            }
        }

        // ── Single-device install (with optional -s serial) ──

        private void InstallWithObb(string apkPath, string installCmd, string serial)
        {
            string obbPath = obbPathField.value.Trim();
            bool hasObb = !string.IsNullOrEmpty(obbPath) && File.Exists(obbPath);
            string abPath = abPathField.value.Trim();
            bool hasAb = !string.IsNullOrEmpty(abPath) && Directory.Exists(abPath);
            bool hasInvalidAb = !string.IsNullOrEmpty(abPath) && !hasAb;
            string prefix = serial != null ? $"-s {serial} " : "";
            string deviceLabel = serial ?? "默认设备";

            SetOutput(installOutput, $"正在安装APK到 {deviceLabel}，请稍候...\n(此操作可能需要较长时间)");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Step 1: Install APK
                    var result = adb.Execute($"{prefix}{installCmd} \"{apkPath}\"");
                    var output = new StringBuilder(result.GetDisplayText());

                    if (!result.IsSuccess)
                    {
                        MainThreadDispatcher.Enqueue(() => SetOutput(installOutput, output.ToString()));
                        return;
                    }

                    if (hasObb || hasAb)
                    {
                        MainThreadDispatcher.Enqueue(() =>
                            SetOutput(installOutput, output + "\n\nAPK安装成功，正在推送扩展资源..."));
                    }

                    if (hasObb)
                    {
                        // Step 2: Extract package name from OBB filename
                        string obbFileName = Path.GetFileName(obbPath);
                        string packageName = ExtractPackageNameFromObb(obbFileName);

                        if (string.IsNullOrEmpty(packageName))
                        {
                            output.AppendLine();
                            output.AppendLine("--- OBB ---");
                            output.AppendLine("[警告] 无法从OBB文件名中提取包名，跳过OBB安装。");
                            output.AppendLine("OBB文件名格式应为: main.<版本号>.<包名>.obb");
                        }
                        else
                        {
                            string remotePath = $"/sdcard/Android/obb/{packageName}/";
                            adb.Execute($"{prefix}shell mkdir -p {ShellQuote(remotePath)}");
                            long obbSize = new FileInfo(obbPath).Length;
                            ReportInstallTransferPreparing(deviceLabel, "OBB", obbFileName, obbSize);
                            var pushResult = adb.ExecuteWithProgress(
                                $"{prefix}push \"{obbPath}\" \"{remotePath}{obbFileName}\"",
                                obbSize,
                                progress => ReportInstallTransferProgress(
                                    deviceLabel, "OBB", obbFileName, progress));

                            output.AppendLine();
                            output.AppendLine("--- OBB 推送结果 ---");
                            output.Append(pushResult.GetDisplayText());
                            if (!pushResult.IsSuccess)
                                ReportInstallTransferFailure(deviceLabel, "OBB", obbFileName, pushResult);
                        }
                    }

                    if (hasInvalidAb)
                    {
                        output.AppendLine();
                        output.AppendLine("--- AB 包 ---");
                        output.AppendLine($"[警告] AB包路径不是有效文件夹，已跳过: {abPath}");
                    }

                    if (hasAb)
                    {
                        string packageName = ExtractPackageNameFromApk(apkPath);
                        if (string.IsNullOrEmpty(packageName))
                        {
                            output.AppendLine();
                            output.AppendLine("--- AB 包 ---");
                            output.AppendLine("[警告] 无法从APK中解析包名，跳过AB包推送。");
                        }
                        else
                        {
                            output.AppendLine();
                            output.Append(PushAbFolderToDevice(abPath, packageName, prefix, deviceLabel));
                        }
                    }

                    MainThreadDispatcher.Enqueue(() =>
                        SetOutput(installOutput, output.ToString()));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AdbToolsApp] 安装失败: {ex.Message}");
                    MainThreadDispatcher.Enqueue(() =>
                        SetOutput(installOutput, $"[错误] 安装过程异常: {ex.Message}"));
                }
            });
        }

        private string PushAbFolderToDevice(
            string abPath,
            string packageName,
            string prefix,
            string deviceLabel)
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- AB 包 ---");

            string remotePath = $"/sdcard/Android/obb/{packageName}/";
            sb.AppendLine($"目标目录: {remotePath}");

            string[] files;
            try
            {
                files = Directory.GetFiles(abPath, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[错误] 读取AB包文件夹失败: {ex.Message}");
                return sb.ToString();
            }

            if (files.Length == 0)
            {
                sb.AppendLine("[警告] AB包文件夹为空，未推送任何文件。");
                return sb.ToString();
            }

            var mkdirResult = adb.Execute($"{prefix}shell mkdir -p {ShellQuote(remotePath)}");
            if (!mkdirResult.IsSuccess)
            {
                sb.AppendLine(mkdirResult.GetDisplayText());
                return sb.ToString();
            }

            // 单进程目录同步可避免为每个文件重复连接 ADB；重试时会跳过已完成文件。
            string sourcePath = abPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar + ".";
            long totalBytes = files.Sum(file => new FileInfo(file).Length);
            long syncedBytes = CalculateAlreadySyncedBytes(abPath, files, remotePath, prefix);
            string itemLabel = $"{Path.GetFileName(abPath)} · {files.Length} 个文件";
            ReportInstallTransferPreparing(deviceLabel, "AB", itemLabel, totalBytes, syncedBytes);
            var pushResult = adb.ExecuteWithProgress(
                $"{prefix}push --sync \"{sourcePath}\" \"{remotePath}\"",
                totalBytes,
                progress => ReportInstallTransferProgress(deviceLabel, "AB", itemLabel, progress),
                initialBytes: syncedBytes,
                preserveProgressOnRetry: true);
            if (!pushResult.IsSuccess)
            {
                ReportInstallTransferFailure(deviceLabel, "AB", itemLabel, pushResult);
                sb.AppendLine("[错误] AB包目录推送失败");
                sb.AppendLine(pushResult.GetDisplayText());
                return sb.ToString();
            }

            sb.AppendLine(pushResult.GetDisplayText());
            sb.AppendLine($"AB包推送完成: {files.Length} 个文件");
            return sb.ToString();
        }

        private VisualElement BuildInstallTransferPanel()
        {
            installTransferPanel = new VisualElement();
            installTransferPanel.style.display = DisplayStyle.None;
            installTransferPanel.style.marginTop = 12;
            installTransferPanel.style.paddingTop = 10;
            installTransferPanel.style.paddingBottom = 10;
            installTransferPanel.style.paddingLeft = 12;
            installTransferPanel.style.paddingRight = 12;
            installTransferPanel.style.backgroundColor = ColBgOutput;
            SetBorderRadius(installTransferPanel, 6);
            SetBorderWidth(installTransferPanel, 1);
            installTransferPanel.style.borderTopColor = ColBorder;
            installTransferPanel.style.borderBottomColor = ColBorder;
            installTransferPanel.style.borderLeftColor = ColBorder;
            installTransferPanel.style.borderRightColor = ColBorder;

            var title = new Label("传输进度");
            title.style.fontSize = 12;
            title.style.color = ColTextDim;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            installTransferPanel.Add(title);

            installTransferRowsContainer = new VisualElement();
            installTransferRowsContainer.style.marginTop = 6;
            installTransferPanel.Add(installTransferRowsContainer);
            return installTransferPanel;
        }

        private void ResetInstallTransferProgress()
        {
            installTransferRows.Clear();
            installTransferRowsContainer?.Clear();
            if (installTransferPanel != null)
                installTransferPanel.style.display = DisplayStyle.None;
            if (dropHint != null)
                dropHint.style.display = DisplayStyle.Flex;
        }

        private void ReportInstallTransferPreparing(
            string deviceLabel,
            string packageType,
            string itemLabel,
            long totalBytes,
            long transferredBytes = 0)
        {
            ReportInstallTransferProgress(deviceLabel, packageType, itemLabel, new AdbTransferProgress
            {
                BytesTransferred = transferredBytes,
                TotalBytes = totalBytes,
                Status = transferredBytes > 0 ? "正在检查增量文件" : "准备传输"
            });
        }

        private long CalculateAlreadySyncedBytes(
            string localRoot,
            IEnumerable<string> localFiles,
            string remoteRoot,
            string prefix)
        {
            var result = adb.Execute(
                $"{prefix}shell find {ShellQuote(remoteRoot)} -type f " +
                "-exec stat -c '%n|%s|%Y' {} \\;",
                120000);
            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
                return 0;

            var remoteFiles = ParseRemoteFileManifest(result.Output, remoteRoot);
            string normalizedRoot = Path.GetFullPath(localRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            long syncedBytes = 0;

            foreach (string file in localFiles)
            {
                string fullPath = Path.GetFullPath(file);
                if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                string relativePath = fullPath.Substring(normalizedRoot.Length).Replace('\\', '/');
                if (!remoteFiles.TryGetValue(relativePath, out RemoteFileState remoteFile))
                    continue;

                var localInfo = new FileInfo(file);
                long localModifiedTime = new DateTimeOffset(localInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
                if (remoteFile.Size == localInfo.Length &&
                    Math.Abs(remoteFile.ModifiedTime - localModifiedTime) <= 1)
                {
                    syncedBytes += localInfo.Length;
                }
            }

            return syncedBytes;
        }

        private static Dictionary<string, RemoteFileState> ParseRemoteFileManifest(
            string output,
            string remoteRoot)
        {
            var files = new Dictionary<string, RemoteFileState>(StringComparer.Ordinal);
            string normalizedRoot = remoteRoot.TrimEnd('/') + "/";

            foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int modifiedSeparator = rawLine.LastIndexOf('|');
                int sizeSeparator = modifiedSeparator > 0
                    ? rawLine.LastIndexOf('|', modifiedSeparator - 1)
                    : -1;
                if (sizeSeparator <= 0 ||
                    !long.TryParse(rawLine.Substring(sizeSeparator + 1, modifiedSeparator - sizeSeparator - 1),
                        out long size) ||
                    !long.TryParse(rawLine.Substring(modifiedSeparator + 1), out long modifiedTime))
                {
                    continue;
                }

                string remotePath = rawLine.Substring(0, sizeSeparator).Replace('\\', '/');
                if (!remotePath.StartsWith(normalizedRoot, StringComparison.Ordinal))
                    continue;

                files[remotePath.Substring(normalizedRoot.Length)] = new RemoteFileState
                {
                    Size = size,
                    ModifiedTime = modifiedTime
                };
            }

            return files;
        }

        private struct RemoteFileState
        {
            public long Size;
            public long ModifiedTime;
        }

        private void ReportInstallTransferProgress(
            string deviceLabel,
            string packageType,
            string itemLabel,
            AdbTransferProgress progress)
        {
            MainThreadDispatcher.Enqueue(() =>
                UpdateInstallTransferProgress(deviceLabel, packageType, itemLabel, progress, false));
        }

        private void ReportInstallTransferFailure(
            string deviceLabel,
            string packageType,
            string itemLabel,
            AdbResult result)
        {
            var progress = new AdbTransferProgress { Status = "传输失败" };
            MainThreadDispatcher.Enqueue(() =>
                UpdateInstallTransferProgress(
                    deviceLabel, packageType, itemLabel, progress, true, result.Error));
        }

        private void UpdateInstallTransferProgress(
            string deviceLabel,
            string packageType,
            string itemLabel,
            AdbTransferProgress progress,
            bool isError,
            string errorMessage = null)
        {
            string rowKey = string.IsNullOrEmpty(deviceLabel) ? "默认设备" : deviceLabel;
            if (!installTransferRows.TryGetValue(rowKey, out TransferProgressRow row))
            {
                row = CreateInstallTransferRow();
                installTransferRows[rowKey] = row;
                installTransferRowsContainer.Add(row.Container);
            }

            installTransferPanel.style.display = DisplayStyle.Flex;
            if (dropHint != null)
                dropHint.style.display = DisplayStyle.None;
            row.Header.text = $"{rowKey}  ·  {packageType}  ·  {itemLabel}";
            row.Header.style.color = isError ? ColDanger : ColText;
            row.ProgressBar.value = isError ? row.ProgressBar.value : progress.Percent;
            row.ProgressBar.title = isError ? "失败" : $"{progress.Percent:F1}%";

            if (isError)
            {
                row.Details.text = string.IsNullOrWhiteSpace(errorMessage)
                    ? "传输失败，请查看下方日志"
                    : $"传输失败：{errorMessage.Trim()}";
                row.Details.style.color = ColDanger;
                return;
            }

            string sizeText = $"{FormatFileSize(progress.BytesTransferred)} / {FormatFileSize(progress.TotalBytes)}";
            string speedText = progress.BytesPerSecond > 0
                ? FormatFileSize((long)progress.BytesPerSecond) + "/s"
                : "--";
            string etaText = FormatTransferRemaining(progress.EstimatedRemaining);
            string attemptText = progress.Attempt > 1 ? $"  ·  第 {progress.Attempt} 次尝试" : "";
            bool completed = progress.Status == "传输完成";
            row.Details.text = completed
                ? $"传输完成  ·  {sizeText}  ·  平均 {speedText}{attemptText}"
                : $"{progress.Status}  ·  {sizeText}  ·  {speedText}  ·  剩余 {etaText}{attemptText}";
            row.Details.style.color = completed ? ColSuccess : ColTextDim;
        }

        private TransferProgressRow CreateInstallTransferRow()
        {
            var container = new VisualElement();
            container.style.marginBottom = 6;

            var header = new Label();
            header.style.fontSize = 11;
            header.style.color = ColText;
            header.style.whiteSpace = WhiteSpace.Normal;
            container.Add(header);

            var progressBar = new ProgressBar
            {
                lowValue = 0,
                highValue = 100,
                value = 0,
                title = "0.0%"
            };
            progressBar.style.height = 20;
            progressBar.style.marginTop = 4;
            container.Add(progressBar);

            var details = new Label("准备传输");
            details.style.fontSize = 10;
            details.style.color = ColTextDim;
            details.style.marginTop = 3;
            details.style.whiteSpace = WhiteSpace.Normal;
            container.Add(details);

            return new TransferProgressRow
            {
                Container = container,
                Header = header,
                ProgressBar = progressBar,
                Details = details
            };
        }

        private static string FormatTransferRemaining(TimeSpan? remaining)
        {
            if (!remaining.HasValue || remaining.Value.TotalSeconds < 0.5)
                return "--";

            TimeSpan value = remaining.Value;
            if (value.TotalHours >= 1)
                return $"{(int)value.TotalHours}小时{value.Minutes:D2}分";
            if (value.TotalMinutes >= 1)
                return $"{(int)value.TotalMinutes}分{value.Seconds:D2}秒";
            return $"{Math.Max(1, value.Seconds)}秒";
        }

        private static string ExtractPackageNameFromApk(string apkPath)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(apkPath))
                {
                    var manifestEntry = archive.GetEntry("AndroidManifest.xml");
                    if (manifestEntry == null) return null;

                    using (var stream = manifestEntry.Open())
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ExtractPackageNameFromBinaryManifest(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdbToolsApp] APK包名解析失败: {ex.Message}");
                return null;
            }
        }

        private static string ExtractPackageNameFromBinaryManifest(byte[] data)
        {
            if (data == null || data.Length < 8) return null;

            string[] strings = null;
            int offset = 8;

            while (offset + 8 <= data.Length)
            {
                ushort chunkType = ReadUInt16(data, offset);
                uint chunkSize = ReadUInt32(data, offset + 4);
                if (chunkSize < 8 || offset + chunkSize > data.Length) break;

                if (chunkType == 0x0001)
                {
                    strings = ReadStringPool(data, offset);
                }
                else if (chunkType == 0x0102 && strings != null)
                {
                    string packageName = TryReadPackageNameFromStartElement(data, offset, strings);
                    if (!string.IsNullOrEmpty(packageName))
                        return packageName;
                }

                offset += (int)chunkSize;
            }

            return null;
        }

        private static string TryReadPackageNameFromStartElement(byte[] data, int chunkOffset, string[] strings)
        {
            if (chunkOffset + 36 > data.Length) return null;

            uint elementNameIndex = ReadUInt32(data, chunkOffset + 20);
            string elementName = GetStringAt(strings, elementNameIndex);
            if (!string.Equals(elementName, "manifest", StringComparison.Ordinal))
                return null;

            ushort headerSize = ReadUInt16(data, chunkOffset + 2);
            ushort attributeStart = ReadUInt16(data, chunkOffset + 24);
            ushort attributeSize = ReadUInt16(data, chunkOffset + 26);
            ushort attributeCount = ReadUInt16(data, chunkOffset + 28);
            int attrOffset = chunkOffset + headerSize + attributeStart;

            for (int i = 0; i < attributeCount; i++)
            {
                int current = attrOffset + i * attributeSize;
                if (current + 20 > data.Length) break;

                uint attrNameIndex = ReadUInt32(data, current + 4);
                string attrName = GetStringAt(strings, attrNameIndex);
                if (!string.Equals(attrName, "package", StringComparison.Ordinal))
                    continue;

                uint rawValueIndex = ReadUInt32(data, current + 8);
                if (rawValueIndex != 0xFFFFFFFF)
                    return GetStringAt(strings, rawValueIndex);

                byte dataType = data[current + 15];
                uint dataIndex = ReadUInt32(data, current + 16);
                return dataType == 0x03 ? GetStringAt(strings, dataIndex) : null;
            }

            return null;
        }

        private static string[] ReadStringPool(byte[] data, int chunkOffset)
        {
            if (chunkOffset + 28 > data.Length) return null;

            uint stringCount = ReadUInt32(data, chunkOffset + 8);
            uint flags = ReadUInt32(data, chunkOffset + 16);
            uint stringsStart = ReadUInt32(data, chunkOffset + 20);
            bool isUtf8 = (flags & 0x00000100) != 0;
            int offsetsStart = chunkOffset + 28;
            int stringsBase = chunkOffset + (int)stringsStart;

            var strings = new string[stringCount];
            for (int i = 0; i < stringCount; i++)
            {
                int offsetPosition = offsetsStart + i * 4;
                if (offsetPosition + 4 > data.Length) break;

                int stringOffset = stringsBase + (int)ReadUInt32(data, offsetPosition);
                strings[i] = isUtf8
                    ? ReadUtf8String(data, stringOffset)
                    : ReadUtf16String(data, stringOffset);
            }

            return strings;
        }

        private static string ReadUtf8String(byte[] data, int offset)
        {
            int cursor = offset;
            if (!TryReadUtf8Length(data, ref cursor, out _)) return null;
            if (!TryReadUtf8Length(data, ref cursor, out int byteLength)) return null;
            if (cursor < 0 || cursor + byteLength > data.Length) return null;
            return Encoding.UTF8.GetString(data, cursor, byteLength);
        }

        private static string ReadUtf16String(byte[] data, int offset)
        {
            int cursor = offset;
            if (!TryReadUtf16Length(data, ref cursor, out int charLength)) return null;
            int byteLength = charLength * 2;
            if (cursor < 0 || cursor + byteLength > data.Length) return null;
            return Encoding.Unicode.GetString(data, cursor, byteLength);
        }

        private static bool TryReadUtf8Length(byte[] data, ref int offset, out int length)
        {
            length = 0;
            if (offset >= data.Length) return false;

            int first = data[offset++];
            if ((first & 0x80) == 0)
            {
                length = first;
                return true;
            }

            if (offset >= data.Length) return false;
            length = ((first & 0x7F) << 8) | data[offset++];
            return true;
        }

        private static bool TryReadUtf16Length(byte[] data, ref int offset, out int length)
        {
            length = 0;
            if (offset + 2 > data.Length) return false;

            int first = ReadUInt16(data, offset);
            offset += 2;
            if ((first & 0x8000) == 0)
            {
                length = first;
                return true;
            }

            if (offset + 2 > data.Length) return false;
            int second = ReadUInt16(data, offset);
            offset += 2;
            length = ((first & 0x7FFF) << 16) | second;
            return true;
        }

        private static string GetStringAt(string[] strings, uint index)
        {
            if (strings == null || index == 0xFFFFFFFF || index >= strings.Length)
                return null;
            return strings[index];
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] |
                (data[offset + 1] << 8) |
                (data[offset + 2] << 16) |
                (data[offset + 3] << 24));
        }

        private static string ExtractPackageNameFromObb(string fileName)
        {
            // Format: main.314159.com.example.app.obb or patch.314159.com.example.app.obb
            if (string.IsNullOrEmpty(fileName)) return null;

            string name = fileName;
            if (name.EndsWith(".obb", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            // Skip first two segments (type and version), rest is package name
            int firstDot = name.IndexOf('.');
            if (firstDot < 0) return null;
            int secondDot = name.IndexOf('.', firstDot + 1);
            if (secondDot < 0) return null;

            return name.Substring(secondDot + 1);
        }

        private bool ValidateApkPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                SetOutput(installOutput, "[错误] 请先选择APK文件路径");
                return false;
            }
            if (!File.Exists(path))
            {
                SetOutput(installOutput, $"[错误] 文件不存在: {path}");
                return false;
            }
            if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                SetOutput(installOutput, "[警告] 所选文件不是APK格式，仍将尝试安装...");
            }
            return true;
        }

        #endregion

        #region Logcat Panel

        private VisualElement BuildLogcatPanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;

            panel.Add(SectionTitle("实时日志"));
            panel.Add(SectionDesc("捕获设备的实时 Logcat 输出"));

            // Controls row 1: filter + start/stop
            var row1 = Row();

            logcatFilterField = StyledTextField("过滤标签", "Unity");
            logcatFilterField.style.width = 200;
            row1.Add(logcatFilterField);

            logcatStartBtn = ActionButton("开始捕获", ColSuccess);
            logcatStartBtn.clicked += OnStartLogcat;
            row1.Add(logcatStartBtn);

            logcatStopBtn = ActionButton("停止", ColDanger);
            logcatStopBtn.style.marginRight = 0;
            logcatStopBtn.SetEnabled(false);
            logcatStopBtn.clicked += OnStopLogcat;
            row1.Add(logcatStopBtn);

            panel.Add(row1);

            // Logcat output area (custom, not using shared OutputArea)
            var outputContainer = new VisualElement();
            outputContainer.style.flexGrow = 1;
            outputContainer.style.marginTop = 12;

            logcatScroll = new ScrollView(ScrollViewMode.Vertical);
            logcatScroll.style.flexGrow = 1;
            logcatScroll.style.backgroundColor = ColBgOutput;
            SetBorderRadius(logcatScroll, 6);
            logcatScroll.style.borderTopWidth = 1;
            logcatScroll.style.borderBottomWidth = 1;
            logcatScroll.style.borderLeftWidth = 1;
            logcatScroll.style.borderRightWidth = 1;
            logcatScroll.style.borderTopColor = ColBorder;
            logcatScroll.style.borderBottomColor = ColBorder;
            logcatScroll.style.borderLeftColor = ColBorder;
            logcatScroll.style.borderRightColor = ColBorder;

            logcatLinesContainer = new VisualElement();
            logcatLinesContainer.style.paddingTop = 8;
            logcatLinesContainer.style.paddingBottom = 8;
            logcatLinesContainer.style.paddingLeft = 10;
            logcatLinesContainer.style.paddingRight = 10;

            var hintLabel = new Label("点击「开始捕获」以启动日志流...");
            hintLabel.style.color = ColTextDim;
            hintLabel.style.fontSize = 13;
            logcatLinesContainer.Add(hintLabel);
            logcatScroll.Add(logcatLinesContainer);

            outputContainer.Add(logcatScroll);

            // Bottom controls
            var bottomRow = Row();
            bottomRow.style.marginTop = 6;

            var clearBtn = ActionButton("清除", ColNeutral);
            clearBtn.clicked += () =>
            {
                lock (logcatLock) { logcatPendingLines.Clear(); }
                logcatSaveBuffer.Clear();
                logcatLinesContainer.Clear();
                logcatCurrentChunk = null;
                logcatCurrentChunkLen = 0;
                Interlocked.Exchange(ref logcatLineCount, 0);
                UpdateLogcatLineCount();
            };
            bottomRow.Add(clearBtn);

            var saveBtn = ActionButton("保存日志", ColAccent);
            saveBtn.clicked += () => SaveLogToFile(logcatSaveBuffer.ToString(), "logcat");
            bottomRow.Add(saveBtn);

            var autoScrollToggle = new Toggle("自动滚动");
            autoScrollToggle.value = true;
            autoScrollToggle.style.color = ColText;
            autoScrollToggle.style.marginLeft = 12;
            autoScrollToggle.RegisterValueChangedCallback(e => logcatAutoScroll = e.newValue);
            bottomRow.Add(autoScrollToggle);

            bottomRow.Add(Spacer());

            // Line count indicator
            var lineCount = new Label("0 行");
            lineCount.name = "logcat-line-count";
            lineCount.style.color = ColTextDim;
            lineCount.style.fontSize = 11;
            bottomRow.Add(lineCount);

            outputContainer.Add(bottomRow);
            panel.Add(outputContainer);

            return panel;
        }

        private int logcatLineCount = 0;

        private void OnStartLogcat()
        {
            string filter = logcatFilterField.value.Trim();
            string args = string.IsNullOrEmpty(filter) ? "logcat" : $"logcat -s {filter}";

            lock (logcatLock) { logcatPendingLines.Clear(); }
            logcatSaveBuffer.Clear();
            logcatLinesContainer.Clear();
            logcatCurrentChunk = null;
            logcatCurrentChunkLen = 0;
            logcatLineCount = 0;
            UpdateLogcatLineCount();

            logcatStartBtn.SetEnabled(false);
            logcatStopBtn.SetEnabled(true);

            adb.StartLogcat(args,
                line =>
                {
                    lock (logcatLock)
                    {
                        logcatPendingLines.Add(line);
                    }
                    Interlocked.Increment(ref logcatLineCount);
                    logcatDirty = true;
                },
                error =>
                {
                    lock (logcatLock)
                    {
                        logcatPendingLines.Add("[ERROR] " + error);
                    }
                    logcatDirty = true;
                }
            );
        }

        private void OnStopLogcat()
        {
            adb.StopLogcat();
            logcatStartBtn.SetEnabled(true);
            logcatStopBtn.SetEnabled(false);
            lock (logcatLock)
            {
                logcatPendingLines.Add("\n--- 日志捕获已停止 ---");
            }
            logcatDirty = true;
            FlushLogcatLines(); // Force immediate flush on stop
        }

        private void FlushLogcatLines()
        {
            List<string> lines;
            lock (logcatLock)
            {
                if (logcatPendingLines.Count == 0) return;
                lines = new List<string>(logcatPendingLines);
                logcatPendingLines.Clear();
            }

            var chunkBuffer = new StringBuilder();
            foreach (var line in lines)
            {
                logcatSaveBuffer.AppendLine(line);

                if (logcatCurrentChunk == null || logcatCurrentChunkLen > LogcatChunkMaxChars)
                {
                    // Flush buffer to previous chunk before creating new one
                    if (chunkBuffer.Length > 0 && logcatCurrentChunk != null)
                    {
                        logcatCurrentChunk.value += chunkBuffer.ToString();
                        chunkBuffer.Clear();
                    }
                    logcatCurrentChunk = CreateLogcatChunk();
                    logcatLinesContainer.Add(logcatCurrentChunk);
                    logcatCurrentChunkLen = 0;
                }

                chunkBuffer.Append(line);
                chunkBuffer.Append('\n');
                logcatCurrentChunkLen += line.Length + 1;
            }
            // Flush remaining buffer
            if (chunkBuffer.Length > 0 && logcatCurrentChunk != null)
                logcatCurrentChunk.value += chunkBuffer.ToString();

            // Trim old chunks to keep UI responsive
            while (logcatLinesContainer.childCount > LogcatMaxChunks)
                logcatLinesContainer.RemoveAt(0);

            // Trim save buffer (remove 50% when exceeded to reduce trim frequency)
            if (logcatSaveBuffer.Length > LogcatMaxSaveChars)
            {
                int removeCount = logcatSaveBuffer.Length / 2;
                logcatSaveBuffer.Remove(0, removeCount);
            }

            UpdateLogcatLineCount();

            if (logcatAutoScroll)
                logcatScroll.scrollOffset = new Vector2(0, float.MaxValue);
        }

        private TextField CreateLogcatChunk()
        {
            var field = new TextField();
            field.multiline = true;
            field.isReadOnly = true;
            field.style.marginTop = 0;
            field.style.marginBottom = 0;

            // Hide the built-in label
            field.labelElement.style.display = DisplayStyle.None;

            // Style the input area to blend seamlessly with the scroll background
            var input = field.Q<VisualElement>(className: "unity-base-text-field__input");
            if (input != null)
            {
                input.style.backgroundColor = Color.clear;
                input.style.borderTopWidth = 0;
                input.style.borderBottomWidth = 0;
                input.style.borderLeftWidth = 0;
                input.style.borderRightWidth = 0;
                input.style.paddingTop = 0;
                input.style.paddingBottom = 0;
                input.style.paddingLeft = 0;
                input.style.paddingRight = 0;
                input.style.color = ColText;
                input.style.fontSize = 13;
                input.style.whiteSpace = WhiteSpace.Normal;
            }

            return field;
        }

        private void UpdateLogcatLineCount()
        {
            var label = uiDoc.rootVisualElement.Q<Label>("logcat-line-count");
            if (label != null)
                label.text = $"{logcatLineCount} 行";
        }

        #endregion

        #region Apps Panel

        private VisualElement BuildAppsPanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;

            panel.Add(SectionTitle("应用管理"));
            panel.Add(SectionDesc("管理设备上的应用: 查看、启动、停止、卸载"));

            // Row 1: list button
            var row1 = Row();

            var listBtn = ActionButton("列出第三方应用", ColAccent);
            listBtn.style.marginRight = 0;
            listBtn.clicked += () =>
            {
                // Clear list and show loading state
                appsPackageNames.Clear();
                _selectedAppRow = null;
                appsListContainer.Clear();
                var loadingLabel = new Label("正在获取应用列表...");
                loadingLabel.style.color = ColTextDim;
                loadingLabel.style.fontSize = 12;
                loadingLabel.style.paddingLeft = 12;
                loadingLabel.style.paddingTop = 8;
                appsListContainer.Add(loadingLabel);

                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    var result = adb.Execute("shell pm list packages -3");
                    MainThreadDispatcher.Enqueue(() => PopulateAppList(result.GetDisplayText()));
                });
            };
            row1.Add(listBtn);

            panel.Add(row1);

            var searchRow = Row();
            searchRow.style.marginTop = 10;

            appsSearchField = StyledTextField("搜索应用", "输入包名关键字进行模糊搜索...");
            appsSearchField.style.flexGrow = 1;
            appsSearchField.style.flexShrink = 1;
            appsSearchField.style.overflow = Overflow.Hidden;
            appsSearchField.RegisterValueChangedCallback(_ => RenderAppList());
            searchRow.Add(appsSearchField);

            panel.Add(searchRow);

            // App list ScrollView
            appsListScroll = new ScrollView(ScrollViewMode.Vertical);
            appsListScroll.style.height = 190;
            appsListScroll.style.marginTop = 10;
            appsListScroll.style.backgroundColor = ColBgOutput;
            SetBorderRadius(appsListScroll, 6);
            appsListScroll.style.borderTopWidth = 1;
            appsListScroll.style.borderBottomWidth = 1;
            appsListScroll.style.borderLeftWidth = 1;
            appsListScroll.style.borderRightWidth = 1;
            appsListScroll.style.borderTopColor = ColBorder;
            appsListScroll.style.borderBottomColor = ColBorder;
            appsListScroll.style.borderLeftColor = ColBorder;
            appsListScroll.style.borderRightColor = ColBorder;

            appsListContainer = new VisualElement();
            appsListScroll.Add(appsListContainer);

            var emptyHint = new Label("点击「列出第三方应用」查看设备上已安装的应用");
            emptyHint.style.color = ColTextDim;
            emptyHint.style.fontSize = 12;
            emptyHint.style.paddingLeft = 12;
            emptyHint.style.paddingTop = 8;
            appsListContainer.Add(emptyHint);

            panel.Add(appsListScroll);

            // Package name input
            var row2 = Row();
            row2.style.marginTop = 10;

            appsPackageField = StyledTextField("应用包名", "com.example.app");
            appsPackageField.style.flexGrow = 1;
            appsPackageField.style.flexShrink = 1;
            appsPackageField.style.overflow = Overflow.Hidden;
            row2.Add(appsPackageField);
            panel.Add(row2);

            // Action buttons
            var row3 = Row();
            row3.style.marginTop = 10;

            var startBtn = ActionButton("启动应用", ColSuccess);
            startBtn.clicked += () =>
            {
                string pkg = appsPackageField.value.Trim();
                if (!string.IsNullOrEmpty(pkg))
                    RunAdbAsync($"shell monkey -p {pkg} -c android.intent.category.LAUNCHER 1",
                        appsOutput, $"正在启动 {pkg}...");
            };
            row3.Add(startBtn);

            var stopBtn = ActionButton("停止应用", ColWarning);
            stopBtn.clicked += () =>
            {
                string pkg = appsPackageField.value.Trim();
                if (!string.IsNullOrEmpty(pkg))
                    RunAdbAsync($"shell am force-stop {pkg}", appsOutput, $"正在停止 {pkg}...");
            };
            row3.Add(stopBtn);

            var uninstallBtn = ActionButton("卸载应用", ColDanger);
            uninstallBtn.style.marginRight = 0;
            uninstallBtn.clicked += () =>
            {
                string pkg = appsPackageField.value.Trim();
                if (!string.IsNullOrEmpty(pkg))
                    RunAdbAsync($"uninstall {pkg}", appsOutput, $"正在卸载 {pkg}...");
            };
            row3.Add(uninstallBtn);

            panel.Add(row3);

            // Output area (for command results like start/stop/uninstall)
            panel.Add(OutputArea(out appsOutput));

            return panel;
        }

        private void PopulateAppList(string output)
        {
            appsPackageNames.Clear();

            if (string.IsNullOrEmpty(output))
            {
                RenderAppList("未获取到应用列表");
                return;
            }

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                string packageName = rawLine.Trim();
                // Strip "package:" prefix from pm list output
                if (packageName.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
                    packageName = packageName.Substring(8);
                if (string.IsNullOrEmpty(packageName)) continue;

                appsPackageNames.Add(packageName);
            }

            RenderAppList("设备上没有找到第三方应用");
        }

        private void RenderAppList(string emptyMessage = null)
        {
            appsListContainer.Clear();
            _selectedAppRow = null;

            string keyword = appsSearchField?.value?.Trim() ?? "";
            var visiblePackages = string.IsNullOrEmpty(keyword)
                ? appsPackageNames
                : appsPackageNames
                    .Where(packageName => packageName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            foreach (var packageName in visiblePackages)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 30;
                row.style.paddingLeft = 12;
                row.style.paddingRight = 12;
                row.style.backgroundColor = Color.clear;

                var label = new Label(packageName);
                label.style.color = ColText;
                label.style.fontSize = 12;
                label.style.flexGrow = 1;
                row.Add(label);

                // Capture for closure
                string pkg = packageName;
                VisualElement capturedRow = row;

                row.RegisterCallback<ClickEvent>(e =>
                {
                    // Deselect previous
                    if (_selectedAppRow != null)
                        _selectedAppRow.style.backgroundColor = Color.clear;
                    // Select this row
                    capturedRow.style.backgroundColor = ColNavActive;
                    _selectedAppRow = capturedRow;
                    appsPackageField.value = pkg;
                });

                row.RegisterCallback<MouseEnterEvent>(e =>
                {
                    if (capturedRow != _selectedAppRow)
                        capturedRow.style.backgroundColor = ColNavHover;
                });
                row.RegisterCallback<MouseLeaveEvent>(e =>
                {
                    if (capturedRow != _selectedAppRow)
                        capturedRow.style.backgroundColor = Color.clear;
                });

                appsListContainer.Add(row);
            }

            if (visiblePackages.Count == 0)
            {
                string message = emptyMessage ?? "没有匹配的应用";
                if (!string.IsNullOrEmpty(keyword) && appsPackageNames.Count > 0)
                    message = $"没有匹配「{keyword}」的应用";

                var emptyLabel = new Label(message);
                emptyLabel.style.color = ColTextDim;
                emptyLabel.style.fontSize = 12;
                emptyLabel.style.paddingLeft = 12;
                emptyLabel.style.paddingTop = 8;
                appsListContainer.Add(emptyLabel);
            }
        }

        #endregion

        #region Screencast Panel

        private VisualElement BuildScreencastPanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;

            panel.Add(SectionTitle("投屏查看"));
            panel.Add(SectionDesc("通过网络查看设备投屏画面"));

            // Controls row
            var row = Row();

            screencastIpField = StyledTextField("设备IP", "192.168.1.100");
            screencastIpField.style.flexGrow = 1;
            screencastIpField.style.flexShrink = 1;
            screencastIpField.style.overflow = Overflow.Hidden;
            row.Add(screencastIpField);

            var autoIpBtn = ActionButton("自动获取", ColAccent);
            autoIpBtn.clicked += OnAutoDetectIp;
            row.Add(autoIpBtn);

            screencastConnectBtn = ActionButton("开始投屏", ColSuccess);
            screencastConnectBtn.clicked += OnStartScreencast;
            row.Add(screencastConnectBtn);

            screencastDisconnectBtn = ActionButton("断开", ColDanger);
            screencastDisconnectBtn.style.marginRight = 0;
            screencastDisconnectBtn.SetEnabled(false);
            screencastDisconnectBtn.clicked += OnStopScreencast;
            row.Add(screencastDisconnectBtn);

            panel.Add(row);

            // Status row
            var statusRow = Row();
            statusRow.style.marginTop = 6;

            screencastStatusLabel = new Label("状态: 未连接");
            screencastStatusLabel.style.color = ColTextDim;
            screencastStatusLabel.style.fontSize = 12;
            statusRow.Add(screencastStatusLabel);

            statusRow.Add(Spacer());

            screencastScreenshotBtn = ActionButton("截取投屏画面", ColAccent);
            screencastScreenshotBtn.SetEnabled(false);
            screencastScreenshotBtn.clicked += OnScreencastScreenshot;
            statusRow.Add(screencastScreenshotBtn);

            screencastOpenBrowserBtn = ActionButton("在浏览器中打开", ColNeutral);
            screencastOpenBrowserBtn.style.display = DisplayStyle.None;
            screencastOpenBrowserBtn.clicked += () =>
            {
                if (!string.IsNullOrEmpty(screencastCurrentUrl))
                    Application.OpenURL(screencastCurrentUrl);
            };
            statusRow.Add(screencastOpenBrowserBtn);

            panel.Add(statusRow);

            // WebView2 placeholder area
            webviewPlaceholder = new VisualElement();
            webviewPlaceholder.style.flexGrow = 1;
            webviewPlaceholder.style.marginTop = 10;
            webviewPlaceholder.style.backgroundColor = ColBgOutput;
            SetBorderRadius(webviewPlaceholder, 6);
            webviewPlaceholder.style.borderTopWidth = 1;
            webviewPlaceholder.style.borderBottomWidth = 1;
            webviewPlaceholder.style.borderLeftWidth = 1;
            webviewPlaceholder.style.borderRightWidth = 1;
            webviewPlaceholder.style.borderTopColor = ColBorder;
            webviewPlaceholder.style.borderBottomColor = ColBorder;
            webviewPlaceholder.style.borderLeftColor = ColBorder;
            webviewPlaceholder.style.borderRightColor = ColBorder;
            webviewPlaceholder.style.justifyContent = Justify.Center;
            webviewPlaceholder.style.alignItems = Align.Center;

            var placeholderLabel = new Label("点击「开始投屏」查看设备画面");
            placeholderLabel.style.color = ColTextDim;
            placeholderLabel.style.fontSize = 14;
            webviewPlaceholder.Add(placeholderLabel);

            webviewPlaceholder.RegisterCallback<GeometryChangedEvent>(OnWebViewPlaceholderGeometryChanged);

            panel.Add(webviewPlaceholder);

            return panel;
        }

        // ── Auto-detect device IP ──

        private void OnAutoDetectIp()
        {
            screencastStatusLabel.text = "状态: 正在获取设备IP...";
            screencastStatusLabel.style.color = ColWarning;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var result = adb.Execute("shell ip route");
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        if (result.IsSuccess && !string.IsNullOrEmpty(result.Output))
                        {
                            string ip = ParseWlanIp(result.Output);
                            if (!string.IsNullOrEmpty(ip))
                            {
                                screencastIpField.value = ip;
                                screencastStatusLabel.text = $"状态: 已获取IP {ip}";
                                screencastStatusLabel.style.color = ColSuccess;
                            }
                            else
                            {
                                screencastStatusLabel.text = "状态: 未找到WiFi IP，请手动输入";
                                screencastStatusLabel.style.color = ColDanger;
                            }
                        }
                        else
                        {
                            screencastStatusLabel.text = "状态: 获取IP失败，请确认设备已连接USB";
                            screencastStatusLabel.style.color = ColDanger;
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AdbToolsApp] IP获取异常: {ex.Message}");
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        screencastStatusLabel.text = $"状态: 获取IP异常";
                        screencastStatusLabel.style.color = ColDanger;
                    });
                }
            });
        }

        private static string ParseWlanIp(string ipRouteOutput)
        {
            // Parse "ip route" output for wlan0 interface IP
            // Example line: "192.168.5.0/24 dev wlan0 proto kernel scope link src 192.168.5.9"
            var lines = ipRouteOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (!line.Contains("wlan0")) continue;
                int srcIdx = line.IndexOf("src ");
                if (srcIdx < 0 || srcIdx + 4 >= line.Length) continue;
                string after = line.Substring(srcIdx + 4).Trim();
                if (string.IsNullOrEmpty(after)) continue;
                int spaceIdx = after.IndexOf(' ');
                string ip = spaceIdx >= 0 ? after.Substring(0, spaceIdx) : after;
                if (System.Net.IPAddress.TryParse(ip, out _))
                    return ip;
            }
            return null;
        }

        // ── Start / Stop Screencast ──

#if UNITY_STANDALONE_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
#endif

        private void OnStartScreencast()
        {
            string ip = screencastIpField.value.Trim();
            if (string.IsNullOrEmpty(ip))
            {
                screencastStatusLabel.text = "状态: 请输入设备IP地址";
                screencastStatusLabel.style.color = ColDanger;
                return;
            }

            screencastCurrentUrl = $"http://{ip}:3342/cast";

#if UNITY_STANDALONE_WIN
            // WebView2 only works in standalone builds — in Editor, coordinate systems don't match
            if (Application.isEditor)
            {
                OpenScreencastInBrowser(ip);
                return;
            }

            if (webView2Host != null && webView2Host.IsReady)
            {
                webView2Host.Navigate(screencastCurrentUrl);
                webView2Host.SetVisible(true);
                screencastStatusLabel.text = $"状态: 已连接 {ip}";
                screencastStatusLabel.style.color = ColSuccess;
                screencastConnectBtn.SetEnabled(false);
                screencastDisconnectBtn.SetEnabled(true);
                screencastScreenshotBtn.SetEnabled(true);
                return;
            }

            screencastStatusLabel.text = "状态: 正在初始化WebView2...";
            screencastStatusLabel.style.color = ColWarning;

            try
            {
                IntPtr hwnd = GetActiveWindow();
                if (hwnd == IntPtr.Zero)
                {
                    ShowScreencastFallback("无法获取窗口句柄");
                    return;
                }

                string userDataFolder = Path.Combine(Application.persistentDataPath, "WebView2Data");

                webView2Host = new WebView2Host();

                // Capture ip in a local variable for the closure
                string capturedIp = ip;
                webView2Host.OnReady += () =>
                {
                    Debug.Log($"[Screencast] WebView2 ready, navigating to {screencastCurrentUrl}");
                    webView2Host.Navigate(screencastCurrentUrl);
                    UpdateWebViewBounds();
                    webView2Host.SetVisible(true);

                    webviewPlaceholder.Clear();

                    screencastStatusLabel.text = $"状态: 已连接 {capturedIp}";
                    screencastStatusLabel.style.color = ColSuccess;
                    screencastConnectBtn.SetEnabled(false);
                    screencastDisconnectBtn.SetEnabled(true);
                    screencastScreenshotBtn.SetEnabled(true);
                };
                webView2Host.OnError += (error) =>
                {
                    ShowScreencastFallback(error);
                };

                webView2Host.Initialize(hwnd, userDataFolder);
            }
            catch (DllNotFoundException)
            {
                ShowScreencastFallback("WebView2Loader.dll 未找到，请确认已正确部署");
            }
            catch (Exception ex)
            {
                ShowScreencastFallback(ex.Message);
            }
#else
            OpenScreencastInBrowser(ip);
#endif
        }

        private void OnStopScreencast()
        {
#if UNITY_STANDALONE_WIN
            if (webView2Host != null && webView2Host.IsReady)
            {
                webView2Host.Navigate("about:blank");
                webView2Host.SetVisible(false);
            }
#endif
            screencastStatusLabel.text = "状态: 已断开";
            screencastStatusLabel.style.color = ColTextDim;
            screencastConnectBtn.SetEnabled(true);
            screencastDisconnectBtn.SetEnabled(false);
            screencastScreenshotBtn.SetEnabled(false);
            screencastCurrentUrl = null;
        }

        // ── Browser Fallback (Editor / non-Windows) ──

        private void OpenScreencastInBrowser(string ip)
        {
            Application.OpenURL(screencastCurrentUrl);
            screencastStatusLabel.text = $"状态: 已在浏览器中打开 {ip}";
            screencastStatusLabel.style.color = ColSuccess;
            screencastConnectBtn.SetEnabled(false);
            screencastDisconnectBtn.SetEnabled(true);
        }

        private void ShowScreencastFallback(string error)
        {
            Debug.LogWarning($"[Screencast] WebView2 初始化失败: {error}");
            screencastStatusLabel.text = $"状态: WebView2不可用 - {error}";
            screencastStatusLabel.style.color = ColDanger;

            screencastOpenBrowserBtn.style.display = DisplayStyle.Flex;

            webviewPlaceholder.Clear();
            var errorLabel = new Label(
                $"无法加载内嵌浏览器: {error}\n\n请使用「在浏览器中打开」按钮查看投屏画面，\n或确认系统已安装 Microsoft Edge 浏览器。");
            errorLabel.style.color = ColTextDim;
            errorLabel.style.fontSize = 13;
            errorLabel.style.whiteSpace = WhiteSpace.Normal;
            errorLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            webviewPlaceholder.Add(errorLabel);

            screencastConnectBtn.SetEnabled(true);
            screencastDisconnectBtn.SetEnabled(false);
            screencastScreenshotBtn.SetEnabled(false);
        }

        // ── Screencast Screenshot ──

        private void OnScreencastScreenshot()
        {
#if UNITY_STANDALONE_WIN
            if (webView2Host == null || !webView2Host.IsReady)
            {
                screencastStatusLabel.text = "状态: WebView2 未就绪，无法截图";
                screencastStatusLabel.style.color = ColDanger;
                return;
            }

            string screenshotsDir = Path.Combine(
                Path.GetDirectoryName(logSavePath), "Screenshots");
            if (!Directory.Exists(screenshotsDir))
                Directory.CreateDirectory(screenshotsDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filePath = Path.Combine(screenshotsDir, $"screencast_{timestamp}.png");

            screencastScreenshotBtn.SetEnabled(false);
            screencastStatusLabel.text = "状态: 正在截取投屏画面...";
            screencastStatusLabel.style.color = ColWarning;

            webView2Host.CaptureToFile(filePath, (success, message) =>
            {
                // WebView2 callbacks fire on the main thread (COM message pump)
                MainThreadDispatcher.Enqueue(() =>
                {
                    screencastScreenshotBtn.SetEnabled(true);

                    if (success)
                    {
                        screencastStatusLabel.text = $"状态: 截图已保存";
                        screencastStatusLabel.style.color = ColSuccess;
                        FileDialogHelper.OpenInExplorer(filePath);
                    }
                    else
                    {
                        screencastStatusLabel.text = $"状态: 截图失败 - {message}";
                        screencastStatusLabel.style.color = ColDanger;
                    }
                });
            });
#else
            screencastStatusLabel.text = "状态: 仅独立构建版本支持投屏截图";
            screencastStatusLabel.style.color = ColWarning;
#endif
        }

        // ── WebView2 Bounds Tracking ──

        private void OnWebViewPlaceholderGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateWebViewBounds();
        }

        private void UpdateWebViewBounds()
        {
#if UNITY_STANDALONE_WIN
            if (webView2Host == null || !webView2Host.IsReady) return;
            if (webviewPlaceholder == null) return;

            var rect = webviewPlaceholder.worldBound;

            // PanelSettings: ScaleWithScreenSize, ref 1280x800, match 0.5
            // worldBound is in reference coordinates; convert to actual screen pixels
            float logW = Mathf.Log(Screen.width / 1280f, 2);
            float logH = Mathf.Log(Screen.height / 800f, 2);
            float scale = Mathf.Pow(2, Mathf.Lerp(logW, logH, 0.5f));

            var bounds = new RECT
            {
                Left = Mathf.RoundToInt(rect.x * scale),
                Top = Mathf.RoundToInt(rect.y * scale),
                Right = Mathf.RoundToInt((rect.x + rect.width) * scale),
                Bottom = Mathf.RoundToInt((rect.y + rect.height) * scale)
            };

            Debug.Log($"[Screencast] Bounds: worldBound=({rect.x:F0},{rect.y:F0},{rect.width:F0},{rect.height:F0}) " +
                      $"screen={Screen.width}x{Screen.height} scale={scale:F3} " +
                      $"→ RECT=({bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom})");

            webView2Host.SetBounds(bounds);
#endif
        }

        #endregion

        #region Performance Panel

        private VisualElement BuildPerformancePanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;

            panel.Add(SectionTitle("性能监控"));
            panel.Add(SectionDesc("实时监控设备系统性能和应用性能指标"));

            // Controls row
            var row = Row();

            perfPackageField = StyledTextField("应用包名 (可选)", "com.example.app");
            perfPackageField.style.flexGrow = 1;
            perfPackageField.style.flexShrink = 1;
            perfPackageField.style.overflow = Overflow.Hidden;
            row.Add(perfPackageField);

            perfStartBtn = ActionButton("开始监控", ColSuccess);
            perfStartBtn.clicked += OnStartPerfMonitor;
            row.Add(perfStartBtn);

            perfStopBtn = ActionButton("停止", ColDanger);
            perfStopBtn.style.marginRight = 0;
            perfStopBtn.SetEnabled(false);
            perfStopBtn.clicked += OnStopPerfMonitor;
            row.Add(perfStopBtn);

            panel.Add(row);

            // Status label
            perfStatusLabel = new Label("状态: 未启动");
            perfStatusLabel.style.color = ColTextDim;
            perfStatusLabel.style.fontSize = 12;
            perfStatusLabel.style.marginTop = 6;
            panel.Add(perfStatusLabel);

            // Scrollable metrics area
            var metricsScroll = new ScrollView(ScrollViewMode.Vertical);
            metricsScroll.style.flexGrow = 1;
            metricsScroll.style.marginTop = 8;

            // ── System metrics section ──
            var sysSection = BuildPerfSection("系统指标");
            sysSection.Add(BuildPerfMetricRow("CPU 使用率", out perfCpuBarFill, out perfCpuValueLabel));
            sysSection.Add(BuildPerfMetricRow("内存使用", out perfMemBarFill, out perfMemValueLabel));
            sysSection.Add(BuildPerfMetricRow("电池电量", out perfBatteryBarFill, out perfBatteryValueLabel));
            sysSection.Add(BuildPerfTextRow("电池温度", out perfBatteryTempLabel));
            sysSection.Add(BuildPerfTextRow("网络 ↓", out perfNetRxLabel));
            sysSection.Add(BuildPerfTextRow("网络 ↑", out perfNetTxLabel));
            metricsScroll.Add(sysSection);

            // ── Memory detail section (top 10 processes) ──
            perfMemDetailSection = BuildPerfSection("内存详情 (Top 10)");
            var memDetailHeaderRow = Row();
            perfMemDetailStatusLabel = new Label("点击刷新查看进程内存占用排行");
            perfMemDetailStatusLabel.style.fontSize = 11;
            perfMemDetailStatusLabel.style.color = ColTextDim;
            perfMemDetailStatusLabel.style.flexGrow = 1;
            memDetailHeaderRow.Add(perfMemDetailStatusLabel);

            perfMemDetailRefreshBtn = ActionButton("刷新", ColAccent);
            perfMemDetailRefreshBtn.clicked += OnRefreshMemoryDetail;
            memDetailHeaderRow.Add(perfMemDetailRefreshBtn);
            perfMemDetailSection.Add(memDetailHeaderRow);

            perfMemDetailContainer = new VisualElement();
            perfMemDetailSection.Add(perfMemDetailContainer);
            metricsScroll.Add(perfMemDetailSection);

            // ── App metrics section (hidden by default) ──
            perfAppSection = BuildPerfSection("应用指标");
            perfAppSection.style.display = DisplayStyle.None;
            perfAppSectionTitle = perfAppSection.Q<Label>();

            perfAppSection.Add(BuildPerfMetricRow("应用 CPU", out perfAppCpuBarFill, out perfAppCpuValueLabel));
            perfAppSection.Add(BuildPerfMetricRow("应用内存", out perfAppMemBarFill, out perfAppMemValueLabel));
            perfAppSection.Add(BuildPerfMetricRow("帧率/卡顿", out perfAppFpsBarFill, out perfAppFpsValueLabel));
            perfAppSection.Add(BuildPerfTextRow("应用网络 ↓", out perfAppNetRxLabel));
            perfAppSection.Add(BuildPerfTextRow("应用网络 ↑", out perfAppNetTxLabel));
            metricsScroll.Add(perfAppSection);

            panel.Add(metricsScroll);

            return panel;
        }

        // ── UI Helpers ──

        private VisualElement BuildPerfSection(string title)
        {
            var section = new VisualElement();
            section.style.marginTop = 10;
            section.style.paddingTop = 10;
            section.style.paddingBottom = 10;
            section.style.paddingLeft = 14;
            section.style.paddingRight = 14;
            section.style.backgroundColor = ColBgOutput;
            SetBorderRadius(section, 6);
            section.style.borderTopWidth = 1;
            section.style.borderBottomWidth = 1;
            section.style.borderLeftWidth = 1;
            section.style.borderRightWidth = 1;
            section.style.borderTopColor = ColBorder;
            section.style.borderBottomColor = ColBorder;
            section.style.borderLeftColor = ColBorder;
            section.style.borderRightColor = ColBorder;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 13;
            titleLabel.style.color = ColTextDim;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = 4;
            section.Add(titleLabel);

            return section;
        }

        private VisualElement BuildPerfMetricRow(string labelText, out VisualElement barFill, out Label valueLabel)
        {
            var row = Row();
            row.style.marginTop = 6;
            row.style.height = 28;

            var label = new Label(labelText);
            label.style.width = 100;
            label.style.fontSize = 12;
            label.style.color = ColText;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(label);

            var barBg = new VisualElement();
            barBg.style.flexGrow = 1;
            barBg.style.height = 18;
            barBg.style.backgroundColor = new Color(0.15f, 0.15f, 0.22f);
            SetBorderRadius(barBg, 4);
            barBg.style.overflow = Overflow.Hidden;

            barFill = new VisualElement();
            barFill.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            barFill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
            barFill.style.backgroundColor = ColSuccess;
            SetBorderRadius(barFill, 4);
            barBg.Add(barFill);
            row.Add(barBg);

            valueLabel = new Label("--");
            valueLabel.style.width = 110;
            valueLabel.style.fontSize = 12;
            valueLabel.style.color = ColText;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            valueLabel.style.marginLeft = 8;
            row.Add(valueLabel);

            return row;
        }

        private VisualElement BuildPerfTextRow(string labelText, out Label valueLabel)
        {
            var row = Row();
            row.style.marginTop = 4;
            row.style.height = 22;

            var label = new Label(labelText);
            label.style.width = 100;
            label.style.fontSize = 12;
            label.style.color = ColText;
            row.Add(label);

            valueLabel = new Label("--");
            valueLabel.style.fontSize = 12;
            valueLabel.style.color = ColText;
            row.Add(valueLabel);

            return row;
        }

        private static Color GetThresholdColor(float value, float yellowThreshold, float redThreshold, bool inverted = false)
        {
            if (inverted)
            {
                if (value <= redThreshold) return ColDanger;
                if (value <= yellowThreshold) return ColWarning;
                return ColSuccess;
            }
            if (value >= redThreshold) return ColDanger;
            if (value >= yellowThreshold) return ColWarning;
            return ColSuccess;
        }

        private static string FormatBytesPerSec(float bytesPerSec)
        {
            if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
            if (bytesPerSec < 1048576) return $"{bytesPerSec / 1024f:F1} KB/s";
            return $"{bytesPerSec / 1048576f:F2} MB/s";
        }

        private static string FormatKb(long kb)
        {
            if (kb < 1024) return $"{kb} KB";
            if (kb < 1048576) return $"{kb / 1024f:F1} MB";
            return $"{kb / 1048576f:F2} GB";
        }

        // ── Start / Stop ──

        private void OnStartPerfMonitor()
        {
            if (!adb.IsAdbAvailable)
            {
                perfStatusLabel.text = "状态: ADB 不可用";
                perfStatusLabel.style.color = ColDanger;
                return;
            }

            perfMonitoringActive = true;
            perfHasPreviousSample = false;
            perfDirty = false;
            perfCurrentPackage = perfPackageField?.value?.Trim() ?? "";

            perfStartBtn.SetEnabled(false);
            perfStopBtn.SetEnabled(true);
            perfStatusLabel.text = "状态: 监控中...";
            perfStatusLabel.style.color = ColSuccess;

            perfMonitorThread = new Thread(PerfMonitorLoop)
            {
                IsBackground = true,
                Name = "PerfMonitorThread"
            };
            perfMonitorThread.Start();
        }

        private void OnStopPerfMonitor()
        {
            perfMonitoringActive = false;

            perfStartBtn.SetEnabled(true);
            perfStopBtn.SetEnabled(false);
            perfStatusLabel.text = "状态: 已停止";
            perfStatusLabel.style.color = ColTextDim;

            if (perfMonitorThread != null)
            {
                if (!perfMonitorThread.Join(3000))
                    Debug.LogWarning("[AdbToolsApp] PerfMonitor thread did not exit within 3s");
                perfMonitorThread = null;
            }
        }

        // ── Memory Detail (Top 10) ──

        private void OnRefreshMemoryDetail()
        {
            if (!adb.IsAdbAvailable)
            {
                perfMemDetailStatusLabel.text = "ADB 不可用";
                perfMemDetailStatusLabel.style.color = ColDanger;
                return;
            }

            if (perfMemDetailLoading) return;
            perfMemDetailLoading = true;
            perfMemDetailRefreshBtn.SetEnabled(false);
            perfMemDetailStatusLabel.text = "正在获取内存信息...";
            perfMemDetailStatusLabel.style.color = ColWarning;

            new Thread(() =>
            {
                var result = adb.Execute("shell dumpsys meminfo", 30000);
                MainThreadDispatcher.Enqueue(() => OnMemoryDetailResult(result));
            }) { IsBackground = true }.Start();
        }

        private void OnMemoryDetailResult(AdbResult result)
        {
            perfMemDetailLoading = false;
            perfMemDetailRefreshBtn.SetEnabled(true);

            if (!result.IsSuccess || !result.HasOutput)
            {
                perfMemDetailStatusLabel.text = "获取失败: " + (result.Error ?? "无输出");
                perfMemDetailStatusLabel.style.color = ColDanger;
                return;
            }

            // Debug: log first 1000 chars of raw output to help diagnose parsing issues
            Debug.Log($"[PerfMonitor] dumpsys meminfo output (first 1000 chars):\n{result.Output.Substring(0, Math.Min(result.Output.Length, 1000))}");

            var entries = ParseMemInfoTopProcesses(result.Output, 10);
            if (entries.Count == 0)
            {
                perfMemDetailStatusLabel.text = "未解析到进程内存数据";
                perfMemDetailStatusLabel.style.color = ColWarning;
                return;
            }

            perfMemDetailStatusLabel.text = $"已刷新 ({DateTime.Now:HH:mm:ss})";
            perfMemDetailStatusLabel.style.color = ColSuccess;

            perfMemDetailContainer.Clear();

            long maxKb = entries[0].pssKb;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                float pct = maxKb > 0 ? Mathf.Clamp01((float)entry.pssKb / maxKb) * 100f : 0;
                var row = BuildMemDetailRow(i + 1, entry.processName, entry.pssKb, pct);
                perfMemDetailContainer.Add(row);
            }
        }

        private struct MemProcessEntry
        {
            public long pssKb;
            public string processName;
        }

        // Matches: "  341,456 kB: process (pid 123)" or "341456K: process (pid 123)"
        // Groups: 1=number (may contain commas), 2=unit (kB/K/KB), 3=rest (process + pid info)
        private static readonly Regex MemLineRegex = new Regex(
            @"^\s*([\d,]+)\s*(?:kB|KB|K)\s*:\s*(.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static List<MemProcessEntry> ParseMemInfoTopProcesses(string output, int topN)
        {
            var entries = new List<MemProcessEntry>();
            var lines = output.Split('\n');
            bool inSection = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();

                // Detect section start: "Total PSS by process:" (may have leading spaces)
                if (line.Contains("Total PSS by process"))
                {
                    inSection = true;
                    continue;
                }

                if (!inSection) continue;

                // End of section: blank line or next "Total " section header
                if (string.IsNullOrEmpty(line))
                    break;
                if (line.StartsWith("Total ") && !line.Contains("Total PSS by process"))
                    break;

                var m = MemLineRegex.Match(line);
                if (!m.Success) continue;

                string numStr = m.Groups[1].Value.Replace(",", "");
                if (!long.TryParse(numStr, out long kb)) continue;

                string name = m.Groups[2].Value.Trim();

                // Remove trailing "(pid ...)" or "(pid ... / activities)"
                int pidParen = name.IndexOf(" (pid ");
                if (pidParen >= 0)
                    name = name.Substring(0, pidParen).Trim();

                if (string.IsNullOrEmpty(name)) continue;

                entries.Add(new MemProcessEntry { pssKb = kb, processName = name });
                if (entries.Count >= topN) break;
            }

            // Fallback: if no "Total PSS by process" section found, try matching all kB lines
            if (entries.Count == 0)
            {
                Debug.Log("[PerfMonitor] 'Total PSS by process' section not found, trying fallback parsing");
                foreach (var rawLine in lines)
                {
                    var m = MemLineRegex.Match(rawLine.Trim());
                    if (!m.Success) continue;

                    string numStr = m.Groups[1].Value.Replace(",", "");
                    if (!long.TryParse(numStr, out long kb)) continue;
                    if (kb <= 0) continue;

                    string name = m.Groups[2].Value.Trim();
                    int pidParen = name.IndexOf(" (pid ");
                    if (pidParen >= 0)
                        name = name.Substring(0, pidParen).Trim();
                    if (string.IsNullOrEmpty(name)) continue;

                    // Avoid duplicate process names, keep the first (largest) entry
                    bool dup = false;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].processName == name) { dup = true; break; }
                    }
                    if (dup) continue;

                    entries.Add(new MemProcessEntry { pssKb = kb, processName = name });
                    if (entries.Count >= topN) break;
                }

                // Sort descending by PSS
                entries.Sort((a, b) => b.pssKb.CompareTo(a.pssKb));
            }

            return entries;
        }

        private VisualElement BuildMemDetailRow(int rank, string processName, long pssKb, float barPercent)
        {
            var row = Row();
            row.style.marginTop = 4;
            row.style.height = 24;

            // Rank label
            var rankLabel = new Label($"{rank}.");
            rankLabel.style.width = 24;
            rankLabel.style.fontSize = 11;
            rankLabel.style.color = ColTextDim;
            rankLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            rankLabel.style.marginRight = 4;
            row.Add(rankLabel);

            // Process name
            var nameLabel = new Label(processName);
            nameLabel.style.width = 200;
            nameLabel.style.fontSize = 11;
            nameLabel.style.color = ColText;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.overflow = Overflow.Hidden;
            row.Add(nameLabel);

            // Progress bar
            var barBg = new VisualElement();
            barBg.style.flexGrow = 1;
            barBg.style.height = 14;
            barBg.style.backgroundColor = new Color(0.15f, 0.15f, 0.22f);
            SetBorderRadius(barBg, 3);
            barBg.style.overflow = Overflow.Hidden;

            var barFill = new VisualElement();
            barFill.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            barFill.style.width = new StyleLength(new Length(barPercent, LengthUnit.Percent));
            barFill.style.backgroundColor = GetThresholdColor(pssKb / 1024f, 200f, 500f);
            SetBorderRadius(barFill, 3);
            barBg.Add(barFill);
            row.Add(barBg);

            // Memory value
            var valLabel = new Label(FormatKb(pssKb));
            valLabel.style.width = 80;
            valLabel.style.fontSize = 11;
            valLabel.style.color = ColText;
            valLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            valLabel.style.marginLeft = 6;
            row.Add(valLabel);

            return row;
        }

        // ── Polling Loop ──

        private void PerfMonitorLoop()
        {
            while (perfMonitoringActive)
            {
                try
                {
                    var snapshot = new PerfSnapshot { isValid = true };

                    // System-wide batch: single ADB call
                    var sysResult = adb.Execute(
                        "shell \"cat /proc/stat; echo '===MEMINFO==='; cat /proc/meminfo; " +
                        "echo '===NETDEV==='; cat /proc/net/dev; echo '===BATTERY==='; dumpsys battery\"",
                        10000);

                    if (!sysResult.IsSuccess)
                    {
                        snapshot.isValid = false;
                        snapshot.errorMessage = sysResult.Error;
                        PublishSnapshot(snapshot);
                        SleepOrExit(PerfPollIntervalMs);
                        continue;
                    }

                    ParseSystemBatch(sysResult.Output, ref snapshot);

                    if (perfHasPreviousSample)
                        ComputeDeltas(ref snapshot, ref perfPreviousSnapshot);

                    // Per-app metrics (optional)
                    string pkg = perfCurrentPackage;
                    if (!string.IsNullOrEmpty(pkg))
                        CollectAppMetrics(pkg, ref snapshot);

                    perfPreviousSnapshot = snapshot;
                    perfHasPreviousSample = true;

                    PublishSnapshot(snapshot);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PerfMonitor] Error: {ex.Message}");
                    PublishSnapshot(new PerfSnapshot { isValid = false, errorMessage = ex.Message });
                }

                SleepOrExit(PerfPollIntervalMs);
            }
        }

        private void PublishSnapshot(PerfSnapshot snapshot)
        {
            lock (perfLock)
            {
                perfPendingSnapshot = snapshot;
                perfDirty = true;
            }
        }

        private void SleepOrExit(int ms)
        {
            int slept = 0;
            while (slept < ms && perfMonitoringActive)
            {
                Thread.Sleep(100);
                slept += 100;
            }
        }

        // ── System Batch Parsing ──

        private static void ParseSystemBatch(string output, ref PerfSnapshot snapshot)
        {
            // Split by sentinel markers
            string cpuSection = output;
            string memSection = "";
            string netSection = "";
            string batterySection = "";

            int memIdx = output.IndexOf("===MEMINFO===", StringComparison.Ordinal);
            if (memIdx >= 0)
            {
                cpuSection = output.Substring(0, memIdx);
                string rest = output.Substring(memIdx + 13);

                int netIdx = rest.IndexOf("===NETDEV===", StringComparison.Ordinal);
                if (netIdx >= 0)
                {
                    memSection = rest.Substring(0, netIdx);
                    rest = rest.Substring(netIdx + 12);

                    int batIdx = rest.IndexOf("===BATTERY===", StringComparison.Ordinal);
                    if (batIdx >= 0)
                    {
                        netSection = rest.Substring(0, batIdx);
                        batterySection = rest.Substring(batIdx + 13);
                    }
                    else
                    {
                        netSection = rest;
                    }
                }
                else
                {
                    memSection = rest;
                }
            }

            ParseProcStat(cpuSection, ref snapshot);
            ParseProcMeminfo(memSection, ref snapshot);
            ParseProcNetDev(netSection, ref snapshot);
            ParseBatteryForPerf(batterySection, ref snapshot);
        }

        private static void ParseProcStat(string section, ref PerfSnapshot snapshot)
        {
            var lines = section.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (!line.StartsWith("cpu ")) continue;
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 8) break;
                long.TryParse(parts[1], out snapshot.cpuUser);
                long.TryParse(parts[2], out snapshot.cpuNice);
                long.TryParse(parts[3], out snapshot.cpuSystem);
                long.TryParse(parts[4], out snapshot.cpuIdle);
                long.TryParse(parts[5], out snapshot.cpuIoWait);
                long.TryParse(parts[6], out snapshot.cpuIrq);
                long.TryParse(parts[7], out snapshot.cpuSoftIrq);
                break;
            }
        }

        private static void ParseProcMeminfo(string section, ref PerfSnapshot snapshot)
        {
            var lines = section.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractKbValue(trimmed, out snapshot.memTotalKb);
                }
                else if (trimmed.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractKbValue(trimmed, out snapshot.memAvailableKb);
                }
            }

            if (snapshot.memTotalKb > 0)
            {
                long used = snapshot.memTotalKb - snapshot.memAvailableKb;
                snapshot.memUsagePercent = used * 100f / snapshot.memTotalKb;
            }
        }

        private static void ExtractKbValue(string line, out long value)
        {
            value = 0;
            // Format: "MemTotal:       7654321 kB"
            int colonIdx = line.IndexOf(':');
            if (colonIdx < 0) return;
            string after = line.Substring(colonIdx + 1).Trim();
            int spaceIdx = after.IndexOf(' ');
            string numStr = spaceIdx >= 0 ? after.Substring(0, spaceIdx) : after;
            long.TryParse(numStr, out value);
        }

        private static void ParseProcNetDev(string section, ref PerfSnapshot snapshot)
        {
            var lines = section.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            long totalRx = 0, totalTx = 0;
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                // Match wlan0, rmnet interfaces
                if (!trimmed.StartsWith("wlan", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("rmnet", StringComparison.OrdinalIgnoreCase))
                    continue;

                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0) continue;
                string data = trimmed.Substring(colonIdx + 1).Trim();
                var parts = data.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                // Column 0 = RX bytes, Column 8 = TX bytes
                if (parts.Length >= 9)
                {
                    if (long.TryParse(parts[0], out long rx)) totalRx += rx;
                    if (long.TryParse(parts[8], out long tx)) totalTx += tx;
                }
            }
            snapshot.netRxBytes = totalRx;
            snapshot.netTxBytes = totalTx;
        }

        private static void ParseBatteryForPerf(string section, ref PerfSnapshot snapshot)
        {
            var lines = section.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("level:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(trimmed.Substring(6).Trim(), out int lv))
                        snapshot.batteryLevel = lv;
                }
                else if (trimmed.StartsWith("temperature:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(trimmed.Substring(12).Trim(), out int temp))
                        snapshot.batteryTempCelsius = temp / 10f;
                }
                else if (trimmed.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = trimmed.Substring(7).Trim();
                    switch (val)
                    {
                        case "2": snapshot.batteryStatus = "充电中"; break;
                        case "3": snapshot.batteryStatus = "放电中"; break;
                        case "4": snapshot.batteryStatus = "未充电"; break;
                        case "5": snapshot.batteryStatus = "已充满"; break;
                        default: snapshot.batteryStatus = val; break;
                    }
                }
            }
        }

        // ── Delta Calculation ──

        private void ComputeDeltas(ref PerfSnapshot current, ref PerfSnapshot previous)
        {
            // CPU%
            long curTotal = current.cpuUser + current.cpuNice + current.cpuSystem + current.cpuIdle
                          + current.cpuIoWait + current.cpuIrq + current.cpuSoftIrq;
            long prevTotal = previous.cpuUser + previous.cpuNice + previous.cpuSystem + previous.cpuIdle
                           + previous.cpuIoWait + previous.cpuIrq + previous.cpuSoftIrq;
            long totalDelta = curTotal - prevTotal;
            long idleDelta = current.cpuIdle - previous.cpuIdle;

            if (totalDelta > 0)
                current.cpuUsagePercent = (totalDelta - idleDelta) * 100f / totalDelta;

            // Network bytes/sec
            float interval = PerfPollIntervalMs / 1000f;
            if (interval > 0)
            {
                long rxDelta = current.netRxBytes - previous.netRxBytes;
                long txDelta = current.netTxBytes - previous.netTxBytes;
                if (rxDelta >= 0) current.netRxBytesPerSec = rxDelta / interval;
                if (txDelta >= 0) current.netTxBytesPerSec = txDelta / interval;
            }

            // App CPU% (if both have app data)
            if (current.hasAppMetrics && previous.hasAppMetrics && totalDelta > 0)
            {
                long appDelta = (current.appUtime + current.appStime) - (previous.appUtime + previous.appStime);
                if (appDelta >= 0)
                    current.appCpuPercent = appDelta * 100f / totalDelta;
            }

            // App network
            if (current.hasAppMetrics && previous.hasAppMetrics && interval > 0)
            {
                long appRxDelta = current.appNetRxBytes - previous.appNetRxBytes;
                long appTxDelta = current.appNetTxBytes - previous.appNetTxBytes;
                if (appRxDelta >= 0) current.appNetRxPerSec = appRxDelta / interval;
                if (appTxDelta >= 0) current.appNetTxPerSec = appTxDelta / interval;
            }
        }

        // ── Per-App Metrics ──

        private void CollectAppMetrics(string packageName, ref PerfSnapshot snapshot)
        {
            // Get PID
            var pidResult = adb.Execute($"shell pidof {packageName}", 5000);
            if (!pidResult.IsSuccess || string.IsNullOrWhiteSpace(pidResult.Output))
                return;

            string pidStr = pidResult.Output.Trim().Split(new[] { ' ', '\n', '\r' })[0];
            if (!int.TryParse(pidStr, out int pid) || pid <= 0)
                return;

            snapshot.hasAppMetrics = true;

            // Batch app metrics
            var appResult = adb.Execute(
                $"shell \"cat /proc/{pid}/stat; echo '===APPMEM==='; dumpsys meminfo {packageName}; " +
                $"echo '===APPGFX==='; dumpsys gfxinfo {packageName} reset\"",
                15000);

            if (!appResult.IsSuccess) return;

            string appOutput = appResult.Output;
            string cpuPart = appOutput;
            string memPart = "";
            string gfxPart = "";

            int memIdx = appOutput.IndexOf("===APPMEM===", StringComparison.Ordinal);
            if (memIdx >= 0)
            {
                cpuPart = appOutput.Substring(0, memIdx);
                string rest = appOutput.Substring(memIdx + 12);

                int gfxIdx = rest.IndexOf("===APPGFX===", StringComparison.Ordinal);
                if (gfxIdx >= 0)
                {
                    memPart = rest.Substring(0, gfxIdx);
                    gfxPart = rest.Substring(gfxIdx + 12);
                }
                else
                {
                    memPart = rest;
                }
            }

            ParseAppCpuStat(cpuPart.Trim(), ref snapshot);
            ParseAppMeminfo(memPart, ref snapshot);
            ParseGfxInfo(gfxPart, ref snapshot);

            // App network (UID-based)
            CollectAppNetwork(packageName, ref snapshot);
        }

        private static void ParseAppCpuStat(string line, ref PerfSnapshot snapshot)
        {
            // /proc/<pid>/stat: "PID (comm) state ..."
            // Fields 14(utime) and 15(stime) are 1-indexed from start
            // comm can contain spaces/parens, so find closing ')' first
            if (string.IsNullOrEmpty(line)) return;

            // Take first line only
            int nlIdx = line.IndexOfAny(new[] { '\r', '\n' });
            if (nlIdx >= 0) line = line.Substring(0, nlIdx);

            int closeParenIdx = line.LastIndexOf(')');
            if (closeParenIdx < 0 || closeParenIdx + 2 >= line.Length) return;

            // After ") " there are fields starting from index 3 (state)
            string afterComm = line.Substring(closeParenIdx + 2);
            var parts = afterComm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            // parts[0]=state, parts[1]=ppid, ..., parts[11]=utime, parts[12]=stime
            if (parts.Length > 12)
            {
                long.TryParse(parts[11], out snapshot.appUtime);
                long.TryParse(parts[12], out snapshot.appStime);
            }
        }

        private static void ParseAppMeminfo(string output, ref PerfSnapshot snapshot)
        {
            // Look for "TOTAL" line with PSS value
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                // Format: "TOTAL    12345    ..." where second field is PSS in kB
                if (parts.Length >= 2 && long.TryParse(parts[1], out long pssKb))
                {
                    snapshot.appPssKb = pssKb;
                    return;
                }
            }
        }

        private static void ParseGfxInfo(string output, ref PerfSnapshot snapshot)
        {
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Total frames rendered:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    int.TryParse(val, out snapshot.appTotalFrames);
                }
                else if (trimmed.StartsWith("Janky frames:", StringComparison.OrdinalIgnoreCase))
                {
                    // Format: "Janky frames: 12 (8.50%)"
                    string val = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    int spaceIdx = val.IndexOf(' ');
                    if (spaceIdx > 0) val = val.Substring(0, spaceIdx);
                    int.TryParse(val, out snapshot.appJankyFrames);
                }
            }
        }

        private void CollectAppNetwork(string packageName, ref PerfSnapshot snapshot)
        {
            // Get UID from dumpsys package
            var pkgResult = adb.Execute($"shell dumpsys package {packageName} | grep userId=", 5000);
            if (!pkgResult.IsSuccess || string.IsNullOrWhiteSpace(pkgResult.Output)) return;

            // Extract userId=XXXXX
            string output = pkgResult.Output;
            int uidIdx = output.IndexOf("userId=", StringComparison.Ordinal);
            if (uidIdx < 0) return;
            string afterUid = output.Substring(uidIdx + 7);
            int endIdx = afterUid.IndexOfAny(new[] { ' ', '\n', '\r', '\t' });
            string uidStr = endIdx >= 0 ? afterUid.Substring(0, endIdx) : afterUid.Trim();
            if (!int.TryParse(uidStr, out int uid) || uid <= 0) return;

            // Read network stats
            var netResult = adb.Execute(
                $"shell \"cat /proc/uid_stat/{uid}/tcp_rcv 2>/dev/null; echo '==='; cat /proc/uid_stat/{uid}/tcp_snd 2>/dev/null\"",
                5000);

            if (!netResult.IsSuccess) return;
            var netParts = netResult.Output.Split(new[] { "===" }, StringSplitOptions.None);
            if (netParts.Length >= 2)
            {
                long.TryParse(netParts[0].Trim(), out snapshot.appNetRxBytes);
                long.TryParse(netParts[1].Trim(), out snapshot.appNetTxBytes);
            }
        }

        // ── UI Flush ──

        private void FlushPerfData()
        {
            PerfSnapshot snapshot;
            lock (perfLock)
            {
                snapshot = perfPendingSnapshot;
            }

            if (!snapshot.isValid)
            {
                perfStatusLabel.text = $"状态: 错误 - {snapshot.errorMessage ?? "未知"}";
                perfStatusLabel.style.color = ColDanger;
                return;
            }

            perfStatusLabel.text = "状态: 监控中...";
            perfStatusLabel.style.color = ColSuccess;

            bool hasDelta = perfHasPreviousSample;

            // CPU
            if (hasDelta)
            {
                float cpu = Mathf.Clamp(snapshot.cpuUsagePercent, 0, 100);
                perfCpuBarFill.style.width = new StyleLength(new Length(cpu, LengthUnit.Percent));
                perfCpuBarFill.style.backgroundColor = GetThresholdColor(cpu, 60f, 85f);
                perfCpuValueLabel.text = $"{cpu:F1}%";
            }
            else
            {
                perfCpuValueLabel.text = "--";
            }

            // Memory
            float mem = Mathf.Clamp(snapshot.memUsagePercent, 0, 100);
            perfMemBarFill.style.width = new StyleLength(new Length(mem, LengthUnit.Percent));
            perfMemBarFill.style.backgroundColor = GetThresholdColor(mem, 70f, 90f);
            long usedKb = snapshot.memTotalKb - snapshot.memAvailableKb;
            perfMemValueLabel.text = snapshot.memTotalKb > 0
                ? $"{mem:F1}% ({FormatKb(usedKb)}/{FormatKb(snapshot.memTotalKb)})"
                : "--";

            // Battery
            float bat = Mathf.Clamp(snapshot.batteryLevel, 0, 100);
            perfBatteryBarFill.style.width = new StyleLength(new Length(bat, LengthUnit.Percent));
            perfBatteryBarFill.style.backgroundColor = GetThresholdColor(bat, 50f, 20f, true);
            string batText = $"{snapshot.batteryLevel}%";
            if (!string.IsNullOrEmpty(snapshot.batteryStatus))
                batText += $"  {snapshot.batteryStatus}";
            perfBatteryValueLabel.text = batText;

            // Battery temperature
            Color tempColor = GetThresholdColor(snapshot.batteryTempCelsius, 35f, 42f);
            perfBatteryTempLabel.text = $"{snapshot.batteryTempCelsius:F1}°C";
            perfBatteryTempLabel.style.color = tempColor;

            // Network
            if (hasDelta)
            {
                perfNetRxLabel.text = FormatBytesPerSec(snapshot.netRxBytesPerSec);
                perfNetTxLabel.text = FormatBytesPerSec(snapshot.netTxBytesPerSec);
            }
            else
            {
                perfNetRxLabel.text = "-- B/s";
                perfNetTxLabel.text = "-- B/s";
            }
            perfNetRxLabel.style.color = ColAccent;
            perfNetTxLabel.style.color = ColAccent;

            // App section
            bool showApp = snapshot.hasAppMetrics;
            perfAppSection.style.display = showApp ? DisplayStyle.Flex : DisplayStyle.None;

            if (showApp)
            {
                string pkg = perfCurrentPackage;
                if (perfAppSectionTitle != null)
                    perfAppSectionTitle.text = $"应用指标 ({pkg})";

                // App CPU
                if (hasDelta)
                {
                    float appCpu = Mathf.Clamp(snapshot.appCpuPercent, 0, 100);
                    perfAppCpuBarFill.style.width = new StyleLength(new Length(appCpu, LengthUnit.Percent));
                    perfAppCpuBarFill.style.backgroundColor = GetThresholdColor(appCpu, 40f, 70f);
                    perfAppCpuValueLabel.text = $"{appCpu:F1}%";
                }
                else
                {
                    perfAppCpuValueLabel.text = "--";
                }

                // App memory (PSS)
                float appMemMb = snapshot.appPssKb / 1024f;
                float appMemPct = Mathf.Clamp(appMemMb / 512f * 100f, 0, 100); // 512MB as reference
                perfAppMemBarFill.style.width = new StyleLength(new Length(appMemPct, LengthUnit.Percent));
                perfAppMemBarFill.style.backgroundColor = GetThresholdColor(appMemMb, 200f, 500f);
                perfAppMemValueLabel.text = snapshot.appPssKb > 0 ? FormatKb(snapshot.appPssKb) : "--";

                // FPS / Janky
                if (snapshot.appTotalFrames > 0)
                {
                    float jankyPct = snapshot.appJankyFrames * 100f / snapshot.appTotalFrames;
                    float jankyBar = Mathf.Clamp(jankyPct, 0, 100);
                    perfAppFpsBarFill.style.width = new StyleLength(new Length(jankyBar, LengthUnit.Percent));
                    perfAppFpsBarFill.style.backgroundColor = GetThresholdColor(jankyPct, 10f, 30f);
                    perfAppFpsValueLabel.text = $"{jankyPct:F1}% 卡顿 ({snapshot.appJankyFrames}/{snapshot.appTotalFrames})";
                }
                else
                {
                    perfAppFpsBarFill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
                    perfAppFpsValueLabel.text = "N/A";
                }

                // App network
                if (hasDelta)
                {
                    perfAppNetRxLabel.text = FormatBytesPerSec(snapshot.appNetRxPerSec);
                    perfAppNetTxLabel.text = FormatBytesPerSec(snapshot.appNetTxPerSec);
                }
                else
                {
                    perfAppNetRxLabel.text = "-- B/s";
                    perfAppNetTxLabel.text = "-- B/s";
                }
                perfAppNetRxLabel.style.color = ColAccent;
                perfAppNetTxLabel.style.color = ColAccent;
            }
        }

        #endregion

        #region File Manager Panel

        // ── ls -la line parser ──
        private static readonly Regex LsLineRegex = new Regex(
            @"^([drwxlsStT\-]{10})\s+(\d+)\s+(\S+)\s+(\S+)\s+(\d+)\s+" +
            @"(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2})\s+(.+)$",
            RegexOptions.Compiled);

        private static readonly Regex LsLineRegexAlt = new Regex(
            @"^([drwxlsStT\-]{10})\s+(\d+)\s+(\S+)\s+(\S+)\s+(\d+)\s+" +
            @"(\w{3}\s+\d{1,2}\s+[\d:]{4,5})\s+(.+)$",
            RegexOptions.Compiled);

        private VisualElement BuildHeadsetControlPanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;

            panel.Add(SectionTitle("头显控制"));
            panel.Add(SectionDesc("查看已连接头显的 Wi-Fi 连接状态，并控制企业级系统配置"));

            var toolbar = Row();
            toolbar.style.marginBottom = 10;

            headsetStatusLabel = new Label("进入页面后自动获取 Wi-Fi 信息");
            headsetStatusLabel.style.flexGrow = 1;
            headsetStatusLabel.style.fontSize = 12;
            headsetStatusLabel.style.color = ColTextDim;
            toolbar.Add(headsetStatusLabel);

            headsetRefreshWifiBtn = ActionButton("刷新 Wi-Fi", ColAccent);
            headsetRefreshWifiBtn.style.marginRight = 0;
            headsetRefreshWifiBtn.clicked += RefreshHeadsetWifiInfo;
            toolbar.Add(headsetRefreshWifiBtn);
            panel.Add(toolbar);

            headsetTabButtons.Clear();
            headsetTabPages.Clear();
            panel.Add(BuildHeadsetTargetDevicePanel());
            panel.Add(BuildHeadsetTabBar());

            headsetWifiScroll = new ScrollView(ScrollViewMode.Vertical);
            headsetWifiScroll.style.flexGrow = 1;
            headsetWifiScroll.style.backgroundColor = Color.clear;
            headsetWifiScroll.style.marginTop = 2;
            headsetWifiScroll.contentContainer.style.paddingRight = 6;

            var wifiPage = BuildHeadsetTabPage();
            wifiPage.Add(BuildHeadsetWifiSwitchPanel());
            wifiPage.Add(BuildHeadsetWifiInfoPanel());
            AddHeadsetTabPage("wifi", wifiPage);

            var systemPage = BuildHeadsetTabPage();
            systemPage.Add(BuildHeadsetScreenPowerPanel());
            systemPage.Add(BuildHeadsetDockVisibilityPanel());
            AddHeadsetTabPage("system", systemPage);

            var developerPage = BuildHeadsetTabPage();
            developerPage.Add(BuildHeadsetDeveloperModePanel());
            AddHeadsetTabPage("developer", developerPage);

            var logPage = BuildHeadsetTabPage();
            logPage.Add(BuildHeadsetLogPullPanel());
            AddHeadsetTabPage("logs", logPage);

            AddHeadsetEmptyState("等待获取设备 Wi-Fi 信息...", ColTextDim);
            panel.Add(headsetWifiScroll);
            ShowHeadsetTab(string.IsNullOrEmpty(activeHeadsetTabId) ? "wifi" : activeHeadsetTabId);

            return panel;
        }

        private VisualElement BuildHeadsetTargetDevicePanel()
        {
            var section = new VisualElement();
            section.style.marginBottom = 10;
            section.style.paddingTop = 10;
            section.style.paddingBottom = 10;
            section.style.paddingLeft = 14;
            section.style.paddingRight = 14;
            section.style.backgroundColor = new Color(0.08f, 0.10f, 0.18f);
            SetBorderRadius(section, 6);
            SetBorderWidth(section, 1);
            section.style.borderTopColor = ColBorder;
            section.style.borderBottomColor = ColBorder;
            section.style.borderLeftColor = ColBorder;
            section.style.borderRightColor = ColBorder;

            var row = Row();

            headsetSwitchDeviceDropdown = StyledDropdownField("目标设备");
            headsetSwitchDeviceDropdown.style.flexGrow = 1;
            headsetSwitchDeviceDropdown.style.flexShrink = 1;
            headsetSwitchDeviceDropdown.style.overflow = Overflow.Hidden;
            row.Add(headsetSwitchDeviceDropdown);

            headsetRefreshSavedWifiBtn = ActionButton("刷新列表", ColAccent);
            headsetRefreshSavedWifiBtn.style.width = 92;
            headsetRefreshSavedWifiBtn.style.marginRight = 0;
            headsetRefreshSavedWifiBtn.clicked += RefreshHeadsetSwitchDevicesAndNetworks;
            row.Add(headsetRefreshSavedWifiBtn);

            section.Add(row);
            return section;
        }

        private VisualElement BuildHeadsetTabBar()
        {
            var tabBar = Row();
            tabBar.style.marginBottom = 10;
            tabBar.style.flexWrap = Wrap.Wrap;

            tabBar.Add(BuildHeadsetTabButton("wifi", "连接与 Wi-Fi"));
            tabBar.Add(BuildHeadsetTabButton("system", "系统控制"));
            tabBar.Add(BuildHeadsetTabButton("developer", "开发者"));
            tabBar.Add(BuildHeadsetTabButton("logs", "日志"));

            return tabBar;
        }

        private Button BuildHeadsetTabButton(string tabId, string text)
        {
            var button = new Button(() => ShowHeadsetTab(tabId));
            button.text = text;
            button.style.height = 32;
            button.style.minWidth = 112;
            button.style.paddingLeft = 14;
            button.style.paddingRight = 14;
            button.style.marginRight = 8;
            button.style.marginBottom = 6;
            button.style.fontSize = 12;
            button.style.color = ColTextDim;
            button.style.backgroundColor = ColBgInputField;
            SetBorderRadius(button, 5);
            SetBorderWidth(button, 1);
            button.style.borderTopColor = ColBorder;
            button.style.borderBottomColor = ColBorder;
            button.style.borderLeftColor = ColBorder;
            button.style.borderRightColor = ColBorder;

            headsetTabButtons[tabId] = button;
            return button;
        }

        private VisualElement BuildHeadsetTabPage()
        {
            var page = new VisualElement();
            page.style.flexGrow = 1;
            page.style.display = DisplayStyle.None;
            return page;
        }

        private void AddHeadsetTabPage(string tabId, VisualElement page)
        {
            headsetTabPages[tabId] = page;
            headsetWifiScroll.Add(page);
        }

        private void ShowHeadsetTab(string tabId)
        {
            if (!headsetTabPages.ContainsKey(tabId))
                tabId = "wifi";

            activeHeadsetTabId = tabId;

            foreach (var kvp in headsetTabPages)
                kvp.Value.style.display = kvp.Key == tabId ? DisplayStyle.Flex : DisplayStyle.None;

            foreach (var kvp in headsetTabButtons)
            {
                bool active = kvp.Key == tabId;
                kvp.Value.style.backgroundColor = active ? ColAccent : ColBgInputField;
                kvp.Value.style.color = active ? ColText : ColTextDim;
                kvp.Value.style.borderTopColor = active ? ColAccent : ColBorder;
                kvp.Value.style.borderBottomColor = active ? ColAccent : ColBorder;
                kvp.Value.style.borderLeftColor = active ? ColAccent : ColBorder;
                kvp.Value.style.borderRightColor = active ? ColAccent : ColBorder;
            }

            if (headsetWifiScroll != null)
                headsetWifiScroll.scrollOffset = Vector2.zero;
        }

        private VisualElement BuildHeadsetWifiInfoPanel()
        {
            var section = new VisualElement();
            section.style.marginBottom = 12;
            section.style.paddingTop = 12;
            section.style.paddingBottom = 12;
            section.style.paddingLeft = 14;
            section.style.paddingRight = 14;
            section.style.backgroundColor = ColBgOutput;
            SetBorderRadius(section, 6);
            SetBorderWidth(section, 1);
            section.style.borderTopColor = ColBorder;
            section.style.borderBottomColor = ColBorder;
            section.style.borderLeftColor = ColBorder;
            section.style.borderRightColor = ColBorder;

            var title = new Label("设备 Wi-Fi 信息");
            title.style.fontSize = 14;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10;
            section.Add(title);

            headsetWifiContainer = new VisualElement();
            headsetWifiContainer.style.flexGrow = 1;
            section.Add(headsetWifiContainer);

            return section;
        }

        private VisualElement BuildHeadsetWifiSwitchPanel()
        {
            var section = new VisualElement();
            section.style.marginBottom = 12;
            section.style.paddingTop = 12;
            section.style.paddingBottom = 12;
            section.style.paddingLeft = 14;
            section.style.paddingRight = 14;
            section.style.backgroundColor = new Color(0.08f, 0.10f, 0.18f);
            SetBorderRadius(section, 6);
            SetBorderWidth(section, 1);
            section.style.borderTopColor = ColBorder;
            section.style.borderBottomColor = ColBorder;
            section.style.borderLeftColor = ColBorder;
            section.style.borderRightColor = ColBorder;

            var title = new Label("Wi-Fi 切换");
            title.style.fontSize = 14;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10;
            section.Add(title);

            var wifiRow = Row();
            wifiRow.style.marginBottom = 8;

            // Wi-Fi 列表独占一行，避免多设备长名称把目标 Wi-Fi 下拉框挤出视野。
            headsetSavedWifiDropdown = StyledDropdownField("本机已保存 Wi-Fi");
            headsetSavedWifiDropdown.style.flexGrow = 1;
            headsetSavedWifiDropdown.style.flexShrink = 1;
            headsetSavedWifiDropdown.style.marginRight = 0;
            headsetSavedWifiDropdown.style.overflow = Overflow.Hidden;
            headsetSavedWifiDropdown.RegisterValueChangedCallback(_ => ApplyRememberedWifiPassword());
            wifiRow.Add(headsetSavedWifiDropdown);
            section.Add(wifiRow);

            var passwordRow = Row();
            passwordRow.style.marginBottom = 8;

            headsetWifiPasswordField = StyledTextField("Wi-Fi 密码", "首次切换需要输入密码");
            headsetWifiPasswordField.isPasswordField = true;
            headsetWifiPasswordField.style.flexGrow = 1;
            headsetWifiPasswordField.style.flexShrink = 1;
            headsetWifiPasswordField.style.overflow = Overflow.Hidden;
            headsetWifiPasswordField.style.marginRight = 0;
            passwordRow.Add(headsetWifiPasswordField);
            section.Add(passwordRow);

            var actionRow = Row();
            actionRow.style.marginBottom = 8;

            actionRow.Add(StyledToggleItem("记住密码", true, out headsetRememberWifiPasswordToggle));

            actionRow.Add(StyledToggleItem("显示密码", false, out headsetShowWifiPasswordToggle));
            headsetShowWifiPasswordToggle.RegisterValueChangedCallback(evt => SetHeadsetWifiPasswordVisible(evt.newValue));

            actionRow.Add(StyledToggleItem("强制切换", true, out headsetForceClearSavedWifiToggle));
            headsetForceClearSavedWifiToggle.tooltip = "切换前清空头显已保存 Wi-Fi，避免 Android 继续选择其它历史网络。";

            actionRow.Add(Spacer());

            headsetSwitchWifiBtn = ActionButton("切换 Wi-Fi", ColSuccess);
            headsetSwitchWifiBtn.style.marginRight = 0;
            headsetSwitchWifiBtn.clicked += SwitchHeadsetWifi;
            actionRow.Add(headsetSwitchWifiBtn);
            section.Add(actionRow);

            headsetSwitchWifiStatusLabel = new Label("选择设备和 Wi-Fi 后，通过 ADB 发起切换；强制切换会先清空头显已保存 Wi-Fi。");
            headsetSwitchWifiStatusLabel.style.fontSize = 12;
            headsetSwitchWifiStatusLabel.style.color = ColTextDim;
            headsetSwitchWifiStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            section.Add(headsetSwitchWifiStatusLabel);

            return section;
        }

        private VisualElement BuildHeadsetQuickActionsPanel()
        {
            var section = new VisualElement();
            section.style.marginBottom = 12;
            section.style.paddingTop = 12;
            section.style.paddingBottom = 10;
            section.style.paddingLeft = 14;
            section.style.paddingRight = 14;
            section.style.backgroundColor = new Color(0.08f, 0.10f, 0.18f);
            SetBorderRadius(section, 6);
            SetBorderWidth(section, 1);
            section.style.borderTopColor = ColBorder;
            section.style.borderBottomColor = ColBorder;
            section.style.borderLeftColor = ColBorder;
            section.style.borderRightColor = ColBorder;

            var title = new Label("快捷操作");
            title.style.fontSize = 14;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 2;
            section.Add(title);

            var desc = new Label("使用上方目标设备执行常用头显控制命令。");
            desc.style.fontSize = 12;
            desc.style.color = ColTextDim;
            desc.style.marginBottom = 10;
            section.Add(desc);

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.Stretch;
            section.Add(grid);

            VisualElement buttons;

            var screenGroup = BuildHeadsetQuickActionGroup(
                "屏幕",
                "Wi-Fi ADB 在深度休眠时可能断开，USB 连接更可靠。",
                out buttons);
            var wakeBtn = HeadsetCompactButton("唤醒屏幕", ColSuccess);
            wakeBtn.tooltip = "通过 ADB 发送 Android KEYCODE_WAKEUP(224)，用于点亮已连接头显屏幕。";
            wakeBtn.clicked += () => SendHeadsetScreenKeyevent(HeadsetKeyeventWakeup, "唤醒屏幕", wakeBtn);
            buttons.Add(wakeBtn);

            var sleepBtn = HeadsetCompactButton("息屏", ColWarning);
            sleepBtn.tooltip = "通过 ADB 发送 Android KEYCODE_SLEEP(223)，用于关闭已连接头显屏幕。";
            sleepBtn.clicked += () => SendHeadsetScreenKeyevent(HeadsetKeyeventSleep, "息屏", sleepBtn);
            buttons.Add(sleepBtn);
            grid.Add(screenGroup);

            var developerGroup = BuildHeadsetQuickActionGroup(
                "开发者",
                "需要设备已通过 ADB 授权；未开启 USB 调试的设备需先在头显内授权。",
                out buttons);
            var queryDeveloperOptionsBtn = HeadsetCompactButton("查询开发者选项", ColAccent);
            queryDeveloperOptionsBtn.tooltip = "读取 Android global setting: development_settings_enabled。";
            queryDeveloperOptionsBtn.clicked += () => SendHeadsetShellCommand(
                "查询开发者选项",
                "settings get global development_settings_enabled",
                queryDeveloperOptionsBtn);
            buttons.Add(queryDeveloperOptionsBtn);

            var showDeveloperOptionsBtn = HeadsetCompactButton("显示开发者选项", ColSuccess);
            showDeveloperOptionsBtn.tooltip = "写入 Android global setting: development_settings_enabled=1，仅对已授权 ADB 设备生效。";
            showDeveloperOptionsBtn.clicked += () => SendHeadsetShellCommand(
                "显示开发者选项",
                "settings put global development_settings_enabled 1",
                showDeveloperOptionsBtn);
            buttons.Add(showDeveloperOptionsBtn);

            var openDeveloperSettingsBtn = HeadsetCompactButton("打开开发者设置", ColAccent);
            openDeveloperSettingsBtn.tooltip = "在头显上打开 Android 开发者选项设置页，方便人工确认 USB 调试授权。";
            openDeveloperSettingsBtn.clicked += () => SendHeadsetShellCommand(
                "打开开发者设置",
                "am start -a android.settings.APPLICATION_DEVELOPMENT_SETTINGS",
                openDeveloperSettingsBtn);
            buttons.Add(openDeveloperSettingsBtn);

            var queryUsbDebugBtn = HeadsetCompactButton("查询 USB 调试", ColNeutral);
            queryUsbDebugBtn.tooltip = "读取 Android global setting: adb_enabled；未授权设备无法由 PC 端直接开启 USB 调试。";
            queryUsbDebugBtn.clicked += () => SendHeadsetShellCommand(
                "查询 USB 调试状态",
                "settings get global adb_enabled",
                queryUsbDebugBtn);
            buttons.Add(queryUsbDebugBtn);
            grid.Add(developerGroup);

            var logGroup = BuildHeadsetQuickActionGroup(
                "日志",
                "拉取 /sdcard/logcatch 到桌面，文件夹按时间命名，日志文件按顺序改为 logcat1.log。",
                out buttons);
            var pullBtn = HeadsetCompactButton("拉取 logcatch", ColSuccess);
            pullBtn.tooltip = "从当前目标设备复制 /sdcard/logcatch 到桌面，并将其中日志按顺序改为 logcat1.log、logcat2.log...";
            pullBtn.clicked += () => PullHeadsetLogcatchFolder(pullBtn);
            buttons.Add(pullBtn);
            grid.Add(logGroup);

            var dockGroup = BuildHeadsetQuickActionGroup(
                "导航栏",
                "通过 PICO 企业级 ToBService 控制系统导航栏显示状态。",
                out buttons);
            var hideBtn = HeadsetCompactButton("隐藏导航栏", ColWarning);
            hideBtn.tooltip = "通过 PICO 企业级 ToBService 执行 set_dock visible=0。";
            hideBtn.clicked += () => SetHeadsetDockVisible(false);
            buttons.Add(hideBtn);

            var showBtn = HeadsetCompactButton("显示导航栏", ColSuccess);
            showBtn.tooltip = "通过 PICO 企业级 ToBService 执行 set_dock visible=1。";
            showBtn.clicked += () => SetHeadsetDockVisible(true);
            buttons.Add(showBtn);
            grid.Add(dockGroup);

            return section;
        }

        private VisualElement BuildHeadsetQuickActionGroup(string titleText, string hintText, out VisualElement buttonsRow)
        {
            var group = new VisualElement();
            group.style.width = new Length(48, LengthUnit.Percent);
            group.style.minWidth = 320;
            group.style.flexGrow = 1;
            group.style.marginRight = 8;
            group.style.marginBottom = 8;
            group.style.paddingTop = 10;
            group.style.paddingBottom = 10;
            group.style.paddingLeft = 12;
            group.style.paddingRight = 12;
            group.style.backgroundColor = new Color(0.06f, 0.08f, 0.15f);
            SetBorderRadius(group, 5);
            SetBorderWidth(group, 1);
            group.style.borderTopColor = ColBorder;
            group.style.borderBottomColor = ColBorder;
            group.style.borderLeftColor = ColBorder;
            group.style.borderRightColor = ColBorder;

            var title = new Label(titleText);
            title.style.fontSize = 13;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            group.Add(title);

            buttonsRow = Row();
            buttonsRow.style.flexWrap = Wrap.Wrap;
            buttonsRow.style.alignItems = Align.Center;
            buttonsRow.style.marginBottom = 4;
            group.Add(buttonsRow);

            var hint = new Label(hintText);
            hint.style.fontSize = 11;
            hint.style.color = ColTextDim;
            hint.style.whiteSpace = WhiteSpace.Normal;
            group.Add(hint);

            return group;
        }

        private Button HeadsetCompactButton(string text, Color bgColor)
        {
            var button = ActionButton(text, bgColor);
            button.style.height = 30;
            button.style.minWidth = 92;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.marginBottom = 6;
            button.style.fontSize = 12;
            return button;
        }

        private VisualElement BuildHeadsetScreenPowerPanel()
        {
            var section = new VisualElement();
            section.style.marginBottom = 12;
            section.style.paddingTop = 12;
            section.style.paddingBottom = 12;
            section.style.paddingLeft = 14;
            section.style.paddingRight = 14;
            section.style.backgroundColor = new Color(0.08f, 0.10f, 0.18f);
            SetBorderRadius(section, 6);
            SetBorderWidth(section, 1);
            section.style.borderTopColor = ColBorder;
            section.style.borderBottomColor = ColBorder;
            section.style.borderLeftColor = ColBorder;
            section.style.borderRightColor = ColBorder;

            var title = new Label("屏幕控制");
            title.style.fontSize = 14;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10;
            section.Add(title);

            var row = Row();
            row.style.marginBottom = 8;
            row.style.flexWrap = Wrap.Wrap;

            var wakeBtn = ActionButton("唤醒屏幕", ColSuccess);
            wakeBtn.tooltip = "通过 ADB 发送 Android KEYCODE_WAKEUP(224)，用于点亮已连接头显屏幕。";
            wakeBtn.clicked += () => SendHeadsetScreenKeyevent(HeadsetKeyeventWakeup, "唤醒屏幕", wakeBtn);
            row.Add(wakeBtn);

            var sleepBtn = ActionButton("息屏", ColWarning);
            sleepBtn.tooltip = "通过 ADB 发送 Android KEYCODE_SLEEP(223)，用于关闭已连接头显屏幕。";
            sleepBtn.clicked += () => SendHeadsetScreenKeyevent(HeadsetKeyeventSleep, "息屏", sleepBtn);
            row.Add(sleepBtn);

            var hint = new Label("使用上方目标设备下拉框中的当前设备；Wi-Fi ADB 在深度休眠时可能断开，USB 连接更可靠。");
            hint.style.fontSize = 12;
            hint.style.color = ColTextDim;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.flexGrow = 1;
            row.Add(hint);

            section.Add(row);

            return section;
        }

        private VisualElement BuildHeadsetDeveloperModePanel()
        {
            var section = new VisualElement();
            section.style.marginBottom = 12;
            section.style.paddingTop = 12;
            section.style.paddingBottom = 12;
            section.style.paddingLeft = 14;
            section.style.paddingRight = 14;
            section.style.backgroundColor = new Color(0.08f, 0.10f, 0.18f);
            SetBorderRadius(section, 6);
            SetBorderWidth(section, 1);
            section.style.borderTopColor = ColBorder;
            section.style.borderBottomColor = ColBorder;
            section.style.borderLeftColor = ColBorder;
            section.style.borderRightColor = ColBorder;

            var title = new Label("开发者模式");
            title.style.fontSize = 14;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10;
            section.Add(title);

            var row = Row();
            row.style.marginBottom = 8;
            row.style.flexWrap = Wrap.Wrap;

            var queryDeveloperOptionsBtn = ActionButton("查询开发者选项", ColAccent);
            queryDeveloperOptionsBtn.tooltip = "读取 Android global setting: development_settings_enabled。";
            queryDeveloperOptionsBtn.clicked += () => SendHeadsetShellCommand(
                "查询开发者选项",
                "settings get global development_settings_enabled",
                queryDeveloperOptionsBtn);
            row.Add(queryDeveloperOptionsBtn);

            var showDeveloperOptionsBtn = ActionButton("显示开发者选项", ColSuccess);
            showDeveloperOptionsBtn.tooltip = "写入 Android global setting: development_settings_enabled=1，仅对已授权 ADB 设备生效。";
            showDeveloperOptionsBtn.clicked += () => SendHeadsetShellCommand(
                "显示开发者选项",
                "settings put global development_settings_enabled 1",
                showDeveloperOptionsBtn);
            row.Add(showDeveloperOptionsBtn);

            var openDeveloperSettingsBtn = ActionButton("打开开发者设置", ColAccent);
            openDeveloperSettingsBtn.tooltip = "在头显上打开 Android 开发者选项设置页，方便人工确认 USB 调试授权。";
            openDeveloperSettingsBtn.clicked += () => SendHeadsetShellCommand(
                "打开开发者设置",
                "am start -a android.settings.APPLICATION_DEVELOPMENT_SETTINGS",
                openDeveloperSettingsBtn);
            row.Add(openDeveloperSettingsBtn);

            var queryUsbDebugBtn = ActionButton("查询 USB 调试", ColNeutral);
            queryUsbDebugBtn.tooltip = "读取 Android global setting: adb_enabled；未授权设备无法由 PC 端直接开启 USB 调试。";
            queryUsbDebugBtn.clicked += () => SendHeadsetShellCommand(
                "查询 USB 调试状态",
                "settings get global adb_enabled",
                queryUsbDebugBtn);
            row.Add(queryUsbDebugBtn);

            section.Add(row);

            var hint = new Label("这些操作需要设备已通过 ADB 授权；未开启 USB 调试的设备仍需在头显内先完成授权。");
            hint.style.fontSize = 12;
            hint.style.color = ColTextDim;
            hint.style.whiteSpace = WhiteSpace.Normal;
            section.Add(hint);

            return section;
        }

        private VisualElement BuildHeadsetLogPullPanel()
        {
            var section = new VisualElement();
            section.style.marginBottom = 12;
            section.style.paddingTop = 12;
            section.style.paddingBottom = 12;
            section.style.paddingLeft = 14;
            section.style.paddingRight = 14;
            section.style.backgroundColor = new Color(0.08f, 0.10f, 0.18f);
            SetBorderRadius(section, 6);
            SetBorderWidth(section, 1);
            section.style.borderTopColor = ColBorder;
            section.style.borderBottomColor = ColBorder;
            section.style.borderLeftColor = ColBorder;
            section.style.borderRightColor = ColBorder;

            var title = new Label("日志拉取");
            title.style.fontSize = 14;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10;
            section.Add(title);

            var row = Row();
            row.style.marginBottom = 8;
            row.style.flexWrap = Wrap.Wrap;

            var pullBtn = ActionButton("拉取 logcatch 到桌面", ColSuccess);
            pullBtn.tooltip = "从当前目标设备复制 /sdcard/logcatch 到桌面，并将其中日志按顺序改为 logcat1.log、logcat2.log...";
            pullBtn.clicked += () => PullHeadsetLogcatchFolder(pullBtn);
            row.Add(pullBtn);

            var hint = new Label("目标文件夹示例: 桌面/logcatch_202607091137；日志文件会按时间顺序重命名为 logcat1.log、logcat2.log...");
            hint.style.fontSize = 12;
            hint.style.color = ColTextDim;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.flexGrow = 1;
            row.Add(hint);

            section.Add(row);

            return section;
        }

        private VisualElement BuildHeadsetDockVisibilityPanel()
        {
            var section = new VisualElement();
            section.style.marginBottom = 12;
            section.style.paddingTop = 12;
            section.style.paddingBottom = 12;
            section.style.paddingLeft = 14;
            section.style.paddingRight = 14;
            section.style.backgroundColor = new Color(0.08f, 0.10f, 0.18f);
            SetBorderRadius(section, 6);
            SetBorderWidth(section, 1);
            section.style.borderTopColor = ColBorder;
            section.style.borderBottomColor = ColBorder;
            section.style.borderLeftColor = ColBorder;
            section.style.borderRightColor = ColBorder;

            var title = new Label("导航栏控制");
            title.style.fontSize = 14;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10;
            section.Add(title);

            var row = Row();
            row.style.marginBottom = 8;

            var hideBtn = ActionButton("隐藏导航栏", ColWarning);
            hideBtn.tooltip = "通过 PICO 企业级 ToBService 执行 set_dock visible=0。";
            hideBtn.clicked += () => SetHeadsetDockVisible(false);
            row.Add(hideBtn);

            var showBtn = ActionButton("显示导航栏", ColSuccess);
            showBtn.tooltip = "通过 PICO 企业级 ToBService 执行 set_dock visible=1。";
            showBtn.clicked += () => SetHeadsetDockVisible(true);
            row.Add(showBtn);

            var hint = new Label("使用上方目标设备下拉框中的当前设备；执行后如未立即变化，请等待系统服务刷新。");
            hint.style.fontSize = 12;
            hint.style.color = ColTextDim;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.flexGrow = 1;
            row.Add(hint);

            section.Add(row);

            return section;
        }

        private void RefreshHeadsetWifiInfo()
        {
            if (headsetWifiLoading || headsetWifiContainer == null)
                return;

            headsetWifiLoading = true;
            headsetRefreshWifiBtn?.SetEnabled(false);
            headsetStatusLabel.text = "正在获取已连接设备的 Wi-Fi 信息...";
            headsetStatusLabel.style.color = ColWarning;
            headsetWifiContainer.Clear();
            AddHeadsetEmptyState("正在查询 ADB 设备列表...", ColTextDim);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var results = new List<HeadsetWifiInfo>();
                string error = null;

                try
                {
                    var devicesResult = adb.Execute("devices -l", 10000);
                    if (!devicesResult.IsSuccess)
                    {
                        error = devicesResult.GetDisplayText();
                    }
                    else
                    {
                        var devices = ParseAdbDevices(devicesResult.Output);
                        foreach (var device in devices)
                        {
                            if (device.Status == "device")
                                results.Add(CollectHeadsetWifiInfo(device));
                            else
                                results.Add(new HeadsetWifiInfo
                                {
                                    Serial = device.Serial,
                                    Model = device.Model,
                                    DeviceStatus = device.Status,
                                    QuerySuccess = false,
                                    ErrorMessage = GetAdbDeviceStatusMessage(device.Status)
                                });
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                MainThreadDispatcher.Enqueue(() => OnHeadsetWifiInfoReady(results, error));
            });
        }

        private void OnHeadsetWifiInfoReady(List<HeadsetWifiInfo> results, string error)
        {
            headsetWifiLoading = false;
            headsetRefreshWifiBtn?.SetEnabled(true);
            headsetWifiContainer.Clear();

            if (!string.IsNullOrEmpty(error))
            {
                headsetStatusLabel.text = "Wi-Fi 信息获取失败";
                headsetStatusLabel.style.color = ColDanger;
                AddHeadsetEmptyState(error, ColDanger);
                return;
            }

            if (results.Count == 0)
            {
                headsetStatusLabel.text = "未发现 ADB 已连接设备";
                headsetStatusLabel.style.color = ColWarning;
                AddHeadsetEmptyState("请确认头显已通过 USB 或网络 ADB 连接，并已完成授权。", ColTextDim);
                return;
            }

            int successCount = results.Count(item => item.QuerySuccess);
            headsetStatusLabel.text = $"已刷新 {results.Count} 台设备，成功 {successCount} 台  {DateTime.Now:HH:mm:ss}";
            headsetStatusLabel.style.color = successCount > 0 ? ColSuccess : ColWarning;

            foreach (var info in results)
                headsetWifiContainer.Add(BuildHeadsetWifiCard(info));
        }

        private void RefreshHeadsetSwitchDevicesAndNetworks()
        {
            if (headsetSwitchDeviceDropdown == null)
                return;

            SetHeadsetSwitchStatus("正在刷新头显设备列表...", ColWarning);
            headsetRefreshSavedWifiBtn?.SetEnabled(false);
            headsetSwitchWifiBtn?.SetEnabled(false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var devices = new List<AdbDeviceInfo>();
                string error = null;

                try
                {
                    var result = adb.Execute("devices -l", 10000);
                    if (!result.IsSuccess)
                        error = result.GetDisplayText();
                    else
                        devices = ParseAdbDevices(result.Output).Where(item => item.Status == "device").ToList();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                MainThreadDispatcher.Enqueue(() =>
                {
                    headsetRefreshSavedWifiBtn?.SetEnabled(true);
                    headsetSwitchWifiBtn?.SetEnabled(true);
                    OnHeadsetSwitchDevicesReady(devices, error);
                });
            });
        }

        private void OnHeadsetSwitchDevicesReady(List<AdbDeviceInfo> devices, string error)
        {
            headsetSwitchDevices.Clear();
            headsetSwitchDevices.AddRange(devices);

            if (!string.IsNullOrEmpty(error))
            {
                headsetSwitchDeviceDropdown.choices = new List<string>();
                headsetSwitchDeviceDropdown.value = "";
                SetHeadsetSwitchStatus($"设备列表刷新失败: {error}", ColDanger);
                return;
            }

            if (headsetSwitchDevices.Count == 0)
            {
                headsetSwitchDeviceDropdown.choices = new List<string>();
                headsetSwitchDeviceDropdown.value = "";
                SetHeadsetSwitchStatus("未发现可切换 Wi-Fi 的 ADB 设备。", ColWarning);
                return;
            }

            var choices = headsetSwitchDevices.Select(GetHeadsetDeviceDisplayName).ToList();
            string previous = headsetSwitchDeviceDropdown.value;
            headsetSwitchDeviceDropdown.choices = choices;
            headsetSwitchDeviceDropdown.value = choices.Contains(previous) ? previous : choices[0];
            RefreshHeadsetSavedWifiNetworks();
        }

        private void RefreshHeadsetSavedWifiNetworks()
        {
            if (headsetSavedWifiDropdown == null)
                return;

            var device = GetSelectedHeadsetSwitchDevice();
            if (device == null)
            {
                headsetSavedWifiDropdown.choices = new List<string>();
                headsetSavedWifiDropdown.value = "";
                SetHeadsetSwitchStatus("请先选择目标设备。", ColWarning);
                return;
            }

            SetHeadsetSwitchStatus("正在读取本机电脑已保存 Wi-Fi...", ColWarning);
            headsetRefreshSavedWifiBtn?.SetEnabled(false);
            headsetSwitchWifiBtn?.SetEnabled(false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var networks = new List<HeadsetSavedWifiNetwork>();
                string error = null;

                try
                {
                    networks = GetLocalSavedWifiNetworks();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                MainThreadDispatcher.Enqueue(() =>
                {
                    headsetRefreshSavedWifiBtn?.SetEnabled(true);
                    headsetSwitchWifiBtn?.SetEnabled(true);
                    OnHeadsetSavedWifiNetworksReady(networks, error);
                });
            });
        }

        private void OnHeadsetSavedWifiNetworksReady(List<HeadsetSavedWifiNetwork> networks, string error)
        {
            headsetSavedWifiNetworks.Clear();
            headsetSavedWifiNetworks.AddRange(networks);

            if (!string.IsNullOrEmpty(error))
            {
                headsetSavedWifiDropdown.choices = new List<string>();
                headsetSavedWifiDropdown.value = "";
                SetHeadsetSwitchStatus($"Wi-Fi 列表读取失败: {error}", ColDanger);
                return;
            }

            if (headsetSavedWifiNetworks.Count == 0)
            {
                headsetSavedWifiDropdown.choices = new List<string>();
                headsetSavedWifiDropdown.value = "";
                SetHeadsetSwitchStatus("本机电脑没有读取到已保存 Wi-Fi。", ColWarning);
                return;
            }

            var choices = headsetSavedWifiNetworks.Select(GetHeadsetSavedWifiDisplayName).ToList();
            string previous = headsetSavedWifiDropdown.value;
            headsetSavedWifiDropdown.choices = choices;
            headsetSavedWifiDropdown.value = choices.Contains(previous) ? previous : choices[0];
            ApplyRememberedWifiPassword();
            SetHeadsetSwitchStatus($"已读取本机 {headsetSavedWifiNetworks.Count} 个已保存 Wi-Fi，密码已自动预填。", ColSuccess);
        }

        private static List<HeadsetSavedWifiNetwork> GetLocalSavedWifiNetworks()
        {
            var profiles = global::WifiPasswordReader.GetSavedWifiProfiles();
            var networks = new List<HeadsetSavedWifiNetwork>();
            var seen = new HashSet<string>();

            foreach (var profile in profiles)
            {
                string ssid = NormalizeWifiValue(profile?.Name);
                if (string.IsNullOrEmpty(ssid) || !seen.Add(ssid))
                    continue;

                string password = profile.Password ?? "";
                networks.Add(new HeadsetSavedWifiNetwork
                {
                    NetworkId = "",
                    Ssid = ssid,
                    Password = password,
                    SecurityTypes = string.IsNullOrEmpty(password) ? "open" : "wpa2-psk"
                });
            }

            return networks.OrderBy(item => item.Ssid, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<HeadsetSavedWifiNetwork> ParseHeadsetSavedWifiNetworks(string output)
        {
            var networks = new List<HeadsetSavedWifiNetwork>();
            if (string.IsNullOrEmpty(output))
                return networks;

            var byKey = new Dictionary<string, HeadsetSavedWifiNetwork>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.IndexOf("Network Id", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var match = Regex.Match(line, @"^\s*(\d+)\s+(.+?)\s{2,}(\S+)\s*$");
                if (!match.Success)
                    continue;

                string networkId = match.Groups[1].Value.Trim();
                string ssid = NormalizeWifiValue(match.Groups[2].Value.Trim());
                string securityType = match.Groups[3].Value.Trim();
                if (string.IsNullOrEmpty(ssid))
                    continue;

                string key = ssid;
                if (!byKey.TryGetValue(key, out var item))
                {
                    item = new HeadsetSavedWifiNetwork
                    {
                        NetworkId = networkId,
                        Ssid = ssid,
                        SecurityTypes = securityType
                    };
                    byKey[key] = item;
                    networks.Add(item);
                }
                else if (!item.SecurityTypes.Contains(securityType))
                {
                    item.SecurityTypes += "/" + securityType;
                }
            }

            return networks;
        }

        private void ApplyRememberedWifiPassword()
        {
            var network = GetSelectedHeadsetSavedWifiNetwork();
            if (network == null || headsetWifiPasswordField == null)
                return;

            string password = network.Password;
            if (string.IsNullOrEmpty(password))
                password = GetRememberedHeadsetWifiPassword(network.Ssid);

            if (!string.IsNullOrEmpty(password))
            {
                headsetWifiPasswordField.value = password;
                headsetRememberWifiPasswordToggle.value = true;
                SetHeadsetSwitchStatus($"已预填 {network.Ssid} 的本机 Wi-Fi 密码。", ColSuccess);
            }
            else
            {
                headsetWifiPasswordField.value = "";
                SetHeadsetSwitchStatus($"{network.Ssid} 未读取到本机密码，如需连接加密 Wi-Fi 请手动输入。", ColWarning);
            }
        }

        private void SetHeadsetWifiPasswordVisible(bool visible)
        {
            if (headsetWifiPasswordField == null)
                return;

            headsetWifiPasswordField.isPasswordField = !visible;
        }

        private void SwitchHeadsetWifi()
        {
            var device = GetSelectedHeadsetSwitchDevice();
            var network = GetSelectedHeadsetSavedWifiNetwork();
            string password = headsetWifiPasswordField?.value ?? "";
            bool forceClearSavedNetworks = headsetForceClearSavedWifiToggle?.value == true;

            if (device == null)
            {
                SetHeadsetSwitchStatus("请先选择目标设备。", ColDanger);
                return;
            }

            if (network == null)
            {
                SetHeadsetSwitchStatus("请先选择要切换的 Wi-Fi。", ColDanger);
                return;
            }

            string fallbackSecurityType = ResolveHeadsetSuggestionSecurityType(network.SecurityTypes);
            bool requiresPassword = fallbackSecurityType == "wpa2" || fallbackSecurityType == "wpa3";

            if (string.IsNullOrEmpty(fallbackSecurityType))
            {
                SetHeadsetSwitchStatus($"暂不支持该 Wi-Fi 安全类型: {network.SecurityTypes}", ColDanger);
                return;
            }

            if (requiresPassword && string.IsNullOrEmpty(password))
            {
                SetHeadsetSwitchStatus("请输入 Wi-Fi 密码。", ColDanger);
                return;
            }

            if (requiresPassword && headsetRememberWifiPasswordToggle?.value == true)
                RememberHeadsetWifiPassword(network.Ssid, password);
            else
                ForgetHeadsetWifiPassword(network.Ssid);

            string switchMode = forceClearSavedNetworks ? "强制切换" : "普通切换";
            SetHeadsetSwitchStatus($"正在{switchMode}到 {network.Ssid}...", ColWarning);
            headsetSwitchWifiBtn?.SetEnabled(false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string status;
                Color color;

                try
                {
                    string securityType = ResolveHeadsetSuggestionSecurityType(device.Serial, network, fallbackSecurityType);
                    bool resolvedRequiresPassword = securityType == "wpa2" || securityType == "wpa3";
                    if (resolvedRequiresPassword && string.IsNullOrEmpty(password))
                    {
                        status = $"未读取到 {network.Ssid} 的本机密码，请手动输入后重试。";
                        color = ColDanger;
                        MainThreadDispatcher.Enqueue(() =>
                        {
                            headsetSwitchWifiBtn?.SetEnabled(true);
                            SetHeadsetSwitchStatus(status, color);
                        });
                        return;
                    }

                    adb.Execute($"-s {device.Serial} shell cmd wifi set-wifi-enabled enabled", 8000);

                    int forgottenCount = 0;
                    if (forceClearSavedNetworks)
                    {
                        var clearResult = ForgetAllHeadsetSavedWifiNetworks(device.Serial);
                        if (!clearResult.Success)
                        {
                            status = $"清空已保存 Wi-Fi 失败: {clearResult.Message}";
                            color = ColDanger;
                            MainThreadDispatcher.Enqueue(() =>
                            {
                                headsetSwitchWifiBtn?.SetEnabled(true);
                                SetHeadsetSwitchStatus(status, color);
                                RefreshHeadsetWifiInfo();
                            });
                            return;
                        }

                        forgottenCount = clearResult.ForgottenCount;

                        // 强制切换要避免旧 suggestion 继续参与系统选网，和 saved network 一起清掉。
                        adb.Execute($"-s {device.Serial} shell cmd wifi remove-all-suggestions", 8000);
                    }

                    // 先移除同名 shell suggestion，避免旧密码或旧参数影响本次切换。
                    adb.Execute($"-s {device.Serial} shell cmd wifi remove-suggestion {ShellQuote(network.Ssid)}", 8000);

                    string credentialPart = resolvedRequiresPassword ? " " + ShellQuote(password) : "";
                    string addCommand = $"-s {device.Serial} shell cmd wifi add-suggestion {ShellQuote(network.Ssid)} {securityType}{credentialPart} -s";
                    var addResult = adb.Execute(addCommand, 12000);
                    if (!addResult.IsSuccess)
                    {
                        status = $"发送 Wi-Fi suggestion 失败: {addResult.GetDisplayText()}";
                        color = ColDanger;
                    }
                    else
                    {
                        var approvalResult = adb.Execute($"-s {device.Serial} shell cmd wifi network-suggestions-set-user-approved com.android.shell yes", 8000);
                        bool approvalDenied = !approvalResult.IsSuccess
                            && approvalResult.GetDisplayText().IndexOf("SecurityException", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (forceClearSavedNetworks)
                            RestartHeadsetWifiRadio(device.Serial);

                        int waitTimeoutMs = forceClearSavedNetworks ? 40000 : 25000;
                        if (WaitForHeadsetWifiSsid(device.Serial, network.Ssid, waitTimeoutMs))
                        {
                            string clearHint = forceClearSavedNetworks ? $"已清空 {forgottenCount} 个已保存 Wi-Fi，" : "";
                            status = $"{clearHint}已切换到 {network.Ssid}";
                            color = ColSuccess;
                        }
                        else
                        {
                            string approvalHint = approvalDenied ? "当前系统拒绝 shell approval；" : "";
                            string clearHint = forceClearSavedNetworks ? $"已清空 {forgottenCount} 个已保存 Wi-Fi；" : "";
                            status = $"{clearHint}已发送 Wi-Fi suggestion: {network.Ssid} ({securityType}, shared)。{approvalHint}系统尚未选择该网络。";
                            color = ColWarning;
                        }
                    }
                }
                catch (Exception ex)
                {
                    status = $"切换 Wi-Fi 异常: {ex.Message}";
                    color = ColDanger;
                }

                MainThreadDispatcher.Enqueue(() =>
                {
                    headsetSwitchWifiBtn?.SetEnabled(true);
                    SetHeadsetSwitchStatus(status, color);
                    RefreshHeadsetWifiInfo();
                });
            });
        }

        private void SendHeadsetScreenKeyevent(int keyevent, string actionText, Button triggerButton)
        {
            var device = GetSelectedHeadsetSwitchDevice();
            if (device == null)
            {
                SetHeadsetSwitchStatus("请先选择目标设备。", ColDanger);
                return;
            }

            SetHeadsetSwitchStatus($"正在{actionText}...", ColWarning);
            triggerButton?.SetEnabled(false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string command = $"-s {device.Serial} shell input keyevent {keyevent}";
                var result = adb.Execute(command, 12000);
                string status = result.IsSuccess
                    ? $"已发送{actionText}指令。{result.GetDisplayText()}"
                    : $"{actionText}失败: {result.GetDisplayText()}";
                Color color = result.IsSuccess ? ColSuccess : ColDanger;

                MainThreadDispatcher.Enqueue(() =>
                {
                    triggerButton?.SetEnabled(true);
                    SetHeadsetSwitchStatus(status, color);
                });
            });
        }

        private void PullHeadsetLogcatchFolder(Button triggerButton)
        {
            var device = GetSelectedHeadsetSwitchDevice();
            if (device == null)
            {
                SetHeadsetSwitchStatus("请先选择目标设备。", ColDanger);
                return;
            }

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath))
            {
                SetHeadsetSwitchStatus("未找到当前用户桌面目录，无法拉取日志。", ColDanger);
                return;
            }

            string destinationPath = GetUniqueDesktopLogcatchPath(desktopPath);
            string tempRoot = Path.Combine(desktopPath, "logcatch_pull_tmp_" + Guid.NewGuid().ToString("N"));
            SetHeadsetSwitchStatus($"正在拉取 /sdcard/logcatch 到桌面...", ColWarning);
            triggerButton?.SetEnabled(false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string status;
                Color color;

                try
                {
                    Directory.CreateDirectory(tempRoot);
                    var pullResult = adb.Execute($"-s {device.Serial} pull /sdcard/logcatch \"{tempRoot}/\"", 300000);
                    string pulledPath = Path.Combine(tempRoot, "logcatch");

                    if (!pullResult.IsSuccess)
                    {
                        status = $"拉取 logcatch 失败: {pullResult.GetDisplayText()}";
                        color = ColDanger;
                    }
                    else if (!Directory.Exists(pulledPath))
                    {
                        status = $"拉取 logcatch 失败: ADB 已返回成功，但未找到临时目录 {pulledPath}";
                        color = ColDanger;
                    }
                    else
                    {
                        // ADB 对目录目标的语义会保留源目录名，先拉到临时目录再改名，避免多套一层 logcatch。
                        Directory.Move(pulledPath, destinationPath);
                        int renamedCount = RenamePulledLogcatchFiles(destinationPath);
                        status = $"已拉取 logcatch 到桌面，并重命名 {renamedCount} 个日志文件:\n{destinationPath}";
                        color = ColSuccess;
                    }
                }
                catch (Exception ex)
                {
                    status = $"拉取 logcatch 异常: {ex.Message}";
                    color = ColDanger;
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempRoot))
                            Directory.Delete(tempRoot, true);
                    }
                    catch { }
                }

                MainThreadDispatcher.Enqueue(() =>
                {
                    triggerButton?.SetEnabled(true);
                    SetHeadsetSwitchStatus(status, color);
                    if (color == ColSuccess && Directory.Exists(destinationPath))
                        FileDialogHelper.OpenInExplorer(destinationPath);
                });
            });
        }

        private static string GetUniqueDesktopLogcatchPath(string desktopPath)
        {
            string baseName = "logcatch_" + DateTime.Now.ToString("yyyyMMddHHmm");
            string candidate = Path.Combine(desktopPath, baseName);
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;

            for (int index = 2; ; index++)
            {
                candidate = Path.Combine(desktopPath, $"{baseName}_{index}");
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                    return candidate;
            }
        }

        private static int RenamePulledLogcatchFiles(string logcatchPath)
        {
            if (string.IsNullOrEmpty(logcatchPath) || !Directory.Exists(logcatchPath))
                return 0;

            var files = Directory.GetFiles(logcatchPath, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                return 0;

            var tempPaths = new List<string>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                string tempPath = Path.Combine(logcatchPath, $".logcat_rename_{Guid.NewGuid():N}.tmp");
                File.Move(files[i].FullName, tempPath);
                tempPaths.Add(tempPath);
            }

            for (int i = 0; i < tempPaths.Count; i++)
            {
                string finalPath = Path.Combine(logcatchPath, $"logcat{i + 1}.log");
                File.Move(tempPaths[i], finalPath);
            }

            return files.Count;
        }

        private void SendHeadsetShellCommand(string actionText, string shellCommand, Button triggerButton)
        {
            var device = GetSelectedHeadsetSwitchDevice();
            if (device == null)
            {
                SetHeadsetSwitchStatus("请先选择目标设备。", ColDanger);
                return;
            }

            SetHeadsetSwitchStatus($"正在{actionText}...", ColWarning);
            triggerButton?.SetEnabled(false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string command = $"-s {device.Serial} shell {shellCommand}";
                var result = adb.Execute(command, 12000);
                string status = result.IsSuccess
                    ? $"{actionText}完成。{result.GetDisplayText()}"
                    : $"{actionText}失败: {result.GetDisplayText()}";
                Color color = result.IsSuccess ? ColSuccess : ColDanger;

                MainThreadDispatcher.Enqueue(() =>
                {
                    triggerButton?.SetEnabled(true);
                    SetHeadsetSwitchStatus(status, color);
                });
            });
        }

        private void SetHeadsetDockVisible(bool visible)
        {
            var device = GetSelectedHeadsetSwitchDevice();
            if (device == null)
            {
                SetHeadsetSwitchStatus("请先选择目标设备。", ColDanger);
                return;
            }

            string actionText = visible ? "显示" : "隐藏";
            int visibleValue = visible ? 1 : 0;
            SetHeadsetSwitchStatus($"正在{actionText}导航栏...", ColWarning);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string command =
                    $"-s {device.Serial} shell am startservice -a com.pvr.tobservice.remoteservice -e act set_dock --ei visible {visibleValue}";
                var result = adb.Execute(command, 12000);
                string status = result.IsSuccess
                    ? $"已发送{actionText}导航栏指令。{result.GetDisplayText()}"
                    : $"{actionText}导航栏失败: {result.GetDisplayText()}";
                Color color = result.IsSuccess ? ColSuccess : ColDanger;

                MainThreadDispatcher.Enqueue(() => SetHeadsetSwitchStatus(status, color));
            });
        }

        private void RestartHeadsetWifiRadio(string serial)
        {
            adb.Execute($"-s {serial} shell cmd wifi set-wifi-enabled disabled", 8000);
            Thread.Sleep(2000);
            adb.Execute($"-s {serial} shell cmd wifi set-wifi-enabled enabled", 8000);
        }

        private HeadsetForgetSavedWifiResult ForgetAllHeadsetSavedWifiNetworks(string serial)
        {
            var listResult = adb.Execute($"-s {serial} shell cmd wifi list-networks", 12000);
            if (!listResult.IsSuccess)
            {
                return new HeadsetForgetSavedWifiResult
                {
                    Success = false,
                    Message = listResult.GetDisplayText()
                };
            }

            var networkIds = ParseHeadsetSavedWifiNetworkIds(listResult.Output);
            int forgottenCount = 0;
            foreach (string networkId in networkIds)
            {
                var forgetResult = adb.Execute($"-s {serial} shell cmd wifi forget-network {networkId}", 8000);
                if (!forgetResult.IsSuccess)
                {
                    return new HeadsetForgetSavedWifiResult
                    {
                        Success = false,
                        ForgottenCount = forgottenCount,
                        Message = $"forget-network {networkId}: {forgetResult.GetDisplayText()}"
                    };
                }

                forgottenCount++;
            }

            return new HeadsetForgetSavedWifiResult
            {
                Success = true,
                ForgottenCount = forgottenCount
            };
        }

        private static List<string> ParseHeadsetSavedWifiNetworkIds(string output)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(output))
                return ids;

            var seen = new HashSet<string>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"^\s*(\d+)\s+");
                if (match.Success && seen.Add(match.Groups[1].Value))
                    ids.Add(match.Groups[1].Value);
            }

            return ids;
        }

        private bool WaitForHeadsetWifiSsid(string serial, string targetSsid, int timeoutMs)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                var result = adb.Execute($"-s {serial} shell cmd wifi status", 8000);
                if (result.IsSuccess)
                {
                    string current = FirstRegex(result.Output, "(?im)^\\s*Wifi is connected to\\s+[\\\"']([^\\\"'\\r\\n]+)[\\\"']");
                    if (current == targetSsid)
                        return true;
                }

                Thread.Sleep(1500);
            }

            return false;
        }

        private string ResolveHeadsetSuggestionSecurityType(string serial, HeadsetSavedWifiNetwork network, string fallbackSecurityType)
        {
            var scanResult = adb.Execute($"-s {serial} shell cmd wifi list-scan-results", 10000);
            if (scanResult.IsSuccess)
            {
                string scannedSecurityType = ResolveHeadsetSuggestionSecurityTypeFromScan(scanResult.Output, network.Ssid);
                if (!string.IsNullOrEmpty(scannedSecurityType))
                    return scannedSecurityType;
            }

            return fallbackSecurityType;
        }

        private static string ResolveHeadsetSuggestionSecurityTypeFromScan(string scanOutput, string targetSsid)
        {
            if (string.IsNullOrEmpty(scanOutput) || string.IsNullOrEmpty(targetSsid))
                return "";

            var lines = scanOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"^\s*[0-9a-f]{2}(?::[0-9a-f]{2}){5}\s+\d+\s+\S+\s+\S+\s+(.+?)\s+(\[[^\r\n]+)$", RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                string ssid = NormalizeWifiValue(match.Groups[1].Value.Trim());
                if (!ssid.Equals(targetSsid, StringComparison.Ordinal))
                    continue;

                string flags = match.Groups[2].Value.ToUpperInvariant();
                bool hasSae = flags.Contains("SAE");
                bool hasPsk = flags.Contains("PSK");
                bool hasOwe = flags.Contains("OWE");

                if (hasSae && !hasPsk)
                    return "wpa3";
                if (hasPsk)
                    return "wpa2";
                if (hasOwe)
                    return "owe";
                return "open";
            }

            return "";
        }

        private static string ResolveHeadsetSuggestionSecurityType(string securityTypes)
        {
            if (string.IsNullOrEmpty(securityTypes))
                return "";

            string value = securityTypes.ToLowerInvariant();
            if (value.Contains("wpa2-psk") || value.Contains("wpa-psk"))
                return "wpa2";
            if (value.Contains("wpa3-sae"))
                return "wpa3";
            if (value.Contains("owe"))
                return "owe";
            if (value.Contains("open"))
                return "open";
            return "";
        }

        private AdbDeviceInfo GetSelectedHeadsetSwitchDevice()
        {
            string selected = headsetSwitchDeviceDropdown?.value;
            if (string.IsNullOrEmpty(selected))
                return null;

            return headsetSwitchDevices.FirstOrDefault(item => GetHeadsetDeviceDisplayName(item) == selected);
        }

        private HeadsetSavedWifiNetwork GetSelectedHeadsetSavedWifiNetwork()
        {
            string selected = headsetSavedWifiDropdown?.value;
            if (string.IsNullOrEmpty(selected))
                return null;

            return headsetSavedWifiNetworks.FirstOrDefault(item => GetHeadsetSavedWifiDisplayName(item) == selected);
        }

        private static string GetHeadsetDeviceDisplayName(AdbDeviceInfo device)
        {
            return string.IsNullOrEmpty(device.Model)
                ? device.Serial
                : $"{device.Model} ({device.Serial})";
        }

        private static string GetHeadsetSavedWifiDisplayName(HeadsetSavedWifiNetwork network)
        {
            return network.Ssid;
        }

        private void SetHeadsetSwitchStatus(string message, Color color)
        {
            if (headsetSwitchWifiStatusLabel == null)
                return;

            headsetSwitchWifiStatusLabel.text = message;
            headsetSwitchWifiStatusLabel.style.color = color;
        }

        private string HeadsetWifiPasswordStorePath =>
            Path.Combine(Application.persistentDataPath, "headset_wifi_passwords.json");

        private void LoadHeadsetWifiPasswordStore()
        {
            try
            {
                string path = HeadsetWifiPasswordStorePath;
                if (!File.Exists(path))
                {
                    headsetWifiPasswordStore = new HeadsetWifiPasswordStore();
                    return;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                headsetWifiPasswordStore = string.IsNullOrEmpty(json)
                    ? new HeadsetWifiPasswordStore()
                    : JsonUtility.FromJson<HeadsetWifiPasswordStore>(json) ?? new HeadsetWifiPasswordStore();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdbToolsApp] Load headset Wi-Fi passwords failed: {ex.Message}");
                headsetWifiPasswordStore = new HeadsetWifiPasswordStore();
            }
        }

        private void SaveHeadsetWifiPasswordStore()
        {
            try
            {
                string json = JsonUtility.ToJson(headsetWifiPasswordStore, true);
                File.WriteAllText(HeadsetWifiPasswordStorePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdbToolsApp] Save headset Wi-Fi passwords failed: {ex.Message}");
            }
        }

        private string GetRememberedHeadsetWifiPassword(string ssid)
        {
            if (string.IsNullOrEmpty(ssid) || headsetWifiPasswordStore?.entries == null)
                return "";

            var entry = headsetWifiPasswordStore.entries.FirstOrDefault(item => item.ssid == ssid);
            return entry?.password ?? "";
        }

        private void RememberHeadsetWifiPassword(string ssid, string password)
        {
            if (string.IsNullOrEmpty(ssid))
                return;

            if (headsetWifiPasswordStore == null)
                headsetWifiPasswordStore = new HeadsetWifiPasswordStore();
            if (headsetWifiPasswordStore.entries == null)
                headsetWifiPasswordStore.entries = new List<HeadsetWifiPasswordEntry>();

            var entry = headsetWifiPasswordStore.entries.FirstOrDefault(item => item.ssid == ssid);
            if (entry == null)
            {
                entry = new HeadsetWifiPasswordEntry { ssid = ssid };
                headsetWifiPasswordStore.entries.Add(entry);
            }

            entry.password = password;
            SaveHeadsetWifiPasswordStore();
        }

        private void ForgetHeadsetWifiPassword(string ssid)
        {
            if (string.IsNullOrEmpty(ssid) || headsetWifiPasswordStore?.entries == null)
                return;

            int removed = headsetWifiPasswordStore.entries.RemoveAll(item => item.ssid == ssid);
            if (removed > 0)
                SaveHeadsetWifiPasswordStore();
        }

        private HeadsetWifiInfo CollectHeadsetWifiInfo(AdbDeviceInfo device)
        {
            var info = new HeadsetWifiInfo
            {
                Serial = device.Serial,
                Model = device.Model,
                DeviceStatus = device.Status
            };

            // 多设备场景必须指定 -s，避免 ADB 默认设备不确定。
            string primaryArgs = $"-s {device.Serial} shell \"cmd wifi status; echo ===IPADDR===; ip addr show wlan0; echo ===ROUTE===; ip route\"";
            var primaryResult = adb.Execute(primaryArgs, 12000);
            string output = (primaryResult.Output ?? "") + "\n" + (primaryResult.Error ?? "");

            ParseHeadsetWifiOutput(output, info);

            AdbResult fallbackResult = null;
            if (string.IsNullOrEmpty(info.Ssid) || string.IsNullOrEmpty(info.Bssid) || string.IsNullOrEmpty(info.Rssi))
            {
                // dumpsys wifi 可能很大，这里只保留当前连接相关行，降低设备 ADB 会话压力。
                string fallbackArgs = $"-s {device.Serial} shell \"dumpsys wifi | grep -i -e mWifiInfo -e WifiInfo -e mLastBssid -e SSID -e BSSID -e RSSI -e Frequency -e 'Link speed' -e MAC -e 'Net ID' -e 'Supplicant state'\"";
                fallbackResult = adb.Execute(fallbackArgs, 12000);
                output = (fallbackResult.Output ?? "") + "\n" + (fallbackResult.Error ?? "");
                ParseHeadsetWifiOutput(output, info);
            }

            bool hasWifiData = HasHeadsetWifiData(info);
            bool commandSuccess = primaryResult.IsSuccess || (fallbackResult != null && fallbackResult.IsSuccess);
            info.QuerySuccess = commandSuccess && hasWifiData;
            if (!info.QuerySuccess)
            {
                info.ErrorMessage = hasWifiData
                    ? (fallbackResult ?? primaryResult).GetDisplayText()
                    : "未解析到 Wi-Fi 连接信息，可能设备未连接 Wi-Fi 或当前系统限制了相关输出。";
            }

            return info;
        }

        private static List<AdbDeviceInfo> ParseAdbDevices(string output)
        {
            var devices = new List<AdbDeviceInfo>();
            if (string.IsNullOrEmpty(output))
                return devices;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("List of")) continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var device = new AdbDeviceInfo
                {
                    Serial = parts[0],
                    Status = parts[1],
                    Description = line
                };

                for (int i = 2; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith("model:"))
                        device.Model = parts[i].Substring(6);
                }

                devices.Add(device);
            }

            return devices;
        }

        private static string GetAdbDeviceStatusMessage(string status)
        {
            switch (status)
            {
                case "offline": return "设备离线，请重新插拔或重连 ADB。";
                case "unauthorized": return "设备未授权，请在头显内确认 USB 调试授权。";
                case "no permissions": return "当前环境没有设备访问权限。";
                default: return $"设备状态不可用: {status}";
            }
        }

        private static void ParseHeadsetWifiOutput(string output, HeadsetWifiInfo info)
        {
            if (string.IsNullOrEmpty(output))
                return;

            string currentWifiInfo = FindHeadsetCurrentWifiInfo(output);
            string primarySource = string.IsNullOrEmpty(currentWifiInfo) ? output : currentWifiInfo;

            string connectedState = FirstRegex(output, @"(?im)^\s*(Wifi is connected to [^\r\n]+)");
            string wifiState = NormalizeWifiState(connectedState);
            if (string.IsNullOrEmpty(wifiState))
                wifiState = NormalizeWifiState(FirstRegex(output, @"(?im)^\s*(Wi-?Fi is [^\r\n]+)"));
            if (string.IsNullOrEmpty(wifiState))
                wifiState = NormalizeWifiState(FirstRegex(output, @"(?im)^\s*(Wifi is [^\r\n]+)"));
            if (!string.IsNullOrEmpty(wifiState))
                info.WifiState = wifiState;

            string ssid = FirstRegex(output, "(?im)^\\s*Wifi is connected to\\s+[\\\"']([^\\\"'\\r\\n]+)[\\\"']");
            if (string.IsNullOrEmpty(ssid))
                ssid = FirstRegex(output, "(?i)current SSID\\(s\\):\\{[^}]*ssid=[\\\"']([^\\\"'\\r\\n]+)[\\\"']");
            if (string.IsNullOrEmpty(ssid))
                ssid = FirstRegex(primarySource, "(?i)\\bSSID:\\s*\\\"?([^\\\",\\r\\n]+)\\\"?");
            ssid = NormalizeWifiValue(ssid);
            if (!string.IsNullOrEmpty(ssid))
                info.Ssid = ssid;

            string bssid = NormalizeWifiValue(FirstRegex(output, "(?im)^\\s*mLastBssid\\s+([0-9a-f]{2}(?::[0-9a-f]{2}){5})"));
            if (string.IsNullOrEmpty(bssid))
                bssid = NormalizeWifiValue(FirstRegex(primarySource, "(?i)\\bBSSID:\\s*([0-9a-f]{2}(?::[0-9a-f]{2}){5})"));
            if (!string.IsNullOrEmpty(bssid))
                info.Bssid = bssid;

            string macAddress = NormalizeWifiValue(FirstRegex(output, "(?i)\\blink/ether\\s+([0-9a-f]{2}(?::[0-9a-f]{2}){5})"));
            if (string.IsNullOrEmpty(macAddress))
                macAddress = NormalizeWifiValue(FirstRegex(primarySource, "(?i)\\bMAC(?: Address)?:\\s*([0-9a-f]{2}(?::[0-9a-f]{2}){5})"));
            if (!string.IsNullOrEmpty(macAddress))
                info.MacAddress = macAddress;

            string ipAddress = NormalizeWifiValue(FirstRegex(primarySource, "(?i)\\bIP:\\s*/?(\\d{1,3}(?:\\.\\d{1,3}){3})"));
            if (string.IsNullOrEmpty(ipAddress))
                ipAddress = NormalizeWifiValue(FirstRegex(output, "\\binet\\s+(\\d{1,3}(?:\\.\\d{1,3}){3})/"));
            if (string.IsNullOrEmpty(ipAddress))
                ipAddress = NormalizeWifiValue(FirstRegex(output, "\\bsrc\\s+(\\d{1,3}(?:\\.\\d{1,3}){3})\\b"));
            if (!string.IsNullOrEmpty(ipAddress))
                info.IpAddress = ipAddress;

            string rssi = NormalizeWifiValue(FirstRegex(primarySource, "(?i)\\bRSSI:\\s*(-?\\d+)"));
            if (!string.IsNullOrEmpty(rssi) && !rssi.EndsWith("dBm"))
                rssi += " dBm";
            if (!string.IsNullOrEmpty(rssi))
                info.Rssi = rssi;

            string frequency = NormalizeWifiValue(FirstRegex(primarySource, "(?i)\\bFrequency:\\s*(\\d+)\\s*MHz"));
            if (!string.IsNullOrEmpty(frequency))
                frequency += " MHz";
            if (!string.IsNullOrEmpty(frequency))
                info.Frequency = frequency;

            string linkSpeed = NormalizeWifiValue(FirstRegex(primarySource, "(?i)\\bLink speed:\\s*([^,\\r\\n]+)"));
            if (!string.IsNullOrEmpty(linkSpeed))
                info.LinkSpeed = linkSpeed;

            string networkId = NormalizeWifiValue(FirstRegex(primarySource, "(?i)\\b(?:Net ID|Network ID|networkId):\\s*(-?\\d+)"));
            if (!string.IsNullOrEmpty(networkId))
                info.NetworkId = networkId;

            string supplicantState = NormalizeWifiValue(FirstRegex(primarySource, "(?i)\\bSupplicant state:\\s*([^,\\r\\n]+)"));
            if (!string.IsNullOrEmpty(supplicantState))
                info.SupplicantState = supplicantState;

            info.WifiState = BuildWifiStatusText(info.Rssi, info.SupplicantState, info.Ssid);
        }

        private static string FindHeadsetCurrentWifiInfo(string output)
        {
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.IndexOf("mWifiInfo", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("WifiInfo", StringComparison.OrdinalIgnoreCase) >= 0)
                    return line;
            }

            foreach (var line in lines)
            {
                if (line.IndexOf("SSID:", StringComparison.OrdinalIgnoreCase) >= 0
                    && line.IndexOf("BSSID:", StringComparison.OrdinalIgnoreCase) >= 0
                    && line.IndexOf("RSSI:", StringComparison.OrdinalIgnoreCase) >= 0)
                    return line;
            }

            return "";
        }

        private static bool HasHeadsetWifiData(HeadsetWifiInfo info)
        {
            return !string.IsNullOrEmpty(info.WifiState)
                || !string.IsNullOrEmpty(info.Ssid)
                || !string.IsNullOrEmpty(info.Bssid)
                || !string.IsNullOrEmpty(info.IpAddress)
                || !string.IsNullOrEmpty(info.MacAddress);
        }

        private static string FirstRegex(string input, string pattern)
        {
            var match = Regex.Match(input, pattern);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : "";
        }

        private static string NormalizeWifiValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value.Trim().Trim('"');
            if (value.Equals("<unknown ssid>", StringComparison.OrdinalIgnoreCase)
                || value.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                || value.Equals("null", StringComparison.OrdinalIgnoreCase)
                || value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                return "";

            return value;
        }

        private static string NormalizeWifiState(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value.Trim();
            var match = Regex.Match(value, "(?i)^Wifi is connected to\\s+[\\\"']([^\\\"'\\r\\n]+)[\\\"']");
            if (match.Success)
                return $"已连接到 {match.Groups[1].Value.Trim()}";

            if (value.Equals("Wifi is enabled", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Wi-Fi is enabled", StringComparison.OrdinalIgnoreCase))
                return "Wi-Fi 已启用";

            if (value.Equals("Wifi is disabled", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Wi-Fi is disabled", StringComparison.OrdinalIgnoreCase))
                return "Wi-Fi 已关闭";

            return value;
        }

        private static string BuildWifiStatusText(string rssi, string supplicantState, string ssid)
        {
            if (!string.IsNullOrEmpty(rssi))
                return $"Signal {rssi}";

            bool connected = supplicantState.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(ssid);
            return connected ? "Connect" : "DisConnect";
        }

        private VisualElement BuildHeadsetWifiCard(HeadsetWifiInfo info)
        {
            var card = new VisualElement();
            card.style.marginBottom = 10;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            card.style.paddingLeft = 14;
            card.style.paddingRight = 14;
            card.style.backgroundColor = new Color(0.08f, 0.10f, 0.18f);
            SetBorderRadius(card, 6);
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = info.QuerySuccess ? ColAccent : ColBorder;
            card.style.borderBottomColor = ColBorder;
            card.style.borderLeftColor = info.QuerySuccess ? ColAccent : ColBorder;
            card.style.borderRightColor = ColBorder;

            var header = Row();
            header.style.marginBottom = 10;

            var title = new Label(string.IsNullOrEmpty(info.Model) ? info.Serial : $"{info.Model}  ({info.Serial})");
            title.style.flexGrow = 1;
            title.style.fontSize = 14;
            title.style.color = ColText;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            var badge = new Label(info.QuerySuccess ? "Wi-Fi 已获取" : info.DeviceStatus);
            badge.style.fontSize = 11;
            badge.style.color = info.QuerySuccess ? ColSuccess : ColWarning;
            badge.style.backgroundColor = info.QuerySuccess
                ? new Color(0.08f, 0.20f, 0.10f)
                : new Color(0.22f, 0.16f, 0.05f);
            badge.style.paddingLeft = 8;
            badge.style.paddingRight = 8;
            badge.style.paddingTop = 3;
            badge.style.paddingBottom = 3;
            SetBorderRadius(badge, 5);
            header.Add(badge);
            card.Add(header);

            card.Add(BuildHeadsetInfoGrid(
                ("SSID", info.Ssid),
                ("IP", info.IpAddress),
                ("BSSID", info.Bssid),
                ("MAC", info.MacAddress),
                ("RSSI", info.Rssi),
                ("频段", info.Frequency),
                ("链路速率", info.LinkSpeed),
                ("网络 ID", info.NetworkId),
                ("Wi-Fi 状态", info.WifiState),
                ("连接状态", info.SupplicantState)
            ));

            if (!info.QuerySuccess && !string.IsNullOrEmpty(info.ErrorMessage))
            {
                var error = new Label(info.ErrorMessage);
                error.style.marginTop = 10;
                error.style.fontSize = 12;
                error.style.color = ColDanger;
                error.style.whiteSpace = WhiteSpace.Normal;
                card.Add(error);
            }

            return card;
        }

        private VisualElement BuildHeadsetInfoGrid(params (string label, string value)[] items)
        {
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;

            foreach (var item in items)
            {
                var cell = new VisualElement();
                cell.style.width = Length.Percent(50);
                cell.style.minWidth = 260;
                cell.style.flexDirection = FlexDirection.Row;
                cell.style.alignItems = Align.Center;
                cell.style.marginBottom = 8;
                cell.Add(BuildHeadsetInfoLabel(item.label));
                cell.Add(BuildHeadsetInfoValue(item.value));
                grid.Add(cell);
            }

            return grid;
        }

        private Label BuildHeadsetInfoLabel(string text)
        {
            var label = new Label(text);
            label.style.width = 72;
            label.style.flexShrink = 0;
            label.style.fontSize = 11;
            label.style.color = ColTextDim;
            return label;
        }

        private Label BuildHeadsetInfoValue(string text)
        {
            bool isEmpty = string.IsNullOrEmpty(text);
            var label = new Label(isEmpty ? "未获取" : text);
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            label.style.fontSize = 12;
            label.style.color = GetHeadsetInfoValueColor(text, isEmpty);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private Color GetHeadsetInfoValueColor(string text, bool isEmpty)
        {
            if (isEmpty)
                return ColTextDim;

            if (text.Equals("Connect", StringComparison.OrdinalIgnoreCase))
                return ColSuccess;

            if (text.Equals("DisConnect", StringComparison.OrdinalIgnoreCase))
                return ColDanger;

            var signalMatch = Regex.Match(text, @"(-?\d+)\s*dBm", RegexOptions.IgnoreCase);
            if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out int rssi))
            {
                if (rssi >= -60) return ColSuccess;
                if (rssi >= -75) return ColWarning;
                return ColDanger;
            }

            return ColText;
        }

        private void AddHeadsetEmptyState(string message, Color color)
        {
            var label = new Label(message);
            label.style.fontSize = 13;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.paddingTop = 18;
            label.style.paddingBottom = 18;
            label.style.paddingLeft = 18;
            label.style.paddingRight = 18;
            headsetWifiContainer.Add(label);
        }

        private VisualElement BuildFileManagerPanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;

            panel.Add(SectionTitle("文件管理"));
            panel.Add(SectionDesc("浏览设备文件系统，推送/拉取/删除文件"));

            // ── Path bar row ──
            var pathRow = Row();
            fileManagerPathField = StyledTextField("设备路径", "/sdcard");
            fileManagerPathField.value = fileManagerCurrentPath;
            fileManagerPathField.style.flexGrow = 1;
            fileManagerPathField.style.flexShrink = 1;
            fileManagerPathField.style.overflow = Overflow.Hidden;
            fileManagerPathField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    NavigateToPath(fileManagerPathField.value.Trim());
            });
            pathRow.Add(fileManagerPathField);

            fileManagerUpBtn = ActionButton("..", ColNeutral);
            fileManagerUpBtn.style.width = 40;
            fileManagerUpBtn.style.paddingLeft = 8;
            fileManagerUpBtn.style.paddingRight = 8;
            fileManagerUpBtn.tooltip = "返回上级目录";
            fileManagerUpBtn.clicked += OnFileManagerNavigateUp;
            pathRow.Add(fileManagerUpBtn);

            fileManagerRefreshBtn = ActionButton("刷新", ColAccent);
            fileManagerRefreshBtn.style.width = 60;
            fileManagerRefreshBtn.style.marginRight = 0;
            fileManagerRefreshBtn.clicked += () => NavigateToPath(fileManagerCurrentPath);
            pathRow.Add(fileManagerRefreshBtn);
            panel.Add(pathRow);

            // ── Status label ──
            fileManagerStatusLabel = new Label("输入路径并回车，或点击刷新浏览设备文件");
            fileManagerStatusLabel.style.fontSize = 11;
            fileManagerStatusLabel.style.color = ColTextDim;
            fileManagerStatusLabel.style.marginTop = 6;
            fileManagerStatusLabel.style.marginBottom = 4;
            panel.Add(fileManagerStatusLabel);

            // ── File list ScrollView ──
            fileManagerListScroll = new ScrollView(ScrollViewMode.Vertical);
            fileManagerListScroll.style.flexGrow = 1;
            fileManagerListScroll.style.flexShrink = 1;
            fileManagerListScroll.style.minHeight = 200;
            fileManagerListScroll.style.backgroundColor = ColBgOutput;
            SetBorderRadius(fileManagerListScroll, 6);
            fileManagerListScroll.style.borderTopWidth = 1;
            fileManagerListScroll.style.borderBottomWidth = 1;
            fileManagerListScroll.style.borderLeftWidth = 1;
            fileManagerListScroll.style.borderRightWidth = 1;
            fileManagerListScroll.style.borderTopColor = ColBorder;
            fileManagerListScroll.style.borderBottomColor = ColBorder;
            fileManagerListScroll.style.borderLeftColor = ColBorder;
            fileManagerListScroll.style.borderRightColor = ColBorder;

            fileManagerListContainer = new VisualElement();
            fileManagerListScroll.Add(fileManagerListContainer);
            panel.Add(fileManagerListScroll);

            // ── Action buttons row ──
            var actionRow = Row();
            actionRow.style.marginTop = 10;

            fileManagerPushBtn = ActionButton("推送文件", ColSuccess);
            fileManagerPushBtn.clicked += OnFileManagerPush;
            actionRow.Add(fileManagerPushBtn);

            fileManagerPullBtn = ActionButton("拉取文件", ColAccent);
            fileManagerPullBtn.clicked += OnFileManagerPull;
            actionRow.Add(fileManagerPullBtn);

            fileManagerMkdirBtn = ActionButton("新建文件夹", ColWarning);
            fileManagerMkdirBtn.clicked += OnFileManagerMkdir;
            actionRow.Add(fileManagerMkdirBtn);

            fileManagerDeleteBtn = ActionButton("删除", ColDanger);
            fileManagerDeleteBtn.style.marginRight = 0;
            fileManagerDeleteBtn.clicked += OnFileManagerDelete;
            actionRow.Add(fileManagerDeleteBtn);

            panel.Add(actionRow);

            // ── Local path row ──
            var localRow = Row();
            localRow.style.marginTop = 10;

            fileManagerLocalPathField = StyledTextField("本地路径", "选择本地文件或文件夹...");
            fileManagerLocalPathField.style.flexGrow = 1;
            fileManagerLocalPathField.style.flexShrink = 1;
            fileManagerLocalPathField.style.overflow = Overflow.Hidden;
            localRow.Add(fileManagerLocalPathField);

            var browseFileBtn = ActionButton("选择文件", ColAccent);
            browseFileBtn.style.width = 90;
            browseFileBtn.clicked += () =>
            {
                var files = Win32Dialog.OpenFileDialog("选择要推送的文件", "All Files\0*.*\0", false);
                if (files != null && files.Length > 0)
                    fileManagerLocalPathField.value = files[0];
            };
            localRow.Add(browseFileBtn);

            var browseFolderBtn = ActionButton("选择文件夹", ColAccent);
            browseFolderBtn.style.width = 90;
            browseFolderBtn.style.marginRight = 0;
            browseFolderBtn.clicked += () =>
            {
                string folder = Win32Dialog.OpenFolderDialog("选择拉取目标文件夹");
                if (!string.IsNullOrEmpty(folder))
                    fileManagerLocalPathField.value = folder;
            };
            localRow.Add(browseFolderBtn);

            panel.Add(localRow);

            // ── Output area ──
            panel.Add(OutputArea(out fileManagerOutput));

            return panel;
        }

        // ── Navigation ──

        private void NavigateToPath(string path)
        {
            if (string.IsNullOrEmpty(path)) path = "/";
            path = path.TrimEnd('/');
            if (string.IsNullOrEmpty(path)) path = "/";

            if (fileManagerLoading) return;
            fileManagerLoading = true;

            fileManagerRefreshBtn.SetEnabled(false);
            fileManagerStatusLabel.text = "正在加载...";
            fileManagerStatusLabel.style.color = ColWarning;
            fileManagerListContainer.Clear();

            string targetPath = path;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = adb.Execute($"shell ls -la {ShellQuote(targetPath + "/")}", 15000);
                MainThreadDispatcher.Enqueue(() =>
                {
                    fileManagerLoading = false;
                    fileManagerRefreshBtn.SetEnabled(true);

                    if (!result.IsSuccess && !result.HasOutput)
                    {
                        fileManagerStatusLabel.text = "加载失败";
                        fileManagerStatusLabel.style.color = ColDanger;
                        SetOutput(fileManagerOutput, result.GetDisplayText());
                        return;
                    }

                    fileManagerCurrentPath = targetPath;
                    fileManagerPathField.value = fileManagerCurrentPath;
                    ParseAndPopulateFileList(result.Output);
                });
            });
        }

        private void OnFileManagerNavigateUp()
        {
            if (fileManagerCurrentPath == "/") return;
            int lastSlash = fileManagerCurrentPath.LastIndexOf('/');
            string parentPath = lastSlash <= 0 ? "/" : fileManagerCurrentPath.Substring(0, lastSlash);
            NavigateToPath(parentPath);
        }

        // ── File list parsing ──

        private void ParseAndPopulateFileList(string lsOutput)
        {
            fileManagerListContainer.Clear();
            fileManagerEntries.Clear();
            _selectedFileRow = null;
            _selectedFileEntry = null;

            if (string.IsNullOrEmpty(lsOutput))
            {
                fileManagerStatusLabel.text = "空目录";
                fileManagerStatusLabel.style.color = ColTextDim;
                return;
            }

            // Add parent directory row
            if (fileManagerCurrentPath != "/")
                AddParentDirectoryRow();

            var lines = lsOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("total ", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(line)) continue;

                var entry = ParseLsLine(line);
                if (entry == null) continue;
                if (entry.Name == "." || entry.Name == "..") continue;

                fileManagerEntries.Add(entry);
            }

            // Sort: directories first, then alphabetical
            fileManagerEntries.Sort((a, b) =>
            {
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var entry in fileManagerEntries)
            {
                var row = BuildFileEntryRow(entry);
                entry.Row = row;
                fileManagerListContainer.Add(row);
            }

            fileManagerStatusLabel.text = $"{fileManagerEntries.Count} 个项目";
            fileManagerStatusLabel.style.color = ColSuccess;
        }

        private static DeviceFileEntry ParseLsLine(string line)
        {
            if (line.Length < 10) return null;

            char typeChar = line[0];
            if (typeChar != 'd' && typeChar != '-' && typeChar != 'l' &&
                typeChar != 'c' && typeChar != 'b' && typeChar != 'p' && typeChar != 's')
                return null;

            var match = LsLineRegex.Match(line);
            if (!match.Success)
                match = LsLineRegexAlt.Match(line);
            if (!match.Success) return null;

            string permissions = match.Groups[1].Value;
            string owner = match.Groups[3].Value;
            string group = match.Groups[4].Value;
            long.TryParse(match.Groups[5].Value, out long size);
            string dateStr = match.Groups[6].Value;
            string name = match.Groups[7].Value.Trim();

            bool isDir = permissions[0] == 'd';
            bool isSymlink = permissions[0] == 'l';

            string linkTarget = null;
            if (isSymlink && name.Contains(" -> "))
            {
                int arrowIdx = name.IndexOf(" -> ");
                linkTarget = name.Substring(arrowIdx + 4);
                name = name.Substring(0, arrowIdx);
            }

            return new DeviceFileEntry
            {
                Name = name,
                IsDirectory = isDir,
                IsSymlink = isSymlink,
                Permissions = permissions,
                Owner = owner,
                Group = group,
                Size = size,
                DateString = dateStr,
                LinkTarget = linkTarget
            };
        }

        // ── UI row builders ──

        private VisualElement BuildFileEntryRow(DeviceFileEntry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 28;
            row.style.paddingLeft = 12;
            row.style.paddingRight = 12;
            row.style.backgroundColor = Color.clear;

            var icon = new Label(entry.IsDirectory ? "[DIR]" : (entry.IsSymlink ? "[LNK]" : "[FILE]"));
            icon.style.width = 48;
            icon.style.fontSize = 10;
            icon.style.color = entry.IsDirectory ? ColAccent : (entry.IsSymlink ? ColWarning : ColTextDim);
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(icon);

            string displayName = entry.Name;
            if (entry.IsSymlink && !string.IsNullOrEmpty(entry.LinkTarget))
                displayName += $" -> {entry.LinkTarget}";

            var nameLabel = new Label(displayName);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.fontSize = 12;
            nameLabel.style.color = ColText;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(nameLabel);

            var sizeLabel = new Label(entry.IsDirectory ? "--" : FormatFileSize(entry.Size));
            sizeLabel.style.width = 80;
            sizeLabel.style.fontSize = 11;
            sizeLabel.style.color = ColTextDim;
            sizeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(sizeLabel);

            var dateLabel = new Label(entry.DateString);
            dateLabel.style.width = 130;
            dateLabel.style.fontSize = 11;
            dateLabel.style.color = ColTextDim;
            dateLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            dateLabel.style.marginLeft = 8;
            row.Add(dateLabel);

            DeviceFileEntry capturedEntry = entry;
            VisualElement capturedRow = row;

            row.RegisterCallback<ClickEvent>(e =>
            {
                if (e.clickCount == 2 && capturedEntry.IsDirectory)
                {
                    string newPath = fileManagerCurrentPath == "/"
                        ? "/" + capturedEntry.Name
                        : fileManagerCurrentPath + "/" + capturedEntry.Name;
                    NavigateToPath(newPath);
                    return;
                }

                if (_selectedFileRow != null)
                    _selectedFileRow.style.backgroundColor = Color.clear;
                capturedRow.style.backgroundColor = ColNavActive;
                _selectedFileRow = capturedRow;
                _selectedFileEntry = capturedEntry;
            });

            row.RegisterCallback<MouseEnterEvent>(e =>
            {
                if (capturedRow != _selectedFileRow)
                    capturedRow.style.backgroundColor = ColNavHover;
            });
            row.RegisterCallback<MouseLeaveEvent>(e =>
            {
                if (capturedRow != _selectedFileRow)
                    capturedRow.style.backgroundColor = Color.clear;
            });

            return row;
        }

        private void AddParentDirectoryRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 28;
            row.style.paddingLeft = 12;
            row.style.paddingRight = 12;
            row.style.backgroundColor = Color.clear;

            var icon = new Label("[..]");
            icon.style.width = 48;
            icon.style.fontSize = 10;
            icon.style.color = ColWarning;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(icon);

            var nameLabel = new Label(".. (返回上级)");
            nameLabel.style.flexGrow = 1;
            nameLabel.style.fontSize = 12;
            nameLabel.style.color = ColTextDim;
            row.Add(nameLabel);

            row.RegisterCallback<ClickEvent>(e => OnFileManagerNavigateUp());
            row.RegisterCallback<MouseEnterEvent>(e => row.style.backgroundColor = ColNavHover);
            row.RegisterCallback<MouseLeaveEvent>(e => row.style.backgroundColor = Color.clear);

            fileManagerListContainer.Add(row);
        }

        // ── File operations ──

        private void OnFileManagerPush()
        {
            string localPath = fileManagerLocalPathField.value.Trim();

            if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
            {
                SetOutput(fileManagerOutput, "[错误] 请先在下方选择要推送的本地文件");
                return;
            }

            string devicePath = fileManagerCurrentPath;
            string fileName = Path.GetFileName(localPath);

            SetOutput(fileManagerOutput, $"正在推送 {fileName} 到 {devicePath}/ ...");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = adb.Execute($"push \"{localPath}\" \"{devicePath}/\"", 300000);
                MainThreadDispatcher.Enqueue(() =>
                {
                    SetOutput(fileManagerOutput, result.GetDisplayText());
                    if (result.IsSuccess)
                        NavigateToPath(fileManagerCurrentPath);
                });
            });
        }

        private void OnFileManagerPull()
        {
            if (_selectedFileEntry == null)
            {
                SetOutput(fileManagerOutput, "[错误] 请先在文件列表中选择要拉取的文件");
                return;
            }

            string localPath = fileManagerLocalPathField.value.Trim();

            if (string.IsNullOrEmpty(localPath) || !Directory.Exists(localPath))
            {
                string folder = Win32Dialog.OpenFolderDialog("选择拉取目标文件夹");
                if (string.IsNullOrEmpty(folder)) return;
                localPath = folder;
                fileManagerLocalPathField.value = localPath;
            }

            string deviceFilePath = fileManagerCurrentPath == "/"
                ? "/" + _selectedFileEntry.Name
                : fileManagerCurrentPath + "/" + _selectedFileEntry.Name;
            string entryName = _selectedFileEntry.Name;

            SetOutput(fileManagerOutput, $"正在拉取 {entryName} 到 {localPath} ...");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = adb.Execute($"pull \"{deviceFilePath}\" \"{localPath}/\"", 300000);
                MainThreadDispatcher.Enqueue(() =>
                {
                    SetOutput(fileManagerOutput, result.GetDisplayText());
                    if (result.IsSuccess)
                    {
                        string pulledPath = Path.Combine(localPath, entryName);
                        if (File.Exists(pulledPath) || Directory.Exists(pulledPath))
                            FileDialogHelper.OpenInExplorer(pulledPath);
                    }
                });
            });
        }

        private void OnFileManagerMkdir()
        {
            var inputRow = new VisualElement();
            inputRow.style.flexDirection = FlexDirection.Row;
            inputRow.style.alignItems = Align.Center;
            inputRow.style.height = 34;
            inputRow.style.paddingLeft = 12;
            inputRow.style.paddingRight = 12;
            inputRow.style.backgroundColor = new Color(0.12f, 0.14f, 0.24f);

            var inputField = StyledTextField("文件夹名称", "NewFolder");
            inputField.style.flexGrow = 1;
            inputField.style.flexShrink = 1;
            inputRow.Add(inputField);

            var confirmBtn = ActionButton("创建", ColSuccess);
            confirmBtn.style.width = 60;
            confirmBtn.style.height = 28;
            confirmBtn.clicked += () =>
            {
                string folderName = inputField.value.Trim();
                if (string.IsNullOrEmpty(folderName))
                {
                    SetOutput(fileManagerOutput, "[错误] 文件夹名称不能为空");
                    return;
                }

                string fullPath = fileManagerCurrentPath == "/"
                    ? "/" + folderName
                    : fileManagerCurrentPath + "/" + folderName;

                fileManagerListContainer.Remove(inputRow);
                SetOutput(fileManagerOutput, $"正在创建文件夹 {folderName}...");

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var result = adb.Execute($"shell mkdir -p {ShellQuote(fullPath)}");
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        SetOutput(fileManagerOutput, result.IsSuccess
                            ? $"文件夹已创建: {fullPath}"
                            : result.GetDisplayText());
                        if (result.IsSuccess)
                            NavigateToPath(fileManagerCurrentPath);
                    });
                });
            };
            inputRow.Add(confirmBtn);

            var cancelBtn = ActionButton("取消", ColNeutral);
            cancelBtn.style.width = 60;
            cancelBtn.style.height = 28;
            cancelBtn.style.marginRight = 0;
            cancelBtn.clicked += () => fileManagerListContainer.Remove(inputRow);
            inputRow.Add(cancelBtn);

            fileManagerListContainer.Insert(0, inputRow);
            inputField.Focus();
        }

        private void OnFileManagerDelete()
        {
            if (_selectedFileEntry == null)
            {
                SetOutput(fileManagerOutput, "[错误] 请先在文件列表中选择要删除的文件或文件夹");
                return;
            }

            string targetName = _selectedFileEntry.Name;
            string fullPath = fileManagerCurrentPath == "/"
                ? "/" + targetName
                : fileManagerCurrentPath + "/" + targetName;
            bool isDir = _selectedFileEntry.IsDirectory;
            string typeDesc = isDir ? "文件夹" : "文件";

            var confirmRow = new VisualElement();
            confirmRow.style.flexDirection = FlexDirection.Row;
            confirmRow.style.alignItems = Align.Center;
            confirmRow.style.height = 34;
            confirmRow.style.paddingLeft = 12;
            confirmRow.style.paddingRight = 12;
            confirmRow.style.backgroundColor = new Color(0.25f, 0.08f, 0.08f);

            var warningLabel = new Label($"确认删除{typeDesc}: {targetName} ?");
            warningLabel.style.flexGrow = 1;
            warningLabel.style.color = ColDanger;
            warningLabel.style.fontSize = 12;
            warningLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            confirmRow.Add(warningLabel);

            var yesBtn = ActionButton("确认删除", ColDanger);
            yesBtn.style.height = 28;
            yesBtn.clicked += () =>
            {
                fileManagerListContainer.Remove(confirmRow);
                SetOutput(fileManagerOutput, $"正在删除 {targetName}...");

                string deleteCmd = isDir
                    ? $"shell rm -rf {ShellQuote(fullPath)}"
                    : $"shell rm -f {ShellQuote(fullPath)}";

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var result = adb.Execute(deleteCmd);
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        SetOutput(fileManagerOutput, result.IsSuccess
                            ? $"已删除: {fullPath}"
                            : result.GetDisplayText());
                        if (result.IsSuccess)
                        {
                            _selectedFileEntry = null;
                            _selectedFileRow = null;
                            NavigateToPath(fileManagerCurrentPath);
                        }
                    });
                });
            };
            confirmRow.Add(yesBtn);

            var noBtn = ActionButton("取消", ColNeutral);
            noBtn.style.height = 28;
            noBtn.style.marginRight = 0;
            noBtn.clicked += () => fileManagerListContainer.Remove(confirmRow);
            confirmRow.Add(noBtn);

            fileManagerListContainer.Insert(0, confirmRow);
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 0) return "N/A";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        #endregion

        #region ADB Command Execution

        /// <summary>
        /// Run an ADB command on a background thread and display the result in the given label.
        /// </summary>
        private void RunAdbAsync(string arguments, Label output, string loadingText = "")
        {
            SetOutput(output, loadingText);

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = adb.Execute(arguments);
                MainThreadDispatcher.Enqueue(() =>
                {
                    SetOutput(output, result.GetDisplayText());
                });
            });
        }

        private void SetOutput(Label output, string text)
        {
            if (output != null)
                output.text = text ?? "";
        }

        #endregion

        #region Log Saving

        private void SaveLogToFile(string content, string prefix = "log")
        {
            if (string.IsNullOrEmpty(content))
            {
                Debug.Log("[AdbToolsApp] No content to save");
                return;
            }

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{prefix}_{timestamp}.txt";
                string filePath = Path.Combine(logSavePath, fileName);

                var sb = new StringBuilder();
                sb.AppendLine("=".PadRight(60, '='));
                sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"工具: SoraTools ADB Command Center");
                sb.AppendLine("=".PadRight(60, '='));
                sb.AppendLine();
                sb.Append(content);

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                Debug.Log($"[AdbToolsApp] Log saved: {filePath}");

                FileDialogHelper.OpenInExplorer(filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AdbToolsApp] Save failed: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Escape a path for use inside adb shell single-quoted strings.
        /// Wraps path in single quotes with proper escaping of embedded single quotes.
        /// </summary>
        private static string ShellQuote(string path) => "'" + path.Replace("'", "'\\''") + "'";

        #region UI Component Helpers

        private Label SectionTitle(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 20;
            label.style.color = ColText;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 2;
            return label;
        }

        private Label SectionDesc(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 12;
            label.style.color = ColTextDim;
            label.style.marginBottom = 14;
            return label;
        }

        private VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexShrink = 0;
            return row;
        }

        private VisualElement Spacer()
        {
            var s = new VisualElement();
            s.style.flexGrow = 1;
            return s;
        }

        private Button ActionButton(string text, Color bgColor)
        {
            var btn = new Button();
            btn.text = text;
            btn.style.height = 34;
            btn.style.paddingLeft = 16;
            btn.style.paddingRight = 16;
            btn.style.marginRight = 8;
            btn.style.backgroundColor = bgColor;
            btn.style.color = ColText;
            SetBorderRadius(btn, 6);
            SetBorderWidth(btn, 0);
            btn.style.fontSize = 13;

            // Hover effect (respects disabled state)
            Color hoverColor = new Color(
                Mathf.Min(bgColor.r + 0.08f, 1f),
                Mathf.Min(bgColor.g + 0.08f, 1f),
                Mathf.Min(bgColor.b + 0.08f, 1f)
            );
            btn.RegisterCallback<MouseEnterEvent>(e =>
            {
                if (btn.enabledSelf)
                    btn.style.backgroundColor = hoverColor;
            });
            btn.RegisterCallback<MouseLeaveEvent>(e =>
            {
                if (btn.enabledSelf)
                    btn.style.backgroundColor = bgColor;
            });

            return btn;
        }

        private TextField StyledTextField(string label, string placeholder)
        {
            var field = new TextField(label);
            field.value = "";
            field.style.marginRight = 8;
            field.style.minHeight = 38;

            // Style the label
            var labelEl = field.labelElement;
            labelEl.style.fontSize = 12;
            labelEl.style.color = ColTextDim;
            labelEl.style.minWidth = 0;

            // Style the input (query by USS class, not name)
            var input = field.Q<VisualElement>(className: "unity-base-text-field__input");
            if (input != null)
            {
                input.style.backgroundColor = ColBgInputField;
                input.style.color = ColText;
                SetBorderRadius(input, 4);
                input.style.borderTopWidth = 1;
                input.style.borderBottomWidth = 1;
                input.style.borderLeftWidth = 1;
                input.style.borderRightWidth = 1;
                input.style.borderTopColor = ColBorder;
                input.style.borderBottomColor = ColBorder;
                input.style.borderLeftColor = ColBorder;
                input.style.borderRightColor = ColBorder;
                input.style.paddingLeft = 8;
                input.style.paddingRight = 8;
                input.style.paddingTop = 5;
                input.style.paddingBottom = 5;
                input.style.height = 38;
                input.style.minHeight = 38;
                input.style.unityTextAlign = TextAnchor.MiddleLeft;

                // Focus highlight
                input.RegisterCallback<FocusInEvent>(e =>
                {
                    input.style.borderTopColor = ColAccent;
                    input.style.borderBottomColor = ColAccent;
                    input.style.borderLeftColor = ColAccent;
                    input.style.borderRightColor = ColAccent;
                });
                input.RegisterCallback<FocusOutEvent>(e =>
                {
                    input.style.borderTopColor = ColBorder;
                    input.style.borderBottomColor = ColBorder;
                    input.style.borderLeftColor = ColBorder;
                    input.style.borderRightColor = ColBorder;
                });
            }

            // Placeholder via tooltip (UI Toolkit doesn't have native placeholder)
            field.tooltip = placeholder;

            return field;
        }

        private DropdownField StyledDropdownField(string label)
        {
            var field = new DropdownField(label);
            field.choices = new List<string>();
            field.value = "";
            field.style.marginRight = 8;
            field.style.minHeight = 38;

            var labelEl = field.labelElement;
            labelEl.style.fontSize = 12;
            labelEl.style.color = ColTextDim;
            labelEl.style.minWidth = 0;

            var input = field.Q<VisualElement>(className: "unity-base-popup-field__input");
            if (input != null)
            {
                input.style.backgroundColor = ColBgInputField;
                input.style.color = ColText;
                SetBorderRadius(input, 4);
                input.style.borderTopWidth = 1;
                input.style.borderBottomWidth = 1;
                input.style.borderLeftWidth = 1;
                input.style.borderRightWidth = 1;
                input.style.borderTopColor = ColBorder;
                input.style.borderBottomColor = ColBorder;
                input.style.borderLeftColor = ColBorder;
                input.style.borderRightColor = ColBorder;
                input.style.paddingLeft = 8;
                input.style.paddingRight = 8;
                input.style.height = 38;
                input.style.minHeight = 38;
            }

            return field;
        }

        private VisualElement StyledToggleItem(string label, bool value, out Toggle toggle)
        {
            var item = Row();
            item.style.height = 34;
            item.style.minHeight = 34;
            item.style.marginRight = 24;
            item.style.flexShrink = 0;
            item.tooltip = label;

            var actualToggle = new Toggle();
            actualToggle.value = value;
            actualToggle.style.display = DisplayStyle.None;
            actualToggle.tooltip = label;
            item.Add(actualToggle);

            var box = new VisualElement();
            box.style.width = 18;
            box.style.height = 18;
            box.style.minWidth = 18;
            box.style.minHeight = 18;
            box.style.marginRight = 8;
            box.style.flexShrink = 0;
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.Center;
            box.style.overflow = Overflow.Visible;
            item.Add(box);

            var checkmark = new Label("✓");
            checkmark.style.width = 18;
            checkmark.style.height = 18;
            checkmark.style.fontSize = 14;
            checkmark.style.color = ColText;
            checkmark.style.unityTextAlign = TextAnchor.MiddleCenter;
            checkmark.style.marginLeft = 0;
            checkmark.style.marginRight = 0;
            checkmark.style.marginTop = 0;
            checkmark.style.marginBottom = 0;
            checkmark.style.paddingLeft = 0;
            checkmark.style.paddingRight = 0;
            checkmark.style.paddingTop = 0;
            checkmark.style.paddingBottom = 0;
            box.Add(checkmark);

            var text = new Label(label);
            text.style.fontSize = 12;
            text.style.color = ColTextDim;
            text.style.unityTextAlign = TextAnchor.MiddleLeft;
            text.style.height = 20;
            text.style.marginTop = 0;
            text.style.marginBottom = 0;
            text.style.paddingTop = 0;
            text.style.paddingBottom = 0;
            item.Add(text);

            void RefreshVisual()
            {
                box.style.backgroundColor = actualToggle.value ? ColAccent : ColBgInputField;
                SetBorderRadius(box, 3);
                SetBorderWidth(box, 1);
                box.style.borderTopColor = actualToggle.value ? ColAccent : ColTextDim;
                box.style.borderBottomColor = actualToggle.value ? ColAccent : ColTextDim;
                box.style.borderLeftColor = actualToggle.value ? ColAccent : ColTextDim;
                box.style.borderRightColor = actualToggle.value ? ColAccent : ColTextDim;
                checkmark.style.display = actualToggle.value ? DisplayStyle.Flex : DisplayStyle.None;
            }

            item.RegisterCallback<ClickEvent>(_ => actualToggle.value = !actualToggle.value);
            actualToggle.RegisterValueChangedCallback(_ => RefreshVisual());
            RefreshVisual();

            toggle = actualToggle;
            return item;
        }

        private VisualElement OutputArea(out Label outputLabel, float minimumHeight = 0)
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.marginTop = 14;
            if (minimumHeight > 0)
            {
                container.style.minHeight = minimumHeight;
                container.style.flexShrink = 0;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            if (minimumHeight > 0)
                scroll.style.minHeight = Math.Max(100, minimumHeight - 42);
            scroll.style.backgroundColor = ColBgOutput;
            SetBorderRadius(scroll, 6);
            scroll.style.borderTopWidth = 1;
            scroll.style.borderBottomWidth = 1;
            scroll.style.borderLeftWidth = 1;
            scroll.style.borderRightWidth = 1;
            scroll.style.borderTopColor = ColBorder;
            scroll.style.borderBottomColor = ColBorder;
            scroll.style.borderLeftColor = ColBorder;
            scroll.style.borderRightColor = ColBorder;

            outputLabel = new Label();
            outputLabel.style.whiteSpace = WhiteSpace.Normal;
            outputLabel.style.color = ColText;
            outputLabel.style.fontSize = 12;
            outputLabel.style.paddingTop = 10;
            outputLabel.style.paddingBottom = 10;
            outputLabel.style.paddingLeft = 12;
            outputLabel.style.paddingRight = 12;
            outputLabel.enableRichText = true;
            scroll.Add(outputLabel);

            container.Add(scroll);

            // Action buttons
            var actions = Row();
            actions.style.marginTop = 6;

            var clearBtn = ActionButton("清除", ColNeutral);
            Label capturedLabel = outputLabel;
            clearBtn.clicked += () => capturedLabel.text = "";
            actions.Add(clearBtn);

            var saveBtn = ActionButton("保存", ColAccent);
            saveBtn.clicked += () => SaveLogToFile(capturedLabel.text);
            actions.Add(saveBtn);

            var openBtn = ActionButton("打开日志文件夹", ColNeutral);
            openBtn.clicked += () => FileDialogHelper.OpenInExplorer(logSavePath);
            actions.Add(openBtn);

            container.Add(actions);

            return container;
        }

        // Style helpers to reduce repetition

        private static void SetBorderRadius(VisualElement el, float radius)
        {
            el.style.borderTopLeftRadius = radius;
            el.style.borderTopRightRadius = radius;
            el.style.borderBottomLeftRadius = radius;
            el.style.borderBottomRightRadius = radius;
        }

        private static void SetBorderWidth(VisualElement el, float width)
        {
            el.style.borderTopWidth = width;
            el.style.borderBottomWidth = width;
            el.style.borderLeftWidth = width;
            el.style.borderRightWidth = width;
        }

        #endregion
    }
}
