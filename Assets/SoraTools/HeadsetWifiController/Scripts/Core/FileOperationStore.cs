using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SoraTools.HeadsetWifi
{
    public sealed class FileOperationStore : IOperationStore
    {
        private const int CurrentFormatVersion = 1;
        private const int DefaultMaximumRecords = 100;

        private readonly string filePath;
        private readonly int maximumRecords;
        private readonly SemaphoreSlim fileLock = new SemaphoreSlim(1, 1);

        public FileOperationStore(string filePath, int maximumRecords = DefaultMaximumRecords)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("An operation storage path is required.", nameof(filePath));
            if (maximumRecords < 1 || maximumRecords > 1000)
                throw new ArgumentOutOfRangeException(nameof(maximumRecords));

            this.filePath = filePath;
            this.maximumRecords = maximumRecords;
        }

        public static string GetDefaultFilePath(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
                throw new ArgumentException("Persistent data path is required.", nameof(persistentDataPath));

            return Path.Combine(persistentDataPath, "headset_wifi_operations.json");
        }

        public async Task AppendAsync(WifiOperationRecord operation, CancellationToken cancellationToken)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            await ExecuteLockedAsync(() =>
            {
                OperationDocument document = ReadDocument();
                document.Records.Insert(0, Sanitize(operation));
                if (document.Records.Count > maximumRecords)
                    document.Records.RemoveRange(maximumRecords, document.Records.Count - maximumRecords);
                WriteDocument(document);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<WifiOperationRecord>> GetRecentAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            if (maximumCount < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumCount));

            return await ExecuteLockedAsync(() =>
            {
                OperationDocument document = ReadDocument();
                int count = Math.Min(maximumCount, document.Records.Count);
                var records = new List<WifiOperationRecord>(count);
                for (int index = 0; index < count; index++)
                    records.Add(Clone(document.Records[index]));

                return (IReadOnlyList<WifiOperationRecord>)records;
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

        private OperationDocument ReadDocument()
        {
            if (!File.Exists(filePath))
                return new OperationDocument();

            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                OperationDocument document = string.IsNullOrWhiteSpace(json)
                    ? new OperationDocument()
                    : JsonUtility.FromJson<OperationDocument>(json);
                if (document == null)
                    throw new InvalidDataException("Operation document is empty.");

                if (document.Records == null)
                    document.Records = new List<WifiOperationRecord>();
                return document;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Unable to read the Wi-Fi operation store.", exception);
            }
        }

        private void WriteDocument(OperationDocument document)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("The Wi-Fi operation storage path has no directory.");

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

        private static WifiOperationRecord Sanitize(WifiOperationRecord source)
        {
            return new WifiOperationRecord
            {
                OperationId = SafeCode(source.OperationId, 64),
                StartedAtUtc = SafeCode(source.StartedAtUtc, 40),
                FinishedAtUtc = SafeCode(source.FinishedAtUtc, 40),
                DeviceHint = WifiRedactor.MaskDeviceIdentifier(source.DeviceHint, ""),
                TargetSsidHint = WifiRedactor.MaskSsid(source.TargetSsidHint),
                SecurityMode = IsDefined(source.SecurityMode) ? source.SecurityMode : WifiSecurityMode.Wpa2Psk,
                FinalState = IsDefined(source.FinalState) ? source.FinalState : WifiMigrationState.Failed,
                FailureKind = IsDefined(source.FailureKind) ? source.FailureKind : WifiMigrationFailureKind.Unexpected,
                Warning = IsDefined(source.Warning) ? source.Warning : WifiMigrationWarning.None,
                MessageCode = SafeCode(source.MessageCode, 96),
                LastObservedState = SafeCode(source.LastObservedState, 48)
            };
        }

        private static WifiOperationRecord Clone(WifiOperationRecord source)
        {
            return new WifiOperationRecord
            {
                OperationId = source.OperationId,
                StartedAtUtc = source.StartedAtUtc,
                FinishedAtUtc = source.FinishedAtUtc,
                DeviceHint = source.DeviceHint,
                TargetSsidHint = source.TargetSsidHint,
                SecurityMode = source.SecurityMode,
                FinalState = source.FinalState,
                FailureKind = source.FailureKind,
                Warning = source.Warning,
                MessageCode = source.MessageCode,
                LastObservedState = source.LastObservedState
            };
        }

        private static bool IsDefined<T>(T value) where T : struct, IConvertible
        {
            return Enum.IsDefined(typeof(T), value);
        }

        private static string SafeCode(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
            for (int index = 0; index < value.Length && builder.Length < maximumLength; index++)
            {
                char character = value[index];
                if (char.IsLetterOrDigit(character)
                    || character == '-'
                    || character == '_'
                    || character == '.'
                    || character == ':')
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class OperationDocument
        {
            public int FormatVersion = CurrentFormatVersion;
            public List<WifiOperationRecord> Records = new List<WifiOperationRecord>();
        }
    }
}
