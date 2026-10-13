using System;
using System.IO;
using System.Linq;
using MagicBox.Core;
using MagicBox.Models;
using UnityEngine;

namespace MagicBox.Services
{
    public class LibraryManager
    {
        private static LibraryManager _instance;
        public static LibraryManager Instance => _instance ?? (_instance = new LibraryManager());

        private LibraryDatabase _db;
        private ResourceLibrary _currentLibrary;
        private string _lockFilePath;

        public LibraryDatabase Database => _db;
        public ResourceLibrary CurrentLibrary => _currentLibrary;
        public bool IsLibraryOpen => _db != null && _db.IsOpen;

        public event Action OnLibraryChanged;

        private LibraryManager() { _db = new LibraryDatabase(); }

        public bool CreateLibrary(string name, string rootPath)
        {
            try
            {
                if (!Directory.Exists(rootPath)) Directory.CreateDirectory(rootPath);
                string filesDir = Path.Combine(rootPath, "files");
                if (!Directory.Exists(filesDir)) Directory.CreateDirectory(filesDir);
                string thumbDir = Path.Combine(rootPath, "thumbnails");
                if (!Directory.Exists(thumbDir)) Directory.CreateDirectory(thumbDir);

                CreateSyncIgnoreFile(rootPath);

                var library = new ResourceLibrary(name, rootPath);
                File.WriteAllText(Path.Combine(rootPath, "library.json"), JsonUtility.ToJson(library, true));

                _db.Open(library.DatabasePath);
                _currentLibrary = library;
                WriteLockFile(rootPath);
                AppSettings.Instance.AddRecentLibrary(rootPath);
                OnLibraryChanged?.Invoke();
                Debug.Log($"[MagicBox] Created library '{name}' at {rootPath}");
                return true;
            }
            catch (Exception e) { Debug.LogError($"[MagicBox] Create library failed: {e.Message}"); return false; }
        }

        public bool OpenLibrary(string rootPath)
        {
            try
            {
                string metaPath = Path.Combine(rootPath, "library.json");
                if (!File.Exists(metaPath)) { Debug.LogError($"[MagicBox] Not a valid library: {rootPath}"); return false; }

                var library = JsonUtility.FromJson<ResourceLibrary>(File.ReadAllText(metaPath));
                if (library == null) return false;

                CheckSyncConflicts(rootPath);
                CheckLockFile(rootPath);

                library.DatabasePath = Path.Combine(rootPath, "magicbox.db");
                _db.Open(library.DatabasePath);
                _currentLibrary = library;
                WriteLockFile(rootPath);
                AppSettings.Instance.AddRecentLibrary(rootPath);
                OnLibraryChanged?.Invoke();
                Debug.Log($"[MagicBox] Opened library '{library.Name}' ({_db.GetResourceCount()} resources)");
                return true;
            }
            catch (Exception e) { Debug.LogError($"[MagicBox] Open library failed: {e.Message}"); return false; }
        }

        public void CloseLibrary()
        {
            _db?.Close();
            DeleteLockFile();
            _currentLibrary = null;
            OnLibraryChanged?.Invoke();
        }

        public string GetFilesDirectory() => _currentLibrary != null ? Path.Combine(_currentLibrary.RootPath, "files") : null;
        public string GetThumbnailsDirectory() => _currentLibrary != null ? Path.Combine(_currentLibrary.RootPath, "thumbnails") : null;

        public void TryAutoOpenLastLibrary()
        {
            string last = AppSettings.Instance.LastLibraryPath;
            if (!string.IsNullOrEmpty(last) && Directory.Exists(last)) OpenLibrary(last);
        }

        private void CreateSyncIgnoreFile(string rootPath)
        {
            string path = Path.Combine(rootPath, ".stignore");
            if (File.Exists(path)) return;
            File.WriteAllText(path,
                "// Syncthing ignore - SQLite temp files\n" +
                "magicbox.db-wal\n" +
                "magicbox.db-shm\n" +
                "magicbox.db-journal\n" +
                ".magicbox.lock\n");
        }

        private void CheckSyncConflicts(string rootPath)
        {
            var conflicts = Directory.GetFiles(rootPath, "magicbox.db.sync-conflict-*");
            if (conflicts.Length > 0)
                Debug.LogWarning($"[MagicBox] Found {conflicts.Length} sync conflict file(s). " +
                    "Changes from another machine may need manual review: " +
                    string.Join(", ", conflicts.Select(Path.GetFileName)));
        }

        private void CheckLockFile(string rootPath)
        {
            string path = Path.Combine(rootPath, ".magicbox.lock");
            if (!File.Exists(path)) return;
            try
            {
                string info = File.ReadAllText(path);
                Debug.LogWarning($"[MagicBox] Library may be in use on another machine: {info.Trim()}");
            }
            catch { }
        }

        private void WriteLockFile(string rootPath)
        {
            string path = Path.Combine(rootPath, ".magicbox.lock");
            try
            {
                File.WriteAllText(path, $"{Environment.MachineName}\n{DateTime.Now:o}");
                _lockFilePath = path;
            }
            catch { }
        }

        private void DeleteLockFile()
        {
            if (string.IsNullOrEmpty(_lockFilePath)) return;
            try { if (File.Exists(_lockFilePath)) File.Delete(_lockFilePath); } catch { }
            _lockFilePath = null;
        }
    }
}
