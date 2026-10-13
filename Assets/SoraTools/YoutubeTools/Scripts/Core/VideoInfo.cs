using System;
using System.Collections.Generic;

namespace YoutubeTools
{
    /// <summary>
    /// Metadata for a YouTube video, parsed from yt-dlp --dump-json output.
    /// </summary>
    [Serializable]
    public class VideoInfo
    {
        public string Id = "";
        public string Title = "";
        public string Channel = "";
        public string ChannelId = "";
        public string Description = "";
        public long Duration;          // seconds
        public long ViewCount;
        public long LikeCount;
        public long DislikeCount;
        public string UploadDate = ""; // YYYYMMDD
        public string ThumbnailUrl = "";
        public string WebpageUrl = "";
        public string Uploader = "";
        public List<FormatInfo> Formats = new List<FormatInfo>();

        // ── Derived helpers ──

        public string DurationDisplay
        {
            get
            {
                if (Duration <= 0) return "--:--";
                var ts = TimeSpan.FromSeconds(Duration);
                return ts.TotalHours >= 1
                    ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                    : $"{ts.Minutes}:{ts.Seconds:D2}";
            }
        }

        public string ViewCountDisplay
        {
            get
            {
                if (ViewCount >= 1_000_000_000) return $"{ViewCount / 1_000_000_000.0:F1}B";
                if (ViewCount >= 1_000_000) return $"{ViewCount / 1_000_000.0:F1}M";
                if (ViewCount >= 1_000) return $"{ViewCount / 1_000.0:F1}K";
                return ViewCount.ToString();
            }
        }

        public string LikeRatioDisplay
        {
            get
            {
                long total = LikeCount + DislikeCount;
                if (total <= 0 || LikeCount <= 0) return "--";
                return $"\uD83D\uDC4D {(LikeCount * 100.0 / total):F0}%";
            }
        }

        public string UploadDateDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(UploadDate) || UploadDate.Length < 8) return UploadDate;
                if (DateTime.TryParseExact(UploadDate, "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                    return dt.ToString("yyyy-MM-dd");
                return UploadDate;
            }
        }

        public bool IsValid => !string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(Title);
    }
}
