using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace YoutubeTools
{
    /// <summary>
    /// Manages multiple concurrent yt-dlp download processes.
    /// All progress fields on DownloadTask are volatile — safe to poll from Update().
    /// </summary>
    public class DownloadService
    {
        // ── Compiled regexes ──
        private static readonly Regex ProgressRegex = new Regex(
            @"\[download\]\s+([\d.]+)%\s+of\s+~?\s*([\d.]+)([\w]+)\s+at\s+([\d.]+)([\w/]+)\s+ETA\s+([\d:]+)",
            RegexOptions.Compiled);

        private static readonly Regex DestinationRegex = new Regex(
            @"\[download\] Destination:\s*(.+)$", RegexOptions.Compiled);

        private static readonly Regex MergerRegex = new Regex(
            @"\[Merger\] Merging formats into ""(.+)""", RegexOptions.Compiled);

        private static readonly Regex FileMoveRegex = new Regex(
            @"\[MoveFiles\] Moving file to: (.+)$", RegexOptions.Compiled);

        // ── Active download tracking ──

        private class ActiveDownload
        {
            public DownloadTask Task;
            public Process      Process;
            public CancellationTokenSource Cts;
        }

        private static int _nextTaskId = 0;

        private readonly object _lock = new object();
        private readonly List<ActiveDownload> _downloads = new List<ActiveDownload>();

        private readonly object _completedLock = new object();
        private readonly Queue<DownloadTask> _completedQueue = new Queue<DownloadTask>();

        // ── Public API ──

        /// <summary>
        /// Returns a snapshot of all currently active (in-progress) tasks.
        /// Safe to call from Update() on the main thread.
        /// </summary>
        public List<DownloadTask> GetActiveTasks()
        {
            lock (_lock)
            {
                var list = new List<DownloadTask>(_downloads.Count);
                foreach (var d in _downloads) list.Add(d.Task);
                return list;
            }
        }

        /// <summary>
        /// Dequeues one completed/failed/cancelled task. Returns false when the queue is empty.
        /// Call repeatedly from Update() until false.
        /// </summary>
        public bool TryDequeueCompleted(out DownloadTask task)
        {
            lock (_completedLock)
            {
                if (_completedQueue.Count > 0) { task = _completedQueue.Dequeue(); return true; }
                task = null;
                return false;
            }
        }

        /// <summary>
        /// Start a new download. Never blocks on existing downloads — always adds a new parallel task.
        /// When <paramref name="ensureH264"/> is true and the downloaded file turns out to be encoded
        /// with a non-H.264 codec (e.g. AV1), ffmpeg will automatically re-encode it to H.264.
        /// </summary>
        public DownloadTask StartDownload(string url, string videoTitle, string savePath, string[] ytDlpFormatArgs, bool ensureH264 = false)
        {
            var cts  = new CancellationTokenSource();
            var task = new DownloadTask
            {
                TaskId       = Interlocked.Increment(ref _nextTaskId),
                VideoTitle   = videoTitle,
                Url          = url,
                OutputPath   = savePath,
                Status       = DownloadStatus.Downloading,
                StatusMessage = "准备下载...",
            };

            var active = new ActiveDownload { Task = task, Cts = cts };
            lock (_lock) _downloads.Add(active);

            string outputTemplate = Path.Combine(savePath, "%(title)s.%(ext)s");
            string formatArgs  = string.Join(" ", ytDlpFormatArgs);
            string cookieArgs  = YoutubeSettings.Instance.BuildCookieArgs();
            string ffmpegDir   = YoutubeSettings.Instance.ResolvedFfmpegDir;

            string args = formatArgs
                        + " --ffmpeg-location \"" + ffmpegDir + "\""
                        + " --no-playlist --newline"
                        + " -o \"" + outputTemplate.Replace("\"", "\\\"") + "\"";

            if (!string.IsNullOrEmpty(cookieArgs))
                args += " " + cookieArgs;

            args += " \"" + url.Replace("\"", "\\\"") + "\"";

            Debug.Log("[DownloadService] yt-dlp args: " + args);
            if (!File.Exists(Path.Combine(ffmpegDir, "ffmpeg.exe")))
                Debug.LogWarning("[DownloadService] ffmpeg.exe not found at: " + ffmpegDir + " — video/audio streams will NOT be merged!");

            Task.Run(() => RunDownload(active, args, cts.Token, ensureH264));
            return task;
        }

        /// <summary>Cancel a specific task.</summary>
        public void CancelTask(DownloadTask task)
        {
            lock (_lock)
            {
                foreach (var d in _downloads)
                {
                    if (d.Task == task)
                    {
                        d.Cts.Cancel();
                        try { d.Process?.Kill(); } catch { }
                        break;
                    }
                }
            }
        }

        /// <summary>Cancel all active downloads (called on app destroy).</summary>
        public void CancelAll()
        {
            List<ActiveDownload> snapshot;
            lock (_lock) snapshot = new List<ActiveDownload>(_downloads);
            foreach (var d in snapshot)
            {
                d.Cts.Cancel();
                try { d.Process?.Kill(); } catch { }
            }
        }

        // ── Background worker ──

        private void RemoveDownload(ActiveDownload active)
        {
            lock (_lock) _downloads.Remove(active);
        }

        private void RunDownload(ActiveDownload active, string args, CancellationToken ct, bool ensureH264 = false)
        {
            var task = active.Task;
            string ytDlpPath  = YoutubeSettings.Instance.ResolvedYtDlpPath;
            string ffmpegDir  = YoutubeSettings.Instance.ResolvedFfmpegDir;
            string workingDir = YoutubeSettings.GetAppDataDir();

            if (!File.Exists(ytDlpPath))
            {
                task.Status = DownloadStatus.Failed;
                task.StatusMessage = "yt-dlp not found: " + ytDlpPath;
                lock (_completedLock) _completedQueue.Enqueue(task);
                RemoveDownload(active);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName  = ytDlpPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding  = System.Text.Encoding.UTF8,
                WorkingDirectory = workingDir,
            };

            string lastDestination = "";

            try
            {
                var process = Process.Start(startInfo);
                lock (_lock) active.Process = process;

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null || ct.IsCancellationRequested) return;
                    ParseProgressLine(e.Data, task, ref lastDestination);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null || ct.IsCancellationRequested) return;
                    ParseProgressLine(e.Data, task, ref lastDestination);
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.WaitForExit(500))
                {
                    if (ct.IsCancellationRequested)
                    {
                        try { process.Kill(); } catch { }
                        break;
                    }
                }

                process.WaitForExit();

                if (ct.IsCancellationRequested)
                {
                    task.Status = DownloadStatus.Cancelled;
                    task.StatusMessage = "下载已取消";
                }
                else if (process.ExitCode == 0)
                {
                    task.ProgressPercent = 100f;
                    if (!string.IsNullOrEmpty(lastDestination))
                        task.OutputPath = lastDestination.Trim();

                    // Plan B: if H.264 was requested, probe the output codec and transcode if needed.
                    bool convertedToH264 = false;
                    if (ensureH264 && !ct.IsCancellationRequested)
                        convertedToH264 = TryConvertToH264IfNeeded(task, ffmpegDir, ct);

                    if (!ct.IsCancellationRequested && task.Status != DownloadStatus.Failed)
                    {
                        task.Status = DownloadStatus.Completed;
                        task.StatusMessage = convertedToH264 ? "下载完成! (已转码为 H.264)" : "下载完成!";
                    }
                }
                else
                {
                    task.Status = DownloadStatus.Failed;
                    task.StatusMessage = "yt-dlp 退出码: " + process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    task.Status = DownloadStatus.Failed;
                    task.StatusMessage = ex.Message;
                    Debug.LogError("[DownloadService] RunDownload failed: " + ex.Message);
                }
            }
            finally
            {
                // Enqueue before removing so Update() can see the terminal status
                lock (_completedLock) _completedQueue.Enqueue(task);
                RemoveDownload(active);
            }
        }

        // ── H.264 conversion helpers (Plan B fallback) ──

        /// <summary>
        /// Probes the output file codec. If it is not H.264, transcodes with ffmpeg.
        /// Returns true if a conversion was actually performed (and succeeded).
        /// Sets task.Status = Failed if conversion was attempted but failed.
        /// </summary>
        private static bool TryConvertToH264IfNeeded(DownloadTask task, string ffmpegDir, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(task.OutputPath) || !File.Exists(task.OutputPath))
                return false;

            string codec = ProbeVideoCodec(task.OutputPath, ffmpegDir);
            if (string.IsNullOrEmpty(codec))
                return false; // can't determine codec — skip

            bool isH264 = codec.IndexOf("h264", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          codec.IndexOf("avc",  StringComparison.OrdinalIgnoreCase) >= 0;
            if (isH264)
            {
                Debug.Log("[DownloadService] Codec is already H.264, no conversion needed.");
                return false;
            }

            Debug.Log($"[DownloadService] Codec is '{codec}', transcoding to H.264.");
            task.Status = DownloadStatus.Converting;
            task.StatusMessage = $"检测到 {codec.ToUpper()} 编码，正在转码 H.264...";

            bool ok = ConvertToH264(task, ffmpegDir, ct);
            if (!ok && !ct.IsCancellationRequested)
            {
                task.Status = DownloadStatus.Failed;
                task.StatusMessage = "H.264 转码失败";
            }
            return ok;
        }

        /// <summary>
        /// Runs ffprobe to get the video codec name of the first video stream.
        /// Returns null if ffprobe is not found or the probe fails.
        /// </summary>
        private static string ProbeVideoCodec(string filePath, string ffmpegDir)
        {
            string ffprobePath = Path.Combine(ffmpegDir, "ffprobe.exe");
            if (!File.Exists(ffprobePath))
            {
                Debug.LogWarning("[DownloadService] ffprobe.exe not found at: " + ffprobePath);
                return null;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName  = ffprobePath,
                    Arguments = "-v quiet -select_streams v:0 -show_entries stream=codec_name" +
                                " -of default=noprint_wrappers=1:nokey=1 \"" + filePath + "\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(10000);
                    Debug.Log($"[DownloadService] ffprobe codec: '{output}' for {Path.GetFileName(filePath)}");
                    return string.IsNullOrEmpty(output) ? null : output;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DownloadService] ffprobe failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Transcodes <see cref="DownloadTask.OutputPath"/> to H.264 in-place using ffmpeg.
        /// A temp file is written alongside the original, then the original is replaced on success.
        /// </summary>
        private static bool ConvertToH264(DownloadTask task, string ffmpegDir, CancellationToken ct)
        {
            string ffmpegPath = Path.Combine(ffmpegDir, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                Debug.LogWarning("[DownloadService] ffmpeg.exe not found for conversion.");
                return false;
            }

            string inputPath  = task.OutputPath;
            string dir        = Path.GetDirectoryName(inputPath);
            string nameNoExt  = Path.GetFileNameWithoutExtension(inputPath);
            string tempPath   = Path.Combine(dir, nameNoExt + "_h264_tmp.mp4");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName  = ffmpegPath,
                    // -crf 18: visually near-lossless; -preset fast: reasonable speed/size trade-off
                    // -c:a copy: audio is not re-encoded (already AAC/compatible)
                    // -movflags +faststart: web-friendly MP4 header placement
                    Arguments = "-i \"" + inputPath + "\" -c:v libx264 -crf 18 -preset fast" +
                                " -c:a copy -movflags +faststart -y \"" + tempPath + "\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                    StandardErrorEncoding  = System.Text.Encoding.UTF8,
                };

                using (var proc = Process.Start(psi))
                {
                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data == null || ct.IsCancellationRequested) return;
                        // ffmpeg progress lines contain "time=" — surface them as status
                        if (e.Data.Contains("time="))
                            task.StatusMessage = "转码中: " + e.Data.Trim();
                    };
                    proc.BeginErrorReadLine();

                    while (!proc.WaitForExit(500))
                    {
                        if (ct.IsCancellationRequested)
                        {
                            try { proc.Kill(); } catch { }
                            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
                            return false;
                        }
                    }

                    proc.WaitForExit();

                    if (proc.ExitCode != 0)
                    {
                        if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
                        Debug.LogError("[DownloadService] ffmpeg conversion exited with code: " + proc.ExitCode);
                        return false;
                    }
                }

                // Replace original with the H.264-encoded file
                File.Delete(inputPath);
                File.Move(tempPath, inputPath);
                task.OutputPath = inputPath;
                return true;
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
                Debug.LogError("[DownloadService] ConvertToH264 exception: " + ex.Message);
                return false;
            }
        }

        // ── Progress line parser ──

        private static void ParseProgressLine(string line, DownloadTask task, ref string lastDest)
        {
            var pm = ProgressRegex.Match(line);
            if (pm.Success)
            {
                if (float.TryParse(pm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float pct))
                    task.ProgressPercent = pct;

                if (float.TryParse(pm.Groups[2].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float totalVal))
                    task.TotalBytes = ParseBytes(totalVal, pm.Groups[3].Value);

                task.DownloadedBytes = (long)(task.TotalBytes * task.ProgressPercent / 100f);

                if (float.TryParse(pm.Groups[4].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float speedVal))
                    task.SpeedBytesPerSec = ParseBytes(speedVal, pm.Groups[5].Value.Replace("/s", ""));

                task.EtaSeconds   = ParseEta(pm.Groups[6].Value);
                task.Status       = DownloadStatus.Downloading;
                task.StatusMessage = string.Format("{0:F1}% | {1} | {2} | ETA {3}",
                    pct, task.SizeDisplay, task.SpeedDisplay, task.EtaDisplay);
                return;
            }

            var dm = DestinationRegex.Match(line);
            if (dm.Success) { lastDest = dm.Groups[1].Value.Trim(); return; }

            var mm = MergerRegex.Match(line);
            if (mm.Success)
            {
                lastDest = mm.Groups[1].Value.Trim();
                task.Status = DownloadStatus.Merging;
                task.StatusMessage = "正在合并视频...";
                return;
            }

            var fm = FileMoveRegex.Match(line);
            if (fm.Success) lastDest = fm.Groups[1].Value.Trim();
        }

        private static long ParseBytes(float value, string unit)
        {
            string u = unit.ToUpperInvariant().Trim();
            if (u.StartsWith("GIB") || u.StartsWith("GB")) return (long)(value * 1073741824L);
            if (u.StartsWith("MIB") || u.StartsWith("MB")) return (long)(value * 1048576L);
            if (u.StartsWith("KIB") || u.StartsWith("KB")) return (long)(value * 1024L);
            return (long)value;
        }

        private static int ParseEta(string eta)
        {
            string[] parts = eta.Split(':');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int h) &&
                int.TryParse(parts[1], out int m) &&
                int.TryParse(parts[2], out int s))
                return h * 3600 + m * 60 + s;
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int m2) &&
                int.TryParse(parts[1], out int s2))
                return m2 * 60 + s2;
            return 0;
        }
    }
}
