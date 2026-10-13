using System;
using System.IO;
using UnityEngine;

namespace YtDownloader
{
    public enum CookieMode { None, Browser, File }
    public enum BrowserChoice { Chrome, Firefox, Edge, Opera }

    [Serializable]
    public class YtDlSettings
    {
        public string YtDlpPath   = "";
        public string FfmpegDir   = "";
        public string DefaultDownloadPath = "";
        public int FormatPresetIndex;
        public int QualityPresetIndex;
        public int CookieModeIndex;
        public int BrowserChoiceIndex;
        public string CookieFilePath = "";

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YtDownloader");

        private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

        private static YtDlSettings _instance;
        public static YtDlSettings Instance => _instance ?? (_instance = Load());

        public string ResolvedYtDlpPath =>
            string.IsNullOrEmpty(YtDlpPath) ? Path.Combine(AppDataDir, "yt-dlp.exe") : YtDlpPath;

        public string ResolvedFfmpegDir =>
            string.IsNullOrEmpty(FfmpegDir) ? AppDataDir : FfmpegDir;

        public static string GetAppDataDir() => AppDataDir;

        public bool YtDlpExists  => File.Exists(ResolvedYtDlpPath);
        public bool FfmpegExists => File.Exists(Path.Combine(ResolvedFfmpegDir, "ffmpeg.exe"));

        public VideoFormatPreset FormatPreset =>
            (VideoFormatPreset)Mathf.Clamp(FormatPresetIndex, 0,
                Enum.GetValues(typeof(VideoFormatPreset)).Length - 1);

        public QualityPreset QualityPreset =>
            (QualityPreset)Mathf.Clamp(QualityPresetIndex, 0,
                Enum.GetValues(typeof(QualityPreset)).Length - 1);

        public CookieMode CookieMode =>
            (CookieMode)Mathf.Clamp(CookieModeIndex, 0,
                Enum.GetValues(typeof(CookieMode)).Length - 1);

        public BrowserChoice BrowserChoice =>
            (BrowserChoice)Mathf.Clamp(BrowserChoiceIndex, 0,
                Enum.GetValues(typeof(BrowserChoice)).Length - 1);

        public string BuildCookieArgs()
        {
            switch (CookieMode)
            {
                case CookieMode.Browser:
                    return "--cookies-from-browser " + BrowserChoice.ToString().ToLowerInvariant();
                case CookieMode.File:
                    if (!string.IsNullOrEmpty(CookieFilePath) && File.Exists(CookieFilePath))
                        return "--cookies \"" + CookieFilePath + "\"";
                    return "";
                default:
                    return "";
            }
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(this, true));
            }
            catch (Exception e)
            {
                Debug.LogError("[YtDownloader] Save settings failed: " + e.Message);
            }
        }

        private static YtDlSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var s = JsonUtility.FromJson<YtDlSettings>(File.ReadAllText(SettingsPath));
                    if (s != null) return s;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YtDownloader] Load settings failed: " + e.Message);
            }
            return new YtDlSettings();
        }

        public static void ResetInstance() => _instance = null;
    }
}
