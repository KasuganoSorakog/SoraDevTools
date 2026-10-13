using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

public class WifiProfile
{
    public string Name { get; set; } = "";
    public string Password { get; set; } = "";
}

public static class WifiPasswordReader
{
    public static List<WifiProfile> GetSavedWifiProfiles()
    {
        var result = new List<WifiProfile>();

        string profilesOutput = RunCommand("netsh", "wlan show profiles");

        var profileMatches = Regex.Matches(
            profilesOutput,
            @"(?:All User Profile|所有用户配置文件)\s*:\s*(.+)"
        );

        foreach (Match match in profileMatches)
        {
            string name = match.Groups[1].Value.Trim();
            string detailOutput = RunCommand("netsh", $"wlan show profile name=\"{name}\" key=clear");

            var keyMatch = Regex.Match(
                detailOutput,
                @"(?:Key Content|关键内容)\s*:\s*(.+)"
            );

            result.Add(new WifiProfile
            {
                Name = name,
                Password = keyMatch.Success ? keyMatch.Groups[1].Value.Trim() : ""
            });
        }

        return result;
    }

    private static string RunCommand(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return "";

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return output + Environment.NewLine + error;
    }
}