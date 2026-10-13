using System;
using UnityEngine.UIElements;

namespace YoutubeTools
{
    /// <summary>
    /// Format and quality DropdownField selectors.
    /// </summary>
    public class FormatSelectorPanel
    {
        private DropdownField _formatDropdown;
        private DropdownField _qualityDropdown;

        public Action<VideoFormatPreset, QualityPreset> OnSelectionChanged;

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("yt-format-panel");

            var fmtLabel = new Label("格式:");
            fmtLabel.AddToClassList("yt-format-label");
            root.Add(fmtLabel);

            _formatDropdown = new DropdownField(DownloadPreset.FormatLabels, 0);
            _formatDropdown.AddToClassList("yt-dropdown");
            _formatDropdown.RegisterValueChangedCallback(_ => OnChanged());
            root.Add(_formatDropdown);

            var qLabel = new Label("画质:");
            qLabel.AddToClassList("yt-format-label");
            root.Add(qLabel);

            _qualityDropdown = new DropdownField(DownloadPreset.QualityLabels, 0);
            _qualityDropdown.AddToClassList("yt-dropdown");
            _qualityDropdown.RegisterValueChangedCallback(_ => OnChanged());
            root.Add(_qualityDropdown);

            // Restore from settings
            var s = YoutubeSettings.Instance;
            _formatDropdown.index = UnityEngine.Mathf.Clamp(
                s.FormatPresetIndex, 0, DownloadPreset.FormatLabels.Count - 1);
            _qualityDropdown.index = UnityEngine.Mathf.Clamp(
                s.QualityPresetIndex, 0, DownloadPreset.QualityLabels.Count - 1);

            UpdateQualityVisibility();

            return root;
        }

        private void OnChanged()
        {
            UpdateQualityVisibility();

            var s = YoutubeSettings.Instance;
            s.FormatPresetIndex  = _formatDropdown.index;
            s.QualityPresetIndex = _qualityDropdown.index;
            s.Save();

            OnSelectionChanged?.Invoke(GetFormat(), GetQuality());
        }

        private void UpdateQualityVisibility()
        {
            bool audioOnly = DownloadPreset.IsAudioOnly(GetFormat());
            _qualityDropdown.style.display = audioOnly ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public VideoFormatPreset GetFormat() =>
            (VideoFormatPreset)_formatDropdown.index;

        public QualityPreset GetQuality() =>
            (QualityPreset)_qualityDropdown.index;

        /// <summary>
        /// Build yt-dlp format arguments for the current selection.
        /// </summary>
        public string[] BuildFormatArgs()
        {
            return DownloadPreset.BuildArguments(GetFormat(), GetQuality());
        }
    }
}
