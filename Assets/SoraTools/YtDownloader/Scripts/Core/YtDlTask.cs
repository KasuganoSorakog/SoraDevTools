using System;

namespace YtDownloader
{
    public enum DownloadStatus
    {
        Idle, Downloading, Merging, Converting, Completed, Failed, Cancelled
    }

    public class DownloadTask
    {
        public int    TaskId;
        public string VideoTitle  = "";
        public string OutputPath  = "";
        public string Url         = "";

        public volatile float         ProgressPercent;
        public long                   DownloadedBytes;
        public long                   TotalBytes;
        public volatile float         SpeedBytesPerSec;
        public volatile int           EtaSeconds;
        public volatile DownloadStatus Status      = DownloadStatus.Idle;
        public volatile string         StatusMessage = "";

        public string SpeedDisplay
        {
            get
            {
                float s = SpeedBytesPerSec;
                if (s >= 1_048_576) return string.Format("{0:F1} MB/s", s / 1_048_576);
                if (s >= 1024)      return string.Format("{0:F1} KB/s", s / 1024);
                return string.Format("{0:F0} B/s", s);
            }
        }

        public string SizeDisplay
        {
            get
            {
                long d = DownloadedBytes, t = TotalBytes;
                string total = t > 0 ? FormatBytes(t) : "??";
                return FormatBytes(d) + "/" + total;
            }
        }

        public string EtaDisplay
        {
            get
            {
                int eta = EtaSeconds;
                if (eta <= 0) return "--";
                if (eta >= 3600) return string.Format("{0}h{1}m", eta / 3600, (eta % 3600) / 60);
                if (eta >= 60)   return string.Format("{0}m{1}s", eta / 60, eta % 60);
                return eta + "s";
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return string.Format("{0:F1} GB", bytes / 1_073_741_824.0);
            if (bytes >= 1_048_576)     return string.Format("{0:F1} MB", bytes / 1_048_576.0);
            if (bytes >= 1024)          return string.Format("{0:F1} KB", bytes / 1024.0);
            return bytes + " B";
        }
    }

    [Serializable]
    public class DownloadHistoryEntry
    {
        public string Title       = "";
        public string FilePath    = "";
        public long   FileSize;
        public string Url         = "";
        public string CompletedAt = "";

        public string FileSizeDisplay
        {
            get
            {
                if (FileSize >= 1_073_741_824) return string.Format("{0:F1} GB", FileSize / 1_073_741_824.0);
                if (FileSize >= 1_048_576)     return string.Format("{0:F1} MB", FileSize / 1_048_576.0);
                if (FileSize >= 1024)          return string.Format("{0:F1} KB", FileSize / 1024.0);
                return FileSize + " B";
            }
        }
    }

    [Serializable]
    public class DownloadHistoryList
    {
        public System.Collections.Generic.List<DownloadHistoryEntry> Entries =
            new System.Collections.Generic.List<DownloadHistoryEntry>();
    }
}
