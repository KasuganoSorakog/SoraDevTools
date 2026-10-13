using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace YoutubeTools
{
    /// <summary>
    /// Main MonoBehaviour for YouTube Tools.
    /// Attach to a GameObject with a UIDocument component.
    /// Builds the UI programmatically and coordinates services.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class YoutubeToolsApp : MonoBehaviour
    {
        [Header("Window")]
        [SerializeField] private int windowWidth  = 1280;
        [SerializeField] private int windowHeight = 900;

        [Header("Styles")]
        [SerializeField] private StyleSheet mainStyleSheet;

        // ── Services ──
        private VideoInfoService       _videoInfoService;
        private ThumbnailService       _thumbnailService;
        private DownloadService        _downloadService;
        private DownloadHistoryService _historyService;

        // ── UI Panels ──
        private UrlInputPanel       _urlPanel;
        private VideoPreviewPanel   _previewPanel;
        private FormatSelectorPanel _formatPanel;
        private DownloadPanel       _downloadPanel;
        private SettingsPanel       _settingsPanel;

        // ── First-run overlay ──
        private VisualElement  _firstRunOverlay;
        private Label          _firstRunProgressLabel;
        private Button         _autoDownloadBtn;
        private volatile float  _firstRunProgress;
        private volatile string _firstRunMessage = "";
        private volatile bool   _firstRunDone;
        private volatile bool   _firstRunFailed;

        // ── State ──
        private VideoInfo     _currentVideo;
        private VisualElement _root;
        private UIDocument    _uiDoc;

        // ── Pending fetch result (written on background thread, read in Update) ──
        private volatile bool   _fetchResultReady;
        private VideoInfoResult _fetchResult;

        // ─────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _uiDoc = GetComponent<UIDocument>();

#if !UNITY_EDITOR
            Screen.SetResolution(windowWidth, windowHeight, false);
#endif
            InitServices();
        }

        private void Start()
        {
            BuildUI();

            if (!YoutubeSettings.Instance.YtDlpExists || !YoutubeSettings.Instance.FfmpegExists)
                ShowFirstRunOverlay();
        }

        private void Update()
        {
            // ── Poll fetch result ──
            if (_fetchResultReady)
            {
                _fetchResultReady = false;
                ApplyFetchResult(_fetchResult);
            }

            // ── Update active download rows ──
            foreach (var task in _downloadService.GetActiveTasks())
                _downloadPanel.UpdateTaskRow(task);

            // ── Process completed/failed/cancelled tasks ──
            while (_downloadService.TryDequeueCompleted(out var completed))
            {
                _downloadPanel.UpdateTaskRow(completed);
                if (completed.Status == DownloadStatus.Completed)
                {
                    AddToHistory(completed);
                    _downloadPanel.RefreshHistory();
                }
            }

            // ── Poll first-run overlay ──
            if (_firstRunOverlay != null &&
                _firstRunOverlay.style.display == DisplayStyle.Flex)
            {
                if (_firstRunProgressLabel != null && !string.IsNullOrEmpty(_firstRunMessage))
                    _firstRunProgressLabel.text = _firstRunMessage;

                if (_firstRunDone)
                {
                    _firstRunDone = false;
                    _firstRunOverlay.style.display = DisplayStyle.None;
                    _urlPanel?.SetStatus("yt-dlp 已就绪，请输入视频链接。", "ok");
                }
                else if (_firstRunFailed)
                {
                    _firstRunFailed = false;
                    if (_autoDownloadBtn != null) _autoDownloadBtn.SetEnabled(true);
                }
            }
        }

        private void OnDestroy()
        {
            _downloadService?.CancelAll();
        }

        // ─────────────────────────────────────────────────────────────────
        // Initialization
        // ─────────────────────────────────────────────────────────────────

        private void InitServices()
        {
            _videoInfoService = new VideoInfoService();
            _thumbnailService = new ThumbnailService(this);
            _downloadService  = new DownloadService();
            _historyService   = new DownloadHistoryService();
        }

        // ─────────────────────────────────────────────────────────────────
        // UI Construction
        // ─────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var panelRoot = _uiDoc.rootVisualElement;
            panelRoot.Clear();

            // Apply to panelRoot so absolute-positioned overlays also get styles
            if (mainStyleSheet != null)
                panelRoot.styleSheets.Add(mainStyleSheet);

            _root = new VisualElement();
            _root.AddToClassList("yt-root");
            panelRoot.Add(_root);

            // Title bar
            _root.Add(BuildTitleBar());

            // ── URL Input ──
            _urlPanel = new UrlInputPanel();
            _urlPanel.OnFetch = OnFetchRequested;
            _root.Add(_urlPanel.Build());

            // ── Video Preview ──
            _previewPanel = new VideoPreviewPanel();
            _root.Add(_previewPanel.Build());

            // ── Format selector ──
            _formatPanel = new FormatSelectorPanel();
            _root.Add(_formatPanel.Build());

            // Divider
            var divider = new VisualElement();
            divider.AddToClassList("yt-divider");
            _root.Add(divider);

            // ── Download panel ──
            _downloadPanel = new DownloadPanel();
            _downloadPanel.OnDownload = OnDownloadRequested;
            _root.Add(_downloadPanel.Build(_historyService));

            // ── Settings overlay (position:absolute inside _root) ──
            _settingsPanel = new SettingsPanel();
            _settingsPanel.OnClose = () => { };
            _root.Add(_settingsPanel.Build());

            // ── First-run overlay (position:absolute inside _root) ──
            _firstRunOverlay = BuildFirstRunOverlay();
            _root.Add(_firstRunOverlay);
        }

        private VisualElement BuildTitleBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("yt-titlebar");

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            var titleLabel = new Label("YouTube");
            titleLabel.AddToClassList("yt-titlebar__accent");
            titleLabel.style.fontSize = 20;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleRow.Add(titleLabel);

            var toolsLabel = new Label(" Tools");
            toolsLabel.AddToClassList("yt-titlebar__title");
            titleRow.Add(toolsLabel);
            bar.Add(titleRow);

            var settingsBtn = new Button(() => _settingsPanel?.Open()) { text = "设置" };
            settingsBtn.AddToClassList("yt-btn-settings");
            bar.Add(settingsBtn);

            return bar;
        }

        private VisualElement BuildFirstRunOverlay()
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("yt-firstrun-overlay");
            overlay.style.display = DisplayStyle.None;

            var card = new VisualElement();
            card.AddToClassList("yt-firstrun-card");
            overlay.Add(card);

            var icon = new Label("YouTube");
            icon.AddToClassList("yt-firstrun-icon");
            icon.style.color = new StyleColor(new Color(1f, 0f, 0f));
            card.Add(icon);

            var title = new Label("欢迎使用 YouTube Tools");
            title.AddToClassList("yt-firstrun-title");
            card.Add(title);

            var desc = new Label(
                "需要 yt-dlp 和 ffmpeg 才能下载视频。\n" +
                "点击「自动下载」从 GitHub 获取（约 100 MB），或选择「手动配置」指定已有路径。");
            desc.AddToClassList("yt-firstrun-desc");
            card.Add(desc);

            _firstRunProgressLabel = new Label("");
            _firstRunProgressLabel.AddToClassList("yt-firstrun-progress");
            card.Add(_firstRunProgressLabel);

            var btnRow = new VisualElement();
            btnRow.AddToClassList("yt-firstrun-btn-row");

            _autoDownloadBtn = new Button(StartAutoDownload) { text = "自动下载 yt-dlp + ffmpeg" };
            _autoDownloadBtn.AddToClassList("yt-btn-auto-download");
            btnRow.Add(_autoDownloadBtn);

            var manualBtn = new Button(() =>
            {
                overlay.style.display = DisplayStyle.None;
                _settingsPanel?.Open();
            }) { text = "跳过 / 手动配置" };
            manualBtn.AddToClassList("yt-btn-manual");
            btnRow.Add(manualBtn);

            card.Add(btnRow);
            return overlay;
        }

        private void ShowFirstRunOverlay()
        {
            if (_firstRunOverlay != null)
                _firstRunOverlay.style.display = DisplayStyle.Flex;
        }

        // ─────────────────────────────────────────────────────────────────
        // First-run: auto-download yt-dlp + ffmpeg
        // ─────────────────────────────────────────────────────────────────

        private void StartAutoDownload()
        {
            if (_autoDownloadBtn != null) _autoDownloadBtn.SetEnabled(false);
            _firstRunMessage = "正在准备...";
            _firstRunDone    = false;
            _firstRunFailed  = false;

            var cts = new CancellationTokenSource();

            bool needYtDlp  = !YoutubeSettings.Instance.YtDlpExists;
            bool needFfmpeg = !YoutubeSettings.Instance.FfmpegExists;

            Task.Run(() =>
            {
                bool ok = true;

                if (needYtDlp && needFfmpeg)
                {
                    // Both missing: yt-dlp 0-50%, ffmpeg 50-100%
                    ok = YtDlpManager.DownloadYtDlp((pct, msg) =>
                    {
                        _firstRunProgress = pct * 0.5f;
                        _firstRunMessage  = msg;
                    }, cts.Token);

                    if (!ok) { _firstRunFailed = true; return; }

                    ok = YtDlpManager.DownloadFfmpeg((pct, msg) =>
                    {
                        _firstRunProgress = 0.5f + pct * 0.5f;
                        _firstRunMessage  = msg;
                    }, cts.Token);
                }
                else if (needYtDlp)
                {
                    ok = YtDlpManager.DownloadYtDlp((pct, msg) =>
                    {
                        _firstRunProgress = pct;
                        _firstRunMessage  = msg;
                    }, cts.Token);
                }
                else if (needFfmpeg)
                {
                    ok = YtDlpManager.DownloadFfmpeg((pct, msg) =>
                    {
                        _firstRunProgress = pct;
                        _firstRunMessage  = msg;
                    }, cts.Token);
                }

                if (ok)
                    _firstRunDone = true;
                else
                    _firstRunFailed = true;
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // Fetch video info
        // ─────────────────────────────────────────────────────────────────

        private void OnFetchRequested(string url)
        {
            _urlPanel.SetStatus("正在获取视频信息...", "");
            _urlPanel.SetFetchEnabled(false);
            _downloadPanel.SetCurrentVideo(null);
            _previewPanel.Clear();
            _currentVideo     = null;
            _fetchResultReady = false;

            Task.Run(() =>
            {
                var result = _videoInfoService.FetchInfo(url);
                _fetchResult      = result;
                _fetchResultReady = true;  // volatile write — signals Update()
            });
        }

        private void ApplyFetchResult(VideoInfoResult result)
        {
            _urlPanel.SetFetchEnabled(true);

            if (!result.IsSuccess)
            {
                _urlPanel.SetStatus("错误: " + result.ErrorMessage, "error");
                return;
            }

            _currentVideo = result.Video;
            _previewPanel.ShowVideo(_currentVideo);
            _downloadPanel.SetCurrentVideo(_currentVideo);
            _urlPanel.SetStatus("获取成功: " + _currentVideo.Title, "ok");

            if (!string.IsNullOrEmpty(_currentVideo.ThumbnailUrl))
            {
                _thumbnailService.DownloadThumbnail(_currentVideo.ThumbnailUrl,
                    tex => _previewPanel.ShowThumbnail(tex));
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Download
        // ─────────────────────────────────────────────────────────────────

        private void OnDownloadRequested(string savePath)
        {
            if (_currentVideo == null)
            {
                _urlPanel.SetStatus("请先获取视频信息", "warn");
                return;
            }

            if (!YoutubeSettings.Instance.YtDlpExists)
            {
                _urlPanel.SetStatus("yt-dlp 未找到，请先完成首次配置", "error");
                ShowFirstRunOverlay();
                return;
            }

            if (!YoutubeSettings.Instance.FfmpegExists)
                _urlPanel.SetStatus("提示: ffmpeg 未找到，视频与音频可能无法合并", "warn");

            string[] formatArgs = _formatPanel.BuildFormatArgs();
            // Enable H.264 fallback transcoding only when MP4 (H.264+AAC) preset is selected
            bool ensureH264 = _formatPanel.GetFormat() == VideoFormatPreset.Mp4H264;

            var task = _downloadService.StartDownload(
                _currentVideo.WebpageUrl,
                _currentVideo.Title,
                savePath,
                formatArgs,
                ensureH264);

            _downloadPanel.AddTaskRow(task, t => _downloadService.CancelTask(t));
        }

        // ─────────────────────────────────────────────────────────────────
        // History
        // ─────────────────────────────────────────────────────────────────

        private void AddToHistory(DownloadTask task)
        {
            long fileSize = 0;
            if (File.Exists(task.OutputPath))
                fileSize = new FileInfo(task.OutputPath).Length;

            var entry = new DownloadHistoryEntry
            {
                Title       = task.VideoTitle,
                FilePath    = task.OutputPath,
                FileSize    = fileSize > 0 ? fileSize : task.TotalBytes,
                Url         = task.Url,
                CompletedAt = DateTime.Now.ToString("o"),
            };

            _historyService.Add(entry);
        }
    }
}
