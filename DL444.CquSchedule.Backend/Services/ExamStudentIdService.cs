using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IExamStudentIdService
    {
        Task<string> GetExamStudentIdAsync(string studentId);
    }

    internal class ExamStudentIdService : IExamStudentIdService
    {
        public async Task<string> GetExamStudentIdAsync(string studentId)
        {
            Aes aes = GetAes();
            using MemoryStream outStream = new MemoryStream();
            using (var encryptor = aes.CreateEncryptor())
            {
                using CryptoStream cryptStream = new CryptoStream(outStream, encryptor, CryptoStreamMode.Write);
                using var writer = new StreamWriter(cryptStream, Encoding.ASCII);
                await writer.WriteAsync(studentId);
            }
            byte[] encrypted = outStream.ToArray();
            return BitConverter.ToString(encrypted).Replace("-", null, StringComparison.Ordinal);
        }

        private static Aes GetAes()
        {
            var encryptor = Aes.Create();
            encryptor.Mode = CipherMode.ECB;
            encryptor.KeySize = 128;
            encryptor.Padding = PaddingMode.PKCS7;
            encryptor.Key = key;
            return encryptor;
        }

        private static readonly byte[] key = Encoding.UTF8.GetBytes("cquisse123456789");
    }
}
