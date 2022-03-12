using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IUpstreamCredentialEncryptionService
    {
        string Encrypt(string password, string key);
    }

    internal sealed class UpstreamCredentialEncryptionService : IUpstreamCredentialEncryptionService
    {
        public string Encrypt(string password, string key) => GetAesString(GetRandomString(64) + password, key, GetRandomString(16));

        private static string GetAesString(string data, string key, string iv)
        {
            Regex regex = new Regex(@"/(^\s+)|(\s+$)/g");
            key = regex.Replace(key, "");
            var utf8 = new UTF8Encoding(false, true);
            byte[] keyBytes = utf8.GetBytes(key);
            byte[] ivBytes = utf8.GetBytes(iv);
            byte[] plaintext = utf8.GetBytes(data);
            using var aes = Aes.Create();
            aes.Key = keyBytes;
            byte[] encrypted = aes.EncryptCbc(plaintext, ivBytes);
            return Convert.ToBase64String(encrypted);
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
