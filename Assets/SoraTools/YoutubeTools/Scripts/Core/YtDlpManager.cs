using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace YoutubeTools
{
    public class ProcessResult
    {
        public bool IsSuccess;
        public int ExitCode = -1;
        public string Output = "";
        public string Error = "";
    }

    /// <summary>
    /// Manages the yt-dlp binary: path resolution, auto-download, and process execution.
    /// All blocking operations must be called from a background thread.
    /// </summary>
    public static class YtDlpManager
    {
        // GitHub release URL for yt-dlp.exe
        private const string YtDlpDownloadUrl =
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        // ── Process execution ──

        /// <summary>
        /// Run yt-dlp synchronously with the given arguments.
        /// Call from a background thread; never call on the Unity main thread.
        /// </summary>
        public static ProcessResult RunYtDlp(string arguments,
            Action<string> onStdout = null,
            Action<string> onStderr = null,
            int timeoutMs = 300_000)
        {
            var settings = YoutubeSettings.Instance;
            string ytDlpPath = settings.ResolvedYtDlpPath;
            string workingDir = YoutubeSettings.GetAppDataDir();

            var result = new ProcessResult();

            if (!File.Exists(ytDlpPath))
            {
                result.IsSuccess = false;
                result.Error = $"yt-dlp not found: {ytDlpPath}";
                return result;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = workingDir,
                };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                using (var process = Process.Start(startInfo))
                {
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null) return;
                        outputBuilder.AppendLine(e.Data);
                        try { onStdout?.Invoke(e.Data); } catch { }
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data == null) return;
                        errorBuilder.AppendLine(e.Data);
                        try { onStderr?.Invoke(e.Data); } catch { }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    bool finished = process.WaitForExit(timeoutMs);
                    if (!finished)
                    {
                        try { process.Kill(); } catch { }
                        result.IsSuccess = false;
                        result.Error = $"yt-dlp timeout ({timeoutMs / 1000}s)";
                        return result;
                    }

                    // Ensure async reads finish
                    process.WaitForExit();

                    result.Output = outputBuilder.ToString();
                    result.Error = errorBuilder.ToString();
                    result.ExitCode = process.ExitCode;
                    // yt-dlp exits with code 1 when warnings are present (e.g. no JS runtime,
                    // ffmpeg not found) even though it produced valid output. Treat non-empty
                    // stdout as success regardless of exit code.
                    result.IsSuccess = process.ExitCode == 0 || !string.IsNullOrWhiteSpace(result.Output);
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Error = ex.Message;
                Debug.LogError($"[YtDlpManager] RunYtDlp failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Start yt-dlp as a long-running process and return it for external management.
        /// The caller is responsible for calling Kill() and Dispose().
        /// </summary>
        public static Process StartYtDlpProcess(string arguments)
        {
            var settings = YoutubeSettings.Instance;
            string ytDlpPath = settings.ResolvedYtDlpPath;
            string workingDir = YoutubeSettings.GetAppDataDir();

            if (!File.Exists(ytDlpPath))
            {
                Debug.LogError($"[YtDlpManager] yt-dlp not found: {ytDlpPath}");
                return null;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workingDir,
            };

            try
            {
                return Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YtDlpManager] StartYtDlpProcess failed: {ex.Message}");
                return null;
            }
        }

        // ── Auto-download ──

        /// <summary>
        /// Download yt-dlp.exe from GitHub Releases into %APPDATA%\YoutubeTools\.
        /// Blocking — call on background thread.
        /// </summary>
        public static bool DownloadYtDlp(Action<float, string> onProgress, System.Threading.CancellationToken ct)
        {
            string destDir = YoutubeSettings.GetAppDataDir();
            string destPath = Path.Combine(destDir, "yt-dlp.exe");

            try
            {
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                onProgress?.Invoke(0f, "连接到 GitHub...");

                var request = (HttpWebRequest)WebRequest.Create(YtDlpDownloadUrl);
                request.AllowAutoRedirect = true;
                request.UserAgent = "Mozilla/5.0 YoutubeTools/1.0";
                request.Timeout = 30_000;

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    long total = response.ContentLength;
                    using (var src = response.GetResponseStream())
                    using (var dst = File.Create(destPath))
                    {
                        byte[] buffer = new byte[81920];
                        long downloaded = 0;
                        int read;
                        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                dst.Close();
                                if (File.Exists(destPath)) File.Delete(destPath);
                                return false;
                            }

                            dst.Write(buffer, 0, read);
                            downloaded += read;

                            float pct = total > 0 ? (float)downloaded / total : 0f;
                            string msg = total > 0
                                ? $"下载中... {downloaded / 1048576.0:F1} / {total / 1048576.0:F1} MB"
                                : $"下载中... {downloaded / 1048576.0:F1} MB";
                            onProgress?.Invoke(pct, msg);
                        }
                    }
                }

                onProgress?.Invoke(1f, "yt-dlp 下载完成!");
                Debug.Log($"[YtDlpManager] yt-dlp downloaded to: {destPath}");
                return true;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke(0f, $"下载失败: {ex.Message}");
                Debug.LogError($"[YtDlpManager] Download failed: {ex.Message}");
                if (File.Exists(destPath))
                    try { File.Delete(destPath); } catch { }
                return false;
            }
        }

        /// <summary>
        /// Download ffmpeg.exe from BtbN GitHub Releases into %APPDATA%\YoutubeTools\.
        /// Blocking — call on background thread.
        /// </summary>
        public static bool DownloadFfmpeg(Action<float, string> onProgress, System.Threading.CancellationToken ct)
        {
            string destDir  = YoutubeSettings.GetAppDataDir();
            string destPath = Path.Combine(destDir, "ffmpeg.exe");
            string tempZip  = Path.Combine(destDir, "_ffmpeg_tmp.zip");

            const string Url =
                "https://github.com/BtbN/ffmpeg-builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

            try
            {
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                onProgress?.Invoke(0f, "连接 GitHub 下载 ffmpeg...");

                var request = (HttpWebRequest)WebRequest.Create(Url);
                request.AllowAutoRedirect = true;
                request.UserAgent = "Mozilla/5.0 YoutubeTools/1.0";
                request.Timeout = 30_000;

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    long total = response.ContentLength;
                    using (var src = response.GetResponseStream())
                    using (var dst = File.Create(tempZip))
                    {
                        byte[] buf = new byte[81920];
                        long downloaded = 0;
                        int read;
                        while ((read = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                dst.Close();
                                if (File.Exists(tempZip)) File.Delete(tempZip);
                                return false;
                            }
                            dst.Write(buf, 0, read);
                            downloaded += read;
                            float pct = total > 0 ? (float)downloaded / total * 0.9f : 0f;
                            string msg = total > 0
                                ? $"下载 ffmpeg... {downloaded / 1048576.0:F0}/{total / 1048576.0:F0} MB"
                                : $"下载 ffmpeg... {downloaded / 1048576.0:F0} MB";
                            onProgress?.Invoke(pct, msg);
                        }
                    }
                }

                onProgress?.Invoke(0.92f, "正在解压 ffmpeg.exe...");

                using (var fs = File.OpenRead(tempZip))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry entry = null;
                    foreach (var e in archive.Entries)
                    {
                        if (e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) &&
                            e.FullName.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            entry = e; break;
                        }
                    }
                    if (entry == null)
                    {
                        onProgress?.Invoke(0f, "解压失败: zip 中未找到 ffmpeg.exe");
                        return false;
                    }
                    using (var es = entry.Open())
                    using (var of = File.Create(destPath))
                        es.CopyTo(of);
                }

                onProgress?.Invoke(1f, "ffmpeg 下载完成!");
                Debug.Log($"[YtDlpManager] ffmpeg downloaded to: {destPath}");
                return true;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke(0f, $"ffmpeg 下载失败: {ex.Message}");
                Debug.LogError($"[YtDlpManager] DownloadFfmpeg failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch { }
            }
        }
    }
}
