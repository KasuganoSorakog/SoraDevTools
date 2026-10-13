using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SoraTools.HeadsetWifi
{
    /// <summary>
    /// Persists profile metadata and Keystore ciphertext in a JSON file while keeping the PSK out of plaintext storage.
    /// </summary>
    public sealed class KeystoreBackedWifiProfileStore : ISecretStore
    {
        private readonly ISecretStore ciphertextStore;
        private readonly IPlatformSecretCipher secretCipher;

        public KeystoreBackedWifiProfileStore(ISecretStore ciphertextStore, IPlatformSecretCipher secretCipher)
        {
            this.ciphertextStore = ciphertextStore ?? throw new ArgumentNullException(nameof(ciphertextStore));
            this.secretCipher = secretCipher ?? throw new ArgumentNullException(nameof(secretCipher));
        }

        public Task<IReadOnlyList<WifiProfile>> ListProfilesAsync(CancellationToken cancellationToken)
        {
            return ciphertextStore.ListProfilesAsync(cancellationToken);
        }

        public Task<WifiProfile> GetProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            return ciphertextStore.GetProfileAsync(profileId, cancellationToken);
        }

        public async Task<WifiCredential> GetCredentialAsync(string profileId, CancellationToken cancellationToken)
        {
            WifiCredential encrypted = await ciphertextStore.GetCredentialAsync(profileId, cancellationToken)
                .ConfigureAwait(false);
            if (encrypted == null)
                return null;

            string password = await secretCipher.DecryptAsync(encrypted.Password, cancellationToken)
                .ConfigureAwait(false);
            return new WifiCredential
            {
                Ssid = encrypted.Ssid,
                SecurityMode = encrypted.SecurityMode,
                Password = password
            };
        }

        public async Task SaveProfileAsync(
            WifiProfile profile,
            WifiCredential credential,
            CancellationToken cancellationToken)
        {
            if (credential == null)
                throw new ArgumentNullException(nameof(credential));
            if (!WifiCredentialValidator.TryValidate(credential, out string failureCode))
            {
                throw new ArgumentException(
                    "Invalid Wi-Fi credential: " + failureCode,
                    nameof(credential));
            }

            string ciphertext = await secretCipher.EncryptAsync(credential.Password, cancellationToken)
                .ConfigureAwait(false);
            var encryptedCredential = new WifiCredential
            {
                Ssid = credential.Ssid,
                SecurityMode = credential.SecurityMode,
                Password = ciphertext
            };
            await ciphertextStore.SaveProfileAsync(profile, encryptedCredential, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            return ciphertextStore.DeleteProfileAsync(profileId, cancellationToken);
        }
    }
}
