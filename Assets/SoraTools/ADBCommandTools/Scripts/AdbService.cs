using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SoraTools.Utils;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SoraTools
{
    /// <summary>
    /// ADB command execution result.
    /// </summary>
    public class AdbResult
    {
        public bool IsSuccess;
        public int ExitCode = -1;
        public string Output = "";
        public string Error = "";
        public DateTime Timestamp = DateTime.Now;

        public bool HasOutput => !string.IsNullOrEmpty(Output);
        public bool HasError => !string.IsNullOrEmpty(Error);

        public string GetDisplayText()
        {
            var sb = new StringBuilder();
            if (HasOutput) sb.Append(Output);
            if (HasError)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(IsSuccess ? "[警告] " : "[错误] ");
                sb.Append(Error);
            }
            if (sb.Length == 0)
                sb.Append(IsSuccess ? "命令执行成功" : "命令执行失败");
            return sb.ToString();
        }
    }

    public class AdbTransferProgress
    {
        public long BytesTransferred;
        public long TotalBytes;
        public double BytesPerSecond;
        public TimeSpan? EstimatedRemaining;
        public int Attempt = 1;
        public string Status = "正在传输";

        public float Percent => TotalBytes > 0
            ? (float)Math.Min(100.0, BytesTransferred * 100.0 / TotalBytes)
            : 0f;
    }

    /// <summary>
    /// Service for executing ADB commands.
    /// Supports synchronous execution and asynchronous logcat streaming.
    /// </summary>
    public class AdbService : IDisposable
    {
        private const int DefaultTimeoutMs = 60000;
        private const int ServerRecoveryTimeoutMs = 30000;
        private const int ProgressPollIntervalMs = 200;

        private string adbPath;
        private Process logcatProcess;
        private bool disposed;
        private readonly object serverRecoveryLock = new object();

        public string AdbPath => adbPath;
        public bool IsLogcatRunning => logcatProcess != null && !logcatProcess.HasExited;
        public bool IsAdbAvailable => !string.IsNullOrEmpty(adbPath) && File.Exists(adbPath);

        public AdbService()
        {
            FindAdbPath();
        }

        private void FindAdbPath()
        {
            string environmentAdbPath = FindEnvironmentAdbPath();
            if (!string.IsNullOrEmpty(environmentAdbPath))
            {
                adbPath = environmentAdbPath;
                Debug.Log($"[AdbService] ADB found from environment: {adbPath}");
                return;
            }

            string bundledPath = Path.Combine(Application.streamingAssetsPath, "ApkTools", "adb.exe");
            bundledPath = Path.GetFullPath(bundledPath).Replace("/", "\\");

            if (File.Exists(bundledPath))
            {
                adbPath = bundledPath;
                Debug.Log($"[AdbService] Bundled ADB found: {adbPath}");
            }
            else
            {
                adbPath = bundledPath;
                Debug.LogWarning($"[AdbService] ADB not found in environment or at: {bundledPath}");
            }
        }

        private static string FindEnvironmentAdbPath()
        {
            foreach (string variableName in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
            {
                foreach (string sdkRoot in GetEnvironmentVariableValues(variableName))
                {
                    string path = ResolveExistingAdbPath(sdkRoot, "platform-tools", "adb.exe");
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
            }

            foreach (string pathValue in GetEnvironmentVariableValues("PATH"))
            {
                foreach (string directory in pathValue.Split(Path.PathSeparator))
                {
                    string path = ResolveExistingAdbPath(directory, "adb.exe");
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
            }

            return null;
        }

        private static IEnumerable<string> GetEnvironmentVariableValues(string variableName)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EnvironmentVariableTarget target in new[]
                     {
                         EnvironmentVariableTarget.Process,
                         EnvironmentVariableTarget.User,
                         EnvironmentVariableTarget.Machine
                     })
            {
                string value = null;
                try { value = Environment.GetEnvironmentVariable(variableName, target); }
                catch (Exception) { }

                if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                    yield return value;
            }
        }

        private static string ResolveExistingAdbPath(params string[] pathParts)
        {
            try
            {
                if (pathParts.Length == 0 || string.IsNullOrWhiteSpace(pathParts[0]))
                    return null;

                pathParts[0] = pathParts[0].Trim().Trim('"');
                string combinedPath = Path.Combine(pathParts);
                string expandedPath = Environment.ExpandEnvironmentVariables(combinedPath);
                return File.Exists(expandedPath)
                    ? Path.GetFullPath(expandedPath).Replace("/", "\\")
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Execute an ADB command synchronously. Call from background thread to avoid UI blocking.
        /// </summary>
        public AdbResult Execute(string arguments, int timeoutMs = DefaultTimeoutMs)
        {
            if (!IsAdbAvailable)
            {
                return new AdbResult
                {
                    IsSuccess = false,
                    Error = $"ADB not found: {adbPath}"
                };
            }

            // Increase timeout for heavy commands
            if (arguments.Contains("install") || arguments.Contains("dumpsys meminfo"))
                timeoutMs = Math.Max(timeoutMs, 180000);

            var result = ExecuteProcess(arguments, timeoutMs);
            if (!ShouldRecoverServer(arguments, result))
                return result;

            lock (serverRecoveryLock)
            {
                if (!ExecuteProcess("start-server", ServerRecoveryTimeoutMs).IsSuccess)
                {
                    ExecuteProcess("kill-server", 10000);
                    ExecuteProcess("start-server", ServerRecoveryTimeoutMs);
                }
            }

            var retryResult = ExecuteProcess(arguments, timeoutMs);
            if (retryResult.IsSuccess)
                retryResult.Output = "[ADB] 连接中断，已自动恢复并重试。\n" + retryResult.Output;
            return retryResult;
        }

        public AdbResult ExecuteWithProgress(
            string arguments,
            long totalBytes,
            Action<AdbTransferProgress> onProgress,
            int timeoutMs = -1,
            long initialBytes = 0,
            bool preserveProgressOnRetry = false)
        {
            if (!IsAdbAvailable)
            {
                return new AdbResult
                {
                    IsSuccess = false,
                    Error = $"ADB not found: {adbPath}"
                };
            }

            long maximumReportedBytes = initialBytes;
            Action<AdbTransferProgress> trackedProgress = progress =>
            {
                maximumReportedBytes = Math.Max(maximumReportedBytes, progress.BytesTransferred);
                onProgress?.Invoke(progress);
            };

            var result = ExecuteProcess(
                arguments, timeoutMs, totalBytes, trackedProgress, 1, initialBytes);
            if (!ShouldRecoverServer(arguments, result))
                return result;

            long retryInitialBytes = preserveProgressOnRetry ? maximumReportedBytes : initialBytes;
            ReportProgress(
                trackedProgress,
                retryInitialBytes,
                totalBytes,
                0,
                null,
                2,
                "ADB连接中断，正在恢复...");
            lock (serverRecoveryLock)
            {
                if (!ExecuteProcess("start-server", ServerRecoveryTimeoutMs).IsSuccess)
                {
                    ExecuteProcess("kill-server", 10000);
                    ExecuteProcess("start-server", ServerRecoveryTimeoutMs);
                }
            }

            var retryResult = ExecuteProcess(
                arguments, timeoutMs, totalBytes, trackedProgress, 2, retryInitialBytes);
            if (retryResult.IsSuccess)
                retryResult.Output = "[ADB] 连接中断，已自动恢复并重试。\n" + retryResult.Output;
            return retryResult;
        }

        private AdbResult ExecuteProcess(
            string arguments,
            int timeoutMs,
            long totalBytes = 0,
            Action<AdbTransferProgress> onProgress = null,
            int attempt = 1,
            long initialBytes = 0)
        {
            var result = new AdbResult();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(startInfo))
                {
                    // Read output/error asynchronously to avoid deadlocks
                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    double finalSpeed = 0;
                    bool finished = onProgress == null
                        ? process.WaitForExit(timeoutMs)
                        : WaitForExitWithProgress(
                            process,
                            timeoutMs,
                            totalBytes,
                            initialBytes,
                            onProgress,
                            attempt,
                            out finalSpeed);
                    if (!finished)
                    {
                        try { process.Kill(); } catch { }
                        result.IsSuccess = false;
                        result.Error = $"命令超时 ({timeoutMs / 1000}秒)";
                        return result;
                    }

                    // Ensure async reads complete after WaitForExit(int) returns true
                    process.WaitForExit();

                    result.Output = outputBuilder.ToString();
                    result.Error = errorBuilder.ToString();
                    result.ExitCode = process.ExitCode;
                    result.IsSuccess = process.ExitCode == 0;
                    if (result.IsSuccess && onProgress != null)
                        ReportProgress(
                            onProgress,
                            totalBytes,
                            totalBytes,
                            finalSpeed,
                            TimeSpan.Zero,
                            attempt,
                            "传输完成");
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Error = ex.Message;
                Debug.LogError($"[AdbService] Execute failed: {ex.Message}");
            }

            return result;
        }

        private static bool WaitForExitWithProgress(
            Process process,
            int timeoutMs,
            long totalBytes,
            long initialBytes,
            Action<AdbTransferProgress> onProgress,
            int attempt,
            out double finalSpeed)
        {
            finalSpeed = 0;
            var timer = Stopwatch.StartNew();
            TryGetReadTransferCount(process, out ulong initialReadBytes);
            long previousBytes = initialBytes;
            double previousSeconds = 0;
            double smoothedSpeed = 0;

            while (true)
            {
                int waitMs = ProgressPollIntervalMs;
                if (timeoutMs >= 0)
                {
                    long remainingMs = timeoutMs - timer.ElapsedMilliseconds;
                    if (remainingMs <= 0)
                    {
                        finalSpeed = smoothedSpeed;
                        return false;
                    }
                    waitMs = (int)Math.Min(waitMs, remainingMs);
                }

                bool finished = process.WaitForExit(waitMs);
                if (TryGetReadTransferCount(process, out ulong currentReadBytes))
                {
                    long processReadBytes = currentReadBytes >= initialReadBytes
                        ? (long)Math.Min((ulong)long.MaxValue, currentReadBytes - initialReadBytes)
                        : 0;
                    long transferredBytes = initialBytes > long.MaxValue - processReadBytes
                        ? long.MaxValue
                        : initialBytes + processReadBytes;
                    transferredBytes = Math.Min(Math.Max(0, transferredBytes), Math.Max(0, totalBytes));

                    double elapsedSeconds = timer.Elapsed.TotalSeconds;
                    double sampleSeconds = elapsedSeconds - previousSeconds;
                    if (sampleSeconds > 0.05 && transferredBytes >= previousBytes)
                    {
                        double currentSpeed = (transferredBytes - previousBytes) / sampleSeconds;
                        smoothedSpeed = smoothedSpeed <= 0
                            ? currentSpeed
                            : smoothedSpeed * 0.7 + currentSpeed * 0.3;
                        previousBytes = transferredBytes;
                        previousSeconds = elapsedSeconds;
                    }

                    TimeSpan? remaining = smoothedSpeed > 1 && totalBytes > transferredBytes
                        ? TimeSpan.FromSeconds((totalBytes - transferredBytes) / smoothedSpeed)
                        : null;
                    ReportProgress(
                        onProgress,
                        transferredBytes,
                        totalBytes,
                        smoothedSpeed,
                        remaining,
                        attempt,
                        attempt > 1 ? "正在重试传输" : "正在传输");
                }

                if (finished)
                {
                    finalSpeed = smoothedSpeed;
                    return true;
                }
            }
        }

        private static void ReportProgress(
            Action<AdbTransferProgress> onProgress,
            long transferredBytes,
            long totalBytes,
            double bytesPerSecond,
            TimeSpan? remaining,
            int attempt,
            string status)
        {
            try
            {
                onProgress?.Invoke(new AdbTransferProgress
                {
                    BytesTransferred = transferredBytes,
                    TotalBytes = totalBytes,
                    BytesPerSecond = bytesPerSecond,
                    EstimatedRemaining = remaining,
                    Attempt = attempt,
                    Status = status
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdbService] Progress callback failed: {ex.Message}");
            }
        }

        private static bool TryGetReadTransferCount(Process process, out ulong readBytes)
        {
            readBytes = 0;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                if (!GetProcessIoCounters(process.Handle, out ProcessIoCounters counters))
                    return false;
                readBytes = counters.ReadTransferCount;
                return true;
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessIoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessIoCounters(IntPtr processHandle, out ProcessIoCounters ioCounters);
#endif

        private static bool ShouldRecoverServer(string arguments, AdbResult result)
        {
            if (result.IsSuccess || !IsSafeToRetry(arguments))
                return false;

            if (result.ExitCode < 0)
                return true;

            string errorText = (result.Output + "\n" + result.Error).ToLowerInvariant();
            string[] daemonFailureMarkers =
            {
                "cannot connect to daemon",
                "daemon still not running",
                "failed to start daemon",
                "server didn't ack",
                "adb server version",
                "protocol fault",
                "connection reset",
                "connection was forcibly closed"
            };

            return Array.Exists(daemonFailureMarkers, errorText.Contains);
        }

        private static bool IsSafeToRetry(string arguments)
        {
            string normalized = " " + arguments.ToLowerInvariant().Trim() + " ";
            return normalized.Contains(" push ") ||
                   normalized.Contains(" pull ") ||
                   normalized.Contains(" devices ");
        }

        /// <summary>
        /// Start asynchronous logcat streaming. Output/error callbacks are dispatched to the main thread.
        /// </summary>
        public void StartLogcat(string arguments, Action<string> onOutput, Action<string> onError)
        {
            StopLogcat();

            if (!IsAdbAvailable)
            {
                onError?.Invoke($"ADB not found: {adbPath}");
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                logcatProcess = new Process { StartInfo = startInfo };

                logcatProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        MainThreadDispatcher.Enqueue(() => onOutput?.Invoke(e.Data));
                };

                logcatProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        MainThreadDispatcher.Enqueue(() => onError?.Invoke(e.Data));
                };

                logcatProcess.Start();
                logcatProcess.BeginOutputReadLine();
                logcatProcess.BeginErrorReadLine();

                Debug.Log($"[AdbService] Logcat started: {arguments}");
            }
            catch (Exception ex)
            {
                logcatProcess = null;
                Debug.LogError($"[AdbService] Logcat start failed: {ex.Message}");
                onError?.Invoke($"Logcat启动失败: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop the running logcat process.
        /// </summary>
        public void StopLogcat()
        {
            if (logcatProcess == null || disposed) return;

            try
            {
                if (!logcatProcess.HasExited)
                {
                    try { logcatProcess.CancelOutputRead(); } catch { }
                    try { logcatProcess.CancelErrorRead(); } catch { }
                    try { logcatProcess.Kill(); } catch { }
                    logcatProcess.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdbService] StopLogcat: {ex.Message}");
            }
            finally
            {
                logcatProcess.Dispose();
                logcatProcess = null;
            }

            Debug.Log("[AdbService] Logcat stopped");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopLogcat();
        }
    }
}
