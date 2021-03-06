using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Models;
using Microsoft.Extensions.Configuration;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IStoredCredentialEncryptionService
    {
        Task<User> EncryptAsync(User user);
        Task<User> DecryptAsync(User user);
    }

    internal class StoredCredentialEncryptionService : IStoredCredentialEncryptionService
    {
        public StoredCredentialEncryptionService(IConfiguration config)
        {
            string key = config.GetValue<string>("Credential:EncryptionKey");
            this.key = Convert.FromBase64String(key);
        }

        public async Task<User> EncryptAsync(User user)
        {
            if (user.Iv != null)
            {
                throw new InvalidOperationException("IV is not empty. Possibly already encrypted.");
            }
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                user.Iv = Convert.ToBase64String(aes.IV);
                using (var encryptedStream = new MemoryStream())
                {
                    using (var cryptoStream = new CryptoStream(encryptedStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        using (var writer = new StreamWriter(cryptoStream))
                        {
                            await writer.WriteAsync(user.Password);
                        }
                    }
                    user.Password = Convert.ToBase64String(encryptedStream.ToArray());
                }
            }
            return user;
        }

        public async Task<User> DecryptAsync(User user)
        {
            if (user.Iv == null)
            {
                throw new ArgumentException("Missing IV in provided credential.");
            }
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = Convert.FromBase64String(user.Iv);
                using (var encryptedStream = new MemoryStream(Convert.FromBase64String(user.Password)))
                {
                    using (var cryptoStream = new CryptoStream(encryptedStream, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        using (var reader = new StreamReader(cryptoStream))
                        {
                            user.Password = await reader.ReadToEndAsync();
                        }
                    }
                }
            }
            user.Iv = null;
            return user;
        }

        private byte[] key;
    }
}