using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SoraTools.HeadsetWifi
{
    public static class WifiStatusParser
    {
        private static readonly Regex ConnectedSsidPattern = new Regex(
            "^\\s*Wi-?Fi is connected to\\s+(?:[\\\"'](?<quoted>[^\\\"'\\r\\n]+)[\\\"']|(?<plain>[^\\r\\n]+))",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly Regex CurrentSsidPattern = new Regex(
            "current SSID\\(s\\):\\{[^}]*ssid=[\\\"'](?<ssid>[^\\\"'\\r\\n]+)[\\\"']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex WifiInfoSsidPattern = new Regex(
            "\\bSSID:\\s*\\\"?(?<ssid>[^\\\",\\r\\n]+)\\\"?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BssidPattern = new Regex(
            "\\bBSSID:\\s*(?<bssid>[0-9a-f]{2}(?::[0-9a-f]{2}){5})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LastBssidPattern = new Regex(
            "^\\s*mLastBssid\\s+(?<bssid>[0-9a-f]{2}(?::[0-9a-f]{2}){5})",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly Regex RssiPattern = new Regex(
            "\\bRSSI:\\s*(?<rssi>-?\\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex StatusIpPattern = new Regex(
            "\\bIP:\\s*/?(?<ip>\\d{1,3}(?:\\.\\d{1,3}){3})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex InterfaceIpPattern = new Regex(
            "\\binet\\s+(?<ip>\\d{1,3}(?:\\.\\d{1,3}){3})/",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RouteSourceIpPattern = new Regex(
            "\\bsrc\\s+(?<ip>\\d{1,3}(?:\\.\\d{1,3}){3})\\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex DefaultRoutePattern = new Regex(
            "^\\s*default\\b(?<body>[^\\r\\n]*)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly Regex RouteInterfacePattern = new Regex(
            "\\bdev\\s+(?<interface>\\S+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RouteGatewayPattern = new Regex(
            "\\bvia\\s+(?<gateway>\\S+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static WifiConnectionStatus Parse(
            string wifiStatusOutput,
            string ipAddressOutput,
            string routeOutput,
            string expectedInterfaceName)
        {
            string wifiStatus = wifiStatusOutput ?? "";
            string ipStatus = ipAddressOutput ?? "";
            string routes = routeOutput ?? "";
            string combined = wifiStatus + "\n" + ipStatus + "\n" + routes;

            var status = new WifiConnectionStatus
            {
                State = ParseConnectionState(wifiStatus),
                InterfaceName = expectedInterfaceName ?? ""
            };

            status.Ssid = ParseSsid(combined);
            status.Bssid = ParseValue(LastBssidPattern, combined, "bssid");
            if (string.IsNullOrEmpty(status.Bssid))
                status.Bssid = ParseValue(BssidPattern, combined, "bssid");

            string rssi = ParseValue(RssiPattern, combined, "rssi");
            status.Rssi = string.IsNullOrEmpty(rssi) ? "" : rssi + " dBm";
            status.IpAddress = ParseIpv4Address(combined);
            status.HasIpv4Address = !string.IsNullOrEmpty(status.IpAddress);

            ParseDefaultRoute(routes, status);
            return status;
        }

        public static string NormalizeSsid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string normalized = value.Trim().Trim('"', '\'');
            if (normalized.Equals("<unknown ssid>", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("null", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("<none>", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return normalized;
        }

        private static WifiConnectionState ParseConnectionState(string wifiStatus)
        {
            if (Contains(wifiStatus, "Wifi is disabled") || Contains(wifiStatus, "Wi-Fi is disabled"))
                return WifiConnectionState.Disabled;
            if (ConnectedSsidPattern.IsMatch(wifiStatus))
                return WifiConnectionState.Connected;
            if (Contains(wifiStatus, "Wifi is not connected") || Contains(wifiStatus, "Wi-Fi is not connected"))
                return WifiConnectionState.Disconnected;
            if (Contains(wifiStatus, "connecting"))
                return WifiConnectionState.Connecting;
            return WifiConnectionState.Unknown;
        }

        private static string ParseSsid(string output)
        {
            Match connectedMatch = ConnectedSsidPattern.Match(output);
            if (connectedMatch.Success)
            {
                string quoted = connectedMatch.Groups["quoted"].Value;
                string plain = connectedMatch.Groups["plain"].Value;
                string connectedSsid = NormalizeSsid(string.IsNullOrEmpty(quoted) ? plain : quoted);
                if (!string.IsNullOrEmpty(connectedSsid))
                    return connectedSsid;
            }

            string currentSsid = NormalizeSsid(ParseValue(CurrentSsidPattern, output, "ssid"));
            if (!string.IsNullOrEmpty(currentSsid))
                return currentSsid;

            return NormalizeSsid(ParseValue(WifiInfoSsidPattern, output, "ssid"));
        }

        private static string ParseIpv4Address(string output)
        {
            string[] candidates =
            {
                ParseValue(StatusIpPattern, output, "ip"),
                ParseValue(InterfaceIpPattern, output, "ip"),
                ParseValue(RouteSourceIpPattern, output, "ip")
            };

            for (int index = 0; index < candidates.Length; index++)
            {
                if (IsUsableIpv4(candidates[index]))
                    return candidates[index];
            }

            return "";
        }

        private static void ParseDefaultRoute(string routeOutput, WifiConnectionStatus status)
        {
            MatchCollection routeMatches = DefaultRoutePattern.Matches(routeOutput ?? "");
            for (int index = 0; index < routeMatches.Count; index++)
            {
                string routeBody = routeMatches[index].Groups["body"].Value;
                string interfaceName = ParseValue(RouteInterfacePattern, routeBody, "interface");
                if (string.IsNullOrEmpty(interfaceName))
                    continue;

                if (!status.HasDefaultRoute)
                    SetDefaultRoute(status, routeBody, interfaceName);
                if (string.Equals(status.InterfaceName, interfaceName, StringComparison.Ordinal))
                {
                    SetDefaultRoute(status, routeBody, interfaceName);
                    return;
                }
            }
        }

        private static void SetDefaultRoute(
            WifiConnectionStatus status,
            string routeBody,
            string interfaceName)
        {
            status.DefaultRouteInterface = interfaceName;
            status.DefaultGateway = ParseValue(RouteGatewayPattern, routeBody, "gateway");
            status.HasDefaultRoute = true;
        }

        private static string ParseValue(Regex pattern, string input, string groupName)
        {
            Match match = pattern.Match(input ?? "");
            return match.Success ? match.Groups[groupName].Value.Trim() : "";
        }

        private static bool IsUsableIpv4(string value)
        {
            if (!IPAddress.TryParse(value, out IPAddress address)
                || address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            byte[] bytes = address.GetAddressBytes();
            return bytes[0] != 0 && bytes[0] != 127;
        }

        private static bool Contains(string value, string marker)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
