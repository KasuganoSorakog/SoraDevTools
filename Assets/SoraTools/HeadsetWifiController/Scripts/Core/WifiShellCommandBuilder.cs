using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace SoraTools.HeadsetWifi
{
    public static class AndroidShellArgumentEscaper
    {
        public static string Quote(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value.IndexOf('\0') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException("Shell arguments must not contain NUL, CR, or LF.", nameof(value));

            return "'" + value.Replace("'", "'\"'\"'") + "'";
        }

        public static string Join(IReadOnlyList<string> arguments)
        {
            if (arguments == null || arguments.Count == 0)
                throw new ArgumentException("At least one shell argument is required.", nameof(arguments));

            var builder = new StringBuilder();
            for (int index = 0; index < arguments.Count; index++)
            {
                if (index > 0)
                    builder.Append(' ');
                builder.Append(Quote(arguments[index]));
            }

            return builder.ToString();
        }
    }

    public static class WifiCredentialValidator
    {
        public static bool TryValidate(WifiCredential credential, out string failureCode)
        {
            failureCode = "";
            if (credential == null)
            {
                failureCode = "CredentialMissing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(credential.Ssid))
            {
                failureCode = "SsidMissing";
                return false;
            }

            if (ContainsUnsafeControlCharacter(credential.Ssid)
                || Encoding.UTF8.GetByteCount(credential.Ssid) > 32)
            {
                failureCode = "SsidInvalid";
                return false;
            }

            if (credential.SecurityMode != WifiSecurityMode.Wpa2Psk
                && credential.SecurityMode != WifiSecurityMode.Wpa3Sae)
            {
                failureCode = "SecurityModeUnsupported";
                return false;
            }

            string password = credential.Password ?? "";
            int passwordBytes = Encoding.UTF8.GetByteCount(password);
            if (ContainsUnsafeControlCharacter(password) || passwordBytes < 8)
            {
                failureCode = "PasswordInvalid";
                return false;
            }

            bool isWpa2RawPsk = credential.SecurityMode == WifiSecurityMode.Wpa2Psk
                && passwordBytes == 64
                && IsAsciiHex(password);
            int maximumBytes = isWpa2RawPsk ? 64 : 63;
            if (passwordBytes > maximumBytes)
            {
                failureCode = "PasswordInvalid";
                return false;
            }

            return true;
        }

        private static bool ContainsUnsafeControlCharacter(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index]))
                    return true;
            }

            return false;
        }

        private static bool IsAsciiHex(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool isDigit = character >= '0' && character <= '9';
                bool isLowerHex = character >= 'a' && character <= 'f';
                bool isUpperHex = character >= 'A' && character <= 'F';
                if (!isDigit && !isLowerHex && !isUpperHex)
                    return false;
            }

            return true;
        }
    }

    public static class WifiShellCommandBuilder
    {
        public const string AndroidApiLevelProperty = "ro.build.version.sdk";
        public const string AndroidReleaseProperty = "ro.build.version.release";
        public const string ManufacturerProperty = "ro.product.manufacturer";
        public const string ModelProperty = "ro.product.model";
        public const string DeviceProperty = "ro.product.device";
        public const string SerialNumberProperty = "ro.serialno";
        public const string BuildFingerprintProperty = "ro.build.fingerprint";

        private static readonly HashSet<string> AllowedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            AndroidApiLevelProperty,
            AndroidReleaseProperty,
            ManufacturerProperty,
            ModelProperty,
            DeviceProperty,
            SerialNumberProperty,
            BuildFingerprintProperty
        };

        private static readonly Regex InterfaceNamePattern = new Regex(
            "^[A-Za-z0-9_.-]{1,31}$",
            RegexOptions.CultureInvariant);

        public static AndroidShellCommand BuildGetPropertyCommand(string propertyName)
        {
            if (!AllowedProperties.Contains(propertyName))
                throw new ArgumentException("The requested Android property is not allow-listed.", nameof(propertyName));

            return new AndroidShellCommand("getprop", false, "getprop", propertyName);
        }

        public static AndroidShellCommand BuildShellUserIdCommand()
        {
            return new AndroidShellCommand("shell-user-id", false, "id", "-u");
        }

        public static AndroidShellCommand BuildWifiHelpCommand()
        {
            return new AndroidShellCommand("wifi-help", false, "cmd", "wifi", "help");
        }

        public static AndroidShellCommand BuildWifiStatusCommand()
        {
            return new AndroidShellCommand("wifi-status", false, "cmd", "wifi", "status");
        }

        public static AndroidShellCommand BuildSetWifiEnabledCommand(bool enabled)
        {
            return new AndroidShellCommand(
                "set-wifi-enabled",
                false,
                "cmd",
                "wifi",
                "set-wifi-enabled",
                enabled ? "enabled" : "disabled");
        }

        public static AndroidShellCommand BuildConnectNetworkCommand(WifiCredential credential)
        {
            if (!WifiCredentialValidator.TryValidate(credential, out string failureCode))
                throw new ArgumentException("Invalid Wi-Fi credential: " + failureCode, nameof(credential));

            return new AndroidShellCommand(
                "connect-network",
                true,
                "cmd",
                "wifi",
                "connect-network",
                credential.Ssid,
                ToCmdWifiSecurityMode(credential.SecurityMode),
                credential.Password);
        }

        public static AndroidShellCommand BuildIpAddressCommand(string interfaceName)
        {
            ValidateInterfaceName(interfaceName);
            return new AndroidShellCommand("wifi-ip-address", false, "ip", "addr", "show", interfaceName);
        }

        public static bool IsValidInterfaceName(string interfaceName)
        {
            return !string.IsNullOrWhiteSpace(interfaceName)
                && InterfaceNamePattern.IsMatch(interfaceName);
        }

        public static AndroidShellCommand BuildDefaultRouteCommand()
        {
            return new AndroidShellCommand("wifi-default-route", false, "ip", "route");
        }

        public static AndroidShellCommand BuildPingCommand(string hostOrAddress)
        {
            if (!TryValidateBusinessAddress(hostOrAddress, out bool isIpv6))
                throw new ArgumentException("Business address must be a DNS host name or IP address.", nameof(hostOrAddress));

            return isIpv6
                ? new AndroidShellCommand("business-address-ping", false, "ping", "-6", "-c", "1", "-W", "3", hostOrAddress)
                : new AndroidShellCommand("business-address-ping", false, "ping", "-c", "1", "-W", "3", hostOrAddress);
        }

        public static bool TryValidateBusinessAddress(string hostOrAddress, out bool isIpv6)
        {
            isIpv6 = false;
            if (string.IsNullOrWhiteSpace(hostOrAddress)
                || hostOrAddress.IndexOf('\0') >= 0
                || hostOrAddress.IndexOf('\r') >= 0
                || hostOrAddress.IndexOf('\n') >= 0)
            {
                return false;
            }

            if (IPAddress.TryParse(hostOrAddress, out IPAddress address))
            {
                isIpv6 = address.AddressFamily == AddressFamily.InterNetworkV6;
                return true;
            }

            if (hostOrAddress.Length > 253 || hostOrAddress.EndsWith(".", StringComparison.Ordinal))
                return false;

            string[] labels = hostOrAddress.Split('.');
            if (labels.Length < 2)
                return false;

            for (int index = 0; index < labels.Length; index++)
            {
                string label = labels[index];
                if (label.Length == 0 || label.Length > 63
                    || label[0] == '-' || label[label.Length - 1] == '-')
                {
                    return false;
                }

                for (int characterIndex = 0; characterIndex < label.Length; characterIndex++)
                {
                    char character = label[characterIndex];
                    bool valid = character >= 'a' && character <= 'z'
                        || character >= 'A' && character <= 'Z'
                        || character >= '0' && character <= '9'
                        || character == '-';
                    if (!valid)
                        return false;
                }
            }

            return true;
        }

        private static string ToCmdWifiSecurityMode(WifiSecurityMode securityMode)
        {
            switch (securityMode)
            {
                case WifiSecurityMode.Wpa2Psk:
                    return "wpa2";
                case WifiSecurityMode.Wpa3Sae:
                    return "wpa3";
                default:
                    throw new ArgumentOutOfRangeException(nameof(securityMode));
            }
        }

        private static void ValidateInterfaceName(string interfaceName)
        {
            if (!IsValidInterfaceName(interfaceName))
            {
                throw new ArgumentException("Invalid network interface name.", nameof(interfaceName));
            }
        }
    }
}
