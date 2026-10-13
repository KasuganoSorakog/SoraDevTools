using System;
using System.Collections.Generic;

namespace SoraTools.HeadsetWifi
{
    public enum WifiSecurityMode
    {
        Wpa2Psk,
        Wpa3Sae
    }

    public enum WifiConnectionState
    {
        Unknown,
        Disabled,
        Disconnected,
        Connecting,
        Connected
    }

    public enum WifiMigrationState
    {
        Idle,
        Prechecking,
        Applying,
        Verifying,
        ObservingStability,
        Persisting,
        Succeeded,
        Failed,
        Cancelled
    }

    public enum WifiMigrationFailureKind
    {
        None,
        Cancelled,
        InvalidRequest,
        CredentialUnavailable,
        UsbPermissionRequired,
        HeadsetAuthorizationRequired,
        UsbDisconnected,
        DeviceUnavailable,
        UnsupportedAndroidVersion,
        UnsupportedFirmware,
        PermissionDenied,
        WifiDisabled,
        CommandTimedOut,
        ConnectionTimeout,
        CommandFailed,
        SsidMismatch,
        IpAddressUnavailable,
        DefaultRouteMissing,
        BusinessAddressUnreachable,
        StabilityLost,
        OperationAlreadyRunning,
        Unexpected
    }

    public enum WifiMigrationWarning
    {
        None,
        CredentialNotPersisted,
        OperationNotRecorded
    }

    [Serializable]
    public sealed class AndroidDeviceProperties
    {
        public string Manufacturer = "";
        public string Model = "";
        public string Device = "";
        public string SerialNumber = "";
        public int AndroidApiLevel;
        public string AndroidRelease = "";
        public string BuildFingerprint = "";
        public string ShellUserId = "";
    }

    [Serializable]
    public sealed class AndroidDeviceDescriptor
    {
        public string SessionId = "";
        public AndroidDeviceProperties Properties = new AndroidDeviceProperties();
    }

    [Serializable]
    public sealed class WifiCredential
    {
        public string Ssid = "";
        public WifiSecurityMode SecurityMode;
        public string Password = "";

        public override string ToString()
        {
            return WifiRedactor.MaskSsid(Ssid) + " (" + SecurityMode + ")";
        }
    }

    [Serializable]
    public sealed class WifiProfile
    {
        public string ProfileId = "";
        public string DisplayName = "";
        public string Ssid = "";
        public WifiSecurityMode SecurityMode;
        public DateTimeOffset CreatedAtUtc;
        public DateTimeOffset UpdatedAtUtc;
    }

    [Serializable]
    public sealed class WifiCapabilities
    {
        public AndroidDeviceDescriptor Device = new AndroidDeviceDescriptor();
        public bool IsCmdWifiAvailable;
        public bool SupportsConnectNetwork;
        public bool SupportsStatus;
        public bool SupportsWpa2;
        public bool SupportsWpa3;
        public string ProbeFailureCode = "";

        public bool CanMigrate
        {
            get
            {
                return IsCmdWifiAvailable
                    && SupportsConnectNetwork
                    && SupportsStatus
                    && string.IsNullOrEmpty(ProbeFailureCode);
            }
        }
    }

    [Serializable]
    public sealed class WifiConnectionStatus
    {
        public WifiConnectionState State;
        public string Ssid = "";
        public string Bssid = "";
        public string InterfaceName = "";
        public string IpAddress = "";
        public string DefaultRouteInterface = "";
        public string DefaultGateway = "";
        public string Rssi = "";

        public bool HasIpv4Address;
        public bool HasDefaultRoute;
    }

    public sealed class AndroidCommandResult
    {
        // ADB basic shell 无法可靠返回远端进程退出码。
        public bool TransportSucceeded;
        public bool TimedOut;
        public int ExitCode = -1;
        public string StandardOutput = "";
        public string StandardError = "";

        public bool HasKnownExitCode
        {
            get { return ExitCode >= 0; }
        }

        public static AndroidCommandResult Failed()
        {
            return new AndroidCommandResult
            {
                TransportSucceeded = false,
                ExitCode = -1
            };
        }
    }

    public sealed class AndroidShellCommand
    {
        private readonly string[] arguments;

        internal AndroidShellCommand(string operationName, bool containsSensitiveArguments, params string[] commandArguments)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                throw new ArgumentException("Operation name is required.", nameof(operationName));
            if (commandArguments == null || commandArguments.Length == 0)
                throw new ArgumentException("Command arguments are required.", nameof(commandArguments));

            OperationName = operationName;
            ContainsSensitiveArguments = containsSensitiveArguments;
            arguments = (string[])commandArguments.Clone();
        }

        public string OperationName { get; private set; }
        public bool ContainsSensitiveArguments { get; private set; }

        public IReadOnlyList<string> GetArgumentsForExecution()
        {
            return Array.AsReadOnly((string[])arguments.Clone());
        }

        public string GetEscapedShellText()
        {
            return AndroidShellArgumentEscaper.Join(arguments);
        }

        public override string ToString()
        {
            return ContainsSensitiveArguments
                ? OperationName + " [sensitive arguments redacted]"
                : OperationName;
        }
    }

    public sealed class WifiMigrationRequest
    {
        public WifiCredential Credential;
        public string ProfileId = "";
        public string ProfileDisplayName = "";
        public bool PersistCredential;
        public string BusinessAddress = "";
        public string WifiInterfaceName = "wlan0";
        public TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(45);
        public TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        public Action<WifiMigrationProgress> ProgressChanged;
    }

    public sealed class WifiMigrationProgress
    {
        public string OperationId = "";
        public WifiMigrationState State;
        public DateTimeOffset OccurredAtUtc;
        public string TargetSsid = "";
        public string Code = "";
    }

    public sealed class WifiMigrationResult
    {
        public string OperationId = "";
        public bool Succeeded;
        public bool NetworkValidated;
        public bool CredentialPersisted;
        public bool ExistingNetworksPreserved = true;
        public string ProfileId = "";
        public WifiMigrationState FinalState;
        public WifiMigrationFailureKind FailureKind;
        public WifiMigrationWarning Warning;
        public string MessageCode = "";
        public DateTimeOffset StartedAtUtc;
        public DateTimeOffset FinishedAtUtc;
        public AndroidDeviceDescriptor Device = new AndroidDeviceDescriptor();
        public WifiConnectionStatus LastConnectionStatus = new WifiConnectionStatus();
        public IReadOnlyList<WifiMigrationState> StateHistory = Array.Empty<WifiMigrationState>();
    }

    [Serializable]
    public sealed class WifiOperationRecord
    {
        public string OperationId = "";
        public string StartedAtUtc = "";
        public string FinishedAtUtc = "";
        public string DeviceHint = "";
        public string TargetSsidHint = "";
        public WifiSecurityMode SecurityMode;
        public WifiMigrationState FinalState;
        public WifiMigrationFailureKind FailureKind;
        public WifiMigrationWarning Warning;
        public string MessageCode = "";
        public string LastObservedState = "";

        public static WifiOperationRecord FromResult(
            WifiMigrationResult result,
            WifiCredential credential)
        {
            var properties = result != null && result.Device != null
                ? result.Device.Properties
                : null;
            var status = result != null ? result.LastConnectionStatus : null;

            return new WifiOperationRecord
            {
                OperationId = result != null ? result.OperationId : "",
                StartedAtUtc = result != null ? result.StartedAtUtc.ToString("O") : "",
                FinishedAtUtc = result != null ? result.FinishedAtUtc.ToString("O") : "",
                DeviceHint = WifiRedactor.MaskDeviceIdentifier(
                    result != null && result.Device != null ? result.Device.SessionId : "",
                    properties != null && !string.IsNullOrEmpty(properties.SerialNumber)
                        ? properties.SerialNumber
                        : properties != null ? properties.Model : ""),
                TargetSsidHint = WifiRedactor.MaskSsid(credential != null ? credential.Ssid : ""),
                SecurityMode = credential != null ? credential.SecurityMode : default,
                FinalState = result != null ? result.FinalState : WifiMigrationState.Failed,
                FailureKind = result != null ? result.FailureKind : WifiMigrationFailureKind.Unexpected,
                Warning = result != null ? result.Warning : WifiMigrationWarning.None,
                MessageCode = result != null ? result.MessageCode : "Unexpected",
                LastObservedState = status != null ? status.State.ToString() : WifiConnectionState.Unknown.ToString()
            };
        }
    }

    public static class WifiRedactor
    {
        public static string MaskSsid(string ssid)
        {
            if (string.IsNullOrEmpty(ssid))
                return "";
            if (ssid.Length == 1)
                return "*";
            if (ssid.Length == 2)
                return "**";

            return ssid.Substring(0, 1) + "***" + ssid.Substring(ssid.Length - 1, 1);
        }

        public static string MaskDeviceIdentifier(string sessionId, string model)
        {
            string source = string.IsNullOrWhiteSpace(sessionId) ? model : sessionId;
            if (string.IsNullOrEmpty(source))
                return "";
            if (source.Length <= 4)
                return "****";

            return source.Substring(0, 2) + "***" + source.Substring(source.Length - 2, 2);
        }
    }
}
