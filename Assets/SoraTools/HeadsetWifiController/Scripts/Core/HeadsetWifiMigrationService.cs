using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SoraTools.HeadsetWifi
{
    public sealed class HeadsetWifiMigrationService : IWifiMigrationService
    {
        public const int MinimumAndroidApiLevel = 30;
        public static readonly TimeSpan RequiredStabilityWindow = TimeSpan.FromSeconds(60);

        private static readonly TimeSpan ControlCommandTimeout = TimeSpan.FromSeconds(8);

        private readonly IAndroidDeviceSession deviceSession;
        private readonly IWifiCapabilityProbe capabilityProbe;
        private readonly ISecretStore secretStore;
        private readonly IOperationStore operationStore;
        private readonly SemaphoreSlim migrationGate = new SemaphoreSlim(1, 1);

        public HeadsetWifiMigrationService(
            IAndroidDeviceSession deviceSession,
            IWifiCapabilityProbe capabilityProbe,
            ISecretStore secretStore = null,
            IOperationStore operationStore = null)
        {
            this.deviceSession = deviceSession ?? throw new ArgumentNullException(nameof(deviceSession));
            this.capabilityProbe = capabilityProbe ?? throw new ArgumentNullException(nameof(capabilityProbe));
            this.secretStore = secretStore;
            this.operationStore = operationStore;
        }

        public Task<WifiCapabilities> ProbeCapabilitiesAsync(CancellationToken cancellationToken)
        {
            return capabilityProbe.ProbeAsync(deviceSession, cancellationToken);
        }

        public async Task<WifiMigrationResult> MigrateAsync(
            WifiMigrationRequest request,
            CancellationToken cancellationToken)
        {
            var context = new MigrationContext(request, deviceSession.DeviceSessionId);
            WifiMigrationResult result = null;
            bool gateAcquired = false;

            try
            {
                if (request == null)
                {
                    result = CompleteFailure(context, WifiMigrationFailureKind.InvalidRequest, "RequestMissing");
                }
                else
                {
                    gateAcquired = await migrationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
                    if (!gateAcquired)
                    {
                        result = CompleteFailure(
                            context,
                            WifiMigrationFailureKind.OperationAlreadyRunning,
                            "OperationAlreadyRunning");
                    }
                    else
                    {
                        result = await RunMigrationAsync(context, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result = CompleteCancelled(context);
            }
            catch
            {
                result = CompleteFailure(context, WifiMigrationFailureKind.Unexpected, "Unexpected");
            }
            finally
            {
                if (gateAcquired)
                    migrationGate.Release();
            }

            if (result == null)
                result = CompleteFailure(context, WifiMigrationFailureKind.Unexpected, "Unexpected");

            await TryAppendOperationAsync(context, result).ConfigureAwait(false);
            return result;
        }

        private async Task<WifiMigrationResult> RunMigrationAsync(
            MigrationContext context,
            CancellationToken cancellationToken)
        {
            Transition(context, WifiMigrationState.Prechecking, "PrecheckStarted");

            context.Credential = await ResolveCredentialAsync(context, cancellationToken).ConfigureAwait(false);
            if (context.Credential == null)
            {
                return CompleteFailure(
                    context,
                    WifiMigrationFailureKind.CredentialUnavailable,
                    "CredentialUnavailable");
            }

            if (!WifiCredentialValidator.TryValidate(context.Credential, out string credentialFailureCode))
            {
                return CompleteFailure(
                    context,
                    WifiMigrationFailureKind.InvalidRequest,
                    credentialFailureCode);
            }

            if (!TryValidateRequestSettings(context, out string requestFailureCode))
            {
                return CompleteFailure(
                    context,
                    WifiMigrationFailureKind.InvalidRequest,
                    requestFailureCode);
            }

            context.Capabilities = await capabilityProbe.ProbeAsync(
                deviceSession,
                cancellationToken).ConfigureAwait(false);
            context.Device = context.Capabilities != null && context.Capabilities.Device != null
                ? context.Capabilities.Device
                : context.Device;

            int apiLevel = context.Device.Properties != null
                ? context.Device.Properties.AndroidApiLevel
                : 0;
            if (apiLevel < MinimumAndroidApiLevel)
            {
                return CompleteFailure(
                    context,
                    WifiMigrationFailureKind.UnsupportedAndroidVersion,
                    "AndroidApiLevelUnsupported");
            }

            if (context.Capabilities == null || !context.Capabilities.CanMigrate)
            {
                return CompleteFailure(
                    context,
                    GetCapabilityFailure(context.Capabilities),
                    "WifiCommandUnsupported");
            }
            if (!SupportsSecurityMode(context.Capabilities, context.Credential.SecurityMode))
            {
                return CompleteFailure(
                    context,
                    WifiMigrationFailureKind.UnsupportedFirmware,
                    "SecurityModeUnsupported");
            }

            Transition(context, WifiMigrationState.Applying, "ConnectRequested");
            WifiMigrationFailureKind commandFailure = await ExecuteCommandAsync(
                WifiShellCommandBuilder.BuildSetWifiEnabledCommand(true),
                cancellationToken).ConfigureAwait(false);
            if (commandFailure != WifiMigrationFailureKind.None)
                return CompleteFailure(context, commandFailure, "EnableWifiRejected");

            commandFailure = await ExecuteCommandAsync(
                WifiShellCommandBuilder.BuildConnectNetworkCommand(context.Credential),
                cancellationToken).ConfigureAwait(false);
            if (commandFailure != WifiMigrationFailureKind.None)
                return CompleteFailure(context, commandFailure, "ConnectNetworkRejected");

            Transition(context, WifiMigrationState.Verifying, "ConnectivityVerificationStarted");
            ConnectivityValidation validation = await WaitForConnectivityAsync(
                context,
                cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
                return CompleteFailure(context, validation.FailureKind, "ConnectivityVerificationFailed");

            Transition(context, WifiMigrationState.ObservingStability, "StabilityObservationStarted");
            ConnectivityValidation stability = await ObserveStabilityAsync(
                context,
                cancellationToken).ConfigureAwait(false);
            if (!stability.IsValid)
            {
                return CompleteFailure(
                    context,
                    WifiMigrationFailureKind.StabilityLost,
                    "StabilityLost_" + stability.FailureKind);
            }

            context.NetworkValidated = true;
            await PersistCredentialIfRequestedAsync(context, cancellationToken).ConfigureAwait(false);
            Transition(context, WifiMigrationState.Succeeded, "MigrationSucceeded");
            return BuildResult(
                context,
                true,
                WifiMigrationFailureKind.None,
                "MigrationSucceeded");
        }

        private async Task<WifiCredential> ResolveCredentialAsync(
            MigrationContext context,
            CancellationToken cancellationToken)
        {
            if (context.Request.Credential != null)
            {
                context.ProfileId = context.Request.ProfileId ?? "";
                return CloneCredential(context.Request.Credential);
            }

            if (string.IsNullOrWhiteSpace(context.Request.ProfileId) || secretStore == null)
                return null;

            context.ProfileId = context.Request.ProfileId;
            WifiCredential storedCredential = await secretStore.GetCredentialAsync(
                context.ProfileId,
                cancellationToken).ConfigureAwait(false);
            return storedCredential == null ? null : CloneCredential(storedCredential);
        }

        private static bool TryValidateRequestSettings(MigrationContext context, out string failureCode)
        {
            failureCode = "";
            string interfaceName = string.IsNullOrWhiteSpace(context.Request.WifiInterfaceName)
                ? "wlan0"
                : context.Request.WifiInterfaceName;
            if (!WifiShellCommandBuilder.IsValidInterfaceName(interfaceName))
            {
                failureCode = "WifiInterfaceInvalid";
                return false;
            }

            if (context.Request.ConnectionTimeout <= TimeSpan.Zero
                || context.Request.ConnectionTimeout > TimeSpan.FromMinutes(3))
            {
                failureCode = "ConnectionTimeoutInvalid";
                return false;
            }

            if (context.Request.PollInterval < TimeSpan.FromMilliseconds(500)
                || context.Request.PollInterval > TimeSpan.FromSeconds(10))
            {
                failureCode = "PollIntervalInvalid";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(context.Request.BusinessAddress)
                && !WifiShellCommandBuilder.TryValidateBusinessAddress(
                    context.Request.BusinessAddress,
                    out _))
            {
                failureCode = "BusinessAddressInvalid";
                return false;
            }

            context.WifiInterfaceName = interfaceName;
            return true;
        }

        private static bool SupportsSecurityMode(
            WifiCapabilities capabilities,
            WifiSecurityMode securityMode)
        {
            return securityMode == WifiSecurityMode.Wpa2Psk
                ? capabilities.SupportsWpa2
                : securityMode == WifiSecurityMode.Wpa3Sae && capabilities.SupportsWpa3;
        }

        private async Task<WifiMigrationFailureKind> ExecuteCommandAsync(
            AndroidShellCommand command,
            CancellationToken cancellationToken)
        {
            AndroidCommandResult result;
            try
            {
                result = await deviceSession.ExecuteShellAsync(
                    command,
                    ControlCommandTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return WifiMigrationFailureKind.DeviceUnavailable;
            }

            return WifiCommandResultClassifier.Classify(result);
        }

        private async Task<ConnectivityValidation> WaitForConnectivityAsync(
            MigrationContext context,
            CancellationToken cancellationToken)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(context.Request.ConnectionTimeout);
            ConnectivityValidation validation = ConnectivityValidation.Failed(
                WifiMigrationFailureKind.ConnectionTimeout);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                validation = await ValidateConnectivityAsync(context, cancellationToken).ConfigureAwait(false);
                context.LastConnectionStatus = validation.Status;
                if (validation.IsValid || DateTimeOffset.UtcNow >= deadline)
                    return validation;

                await DelayUntilNextPollAsync(context.Request.PollInterval, deadline, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task<ConnectivityValidation> ObserveStabilityAsync(
            MigrationContext context,
            CancellationToken cancellationToken)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(RequiredStabilityWindow);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConnectivityValidation validation = await ValidateConnectivityAsync(
                    context,
                    cancellationToken).ConfigureAwait(false);
                context.LastConnectionStatus = validation.Status;
                if (!validation.IsValid)
                    return validation;

                await DelayUntilNextPollAsync(context.Request.PollInterval, deadline, cancellationToken)
                    .ConfigureAwait(false);
            }

            return ConnectivityValidation.Valid(context.LastConnectionStatus);
        }

        private async Task<ConnectivityValidation> ValidateConnectivityAsync(
            MigrationContext context,
            CancellationToken cancellationToken)
        {
            AndroidCommandResult wifiStatusResult = await ExecuteForObservationAsync(
                WifiShellCommandBuilder.BuildWifiStatusCommand(),
                cancellationToken).ConfigureAwait(false);
            WifiMigrationFailureKind commandFailure = WifiCommandResultClassifier.Classify(wifiStatusResult);
            if (commandFailure != WifiMigrationFailureKind.None)
                return ConnectivityValidation.Failed(commandFailure, context.LastConnectionStatus);

            AndroidCommandResult ipAddressResult = await ExecuteForObservationAsync(
                WifiShellCommandBuilder.BuildIpAddressCommand(context.WifiInterfaceName),
                cancellationToken).ConfigureAwait(false);
            commandFailure = WifiCommandResultClassifier.Classify(ipAddressResult);
            if (commandFailure != WifiMigrationFailureKind.None)
                return ConnectivityValidation.Failed(commandFailure, context.LastConnectionStatus);

            AndroidCommandResult routeResult = await ExecuteForObservationAsync(
                WifiShellCommandBuilder.BuildDefaultRouteCommand(),
                cancellationToken).ConfigureAwait(false);
            commandFailure = WifiCommandResultClassifier.Classify(routeResult);
            if (commandFailure != WifiMigrationFailureKind.None)
                return ConnectivityValidation.Failed(commandFailure, context.LastConnectionStatus);

            WifiConnectionStatus status = WifiStatusParser.Parse(
                wifiStatusResult.StandardOutput,
                ipAddressResult.StandardOutput,
                routeResult.StandardOutput,
                context.WifiInterfaceName);
            if (status.State == WifiConnectionState.Disabled)
                return ConnectivityValidation.Failed(WifiMigrationFailureKind.WifiDisabled, status);
            if (!string.Equals(status.Ssid, context.Credential.Ssid, StringComparison.Ordinal))
                return ConnectivityValidation.Failed(WifiMigrationFailureKind.SsidMismatch, status);
            if (!status.HasIpv4Address)
                return ConnectivityValidation.Failed(WifiMigrationFailureKind.IpAddressUnavailable, status);
            if (!status.HasDefaultRoute
                || !string.Equals(
                    status.DefaultRouteInterface,
                    context.WifiInterfaceName,
                    StringComparison.Ordinal))
            {
                return ConnectivityValidation.Failed(WifiMigrationFailureKind.DefaultRouteMissing, status);
            }

            if (!string.IsNullOrWhiteSpace(context.Request.BusinessAddress))
            {
                AndroidCommandResult pingResult = await ExecuteForObservationAsync(
                    WifiShellCommandBuilder.BuildPingCommand(context.Request.BusinessAddress),
                    cancellationToken).ConfigureAwait(false);
                commandFailure = WifiCommandResultClassifier.Classify(pingResult);
                if (commandFailure != WifiMigrationFailureKind.None)
                    return ConnectivityValidation.Failed(commandFailure, status);
                if (!HasSuccessfulPing(pingResult))
                    return ConnectivityValidation.Failed(WifiMigrationFailureKind.BusinessAddressUnreachable, status);
            }

            return ConnectivityValidation.Valid(status);
        }

        private async Task<AndroidCommandResult> ExecuteForObservationAsync(
            AndroidShellCommand command,
            CancellationToken cancellationToken)
        {
            try
            {
                AndroidCommandResult result = await deviceSession.ExecuteShellAsync(
                    command,
                    ControlCommandTimeout,
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

        private async Task PersistCredentialIfRequestedAsync(
            MigrationContext context,
            CancellationToken cancellationToken)
        {
            if (!context.Request.PersistCredential)
                return;

            Transition(context, WifiMigrationState.Persisting, "CredentialPersistenceStarted");
            if (secretStore == null)
            {
                context.Warning = WifiMigrationWarning.CredentialNotPersisted;
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(context.ProfileId))
                    context.ProfileId = CreateProfileId(context.Credential);

                await secretStore.SaveProfileAsync(
                    new WifiProfile
                    {
                        ProfileId = context.ProfileId,
                        DisplayName = string.IsNullOrWhiteSpace(context.Request.ProfileDisplayName)
                            ? context.Credential.Ssid
                            : context.Request.ProfileDisplayName,
                        Ssid = context.Credential.Ssid,
                        SecurityMode = context.Credential.SecurityMode
                    },
                    context.Credential,
                    cancellationToken).ConfigureAwait(false);
                context.CredentialPersisted = true;
            }
            catch
            {
                context.Warning = WifiMigrationWarning.CredentialNotPersisted;
            }
        }

        private async Task TryAppendOperationAsync(
            MigrationContext context,
            WifiMigrationResult result)
        {
            if (operationStore == null)
                return;

            try
            {
                await operationStore.AppendAsync(
                    WifiOperationRecord.FromResult(result, context.Credential),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                if (result.Warning == WifiMigrationWarning.None)
                    result.Warning = WifiMigrationWarning.OperationNotRecorded;
            }
        }

        private static async Task DelayUntilNextPollAsync(
            TimeSpan pollInterval,
            DateTimeOffset deadline,
            CancellationToken cancellationToken)
        {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            TimeSpan delay = pollInterval < remaining ? pollInterval : remaining;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        private static bool HasSuccessfulPing(AndroidCommandResult result)
        {
            string output = WifiCommandResultClassifier.CombineOutput(result);
            return output.IndexOf("0% packet loss", StringComparison.OrdinalIgnoreCase) >= 0
                && output.IndexOf("100% packet loss", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static WifiMigrationFailureKind GetCapabilityFailure(WifiCapabilities capabilities)
        {
            if (capabilities == null || string.IsNullOrWhiteSpace(capabilities.ProbeFailureCode))
                return WifiMigrationFailureKind.UnsupportedFirmware;

            return Enum.TryParse(
                capabilities.ProbeFailureCode,
                true,
                out WifiMigrationFailureKind failureKind)
                ? failureKind
                : WifiMigrationFailureKind.UnsupportedFirmware;
        }

        private WifiMigrationResult CompleteFailure(
            MigrationContext context,
            WifiMigrationFailureKind failureKind,
            string messageCode)
        {
            Transition(context, WifiMigrationState.Failed, messageCode);
            return BuildResult(context, false, failureKind, messageCode);
        }

        private WifiMigrationResult CompleteCancelled(MigrationContext context)
        {
            Transition(context, WifiMigrationState.Cancelled, "Cancelled");
            return BuildResult(
                context,
                false,
                WifiMigrationFailureKind.Cancelled,
                "Cancelled");
        }

        private static WifiMigrationResult BuildResult(
            MigrationContext context,
            bool succeeded,
            WifiMigrationFailureKind failureKind,
            string messageCode)
        {
            return new WifiMigrationResult
            {
                OperationId = context.OperationId,
                Succeeded = succeeded,
                NetworkValidated = context.NetworkValidated,
                CredentialPersisted = context.CredentialPersisted,
                ProfileId = context.ProfileId ?? "",
                FinalState = context.States.Count > 0
                    ? context.States[context.States.Count - 1]
                    : WifiMigrationState.Failed,
                FailureKind = failureKind,
                Warning = context.Warning,
                MessageCode = messageCode,
                StartedAtUtc = context.StartedAtUtc,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                Device = CloneDevice(context.Device),
                LastConnectionStatus = CloneStatus(context.LastConnectionStatus),
                StateHistory = new List<WifiMigrationState>(context.States).AsReadOnly()
            };
        }

        private static void Transition(
            MigrationContext context,
            WifiMigrationState state,
            string code)
        {
            if (context.States.Count > 0 && context.States[context.States.Count - 1] == state)
                return;

            context.States.Add(state);
            Action<WifiMigrationProgress> callback = context.Request != null
                ? context.Request.ProgressChanged
                : null;
            if (callback == null)
                return;

            try
            {
                callback(new WifiMigrationProgress
                {
                    OperationId = context.OperationId,
                    State = state,
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    TargetSsid = context.Credential != null ? context.Credential.Ssid : "",
                    Code = code
                });
            }
            catch
            {
            }
        }

        private static WifiCredential CloneCredential(WifiCredential source)
        {
            return new WifiCredential
            {
                Ssid = source.Ssid,
                SecurityMode = source.SecurityMode,
                Password = source.Password
            };
        }

        private static AndroidDeviceDescriptor CloneDevice(AndroidDeviceDescriptor source)
        {
            AndroidDeviceProperties properties = source != null ? source.Properties : null;
            return new AndroidDeviceDescriptor
            {
                SessionId = source != null ? source.SessionId : "",
                Properties = new AndroidDeviceProperties
                {
                    Manufacturer = properties != null ? properties.Manufacturer : "",
                    Model = properties != null ? properties.Model : "",
                    Device = properties != null ? properties.Device : "",
                    SerialNumber = properties != null ? properties.SerialNumber : "",
                    AndroidApiLevel = properties != null ? properties.AndroidApiLevel : 0,
                    AndroidRelease = properties != null ? properties.AndroidRelease : "",
                    BuildFingerprint = properties != null ? properties.BuildFingerprint : "",
                    ShellUserId = properties != null ? properties.ShellUserId : ""
                }
            };
        }

        private static WifiConnectionStatus CloneStatus(WifiConnectionStatus source)
        {
            if (source == null)
                return new WifiConnectionStatus();

            return new WifiConnectionStatus
            {
                State = source.State,
                Ssid = source.Ssid,
                Bssid = source.Bssid,
                InterfaceName = source.InterfaceName,
                IpAddress = source.IpAddress,
                DefaultRouteInterface = source.DefaultRouteInterface,
                DefaultGateway = source.DefaultGateway,
                Rssi = source.Rssi,
                HasIpv4Address = source.HasIpv4Address,
                HasDefaultRoute = source.HasDefaultRoute
            };
        }

        private static string CreateProfileId(WifiCredential credential)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(credential.Ssid + "\0" + credential.SecurityMode);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder("wifi-");
                for (int index = 0; index < 8; index++)
                    builder.Append(hash[index].ToString("x2"));
                return builder.ToString();
            }
        }

        private sealed class MigrationContext
        {
            public readonly WifiMigrationRequest Request;
            public readonly string OperationId = Guid.NewGuid().ToString("N");
            public readonly DateTimeOffset StartedAtUtc = DateTimeOffset.UtcNow;
            public readonly List<WifiMigrationState> States = new List<WifiMigrationState>();
            public WifiCredential Credential;
            public WifiCapabilities Capabilities;
            public AndroidDeviceDescriptor Device;
            public WifiConnectionStatus LastConnectionStatus = new WifiConnectionStatus();
            public string ProfileId = "";
            public string WifiInterfaceName = "wlan0";
            public bool NetworkValidated;
            public bool CredentialPersisted;
            public WifiMigrationWarning Warning;

            public MigrationContext(WifiMigrationRequest request, string sessionId)
            {
                Request = request;
                Device = new AndroidDeviceDescriptor { SessionId = sessionId ?? "" };
            }
        }

        private sealed class ConnectivityValidation
        {
            public bool IsValid;
            public WifiMigrationFailureKind FailureKind;
            public WifiConnectionStatus Status;

            public static ConnectivityValidation Valid(WifiConnectionStatus status)
            {
                return new ConnectivityValidation
                {
                    IsValid = true,
                    FailureKind = WifiMigrationFailureKind.None,
                    Status = status ?? new WifiConnectionStatus()
                };
            }

            public static ConnectivityValidation Failed(
                WifiMigrationFailureKind failureKind,
                WifiConnectionStatus status = null)
            {
                return new ConnectivityValidation
                {
                    IsValid = false,
                    FailureKind = failureKind,
                    Status = status ?? new WifiConnectionStatus()
                };
            }
        }
    }

}
