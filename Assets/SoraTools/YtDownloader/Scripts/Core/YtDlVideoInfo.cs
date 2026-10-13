using System;
using System.Collections.Generic;

namespace YtDownloader
{
    [Serializable]
    public class VideoInfo
    {
        public string Id          = "";
        public string Title       = "";
        public string Channel     = "";
        public string ChannelId   = "";
        public string Description = "";
        public long   Duration;
        public long   ViewCount;
        public long   LikeCount;
        public string UploadDate  = "";
        public string ThumbnailUrl = "";
        public string WebpageUrl  = "";
        public string Uploader    = "";
        public List<FormatInfo> Formats = new List<FormatInfo>();

        public string DurationDisplay
        {
            get
            {
                if (Duration <= 0) return "--:--";
                var ts = TimeSpan.FromSeconds(Duration);
                return ts.TotalHours >= 1
                    ? string.Format("{0}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds)
                    : string.Format("{0}:{1:D2}", ts.Minutes, ts.Seconds);
            }
        }

        public string ViewCountDisplay
        {
            get
            {
                if (ViewCount >= 1_000_000_000) return string.Format("{0:F1}B", ViewCount / 1_000_000_000.0);
                if (ViewCount >= 1_000_000)     return string.Format("{0:F1}M", ViewCount / 1_000_000.0);
                if (ViewCount >= 1_000)         return string.Format("{0:F1}K", ViewCount / 1_000.0);
                return ViewCount.ToString();
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
