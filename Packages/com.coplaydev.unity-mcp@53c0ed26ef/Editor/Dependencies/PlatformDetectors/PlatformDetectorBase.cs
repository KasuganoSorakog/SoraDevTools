using System;
using System.Diagnostics;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Services;
using UnityEditor;

namespace MCPForUnity.Editor.Dependencies.PlatformDetectors
{
    /// <summary>
    /// Base class for platform-specific dependency detection
    /// </summary>
    public abstract class PlatformDetectorBase : IPlatformDetector
    {
        public abstract string PlatformName { get; }
        public abstract bool CanDetect { get; }

        public abstract DependencyStatus DetectPython();
        public abstract string GetPythonInstallUrl();
        public abstract string GetUvInstallUrl();
        public abstract string GetInstallationRecommendations();

        public virtual DependencyStatus DetectUv()
        {
            var status = new DependencyStatus("uv Package Manager", isRequired: true)
            {
                InstallationHint = GetUvInstallUrl()
            };

            try
            {
                if (TryFindUvExecutable(out string uvPath, out string version, out string source))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = uvPath;
                    status.Details = $"Found uv {version} {source}";
                    return status;
                }

                status.ErrorMessage = "uv/uvx not found";
                status.Details = "Install uv, set uvx path in Prefs, or restart Unity after PATH changes.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting uv: {ex.Message}";
            }

            return status;
        }

        protected bool TryFindUvExecutable(out string uvPath, out string version, out string source)
        {
            uvPath = null;
            version = null;
            source = null;

            if (TryGetUvOverride(out string overridePath) &&
                TryValidateUvExecutable(overridePath, out version))
            {
                uvPath = overridePath;
                source = $"from Prefs override: {overridePath}";
                return true;
            }

            if (TryFindUvInKnownLocations(out uvPath, out version))
            {
                source = $"at {uvPath}";
                return true;
            }

            if (TryFindUvInPath(out uvPath, out version))
            {
                source = "in PATH";
                return true;
            }

            return false;
        }

        protected bool TryFindUvInKnownLocations(out string uvPath, out string version)
        {
            uvPath = null;
            version = null;

            foreach (string candidate in PathResolverService.EnumerateUvxCandidates())
            {
                if (TryValidateExistingUvExecutable(candidate, out version))
                {
                    uvPath = candidate;
                    return true;
                }
            }

            foreach (string candidate in PathResolverService.EnumerateUvCandidates())
            {
                if (TryValidateExistingUvExecutable(candidate, out version))
                {
                    uvPath = candidate;
                    return true;
                }
            }

            return false;
        }

        protected bool TryFindUvInPath(out string uvPath, out string version)
        {
            uvPath = null;
            version = null;

            // Try common uv command names
            var commands = new[] { "uvx", "uv" };

            foreach (var cmd in commands)
            {
                if (TryValidateUvExecutable(cmd, out version))
                {
                    uvPath = cmd;
                    return true;
                }
            }

            return false;
        }

        protected bool TryValidateExistingUvExecutable(string candidate, out string version)
        {
            version = null;
            return !string.IsNullOrWhiteSpace(candidate) &&
                   File.Exists(candidate) &&
                   TryValidateUvExecutable(candidate, out version);
        }

        protected bool TryValidateUvExecutable(string executable, out string version)
        {
            version = null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    return false;
                }

                string output = process.StandardOutput.ReadToEnd().Trim();
                if (process.ExitCode != 0)
                {
                    return false;
                }

                return TryParseUvVersionOutput(output, out version);
            }
            catch
            {
                return false;
            }
        }

        protected bool TryParseUvVersionOutput(string output, out string version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            string trimmed = output.Trim();
            if (trimmed.StartsWith("uvx ", StringComparison.OrdinalIgnoreCase))
            {
                version = trimmed.Substring(4).Trim();
                return !string.IsNullOrEmpty(version);
            }

            if (trimmed.StartsWith("uv ", StringComparison.OrdinalIgnoreCase))
            {
                version = trimmed.Substring(3).Trim();
                return !string.IsNullOrEmpty(version);
            }

            return false;
        }

        private static bool TryGetUvOverride(out string overridePath)
        {
            overridePath = null;

            try
            {
                string value = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    overridePath = value;
                    return true;
                }
            }
            catch
            {
                // Ignore EditorPrefs read errors and continue probing the system.
            }

            return false;
        }

        protected bool TryParseVersion(string version, out int major, out int minor)
        {
            major = 0;
            minor = 0;

            try
            {
                var parts = version.Split('.');
                if (parts.Length >= 2)
                {
                    return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return false;
        }
    }
}
