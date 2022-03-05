using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IUpstreamCredentialEncryptionService
    {
        Task<string> EncryptAsync(string password, string key);
    }

    internal sealed class UpstreamCredentialEncryptionService : IUpstreamCredentialEncryptionService
    {
        public Task<string> EncryptAsync(string password, string key) => GetAesStringAsync(GetRandomString(64) + password, key, GetRandomString(16));

        private static async Task<string> GetAesStringAsync(string data, string key, string iv)
        {
            Regex regex = new Regex(@"/(^\s+)|(\s+$)/g");
            key = regex.Replace(key, "");
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] ivBytes = Encoding.UTF8.GetBytes(iv);
            using var aes = GetAes(keyBytes, ivBytes);
            using MemoryStream outStream = new MemoryStream();
            using (var encryptor = aes.CreateEncryptor())
            {
                using CryptoStream cryptStream = new CryptoStream(outStream, encryptor, CryptoStreamMode.Write);
                using var writer = new StreamWriter(cryptStream, Encoding.GetEncoding("GB2312"));
                await writer.WriteAsync(data);
            }
            byte[] encrypted = outStream.ToArray();
            return Convert.ToBase64String(encrypted);
        }

        private static Aes GetAes(byte[] key, byte[] iv)
        {
            var encryptor = Aes.Create();
            encryptor.Mode = CipherMode.CBC;
            encryptor.KeySize = 128;
            encryptor.Padding = PaddingMode.PKCS7;
            encryptor.Key = key;
            encryptor.IV = iv;
            return encryptor;
        }

        private static string GetRandomString(int length)
        {
            StringBuilder builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                builder.Append(charCandidates[random.Next(0, charCandidates.Length)]);
            }
            return builder.ToString();
        }

        private static readonly Random random = new Random((int)DateTime.UtcNow.Ticks);
        private const string charCandidates = "ABCDEFGHJKMNPQRSTWXYZabcdefhijkmnprstwxyz2345678";
    }
}
