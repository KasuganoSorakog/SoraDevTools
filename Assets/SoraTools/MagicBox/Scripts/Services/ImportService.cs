using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MagicBox.Core;
using MagicBox.Models;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MagicBox.Services
{
    /// <summary>
    /// Progress info reported during batch import.
    /// </summary>
    public class ImportProgress
    {
        public int TotalFiles;
        public int CurrentIndex;
        public string CurrentFileName;
        public int ImportedCount;
        public int SkippedCount;
        public int FailedCount;
        public bool IsComplete;
    }

    public class ImportService
    {
        private readonly LibraryManager _lib;
        public ImportService(LibraryManager lib) { _lib = lib; }

        public event Action<int, int> OnImportProgress;

        // ============ Sync API (kept for backward compatibility) ============

        public List<ResourceItem> ImportFiles(string[] filePaths, string folderId = null)
        {
            if (!_lib.IsLibraryOpen) return new List<ResourceItem>();
            _tagCache = null;
            var imported = new List<ResourceItem>();
            for (int i = 0; i < filePaths.Length; i++)
            {
                OnImportProgress?.Invoke(i + 1, filePaths.Length);
                try
                {
                    var item = ImportSingleFile(filePaths[i], folderId);
                    if (item != null) imported.Add(item);
                }
                catch (Exception e) { Debug.LogWarning($"[MagicBox] Import failed '{filePaths[i]}': {e.Message}"); }
            }
            return imported;
        }

        public ResourceItem ImportSingleFile(string filePath, string folderId = null)
        {
            bool isDir = Directory.Exists(filePath);
            if (!isDir && !File.Exists(filePath)) return null;

            var fi = isDir ? null : new FileInfo(filePath);
            string fileName = Path.GetFileName(filePath);
            string ext = isDir ? "" : Path.GetExtension(filePath);
            long fileSize = isDir ? GetDirSize(filePath) : fi.Length;

            string hash = isDir ? "" : ComputeHash(filePath);
            if (!string.IsNullOrEmpty(hash) && _lib.Database.ResourceExistsByHash(hash))
            { Debug.Log($"[MagicBox] Skipping duplicate: {fileName}"); return null; }

            var type = isDir ? ResourceType.Folder : ResourceItem.DetectType(ext);
            bool useRef = fileSize >= AppSettings.Instance.ReferenceModeThreshold;

            string relPath = useRef ? "" : CopyToLibrary(filePath, isDir);

            var item = new ResourceItem
            {
                Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName, RelativePath = relPath, OriginalPath = filePath,
                Type = type, FileSize = fileSize, FileHash = hash, Rating = 0,
                Version = "", Description = "", SourceUrl = "", UnityVersionMin = "", UnityVersionMax = "",
                ThumbnailPath = "", IsReference = useRef,
                ImportedAt = DateTime.Now.ToString("o"),
                FileModifiedAt = fi?.LastWriteTime.ToString("o") ?? DateTime.Now.ToString("o"),
                FolderId = string.IsNullOrEmpty(folderId) ? null : folderId,
                TagIds = new List<string>()
            };

            AutoAddTags(item);
            _lib.Database.InsertResource(item);
            return item;
        }

        // ============ Async API ============

        private CancellationTokenSource _importCts;
        private ImportProgress _currentProgress;
        private volatile bool _isImporting;

        /// <summary>True if an async import is running.</summary>
        public bool IsImporting => _isImporting;

        /// <summary>Current progress snapshot (poll from UI Update loop).</summary>
        public ImportProgress CurrentProgress => _currentProgress;

        /// <summary>Cancel the running async import.</summary>
        public void CancelImport() => _importCts?.Cancel();

        /// <summary>
        /// Start importing files on a background thread.
        /// Call <see cref="CurrentProgress"/> from the UI thread to poll progress.
        /// <paramref name="onComplete"/> is invoked on the background thread when done.
        /// </summary>
        public void ImportFilesAsync(string[] filePaths, string folderId, Action<List<ResourceItem>> onComplete)
        {
            if (!_lib.IsLibraryOpen || _isImporting) return;

            _importCts = new CancellationTokenSource();
            _isImporting = true;
            _tagCache = null;
            _currentProgress = new ImportProgress
            {
                TotalFiles = filePaths.Length,
                CurrentIndex = 0,
                CurrentFileName = "",
                ImportedCount = 0,
                SkippedCount = 0,
                FailedCount = 0,
                IsComplete = false
            };

            var ct = _importCts.Token;

            Task.Run(() =>
            {
                var imported = new List<ResourceItem>();
                try
                {
                    for (int i = 0; i < filePaths.Length; i++)
                    {
                        if (ct.IsCancellationRequested) break;

                        _currentProgress.CurrentIndex = i + 1;
                        _currentProgress.CurrentFileName = Path.GetFileName(filePaths[i]);

                        try
                        {
                            var item = _lib.Database.RunLocked(() => ImportSingleFile(filePaths[i], folderId));
                            if (item != null)
                            {
                                imported.Add(item);
                                _currentProgress.ImportedCount++;
                            }
                            else
                            {
                                _currentProgress.SkippedCount++;
                            }
                        }
                        catch (Exception e)
                        {
                            _currentProgress.FailedCount++;
                            Debug.LogWarning($"[MagicBox] Import failed '{filePaths[i]}': {e.Message}");
                        }
                    }
                }
                finally
                {
                    _currentProgress.IsComplete = true;
                    _isImporting = false;
                    _importCts = null;
                    _tagCache = null;
                }
                onComplete?.Invoke(imported);
            }, ct);
        }

        /// <summary>Async version of ScanAndImportFolder.</summary>
        public void ScanAndImportFolderAsync(string folderPath, string folderId, Action<List<ResourceItem>> onComplete)
        {
            if (!Directory.Exists(folderPath)) { onComplete?.Invoke(new List<ResourceItem>()); return; }
            var files = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(folderPath))
                {
                    string n = Path.GetFileName(f);
                    if (!n.StartsWith(".") && n != "Thumbs.db" && n != "desktop.ini") files.Add(f);
                }
                foreach (var d in Directory.GetDirectories(folderPath))
                {
                    string n = Path.GetFileName(d);
                    if (!n.StartsWith(".") && !n.StartsWith("#")) files.Add(d);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[MagicBox] Scan error: {e.Message}"); }
            ImportFilesAsync(files.ToArray(), folderId, onComplete);
        }

        // ============ Sync helpers (unchanged) ============

        public List<ResourceItem> ScanAndImportFolder(string folderPath, string folderId = null)
        {
            if (!Directory.Exists(folderPath)) return new List<ResourceItem>();
            var files = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(folderPath))
                {
                    string n = Path.GetFileName(f);
                    if (!n.StartsWith(".") && n != "Thumbs.db" && n != "desktop.ini") files.Add(f);
                }
                foreach (var d in Directory.GetDirectories(folderPath))
                {
                    string n = Path.GetFileName(d);
                    if (!n.StartsWith(".") && !n.StartsWith("#")) files.Add(d);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[MagicBox] Scan error: {e.Message}"); }
            return ImportFiles(files.ToArray(), folderId);
        }

        public string GetResourceFilePath(ResourceItem item)
        {
            if (item.IsReference) return item.OriginalPath;
            string dir = _lib.GetFilesDirectory();
            return dir != null ? Path.Combine(dir, item.RelativePath) : null;
        }

        /// <summary>Import a .unitypackage into the Unity project (Editor only).</summary>
        public void ImportToUnity(ResourceItem item, bool interactive = true)
        {
            #if UNITY_EDITOR
            if (item == null || item.Type != ResourceType.UnityPackage) return;
            string path = GetResourceFilePath(item);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            AssetDatabase.ImportPackage(path, interactive);
            Debug.Log($"[MagicBox] Importing package to Unity: {item.FileName}");
            #else
            Debug.LogWarning("[MagicBox] Import to Unity is only available in the Unity Editor.");
            #endif
        }

        /// <summary>Generate thumbnails for Texture-type items (must be called from main thread).</summary>
        public void GenerateThumbnails(List<ResourceItem> items)
        {
            string thumbDir = _lib.GetThumbnailsDirectory();
            if (string.IsNullOrEmpty(thumbDir)) return;
            if (!Directory.Exists(thumbDir)) Directory.CreateDirectory(thumbDir);

            foreach (var item in items)
            {
                if (item.Type != ResourceType.Texture) continue;
                if (!string.IsNullOrEmpty(item.ThumbnailPath)) continue;

                string srcPath = GetResourceFilePath(item);
                if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) continue;
                if (new FileInfo(srcPath).Length > 50 * 1024 * 1024) continue;

                try
                {
                    byte[] bytes = File.ReadAllBytes(srcPath);
                    var srcTex = new Texture2D(2, 2);
                    if (!srcTex.LoadImage(bytes)) { UnityEngine.Object.Destroy(srcTex); continue; }

                    int tw = 128, th = 128;
                    if (srcTex.width > srcTex.height)
                        th = Mathf.Max(1, srcTex.height * 128 / srcTex.width);
                    else
                        tw = Mathf.Max(1, srcTex.width * 128 / srcTex.height);

                    var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
                    var prevRT = RenderTexture.active;
                    Graphics.Blit(srcTex, rt);
                    RenderTexture.active = rt;
                    var thumbTex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
                    thumbTex.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
                    thumbTex.Apply();
                    RenderTexture.active = prevRT;
                    RenderTexture.ReleaseTemporary(rt);
                    UnityEngine.Object.Destroy(srcTex);

                    string thumbName = $"{item.Id}.png";
                    File.WriteAllBytes(Path.Combine(thumbDir, thumbName), thumbTex.EncodeToPNG());
                    UnityEngine.Object.Destroy(thumbTex);

                    item.ThumbnailPath = thumbName;
                    _lib.Database.UpdateResourceMetadata(item);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MagicBox] Thumbnail failed for '{item.Name}': {e.Message}");
                }
            }
        }

        public void OpenResourceFile(ResourceItem item)
        {
            string path = GetResourceFilePath(item);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }

        public void OpenContainingFolder(ResourceItem item)
        {
            string path = GetResourceFilePath(item);
            if (!string.IsNullOrEmpty(path))
            {
                if (File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                else if (Directory.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
            }
        }

        public void CopyResourceTo(ResourceItem item, string destFolder)
        {
            string src = GetResourceFilePath(item);
            if (string.IsNullOrEmpty(src) || !File.Exists(src)) return;
            string dest = Path.Combine(destFolder, item.FileName);
            File.Copy(src, dest, true);
            Debug.Log($"[MagicBox] Copied to {dest}");
        }

        private string CopyToLibrary(string src, bool isDir)
        {
            string filesDir = _lib.GetFilesDirectory();
            if (filesDir == null) return "";
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string destName = $"{stamp}_{Path.GetFileName(src)}";
            string dest = Path.Combine(filesDir, destName);
            try
            {
                if (isDir) CopyDir(src, dest); else File.Copy(src, dest, false);
                return destName;
            }
            catch (Exception e) { Debug.LogWarning($"[MagicBox] Copy failed: {e.Message}"); return ""; }
        }

        private void CopyDir(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
        }

        private string ComputeHash(string path)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(path))
                {
                    if (stream.Length > 10 * 1024 * 1024)
                    {
                        byte[] buf = new byte[10 * 1024 * 1024];
                        stream.Read(buf, 0, buf.Length);
                        return BitConverter.ToString(md5.ComputeHash(buf)).Replace("-", "").ToLowerInvariant();
                    }
                    return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
            }
            catch { return ""; }
        }

        private long GetDirSize(string path)
        {
            long s = 0;
            try { foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) s += new FileInfo(f).Length; } catch { }
            return s;
        }

        // Tag cache to avoid querying all tags per file during batch import
        private List<ResourceTag> _tagCache;

        private void AutoAddTags(ResourceItem item)
        {
            string tagName = GetTypeTagName(item.Type);
            if (tagName == null) return;
            if (_tagCache == null) _tagCache = _lib.Database.GetAllTags();
            var existing = _tagCache.Find(t => t.Name == tagName);
            if (existing == null)
            {
                existing = new ResourceTag(tagName, "Type", GetTypeColor(item.Type));
                _lib.Database.InsertTag(existing);
                _tagCache.Add(existing);
            }
            item.TagIds.Add(existing.Id);
        }

        private string GetTypeTagName(ResourceType t)
        {
            switch (t) { case ResourceType.UnityPackage: return "UnityPackage"; case ResourceType.ZipArchive: return "ZIP";
                case ResourceType.CSharpScript: return "Script"; case ResourceType.ShaderFile: return "Shader";
                case ResourceType.Texture: return "Texture"; case ResourceType.Font: return "Font";
                case ResourceType.Document: return "Document"; case ResourceType.Executable: return "Executable";
                case ResourceType.Model3D: return "Model"; case ResourceType.Audio: return "Audio"; default: return null; }
        }

        private string GetTypeColor(ResourceType t)
        {
            switch (t) { case ResourceType.UnityPackage: return "#66BB6A"; case ResourceType.ZipArchive: return "#FFA726";
                case ResourceType.CSharpScript: return "#42A5F5"; case ResourceType.ShaderFile: return "#AB47BC";
                case ResourceType.Texture: return "#EF5350"; case ResourceType.Font: return "#78909C"; default: return "#4FC3F7"; }
        }
    }
}
