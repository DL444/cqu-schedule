namespace DL444.CquSchedule.Backend.Models
{
    internal enum AuthenticationResult
    {
        Success,
        IncorrectCredential,
        CaptchaRequired,
        InfoRequired,
        UnknownFailure
    }
}
