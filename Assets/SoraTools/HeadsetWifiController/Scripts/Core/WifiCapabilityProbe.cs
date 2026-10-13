using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SoraTools.HeadsetWifi
{
    public sealed class WifiCapabilityProbe : IWifiCapabilityProbe
    {
        private static readonly TimeSpan ProbeCommandTimeout = TimeSpan.FromSeconds(5);

        public async Task<WifiCapabilities> ProbeAsync(
            IAndroidDeviceSession deviceSession,
            CancellationToken cancellationToken)
        {
            if (deviceSession == null)
                throw new ArgumentNullException(nameof(deviceSession));

            var capabilities = new WifiCapabilities
            {
                Device = new AndroidDeviceDescriptor
                {
                    SessionId = deviceSession.DeviceSessionId ?? ""
                }
            };

            AndroidDeviceProperties properties = capabilities.Device.Properties;
            properties.AndroidApiLevel = ParseApiLevel(await ReadPropertyAsync(
                deviceSession,
                WifiShellCommandBuilder.AndroidApiLevelProperty,
                cancellationToken).ConfigureAwait(false));
            properties.AndroidRelease = await ReadPropertyAsync(
                deviceSession,
                WifiShellCommandBuilder.AndroidReleaseProperty,
                cancellationToken).ConfigureAwait(false);
            properties.Manufacturer = await ReadPropertyAsync(
                deviceSession,
                WifiShellCommandBuilder.ManufacturerProperty,
                cancellationToken).ConfigureAwait(false);
            properties.Model = await ReadPropertyAsync(
                deviceSession,
                WifiShellCommandBuilder.ModelProperty,
                cancellationToken).ConfigureAwait(false);
            properties.Device = await ReadPropertyAsync(
                deviceSession,
                WifiShellCommandBuilder.DeviceProperty,
                cancellationToken).ConfigureAwait(false);
            properties.SerialNumber = await ReadPropertyAsync(
                deviceSession,
                WifiShellCommandBuilder.SerialNumberProperty,
                cancellationToken).ConfigureAwait(false);
            properties.BuildFingerprint = await ReadPropertyAsync(
                deviceSession,
                WifiShellCommandBuilder.BuildFingerprintProperty,
                cancellationToken).ConfigureAwait(false);
            AndroidCommandResult shellUserIdResult = await ExecuteSafeAsync(
                deviceSession,
                WifiShellCommandBuilder.BuildShellUserIdCommand(),
                cancellationToken).ConfigureAwait(false);
            WifiMigrationFailureKind shellUserFailure = WifiCommandResultClassifier.Classify(shellUserIdResult);
            if (shellUserFailure != WifiMigrationFailureKind.None)
            {
                capabilities.ProbeFailureCode = shellUserFailure.ToString();
                return capabilities;
            }

            properties.ShellUserId = ReadFirstLine(shellUserIdResult.StandardOutput);
            if (!string.Equals(properties.ShellUserId, "0", StringComparison.Ordinal)
                && !string.Equals(properties.ShellUserId, "2000", StringComparison.Ordinal))
            {
                capabilities.ProbeFailureCode = WifiMigrationFailureKind.PermissionDenied.ToString();
                return capabilities;
            }

            AndroidCommandResult helpResult = await ExecuteSafeAsync(
                deviceSession,
                WifiShellCommandBuilder.BuildWifiHelpCommand(),
                cancellationToken).ConfigureAwait(false);
            WifiMigrationFailureKind failure = WifiCommandResultClassifier.Classify(helpResult);
            if (failure != WifiMigrationFailureKind.None)
            {
                capabilities.ProbeFailureCode = failure.ToString();
                return capabilities;
            }

            string helpText = WifiCommandResultClassifier.CombineOutput(helpResult);
            capabilities.IsCmdWifiAvailable = true;
            capabilities.SupportsConnectNetwork = ContainsCommand(helpText, "connect-network");
            capabilities.SupportsStatus = ContainsCommand(helpText, "status");
            capabilities.SupportsWpa2 = ContainsToken(helpText, "wpa2");
            capabilities.SupportsWpa3 = ContainsToken(helpText, "wpa3");

            if (!capabilities.SupportsConnectNetwork || !capabilities.SupportsStatus)
            {
                capabilities.ProbeFailureCode = WifiMigrationFailureKind.UnsupportedFirmware.ToString();
                return capabilities;
            }

            AndroidCommandResult statusResult = await ExecuteSafeAsync(
                deviceSession,
                WifiShellCommandBuilder.BuildWifiStatusCommand(),
                cancellationToken).ConfigureAwait(false);
            WifiMigrationFailureKind statusFailure = WifiCommandResultClassifier.Classify(statusResult);
            if (statusFailure != WifiMigrationFailureKind.None)
                capabilities.ProbeFailureCode = statusFailure.ToString();

            return capabilities;
        }

        private static async Task<string> ReadPropertyAsync(
            IAndroidDeviceSession deviceSession,
            string propertyName,
            CancellationToken cancellationToken)
        {
            AndroidCommandResult result = await ExecuteSafeAsync(
                deviceSession,
                WifiShellCommandBuilder.BuildGetPropertyCommand(propertyName),
                cancellationToken).ConfigureAwait(false);
            return WifiCommandResultClassifier.Classify(result) == WifiMigrationFailureKind.None
                ? ReadFirstLine(result.StandardOutput)
                : "";
        }

        private static async Task<AndroidCommandResult> ExecuteSafeAsync(
            IAndroidDeviceSession deviceSession,
            AndroidShellCommand command,
            CancellationToken cancellationToken)
        {
            try
            {
                AndroidCommandResult result = await deviceSession.ExecuteShellAsync(
                    command,
                    ProbeCommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                return result ?? AndroidCommandResult.Failed();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return AndroidCommandResult.Failed();
            }
        }

        private static int ParseApiLevel(string value)
        {
            return int.TryParse(value, out int apiLevel) ? apiLevel : 0;
        }

        private static string ReadFirstLine(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            int lineBreak = value.IndexOfAny(new[] { '\r', '\n' });
            return (lineBreak >= 0 ? value.Substring(0, lineBreak) : value).Trim();
        }

        private static bool ContainsCommand(string text, string command)
        {
            return Regex.IsMatch(
                text ?? "",
                "(?<![A-Za-z0-9-])" + Regex.Escape(command) + "(?![A-Za-z0-9-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool ContainsToken(string text, string token)
        {
            return Regex.IsMatch(
                text ?? "",
                "(?<![A-Za-z0-9])" + Regex.Escape(token) + "(?![A-Za-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    public static class WifiCommandResultClassifier
    {
        public static WifiMigrationFailureKind Classify(AndroidCommandResult result)
        {
            if (result != null && result.TimedOut)
                return WifiMigrationFailureKind.CommandTimedOut;
            if (result == null)
                return WifiMigrationFailureKind.DeviceUnavailable;

            string output = CombineOutput(result);
            if (!result.TransportSucceeded)
            {
                if (ContainsAny(
                    output,
                    "authorization_timeout",
                    "auth_failed",
                    "unauthorized",
                    "authorization required"))
                {
                    return WifiMigrationFailureKind.HeadsetAuthorizationRequired;
                }
                if (ContainsAny(
                    output,
                    "usb_permission_required",
                    "usb_permission_denied",
                    "no permissions",
                    "usb permission",
                    "permission denied"))
                {
                    return WifiMigrationFailureKind.UsbPermissionRequired;
                }
                if (ContainsAny(
                    output,
                    "usb_disconnected",
                    "usb_device_not_found",
                    "usb_interface_not_found",
                    "usb_open_failed",
                    "usb_claim_failed",
                    "usb_io_failure",
                    "usb_transfer_failed",
                    "not_connected",
                    "device not found",
                    "device offline",
                    "no devices/emulators found",
                    "connection reset",
                    "broken pipe",
                    "transport endpoint is not connected"))
                {
                    return WifiMigrationFailureKind.UsbDisconnected;
                }

                return WifiMigrationFailureKind.DeviceUnavailable;
            }

            if (ContainsAny(output, "securityexception", "permission denied", "requires android.permission", "not allowed"))
                return WifiMigrationFailureKind.PermissionDenied;
            if (ContainsAny(output, "unknown command", "wifishellcommand exception", "no such service", "not found"))
                return WifiMigrationFailureKind.UnsupportedFirmware;
            if (ContainsAny(output, "connection failed", "error:", "exception"))
                return WifiMigrationFailureKind.CommandFailed;
            if (result.HasKnownExitCode && result.ExitCode != 0)
                return WifiMigrationFailureKind.CommandFailed;

            return WifiMigrationFailureKind.None;
        }

        public static string CombineOutput(AndroidCommandResult result)
        {
            if (result == null)
                return "";

            return (result.StandardOutput ?? "") + "\n" + (result.StandardError ?? "");
        }

        private static bool ContainsAny(string value, params string[] markers)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int index = 0; index < markers.Length; index++)
            {
                if (value.IndexOf(markers[index], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
