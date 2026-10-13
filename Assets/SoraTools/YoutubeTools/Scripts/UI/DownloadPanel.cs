using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SoraTools.Utils;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace YoutubeTools
{
    /// <summary>
    /// Save-path selector, parallel download queue rows, and download history list.
    /// </summary>
    public class DownloadPanel
    {
        private TextField     _pathField;
        private Button        _downloadBtn;
        private Button        _previewBtn;
        private VisualElement _activeContainer;
        private VisualElement _historyContainer;

        private DownloadHistoryService _history;
        private VideoInfo _currentVideo;

        public Action<string> OnDownload;  // passes savePath

        // ── Per-task row data ──

        private class TaskRowData
        {
            public VisualElement Row;
            public VisualElement Fill;
            public Label         StatusText;
            public Button        ActionBtn;
            public bool          IsDone;
            public Action        CancelAction;
        }

        private readonly Dictionary<int, TaskRowData> _taskRows = new Dictionary<int, TaskRowData>();

        // ─────────────────────────────────────────────────────────────────

        public VisualElement Build(DownloadHistoryService history)
        {
            _history = history;

            var root = new VisualElement();
            root.AddToClassList("yt-download-panel");

            // ── Save path row ──
            var pathRow = new VisualElement();
            pathRow.AddToClassList("yt-path-row");

            var pathLabel = new Label("保存路径:");
            pathLabel.AddToClassList("yt-path-label");
            pathRow.Add(pathLabel);

            _pathField = new TextField();
            _pathField.AddToClassList("yt-path-field");

            string defaultPath = YoutubeSettings.Instance.DefaultDownloadPath;
            if (string.IsNullOrEmpty(defaultPath))
                defaultPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyVideos);
            _pathField.value = defaultPath;
            pathRow.Add(_pathField);

            var browseBtn = new Button(BrowsePath) { text = "浏览..." };
            browseBtn.AddToClassList("yt-btn-browse");
            pathRow.Add(browseBtn);
            root.Add(pathRow);

            // ── Active downloads queue ──
            var activeSection = new VisualElement();
            activeSection.AddToClassList("yt-active-section");

            var activeHeader = new Label("下载队列");
            activeHeader.AddToClassList("yt-history-title");
            activeSection.Add(activeHeader);

            _activeContainer = new ScrollView(ScrollViewMode.Vertical);
            _activeContainer.AddToClassList("yt-scrollview");
            _activeContainer.style.maxHeight = 180;
            activeSection.Add(_activeContainer);

            root.Add(activeSection);

            // ── Action buttons ──
            var actionRow = new VisualElement();
            actionRow.AddToClassList("yt-action-row");

            _previewBtn = new Button(OpenInBrowser) { text = "在浏览器预览" };
            _previewBtn.AddToClassList("yt-btn-preview");
            _previewBtn.SetEnabled(false);
            actionRow.Add(_previewBtn);

            _downloadBtn = new Button(TriggerDownload) { text = "开始下载" };
            _downloadBtn.AddToClassList("yt-btn-download");
            _downloadBtn.SetEnabled(false);
            actionRow.Add(_downloadBtn);

            root.Add(actionRow);

            // ── History section ──
            var historySection = new VisualElement();
            historySection.AddToClassList("yt-history-section");

            var historyHeader = new VisualElement();
            historyHeader.AddToClassList("yt-history-header");
            var historyTitle = new Label("下载历史");
            historyTitle.AddToClassList("yt-history-title");
            historyHeader.Add(historyTitle);
            var clearAllBtn = new Button(() =>
            {
                _history?.Clear();
                RefreshHistory();
            }) { text = "清空全部" };
            clearAllBtn.AddToClassList("yt-history-clear-btn");
            historyHeader.Add(clearAllBtn);
            historySection.Add(historyHeader);

            _historyContainer = new ScrollView(ScrollViewMode.Vertical);
            _historyContainer.AddToClassList("yt-scrollview");
            _historyContainer.style.maxHeight = 200;
            historySection.Add(_historyContainer);

            root.Add(historySection);

            RefreshHistory();
            return root;
        }

        // ── Task row management (called from main thread via Update) ──

        /// <summary>Add a row for a newly started download task.</summary>
        public void AddTaskRow(DownloadTask task, Action<DownloadTask> onCancel)
        {
            var row = new VisualElement();
            row.AddToClassList("yt-active-task");

            var titleLabel = new Label(
                string.IsNullOrEmpty(task.VideoTitle) ? "下载中..." : task.VideoTitle);
            titleLabel.AddToClassList("yt-active-task-title");
            row.Add(titleLabel);

            var progressBg = new VisualElement();
            progressBg.AddToClassList("yt-progress-bg");

            var fill = new VisualElement();
            fill.AddToClassList("yt-progress-fill");
            fill.style.width = Length.Percent(0);
            progressBg.Add(fill);
            row.Add(progressBg);

            var statusText = new Label("准备下载...");
            statusText.AddToClassList("yt-progress-text");
            row.Add(statusText);

            var data = new TaskRowData
            {
                Row          = row,
                Fill         = fill,
                StatusText   = statusText,
                CancelAction = () => onCancel?.Invoke(task),
            };

            var actionBtn = new Button(() => data.CancelAction?.Invoke()) { text = "取消" };
            actionBtn.AddToClassList("yt-btn-cancel");
            row.Add(actionBtn);
            data.ActionBtn = actionBtn;

            _taskRows[task.TaskId] = data;
            _activeContainer.Add(row);
        }

        /// <summary>
        /// Refresh the row for a task. When the task reaches a terminal state the first time,
        /// replaces the "取消" button with "关闭".
        /// </summary>
        public void UpdateTaskRow(DownloadTask task)
        {
            if (!_taskRows.TryGetValue(task.TaskId, out var data)) return;

            data.Fill.style.width = Length.Percent(task.ProgressPercent);

            bool terminal = task.Status == DownloadStatus.Completed ||
                            task.Status == DownloadStatus.Failed    ||
                            task.Status == DownloadStatus.Cancelled;

            if (terminal && !data.IsDone)
            {
                data.IsDone      = true;
                data.CancelAction = null;
                int id = task.TaskId;
                data.ActionBtn.text = "关闭";
                // Replace old click handler with close handler
                data.ActionBtn.clicked += () => RemoveTaskRow(id);
            }

            switch (task.Status)
            {
                case DownloadStatus.Completed:
                    data.Fill.AddToClassList("done");
                    data.StatusText.text = "下载完成!";
                    break;
                case DownloadStatus.Failed:
                    data.StatusText.text = "失败: " + task.StatusMessage;
                    break;
                case DownloadStatus.Cancelled:
                    data.StatusText.text = "已取消";
                    break;
                default:
                    data.StatusText.text = task.StatusMessage;
                    break;
            }
        }

        private void RemoveTaskRow(int taskId)
        {
            if (!_taskRows.TryGetValue(taskId, out var data)) return;
            _activeContainer.Remove(data.Row);
            _taskRows.Remove(taskId);
        }

        // ── Video state ──

        public void SetCurrentVideo(VideoInfo video)
        {
            _currentVideo = video;
            bool has = video != null;
            _downloadBtn.SetEnabled(has);
            _previewBtn.SetEnabled(has);
        }

        // ── History ──

        public void RefreshHistory()
        {
            _historyContainer.Clear();
            var entries = _history?.GetAll() ?? new List<DownloadHistoryEntry>();
            if (entries.Count == 0)
            {
                var empty = new Label("暂无下载记录");
                empty.AddToClassList("yt-empty-label");
                _historyContainer.Add(empty);
                return;
            }
            foreach (var entry in entries)
                _historyContainer.Add(BuildHistoryItem(entry));
        }

        private VisualElement BuildHistoryItem(DownloadHistoryEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("yt-history-item");

            var icon = new Label("[完]");
            icon.AddToClassList("yt-history-item-icon");
            row.Add(icon);

            var name = new Label(Path.GetFileName(entry.FilePath));
            name.AddToClassList("yt-history-item-name");
            row.Add(name);

            var size = new Label(entry.FileSizeDisplay);
            size.AddToClassList("yt-history-item-size");
            row.Add(size);

            var openBtn = new Button(() => OpenFile(entry)) { text = "打开" };
            openBtn.AddToClassList("yt-history-item-btn");
            row.Add(openBtn);

            var locBtn = new Button(() => ShowInExplorer(entry)) { text = "位置" };
            locBtn.AddToClassList("yt-history-item-btn");
            row.Add(locBtn);

            var delBtn = new Button(() =>
            {
                _history?.Remove(entry);
                RefreshHistory();
            }) { text = "删除" };
            delBtn.AddToClassList("yt-history-item-btn");
            delBtn.AddToClassList("yt-history-item-btn--danger");
            row.Add(delBtn);

            return row;
        }

        // ── Button handlers ──

        private void TriggerDownload()
        {
            string savePath = _pathField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(savePath))
            {
                Debug.LogWarning("[DownloadPanel] No save path specified.");
                return;
            }
            if (!Directory.Exists(savePath))
            {
                try { Directory.CreateDirectory(savePath); }
                catch (Exception e)
                {
                    Debug.LogError("[DownloadPanel] Cannot create directory: " + e.Message);
                    return;
                }
            }
            YoutubeSettings.Instance.DefaultDownloadPath = savePath;
            YoutubeSettings.Instance.Save();
            OnDownload?.Invoke(savePath);
        }

        private void BrowsePath()
        {
#if UNITY_EDITOR
            string selected = UnityEditor.EditorUtility.OpenFolderPanel("选择保存文件夹", _pathField.value, "");
            if (!string.IsNullOrEmpty(selected))
                _pathField.value = selected;
#else
            try
            {
                string selected = Win32Dialog.OpenFolderDialog("选择保存文件夹");
                if (!string.IsNullOrEmpty(selected))
                    _pathField.value = selected;
            }
            catch { }
#endif
        }

        private void OpenInBrowser()
        {
            if (_currentVideo == null || string.IsNullOrEmpty(_currentVideo.WebpageUrl)) return;
            try { Process.Start(_currentVideo.WebpageUrl); } catch { }
        }

        private static void OpenFile(DownloadHistoryEntry entry)
        {
            if (File.Exists(entry.FilePath))
                try { Process.Start(entry.FilePath); } catch { }
        }

        private static void ShowInExplorer(DownloadHistoryEntry entry)
        {
            if (File.Exists(entry.FilePath))
                try { Process.Start("explorer.exe", "/select,\"" + entry.FilePath + "\""); } catch { }
            else if (Directory.Exists(Path.GetDirectoryName(entry.FilePath)))
                try { Process.Start("explorer.exe", Path.GetDirectoryName(entry.FilePath)); } catch { }
        }
    }
}
