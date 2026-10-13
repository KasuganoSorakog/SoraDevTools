using UnityEngine;
using UnityEngine.UIElements;

namespace YoutubeTools
{
    /// <summary>
    /// Thumbnail + video metadata display panel.
    /// </summary>
    public class VideoPreviewPanel
    {
        private VisualElement _thumbnailImg;
        private VisualElement _thumbnailPlaceholder;
        private Label _titleLabel;
        private Label _channelLabel;
        private Label _durationLabel;
        private Label _viewsLabel;
        private Label _likesLabel;
        private Label _dateLabel;

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("yt-preview-panel");

            // Thumbnail area
            _thumbnailPlaceholder = new VisualElement();
            _thumbnailPlaceholder.AddToClassList("yt-thumbnail-placeholder");
            var placeholderLabel = new Label("预览图");
            placeholderLabel.AddToClassList("yt-thumbnail-placeholder-label");
            _thumbnailPlaceholder.Add(placeholderLabel);
            root.Add(_thumbnailPlaceholder);

            _thumbnailImg = new VisualElement();
            _thumbnailImg.AddToClassList("yt-thumbnail");
            _thumbnailImg.style.display = DisplayStyle.None;
            root.Add(_thumbnailImg);

            // Metadata area
            var meta = new VisualElement();
            meta.AddToClassList("yt-meta-container");

            _titleLabel = new Label("--");
            _titleLabel.AddToClassList("yt-meta-title");
            meta.Add(_titleLabel);

            _channelLabel = new Label("--");
            _channelLabel.AddToClassList("yt-meta-channel");
            meta.Add(_channelLabel);

            var statsRow = new VisualElement();
            statsRow.AddToClassList("yt-meta-row");

            _durationLabel = CreateStat("时长", "--");
            _viewsLabel    = CreateStat("播放量", "--");
            _likesLabel    = new Label("--");
            _likesLabel.AddToClassList("yt-meta-stat");
            statsRow.Add(_durationLabel);
            statsRow.Add(_viewsLabel);
            statsRow.Add(_likesLabel);
            meta.Add(statsRow);

            _dateLabel = new Label("上传日期: --");
            _dateLabel.AddToClassList("yt-meta-date");
            meta.Add(_dateLabel);

            root.Add(meta);
            return root;
        }

        private static Label CreateStat(string label, string value)
        {
            var lbl = new Label(label + ": " + value);
            lbl.AddToClassList("yt-meta-stat");
            return lbl;
        }

        public void ShowVideo(VideoInfo info)
        {
            if (info == null) { Clear(); return; }

            _titleLabel.text   = info.Title;
            _channelLabel.text = "频道: " + info.Channel;
            _durationLabel.text = "时长: " + info.DurationDisplay;
            _viewsLabel.text    = "播放量: " + info.ViewCountDisplay;
            _likesLabel.text    = info.LikeRatioDisplay;
            _dateLabel.text     = "上传日期: " + info.UploadDateDisplay;
        }

        public void Clear()
        {
            _titleLabel.text    = "--";
            _channelLabel.text  = "--";
            _durationLabel.text = "时长: --";
            _viewsLabel.text    = "播放量: --";
            _likesLabel.text    = "--";
            _dateLabel.text     = "上传日期: --";
            HideThumbnail();
        }

        public void ShowThumbnail(Texture2D tex)
        {
            if (tex == null) return;
            _thumbnailPlaceholder.style.display = DisplayStyle.None;
            _thumbnailImg.style.display = DisplayStyle.Flex;
            _thumbnailImg.style.backgroundImage = new StyleBackground(tex);
            _thumbnailImg.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));
        }

        public void HideThumbnail()
        {
            _thumbnailPlaceholder.style.display = DisplayStyle.Flex;
            _thumbnailImg.style.display = DisplayStyle.None;
            _thumbnailImg.style.backgroundImage = StyleKeyword.None;
        }
    }
}
