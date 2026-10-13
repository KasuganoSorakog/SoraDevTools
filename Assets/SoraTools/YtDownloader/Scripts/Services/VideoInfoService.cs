using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace YtDownloader
{
    public class VideoInfoResult
    {
        public bool      IsSuccess;
        public string    ErrorMessage = "";
        public VideoInfo Video;
    }

    /// <summary>
    /// Calls yt-dlp --dump-json and parses the result into a VideoInfo.
    /// All methods must be called from a background thread.
    /// </summary>
    public class VideoInfoService
    {
        public VideoInfoResult FetchInfo(string url)
        {
            var result = new VideoInfoResult();

            if (string.IsNullOrWhiteSpace(url))
            { result.ErrorMessage = "URL 不能为空"; return result; }

            string cookieArgs = YtDlSettings.Instance.BuildCookieArgs();
            string ffmpegDir  = YtDlSettings.Instance.ResolvedFfmpegDir;
            string args = "--dump-json --no-playlist --ffmpeg-location \"" + ffmpegDir + "\"";
            if (!string.IsNullOrEmpty(cookieArgs))
                args += " " + cookieArgs;
            args += " \"" + url.Replace("\"", "\\\"") + "\"";

            var procResult = YtDlManager.RunYtDlp(args, timeoutMs: 60000);

            if (string.IsNullOrWhiteSpace(procResult.Output))
            {
                result.ErrorMessage = string.IsNullOrEmpty(procResult.Error)
                    ? "yt-dlp 未返回数据"
                    : procResult.Error.Trim();
                return result;
            }

            try
            {
                result.Video      = ParseJson(procResult.Output);
                result.IsSuccess  = result.Video.IsValid;
                if (!result.IsSuccess) result.ErrorMessage = "无法解析视频信息";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "解析失败: " + ex.Message;
                Debug.LogError("[VideoInfoService] Parse error: " + ex);
            }

            return result;
        }

        private static VideoInfo ParseJson(string json)
        {
            var info = new VideoInfo();
            info.Id           = ExtractString(json, "id");
            info.Title        = UnescapeJson(ExtractString(json, "title"));
            info.Channel      = UnescapeJson(ExtractString(json, "channel"));
            info.ChannelId    = ExtractString(json, "channel_id");
            info.Uploader     = UnescapeJson(ExtractString(json, "uploader"));
            info.ThumbnailUrl = UnescapeJson(ExtractString(json, "thumbnail"));
            info.WebpageUrl   = UnescapeJson(ExtractString(json, "webpage_url"));
            info.UploadDate   = ExtractString(json, "upload_date");
            info.Duration     = ExtractLong(json, "duration");
            info.ViewCount    = ExtractLong(json, "view_count");
            info.LikeCount    = ExtractLong(json, "like_count");
            info.Formats      = ParseFormats(json);

            if (string.IsNullOrEmpty(info.Channel)) info.Channel = info.Uploader;
            return info;
        }

        private static List<FormatInfo> ParseFormats(string json)
        {
            var formats = new List<FormatInfo>();
            int startIdx = json.IndexOf("\"formats\":", StringComparison.Ordinal);
            if (startIdx < 0) return formats;

            int arrayStart = json.IndexOf('[', startIdx);
            if (arrayStart < 0) return formats;

            int depth = 0, arrayEnd = arrayStart;
            for (int i = arrayStart; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[' || c == '{') depth++;
                else if (c == ']' || c == '}') { depth--; if (depth == 0) { arrayEnd = i; break; } }
            }

            string block = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
            foreach (var entry in SplitFormatObjects(block))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var f = new FormatInfo
                {
                    FormatId   = ExtractString(entry, "format_id"),
                    Ext        = ExtractString(entry, "ext"),
                    VCodec     = ExtractString(entry, "vcodec"),
                    ACodec     = ExtractString(entry, "acodec"),
                    FormatNote = ExtractString(entry, "format_note"),
                    Width      = (int)ExtractLong(entry, "width"),
                    Height     = (int)ExtractLong(entry, "height"),
                    Fps        = ExtractFloat(entry, "fps"),
                    Filesize   = ExtractLong(entry, "filesize"),
                };
                if (f.Filesize == 0) f.Filesize = ExtractLong(entry, "filesize_approx");
                formats.Add(f);
            }
            return formats;
        }

        private static List<string> SplitFormatObjects(string block)
        {
            var results = new List<string>();
            int depth = 0, start = -1;
            for (int i = 0; i < block.Length; i++)
            {
                char c = block[i];
                if (c == '{') { if (depth == 0) start = i; depth++; }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    { results.Add(block.Substring(start + 1, i - start - 1)); start = -1; }
                }
            }
            return results;
        }

        private static string ExtractString(string json, string key)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"";
            var m = Regex.Match(json, pattern);
            return m.Success ? m.Groups[1].Value : "";
        }

        private static long ExtractLong(string json, string key)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)";
            var m = Regex.Match(json, pattern);
            return m.Success && long.TryParse(m.Groups[1].Value, out long v) ? v : 0L;
        }

        private static float ExtractFloat(string json, string key)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?[\\d.]+)";
            var m = Regex.Match(json, pattern);
            return m.Success && float.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\n", "\n").Replace("\\t", "\t")
                    .Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\/", "/");
        }
    }
}
