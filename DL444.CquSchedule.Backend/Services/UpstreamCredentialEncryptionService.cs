using System;
using System.Security.Cryptography;
using System.Text;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IUpstreamCredentialEncryptionService
    {
        string Encrypt(string password, string key);
    }

    internal sealed class UpstreamCredentialEncryptionService : IUpstreamCredentialEncryptionService
    {
        public string Encrypt(string password, string key) => GetDesString(password, key);

        private static string GetDesString(string data, string key)
        {
            byte[] plaintext = new UTF8Encoding(false, true).GetBytes(data);
            using var des = DES.Create();
            des.Key = Convert.FromBase64String(key);
            byte[] encrypted = des.EncryptEcb(plaintext, PaddingMode.PKCS7);
            return Convert.ToBase64String(encrypted);
        }
    }
}
