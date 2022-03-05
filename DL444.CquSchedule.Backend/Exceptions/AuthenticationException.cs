using System;
using DL444.CquSchedule.Backend.Models;

namespace DL444.CquSchedule.Backend.Exceptions
{
    internal sealed class AuthenticationException : Exception
    {
        public AuthenticationException() { }
        public AuthenticationException(string message) : base(message) { }
        public AuthenticationException(string message, Exception innerException) : base(message, innerException) { }

        public AuthenticationResult Result { get; set; }
        public string ErrorDescription { get; set; }
    }
}
