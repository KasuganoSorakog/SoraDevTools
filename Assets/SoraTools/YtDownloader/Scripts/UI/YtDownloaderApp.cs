using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace YtDownloader
{
    /// <summary>
    /// Main MonoBehaviour for YtDownloader.
    /// Uses IMGUI (OnGUI) for full Unity 2019-2022 + PC build compatibility.
    /// Attach to any GameObject in the scene.
    /// </summary>
    public class YtDownloaderApp : MonoBehaviour
    {
        // ── Serialized ──
        [SerializeField] private Font   chineseFont;
        [SerializeField] private int    windowWidth  = 1280;
        [SerializeField] private int    windowHeight = 900;

        // ── Layout constants ──
        private const float HEADER_H   = 48f;
        private const float SIDE_PAD   = 8f;
        private const float SECTION_GAP = 5f;
        private const float THUMB_W    = 272f;
        private const float THUMB_H    = 153f;   // 16:9

        // ── Services ──
        private VideoInfoService       _videoInfoSvc;
        private DownloadService        _downloadSvc;
        private DownloadHistoryService _historySvc;
        private ThumbnailService       _thumbnailSvc;

        // ── Fetch state ──
        private string           _urlInput    = "";
        private bool             _isFetching;
        private volatile bool    _fetchReady;
        private VideoInfoResult  _fetchResult;
        private string           _statusMsg   = "";
        private enum StatusLevel { None, Ok, Warn, Error }
        private StatusLevel      _statusLevel = StatusLevel.None;
        private VideoInfo        _currentVideo;
        private Texture2D        _thumbnail;

        // ── Download / history state ──
        private int    _formatIdx;
        private int    _qualityIdx;
        private string _savePath = "";
        private List<DownloadTask>          _activeTasks = new List<DownloadTask>();
        private List<DownloadHistoryEntry>  _history     = new List<DownloadHistoryEntry>();
        private Vector2 _listScrollPos;
        private int     _activeTab;   // 0 = downloads, 1 = history

        // ── Settings modal state ──
        private bool    _showSettings;
        private string  _sett_ytdlp      = "";
        private string  _sett_ffmpeg     = "";
        private string  _sett_cookie     = "";
        private int     _sett_cookieMode;
        private int     _sett_browser;
        private Vector2 _settingsScroll;

        // ── First-run modal state ──
        private bool             _showFirstRun;
        private volatile float   _firstRunProgress;
        private volatile string  _firstRunMsg  = "";
        private volatile bool    _firstRunDone;
        private volatile bool    _firstRunFailed;
        private bool             _firstRunBusy;

        // ── Dropdown state ──
        private bool _showFormatDd;
        private bool _showQualityDd;
        private Rect _formatDdScreenRect;
        private Rect _qualityDdScreenRect;
        private Rect _contentAreaRect;

        // ── Colors ──
        private static readonly Color C_BG         = new Color(0.051f, 0.051f, 0.129f);
        private static readonly Color C_HEADER     = new Color(0.031f, 0.031f, 0.102f);
        private static readonly Color C_PANEL      = new Color(0.086f, 0.086f, 0.161f);
        private static readonly Color C_PANEL_DARK = new Color(0.055f, 0.055f, 0.118f);
        private static readonly Color C_INPUT      = new Color(0.039f, 0.039f, 0.102f);
        private static readonly Color C_BTN        = new Color(0.110f, 0.110f, 0.227f);
        private static readonly Color C_BTN_HOVER  = new Color(0.145f, 0.145f, 0.314f);
        private static readonly Color C_ACCENT     = new Color(0.902f, 0.224f, 0.275f);
        private static readonly Color C_ACCENT_H   = new Color(0.976f, 0.345f, 0.388f);
        private static readonly Color C_BLUE       = new Color(0.114f, 0.333f, 0.800f);
        private static readonly Color C_BLUE_H     = new Color(0.161f, 0.400f, 0.910f);
        private static readonly Color C_SUCCESS    = new Color(0.176f, 0.776f, 0.325f);
        private static readonly Color C_WARNING    = new Color(0.957f, 0.635f, 0.196f);
        private static readonly Color C_TEXT       = new Color(0.910f, 0.910f, 1.000f);
        private static readonly Color C_TEXT_MUTED = new Color(0.471f, 0.471f, 0.659f);
        private static readonly Color C_BORDER     = new Color(0.145f, 0.145f, 0.271f);
        private static readonly Color C_PROG_BG    = new Color(0.059f, 0.059f, 0.165f);
        private static readonly Color C_PROG_ACT   = new Color(0.282f, 0.584f, 0.937f);
        private static readonly Color C_PROG_OK    = new Color(0.176f, 0.776f, 0.325f);
        private static readonly Color C_PROG_ERR   = new Color(0.902f, 0.224f, 0.275f);
        private static readonly Color C_PROG_IDLE  = new Color(0.471f, 0.471f, 0.659f);
        private static readonly Color C_DANGER     = new Color(0.400f, 0.082f, 0.122f);
        private static readonly Color C_DANGER_H   = new Color(0.549f, 0.114f, 0.165f);

        // ── Styles ──
        private bool     _stylesReady;
        private GUIStyle _stTitle;
        private GUIStyle _stLabel;
        private GUIStyle _stLabelMuted;
        private GUIStyle _stLabelBold;
        private GUIStyle _stLabelOk;
        private GUIStyle _stLabelWarn;
        private GUIStyle _stLabelError;
        private GUIStyle _stInput;
        private GUIStyle _stBtnNormal;
        private GUIStyle _stBtnAccent;
        private GUIStyle _stBtnDanger;
        private GUIStyle _stBtnSmall;
        private GUIStyle _stBtnDropdown;
        private GUIStyle _stDropdownItem;
        private GUIStyle _stPanel;
        private GUIStyle _stTabNormal;
        private GUIStyle _stTabActive;

        // ─────────────────────────────────────────────────────────────────
        // Unity lifecycle
        // ─────────────────────────────────────────────────────────────────

        private void Awake()
        {
#if !UNITY_EDITOR
            Screen.SetResolution(windowWidth, windowHeight, false);
#endif
            _videoInfoSvc = new VideoInfoService();
            _thumbnailSvc = new ThumbnailService(this);
            _downloadSvc  = new DownloadService();
            _historySvc   = new DownloadHistoryService();
        }

        private void Start()
        {
            _savePath   = YtDlSettings.Instance.DefaultDownloadPath;
            if (string.IsNullOrEmpty(_savePath))
                _savePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            _formatIdx  = YtDlSettings.Instance.FormatPresetIndex;
            _qualityIdx = YtDlSettings.Instance.QualityPresetIndex;
            _history    = _historySvc.GetAll();

            if (!YtDlSettings.Instance.YtDlpExists || !YtDlSettings.Instance.FfmpegExists)
                _showFirstRun = true;
        }

        private void Update()
        {
            if (_fetchReady)
            {
                _fetchReady = false;
                ApplyFetchResult(_fetchResult);
            }

            _activeTasks = _downloadSvc.GetActiveTasks();
            while (_downloadSvc.TryDequeueCompleted(out var completed))
            {
                if (completed.Status == DownloadStatus.Completed)
                    AddToHistory(completed);
                _history = _historySvc.GetAll();
            }

            if (_showFirstRun && _firstRunBusy)
            {
                if (_firstRunDone)
                {
                    _firstRunDone = false; _firstRunBusy = false; _showFirstRun = false;
                    SetStatus("yt-dlp 已就绪，请输入视频链接。", StatusLevel.Ok);
                }
                else if (_firstRunFailed)
                {
                    _firstRunFailed = false; _firstRunBusy = false;
                }
            }
        }

        private void OnDestroy()
        {
            _downloadSvc?.CancelAll();
        }

        // ─────────────────────────────────────────────────────────────────
        // OnGUI entry point
        // ─────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_stylesReady) InitStyles();

            // Background
            DrawRect(new Rect(0, 0, Screen.width, Screen.height), C_BG);

            // Header (outside scroll, fixed)
            DrawHeader();

            // Main content area
            float areaX = SIDE_PAD;
            float areaY = HEADER_H + SIDE_PAD;
            float areaW = Screen.width  - SIDE_PAD * 2f;
            float areaH = Screen.height - HEADER_H - SIDE_PAD * 2f;
            _contentAreaRect = new Rect(areaX, areaY, areaW, areaH);

            GUILayout.BeginArea(_contentAreaRect);
            DrawMainContent();
            GUILayout.EndArea();

            // Dropdowns drawn after main content (Z-order on top)
            if (_showFormatDd)
                DrawDropdown(_formatDdScreenRect,
                    DownloadPreset.FormatLabels, ref _formatIdx, ref _showFormatDd);
            if (_showQualityDd)
                DrawDropdown(_qualityDdScreenRect,
                    DownloadPreset.QualityLabels, ref _qualityIdx, ref _showQualityDd);

            // Modals on top of everything
            if (_showSettings)  DrawSettingsModal();
            if (_showFirstRun)  DrawFirstRunModal();

            // Close dropdowns on outside click
            if ((_showFormatDd || _showQualityDd) &&
                Event.current.type == EventType.MouseDown)
            {
                bool inFmt  = _showFormatDd  && _formatDdScreenRect.Contains(Event.current.mousePosition);
                bool inQual = _showQualityDd && _qualityDdScreenRect.Contains(Event.current.mousePosition);
                if (!inFmt && !inQual)
                {
                    _showFormatDd = _showQualityDd = false;
                    Event.current.Use();
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Header
        // ─────────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            DrawRect(new Rect(0, 0, Screen.width, HEADER_H), C_HEADER);
            // Accent bottom bar
            DrawRect(new Rect(0, HEADER_H - 2f, Screen.width, 2f), C_ACCENT);

            if (!_stylesReady) return;

            // "YT" accent + subtitle
            GUI.Label(new Rect(16f, 6f, 50f, HEADER_H - 12f), "YT", _stTitle);
            GUI.Label(new Rect(64f, 6f, 240f, HEADER_H - 12f), " Downloader", _stLabelBold);

            // Settings button
            if (GUI.Button(new Rect(Screen.width - 100f, 9f, 86f, HEADER_H - 18f), "  设置  ", _stBtnSmall))
                OpenSettings();
        }

        // ─────────────────────────────────────────────────────────────────
        // Main content
        // ─────────────────────────────────────────────────────────────────

        private void DrawMainContent()
        {
            DrawUrlSection();
            GUILayout.Space(SECTION_GAP);
            DrawVideoSection();
            GUILayout.Space(SECTION_GAP);
            DrawFormatSection();
            GUILayout.Space(SECTION_GAP);
            DrawDownloadSection();
            GUILayout.Space(SECTION_GAP);
            DrawTabSection();
        }

        // ── URL section ──

        private void DrawUrlSection()
        {
            BeginPanel();

            GUILayout.BeginHorizontal();
            GUILayout.Label("URL", _stLabelMuted, GUILayout.Width(28));
            GUILayout.Space(4f);
            GUI.SetNextControlName("UrlField");
            _urlInput = GUILayout.TextField(_urlInput, _stInput,
                GUILayout.ExpandWidth(true), GUILayout.Height(30f));
            GUILayout.Space(6f);

            string fetchBtnLabel = _isFetching ? "获取中..." : "获取信息";
            GUI.enabled = !_isFetching;
            if (GUILayout.Button(fetchBtnLabel, _stBtnAccent, GUILayout.Width(90f), GUILayout.Height(30f)))
                StartFetch();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // Enter-key shortcut
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return ||
                 Event.current.keyCode == KeyCode.KeypadEnter) &&
                !_isFetching && !string.IsNullOrWhiteSpace(_urlInput))
            {
                StartFetch();
                Event.current.Use();
            }

            GUILayout.Space(2f);
            // Status row
            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUIStyle st = _statusLevel == StatusLevel.Ok    ? _stLabelOk
                            : _statusLevel == StatusLevel.Warn  ? _stLabelWarn
                            : _statusLevel == StatusLevel.Error ? _stLabelError
                            : _stLabelMuted;
                GUILayout.Label(_statusMsg, st);
            }
            else
            {
                GUILayout.Label(" ", _stLabelMuted);
            }

            EndPanel();
        }

        // ── Video preview section ──

        private void DrawVideoSection()
        {
            BeginPanel();
            GUILayout.BeginHorizontal(GUILayout.Height(THUMB_H));

            // Thumbnail area
            Rect thumbRect = GUILayoutUtility.GetRect(THUMB_W, THUMB_H,
                GUILayout.Width(THUMB_W), GUILayout.Height(THUMB_H));

            if (_thumbnail != null)
            {
                GUI.DrawTexture(thumbRect, _thumbnail, ScaleMode.ScaleAndCrop);
            }
            else
            {
                DrawRect(thumbRect, C_PANEL_DARK);
                string placeholder = _currentVideo != null
                    ? "加载缩略图..."
                    : "请输入 URL 并点击「获取信息」";
                GUI.Label(
                    new Rect(thumbRect.x + 6f, thumbRect.y + thumbRect.height * 0.5f - 10f,
                             thumbRect.width - 12f, 20f),
                    placeholder, _stLabelMuted);
            }

            GUILayout.Space(12f);

            // Info column
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Space(4f);
            if (_currentVideo != null)
            {
                GUILayout.Label(_currentVideo.Title, _stLabelBold);
                GUILayout.Space(4f);
                GUILayout.Label("频道: " + _currentVideo.Channel, _stLabel);
                GUILayout.Space(2f);
                GUILayout.Label(
                    string.Format("时长: {0}   播放: {1}   上传: {2}",
                        _currentVideo.DurationDisplay,
                        _currentVideo.ViewCountDisplay,
                        _currentVideo.UploadDateDisplay),
                    _stLabelMuted);
            }
            else
            {
                GUILayout.Label("暂无视频信息", _stLabelMuted);
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            EndPanel();
        }

        // ── Format / quality section ──

        private void DrawFormatSection()
        {
            BeginPanel();
            GUILayout.BeginHorizontal();

            GUILayout.Label("格式", _stLabelMuted, GUILayout.Width(28f));
            GUILayout.Space(4f);

            string fmtLabel = DownloadPreset.FormatLabels[_formatIdx] + "  ▾";
            Rect fmtRect = GUILayoutUtility.GetRect(220f, 30f,
                GUILayout.Width(220f), GUILayout.Height(30f));
            if (GUI.Button(fmtRect, fmtLabel, _stBtnDropdown))
            {
                _showFormatDd  = !_showFormatDd;
                _showQualityDd = false;
                _formatDdScreenRect = LocalToScreen(fmtRect);
            }

            GUILayout.Space(18f);
            GUILayout.Label("画质", _stLabelMuted, GUILayout.Width(28f));
            GUILayout.Space(4f);

            string qualLabel = DownloadPreset.QualityLabels[_qualityIdx] + "  ▾";
            Rect qualRect = GUILayoutUtility.GetRect(155f, 30f,
                GUILayout.Width(155f), GUILayout.Height(30f));
            if (GUI.Button(qualRect, qualLabel, _stBtnDropdown))
            {
                _showQualityDd = !_showQualityDd;
                _showFormatDd  = false;
                _qualityDdScreenRect = LocalToScreen(qualRect);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            EndPanel();
        }

        // ── Save path / download section ──

        private void DrawDownloadSection()
        {
            BeginPanel();
            GUILayout.BeginHorizontal();

            GUILayout.Label("保存至", _stLabelMuted, GUILayout.Width(42f));
            GUILayout.Space(4f);
            _savePath = GUILayout.TextField(_savePath, _stInput,
                GUILayout.ExpandWidth(true), GUILayout.Height(30f));
            GUILayout.Space(4f);

            if (GUILayout.Button("浏览", _stBtnSmall, GUILayout.Width(50f), GUILayout.Height(30f)))
            {
                string picked = FolderBrowserHelper.Browse("选择保存目录");
                if (!string.IsNullOrEmpty(picked)) _savePath = picked;
            }

            GUILayout.Space(8f);
            bool canDownload = _currentVideo != null && !string.IsNullOrWhiteSpace(_savePath);
            GUI.enabled = canDownload;
            if (GUILayout.Button("↓  开始下载", _stBtnAccent, GUILayout.Width(104f), GUILayout.Height(30f)))
                StartDownload();
            GUI.enabled = true;

            GUILayout.EndHorizontal();
            EndPanel();
        }

        // ── Tab section (downloads / history) ──

        private void DrawTabSection()
        {
            // Tab bar
            GUILayout.BeginHorizontal();
            string tab0 = string.Format("下载任务 ({0})", _activeTasks.Count);
            string tab1 = string.Format("历史记录 ({0})", _history.Count);

            if (GUILayout.Button(tab0,
                    _activeTab == 0 ? _stTabActive : _stTabNormal,
                    GUILayout.Width(155f), GUILayout.Height(32f)))
                _activeTab = 0;

            GUILayout.Space(2f);
            if (GUILayout.Button(tab1,
                    _activeTab == 1 ? _stTabActive : _stTabNormal,
                    GUILayout.Width(155f), GUILayout.Height(32f)))
                _activeTab = 1;

            GUILayout.FlexibleSpace();

            if (_activeTab == 1 && _history.Count > 0)
            {
                if (GUILayout.Button("清空历史", _stBtnSmall,
                        GUILayout.Width(72f), GUILayout.Height(32f)))
                { _historySvc.Clear(); _history = _historySvc.GetAll(); }
            }
            GUILayout.EndHorizontal();

            // Tab content
            GUILayout.BeginVertical(_stPanel, GUILayout.ExpandHeight(true));
            GUILayout.Space(4f);
            _listScrollPos = GUILayout.BeginScrollView(_listScrollPos);

            if (_activeTab == 0) DrawDownloadList();
            else                 DrawHistoryList();

            GUILayout.EndScrollView();
            GUILayout.Space(4f);
            GUILayout.EndVertical();
        }

        // ── Download task list ──

        private void DrawDownloadList()
        {
            if (_activeTasks.Count == 0)
            {
                GUILayout.Space(16f);
                GUILayout.BeginHorizontal();
                GUILayout.Space(12f);
                GUILayout.Label("暂无下载任务", _stLabelMuted);
                GUILayout.EndHorizontal();
                return;
            }

            foreach (var task in _activeTasks)
            {
                DrawTaskRow(task);
                GUILayout.Space(4f);
            }
        }

        private void DrawTaskRow(DownloadTask task)
        {
            GUILayout.BeginVertical(_stPanel);
            GUILayout.Space(6f);

            // Title row
            GUILayout.BeginHorizontal();
            GUILayout.Space(8f);
            string displayTitle = task.VideoTitle.Length > 70
                ? task.VideoTitle.Substring(0, 67) + "..."
                : task.VideoTitle;
            GUILayout.Label(displayTitle, _stLabel, GUILayout.ExpandWidth(true));

            bool isDone = task.Status == DownloadStatus.Completed ||
                          task.Status == DownloadStatus.Failed     ||
                          task.Status == DownloadStatus.Cancelled;
            if (!isDone)
            {
                if (GUILayout.Button("取消", _stBtnDanger, GUILayout.Width(48f), GUILayout.Height(22f)))
                    _downloadSvc.CancelTask(task);
            }
            GUILayout.Space(8f);
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // Progress bar
            GUILayout.BeginHorizontal();
            GUILayout.Space(8f);
            Rect barRect = GUILayoutUtility.GetRect(0, 16f,
                GUILayout.ExpandWidth(true), GUILayout.Height(16f));
            // Subtract right margin
            barRect.width -= 8f;

            Color fillColor = task.Status == DownloadStatus.Completed ? C_PROG_OK
                            : task.Status == DownloadStatus.Failed     ? C_PROG_ERR
                            : task.Status == DownloadStatus.Cancelled  ? C_PROG_IDLE
                            : task.Status == DownloadStatus.Converting  ? C_WARNING
                            : C_PROG_ACT;

            DrawProgressBar(barRect, task.ProgressPercent / 100f, fillColor, task.StatusMessage);
            GUILayout.Space(8f);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.EndVertical();
        }

        // ── History list ──

        private void DrawHistoryList()
        {
            if (_history.Count == 0)
            {
                GUILayout.Space(16f);
                GUILayout.BeginHorizontal();
                GUILayout.Space(12f);
                GUILayout.Label("暂无下载历史", _stLabelMuted);
                GUILayout.EndHorizontal();
                return;
            }

            DownloadHistoryEntry toRemove = null;
            foreach (var entry in _history)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(10f);

                string title = entry.Title.Length > 60
                    ? entry.Title.Substring(0, 57) + "..."
                    : entry.Title;
                GUILayout.Label(title, _stLabel, GUILayout.ExpandWidth(true));

                GUILayout.Label(entry.FileSizeDisplay, _stLabelMuted, GUILayout.Width(70f));

                string dateStr = "";
                if (!string.IsNullOrEmpty(entry.CompletedAt))
                {
                    if (DateTime.TryParse(entry.CompletedAt, out var dt))
                        dateStr = dt.ToString("MM-dd HH:mm");
                    else if (entry.CompletedAt.Length >= 16)
                        dateStr = entry.CompletedAt.Substring(0, 16);
                }
                GUILayout.Label(dateStr, _stLabelMuted, GUILayout.Width(80f));

                if (GUILayout.Button("打开", _stBtnSmall, GUILayout.Width(42f), GUILayout.Height(22f)))
                {
                    string dir = File.Exists(entry.FilePath)
                        ? Path.GetDirectoryName(entry.FilePath)
                        : entry.FilePath;
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        System.Diagnostics.Process.Start("explorer.exe", dir);
                }
                GUILayout.Space(2f);
                if (GUILayout.Button("×", _stBtnSmall, GUILayout.Width(22f), GUILayout.Height(22f)))
                    toRemove = entry;
                GUILayout.Space(10f);
                GUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }

            if (toRemove != null)
            { _historySvc.Remove(toRemove); _history = _historySvc.GetAll(); }
        }

        // ─────────────────────────────────────────────────────────────────
        // Dropdown popup
        // ─────────────────────────────────────────────────────────────────

        private void DrawDropdown(Rect triggerRect, List<string> items,
            ref int selected, ref bool show)
        {
            float itemH  = 28f;
            float listW  = Mathf.Max(triggerRect.width, 180f);
            float listH  = items.Count * itemH;
            float listX  = triggerRect.x;
            float listY  = triggerRect.yMax + 2f;

            // Flip upward if not enough space below
            if (listY + listH > Screen.height - 10f)
                listY = triggerRect.y - listH - 2f;

            // Border
            DrawRect(new Rect(listX - 1f, listY - 1f, listW + 2f, listH + 2f), C_BORDER);

            for (int i = 0; i < items.Count; i++)
            {
                Rect itemRect = new Rect(listX, listY + i * itemH, listW, itemH);
                bool hover    = itemRect.Contains(Event.current.mousePosition);
                Color bg = (i == selected) ? C_BLUE : hover ? C_BTN_HOVER : C_PANEL;
                DrawRect(itemRect, bg);

                if (GUI.Button(itemRect, items[i], _stDropdownItem))
                {
                    selected = i;
                    show = false;
                    Event.current.Use();
                }
            }

            // Close on outside click
            var listRect = new Rect(listX, listY, listW, listH);
            if (Event.current.type == EventType.MouseDown &&
                !listRect.Contains(Event.current.mousePosition) &&
                !triggerRect.Contains(Event.current.mousePosition))
            {
                show = false;
                Event.current.Use();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Settings modal
        // ─────────────────────────────────────────────────────────────────

        private void DrawSettingsModal()
        {
            // Dimmer
            DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.78f));

            float w = 580f, h = 460f;
            float x = (Screen.width  - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            DrawRect(new Rect(x - 1f, y - 1f, w + 2f, h + 2f), C_BORDER);
            DrawRect(new Rect(x, y, w, h), C_PANEL);
            DrawRect(new Rect(x, y, w, 3f), C_ACCENT);

            GUILayout.BeginArea(new Rect(x, y + 3f, w, h - 3f));
            GUILayout.Space(14f);

            GUILayout.BeginHorizontal();
            GUILayout.Space(20f);
            GUILayout.Label("设置", _stLabelBold);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", _stBtnSmall, GUILayout.Width(28f), GUILayout.Height(26f)))
                CloseSettings();
            GUILayout.Space(14f);
            GUILayout.EndHorizontal();

            GUILayout.Space(12f);
            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll,
                GUILayout.ExpandHeight(true));

            DrawSettingsField("yt-dlp 路径", ref _sett_ytdlp, "留空使用默认: %APPDATA%\\YtDownloader\\yt-dlp.exe");
            GUILayout.Space(4f);
            DrawSettingsField("ffmpeg 目录", ref _sett_ffmpeg, "留空使用默认: %APPDATA%\\YtDownloader\\");
            GUILayout.Space(10f);

            // Cookie mode selector
            GUILayout.BeginHorizontal();
            GUILayout.Space(20f);
            GUILayout.Label("Cookie 模式", _stLabelMuted, GUILayout.Width(80f));
            GUILayout.Space(8f);
            string[] cookieModes = { "不使用", "浏览器", "文件" };
            for (int i = 0; i < cookieModes.Length; i++)
            {
                if (GUILayout.Button(cookieModes[i],
                        _sett_cookieMode == i ? _stTabActive : _stTabNormal,
                        GUILayout.Width(68f), GUILayout.Height(26f)))
                    _sett_cookieMode = i;
                GUILayout.Space(2f);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            if (_sett_cookieMode == 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20f);
                GUILayout.Label("浏览器", _stLabelMuted, GUILayout.Width(80f));
                GUILayout.Space(8f);
                string[] browsers = { "Chrome", "Firefox", "Edge", "Opera" };
                for (int i = 0; i < browsers.Length; i++)
                {
                    if (GUILayout.Button(browsers[i],
                            _sett_browser == i ? _stTabActive : _stTabNormal,
                            GUILayout.Width(68f), GUILayout.Height(26f)))
                        _sett_browser = i;
                    GUILayout.Space(2f);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
            }

            if (_sett_cookieMode == 2)
            {
                DrawSettingsField("Cookie 文件", ref _sett_cookie, "cookies.txt 文件路径");
                GUILayout.Space(4f);
            }

            GUILayout.EndScrollView();

            // Action buttons
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("保存", _stBtnAccent, GUILayout.Width(88f), GUILayout.Height(30f)))
                SaveSettings();
            GUILayout.Space(8f);
            if (GUILayout.Button("取消", _stBtnSmall, GUILayout.Width(72f), GUILayout.Height(30f)))
                CloseSettings();
            GUILayout.Space(20f);
            GUILayout.EndHorizontal();
            GUILayout.Space(12f);

            GUILayout.EndArea();
        }

        private void DrawSettingsField(string label, ref string value, string hint)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(20f);
            GUILayout.Label(label, _stLabelMuted, GUILayout.Width(80f));
            GUILayout.Space(8f);
            value = GUILayout.TextField(value, _stInput,
                GUILayout.ExpandWidth(true), GUILayout.Height(26f));
            GUILayout.Space(20f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(110f);
            GUILayout.Label(hint, _stLabelMuted);
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        // ─────────────────────────────────────────────────────────────────
        // First-run modal
        // ─────────────────────────────────────────────────────────────────

        private void DrawFirstRunModal()
        {
            DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.88f));

            float w = 520f, h = 320f;
            float x = (Screen.width  - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            DrawRect(new Rect(x - 1f, y - 1f, w + 2f, h + 2f), C_BORDER);
            DrawRect(new Rect(x, y, w, h), C_PANEL);
            DrawRect(new Rect(x, y, w, 3f), C_ACCENT);

            GUILayout.BeginArea(new Rect(x, y + 3f, w, h - 3f));
            GUILayout.Space(22f);

            GUILayout.BeginHorizontal();
            GUILayout.Space(28f);
            GUILayout.Label("欢迎使用 YouTube Downloader", _stLabelBold);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Space(28f);
            GUILayout.Label("需要 yt-dlp 和 ffmpeg 才能下载视频。", _stLabel);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(28f);
            GUILayout.Label("点击「自动下载」将从 GitHub 获取（约 100 MB）。", _stLabelMuted);
            GUILayout.EndHorizontal();

            GUILayout.Space(16f);

            if (_firstRunBusy)
            {
                float prog = _firstRunProgress;
                string msg = _firstRunMsg;

                GUILayout.BeginHorizontal();
                GUILayout.Space(28f);
                Rect barRect = GUILayoutUtility.GetRect(0, 14f,
                    GUILayout.ExpandWidth(true), GUILayout.Height(14f));
                barRect.width -= 28f;
                DrawProgressBar(barRect, prog, C_PROG_ACT, "");
                GUILayout.Space(28f);
                GUILayout.EndHorizontal();

                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                GUILayout.Space(28f);
                GUILayout.Label(msg, _stLabelMuted);
                GUILayout.EndHorizontal();
            }
            else if (_firstRunFailed)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(28f);
                GUILayout.Label("下载失败，请检查网络后重试。", _stLabelError);
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            GUILayout.Space(28f);
            GUI.enabled = !_firstRunBusy;
            if (GUILayout.Button("自动下载 yt-dlp + ffmpeg", _stBtnAccent,
                    GUILayout.Width(216f), GUILayout.Height(34f)))
                StartAutoDownload();
            GUILayout.Space(12f);
            if (GUILayout.Button("跳过 / 手动配置", _stBtnSmall,
                    GUILayout.Width(128f), GUILayout.Height(34f)))
            { _showFirstRun = false; OpenSettings(); }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(20f);
            GUILayout.EndArea();
        }

        // ─────────────────────────────────────────────────────────────────
        // Panel helpers
        // ─────────────────────────────────────────────────────────────────

        private void BeginPanel()
        {
            GUILayout.BeginVertical(_stPanel);
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Space(8f);
            GUILayout.BeginVertical();
        }

        private void EndPanel()
        {
            GUILayout.EndVertical();
            GUILayout.Space(8f);
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            GUILayout.EndVertical();
        }

        private void DrawProgressBar(Rect rect, float t, Color fillColor, string label)
        {
            t = Mathf.Clamp01(t);
            DrawRect(rect, C_PROG_BG);
            if (t > 0f)
                DrawRect(new Rect(rect.x, rect.y, rect.width * t, rect.height), fillColor);

            if (!string.IsNullOrEmpty(label) && _stylesReady)
            {
                var style = new GUIStyle(_stLabelMuted)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize  = 10,
                    clipping  = TextClipping.Clip,
                };
                GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height),
                    label, style);
            }
        }

        private void DrawRect(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private Rect LocalToScreen(Rect localRect)
        {
            return new Rect(
                _contentAreaRect.x + localRect.x,
                _contentAreaRect.y + localRect.y,
                localRect.width,
                localRect.height);
        }

        // ─────────────────────────────────────────────────────────────────
        // Action handlers
        // ─────────────────────────────────────────────────────────────────

        private void StartFetch()
        {
            string url = _urlInput.Trim();
            if (string.IsNullOrEmpty(url))
            { SetStatus("请输入视频 URL", StatusLevel.Warn); return; }

            if (!YtDlSettings.Instance.YtDlpExists)
            { SetStatus("yt-dlp 未找到，请先完成初始化", StatusLevel.Error); _showFirstRun = true; return; }

            _isFetching   = true;
            _currentVideo = null;
            _thumbnail    = null;
            _fetchReady   = false;
            SetStatus("正在获取视频信息...", StatusLevel.None);

            System.Threading.Tasks.Task.Run(() =>
            {
                var result = _videoInfoSvc.FetchInfo(url);
                _fetchResult = result;
                _fetchReady  = true;
            });
        }

        private void ApplyFetchResult(VideoInfoResult result)
        {
            _isFetching = false;
            if (!result.IsSuccess)
            { SetStatus("错误: " + result.ErrorMessage, StatusLevel.Error); return; }

            _currentVideo = result.Video;
            SetStatus("获取成功: " + _currentVideo.Title, StatusLevel.Ok);

            if (!string.IsNullOrEmpty(_currentVideo.ThumbnailUrl))
                _thumbnailSvc.DownloadThumbnail(_currentVideo.ThumbnailUrl,
                    tex => _thumbnail = tex);
        }

        private void StartDownload()
        {
            if (_currentVideo == null)
            { SetStatus("请先获取视频信息", StatusLevel.Warn); return; }

            if (!YtDlSettings.Instance.YtDlpExists)
            { SetStatus("yt-dlp 未找到", StatusLevel.Error); _showFirstRun = true; return; }

            if (!Directory.Exists(_savePath))
            { SetStatus("保存路径不存在: " + _savePath, StatusLevel.Error); return; }

            YtDlSettings.Instance.DefaultDownloadPath = _savePath;
            YtDlSettings.Instance.FormatPresetIndex   = _formatIdx;
            YtDlSettings.Instance.QualityPresetIndex  = _qualityIdx;
            YtDlSettings.Instance.Save();

            var fmt      = (VideoFormatPreset)_formatIdx;
            var qual     = (QualityPreset)_qualityIdx;
            var fmtArgs  = DownloadPreset.BuildArguments(fmt, qual);
            bool ensure264 = fmt == VideoFormatPreset.Mp4H264;

            _downloadSvc.StartDownload(_currentVideo.WebpageUrl, _currentVideo.Title,
                _savePath, fmtArgs, ensure264);

            _activeTab = 0;
            SetStatus("下载任务已添加", StatusLevel.Ok);
        }

        private void AddToHistory(DownloadTask task)
        {
            long size = 0;
            if (File.Exists(task.OutputPath))
                size = new FileInfo(task.OutputPath).Length;

            _historySvc.Add(new DownloadHistoryEntry
            {
                Title       = task.VideoTitle,
                FilePath    = task.OutputPath,
                FileSize    = size > 0 ? size : task.TotalBytes,
                Url         = task.Url,
                CompletedAt = DateTime.Now.ToString("o"),
            });
        }

        private void OpenSettings()
        {
            var s = YtDlSettings.Instance;
            _sett_ytdlp      = s.YtDlpPath;
            _sett_ffmpeg     = s.FfmpegDir;
            _sett_cookie     = s.CookieFilePath;
            _sett_cookieMode = s.CookieModeIndex;
            _sett_browser    = s.BrowserChoiceIndex;
            _showSettings    = true;
        }

        private void SaveSettings()
        {
            var s = YtDlSettings.Instance;
            s.YtDlpPath          = _sett_ytdlp.Trim();
            s.FfmpegDir          = _sett_ffmpeg.Trim();
            s.CookieFilePath     = _sett_cookie.Trim();
            s.CookieModeIndex    = _sett_cookieMode;
            s.BrowserChoiceIndex = _sett_browser;
            s.Save();
            _showSettings = false;
        }

        private void CloseSettings() => _showSettings = false;

        private void SetStatus(string msg, StatusLevel level)
        { _statusMsg = msg; _statusLevel = level; }

        private void StartAutoDownload()
        {
            _firstRunBusy     = true;
            _firstRunDone     = false;
            _firstRunFailed   = false;
            _firstRunProgress = 0f;
            _firstRunMsg      = "准备中...";

            bool needYtDlp  = !YtDlSettings.Instance.YtDlpExists;
            bool needFfmpeg = !YtDlSettings.Instance.FfmpegExists;
            var  cts        = new CancellationTokenSource();

            System.Threading.Tasks.Task.Run(() =>
            {
                bool ok = true;
                if (needYtDlp && needFfmpeg)
                {
                    ok = YtDlManager.DownloadYtDlp(
                        (p, m) => { _firstRunProgress = p * 0.5f; _firstRunMsg = m; }, cts.Token);
                    if (ok)
                        ok = YtDlManager.DownloadFfmpeg(
                            (p, m) => { _firstRunProgress = 0.5f + p * 0.5f; _firstRunMsg = m; }, cts.Token);
                }
                else if (needYtDlp)
                    ok = YtDlManager.DownloadYtDlp(
                        (p, m) => { _firstRunProgress = p; _firstRunMsg = m; }, cts.Token);
                else if (needFfmpeg)
                    ok = YtDlManager.DownloadFfmpeg(
                        (p, m) => { _firstRunProgress = p; _firstRunMsg = m; }, cts.Token);

                if (ok) _firstRunDone   = true;
                else    _firstRunFailed = true;
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // Style initialization (lazy, called on first OnGUI frame)
        // ─────────────────────────────────────────────────────────────────

        private void InitStyles()
        {
            // Base label (font applied first so derived styles inherit it)
            _stLabel = new GUIStyle(GUI.skin.label)
            {
                normal   = { textColor = C_TEXT },
                fontSize = 13,
                wordWrap = false,
            };
            if (chineseFont != null) _stLabel.font = chineseFont;

            _stLabelMuted = new GUIStyle(_stLabel)
                { normal = { textColor = C_TEXT_MUTED }, fontSize = 12 };
            _stLabelBold  = new GUIStyle(_stLabel)
                { fontStyle = FontStyle.Bold };
            _stLabelOk    = new GUIStyle(_stLabel)
                { normal = { textColor = C_SUCCESS } };
            _stLabelWarn  = new GUIStyle(_stLabel)
                { normal = { textColor = C_WARNING } };
            _stLabelError = new GUIStyle(_stLabel)
                { normal = { textColor = C_PROG_ERR } };

            _stTitle = new GUIStyle(_stLabelBold)
            {
                fontSize = 22,
                normal   = { textColor = C_ACCENT },
            };

            _stInput = new GUIStyle(GUI.skin.textField)
            {
                normal  = { background = MakeTex(C_INPUT),       textColor = C_TEXT },
                focused = { background = MakeTex(C_PANEL_DARK),  textColor = C_TEXT },
                hover   = { background = MakeTex(C_INPUT),       textColor = C_TEXT },
                padding  = new RectOffset(8, 8, 4, 4),
                fontSize = 13,
            };
            if (chineseFont != null) _stInput.font = chineseFont;

            _stBtnNormal  = MakeBtnStyle(C_BTN,    C_BTN_HOVER, C_BORDER,    C_TEXT,      13);
            _stBtnAccent  = MakeBtnStyle(C_ACCENT, C_ACCENT_H,  C_ACCENT_H,  Color.white, 13);
            _stBtnDanger  = MakeBtnStyle(C_DANGER, C_DANGER_H,  C_DANGER_H,  C_TEXT,      12);
            _stBtnSmall   = MakeBtnStyle(C_BTN,    C_BTN_HOVER, C_BTN_HOVER, C_TEXT_MUTED,12);

            _stBtnDropdown = MakeBtnStyle(C_INPUT, C_BTN_HOVER, C_BORDER, C_TEXT, 13);
            _stBtnDropdown.alignment = TextAnchor.MiddleLeft;
            _stBtnDropdown.padding   = new RectOffset(10, 6, 0, 0);
            if (chineseFont != null) _stBtnDropdown.font = chineseFont;

            _stDropdownItem = new GUIStyle(GUI.skin.label)
            {
                normal    = { background = null, textColor = C_TEXT },
                hover     = { background = MakeTex(C_BTN_HOVER), textColor = C_TEXT },
                padding   = new RectOffset(10, 6, 4, 4),
                fontSize  = 13,
                alignment = TextAnchor.MiddleLeft,
            };
            if (chineseFont != null) _stDropdownItem.font = chineseFont;

            _stPanel = new GUIStyle(GUI.skin.box)
            {
                normal  = { background = MakeTex(C_PANEL) },
                padding = new RectOffset(0, 0, 0, 0),
                border  = new RectOffset(1, 1, 1, 1),
            };

            _stTabNormal = MakeBtnStyle(C_PANEL_DARK, C_BTN_HOVER, C_BORDER,  C_TEXT_MUTED, 12);
            _stTabActive = MakeBtnStyle(C_BLUE,       C_BLUE_H,    C_BLUE_H,  Color.white,  12);
            if (chineseFont != null)
            {
                _stTabNormal.font = chineseFont;
                _stTabActive.font = chineseFont;
                _stBtnNormal.font = chineseFont;
                _stBtnAccent.font = chineseFont;
                _stBtnDanger.font = chineseFont;
                _stBtnSmall.font  = chineseFont;
            }

            _stylesReady = true;
        }

        private GUIStyle MakeBtnStyle(Color bg, Color bgHover, Color bgActive,
            Color textColor, int fontSize)
        {
            return new GUIStyle(GUI.skin.button)
            {
                normal  = { background = MakeTex(bg),       textColor = textColor },
                hover   = { background = MakeTex(bgHover),  textColor = textColor },
                active  = { background = MakeTex(bgActive), textColor = textColor },
                focused = { background = MakeTex(bg),       textColor = textColor },
                padding   = new RectOffset(8, 8, 3, 3),
                fontSize  = fontSize,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private Texture2D MakeTex(Color col)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, col);
            t.Apply();
            t.hideFlags = HideFlags.DontSave;
            return t;
        }
    }
}
