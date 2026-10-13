using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace YtDownloader
{
    /// <summary>
    /// Manages multiple concurrent yt-dlp download processes.
    /// All progress fields on DownloadTask are volatile — safe to poll from Update().
    /// </summary>
    public class DownloadService
    {
        private static readonly Regex ProgressRegex = new Regex(
            @"\[download\]\s+([\d.]+)%\s+of\s+~?\s*([\d.]+)([\w]+)\s+at\s+([\d.]+)([\w/]+)\s+ETA\s+([\d:]+)",
            RegexOptions.Compiled);

        private static readonly Regex DestinationRegex = new Regex(
            @"\[download\] Destination:\s*(.+)$", RegexOptions.Compiled);

        private static readonly Regex MergerRegex = new Regex(
            @"\[Merger\] Merging formats into ""(.+)""", RegexOptions.Compiled);

        private static readonly Regex FileMoveRegex = new Regex(
            @"\[MoveFiles\] Moving file to: (.+)$", RegexOptions.Compiled);

        private class ActiveDownload
        {
            public DownloadTask Task;
            public Process      Process;
            public CancellationTokenSource Cts;
        }

        private static int _nextTaskId = 0;
        private readonly object _lock           = new object();
        private readonly List<ActiveDownload> _downloads = new List<ActiveDownload>();
        private readonly object _completedLock  = new object();
        private readonly Queue<DownloadTask> _completedQueue = new Queue<DownloadTask>();

        public List<DownloadTask> GetActiveTasks()
        {
            lock (_lock)
            {
                var list = new List<DownloadTask>(_downloads.Count);
                foreach (var d in _downloads) list.Add(d.Task);
                return list;
            }
        }

        public bool TryDequeueCompleted(out DownloadTask task)
        {
            lock (_completedLock)
            {
                if (_completedQueue.Count > 0) { task = _completedQueue.Dequeue(); return true; }
                task = null;
                return false;
            }
        }

        public DownloadTask StartDownload(string url, string videoTitle, string savePath,
            string[] ytDlpFormatArgs, bool ensureH264 = false)
        {
            var cts  = new CancellationTokenSource();
            var task = new DownloadTask
            {
                TaskId        = Interlocked.Increment(ref _nextTaskId),
                VideoTitle    = videoTitle,
                Url           = url,
                OutputPath    = savePath,
                Status        = DownloadStatus.Downloading,
                StatusMessage = "准备下载...",
            };

            var active = new ActiveDownload { Task = task, Cts = cts };
            lock (_lock) _downloads.Add(active);

            string outputTemplate = Path.Combine(savePath, "%(title)s.%(ext)s");
            string formatArgs  = string.Join(" ", ytDlpFormatArgs);
            string cookieArgs  = YtDlSettings.Instance.BuildCookieArgs();
            string ffmpegDir   = YtDlSettings.Instance.ResolvedFfmpegDir;

            string args = formatArgs
                        + " --ffmpeg-location \"" + ffmpegDir + "\""
                        + " --no-playlist --newline"
                        + " -o \"" + outputTemplate.Replace("\"", "\\\"") + "\"";

            if (!string.IsNullOrEmpty(cookieArgs))
                args += " " + cookieArgs;
            args += " \"" + url.Replace("\"", "\\\"") + "\"";

            if (!File.Exists(Path.Combine(ffmpegDir, "ffmpeg.exe")))
                Debug.LogWarning("[DownloadService] ffmpeg.exe not found at: " + ffmpegDir);

            Task.Run(() => RunDownload(active, args, cts.Token, ensureH264));
            return task;
        }

        public void CancelTask(DownloadTask task)
        {
            lock (_lock)
            {
                foreach (var d in _downloads)
                {
                    if (d.Task == task)
                    { d.Cts.Cancel(); try { d.Process?.Kill(); } catch { } break; }
                }
            }
        }

        public void CancelAll()
        {
            List<ActiveDownload> snapshot;
            lock (_lock) snapshot = new List<ActiveDownload>(_downloads);
            foreach (var d in snapshot)
            { d.Cts.Cancel(); try { d.Process?.Kill(); } catch { } }
        }

        private void RemoveDownload(ActiveDownload active)
        {
            lock (_lock) _downloads.Remove(active);
        }

        private void RunDownload(ActiveDownload active, string args, CancellationToken ct,
            bool ensureH264 = false)
        {
            var task      = active.Task;
            string ytDlpPath = YtDlSettings.Instance.ResolvedYtDlpPath;
            string ffmpegDir = YtDlSettings.Instance.ResolvedFfmpegDir;

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
                FileName               = ytDlpPath,
                Arguments              = args,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding  = System.Text.Encoding.UTF8,
                WorkingDirectory       = YtDlSettings.GetAppDataDir(),
            };

            string lastDest = "";

            try
            {
                var process = Process.Start(startInfo);
                lock (_lock) active.Process = process;

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null || ct.IsCancellationRequested) return;
                    ParseProgressLine(e.Data, task, ref lastDest);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null || ct.IsCancellationRequested) return;
                    ParseProgressLine(e.Data, task, ref lastDest);
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.WaitForExit(500))
                {
                    if (ct.IsCancellationRequested)
                    { try { process.Kill(); } catch { } break; }
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
                    if (!string.IsNullOrEmpty(lastDest)) task.OutputPath = lastDest.Trim();

                    bool converted = false;
                    if (ensureH264 && !ct.IsCancellationRequested)
                        converted = TryConvertToH264IfNeeded(task, ffmpegDir, ct);

                    if (!ct.IsCancellationRequested && task.Status != DownloadStatus.Failed)
                    {
                        task.Status = DownloadStatus.Completed;
                        task.StatusMessage = converted ? "下载完成! (已转码 H.264)" : "下载完成!";
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
                lock (_completedLock) _completedQueue.Enqueue(task);
                RemoveDownload(active);
            }
        }

        // ── H.264 conversion ──

        private static bool TryConvertToH264IfNeeded(DownloadTask task, string ffmpegDir,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(task.OutputPath) || !File.Exists(task.OutputPath))
                return false;

            string codec = ProbeVideoCodec(task.OutputPath, ffmpegDir);
            if (string.IsNullOrEmpty(codec)) return false;

            bool isH264 = codec.IndexOf("h264", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          codec.IndexOf("avc",  StringComparison.OrdinalIgnoreCase) >= 0;
            if (isH264) return false;

            task.Status = DownloadStatus.Converting;
            task.StatusMessage = "检测到 " + codec.ToUpper() + " 编码，正在转码 H.264...";
            bool ok = ConvertToH264(task, ffmpegDir, ct);
            if (!ok && !ct.IsCancellationRequested)
            { task.Status = DownloadStatus.Failed; task.StatusMessage = "H.264 转码失败"; }
            return ok;
        }

        private static string ProbeVideoCodec(string filePath, string ffmpegDir)
        {
            string ffprobePath = Path.Combine(ffmpegDir, "ffprobe.exe");
            if (!File.Exists(ffprobePath)) return null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName  = ffprobePath,
                    Arguments = "-v quiet -select_streams v:0 -show_entries stream=codec_name" +
                                " -of default=noprint_wrappers=1:nokey=1 \"" + filePath + "\"",
                    UseShellExecute = false, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true,
                };
                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(10000);
                    return string.IsNullOrEmpty(output) ? null : output;
                }
            }
            catch { return null; }
        }

        private static bool ConvertToH264(DownloadTask task, string ffmpegDir, CancellationToken ct)
        {
            string ffmpegPath = Path.Combine(ffmpegDir, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath)) return false;

            string inputPath = task.OutputPath;
            string tempPath  = Path.Combine(Path.GetDirectoryName(inputPath),
                Path.GetFileNameWithoutExtension(inputPath) + "_h264_tmp.mp4");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName  = ffmpegPath,
                    Arguments = "-i \"" + inputPath + "\" -c:v libx264 -crf 18 -preset fast" +
                                " -c:a copy -movflags +faststart -y \"" + tempPath + "\"",
                    UseShellExecute = false, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                };

                using (var proc = Process.Start(psi))
                {
                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null && !ct.IsCancellationRequested && e.Data.Contains("time="))
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
                    { if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { } return false; }
                }

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

        // ── Progress parsing ──

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
                    task.SpeedBytesPerSec = ParseBytes(speedVal,
                        pm.Groups[5].Value.Replace("/s", ""));

                task.EtaSeconds    = ParseEta(pm.Groups[6].Value);
                task.Status        = DownloadStatus.Downloading;
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
