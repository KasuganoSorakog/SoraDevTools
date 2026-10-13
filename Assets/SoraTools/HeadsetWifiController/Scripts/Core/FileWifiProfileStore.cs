using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SoraTools.HeadsetWifi
{
    public sealed class FileWifiProfileStore : ISecretStore
    {
        private const int CurrentFormatVersion = 1;

        private readonly string filePath;
        private readonly SemaphoreSlim fileLock = new SemaphoreSlim(1, 1);

        public FileWifiProfileStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A profile storage path is required.", nameof(filePath));

            this.filePath = filePath;
        }

        public static string GetDefaultFilePath(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
                throw new ArgumentException("Persistent data path is required.", nameof(persistentDataPath));

            return Path.Combine(persistentDataPath, "headset_wifi_profiles.json");
        }

        public async Task<IReadOnlyList<WifiProfile>> ListProfilesAsync(CancellationToken cancellationToken)
        {
            return await ExecuteLockedAsync(() =>
            {
                ProfileDocument document = ReadDocument();
                var profiles = new List<WifiProfile>(document.Entries.Count);
                for (int index = 0; index < document.Entries.Count; index++)
                    profiles.Add(document.Entries[index].ToProfile());

                profiles.Sort((left, right) => string.Compare(
                    left.DisplayName,
                    right.DisplayName,
                    StringComparison.OrdinalIgnoreCase));
                return (IReadOnlyList<WifiProfile>)profiles;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<WifiProfile> GetProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            ValidateProfileId(profileId);
            return await ExecuteLockedAsync(() =>
            {
                ProfileEntry entry = FindEntry(ReadDocument(), profileId);
                return entry != null ? entry.ToProfile() : null;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<WifiCredential> GetCredentialAsync(string profileId, CancellationToken cancellationToken)
        {
            ValidateProfileId(profileId);
            return await ExecuteLockedAsync(() =>
            {
                ProfileEntry entry = FindEntry(ReadDocument(), profileId);
                return entry != null ? entry.ToCredential() : null;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task SaveProfileAsync(
            WifiProfile profile,
            WifiCredential credential,
            CancellationToken cancellationToken)
        {
            ValidateProfile(profile, credential);
            await ExecuteLockedAsync(() =>
            {
                ProfileDocument document = ReadDocument();
                ProfileEntry entry = FindEntry(document, profile.ProfileId);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (entry == null)
                {
                    entry = new ProfileEntry
                    {
                        Id = profile.ProfileId,
                        CreatedAtUtc = now.ToString("O", CultureInfo.InvariantCulture)
                    };
                    document.Entries.Add(entry);
                }

                entry.DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                    ? credential.Ssid
                    : profile.DisplayName;
                entry.Ssid = credential.Ssid;
                entry.SecurityMode = credential.SecurityMode;
                entry.Password = credential.Password;
                entry.UpdatedAtUtc = now.ToString("O", CultureInfo.InvariantCulture);
                WriteDocument(document);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            ValidateProfileId(profileId);
            await ExecuteLockedAsync(() =>
            {
                ProfileDocument document = ReadDocument();
                ProfileEntry entry = FindEntry(document, profileId);
                if (entry != null)
                {
                    document.Entries.Remove(entry);
                    WriteDocument(document);
                }

                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<T> ExecuteLockedAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return action();
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                fileLock.Release();
            }
        }

        private ProfileDocument ReadDocument()
        {
            if (!File.Exists(filePath))
                return new ProfileDocument();

            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                ProfileDocument document = string.IsNullOrWhiteSpace(json)
                    ? new ProfileDocument()
                    : JsonUtility.FromJson<ProfileDocument>(json);
                if (document == null)
                    throw new InvalidDataException("Profile document is empty.");

                if (document.Entries == null)
                    document.Entries = new List<ProfileEntry>();
                return document;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Unable to read the Wi-Fi profile store.", exception);
            }
        }

        private void WriteDocument(ProfileDocument document)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("The Wi-Fi profile storage path has no directory.");

            Directory.CreateDirectory(directory);
            document.FormatVersion = CurrentFormatVersion;
            string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonUtility.ToJson(document, true),
                    new UTF8Encoding(false));
                ReplaceFile(temporaryPath, filePath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static void ReplaceFile(string temporaryPath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(temporaryPath, destinationPath);
                return;
            }

            try
            {
                File.Replace(temporaryPath, destinationPath, null);
            }
            catch
            {
                File.Copy(temporaryPath, destinationPath, true);
                File.Delete(temporaryPath);
            }
        }

        private static ProfileEntry FindEntry(ProfileDocument document, string profileId)
        {
            for (int index = 0; index < document.Entries.Count; index++)
            {
                if (string.Equals(document.Entries[index].Id, profileId, StringComparison.Ordinal))
                    return document.Entries[index];
            }

            return null;
        }

        private static void ValidateProfile(WifiProfile profile, WifiCredential credential)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            ValidateProfileId(profile.ProfileId);
            ValidateEncryptedCredential(credential);
            if (!string.IsNullOrEmpty(profile.Ssid)
                && !string.Equals(profile.Ssid, credential.Ssid, StringComparison.Ordinal))
            {
                throw new ArgumentException("Profile SSID must match the credential SSID.", nameof(profile));
            }
            if (profile.SecurityMode != credential.SecurityMode)
                throw new ArgumentException("Profile security mode must match the credential security mode.", nameof(profile));
        }

        private static void ValidateEncryptedCredential(WifiCredential credential)
        {
            if (credential == null)
                throw new ArgumentNullException(nameof(credential));
            if (string.IsNullOrWhiteSpace(credential.Ssid)
                || ContainsControlCharacter(credential.Ssid)
                || Encoding.UTF8.GetByteCount(credential.Ssid) > 32)
            {
                throw new ArgumentException("Invalid Wi-Fi profile SSID.", nameof(credential));
            }
            if (!Enum.IsDefined(typeof(WifiSecurityMode), credential.SecurityMode))
                throw new ArgumentException("Invalid Wi-Fi profile security mode.", nameof(credential));
            if (string.IsNullOrEmpty(credential.Password)
                || ContainsControlCharacter(credential.Password)
                || credential.Password.Length > 8192)
            {
                throw new ArgumentException("Invalid encrypted Wi-Fi profile payload.", nameof(credential));
            }
        }

        private static bool ContainsControlCharacter(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index]))
                    return true;
            }

            return false;
        }

        private static void ValidateProfileId(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId) || profileId.Length > 128)
                throw new ArgumentException("Invalid Wi-Fi profile identifier.", nameof(profileId));

            for (int index = 0; index < profileId.Length; index++)
            {
                if (char.IsControl(profileId[index]))
                    throw new ArgumentException("Invalid Wi-Fi profile identifier.", nameof(profileId));
            }
        }

        [Serializable]
        private sealed class ProfileDocument
        {
            public int FormatVersion = CurrentFormatVersion;
            public List<ProfileEntry> Entries = new List<ProfileEntry>();
        }

        [Serializable]
        private sealed class ProfileEntry
        {
            public string Id = "";
            public string DisplayName = "";
            public string Ssid = "";
            public WifiSecurityMode SecurityMode;
            public string Password = "";
            public string CreatedAtUtc = "";
            public string UpdatedAtUtc = "";

            public WifiProfile ToProfile()
            {
                return new WifiProfile
                {
                    ProfileId = Id,
                    DisplayName = DisplayName,
                    Ssid = Ssid,
                    SecurityMode = SecurityMode,
                    CreatedAtUtc = ParseTimestamp(CreatedAtUtc),
                    UpdatedAtUtc = ParseTimestamp(UpdatedAtUtc)
                };
            }

            public WifiCredential ToCredential()
            {
                return new WifiCredential
                {
                    Ssid = Ssid,
                    SecurityMode = SecurityMode,
                    Password = Password
                };
            }

            private static DateTimeOffset ParseTimestamp(string value)
            {
                return DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset timestamp)
                    ? timestamp
                    : DateTimeOffset.MinValue;
            }
        }
    }
}
