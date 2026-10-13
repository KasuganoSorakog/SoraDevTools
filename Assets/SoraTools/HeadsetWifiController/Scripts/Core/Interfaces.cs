using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SoraTools.HeadsetWifi
{
    public interface IAndroidDeviceSession
    {
        string DeviceSessionId { get; }

        /// <summary>
        /// 通过平板 USB Host 的 ADB 传输，在远端 PICO adbd shell 执行 argv 命令。
        /// 实现不得在平板应用自身进程中执行该命令。
        /// </summary>
        Task<AndroidCommandResult> ExecuteShellAsync(
            AndroidShellCommand command,
            System.TimeSpan timeout,
            CancellationToken cancellationToken);
    }

    public interface IWifiCapabilityProbe
    {
        Task<WifiCapabilities> ProbeAsync(
            IAndroidDeviceSession deviceSession,
            CancellationToken cancellationToken);
    }

    public interface IWifiMigrationService
    {
        Task<WifiCapabilities> ProbeCapabilitiesAsync(CancellationToken cancellationToken);

        Task<WifiMigrationResult> MigrateAsync(
            WifiMigrationRequest request,
            CancellationToken cancellationToken);
    }

    public interface ISecretStore
    {
        Task<IReadOnlyList<WifiProfile>> ListProfilesAsync(CancellationToken cancellationToken);

        Task<WifiProfile> GetProfileAsync(string profileId, CancellationToken cancellationToken);

        Task<WifiCredential> GetCredentialAsync(string profileId, CancellationToken cancellationToken);

        Task SaveProfileAsync(
            WifiProfile profile,
            WifiCredential credential,
            CancellationToken cancellationToken);

        Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken);
    }

    public interface IOperationStore
    {
        Task AppendAsync(WifiOperationRecord operation, CancellationToken cancellationToken);

        Task<IReadOnlyList<WifiOperationRecord>> GetRecentAsync(
            int maximumCount,
            CancellationToken cancellationToken);
    }
}
