using System;
using System.IO;
using UnityEngine;

namespace YoutubeTools
{
    public enum CookieMode
    {
        None,
        Browser,
        File,
    }

    public enum BrowserChoice
    {
        Chrome,
        Firefox,
        Edge,
        Opera,
    }

    /// <summary>
    /// Persistent settings singleton for YouTube Tools.
    /// Stored at %APPDATA%\YoutubeTools\settings.json.
    /// </summary>
    [Serializable]
    public class YoutubeSettings
    {
        // ── yt-dlp / ffmpeg paths ──
        public string YtDlpPath = "";      // empty → use AppDataPath default
        public string FfmpegDir = "";      // empty → use AppDataPath default

        // ── Download ──
        public string DefaultDownloadPath = "";
        public int FormatPresetIndex;      // VideoFormatPreset
        public int QualityPresetIndex;     // QualityPreset

        // ── Cookie ──
        public int CookieModeIndex;        // CookieMode
        public int BrowserChoiceIndex;     // BrowserChoice
        public string CookieFilePath = "";

        // ─────────────────────────────────────────────────────────────────

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YoutubeTools");

        private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

        private static YoutubeSettings _instance;
        public static YoutubeSettings Instance => _instance ?? (_instance = Load());

        // ── Computed paths ──

        public string ResolvedYtDlpPath =>
            string.IsNullOrEmpty(YtDlpPath)
                ? Path.Combine(AppDataDir, "yt-dlp.exe")
                : YtDlpPath;

        public string ResolvedFfmpegDir =>
            string.IsNullOrEmpty(FfmpegDir)
                ? AppDataDir
                : FfmpegDir;

        public static string GetAppDataDir() => AppDataDir;

        public bool YtDlpExists  => File.Exists(ResolvedYtDlpPath);
        public bool FfmpegExists => File.Exists(Path.Combine(ResolvedFfmpegDir, "ffmpeg.exe"));

        // ── Getters ──

        public VideoFormatPreset FormatPreset =>
            (VideoFormatPreset)Mathf.Clamp(FormatPresetIndex, 0,
                System.Enum.GetValues(typeof(VideoFormatPreset)).Length - 1);

        public QualityPreset QualityPreset =>
            (QualityPreset)Mathf.Clamp(QualityPresetIndex, 0,
                System.Enum.GetValues(typeof(QualityPreset)).Length - 1);

        public CookieMode CookieMode =>
            (CookieMode)Mathf.Clamp(CookieModeIndex, 0,
                System.Enum.GetValues(typeof(CookieMode)).Length - 1);

        public BrowserChoice BrowserChoice =>
            (BrowserChoice)Mathf.Clamp(BrowserChoiceIndex, 0,
                System.Enum.GetValues(typeof(BrowserChoice)).Length - 1);

        /// <summary>
        /// Returns the cookie argument string to append to yt-dlp invocations.
        /// </summary>
        public string BuildCookieArgs()
        {
            switch (CookieMode)
            {
                case CookieMode.Browser:
                    string browser = BrowserChoice.ToString().ToLowerInvariant();
                    return $"--cookies-from-browser {browser}";
                case CookieMode.File:
                    if (!string.IsNullOrEmpty(CookieFilePath) && File.Exists(CookieFilePath))
                        return $"--cookies \"{CookieFilePath}\"";
                    return "";
                default:
                    return "";
            }
        }

        // ── Persistence ──

        public void Save()
        {
            try
            {
                if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(this, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[YoutubeTools] Save settings failed: {e.Message}");
            }
        }

        private static YoutubeSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var s = JsonUtility.FromJson<YoutubeSettings>(File.ReadAllText(SettingsPath));
                    if (s != null) return s;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[YoutubeTools] Load settings failed: {e.Message}");
            }
            return new YoutubeSettings();
        }

        public static void ResetInstance() => _instance = null;
    }
}
