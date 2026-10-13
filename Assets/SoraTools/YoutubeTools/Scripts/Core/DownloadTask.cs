using System;

namespace YoutubeTools
{
    public enum DownloadStatus
    {
        Idle,
        Downloading,
        Merging,
        Converting,   // ffmpeg re-encoding to H.264 (fallback for non-H264 sources)
        Completed,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// Tracks the state of an in-progress download.
    /// Fields marked volatile are written from background threads and read by Update().
    /// </summary>
    public class DownloadTask
    {
        public int    TaskId;
        public string VideoTitle = "";
        public string OutputPath = "";
        public string Url = "";

        // Written by background thread (long cannot be volatile in C#; reads are display-only)
        public volatile float ProgressPercent;    // 0..100
        public long DownloadedBytes;
        public long TotalBytes;
        public volatile float SpeedBytesPerSec;
        public volatile int EtaSeconds;
        public volatile DownloadStatus Status = DownloadStatus.Idle;
        public volatile string StatusMessage = "";

        public string SpeedDisplay
        {
            get
            {
                float s = SpeedBytesPerSec;
                if (s >= 1_048_576) return $"{s / 1_048_576:F1} MB/s";
                if (s >= 1024)      return $"{s / 1024:F1} KB/s";
                return $"{s:F0} B/s";
            }
        }

        public string SizeDisplay
        {
            get
            {
                long d = DownloadedBytes, t = TotalBytes;
                string total = t > 0 ? FormatBytes(t) : "??";
                return $"{FormatBytes(d)}/{total}";
            }
        }

        public string EtaDisplay
        {
            get
            {
                int eta = EtaSeconds;
                if (eta <= 0) return "--";
                if (eta >= 3600) return $"{eta / 3600}h{(eta % 3600) / 60}m";
                if (eta >= 60)   return $"{eta / 60}m{eta % 60}s";
                return $"{eta}s";
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1024)          return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A completed download entry stored in history.json.
    /// </summary>
    [Serializable]
    public class DownloadHistoryEntry
    {
        public string Title = "";
        public string FilePath = "";
        public long FileSize;
        public string Url = "";
        public string CompletedAt = ""; // ISO 8601

        public string FileSizeDisplay
        {
            get
            {
                if (FileSize >= 1_073_741_824) return $"{FileSize / 1_073_741_824.0:F1} GB";
                if (FileSize >= 1_048_576)     return $"{FileSize / 1_048_576.0:F1} MB";
                if (FileSize >= 1024)          return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize} B";
            }
        }
    }

    /// <summary>
    /// Wrapper for serializing a list of history entries via JsonUtility.
    /// </summary>
    [Serializable]
    public class DownloadHistoryList
    {
        public System.Collections.Generic.List<DownloadHistoryEntry> Entries =
            new System.Collections.Generic.List<DownloadHistoryEntry>();
    }
}
