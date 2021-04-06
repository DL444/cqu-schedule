using Azure.Security.KeyVault.Keys.Cryptography;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface ICryptographyClientContainerService
    {
        CryptographyClient Client { get; set; }
    }

    internal class CryptographyClientContainerService : ICryptographyClientContainerService
    {
        public CryptographyClient Client 
        { 
            get => _client; 
            set
            {
                lock (clientLock)
                {
                    _client = value;
                }
            }
        }

        private CryptographyClient _client;
        private readonly object clientLock = new object();
    }
}
