using System;
using System.Text;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using DL444.CquSchedule.Backend.Models;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IStoredCredentialEncryptionService
    {
        Task<User> EncryptAsync(User user);
        Task<User> DecryptAsync(User user);
    }

    internal sealed class KeyVaultCredentialEncryptionService : IStoredCredentialEncryptionService
    {
        public KeyVaultCredentialEncryptionService(ICryptographyClientContainerService clientContainerService) => this.clientContainerService = clientContainerService;

        public async Task<User> EncryptAsync(User user)
        {
            if (user.KeyId != null)
            {
                throw new InvalidOperationException("KeyId is not empty. Possibly already encrypted.");
            }
            CryptographyClient client = clientContainerService.Client;
            byte[] plaintext = Encoding.UTF8.GetBytes(user.Password);
            EncryptResult result = await client.EncryptAsync(EncryptionAlgorithm.RsaOaep256, plaintext);
            user.Password = Convert.ToBase64String(result.Ciphertext);
            user.KeyId = client.KeyId;
            return user;
        }

        public async Task<User> DecryptAsync(User user)
        {
            if (user.KeyId == null)
            {
                throw new InvalidOperationException("Missing KeyId in provided credential.");
            }
            CryptographyClient client = clientContainerService.Client;
            if (!user.KeyId.Equals(client.KeyId))
            {
                client = new CryptographyClient(new Uri(user.KeyId), new DefaultAzureCredential());
            }
            byte[] ciphertext = Convert.FromBase64String(user.Password);
            DecryptResult result = await client.DecryptAsync(EncryptionAlgorithm.RsaOaep256, ciphertext);
            user.Password = Encoding.UTF8.GetString(result.Plaintext);
            user.KeyId = null;
            return user;
        }

        private readonly ICryptographyClientContainerService clientContainerService;
    }
}
