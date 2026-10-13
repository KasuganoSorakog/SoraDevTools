using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace YoutubeTools
{
    /// <summary>
    /// CRUD for download history stored in %APPDATA%\YoutubeTools\history.json.
    /// Thread-safe via lock.
    /// </summary>
    public class DownloadHistoryService
    {
        private static readonly string HistoryPath = Path.Combine(
            YoutubeSettings.GetAppDataDir(), "history.json");

        private readonly object _lock = new object();
        private List<DownloadHistoryEntry> _entries;

        public DownloadHistoryService()
        {
            _entries = LoadFromDisk();
        }

        public List<DownloadHistoryEntry> GetAll()
        {
            lock (_lock)
            {
                return new List<DownloadHistoryEntry>(_entries);
            }
        }

        public void Add(DownloadHistoryEntry entry)
        {
            lock (_lock)
            {
                _entries.Insert(0, entry);
                if (_entries.Count > 100)
                    _entries.RemoveRange(100, _entries.Count - 100);
                Save();
            }
        }

        public void Remove(DownloadHistoryEntry entry)
        {
            lock (_lock)
            {
                _entries.Remove(entry);
                Save();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                Save();
            }
        }

        private void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(HistoryPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var wrapper = new DownloadHistoryList { Entries = _entries };
                File.WriteAllText(HistoryPath, JsonUtility.ToJson(wrapper, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[YoutubeTools] Save history failed: {e.Message}");
            }
        }

        private static List<DownloadHistoryEntry> LoadFromDisk()
        {
            try
            {
                if (File.Exists(HistoryPath))
                {
                    var wrapper = JsonUtility.FromJson<DownloadHistoryList>(File.ReadAllText(HistoryPath));
                    if (wrapper?.Entries != null) return wrapper.Entries;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[YoutubeTools] Load history failed: {e.Message}");
            }
            return new List<DownloadHistoryEntry>();
        }
    }
}
