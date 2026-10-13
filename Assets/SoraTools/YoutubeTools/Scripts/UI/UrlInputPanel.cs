using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace YoutubeTools
{
    /// <summary>
    /// URL input bar with fetch button and status label.
    /// </summary>
    public class UrlInputPanel
    {
        private TextField _urlField;
        private Button _fetchBtn;
        private Label _statusLabel;

        public Action<string> OnFetch;

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("yt-url-panel");

            var row = new VisualElement();
            row.AddToClassList("yt-url-row");

            // UIToolkit 中 TextField（复合控件）直接使用 flex-grow 时，
            // 内部布局可能导致相邻按钮被挤出视区。
            // 解决方案：用普通 VisualElement 作为 wrapper 承担 flex-grow，
            // TextField 填满 wrapper 即可，这样 flex 计算完全可靠。
            var fieldWrapper = new VisualElement();
            fieldWrapper.AddToClassList("yt-url-field-wrapper");

            _urlField = new TextField();
            _urlField.AddToClassList("yt-url-field");
            _urlField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    TriggerFetch();
            });
            fieldWrapper.Add(_urlField);
            row.Add(fieldWrapper);

            _fetchBtn = new Button(TriggerFetch) { text = "获取视频信息" };
            _fetchBtn.AddToClassList("yt-btn-fetch");
            row.Add(_fetchBtn);

            root.Add(row);

            _statusLabel = new Label("");
            _statusLabel.AddToClassList("yt-status-label");
            root.Add(_statusLabel);

            return root;
        }

        private void TriggerFetch()
        {
            string url = _urlField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(url)) return;
            OnFetch?.Invoke(url);
        }

        public void SetStatus(string message, string style = "")
        {
            if (_statusLabel == null) return;
            _statusLabel.text = message;
            _statusLabel.RemoveFromClassList("ok");
            _statusLabel.RemoveFromClassList("error");
            _statusLabel.RemoveFromClassList("warn");
            if (!string.IsNullOrEmpty(style))
                _statusLabel.AddToClassList(style);
        }

        public void SetFetchEnabled(bool enabled)
        {
            if (_fetchBtn != null) _fetchBtn.SetEnabled(enabled);
        }

        public void SetUrl(string url)
        {
            if (_urlField != null) _urlField.value = url;
        }

        public string GetUrl() => _urlField?.value?.Trim() ?? "";
    }
}
