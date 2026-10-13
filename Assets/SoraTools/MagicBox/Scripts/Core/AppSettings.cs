using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MagicBox.Core
{
    [Serializable]
    public class AppSettings
    {
        public string LastLibraryPath = "";
        public List<string> RecentLibraries = new List<string>();
        public int ViewMode = 0;
        public float ThumbnailSize = 130f;
        public bool ShowDetailPanel = true;
        public string Language = "en";
        public long ReferenceModeThreshold = 100 * 1024 * 1024;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MagicBox", "settings.json");

        private static AppSettings _instance;
        public static AppSettings Instance => _instance ?? (_instance = Load());

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(this, true));
            }
            catch (Exception e) { Debug.LogError($"[MagicBox] Save settings failed: {e.Message}"); }
        }

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var s = JsonUtility.FromJson<AppSettings>(File.ReadAllText(SettingsPath));
                    if (s != null) return s;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[MagicBox] Load settings failed: {e.Message}"); }
            return new AppSettings();
        }

        public void AddRecentLibrary(string path)
        {
            RecentLibraries.Remove(path);
            RecentLibraries.Insert(0, path);
            if (RecentLibraries.Count > 10) RecentLibraries.RemoveRange(10, RecentLibraries.Count - 10);
            LastLibraryPath = path;
            Save();
        }
    }
}
