using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SoraTools.HeadsetWifi
{
    public sealed class UsbAdbDeviceInfo
    {
        public string DeviceName = "";
        public int VendorId;
        public int ProductId;
        public bool HasPermission;
    }

    public sealed class UsbAdbOperationResult
    {
        public bool Succeeded;
        public string Code = "";
        public string DeviceName = "";
    }

    public sealed class UsbAdbDeviceEvent
    {
        public string EventName = "";
        public string DeviceName = "";
        public string Code = "";
    }

    public interface IPlatformSecretCipher
    {
        Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken);
        Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Bridges Unity to the Android USB-host ADB library. Shell requests always target the remote adbd.
    /// </summary>
    public sealed class AndroidUsbAdbSession : IAndroidDeviceSession, IPlatformSecretCipher, IDisposable
    {
        private const string BridgeClassName = "com.soratools.usbadb.AndroidUsbAdbBridge";
        private const string UnityPlayerClassName = "com.unity3d.player.UnityPlayer";

        private readonly object syncRoot = new object();
        private readonly Dictionary<string, TaskCompletionSource<BridgeEventPayload>> pendingOperations =
            new Dictionary<string, TaskCompletionSource<BridgeEventPayload>>();
        private bool disposed;
        private string deviceSessionId = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject bridge;
        private BridgeCallbackProxy callback;
#endif

        public event Action<UsbAdbDeviceEvent> DeviceEventReceived;

        public string DeviceSessionId
        {
            get { return deviceSessionId; }
        }

        private AndroidUsbAdbSession()
        {
        }

        public static async Task<AndroidUsbAdbSession> CreateAsync()
        {
            HeadsetWifiMainThread.Initialize();
            var session = new AndroidUsbAdbSession();
            await session.InitializeAsync().ConfigureAwait(false);
            return session;
        }

        public async Task<IReadOnlyList<UsbAdbDeviceInfo>> ScanDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            string json = await HeadsetWifiMainThread.RunAsync(() =>
            {
                ThrowIfDisposed();
                return bridge.Call<string>("scanDevices");
            }).ConfigureAwait(false);
            return ParseDeviceList(json);
#else
            return Array.Empty<UsbAdbDeviceInfo>();
#endif
        }

        public Task<UsbAdbOperationResult> RequestUsbPermissionAsync(
            string deviceName,
            CancellationToken cancellationToken)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return StartOperationAsync(
                "requestUsbPermission",
                deviceName,
                cancellationToken,
                (operationId) => bridge.Call("requestUsbPermission", deviceName, operationId));
#else
            return Task.FromResult(UnsupportedOperation(deviceName));
#endif
        }

        public async Task<UsbAdbOperationResult> ConnectAsync(
            string deviceName,
            CancellationToken cancellationToken)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            deviceSessionId = "";
            UsbAdbOperationResult result = await StartOperationAsync(
                "connect",
                deviceName,
                cancellationToken,
                (operationId) => bridge.Call("connect", deviceName, operationId)).ConfigureAwait(false);
            if (result.Succeeded)
                deviceSessionId = result.DeviceName;
            return result;
#else
            await Task.Yield();
            return UnsupportedOperation(deviceName);
#endif
        }

        public async Task<AndroidCommandResult> ExecuteShellAsync(
            AndroidShellCommand command,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

#if UNITY_ANDROID && !UNITY_EDITOR
            int timeoutMs = GetTimeoutMilliseconds(timeout);
            BridgeEventPayload payload = await StartRawOperationAsync(
                "executeShell",
                cancellationToken,
                (operationId) => bridge.Call(
                    "executeShell",
                    command.GetEscapedShellText(),
                    timeoutMs,
                    operationId),
                completeOnCancellation: false).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ToCommandResult(payload);
#else
            await Task.Yield();
            return new AndroidCommandResult
            {
                TransportSucceeded = false,
                StandardError = "PlatformNotAndroid"
            };
#endif
        }

        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken)
        {
            return TransformSecretAsync("encryptSecret", plaintext, cancellationToken);
        }

        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken)
        {
            return TransformSecretAsync("decryptSecret", ciphertext, cancellationToken);
        }

        public void Disconnect()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            HeadsetWifiMainThread.Enqueue(() =>
            {
                if (!disposed)
                    bridge.Call("disconnect");
            });
#endif
            deviceSessionId = "";
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            lock (syncRoot)
            {
                foreach (TaskCompletionSource<BridgeEventPayload> completion in pendingOperations.Values)
                    completion.TrySetCanceled();
                pendingOperations.Clear();
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            HeadsetWifiMainThread.Enqueue(() =>
            {
                bridge?.Call("dispose");
                bridge?.Dispose();
                bridge = null;
                callback = null;
            });
#endif
        }

        private async Task InitializeAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            await HeadsetWifiMainThread.RunAsync(() =>
            {
                using (var unityPlayer = new AndroidJavaClass(UnityPlayerClassName))
                {
                    AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    callback = new BridgeCallbackProxy(this);
                    bridge = new AndroidJavaObject(BridgeClassName, activity, callback);
                }
                return true;
            }).ConfigureAwait(false);
#endif
        }

        private Task<UsbAdbOperationResult> StartOperationAsync(
            string operationName,
            string deviceName,
            CancellationToken cancellationToken,
            Action<string> invocation)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return StartRawOperationAsync(operationName, cancellationToken, invocation)
                .ContinueWith(task => ToOperationResult(task.GetAwaiter().GetResult(), deviceName),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
#else
            return Task.FromResult(new UsbAdbOperationResult
            {
                Succeeded = false,
                Code = "PlatformNotAndroid",
                DeviceName = deviceName ?? ""
            });
#endif
        }

        private async Task<BridgeEventPayload> StartRawOperationAsync(
            string operationName,
            CancellationToken cancellationToken,
            Action<string> invocation,
            bool completeOnCancellation = true)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            string operationId = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<BridgeEventPayload>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (syncRoot)
            {
                pendingOperations.Add(operationId, completion);
            }

            using (cancellationToken.Register(() =>
            {
                if (completeOnCancellation)
                    CancelOperation(operationId, completion);
                else
                    CancelRemoteOperation(operationId);
            }))
            {
                try
                {
                    await HeadsetWifiMainThread.RunAsync(() =>
                    {
                        ThrowIfDisposed();
                        invocation(operationId);
                        return true;
                    }).ConfigureAwait(false);
                    return await completion.Task.ConfigureAwait(false);
                }
                finally
                {
                    lock (syncRoot)
                    {
                        pendingOperations.Remove(operationId);
                    }
                }
            }
#else
            return new BridgeEventPayload { @event = "operation_failed", errorCode = "PlatformNotAndroid" };
#endif
        }

        private async Task<string> TransformSecretAsync(
            string methodName,
            string value,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Secret value is required.", nameof(value));

            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            string transformed = await HeadsetWifiMainThread.RunAsync(() =>
            {
                ThrowIfDisposed();
                return bridge.Call<string>(methodName, value);
            }).ConfigureAwait(false);
            if (string.IsNullOrEmpty(transformed))
                throw new InvalidOperationException("AndroidKeystoreUnavailable");
            return transformed;
#else
            throw new PlatformNotSupportedException("Android Keystore is only available in Android player builds.");
#endif
        }

        private void OnBridgeEvent(string json)
        {
            HeadsetWifiMainThread.Enqueue(() => DispatchBridgeEvent(json));
        }

        private void DispatchBridgeEvent(string json)
        {
            BridgeEventPayload payload = ParseBridgeEvent(json);
            if (payload == null)
                return;

            if (!string.IsNullOrEmpty(payload.operationId))
            {
                TaskCompletionSource<BridgeEventPayload> completion;
                lock (syncRoot)
                {
                    pendingOperations.TryGetValue(payload.operationId, out completion);
                }
                completion?.TrySetResult(payload);
            }

            if (payload.@event == "connected")
                deviceSessionId = payload.deviceName ?? "";
            if (payload.@event == "disconnected" || payload.@event == "device_detached")
                deviceSessionId = "";

            if (payload.@event == "device_attached"
                || payload.@event == "device_detached"
                || payload.@event == "disconnected")
            {
                DeviceEventReceived?.Invoke(new UsbAdbDeviceEvent
                {
                    EventName = payload.@event ?? "",
                    DeviceName = payload.deviceName ?? "",
                    Code = payload.errorCode ?? payload.reason ?? ""
                });
            }
        }

        private static IReadOnlyList<UsbAdbDeviceInfo> ParseDeviceList(string json)
        {
            var parsed = JsonUtility.FromJson<BridgeDeviceList>(json ?? "");
            if (parsed?.devices == null)
                return Array.Empty<UsbAdbDeviceInfo>();

            var devices = new List<UsbAdbDeviceInfo>(parsed.devices.Length);
            for (int index = 0; index < parsed.devices.Length; index++)
            {
                BridgeDevice device = parsed.devices[index];
                if (device == null || string.IsNullOrEmpty(device.deviceName))
                    continue;
                devices.Add(new UsbAdbDeviceInfo
                {
                    DeviceName = device.deviceName,
                    VendorId = device.vendorId,
                    ProductId = device.productId,
                    HasPermission = device.hasPermission
                });
            }
            return devices;
        }

        private static BridgeEventPayload ParseBridgeEvent(string json)
        {
            try
            {
                return JsonUtility.FromJson<BridgeEventPayload>(json ?? "");
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static UsbAdbOperationResult ToOperationResult(
            BridgeEventPayload payload,
            string fallbackDeviceName)
        {
            bool succeeded = payload != null
                && (payload.@event == "connected"
                    || (payload.@event == "usb_permission" && payload.granted));
            return new UsbAdbOperationResult
            {
                Succeeded = succeeded,
                Code = payload?.errorCode ?? "",
                DeviceName = string.IsNullOrEmpty(payload?.deviceName)
                    ? fallbackDeviceName ?? ""
                    : payload.deviceName
            };
        }

        private static UsbAdbOperationResult UnsupportedOperation(string deviceName)
        {
            return new UsbAdbOperationResult
            {
                Succeeded = false,
                Code = "PlatformNotAndroid",
                DeviceName = deviceName ?? ""
            };
        }

        private static AndroidCommandResult ToCommandResult(BridgeEventPayload payload)
        {
            if (payload != null && payload.@event == "shell_complete")
            {
                return new AndroidCommandResult
                {
                    TransportSucceeded = payload.transportSucceeded,
                    ExitCode = payload.exitCode,
                    StandardOutput = payload.output ?? "",
                    StandardError = payload.stderr ?? ""
                };
            }

            string code = payload?.errorCode ?? "BridgeOperationFailed";
            return new AndroidCommandResult
            {
                TransportSucceeded = false,
                TimedOut = code.IndexOf("TIMEOUT", StringComparison.OrdinalIgnoreCase) >= 0,
                ExitCode = -1,
                StandardError = code
            };
        }

        private static int GetTimeoutMilliseconds(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
                throw new ArgumentOutOfRangeException(nameof(timeout));
            return (int)Math.Ceiling(timeout.TotalMilliseconds);
        }

        private void CancelOperation(
            string operationId,
            TaskCompletionSource<BridgeEventPayload> completion)
        {
            lock (syncRoot)
            {
                pendingOperations.Remove(operationId);
            }
            completion.TrySetCanceled();
        }

        private void CancelRemoteOperation(string operationId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            HeadsetWifiMainThread.Enqueue(() =>
            {
                if (!disposed)
                    bridge.Call("cancel", operationId);
            });
#endif
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AndroidUsbAdbSession));
        }

        [Serializable]
        private sealed class BridgeDeviceList
        {
            public BridgeDevice[] devices;
        }

        [Serializable]
        private sealed class BridgeDevice
        {
            public string deviceName;
            public int vendorId;
            public int productId;
            public bool hasPermission;
        }

        [Serializable]
        private sealed class BridgeEventPayload
        {
            public string @event;
            public string operationId;
            public string deviceName;
            public string errorCode;
            public string reason;
            public bool granted;
            public string output;
            public string stderr;
            public int exitCode = -1;
            public bool transportSucceeded;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class BridgeCallbackProxy : AndroidJavaProxy
        {
            private readonly AndroidUsbAdbSession owner;

            public BridgeCallbackProxy(AndroidUsbAdbSession owner)
                : base("com.soratools.usbadb.BridgeCallback")
            {
                this.owner = owner;
            }

            public void onBridgeEvent(string eventJson)
            {
                owner.OnBridgeEvent(eventJson);
            }
        }
#endif
    }
}
