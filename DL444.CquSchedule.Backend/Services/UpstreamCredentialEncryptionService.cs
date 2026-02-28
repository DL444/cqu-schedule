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
        public string Encrypt(string password, string key)
        {
            byte[] keyBytes = Convert.FromBase64String(key);
            return keyBytes.Length switch
            {
                8 => EncryptDes(password, keyBytes),
                16 or 24 or 32 => EncryptAes(password, keyBytes),
                _ => throw new CryptographicException($"Upstream credential key size is unsupported: {keyBytes.Length} bytes.")
            };
        }

        private static string EncryptDes(string data, byte[] keyBytes)
        {
            byte[] plaintext = utf8Encoding.GetBytes(data);
            using var des = DES.Create();
            des.Key = keyBytes;
            byte[] encrypted = des.EncryptEcb(plaintext, PaddingMode.PKCS7);
            return Convert.ToBase64String(encrypted);
        }

        private static string EncryptAes(string data, byte[] keyBytes)
        {
            byte[] plaintext = utf8Encoding.GetBytes(data);
            using var aes = Aes.Create();
            aes.Key = keyBytes;
            byte[] encrypted = aes.EncryptEcb(plaintext, PaddingMode.PKCS7);
            return Convert.ToBase64String(encrypted);
        }

        private static readonly UTF8Encoding utf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }
}
