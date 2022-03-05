using DL444.CquSchedule.Backend.Extensions;

namespace DL444.CquSchedule.Backend.Models
{
    internal interface ISignInContext
    {
        bool IsValid { get; }
    }

    internal sealed class UndergraduateSignInContext : ISignInContext
    {
        public string Token { get; set; }
        public bool IsValid => !string.IsNullOrEmpty(Token);
    }

    internal sealed class PostgraduateSignInContext : ISignInContext
    {
        public string StudentSerial { get; set; }
        public CookieContainer CookieContainer { get; set; }
        public bool IsValid => !string.IsNullOrEmpty(StudentSerial) && CookieContainer != null;
    }
}
