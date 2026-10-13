using System;
using System.Collections.Generic;

namespace YoutubeTools
{
    /// <summary>
    /// Represents a single stream format returned by yt-dlp --dump-json.
    /// </summary>
    [Serializable]
    public class FormatInfo
    {
        public string FormatId = "";
        public string Ext = "";
        public int Width;
        public int Height;
        public float Fps;
        public long Filesize;
        public string VCodec = "";
        public string ACodec = "";
        public string FormatNote = "";

        public bool IsVideoOnly => !string.IsNullOrEmpty(VCodec) && VCodec != "none" &&
                                   (string.IsNullOrEmpty(ACodec) || ACodec == "none");
        public bool IsAudioOnly => (string.IsNullOrEmpty(VCodec) || VCodec == "none") &&
                                   !string.IsNullOrEmpty(ACodec) && ACodec != "none";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Download Preset
    // ─────────────────────────────────────────────────────────────────────────

    public enum VideoFormatPreset
    {
        Mp4H264,      // MP4 (H.264+AAC)
        Mp4H265,      // MP4 (H.265+AAC)
        WebmVP9,      // WebM (VP9+Opus)
        MkvBest,      // MKV (最佳质量)
        Mp3_320,      // MP3 320kbps
        Mp3_192,      // MP3 192kbps
        M4A,          // M4A (AAC)
        Wav,          // WAV (无损)
        Flac,         // FLAC
    }

    public enum QualityPreset
    {
        Best,
        P2160,
        P1440,
        P1080,
        P720,
        P480,
        P360,
        P240,
        P144,
        Worst,
    }

    /// <summary>
    /// Builds yt-dlp format arguments from user-selected preset + quality.
    /// </summary>
    public static class DownloadPreset
    {
        public static readonly List<string> FormatLabels = new List<string>
        {
            "MP4 (H.264+AAC)",
            "MP4 (H.265+AAC)",
            "WebM (VP9+Opus)",
            "MKV (最佳质量)",
            "MP3 320kbps",
            "MP3 192kbps",
            "M4A (AAC)",
            "WAV (无损)",
            "FLAC",
        };

        public static readonly List<string> QualityLabels = new List<string>
        {
            "最高画质",
            "2160p (4K)",
            "1440p (2K)",
            "1080p",
            "720p",
            "480p",
            "360p",
            "240p",
            "144p",
            "最低画质",
        };

        public static bool IsAudioOnly(VideoFormatPreset fmt)
        {
            return fmt == VideoFormatPreset.Mp3_320 ||
                   fmt == VideoFormatPreset.Mp3_192 ||
                   fmt == VideoFormatPreset.M4A ||
                   fmt == VideoFormatPreset.Wav ||
                   fmt == VideoFormatPreset.Flac;
        }

        /// <summary>
        /// Build the yt-dlp format/quality arguments list (not including URL, output path,
        /// --ffmpeg-location, or cookie args — those are added by DownloadService with proper quoting).
        /// </summary>
        public static string[] BuildArguments(VideoFormatPreset fmt, QualityPreset quality)
        {
            var args = new System.Collections.Generic.List<string>();

            // Quality height constraint (ignored for audio-only)
            string heightFilter = BuildHeightFilter(quality, fmt);

            switch (fmt)
            {
                case VideoFormatPreset.Mp4H264:
                    args.Add("-f");
                    // Prefer H.264 (avc1) streams explicitly to avoid AV1/VP9 in mp4 container.
                    // Falls back to best[ext=mp4] then best so the download always succeeds even
                    // when the source has no H.264 stream — DownloadService will transcode in that case.
                    args.Add($"bestvideo[vcodec^=avc1][ext=mp4]{heightFilter}+bestaudio[ext=m4a]" +
                             $"/bestvideo[vcodec^=avc1]{heightFilter}+bestaudio" +
                             $"/best[ext=mp4]{heightFilter}/best{heightFilter}");
                    args.Add("--merge-output-format"); args.Add("mp4");
                    break;

                case VideoFormatPreset.Mp4H265:
                    args.Add("-f");
                    args.Add($"bestvideo[vcodec^=av01]{heightFilter}+bestaudio/bestvideo{heightFilter}+bestaudio");
                    args.Add("--merge-output-format"); args.Add("mp4");
                    break;

                case VideoFormatPreset.WebmVP9:
                    args.Add("-f");
                    args.Add($"bestvideo[ext=webm]{heightFilter}+bestaudio[ext=webm]/best[ext=webm]{heightFilter}");
                    args.Add("--merge-output-format"); args.Add("webm");
                    break;

                case VideoFormatPreset.MkvBest:
                    args.Add("-f");
                    args.Add($"bestvideo{heightFilter}+bestaudio");
                    args.Add("--merge-output-format"); args.Add("mkv");
                    break;

                case VideoFormatPreset.Mp3_320:
                    args.Add("-x");
                    args.Add("--audio-format"); args.Add("mp3");
                    args.Add("--audio-quality"); args.Add("0");
                    break;

                case VideoFormatPreset.Mp3_192:
                    args.Add("-x");
                    args.Add("--audio-format"); args.Add("mp3");
                    args.Add("--audio-quality"); args.Add("192K");
                    break;

                case VideoFormatPreset.M4A:
                    args.Add("-x");
                    args.Add("--audio-format"); args.Add("m4a");
                    break;

                case VideoFormatPreset.Wav:
                    args.Add("-x");
                    args.Add("--audio-format"); args.Add("wav");
                    break;

                case VideoFormatPreset.Flac:
                    args.Add("-x");
                    args.Add("--audio-format"); args.Add("flac");
                    break;
            }

            return args.ToArray();
        }

        private static string BuildHeightFilter(QualityPreset q, VideoFormatPreset fmt)
        {
            if (IsAudioOnly(fmt)) return "";
            switch (q)
            {
                case QualityPreset.P2160: return "[height<=2160]";
                case QualityPreset.P1440: return "[height<=1440]";
                case QualityPreset.P1080: return "[height<=1080]";
                case QualityPreset.P720:  return "[height<=720]";
                case QualityPreset.P480:  return "[height<=480]";
                case QualityPreset.P360:  return "[height<=360]";
                case QualityPreset.P240:  return "[height<=240]";
                case QualityPreset.P144:  return "[height<=144]";
                case QualityPreset.Worst: return "[height<=144]";
                default: return ""; // Best
            }
        }
    }
}
