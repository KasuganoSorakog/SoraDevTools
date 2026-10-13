using System;
using System.IO;
using UnityEngine.UIElements;

namespace YoutubeTools
{
    /// <summary>
    /// Settings overlay panel (position:absolute).
    /// Covers the whole window when open.
    /// </summary>
    public class SettingsPanel
    {
        private VisualElement _overlay;
        private TextField _ytDlpPathField;
        private TextField _ffmpegDirField;
        private DropdownField _cookieModeDropdown;
        private DropdownField _browserDropdown;
        private TextField _cookieFileField;
        private VisualElement _browserRow;
        private VisualElement _cookieFileRow;

        public Action OnClose;

        public VisualElement Build()
        {
            _overlay = new VisualElement();
            _overlay.AddToClassList("yt-settings-overlay");
            _overlay.style.display = DisplayStyle.None;

            // Clicking the dim background closes the panel
            _overlay.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target == _overlay) Close();
            });

            var card = new VisualElement();
            card.AddToClassList("yt-settings-card");
            _overlay.Add(card);

            // Title
            var title = new Label("设置");
            title.AddToClassList("yt-settings-title");
            card.Add(title);

            // ── yt-dlp / ffmpeg section ──
            AddSectionTitle(card, "工具路径");

            _ytDlpPathField = AddFieldRow(card, "yt-dlp 路径:", "留空使用默认位置");
            _ffmpegDirField = AddFieldRow(card, "ffmpeg 目录:", "留空使用默认位置");

            // ── Cookie section ──
            AddSectionTitle(card, "Cookie 认证");

            var cookieModeRow = new VisualElement();
            cookieModeRow.AddToClassList("yt-settings-row");
            var cookieModeLabel = new Label("Cookie 模式:");
            cookieModeLabel.AddToClassList("yt-settings-label");
            cookieModeRow.Add(cookieModeLabel);

            var cookieModeWrapper = new VisualElement();
            cookieModeWrapper.AddToClassList("yt-settings-field-wrapper");
            _cookieModeDropdown = new DropdownField(
                new System.Collections.Generic.List<string> { "不使用", "从浏览器", "指定文件" }, 0);
            _cookieModeDropdown.AddToClassList("yt-settings-dropdown");
            _cookieModeDropdown.RegisterValueChangedCallback(_ => UpdateCookieRowVisibility());
            cookieModeWrapper.Add(_cookieModeDropdown);
            cookieModeRow.Add(cookieModeWrapper);
            card.Add(cookieModeRow);

            // Browser choice row
            _browserRow = new VisualElement();
            _browserRow.AddToClassList("yt-settings-row");
            var browserLabel = new Label("浏览器:");
            browserLabel.AddToClassList("yt-settings-label");
            _browserRow.Add(browserLabel);
            var browserWrapper = new VisualElement();
            browserWrapper.AddToClassList("yt-settings-field-wrapper");
            _browserDropdown = new DropdownField(
                new System.Collections.Generic.List<string> { "Chrome", "Firefox", "Edge", "Opera" }, 0);
            _browserDropdown.AddToClassList("yt-settings-dropdown");
            browserWrapper.Add(_browserDropdown);
            _browserRow.Add(browserWrapper);
            card.Add(_browserRow);

            // Cookie file row
            _cookieFileRow = new VisualElement();
            _cookieFileRow.AddToClassList("yt-settings-row");
            var cFileLabel = new Label("Cookie 文件:");
            cFileLabel.AddToClassList("yt-settings-label");
            _cookieFileRow.Add(cFileLabel);
            var cookieFileWrapper = new VisualElement();
            cookieFileWrapper.AddToClassList("yt-settings-field-wrapper");
            _cookieFileField = new TextField();
            _cookieFileField.AddToClassList("yt-settings-field");
            cookieFileWrapper.Add(_cookieFileField);
            _cookieFileRow.Add(cookieFileWrapper);
            card.Add(_cookieFileRow);

            UpdateCookieRowVisibility();

            // ── Buttons ──
            var btnRow = new VisualElement();
            btnRow.AddToClassList("yt-settings-btn-row");

            var closeBtn = new Button(Close) { text = "取消" };
            closeBtn.AddToClassList("yt-btn-close");
            btnRow.Add(closeBtn);

            var saveBtn = new Button(SaveAndClose) { text = "保存" };
            saveBtn.AddToClassList("yt-btn-save");
            btnRow.Add(saveBtn);

            card.Add(btnRow);
            return _overlay;
        }

        public void Open()
        {
            // Load current settings
            var s = YoutubeSettings.Instance;
            _ytDlpPathField.value      = s.YtDlpPath;
            _ffmpegDirField.value      = s.FfmpegDir;
            _cookieModeDropdown.index  = s.CookieModeIndex;
            _browserDropdown.index     = s.BrowserChoiceIndex;
            _cookieFileField.value     = s.CookieFilePath;

            UpdateCookieRowVisibility();
            _overlay.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            _overlay.style.display = DisplayStyle.None;
            OnClose?.Invoke();
        }

        private void SaveAndClose()
        {
            var s = YoutubeSettings.Instance;
            s.YtDlpPath        = _ytDlpPathField.value.Trim();
            s.FfmpegDir        = _ffmpegDirField.value.Trim();
            s.CookieModeIndex  = _cookieModeDropdown.index;
            s.BrowserChoiceIndex = _browserDropdown.index;
            s.CookieFilePath   = _cookieFileField.value.Trim();
            s.Save();
            YoutubeSettings.ResetInstance(); // force reload
            Close();
        }

        private void UpdateCookieRowVisibility()
        {
            int mode = _cookieModeDropdown.index;
            _browserRow.style.display   = (mode == 1) ? DisplayStyle.Flex : DisplayStyle.None;
            _cookieFileRow.style.display = (mode == 2) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void AddSectionTitle(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("yt-settings-section-title");
            parent.Add(label);
        }

        private static TextField AddFieldRow(VisualElement parent, string labelText, string placeholder)
        {
            var row = new VisualElement();
            row.AddToClassList("yt-settings-row");

            var label = new Label(labelText);
            label.AddToClassList("yt-settings-label");
            row.Add(label);

            // Wrap TextField in plain VisualElement so flex-grow is handled reliably
            var fieldWrapper = new VisualElement();
            fieldWrapper.AddToClassList("yt-settings-field-wrapper");

            var field = new TextField();
            field.AddToClassList("yt-settings-field");
            fieldWrapper.Add(field);
            row.Add(fieldWrapper);

            parent.Add(row);
            return field;
        }
    }
}
