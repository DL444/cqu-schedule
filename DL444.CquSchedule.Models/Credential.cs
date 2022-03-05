namespace DL444.CquSchedule.Models
{
    public sealed class Credential
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool ShouldSaveCredential { get; set; }
    }
}
