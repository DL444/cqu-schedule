using System;
using System.Security.Cryptography;
using System.Text;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IExamStudentIdService
    {
        string GetExamStudentId(string studentId);
    }

    internal sealed class ExamStudentIdService : IExamStudentIdService
    {
        public string GetExamStudentId(string studentId)
        {
            using Aes aes = Aes.Create();
            aes.Key = key;
            byte[] encrypted = aes.EncryptEcb(Encoding.ASCII.GetBytes(studentId), PaddingMode.PKCS7);
            return Convert.ToHexString(encrypted);
        }

        private static readonly byte[] key = Encoding.UTF8.GetBytes("cquisse123456789");
    }
}
