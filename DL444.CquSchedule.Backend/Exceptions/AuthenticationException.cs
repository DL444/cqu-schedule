using System;

namespace DL444.CquSchedule.Backend.Exceptions
{
    internal class AuthenticationException : Exception
    {
        public AuthenticationException() { }
        public AuthenticationException(string message) : base(message) { }
        public AuthenticationException(string message, Exception innerException) : base(message, innerException) { }

        public AuthenticationFailedReason Reason { get; set; }
        public string ErrorDescription { get; set; }
    }

    internal enum AuthenticationFailedReason
    {
        Unknown = 0,
        IncorrectCredential = 1,
        CaptchaRequired = 2
    }
}
